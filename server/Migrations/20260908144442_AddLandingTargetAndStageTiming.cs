using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLandingTargetAndStageTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CleanupCompletedAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreparedAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RebaseStartedAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCheckoutPath",
                table: "AgentTaskLandings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TargetCheckoutRecorded",
                table: "AgentTaskLandings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationStartedAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CleanupCompletedAt",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "PreparedAt",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "RebaseStartedAt",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "TargetCheckoutPath",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "TargetCheckoutRecorded",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "VerificationStartedAt",
                table: "AgentTaskLandings");
        }
    }
}
