using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduHisob.Data;

public sealed class CenterDataService(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    AuthenticationStateProvider authenticationStateProvider,
    CenterAccess centerAccess)
{
    private async Task<int> GetOrganizationIdAsync(params CenterRole[] allowedRoles) =>
        (await centerAccess.RequireAsync(allowedRoles)).OrganizationId;

    // Foyda, umumiy summalar, ish haqi va ustoz ulushini ko'rish huquqi (asoschi va buxgalter).
    public async Task<bool> CanSeeFinanceAsync()
    {
        var role = (await centerAccess.RequireAsync()).Role;
        return CenterAccess.Finance.Contains(role);
    }

    public async Task<DashboardSummary> GetDashboardAsync()
    {
        var organizationId = await GetOrganizationIdAsync();
        await using var db = await contextFactory.CreateDbContextAsync();

        var organization = await db.Organizations
            .Where(center => center.Id == organizationId)
            .Select(center => new { center.Name, BranchCount = center.Branches.Count, center.TrialEndsAtUtc })
            .SingleAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var previousMonth = currentMonth.AddMonths(-1);
        var previousPeriodEnd = new DateOnly(previousMonth.Year, previousMonth.Month,
            Math.Min(today.Day, DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month)));
        var studentCounts = await db.Students.AsNoTracking()
            .Where(student => student.OrganizationId == organizationId)
            .GroupBy(student => student.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count);
        int CountStudents(StudentStatus status) =>
            studentCounts.TryGetValue(status, out var count) ? count : 0;

        var newStudentsThisMonth = await db.Students.CountAsync(student =>
            student.OrganizationId == organizationId &&
            student.JoinedOn >= currentMonth &&
            student.JoinedOn <= today);
        var activeGroups = await db.StudyGroups.CountAsync(group =>
            group.OrganizationId == organizationId && group.IsActive);
        var currentPeriodIncome = await db.FinancialTransactions
            .Where(transaction =>
                transaction.OrganizationId == organizationId &&
                transaction.Type == FinancialTransactionType.Income &&
                transaction.TransactionDate >= currentMonth &&
                transaction.TransactionDate <= today)
            .SumAsync(transaction => (decimal?)transaction.Amount) ?? 0m;
        var previousPeriodIncome = await db.FinancialTransactions
            .Where(transaction =>
                transaction.OrganizationId == organizationId &&
                transaction.Type == FinancialTransactionType.Income &&
                transaction.TransactionDate >= previousMonth &&
                transaction.TransactionDate <= previousPeriodEnd)
            .SumAsync(transaction => (decimal?)transaction.Amount) ?? 0m;
        var attendanceCounts = await db.AttendanceRecords.AsNoTracking()
            .Where(record =>
                record.OrganizationId == organizationId &&
                record.LessonDate >= currentMonth &&
                record.LessonDate <= today)
            .GroupBy(record => record.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count);
        int CountAttendance(AttendanceStatus status) =>
            attendanceCounts.TryGetValue(status, out var count) ? count : 0;
        var attendedLessons = CountAttendance(AttendanceStatus.Present) + CountAttendance(AttendanceStatus.Late);
        var countedLessons = attendedLessons + CountAttendance(AttendanceStatus.Absent);

        return new DashboardSummary(
            organization.Name,
            organization.BranchCount,
            studentCounts.Where(item => item.Key != StudentStatus.Archived).Sum(item => item.Value),
            CountStudents(StudentStatus.Active),
            CountStudents(StudentStatus.Trial),
            CountStudents(StudentStatus.Frozen),
            CountStudents(StudentStatus.Left),
            newStudentsThisMonth,
            activeGroups,
            currentPeriodIncome,
            previousPeriodIncome,
            attendedLessons,
            countedLessons,
            organization.TrialEndsAtUtc);
    }

    // Bosh sahifadagi moliya bloki. Natija = shu oydagi barcha tushum − barcha xarajat (ish haqi ham xarajatga kiradi),
    // "Hisobotlar" sahifasidagi davr natijasi bilan bir xil formula.
    public async Task<DashboardFinance> GetDashboardFinanceAsync()
    {
        var organizationId = await GetOrganizationIdAsync(CenterAccess.Finance);
        await using var db = await contextFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var rows = await db.FinancialTransactions.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.TransactionDate >= monthStart && item.TransactionDate <= today)
            .Select(item => new { item.Type, item.Amount })
            .ToListAsync();

        await ChargeLedger.SyncAsync(db, organizationId, ChargeLedger.CurrentMonth());
        var debts = (await ChargeLedger.GetTotalsAsync(db, organizationId)).Values.Where(total => total.Balance < 0).ToList();

        return new DashboardFinance(
            rows.Where(row => row.Type == FinancialTransactionType.Income).Sum(row => row.Amount),
            rows.Where(row => row.Type == FinancialTransactionType.Expense).Sum(row => row.Amount),
            debts.Count,
            -debts.Sum(total => total.Balance));
    }

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync()
    {
        var organizationId = await GetOrganizationIdAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Branches
            .AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive)
            .OrderBy(branch => branch.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<BranchRoomSummary>> GetBranchRoomsAsync()
    {
        var organizationId = await GetOrganizationIdAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        return await (
            from room in db.BranchRooms.AsNoTracking()
            join branch in db.Branches on new { room.OrganizationId, room.BranchId } equals new { branch.OrganizationId, BranchId = branch.Id }
            where room.OrganizationId == organizationId && room.IsActive
            orderby branch.Name, room.Name
            select new BranchRoomSummary(
                room.Id,
                room.BranchId,
                room.Name,
                branch.Name,
                room.Capacity,
                db.StudyGroups.Count(studyGroup =>
                    studyGroup.OrganizationId == organizationId &&
                    studyGroup.BranchId == room.BranchId &&
                    studyGroup.RoomId == room.Id &&
                    studyGroup.IsActive)))
            .ToListAsync();
    }

    public async Task AddBranchRoomAsync(int branchId, string name, int capacity)
    {
        name = name.Trim();
        if (name.Length is < 2 or > 120 || capacity is < 1 or > 1000)
        {
            throw new InvalidOperationException("Xona nomi 2–120 belgidan, sig‘imi esa 1–1000 oralig‘ida bo‘lishi kerak.");
        }

        var organizationId = await GetOrganizationIdAsync(CenterAccess.Management);
        await using var db = await contextFactory.CreateDbContextAsync();
        var branchExists = await db.Branches.AnyAsync(branch =>
            branch.Id == branchId && branch.OrganizationId == organizationId && branch.IsActive);
        if (!branchExists)
        {
            throw new InvalidOperationException("Tanlangan filial ushbu markazga tegishli emas.");
        }

        db.BranchRooms.Add(new BranchRoom
        {
            OrganizationId = organizationId,
            BranchId = branchId,
            Name = name,
            Capacity = capacity
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Student>> GetStudentsAsync(
        string? search = null,
        int? branchId = null,
        StudentStatus? status = null)
    {
        var organizationId = await GetOrganizationIdAsync(CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();

        var query = db.Students
            .AsNoTracking()
            .Where(student => student.OrganizationId == organizationId);

        if (status.HasValue)
        {
            query = query.Where(student => student.Status == status.Value);
        }
        else
        {
            query = query.Where(student => student.Status != StudentStatus.Archived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(student => student.FullName.Contains(term) || student.Phone.Contains(term));
        }

        if (branchId.HasValue)
        {
            query = query.Where(student => student.BranchId == branchId.Value);
        }

        return await query.OrderBy(student => student.FullName).ToListAsync();
    }

    public async Task AddStudentAsync(Student student)
    {
        var organizationId = await GetOrganizationIdAsync(CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();
        var branchBelongsToCenter = await db.Branches.AnyAsync(branch =>
            branch.Id == student.BranchId &&
            branch.OrganizationId == organizationId &&
            branch.IsActive);
        if (!branchBelongsToCenter)
        {
            throw new InvalidOperationException("Tanlangan filial ushbu markazga tegishli emas.");
        }

        student.OrganizationId = organizationId;
        student.FullName = student.FullName.Trim();
        student.Phone = student.Phone.Trim();
        student.ParentPhone = student.ParentPhone.Trim();
        await db.Students.AddAsync(student);
        await db.SaveChangesAsync();
    }

    public async Task AddBranchAsync(string name, string address)
    {
        var organizationId = await GetOrganizationIdAsync(CenterAccess.Management);
        await using var db = await contextFactory.CreateDbContextAsync();
        db.Branches.Add(new Branch
        {
            OrganizationId = organizationId,
            Name = name.Trim(),
            Address = address.Trim()
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Course>> GetCoursesAsync()
    {
        var organizationId = await GetOrganizationIdAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Courses.AsNoTracking()
            .Where(course => course.OrganizationId == organizationId && course.IsActive)
            .OrderBy(course => course.Name)
            .ToListAsync();
    }

    public async Task<int> AddCourseAsync(string name, string description)
    {
        name = name.Trim();
        description = description.Trim();
        if (name.Length is < 2 or > 120 || description.Length > 500)
        {
            throw new InvalidOperationException("Kurs nomi 2–120 belgidan, izoh esa 500 belgidan oshmasin.");
        }

        var organizationId = await GetOrganizationIdAsync(CenterAccess.Management);
        await using var db = await contextFactory.CreateDbContextAsync();
        var course = new Course
        {
            OrganizationId = organizationId,
            Name = name,
            Description = description
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course.Id;
    }

    public async Task<IReadOnlyList<StudyGroupSummary>> GetStudyGroupsAsync()
    {
        var user = await centerAccess.RequireAsync();
        var organizationId = user.OrganizationId;
        // Ustoz ulushi faqat moliya xodimlariga ko'rinadi.
        var canSeeShare = CenterAccess.Finance.Contains(user.Role);
        await using var db = await contextFactory.CreateDbContextAsync();
        return await (
            from studyGroup in db.StudyGroups.AsNoTracking()
            join course in db.Courses on new { studyGroup.OrganizationId, studyGroup.CourseId } equals new { course.OrganizationId, CourseId = course.Id }
            join branch in db.Branches on new { studyGroup.OrganizationId, studyGroup.BranchId } equals new { branch.OrganizationId, BranchId = branch.Id }
            where studyGroup.OrganizationId == organizationId && studyGroup.IsActive
            orderby studyGroup.Name
            select new StudyGroupSummary(
                studyGroup.Id,
                studyGroup.Name,
                course.Name,
                studyGroup.BranchId,
                branch.Name,
                studyGroup.TeacherName,
                studyGroup.RoomName,
                studyGroup.DaysOfWeek,
                studyGroup.StartTime,
                studyGroup.Capacity,
                studyGroup.MonthlyFee,
                studyGroup.LessonsPerMonth,
                studyGroup.LessonDurationMinutes,
                db.GroupEnrollments.Count(enrollment =>
                    enrollment.OrganizationId == organizationId &&
                    enrollment.StudyGroupId == studyGroup.Id &&
                    enrollment.LeftOn == null),
                studyGroup.TeacherId,
                canSeeShare ? studyGroup.TeacherSharePercent : 0,
                studyGroup.CourseId,
                studyGroup.RoomId))
            .ToListAsync();
    }

    // Brauzer va yordamchidan kelgan vaqtni "HH:mm" ko'rinishiga keltiradi: "9:00", "09:00:00", "9.30", "9" → "09:00".
    public static string? NormalizeTime(string? value)
    {
        var text = (value ?? "").Trim().Replace('.', ':');
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var hour)) text = $"{hour}:00";
        return TimeOnly.TryParseExact(text, ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "HH:mm:ss.fff"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
            : null;
    }

    public async Task AddStudyGroupAsync(StudyGroup group)
    {
        group.StartTime = NormalizeTime(group.StartTime) ?? group.StartTime;
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(group, new ValidationContext(group), validationResults, validateAllProperties: true))
        {
            throw new InvalidOperationException(string.Join(" ", validationResults.Select(result => result.ErrorMessage)));
        }

        var weekdays = group.DaysOfWeek.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (weekdays.Length == 0 ||
            weekdays.Any(day => !int.TryParse(day, out var value) || !Enum.IsDefined((DayOfWeek)value)) ||
            weekdays.Distinct(StringComparer.Ordinal).Count() != weekdays.Length)
        {
            throw new InvalidOperationException("Hafta kunlari to‘g‘ri tanlanmagan.");
        }

        var organizationId = await GetOrganizationIdAsync(CenterAccess.Management);
        await using var db = await contextFactory.CreateDbContextAsync();
        var validReferences = await db.Branches.AnyAsync(branch =>
            branch.Id == group.BranchId && branch.OrganizationId == organizationId && branch.IsActive)
            && await db.Courses.AnyAsync(course =>
                course.Id == group.CourseId && course.OrganizationId == organizationId && course.IsActive);
        if (!validReferences)
        {
            throw new InvalidOperationException("Tanlangan kurs yoki filial ushbu markazga tegishli emas.");
        }

        if (group.TeacherId.HasValue)
        {
            var teacher = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee =>
                employee.OrganizationId == organizationId &&
                employee.Id == group.TeacherId.Value &&
                employee.Role == EmployeeRole.Teacher &&
                employee.IsActive);
            group.TeacherName = teacher?.FullName
                ?? throw new InvalidOperationException("Tanlangan ustoz ushbu markazda topilmadi.");
            // Ulush guruh ochishda kiritilmaydi: ustozning moliyada belgilangan foizi olinadi.
            group.TeacherSharePercent = teacher.PayType == EmployeePayType.SalaryOnly ? 0 : teacher.DefaultSharePercent;
        }
        else
        {
            group.TeacherSharePercent = 0;
        }

        BranchRoom? room = null;
        if (group.RoomId.HasValue)
        {
            room = await db.BranchRooms.SingleOrDefaultAsync(item =>
                item.Id == group.RoomId.Value &&
                item.OrganizationId == organizationId &&
                item.BranchId == group.BranchId &&
                item.IsActive);
            if (room is null)
            {
                throw new InvalidOperationException("Tanlangan xona ushbu filialga tegishli emas yoki faol emas.");
            }

            if (group.Capacity > room.Capacity)
            {
                throw new InvalidOperationException($"Guruh sig‘imi xona sig‘imidan ({room.Capacity}) oshmasligi kerak.");
            }
        }

        group.OrganizationId = organizationId;
        group.Name = group.Name.Trim();
        group.TeacherName = group.TeacherName.Trim();
        group.RoomName = room?.Name ?? group.RoomName.Trim();
        group.DaysOfWeek = group.DaysOfWeek.Trim();

        var selectedDays = weekdays.Select(int.Parse).ToHashSet();
        var startTime = TimeOnly.ParseExact(group.StartTime, "HH:mm", CultureInfo.InvariantCulture);
        var startMinutes = startTime.Hour * 60 + startTime.Minute;
        var endMinutes = startMinutes + group.LessonDurationMinutes;
        if (endMinutes > 24 * 60)
        {
            throw new InvalidOperationException("Dars yarim tundan o‘tib ketmasligi kerak.");
        }
        var existingSchedules = await db.StudyGroups.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.BranchId == group.BranchId && item.IsActive)
            .Select(item => new
            {
                item.Name,
                item.RoomId,
                item.RoomName,
                item.TeacherId,
                item.TeacherName,
                item.DaysOfWeek,
                item.StartTime,
                item.LessonDurationMinutes
            })
            .ToListAsync();

        foreach (var existing in existingSchedules)
        {
            var existingDays = existing.DaysOfWeek.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(value => int.TryParse(value, out _))
                .Select(int.Parse);
            if (!selectedDays.Overlaps(existingDays))
            {
                continue;
            }

            var existingStartTime = TimeOnly.ParseExact(existing.StartTime, "HH:mm", CultureInfo.InvariantCulture);
            var existingStart = existingStartTime.Hour * 60 + existingStartTime.Minute;
            var existingEnd = existingStart + existing.LessonDurationMinutes;
            if (startMinutes >= existingEnd || existingStart >= endMinutes)
            {
                continue;
            }

            var sameRoom = !string.IsNullOrWhiteSpace(group.RoomName) &&
                ((group.RoomId.HasValue && existing.RoomId == group.RoomId) ||
                 string.Equals(group.RoomName, existing.RoomName, StringComparison.OrdinalIgnoreCase));
            if (sameRoom)
            {
                throw new InvalidOperationException($"“{group.RoomName}” xona {existing.Name} guruhi uchun shu vaqtda band.");
            }

            var sameTeacher = (group.TeacherId.HasValue && existing.TeacherId == group.TeacherId) ||
                (!string.IsNullOrWhiteSpace(group.TeacherName) &&
                 string.Equals(group.TeacherName, existing.TeacherName, StringComparison.OrdinalIgnoreCase));
            if (sameTeacher)
            {
                throw new InvalidOperationException($"“{group.TeacherName}” ustoz {existing.Name} guruhi bilan shu vaqtda band.");
            }
        }

        db.StudyGroups.Add(group);
        await db.SaveChangesAsync();
    }

    public async Task EnrollStudentAsync(int groupId, int studentId, decimal discountPercent = 0, string discountNote = "")
    {
        discountNote = discountNote.Trim();
        if (discountPercent is < 0 or > 100 || discountNote.Length > 200)
        {
            throw new InvalidOperationException("Chegirma 0–100% oralig‘ida, izohi 200 belgidan oshmasin.");
        }

        var organizationId = await GetOrganizationIdAsync(CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();
        var group = await db.StudyGroups.SingleOrDefaultAsync(item =>
            item.Id == groupId && item.OrganizationId == organizationId && item.IsActive);
        var student = await db.Students.SingleOrDefaultAsync(item =>
            item.Id == studentId && item.OrganizationId == organizationId &&
            item.Status != StudentStatus.Archived && item.Status != StudentStatus.Left);
        if (group is null || student is null)
        {
            throw new InvalidOperationException("Tanlangan guruh yoki o‘quvchi ushbu markazga tegishli emas.");
        }

        if (group.BranchId != student.BranchId)
        {
            throw new InvalidOperationException("O‘quvchi va guruh bir filialga tegishli bo‘lishi kerak.");
        }

        var currentCount = await db.GroupEnrollments.CountAsync(enrollment =>
            enrollment.OrganizationId == organizationId && enrollment.StudyGroupId == groupId && enrollment.LeftOn == null);
        if (currentCount >= group.Capacity)
        {
            throw new InvalidOperationException("Guruh sig‘imi to‘lgan. Avval guruh sig‘imini oshiring.");
        }

        var existing = await db.GroupEnrollments.SingleOrDefaultAsync(enrollment =>
            enrollment.OrganizationId == organizationId &&
            enrollment.StudyGroupId == groupId &&
            enrollment.StudentId == studentId);
        if (existing is { LeftOn: null })
        {
            throw new InvalidOperationException("Bu o‘quvchi allaqachon guruhga biriktirilgan.");
        }

        if (existing is not null)
        {
            // Avval chiqib ketgan o'quvchi qaytdi: a'zolik qayta faollashadi, hisob qaytgan kundan boshlanadi.
            existing.LeftOn = null;
            existing.LeaveReasonId = null;
            existing.LeaveNote = "";
            existing.EnrolledAtUtc = DateTime.UtcNow;
            existing.DiscountPercent = discountPercent;
            existing.DiscountNote = discountNote;
        }
        else
        {
            db.GroupEnrollments.Add(new GroupEnrollment
            {
                OrganizationId = organizationId,
                StudyGroupId = groupId,
                StudentId = studentId,
                DiscountPercent = discountPercent,
                DiscountNote = discountNote
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<AttendanceEntry>> GetAttendanceAsync(int groupId, DateOnly lessonDate)
    {
        var organizationId = await GetOrganizationIdAsync(CenterAccess.Teaching);
        await using var db = await contextFactory.CreateDbContextAsync();
        var groupExists = await db.StudyGroups.AnyAsync(group =>
            group.Id == groupId && group.OrganizationId == organizationId && group.IsActive);
        if (!groupExists)
        {
            throw new InvalidOperationException("Guruh topilmadi.");
        }

        return await (
            from enrollment in db.GroupEnrollments.AsNoTracking()
            join student in db.Students on new { enrollment.OrganizationId, enrollment.StudentId } equals new { student.OrganizationId, StudentId = student.Id }
            join record in db.AttendanceRecords.Where(record => record.LessonDate == lessonDate)
                on new { enrollment.OrganizationId, enrollment.StudyGroupId, enrollment.StudentId }
                equals new { record.OrganizationId, record.StudyGroupId, record.StudentId } into attendance
            from record in attendance.DefaultIfEmpty()
            where enrollment.OrganizationId == organizationId &&
                enrollment.StudyGroupId == groupId &&
                (enrollment.LeftOn == null || enrollment.LeftOn > lessonDate)
            orderby student.FullName
            select new AttendanceEntry(student.Id, student.FullName, record == null ? null : record.Status))
            .ToListAsync();
    }

    public async Task SaveAttendanceAsync(int groupId, DateOnly lessonDate, IReadOnlyCollection<AttendanceEntry> entries)
    {
        var organizationId = await GetOrganizationIdAsync(CenterAccess.Teaching);
        await using var db = await contextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var groupExists = await db.StudyGroups.AnyAsync(group =>
            group.OrganizationId == organizationId && group.Id == groupId && group.IsActive);
        if (!groupExists)
        {
            throw new InvalidOperationException("Guruh topilmadi.");
        }

        var enrolledStudentIds = await db.GroupEnrollments
            .Where(enrollment =>
                enrollment.OrganizationId == organizationId &&
                enrollment.StudyGroupId == groupId &&
                (enrollment.LeftOn == null || enrollment.LeftOn > lessonDate))
            .Select(enrollment => enrollment.StudentId)
            .ToListAsync();
        if (enrolledStudentIds.Count != entries.Count ||
            entries.Select(entry => entry.StudentId).Distinct().Count() != entries.Count ||
            entries.Any(entry => !enrolledStudentIds.Contains(entry.StudentId)))
        {
            throw new InvalidOperationException("Davomat faqat tanlangan guruhga biriktirilgan o‘quvchilar uchun saqlanadi.");
        }

        var records = await db.AttendanceRecords.Where(record =>
            record.OrganizationId == organizationId &&
            record.StudyGroupId == groupId &&
            record.LessonDate == lessonDate).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var entry in entries)
        {
            if (entry.Status is null || !Enum.IsDefined(entry.Status.Value))
            {
                throw new InvalidOperationException("Har bir o‘quvchining davomat holatini tanlang.");
            }

            var record = records.SingleOrDefault(item => item.StudentId == entry.StudentId);
            if (record is null)
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    OrganizationId = organizationId,
                    StudyGroupId = groupId,
                    StudentId = entry.StudentId,
                    LessonDate = lessonDate,
                    Status = entry.Status.Value,
                    UpdatedAtUtc = now
                });
            }
            else
            {
                record.Status = entry.Status.Value;
                record.UpdatedAtUtc = now;
            }
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    // Guruh formalari uchun ustozlar ro'yxati: faqat ism, maosh va foizsiz (administrator ham ko'radi).
    public async Task<IReadOnlyList<TeacherOption>> GetTeacherOptionsAsync()
    {
        var organizationId = await GetOrganizationIdAsync([.. CenterAccess.Management, .. CenterAccess.Hr]);
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Employees.AsNoTracking()
            .Where(employee => employee.OrganizationId == organizationId && employee.IsActive && employee.Role == EmployeeRole.Teacher)
            .OrderBy(employee => employee.FullName)
            .Select(employee => new TeacherOption(employee.Id, employee.FullName))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<EmployeeSummary>> GetEmployeesAsync(string? search = null)
    {
        var organizationId = await GetOrganizationIdAsync(CenterAccess.Hr);
        await using var db = await contextFactory.CreateDbContextAsync();
        var employees = db.Employees.AsNoTracking()
            .Where(employee => employee.OrganizationId == organizationId && employee.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            employees = employees.Where(employee =>
                employee.FullName.Contains(term) ||
                employee.Phone.Contains(term) ||
                employee.Position.Contains(term));
        }

        return await (
            from employee in employees
            join branch in db.Branches on new { employee.OrganizationId, employee.BranchId } equals new { branch.OrganizationId, BranchId = branch.Id }
            orderby employee.FullName
            select new EmployeeSummary(
                employee.Id,
                employee.FullName,
                employee.Phone,
                employee.Position,
                employee.Role,
                employee.BranchId,
                branch.Name,
                employee.HiredOn,
                employee.AgreedMonthlySalary,
                employee.PayType,
                employee.DefaultSharePercent))
            .ToListAsync();
    }

    public async Task AddEmployeeAsync(Employee employee)
    {
        NormalizeEmployee(employee);
        var user = await centerAccess.RequireAsync(CenterAccess.Hr);
        var organizationId = user.OrganizationId;
        // Ustoz foizini faqat moliya belgilaydi (Moliya → Ustozlar ulushi).
        if (!CenterAccess.Finance.Contains(user.Role))
        {
            employee.DefaultSharePercent = 0;
        }
        await using var db = await contextFactory.CreateDbContextAsync();
        await EnsureBranchAsync(db, organizationId, employee.BranchId);

        employee.OrganizationId = organizationId;
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
    }

    public async Task UpdateEmployeeAsync(Employee changes)
    {
        NormalizeEmployee(changes);
        var organizationId = await GetOrganizationIdAsync(CenterAccess.Hr);
        await using var db = await contextFactory.CreateDbContextAsync();
        var employee = await db.Employees.SingleOrDefaultAsync(item =>
                item.Id == changes.Id && item.OrganizationId == organizationId && item.IsActive)
            ?? throw new InvalidOperationException("Xodim topilmadi.");
        await EnsureBranchAsync(db, organizationId, changes.BranchId);

        employee.FullName = changes.FullName;
        employee.Phone = changes.Phone;
        employee.Position = changes.Position;
        employee.Role = changes.Role;
        employee.BranchId = changes.BranchId;
        employee.HiredOn = changes.HiredOn;
        employee.AgreedMonthlySalary = changes.AgreedMonthlySalary;
        employee.PayType = changes.PayType;
        // Foiz bu yerda o'zgarmaydi — u Moliya → Ustozlar ulushi bo'limida belgilanadi.
        if (employee.PayType == EmployeePayType.SalaryOnly)
        {
            employee.DefaultSharePercent = 0;
        }
        await db.SaveChangesAsync();
    }

    private static void NormalizeEmployee(Employee employee)
    {
        employee.FullName = employee.FullName.Trim();
        employee.Phone = employee.Phone.Trim();
        employee.Position = employee.Position.Trim();
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(employee, new ValidationContext(employee), validationResults, validateAllProperties: true))
        {
            throw new InvalidOperationException(string.Join(" ", validationResults.Select(result => result.ErrorMessage)));
        }
        if (!Enum.IsDefined(employee.Role) || !Enum.IsDefined(employee.PayType))
        {
            throw new InvalidOperationException("Xodim turini tanlang.");
        }

        // Oddiy xodim guruhdan ulush olmaydi; ustozda esa tanlangan sxemaga mos bo'lmagan qism nolga tushadi.
        if (employee.Role == EmployeeRole.Staff)
        {
            employee.PayType = EmployeePayType.SalaryOnly;
        }
        if (employee.PayType == EmployeePayType.SalaryOnly)
        {
            employee.DefaultSharePercent = 0;
        }
        if (employee.PayType == EmployeePayType.ShareOnly)
        {
            employee.AgreedMonthlySalary = 0;
        }
        if (employee.PayType != EmployeePayType.ShareOnly && employee.AgreedMonthlySalary <= 0)
        {
            throw new InvalidOperationException("Kelishilgan oylikni kiriting.");
        }
    }

    private static async Task EnsureBranchAsync(ApplicationDbContext db, int organizationId, int branchId)
    {
        var branchExists = await db.Branches.AnyAsync(branch =>
            branch.Id == branchId && branch.OrganizationId == organizationId && branch.IsActive);
        if (!branchExists)
        {
            throw new InvalidOperationException("Tanlangan filial ushbu markazga tegishli emas.");
        }
    }

    public async Task<IReadOnlyList<EmployeePayrollSummary>> GetPayrollSummaryAsync(DateOnly payrollMonth)
    {
        var organizationId = await GetOrganizationIdAsync(CenterAccess.Hr);
        await using var db = await contextFactory.CreateDbContextAsync();
        var employees = await db.Employees.AsNoTracking()
            .Where(employee => employee.OrganizationId == organizationId && employee.IsActive)
            .OrderBy(employee => employee.FullName)
            .Select(employee => new { employee.Id, employee.FullName, employee.Position, employee.Role, employee.AgreedMonthlySalary, employee.PayType })
            .ToListAsync();
        var entries = await db.PayrollEntries.AsNoTracking()
            .Where(entry => entry.OrganizationId == organizationId && entry.PayrollMonth == payrollMonth)
            .ToListAsync();
        var revenueShares = await GetTeacherRevenueSharesAsync(db, organizationId, payrollMonth);

        return employees.Select(employee =>
        {
            var employeeEntries = entries.Where(entry => entry.EmployeeId == employee.Id).ToList();
            var bonuses = employeeEntries.Where(entry => entry.Type == PayrollEntryType.Bonus).Sum(entry => entry.Amount);
            var advances = employeeEntries.Where(entry => entry.Type == PayrollEntryType.Advance).Sum(entry => entry.Amount);
            var salaryPayments = employeeEntries.Where(entry => entry.Type == PayrollEntryType.SalaryPayment).Sum(entry => entry.Amount);
            // "Faqat oylik" ustozga guruhda foiz yozilgan bo'lsa ham ulush qo'shilmaydi; "faqat foiz"da oylik nol.
            var revenueShare = employee.PayType == EmployeePayType.SalaryOnly ? 0 : revenueShares.GetValueOrDefault(employee.Id);
            var salary = employee.PayType == EmployeePayType.ShareOnly ? 0 : employee.AgreedMonthlySalary;
            return new EmployeePayrollSummary(
                employee.Id,
                employee.FullName,
                employee.Position,
                employee.Role,
                salary,
                bonuses,
                advances,
                salaryPayments,
                salary + revenueShare + bonuses - advances - salaryPayments,
                revenueShare,
                employee.PayType);
        }).ToList();
    }

    // Ustoz ulushi: shu oyda ustoz guruhlari uchun qabul qilingan o'quvchi to'lovlari × guruhdagi ulush foizi.
    private static async Task<Dictionary<int, decimal>> GetTeacherRevenueSharesAsync(
        ApplicationDbContext db,
        int organizationId,
        DateOnly payrollMonth)
    {
        var monthEnd = payrollMonth.AddMonths(1).AddDays(-1);
        var groups = await db.StudyGroups.AsNoTracking()
            .Where(group => group.OrganizationId == organizationId && group.TeacherId != null)
            .Select(group => new { group.Id, TeacherId = group.TeacherId!.Value, group.TeacherSharePercent })
            .ToListAsync();
        groups = groups.Where(group => group.TeacherSharePercent > 0).ToList();
        if (groups.Count == 0)
        {
            return [];
        }

        var groupIds = groups.Select(group => group.Id).ToArray();
        var payments = await db.FinancialTransactions.AsNoTracking()
            .Where(transaction =>
                transaction.OrganizationId == organizationId &&
                transaction.Type == FinancialTransactionType.Income &&
                transaction.StudyGroupId != null &&
                groupIds.Contains(transaction.StudyGroupId.Value) &&
                transaction.TransactionDate >= payrollMonth &&
                transaction.TransactionDate <= monthEnd)
            .Select(transaction => new { GroupId = transaction.StudyGroupId!.Value, transaction.Amount })
            .ToListAsync();

        return groups
            .Select(group => new
            {
                group.TeacherId,
                Share = payments.Where(payment => payment.GroupId == group.Id).Sum(payment => payment.Amount) * group.TeacherSharePercent / 100m
            })
            .GroupBy(item => item.TeacherId)
            .ToDictionary(item => item.Key, item => Math.Round(item.Sum(row => row.Share), 0, MidpointRounding.AwayFromZero));
    }

    // Ustozlar ulushi faqat moliyada boshqariladi: ustozning odatiy foizi va har bir guruhdagi foizi,
    // tanlangan oydagi guruh tushumi va undan hisoblangan ulush bilan.
    public async Task<IReadOnlyList<TeacherShareRow>> GetTeacherSharesAsync(DateOnly month)
    {
        var organizationId = await GetOrganizationIdAsync(CenterAccess.Finance);
        month = new DateOnly(month.Year, month.Month, 1);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        await using var db = await contextFactory.CreateDbContextAsync();
        var teachers = await db.Employees.AsNoTracking()
            .Where(employee => employee.OrganizationId == organizationId && employee.IsActive && employee.Role == EmployeeRole.Teacher)
            .OrderBy(employee => employee.FullName)
            .Select(employee => new { employee.Id, employee.FullName, employee.PayType, employee.AgreedMonthlySalary, employee.DefaultSharePercent })
            .ToListAsync();
        var groups = await db.StudyGroups.AsNoTracking()
            .Where(group => group.OrganizationId == organizationId && group.IsActive && group.TeacherId != null)
            .OrderBy(group => group.Name)
            .Select(group => new
            {
                group.Id,
                group.Name,
                TeacherId = group.TeacherId!.Value,
                group.TeacherSharePercent,
                group.MonthlyFee,
                StudentCount = db.GroupEnrollments.Count(enrollment =>
                    enrollment.OrganizationId == organizationId && enrollment.StudyGroupId == group.Id && enrollment.LeftOn == null)
            })
            .ToListAsync();
        var income = (await db.FinancialTransactions.AsNoTracking()
                .Where(item =>
                    item.OrganizationId == organizationId &&
                    item.Type == FinancialTransactionType.Income &&
                    item.StudyGroupId != null &&
                    item.TransactionDate >= month &&
                    item.TransactionDate <= monthEnd)
                .Select(item => new { GroupId = item.StudyGroupId!.Value, item.Amount })
                .ToListAsync())
            .GroupBy(item => item.GroupId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

        return teachers
            .Select(teacher => new TeacherShareRow(
                teacher.Id,
                teacher.FullName,
                teacher.PayType,
                teacher.AgreedMonthlySalary,
                teacher.DefaultSharePercent,
                groups
                    .Where(group => group.TeacherId == teacher.Id)
                    .Select(group =>
                    {
                        var groupIncome = income.TryGetValue(group.Id, out var amount) ? amount : 0m;
                        var percent = teacher.PayType == EmployeePayType.SalaryOnly ? 0 : group.TeacherSharePercent;
                        return new TeacherShareGroup(
                            group.Id,
                            group.Name,
                            group.TeacherSharePercent,
                            group.MonthlyFee,
                            group.StudentCount,
                            groupIncome,
                            Math.Round(groupIncome * percent / 100m, 0, MidpointRounding.AwayFromZero));
                    })
                    .ToList()))
            .ToList();
    }

    // Ustozning odatiy foizi: yangi guruhlarga avtomatik qo'yiladi; xohlansa mavjud guruhlariga ham.
    public async Task SetTeacherDefaultShareAsync(int employeeId, decimal percent, bool applyToGroups)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Finance);
        EnsureSharePercent(percent);
        await using var db = await contextFactory.CreateDbContextAsync();
        var teacher = await db.Employees.SingleOrDefaultAsync(employee =>
                employee.OrganizationId == user.OrganizationId && employee.Id == employeeId && employee.IsActive && employee.Role == EmployeeRole.Teacher)
            ?? throw new InvalidOperationException("Ustoz topilmadi.");
        EnsureTeacherTakesShare(teacher, percent);

        teacher.DefaultSharePercent = percent;
        var updatedGroups = 0;
        if (applyToGroups)
        {
            var groups = await db.StudyGroups
                .Where(group => group.OrganizationId == user.OrganizationId && group.IsActive && group.TeacherId == teacher.Id)
                .ToListAsync();
            groups.ForEach(group => group.TeacherSharePercent = percent);
            updatedGroups = groups.Count;
        }

        db.FinancialAuditEntries.Add(new FinancialAuditEntry
        {
            OrganizationId = user.OrganizationId,
            ActorUserId = user.UserId,
            Action = "Ustoz ulushi",
            Description = $"{teacher.FullName}: odatiy foiz {percent:0.##}%" + (applyToGroups ? $", {updatedGroups} ta guruhga qo‘llandi" : "")
        });
        await db.SaveChangesAsync();
    }

    public async Task SetGroupShareAsync(int groupId, decimal percent)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Finance);
        EnsureSharePercent(percent);
        await using var db = await contextFactory.CreateDbContextAsync();
        var group = await db.StudyGroups.SingleOrDefaultAsync(item => item.OrganizationId == user.OrganizationId && item.Id == groupId && item.IsActive)
            ?? throw new InvalidOperationException("Guruh topilmadi.");
        if (group.TeacherId is not { } teacherId)
        {
            throw new InvalidOperationException("Guruhga avval ustoz biriktiring.");
        }
        var teacher = await db.Employees.AsNoTracking().SingleAsync(employee => employee.OrganizationId == user.OrganizationId && employee.Id == teacherId);
        EnsureTeacherTakesShare(teacher, percent);

        group.TeacherSharePercent = percent;
        db.FinancialAuditEntries.Add(new FinancialAuditEntry
        {
            OrganizationId = user.OrganizationId,
            ActorUserId = user.UserId,
            Action = "Ustoz ulushi",
            Description = $"{group.Name} ({teacher.FullName}): {percent:0.##}%"
        });
        await db.SaveChangesAsync();
    }

    private static void EnsureSharePercent(decimal percent)
    {
        if (percent is < 0 or > 100)
        {
            throw new InvalidOperationException("Ulush 0–100% oralig‘ida bo‘lsin.");
        }
    }

    private static void EnsureTeacherTakesShare(Employee teacher, decimal percent)
    {
        if (percent > 0 && teacher.PayType == EmployeePayType.SalaryOnly)
        {
            throw new InvalidOperationException($"{teacher.FullName} faqat oylik oladi. Foiz berish uchun Xodimlar bo‘limida to‘lov turini o‘zgartiring.");
        }
    }

    public async Task<IReadOnlyList<PayrollEntrySummary>> GetPayrollEntriesAsync(int employeeId, DateOnly payrollMonth)
    {
        var organizationId = await GetOrganizationIdAsync(CenterAccess.Hr);
        await using var db = await contextFactory.CreateDbContextAsync();
        return await (
            from entry in db.PayrollEntries.AsNoTracking()
            join employee in db.Employees on new { entry.OrganizationId, entry.EmployeeId } equals new { employee.OrganizationId, EmployeeId = employee.Id }
            where entry.OrganizationId == organizationId &&
                entry.EmployeeId == employeeId &&
                entry.PayrollMonth == payrollMonth
            orderby entry.EntryDate descending, entry.Id descending
            select new PayrollEntrySummary(
                entry.Id,
                employee.FullName,
                entry.EntryDate,
                entry.Type,
                entry.Amount,
                entry.PaymentMethod,
                entry.Note))
            .ToListAsync();
    }

    public async Task AddPayrollEntryAsync(
        int employeeId,
        DateOnly payrollMonth,
        DateOnly entryDate,
        PayrollEntryType type,
        decimal amount,
        PaymentMethod? paymentMethod,
        string note)
    {
        if (payrollMonth.Day != 1 || amount <= 0 || amount > 999999999 || !Enum.IsDefined(type))
        {
            throw new InvalidOperationException("Oy va summa qiymatlarini tekshiring.");
        }
        if (note.Trim().Length > 500)
        {
            throw new InvalidOperationException("Izoh 500 belgidan oshmasin.");
        }
        if (type == PayrollEntryType.Bonus && paymentMethod is not null)
        {
            throw new InvalidOperationException("Bonus hisoblanadi; to‘lov usuli faqat avans yoki ish haqi berilganda tanlanadi.");
        }
        if (type != PayrollEntryType.Bonus && (paymentMethod is null || !Enum.IsDefined(paymentMethod.Value)))
        {
            throw new InvalidOperationException("To‘lov usulini tanlang.");
        }

        var organizationId = await GetOrganizationIdAsync(CenterAccess.Hr);
        await using var db = await contextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var employee = await db.Employees.SingleOrDefaultAsync(item =>
            item.OrganizationId == organizationId && item.Id == employeeId && item.IsActive);
        if (employee is null)
        {
            throw new InvalidOperationException("Xodim ushbu markazda topilmadi.");
        }

        var existingEntries = await db.PayrollEntries.Where(entry =>
            entry.OrganizationId == organizationId &&
            entry.EmployeeId == employeeId &&
            entry.PayrollMonth == payrollMonth).ToListAsync();
        if (type != PayrollEntryType.Bonus)
        {
            var revenueShares = await GetTeacherRevenueSharesAsync(db, organizationId, payrollMonth);
            var accrued = employee.AgreedMonthlySalary +
                revenueShares.GetValueOrDefault(employee.Id) +
                existingEntries.Where(entry => entry.Type == PayrollEntryType.Bonus).Sum(entry => entry.Amount);
            var alreadyPaid = existingEntries
                .Where(entry => entry.Type is PayrollEntryType.Advance or PayrollEntryType.SalaryPayment)
                .Sum(entry => entry.Amount);
            if (alreadyPaid + amount > accrued)
            {
                throw new InvalidOperationException("Avans va ish haqi jami hisoblangan summadan oshmasligi kerak.");
            }
        }

        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var recordedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Avval tizimga kiring.");
        var entry = new PayrollEntry
        {
            OrganizationId = organizationId,
            EmployeeId = employee.Id,
            BranchId = employee.BranchId,
            PayrollMonth = payrollMonth,
            EntryDate = entryDate,
            Type = type,
            Amount = amount,
            PaymentMethod = paymentMethod,
            Note = note.Trim(),
            RecordedByUserId = recordedByUserId
        };
        db.PayrollEntries.Add(entry);
        await db.SaveChangesAsync();

        if (type is PayrollEntryType.Advance or PayrollEntryType.SalaryPayment)
        {
            db.FinancialTransactions.Add(new FinancialTransaction
            {
                OrganizationId = organizationId,
                BranchId = employee.BranchId,
                TransactionDate = entryDate,
                Category = type == PayrollEntryType.Advance ? "Xodim avansi" : "Ish haqi",
                Amount = amount,
                PaymentMethod = paymentMethod!.Value,
                Note = string.IsNullOrWhiteSpace(note) ? employee.FullName : $"{employee.FullName}: {note.Trim()}",
                PayrollEntryId = entry.Id,
                RecordedByUserId = recordedByUserId
            });
            await db.SaveChangesAsync();
        }

        db.FinancialAuditEntries.Add(new FinancialAuditEntry
        {
            OrganizationId = organizationId,
            ActorUserId = recordedByUserId,
            Action = "Ish haqi qaydi",
            Description = $"{employee.FullName}: {type}, {amount:N0} so‘m, {payrollMonth:yyyy-MM}"
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<FinancialTransactionSummary>> GetFinancialTransactionsAsync(DateOnly startDate, DateOnly endDate, int? branchId = null)
    {
        if (endDate < startDate)
        {
            throw new InvalidOperationException("Sana oralig‘ini tekshiring.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.Cash);
        var organizationId = user.OrganizationId;
        await using var db = await contextFactory.CreateDbContextAsync();
        var transactions = db.FinancialTransactions.AsNoTracking()
            .Where(transaction =>
                transaction.OrganizationId == organizationId &&
                transaction.TransactionDate >= startDate &&
                transaction.TransactionDate <= endDate);
        // Ish haqi to'lovlari (xodimlar maoshi) faqat moliya xodimlariga ko'rinadi.
        if (!CenterAccess.Finance.Contains(user.Role))
        {
            transactions = transactions.Where(transaction => transaction.PayrollEntryId == null);
        }
        if (branchId.HasValue)
        {
            transactions = transactions.Where(transaction => transaction.BranchId == branchId.Value);
        }

        return await (
            from transaction in transactions
            join branch in db.Branches on new { transaction.OrganizationId, transaction.BranchId } equals new { branch.OrganizationId, BranchId = branch.Id }
            orderby transaction.TransactionDate descending, transaction.Id descending
            select new FinancialTransactionSummary(
                transaction.Id,
                transaction.TransactionDate,
                transaction.Type,
                transaction.Category,
                transaction.Amount,
                transaction.PaymentMethod,
                transaction.Note,
                transaction.BranchId,
                branch.Name,
                db.Students
                    .Where(student => student.OrganizationId == organizationId && student.Id == transaction.StudentId)
                    .Select(student => student.FullName)
                    .FirstOrDefault()))
            .ToListAsync();
    }

    public async Task<int> AddFinancialTransactionAsync(
        FinancialTransactionType type,
        int branchId,
        int? studentId,
        DateOnly date,
        string category,
        decimal amount,
        PaymentMethod paymentMethod,
        string note,
        int? studyGroupId = null)
    {
        category = category.Trim();
        note = note.Trim();
        if (!Enum.IsDefined(type) ||
            (studentId is null && (category.Length is < 2 or > 120)) ||
            note.Length > 500 ||
            amount <= 0 ||
            amount > 999999999 ||
            !Enum.IsDefined(paymentMethod))
        {
            throw new InvalidOperationException("Moliya qaydi ma’lumotlarini tekshiring.");
        }
        if (studentId.HasValue && type != FinancialTransactionType.Income)
        {
            throw new InvalidOperationException("O‘quvchi to‘lovi daromad sifatida qayd etiladi.");
        }
        if (studyGroupId.HasValue && studentId is null)
        {
            throw new InvalidOperationException("Guruh faqat o‘quvchi to‘lovi uchun tanlanadi.");
        }
        if (type == FinancialTransactionType.Expense && !ExpenseCategories.IsKnown(category) && note.Length < 5)
        {
            throw new InvalidOperationException($"“{category}” ro‘yxatdagi turkum emas. Bu xarajat nima ekanini izohda yozing (kamida 5 belgi).");
        }

        var organizationId = await GetOrganizationIdAsync(CenterAccess.Cash);
        await using var db = await contextFactory.CreateDbContextAsync();
        var branchExists = await db.Branches.AnyAsync(branch =>
            branch.OrganizationId == organizationId && branch.Id == branchId && branch.IsActive);
        if (!branchExists)
        {
            throw new InvalidOperationException("Tanlangan filial ushbu markazga tegishli emas.");
        }

        if (studentId.HasValue)
        {
            var studentExists = await db.Students.AnyAsync(student =>
                student.OrganizationId == organizationId &&
                student.Id == studentId.Value &&
                student.BranchId == branchId &&
                student.Status != StudentStatus.Archived);
            if (!studentExists)
            {
                throw new InvalidOperationException("Tanlangan o‘quvchi ushbu filialda topilmadi.");
            }
            category = "O‘quvchi to‘lovi";

            if (studyGroupId.HasValue)
            {
                var memberOfGroup = await db.GroupEnrollments.AnyAsync(enrollment =>
                    enrollment.OrganizationId == organizationId &&
                    enrollment.StudyGroupId == studyGroupId.Value &&
                    enrollment.StudentId == studentId.Value);
                if (!memberOfGroup)
                {
                    throw new InvalidOperationException("O‘quvchi tanlangan guruhda o‘qimaydi.");
                }
            }
        }

        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var actorUserId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Avval tizimga kiring.");
        var transaction = new FinancialTransaction
        {
            OrganizationId = organizationId,
            BranchId = branchId,
            TransactionDate = date,
            Type = type,
            Category = category,
            StudentId = studentId,
            StudyGroupId = studyGroupId,
            Amount = amount,
            PaymentMethod = paymentMethod,
            Note = note,
            RecordedByUserId = actorUserId
        };
        db.FinancialTransactions.Add(transaction);
        db.FinancialAuditEntries.Add(new FinancialAuditEntry
        {
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            Action = type == FinancialTransactionType.Income ? "Daromad qaydi" : "Xarajat qaydi",
            Description = $"{category}: {amount:N0} so‘m, {date:dd.MM.yyyy}"
        });
        await db.SaveChangesAsync();
        return transaction.Id;
    }

    public async Task<IReadOnlyList<ExpenseBudgetSummary>> GetExpenseBudgetsAsync(DateOnly month, int? branchId = null)
    {
        if (month.Day != 1)
        {
            throw new InvalidOperationException("Budjet oyi oyning birinchi sanasi bo‘lishi kerak.");
        }

        var organizationId = await GetOrganizationIdAsync(CenterAccess.Finance);
        await using var db = await contextFactory.CreateDbContextAsync();
        var query = db.ExpenseBudgets.AsNoTracking()
            .Where(budget => budget.OrganizationId == organizationId && budget.Month == month);
        if (branchId.HasValue)
        {
            query = query.Where(budget => budget.BranchId == branchId.Value);
        }

        return await (
            from budget in query
            join branch in db.Branches on new { budget.OrganizationId, budget.BranchId } equals new { branch.OrganizationId, BranchId = branch.Id }
            orderby branch.Name, budget.Category
            select new ExpenseBudgetSummary(
                budget.Id,
                budget.BranchId,
                branch.Name,
                budget.Month,
                budget.Category,
                budget.LimitAmount))
            .ToListAsync();
    }

    public async Task SaveExpenseBudgetAsync(int branchId, DateOnly month, string category, decimal limitAmount)
    {
        category = category.Trim();
        if (month.Day != 1 ||
            category.Length is < 2 or > 120 ||
            limitAmount <= 0 ||
            limitAmount > 999999999)
        {
            throw new InvalidOperationException("Budjet oyi, kategoriya va limitni tekshiring.");
        }

        var organizationId = await GetOrganizationIdAsync(CenterAccess.Finance);
        await using var db = await contextFactory.CreateDbContextAsync();
        var branch = await db.Branches.SingleOrDefaultAsync(item =>
            item.OrganizationId == organizationId && item.Id == branchId && item.IsActive);
        if (branch is null)
        {
            throw new InvalidOperationException("Tanlangan filial ushbu markazga tegishli emas.");
        }

        var budget = await db.ExpenseBudgets.SingleOrDefaultAsync(item =>
            item.OrganizationId == organizationId &&
            item.BranchId == branchId &&
            item.Month == month &&
            item.Category == category);
        var action = budget is null ? "Budjet yaratildi" : "Budjet yangilandi";
        if (budget is null)
        {
            db.ExpenseBudgets.Add(new ExpenseBudget
            {
                OrganizationId = organizationId,
                BranchId = branchId,
                Month = month,
                Category = category,
                LimitAmount = limitAmount
            });
        }
        else
        {
            budget.LimitAmount = limitAmount;
        }

        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var actorUserId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Avval tizimga kiring.");
        db.FinancialAuditEntries.Add(new FinancialAuditEntry
        {
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            Action = action,
            Description = $"{branch.Name}, {category}, {month:yyyy-MM}: {limitAmount:N0} so‘m"
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<StudentDebtSummary>> GetStudentDebtReportAsync(DateOnly throughMonth, int? branchId = null)
    {
        if (throughMonth.Day != 1)
        {
            throw new InvalidOperationException("Hisobot oyi oyning birinchi sanasi bo‘lishi kerak.");
        }

        var organizationId = await GetOrganizationIdAsync(CenterAccess.Finance);
        await using var db = await contextFactory.CreateDbContextAsync();

        // Hisobot oyidan oldingi oylar hisoblanadi. Manba — o'quvchi balansi daftari (ChargeLedger),
        // shuning uchun bu yerdagi qarz o'quvchi profili va "Qarzdorlar" sahifasidagi bilan bir xil.
        var lastChargedMonth = throughMonth.AddMonths(-1);
        await ChargeLedger.SyncAsync(db, organizationId, ChargeLedger.CurrentMonth() < lastChargedMonth ? ChargeLedger.CurrentMonth() : lastChargedMonth);
        var lastIncludedDay = throughMonth.AddDays(-1);

        var charges = await db.StudentCharges.AsNoTracking()
            .Where(charge => charge.OrganizationId == organizationId && charge.Month < throughMonth)
            .Select(charge => new { charge.StudentId, charge.StudyGroupId, charge.Amount })
            .ToListAsync();
        var payments = await db.FinancialTransactions.AsNoTracking()
            .Where(item =>
                item.OrganizationId == organizationId &&
                item.Type == FinancialTransactionType.Income &&
                item.StudentId.HasValue &&
                item.TransactionDate <= lastIncludedDay)
            .Select(item => new { StudentId = item.StudentId!.Value, item.Amount })
            .ToListAsync();

        var studentIds = charges.Select(item => item.StudentId).Union(payments.Select(item => item.StudentId)).ToArray();
        var students = await (
            from student in db.Students.AsNoTracking()
            join branch in db.Branches.AsNoTracking()
                on new { student.OrganizationId, student.BranchId } equals new { branch.OrganizationId, BranchId = branch.Id }
            where student.OrganizationId == organizationId && studentIds.Contains(student.Id)
            select new { student.Id, student.FullName, student.BranchId, BranchName = branch.Name })
            .ToListAsync();
        if (branchId.HasValue)
        {
            students = students.Where(item => item.BranchId == branchId.Value).ToList();
        }

        var groupNames = await db.StudyGroups.AsNoTracking()
            .Where(group => group.OrganizationId == organizationId)
            .Select(group => new { group.Id, group.Name })
            .ToDictionaryAsync(group => group.Id, group => group.Name);

        return students
            .Select(student =>
            {
                var studentCharges = charges.Where(item => item.StudentId == student.Id).ToList();
                var expected = studentCharges.Sum(item => item.Amount);
                var paid = payments.Where(item => item.StudentId == student.Id).Sum(item => item.Amount);
                return new StudentDebtSummary(
                    student.Id,
                    student.FullName,
                    student.BranchName,
                    string.Join(", ", studentCharges.Select(item => groupNames.GetValueOrDefault(item.StudyGroupId, "—")).Distinct().Order()),
                    expected,
                    paid,
                    Math.Max(0, expected - paid));
            })
            .Where(item => item.ExpectedAmount > 0)
            .OrderByDescending(item => item.Balance)
            .ThenBy(item => item.StudentName)
            .ToList();
    }

    public async Task<IReadOnlyList<FinancialAuditSummary>> GetFinancialAuditAsync(DateOnly startDate, DateOnly endDate, int limit = 100)
    {
        if (endDate < startDate || limit is < 1 or > 500)
        {
            throw new InvalidOperationException("Audit sanalari va natijalar sonini tekshiring.");
        }

        var organizationId = await GetOrganizationIdAsync(CenterAccess.Finance);
        var startUtc = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtcExclusive = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await using var db = await contextFactory.CreateDbContextAsync();
        return await (
            from entry in db.FinancialAuditEntries.AsNoTracking()
            where entry.OrganizationId == organizationId &&
                entry.RecordedAtUtc >= startUtc &&
                entry.RecordedAtUtc < endUtcExclusive
            orderby entry.RecordedAtUtc descending, entry.Id descending
            select new FinancialAuditSummary(
                entry.RecordedAtUtc,
                db.Users.Where(user => user.Id == entry.ActorUserId).Select(user => user.Email).FirstOrDefault() ?? "Noma’lum foydalanuvchi",
                entry.Action,
                entry.Description))
            .Take(limit)
            .ToListAsync();
    }
}

public sealed record StudyGroupSummary(
    int Id,
    string Name,
    string CourseName,
    int BranchId,
    string BranchName,
    string TeacherName,
    string RoomName,
    string DaysOfWeek,
    string StartTime,
    int Capacity,
    decimal MonthlyFee,
    int LessonsPerMonth,
    int LessonDurationMinutes,
    int StudentCount,
    int? TeacherId = null,
    decimal TeacherSharePercent = 0,
    int CourseId = 0,
    int? RoomId = null);

public sealed record BranchRoomSummary(
    int Id,
    int BranchId,
    string Name,
    string BranchName,
    int Capacity,
    int ActiveGroupCount);

public sealed class AttendanceEntry(int studentId, string studentName, AttendanceStatus? status)
{
    public int StudentId { get; } = studentId;
    public string StudentName { get; } = studentName;
    public AttendanceStatus? Status { get; set; } = status;
}

public sealed record EmployeeSummary(
    int Id,
    string FullName,
    string Phone,
    string Position,
    EmployeeRole Role,
    int BranchId,
    string BranchName,
    DateOnly HiredOn,
    decimal AgreedMonthlySalary,
    EmployeePayType PayType = EmployeePayType.SalaryAndShare,
    decimal DefaultSharePercent = 0);

public sealed record EmployeePayrollSummary(
    int EmployeeId,
    string FullName,
    string Position,
    EmployeeRole Role,
    decimal AgreedMonthlySalary,
    decimal Bonuses,
    decimal Advances,
    decimal SalaryPayments,
    decimal Remaining,
    decimal RevenueShare = 0,
    EmployeePayType PayType = EmployeePayType.SalaryAndShare);

public sealed record PayrollEntrySummary(
    int Id,
    string EmployeeName,
    DateOnly EntryDate,
    PayrollEntryType Type,
    decimal Amount,
    PaymentMethod? PaymentMethod,
    string Note);

public sealed record FinancialTransactionSummary(
    int Id,
    DateOnly Date,
    FinancialTransactionType Type,
    string Category,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string Note,
    int BranchId,
    string BranchName,
    string? StudentName);

public sealed record ExpenseBudgetSummary(
    int Id,
    int BranchId,
    string BranchName,
    DateOnly Month,
    string Category,
    decimal LimitAmount);

public sealed record StudentDebtSummary(
    int StudentId,
    string StudentName,
    string BranchName,
    string GroupNames,
    decimal ExpectedAmount,
    decimal PaidAmount,
    decimal Balance);

public sealed record FinancialAuditSummary(
    DateTime RecordedAtUtc,
    string Actor,
    string Action,
    string Description);

public sealed record TeacherShareGroup(
    int GroupId,
    string Name,
    decimal SharePercent,
    decimal MonthlyFee,
    int StudentCount,
    decimal MonthIncome,
    decimal MonthShare);

public sealed record TeacherShareRow(
    int EmployeeId,
    string FullName,
    EmployeePayType PayType,
    decimal AgreedMonthlySalary,
    decimal DefaultSharePercent,
    IReadOnlyList<TeacherShareGroup> Groups);
public sealed record TeacherOption(int Id, string FullName);
