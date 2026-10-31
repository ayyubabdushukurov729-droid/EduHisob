namespace EduHisob.Data.Assistant;

public sealed record HelpTopic(string Title, string[] Keywords, string Answer);

// Sayt qanday ishlashi haqidagi bilim bazasi: "qanday qilaman?", "nima degani?" kabi savollarga javob.
// Kalit so'zlar apostrofsiz, kichik harfda va so'z boshi ko'rinishida yoziladi ("chegirm" → chegirma, chegirmasi).
public static class HelpKnowledge
{
    public static readonly IReadOnlyList<HelpTopic> Topics =
    [
        new("O‘quvchi qo‘shish", ["oquvchi qosh", "yangi oquvchi", "oquvchi royxatga", "talaba qosh", "oquvchi kirit"], """
            **O‘quvchi qo‘shish:**
            1. [O‘quvchilar → Yangi o‘quvchi](/oquvchilar/yangi) sahifasini oching.
            2. Ism-familiya, telefon, xohlasangiz ota-ona telefoni va tug‘ilgan sanani yozing.
            3. Holatini tanlang: **Sinovda** (sinov darsi) yoki **Faol**.
            4. Saqlang — tizim shunday ism yoki telefon bazada bo‘lsa ogohlantiradi.
            5. Profilda **Guruhga yozish** bilan guruh tanlang.

            Yoki shu yerda yozing: _yangi o‘quvchi Ali Valiyev 90 123 45 67 Ingliz A1 guruhiga_.
            """),
        new("Guruhga yozish", ["guruhga yoz", "guruhga qosh", "guruhga kirit", "boshqa guruh", "guruh almashtir"], """
            **O‘quvchini guruhga yozish:** o‘quvchi profilini oching → **Guruhlar** yorlig‘i → **Guruhga yozish** → guruhni tanlang.
            Oylik to‘lov guruh narxidan hisoblanadi. Agar o‘quvchi oyning 1-sanasidan keyin qo‘shilsa, to‘lov **keyingi oydan** boshlanadi.
            Guruhni almashtirish uchun eski guruhdan chiqarib (sababi bilan), yangisiga yozing.
            """),
        new("To‘lov qabul qilish", ["tolov qabul", "tolov ol", "pul qabul", "tolov qil", "chek", "kvitansiya"], """
            **To‘lov qabul qilish:**
            1. O‘quvchi profilini oching → **To‘lovlar** yorlig‘i.
            2. Summa, usul (naqd, karta, terminal, o‘tkazma), guruh va sanani kiriting.
            3. Saqlang — **Chek** havolasi chiqadi, uni chop etish mumkin.

            Yoki shu yerda yozing: _Ali Valiyevdan 500 ming naqd to‘lov_. To‘lovni kassir, administrator, buxgalter va qabulxona qabul qiladi.
            """),
        new("Balans qanday hisoblanadi", ["balans", "balans hisob", "oylik tolov hisob", "manfiy", "minus"], """
            **Balans = to‘langan pul − hisoblangan oylik to‘lovlar.**
            - Har oy boshida o‘quvchining har bir guruhi uchun oylik to‘lov avtomatik hisoblanadi (chegirma hisobga olinadi).
            - Muzlatilgan oylarga to‘lov hisoblanmaydi.
            - Balans **minus** bo‘lsa — o‘quvchi qarzdor, plyus bo‘lsa — oldindan to‘lagan.
            Qarzdorlar sahifasi, hisobot va bosh sahifadagi qarz bir xil manbadan olinadi, shuning uchun raqamlar mos keladi.
            """),
        new("Chegirma berish", ["chegirm", "skidka", "arzonlashtir", "foiz chegirma", "aka uka"], """
            **Chegirma:** o‘quvchi profili → **Guruhlar** → guruh qatoridagi **Chegirma** → foiz va sababini yozing.
            Chegirma joriy va keyingi oylarga qo‘llanadi. Chegirmani faqat **Asoschi va Administrator** bera oladi — kassir bera olmaydi.
            """),
        new("Muzlatish", ["muzlat", "toxtat", "tatil", "vaqtincha", "kasal"], """
            **Muzlatish** — o‘quvchi vaqtincha kelmasa (kasallik, ta’til). Profil → **Muzlatish** → qaysi oydan qaysi oygacha va sababi.
            Muzlatilgan oylarga to‘lov hisoblanmaydi. Muddat tugagach o‘quvchi avtomatik faol bo‘ladi; oldinroq qaytsa muzlatishni bekor qilish mumkin.
            """),
        new("Guruhdan chiqarish va ketish sabablari", ["chiqar", "ketdi", "ketish", "chiqib ket", "sabab", "tark"], """
            **Guruhdan chiqarish:** profil → **Guruhlar** → **Chiqarish** → sana va **sabab** (majburiy).
            Boshqa guruhi qolmasa, o‘quvchi holati **Ketgan** bo‘ladi va chiqqan kundan keyin to‘lov hisoblanmaydi.
            [Chiqib ketganlar](/chiqib-ketganlar) sahifasida kim, qachon va nima sababdan ketgani ko‘rinadi; sabablar ro‘yxatini ham o‘sha yerda boshqarasiz.
            """),
        new("O‘quvchi holatlari", ["holat", "sinovda", "faol", "arxiv", "sinov dars", "status"], """
            **O‘quvchi holatlari:**
            - **Sinovda** — sinov darsida; to‘lov hisoblanmaydi. Faolga o‘tkazilganda o‘sha kundan qo‘shilgan hisoblanadi.
            - **Faol** — o‘qiyapti, oylik to‘lov hisoblanadi.
            - **Muzlatilgan** — vaqtincha to‘xtatgan.
            - **Ketgan** — barcha guruhlardan chiqqan.
            - **Arxiv** — ro‘yxatlarda ko‘rinmaydi (faqat Asoschi/Administrator arxivlaydi).
            """),
        new("Qarzdorlar doskasi", ["qarzdor", "qarzdorlar bilan", "qarz bilan ishla", "qarzdorga", "vada", "boglanildi", "muammoli", "qarz bosqich"], """
            **[Qarzdorlar](/qarzdorlar) doskasi** qarzdorlarni bosqichlarga ajratadi: **Yangi → Bog‘lanildi → Va’da berdi → Muammoli → To‘ladi**.
            - Kartani ochib bosqich, va’da sanasi va izoh yozasiz.
            - Va’da muddati o‘tsa, karta qizil bo‘ladi.
            - O‘quvchi to‘lasa, karta o‘zi **To‘ladi** ga o‘tadi; yana qarzga tushsa «2-marta» belgisi chiqadi.
            """),
        new("Guruh ochish", ["guruh och", "yangi guruh", "guruh yarat", "guruh qosh"], """
            **Yangi guruh:** [Kurslar va guruhlar](/guruhlar) → **Guruh ochish**: kurs, nom, ustoz, hafta kunlari, vaqt, narx, sig‘im va xona. Ustoz ulushi guruhda kiritilmaydi — ustozning moliyada belgilangan foizi avtomatik qo‘yiladi.
            Tizim bir ustoz yoki bir xonaga bir vaqtda ikki dars qo‘yishga yo‘l qo‘ymaydi. Guruh ochishni Asoschi va Administrator qiladi.
            """),
        new("Guruh narxini yoki ustozini o‘zgartirish", ["narx ozgar", "narxini ozgar", "ustozini ozgar", "guruh tahrir", "guruhni ozgar"], """
            Guruh sahifasini oching → **Tahrirlash**: nom, ustoz, narx, sig‘im. Ustoz almashsa, yangi ustozning foizi olinadi; foizni faqat moliya o‘zgartiradi.
            Yangi narx **keyingi oydan** qo‘llanadi — joriy oyning hisob-kitobi buzilmaydi.
            """),
        new("Kurs, filial va xona", ["kurs qosh", "filial qosh", "xona qosh", "yangi filial", "yangi kurs", "auditoriya"], """
            - **Kurs** — [Kurslar va guruhlar](/guruhlar) sahifasida.
            - **Filial** — [Filiallar](/filiallar) sahifasida.
            - **Xona** — [Xonalar](/xonalar) sahifasida (sig‘imi bilan).
            Bularni Asoschi va Administrator qo‘shadi.
            """),
        new("Dars jadvali", ["dars jadval", "raspisaniye", "jadval"], """
            [Dars jadvali](/dars-jadvali) haftalik ko‘rinishda barcha guruhlar darslarini xona va ustoz bo‘yicha ko‘rsatadi. Jadval guruhning hafta kunlari va vaqtidan avtomatik tuziladi.
            Sotuv xodimlari uchun **[Sotuv → Dars jadvali va bo‘sh joylar](/dars-jadvali?sotuv=1)**: har bir guruhda nechta bo‘sh joy borligi, kunlari, vaqti va narxi.
            Shu yerda _bugungi darslar_ yoki _ertangi darslar_ deb so‘rasangiz ham bo‘ladi.
            """),
        new("SMS va xabarlar", ["sms", "sms yubor", "qarzdorlarga sms", "xabar yubor", "eslatma yubor", "telegram xabar", "rassilka"], """
            SMS va Telegram orqali avtomatik xabar yuborish **hozircha ulanmagan** — keyingi bosqichda qo‘shiladi.
            Hozir qarzdorlar bilan [Qarzdorlar](/qarzdorlar) doskasi orqali ishlaysiz: telefon raqami ko‘rinadi, qo‘ng‘iroq natijasi va va’da sanasini yozib borasiz.
            """),
        new("Davomat belgilash", ["davomat belgila", "davomat qil", "yoqlama", "kelmadi belgila", "davomat qanday"], """
            **Davomat:** [Davomat](/davomat) → guruh va sanani tanlang → har bir o‘quvchiga **Keldi / Kelmadi / Kechikdi / Sababli** belgilang → saqlang.
            Ustoz, Administrator, Asoschi va Menejer belgilay oladi. Guruh sahifasida oylik davomat jadvali ko‘rinadi.
            """),
        new("Xodim va ustoz qo‘shish", ["xodim qosh", "ustoz qosh", "yangi xodim", "yangi ustoz", "oqituvchi qosh", "xodim tahrir"], """
            **Xodim qo‘shish:** [Xodimlar](/xodimlar) → ism, lavozim, turi (Ustoz/Xodim), telefon, filial, ish boshlagan sana va haq.
            Ro‘yxatdagi **Tahrirlash** tugmasi bilan keyin o‘zgartirasiz. Bu ro‘yxat ish haqi uchun; tizimga kirish akkaunti alohida — [Foydalanuvchilar](/foydalanuvchilar) da ochiladi.
            """),
        new("Ustoz haqi: oylik va foiz", ["oylik va foiz", "foiz ol", "ulush", "haq sxema", "foizga ishla", "oylikka ishla", "ustoz foiz", "foiz hisob", "foizi"], """
            **Ustoz haqi** ikki joyda:
            - **Haq sxemasi va oylik** — [Xodimlar](/xodimlar) → Tahrirlash: **Oylik + foiz**, **Faqat oylik** yoki **Faqat foiz**.
            - **Foiz (ulush)** — faqat **[Moliya → Ustozlar ulushi](/ustoz-ulushi)** da: ustozning odatiy foizi (yangi guruhlarga avtomatik qo‘yiladi) va har bir guruhdagi foiz.
            Ulush = shu oyda ustoz guruhlari uchun **qabul qilingan to‘lovlar × guruhdagi foiz**; u [Ish haqi](/ish-haqi) da oylikka qo‘shiladi.
            Ulushni faqat **Asoschi va Buxgalter** ko‘radi va o‘zgartiradi. Yordamchiga ham yozish mumkin: _«Malika foizi 40% bo‘lsin»_.
            """),
        new("Ish haqi, bonus va avans", ["ish haqi", "bonus", "avans", "maosh", "oylik ber", "ish haqi tola"], """
            **[Ish haqi](/ish-haqi)** sahifasi oy bo‘yicha: hisoblangan (oylik + ulush + bonus), berilgan (avans + to‘lov) va **qolgan** summa.
            O‘ng tomondagi formadan **bonus**, **avans** yoki **ish haqi to‘lovi** yoziladi. To‘lovlar avtomatik xarajatlarga tushadi.
            Ish haqini Asoschi, Administrator, Buxgalter va Kadrlar bo‘limi ko‘radi.
            """),
        new("Xarajat yozish va budjet", ["xarajat yoz", "xarajat qosh", "chiqim", "budjet", "limit", "xarajat turkum"], """
            **Xarajat:** [Umumiy moliya](/moliya) → **Xarajat** → turkum, summa, usul, sana va izoh.
            Ro‘yxatda bo‘lmagan turkum yozsangiz, izoh majburiy (kamida 5 belgi) — shunda hisobot tushunarli qoladi.
            **Budjet:** [Hisobotlar](/hisobotlar) da har turkumga oylik limit qo‘yasiz; oshib ketsa ogohlantirish chiqadi.
            Yoki shu yerda: _xarajat ijara 3 mln karta_.
            """),
        new("Kassa va pul topshirish", ["kassa", "topshir", "pul ber", "naqd pul", "pulni oldim", "inkassa"], """
            **[Kassa](/kassa)** — kimning qo‘lida qancha naqd pul borligini ko‘rsatadi (o‘zi qabul qilgan to‘lovlar − xarajatlar ± topshirilganlar).
            - Kassir pulni **Topshirish** bilan asoschi yoki buxgalterga yuboradi.
            - Qabul qiluvchi **Pulni oldim** yoki **Rad etish** ni bosadi; tasdiqlanmaguncha pul topshiruvchida hisoblanadi.
            - Qo‘lidagidan ko‘p pul topshirib bo‘lmaydi.
            """),
        new("Hisobotlar", ["hisobot qayer", "hisobot qanday", "hisobotlar sahifa", "audit", "kim ozgartir"], """
            **[Hisobotlar](/hisobotlar)**: davr bo‘yicha tushum va xarajat, filiallar, budjet, qarzlar va **audit jurnali** (kim qachon qanday moliyaviy amal qilgani).
            Shu yerda ham so‘rashingiz mumkin: _o‘tgan oy hisoboti_, _sentabr hisoboti_, _bu oy o‘tgan oyga nisbatan_.
            """),
        new("Lidlar va sotuv", ["lid", "sotuv", "potentsial", "mijoz", "voronka", "lid bosqich", "oquvchiga aylantir"], """
            **[Lidlar](/sotuv)** — hali o‘quvchi bo‘lmagan qiziquvchilar. Bosqichlar: **Yangi → Bog‘lanildi → Sinov darsi → O‘quvchi bo‘ldi / Yo‘qotildi**.
            - Lid qo‘shishda manba (Instagram, Telegram, tanish...) majburiy — reklama samarasini ko‘rish uchun.
            - **O‘quvchi qilish** tugmasi lidni sinovdagi o‘quvchiga aylantiradi.
            - Yo‘qotilgan lid uchun sabab yoziladi.
            """),
        new("Foydalanuvchi va akkaunt ochish", ["akkaunt", "foydalanuvchi qosh", "login ber", "kirish ber", "xodimga kirish", "parol ber"], """
            **Xodimga kirish ochish** (faqat Asoschi): [Foydalanuvchilar](/foydalanuvchilar) → ism, **telefon** (login shu bo‘ladi), boshlang‘ich parol va rol.
            Xodim telefon raqami va parol bilan kiradi. Akkauntni o‘chirish, rolni o‘zgartirish va parolni yangilash ham shu sahifada.
            """),
        new("Rollar va ruxsatlar", ["rol", "ruxsat", "kim nimani", "huquq", "dostup"], """
            **Rollar:**
            - **Asoschi** — hammasi + foydalanuvchilar.
            - **Administrator** — o‘quvchilar, guruhlar, jadval, davomat, lidlar, filial/xona, to‘lov qabul qilish va kassa. Moliyaviy hisobotlar, ish haqi, xodimlar maoshi va ustoz ulushi **ko‘rinmaydi**.
            - **Buxgalter** — moliya, ish haqi, ustozlar ulushi, hisobotlar, kassa. **Kassir** — o‘quvchilar, to‘lov, qarzdorlar, kassa.
            - **Qabulxona** — o‘quvchi, lid, to‘lov, kassa. **Menejer** — o‘quvchilar, guruhlar, davomat, lidlar (pulsiz).
            - **Ustoz** — dars jadvali va davomat. **Sotuv menejeri** / **Marketolog** — lidlar.
            - **Kadrlar bo‘limi** — xodimlar va ish haqi.
            """),
        new("Kirish va parol", ["parol unut", "kira olmay", "login", "parolni ozgar", "parolim", "parol ozgar", "parolni almashtir", "kirish", "telefon bilan kir"], """
            Xodimlar **telefon raqami** (+998 90 123 45 67) va parol bilan kiradi; asoschi ro‘yxatdan o‘tgan email bilan ham kira oladi.
            **O‘z parolingizni o‘zgartirish:** chap menyuning pastidagi **[Parolni o‘zgartirish](/Account/Manage/ChangePassword)** → hozirgi va yangi parol.
            Parolni unutgan xodimga asoschi [Foydalanuvchilar](/foydalanuvchilar) → **Parol** orqali yangi parol beradi. Akkaunt o‘chirilgan bo‘lsa, «Akkauntingiz o‘chirilgan» xabari chiqadi.
            """),
        new("Qidiruv", ["qidir", "topa olmay", "izla", "poisk"], """
            Yuqoridagi **qidiruv** maydoniga ism, telefon, guruh yoki sahifa nomini yozing — o‘quvchilar, guruhlar, xodimlar, lidlar va sahifalar bo‘yicha natija chiqadi.
            Shu yerda ham ism yozsangiz, o‘quvchi ma’lumotini ko‘rsataman.
            """),
        new("Dublikat o‘quvchilar", ["dublikat", "ikki marta", "takror", "bir xil oquvchi"], """
            Yangi o‘quvchi qo‘shishda tizim telefonning oxirgi 9 raqami yoki ism bo‘yicha o‘xshashlarni topib ogohlantiradi. Haqiqatan boshqa odam bo‘lsa, «baribir saqlash» mumkin.
            """),
        new("O‘quvchi ma’lumotini tahrirlash", ["tahrirla", "ismini ozgar", "telefonini ozgar", "malumot ozgar", "oquvchi ozgar"], """
            O‘quvchi profili → **Tahrirlash**: ism, telefon, ota-ona telefoni, tug‘ilgan sana va izoh. Holatni profil yuqorisidagi tugmalar bilan o‘zgartirasiz.
            """),
        new("Yordamchi haqida", ["yordamchi", "sen nima", "agent", "bot"], """
            Men EduHisob ichidagi yordamchiman — pullik AI emas, saytning o‘zida ishlayman va faqat markazingiz bo‘yicha javob beraman.
            Raqamlarni (hisobot, qarz, ish haqi, guruhlar, davomat, kassa) to‘g‘ridan-to‘g‘ri bazadan olaman, sayt qanday ishlashini tushuntiraman va o‘quvchi, to‘lov, xarajat, xodim, lid yozaman — har doim tasdiqlatib.
            """)
    ];

    public static (HelpTopic Topic, int Score)? Best(string text)
    {
        var tokens = UzbekText.Tokens(text);
        var best = Topics
            .Select(topic => (Topic: topic, Score: topic.Keywords.Sum(keyword => UzbekText.Matches(tokens, keyword) ? keyword.Count(c => c == ' ') + 1 : 0)))
            .OrderByDescending(item => item.Score)
            .First();
        return best.Score > 0 ? best : null;
    }
}
