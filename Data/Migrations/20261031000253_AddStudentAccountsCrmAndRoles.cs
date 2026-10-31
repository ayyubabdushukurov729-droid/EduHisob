using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduHisob.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentAccountsCrmAndRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeacherId",
                table: "StudyGroups",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TeacherSharePercent",
                table: "StudyGroups",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "Students",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentPhone",
                table: "Students",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DiscountNote",
                table: "GroupEnrollments",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "GroupEnrollments",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "LeaveNote",
                table: "GroupEnrollments",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LeaveReasonId",
                table: "GroupEnrollments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LeftOn",
                table: "GroupEnrollments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordedByUserId",
                table: "FinancialTransactions",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StudyGroupId",
                table: "FinancialTransactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CenterRole",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "CashTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ToUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashTransfers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DebtCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    StudentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    PromisedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Occurrence = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DebtCases_Students_OrganizationId_StudentId",
                        columns: x => new { x.OrganizationId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    BranchId = table.Column<int>(type: "INTEGER", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    CourseInterest = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    LostReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ConvertedStudentId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leads_Branches_OrganizationId_BranchId",
                        columns: x => new { x.OrganizationId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveReasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveReasons", x => x.Id);
                    table.UniqueConstraint("AK_LeaveReasons_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaveReasons_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentCharges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    StudentId = table.Column<int>(type: "INTEGER", nullable: false),
                    StudyGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FullAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentCharges_Students_OrganizationId_StudentId",
                        columns: x => new { x.OrganizationId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentCharges_StudyGroups_OrganizationId_StudyGroupId",
                        columns: x => new { x.OrganizationId, x.StudyGroupId },
                        principalTable: "StudyGroups",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentFreezes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    StudentId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromMonth = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ToMonth = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentFreezes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentFreezes_Students_OrganizationId_StudentId",
                        columns: x => new { x.OrganizationId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    StudentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AuthorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentNotes_Students_OrganizationId_StudentId",
                        columns: x => new { x.OrganizationId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroups_OrganizationId_TeacherId",
                table: "StudyGroups",
                columns: new[] { "OrganizationId", "TeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupEnrollments_OrganizationId_LeaveReasonId",
                table: "GroupEnrollments",
                columns: new[] { "OrganizationId", "LeaveReasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_OrganizationId_StudyGroupId",
                table: "FinancialTransactions",
                columns: new[] { "OrganizationId", "StudyGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_OrganizationId_ToUserId_Status",
                table: "CashTransfers",
                columns: new[] { "OrganizationId", "ToUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DebtCases_OrganizationId_StudentId",
                table: "DebtCases",
                columns: new[] { "OrganizationId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leads_OrganizationId_BranchId",
                table: "Leads",
                columns: new[] { "OrganizationId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_OrganizationId_Phone",
                table: "Leads",
                columns: new[] { "OrganizationId", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_OrganizationId_Stage",
                table: "Leads",
                columns: new[] { "OrganizationId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveReasons_OrganizationId_NormalizedName",
                table: "LeaveReasons",
                columns: new[] { "OrganizationId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentCharges_OrganizationId_StudentId_StudyGroupId_Month",
                table: "StudentCharges",
                columns: new[] { "OrganizationId", "StudentId", "StudyGroupId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentCharges_OrganizationId_StudyGroupId",
                table: "StudentCharges",
                columns: new[] { "OrganizationId", "StudyGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentFreezes_OrganizationId_StudentId",
                table: "StudentFreezes",
                columns: new[] { "OrganizationId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentNotes_OrganizationId_StudentId",
                table: "StudentNotes",
                columns: new[] { "OrganizationId", "StudentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialTransactions_StudyGroups_OrganizationId_StudyGroupId",
                table: "FinancialTransactions",
                columns: new[] { "OrganizationId", "StudyGroupId" },
                principalTable: "StudyGroups",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GroupEnrollments_LeaveReasons_OrganizationId_LeaveReasonId",
                table: "GroupEnrollments",
                columns: new[] { "OrganizationId", "LeaveReasonId" },
                principalTable: "LeaveReasons",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudyGroups_Employees_OrganizationId_TeacherId",
                table: "StudyGroups",
                columns: new[] { "OrganizationId", "TeacherId" },
                principalTable: "Employees",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinancialTransactions_StudyGroups_OrganizationId_StudyGroupId",
                table: "FinancialTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_GroupEnrollments_LeaveReasons_OrganizationId_LeaveReasonId",
                table: "GroupEnrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_StudyGroups_Employees_OrganizationId_TeacherId",
                table: "StudyGroups");

            migrationBuilder.DropTable(
                name: "CashTransfers");

            migrationBuilder.DropTable(
                name: "DebtCases");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "LeaveReasons");

            migrationBuilder.DropTable(
                name: "StudentCharges");

            migrationBuilder.DropTable(
                name: "StudentFreezes");

            migrationBuilder.DropTable(
                name: "StudentNotes");

            migrationBuilder.DropIndex(
                name: "IX_StudyGroups_OrganizationId_TeacherId",
                table: "StudyGroups");

            migrationBuilder.DropIndex(
                name: "IX_GroupEnrollments_OrganizationId_LeaveReasonId",
                table: "GroupEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_FinancialTransactions_OrganizationId_StudyGroupId",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "TeacherId",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "TeacherSharePercent",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ParentPhone",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "DiscountNote",
                table: "GroupEnrollments");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "GroupEnrollments");

            migrationBuilder.DropColumn(
                name: "LeaveNote",
                table: "GroupEnrollments");

            migrationBuilder.DropColumn(
                name: "LeaveReasonId",
                table: "GroupEnrollments");

            migrationBuilder.DropColumn(
                name: "LeftOn",
                table: "GroupEnrollments");

            migrationBuilder.DropColumn(
                name: "RecordedByUserId",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "StudyGroupId",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "CenterRole",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AspNetUsers");
        }
    }
}
