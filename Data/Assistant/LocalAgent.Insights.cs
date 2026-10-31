using System.Text;

namespace EduHisob.Data.Assistant;

// Qo'shimcha javoblar: ustozlar ulushi (faqat moliya), rollar, yangi o'quvchilar, kunlik lidlar,
// ustozlar reytingi va kurs bo'yicha jadval/bo'sh joylar.
public sealed partial class LocalAgent
{
    private static readonly string[] RoleQuestionWords =
        ["nima qila", "nimalar qila", "ruxsat", "huquq", "vazifa", "nimani kor", "nimalarni kor", "kora oladi", "qila oladi", "kira oladi", "dostup", "imkoniyat"];

    private static CenterRole? RoleMentioned(IReadOnlyList<string> tokens)
    {
        if (UzbekText.Fuzzy(tokens, "sotuv menejer")) return CenterRole.SalesManager;
        if (UzbekText.Fuzzy(tokens, "administrator", "admin", "adminlar")) return CenterRole.Admin;
        if (UzbekText.Fuzzy(tokens, "buxgalter", "hisobchi")) return CenterRole.Accountant;
        if (UzbekText.Fuzzy(tokens, "kassir")) return CenterRole.Cashier;
        if (UzbekText.Fuzzy(tokens, "qabulxona", "resepshn", "reception")) return CenterRole.Receptionist;
        if (UzbekText.Fuzzy(tokens, "marketolog", "marketing")) return CenterRole.Marketer;
        if (UzbekText.Fuzzy(tokens, "kadrlar", "hr")) return CenterRole.HrManager;
        if (UzbekText.Fuzzy(tokens, "menejer")) return CenterRole.Manager;
        if (UzbekText.Fuzzy(tokens, "asoschi", "direktor", "rahbar")) return CenterRole.Owner;
        if (UzbekText.Fuzzy(tokens, "ustoz", "oqituvchi")) return CenterRole.Teacher;
        return null;
    }

    private static string RoleInfo(CenterRole role)
    {
        var sections = new (string Name, CenterRole[]? Roles)[]
        {
            ("O‘quvchilar va guruhlar", CenterAccess.StudentDesk),
            ("Dars jadvali", null),
            ("Davomat", CenterAccess.Teaching),
            ("Lidlar (sotuv)", CenterAccess.Sales),
            ("To‘lov qabul qilish, qarzdorlar, kassa", CenterAccess.Cash),
            ("Filiallar, xonalar, foydalanuvchilar ro‘yxati", CenterAccess.Management),
            ("Xodimlar", CenterAccess.Hr),
            ("Ish haqi", CenterAccess.Hr),
            ("Moliyaviy hisobotlar va ustozlar ulushi", CenterAccess.Finance),
            ("Akkaunt ochish va rollarni berish", [CenterRole.Owner])
        };
        var open = sections.Where(s => s.Roles is null || s.Roles.Contains(role)).Select(s => s.Name).ToList();
        var closed = sections.Where(s => s.Roles is not null && !s.Roles.Contains(role)).Select(s => s.Name).ToList();
        var text = new StringBuilder($"**{CenterRoles.Label(role)}** — {CenterRoles.Description(role)}.\n\n");
        text.Append("**Ochiq:** ").Append(string.Join(", ", open)).Append('.');
        if (closed.Count > 0) text.Append("\n\n**Yopiq:** ").Append(string.Join(", ", closed)).Append('.');
        return text.ToString();
    }

    // ---------- Ustozlar ulushi ----------

    private async Task<bool> CanSeeSharesAsync()
    {
        var role = (await access.GetAsync()).Role;
        return CenterAccess.Finance.Contains(role);
    }

    private const string SharesClosed = "Ustozlar ulushi faqat **asoschi va buxgalterga** ko‘rinadi — u Moliya → Ustozlar ulushi bo‘limida boshqariladi.";

