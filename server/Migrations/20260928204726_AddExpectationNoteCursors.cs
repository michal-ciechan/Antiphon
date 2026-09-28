using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddExpectationNoteCursors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NoteOutboxCursorAt",
                table: "ExpectationWatchStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NoteOutboxCursorId",
                table: "ExpectationWatchStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NoteQueueCursorAt",
                table: "ExpectationWatchStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NoteQueueCursorId",
                table: "ExpectationWatchStates",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoteOutboxCursorAt",
                table: "ExpectationWatchStates");

            migrationBuilder.DropColumn(
                name: "NoteOutboxCursorId",
                table: "ExpectationWatchStates");

            migrationBuilder.DropColumn(
                name: "NoteQueueCursorAt",
                table: "ExpectationWatchStates");

            migrationBuilder.DropColumn(
                name: "NoteQueueCursorId",
                table: "ExpectationWatchStates");
        }
    }
}
