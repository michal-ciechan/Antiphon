using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0146 S2: AgentTasks.NextStage and NextHandoff, parsed from the
    /// <c>--- next stage ---</c> block at settlement. No backfill — pre-existing
    /// rows have no handoff. Hand-written (running daemons lock bin/); the
    /// [DbContext]/[Migration] attributes normally generated into the Designer
    /// live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260904060000_AddAgentTaskNextStage")]
    public partial class AddAgentTaskNextStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NextHandoff",
                table: "AgentTasks",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NextStage",
                table: "AgentTasks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextHandoff",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "NextStage",
                table: "AgentTasks");
        }
    }
}
