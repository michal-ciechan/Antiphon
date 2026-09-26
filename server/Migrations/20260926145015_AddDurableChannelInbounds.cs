using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableChannelInbounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceChannelInboundId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelInbounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NativeMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EnvelopeJson = table.Column<string>(type: "text", nullable: true),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChatChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    QueueMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransferredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WakeTimeoutIncidentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelInbounds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_SourceChannelInboundId",
                table: "SessionQueuedMessages",
                column: "SourceChannelInboundId",
                unique: true,
                filter: "\"SourceChannelInboundId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelInbounds_AgentId_QueueMessageId_AcceptedAt",
                table: "ChannelInbounds",
                columns: new[] { "AgentId", "QueueMessageId", "AcceptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelInbounds_Provider_ConversationId_NativeMessageId",
                table: "ChannelInbounds",
                columns: new[] { "Provider", "ConversationId", "NativeMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelInbounds_QueueMessageId",
                table: "ChannelInbounds",
                column: "QueueMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelInbounds");

            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_SourceChannelInboundId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "SourceChannelInboundId",
                table: "SessionQueuedMessages");
        }
    }
}
