using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace EduHisob.Data;

// O'quvchi va guruh profillari, balans, chegirma, muzlatish, guruhdan chiqarish, qarzdorlar doskasi va chek.
public sealed class StudentAccountService(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    CenterAccess centerAccess)
{
    private static readonly string[] DefaultLeaveReasons =
    [
        "Kursni tugatdi",
        "Shaxsiy sabab",
        "Bog‘lanib bo‘lmadi",
        "Narx qimmatlik qildi",
        "Ustoz yoqmadi",
        "Vaqt to‘g‘ri kelmadi",
        "Boshqa joyga ko‘chdi",
        "Xato kiritilgan (dublikat)"
    ];

    // ---------- O'quvchi profili ----------

    public async Task<StudentProfile> GetStudentProfileAsync(int studentId)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.StudentDesk);
        var organizationId = user.OrganizationId;
        await using var db = await contextFactory.CreateDbContextAsync();
        await ChargeLedger.SyncAsync(db, organizationId, ChargeLedger.CurrentMonth());

        var student = await db.Students.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == studentId)
            ?? throw new InvalidOperationException("O‘quvchi topilmadi.");
        var branchName = await db.Branches.Where(branch => branch.OrganizationId == organizationId && branch.Id == student.BranchId)
            .Select(branch => branch.Name).SingleAsync();
        var totals = (await ChargeLedger.GetTotalsAsync(db, organizationId, [studentId]))
            .GetValueOrDefault(studentId, new StudentLedgerTotals(0, 0));

        var reasons = await db.LeaveReasons.AsNoTracking()
            .Where(reason => reason.OrganizationId == organizationId)
            .ToDictionaryAsync(reason => reason.Id, reason => reason.Name);
        var monthStart = ChargeLedger.CurrentMonth();
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(record =>
                record.OrganizationId == organizationId &&
                record.StudentId == studentId &&
                record.LessonDate >= monthStart &&
                record.LessonDate <= monthEnd)
            .Select(record => new { record.StudyGroupId, record.LessonDate, record.Status })
            .ToListAsync();

        var groups = (await (
                from enrollment in db.GroupEnrollments.AsNoTracking()
                join studyGroup in db.StudyGroups.AsNoTracking()
                    on new { enrollment.OrganizationId, GroupId = enrollment.StudyGroupId } equals new { studyGroup.OrganizationId, GroupId = studyGroup.Id }
                join course in db.Courses.AsNoTracking()
                    on new { studyGroup.OrganizationId, studyGroup.CourseId } equals new { course.OrganizationId, CourseId = course.Id }
                where enrollment.OrganizationId == organizationId && enrollment.StudentId == studentId
                orderby enrollment.LeftOn, studyGroup.Name
                select new
                {
                    studyGroup.Id,
                    studyGroup.Name,
                    CourseName = course.Name,
                    studyGroup.TeacherName,
                    studyGroup.DaysOfWeek,
                    studyGroup.StartTime,
                    studyGroup.MonthlyFee,
                    enrollment.DiscountPercent,
                    enrollment.DiscountNote,
                    enrollment.EnrolledAtUtc,
                    enrollment.LeftOn,
                    enrollment.LeaveReasonId,
                    enrollment.LeaveNote
                }).ToListAsync())
            .Select(item => new StudentGroupMembership(
                item.Id,
                item.Name,
                item.CourseName,
                item.TeacherName,
                item.DaysOfWeek,
                item.StartTime,
                item.MonthlyFee,
                item.DiscountPercent,
                item.DiscountNote,
                ChargeLedger.ApplyDiscount(item.MonthlyFee, item.DiscountPercent),
                DateOnly.FromDateTime(item.EnrolledAtUtc),
                item.LeftOn,
                item.LeaveReasonId is { } reasonId ? reasons.GetValueOrDefault(reasonId) : null,
                item.LeaveNote,
                attendance.Where(record => record.StudyGroupId == item.Id)
                    .OrderBy(record => record.LessonDate)
                    .Select(record => new AttendanceMark(record.LessonDate, record.Status))
                    .ToList()))
            .ToList();
        var groupNames = groups.ToDictionary(group => group.GroupId, group => group.GroupName);

        var userNames = await db.Users.AsNoTracking()
            .Where(account => account.OrganizationId == organizationId)
            .Select(account => new { account.Id, account.FullName, account.Email })
            .ToDictionaryAsync(account => account.Id, account => string.IsNullOrWhiteSpace(account.FullName) ? account.Email ?? "" : account.FullName);

        var payments = (await db.FinancialTransactions.AsNoTracking()
                .Where(transaction =>
                    transaction.OrganizationId == organizationId &&
                    transaction.StudentId == studentId &&
                    transaction.Type == FinancialTransactionType.Income)
                .OrderByDescending(transaction => transaction.TransactionDate)
                .ThenByDescending(transaction => transaction.Id)
                .Select(transaction => new
                {
                    transaction.Id,
                    transaction.TransactionDate,
                    transaction.Amount,
                    transaction.PaymentMethod,
                    transaction.StudyGroupId,
                    transaction.Note,
                    transaction.RecordedByUserId
                })
                .ToListAsync())
            .Select(item => new StudentPaymentRow(
                item.Id,
                item.TransactionDate,
                item.Amount,
                item.PaymentMethod,
                item.StudyGroupId is { } groupId ? groupNames.GetValueOrDefault(groupId, "—") : null,
                item.Note,
                item.RecordedByUserId is { } recorder ? userNames.GetValueOrDefault(recorder, "") : ""))
            .ToList();

        var charges = (await db.StudentCharges.AsNoTracking()
                .Where(charge => charge.OrganizationId == organizationId && charge.StudentId == studentId)
                .OrderByDescending(charge => charge.Month)
                .Select(charge => new { charge.Month, charge.StudyGroupId, charge.FullAmount, charge.DiscountAmount, charge.Amount })
                .ToListAsync())
            .Select(item => new StudentChargeRow(
                item.Month,
                groupNames.GetValueOrDefault(item.StudyGroupId, "—"),
                item.FullAmount,
                item.DiscountAmount,
                item.Amount))
            .ToList();

        var freezes = await db.StudentFreezes.AsNoTracking()
            .Where(freeze => freeze.OrganizationId == organizationId && freeze.StudentId == studentId)
            .OrderByDescending(freeze => freeze.FromMonth)
            .Select(freeze => new StudentFreezeRow(freeze.Id, freeze.FromMonth, freeze.ToMonth, freeze.Reason))
            .ToListAsync();

        var notes = (await db.StudentNotes.AsNoTracking()
                .Where(note => note.OrganizationId == organizationId && note.StudentId == studentId)
                .OrderByDescending(note => note.CreatedAtUtc)
                .Select(note => new { note.Id, note.Text, note.AuthorUserId, note.CreatedAtUtc })
                .ToListAsync())
            .Select(note => new StudentNoteRow(note.Id, note.Text, userNames.GetValueOrDefault(note.AuthorUserId, ""), note.CreatedAtUtc))
            .ToList();

        var debtCase = await db.DebtCases.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.StudentId == studentId)
            .Select(item => new { item.Stage, item.PromisedOn, item.Occurrence })
            .SingleOrDefaultAsync();

        return new StudentProfile(
            student,
            branchName,
            totals.Charged,
            totals.Paid,
            totals.Balance,
            groups,
            payments,
            charges,
            freezes,
            notes,
            debtCase?.Stage,
            debtCase?.PromisedOn,
            debtCase?.Occurrence ?? 0);
    }

    public async Task UpdateStudentAsync(int studentId, string fullName, string phone, string parentPhone, DateOnly? birthDate, string notes)
    {
        fullName = fullName.Trim();
        phone = phone.Trim();
        parentPhone = parentPhone.Trim();
        notes = notes.Trim();
        if (fullName.Length is < 2 or > 160 || !IsPhone(phone) || (parentPhone.Length > 0 && !IsPhone(parentPhone)) || notes.Length > 1000)
        {
            throw new InvalidOperationException("Ism (2–160 belgi) va telefon raqamlarini tekshiring.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();
        var student = await FindStudentAsync(db, user.OrganizationId, studentId);
        student.FullName = fullName;
        student.Phone = phone;
        student.ParentPhone = parentPhone;
        student.BirthDate = birthDate;
        student.Notes = notes;
        await db.SaveChangesAsync();
    }

    // Sinov → Faol (yoki aksincha). Muzlatish, chiqarish va arxiv alohida amallar orqali.
    public async Task SetStudentStatusAsync(int studentId, StudentStatus status)
    {
        if (status is not (StudentStatus.Trial or StudentStatus.Active or StudentStatus.Archived))
        {
            throw new InvalidOperationException("Muzlatish va markazdan chiqarish uchun alohida tugmalardan foydalaning.");
        }

        var user = await centerAccess.RequireAsync(status == StudentStatus.Archived ? CenterAccess.Management : CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();
        var student = await FindStudentAsync(db, user.OrganizationId, studentId);
        if (status == StudentStatus.Archived &&
            await db.GroupEnrollments.AnyAsync(item => item.OrganizationId == user.OrganizationId && item.StudentId == studentId && item.LeftOn == null))
        {
            throw new InvalidOperationException("Arxivlashdan oldin o‘quvchini barcha guruhlardan chiqaring.");
        }

        if (student.Status == StudentStatus.Trial && status == StudentStatus.Active)
        {
            // To'lov hisobi faollashgan kundan boshlanadi, sinov davri hisoblanmaydi.
            student.JoinedOn = DateOnly.FromDateTime(DateTime.Today);
        }

        student.Status = status;
        await db.SaveChangesAsync();
    }

    public async Task FreezeAsync(int studentId, DateOnly fromMonth, DateOnly toMonth, string reason)
    {
        reason = reason.Trim();
        fromMonth = ChargeLedger.MonthOf(fromMonth);
        toMonth = ChargeLedger.MonthOf(toMonth);
        if (toMonth < fromMonth || toMonth > fromMonth.AddMonths(11))
        {
            throw new InvalidOperationException("Muzlatish oralig‘i 1–12 oy bo‘lishi kerak.");
        }
        if (fromMonth < ChargeLedger.CurrentMonth())
        {
            throw new InvalidOperationException("O‘tgan oylarni muzlatib bo‘lmaydi. Joriy yoki keyingi oydan boshlang.");
        }
        if (reason.Length is < 3 or > 300)
        {
            throw new InvalidOperationException("Muzlatish sababini yozing (3–300 belgi).");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();
        var student = await FindStudentAsync(db, user.OrganizationId, studentId);
        if (student.Status is not (StudentStatus.Active or StudentStatus.Frozen))
        {
            throw new InvalidOperationException("Faqat faol o‘quvchini muzlatish mumkin.");
        }
        if (await db.StudentFreezes.AnyAsync(freeze =>
            freeze.OrganizationId == user.OrganizationId &&
            freeze.StudentId == studentId &&
            freeze.FromMonth <= toMonth &&
            freeze.ToMonth >= fromMonth))
        {
            throw new InvalidOperationException("Bu oylar uchun muzlatish allaqachon mavjud.");
        }

        // Shu oylar uchun allaqachon hisoblangan (to'lanmagan) summalar bekor qilinadi.
        var charges = await db.StudentCharges.Where(charge =>
            charge.OrganizationId == user.OrganizationId &&
            charge.StudentId == studentId &&
            charge.Month >= fromMonth &&
            charge.Month <= toMonth).ToListAsync();
        db.StudentCharges.RemoveRange(charges);
        db.StudentFreezes.Add(new StudentFreeze
        {
            OrganizationId = user.OrganizationId,
            StudentId = studentId,
            FromMonth = fromMonth,
            ToMonth = toMonth,
            Reason = reason
        });
        await db.SaveChangesAsync();
        await ChargeLedger.ReconcileFrozenStatusesAsync(db, user.OrganizationId);
    }

    public async Task CancelFreezeAsync(int studentId, int freezeId)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();
        var freeze = await db.StudentFreezes.SingleOrDefaultAsync(item =>
            item.OrganizationId == user.OrganizationId && item.StudentId == studentId && item.Id == freezeId)
            ?? throw new InvalidOperationException("Muzlatish topilmadi.");
        var currentMonth = ChargeLedger.CurrentMonth();
        if (freeze.ToMonth < currentMonth)
        {
            throw new InvalidOperationException("Tugagan muzlatishni bekor qilib bo‘lmaydi.");
        }

        if (freeze.FromMonth >= currentMonth)
        {
            db.StudentFreezes.Remove(freeze);
        }
        else
        {
            // Boshlangan muzlatish o'tgan oy bilan yakunlanadi; joriy oydan to'lov qayta hisoblanadi.
            freeze.ToMonth = currentMonth.AddMonths(-1);
        }

        await db.SaveChangesAsync();
        await ChargeLedger.ReconcileFrozenStatusesAsync(db, user.OrganizationId);
    }

    public async Task SetDiscountAsync(int studentId, int groupId, decimal discountPercent, string note)
    {
        note = note.Trim();
        if (discountPercent is < 0 or > 100 || note.Length > 200)
        {
            throw new InvalidOperationException("Chegirma 0–100% oralig‘ida, izohi 200 belgidan oshmasin.");
        }
        if (discountPercent > 0 && note.Length < 3)
        {
            throw new InvalidOperationException("Chegirma sababini yozing (masalan: aka-uka, a’lochi, xodim farzandi).");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.Management);
        await using var db = await contextFactory.CreateDbContextAsync();
        var enrollment = await db.GroupEnrollments.SingleOrDefaultAsync(item =>
            item.OrganizationId == user.OrganizationId && item.StudentId == studentId && item.StudyGroupId == groupId && item.LeftOn == null)
            ?? throw new InvalidOperationException("O‘quvchi bu guruhda faol emas.");
        var fee = await db.StudyGroups.Where(group => group.OrganizationId == user.OrganizationId && group.Id == groupId)
            .Select(group => group.MonthlyFee).SingleAsync();
        enrollment.DiscountPercent = discountPercent;
        enrollment.DiscountNote = note;

        // Joriy va keyingi oylar hisobi yangi chegirma bilan qayta hisoblanadi; o'tgan oylar o'zgarmaydi.
        var currentMonth = ChargeLedger.CurrentMonth();
        var charges = await db.StudentCharges.Where(charge =>
            charge.OrganizationId == user.OrganizationId &&
            charge.StudentId == studentId &&
            charge.StudyGroupId == groupId &&
            charge.Month >= currentMonth).ToListAsync();
        foreach (var charge in charges)
        {
            charge.FullAmount = fee;
            charge.Amount = ChargeLedger.ApplyDiscount(fee, discountPercent);
            charge.DiscountAmount = fee - charge.Amount;
        }

        db.FinancialAuditEntries.Add(new FinancialAuditEntry
        {
            OrganizationId = user.OrganizationId,
            ActorUserId = user.UserId,
            Action = "Chegirma",
            Description = $"O‘quvchi #{studentId}, guruh #{groupId}: {discountPercent:0.##}% ({note})"
        });
        await db.SaveChangesAsync();
    }

    public async Task LeaveGroupAsync(int studentId, int groupId, DateOnly leftOn, int leaveReasonId, string note)
    {
        note = note.Trim();
        if (note.Length > 300)
        {
            throw new InvalidOperationException("Izoh 300 belgidan oshmasin.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();
        var enrollment = await db.GroupEnrollments.SingleOrDefaultAsync(item =>
            item.OrganizationId == user.OrganizationId && item.StudentId == studentId && item.StudyGroupId == groupId && item.LeftOn == null)
            ?? throw new InvalidOperationException("O‘quvchi bu guruhda faol emas.");
        if (!await db.LeaveReasons.AnyAsync(reason => reason.OrganizationId == user.OrganizationId && reason.Id == leaveReasonId && reason.IsActive))
        {
            throw new InvalidOperationException("Chiqish sababini tanlang.");
        }
        if (leftOn < DateOnly.FromDateTime(enrollment.EnrolledAtUtc))
        {
            throw new InvalidOperationException("Chiqish sanasi guruhga qo‘shilgan sanadan oldin bo‘lishi mumkin emas.");
        }

        enrollment.LeftOn = leftOn;
        enrollment.LeaveReasonId = leaveReasonId;
        enrollment.LeaveNote = note;

        // Chiqqandan keyingi oylar uchun hisoblangan summalar bekor qilinadi.
        var firstNotChargedMonth = ChargeLedger.MonthOf(leftOn).AddMonths(leftOn.Day == 1 ? 0 : 1);
        var futureCharges = await db.StudentCharges.Where(charge =>
            charge.OrganizationId == user.OrganizationId &&
            charge.StudentId == studentId &&
            charge.StudyGroupId == groupId &&
            charge.Month >= firstNotChargedMonth).ToListAsync();
        db.StudentCharges.RemoveRange(futureCharges);

        var stillActive = await db.GroupEnrollments.AnyAsync(item =>
            item.OrganizationId == user.OrganizationId &&
            item.StudentId == studentId &&
            item.StudyGroupId != groupId &&
            item.LeftOn == null);
        if (!stillActive)
        {
            var student = await FindStudentAsync(db, user.OrganizationId, studentId);
            if (student.Status is StudentStatus.Active or StudentStatus.Trial or StudentStatus.Frozen)
            {
                student.Status = StudentStatus.Left;
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task AddNoteAsync(int studentId, string text)
    {
        text = text.Trim();
        if (text.Length is < 2 or > 1000)
        {
            throw new InvalidOperationException("Izoh 2–1000 belgidan iborat bo‘lsin.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();
        await FindStudentAsync(db, user.OrganizationId, studentId);
        db.StudentNotes.Add(new StudentNote
        {
            OrganizationId = user.OrganizationId,
            StudentId = studentId,
            Text = text,
            AuthorUserId = user.UserId
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(string phone, string fullName)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.StudentDesk);
        var digits = DigitsOnly(phone);
        var name = fullName.Trim().ToLowerInvariant();
        await using var db = await contextFactory.CreateDbContextAsync();
        var students = await db.Students.AsNoTracking()
            .Where(student => student.OrganizationId == user.OrganizationId)
            .Select(student => new { student.Id, student.FullName, student.Phone, student.ParentPhone, student.Status })
            .ToListAsync();

        // Telefonlar turlicha yozilishi mumkin (+998 90..., 90-...), shuning uchun oxirgi 9 raqam solishtiriladi.
        var key = digits.Length >= 9 ? digits[^9..] : digits;
        return students
            .Where(student =>
                (key.Length >= 7 && (DigitsOnly(student.Phone).EndsWith(key, StringComparison.Ordinal) ||
                                     DigitsOnly(student.ParentPhone).EndsWith(key, StringComparison.Ordinal))) ||
                (name.Length >= 5 && student.FullName.Trim().ToLowerInvariant() == name))
            .Select(student => new DuplicateCandidate(student.Id, student.FullName, student.Phone, student.Status))
            .Take(5)
            .ToList();
    }

    public async Task<Dictionary<int, decimal>> GetBalancesAsync()
    {
        var user = await centerAccess.RequireAsync(CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();
        await ChargeLedger.SyncAsync(db, user.OrganizationId, ChargeLedger.CurrentMonth());
        return (await ChargeLedger.GetTotalsAsync(db, user.OrganizationId))
            .ToDictionary(item => item.Key, item => item.Value.Balance);
    }

    // ---------- Chiqish sabablari va chiqib ketganlar ----------

    public async Task<IReadOnlyList<LeaveReason>> GetLeaveReasonsAsync()
    {
        var user = await centerAccess.RequireAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        if (!await db.LeaveReasons.AnyAsync(reason => reason.OrganizationId == user.OrganizationId))
        {
            foreach (var name in DefaultLeaveReasons)
            {
                db.LeaveReasons.Add(new LeaveReason { OrganizationId = user.OrganizationId, Name = name, NormalizedName = LeaveReason.Normalize(name) });
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return await db.LeaveReasons.AsNoTracking()
            .Where(reason => reason.OrganizationId == user.OrganizationId && reason.IsActive)
            .OrderBy(reason => reason.Name)
            .ToListAsync();
    }

    public async Task AddLeaveReasonAsync(string name)
    {
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 3 or > 120)
        {
            throw new InvalidOperationException("Sabab nomi 3–120 belgidan iborat bo‘lsin.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.Management);
        var normalized = LeaveReason.Normalize(name);
        await using var db = await contextFactory.CreateDbContextAsync();
        var existing = await db.LeaveReasons.SingleOrDefaultAsync(reason =>
            reason.OrganizationId == user.OrganizationId && reason.NormalizedName == normalized);
        if (existing is { IsActive: true })
        {
            throw new InvalidOperationException($"“{existing.Name}” sababi allaqachon mavjud.");
        }

        if (existing is not null)
        {
            existing.IsActive = true;
        }
        else
        {
            db.LeaveReasons.Add(new LeaveReason { OrganizationId = user.OrganizationId, Name = name, NormalizedName = normalized });
        }

        await db.SaveChangesAsync();
    }

    public async Task DeactivateLeaveReasonAsync(int reasonId)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Management);
        await using var db = await contextFactory.CreateDbContextAsync();
        var reason = await db.LeaveReasons.SingleOrDefaultAsync(item => item.OrganizationId == user.OrganizationId && item.Id == reasonId)
            ?? throw new InvalidOperationException("Sabab topilmadi.");
        reason.IsActive = false;
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<LeftStudentRow>> GetLeftStudentsAsync(DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
        {
            throw new InvalidOperationException("Sana oralig‘ini tekshiring.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.StudentDesk);
        await using var db = await contextFactory.CreateDbContextAsync();
        var rows = await (
            from enrollment in db.GroupEnrollments.AsNoTracking()
            join student in db.Students.AsNoTracking()
                on new { enrollment.OrganizationId, enrollment.StudentId } equals new { student.OrganizationId, StudentId = student.Id }
            join studyGroup in db.StudyGroups.AsNoTracking()
                on new { enrollment.OrganizationId, GroupId = enrollment.StudyGroupId } equals new { studyGroup.OrganizationId, GroupId = studyGroup.Id }
            join reason in db.LeaveReasons.AsNoTracking()
                on new { enrollment.OrganizationId, ReasonId = enrollment.LeaveReasonId } equals new { reason.OrganizationId, ReasonId = (int?)reason.Id } into reasonJoin
            from reason in reasonJoin.DefaultIfEmpty()
            where enrollment.OrganizationId == user.OrganizationId &&
                enrollment.LeftOn != null &&
                enrollment.LeftOn >= fromDate &&
                enrollment.LeftOn <= toDate
            orderby enrollment.LeftOn descending
            select new LeftStudentRow(
                student.Id,
                student.FullName,
                student.Phone,
                studyGroup.Name,
                studyGroup.TeacherName,
                enrollment.LeftOn!.Value,
                reason == null ? "Sabab ko‘rsatilmagan" : reason.Name,
                enrollment.LeaveNote))
            .ToListAsync();
        return rows;
    }

    // ---------- Qarzdorlar doskasi ----------

    public async Task<IReadOnlyList<DebtorCard>> GetDebtorsAsync(int? branchId = null)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Cash);
        var organizationId = user.OrganizationId;
        await using var db = await contextFactory.CreateDbContextAsync();
        await ChargeLedger.SyncAsync(db, organizationId, ChargeLedger.CurrentMonth());
        var totals = await ChargeLedger.GetTotalsAsync(db, organizationId);

        // Holatlarni balansga moslash: yangi qarzdorlar "Yangi"ga, qarzini yopganlar "To'ladi"ga o'tadi.
        var cases = await db.DebtCases.Where(item => item.OrganizationId == organizationId).ToDictionaryAsync(item => item.StudentId);
        var now = DateTime.UtcNow;
        foreach (var (studentId, total) in totals)
        {
            cases.TryGetValue(studentId, out var debtCase);
            if (total.Balance < 0)
            {
                if (debtCase is null)
                {
                    debtCase = new DebtCase { OrganizationId = organizationId, StudentId = studentId, Stage = DebtStage.New };
                    db.DebtCases.Add(debtCase);
                    cases[studentId] = debtCase;
                }
                else if (debtCase.Stage == DebtStage.Paid)
                {
                    debtCase.Stage = DebtStage.New;
                    debtCase.Occurrence++;
                    debtCase.PromisedOn = null;
                    debtCase.Note = "";
                    debtCase.UpdatedAtUtc = now;
                }
            }
            else if (debtCase is not null && debtCase.Stage != DebtStage.Paid)
            {
                debtCase.Stage = DebtStage.Paid;
                debtCase.UpdatedAtUtc = now;
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }

        var recentPaidSince = now.AddDays(-30);
        var visible = cases.Values
            .Where(item => item.Stage != DebtStage.Paid || item.UpdatedAtUtc >= recentPaidSince)
            .ToList();
        var studentIds = visible.Select(item => item.StudentId).ToArray();
        var students = await (
            from student in db.Students.AsNoTracking()
            join branch in db.Branches.AsNoTracking()
                on new { student.OrganizationId, student.BranchId } equals new { branch.OrganizationId, BranchId = branch.Id }
            where student.OrganizationId == organizationId && studentIds.Contains(student.Id)
            select new { student.Id, student.FullName, student.Phone, student.ParentPhone, student.BranchId, BranchName = branch.Name })
            .ToDictionaryAsync(item => item.Id);
        var groups = await (
            from enrollment in db.GroupEnrollments.AsNoTracking()
            join studyGroup in db.StudyGroups.AsNoTracking()
                on new { enrollment.OrganizationId, GroupId = enrollment.StudyGroupId } equals new { studyGroup.OrganizationId, GroupId = studyGroup.Id }
            where enrollment.OrganizationId == organizationId && studentIds.Contains(enrollment.StudentId) && enrollment.LeftOn == null
            select new { enrollment.StudentId, studyGroup.Name })
            .ToListAsync();
        var lastPayments = (await db.FinancialTransactions.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && item.StudentId != null && studentIds.Contains(item.StudentId.Value))
                .Select(item => new { StudentId = item.StudentId!.Value, item.TransactionDate })
                .ToListAsync())
            .GroupBy(item => item.StudentId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.TransactionDate));

        return visible
            .Where(item => students.ContainsKey(item.StudentId))
            .Where(item => branchId is null || students[item.StudentId].BranchId == branchId)
            .Select(item =>
            {
                var student = students[item.StudentId];
                return new DebtorCard(
                    item.StudentId,
                    student.FullName,
                    student.Phone,
                    student.ParentPhone,
                    student.BranchName,
                    string.Join(", ", groups.Where(group => group.StudentId == item.StudentId).Select(group => group.Name).Order()),
                    Math.Max(0, -totals.GetValueOrDefault(item.StudentId, new StudentLedgerTotals(0, 0)).Balance),
                    item.Stage,
                    item.PromisedOn,
                    item.Note,
                    item.Occurrence,
                    lastPayments.TryGetValue(item.StudentId, out var lastPayment) ? lastPayment : null,
                    item.UpdatedAtUtc);
            })
            .OrderByDescending(item => item.Debt)
            .ToList();
    }

    public async Task UpdateDebtCaseAsync(int studentId, DebtStage stage, DateOnly? promisedOn, string note)
    {
        note = note.Trim();
        if (!Enum.IsDefined(stage) || stage == DebtStage.Paid)
        {
            throw new InvalidOperationException("“To‘ladi” holati to‘lov qabul qilinganda avtomatik qo‘yiladi.");
        }
        if (stage == DebtStage.Promised && (promisedOn is null || promisedOn < DateOnly.FromDateTime(DateTime.Today)))
        {
            throw new InvalidOperationException("Va’da qilingan to‘lov sanasini kiriting (bugun yoki keyin).");
        }
        if (note.Length > 500)
        {
            throw new InvalidOperationException("Izoh 500 belgidan oshmasin.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.Cash);
        await using var db = await contextFactory.CreateDbContextAsync();
        var debtCase = await db.DebtCases.SingleOrDefaultAsync(item => item.OrganizationId == user.OrganizationId && item.StudentId == studentId)
            ?? throw new InvalidOperationException("Qarzdor topilmadi. Sahifani yangilang.");
        debtCase.Stage = stage;
        debtCase.PromisedOn = stage == DebtStage.Promised ? promisedOn : null;
        debtCase.Note = note;
        debtCase.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    // ---------- Guruh profili ----------

    public async Task<GroupProfile> GetGroupProfileAsync(int groupId, DateOnly month)
    {
        month = ChargeLedger.MonthOf(month);
        var user = await centerAccess.RequireAsync();
        var organizationId = user.OrganizationId;
        var canSeeMoney = CenterAccess.StudentDesk.Contains(user.Role);
        var canSeeShare = CenterAccess.Finance.Contains(user.Role);
        await using var db = await contextFactory.CreateDbContextAsync();
        if (canSeeMoney)
        {
            await ChargeLedger.SyncAsync(db, organizationId, ChargeLedger.CurrentMonth());
        }

        var group = await (
            from studyGroup in db.StudyGroups.AsNoTracking()
            join course in db.Courses.AsNoTracking()
                on new { studyGroup.OrganizationId, studyGroup.CourseId } equals new { course.OrganizationId, CourseId = course.Id }
            join branch in db.Branches.AsNoTracking()
                on new { studyGroup.OrganizationId, studyGroup.BranchId } equals new { branch.OrganizationId, BranchId = branch.Id }
            where studyGroup.OrganizationId == organizationId && studyGroup.Id == groupId
            select new { Group = studyGroup, CourseName = course.Name, BranchName = branch.Name })
            .SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("Guruh topilmadi.");

        var monthEnd = month.AddMonths(1).AddDays(-1);
        var members = await (
            from enrollment in db.GroupEnrollments.AsNoTracking()
            join student in db.Students.AsNoTracking()
                on new { enrollment.OrganizationId, enrollment.StudentId } equals new { student.OrganizationId, StudentId = student.Id }
            where enrollment.OrganizationId == organizationId &&
                enrollment.StudyGroupId == groupId &&
                (enrollment.LeftOn == null || enrollment.LeftOn >= month)
            orderby student.FullName
            select new
            {
                student.Id,
                student.FullName,
                student.Phone,
                student.Status,
                enrollment.DiscountPercent,
                enrollment.EnrolledAtUtc,
                enrollment.LeftOn
            }).ToListAsync();

        var studentIds = members.Select(member => member.Id).ToArray();
        var totals = canSeeMoney ? await ChargeLedger.GetTotalsAsync(db, organizationId, studentIds) : [];
        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(record =>
                record.OrganizationId == organizationId &&
                record.StudyGroupId == groupId &&
                record.LessonDate >= month &&
                record.LessonDate <= monthEnd)
            .Select(record => new { record.StudentId, record.LessonDate, record.Status })
            .ToListAsync();
        var lessonDates = attendance.Select(record => record.LessonDate).Distinct().Order().ToList();

        decimal monthIncome = 0;
        if (canSeeMoney)
        {
            monthIncome = (await db.FinancialTransactions.AsNoTracking()
                    .Where(item =>
                        item.OrganizationId == organizationId &&
                        item.Type == FinancialTransactionType.Income &&
                        item.StudyGroupId == groupId &&
                        item.TransactionDate >= month &&
                        item.TransactionDate <= monthEnd)
                    .Select(item => item.Amount)
                    .ToListAsync())
                .Sum();
        }

        var rows = members.Select(member => new GroupMemberRow(
                member.Id,
                member.FullName,
                member.Phone,
                member.Status,
                member.DiscountPercent,
                ChargeLedger.ApplyDiscount(group.Group.MonthlyFee, member.DiscountPercent),
                DateOnly.FromDateTime(member.EnrolledAtUtc),
                member.LeftOn,
                canSeeMoney ? totals.GetValueOrDefault(member.Id, new StudentLedgerTotals(0, 0)).Balance : null,
                lessonDates.ToDictionary(
                    date => date,
                    date => attendance.FirstOrDefault(record => record.StudentId == member.Id && record.LessonDate == date)?.Status)))
            .ToList();

        return new GroupProfile(
            group.Group.Id,
            group.Group.Name,
            group.CourseName,
            group.BranchName,
            group.Group.BranchId,
            group.Group.TeacherId,
            group.Group.TeacherName,
            canSeeShare ? group.Group.TeacherSharePercent : 0,
            group.Group.RoomName,
            group.Group.DaysOfWeek,
            group.Group.StartTime,
            group.Group.LessonDurationMinutes,
            group.Group.LessonsPerMonth,
            group.Group.Capacity,
            group.Group.MonthlyFee,
            group.Group.IsActive,
            month,
            lessonDates,
            rows,
            canSeeMoney ? monthIncome : null,
            canSeeShare ? Math.Round(monthIncome * group.Group.TeacherSharePercent / 100m, 0, MidpointRounding.AwayFromZero) : null);
    }

    // Ustoz ulushi bu yerda o'zgarmaydi (u faqat moliyada): ustoz almashsa, yangi ustozning foizi olinadi.
    public async Task UpdateGroupAsync(int groupId, string name, int? teacherId, decimal monthlyFee, int capacity)
    {
        name = name.Trim();
        if (name.Length is < 2 or > 120 || monthlyFee is < 0 or > 999999999 || capacity is < 1 or > 1000)
        {
            throw new InvalidOperationException("Guruh nomi, narx va sig‘imni tekshiring.");
        }
        var user = await centerAccess.RequireAsync(CenterAccess.Management);
        await using var db = await contextFactory.CreateDbContextAsync();
        var group = await db.StudyGroups.SingleOrDefaultAsync(item => item.OrganizationId == user.OrganizationId && item.Id == groupId)
            ?? throw new InvalidOperationException("Guruh topilmadi.");
        var activeCount = await db.GroupEnrollments.CountAsync(item =>
            item.OrganizationId == user.OrganizationId && item.StudyGroupId == groupId && item.LeftOn == null);
        if (capacity < activeCount)
        {
            throw new InvalidOperationException($"Guruhda {activeCount} o‘quvchi bor, sig‘im bundan kam bo‘lmasin.");
        }
        if (group.RoomId is { } roomId)
        {
            var roomCapacity = await db.BranchRooms.Where(room => room.OrganizationId == user.OrganizationId && room.Id == roomId)
                .Select(room => room.Capacity).SingleOrDefaultAsync();
            if (roomCapacity > 0 && capacity > roomCapacity)
            {
                throw new InvalidOperationException($"Guruh sig‘imi xona sig‘imidan ({roomCapacity}) oshmasligi kerak.");
            }
        }

        if (teacherId != group.TeacherId)
        {
            if (teacherId is { } newTeacherId)
            {
                var teacher = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee =>
                    employee.OrganizationId == user.OrganizationId && employee.Id == newTeacherId &&
                    employee.Role == EmployeeRole.Teacher && employee.IsActive)
                    ?? throw new InvalidOperationException("Tanlangan ustoz topilmadi.");
                await EnsureTeacherIsFreeAsync(db, user.OrganizationId, group, newTeacherId);
                group.TeacherName = teacher.FullName;
                group.TeacherSharePercent = teacher.PayType == EmployeePayType.SalaryOnly ? 0 : teacher.DefaultSharePercent;
            }
            else
            {
                group.TeacherName = "";
                group.TeacherSharePercent = 0;
            }

            group.TeacherId = teacherId;
        }

        var feeChanged = group.MonthlyFee != monthlyFee;
        group.Name = name;
        group.MonthlyFee = monthlyFee;
        group.Capacity = capacity;
        db.FinancialAuditEntries.Add(new FinancialAuditEntry
        {
            OrganizationId = user.OrganizationId,
            ActorUserId = user.UserId,
            Action = "Guruh tahriri",
            Description = $"{name}: narx {monthlyFee:N0} so‘m, sig‘im {capacity}"
        });
        await db.SaveChangesAsync();
        if (feeChanged)
        {
            // Yangi narx keyingi oydan qo'llanadi; o'tgan va joriy oy hisoblari o'zgarmaydi.
            var nextMonth = ChargeLedger.CurrentMonth().AddMonths(1);
            var futureCharges = await db.StudentCharges.Where(charge =>
                charge.OrganizationId == user.OrganizationId && charge.StudyGroupId == groupId && charge.Month >= nextMonth).ToListAsync();
            db.StudentCharges.RemoveRange(futureCharges);
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureTeacherIsFreeAsync(ApplicationDbContext db, int organizationId, StudyGroup group, int teacherId)
    {
        var days = ParseDays(group.DaysOfWeek);
        var start = TimeOnly.ParseExact(group.StartTime, "HH:mm", CultureInfo.InvariantCulture);
        var startMinutes = start.Hour * 60 + start.Minute;
        var endMinutes = startMinutes + group.LessonDurationMinutes;
        var others = await db.StudyGroups.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.Id != group.Id && item.IsActive && item.TeacherId == teacherId)
            .Select(item => new { item.Name, item.DaysOfWeek, item.StartTime, item.LessonDurationMinutes })
            .ToListAsync();
        foreach (var other in others)
        {
            if (!days.Overlaps(ParseDays(other.DaysOfWeek)))
            {
                continue;
            }

            var otherStart = TimeOnly.ParseExact(other.StartTime, "HH:mm", CultureInfo.InvariantCulture);
            var otherStartMinutes = otherStart.Hour * 60 + otherStart.Minute;
            if (startMinutes < otherStartMinutes + other.LessonDurationMinutes && otherStartMinutes < endMinutes)
            {
                throw new InvalidOperationException($"Bu ustoz {other.Name} guruhi bilan shu vaqtda band.");
            }
        }
    }

    // ---------- Chek ----------

    public async Task<PaymentReceipt> GetReceiptAsync(int transactionId)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Cash);
        await using var db = await contextFactory.CreateDbContextAsync();
        var payment = await db.FinancialTransactions.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.OrganizationId == user.OrganizationId &&
                item.Id == transactionId &&
                item.Type == FinancialTransactionType.Income &&
                item.StudentId != null)
            ?? throw new InvalidOperationException("To‘lov topilmadi.");
        var centerName = await db.Organizations.Where(center => center.Id == user.OrganizationId).Select(center => center.Name).SingleAsync();
        var branchName = await db.Branches.Where(branch => branch.OrganizationId == user.OrganizationId && branch.Id == payment.BranchId)
            .Select(branch => branch.Name).SingleAsync();
        var student = await db.Students.AsNoTracking()
            .Where(item => item.OrganizationId == user.OrganizationId && item.Id == payment.StudentId)
            .Select(item => new { item.FullName, item.Phone })
            .SingleAsync();
        var groupName = payment.StudyGroupId is { } groupId
            ? await db.StudyGroups.Where(group => group.OrganizationId == user.OrganizationId && group.Id == groupId).Select(group => group.Name).SingleOrDefaultAsync()
            : null;
        var cashier = payment.RecordedByUserId is { } recorder
            ? await db.Users.Where(account => account.Id == recorder).Select(account => account.FullName != "" ? account.FullName : account.Email).SingleOrDefaultAsync()
            : null;
        var balance = (await ChargeLedger.GetTotalsAsync(db, user.OrganizationId, [payment.StudentId!.Value]))
            .GetValueOrDefault(payment.StudentId.Value, new StudentLedgerTotals(0, 0)).Balance;

        return new PaymentReceipt(
            payment.Id,
            centerName,
            branchName,
            student.FullName,
            student.Phone,
            groupName,
            payment.TransactionDate,
            payment.Amount,
            payment.PaymentMethod,
            payment.Note,
            cashier ?? "",
            balance);
    }

    // ---------- Yordamchilar ----------

    private static async Task<Student> FindStudentAsync(ApplicationDbContext db, int organizationId, int studentId) =>
        await db.Students.SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == studentId)
        ?? throw new InvalidOperationException("O‘quvchi topilmadi.");

    private static HashSet<int> ParseDays(string days) =>
        days.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(value => int.TryParse(value, out _))
            .Select(int.Parse)
            .ToHashSet();

    internal static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    private static bool IsPhone(string value) => value.Length <= 32 && DigitsOnly(value).Length is >= 7 and <= 15;

    public static string PaymentMethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Naqd",
        PaymentMethod.Card => "Karta",
        PaymentMethod.Terminal => "Terminal",
        _ => "Bank o‘tkazmasi"
    };

    public static string DebtStageLabel(DebtStage stage) => stage switch
    {
        DebtStage.New => "Yangi qarzdorlar",
        DebtStage.Contacted => "Bog‘lanildi",
        DebtStage.Promised => "Va’da berdi",
        DebtStage.Problem => "Muammoli",
        _ => "To‘ladi"
    };
}

public sealed record AttendanceMark(DateOnly Date, AttendanceStatus Status);

public sealed record StudentGroupMembership(
    int GroupId,
    string GroupName,
    string CourseName,
    string TeacherName,
    string DaysOfWeek,
    string StartTime,
    decimal MonthlyFee,
    decimal DiscountPercent,
    string DiscountNote,
    decimal FeeAfterDiscount,
    DateOnly EnrolledOn,
    DateOnly? LeftOn,
    string? LeaveReason,
    string LeaveNote,
    IReadOnlyList<AttendanceMark> MonthAttendance);

public sealed record StudentPaymentRow(int Id, DateOnly Date, decimal Amount, PaymentMethod Method, string? GroupName, string Note, string RecordedBy);
public sealed record StudentChargeRow(DateOnly Month, string GroupName, decimal FullAmount, decimal DiscountAmount, decimal Amount);
public sealed record StudentFreezeRow(int Id, DateOnly FromMonth, DateOnly ToMonth, string Reason);
public sealed record StudentNoteRow(int Id, string Text, string Author, DateTime CreatedAtUtc);

public sealed record StudentProfile(
    Student Student,
    string BranchName,
    decimal TotalCharged,
    decimal TotalPaid,
    decimal Balance,
    IReadOnlyList<StudentGroupMembership> Groups,
    IReadOnlyList<StudentPaymentRow> Payments,
    IReadOnlyList<StudentChargeRow> Charges,
    IReadOnlyList<StudentFreezeRow> Freezes,
    IReadOnlyList<StudentNoteRow> Notes,
    DebtStage? DebtStage,
    DateOnly? PromisedOn,
    int DebtOccurrence);

public sealed record DuplicateCandidate(int Id, string FullName, string Phone, StudentStatus Status);

public sealed record LeftStudentRow(
    int StudentId,
    string StudentName,
    string Phone,
    string GroupName,
    string TeacherName,
    DateOnly LeftOn,
    string Reason,
    string Note);

public sealed record DebtorCard(
    int StudentId,
    string StudentName,
    string Phone,
    string ParentPhone,
    string BranchName,
    string GroupNames,
    decimal Debt,
    DebtStage Stage,
    DateOnly? PromisedOn,
    string Note,
    int Occurrence,
    DateOnly? LastPaymentOn,
    DateTime UpdatedAtUtc);

public sealed record GroupMemberRow(
    int StudentId,
    string FullName,
    string Phone,
    StudentStatus Status,
    decimal DiscountPercent,
    decimal FeeAfterDiscount,
    DateOnly EnrolledOn,
    DateOnly? LeftOn,
    decimal? Balance,
    IReadOnlyDictionary<DateOnly, AttendanceStatus?> Attendance);

public sealed record GroupProfile(
    int Id,
    string Name,
    string CourseName,
    string BranchName,
    int BranchId,
    int? TeacherId,
    string TeacherName,
    decimal TeacherSharePercent,
    string RoomName,
    string DaysOfWeek,
    string StartTime,
    int LessonDurationMinutes,
    int LessonsPerMonth,
    int Capacity,
    decimal MonthlyFee,
    bool IsActive,
    DateOnly Month,
    IReadOnlyList<DateOnly> LessonDates,
    IReadOnlyList<GroupMemberRow> Members,
    decimal? MonthIncome,
    decimal? TeacherShareAmount);

public sealed record PaymentReceipt(
    int Id,
    string CenterName,
    string BranchName,
    string StudentName,
    string StudentPhone,
    string? GroupName,
    DateOnly Date,
    decimal Amount,
    PaymentMethod Method,
    string Note,
    string Cashier,
    decimal BalanceNow);
