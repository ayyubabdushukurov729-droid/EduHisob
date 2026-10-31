using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public sealed class StudyGroup
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int BranchId { get; set; }
    public int CourseId { get; set; }
    public int? RoomId { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = "";

    public int? TeacherId { get; set; }

    [MaxLength(120)]
    public string TeacherName { get; set; } = "";

    // Ustozga shu guruh tushumidan beriladigan ulush (0–100%).
    [Range(typeof(decimal), "0", "100")]
    public decimal TeacherSharePercent { get; set; }

    [MaxLength(120)]
    public string RoomName { get; set; } = "";

    [Required]
    [MaxLength(80)]
    public string DaysOfWeek { get; set; } = "";

    [Required]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Boshlanish vaqtini 09:00 ko‘rinishida kiriting.")]
    public string StartTime { get; set; } = "09:00";

    [Range(1, 1000)]
    public int Capacity { get; set; } = 20;

    [Range(typeof(decimal), "0", "999999999")]
    public decimal MonthlyFee { get; set; }

    [Range(1, 100)]
    public int LessonsPerMonth { get; set; } = 12;

    [Range(1, 600)]
    public int LessonDurationMinutes { get; set; } = 60;

    public bool IsActive { get; set; } = true;
}