    private async Task<string> SharesAsync(string text)
    {
        if (!await CanSeeSharesAsync()) return SharesClosed;
        var month = UzbekText.Month(text, Today) ?? new DateOnly(Today.Year, Today.Month, 1);
        var rows = await center.GetTeacherSharesAsync(month);
        steps.Add(new AssistantStep("get_teacher_shares", false));
        lastTopic = null;
        if (rows.Count == 0) return "Ustozlar yo‘q. Avval [Xodimlar](/xodimlar) bo‘limida ustoz qo‘shing.";

        var (teacher, group) = MatchShareTarget(rows, text);
        if (group is { } g)
        {
            var owner = rows.First(r => r.Groups.Contains(g));
            return $"""
                **[{g.Name}](/guruhlar/{g.GroupId})** — ustoz **{owner.FullName}**, ulushi **{(owner.PayType == EmployeePayType.SalaryOnly ? "yo‘q (faqat oylik)" : $"{g.SharePercent:0.##}%")}**.
                {UzbekText.MonthName(month)}: guruh tushumi {UzbekText.Money(g.MonthIncome)}, ustoz ulushi **{UzbekText.Money(g.MonthShare)}**.

                O‘zgartirish: _«{g.Name} guruhida ulush 35% bo‘lsin»_ yoki [Ustozlar ulushi](/ustoz-ulushi).
                """;
        }
        if (teacher is { } t)
        {
            var body = new StringBuilder($"**{t.FullName}** — {PayScheme(t)}\n\n");
            if (t.Groups.Count == 0) return body.Append("Faol guruhi yo‘q.").ToString();
            body.Append($"| Guruh | Foiz | {UzbekText.MonthName(month)} tushumi | Ulush |\n|---|---|---|---|\n");
            foreach (var row in t.Groups) body.Append($"| [{row.Name}](/guruhlar/{row.GroupId}) | {row.SharePercent:0.##}% | {UzbekText.Money(row.MonthIncome)} | {UzbekText.Money(row.MonthShare)} |\n");
            body.Append($"\nJami ulush: **{UzbekText.Money(t.Groups.Sum(x => x.MonthShare))}**.");
            return body.ToString();
        }

        // "IELTS guruhida necha foiz?" — nomida shu so'z bor guruhlar.
        var queryTokens = UzbekText.Tokens(text);
        var partial = rows
            .SelectMany(r => r.Groups.Select(x => (Teacher: r, Group: x)))
            .Where(x => UzbekText.Tokens(x.Group.Name).Any(word => word.Length >= 4 && queryTokens.Contains(word)))
            .ToList();
        if (partial.Count is > 0 and <= 6)
        {
            var some = new StringBuilder($"| Guruh | Ustoz | Foiz | {UzbekText.MonthName(month)} ulushi |\n|---|---|---|---|\n");
            foreach (var (r, x) in partial) some.Append($"| [{x.Name}](/guruhlar/{x.GroupId}) | {r.FullName} | {(r.PayType == EmployeePayType.SalaryOnly ? "faqat oylik" : $"{x.SharePercent:0.##}%")} | {UzbekText.Money(x.MonthShare)} |\n");
            return some.ToString();
        }

        var all = new StringBuilder($"#### Ustozlar ulushi — {UzbekText.MonthName(month)}\n\n| Ustoz | Sxema | Odatiy foiz | Guruhlar | Ulush |\n|---|---|---|---|---|\n");
        foreach (var r in rows)
        {
            var groups = r.Groups.Count == 0 ? "—" : string.Join(", ", r.Groups.Select(x => $"{x.Name} ({x.SharePercent:0.##}%)"));
            all.Append($"| {r.FullName} | {PayType(r.PayType)} | {(r.PayType == EmployeePayType.SalaryOnly ? "—" : $"{r.DefaultSharePercent:0.##}%")} | {groups} | {UzbekText.Money(r.Groups.Sum(x => x.MonthShare))} |\n");
        }
        all.Append($"\nJami: **{UzbekText.Money(rows.Sum(r => r.Groups.Sum(x => x.MonthShare)))}**. Boshqarish: [Ustozlar ulushi](/ustoz-ulushi)");
        return all.ToString();
    }

    private async Task<string> StartShareChangeAsync(string text, decimal percent)
    {
        if (!await CanSeeSharesAsync()) return SharesClosed;
        var rows = await center.GetTeacherSharesAsync(Today);
        var (teacher, group) = MatchShareTarget(rows, text);
        if (group is { } g)
        {
            return await StartAsync(new Draft("share") { Slots = { ["target"] = "group", ["target_id"] = g.GroupId, ["target_name"] = g.Name, ["percent"] = percent } });
        }
        if (teacher is { } t)
        {
            var onlyNew = UzbekText.Has(UzbekText.Key(text), "faqat yangi", "yangi guruhlar");
            return await StartAsync(new Draft("share")
            {
                Slots = { ["target"] = "teacher", ["target_id"] = t.EmployeeId, ["target_name"] = t.FullName, ["percent"] = percent, ["apply_to_groups"] = !onlyNew }
            });
        }
        return "Qaysi ustoz yoki guruh? Masalan: _«Malika Rahimova foizi 40% bo‘lsin»_ yoki _«IELTS Foundation guruhida ulush 35%»_.";
    }

    // Matndan ustoz yoki guruhni topadi: guruh nomi to'liq mos kelsa guruh, aks holda ism bo'yicha ustoz.
    private static (TeacherShareRow? Teacher, TeacherShareGroup? Group) MatchShareTarget(IReadOnlyList<TeacherShareRow> rows, string text)
    {
        var tokens = UzbekText.Tokens(text);
        var groups = rows.SelectMany(r => r.Groups)
            .Select(g => (Group: g, Tokens: UzbekText.Tokens(g.Name).Where(x => x.Length >= 2).ToArray()))
            .Where(g => g.Tokens.Length > 0 && g.Tokens.All(x => tokens.Contains(x)))
            .OrderByDescending(g => g.Tokens.Length)
            .ToList();
        if (groups.Count > 0 && (groups.Count == 1 || groups[0].Tokens.Length > groups[1].Tokens.Length)) return (null, groups[0].Group);

        var teachers = rows
            .Select(r => (Row: r, Hits: UzbekText.Tokens(r.FullName).Count(part => part.Length >= 3 && tokens.Any(x => x.StartsWith(part[..Math.Min(part.Length, 5)], StringComparison.Ordinal)))))
            .Where(r => r.Hits > 0)
            .OrderByDescending(r => r.Hits)
            .ToList();
        if (teachers.Count == 1 || (teachers.Count > 1 && teachers[0].Hits > teachers[1].Hits)) return (teachers[0].Row, null);
        return (null, null);
    }

    private static string PayType(EmployeePayType type) => type switch
    {
        EmployeePayType.SalaryOnly => "faqat oylik",
        EmployeePayType.ShareOnly => "faqat foiz",
        _ => "oylik + foiz"
    };

    private static string PayScheme(TeacherShareRow teacher) => teacher.PayType switch
    {
        EmployeePayType.SalaryOnly => $"faqat oylik ({UzbekText.Money(teacher.AgreedMonthlySalary)}), foiz olmaydi.",
        EmployeePayType.ShareOnly => $"faqat foiz, odatiy foizi **{teacher.DefaultSharePercent:0.##}%**.",
        _ => $"oylik {UzbekText.Money(teacher.AgreedMonthlySalary)} + foiz, odatiy foizi **{teacher.DefaultSharePercent:0.##}%**."
    };

    // ---------- Yangi o'quvchilar, kunlik lidlar, ustozlar reytingi ----------

    private async Task<string> NewStudentsAsync(DateOnly? month)
    {
        var from = month ?? new DateOnly(Today.Year, Today.Month, 1);
        var to = from.AddMonths(1);
        var students = (await center.GetStudentsAsync())
            .Where(s => s.JoinedOn >= from && s.JoinedOn < to && s.Status != StudentStatus.Archived)
            .OrderByDescending(s => s.JoinedOn)
            .ToList();
        steps.Add(new AssistantStep("list_students", false));
        if (students.Count == 0) return $"{UzbekText.MonthName(from)} oyida yangi o‘quvchi qo‘shilmagan.";
        var text = new StringBuilder($"{UzbekText.MonthName(from)}: **{students.Count} ta yangi o‘quvchi**\n\n| O‘quvchi | Qo‘shilgan | Holat |\n|---|---|---|\n");
        foreach (var s in students.Take(15)) text.Append($"| [{s.FullName}](/oquvchilar/{s.Id}) | {s.JoinedOn:dd.MM} | {StatusLabel(s.Status)} |\n");
        if (students.Count > 15) text.Append($"\n…yana {students.Count - 15} ta. [Hammasi](/oquvchilar)");
        return text.ToString();
    }

    private async Task<string> LeadsOnDayAsync(DateOnly day)
    {
        var leads = (await sales.GetLeadsAsync(includeClosed: true))
            .Where(l => DateOnly.FromDateTime(l.CreatedAtUtc.ToLocalTime()) == day)
            .ToList();
        steps.Add(new AssistantStep("list_leads", false));
        var label = day == Today ? "Bugun" : day == Today.AddDays(-1) ? "Kecha" : day.ToString("dd.MM.yyyy");
        if (leads.Count == 0) return $"{label} yangi lid tushmagan. [Lidlar doskasi](/sotuv)";
        var text = new StringBuilder($"{label} **{leads.Count} ta yangi lid**\n\n| Ism | Telefon | Manba | Qiziqishi |\n|---|---|---|---|\n");
        foreach (var l in leads.Take(15)) text.Append($"| {l.FullName} | {l.Phone} | {Lead.SourceLabel(l.Source)} | {Dash(l.CourseInterest)} |\n");
        var bySource = leads.GroupBy(l => l.Source).OrderByDescending(x => x.Count()).Select(x => $"{Lead.SourceLabel(x.Key)} {x.Count()}");
        text.Append($"\nManbalar: {string.Join(", ", bySource)}. [Lidlar doskasi](/sotuv)");
        return text.ToString();
    }

    private async Task<string> TeachersRankAsync()
    {
        var groups = (await center.GetStudyGroupsAsync()).Where(g => !string.IsNullOrWhiteSpace(g.TeacherName)).ToList();
        steps.Add(new AssistantStep("list_groups", false));
        if (groups.Count == 0) return "Guruhlarga hali ustoz biriktirilmagan.";
        var rank = groups.GroupBy(g => g.TeacherName)
            .Select(x => (Name: x.Key, Groups: x.Count(), Students: x.Sum(g => g.StudentCount), Seats: x.Sum(g => g.Capacity)))
            .OrderByDescending(x => x.Students)
            .ToList();
        var text = new StringBuilder($"Eng ko‘p o‘quvchi — **{rank[0].Name}** ({rank[0].Students} nafar).\n\n| Ustoz | Guruh | O‘quvchi | To‘lganlik |\n|---|---|---|---|\n");
        foreach (var r in rank) text.Append($"| {r.Name} | {r.Groups} | {r.Students} | {(r.Seats == 0 ? 0 : r.Students * 100 / r.Seats)}% |\n");
        return text.ToString();
    }

    // ---------- Kurs bo'yicha jadval va bo'sh joylar (sotuv uchun) ----------

    private static string? MatchCourse(IReadOnlyList<StudyGroupSummary> groups, IReadOnlyList<string> tokens)
    {
        var courses = groups.Select(g => g.CourseName).Distinct().ToList();
        var full = courses
            .Select(c => (Course: c, Parts: UzbekText.Tokens(c).Where(p => p.Length >= 3).ToArray()))
            .Where(c => c.Parts.Length > 0 && c.Parts.All(p => UzbekText.Matches(tokens, p)))
            .OrderByDescending(c => c.Parts.Length)
            .ToList();
        if (full.Count > 0) return full[0].Course;

        // Kurs nomidagi o'ziga xos so'z ("Python", "IELTS") — faqat bitta kursda uchrasa, shu kurs ("tili" kabi umumiy so'zlar o'tmaydi).
        var byWord = courses
            .Where(c => UzbekText.Tokens(c).Any(word => word.Length >= 4 && tokens.Contains(word) &&
                courses.Count(other => UzbekText.Tokens(other).Contains(word)) == 1))
            .ToList();
        return byWord.Count == 1 ? byWord[0] : null;
    }

    private async Task<string?> CourseScheduleAsync(IReadOnlyList<string> tokens, bool onlyFree)
    {
        var groups = await center.GetStudyGroupsAsync();
        if (MatchCourse(groups, tokens) is not { } course) return null;
        var list = groups.Where(g => g.CourseName == course).OrderBy(g => g.StartTime).ToList();
        if (onlyFree) list = list.Where(g => g.Capacity > g.StudentCount).ToList();
        steps.Add(new AssistantStep("list_groups", false));
        if (list.Count == 0) return $"**{course}** bo‘yicha {(onlyFree ? "bo‘sh joyli guruh yo‘q" : "faol guruh yo‘q")}. Mijozni lid sifatida yozib qo‘ying — yangi guruh ochilganda bog‘lanasiz.";
        var free = list.Sum(g => Math.Max(0, g.Capacity - g.StudentCount));
        var text = new StringBuilder($"**{course}** — {list.Count} ta guruh, jami **{free} ta bo‘sh joy**.\n\n| Guruh | Kunlar | Vaqt | Ustoz | Narx | Bo‘sh joy |\n|---|---|---|---|---|---|\n");
        foreach (var g in list)
        {
            var seats = g.Capacity - g.StudentCount;
            text.Append($"| [{g.Name}](/guruhlar/{g.Id}) | {Days(g.DaysOfWeek)} | {g.StartTime} | {Dash(g.TeacherName)} | {UzbekText.Money(g.MonthlyFee)} | {(seats > 0 ? $"{seats} ta" : "to‘lgan")} |\n");
        }
        text.Append("\n[Dars jadvali va bo‘sh joylar](/dars-jadvali?sotuv=1)");
        return text.ToString();
    }
}
