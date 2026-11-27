using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Antiphon.Messaging.Service.Migrations;

[DbContext(typeof(MessagingDbContext))]
[Migration("20261127101000_AddInboxAnswerReceipts")]
public sealed class AddInboxAnswerReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE "InboxAnswerReceipts" (
            "Channel" text NOT NULL, "ConversationId" text NOT NULL, "ChannelMessageId" text NOT NULL,
            "AnsweredAt" timestamp with time zone NOT NULL, "OutboundMessageId" text,
            CONSTRAINT "PK_InboxAnswerReceipts" PRIMARY KEY ("Channel", "ConversationId", "ChannelMessageId"));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("InboxAnswerReceipts");
}
