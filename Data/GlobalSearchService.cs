using Microsoft.EntityFrameworkCore;

namespace EduHisob.Data;

// Yuqori paneldagi umumiy qidiruv: o'quvchi, guruh, xodim, lid va sahifa nomlari bo'yicha.
public sealed class GlobalSearchService(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    CenterAccess centerAccess)
{
    private static readonly (string Title, string Href, string Keywords, CenterRole[] Roles)[] Pages =
    [
        ("Bosh sahifa", "/boshqaruv", "bosh dashboard asosiy", []),
        ("O‘quvchilar", "/oquvchilar", "oquvchi talaba student royxat", CenterAccess.StudentDesk),
        ("Yangi o‘quvchi qo‘shish", "/oquvchilar/yangi", "qoshish yangi oquvchi talaba", CenterAccess.StudentDesk),
        ("Qarzdorlar", "/qarzdorlar", "qarz qarzdor debitor tolamagan", CenterAccess.Cash),
        ("Chiqib ketganlar", "/chiqib-ketganlar", "ketgan chiqgan arxiv sabab", CenterAccess.StudentDesk),
        ("Sotuv (lidlar)", "/sotuv", "lid sotuv crm voronka mijoz", CenterAccess.Sales),
        ("Filiallar", "/filiallar", "filial", CenterAccess.Management),
        ("Kurslar va guruhlar", "/guruhlar", "kurs guruh", CenterAccess.StudentDesk),
        ("Xonalar", "/xonalar", "xona auditoriya", CenterAccess.Management),
        ("Dars jadvali", "/dars-jadvali", "jadval dars vaqt", []),
        ("Davomat", "/davomat", "davomat yoqlama keldi kelmadi", CenterAccess.Teaching),
        ("Xodimlar", "/xodimlar", "xodim ustoz oqituvchi", CenterAccess.Hr),
        ("Foydalanuvchilar va rollar", "/foydalanuvchilar", "rol ruxsat akkaunt foydalanuvchi parol", CenterAccess.Management),
        ("Umumiy moliya", "/moliya", "moliya tolov tushum xarajat kirim chiqim", CenterAccess.Cash),
        ("Kassa (mendagi pul)", "/kassa", "kassa naqd mendagi pul topshirish", CenterAccess.Cash),
        ("Ish haqi", "/ish-haqi", "ish haqi oylik maosh avans", CenterAccess.Hr),
        ("Ustozlar ulushi", "/ustoz-ulushi", "ustoz ulush foiz protsent", CenterAccess.Finance),
        ("Hisobotlar", "/hisobotlar", "hisobot statistika budjet audit", CenterAccess.Finance)
    ];

    public async Task<GlobalSearchResult> SearchAsync(string query)
    {
        query = query.Trim();
        var user = await centerAccess.RequireAsync();
        if (query.Length < 2)
        {
            return new GlobalSearchResult(query, [], [], [], [], []);
        }

        var normalized = Normalize(query);
        var pages = Pages
            .Where(page => page.Roles.Length == 0 || page.Roles.Contains(user.Role))
            .Where(page => Normalize(page.Title).Contains(normalized) || page.Keywords.Contains(normalized))
            .Select(page => new SearchHit(page.Title, page.Href, "Sahifa"))
            .ToList();

        var digits = StudentAccountService.DigitsOnly(query);
        var phoneKey = digits.Length >= 4 ? digits : null;
        await using var db = await contextFactory.CreateDbContextAsync();

        List<SearchHit> students = [];
        if (CenterAccess.StudentDesk.Contains(user.Role))
        {
            var rows = await db.Students.AsNoTracking()
                .Where(student => student.OrganizationId == user.OrganizationId)
                .Select(student => new { student.Id, student.FullName, student.Phone, student.ParentPhone, student.Status })
                .ToListAsync();
            students = rows
                .Where(student => Normalize(student.FullName).Contains(normalized) ||
                    (phoneKey is not null && (StudentAccountService.DigitsOnly(student.Phone).Contains(phoneKey) ||
                                              StudentAccountService.DigitsOnly(student.ParentPhone).Contains(phoneKey))))
                .Take(20)
                .Select(student => new SearchHit(student.FullName, $"/oquvchilar/{student.Id}", student.Phone))
                .ToList();
        }

        var groups = (await db.StudyGroups.AsNoTracking()
                .Where(group => group.OrganizationId == user.OrganizationId && group.IsActive)
                .Select(group => new { group.Id, group.Name, group.TeacherName })
                .ToListAsync())
            .Where(group => Normalize(group.Name).Contains(normalized) || Normalize(group.TeacherName).Contains(normalized))
            .Take(20)
            .Select(group => new SearchHit(group.Name, $"/guruhlar/{group.Id}", string.IsNullOrWhiteSpace(group.TeacherName) ? "Ustoz belgilanmagan" : group.TeacherName))
            .ToList();

        List<SearchHit> employees = [];
        if (CenterAccess.Hr.Contains(user.Role))
        {
            employees = (await db.Employees.AsNoTracking()
                    .Where(employee => employee.OrganizationId == user.OrganizationId && employee.IsActive)
                    .Select(employee => new { employee.FullName, employee.Phone, employee.Position })
                    .ToListAsync())
                .Where(employee => Normalize(employee.FullName).Contains(normalized) ||
                    Normalize(employee.Position).Contains(normalized) ||
                    (phoneKey is not null && StudentAccountService.DigitsOnly(employee.Phone).Contains(phoneKey)))
                .Take(20)
                .Select(employee => new SearchHit(employee.FullName, "/xodimlar", employee.Position))
                .ToList();
        }

        List<SearchHit> leads = [];
        if (CenterAccess.Sales.Contains(user.Role))
        {
            leads = (await db.Leads.AsNoTracking()
                    .Where(lead => lead.OrganizationId == user.OrganizationId)
                    .Select(lead => new { lead.FullName, lead.Phone, lead.Stage })
                    .ToListAsync())
                .Where(lead => Normalize(lead.FullName).Contains(normalized) ||
                    (phoneKey is not null && StudentAccountService.DigitsOnly(lead.Phone).Contains(phoneKey)))
                .Take(20)
                .Select(lead => new SearchHit(lead.FullName, $"/sotuv?q={Uri.EscapeDataString(lead.Phone)}", $"{lead.Phone} · {Lead.StageLabel(lead.Stage)}"))
                .ToList();
        }

        return new GlobalSearchResult(query, pages, students, groups, employees, leads);
    }

    // Apostrof turlari va katta-kichik harf farqi qidiruvga xalaqit bermasligi uchun.
    private static string Normalize(string value) =>
        new string(value.ToLowerInvariant()
            .Replace("o‘", "o").Replace("g‘", "g").Replace("o'", "o").Replace("g'", "g").Replace("o`", "o").Replace("g`", "g")
            .Where(character => character is not ('’' or '‘' or '\'' or '`'))
            .ToArray());
}

public sealed record SearchHit(string Title, string Href, string Subtitle);

public sealed record GlobalSearchResult(
    string Query,
    IReadOnlyList<SearchHit> Pages,
    IReadOnlyList<SearchHit> Students,
    IReadOnlyList<SearchHit> Groups,
    IReadOnlyList<SearchHit> Employees,
    IReadOnlyList<SearchHit> Leads)
{
    public bool IsEmpty => Pages.Count + Students.Count + Groups.Count + Employees.Count + Leads.Count == 0;
}
