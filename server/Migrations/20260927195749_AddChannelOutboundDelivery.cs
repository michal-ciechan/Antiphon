using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelOutboundDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChannelOutboundDeliveryId",
                table: "SessionQueuedMessages",
                type: "uuid",
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
                    SourceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    InboundAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptSequence = table.Column<long>(type: "bigint", nullable: false),
                    FirstTextSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastTextSequence = table.Column<long>(type: "bigint", nullable: false),
                    SendKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConverterAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptRevision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptText = table.Column<string>(type: "text", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaxPending = table.Column<int>(type: "integer", nullable: false),
                    InputPath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    InputSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OutputPath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    OutputSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConversionTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeadlineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublicationAttempts = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConversionOutcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelOutboundDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelOutboundDeliveries_ChatChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "ChatChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_ChannelOutboundDeliveryId",
                table: "SessionQueuedMessages",
                column: "ChannelOutboundDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_OutboundDeliveryId",
                table: "AgentTasks",
                column: "OutboundDeliveryId",
                unique: true,
                filter: "\"OutboundDeliveryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_ChannelId_CreatedAt",
                table: "ChannelOutboundDeliveries",
                columns: new[] { "ChannelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_SourceKey",
                table: "ChannelOutboundDeliveries",
                column: "SourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundDeliveries_State_LeaseUntil",
                table: "ChannelOutboundDeliveries",
                columns: new[] { "State", "LeaseUntil" });

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTasks_ChannelOutboundDeliveries_OutboundDeliveryId",
                table: "AgentTasks",
                column: "OutboundDeliveryId",
                principalTable: "ChannelOutboundDeliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SessionQueuedMessages_ChannelOutboundDeliveries_ChannelOutb~",
                table: "SessionQueuedMessages",
                column: "ChannelOutboundDeliveryId",
                principalTable: "ChannelOutboundDeliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentTasks_ChannelOutboundDeliveries_OutboundDeliveryId",
                table: "AgentTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_SessionQueuedMessages_ChannelOutboundDeliveries_ChannelOutb~",
                table: "SessionQueuedMessages");

            migrationBuilder.DropTable(
                name: "ChannelOutboundDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_ChannelOutboundDeliveryId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_OutboundDeliveryId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ChannelOutboundDeliveryId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "OutboundDeliveryId",
                table: "AgentTasks");
        }
    }
}
