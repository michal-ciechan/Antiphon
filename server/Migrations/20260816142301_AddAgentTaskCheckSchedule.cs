using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskCheckSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CheckCount",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ExpectedDurationMinutes",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextCheckAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckCount",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "ExpectedDurationMinutes",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "NextCheckAt",
                table: "AgentTasks");
        }
    }
}
