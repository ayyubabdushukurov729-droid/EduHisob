using System.Text;
using System.Text.Json;

namespace EduHisob.Data.Assistant;

// Ma'lumot savollariga javoblar: jadval, ustoz va guruh, davomat, kassa, solishtirish, sonlar va boshqalar.
public sealed partial class LocalAgent
{
    private static readonly string[] WeekdayNames = ["yakshanba", "dushanba", "seshanba", "chorshanba", "payshanba", "juma", "shanba"];

    private static DateOnly ScheduleDay(IReadOnlyList<string> tokens)
    {
        if (UzbekText.Fuzzy(tokens, "indinga")) return Today.AddDays(2);
        if (UzbekText.Fuzzy(tokens, "ertaga")) return Today.AddDays(1);
        for (var day = 0; day < WeekdayNames.Length; day++)
        {
            if (!UzbekText.Fuzzy(tokens, WeekdayNames[day])) continue;
            var offset = ((day - (int)Today.DayOfWeek) + 7) % 7;
            return Today.AddDays(offset);
        }
        return Today;
    }

    private async Task<string> ScheduleAsync(DateOnly date)
    {
        var groups = (await center.GetStudyGroupsAsync())
            .Where(g => g.DaysOfWeek.Split(',').Contains(((int)date.DayOfWeek).ToString()))
            .OrderBy(g => g.StartTime)
            .ToList();
        steps.Add(new AssistantStep("list_groups", false));
        var label = date == Today ? "Bugun" : date == Today.AddDays(1) ? "Ertaga" : $"{date:dd.MM}";
        var dayName = WeekdayNames[(int)date.DayOfWeek];
        if (groups.Count == 0) return $"{label} ({dayName}) dars yo‘q.";
        var text = new StringBuilder($"**{label} ({dayName}) — {groups.Count} ta dars**\n\n| Vaqt | Guruh | Ustoz | Xona | O‘quvchi |\n|---|---|---|---|---|\n");
        foreach (var g in groups) text.Append($"| {g.StartTime} | [{g.Name}](/guruhlar/{g.Id}) | {Dash(g.TeacherName)} | {Dash(g.RoomName)} | {g.StudentCount} |\n");
        text.Append("\n[To‘liq dars jadvali](/dars-jadvali)");
        return text.ToString();
    }

