using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public sealed class ExpenseBudget
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int BranchId { get; set; }
    public DateOnly Month { get; set; }

    [Required]
    [MaxLength(120)]
    public string Category { get; set; } = "";

    [Range(typeof(decimal), "1", "999999999")]
    public decimal LimitAmount { get; set; }
}

public sealed class FinancialAuditEntry
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(450)]
    public string ActorUserId { get; set; } = "";

    [Required]
    [MaxLength(40)]
    public string Action { get; set; } = "";

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = "";
}
