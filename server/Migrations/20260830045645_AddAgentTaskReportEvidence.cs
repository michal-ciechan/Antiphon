using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskReportEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReportEvidence",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportNudgedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorktreeBaseSha",
                table: "AgentTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportEvidence",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ReportNudgedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBaseSha",
                table: "AgentTasks");
        }
    }
}
