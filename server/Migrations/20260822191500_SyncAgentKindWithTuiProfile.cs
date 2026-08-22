using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    // Hand-written (no .Designer.cs) — data-only, matching RenameModelFamilyToLevel. The running
    // AppHost locks bin/, so `dotnet ef migrations add` could not build. The [DbContext]/[Migration]
    // attributes normally generated into the Designer live here instead. No snapshot change: the
    // model is unchanged.
    /// <summary>
    /// CARD-0138: backfill Agents.Kind from the attached TUI profile where they disagree.
    /// Pool delegates are excluded — their Kind is load-bearing while TuiProfileId is null, and a
    /// mismatched pool row must not be rewritten by a re-run.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260822191500_SyncAgentKindWithTuiProfile")]
    public partial class SyncAgentKindWithTuiProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Agents" a
                SET    "Kind" = p."Kind"
                FROM   "AgentTuiProfiles" p
                WHERE  p."Id" = a."TuiProfileId"
                  AND  a."Kind" <> p."Kind"
                  AND  NOT a."IsPoolDelegate";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: the pre-fix values were a column default, not a decision, and restoring
            // "everything is ClaudeCode" would restore the bug (CARD-0138).
        }
    }
}
