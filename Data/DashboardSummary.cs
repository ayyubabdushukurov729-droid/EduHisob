namespace EduHisob.Data;

public sealed record DashboardSummary(
    string CenterName,
    int BranchCount,
    int StudentCount,
    int ActiveStudents,
    int TrialStudents,
    int FrozenStudents,
    int LeftStudents,
    int NewStudentsThisMonth,
    int ActiveGroups,
    decimal CurrentPeriodIncome,
    decimal PreviousPeriodIncome,
    int AttendedLessons,
    int CountedLessons,
    DateTime TrialEndsAtUtc);

public sealed record DashboardFinance(decimal MonthIncome, decimal MonthExpense, int DebtorCount, decimal DebtTotal)
{
    public decimal MonthResult => MonthIncome - MonthExpense;
}
