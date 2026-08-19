using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentAutoCompactOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoCompactContextPercent",
                table: "Agents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoCompactEnabled",
                table: "Agents",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutoCompactIdleMinutes",
                table: "Agents",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoCompactContextPercent",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "AutoCompactEnabled",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "AutoCompactIdleMinutes",
                table: "Agents");
        }
    }
}
