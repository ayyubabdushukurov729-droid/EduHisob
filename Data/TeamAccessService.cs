using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduHisob.Data;

// Markaz xodimlarining tizimga kirish akkauntlari va rollari. O'zgartirishni faqat asoschi qila oladi.
// Asoschi roli bitta: uni boshqa akkauntga berib bo'lmaydi, shuning uchun to'liq huquqli akkauntlar ko'payib ketmaydi.
public sealed class TeamAccessService(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    UserManager<ApplicationUser> userManager,
    CenterAccess centerAccess)
{
    public async Task<IReadOnlyList<TeamMember>> GetMembersAsync()
    {
        var user = await centerAccess.RequireAsync(CenterAccess.Management);
        await using var db = await contextFactory.CreateDbContextAsync();
        var lastActions = (await db.FinancialAuditEntries.AsNoTracking()
                .Where(entry => entry.OrganizationId == user.OrganizationId)
                .Select(entry => new { entry.ActorUserId, entry.RecordedAtUtc })
                .ToListAsync())
            .GroupBy(entry => entry.ActorUserId)
            .ToDictionary(group => group.Key, group => group.Max(entry => entry.RecordedAtUtc));
        return (await db.Users.AsNoTracking()
                .Where(account => account.OrganizationId == user.OrganizationId)
                .Select(account => new { account.Id, account.FullName, account.UserName, account.PhoneNumber, account.CenterRole, account.IsActive })
                .ToListAsync())
            .Select(account => new TeamMember(
                account.Id,
                account.FullName,
                account.UserName ?? "",
                account.PhoneNumber ?? "",
                account.CenterRole,
                account.IsActive,
                account.Id == user.UserId,
                lastActions.TryGetValue(account.Id, out var lastAction) ? lastAction : null))
            .OrderBy(account => account.Role)
            .ThenBy(account => account.FullName)
            .ToList();
    }

    // Xodim akkaunti elektron pochtasiz ochiladi: login — telefon raqami (998XXXXXXXXX ko'rinishida).
    public async Task CreateMemberAsync(string fullName, string phone, string password, CenterRole role)
    {
        fullName = string.Join(' ', fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        phone = phone.Trim();
        // Ism va familiya majburiy: "A" yoki "Admin 4569" kabi kimligi noma'lum akkauntlar bo'lmasligi kerak.
        if (fullName.Length < 5 || !fullName.Contains(' '))
        {
            throw new InvalidOperationException("Xodimning ism va familiyasini to‘liq yozing.");
        }
        var login = PhoneLogin(phone)
            ?? throw new InvalidOperationException("Telefon raqamini to‘liq kiriting, masalan: +998 90 123 45 67.");
        if (role == CenterRole.Owner || !Enum.IsDefined(role))
        {
            throw new InvalidOperationException("Asoschi roli faqat bitta bo‘ladi. Boshqa rolni tanlang.");
        }

        var owner = await centerAccess.RequireAsync(CenterRole.Owner);
        if (await userManager.FindByNameAsync(login) is not null)
        {
            throw new InvalidOperationException("Bu telefon raqami bilan akkaunt allaqachon mavjud.");
        }

        var account = new ApplicationUser
        {
            UserName = login,
            PhoneNumber = "+" + login,
            PhoneNumberConfirmed = true,
            FullName = fullName,
            OrganizationId = owner.OrganizationId,
            CenterRole = role,
            IsActive = true
        };
        var result = await userManager.CreateAsync(account, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(TranslateError)));
        }

        await AuditAsync(owner, "Akkaunt ochildi", $"{fullName} (+{login}) — {CenterRoles.Label(role)}");
    }

    // "+998 90 123-45-67", "90 123 45 67" va "998901234567" bir xil loginga aylanadi. Noto'g'ri raqam uchun null.
    public static string? PhoneLogin(string input)
    {
        var digits = StudentAccountService.DigitsOnly(input);
        if (digits.Length == 9)
        {
            digits = "998" + digits;
        }

        return digits.Length is >= 12 and <= 15 ? digits : null;
    }

    public async Task ChangeRoleAsync(string userId, CenterRole role)
    {
        if (role == CenterRole.Owner || !Enum.IsDefined(role))
        {
            throw new InvalidOperationException("Asoschi rolini boshqa akkauntga berib bo‘lmaydi.");
        }

        var (owner, account) = await GetEditableAsync(userId);
        var previous = account.CenterRole;
        account.CenterRole = role;
        await SaveAsync(account);
        await AuditAsync(owner, "Rol o‘zgardi", $"{account.FullName}: {CenterRoles.Label(previous)} → {CenterRoles.Label(role)}");
    }

    public async Task SetActiveAsync(string userId, bool isActive)
    {
        var (owner, account) = await GetEditableAsync(userId);
        account.IsActive = isActive;
        await SaveAsync(account);
        await AuditAsync(owner, isActive ? "Akkaunt yoqildi" : "Akkaunt o‘chirildi", account.FullName);
    }

    public async Task ResetPasswordAsync(string userId, string newPassword)
    {
        var (owner, account) = await GetEditableAsync(userId);
        var token = await userManager.GeneratePasswordResetTokenAsync(account);
        var result = await userManager.ResetPasswordAsync(account, token, newPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(TranslateError)));
        }

        await AuditAsync(owner, "Parol yangilandi", account.FullName);
    }

    private async Task<(CenterUser Owner, ApplicationUser Account)> GetEditableAsync(string userId)
    {
        var owner = await centerAccess.RequireAsync(CenterRole.Owner);
        if (userId == owner.UserId)
        {
            throw new InvalidOperationException("O‘z akkauntingizni bu yerda o‘zgartira olmaysiz.");
        }

        var account = await userManager.FindByIdAsync(userId);
        if (account is null || account.OrganizationId != owner.OrganizationId)
        {
            throw new InvalidOperationException("Akkaunt topilmadi.");
        }
        if (account.CenterRole == CenterRole.Owner)
        {
            throw new InvalidOperationException("Asoschi akkauntini o‘zgartirib bo‘lmaydi.");
        }

        return (owner, account);
    }

    private async Task SaveAsync(ApplicationUser account)
    {
        var result = await userManager.UpdateAsync(account);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(TranslateError)));
        }

        // Ochiq sessiyalar yangi rol yoki o'chirilish holatini qabul qilishi uchun.
        await userManager.UpdateSecurityStampAsync(account);
    }

    private async Task AuditAsync(CenterUser actor, string action, string description)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        db.FinancialAuditEntries.Add(new FinancialAuditEntry
        {
            OrganizationId = actor.OrganizationId,
            ActorUserId = actor.UserId,
            Action = action,
            Description = description.Length > 500 ? description[..500] : description
        });
        await db.SaveChangesAsync();
    }

    internal static string TranslateError(IdentityError error) => error.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordTooShort) => "Parol kamida 8 belgidan iborat bo‘lsin.",
        nameof(IdentityErrorDescriber.PasswordRequiresDigit) => "Parolda kamida bitta raqam bo‘lsin.",
        nameof(IdentityErrorDescriber.DuplicateUserName) => "Bu telefon raqami band.",
        nameof(IdentityErrorDescriber.DuplicateEmail) => "Bu elektron pochta band.",
        nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) => "Login noto‘g‘ri.",
        _ => error.Description
    };
}

public sealed record TeamMember(
    string UserId,
    string FullName,
    string Login,
    string Phone,
    CenterRole Role,
    bool IsActive,
    bool IsMe,
    DateTime? LastFinancialActionUtc);
