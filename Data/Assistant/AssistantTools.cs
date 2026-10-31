using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduHisob.Data.Assistant;

// Yordamchi bajaradigan amallar. Har bir amal saytdagi tayyor xizmatni chaqiradi, shuning uchun
// rol ruxsatlari, tekshiruvlar va audit xuddi foydalanuvchi o'zi bosgandagidek ishlaydi.
public sealed class AssistantTools(
    CenterDataService center,
    StudentAccountService accounts,
    SalesService sales,
    GlobalSearchService search)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<(string Result, bool IsError)> RunAsync(string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        try
        {
            var args = new Args(input);
            object result = name switch
            {
                "get_overview" => await OverviewAsync(),
                "get_month_report" => await MonthReportAsync(args.Month("month")),
                "search" => await search.SearchAsync(args.Str("query")),
                "list_students" => await ListStudentsAsync(args),
                "get_student" => await accounts.GetStudentProfileAsync(args.Int("student_id")),
                "add_student" => await AddStudentAsync(args),
                "update_student" => await UpdateStudentAsync(args),
                "enroll_student" => await Done(center.EnrollStudentAsync(args.Int("student_id"), args.Int("group_id"), args.DecOr("discount_percent", 0), args.StrOr("discount_note", "")!), "O‘quvchi guruhga yozildi."),
                "leave_group" => await LeaveGroupAsync(args),
                "freeze_student" => await Done(accounts.FreezeAsync(args.Int("student_id"), args.Month("from_month"), args.Month("to_month"), args.Str("reason")), "Muzlatildi."),
                "add_student_note" => await Done(accounts.AddNoteAsync(args.Int("student_id"), args.Str("text")), "Izoh qo‘shildi."),
                "accept_payment" => await AcceptPaymentAsync(args),
                "list_groups" => await center.GetStudyGroupsAsync(),
                "get_group" => await accounts.GetGroupProfileAsync(args.Int("group_id"), args.MonthOr("month", ThisMonth)),
                "create_group" => await CreateGroupAsync(args),
                "list_reference_data" => new { branches = (await center.GetBranchesAsync()).Select(b => new { b.Id, b.Name }), rooms = await center.GetBranchRoomsAsync(), courses = (await center.GetCoursesAsync()).Select(c => new { c.Id, c.Name }) },
                "add_course" => new { courseId = await center.AddCourseAsync(args.Str("name"), args.StrOr("description", "")!) },
                "list_debtors" => await accounts.GetDebtorsAsync(),
                "list_transactions" => await ListTransactionsAsync(args),
                "add_expense" => await AddExpenseAsync(args),
                "list_employees" => await center.GetEmployeesAsync(),
                "add_employee" => await AddEmployeeAsync(args),
                "update_employee" => await UpdateEmployeeAsync(args),
                "get_teacher_shares" => await center.GetTeacherSharesAsync(args.Month("month")),
                "set_teacher_share" => await SetTeacherShareAsync(args),
                "set_group_share" => await SetGroupShareAsync(args),
                "get_payroll" => await center.GetPayrollSummaryAsync(args.Month("month")),
                "add_payroll_entry" => await AddPayrollEntryAsync(args),
                "list_leads" => (await sales.GetLeadsAsync(args.StrOr("search", null), includeClosed: args.BoolOr("include_closed", false))).Take(80),
                "add_lead" => await AddLeadAsync(args),
                "move_lead" => await Done(sales.MoveAsync(args.Int("lead_id"), args.Enum<LeadStage>("stage"), args.StrOr("lost_reason", "")!), "Lid bosqichi o‘zgardi."),
                "convert_lead" => await ConvertLeadAsync(args.Int("lead_id")),
                _ => throw new InvalidOperationException($"Noma’lum amal: {name}")
            };
            return (JsonSerializer.Serialize(result, JsonOptions), false);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            var message = exception switch
            {
                UnauthorizedAccessException => $"Ruxsat yo‘q: {exception.Message} Foydalanuvchining roli bu amalga ruxsat bermaydi.",
                Microsoft.EntityFrameworkCore.DbUpdateException => "Ma’lumotni saqlab bo‘lmadi.",
                _ => exception.Message
            };
            return (message, true);
        }
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static DateOnly ThisMonth => new(Today.Year, Today.Month, 1);

    private static async Task<object> Done(Task task, string message)
    {
        await task;
        return new { ok = true, message };
    }

    // Rol ruxsat bermagan bo'lim xato emas: hisobotning shu qismi shunchaki tushib qoladi.
    private static async Task<object?> Optional<T>(Func<Task<T>> load)
    {
        try { return await load(); }
        catch (UnauthorizedAccessException) { return null; }
    }

    private async Task<object> OverviewAsync() => new
    {
        summary = await center.GetDashboardAsync(),
        finance = await Optional(center.GetDashboardFinanceAsync)
    };

    private async Task<object> MonthReportAsync(DateOnly month)
    {
        var end = month.AddMonths(1).AddDays(-1);
        var report = new Dictionary<string, object?> { ["month"] = month.ToString("yyyy-MM"), ["from"] = month, ["to"] = end };

        // Oylik natija (foyda) faqat moliya xodimlariga: administrator va kassir kassada ishlaydi, lekin foydani ko'rmaydi.
        var transactions = await center.CanSeeFinanceAsync() ? await Optional(() => center.GetFinancialTransactionsAsync(month, end)) : null;
        if (transactions is IReadOnlyList<FinancialTransactionSummary> rows)
        {
            var income = rows.Where(t => t.Type == FinancialTransactionType.Income).ToList();
            var expense = rows.Where(t => t.Type == FinancialTransactionType.Expense).ToList();
            report["income"] = income.Sum(t => t.Amount);
            report["expense"] = expense.Sum(t => t.Amount);
            report["result"] = income.Sum(t => t.Amount) - expense.Sum(t => t.Amount);
            report["incomePaymentsCount"] = income.Count;
            report["incomeByMethod"] = income.GroupBy(t => t.PaymentMethod).ToDictionary(g => g.Key.ToString(), g => g.Sum(t => t.Amount));
            report["incomeByBranch"] = income.GroupBy(t => t.BranchName).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
            report["expenseByCategory"] = expense.GroupBy(t => t.Category).OrderByDescending(g => g.Sum(t => t.Amount)).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
            report["expenseByBranch"] = expense.GroupBy(t => t.BranchName).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        }
        else
        {
            report["finance"] = "Rolingiz moliyaviy raqamlarni ko‘rishga ruxsat bermaydi.";
        }

        if (await Optional(() => center.GetExpenseBudgetsAsync(month)) is IReadOnlyList<ExpenseBudgetSummary> budgets && budgets.Count > 0)
        {
            report["budgets"] = budgets.Select(b => new { b.BranchName, b.Category, b.LimitAmount });
        }
        if (await Optional(() => center.GetPayrollSummaryAsync(month)) is IReadOnlyList<EmployeePayrollSummary> payroll)
        {
            report["payroll"] = new
            {
                accrued = payroll.Sum(p => p.AgreedMonthlySalary + p.RevenueShare + p.Bonuses),
                teacherShares = payroll.Sum(p => p.RevenueShare),
                paid = payroll.Sum(p => p.Advances + p.SalaryPayments),
                remaining = payroll.Sum(p => p.Remaining)
            };
        }
        if (await Optional(() => center.GetStudentsAsync()) is IReadOnlyList<Student> students)
        {
            report["newStudents"] = students.Count(s => s.JoinedOn >= month && s.JoinedOn <= end);
            report["studentsNow"] = students.GroupBy(s => s.Status).ToDictionary(g => g.Key.ToString(), g => g.Count());
        }
        if (await Optional(() => accounts.GetLeftStudentsAsync(month, end)) is IReadOnlyList<LeftStudentRow> left)
        {
            report["leftStudents"] = left.Count;
            report["leftByReason"] = left.GroupBy(l => l.Reason).ToDictionary(g => g.Key, g => g.Count());
        }
        if (await Optional(() => center.GetStudentDebtReportAsync(month)) is IReadOnlyList<StudentDebtSummary> debts)
        {
            var debtors = debts.Where(d => d.Balance < 0).ToList();
            report["debtAtMonthEnd"] = new { students = debtors.Count, total = -debtors.Sum(d => d.Balance) };
        }
        if (await Optional(() => sales.GetFunnelAsync(month, end)) is SalesFunnel funnel)
        {
            report["salesFunnel"] = funnel;
        }

        return report;
    }

    private async Task<object> ListStudentsAsync(Args args)
    {
        var students = await center.GetStudentsAsync(args.StrOr("search", null), status: args.EnumOr<StudentStatus>("status"));
        var balances = await accounts.GetBalancesAsync();
        var rows = students.Select(s => new
        {
            s.Id, s.FullName, s.Phone, s.ParentPhone, s.Status, s.JoinedOn,
            balance = balances.GetValueOrDefault(s.Id),
            link = $"/oquvchilar/{s.Id}"
        });
        if (args.BoolOr("debtors_only", false))
        {
            rows = rows.Where(s => s.balance < 0).OrderBy(s => s.balance);
        }

        var list = rows.ToList();
        return new { total = list.Count, shown = Math.Min(list.Count, 60), students = list.Take(60) };
    }

    private async Task<int> DefaultBranchAsync(Args args) =>
        args.IntOr("branch_id") ?? (await center.GetBranchesAsync()).FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("Markazda faol filial yo‘q.");

    private async Task<object> AddStudentAsync(Args args)
    {
        var fullName = args.Str("full_name");
        var phone = args.Str("phone");
        if (!args.BoolOr("allow_duplicate", false))
        {
            var duplicates = await accounts.FindDuplicatesAsync(phone, fullName);
            if (duplicates.Count > 0)
            {
                return new { saved = false, message = "Shunga o‘xshash o‘quvchilar bor. Foydalanuvchidan so‘rang: bu boshqa odammi?", duplicates };
            }
        }

        var student = new Student
        {
            FullName = fullName,
            Phone = phone,
            ParentPhone = args.StrOr("parent_phone", "")!,
            BirthDate = args.DateOr("birth_date"),
            Status = args.EnumOr<StudentStatus>("status") ?? StudentStatus.Active,
            BranchId = await DefaultBranchAsync(args),
            JoinedOn = Today
        };
        await center.AddStudentAsync(student);
        var enrolled = false;
        if (args.IntOr("group_id") is int groupId)
        {
            await center.EnrollStudentAsync(groupId, student.Id, args.DecOr("discount_percent", 0), "");
            enrolled = true;
        }

        return new { saved = true, studentId = student.Id, enrolledInGroup = enrolled, link = $"/oquvchilar/{student.Id}" };
    }

    private async Task<object> UpdateStudentAsync(Args args)
    {
        var id = args.Int("student_id");
        var current = (await accounts.GetStudentProfileAsync(id)).Student;
        await accounts.UpdateStudentAsync(
            id,
            args.StrOr("full_name", current.FullName)!,
            args.StrOr("phone", current.Phone)!,
            args.StrOr("parent_phone", current.ParentPhone)!,
            args.DateOr("birth_date") ?? current.BirthDate,
            args.StrOr("notes", current.Notes)!);
        if (args.EnumOr<StudentStatus>("status") is StudentStatus status && status != current.Status)
        {
            await accounts.SetStudentStatusAsync(id, status);
        }

        return new { ok = true, link = $"/oquvchilar/{id}" };
    }

    private async Task<object> LeaveGroupAsync(Args args)
    {
        var reasons = await accounts.GetLeaveReasonsAsync();
        if (args.IntOr("reason_id") is not int reasonId)
        {
            return new { saved = false, message = "Chiqish sababini tanlang (foydalanuvchidan so‘rang).", reasons = reasons.Select(r => new { r.Id, r.Name }) };
        }

        await accounts.LeaveGroupAsync(args.Int("student_id"), args.Int("group_id"), args.DateOr("left_on") ?? Today, reasonId, args.StrOr("note", "")!);
        return new { ok = true, message = "O‘quvchi guruhdan chiqarildi." };
    }

    private async Task<object> AcceptPaymentAsync(Args args)
    {
        var studentId = args.Int("student_id");
        var profile = await accounts.GetStudentProfileAsync(studentId);
        var groupId = args.IntOr("group_id");
        var activeGroups = profile.Groups.Where(g => g.LeftOn is null).ToList();
        if (groupId is null && activeGroups.Count == 1)
        {
            groupId = activeGroups[0].GroupId;
        }
        else if (groupId is null && activeGroups.Count > 1)
        {
            return new { saved = false, message = "O‘quvchi bir nechta guruhda. Qaysi guruh uchun to‘lov ekanini so‘rang.", groups = activeGroups.Select(g => new { g.GroupId, g.GroupName }) };
        }

        var transactionId = await center.AddFinancialTransactionAsync(
            FinancialTransactionType.Income, profile.Student.BranchId, studentId, args.DateOr("date") ?? Today, "",
            args.Dec("amount"), args.Enum<PaymentMethod>("method"), args.StrOr("note", "")!, groupId);
        var balance = (await accounts.GetStudentProfileAsync(studentId)).Balance;
        return new { saved = true, transactionId, newBalance = balance, receipt = $"/chek/{transactionId}", student = $"/oquvchilar/{studentId}" };
    }

    private async Task<object> CreateGroupAsync(Args args)
    {
        var group = new StudyGroup
        {
            Name = args.Str("name"),
            CourseId = args.Int("course_id"),
            BranchId = await DefaultBranchAsync(args),
            TeacherId = args.IntOr("teacher_id"),
            DaysOfWeek = args.Str("days_of_week"),
            StartTime = args.Str("start_time"),
            MonthlyFee = args.Dec("monthly_fee"),
            Capacity = args.IntOr("capacity") ?? 20,
            RoomId = args.IntOr("room_id")
        };
        await center.AddStudyGroupAsync(group);
        return new { saved = true, groupId = group.Id, link = $"/guruhlar/{group.Id}" };
    }

    private async Task<object> ListTransactionsAsync(Args args)
    {
        var rows = await center.GetFinancialTransactionsAsync(args.Date("from"), args.Date("to"));
        if (args.EnumOr<FinancialTransactionType>("type") is FinancialTransactionType type)
        {
            rows = rows.Where(t => t.Type == type).ToList();
        }

        return new { total = rows.Count, sum = rows.Sum(t => t.Type == FinancialTransactionType.Income ? t.Amount : -t.Amount), shown = Math.Min(rows.Count, 150), transactions = rows.Take(150) };
    }

    private async Task<object> AddExpenseAsync(Args args)
    {
        var id = await center.AddFinancialTransactionAsync(
            FinancialTransactionType.Expense, await DefaultBranchAsync(args), null, args.DateOr("date") ?? Today,
            args.Str("category"), args.Dec("amount"), args.Enum<PaymentMethod>("method"), args.Str("note"));
        return new { saved = true, transactionId = id };
    }

    private async Task<object> AddEmployeeAsync(Args args)
    {
        var employee = new Employee
        {
            FullName = args.Str("full_name"),
            Phone = args.Str("phone"),
            Position = args.Str("position"),
            Role = args.Enum<EmployeeRole>("kind"),
            PayType = args.EnumOr<EmployeePayType>("pay_type") ?? EmployeePayType.SalaryAndShare,
            AgreedMonthlySalary = args.DecOr("salary", 0),
            DefaultSharePercent = args.DecOr("share_percent", 0),
            BranchId = await DefaultBranchAsync(args),
            HiredOn = args.DateOr("hired_on") ?? Today
        };
        await center.AddEmployeeAsync(employee);
        return new { saved = true, employeeId = employee.Id };
    }

    private async Task<object> UpdateEmployeeAsync(Args args)
    {
        var id = args.Int("employee_id");
        var current = (await center.GetEmployeesAsync()).SingleOrDefault(e => e.Id == id)
            ?? throw new InvalidOperationException("Xodim topilmadi.");
        await center.UpdateEmployeeAsync(new Employee
        {
            Id = id,
            FullName = args.StrOr("full_name", current.FullName)!,
            Phone = args.StrOr("phone", current.Phone)!,
            Position = args.StrOr("position", current.Position)!,
            Role = current.Role,
            BranchId = args.IntOr("branch_id") ?? current.BranchId,
            HiredOn = current.HiredOn,
            AgreedMonthlySalary = args.DecOr("salary", current.AgreedMonthlySalary),
            PayType = args.EnumOr<EmployeePayType>("pay_type") ?? current.PayType,
            DefaultSharePercent = args.DecOr("share_percent", current.DefaultSharePercent)
        });
        return new { ok = true };
    }

    // Ustoz ulushi faqat moliya orqali (CenterDataService ichida Finance roli tekshiriladi).
    private async Task<object> SetTeacherShareAsync(Args args)
    {
        await center.SetTeacherDefaultShareAsync(args.Int("employee_id"), args.Dec("percent"), args.BoolOr("apply_to_groups", true));
        return new { ok = true };
    }

    private async Task<object> SetGroupShareAsync(Args args)
    {
        await center.SetGroupShareAsync(args.Int("group_id"), args.Dec("percent"));
        return new { ok = true };
    }

    private async Task<object> AddPayrollEntryAsync(Args args)
    {
        var type = args.Enum<PayrollEntryType>("type");
        await center.AddPayrollEntryAsync(
            args.Int("employee_id"), args.Month("month"), Today, type, args.Dec("amount"),
            type == PayrollEntryType.Bonus ? null : args.EnumOr<PaymentMethod>("method") ?? PaymentMethod.Cash,
            args.StrOr("note", "")!);
        return new { saved = true };
    }

    private async Task<object> ConvertLeadAsync(int leadId)
    {
        var studentId = await sales.ConvertToStudentAsync(leadId);
        return new { saved = true, studentId, link = $"/oquvchilar/{studentId}" };
    }

    private async Task<object> AddLeadAsync(Args args)
    {
        var id = await sales.AddLeadAsync(new LeadInput
        {
            BranchId = await DefaultBranchAsync(args),
            FullName = args.Str("full_name"),
            Phone = args.Str("phone"),
            Source = args.Enum<LeadSource>("source"),
            CourseInterest = args.StrOr("course_interest", "")!,
            Note = args.StrOr("note", "")!
        }, allowDuplicate: false);
        return new { saved = true, leadId = id };
    }

    // Model yuborgan JSON kirishni o'qish. Xato bo'lsa ArgumentException — model uni ko'rib, tuzatib qayta chaqiradi.
    private sealed class Args(IReadOnlyDictionary<string, JsonElement> input)
    {
        private JsonElement? Get(string key) =>
            input.TryGetValue(key, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? value : null;

        public string Str(string key) => StrOr(key, null) is { Length: > 0 } text ? text : throw new ArgumentException($"'{key}' maydoni kerak.");
        public string? StrOr(string key, string? fallback) => Get(key) is { } v ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())?.Trim() : fallback;
        public int Int(string key) => IntOr(key) ?? throw new ArgumentException($"'{key}' maydoni kerak.");
        public int? IntOr(string key) => Get(key) is { } v ? (v.ValueKind == JsonValueKind.Number ? v.GetInt32() : int.Parse(v.GetString()!, CultureInfo.InvariantCulture)) : null;
        public decimal Dec(string key) => Get(key) is { } ? DecOr(key, 0) : throw new ArgumentException($"'{key}' maydoni kerak.");
        public decimal DecOr(string key, decimal fallback) => Get(key) is { } v ? (v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : decimal.Parse(v.GetString()!.Replace(" ", ""), CultureInfo.InvariantCulture)) : fallback;
        public bool BoolOr(string key, bool fallback) => Get(key) is { } v ? v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true") : fallback;
        public DateOnly Date(string key) => DateOr(key) ?? throw new ArgumentException($"'{key}' maydoni kerak (YYYY-MM-DD).");
        public DateOnly? DateOr(string key) => StrOr(key, null) is { Length: > 0 } text ? DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        public DateOnly Month(string key) => MonthOr(key, null) ?? throw new ArgumentException($"'{key}' maydoni kerak (YYYY-MM).");
        public DateOnly? MonthOr(string key, DateOnly? fallback) =>
            StrOr(key, null) is { Length: >= 7 } text ? DateOnly.ParseExact(text[..7], "yyyy-MM", CultureInfo.InvariantCulture) : fallback;
        public DateOnly MonthOr(string key, DateOnly fallback) => MonthOr(key, (DateOnly?)fallback)!.Value;
        public T Enum<T>(string key) where T : struct, System.Enum => EnumOr<T>(key) ?? throw new ArgumentException($"'{key}' maydoni kerak.");
        public T? EnumOr<T>(string key) where T : struct, System.Enum =>
            StrOr(key, null) is { Length: > 0 } text
                ? System.Enum.TryParse<T>(text, ignoreCase: true, out var value) && System.Enum.IsDefined(value) ? value : throw new ArgumentException($"'{key}' qiymati noto‘g‘ri: {text}.")
                : null;
    }
}
