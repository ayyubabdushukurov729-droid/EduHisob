namespace EduHisob.Data;

public enum CenterStatus { Trial, Paid, Expiring, Expired, Blocked }

public static class CenterStatuses
{
    public const int ExpiringDays = 7;

    public static CenterStatus Of(bool isBlocked, DateTime trialEndsAtUtc, DateTime? paidUntilUtc, DateTime nowUtc)
    {
        if (isBlocked) return CenterStatus.Blocked;
        var accessEnds = AccessEnds(trialEndsAtUtc, paidUntilUtc);
        if (accessEnds < nowUtc) return CenterStatus.Expired;
        if (accessEnds < nowUtc.AddDays(ExpiringDays)) return CenterStatus.Expiring;
        return paidUntilUtc is { } paid && paid > nowUtc ? CenterStatus.Paid : CenterStatus.Trial;
    }

    public static DateTime AccessEnds(DateTime trialEndsAtUtc, DateTime? paidUntilUtc) =>
        paidUntilUtc is { } paid && paid > trialEndsAtUtc ? paid : trialEndsAtUtc;

    public static string Label(CenterStatus status) => status switch
    {
        CenterStatus.Trial => "Sinovda",
        CenterStatus.Paid => "To‘langan",
        CenterStatus.Expiring => "Tugayapti",
        CenterStatus.Expired => "Muddati o‘tgan",
        CenterStatus.Blocked => "Bloklangan",
        _ => status.ToString()
    };

    public static string Css(CenterStatus status) => status switch
    {
        CenterStatus.Paid => "status-active",
        CenterStatus.Trial => "status-frozen",
        CenterStatus.Expiring => "status-trial",
        CenterStatus.Expired => "status-danger",
        _ => "status-muted"
    };
}

public sealed record CenterDirectoryEntry(
    int Id,
    string Name,
    string OwnerName,
    string OwnerPhone,
    DateTime CreatedAtUtc,
    DateTime TrialEndsAtUtc,
    DateTime? PaidUntilUtc,
    decimal MonthlyFee,
    bool IsBlocked,
    CenterStatus Status,
    int BranchCount,
    int StudentCount,
    int ActiveStudentCount,
    int ActiveGroupCount,
    int UserCount,
    decimal PaidTotal)
{
    public DateTime AccessEndsAtUtc => CenterStatuses.AccessEnds(TrialEndsAtUtc, PaidUntilUtc);
}

public sealed record PlatformOverview(
    int CenterCount,
    int PaidCount,
    int TrialCount,
    int ExpiringCount,
    int ExpiredCount,
    int BlockedCount,
    int NewCentersThisMonth,
    decimal RevenueThisMonth,
    decimal RevenueLastMonth,
    decimal ExpectedMonthly,
    decimal RevenueTotal,
    int StudentCount,
    int ActiveStudentCount,
    decimal CentersIncomeThisMonth,
    IReadOnlyList<PlatformMonthRevenue> Months,
    IReadOnlyList<CenterDirectoryEntry> NeedsAttention,
    IReadOnlyList<PlatformPaymentRow> RecentPayments);

public sealed record PlatformMonthRevenue(DateOnly Month, decimal Amount, int PaymentCount, int NewCenters);

public sealed record PlatformPaymentRow(
    int Id,
    int CenterId,
    string CenterName,
    decimal Amount,
    int Months,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    PaymentMethod Method,
    string Note,
    DateTime PaidAtUtc,
    bool CenterExists);

public sealed record PlatformCenterMember(string UserId, string FullName, string Login, CenterRole Role, bool IsActive);

public sealed record PlatformCenterDetail(
    CenterDirectoryEntry Center,
    string BlockReason,
    string PlatformNote,
    IReadOnlyList<PlatformCenterMember> Members,
    IReadOnlyList<PlatformPaymentRow> Payments);

public sealed record NewCenterInput(
    string CenterName,
    string OwnerName,
    string OwnerPhone,
    string Password,
    int TrialDays,
    decimal MonthlyFee);

public sealed record PlatformCenterReport(
    string CenterName,
    string OwnerPhone,
    DateTime CreatedAtUtc,
    DateTime TrialEndsAtUtc,
    int BranchCount,
    int StudentCount,
    int ActiveStudentCount,
    int TrialStudentCount,
    int FrozenStudentCount,
    int LeftStudentCount,
    int ActiveGroupCount,
    int AttendanceCount,
    int AttendedCount,
    decimal IncomeTotal,
    decimal ExpenseTotal,
    IReadOnlyList<PlatformDailyTotal> DailyTotals,
    IReadOnlyList<PlatformCategoryTotal> CategoryTotals,
    IReadOnlyList<PlatformPaymentTotal> PaymentTotals);

public sealed record PlatformBranchOption(int Id, string Name);
public sealed record PlatformDailyTotal(DateOnly Date, decimal Income, decimal Expense);
public sealed record PlatformCategoryTotal(FinancialTransactionType Type, string Category, int Count, decimal Amount);
public sealed record PlatformPaymentTotal(FinancialTransactionType Type, PaymentMethod Method, int Count, decimal Amount);
