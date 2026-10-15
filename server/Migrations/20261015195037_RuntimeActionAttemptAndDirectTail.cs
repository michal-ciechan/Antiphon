using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeActionAttemptAndDirectTail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AcceptedTaskAttempt",
                table: "SessionRuntimeActions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcceptedTaskId",
                table: "SessionRuntimeActions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DirectReplyConversationId",
                table: "SessionQueuedMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DirectReplyHandle",
                table: "SessionQueuedMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DirectReplyPendingThroughSequence",
                table: "SessionQueuedMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DirectReplyPromptSequence",
                table: "SessionQueuedMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DirectReplyProvider",
                table: "SessionQueuedMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DirectReplyTailClosedAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DirectReplyThroughSequence",
                table: "SessionQueuedMessages",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedTaskAttempt",
                table: "SessionRuntimeActions");

            migrationBuilder.DropColumn(
                name: "AcceptedTaskId",
                table: "SessionRuntimeActions");

            migrationBuilder.DropColumn(
                name: "DirectReplyConversationId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "DirectReplyHandle",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "DirectReplyPendingThroughSequence",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "DirectReplyPromptSequence",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "DirectReplyProvider",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "DirectReplyTailClosedAt",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "DirectReplyThroughSequence",
                table: "SessionQueuedMessages");
        }
    }
}
