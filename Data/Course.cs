using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public sealed class Course
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string Description { get; set; } = "";

    public bool IsActive { get; set; } = true;
}
