using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class Card0418ChannelOutbound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OutboundDeliveryId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutboundAgentProfile",
                table: "ChatChannels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OutboundDeliveryId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelOutboundDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromptSequence = table.Column<long>(type: "bigint", nullable: true),
                    TextWindowStart = table.Column<long>(type: "bigint", nullable: true),
                    TextWindowEnd = table.Column<long>(type: "bigint", nullable: true),
                    SendKind = table.Column<int>(type: "integer", nullable: false),
                    ChannelProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReplyHandle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReplyToMessageId = table.Column<string>(type: "text", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeadlineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConversionTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProfileSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FrozenReplyJson = table.Column<string>(type: "text", nullable: true),
                    OutputManifestJson = table.Column<string>(type: "text", nullable: true),
                    SealedPayloadJson = table.Column<string>(type: "text", nullable: true),
                    SealedPayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublishAttempts = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceComplete = table.Column<bool>(type: "boolean", nullable: false),
                    ConversionSucceeded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelOutboundDeliveries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_OutboundDeliveryId",
                table: "SessionQueuedMessages",
                column: "OutboundDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_OutboundDeliveryId",
                table: "AgentTasks",
                column: "OutboundDeliveryId",
                unique: true,
                filter: "\"OutboundDeliveryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_ConversionTaskId",
                table: "ChannelOutboundDeliveries",
                column: "ConversionTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_SourceKey",
                table: "ChannelOutboundDeliveries",
                column: "SourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_State_CreatedAt",
                table: "ChannelOutboundDeliveries",
                columns: new[] { "State", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelOutboundDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_OutboundDeliveryId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_OutboundDeliveryId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "OutboundDeliveryId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "OutboundAgentProfile",
                table: "ChatChannels");

            migrationBuilder.DropColumn(
                name: "OutboundDeliveryId",
                table: "AgentTasks");
        }
    }
}
