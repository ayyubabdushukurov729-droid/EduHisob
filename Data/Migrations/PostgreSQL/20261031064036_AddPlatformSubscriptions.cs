using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EduHisob.Data.Migrations.PostgreSQL
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
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyFee",
                table: "Organizations",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidUntilUtc",
                table: "Organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlatformNote",
                table: "Organizations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsPlatformAdmin",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PlatformPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CenterId = table.Column<int>(type: "integer", nullable: false),
                    CenterName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
