using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public sealed class BranchRoom
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int BranchId { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = "";

    [Range(1, 1000)]
    public int Capacity { get; set; } = 20;

    public bool IsActive { get; set; } = true;
}
