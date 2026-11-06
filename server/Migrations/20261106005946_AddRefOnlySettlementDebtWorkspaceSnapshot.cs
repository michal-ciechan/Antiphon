using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRefOnlySettlementDebtWorkspaceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkspaceSnapshotJson",
                table: "AgentTaskSyncDebts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keep the refusal check and removal in the migration transaction's table lock.
            // Recorded recovery authority must be drained before an older schema can replace it.
            migrationBuilder.Sql("""
                LOCK TABLE "AgentTaskSyncDebts" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "AgentTaskSyncDebts" WHERE "WorkspaceSnapshotJson" IS NOT NULL) THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            MESSAGE = 'card0824_refonly_snapshot_downgrade_blocked',
                            HINT = 'Drain recorded RefOnly settlement debt snapshots before downgrading.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "WorkspaceSnapshotJson",
                table: "AgentTaskSyncDebts");
        }
    }
}
