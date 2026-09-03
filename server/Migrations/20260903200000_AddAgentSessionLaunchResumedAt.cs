using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0340: nullable AgentSessions.LaunchResumedAt. Hand-written (running daemons lock
    /// bin/); the [DbContext]/[Migration] attributes normally generated into the Designer live
    /// here. Snapshot is updated to match. No backfill.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260903200000_AddAgentSessionLaunchResumedAt")]
    public partial class AddAgentSessionLaunchResumedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LaunchResumedAt",
                table: "AgentSessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LaunchResumedAt",
                table: "AgentSessions");
        }
    }
}
