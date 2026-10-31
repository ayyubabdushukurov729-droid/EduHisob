using Microsoft.EntityFrameworkCore;

namespace EduHisob.Data;

// Lidlar (potensial o'quvchilar) bilan ishlash. Manba va yo'qotish sababi majburiy —
// aks holda sotuv hisoboti ma'nosiz bo'lib qoladi.
public sealed class SalesService(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    CenterAccess centerAccess)
{
    public async Task<IReadOnlyList<LeadRow>> GetLeadsAsync(string? search = null, int? branchId = null, LeadSource? source = null, bool includeClosed = true)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Sales);
        await using var db = await contextFactory.CreateDbContextAsync();
        var query = db.Leads.AsNoTracking().Where(lead => lead.OrganizationId == user.OrganizationId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(lead => lead.FullName.Contains(term) || lead.Phone.Contains(term) || lead.CourseInterest.Contains(term));
        }
        if (branchId.HasValue)
        {
            query = query.Where(lead => lead.BranchId == branchId.Value);
        }
        if (source.HasValue)
        {
            query = query.Where(lead => lead.Source == source.Value);
        }
        if (!includeClosed)
        {
            query = query.Where(lead => lead.Stage != LeadStage.Converted && lead.Stage != LeadStage.Lost);
        }

        var names = await UserNamesAsync(db, user.OrganizationId);
        var branches = await db.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == user.OrganizationId)
            .ToDictionaryAsync(branch => branch.Id, branch => branch.Name);
        return (await query.OrderByDescending(lead => lead.UpdatedAtUtc).Take(1000).ToListAsync())
            .Select(lead => new LeadRow(
                lead.Id,
                lead.FullName,
                lead.Phone,
                lead.Source,
                lead.CourseInterest,
                lead.Stage,
                lead.AssignedUserId,
                lead.AssignedUserId is { } assigned ? names.GetValueOrDefault(assigned, "") : "",
                lead.LostReason,
                lead.Note,
                lead.BranchId,
                branches.GetValueOrDefault(lead.BranchId, ""),
                lead.ConvertedStudentId,
                lead.CreatedAtUtc,
                lead.UpdatedAtUtc))
            .ToList();
    }

    public async Task<SalesFunnel> GetFunnelAsync(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new InvalidOperationException("Sana oralig‘ini tekshiring.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.Sales);
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await using var db = await contextFactory.CreateDbContextAsync();
        var leads = await db.Leads.AsNoTracking()
            .Where(lead => lead.OrganizationId == user.OrganizationId && lead.CreatedAtUtc >= fromUtc && lead.CreatedAtUtc < toUtc)
            .Select(lead => new { lead.Stage, lead.Source, lead.LostReason, lead.CreatedAtUtc, lead.ClosedAtUtc })
            .ToListAsync();

        var converted = leads.Where(lead => lead.Stage == LeadStage.Converted).ToList();
        var averageDays = converted.Count == 0 || converted.All(lead => lead.ClosedAtUtc is null)
            ? (double?)null
            : converted.Where(lead => lead.ClosedAtUtc is not null).Average(lead => (lead.ClosedAtUtc!.Value - lead.CreatedAtUtc).TotalDays);

        return new SalesFunnel(
            leads.Count,
            Enum.GetValues<LeadStage>().ToDictionary(stage => stage, stage => leads.Count(lead => lead.Stage == stage)),
            leads.GroupBy(lead => lead.Source)
                .Select(group => new SourceStat(group.Key, group.Count(), group.Count(lead => lead.Stage == LeadStage.Converted)))
                .OrderByDescending(stat => stat.Leads)
                .ToList(),
            leads.Where(lead => lead.Stage == LeadStage.Lost)
                .GroupBy(lead => lead.LostReason)
                .Select(group => new ReasonStat(group.Key, group.Count()))
                .OrderByDescending(stat => stat.Count)
                .ToList(),
            averageDays);
    }

    public async Task<int> AddLeadAsync(LeadInput input, bool allowDuplicate)
    {
        var fullName = input.FullName.Trim();
        var phone = input.Phone.Trim();
        var digits = StudentAccountService.DigitsOnly(phone);
        if (fullName.Length is < 2 or > 160 || digits.Length is < 7 or > 15 || phone.Length > 32)
        {
            throw new InvalidOperationException("Ism (2–160 belgi) va telefon raqamini tekshiring.");
        }
        if (input.Source is null || !Enum.IsDefined(input.Source.Value))
        {
            throw new InvalidOperationException("Lid manbasini tanlang — u sotuv hisobotida kerak.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.Sales);
        await using var db = await contextFactory.CreateDbContextAsync();
        if (!await db.Branches.AnyAsync(branch => branch.OrganizationId == user.OrganizationId && branch.Id == input.BranchId && branch.IsActive))
        {
            throw new InvalidOperationException("Filialni tanlang.");
        }
        if (input.AssignedUserId is { } assigned &&
            !await db.Users.AnyAsync(account => account.Id == assigned && account.OrganizationId == user.OrganizationId && account.IsActive))
        {
            throw new InvalidOperationException("Mas’ul xodim topilmadi.");
        }

        if (!allowDuplicate)
        {
            var key = digits.Length >= 9 ? digits[^9..] : digits;
            var leadPhones = await db.Leads.AsNoTracking()
                .Where(lead => lead.OrganizationId == user.OrganizationId && lead.Stage != LeadStage.Lost)
                .Select(lead => lead.Phone).ToListAsync();
            var studentPhones = await db.Students.AsNoTracking()
                .Where(student => student.OrganizationId == user.OrganizationId)
                .Select(student => student.Phone).ToListAsync();
            if (leadPhones.Any(value => StudentAccountService.DigitsOnly(value).EndsWith(key, StringComparison.Ordinal)))
            {
                throw new DuplicateLeadException("Bu raqam bilan ochiq lid allaqachon bor.");
            }
            if (studentPhones.Any(value => StudentAccountService.DigitsOnly(value).EndsWith(key, StringComparison.Ordinal)))
            {
                throw new DuplicateLeadException("Bu raqam bilan o‘quvchi allaqachon ro‘yxatda bor.");
            }
        }

        var lead = new Lead
        {
            OrganizationId = user.OrganizationId,
            BranchId = input.BranchId,
            FullName = fullName,
            Phone = phone,
            Source = input.Source.Value,
            CourseInterest = input.CourseInterest.Trim(),
            Note = input.Note.Trim(),
            AssignedUserId = input.AssignedUserId ?? user.UserId,
            Stage = LeadStage.New
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id;
    }

    public async Task MoveAsync(int leadId, LeadStage stage, string lostReason)
    {
        lostReason = lostReason.Trim();
        if (!Enum.IsDefined(stage) || stage == LeadStage.Converted)
        {
            throw new InvalidOperationException("Lidni o‘quvchiga aylantirish uchun “O‘quvchi qilish” tugmasidan foydalaning.");
        }
        if (stage == LeadStage.Lost && lostReason.Length is < 3 or > 300)
        {
            throw new InvalidOperationException("Yo‘qotish sababini yozing (3–300 belgi).");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.Sales);
        await using var db = await contextFactory.CreateDbContextAsync();
        var lead = await FindAsync(db, user.OrganizationId, leadId);
        if (lead.Stage == LeadStage.Converted)
        {
            throw new InvalidOperationException("O‘quvchiga aylangan lidni qaytarib bo‘lmaydi.");
        }

        lead.Stage = stage;
        lead.LostReason = stage == LeadStage.Lost ? lostReason : "";
        lead.ClosedAtUtc = stage == LeadStage.Lost ? DateTime.UtcNow : null;
        lead.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task AssignAsync(int leadId, string? userId)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Sales);
        await using var db = await contextFactory.CreateDbContextAsync();
        var lead = await FindAsync(db, user.OrganizationId, leadId);
        if (userId is not null &&
            !await db.Users.AnyAsync(account => account.Id == userId && account.OrganizationId == user.OrganizationId && account.IsActive))
        {
            throw new InvalidOperationException("Mas’ul xodim topilmadi.");
        }

        lead.AssignedUserId = userId;
        lead.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task UpdateNoteAsync(int leadId, string note)
    {
        note = note.Trim();
        if (note.Length > 1000)
        {
            throw new InvalidOperationException("Izoh 1000 belgidan oshmasin.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.Sales);
        await using var db = await contextFactory.CreateDbContextAsync();
        var lead = await FindAsync(db, user.OrganizationId, leadId);
        lead.Note = note;
        lead.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    // Lid sinov holatidagi o'quvchiga aylanadi; guruhga qo'shish va to'lov o'quvchi profilida qilinadi.
    public async Task<int> ConvertToStudentAsync(int leadId)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Sales);
        await using var db = await contextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var lead = await FindAsync(db, user.OrganizationId, leadId);
        if (lead.Stage == LeadStage.Converted && lead.ConvertedStudentId is { } existingId)
        {
            return existingId;
        }
        if (lead.Stage == LeadStage.Lost)
        {
            throw new InvalidOperationException("Yo‘qotilgan lidni avval qayta oching.");
        }

        var student = new Student
        {
            OrganizationId = user.OrganizationId,
            BranchId = lead.BranchId,
            FullName = lead.FullName,
            Phone = lead.Phone,
            Status = StudentStatus.Trial,
            JoinedOn = DateOnly.FromDateTime(DateTime.Today),
            Notes = string.IsNullOrWhiteSpace(lead.CourseInterest) ? lead.Note : $"Qiziqqan kurs: {lead.CourseInterest}. {lead.Note}".Trim()
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        lead.Stage = LeadStage.Converted;
        lead.ConvertedStudentId = student.Id;
        lead.ClosedAtUtc = DateTime.UtcNow;
        lead.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return student.Id;
    }

    public async Task<IReadOnlyList<CashRecipient>> GetAssignableUsersAsync()
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Sales);
        await using var db = await contextFactory.CreateDbContextAsync();
        return (await db.Users.AsNoTracking()
                .Where(account => account.OrganizationId == user.OrganizationId && account.IsActive)
                .Select(account => new { account.Id, account.FullName, account.Email, account.CenterRole })
                .ToListAsync())
            .Where(account => CenterAccess.Sales.Contains(account.CenterRole))
            .Select(account => new CashRecipient(account.Id, string.IsNullOrWhiteSpace(account.FullName) ? account.Email ?? "" : account.FullName, account.CenterRole))
            .OrderBy(account => account.Name)
            .ToList();
    }

    private static async Task<Lead> FindAsync(ApplicationDbContext db, int organizationId, int leadId) =>
        await db.Leads.SingleOrDefaultAsync(lead => lead.OrganizationId == organizationId && lead.Id == leadId)
        ?? throw new InvalidOperationException("Lid topilmadi.");

    private static async Task<Dictionary<string, string>> UserNamesAsync(ApplicationDbContext db, int organizationId) =>
        await db.Users.AsNoTracking()
            .Where(account => account.OrganizationId == organizationId)
            .ToDictionaryAsync(account => account.Id, account => account.FullName != "" ? account.FullName : account.Email ?? "");
}

public sealed class DuplicateLeadException(string message) : InvalidOperationException(message);

public sealed class LeadInput
{
    public int BranchId { get; set; }
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public LeadSource? Source { get; set; }
    public string CourseInterest { get; set; } = "";
    public string Note { get; set; } = "";
    public string? AssignedUserId { get; set; }
}

public sealed record LeadRow(
    int Id,
    string FullName,
    string Phone,
    LeadSource Source,
    string CourseInterest,
    LeadStage Stage,
    string? AssignedUserId,
    string AssignedName,
    string LostReason,
    string Note,
    int BranchId,
    string BranchName,
    int? ConvertedStudentId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record SourceStat(LeadSource Source, int Leads, int Converted);
public sealed record ReasonStat(string Reason, int Count);

public sealed record SalesFunnel(
    int TotalLeads,
    IReadOnlyDictionary<LeadStage, int> ByStage,
    IReadOnlyList<SourceStat> BySource,
    IReadOnlyList<ReasonStat> LostReasons,
    double? AverageDaysToConvert);
