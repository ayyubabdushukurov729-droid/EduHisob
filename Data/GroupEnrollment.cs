using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public sealed class GroupEnrollment
{
    public int OrganizationId { get; set; }
    public int StudyGroupId { get; set; }
    public int StudentId { get; set; }
    public DateTime EnrolledAtUtc { get; set; } = DateTime.UtcNow;

    // 0–100. Oylik to'lov hisoblanganda shu foiz ayiriladi.
    public decimal DiscountPercent { get; set; }

    [MaxLength(200)]
    public string DiscountNote { get; set; } = "";

    // null bo'lsa o'quvchi guruhda faol. Davomat tarixi saqlanishi uchun yozuv o'chirilmaydi.
    public DateOnly? LeftOn { get; set; }
    public int? LeaveReasonId { get; set; }

    [MaxLength(300)]
    public string LeaveNote { get; set; } = "";
}
