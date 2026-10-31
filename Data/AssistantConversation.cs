using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

// Yordamchi bilan suhbat. Har bir foydalanuvchi faqat o'z suhbatlarini ko'radi va o'chira oladi.
public sealed class AssistantConversation
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = "";

    [Required]
    [MaxLength(120)]
    public string Title { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AssistantMessage
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public bool FromUser { get; set; }
    public bool IsError { get; set; }

    [Required]
    [MaxLength(20000)]
    public string Text { get; set; } = "";

    // Bajarilgan amallar: "get_month_report,!accept_payment" ("!" — bajarilmagan).
    [MaxLength(1000)]
    public string Steps { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
