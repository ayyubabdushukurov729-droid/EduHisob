using Microsoft.EntityFrameworkCore;

namespace EduHisob.Data;

// "Mendagi pul": har bir xodim qo'lidagi naqd pul va xodimlar o'rtasida pul topshirish.
// Naqd pul = o'zi qabul qilgan naqd tushum − o'zi bergan naqd xarajat + qabul qilingan topshiriqlar − topshirilganlar.
public sealed class CashDeskService(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    CenterAccess centerAccess)
{
    public async Task<CashDeskOverview> GetOverviewAsync()
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Cash);
        var canSeeAll = CenterAccess.Finance.Contains(user.Role);
        await using var db = await contextFactory.CreateDbContextAsync();

        var people = await db.Users.AsNoTracking()
            .Where(account => account.OrganizationId == user.OrganizationId)
            .Select(account => new { account.Id, account.FullName, account.Email, account.CenterRole, account.IsActive })
            .ToListAsync();
        string NameOf(string id)
        {
            var person = people.FirstOrDefault(item => item.Id == id);
            return person is null ? "Noma’lum" : string.IsNullOrWhiteSpace(person.FullName) ? person.Email ?? "" : person.FullName;
        }

        var cashRows = await db.FinancialTransactions.AsNoTracking()
            .Where(item =>
                item.OrganizationId == user.OrganizationId &&
                item.PaymentMethod == PaymentMethod.Cash &&
                item.RecordedByUserId != null)
            .Select(item => new { item.RecordedByUserId, item.Type, item.Amount })
            .ToListAsync();
        var transfers = await db.CashTransfers.AsNoTracking()
            .Where(item => item.OrganizationId == user.OrganizationId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync();

        CashHolder Holder(string userId, string name, CenterRole role)
        {
            var received = cashRows.Where(row => row.RecordedByUserId == userId && row.Type == FinancialTransactionType.Income).Sum(row => row.Amount);
            var spent = cashRows.Where(row => row.RecordedByUserId == userId && row.Type == FinancialTransactionType.Expense).Sum(row => row.Amount);
            var incoming = transfers.Where(item => item.ToUserId == userId && item.Status == CashTransferStatus.Accepted).Sum(item => item.Amount);
            var outgoing = transfers.Where(item => item.FromUserId == userId && item.Status == CashTransferStatus.Accepted).Sum(item => item.Amount);
            var pendingOut = transfers.Where(item => item.FromUserId == userId && item.Status == CashTransferStatus.Pending).Sum(item => item.Amount);
            var balance = received - spent + incoming - outgoing;
            return new CashHolder(userId, name, role, received, spent, incoming, outgoing, balance, balance - pendingOut);
        }

        var me = Holder(user.UserId, user.DisplayName, user.Role);
        var holders = canSeeAll
            ? people.Where(person => person.IsActive || cashRows.Any(row => row.RecordedByUserId == person.Id))
                .Select(person => Holder(person.Id, NameOf(person.Id), person.CenterRole))
                .Where(holder => holder.UserId == user.UserId || holder.Balance != 0 || holder.Received != 0 || holder.Incoming != 0)
                .OrderByDescending(holder => holder.Balance)
                .ToList()
            : [me];

        var visibleTransfers = transfers
            .Where(item => canSeeAll || item.FromUserId == user.UserId || item.ToUserId == user.UserId)
            .Take(200)
            .Select(item => new CashTransferRow(
                item.Id,
                item.FromUserId,
                NameOf(item.FromUserId),
                item.ToUserId,
                NameOf(item.ToUserId),
                item.Amount,
                item.Note,
                item.Status,
                item.CreatedAtUtc,
                item.RespondedAtUtc))
            .ToList();

        // Filial kassasi: barcha naqd tushum − naqd xarajat. Manfiy bo'lsa, kiritilmagan tushum yoki xato yozuv bor.
        var branchCash = await (
            from transaction in db.FinancialTransactions.AsNoTracking()
            join branch in db.Branches.AsNoTracking()
                on new { transaction.OrganizationId, transaction.BranchId } equals new { branch.OrganizationId, BranchId = branch.Id }
            where transaction.OrganizationId == user.OrganizationId && transaction.PaymentMethod == PaymentMethod.Cash
            select new { branch.Id, branch.Name, transaction.Type, transaction.Amount })
            .ToListAsync();
        var branches = canSeeAll
            ? branchCash.GroupBy(row => new { row.Id, row.Name })
                .Select(group => new BranchCashRow(
                    group.Key.Id,
                    group.Key.Name,
                    group.Where(row => row.Type == FinancialTransactionType.Income).Sum(row => row.Amount),
                    group.Where(row => row.Type == FinancialTransactionType.Expense).Sum(row => row.Amount)))
                .OrderBy(row => row.BranchName)
                .ToList()
            : [];

        var recipients = people
            .Where(person => person.IsActive && person.Id != user.UserId && CenterAccess.Cash.Contains(person.CenterRole))
            .Select(person => new CashRecipient(person.Id, NameOf(person.Id), person.CenterRole))
            .OrderBy(person => person.Name)
            .ToList();

        return new CashDeskOverview(me, holders, visibleTransfers, branches, recipients, canSeeAll);
    }

    public async Task SendAsync(string toUserId, decimal amount, string note)
    {
        note = note.Trim();
        if (amount is <= 0 or > 999999999 || note.Length > 300)
        {
            throw new InvalidOperationException("Summa va izohni tekshiring.");
        }

        var user = await centerAccess.RequireAsync(CenterAccess.Cash);
        if (toUserId == user.UserId)
        {
            throw new InvalidOperationException("O‘zingizga pul topshira olmaysiz.");
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var recipient = await db.Users.AsNoTracking()
            .Where(account => account.Id == toUserId && account.OrganizationId == user.OrganizationId && account.IsActive)
            .Select(account => new { account.CenterRole })
            .SingleOrDefaultAsync();
        if (recipient is null || !CenterAccess.Cash.Contains(recipient.CenterRole))
        {
            throw new InvalidOperationException("Qabul qiluvchi topilmadi yoki kassaga ruxsati yo‘q.");
        }

        var overview = await GetOverviewAsync();
        if (amount > overview.Me.Available)
        {
            throw new InvalidOperationException($"Qo‘lingizda {overview.Me.Available:N0} so‘m bor (tasdiqlanmagan topshiriqlardan tashqari). Shundan ko‘p topshirib bo‘lmaydi.");
        }

        db.CashTransfers.Add(new CashTransfer
        {
            OrganizationId = user.OrganizationId,
            FromUserId = user.UserId,
            ToUserId = toUserId,
            Amount = amount,
            Note = note,
            Status = CashTransferStatus.Pending
        });
        db.FinancialAuditEntries.Add(new FinancialAuditEntry
        {
            OrganizationId = user.OrganizationId,
            ActorUserId = user.UserId,
            Action = "Kassa topshirig‘i",
            Description = $"{amount:N0} so‘m topshirildi (tasdiq kutilmoqda)"
        });
        await db.SaveChangesAsync();
    }

    public async Task RespondAsync(int transferId, bool accept)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Cash);
        await using var db = await contextFactory.CreateDbContextAsync();
        var transfer = await db.CashTransfers.SingleOrDefaultAsync(item =>
            item.OrganizationId == user.OrganizationId && item.Id == transferId)
            ?? throw new InvalidOperationException("Topshiriq topilmadi.");
        if (transfer.ToUserId != user.UserId)
        {
            throw new UnauthorizedAccessException("Faqat qabul qiluvchi tasdiqlay oladi.");
        }
        if (transfer.Status != CashTransferStatus.Pending)
        {
            throw new InvalidOperationException("Bu topshiriq allaqachon ko‘rib chiqilgan.");
        }

        transfer.Status = accept ? CashTransferStatus.Accepted : CashTransferStatus.Rejected;
        transfer.RespondedAtUtc = DateTime.UtcNow;
        db.FinancialAuditEntries.Add(new FinancialAuditEntry
        {
            OrganizationId = user.OrganizationId,
            ActorUserId = user.UserId,
            Action = accept ? "Kassa qabul qilindi" : "Kassa rad etildi",
            Description = $"{transfer.Amount:N0} so‘m"
        });
        await db.SaveChangesAsync();
    }

    public async Task CancelAsync(int transferId)
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Cash);
        await using var db = await contextFactory.CreateDbContextAsync();
        var transfer = await db.CashTransfers.SingleOrDefaultAsync(item =>
            item.OrganizationId == user.OrganizationId && item.Id == transferId && item.FromUserId == user.UserId)
            ?? throw new InvalidOperationException("Topshiriq topilmadi.");
        if (transfer.Status != CashTransferStatus.Pending)
        {
            throw new InvalidOperationException("Faqat tasdiqlanmagan topshiriqni bekor qilish mumkin.");
        }

        db.CashTransfers.Remove(transfer);
        await db.SaveChangesAsync();
    }
}

public sealed record CashHolder(
    string UserId,
    string Name,
    CenterRole Role,
    decimal Received,
    decimal Spent,
    decimal Incoming,
    decimal Outgoing,
    decimal Balance,
    decimal Available);

public sealed record CashTransferRow(
    int Id,
    string FromUserId,
    string FromName,
    string ToUserId,
    string ToName,
    decimal Amount,
    string Note,
    CashTransferStatus Status,
    DateTime CreatedAtUtc,
    DateTime? RespondedAtUtc);

public sealed record BranchCashRow(int BranchId, string BranchName, decimal CashIncome, decimal CashExpense)
{
    public decimal Balance => CashIncome - CashExpense;
}

public sealed record CashRecipient(string UserId, string Name, CenterRole Role);

public sealed record CashDeskOverview(
    CashHolder Me,
    IReadOnlyList<CashHolder> Holders,
    IReadOnlyList<CashTransferRow> Transfers,
    IReadOnlyList<BranchCashRow> Branches,
    IReadOnlyList<CashRecipient> Recipients,
    bool CanSeeAll);
