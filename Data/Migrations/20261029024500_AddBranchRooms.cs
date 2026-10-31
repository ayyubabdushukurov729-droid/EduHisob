using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduHisob.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoomId",
                table: "StudyGroups",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BranchRooms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<int>(type: "INTEGER", nullable: false),
                    BranchId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchRooms", x => x.Id);
                    table.UniqueConstraint("AK_BranchRooms_OrganizationId_BranchId_Id", x => new { x.OrganizationId, x.BranchId, x.Id });
                    table.ForeignKey(
                        name: "FK_BranchRooms_Branches_OrganizationId_BranchId",
                        columns: x => new { x.OrganizationId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroups_OrganizationId_BranchId_RoomId",
                table: "StudyGroups",
                columns: new[] { "OrganizationId", "BranchId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchRooms_OrganizationId_BranchId_Name",
                table: "BranchRooms",
                columns: new[] { "OrganizationId", "BranchId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StudyGroups_BranchRooms_OrganizationId_BranchId_RoomId",
                table: "StudyGroups",
                columns: new[] { "OrganizationId", "BranchId", "RoomId" },
                principalTable: "BranchRooms",
                principalColumns: new[] { "OrganizationId", "BranchId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudyGroups_BranchRooms_OrganizationId_BranchId_RoomId",
                table: "StudyGroups");

            migrationBuilder.DropTable(
                name: "BranchRooms");

            migrationBuilder.DropIndex(
                name: "IX_StudyGroups_OrganizationId_BranchId_RoomId",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "StudyGroups");
        }
    }
}
