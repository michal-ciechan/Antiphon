using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTranscriptHotPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TranscriptEntries_AgentSessionId_Uuid",
                table: "TranscriptEntries",
                columns: new[] { "AgentSessionId", "Uuid" },
                filter: "\"Uuid\" IS NOT NULL")
                .Annotation("Npgsql:CreatedConcurrently", true)
                .Annotation("Npgsql:IndexInclude", new[] { "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptEntries_End_AgentSessionId_Sequence",
                table: "TranscriptEntries",
                columns: new[] { "AgentSessionId", "Sequence" },
                filter: "\"Kind\" IN ('TurnEnd', 'SessionRestartBoundary')\nOR (\"Kind\" = 'CompactBoundary' AND \"Text\" IS NOT NULL AND strpos(\"Text\", '(manual)') > 0)\nOR (\"Kind\" = 'UserPrompt' AND \"Text\" IS NOT NULL AND \"Text\" LIKE '[Request interrupted%')")
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptEntries_End_AgentSessionId_Timestamp",
                table: "TranscriptEntries",
                columns: new[] { "AgentSessionId", "Timestamp" },
                filter: "(\"Kind\" IN ('TurnEnd', 'SessionRestartBoundary')\nOR (\"Kind\" = 'CompactBoundary' AND \"Text\" IS NOT NULL AND strpos(\"Text\", '(manual)') > 0)\nOR (\"Kind\" = 'UserPrompt' AND \"Text\" IS NOT NULL AND \"Text\" LIKE '[Request interrupted%')) AND \"Timestamp\" IS NOT NULL")
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TranscriptEntries_AgentSessionId_Uuid",
                table: "TranscriptEntries");

            migrationBuilder.DropIndex(
                name: "IX_TranscriptEntries_End_AgentSessionId_Sequence",
                table: "TranscriptEntries");

            migrationBuilder.DropIndex(
                name: "IX_TranscriptEntries_End_AgentSessionId_Timestamp",
                table: "TranscriptEntries");
        }
    }
}
