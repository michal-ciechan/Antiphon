using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class CloseTaskSupervisionReportWatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextReportScanAt",
                table: "TaskSupervisionNotices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportScanAttempts",
                table: "TaskSupervisionNotices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportWatchCloseReason",
                table: "TaskSupervisionNotices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportWatchClosedAt",
                table: "TaskSupervisionNotices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSupervisionNotices_NextReportScanAt_Id",
                table: "TaskSupervisionNotices",
                columns: new[] { "NextReportScanAt", "Id" },
                filter: "\"Purpose\" = 1 AND \"ReportWatchClosedAt\" IS NULL AND \"ReportForwardedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSupervisionNotices_RecipientSessionId",
                table: "TaskSupervisionNotices",
                column: "RecipientSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSupervisionNotices_SessionId",
                table: "TaskSupervisionNotices",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskSupervisionNotices_NextReportScanAt_Id",
                table: "TaskSupervisionNotices");

            migrationBuilder.DropIndex(
                name: "IX_TaskSupervisionNotices_RecipientSessionId",
                table: "TaskSupervisionNotices");

            migrationBuilder.DropIndex(
                name: "IX_TaskSupervisionNotices_SessionId",
                table: "TaskSupervisionNotices");

            migrationBuilder.DropColumn(
                name: "NextReportScanAt",
                table: "TaskSupervisionNotices");

            migrationBuilder.DropColumn(
                name: "ReportScanAttempts",
                table: "TaskSupervisionNotices");

            migrationBuilder.DropColumn(
                name: "ReportWatchCloseReason",
                table: "TaskSupervisionNotices");

            migrationBuilder.DropColumn(
                name: "ReportWatchClosedAt",
                table: "TaskSupervisionNotices");
        }
    }
}
