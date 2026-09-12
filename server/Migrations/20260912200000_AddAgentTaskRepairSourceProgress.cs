using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0499: AgentTasks.RepairSourceTaskId, ProgressBaselineJson, CompletionProgressEvidenceJson.
    /// No backfill — pre-existing rows have no repair source or progress snapshot.
    /// Hand-written (running daemons lock bin/); snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260912200000_AddAgentTaskRepairSourceProgress")]
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
