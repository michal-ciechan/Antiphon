using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPolledCompletionNoteShrink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentDigest",
                table: "SessionQueuedMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoteHeader",
                table: "SessionQueuedMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceTaskId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPolledResultAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPolledResultHash",
                table: "AgentTasks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentDigest",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "NoteHeader",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "SourceTaskId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "LastPolledResultAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "LastPolledResultHash",
                table: "AgentTasks");
        }
    }
}
