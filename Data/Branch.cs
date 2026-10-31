using System.ComponentModel.DataAnnotations;

namespace EduHisob.Data;

public sealed class Branch
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(300)]
    public string Address { get; set; } = "";

    public bool IsActive { get; set; } = true;
}
