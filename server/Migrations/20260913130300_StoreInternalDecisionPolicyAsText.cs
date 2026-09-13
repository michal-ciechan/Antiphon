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

        /// <remarks>
        /// Hand-written, not AlterColumn: PostgreSQL has no assignment cast from text to jsonb, so
        /// the generated <c>ALTER COLUMN ... TYPE jsonb</c> fails with 42804 on any database that
        /// has ever stored a policy. A Down that cannot run is not a rollback.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "AgentTasks"
                    ALTER COLUMN "InternalDecisionPolicyJson" TYPE jsonb
                        USING "InternalDecisionPolicyJson"::jsonb;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "AgentTasks"
                    ALTER COLUMN "InternalDecisionAuditBaselineJson" TYPE jsonb
                        USING "InternalDecisionAuditBaselineJson"::jsonb;
                """);
        }
    }
}
