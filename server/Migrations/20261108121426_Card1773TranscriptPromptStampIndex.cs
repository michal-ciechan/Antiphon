using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class Card1773TranscriptPromptStampIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same recovery as CARD-0698. A timed-out CREATE INDEX CONCURRENTLY leaves an INVALID
            // index and no history row; the next startup must rename, drop, and create again.
            AddTranscriptHotPathIndexes.CreateRetryableIndex(migrationBuilder,
                name: "IX_TranscriptEntries_AgentSessionId_Kind_Sequence",
                columns: "\"AgentSessionId\", \"Kind\", \"Sequence\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Additive. Dropping the index loses no rows. CONCURRENTLY cannot run inside the
            // migration transaction, and IF EXISTS keeps a second downgrade from throwing.
            // The cleanup name is what an interrupted retry leaves behind.
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_TranscriptEntries_AgentSessionId_Kind_Sequence";""",
                suppressTransaction: true);
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_TranscriptEntries_AgentSessionId_Kind_Sequence_invalid";""",
                suppressTransaction: true);
        }
    }
}
