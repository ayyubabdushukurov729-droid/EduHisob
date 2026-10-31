using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public sealed class Organization
{
    public int Id { get; set; }

    [MaxLength(160)]
    public string Name { get; set; } = "";

    [MaxLength(32)]
    public string OwnerPhone { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime TrialEndsAtUtc { get; set; }

    // Obuna: platforma egasi to'lov qabul qilganda uzayadi.
    public DateTime? PaidUntilUtc { get; set; }

    // Markaz har oy to'laydigan summa (platforma egasi belgilaydi).
    public decimal MonthlyFee { get; set; }

    public bool IsBlocked { get; set; }

    [MaxLength(300)]
    public string BlockReason { get; set; } = "";

    // Faqat platforma egasi ko'radigan izoh.
    [MaxLength(1000)]
    public string PlatformNote { get; set; } = "";

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();

    // Kirish shu sanagacha ochiq: sinov yoki to'langan muddatning kechrog'i.
    public DateTime AccessEndsAtUtc => PaidUntilUtc is { } paid && paid > TrialEndsAtUtc ? paid : TrialEndsAtUtc;
}

// Markazdan obuna uchun olingan to'lov. Markaz o'chirilsa ham tarix qoladi (shuning uchun FK yo'q va nomi saqlanadi).
public sealed class PlatformPayment
{
    public int Id { get; set; }
    public int CenterId { get; set; }

    [MaxLength(160)]
    public string CenterName { get; set; } = "";

    public decimal Amount { get; set; }
    public int Months { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public PaymentMethod Method { get; set; }

    [MaxLength(300)]
    public string Note { get; set; } = "";

    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;
}
