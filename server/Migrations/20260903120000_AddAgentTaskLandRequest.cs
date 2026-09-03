using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0331: durable land-request columns on AgentTasks. The channel is a hand-off;
    /// LandRequestedAt is the queue. Hand-written (running daemons lock bin/); the
    /// [DbContext]/[Migration] attributes normally generated into the Designer live here.
    /// Snapshot is updated to match. No data backfill (D5).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260903120000_AddAgentTaskLandRequest")]
    public partial class AddAgentTaskLandRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LandAttempt",
                table: "AgentTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LandRequestedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LandStartedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LandVerifyFilter",
                table: "AgentTasks",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_LandRequestedAt",
                table: "AgentTasks",
                column: "LandRequestedAt",
                filter: "\"LandRequestedAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_LandRequestedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "LandAttempt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "LandRequestedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "LandStartedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "LandVerifyFilter",
                table: "AgentTasks");
        }
    }
}
