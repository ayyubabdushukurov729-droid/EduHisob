using Microsoft.EntityFrameworkCore;

namespace EduHisob.Data;

public sealed record StudentLedgerTotals(decimal Charged, decimal Paid)
{
    public decimal Balance => Paid - Charged;
}

// O'quvchi balansining yagona manbai. Qarzdorlar, profil, ro'yxat va hisobotlar faqat shu yerdan foydalanadi.
public static class ChargeLedger
{
    // Oy boshida o'sha oy uchun to'lov hisoblanadi. O'quvchi oy o'rtasida qo'shilsa, hisob keyingi oydan boshlanadi.
    public static DateOnly FirstChargedMonth(DateOnly joinedOn, DateTime enrolledAtUtc)
    {
        var enrolledOn = DateOnly.FromDateTime(enrolledAtUtc);
        var startedOn = joinedOn > enrolledOn ? joinedOn : enrolledOn;
        var month = new DateOnly(startedOn.Year, startedOn.Month, 1);
        return startedOn.Day > 1 ? month.AddMonths(1) : month;
    }

    public static DateOnly MonthOf(DateOnly date) => new(date.Year, date.Month, 1);

    public static DateOnly CurrentMonth() => MonthOf(DateOnly.FromDateTime(DateTime.Today));

    public static decimal ApplyDiscount(decimal fee, decimal discountPercent) =>
        Math.Round(fee * (100m - Math.Clamp(discountPercent, 0m, 100m)) / 100m, 0, MidpointRounding.AwayFromZero);

    // Yetishmayotgan oylik hisoblarni yaratadi (takror chaqirilsa ham ikki marta yozmaydi).
    // Faqat "Faol" o'quvchilar, guruhdagi faol a'zoliklar va muzlatilmagan oylar hisoblanadi.
    public static async Task<int> SyncAsync(ApplicationDbContext db, int organizationId, DateOnly throughMonth)
    {
        await ReconcileFrozenStatusesAsync(db, organizationId);

        var memberships = await (
            from enrollment in db.GroupEnrollments.AsNoTracking()
            join student in db.Students.AsNoTracking()
                on new { enrollment.OrganizationId, enrollment.StudentId } equals new { student.OrganizationId, StudentId = student.Id }
            join studyGroup in db.StudyGroups.AsNoTracking()
                on new { enrollment.OrganizationId, GroupId = enrollment.StudyGroupId } equals new { studyGroup.OrganizationId, GroupId = studyGroup.Id }
            where enrollment.OrganizationId == organizationId &&
                (student.Status == StudentStatus.Active || student.Status == StudentStatus.Frozen) &&
                studyGroup.IsActive
            select new
            {
                student.Status,
                enrollment.StudentId,
                enrollment.StudyGroupId,
                enrollment.EnrolledAtUtc,
                enrollment.DiscountPercent,
                enrollment.LeftOn,
                student.JoinedOn,
                studyGroup.MonthlyFee
            }).ToListAsync();
        memberships = memberships.Where(item => item.MonthlyFee > 0).ToList();
        if (memberships.Count == 0)
        {
            return 0;
        }

        var studentIds = memberships.Select(item => item.StudentId).Distinct().ToArray();
        var existing = (await db.StudentCharges.AsNoTracking()
                .Where(charge => charge.OrganizationId == organizationId && studentIds.Contains(charge.StudentId))
                .Select(charge => new { charge.StudentId, charge.StudyGroupId, charge.Month })
                .ToListAsync())
            .Select(charge => (charge.StudentId, charge.StudyGroupId, charge.Month))
            .ToHashSet();
        var freezes = await db.StudentFreezes.AsNoTracking()
            .Where(freeze => freeze.OrganizationId == organizationId && studentIds.Contains(freeze.StudentId))
            .Select(freeze => new { freeze.StudentId, freeze.FromMonth, freeze.ToMonth })
            .ToListAsync();

        var studentsWithFreezes = freezes.Select(freeze => freeze.StudentId).ToHashSet();
        var created = 0;
        foreach (var item in memberships)
        {
            // Muzlatish davri kiritilmagan "Muzlatilgan" o'quvchi (eski yozuvlar) umuman hisoblanmaydi.
            if (item.Status == StudentStatus.Frozen && !studentsWithFreezes.Contains(item.StudentId))
            {
                continue;
            }

            var month = FirstChargedMonth(item.JoinedOn, item.EnrolledAtUtc);
            // Oy o'rtasida chiqsa o'sha oy hisoblanadi; oyning 1-kuni chiqsa, o'sha oy hisoblanmaydi.
            var lastMonth = throughMonth;
            if (item.LeftOn is { } leftOn)
            {
                var lastActiveMonth = MonthOf(leftOn).AddMonths(leftOn.Day == 1 ? -1 : 0);
                if (lastActiveMonth < lastMonth)
                {
                    lastMonth = lastActiveMonth;
                }
            }
            for (; month <= lastMonth; month = month.AddMonths(1))
            {
                if (existing.Contains((item.StudentId, item.StudyGroupId, month)))
                {
                    continue;
                }

                var frozenMonth = month;
                if (freezes.Any(freeze => freeze.StudentId == item.StudentId && freeze.FromMonth <= frozenMonth && freeze.ToMonth >= frozenMonth))
                {
                    continue;
                }

                var amount = ApplyDiscount(item.MonthlyFee, item.DiscountPercent);
                db.StudentCharges.Add(new StudentCharge
                {
                    OrganizationId = organizationId,
                    StudentId = item.StudentId,
                    StudyGroupId = item.StudyGroupId,
                    Month = month,
                    FullAmount = item.MonthlyFee,
                    DiscountAmount = item.MonthlyFee - amount,
                    Amount = amount
                });
                existing.Add((item.StudentId, item.StudyGroupId, month));
                created++;
            }
        }

        if (created > 0)
        {
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Boshqa so'rov xuddi shu hisoblarni parallel yaratgan bo'lishi mumkin; keyingi chaqiruvda tekislanadi.
                db.ChangeTracker.Clear();
                return 0;
            }
        }

