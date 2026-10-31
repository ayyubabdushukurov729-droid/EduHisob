using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public enum PayrollEntryType
{
    Bonus,
    Advance,
    SalaryPayment
}

public enum PaymentMethod
{
    Cash,
    Card,
    Terminal,
    BankTransfer
}

public enum FinancialTransactionType
{
    Expense,
    Income
}

public sealed class PayrollEntry
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int EmployeeId { get; set; }
    public int BranchId { get; set; }
    public DateOnly PayrollMonth { get; set; }
    public DateOnly EntryDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public PayrollEntryType Type { get; set; }

    [Range(typeof(decimal), "1", "999999999")]
    public decimal Amount { get; set; }

    public PaymentMethod? PaymentMethod { get; set; }

    [MaxLength(500)]
    public string Note { get; set; } = "";

    [MaxLength(450)]
    public string RecordedByUserId { get; set; } = "";
}

public sealed class FinancialTransaction
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int BranchId { get; set; }
    public DateOnly TransactionDate { get; set; }
    public FinancialTransactionType Type { get; set; }

    [Required]
    [MaxLength(120)]
    public string Category { get; set; } = "";

    public int? StudentId { get; set; }

    [Range(typeof(decimal), "1", "999999999")]
    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    [MaxLength(500)]
    public string Note { get; set; } = "";

    public int? PayrollEntryId { get; set; }

    // O'quvchi to'lovi qaysi guruh uchun (ustoz ulushini hisoblash uchun).
    public int? StudyGroupId { get; set; }

    [MaxLength(450)]
    public string? RecordedByUserId { get; set; }
}
