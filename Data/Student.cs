using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public enum StudentStatus
{
    Trial,
    Active,
    Frozen,
    Left,
    Archived
}

public sealed class Student
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

    // Ixtiyoriy, shuning uchun [Phone] qo'yilmagan (u bo'sh qiymatni rad etadi).
    [MaxLength(32)]
    public string ParentPhone { get; set; } = "";

    public DateOnly? BirthDate { get; set; }

    public StudentStatus Status { get; set; } = StudentStatus.Active;
    public DateOnly JoinedOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [MaxLength(1000)]
    public string Notes { get; set; } = "";
}