        return created;
    }

    // Muzlatish davri boshlangan o'quvchini "Muzlatilgan"ga, tugaganini "Faol"ga o'tkazadi.
    public static async Task ReconcileFrozenStatusesAsync(ApplicationDbContext db, int organizationId)
    {
        var currentMonth = CurrentMonth();
        var freezes = await db.StudentFreezes.AsNoTracking()
            .Where(freeze => freeze.OrganizationId == organizationId)
            .Select(freeze => new { freeze.StudentId, freeze.FromMonth, freeze.ToMonth })
            .ToListAsync();
        if (freezes.Count == 0)
        {
            return;
        }

        var frozenNow = freezes
            .Where(freeze => freeze.FromMonth <= currentMonth && freeze.ToMonth >= currentMonth)
            .Select(freeze => freeze.StudentId)
            .ToHashSet();
        var withFreezes = freezes.Select(freeze => freeze.StudentId).Distinct().ToArray();
        var students = await db.Students
            .Where(student =>
                student.OrganizationId == organizationId &&
                withFreezes.Contains(student.Id) &&
                (student.Status == StudentStatus.Active || student.Status == StudentStatus.Frozen))
            .ToListAsync();
        foreach (var student in students)
        {
            student.Status = frozenNow.Contains(student.Id) ? StudentStatus.Frozen : StudentStatus.Active;
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }

    public static async Task<Dictionary<int, StudentLedgerTotals>> GetTotalsAsync(
        ApplicationDbContext db,
        int organizationId,
        IReadOnlyCollection<int>? studentIds = null)
    {
        var charges = db.StudentCharges.AsNoTracking().Where(charge => charge.OrganizationId == organizationId);
        var payments = db.FinancialTransactions.AsNoTracking().Where(transaction =>
            transaction.OrganizationId == organizationId &&
            transaction.Type == FinancialTransactionType.Income &&
            transaction.StudentId.HasValue);
        if (studentIds is not null)
        {
            charges = charges.Where(charge => studentIds.Contains(charge.StudentId));
            payments = payments.Where(transaction => studentIds.Contains(transaction.StudentId!.Value));
        }

        // SQLite decimal yig'indisini serverda ishonchli hisoblamaydi, shuning uchun summalar xotirada qo'shiladi.
        var chargeRows = await charges.Select(charge => new { charge.StudentId, charge.Amount }).ToListAsync();
        var paymentRows = await payments.Select(transaction => new { StudentId = transaction.StudentId!.Value, transaction.Amount }).ToListAsync();

        var charged = chargeRows.GroupBy(row => row.StudentId).ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
        var paid = paymentRows.GroupBy(row => row.StudentId).ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
        return charged.Keys.Union(paid.Keys).ToDictionary(
            id => id,
            id => new StudentLedgerTotals(charged.GetValueOrDefault(id), paid.GetValueOrDefault(id)));
    }
}
