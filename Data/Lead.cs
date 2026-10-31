using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public enum LeadStage
{
    New,
    Contacted,
    TrialLesson,
    Converted,
    Lost
}

public enum LeadSource
{
    Instagram,
    Telegram,
    Facebook,
    Website,
    PhoneCall,
    Referral,
    WalkIn,
    Other
}

public sealed class Lead
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int BranchId { get; set; }

    [Required]
    [MaxLength(160)]
    public string FullName { get; set; } = "";

    [Required]
    [MaxLength(32)]
    public string Phone { get; set; } = "";

    public LeadSource Source { get; set; }

    [MaxLength(120)]
    public string CourseInterest { get; set; } = "";

    public LeadStage Stage { get; set; }

    [MaxLength(450)]
    public string? AssignedUserId { get; set; }

    [MaxLength(300)]
    public string LostReason { get; set; } = "";

    [MaxLength(1000)]
    public string Note { get; set; } = "";

    public int? ConvertedStudentId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }

    public static string StageLabel(LeadStage stage) => stage switch
    {
        LeadStage.New => "Yangi lid",
        LeadStage.Contacted => "Bog‘lanildi",
        LeadStage.TrialLesson => "Sinov darsida",
        LeadStage.Converted => "O‘quvchi bo‘ldi",
        _ => "Yo‘qotildi"
    };

    public static string SourceLabel(LeadSource source) => source switch
    {
        LeadSource.Instagram => "Instagram",
        LeadSource.Telegram => "Telegram",
        LeadSource.Facebook => "Facebook",
        LeadSource.Website => "Veb-sayt",
        LeadSource.PhoneCall => "Qo‘ng‘iroq",
        LeadSource.Referral => "Tanish orqali",
        LeadSource.WalkIn => "O‘zi keldi",
        _ => "Boshqa"
    };
}
