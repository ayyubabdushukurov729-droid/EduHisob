using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EduHisob.Data.Assistant;

// O'zbekcha erkin matndan ma'no ajratish: kirill → lotin, apostroflarni birxillashtirish, summa, telefon,
// foiz, oy va to'lov usulini topish. Hech qanday tashqi xizmat ishlatilmaydi.
public static partial class UzbekText
{
    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo", ['ж'] = "j",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "x", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sh", ['ъ'] = "'", ['ы'] = "i", ['ь'] = "", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya", ['ў'] = "o'", ['қ'] = "q", ['ғ'] = "g'", ['ҳ'] = "h"
    };

    public static readonly CultureInfo Uz = CreateCulture();

    private static CultureInfo CreateCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberGroupSeparator = " ";
        return culture;
    }

    // Kichik harf, lotin yozuvi, barcha apostroflar ' ko'rinishida. Ism va matnni ko'rsatish uchun.
    public static string Latin(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var raw in text.Trim().ToLowerInvariant())
        {
            if (Cyrillic.TryGetValue(raw, out var latin)) builder.Append(latin);
            else if (raw is '‘' or '’' or 'ʻ' or 'ʼ' or '`' or '´') builder.Append('\'');
            else builder.Append(raw);
        }
        return builder.ToString();
    }

    // Solishtirish uchun kalit: apostrof va tinish belgilarisiz. "O‘tgan oy" → "otgan oy".
    public static string Key(string text) => Spaces().Replace(NonWord().Replace(Latin(text).Replace("'", ""), " "), " ").Trim();

    public static string[] Tokens(string text) => Key(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Kalit so'z (bir yoki bir necha so'z) matnda bormi: so'z boshi mos kelsa yoki bitta harf xatosi bo'lsa ham topiladi.
    // "qarzdorlr" → "qarzdor", "hisobt" → "hisobot".
    public static bool Matches(IReadOnlyList<string> tokens, string keyword) =>
        keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => tokens.Any(token => WordMatches(token, word)));

    public static bool Fuzzy(IReadOnlyList<string> tokens, params string[] keywords) => keywords.Any(keyword => Matches(tokens, keyword));

    private static bool WordMatches(string token, string word)
    {
        if (token == word || (word.Length >= 3 && token.StartsWith(word, StringComparison.Ordinal))) return true;
        if (word.Length < 5 || token.Length < word.Length - 1) return false;
        var head = token[..Math.Min(token.Length, word.Length)];
        return Distance(head, word) <= 1 || (token.Length > word.Length && Distance(token[..(word.Length + 1)], word) <= 1);
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    public static bool Has(string key, params string[] words) => words.Any(word => (" " + key + " ").Contains(" " + word, StringComparison.Ordinal));

    // "Aliyevdan", "guruhiga" → "aliyev", "guruh". Ism va guruh nomlarini topishda ishlatiladi.
    public static string Stem(string token)
    {
        string[] suffixes = ["larning", "larni", "larga", "lardan", "ning", "dagi", "idan", "iga", "ini", "dan", "ga", "ka", "qa", "ni", "da", "ga", "ning", "lar"];
        foreach (var suffix in suffixes)
        {
            if (token.Length - suffix.Length >= 3 && token.EndsWith(suffix, StringComparison.Ordinal))
            {
                return token[..^suffix.Length];
            }
        }
        return token;
    }

    public static string? Phone(string text) =>
        PhoneRegex().Match(Latin(text)) is { Success: true } match ? TeamAccessService.PhoneLogin(match.Value) is { } digits ? "+" + digits : null : null;

    public static string WithoutPhone(string text) => PhoneRegex().Replace(Latin(text), " ");

    public static decimal? Percent(string text) =>
        PercentRegex().Match(Latin(text)) is { Success: true } match ? decimal.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) : null;

    // "500 ming", "1,5 mln", "1 200 000", "800k" → so'm. Telefon va foizlar oldindan olib tashlanadi.
    public static decimal? Amount(string text)
    {
        var clean = PercentRegex().Replace(WithoutPhone(text), " ");
        decimal? best = null;
        foreach (Match match in AmountRegex().Matches(clean))
        {
            var number = decimal.Parse(match.Groups[1].Value.Replace(" ", "").Replace(',', '.'), CultureInfo.InvariantCulture);
            var multiplier = match.Groups[2].Value switch
            {
                "ming" or "k" => 1_000m,
                "mln" or "million" or "mil" => 1_000_000m,
                "mlrd" or "milliard" => 1_000_000_000m,
                _ => 1m
            };
            var amount = number * multiplier;
            if (amount >= 1_000 && (best is null || amount > best)) best = amount;
        }
        return best;
    }

    public static PaymentMethod? Method(string key) =>
        Has(key, "naqd") ? PaymentMethod.Cash
        : Has(key, "karta", "plastik", "click", "payme", "uzcard", "humo", "kartadan", "kartaga") ? PaymentMethod.Card
        : Has(key, "terminal") ? PaymentMethod.Terminal
        : Has(key, "otkazma", "bank", "perechisl", "hisob raqam") ? PaymentMethod.BankTransfer
        : null;

    public static string MethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Naqd",
        PaymentMethod.Card => "Karta",
        PaymentMethod.Terminal => "Terminal",
        _ => "Bank o‘tkazmasi"
    };

    private static readonly string[] MonthNames = ["yanvar", "fevral", "mart", "aprel", "may", "iyun", "iyul", "avgust", "sentabr", "oktabr", "noyabr", "dekabr"];

    public static string MonthName(DateOnly month) => $"{char.ToUpperInvariant(MonthNames[month.Month - 1][0])}{MonthNames[month.Month - 1][1..]} {month.Year}";

    // "o'tgan oy", "bu oy", "sentabr", "2026-09", "o'tgan yil dekabr". Topilmasa null.
    public static DateOnly? Month(string text, DateOnly today)
    {
        var key = Key(text);
        var current = new DateOnly(today.Year, today.Month, 1);
        if (IsoMonth().Match(key) is { Success: true } iso)
        {
            return new DateOnly(int.Parse(iso.Groups[1].Value), int.Parse(iso.Groups[2].Value), 1);
        }
        if (Has(key, "otgan oydan oldingi", "oldingi oydan oldin")) return current.AddMonths(-2);
        if (Has(key, "otgan oy", "oldingi oy", "otkan oy")) return current.AddMonths(-1);
        if (Has(key, "bu oy", "shu oy", "joriy oy", "hozirgi oy")) return current;

        var tokens = key.Replace("tyabr", "tabr").Split(' ');
        for (var i = 0; i < MonthNames.Length; i++)
        {
            var name = MonthNames[i];
            if (!tokens.Any(token => token == name || (token.StartsWith(name, StringComparison.Ordinal) && token.Length <= name.Length + 4) || (name.Length > 5 && token.StartsWith(name[..5], StringComparison.Ordinal))))
            {
                continue;
            }

            var year = YearRegex().Match(key) is { Success: true } y ? int.Parse(y.Value) : today.Year;
            var month = new DateOnly(year, i + 1, 1);
            // Yil aytilmagan bo'lsa, kelajakdagi emas, oxirgi o'tgan shu oy olinadi.
            return !YearRegex().IsMatch(key) && month > current ? month.AddYears(-1) : month;
        }
        return null;
    }

    public static string Money(decimal amount) => $"{amount.ToString("#,0", Uz)} so‘m";

    public static string TitleCase(string text) => string.Join(' ', Latin(text)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    [GeneratedRegex(@"(?:\+?998[\s-]?)?\(?\d{2}\)?[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2}(?!\d)")] private static partial Regex PhoneRegex();
    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(?:%|foiz|протсент|prosent)")] private static partial Regex PercentRegex();
    [GeneratedRegex(@"(\d{1,3}(?:\s\d{3})+|\d+(?:[.,]\d+)?)\s*(mlrd|milliard|mln|million|mil|ming|k)?(?![a-z])")] private static partial Regex AmountRegex();
    [GeneratedRegex(@"(20\d{2})[-./ ](0?[1-9]|1[0-2])\b")] private static partial Regex IsoMonth();
    [GeneratedRegex(@"\b20\d{2}\b")] private static partial Regex YearRegex();
    [GeneratedRegex(@"[^\p{L}\p{N}%+]+")] private static partial Regex NonWord();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
