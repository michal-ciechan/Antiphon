using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Messaging.Service.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MessagingDbContext))]
    [Migration("20260830190000_AddInboxLagMetadata")]
    public partial class AddInboxLagMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Topic",
                table: "Inbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Partition",
                table: "Inbox",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Offset",
                table: "Inbox",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcknowledgedAt",
                table: "Inbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OperationalEventPublishedAt",
                table: "Inbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAckAttemptAt",
                table: "Inbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AckAttemptCount",
                table: "Inbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Topic_Partition_Offset",
                table: "Inbox",
                columns: new[] { "Topic", "Partition", "Offset" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Inbox_Topic_Partition_Offset",
                table: "Inbox");

            migrationBuilder.DropColumn(name: "Topic", table: "Inbox");
            migrationBuilder.DropColumn(name: "Partition", table: "Inbox");
            migrationBuilder.DropColumn(name: "Offset", table: "Inbox");
            migrationBuilder.DropColumn(name: "AcknowledgedAt", table: "Inbox");
            migrationBuilder.DropColumn(name: "OperationalEventPublishedAt", table: "Inbox");
            migrationBuilder.DropColumn(name: "NextAckAttemptAt", table: "Inbox");
            migrationBuilder.DropColumn(name: "AckAttemptCount", table: "Inbox");
        }
    }
}
