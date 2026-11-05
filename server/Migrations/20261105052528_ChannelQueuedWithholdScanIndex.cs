using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class ChannelQueuedWithholdScanIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SessionQueuedMessages_AgedChannelWithhold",
                table: "SessionQueuedMessages",
                columns: new[] { "FirstWithheldAt", "Id" },
                filter: "\"Origin\" = 1 AND \"Status\" = 0 AND \"DeliveryAttempts\" = 0 AND \"LastDeliveryStartedAt\" IS NULL AND \"LastDeliveryBaselineSequence\" IS NULL AND \"DeliveryVerdict\" IS NULL AND \"WithholdCount\" > 0 AND \"FirstWithheldAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessionQueuedMessages_AgedChannelWithhold",
                table: "SessionQueuedMessages");
        }
    }
}
