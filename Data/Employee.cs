using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public enum EmployeeRole
{
    Teacher,
    Staff
}

// Ustoz qanday haq oladi. SalaryAndShare birinchi turadi: eski yozuvlar (0) avvalgidek oylik + ulush hisoblanadi.
public enum EmployeePayType
{
    SalaryAndShare,
    SalaryOnly,
    ShareOnly
}

public sealed class Employee
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int BranchId { get; set; }

    [Required]
    [MaxLength(160)]
    public string FullName { get; set; } = "";

    [Required]
    [Phone]
    [MaxLength(32)]
    public string Phone { get; set; } = "";

    [Required]
    [MaxLength(120)]
    public string Position { get; set; } = "";

    public EmployeeRole Role { get; set; }
    public DateOnly HiredOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Range(typeof(decimal), "0", "999999999")]
    public decimal AgreedMonthlySalary { get; set; }

    public EmployeePayType PayType { get; set; }

    // Yangi guruh ochilganda ulush foizi shu qiymat bilan to'ldiriladi.
    [Range(typeof(decimal), "0", "100")]
    public decimal DefaultSharePercent { get; set; }

    public bool IsActive { get; set; } = true;
}
