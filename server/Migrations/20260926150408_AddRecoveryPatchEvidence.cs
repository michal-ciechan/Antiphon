using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoveryPatchEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RecoveryPatchesContained",
                table: "AgentTaskLandRequests",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryUncontainedPatches",
                table: "AgentTaskLandRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecoveryPatchesContained",
                table: "AgentTaskLandings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryUncontainedPatches",
                table: "AgentTaskLandings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecoveryPatchesContained",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryUncontainedPatches",
                table: "AgentTaskLandRequests");

            migrationBuilder.DropColumn(
                name: "RecoveryPatchesContained",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RecoveryUncontainedPatches",
                table: "AgentTaskLandings");
        }
    }
}
