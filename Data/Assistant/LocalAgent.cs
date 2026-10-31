using System.Text;
using System.Text.Json;

namespace EduHisob.Data.Assistant;

public sealed record AssistantStep(string ToolName, bool IsError);

public sealed record AssistantReply(string Text, IReadOnlyList<AssistantStep> Steps);

// O'zimizning yordamchi: tashqi AI xizmatisiz, o'zbekcha buyruqlarni qoidalar asosida tushunadi.
// Yetishmagan ma'lumotni so'raydi, yozadigan har bir amalni tasdiqlatadi va AssistantTools orqali bajaradi —
// shuning uchun rol ruxsatlari va tekshiruvlar sayt sahifalaridagidek ishlaydi.
public sealed partial class LocalAgent(
    AssistantTools tools,
    CenterDataService center,
    StudentAccountService accounts,
    CashDeskService cash,
    SalesService sales,
    CenterAccess access)
{
    private Draft? draft;
    private List<AssistantStep> steps = [];

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    // Ism qidirishda e'tiborga olinmaydigan so'zlar (kalit ko'rinishida, apostrofsiz).
    private static readonly HashSet<string> StopWords =
    [
        "tolov", "tolovi", "tolovni", "tolovini", "toladi", "tolash", "tolasin", "qabul", "qil", "qiling", "qilib", "qildi", "ol", "oling", "olindi", "olib",
        "naqd", "naqdda", "karta", "kartadan", "kartaga", "plastik", "terminal", "terminaldan", "otkazma", "bank", "click", "payme",
        "ming", "mln", "million", "mlrd", "som", "sum", "sumlik", "somlik", "uchun", "oylik", "oyi", "oyga", "bu", "shu", "otgan",
        "yangi", "oquvchi", "oquvchini", "oquvchiga", "talaba", "qosh", "qoshing", "qoshib", "qoshish", "qoshgin", "yoz", "yozing", "yozib",
        "guruh", "guruhga", "guruhiga", "guruhi", "telefon", "tel", "raqam", "raqami", "nomer", "ismi", "ism", "familiya", "va", "bilan",
        "xarajat", "xarajati", "xodim", "xodimni", "ustoz", "ustozni", "oqituvchi", "lid", "lidni", "iltimos", "ber", "bering", "kerak",
        "sinov", "sinovga", "sinovda", "faol", "foiz", "maosh", "oylikka", "haqida", "malumot", "profil", "profili", "balans", "balansi",
        "qancha", "qarzi", "qarz", "korsat", "kim", "nima", "ha", "yoq", "dan", "ga", "ni", "pul", "puli", "summa", "lavozim", "lavozimi",
        "manba", "instagram", "telegram", "facebook", "sayt", "tanish", "qongiroq", "kurs", "kursi", "kursiga", "qiziqadi", "dars", "darsi",
        "nechta", "qaysi", "qayer", "qayerda", "darslar", "darslari", "beradi", "bugun", "ertaga", "kimlar", "telefoni", "bor", "qanday", "uning", "unga",
        "guruhlar", "guruhlari", "guruhda", "guruhlarda", "oquvchilar", "oquvchilari", "ustozlar", "ustozi", "xodimlar", "royxat", "royxati", "haqidagi",
        "malumoti", "korsating", "ayting", "aytchi", "chi", "endi", "hozir", "qilyapti", "oqiydi", "oqiyapti", "boladi", "kerakmi", "bormi", "nomi"
    ];

    public void Reset()
    {
        draft = null;
    }

    public async Task<AssistantReply> SendAsync(string text)
    {
        steps = [];
        string reply;
        try
        {
            reply = await HandleAsync(text.Trim());
        }
        catch (UnauthorizedAccessException)
        {
            draft = null;
            reply = "Rolingiz bu ma’lumotni ko‘rishga yoki bu amalni bajarishga ruxsat bermaydi.";
        }
        catch (InvalidOperationException exception)
        {
            draft = null;
            reply = exception.Message;
        }
        return new AssistantReply(reply, steps);
    }

    private async Task<string> HandleAsync(string text)
    {
        var key = UzbekText.Key(text);
        if (key.Length == 0) return Help();

        if (draft is not null)
        {
            if (IsNo(key))
            {
                draft = null;
                return "Bekor qilindi. Yana nima qilamiz?";
            }
            if (draft.AwaitingConfirm)
            {
                if (IsYes(key)) return await ExecuteAsync();
                draft = null;
            }
            else if (draft.Choices is { } choices)
            {
                var picked = Pick(choices, text);
                if (picked is not null)
                {
                    draft.Slots[draft.ChoiceSlot!] = picked.Value.Id;
                    draft.Slots[draft.ChoiceSlot + "_name"] = picked.Value.Name;
                    draft.Choices = null;
                    return await AdvanceAsync();
                }
                draft = null;
            }
            else if (draft.Asking is { } asking && FillAnswer(asking, text))
            {
                return await AdvanceAsync();
            }
            else
            {
                draft = null;
            }
        }

        return await RouteAsync(text, key);
    }

    // ---------- Niyatni aniqlash ----------

    // Suhbat konteksti: "o'tgan oychi?" yoki "uning qarzi?" kabi davom savollari uchun.
    private string? lastTopic;
    private int? lastStudentId;

    private static readonly string[] HowToWords =
        ["qanday", "qanaqa", "qayerda", "qayerdan", "qilsam", "qilaman", "qilinadi", "qilish kerak", "nima degani", "nima bu", "nimaga", "nega", "tushuntir", "orgat", "mumkinmi", "boladimi", "bilmayman", "qila olaman", "qanaqasiga"];

    private async Task<string> RouteAsync(string text, string key)
    {
        var tokens = UzbekText.Tokens(text);
        var amount = UzbekText.Amount(text);
        var isQuestion = UzbekText.Has(key, "nechta", "qancha", "royxat", "korsat", "qaysi", "kimlar", "tushdi", "bormi") || text.Contains('?');
        var howTo = UzbekText.Fuzzy(tokens, HowToWords);

        // 1. Salomlashish va suhbat.
        if (tokens.Length <= 3 && UzbekText.Fuzzy(tokens, "salom", "assalom", "hayrli", "privet")) return "Assalomu alaykum! Markazingiz bo‘yicha nima kerak? Hisobot, qarzdorlar, o‘quvchi, to‘lov yoki sayt qanday ishlashi haqida so‘rang.";
        if (tokens.Length <= 4 && UzbekText.Fuzzy(tokens, "rahmat", "raxmat", "tashakkur", "spasibo", "barakalla")) return "Arzimaydi! Yana nima yordam kerak bo‘lsa, yozing.";
        if (tokens.Length <= 3 && UzbekText.Fuzzy(tokens, "yordam", "help", "buyruq", "menyu")) return Help();
        if (tokens.Length <= 4 && UzbekText.Fuzzy(tokens, "kimsan", "kimsiz", "sen kim", "siz kim", "isming", "ismingiz")) return HelpKnowledge.Topics[^1].Answer;
        // Rollar: "administrator nima qila oladi?", "menga nima ruxsat?"
        if (UzbekText.Fuzzy(tokens, RoleQuestionWords) && !UzbekText.Fuzzy(tokens, "olasan", "qilasan"))
        {
            if (UzbekText.Fuzzy(tokens, "menga", "mening", "rolim", "olaman", "ruxsatim", "huquqim")) return await MyRoleAsync();
            if (RoleMentioned(tokens) is { } role) return RoleInfo(role);
        }
        if (UzbekText.Fuzzy(tokens, "nima qila olasan", "nima qilasan", "nimalar qila", "imkoniyat")) return Help();
        // Mavzudan tashqari savollar: sayt bilan bog'liq so'zlar bo'lsa ham ("dollar kursi") javob bermaymiz.
        if (UzbekText.Fuzzy(tokens, OffTopicWords)) return OffTopic;

        // 2. Davom savoli: faqat oy aytilgan bo'lsa ("o'tgan oychi?", "sentabr-chi") — oldingi mavzu shu oy uchun.
        if (lastTopic is not null && UzbekText.Month(text, Today) is { } followMonth && tokens.All(IsMonthWord))
        {
            return await RunTopicAsync(lastTopic, followMonth);
        }

        // "Lidlar qayerdan kelyapti?" — bu ma'lumot savoli, yo'riqnoma emas.
        if (UzbekText.Fuzzy(tokens, "lid", "lidlar", "mijoz") && UzbekText.Fuzzy(tokens, "qayerdan", "manba", "konversiya", "samara"))
        {
            return await FunnelAsync(UzbekText.Month(text, Today));
        }

        // 3. "Qanday qilaman?" — sayt bo'yicha bilim bazasi.
        if (howTo && HelpKnowledge.Best(text) is { } help)
        {
            return help.Topic.Answer;
        }

        // Ustozlar ulushi (faqat moliya): "Malika foizi necha?", "IELTS guruhida ulush 35% bo'lsin".
        if (UzbekText.Fuzzy(tokens, "ulush", "ulushi", "foiz", "foizi", "foizini", "protsent") &&
            !UzbekText.Fuzzy(tokens, "chegirma", "skidka", "davomat", "toldi", "tolgan"))
        {
            var percent = UzbekText.Percent(text);
            var change = percent is not null && UzbekText.Fuzzy(tokens, "qil", "qoy", "belgila", "ozgartir", "bolsin", "oshir", "kamaytir", "qilib");
            if (change) return await StartShareChangeAsync(text, percent!.Value);
            if (UzbekText.Fuzzy(tokens, "ulush", "ulushi", "ustoz", "ustozlar", "oqituvchi") || await CanSeeSharesAsync()) return await SharesAsync(text);
        }

        // "Bugun nechta lid keldi?" — "keldi" ketganlar bilan adashmasin.
        if (UzbekText.Fuzzy(tokens, "lid", "lidlar") && UzbekText.Has(key, "bugun", "kecha") && !UzbekText.Has(key, "qosh", "yoz"))
        {
            return await LeadsOnDayAsync(UzbekText.Has(key, "kecha") ? Today.AddDays(-1) : Today);
        }

        // "Yangi o'quvchilar bu oy", "sentabrda nechta yangi o'quvchi" — ro'yxat, qo'shish emas.
        if (UzbekText.Has(key, "yangi") && (UzbekText.Has(key, "oquvchilar", "talabalar") ||
            (UzbekText.Has(key, "oquvchi") && (isQuestion || UzbekText.Month(text, Today) is not null || UzbekText.Has(key, "bu oy", "shu oy")))))
        {
            return await NewStudentsAsync(UzbekText.Month(text, Today));
        }

        // "Qaysi ustozda eng ko'p o'quvchi?"
        if (UzbekText.Fuzzy(tokens, "ustoz", "ustozlar", "oqituvchi") && UzbekText.Fuzzy(tokens, "eng kop", "reyting", "kimda kop", "kopro", "eng yaxshi", "top"))
        {
            return await TeachersRankAsync();
        }

        // Sotuv uchun: "ingliz tili qachon?", "Python'ga bo'sh joy bormi?"
        if (UzbekText.Fuzzy(tokens, "qachon", "qaysi kun", "kunlari", "vaqt", "soat", "jadval", "bosh joy", "joy bor", "bormi", "yozilsa", "yozilmoqchi", "guruhlari", "narxi") &&
            await CourseScheduleAsync(tokens, UzbekText.Fuzzy(tokens, "bosh joy", "joy bor", "joy qolgan", "yozilsa", "yozilmoqchi")) is { } courseAnswer)
        {
            return courseAnswer;
        }

        // 4. Yozish amallari (har doim tasdiq bilan).
        if (UzbekText.Has(key, "bugun", "kecha") && UzbekText.Has(key, "tolov", "tushum", "tushdi", "pul", "kirim"))
        {
            return await DayPaymentsAsync(UzbekText.Has(key, "kecha") ? Today.AddDays(-1) : Today);
        }
        if (amount is not null && !isQuestion && UzbekText.Has(key, "tolov", "toladi", "tolash", "pul ol", "pul qabul", "tolov qabul"))
        {
            var payment = new Draft("payment") { Slots = { ["amount"] = amount, ["method"] = UzbekText.Method(key) }, NameText = text };
            if (lastStudentId is int pronounStudent && UzbekText.Has(key, "unga", "undan", "uning", "shu oquvchi"))
            {
                payment.Slots["student_id"] = pronounStudent;
                payment.Slots["student_id_name"] = (await accounts.GetStudentProfileAsync(pronounStudent)).Student.FullName;
            }
            return await StartAsync(payment);
        }
        if (UzbekText.Has(key, "xarajat") && (amount is not null || UzbekText.Has(key, "yoz", "qosh", "kirit")) && !isQuestion && !UzbekText.Has(key, "hisobot"))
        {
            return await StartAsync(new Draft("expense") { Slots = { ["amount"] = amount, ["method"] = UzbekText.Method(key), ["category"] = DetectCategory(key), ["note"] = text } });
        }
        if (UzbekText.Has(key, "oquvchi", "talaba") && UzbekText.Has(key, "qosh", "yangi", "yoz", "royxatga ol") && !isQuestion)
        {
            return await StartStudentAsync(text, key);
        }
        if (UzbekText.Has(key, "xodim", "ustoz", "oqituvchi") && UzbekText.Has(key, "qosh", "yangi", "ishga ol") && !isQuestion)
        {
            return await StartEmployeeAsync(text, key);
        }
        if (UzbekText.Has(key, "lid") && UzbekText.Has(key, "qosh", "yangi", "yoz") && !isQuestion)
        {
            return await StartLeadAsync(text, key);
        }

        // 5. "Uning qarzi?", "uning telefoni?" — oxirgi gaplashilgan o'quvchi.
        if (lastStudentId is int lastStudent && UzbekText.Has(key, "uning", "unga", "uni", "u qancha", "shu oquvchi"))
        {
            return await StudentInfoAsync(lastStudent);
        }

        // 6. Aniq ism: ustoz/xodim, so'ng o'quvchi.
        // Ustoz ismi: "ustoz", "dars beradi", "guruhlari" so'zlari bo'lsa yoki shu ismli o'quvchi bo'lmasa.
        var aboutTeaching = UzbekText.Fuzzy(tokens, "ustoz", "oqituvchi", "xodim", "dars beradi", "guruhlari", "guruhlarda", "qaysi guruh");
        var (students, exactStudent) = await FindStudentsAsync(text);
        if ((aboutTeaching || !exactStudent) && await TeacherInfoAsync(text) is { } teacherAnswer) return teacherAnswer;
        if (exactStudent && students.Count == 1) return await StudentInfoAsync(students[0].Id);
        if (exactStudent && students.Count > 1) return "Bir nechta o‘quvchi topildi:\n" + string.Join("\n", students.Take(8).Select(s => $"- [{s.Name}](/oquvchilar/{s.Id})"));

        // 7. Ma'lumot savollari.
        var month = UzbekText.Month(text, Today);
        if (UzbekText.Fuzzy(tokens, "solishtir", "taqqosla", "nisbatan", "farqi", "osdimi", "osdik", "kamaydimi", "osish", "dinamika"))
        {
            // "O'tgan oyga nisbatan" — joriy oy o'tgan oy bilan solishtiriladi, o'tgan oy o'zidan oldingisi bilan emas.
            var current = new DateOnly(Today.Year, Today.Month, 1);
            var compareMonth = month is { } m && !(m == current.AddMonths(-1) && UzbekText.Has(key, "otgan oyga", "oldingi oyga")) ? m : current;
            return await RunTopicAsync("compare", compareMonth);
        }
        if (UzbekText.Fuzzy(tokens, "tugilgan kun", "tugilgan", "birthday")) return await BirthdaysAsync(onlyToday: UzbekText.Fuzzy(tokens, "bugun"));
        if (UzbekText.Fuzzy(tokens, "jadval", "raspisaniye") || (UzbekText.Fuzzy(tokens, "dars", "darslar") && UzbekText.Fuzzy(tokens, "bugun", "ertaga", "indinga", "dushanba", "seshanba", "chorshanba", "payshanba", "juma", "shanba", "yakshanba")))
        {
            return await ScheduleAsync(ScheduleDay(tokens));
        }
        if (UzbekText.Fuzzy(tokens, "davomat", "qatnash", "kelmagan", "kelmadi", "yoqlama")) return await AttendanceAsync(bestFirst: UzbekText.Fuzzy(tokens, "eng yaxshi", "yuqori", "eng kop", "yaxshi"));
        if (UzbekText.Fuzzy(tokens, "kassa", "naqd qoldiq", "mendagi pul", "qolimdagi")) return await CashAsync();
        if (UzbekText.Fuzzy(tokens, "hisobot", "tushum", "foyda", "daromad", "natija", "zarar", "xarajat", "kirim", "chiqim")) return await RunTopicAsync("report", month);
        if (UzbekText.Fuzzy(tokens, "ish haqi", "ishhaqi", "maosh", "oylik", "zarplata")) return await RunTopicAsync("payroll", month);
        if (UzbekText.Fuzzy(tokens, "muzlatil", "muzlat")) return await StatusListAsync(StudentStatus.Frozen);
        if (UzbekText.Fuzzy(tokens, "sinovda", "sinovdagi", "sinov")) return await StatusListAsync(StudentStatus.Trial);
        if (UzbekText.Fuzzy(tokens, "ketgan", "ketdi", "ketishdi", "ketish", "chiqib ket", "tark") && !UzbekText.Has(key, "keldi", "kelgan", "kelyapti", "kelishdi")) return await RunTopicAsync("left", month);
        if (UzbekText.Fuzzy(tokens, "qarz", "qarzdor")) return await DebtorsAsync();
        if (UzbekText.Fuzzy(tokens, "bosh joy", "joy bor", "joy qolgan", "bosh orin")) return await FreeSeatsAsync();
        if (UzbekText.Fuzzy(tokens, "lid", "lidlar", "sotuv", "voronka", "konversiya", "reklama"))
        {
            return UzbekText.Fuzzy(tokens, "manba", "qayerdan", "voronka", "konversiya", "statistika", "reklama", "samara") ? await FunnelAsync(month) : await LeadsAsync();
        }
        if (await GroupInfoAsync(text) is { } groupAnswer) return groupAnswer;
        if (UzbekText.Fuzzy(tokens, "nechta", "qancha", "soni", "jami") && await CountAsync(tokens) is { } count) return count;
        if (UzbekText.Fuzzy(tokens, "guruh", "guruhlar")) return await GroupsAsync();
        if (UzbekText.Fuzzy(tokens, "kurs", "kurslar")) return await CoursesAsync();
        if (UzbekText.Fuzzy(tokens, "filial", "xona", "xonalar", "auditoriya", "manzil")) return await BranchesAsync();
        if (UzbekText.Fuzzy(tokens, "xodim", "ustoz", "oqituvchi", "jamoa")) return await EmployeesAsync();
        if (UzbekText.Fuzzy(tokens, "rolim", "men kim", "ruxsatim", "nimalarni kor", "huquqim")) return await MyRoleAsync();
        if (UzbekText.Fuzzy(tokens, "statistika", "umumiy", "korsatkich", "holat", "dashboard", "ahvol", "vaziyat")) return await OverviewAsync();
        if (UzbekText.Fuzzy(tokens, "sana", "bugun nechanchi", "qaysi kun", "soat")) return $"Bugun {Today:dd.MM.yyyy}, soat {DateTime.Now:HH:mm}.";

        // 8. Bilim bazasi ("qanday" so'zisiz ham), keyin qidiruv.
        if (HelpKnowledge.Best(text) is { Score: >= 2 } knowledge) return knowledge.Topic.Answer;
        if (students.Count > 0 && !exactStudent)
        {
            return "Aynan shu nomli o‘quvchi topilmadi. Ehtimol, shulardan biri:\n" + string.Join("\n", students.Take(6).Select(s => $"- [{s.Name}](/oquvchilar/{s.Id})"));
        }
        if (await SearchHitsAsync(text) is { } hits) return hits;
        if (HelpKnowledge.Best(text) is { } weak) return weak.Topic.Answer;

        return OffTopic;
    }

    private const string OffTopic = """
        Men faqat **EduHisob va markazingiz** bo‘yicha javob beraman — bu savolni tushunmadim yoki u sayt mavzusiga kirmaydi.
        Masalan, so‘rang: _o‘tgan oy hisoboti_, _qarzdorlar_, _bugungi darslar_, _qanday chegirma beraman?_ yoki **yordam** deb yozing.
        """;

    // Markazga aloqasi yo'q mavzular. "kurs" yoki "pul" kabi so'zlar bilan adashmaslik uchun oldindan tekshiriladi.
    private static readonly string[] OffTopicWords =
    [
        "dollar", "valyuta", "evro", "rubl", "ob havo", "obhavo", "pogoda", "futbol", "match", "real madrid", "barselona", "siyosat",
        "prezident", "saylov", "kino", "film", "serial", "musiqa", "qoshiq", "retsept", "taom", "ovqat pishir", "taksi", "yandex", "yandeks",
        "bitcoin", "kripto", "aksiya narx", "anekdot", "hazil", "sher yoz", "namoz vaqt", "olimpiada", "uy vazifa"
    ];

    // "bu oychi", "o'tgan oy-chi", "sentabr 2026" kabi faqat oyni bildiruvchi so'zlar.
    private static bool IsMonthWord(string token) =>
        token is "bu" or "shu" or "oy" or "oychi" or "oyi" or "otgan" or "oldingi" or "joriy" or "hozirgi" or "chi" or "va" or "ham" or "endi" or "qani" or "uchun"
        || token.All(char.IsDigit)
        || UzbekText.Month(token, Today) is not null;

    private Task<string> RunTopicAsync(string topic, DateOnly? month)
    {
        var current = new DateOnly(Today.Year, Today.Month, 1);
        lastTopic = topic;
        return topic switch
        {
            "compare" => CompareAsync(month ?? current),
            "payroll" => PayrollAsync(month ?? current),
            "left" => LeftAsync(month),
            _ => MonthReportAsync(month ?? current)
        };
    }

    private static bool IsYes(string key) =>
        key is "ha" or "xa" or "ha ha" or "albatta" or "mayli" or "ok" or "okey" or "tasdiq" or "tasdiqlayman" or "togri" or "yes" or "da" or "baribir" or "baribir qosh"
        || key.StartsWith("ha ", StringComparison.Ordinal) || key.StartsWith("tasdiq", StringComparison.Ordinal) || key.StartsWith("baribir", StringComparison.Ordinal);

    private static bool IsNo(string key) =>
        key is "yoq" or "yo q" or "kerak emas" or "bekor" or "bekor qil" or "no" or "net" or "otmen" or "toxta"
        || key.StartsWith("bekor", StringComparison.Ordinal) || key.StartsWith("yoq ", StringComparison.Ordinal);

    private static string Help() => """
        Men EduHisob ichidagi yordamchiman — faqat markazingiz va sayt bo‘yicha javob beraman. Masalan:

        **Raqamlar:** _o‘tgan oy hisoboti_, _bu oy o‘tgan oyga nisbatan_, _qarzdorlar_, _bu oy ish haqi_, _bugun to‘lovlar_, _kassada qancha pul_, _nechta faol o‘quvchi bor_
        **Ta’lim:** _bugungi darslar_, _IELTS guruhi_, _Malika ustoz guruhlari_, _bo‘sh joy qaysi guruhlarda_, _davomat_, _muzlatilganlar_, _bu oy ketganlar_, _tug‘ilgan kunlar_
        **Sotuv:** _lidlar_, _lidlar qayerdan kelyapti_
        **O‘quvchi:** ismini yozing — balans, guruh, oxirgi to‘lov; keyin _uning qarzi?_ yoki _unga 300 ming to‘lov_
        **Amallar:** _yangi o‘quvchi Ali Valiyev 90 123 45 67 Ingliz A1 guruhiga_, _Ali Valiyevdan 500 ming naqd to‘lov_, _xarajat ijara 3 mln karta_, _yangi ustoz Aziza Karimova 90 111 22 33 oylik 2 mln 30%_, _yangi lid Sardor 93 555 44 33 instagram_
        **Sayt haqida:** _qanday chegirma beraman?_, _muzlatish nima?_, _kassa qanday ishlaydi?_, _ustoz foizi qanday hisoblanadi?_

        Yozadigan har bir amalni bajarishdan oldin tasdiqlatib olaman.
        """;

    // ---------- Yozish amallari: qadamma-qadam ----------

    private sealed class Draft(string kind)
    {
        public string Kind { get; } = kind;
        public Dictionary<string, object?> Slots { get; } = [];
        public string? NameText { get; set; }
        public string? Asking { get; set; }
        public List<(int Id, string Name)>? Choices { get; set; }
        public string? ChoiceSlot { get; set; }
        public bool AwaitingConfirm { get; set; }

        public object? this[string slot] => Slots.GetValueOrDefault(slot);
        public bool Missing(string slot) => this[slot] is null or "";
    }

    private Task<string> StartAsync(Draft newDraft)
    {
        draft = newDraft;
        return AdvanceAsync();
    }

    private async Task<string> StartStudentAsync(string text, string key)
    {
        var group = await MatchGroupAsync(text);
        var name = ExtractName(text, group?.Tokens ?? []);
        var newDraft = new Draft("student")
        {
            Slots =
            {
                ["full_name"] = name,
                ["phone"] = UzbekText.Phone(text),
                ["status"] = UzbekText.Has(key, "sinov") ? "Trial" : "Active",
                ["group_id"] = group?.Id,
                ["group_id_name"] = group?.Name
            }
        };
        return await StartAsync(newDraft);
    }

    private Task<string> StartEmployeeAsync(string text, string key)
    {
        var isTeacher = UzbekText.Has(key, "ustoz", "oqituvchi");
        var percent = UzbekText.Percent(text);
        var salary = UzbekText.Amount(text);
        return StartAsync(new Draft("employee")
        {
            Slots =
            {
                ["full_name"] = ExtractName(text, []),
                ["phone"] = UzbekText.Phone(text),
                ["kind"] = isTeacher ? "Teacher" : "Staff",
                ["position"] = isTeacher ? "Ustoz" : "Xodim",
                ["salary"] = salary,
                ["share_percent"] = percent
            }
        });
    }

    private async Task<string> StartLeadAsync(string text, string key)
    {
        var courses = await center.GetCoursesAsync();
        var course = courses.FirstOrDefault(c => UzbekText.Tokens(c.Name).Any(token => token.Length >= 4 && UzbekText.Has(key, token)));
        var source = UzbekText.Has(key, "instagram", "insta") ? "Instagram"
            : UzbekText.Has(key, "telegram") ? "Telegram"
            : UzbekText.Has(key, "facebook") ? "Facebook"
            : UzbekText.Has(key, "sayt", "website") ? "Website"
            : UzbekText.Has(key, "qongiroq", "zvonok") ? "PhoneCall"
            : UzbekText.Has(key, "tanish", "tavsiya", "referal") ? "Referral"
            : UzbekText.Has(key, "ozi keldi", "kelib", "ofisga") ? "WalkIn"
            : null;
        return await StartAsync(new Draft("lead")
        {
            Slots =
            {
                ["full_name"] = ExtractName(text, course is null ? [] : UzbekText.Tokens(course.Name)),
                ["phone"] = UzbekText.Phone(text),
                ["source"] = source,
                ["course_interest"] = course?.Name ?? ""
            }
        });
    }

    // Javobni kutilayotgan maydonga yozadi. Mos kelmasa false — unda xabar yangi buyruq sifatida o'qiladi.
    private bool FillAnswer(string slot, string text)
    {
        var key = UzbekText.Key(text);
        switch (slot)
        {
            case "full_name":
                if (UzbekText.Phone(text) is { } phoneToo) draft!.Slots["phone"] = phoneToo;
                var name = ExtractName(text, []);
                if (name is null) return false;
                draft!.Slots["full_name"] = name;
                return true;
            case "phone":
                if (UzbekText.Phone(text) is not { } phone) return false;
                draft!.Slots["phone"] = phone;
                return true;
            case "amount":
            case "salary":
                if (UzbekText.Percent(text) is { } percent) draft!.Slots["share_percent"] = percent;
                if (UzbekText.Amount(text) is { } amount) draft!.Slots[slot] = amount;
                return draft!.Slots.GetValueOrDefault(slot) is not null || draft.Slots.GetValueOrDefault("share_percent") is not null;
            case "student":
                draft!.NameText = text;
                return true;
            case "category":
                draft!.Slots["category"] = DetectCategory(key) ?? UzbekText.TitleCase(text);
                return true;
            case "source":
                draft!.Slots["source"] = "Other";
                return true;
            default:
                return false;
        }
    }

    private async Task<string> AdvanceAsync()
    {
        var d = draft!;
        switch (d.Kind)
        {
            case "student":
                if (d.Missing("full_name")) return Ask("full_name", "O‘quvchining ism-familiyasini yozing.");
                if (d.Missing("phone")) return Ask("phone", $"**{d["full_name"]}** — telefon raqamini yozing.");
                return Confirm($"""
                    Yangi o‘quvchi qo‘shaman:
                    - **Ism:** {d["full_name"]}
                    - **Telefon:** {d["phone"]}
                    - **Holat:** {(Equals(d["status"], "Trial") ? "Sinovda" : "Faol")}
                    - **Guruh:** {d["group_id_name"] ?? "— (keyin yozish mumkin)"}
                    """);

            case "payment":
                if (d.Missing("student_id"))
                {
                    var (found, exactName) = await FindStudentsAsync(d.NameText ?? "");
                    if (exactName && found.Count == 1)
                    {
                        d.Slots["student_id"] = found[0].Id;
                        d.Slots["student_id_name"] = found[0].Name;
                    }
                    else if (found.Count > 0)
                    {
                        return Choose("student_id", exactName ? "Qaysi o‘quvchi? Raqamini yozing:" : "Aynan shu ismli o‘quvchi topilmadi. Shulardan biri bo‘lsa, raqamini yozing (yoki «bekor»):", found);
                    }
                    else
                    {
                        return Ask("student", "Qaysi o‘quvchidan to‘lov? Ismini yozing.");
                    }
                }
                if (d.Missing("amount")) return Ask("amount", "Qancha summa? Masalan: 500 ming.");
                if (d.Missing("group_id") && !d.Slots.ContainsKey("group_checked"))
                {
                    d.Slots["group_checked"] = true;
                    var profile = await accounts.GetStudentProfileAsync((int)d["student_id"]!);
                    var groups = profile.Groups.Where(g => g.LeftOn is null).ToList();
                    if (groups.Count == 1)
                    {
                        d.Slots["group_id"] = groups[0].GroupId;
                        d.Slots["group_id_name"] = groups[0].GroupName;
                    }
                    else if (groups.Count > 1)
                    {
                        return Choose("group_id", "O‘quvchi bir nechta guruhda. Qaysi guruh uchun?", groups.Select(g => (g.GroupId, g.GroupName)).ToList());
                    }
                    d.Slots["balance"] = profile.Balance;
                }
                var method = (PaymentMethod?)d["method"] ?? PaymentMethod.Cash;
                d.Slots["method"] = method;
                return Confirm($"""
                    To‘lov qabul qilaman:
                    - **O‘quvchi:** {d["student_id_name"]}
                    - **Summa:** {UzbekText.Money((decimal)d["amount"]!)}
                    - **Usul:** {UzbekText.MethodName(method)}
                    - **Guruh:** {d["group_id_name"] ?? "—"}
                    - **Sana:** {Today:dd.MM.yyyy}
                    """);

            case "expense":
                if (d.Missing("amount")) return Ask("amount", "Xarajat summasi qancha?");
                if (d.Missing("category")) return Ask("category", "Qaysi turkum? Masalan: Ijara, Kommunal xizmatlar, Marketing va reklama, Ta’mirlash.");
                var expenseMethod = (PaymentMethod?)d["method"] ?? PaymentMethod.Cash;
                d.Slots["method"] = expenseMethod;
                return Confirm($"""
                    Xarajat yozaman:
                    - **Turkum:** {d["category"]}
                    - **Summa:** {UzbekText.Money((decimal)d["amount"]!)}
                    - **Usul:** {UzbekText.MethodName(expenseMethod)}
                    - **Izoh:** {d["note"]}
                    """);

            case "employee":
                if (d.Missing("full_name")) return Ask("full_name", "Xodimning ism-familiyasini yozing.");
                if (d.Missing("phone")) return Ask("phone", $"**{d["full_name"]}** — telefon raqamini yozing.");
                var teacher = Equals(d["kind"], "Teacher");
                if (d.Missing("salary") && d.Missing("share_percent"))
                {
                    return Ask("salary", teacher ? "Haq qanday bo‘ladi? Masalan: «oylik 2 mln», «30%» yoki «2 mln va 30%»." : "Kelishilgan oylik qancha?");
                }
                var payType = !teacher || d.Missing("share_percent") ? "SalaryOnly" : d.Missing("salary") ? "ShareOnly" : "SalaryAndShare";
                d.Slots["pay_type"] = payType;
                var pay = payType switch
                {
                    "ShareOnly" => $"faqat foiz — {d["share_percent"]}%",
                    "SalaryAndShare" => $"oylik {UzbekText.Money((decimal)d["salary"]!)} + {d["share_percent"]}%",
                    _ => $"oylik {UzbekText.Money((decimal)d["salary"]!)}"
                };
                return Confirm($"""
                    Yangi {(teacher ? "ustoz" : "xodim")} qo‘shaman:
                    - **Ism:** {d["full_name"]}
                    - **Telefon:** {d["phone"]}
                    - **Haq:** {pay}
                    """);

            case "share":
                return Confirm(Equals(d["target"], "group")
                    ? $"**{d["target_name"]}** guruhida ustoz ulushini **{d["percent"]}%** qilaman."
                    : $"**{d["target_name"]}** — odatiy foizini **{d["percent"]}%** qilaman{(Equals(d["apply_to_groups"], true) ? " va barcha faol guruhlariga ham qo‘llayman" : " (faqat yangi guruhlarga qo‘yiladi)")}.");

            case "lead":
                if (d.Missing("full_name")) return Ask("full_name", "Lidning ismini yozing.");
                if (d.Missing("phone")) return Ask("phone", $"**{d["full_name"]}** — telefon raqamini yozing.");
                d.Slots["source"] ??= "Other";
                return Confirm($"""
                    Yangi lid qo‘shaman:
                    - **Ism:** {d["full_name"]}
                    - **Telefon:** {d["phone"]}
                    - **Manba:** {Lead.SourceLabel(Enum.Parse<LeadSource>((string)d["source"]!))}
                    - **Qiziqishi:** {(d["course_interest"] is string { Length: > 0 } course ? course : "—")}
                    """);
        }
        draft = null;
        return Help();
    }

    private string Ask(string slot, string question)
    {
        draft!.Asking = slot;
        return question + "\n\n_Bekor qilish uchun «bekor» deb yozing._";
    }

    private string Choose(string slot, string question, IReadOnlyList<(int Id, string Name)> options)
    {
        draft!.Choices = options.Take(9).ToList();
        draft.ChoiceSlot = slot;
        draft.Asking = null;
        return question + "\n" + string.Join("\n", draft.Choices.Select((option, index) => $"{index + 1}. {option.Name}"));
    }

    private string Confirm(string summary)
    {
        draft!.AwaitingConfirm = true;
        draft.Asking = null;
        return summary + "\n\n**Tasdiqlaysizmi?** (ha / yo‘q)";
    }

    private static (int Id, string Name)? Pick(List<(int Id, string Name)> choices, string text)
    {
        var key = UzbekText.Key(text);
        if (int.TryParse(key, out var number) && number >= 1 && number <= choices.Count) return choices[number - 1];
        var matches = choices.Where(choice => UzbekText.Tokens(text).All(token => UzbekText.Key(choice.Name).Contains(token, StringComparison.Ordinal))).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private async Task<string> ExecuteAsync()
    {
        var d = draft!;
        draft = null;
        var (tool, args) = d.Kind switch
        {
            "student" => ("add_student", new Dictionary<string, object?>
            {
                ["full_name"] = d["full_name"], ["phone"] = d["phone"], ["status"] = d["status"],
                ["group_id"] = d["group_id"], ["allow_duplicate"] = d["allow_duplicate"]
            }),
            "payment" => ("accept_payment", new Dictionary<string, object?>
            {
                ["student_id"] = d["student_id"], ["amount"] = d["amount"], ["method"] = d["method"]?.ToString(), ["group_id"] = d["group_id"]
            }),
            "expense" => ("add_expense", new Dictionary<string, object?>
            {
                ["category"] = d["category"], ["amount"] = d["amount"], ["method"] = d["method"]?.ToString(), ["note"] = d["note"]
            }),
            "employee" => ("add_employee", new Dictionary<string, object?>
            {
                ["full_name"] = d["full_name"], ["phone"] = d["phone"], ["position"] = d["position"], ["kind"] = d["kind"],
                ["pay_type"] = d["pay_type"], ["salary"] = d["salary"], ["share_percent"] = d["share_percent"]
            }),
            "lead" => ("add_lead", new Dictionary<string, object?>
            {
                ["full_name"] = d["full_name"], ["phone"] = d["phone"], ["source"] = d["source"], ["course_interest"] = d["course_interest"]
            }),
            "share" => (Equals(d["target"], "group") ? "set_group_share" : "set_teacher_share", new Dictionary<string, object?>
            {
                [Equals(d["target"], "group") ? "group_id" : "employee_id"] = d["target_id"], ["percent"] = d["percent"], ["apply_to_groups"] = d["apply_to_groups"]
            }),
            _ => throw new InvalidOperationException("Noma’lum amal.")
        };

        var input = args.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value));
        var (result, isError) = await tools.RunAsync(tool, input);
        steps.Add(new AssistantStep(tool, isError));
        if (isError) return "Bajarilmadi: " + result;

        using var json = JsonDocument.Parse(result);
        var root = json.RootElement;
        if (root.TryGetProperty("duplicates", out var duplicates))
        {
            d.Slots["allow_duplicate"] = true;
            draft = d;
            d.AwaitingConfirm = true;
            var list = string.Join("\n", duplicates.EnumerateArray().Take(5).Select(item => $"- {Describe(item)}"));
            return $"Shunga o‘xshash o‘quvchi(lar) bazada bor:\n{list}\n\nBaribir yangi o‘quvchi sifatida qo‘shaymi? (ha / yo‘q)";
        }

        return d.Kind switch
        {
            "student" => $"Qo‘shildi ✓ **{d["full_name"]}**{(d["group_id_name"] is string g ? $", {g} guruhiga yozildi" : "")}. [Profilni ochish]({Str(root, "link")})",
            "payment" => $"To‘lov qabul qilindi ✓ {UzbekText.Money((decimal)d["amount"]!)} — {d["student_id_name"]}. Yangi balans: **{UzbekText.Money(root.GetProperty("newBalance").GetDecimal())}**.\n\n[Chekni ochish]({Str(root, "receipt")}) · [Profil]({Str(root, "student")})",
            "expense" => $"Xarajat yozildi ✓ {d["category"]} — {UzbekText.Money((decimal)d["amount"]!)}.",
            "employee" => $"Qo‘shildi ✓ **{d["full_name"]}**. [Xodimlar ro‘yxati](/xodimlar)",
            "lead" => $"Lid qo‘shildi ✓ **{d["full_name"]}**. [Lidlar](/sotuv)",
            "share" => $"Saqlandi ✓ {d["target_name"]}: ustoz ulushi **{d["percent"]}%**. [Ustozlar ulushi](/ustoz-ulushi)",
            _ => "Bajarildi ✓"
        };
    }

    private static string Str(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private static string Describe(JsonElement item)
    {
        var parts = item.EnumerateObject()
            .Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number && !p.Name.EndsWith("Id", StringComparison.Ordinal))
            .Select(p => p.Value.ToString())
            .Where(v => v.Length > 0);
        return string.Join(" · ", parts);
    }

    // ---------- Ism, guruh, turkumni topish ----------

    private static string? ExtractName(string text, IReadOnlyCollection<string> exclude)
    {
        var words = UzbekText.WithoutPhone(text)
            .Split([' ', ',', '.', ';', ':', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word =>
            {
                var key = UzbekText.Key(word);
                return key.Length >= 2 && !key.Any(char.IsDigit) && !StopWords.Contains(key) && !StopWords.Contains(UzbekText.Stem(key))
                    && !exclude.Contains(key) && !exclude.Contains(UzbekText.Stem(key)) && UzbekText.Method(key) is null
                    && UzbekText.Month(word, Today) is null;
            })
            .Take(3)
            .ToList();
        if (words.Count == 0) return null;

        // Oxirgi so'zdagi "-dan", "-ga" kabi qo'shimchalar ismga kirmaydi: "Valiyevdan" → "Valiyev".
        words[^1] = UzbekText.Stem(UzbekText.Latin(words[^1]));
        return UzbekText.TitleCase(string.Join(' ', words));
    }

    private sealed record GroupMatch(int Id, string Name, string[] Tokens);

    private async Task<GroupMatch?> MatchGroupAsync(string text)
    {
        var tokens = UzbekText.Tokens(text).Select(UzbekText.Stem).ToHashSet();
        var groups = await center.GetStudyGroupsAsync();
        var scored = groups
            .Select(group =>
            {
                var groupTokens = UzbekText.Tokens(group.Name);
                var hits = groupTokens.Count(gt => tokens.Any(t => t == gt || (t.Length >= 4 && gt.StartsWith(t, StringComparison.Ordinal)) || (gt.Length >= 4 && t.StartsWith(gt, StringComparison.Ordinal))));
                return (Group: group, Hits: hits, Share: groupTokens.Length == 0 ? 0 : (double)hits / groupTokens.Length, Tokens: groupTokens);
            })
            .Where(item => item.Hits > 0 && item.Share >= 0.5)
            .OrderByDescending(item => item.Hits).ThenByDescending(item => item.Share)
            .ToList();
        if (scored.Count == 0 || (scored.Count > 1 && scored[0].Hits == scored[1].Hits && scored[0].Share == scored[1].Share)) return null;
        return new GroupMatch(scored[0].Group.Id, scored[0].Group.Name, scored[0].Tokens);
    }

    // Exact: so'rovdagi barcha so'zlar (yoki telefon) mos kelgan. Aks holda eng yaqinlari qaytadi.
    private async Task<(List<(int Id, string Name)> Matches, bool Exact)> FindStudentsAsync(string text)
    {
        var queryTokens = NameTokens(text);
        var phone = UzbekText.Phone(text);
        if (queryTokens.Count == 0 && phone is null) return ([], false);

        var students = await center.GetStudentsAsync();
        var scored = students
            .Where(student => student.Status != StudentStatus.Archived)
            .Select(student =>
            {
                var nameTokens = UzbekText.Tokens(student.FullName);
                var score = queryTokens.Count(q => nameTokens.Any(n => n.StartsWith(q, StringComparison.Ordinal)));
                if (phone is not null && StudentAccountService.DigitsOnly(student.Phone).EndsWith(phone[^9..], StringComparison.Ordinal)) score += 5;
                return (student.Id, student.FullName, student.Phone, Score: score);
            })
            .Where(item => item.Score > 0)
            .ToList();
        if (scored.Count == 0) return ([], false);
        var top = scored.Max(item => item.Score);
        var best = scored.Where(item => item.Score == top).OrderBy(item => item.FullName).Take(9).ToList();
        // Bir xil ismlilarni telefon bilan ajratamiz.
        var matches = best.Select(item => (item.Id, best.Count(other => other.FullName == item.FullName) > 1 ? $"{item.FullName} · {item.Phone}" : item.FullName)).ToList();
        return (matches, top >= queryTokens.Count || top >= 5);
    }

    private static string? DetectCategory(string key)
    {
        (string Word, string Category)[] map =
        [
            ("ijara", "Ijara"), ("arenda", "Ijara"), ("kommunal", "Kommunal xizmatlar"), ("svet", "Kommunal xizmatlar"), ("elektr", "Kommunal xizmatlar"),
            ("gaz", "Kommunal xizmatlar"), ("suv", "Kommunal xizmatlar"), ("internet", "Internet va aloqa"), ("aloqa", "Internet va aloqa"),
            ("reklama", "Marketing va reklama"), ("marketing", "Marketing va reklama"), ("target", "Marketing va reklama"),
            ("darslik", "Darslik va o‘quv materiallari"), ("kitob", "Darslik va o‘quv materiallari"), ("jihoz", "Ofis jihozlari"),
            ("mebel", "Ofis jihozlari"), ("kompyuter", "Ofis jihozlari"), ("xojalik", "Xo‘jalik xarajatlari"), ("tozalik", "Xo‘jalik xarajatlari"),
            ("tamir", "Ta’mirlash"), ("remont", "Ta’mirlash"), ("soliq", "Soliqlar"), ("bank", "Bank xizmatlari"), ("transport", "Transport"),
            ("taksi", "Transport"), ("benzin", "Transport"), ("investor", "Investorga to‘lov")
        ];
        return map.FirstOrDefault(item => UzbekText.Has(key, item.Word)).Category;
    }

    // ---------- O'qish: hisobot va ro'yxatlar ----------

    private async Task<string> MonthReportAsync(DateOnly month)
    {
        var input = new Dictionary<string, JsonElement> { ["month"] = JsonSerializer.SerializeToElement(month.ToString("yyyy-MM")) };
        var (result, isError) = await tools.RunAsync("get_month_report", input);
        steps.Add(new AssistantStep("get_month_report", isError));
        if (isError) return result;

        using var json = JsonDocument.Parse(result);
        var r = json.RootElement;
        var text = new StringBuilder($"#### {UzbekText.MonthName(month)} hisoboti\n");
        if (r.TryGetProperty("income", out var income))
        {
            var resultValue = r.GetProperty("result").GetDecimal();
            text.Append($"""

                | Ko‘rsatkich | Summa |
                |---|---|
                | Tushum ({r.GetProperty("incomePaymentsCount").GetInt32()} ta to‘lov) | {UzbekText.Money(income.GetDecimal())} |
                | Xarajat | {UzbekText.Money(r.GetProperty("expense").GetDecimal())} |
                | **Natija** | **{(resultValue < 0 ? "−" : "+")}{UzbekText.Money(Math.Abs(resultValue))}** |

                """);
            var methods = r.GetProperty("incomeByMethod").EnumerateObject().Select(p => $"{UzbekText.MethodName(Enum.Parse<PaymentMethod>(p.Name))} {UzbekText.Money(p.Value.GetDecimal())}").ToList();
            if (methods.Count > 0) text.Append($"\n**Tushum usullari:** {string.Join(", ", methods)}\n");
            var categories = r.GetProperty("expenseByCategory").EnumerateObject().Take(6).ToList();
            if (categories.Count > 0)
            {
                text.Append("\n**Asosiy xarajatlar:**\n");
                foreach (var category in categories) text.Append($"- {category.Name}: {UzbekText.Money(category.Value.GetDecimal())}\n");
            }
        }
        else if (r.TryGetProperty("finance", out var finance))
        {
            text.Append($"\n_{finance.GetString()}_\n");
        }

        if (r.TryGetProperty("payroll", out var payroll))
        {
            text.Append($"\n**Ish haqi:** hisoblangan {UzbekText.Money(payroll.GetProperty("accrued").GetDecimal())} (shundan ustoz ulushi {UzbekText.Money(payroll.GetProperty("teacherShares").GetDecimal())}), berilgan {UzbekText.Money(payroll.GetProperty("paid").GetDecimal())}, qolgan {UzbekText.Money(payroll.GetProperty("remaining").GetDecimal())}.\n");
        }
        if (r.TryGetProperty("newStudents", out var newStudents))
        {
            text.Append($"\n**O‘quvchilar:** yangi {newStudents.GetInt32()} nafar");
            if (r.TryGetProperty("leftStudents", out var left))
            {
                text.Append($", ketgan {left.GetInt32()} nafar");
                var reasons = r.GetProperty("leftByReason").EnumerateObject().Select(p => $"{p.Name} — {p.Value.GetInt32()}").ToList();
                if (reasons.Count > 0) text.Append($" ({string.Join(", ", reasons)})");
            }
            text.Append(".\n");
        }
        if (r.TryGetProperty("debtAtMonthEnd", out var debt))
        {
            text.Append($"\n**Qarz:** oy oxirida {debt.GetProperty("students").GetInt32()} nafar o‘quvchida jami {UzbekText.Money(debt.GetProperty("total").GetDecimal())}. [Qarzdorlar](/qarzdorlar)\n");
        }
        if (r.TryGetProperty("salesFunnel", out var funnel) && funnel.GetProperty("TotalLeads").GetInt32() > 0)
        {
            text.Append($"\n**Lidlar:** {funnel.GetProperty("TotalLeads").GetInt32()} ta yangi lid.\n");
        }
        text.Append("\nBatafsil: [Hisobotlar](/hisobotlar)");
        return text.ToString();
    }

    private async Task<string> OverviewAsync()
    {
        var summary = await center.GetDashboardAsync();
        steps.Add(new AssistantStep("get_overview", false));
        var text = new StringBuilder($"""
            #### {summary.CenterName}
            - O‘quvchilar: **{summary.StudentCount}** (faol {summary.ActiveStudents}, sinovda {summary.TrialStudents}, muzlatilgan {summary.FrozenStudents}, ketgan {summary.LeftStudents})
            - Bu oy qo‘shilgan: {summary.NewStudentsThisMonth}
            - Faol guruhlar: {summary.ActiveGroups}
            """);
        try
        {
            var finance = await center.GetDashboardFinanceAsync();
            text.Append($"\n- Shu oy tushum: {UzbekText.Money(finance.MonthIncome)}, xarajat: {UzbekText.Money(finance.MonthExpense)}, natija: **{UzbekText.Money(finance.MonthResult)}**");
            text.Append($"\n- Qarz: {UzbekText.Money(finance.DebtTotal)} ({finance.DebtorCount} nafar)");
        }
        catch (UnauthorizedAccessException)
        {
        }
        return text.ToString();
    }

    private async Task<string> DebtorsAsync()
    {
        var debtors = (await accounts.GetDebtorsAsync()).Where(d => d.Debt > 0 && d.Stage != DebtStage.Paid).OrderByDescending(d => d.Debt).ToList();
        steps.Add(new AssistantStep("list_debtors", false));
        if (debtors.Count == 0) return "Hozir qarzdor o‘quvchi yo‘q ✓";
        var text = new StringBuilder($"**{debtors.Count} nafar qarzdor**, jami **{UzbekText.Money(debtors.Sum(d => d.Debt))}**.\n\n| O‘quvchi | Qarz | Bosqich | Telefon |\n|---|---|---|---|\n");
        foreach (var d in debtors.Take(10))
        {
            var stage = StudentAccountService.DebtStageLabel(d.Stage) + (d.PromisedOn is { } promised ? $" ({promised:dd.MM})" : "");
            text.Append($"| [{d.StudentName}](/oquvchilar/{d.StudentId}) | {UzbekText.Money(d.Debt)} | {stage} | {d.Phone} |\n");
        }
        if (debtors.Count > 10) text.Append($"\nYana {debtors.Count - 10} nafar — [Qarzdorlar doskasi](/qarzdorlar)");
        return text.ToString();
    }

    private async Task<string> PayrollAsync(DateOnly month)
    {
        var rows = await center.GetPayrollSummaryAsync(month);
        steps.Add(new AssistantStep("get_payroll", false));
        if (rows.Count == 0) return "Faol xodimlar yo‘q.";
        var text = new StringBuilder($"#### {UzbekText.MonthName(month)} ish haqi\n\n| Xodim | Hisoblangan | Berilgan | Qolgan |\n|---|---|---|---|\n");
        foreach (var row in rows.OrderByDescending(r => r.Remaining))
        {
            var accrued = row.AgreedMonthlySalary + row.RevenueShare + row.Bonuses;
            text.Append($"| {row.FullName} | {UzbekText.Money(accrued)} | {UzbekText.Money(row.Advances + row.SalaryPayments)} | {UzbekText.Money(row.Remaining)} |\n");
        }
        text.Append($"\n**Jami qolgan:** {UzbekText.Money(rows.Sum(r => r.Remaining))}. [Ish haqi sahifasi](/ish-haqi)");
        return text.ToString();
    }

    private async Task<string> DayPaymentsAsync(DateOnly day)
    {
        var income = (await center.GetFinancialTransactionsAsync(day, day)).Where(t => t.Type == FinancialTransactionType.Income).ToList();
        steps.Add(new AssistantStep("list_transactions", false));
        var label = day == Today ? "Bugun" : "Kecha";
        if (income.Count == 0) return $"{label} to‘lov tushmagan.";
        var text = new StringBuilder($"{label} **{income.Count} ta to‘lov**, jami **{UzbekText.Money(income.Sum(t => t.Amount))}**.\n\n");
        foreach (var group in income.GroupBy(t => t.PaymentMethod)) text.Append($"- {UzbekText.MethodName(group.Key)}: {UzbekText.Money(group.Sum(t => t.Amount))}\n");
        text.Append("\n| O‘quvchi | Summa | Usul |\n|---|---|---|\n");
        foreach (var t in income.Take(15)) text.Append($"| {t.StudentName ?? t.Category} | {UzbekText.Money(t.Amount)} | {UzbekText.MethodName(t.PaymentMethod)} |\n");
        return text.ToString();
    }

    private async Task<string> FreeSeatsAsync()
    {
        var groups = (await center.GetStudyGroupsAsync()).Where(g => g.Capacity > g.StudentCount).OrderByDescending(g => g.Capacity - g.StudentCount).ToList();
        steps.Add(new AssistantStep("list_groups", false));
        if (groups.Count == 0) return "Barcha guruhlar to‘la.";
        var text = new StringBuilder("| Guruh | Bo‘sh joy | Ustoz | Vaqt |\n|---|---|---|---|\n");
        foreach (var g in groups) text.Append($"| [{g.Name}](/guruhlar/{g.Id}) | {g.Capacity - g.StudentCount} / {g.Capacity} | {g.TeacherName} | {g.StartTime} |\n");
        return text.ToString();
    }

    private async Task<string> GroupsAsync()
    {
        var groups = await center.GetStudyGroupsAsync();
        steps.Add(new AssistantStep("list_groups", false));
        if (groups.Count == 0) return "Hali guruh ochilmagan. [Guruh ochish](/guruhlar)";
        var text = new StringBuilder($"**{groups.Count} ta guruh**\n\n| Guruh | O‘quvchilar | Ustoz | Narx |\n|---|---|---|---|\n");
        foreach (var g in groups) text.Append($"| [{g.Name}](/guruhlar/{g.Id}) | {g.StudentCount} / {g.Capacity} | {g.TeacherName} | {UzbekText.Money(g.MonthlyFee)} |\n");
        return text.ToString();
    }

    private async Task<string> EmployeesAsync()
    {
        var employees = await center.GetEmployeesAsync();
        steps.Add(new AssistantStep("list_employees", false));
        var text = new StringBuilder($"**{employees.Count} nafar xodim**\n\n| Ism | Lavozim | Haq |\n|---|---|---|\n");
        foreach (var e in employees)
        {
            var pay = e.Role == EmployeeRole.Staff || e.PayType == EmployeePayType.SalaryOnly ? UzbekText.Money(e.AgreedMonthlySalary)
                : e.PayType == EmployeePayType.ShareOnly ? $"{e.DefaultSharePercent:0.##}%"
                : $"{UzbekText.Money(e.AgreedMonthlySalary)} + {e.DefaultSharePercent:0.##}%";
            text.Append($"| {e.FullName} | {e.Position} | {pay} |\n");
        }
        return text.ToString();
    }

    private async Task<string> LeadsAsync()
    {
        var (result, isError) = await tools.RunAsync("list_leads", new Dictionary<string, JsonElement>());
        steps.Add(new AssistantStep("list_leads", isError));
        if (isError) return result;
        using var json = JsonDocument.Parse(result);
        var leads = json.RootElement.EnumerateArray().ToList();
        if (leads.Count == 0) return "Ochiq lid yo‘q.";
        var text = new StringBuilder($"**{leads.Count} ta ochiq lid**\n\n| Ism | Telefon | Bosqich | Manba |\n|---|---|---|---|\n");
        foreach (var lead in leads.Take(15))
        {
            text.Append($"| {lead.GetProperty("FullName").GetString()} | {lead.GetProperty("Phone").GetString()} | {Lead.StageLabel(Enum.Parse<LeadStage>(lead.GetProperty("Stage").GetString()!))} | {Lead.SourceLabel(Enum.Parse<LeadSource>(lead.GetProperty("Source").GetString()!))} |\n");
        }
        text.Append("\n[Lidlar doskasi](/sotuv)");
        return text.ToString();
    }

    private async Task<string> StudentInfoAsync(int studentId)
    {
        var profile = await accounts.GetStudentProfileAsync(studentId);
        steps.Add(new AssistantStep("get_student", false));
        lastStudentId = studentId;
        var s = profile.Student;
        var groups = profile.Groups.Where(g => g.LeftOn is null).Select(g => $"{g.GroupName} ({UzbekText.Money(g.FeeAfterDiscount)})").ToList();
        var lastPayment = profile.Payments.OrderByDescending(p => p.Date).FirstOrDefault();
        return $"""
            #### [{s.FullName}](/oquvchilar/{s.Id})
            - Telefon: {s.Phone}{(s.ParentPhone.Length > 0 ? $", ota-ona: {s.ParentPhone}" : "")}
            - Holat: {StatusLabel(s.Status)}
            - Guruhlar: {(groups.Count > 0 ? string.Join(", ", groups) : "—")}
            - Balans: **{(profile.Balance < 0 ? $"qarz {UzbekText.Money(-profile.Balance)}" : UzbekText.Money(profile.Balance))}**
            - Oxirgi to‘lov: {(lastPayment is null ? "—" : $"{lastPayment.Date:dd.MM.yyyy}, {UzbekText.Money(lastPayment.Amount)}")}
            """;
    }

    // Saytdagi umumiy qidiruv; hech narsa topilmasa null.
    private async Task<string?> SearchHitsAsync(string text)
    {
        var (result, isError) = await tools.RunAsync("search", new Dictionary<string, JsonElement> { ["query"] = JsonSerializer.SerializeToElement(text) });
        if (isError) return null;
        using var json = JsonDocument.Parse(result);
        var hits = new[] { "Pages", "Students", "Groups", "Employees", "Leads" }
            .SelectMany(name => json.RootElement.GetProperty(name).EnumerateArray())
            .Take(8)
            .Select(hit => $"- [{hit.GetProperty("Title").GetString()}]({hit.GetProperty("Href").GetString()}) — {hit.GetProperty("Subtitle").GetString()}")
            .ToList();
        if (hits.Count == 0) return null;
        steps.Add(new AssistantStep("search", false));
        return "Shular topildi:\n" + string.Join("\n", hits);
    }

    private static string StatusLabel(StudentStatus status) => status switch
    {
        StudentStatus.Trial => "Sinovda",
        StudentStatus.Active => "Faol",
        StudentStatus.Frozen => "Muzlatilgan",
        StudentStatus.Left => "Ketgan",
        _ => "Arxivda"
    };
}
