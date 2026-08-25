using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    // Hand-written (no .Designer.cs) — data-only, matching SyncAgentKindWithTuiProfile. The running
    // AppHost locks bin/, so `dotnet ef migrations add` could not build. The [DbContext]/[Migration]
    // attributes normally generated into the Designer live here instead. No snapshot change: the
    // model is unchanged.
    /// <summary>
    /// CARD-0182 D1: write the literal '--model' into existing NULL ModelArgumentName revisions of
    /// non-Raw profiles, recording what they have always done at launch (the resolver used to
    /// default null to --model). After this, a blank field is a blank the operator chose, and
    /// means "the program owns its model; append nothing".
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260825120000_BackfillNonRawModelArgumentName")]
    public partial class BackfillNonRawModelArgumentName : Migration
    {
        /// <summary>
        /// Semantics-preserving rewrite: null non-Raw revisions become '--model'. Raw stays null
        /// (its catalogue default is null). Idempotent.
        /// </summary>
        public const string BackfillSql =
            """
            UPDATE "AgentTuiProfileRevisions" r
            SET "ModelArgumentName" = '--model'
            FROM "AgentTuiProfiles" p
            WHERE p."Id" = r."ProfileId"
              AND r."ModelArgumentName" IS NULL
              AND p."Kind" <> 0;
            """;

        /// <summary>
        /// Names any Raw-profile agent that already carries an exact ModelId: after this card,
        /// that combination is 409 model_argument_unsupported at the next save or launch.
        /// </summary>
        public const string CensusSql =
            """
            DO $$
            DECLARE
              rec record;
            BEGIN
              FOR rec IN
                SELECT a."Name" AS agent_name, a."ModelId" AS model_id, p."DisplayName" AS profile_name
                FROM "Agents" a
                INNER JOIN "AgentTuiProfiles" p ON p."Id" = a."TuiProfileId"
                WHERE p."Kind" = 0
                  AND a."ModelId" IS NOT NULL
                  AND btrim(a."ModelId") <> ''
              LOOP
                RAISE NOTICE 'CARD-0182: Raw profile "%" agent "%" has ModelId "%"; next launch will 409 model_argument_unsupported',
                  rec.profile_name, rec.agent_name, rec.model_id;
              END LOOP;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BackfillSql);
            migrationBuilder.Sql(CensusSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: restoring NULL would restore the pre-CARD-0182 "null means --model" lie for
            // any revision the operator has not since rewritten, and would blank fields that were
            // already '--model' before this card.
        }
    }
}
