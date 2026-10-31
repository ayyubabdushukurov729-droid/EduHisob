using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduHisob.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockReason",
                table: "Organizations",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "Organizations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyFee",
                table: "Organizations",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidUntilUtc",
                table: "Organizations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlatformNote",
                table: "Organizations",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsPlatformAdmin",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PlatformPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CenterId = table.Column<int>(type: "INTEGER", nullable: false),
                    CenterName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Months = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformPayments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformPayments_CenterId",
                table: "PlatformPayments",
                column: "CenterId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformPayments_PaidAtUtc",
                table: "PlatformPayments",
                column: "PaidAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformPayments");

            migrationBuilder.DropColumn(
                name: "BlockReason",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "MonthlyFee",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "PaidUntilUtc",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "PlatformNote",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "IsPlatformAdmin",
                table: "AspNetUsers");
        }
    }
}
