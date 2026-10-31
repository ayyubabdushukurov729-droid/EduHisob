namespace EduHisob.Data;

// Yangi rollar faqat oxiriga qo'shiladi: bazada rol raqam sifatida saqlanadi.
public enum CenterRole
{
    Owner,
    Admin,
    Accountant,
    Cashier,
    Teacher,
    SalesManager,
    Manager,
    Receptionist,
    Marketer,
    HrManager
}

public static class CenterRoles
{
    public const string Owner = nameof(CenterRole.Owner);
    public const string Admin = nameof(CenterRole.Admin);
    public const string Accountant = nameof(CenterRole.Accountant);
    public const string Cashier = nameof(CenterRole.Cashier);
    public const string Teacher = nameof(CenterRole.Teacher);
    public const string SalesManager = nameof(CenterRole.SalesManager);
    public const string Manager = nameof(CenterRole.Manager);
    public const string Receptionist = nameof(CenterRole.Receptionist);
    public const string Marketer = nameof(CenterRole.Marketer);
    public const string HrManager = nameof(CenterRole.HrManager);

    // [Authorize(Roles = ...)] uchun tayyor ro'yxatlar. CenterAccess dagi massivlar bilan bir xil bo'lishi shart.
    public const string Management = Owner + "," + Admin;
    // Administrator moliya hisobotlari, ish haqi va ustoz ulushini ko'rmaydi, lekin to'lov qabul qiladi.
    public const string Finance = Owner + "," + Accountant;
    public const string Hr = Finance + "," + HrManager;
    public const string Cash = Finance + "," + Admin + "," + Cashier + "," + Receptionist;
    public const string StudentDesk = Cash + "," + SalesManager + "," + Manager;
    public const string Teaching = Owner + "," + Admin + "," + Teacher + "," + Manager;
    public const string Sales = Owner + "," + Admin + "," + SalesManager + "," + Manager + "," + Receptionist + "," + Marketer;

    public static string Label(CenterRole role) => role switch
    {
        CenterRole.Owner => "Asoschi",
        CenterRole.Admin => "Administrator",
        CenterRole.Accountant => "Buxgalter",
        CenterRole.Cashier => "Kassir",
        CenterRole.Teacher => "Ustoz",
        CenterRole.SalesManager => "Sotuv menejeri",
        CenterRole.Manager => "Menejer",
        CenterRole.Receptionist => "Qabulxona",
        CenterRole.Marketer => "Marketolog",
        CenterRole.HrManager => "Kadrlar bo‘limi",
        _ => role.ToString()
    };

    public static string Description(CenterRole role) => role switch
    {
        CenterRole.Owner => "Barcha bo‘limlar va foydalanuvchilarni boshqarish",
        CenterRole.Admin => "O‘quvchilar, guruhlar, dars jadvali, davomat, lidlar, filial va xonalar, to‘lov qabul qilish va kassa. Moliyaviy hisobotlar, ish haqi, xodimlar maoshi va ustoz ulushi ko‘rinmaydi",
        CenterRole.Accountant => "Moliya, ish haqi, hisobotlar, qarzdorlar va kassa",
        CenterRole.Cashier => "O‘quvchilar, to‘lov qabul qilish, qarzdorlar va kassa",
        CenterRole.Teacher => "Dars jadvali va davomat",
        CenterRole.SalesManager => "Lidlar, sotuv va yangi o‘quvchilar",
        CenterRole.Manager => "O‘quvchilar, guruhlar, davomat va lidlar — pul bo‘limlarisiz",
        CenterRole.Receptionist => "O‘quvchi va lid qabul qilish, to‘lov olish, qarzdorlar va kassa",
        CenterRole.Marketer => "Faqat lidlar va sotuv voronkasi",
        CenterRole.HrManager => "Xodimlar va ish haqi",
        _ => ""
    };
}
