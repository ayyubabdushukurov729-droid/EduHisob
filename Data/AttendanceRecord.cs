using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public enum AttendanceStatus
{
    Present,
    Absent,
    Late,
    Excused
}

public sealed class AttendanceRecord
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int StudyGroupId { get; set; }
    public int StudentId { get; set; }
    public DateOnly LessonDate { get; set; }
    public AttendanceStatus Status { get; set; }

    [MaxLength(500)]
    public string Note { get; set; } = "";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
