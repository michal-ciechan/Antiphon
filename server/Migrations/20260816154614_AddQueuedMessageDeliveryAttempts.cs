using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddQueuedMessageDeliveryAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeliveryAttempts",
                table: "SessionQueuedMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LastDeliveryBaselineSequence",
                table: "SessionQueuedMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDeliveryStartedAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryAttempts",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "LastDeliveryBaselineSequence",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "LastDeliveryStartedAt",
                table: "SessionQueuedMessages");
        }
    }
}
