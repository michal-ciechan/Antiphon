using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0407 S1: AgentTasks.InternalDecisionPolicyJson, InternalDecisionPolicyHash,
    /// InternalDecisionAuditBaselineJson. Null on every pre-existing row — no backfill.
    /// </summary>
    public partial class AddAgentTaskInternalDecisionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InternalDecisionAuditBaselineJson",
                table: "AgentTasks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalDecisionPolicyHash",
                table: "AgentTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalDecisionPolicyJson",
                table: "AgentTasks",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InternalDecisionAuditBaselineJson",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "InternalDecisionPolicyHash",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "InternalDecisionPolicyJson",
                table: "AgentTasks");
        }
    }
}
