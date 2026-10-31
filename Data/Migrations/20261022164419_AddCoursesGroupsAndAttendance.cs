using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduHisob.Migrations
{
    /// <inheritdoc />
    public partial class AddCoursesGroupsAndAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Students_OrganizationId_Id",
                table: "Students",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                    table.UniqueConstraint("AK_Courses_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Courses_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudyGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    BranchId = table.Column<int>(type: "INTEGER", nullable: false),
                    CourseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    TeacherName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RoomName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DaysOfWeek = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    StartTime = table.Column<string>(type: "TEXT", nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    MonthlyFee = table.Column<decimal>(type: "TEXT", nullable: false),
                    LessonsPerMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    LessonDurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyGroups", x => x.Id);
                    table.UniqueConstraint("AK_StudyGroups_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_StudyGroups_Branches_OrganizationId_BranchId",
                        columns: x => new { x.OrganizationId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudyGroups_Courses_OrganizationId_CourseId",
                        columns: x => new { x.OrganizationId, x.CourseId },
                        principalTable: "Courses",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroupEnrollments",
                columns: table => new
                {
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    StudyGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    StudentId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnrolledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupEnrollments", x => new { x.OrganizationId, x.StudyGroupId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_GroupEnrollments_Students_OrganizationId_StudentId",
                        columns: x => new { x.OrganizationId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupEnrollments_StudyGroups_OrganizationId_StudyGroupId",
                        columns: x => new { x.OrganizationId, x.StudyGroupId },
                        principalTable: "StudyGroups",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    StudyGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    StudentId = table.Column<int>(type: "INTEGER", nullable: false),
                    LessonDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_GroupEnrollments_OrganizationId_StudyGroupId_StudentId",
                        columns: x => new { x.OrganizationId, x.StudyGroupId, x.StudentId },
                        principalTable: "GroupEnrollments",
                        principalColumns: new[] { "OrganizationId", "StudyGroupId", "StudentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_OrganizationId_StudyGroupId_StudentId_LessonDate",
                table: "AttendanceRecords",
                columns: new[] { "OrganizationId", "StudyGroupId", "StudentId", "LessonDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_OrganizationId_Name",
                table: "Courses",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupEnrollments_OrganizationId_StudentId",
                table: "GroupEnrollments",
                columns: new[] { "OrganizationId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroups_OrganizationId_BranchId_Name",
                table: "StudyGroups",
                columns: new[] { "OrganizationId", "BranchId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroups_OrganizationId_CourseId",
                table: "StudyGroups",
                columns: new[] { "OrganizationId", "CourseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceRecords");

            migrationBuilder.DropTable(
                name: "GroupEnrollments");

            migrationBuilder.DropTable(
                name: "StudyGroups");

            migrationBuilder.DropTable(
                name: "Courses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Students_OrganizationId_Id",
                table: "Students");
        }
    }
}
