using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0332 S1: nullable ComplexityChains.Role. One active row per (Role?, Complexity);
    /// Role NULL is the any-role fallback (a CARD-0090 row, no data rewrite). Unique index
    /// uses NULLS NOT DISTINCT so two any-role rows of the same complexity cannot coexist.
    /// Hand-written (running daemons lock bin/); the [DbContext]/[Migration] attributes
    /// normally generated into the Designer live here. Snapshot is updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260904020000_AddComplexityChainRole")]
    public partial class AddComplexityChainRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ComplexityChains_Complexity_Active",
                table: "ComplexityChains");

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "ComplexityChains",
                type: "integer",
                nullable: true);

            // Raw SQL: EF's CreateIndex does not reliably emit NULLS NOT DISTINCT even with
            // the Npgsql:NullsDistinct annotation. Postgres 15+ (live 16) supports it.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_ComplexityChains_Role_Complexity_Active"
                ON "ComplexityChains" ("Role", "Complexity")
                NULLS NOT DISTINCT
                WHERE "ClearedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // History stays readable; only active role cells are cleared so the old
            // one-active-per-complexity index can be recreated.
            migrationBuilder.Sql(
                """
                UPDATE "ComplexityChains"
                SET "ClearedAt" = NOW() AT TIME ZONE 'utc'
                WHERE "ClearedAt" IS NULL AND "Role" IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_ComplexityChains_Role_Complexity_Active";""");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "ComplexityChains");

            migrationBuilder.CreateIndex(
                name: "IX_ComplexityChains_Complexity_Active",
                table: "ComplexityChains",
                column: "Complexity",
                unique: true,
                filter: "\"ClearedAt\" IS NULL");
        }
    }
}
