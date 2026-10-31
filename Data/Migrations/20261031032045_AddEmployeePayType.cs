using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduHisob.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeePayType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DefaultSharePercent",
                table: "Employees",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PayType",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Mavjud yozuvlar: oddiy xodim — faqat oylik, oyligi nol ustoz — faqat foiz. Qolgan ustozlar oylik + foiz.
            migrationBuilder.Sql("UPDATE Employees SET PayType = 1 WHERE Role = 1;");
            migrationBuilder.Sql("UPDATE Employees SET PayType = 2 WHERE Role = 0 AND CAST(AgreedMonthlySalary AS REAL) = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultSharePercent",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PayType",
                table: "Employees");
        }
    }
}
