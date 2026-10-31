using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

// Har oy har bir faol guruh a'zoligi uchun hisoblangan to'lov. O'quvchi balansi = to'lovlar − hisoblanganlar.
public sealed class StudentCharge
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int StudentId { get; set; }
    public int StudyGroupId { get; set; }
    public DateOnly Month { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal FullAmount { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal DiscountAmount { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal Amount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// Muzlatish oylar bo'yicha: FromMonth..ToMonth oralig'idagi oylarga to'lov hisoblanmaydi.
public sealed class StudentFreeze
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int StudentId { get; set; }
    public DateOnly FromMonth { get; set; }
    public DateOnly ToMonth { get; set; }

    [MaxLength(300)]
    public string Reason { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class StudentNote
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int StudentId { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Text { get; set; } = "";

    [MaxLength(450)]
    public string AuthorUserId { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum DebtStage
{
    New,
    Contacted,
    Promised,
    Problem,
    Paid
}

// Qarzdor bilan ishlash holati. Har bir o'quvchiga bitta yozuv.
public sealed class DebtCase
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int StudentId { get; set; }
    public DebtStage Stage { get; set; }
    public DateOnly? PromisedOn { get; set; }

    [MaxLength(500)]
    public string Note { get; set; } = "";

    // Nechanchi marta qarzga tushgani ("Qayta qarz · 2-marta").
    public int Occurrence { get; set; } = 1;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LeaveReason
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = "";

    // Takroriy nomlarning oldini olish uchun kichik harfli, ortiqcha bo'shliqsiz ko'rinish.
    [Required]
    [MaxLength(120)]
    public string NormalizedName { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public static string Normalize(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Replace('’', '\'').Replace('‘', '\'').Replace('`', '\'')
            .ToLowerInvariant();
}
