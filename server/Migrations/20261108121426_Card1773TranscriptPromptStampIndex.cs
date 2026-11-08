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
            migrationBuilder.CreateIndex(
                name: "IX_TranscriptEntries_AgentSessionId_Kind_Sequence",
                table: "TranscriptEntries",
                columns: new[] { "AgentSessionId", "Kind", "Sequence" })
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Additive. Dropping the index loses no rows. CONCURRENTLY cannot run inside the
            // migration transaction, and IF EXISTS keeps a second downgrade from throwing.
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_TranscriptEntries_AgentSessionId_Kind_Sequence";""",
                suppressTransaction: true);
        }
    }
}
