using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionExitSignalEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExitSignal",
                table: "RunAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExitSignal",
                table: "AgentSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExitStopIntentJson",
                table: "AgentSessions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExitSignal",
                table: "AgentIncidents",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExitSignal",
                table: "RunAttempts");

            migrationBuilder.DropColumn(
                name: "ExitSignal",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "ExitStopIntentJson",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "ExitSignal",
                table: "AgentIncidents");
        }
    }
}
