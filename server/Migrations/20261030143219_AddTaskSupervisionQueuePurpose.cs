using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskSupervisionQueuePurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceTaskSupervisionNoticeId",
                table: "SessionQueuedMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_SourceTaskSupervisionNoticeId",
                table: "SessionQueuedMessages",
                column: "SourceTaskSupervisionNoticeId",
                unique: true,
                filter: "\"SourceTaskSupervisionNoticeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_SourceTaskSupervisionNoticeId",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "SourceTaskSupervisionNoticeId",
                table: "SessionQueuedMessages");
        }
    }
}
