namespace EduHisob.Data;

// Tavsiya etilgan xarajat turkumlari. Boshqa nom ham kiritsa bo'ladi, lekin unda izoh majburiy —
// "Xarajat 1", "Sh-x" kabi tushunarsiz turkumlar hisobotni ma'nosiz qiladi.
public static class ExpenseCategories
{
    public static readonly string[] Standard =
    [
        "Ijara",
        "Kommunal xizmatlar",
        "Internet va aloqa",
        "Marketing va reklama",
        "Darslik va o‘quv materiallari",
        "Ofis jihozlari",
        "Xo‘jalik xarajatlari",
        "Ta’mirlash",
        "Soliqlar",
        "Bank xizmatlari",
        "Transport",
        "Investorga to‘lov",
        "Rivojlantirish (reinvest)"
    ];

    // Ish haqi kabi tizim o'zi yaratadigan turkumlar.
    public static readonly string[] System = ["Ish haqi", "Xodim avansi", "O‘quvchi to‘lovi"];

    public static bool IsKnown(string category) =>
        Standard.Concat(System).Any(item => string.Equals(item, category.Trim(), StringComparison.OrdinalIgnoreCase));
}
