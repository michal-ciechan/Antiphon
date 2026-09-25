using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDedicatedLandWorktree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanonicalAdvanceReason",
                table: "AgentTaskLandings",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanonicalAdvanceStartedAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanonicalAdvancedAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LandWorkspaceReadyAt",
                table: "AgentTaskLandings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LandWorktreePath",
                table: "AgentTaskLandings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalTargetBeforeSha",
                table: "AgentTaskLandings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLocalSha",
                table: "AgentTaskLandings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanonicalAdvanceReason",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "CanonicalAdvanceStartedAt",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "CanonicalAdvancedAt",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "LandWorkspaceReadyAt",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "LandWorktreePath",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "LocalTargetBeforeSha",
                table: "AgentTaskLandings");

            migrationBuilder.DropColumn(
                name: "SourceLocalSha",
                table: "AgentTaskLandings");
        }
    }
}
