using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public enum CashTransferStatus
{
    Pending,
    Accepted,
    Rejected
}

// Xodimlar o'rtasida naqd pul topshirish. Qabul qiluvchi tasdiqlamaguncha pul jo'natuvchida hisoblanadi.
public sealed class CashTransfer
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }

    [Required]
    [MaxLength(450)]
    public string FromUserId { get; set; } = "";

    [Required]
    [MaxLength(450)]
    public string ToUserId { get; set; } = "";

    [Range(typeof(decimal), "1", "999999999")]
    public decimal Amount { get; set; }

    [MaxLength(300)]
    public string Note { get; set; } = "";

    public CashTransferStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAtUtc { get; set; }
}
