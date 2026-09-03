using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0340 S3 / CARD-0342: nullable SessionQueuedMessages.DeliveryVerdict and
    /// DeliveryVerdictAt. Generated with <c>dotnet ef migrations add</c>. No backfill.
    /// </summary>
    public partial class AddSessionQueuedMessageDeliveryVerdict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeliveryVerdict",
                table: "SessionQueuedMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryVerdictAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryVerdict",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryVerdictAt",
                table: "SessionQueuedMessages");
        }
    }
}
