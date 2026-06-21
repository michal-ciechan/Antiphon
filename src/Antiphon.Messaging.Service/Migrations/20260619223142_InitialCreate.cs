using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Messaging.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Inbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    ChannelMessageId = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<string>(type: "text", nullable: false),
                    ConversationTitle = table.Column<string>(type: "text", nullable: true),
                    AuthorDisplay = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    MentionsMe = table.Column<bool>(type: "boolean", nullable: false),
                    HasAttachments = table.Column<bool>(type: "boolean", nullable: false),
                    ReplyHandle = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EnvelopeJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Channel_ChannelMessageId",
                table: "Inbox",
                columns: new[] { "Channel", "ChannelMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Channel_Status",
                table: "Inbox",
                columns: new[] { "Channel", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_ReceivedAt",
                table: "Inbox",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Inbox");
        }
    }
}
