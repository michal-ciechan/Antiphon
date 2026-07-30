using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    // Hand-written (no .Designer.cs) — same bin-lock reason as the migrations before it.
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260730080000_AddTranscriptTokenUsage")]
    public partial class AddTranscriptTokenUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiCallId",
                table: "TranscriptEntries",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "TranscriptEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "TranscriptEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheReadTokens",
                table: "TranscriptEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheCreationTokens",
                table: "TranscriptEntries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ApiCallId", table: "TranscriptEntries");
            migrationBuilder.DropColumn(name: "InputTokens", table: "TranscriptEntries");
            migrationBuilder.DropColumn(name: "OutputTokens", table: "TranscriptEntries");
            migrationBuilder.DropColumn(name: "CacheReadTokens", table: "TranscriptEntries");
            migrationBuilder.DropColumn(name: "CacheCreationTokens", table: "TranscriptEntries");
        }
    }
}
