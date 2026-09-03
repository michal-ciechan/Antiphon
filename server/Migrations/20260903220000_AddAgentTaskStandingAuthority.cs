using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0294 S1: AgentTasks.StandingAuthority, AutoContinueOnWait, AutoContinuedAt.
    /// AutoContinue columns ship with this migration so S3 does not need a second one.
    /// No backfill — pre-existing rows have no standing authority.
    /// Hand-written (running daemons lock bin/); the [DbContext]/[Migration] attributes
    /// normally generated into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260903220000_AddAgentTaskStandingAuthority")]
    public partial class AddAgentTaskStandingAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StandingAuthority",
                table: "AgentTasks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoContinueOnWait",
                table: "AgentTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoContinuedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StandingAuthority",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "AutoContinueOnWait",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "AutoContinuedAt",
                table: "AgentTasks");
        }
    }
}
