using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCallerReportSettlementReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedReportCompletedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReceivedReportPromptSequence",
                table: "AgentTasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivedReportSessionId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceivedReportCompletedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ReceivedReportPromptSequence",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ReceivedReportSessionId",
                table: "AgentTasks");
        }
    }
}
