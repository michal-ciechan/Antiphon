using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockedTaskParkLegacyReclaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LegacyDiscovery",
                table: "AgentTaskParks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BlockedTaskParkReclaimCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    AfterTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedTaskParkReclaimCursors", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockedTaskParkReclaimCursors");

            migrationBuilder.DropColumn(
                name: "LegacyDiscovery",
                table: "AgentTaskParks");
        }
    }
}