    // "Malika ustoz guruhlari", "Jasur qaysi guruhlarda dars beradi". Ustoz topilmasa null.
    private async Task<string?> TeacherInfoAsync(string text)
    {
        var query = NameTokens(text);
        if (query.Count == 0) return null;
        var groups = await center.GetStudyGroupsAsync();
        var teachers = groups
            .Where(g => !string.IsNullOrWhiteSpace(g.TeacherName))
            .GroupBy(g => g.TeacherName)
            .Where(t => query.All(q => UzbekText.Tokens(t.Key).Any(n => n.StartsWith(q, StringComparison.Ordinal))))
            .ToList();
        if (teachers.Count != 1) return null;

        var teacher = teachers[0];
        steps.Add(new AssistantStep("list_groups", false));
        var text2 = new StringBuilder($"**{teacher.Key}** — {teacher.Count()} ta guruh, {teacher.Sum(g => g.StudentCount)} nafar o‘quvchi\n\n| Guruh | Kunlar | Vaqt | O‘quvchi | Ulush |\n|---|---|---|---|---|\n");
        foreach (var g in teacher.OrderBy(g => g.StartTime))
        {
            text2.Append($"| [{g.Name}](/guruhlar/{g.Id}) | {Days(g.DaysOfWeek)} | {g.StartTime} | {g.StudentCount} / {g.Capacity} | {(g.TeacherSharePercent > 0 ? $"{g.TeacherSharePercent:0.##}%" : "—")} |\n");
        }
        return text2.ToString();
    }

    // Guruh nomi yozilgan bo'lsa — uning tarkibi, jadvali, davomati. Topilmasa null.
    private async Task<string?> GroupInfoAsync(string text)
    {
        if (await MatchGroupAsync(text) is not { } match) return null;
        var month = new DateOnly(Today.Year, Today.Month, 1);
        var group = await accounts.GetGroupProfileAsync(match.Id, month);
        steps.Add(new AssistantStep("get_group", false));
        var members = group.Members.Where(m => m.LeftOn is null).OrderBy(m => m.FullName).ToList();
        var attendance = AttendanceRate(members.SelectMany(m => m.Attendance.Values));
        var text2 = new StringBuilder($"""
            #### [{group.Name}](/guruhlar/{group.Id})
            - Kurs: {group.CourseName}, filial: {group.BranchName}
            - Ustoz: {Dash(group.TeacherName)}{(group.TeacherSharePercent > 0 ? $" ({group.TeacherSharePercent:0.##}%)" : "")}
            - Jadval: {Days(group.DaysOfWeek)}, {group.StartTime}, xona: {Dash(group.RoomName)}
            - Narx: {UzbekText.Money(group.MonthlyFee)}, o‘quvchilar: **{members.Count} / {group.Capacity}**
            - Shu oy davomati: {(attendance is null ? "belgilanmagan" : $"{attendance}%")}
            """);
        if (group.MonthIncome is { } income) text2.Append($"\n- Shu oy tushumi: {UzbekText.Money(income)}");
        if (members.Count > 0)
        {
            var showMoney = members.Any(m => m.Balance is not null);
            text2.Append(showMoney ? "\n\n| O‘quvchi | Telefon | Balans |\n|---|---|---|\n" : "\n\n| O‘quvchi | Telefon |\n|---|---|\n");
            foreach (var m in members.Take(25))
            {
                text2.Append($"| [{m.FullName}](/oquvchilar/{m.StudentId}) | {m.Phone} |");
                if (showMoney) text2.Append($" {(m.Balance < 0 ? $"qarz {UzbekText.Money(-m.Balance!.Value)}" : UzbekText.Money(m.Balance ?? 0))} |");
                text2.Append('\n');
            }
        }
        return text2.ToString();
    }

    private async Task<string> AttendanceAsync(bool bestFirst = false)
    {
        var month = new DateOnly(Today.Year, Today.Month, 1);
        var summary = await center.GetDashboardAsync();
        var rows = new List<(StudyGroupSummary Group, int? Rate)>();
        foreach (var g in await center.GetStudyGroupsAsync())
        {
            var profile = await accounts.GetGroupProfileAsync(g.Id, month);
            rows.Add((g, AttendanceRate(profile.Members.Where(m => m.LeftOn is null).SelectMany(m => m.Attendance.Values))));
        }
        steps.Add(new AssistantStep("attendance", false));
        var overall = summary.CountedLessons == 0 ? "belgilanmagan" : $"{Math.Round(100.0 * summary.AttendedLessons / summary.CountedLessons)}%";
        var text = new StringBuilder($"**Shu oy davomati: {overall}**\n\n| Guruh | Davomat | Ustoz |\n|---|---|---|\n");
        var ordered = bestFirst ? rows.OrderByDescending(r => r.Rate ?? -1) : rows.OrderBy(r => r.Rate ?? 101);
        foreach (var (g, rate) in ordered)
        {
            text.Append($"| [{g.Name}](/guruhlar/{g.Id}) | {(rate is null ? "—" : $"{rate}%")} | {Dash(g.TeacherName)} |\n");
        }
        text.Append("\nDavomat belgilash: [Davomat](/davomat)");
        return text.ToString();
    }

    private static int? AttendanceRate(IEnumerable<AttendanceStatus?> marks)
    {
        var counted = marks.Where(m => m is not null && m != AttendanceStatus.Excused).ToList();
        if (counted.Count == 0) return null;
        return (int)Math.Round(100.0 * counted.Count(m => m is AttendanceStatus.Present or AttendanceStatus.Late) / counted.Count);
    }

    private async Task<string> CashAsync()
    {
        var overview = await cash.GetOverviewAsync();
        steps.Add(new AssistantStep("cash", false));
        var text = new StringBuilder($"**Sizdagi naqd pul:** {UzbekText.Money(overview.Me.Available)}");
        var pending = overview.Transfers.Where(t => t.Status == CashTransferStatus.Pending).ToList();
        if (overview.CanSeeAll)
        {
            text.Append("\n\n| Kimda | Naqd |\n|---|---|\n");
            foreach (var holder in overview.Holders.Where(h => h.Balance != 0).OrderByDescending(h => h.Balance))
            {
                text.Append($"| {holder.Name} ({CenterRoles.Label(holder.Role)}) | {UzbekText.Money(holder.Balance)} |\n");
            }
            text.Append($"\n**Jami naqd (filiallar):** {UzbekText.Money(overview.Branches.Sum(b => b.Balance))}");
        }
        if (pending.Count > 0)
        {
            text.Append($"\n\n⏳ {pending.Count} ta topshiriq tasdiq kutyapti: " + string.Join(", ", pending.Select(t => $"{t.FromName} → {t.ToName} {UzbekText.Money(t.Amount)}")));
        }
        text.Append("\n\n[Kassa](/kassa)");
        return text.ToString();
    }

    private async Task<string> StatusListAsync(StudentStatus status)
    {
        var students = await center.GetStudentsAsync(status: status);
        steps.Add(new AssistantStep("list_students", false));
        var title = status == StudentStatus.Frozen ? "Muzlatilgan" : "Sinovdagi";
        if (students.Count == 0) return $"{title} o‘quvchi yo‘q.";
        var text = new StringBuilder($"**{title} o‘quvchilar: {students.Count} nafar**\n");
        foreach (var s in students.Take(25)) text.Append($"- [{s.FullName}](/oquvchilar/{s.Id}) — {s.Phone}\n");
        if (students.Count > 25) text.Append($"\nYana {students.Count - 25} nafar — [O‘quvchilar](/oquvchilar?status={status})");
        return text.ToString();
    }

    private async Task<string> LeftAsync(DateOnly? month)
    {
        var from = month ?? new DateOnly(Today.Year, Today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var left = await accounts.GetLeftStudentsAsync(from, to);
        steps.Add(new AssistantStep("left_students", false));
        if (left.Count == 0) return $"{UzbekText.MonthName(from)} oyida hech kim ketmagan.";
        var text = new StringBuilder($"**{UzbekText.MonthName(from)}: {left.Count} nafar ketgan**\n\nSabablar: " +
            string.Join(", ", left.GroupBy(l => l.Reason).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} — {g.Count()}")) +
            "\n\n| O‘quvchi | Guruh | Sana | Sabab |\n|---|---|---|---|\n");
        foreach (var l in left.Take(20)) text.Append($"| [{l.StudentName}](/oquvchilar/{l.StudentId}) | {l.GroupName} | {l.LeftOn:dd.MM} | {l.Reason} |\n");
        return text.ToString();
    }

    private async Task<string> FunnelAsync(DateOnly? month)
    {
        var from = month ?? new DateOnly(Today.Year, Today.Month, 1);
        var funnel = await sales.GetFunnelAsync(from, from.AddMonths(1).AddDays(-1));
        steps.Add(new AssistantStep("funnel", false));
        if (funnel.TotalLeads == 0) return $"{UzbekText.MonthName(from)} oyida lid kelmagan.";
        var converted = funnel.ByStage.GetValueOrDefault(LeadStage.Converted);
        var text = new StringBuilder($"**{UzbekText.MonthName(from)}: {funnel.TotalLeads} ta lid**, o‘quvchi bo‘lgani {converted} ta ({100 * converted / funnel.TotalLeads}%)\n\n| Manba | Lid | O‘quvchi bo‘ldi |\n|---|---|---|\n");
        foreach (var s in funnel.BySource.OrderByDescending(s => s.Leads)) text.Append($"| {Lead.SourceLabel(s.Source)} | {s.Leads} | {s.Converted} |\n");
        if (funnel.LostReasons.Count > 0) text.Append("\nYo‘qotish sabablari: " + string.Join(", ", funnel.LostReasons.Select(r => $"{r.Reason} — {r.Count}")));
        return text.ToString();
    }

    // "Nechta faol o'quvchi bor?", "nechta guruh?", "ustozlar soni". Mavzu aniqlanmasa null.
    private async Task<string?> CountAsync(IReadOnlyList<string> tokens)
    {
        if (UzbekText.Fuzzy(tokens, "oquvchi", "talaba", "bola"))
        {
            var summary = await center.GetDashboardAsync();
            steps.Add(new AssistantStep("get_overview", false));
            if (UzbekText.Fuzzy(tokens, "faol")) return $"Faol o‘quvchilar: **{summary.ActiveStudents}** nafar.";
            if (UzbekText.Fuzzy(tokens, "sinov")) return $"Sinovdagi o‘quvchilar: **{summary.TrialStudents}** nafar.";
            if (UzbekText.Fuzzy(tokens, "muzlat")) return $"Muzlatilgan o‘quvchilar: **{summary.FrozenStudents}** nafar.";
            if (UzbekText.Fuzzy(tokens, "ketgan")) return $"Ketgan o‘quvchilar: **{summary.LeftStudents}** nafar.";
            if (UzbekText.Fuzzy(tokens, "yangi", "qoshilgan")) return $"Bu oy qo‘shilgan: **{summary.NewStudentsThisMonth}** nafar.";
            return $"Jami o‘quvchilar: **{summary.StudentCount}** nafar (faol {summary.ActiveStudents}, sinovda {summary.TrialStudents}, muzlatilgan {summary.FrozenStudents}, ketgan {summary.LeftStudents}).";
        }
        if (UzbekText.Fuzzy(tokens, "guruh"))
        {
            var groups = await center.GetStudyGroupsAsync();
            return $"Faol guruhlar: **{groups.Count}** ta, ularda {groups.Sum(g => g.StudentCount)} nafar o‘quvchi (sig‘im {groups.Sum(g => g.Capacity)}).";
        }
        if (UzbekText.Fuzzy(tokens, "ustoz", "oqituvchi"))
        {
            var teachers = (await center.GetStudyGroupsAsync()).Select(g => g.TeacherName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count();
            return $"Guruhlarda dars berayotgan ustozlar: **{teachers}** nafar.";
        }
        if (UzbekText.Fuzzy(tokens, "xodim")) return $"Faol xodimlar: **{(await center.GetEmployeesAsync()).Count}** nafar.";
        if (UzbekText.Fuzzy(tokens, "filial")) return $"Faol filiallar: **{(await center.GetBranchesAsync()).Count}** ta.";
        if (UzbekText.Fuzzy(tokens, "kurs")) return $"Kurslar: **{(await center.GetCoursesAsync()).Count}** ta.";
        if (UzbekText.Fuzzy(tokens, "lid")) return $"Ochiq lidlar: **{(await sales.GetLeadsAsync(includeClosed: false)).Count}** ta.";
        return null;
    }

    private async Task<string> CoursesAsync()
    {
        var courses = await center.GetCoursesAsync();
        var groups = await center.GetStudyGroupsAsync();
        steps.Add(new AssistantStep("list_reference_data", false));
        var text = new StringBuilder($"**{courses.Count} ta kurs**\n\n| Kurs | Guruhlar | O‘quvchilar |\n|---|---|---|\n");
        foreach (var c in courses)
        {
            var own = groups.Where(g => g.CourseId == c.Id).ToList();
            text.Append($"| {c.Name} | {own.Count} | {own.Sum(g => g.StudentCount)} |\n");
        }
        return text.ToString();
    }

    private async Task<string> BranchesAsync()
    {
        var branches = await center.GetBranchesAsync();
        var rooms = await center.GetBranchRoomsAsync();
        steps.Add(new AssistantStep("list_reference_data", false));
        var text = new StringBuilder();
        foreach (var b in branches)
        {
            var own = rooms.Where(r => r.BranchId == b.Id).ToList();
            text.Append($"- **{b.Name}**{(string.IsNullOrWhiteSpace(b.Address) ? "" : $" — {b.Address}")}: {(own.Count == 0 ? "xona qo‘shilmagan" : string.Join(", ", own.Select(r => $"{r.Name} ({r.Capacity} o‘rin)")))}\n");
        }
        return text.ToString();
    }

    private async Task<string> MyRoleAsync()
    {
        var me = await access.GetAsync();
        return $"Siz **{me.DisplayName}**.\n\n" + RoleInfo(me.Role);
    }

    private async Task<string> BirthdaysAsync(bool onlyToday = false)
    {
        var students = (await center.GetStudentsAsync())
            .Where(s => s.BirthDate is { } b && b.Month == Today.Month && (!onlyToday || b.Day == Today.Day) && s.Status is not (StudentStatus.Archived or StudentStatus.Left))
            .OrderBy(s => s.BirthDate!.Value.Day)
            .ToList();
        steps.Add(new AssistantStep("list_students", false));
        if (students.Count == 0) return onlyToday ? "Bugun tug‘ilgan kuni bo‘lgan o‘quvchi yo‘q." : "Bu oy tug‘ilgan kuni bo‘ladigan o‘quvchi yo‘q (yoki tug‘ilgan sanasi kiritilmagan).";
        var text = new StringBuilder($"**Bu oy tug‘ilgan kunlar: {students.Count} nafar**\n");
        foreach (var s in students)
        {
            var birth = s.BirthDate!.Value;
            var mark = birth.Day == Today.Day ? " 🎉 **bugun**" : birth.Day < Today.Day ? " (o‘tdi)" : "";
            text.Append($"- {birth:dd.MM} — [{s.FullName}](/oquvchilar/{s.Id}), {Today.Year - birth.Year} yosh{mark}\n");
        }
        return text.ToString();
    }

    // Tanlangan oyni oldingi oy bilan solishtirish.
    private async Task<string> CompareAsync(DateOnly month)
    {
        var previous = month.AddMonths(-1);
        var (current, currentError) = await tools.RunAsync("get_month_report", MonthInput(month));
        var (before, beforeError) = await tools.RunAsync("get_month_report", MonthInput(previous));
        steps.Add(new AssistantStep("get_month_report", currentError || beforeError));
        if (currentError) return current;

        using var a = JsonDocument.Parse(current);
        using var b = JsonDocument.Parse(before);
        var text = new StringBuilder($"**{UzbekText.MonthName(month)}** va **{UzbekText.MonthName(previous)}**\n\n| Ko‘rsatkich | {UzbekText.MonthName(previous)} | {UzbekText.MonthName(month)} | O‘zgarish |\n|---|---|---|---|\n");
        void Row(string label, string name, bool money)
        {
            if (!a.RootElement.TryGetProperty(name, out var x) || !b.RootElement.TryGetProperty(name, out var y)) return;
            var now = x.GetDecimal();
            var was = y.GetDecimal();
            var change = was == 0 ? (now == 0 ? "—" : "yangi") : $"{(now >= was ? "+" : "")}{Math.Round((now - was) * 100 / Math.Abs(was))}%";
            text.Append($"| {label} | {(money ? UzbekText.Money(was) : was.ToString("0"))} | {(money ? UzbekText.Money(now) : now.ToString("0"))} | {change} |\n");
        }
        Row("Tushum", "income", true);
        Row("Xarajat", "expense", true);
        Row("Natija", "result", true);
        Row("Yangi o‘quvchilar", "newStudents", false);
        Row("Ketgan o‘quvchilar", "leftStudents", false);
        return text.ToString();
    }

    private static Dictionary<string, JsonElement> MonthInput(DateOnly month) => new() { ["month"] = JsonSerializer.SerializeToElement(month.ToString("yyyy-MM")) };

    // Ism qidirish uchun so'zlar: savol va xizmat so'zlarisiz.
    private static List<string> NameTokens(string text) => UzbekText.Tokens(UzbekText.WithoutPhone(text))
        .Where(token => token.Length >= 3 && !token.Any(char.IsDigit) && !StopWords.Contains(token))
        .Select(UzbekText.Stem)
        .Where(token => !StopWords.Contains(token))
        .Distinct()
        .ToList();

    private static string Days(string daysOfWeek) => string.Join(", ", daysOfWeek
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(d => int.TryParse(d, out var day) && day is >= 0 and <= 6 ? WeekdayNames[day][..3] : d));

    private static string Dash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
