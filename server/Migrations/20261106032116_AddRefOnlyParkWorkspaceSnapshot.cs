using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRefOnlyParkWorkspaceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkspaceSnapshotJson",
                table: "AgentTaskParks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The table lock remains held through DropColumn in the migration transaction.
            migrationBuilder.Sql("""
                LOCK TABLE "AgentTaskParks" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "AgentTaskParks" WHERE "WorkspaceSnapshotJson" IS NOT NULL) THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            MESSAGE = 'card0824_refonly_snapshot_downgrade_blocked',
                            HINT = 'Drain recorded RefOnly park snapshots before downgrading.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "WorkspaceSnapshotJson",
                table: "AgentTaskParks");
        }
    }
}
