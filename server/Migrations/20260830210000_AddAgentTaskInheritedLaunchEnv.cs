using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0260 S1: persist the caller's LLM-routing env snapshot on the task row so every
    /// dispatch path (tick, retry, relaunch) re-applies it. Hand-written (running daemons lock
    /// bin/); the [DbContext]/[Migration] attributes normally generated into the Designer live
    /// here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260830210000_AddAgentTaskInheritedLaunchEnv")]
    public partial class AddAgentTaskInheritedLaunchEnv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InheritedLaunchEnvJson",
                table: "AgentTasks",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InheritedLaunchEnvJson",
                table: "AgentTasks");
        }
    }
}
