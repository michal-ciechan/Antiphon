using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionQueuedMessageWithholdRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstWithheldAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastWithheldAt",
                table: "SessionQueuedMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastWithholdReason",
                table: "SessionQueuedMessages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WithholdCount",
                table: "SessionQueuedMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstWithheldAt",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "LastWithheldAt",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "LastWithholdReason",
                table: "SessionQueuedMessages");

            migrationBuilder.DropColumn(
                name: "WithholdCount",
                table: "SessionQueuedMessages");
        }
    }
}
