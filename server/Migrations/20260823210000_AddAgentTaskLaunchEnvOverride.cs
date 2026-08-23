using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0106 gap 1: persist a task's launch-time env overlay so async dispatch and a
    /// task-session relaunch re-apply it. Hand-written (running daemons lock bin/); the
    /// [DbContext]/[Migration] attributes normally generated into the Designer live here.
    /// Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260823210000_AddAgentTaskLaunchEnvOverride")]
    public partial class AddAgentTaskLaunchEnvOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LaunchEnvOverrideJson",
                table: "AgentTasks",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LaunchEnvOverrideJson",
                table: "AgentTasks");
        }
    }
}
