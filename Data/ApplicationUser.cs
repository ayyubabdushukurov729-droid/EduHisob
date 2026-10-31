using Microsoft.AspNetCore.Identity;

namespace EduHisob.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    public int? OrganizationId { get; set; }
    public string FullName { get; set; } = "";
    public CenterRole CenterRole { get; set; } = CenterRole.Owner;
    public bool IsActive { get; set; } = true;

    // Platforma egasi: markazga biriktirilmaydi, barcha markazlarni boshqaradi.
    public bool IsPlatformAdmin { get; set; }
}
