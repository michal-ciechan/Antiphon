using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelReplySettledAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChannelReplySettledAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            // CARD-0067. Every Channel-origin row that predates this column belongs to a round trip
            // that is already over: its reply route lived in process memory and died with whatever
            // process held it. Backfilling them as settled is what keeps the deploy safe in BOTH
            // directions — an unsettled historical row would let the first turn after deploy answer a
            // days-old prompt into a live family chat, and the TTL sweep would raise a Critical
            // incident for every channel message the deployment has ever handled. Only correlations
            // created from here on are in play. Origin = 1 is QueuedMessageOrigin.Channel.
            migrationBuilder.Sql(
                """
                UPDATE "SessionQueuedMessages"
                SET "ChannelReplySettledAt" = "CreatedAt"
                WHERE "Origin" = 1 AND "ChannelReplySettledAt" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_OpenChannelCorrelations",
                table: "SessionQueuedMessages",
                columns: new[] { "Origin", "Status" },
                filter: "\"ChannelReplySettledAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_OpenChannelCorrelations",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "ChannelReplySettledAt",
                table: "SessionQueuedMessages");
        }
    }
}
