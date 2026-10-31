using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduHisob.Data;

public sealed record CenterUser(string UserId, int OrganizationId, CenterRole Role, string DisplayName);

public sealed record CenterLock(string CenterName, bool Blocked, string Message);

// Joriy foydalanuvchining markazi va rolini aniqlaydi. Har bir chaqiruvda bazadan o'qiladi,
// shuning uchun rol o'zgarsa yoki akkaunt o'chirilsa darhol kuchga kiradi.
public sealed class CenterAccess(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    AuthenticationStateProvider authenticationStateProvider)
{
    public async Task<CenterUser> GetAsync()
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Avval tizimga kiring.");

        await using var db = await contextFactory.CreateDbContextAsync();
        var account = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.OrganizationId, user.CenterRole, user.IsActive, user.FullName, user.Email })
            .SingleOrDefaultAsync()
            ?? throw new UnauthorizedAccessException("Avval tizimga kiring.");

        if (!account.IsActive)
        {
            throw new UnauthorizedAccessException("Akkauntingiz o‘chirilgan.");
        }
        if (account.OrganizationId is null)
        {
            throw new InvalidOperationException("Hisobingizga markaz biriktirilmagan.");
        }

        var centerLock = await FindLockAsync(db, account.OrganizationId.Value);
        if (centerLock is not null)
        {
            throw new UnauthorizedAccessException(centerLock.Message);
        }

        var displayName = string.IsNullOrWhiteSpace(account.FullName) ? account.Email ?? "" : account.FullName;
        return new CenterUser(userId, account.OrganizationId.Value, account.CenterRole, displayName);
    }

    // Markaz bloklangan yoki obunasi tugagan bo'lsa sababini qaytaradi. Platforma egasi va tizimga kirmaganlar uchun null.
    public async Task<CenterLock?> GetLockAsync()
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return null;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var organizationId = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.OrganizationId)
            .SingleOrDefaultAsync();
        return organizationId is int id ? await FindLockAsync(db, id) : null;
    }

    private static async Task<CenterLock?> FindLockAsync(ApplicationDbContext db, int organizationId)
    {
        var center = await db.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId)
            .Select(item => new { item.Name, item.IsBlocked, item.BlockReason, item.TrialEndsAtUtc, item.PaidUntilUtc })
            .SingleOrDefaultAsync();
        if (center is null)
        {
            return new CenterLock("", true, "Markaz topilmadi.");
        }
        if (center.IsBlocked)
        {
            var reason = string.IsNullOrWhiteSpace(center.BlockReason) ? "" : $" Sabab: {center.BlockReason}";
            return new CenterLock(center.Name, true, $"Markaz kabineti vaqtincha yopilgan.{reason}");
        }

        var accessEnds = center.PaidUntilUtc is { } paid && paid > center.TrialEndsAtUtc ? paid : center.TrialEndsAtUtc;
        return accessEnds < DateTime.UtcNow
            ? new CenterLock(center.Name, false, $"Obuna muddati {accessEnds.ToLocalTime():dd.MM.yyyy} da tugagan. Davom etish uchun to‘lovni amalga oshiring.")
            : null;
    }

    public async Task<CenterUser> RequireAsync(params CenterRole[] roles)
    {
        var user = await GetAsync();
        if (roles.Length > 0 && !roles.Contains(user.Role))
        {
            throw new UnauthorizedAccessException("Bu amal uchun ruxsatingiz yo‘q.");
        }

        return user;
    }

    public static readonly CenterRole[] Management = [CenterRole.Owner, CenterRole.Admin];
    public static readonly CenterRole[] Finance = [CenterRole.Owner, CenterRole.Accountant];
    public static readonly CenterRole[] Hr = [.. Finance, CenterRole.HrManager];
    public static readonly CenterRole[] Cash = [.. Finance, CenterRole.Admin, CenterRole.Cashier, CenterRole.Receptionist];
    public static readonly CenterRole[] StudentDesk = [.. Cash, CenterRole.SalesManager, CenterRole.Manager];
    public static readonly CenterRole[] Teaching = [CenterRole.Owner, CenterRole.Admin, CenterRole.Teacher, CenterRole.Manager];
    public static readonly CenterRole[] Sales = [CenterRole.Owner, CenterRole.Admin, CenterRole.SalesManager, CenterRole.Manager, CenterRole.Receptionist, CenterRole.Marketer];
}

// Login cookie'siga rol va ismni qo'shadi: [Authorize(Roles = ...)] va AuthorizeView Roles shular bilan ishlaydi.
public sealed class CenterUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        // Platforma egasi markaz rolini olmaydi: markaz menyulari unga ko'rinmaydi.
        identity.AddClaim(user.IsPlatformAdmin
            ? new Claim(PlatformAdminDataService.ClaimType, "true")
            : new Claim(ClaimTypes.Role, user.CenterRole.ToString()));
        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            identity.AddClaim(new Claim("full_name", user.FullName));
        }

        return identity;
    }
}

// O'chirilgan (IsActive = false) akkaunt tizimga kira olmaydi.
public sealed class CenterSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user) =>
        user.IsActive && await base.CanSignInAsync(user);
}
