using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduHisob.Data.Migrations.PostgreSQL
{
    /// <inheritdoc />
    public partial class AddFinancialIncomeAndStudentPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StudentId",
                table: "FinancialTransactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "FinancialTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_OrganizationId_StudentId",
                table: "FinancialTransactions",
                columns: new[] { "OrganizationId", "StudentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialTransactions_Students_OrganizationId_StudentId",
                table: "FinancialTransactions",
                columns: new[] { "OrganizationId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinancialTransactions_Students_OrganizationId_StudentId",
                table: "FinancialTransactions");

            migrationBuilder.DropIndex(
                name: "IX_FinancialTransactions_OrganizationId_StudentId",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "StudentId",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "FinancialTransactions");
        }
    }
}
