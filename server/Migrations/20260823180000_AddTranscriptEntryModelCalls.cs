using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0157 S1: carry Grok turn_completed.usage.modelCalls onto TranscriptEntry.
    /// Hand-written (running daemons lock bin/); snapshot is updated to match. Nullable,
    /// no backfill — null means pre-carriage or non-Grok.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260823180000_AddTranscriptEntryModelCalls")]
    public partial class AddTranscriptEntryModelCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ModelCalls",
                table: "TranscriptEntries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelCalls",
                table: "TranscriptEntries");
        }
    }
}
