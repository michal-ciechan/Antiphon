using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Messaging.Service.Migrations
{
    /// <inheritdoc />
    public partial class ScopeInboxIdentityToConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep the existing uniqueness guard until its replacement exists.
            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Channel_ConversationId_ChannelMessageId",
                table: "Inbox",
                columns: new[] { "Channel", "ConversationId", "ChannelMessageId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_Inbox_Channel_ChannelMessageId",
                table: "Inbox");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cross-chat collisions cannot fit the old key. Refuse transactionally,
            // preserving all rows and the new guard, rather than deleting messages.
            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Channel_ChannelMessageId",
                table: "Inbox",
                columns: new[] { "Channel", "ChannelMessageId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_Inbox_Channel_ConversationId_ChannelMessageId",
                table: "Inbox");
        }
    }
}
