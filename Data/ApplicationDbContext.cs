using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EduHisob.Data;

public class ApplicationDbContext(DbContextOptions options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<BranchRoom> BranchRooms => Set<BranchRoom>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<StudyGroup> StudyGroups => Set<StudyGroup>();
    public DbSet<GroupEnrollment> GroupEnrollments => Set<GroupEnrollment>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<PayrollEntry> PayrollEntries => Set<PayrollEntry>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();
    public DbSet<ExpenseBudget> ExpenseBudgets => Set<ExpenseBudget>();
    public DbSet<FinancialAuditEntry> FinancialAuditEntries => Set<FinancialAuditEntry>();
    public DbSet<StudentCharge> StudentCharges => Set<StudentCharge>();
    public DbSet<StudentFreeze> StudentFreezes => Set<StudentFreeze>();
    public DbSet<StudentNote> StudentNotes => Set<StudentNote>();
    public DbSet<DebtCase> DebtCases => Set<DebtCase>();
    public DbSet<LeaveReason> LeaveReasons => Set<LeaveReason>();
    public DbSet<CashTransfer> CashTransfers => Set<CashTransfer>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<AssistantConversation> AssistantConversations => Set<AssistantConversation>();
    public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();
    public DbSet<PlatformPayment> PlatformPayments => Set<PlatformPayment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<StudyGroup>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(group => new { group.OrganizationId, group.TeacherId })
            .HasPrincipalKey(employee => new { employee.OrganizationId, employee.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StudentCharge>()
            .HasIndex(charge => new { charge.OrganizationId, charge.StudentId, charge.StudyGroupId, charge.Month })
            .IsUnique();

        builder.Entity<StudentCharge>()
            .HasOne<Student>()
            .WithMany()
            .HasForeignKey(charge => new { charge.OrganizationId, charge.StudentId })
            .HasPrincipalKey(student => new { student.OrganizationId, student.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StudentCharge>()
            .HasOne<StudyGroup>()
            .WithMany()
            .HasForeignKey(charge => new { charge.OrganizationId, charge.StudyGroupId })
            .HasPrincipalKey(group => new { group.OrganizationId, group.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StudentFreeze>()
            .HasOne<Student>()
            .WithMany()
            .HasForeignKey(freeze => new { freeze.OrganizationId, freeze.StudentId })
            .HasPrincipalKey(student => new { student.OrganizationId, student.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<AssistantConversation>()
            .HasIndex(conversation => new { conversation.OrganizationId, conversation.UserId, conversation.UpdatedAtUtc });

        // Suhbat o'chirilsa, xabarlari ham o'chadi.
        builder.Entity<AssistantMessage>()
            .HasOne<AssistantConversation>()
            .WithMany()
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StudentNote>()
            .HasOne<Student>()
            .WithMany()
            .HasForeignKey(note => new { note.OrganizationId, note.StudentId })
            .HasPrincipalKey(student => new { student.OrganizationId, student.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<DebtCase>()
            .HasIndex(debtCase => new { debtCase.OrganizationId, debtCase.StudentId })
            .IsUnique();

        builder.Entity<DebtCase>()
            .HasOne<Student>()
            .WithMany()
            .HasForeignKey(debtCase => new { debtCase.OrganizationId, debtCase.StudentId })
            .HasPrincipalKey(student => new { student.OrganizationId, student.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<LeaveReason>()
            .HasAlternateKey(reason => new { reason.OrganizationId, reason.Id });

        builder.Entity<LeaveReason>()
            .HasIndex(reason => new { reason.OrganizationId, reason.NormalizedName })
            .IsUnique();

        builder.Entity<LeaveReason>()
            .HasOne<Organization>()
            .WithMany()
            .HasForeignKey(reason => reason.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GroupEnrollment>()
            .HasOne<LeaveReason>()
            .WithMany()
            .HasForeignKey(enrollment => new { enrollment.OrganizationId, enrollment.LeaveReasonId })
            .HasPrincipalKey(reason => new { reason.OrganizationId, reason.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<FinancialTransaction>()
            .HasOne<StudyGroup>()
            .WithMany()
            .HasForeignKey(transaction => new { transaction.OrganizationId, transaction.StudyGroupId })
            .HasPrincipalKey(group => new { group.OrganizationId, group.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CashTransfer>()
            .HasOne<Organization>()
            .WithMany()
            .HasForeignKey(transfer => transfer.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CashTransfer>()
            .HasIndex(transfer => new { transfer.OrganizationId, transfer.ToUserId, transfer.Status });

        builder.Entity<Lead>()
            .HasOne<Branch>()
            .WithMany()
            .HasForeignKey(lead => new { lead.OrganizationId, lead.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Lead>()
            .HasIndex(lead => new { lead.OrganizationId, lead.Stage });

        builder.Entity<Lead>()
            .HasIndex(lead => new { lead.OrganizationId, lead.Phone });

        builder.Entity<ApplicationUser>()
            .HasOne<Organization>()
            .WithMany()
            .HasForeignKey(user => user.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Branch>()
            .HasIndex(branch => new { branch.OrganizationId, branch.Name })
            .IsUnique();

        builder.Entity<Branch>()
            .HasAlternateKey(branch => new { branch.OrganizationId, branch.Id });

        builder.Entity<Student>()
            .HasIndex(student => new { student.OrganizationId, student.BranchId, student.FullName });

        builder.Entity<Student>()
            .HasAlternateKey(student => new { student.OrganizationId, student.Id });

        builder.Entity<Student>()
            .HasOne<Branch>()
            .WithMany()
            .HasForeignKey(student => new { student.OrganizationId, student.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Course>()
            .HasAlternateKey(course => new { course.OrganizationId, course.Id });

        builder.Entity<Course>()
            .HasIndex(course => new { course.OrganizationId, course.Name })
            .IsUnique();

        builder.Entity<Course>()
            .HasOne<Organization>()
            .WithMany()
            .HasForeignKey(course => course.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StudyGroup>()
            .HasAlternateKey(group => new { group.OrganizationId, group.Id });

        builder.Entity<StudyGroup>()
            .HasIndex(group => new { group.OrganizationId, group.BranchId, group.Name })
            .IsUnique();

        builder.Entity<StudyGroup>()
            .HasOne<Branch>()
            .WithMany()
            .HasForeignKey(group => new { group.OrganizationId, group.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<BranchRoom>()
            .HasAlternateKey(room => new { room.OrganizationId, room.BranchId, room.Id });

        builder.Entity<BranchRoom>()
            .HasIndex(room => new { room.OrganizationId, room.BranchId, room.Name })
            .IsUnique();

        builder.Entity<BranchRoom>()
            .HasOne<Branch>()
            .WithMany()
            .HasForeignKey(room => new { room.OrganizationId, room.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StudyGroup>()
            .HasOne<BranchRoom>()
            .WithMany()
            .HasForeignKey(group => new { group.OrganizationId, group.BranchId, group.RoomId })
            .HasPrincipalKey(room => new { room.OrganizationId, room.BranchId, room.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StudyGroup>()
            .HasOne<Course>()
            .WithMany()
            .HasForeignKey(group => new { group.OrganizationId, group.CourseId })
            .HasPrincipalKey(course => new { course.OrganizationId, course.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<GroupEnrollment>()
            .HasKey(enrollment => new { enrollment.OrganizationId, enrollment.StudyGroupId, enrollment.StudentId });

        builder.Entity<GroupEnrollment>()
            .HasOne<StudyGroup>()
            .WithMany()
            .HasForeignKey(enrollment => new { enrollment.OrganizationId, enrollment.StudyGroupId })
            .HasPrincipalKey(group => new { group.OrganizationId, group.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<GroupEnrollment>()
            .HasOne<Student>()
            .WithMany()
            .HasForeignKey(enrollment => new { enrollment.OrganizationId, enrollment.StudentId })
            .HasPrincipalKey(student => new { student.OrganizationId, student.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<AttendanceRecord>()
            .HasIndex(record => new { record.OrganizationId, record.StudyGroupId, record.StudentId, record.LessonDate })
            .IsUnique();

        builder.Entity<AttendanceRecord>()
            .HasOne<GroupEnrollment>()
            .WithMany()
            .HasForeignKey(record => new { record.OrganizationId, record.StudyGroupId, record.StudentId })
            .HasPrincipalKey(enrollment => new { enrollment.OrganizationId, enrollment.StudyGroupId, enrollment.StudentId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Employee>()
            .HasAlternateKey(employee => new { employee.OrganizationId, employee.Id });

        builder.Entity<Employee>()
            .HasIndex(employee => new { employee.OrganizationId, employee.BranchId, employee.FullName });

        builder.Entity<Employee>()
            .HasOne<Branch>()
            .WithMany()
            .HasForeignKey(employee => new { employee.OrganizationId, employee.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PayrollEntry>()
            .HasAlternateKey(entry => new { entry.OrganizationId, entry.Id });

        builder.Entity<PayrollEntry>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(entry => new { entry.OrganizationId, entry.EmployeeId })
            .HasPrincipalKey(employee => new { employee.OrganizationId, employee.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PayrollEntry>()
            .HasOne<Branch>()
            .WithMany()
            .HasForeignKey(entry => new { entry.OrganizationId, entry.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PayrollEntry>()
            .HasIndex(entry => new { entry.OrganizationId, entry.EmployeeId, entry.PayrollMonth });

        builder.Entity<FinancialTransaction>()
            .HasAlternateKey(transaction => new { transaction.OrganizationId, transaction.Id });

        builder.Entity<FinancialTransaction>()
            .HasOne<Branch>()
            .WithMany()
            .HasForeignKey(transaction => new { transaction.OrganizationId, transaction.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<FinancialTransaction>()
            .HasOne<PayrollEntry>()
            .WithMany()
            .HasForeignKey(transaction => new { transaction.OrganizationId, transaction.PayrollEntryId })
            .HasPrincipalKey(entry => new { entry.OrganizationId, entry.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<FinancialTransaction>()
            .HasOne<Student>()
            .WithMany()
            .HasForeignKey(transaction => new { transaction.OrganizationId, transaction.StudentId })
            .HasPrincipalKey(student => new { student.OrganizationId, student.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<FinancialTransaction>()
            .HasIndex(transaction => new { transaction.OrganizationId, transaction.PayrollEntryId })
            .IsUnique();

        builder.Entity<FinancialTransaction>()
            .HasIndex(transaction => new { transaction.OrganizationId, transaction.StudentId });

        builder.Entity<ExpenseBudget>()
            .HasOne<Branch>()
            .WithMany()
            .HasForeignKey(budget => new { budget.OrganizationId, budget.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ExpenseBudget>()
            .HasIndex(budget => new { budget.OrganizationId, budget.BranchId, budget.Month, budget.Category })
            .IsUnique();

        builder.Entity<FinancialAuditEntry>()
            .HasOne<Organization>()
            .WithMany()
            .HasForeignKey(entry => entry.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<FinancialAuditEntry>()
            .HasIndex(entry => new { entry.OrganizationId, entry.RecordedAtUtc });

        builder.Entity<Branch>()
            .HasOne(branch => branch.Organization)
            .WithMany(organization => organization.Branches)
            .HasForeignKey(branch => branch.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PlatformPayment>().HasIndex(payment => payment.PaidAtUtc);
        builder.Entity<PlatformPayment>().HasIndex(payment => payment.CenterId);
    }
}
