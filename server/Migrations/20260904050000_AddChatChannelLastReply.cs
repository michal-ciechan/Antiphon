using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0338 S4: stamp the last outbound channel reply separately from inbound
    /// LastMessageAt / LastAuthor / LastChannelMessageId. Hand-written (running
    /// daemons lock bin/); the [DbContext]/[Migration] attributes normally generated
    /// into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260904050000_AddChatChannelLastReply")]
    public partial class AddChatChannelLastReply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastReplyAt",
                table: "ChatChannels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastReplyPreview",
                table: "ChatChannels",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReplyAt",
                table: "ChatChannels");

            migrationBuilder.DropColumn(
                name: "LastReplyPreview",
                table: "ChatChannels");
        }
    }
}
