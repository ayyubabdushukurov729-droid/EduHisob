using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EduHisob.Data;

// Platforma egasining kabineti: barcha markazlar, ularning hisobotlari, obuna to'lovlari,
// markaz qo'shish, bloklash va butunlay o'chirish. Har bir amal oldidan egalik bazadan tekshiriladi.
public sealed class PlatformAdminDataService(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    AuthenticationStateProvider authenticationStateProvider,
    UserManager<ApplicationUser> userManager)
{
    public const string Policy = "PlatformAdmin";
    public const string ClaimType = "platform_admin";

    // Sozlamadagi telefon va parol bilan platforma egasi akkauntini yaratadi (bor bo'lsa, faqat belgini tiklaydi).
    public static async Task EnsureAdminAccountAsync(UserManager<ApplicationUser> users, IConfiguration configuration, ILogger logger)
    {
        var phone = configuration["PlatformAdmin:Phone"];
        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        var login = TeamAccessService.PhoneLogin(phone);
        if (login is null)
        {
            logger.LogWarning("PlatformAdmin:Phone is not a valid phone number.");
            return;
        }

        // Platforma egasi bitta — sozlamadagi raqam. Raqam almashtirilsa, eski akkaunt huquqini yo'qotadi.
        var formerAdmins = await users.Users.Where(user => user.IsPlatformAdmin && user.UserName != login).ToListAsync();
        foreach (var former in formerAdmins)
        {
            former.IsPlatformAdmin = false;
            former.IsActive = false;
            await users.UpdateAsync(former);
            await users.UpdateSecurityStampAsync(former);
        }

        var password = configuration["PlatformAdmin:Password"];
        var account = await users.FindByNameAsync(login);
        if (account is null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("PlatformAdmin:Password is required to create the platform owner account.");
                return;
            }

            account = new ApplicationUser
            {
                UserName = login,
                PhoneNumber = "+" + login,
                PhoneNumberConfirmed = true,
                FullName = configuration["PlatformAdmin:Name"] is { Length: > 0 } name ? name : "Platforma egasi",
                IsPlatformAdmin = true,
                IsActive = true
            };
            var created = await users.CreateAsync(account, password);
            if (!created.Succeeded)
            {
                logger.LogError("Platform owner account was not created: {Errors}", string.Join("; ", created.Errors.Select(error => error.Description)));
            }

            return;
        }

        // Markaz xodimining akkauntini platforma egasiga aylantirib yubormaslik uchun.
        if (account.OrganizationId is not null)
        {
            logger.LogError("PlatformAdmin:Phone belongs to a center account; choose another phone number.");
            return;
        }

        if (!account.IsPlatformAdmin || !account.IsActive)
        {
            account.IsPlatformAdmin = true;
            account.IsActive = true;
            await users.UpdateAsync(account);
        }

        if (configuration.GetValue<bool>("PlatformAdmin:ResetPassword") && !string.IsNullOrWhiteSpace(password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(account);
            await users.ResetPasswordAsync(account, token, password);
        }
    }

    public async Task<bool> IsPlatformAdminAsync()
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return false;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Users.AnyAsync(account => account.Id == userId && account.IsPlatformAdmin && account.IsActive);
    }

    public async Task<PlatformOverview> GetOverviewAsync()
    {
        await EnsurePlatformAdminAsync();
        var centers = await LoadDirectoryAsync(null);
        var now = DateTime.UtcNow;
        var thisMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

        await using var db = await contextFactory.CreateDbContextAsync();
        var payments = await PaymentRows(db, db.PlatformPayments.AsNoTracking()).ToListAsync();
        var existing = centers.Select(center => center.Id).ToHashSet();
        payments = payments.Select(payment => payment with { CenterExists = existing.Contains(payment.CenterId) }).ToList();

        decimal Revenue(DateOnly month) => payments.Where(payment => MonthOf(payment.PaidAtUtc) == month).Sum(payment => payment.Amount);
        var months = Enumerable.Range(0, 6)
            .Select(offset => thisMonth.AddMonths(offset - 5))
            .Select(month => new PlatformMonthRevenue(
                month,
                Revenue(month),
                payments.Count(payment => MonthOf(payment.PaidAtUtc) == month),
                centers.Count(center => MonthOf(center.CreatedAtUtc) == month)))
            .ToList();

        var monthStart = thisMonth;
        var monthEnd = thisMonth.AddMonths(1).AddDays(-1);
        var centersIncome = await db.FinancialTransactions.AsNoTracking()
            .Where(item => item.Type == FinancialTransactionType.Income && item.TransactionDate >= monthStart && item.TransactionDate <= monthEnd)
            .SumAsync(item => (decimal?)item.Amount) ?? 0m;

        int Count(CenterStatus status) => centers.Count(center => center.Status == status);
        return new PlatformOverview(
            centers.Count,
            Count(CenterStatus.Paid),
            Count(CenterStatus.Trial),
            Count(CenterStatus.Expiring),
            Count(CenterStatus.Expired),
            Count(CenterStatus.Blocked),
            centers.Count(center => MonthOf(center.CreatedAtUtc) == thisMonth),
            Revenue(thisMonth),
            Revenue(thisMonth.AddMonths(-1)),
            centers.Where(center => center.Status is not (CenterStatus.Blocked or CenterStatus.Expired)).Sum(center => center.MonthlyFee),
            payments.Sum(payment => payment.Amount),
            centers.Sum(center => center.StudentCount),
            centers.Sum(center => center.ActiveStudentCount),
            centersIncome,
            months,
            centers
                .Where(center => center.Status is CenterStatus.Expiring or CenterStatus.Expired)
                .OrderBy(center => center.AccessEndsAtUtc)
                .Take(10)
                .ToList(),
            payments.Take(8).ToList());
    }

    public async Task<IReadOnlyList<CenterDirectoryEntry>> GetCentersAsync(string? search = null, CenterStatus? status = null)
    {
        await EnsurePlatformAdminAsync();
        var centers = await LoadDirectoryAsync(search);
        return status is null ? centers : centers.Where(center => center.Status == status).ToList();
    }

    public async Task<PlatformCenterDetail> GetCenterAsync(int centerId)
    {
        await EnsurePlatformAdminAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        var center = (await LoadDirectoryAsync(null, centerId)).SingleOrDefault()
            ?? throw new InvalidOperationException("Markaz topilmadi.");
        var extra = await db.Organizations.AsNoTracking()
            .Where(item => item.Id == centerId)
            .Select(item => new { item.BlockReason, item.PlatformNote })
            .SingleAsync();
        var members = await db.Users.AsNoTracking()
            .Where(account => account.OrganizationId == centerId)
            .OrderBy(account => account.CenterRole)
            .ThenBy(account => account.FullName)
            .Select(account => new PlatformCenterMember(account.Id, account.FullName, account.UserName ?? "", account.CenterRole, account.IsActive))
            .ToListAsync();
        var payments = await PaymentRows(db, db.PlatformPayments.AsNoTracking().Where(payment => payment.CenterId == centerId)).ToListAsync();
        return new PlatformCenterDetail(center, extra.BlockReason, extra.PlatformNote, members, payments);
    }

    // Yangi markaz: tashkilot, asoschi akkaunti (telefon — login) va asosiy filial bitta tranzaksiyada.
    public async Task<int> CreateCenterAsync(NewCenterInput input)
    {
        await EnsurePlatformAdminAsync();
        var name = string.Join(' ', input.CenterName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var ownerName = string.Join(' ', input.OwnerName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length < 2 || name.Length > 160)
        {
            throw new InvalidOperationException("Markaz nomini kiriting.");
        }
        if (ownerName.Length < 5 || !ownerName.Contains(' '))
        {
            throw new InvalidOperationException("Markaz rahbarining ism va familiyasini to‘liq yozing.");
        }
        var login = TeamAccessService.PhoneLogin(input.OwnerPhone)
            ?? throw new InvalidOperationException("Telefon raqamini to‘liq kiriting, masalan: +998 90 123 45 67.");
        if (input.TrialDays is < 0 or > 365)
        {
            throw new InvalidOperationException("Sinov muddati 0–365 kun oralig‘ida bo‘lsin.");
        }
        if (input.MonthlyFee < 0)
        {
            throw new InvalidOperationException("Oylik to‘lov manfiy bo‘lmasin.");
        }
        if (await userManager.FindByNameAsync(login) is not null)
        {
            throw new InvalidOperationException("Bu telefon raqami bilan akkaunt allaqachon mavjud.");
        }

        // Akkaunt markaz bilan bitta tranzaksiyada saqlanishi uchun to'g'ridan-to'g'ri bazaga yoziladi.
        var owner = new ApplicationUser
        {
            UserName = login,
            NormalizedUserName = userManager.NormalizeName(login),
            PhoneNumber = "+" + login,
            PhoneNumberConfirmed = true,
            FullName = ownerName,
            CenterRole = CenterRole.Owner,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        var passwordErrors = await ValidatePasswordAsync(owner, input.Password);
        if (passwordErrors is not null)
        {
            throw new InvalidOperationException(passwordErrors);
        }
        owner.PasswordHash = userManager.PasswordHasher.HashPassword(owner, input.Password);

        await using var db = await contextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var center = new Organization
        {
            Name = name,
            OwnerPhone = "+" + login,
            TrialEndsAtUtc = DateTime.UtcNow.AddDays(input.TrialDays),
            MonthlyFee = input.MonthlyFee
        };
        db.Organizations.Add(center);
        await db.SaveChangesAsync();
        db.Branches.Add(new Branch { OrganizationId = center.Id, Name = "Asosiy filial" });
        owner.OrganizationId = center.Id;
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return center.Id;
    }

    public async Task UpdateCenterAsync(int centerId, string name, decimal monthlyFee, string note)
    {
        await EnsurePlatformAdminAsync();
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length < 2 || name.Length > 160)
        {
            throw new InvalidOperationException("Markaz nomini kiriting.");
        }
        if (monthlyFee < 0)
        {
            throw new InvalidOperationException("Oylik to‘lov manfiy bo‘lmasin.");
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var center = await FindCenterAsync(db, centerId);
        center.Name = name;
        center.MonthlyFee = monthlyFee;
        center.PlatformNote = Clip(note, 1000);
        await db.SaveChangesAsync();
    }

    // Sinov (bepul) muddatini bugundan yoki joriy tugash sanasidan boshlab uzaytiradi.
    public async Task ExtendTrialAsync(int centerId, int days)
    {
        await EnsurePlatformAdminAsync();
        if (days is < 1 or > 365)
        {
            throw new InvalidOperationException("Uzaytirish 1–365 kun oralig‘ida bo‘lsin.");
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var center = await FindCenterAsync(db, centerId);
        var from = center.TrialEndsAtUtc > DateTime.UtcNow ? center.TrialEndsAtUtc : DateTime.UtcNow;
        center.TrialEndsAtUtc = from.AddDays(days);
        await db.SaveChangesAsync();
    }

    // Obuna to'lovi: kirish muddati tugagan joyidan (yoki bugundan) boshlab necha oyga uzayadi.
    public async Task<PlatformPaymentRow> RecordPaymentAsync(int centerId, decimal amount, int months, PaymentMethod method, string? note)
    {
        await EnsurePlatformAdminAsync();
        if (amount <= 0 || amount > 1_000_000_000)
        {
            throw new InvalidOperationException("To‘lov summasini to‘g‘ri kiriting.");
        }
        if (months is < 1 or > 36)
        {
            throw new InvalidOperationException("Necha oy uchun to‘lanayotganini kiriting (1–36).");
        }
        if (!Enum.IsDefined(method))
        {
            throw new InvalidOperationException("To‘lov usulini tanlang.");
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var center = await FindCenterAsync(db, centerId);
        var now = DateTime.UtcNow;
        var accessEnds = center.AccessEndsAtUtc;
        var start = accessEnds > now ? accessEnds : now;
        var payment = new PlatformPayment
        {
            CenterId = center.Id,
            CenterName = center.Name,
            Amount = amount,
            Months = months,
            PeriodStartUtc = start,
            PeriodEndUtc = start.AddMonths(months),
            Method = method,
            Note = Clip(note, 300),
            PaidAtUtc = now
        };
        center.PaidUntilUtc = payment.PeriodEndUtc;
        db.PlatformPayments.Add(payment);
        await db.SaveChangesAsync();
        return new PlatformPaymentRow(payment.Id, center.Id, center.Name, amount, months, payment.PeriodStartUtc, payment.PeriodEndUtc, method, payment.Note, now, true);
    }

    // Faqat markazning oxirgi to'lovini bekor qilish mumkin: muddat shu to'lovdan oldingi holatiga qaytadi.
    public async Task CancelLastPaymentAsync(int centerId, int paymentId)
    {
        await EnsurePlatformAdminAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        var center = await FindCenterAsync(db, centerId);
        var last = await db.PlatformPayments
            .Where(payment => payment.CenterId == centerId)
            .OrderByDescending(payment => payment.Id)
            .FirstOrDefaultAsync();
        if (last is null || last.Id != paymentId)
        {
            throw new InvalidOperationException("Faqat oxirgi to‘lovni bekor qilish mumkin.");
        }

        var previous = await db.PlatformPayments
            .Where(payment => payment.CenterId == centerId && payment.Id != last.Id)
            .OrderByDescending(payment => payment.Id)
            .Select(payment => (DateTime?)payment.PeriodEndUtc)
            .FirstOrDefaultAsync();
        center.PaidUntilUtc = previous;
        db.PlatformPayments.Remove(last);
        await db.SaveChangesAsync();
    }

    public async Task SetBlockedAsync(int centerId, bool blocked, string? reason)
    {
        await EnsurePlatformAdminAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        var center = await FindCenterAsync(db, centerId);
        center.IsBlocked = blocked;

        center.BlockReason = blocked ? Clip(reason, 300) : "";
        await db.SaveChangesAsync();
    }

    public async Task ResetOwnerPasswordAsync(int centerId, string newPassword)
    {
        await EnsurePlatformAdminAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        await FindCenterAsync(db, centerId);
        var ownerId = await db.Users
            .Where(account => account.OrganizationId == centerId && account.CenterRole == CenterRole.Owner)
            .OrderBy(account => account.Id)
            .Select(account => account.Id)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Markaz asoschisining akkaunti topilmadi.");
        var owner = await userManager.FindByIdAsync(ownerId)
            ?? throw new InvalidOperationException("Markaz asoschisining akkaunti topilmadi.");
        var token = await userManager.GeneratePasswordResetTokenAsync(owner);
        var result = await userManager.ResetPasswordAsync(owner, token, newPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(TeamAccessService.TranslateError)));
        }
        await userManager.SetLockoutEndDateAsync(owner, null);
    }

    // Markazni barcha ma'lumotlari va akkauntlari bilan butunlay o'chiradi. Qaytarib bo'lmaydi,
    // shuning uchun markaz nomini aynan yozib tasdiqlash kerak. Obuna to'lovlari tarixi saqlanib qoladi.
    public async Task DeleteCenterAsync(int centerId, string confirmName)
    {
        await EnsurePlatformAdminAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        var center = await FindCenterAsync(db, centerId);
        if (!string.Equals(confirmName.Trim(), center.Name.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Tasdiqlash uchun markaz nomini aynan yozing.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var (table, column) in CenterTablesInDeleteOrder(db))
        {
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{table}\" WHERE \"{column}\" = {{0}}", centerId);
        }
        await db.Users.Where(account => account.OrganizationId == centerId).ExecuteDeleteAsync();
        await db.Organizations.Where(item => item.Id == centerId).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<PlatformPaymentRow>> GetPaymentsAsync(DateOnly from, DateOnly to)
    {
        await EnsurePlatformAdminAsync();
        if (from > to)
        {
            throw new InvalidOperationException("Boshlanish sanasi tugash sanasidan keyin bo‘lmasin.");
        }

        var start = from.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        await using var db = await contextFactory.CreateDbContextAsync();
        var rows = await PaymentRows(db, db.PlatformPayments.AsNoTracking().Where(payment => payment.PaidAtUtc >= start && payment.PaidAtUtc < end)).ToListAsync();
        var existing = (await db.Organizations.Select(item => item.Id).ToListAsync()).ToHashSet();
        return rows.Select(row => row with { CenterExists = existing.Contains(row.CenterId) }).ToList();
    }

    public async Task<IReadOnlyList<PlatformBranchOption>> GetCenterBranchesAsync(int centerId)
    {
        await EnsurePlatformAdminAsync();
        await using var db = await contextFactory.CreateDbContextAsync();
        if (!await db.Organizations.AnyAsync(center => center.Id == centerId))
        {
            throw new InvalidOperationException("Tanlangan markaz topilmadi.");
        }

        return await db.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == centerId && branch.IsActive)
            .OrderBy(branch => branch.Name)
            .Select(branch => new PlatformBranchOption(branch.Id, branch.Name))
            .ToListAsync();
    }

    public async Task<PlatformCenterReport> GetCenterReportAsync(
        int centerId,
        DateOnly startDate,
        DateOnly endDate,
        int? branchId = null)
    {
        await EnsurePlatformAdminAsync();
        if (startDate > endDate || endDate.DayNumber - startDate.DayNumber > 730)
        {
            throw new InvalidOperationException("Hisobot davri 1 kundan 2 yilgacha bo‘lishi kerak.");
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var center = await db.Organizations.AsNoTracking()
            .Where(item => item.Id == centerId)
            .Select(item => new { item.Name, item.OwnerPhone, item.CreatedAtUtc, item.TrialEndsAtUtc })
            .SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("Tanlangan markaz topilmadi.");

        if (branchId.HasValue && !await db.Branches.AnyAsync(branch =>
                branch.OrganizationId == centerId && branch.Id == branchId.Value && branch.IsActive))
        {
            throw new InvalidOperationException("Tanlangan filial ushbu markazga tegishli emas.");
        }

        var transactions = db.FinancialTransactions.AsNoTracking()
            .Where(item =>
                item.OrganizationId == centerId &&
                item.TransactionDate >= startDate &&
                item.TransactionDate <= endDate);
        if (branchId.HasValue)
        {
            transactions = transactions.Where(item => item.BranchId == branchId.Value);
        }

        var totals = await transactions
            .GroupBy(item => item.Type)
            .Select(group => new { Type = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToListAsync();
        decimal Total(FinancialTransactionType type) =>
            totals.SingleOrDefault(item => item.Type == type)?.Amount ?? 0m;

        var dailyTotalRows = await transactions
            .GroupBy(item => item.TransactionDate)
            .Select(group => new
            {
                Date = group.Key,
                Income = group.Where(item => item.Type == FinancialTransactionType.Income).Sum(item => (decimal?)item.Amount) ?? 0m,
                Expense = group.Where(item => item.Type == FinancialTransactionType.Expense).Sum(item => (decimal?)item.Amount) ?? 0m
            })
            .OrderBy(item => item.Date)
            .ToListAsync();
        var dailyTotals = dailyTotalRows
            .Select(item => new PlatformDailyTotal(item.Date, item.Income, item.Expense))
            .ToList();

        var categoryTotalRows = await transactions
            .GroupBy(item => new { item.Type, item.Category })
            .Select(group => new
            {
                group.Key.Type,
                group.Key.Category,
                Count = group.Count(),
                Amount = group.Sum(item => item.Amount)
            })
            .OrderBy(item => item.Type)
            .ThenBy(item => item.Category)
            .ToListAsync();
        var categoryTotals = categoryTotalRows
            .Select(item => new PlatformCategoryTotal(item.Type, item.Category, item.Count, item.Amount))
            .ToList();

        var paymentTotalRows = await transactions
            .GroupBy(item => new { item.Type, item.PaymentMethod })
            .Select(group => new
            {
                group.Key.Type,
                Method = group.Key.PaymentMethod,
                Count = group.Count(),
                Amount = group.Sum(item => item.Amount)
            })
            .OrderBy(item => item.Type)
            .ThenBy(item => item.Method)
            .ToListAsync();
        var paymentTotals = paymentTotalRows
            .Select(item => new PlatformPaymentTotal(item.Type, item.Method, item.Count, item.Amount))
            .ToList();

        var students = db.Students.AsNoTracking()
            .Where(item => item.OrganizationId == centerId);
        if (branchId.HasValue)
        {
            students = students.Where(item => item.BranchId == branchId.Value);
        }

        var studentCounts = await students
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count);
        int StudentCount(StudentStatus status) =>
            studentCounts.TryGetValue(status, out var count) ? count : 0;

        var groups = db.StudyGroups.AsNoTracking()
            .Where(item => item.OrganizationId == centerId && item.IsActive);
        if (branchId.HasValue)
        {
            groups = groups.Where(item => item.BranchId == branchId.Value);
        }

        var groupCount = await groups.CountAsync();
        var branchCount = branchId.HasValue
            ? 1
            : await db.Branches.CountAsync(item => item.OrganizationId == centerId && item.IsActive);
        var attendance = db.AttendanceRecords.AsNoTracking()
            .Where(item =>
                item.OrganizationId == centerId &&
                item.LessonDate >= startDate &&
                item.LessonDate <= endDate);
        if (branchId.HasValue)
        {
            attendance = attendance.Where(item =>
                db.StudyGroups.Any(group =>
                    group.OrganizationId == centerId &&
                    group.Id == item.StudyGroupId &&
                    group.BranchId == branchId.Value));
        }

        var attendanceCounts = await attendance
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count);
        int AttendanceCount(AttendanceStatus status) =>
            attendanceCounts.TryGetValue(status, out var count) ? count : 0;
        var attended = AttendanceCount(AttendanceStatus.Present) + AttendanceCount(AttendanceStatus.Late);
        var attendanceTotal = attended + AttendanceCount(AttendanceStatus.Absent);

        return new PlatformCenterReport(
            center.Name,
            center.OwnerPhone,
            center.CreatedAtUtc,
            center.TrialEndsAtUtc,
            branchCount,
            studentCounts.Values.Sum(),
            StudentCount(StudentStatus.Active),
            StudentCount(StudentStatus.Trial),
            StudentCount(StudentStatus.Frozen),
            StudentCount(StudentStatus.Left),
            groupCount,
            attendanceTotal,
            attended,
            Total(FinancialTransactionType.Income),
            Total(FinancialTransactionType.Expense),
            dailyTotals,
            categoryTotals,
            paymentTotals);
    }


    private async Task<List<CenterDirectoryEntry>> LoadDirectoryAsync(string? search, int? onlyCenterId = null)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var query = db.Organizations.AsNoTracking();
        if (onlyCenterId is int id)
        {
            query = query.Where(center => center.Id == id);
        }

        var rows = await query
            .Select(center => new
            {
                center.Id,
                center.Name,
                center.OwnerPhone,
                center.CreatedAtUtc,
                center.TrialEndsAtUtc,
                center.PaidUntilUtc,
                center.MonthlyFee,
                center.IsBlocked,
                BranchCount = center.Branches.Count(branch => branch.IsActive),
                StudentCount = db.Students.Count(student => student.OrganizationId == center.Id),
                ActiveStudentCount = db.Students.Count(student => student.OrganizationId == center.Id && student.Status == StudentStatus.Active),
                ActiveGroupCount = db.StudyGroups.Count(group => group.OrganizationId == center.Id && group.IsActive),
                UserCount = db.Users.Count(account => account.OrganizationId == center.Id),
                OwnerName = db.Users
                    .Where(account => account.OrganizationId == center.Id && account.CenterRole == CenterRole.Owner)
                    .OrderBy(account => account.Id)
                    .Select(account => account.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync();
        // To'lovlar soni kam: markazlar bo'yicha xotirada jamlanadi.
        var paidTotals = (await db.PlatformPayments.AsNoTracking()
                .Select(payment => new { payment.CenterId, payment.Amount })
                .ToListAsync())
            .GroupBy(payment => payment.CenterId)
            .ToDictionary(group => group.Key, group => group.Sum(payment => payment.Amount));

        var now = DateTime.UtcNow;
        var centers = rows
            .Select(row => new CenterDirectoryEntry(
                row.Id,
                row.Name,
                row.OwnerName ?? "",
                row.OwnerPhone,
                row.CreatedAtUtc,
                row.TrialEndsAtUtc,
                row.PaidUntilUtc,
                row.MonthlyFee,
                row.IsBlocked,
                CenterStatuses.Of(row.IsBlocked, row.TrialEndsAtUtc, row.PaidUntilUtc, now),
                row.BranchCount,
                row.StudentCount,
                row.ActiveStudentCount,
                row.ActiveGroupCount,
                row.UserCount,
                paidTotals.TryGetValue(row.Id, out var paid) ? paid : 0m))
            .OrderByDescending(center => center.CreatedAtUtc);

        if (string.IsNullOrWhiteSpace(search))
        {
            return centers.ToList();
        }

        var term = search.Trim();
        var digits = StudentAccountService.DigitsOnly(term);
        return centers
            .Where(center =>
                center.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                center.OwnerName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                (digits.Length >= 3 && StudentAccountService.DigitsOnly(center.OwnerPhone).Contains(digits)))
            .ToList();
    }

    private static IQueryable<PlatformPaymentRow> PaymentRows(ApplicationDbContext db, IQueryable<PlatformPayment> payments) => payments
        .OrderByDescending(payment => payment.PaidAtUtc)
        .ThenByDescending(payment => payment.Id)
        .Select(payment => new PlatformPaymentRow(
            payment.Id,
            payment.CenterId,
            payment.CenterName,
            payment.Amount,
            payment.Months,
            payment.PeriodStartUtc,
            payment.PeriodEndUtc,
            payment.Method,
            payment.Note,
            payment.PaidAtUtc,
            true));

    private static async Task<Organization> FindCenterAsync(ApplicationDbContext db, int centerId) =>
        await db.Organizations.SingleOrDefaultAsync(center => center.Id == centerId)
        ?? throw new InvalidOperationException("Markaz topilmadi.");

    // OrganizationId ustuni bor barcha jadvallar: avval bog'liq (bola) jadvallar, keyin ular tayangan jadvallar.
    // Yangi jadval qo'shilsa ham ro'yxat modeldan avtomatik olinadi.
    public static IReadOnlyList<(string Table, string Column)> CenterTablesInDeleteOrder(ApplicationDbContext db)
    {
        var remaining = db.Model.GetEntityTypes()
            .Where(type => type.ClrType != typeof(Organization) && type.ClrType != typeof(ApplicationUser) && !type.IsOwned())
            .Where(type => type.FindProperty(nameof(Branch.OrganizationId)) is not null && type.GetTableName() is not null)
            .ToList();
        var ordered = new List<(string, string)>();
        while (remaining.Count > 0)
        {
            var leaf = remaining.FirstOrDefault(principal => !remaining.Any(dependent =>
                    dependent != principal &&
                    dependent.GetForeignKeys().Any(key => key.PrincipalEntityType == principal)))
                ?? throw new InvalidOperationException("Markaz jadvallari o‘rtasida aylanma bog‘lanish bor.");
            var column = leaf.FindProperty(nameof(Branch.OrganizationId))!
                .GetColumnName(StoreObjectIdentifier.Table(leaf.GetTableName()!, leaf.GetSchema()))!;
            ordered.Add((leaf.GetTableName()!, column));
            remaining.Remove(leaf);
        }

        return ordered;
    }

    private async Task<string?> ValidatePasswordAsync(ApplicationUser user, string password)
    {
        var errors = new List<string>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, password ?? "");
            errors.AddRange(result.Errors.Select(TeamAccessService.TranslateError));
        }

        return errors.Count == 0 ? null : string.Join(" ", errors.Distinct());
    }

    private static DateOnly MonthOf(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return new DateOnly(local.Year, local.Month, 1);
    }

    private static string Clip(string? text, int length)
    {
        var value = (text ?? "").Trim();
        return value.Length > length ? value[..length] : value;
    }

    private async Task EnsurePlatformAdminAsync()
    {
        if (!await IsPlatformAdminAsync())
        {
            throw new UnauthorizedAccessException("Platforma kabinetiga ruxsatingiz yo‘q.");
        }
    }
}
