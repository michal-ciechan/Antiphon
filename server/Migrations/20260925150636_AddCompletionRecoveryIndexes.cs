using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletionRecoveryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_CompletionWakeup",
                table: "SessionQueuedMessages",
                column: "Id",
                filter: "\"Status\" = 0 AND \"DeliveryAttempts\" = 0 AND \"SourceTaskId\" IS NOT NULL AND \"ContentDigest\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_MissingCompletionNote",
                table: "AgentTasks",
                column: "Id",
                filter: "\"CompletionNoteQueuedAt\" IS NULL AND \"SourceLandingOperationId\" IS NOT NULL AND \"ParentSessionId\" IS NOT NULL AND \"ReplyTo\" = 1 AND \"Result\" IS NOT NULL AND \"Status\" IN (4, 5, 6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_CompletionWakeup",
                table: "SessionQueuedMessages");

            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_MissingCompletionNote",
                table: "AgentTasks");
        }
    }
}
