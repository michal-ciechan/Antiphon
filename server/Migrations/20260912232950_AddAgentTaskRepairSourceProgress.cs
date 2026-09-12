using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>CARD-0499. Repair source identity and progress snapshots. No backfill.</summary>
    public partial class AddAgentTaskRepairSourceProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompletionProgressEvidenceJson",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProgressBaselineJson",
                table: "AgentTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RepairSourceTaskId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionProgressEvidenceJson",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ProgressBaselineJson",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RepairSourceTaskId",
                table: "AgentTasks");
        }
    }
}
