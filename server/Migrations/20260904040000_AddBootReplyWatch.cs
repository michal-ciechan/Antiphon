using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0312 S1/S4: AgentSessions.BootPromptSequence / BootReplyDueAt and
    /// AgentSupervisionStates.LivenessLatchedAt. Hand-written (running daemons lock bin/); the
    /// [DbContext]/[Migration] attributes normally generated into the Designer live here, and the
    /// snapshot is updated to match. All three are nullable with no default and no backfill: null
    /// means "no watch armed" / "not latched", which is the correct reading of every legacy row,
    /// so the change is inert until a launch arms one.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260904040000_AddBootReplyWatch")]
    public partial class AddBootReplyWatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BootPromptSequence",
                table: "AgentSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BootReplyDueAt",
                table: "AgentSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LivenessLatchedAt",
                table: "AgentSupervisionStates",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BootPromptSequence",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "BootReplyDueAt",
                table: "AgentSessions");

            migrationBuilder.DropColumn(
                name: "LivenessLatchedAt",
                table: "AgentSupervisionStates");
        }
    }
}
