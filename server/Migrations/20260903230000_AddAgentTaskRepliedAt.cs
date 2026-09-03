using System;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0348: AgentTasks.RepliedAt and RepliedAtSequence.
    /// RepliedAt is the elapsed-clock stamp (both reply paths). RepliedAtSequence is the
    /// transcript high-water mark stamped only by Blocked → Working so settlement refuses
    /// the pre-reply turn. No backfill — overlay vs Blocked-reply cannot be told apart
    /// from Replied events, and pre-deploy replies are past the stale-resettle window.
    /// Hand-written (running daemons lock bin/); the [DbContext]/[Migration] attributes
    /// normally generated into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260903230000_AddAgentTaskRepliedAt")]
    public partial class AddAgentTaskRepliedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedAt",
                table: "AgentTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RepliedAtSequence",
                table: "AgentTasks",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepliedAt",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "RepliedAtSequence",
                table: "AgentTasks");
        }
    }
}
