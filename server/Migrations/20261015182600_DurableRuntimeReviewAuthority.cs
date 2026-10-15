using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class DurableRuntimeReviewAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "LateConfirmedChannelMessageIds",
                table: "SessionRuntimeActions",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewReplyConsumedAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewThreadId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuntimeActionKey",
                table: "SessionQueuedMessages",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_AgentSessionId_ReviewThreadId",
                table: "SessionQueuedMessages",
                columns: new[] { "AgentSessionId", "ReviewThreadId" },
                filter: "\"ReviewThreadId\" IS NOT NULL AND \"ReviewReplyConsumedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_AgentSessionId_RuntimeActionKey",
                table: "SessionQueuedMessages",
                columns: new[] { "AgentSessionId", "RuntimeActionKey" },
                unique: true,
                filter: "\"RuntimeActionKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_AgentSessionId_ReviewThreadId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_AgentSessionId_RuntimeActionKey",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "LateConfirmedChannelMessageIds",
                table: "SessionRuntimeActions");

            migrationBuilder.DropColumn(
                name: "ReviewReplyConsumedAt",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "ReviewThreadId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "RuntimeActionKey",
                table: "SessionQueuedMessages");
        }
    }
}
