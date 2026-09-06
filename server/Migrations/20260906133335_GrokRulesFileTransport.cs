using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class GrokRulesFileTransport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RulesAcknowledgedAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RulesBoundarySequence",
                table: "SessionQueuedMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RulesChainId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RulesCoveredByMessageId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RulesDeadlineAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RulesFailure",
                table: "SessionQueuedMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RulesFollowOnCount",
                table: "SessionQueuedMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "RulesPromptSequence",
                table: "SessionQueuedMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RulesReceiptJson",
                table: "SessionQueuedMessages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RulesRefreshKey",
                table: "SessionQueuedMessages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RulesTurnEndSequence",
                table: "SessionQueuedMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrokRulesExpectedByteCount",
                table: "AgentSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrokRulesExpectedSha256",
                table: "AgentSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrokRulesFailure",
                table: "AgentSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GrokRulesGeneration",
                table: "AgentSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GrokRulesLaunchTranscriptFloor",
                table: "AgentSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GrokRulesReadyAt",
                table: "AgentSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrokRulesReceiptJson",
                table: "AgentSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrokRulesState",
                table: "AgentSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_AgentSessionId_RulesRefreshKey",
                table: "SessionQueuedMessages",
                columns: new[] { "AgentSessionId", "RulesRefreshKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_AgentSessionId_RulesRefreshKey",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesAcknowledgedAt",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesBoundarySequence",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesChainId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesCoveredByMessageId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesDeadlineAt",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesFailure",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesFollowOnCount",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesPromptSequence",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesReceiptJson",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesRefreshKey",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RulesTurnEndSequence",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "GrokRulesExpectedByteCount",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "GrokRulesExpectedSha256",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "GrokRulesFailure",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "GrokRulesGeneration",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "GrokRulesLaunchTranscriptFloor",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "GrokRulesReadyAt",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "GrokRulesReceiptJson",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "GrokRulesState",
                table: "AgentSessions");
        }
    }
}
