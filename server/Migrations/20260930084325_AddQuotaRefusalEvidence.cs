using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotaRefusalEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiErrorTimeZoneId",
                table: "TranscriptEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResetAtUtc",
                table: "ApiErrorRecoveries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiErrorTimeZoneId",
                table: "TranscriptEntries");

            migrationBuilder.DropColumn(
                name: "ResetAtUtc",
                table: "ApiErrorRecoveries");
        }
    }
}
