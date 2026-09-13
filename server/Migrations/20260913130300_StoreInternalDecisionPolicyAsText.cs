using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Antiphon.Server.Infrastructure.Data;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0407 S1: keep the canonical policy/baseline JSON byte-stable. jsonb reorders keys
    /// and would make InternalDecisionPolicyHash disagree with the reloaded document.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260913130300_StoreInternalDecisionPolicyAsText")]
    public partial class StoreInternalDecisionPolicyAsText : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "InternalDecisionPolicyJson",
                table: "AgentTasks",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InternalDecisionAuditBaselineJson",
                table: "AgentTasks",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "InternalDecisionPolicyJson",
                table: "AgentTasks",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InternalDecisionAuditBaselineJson",
                table: "AgentTasks",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
