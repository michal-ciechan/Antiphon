using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLandNotificationMonitoringConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "AgentTaskLandNotifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "ErrorAt",
                table: "AgentTaskLandNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WarningAt",
                table: "AgentTaskLandNotifications",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "AgentTaskLandNotifications");

            migrationBuilder.DropColumn(
                name: "ErrorAt",
                table: "AgentTaskLandNotifications");

            migrationBuilder.DropColumn(
                name: "WarningAt",
                table: "AgentTaskLandNotifications");
        }
    }
}
