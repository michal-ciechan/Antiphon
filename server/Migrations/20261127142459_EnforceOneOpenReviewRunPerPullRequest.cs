using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOneOpenReviewRunPerPullRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep backfill and index installation in one transaction with a bounded lock wait.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            migrationBuilder.AddColumn<int>(
                name: "GitHubPrNumber",
                table: "ReviewRuns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitHubRepo",
                table: "ReviewRuns",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION "NormalizeReviewGitHubRepo"(repo text) RETURNS text
                LANGUAGE sql IMMUTABLE STRICT AS $$
                    SELECT lower(regexp_replace(btrim(repo, E' \t\r\n'),
                        '^(https?://github[.]com/)?([a-z0-9_.-]+/[a-z0-9_.-]+)(/(pull/)?[0-9]+)?/?([?#].*)?$',
                        '\2', 'i') COLLATE "C");
                $$;

                UPDATE "ReviewRuns" AS r
                SET "GitHubRepo" = "NormalizeReviewGitHubRepo"(s."Repo"), "GitHubPrNumber" = s."PrNumber"
                FROM "ReviewSubjects" AS s
                WHERE s."Id" = r."SubjectId" AND s."Host" = 'GitHub' AND s."PrNumber" > 0;

                CREATE FUNCTION "FillReviewRunGitHubIdentity"() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    SELECT "NormalizeReviewGitHubRepo"(s."Repo"), s."PrNumber"
                    INTO NEW."GitHubRepo", NEW."GitHubPrNumber"
                    FROM "ReviewSubjects" AS s
                    WHERE s."Id" = NEW."SubjectId" AND s."Host" = 'GitHub' AND s."PrNumber" > 0;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_ReviewRuns_FillGitHubIdentity"
                BEFORE INSERT OR UPDATE OF "SubjectId", "GitHubRepo", "GitHubPrNumber" ON "ReviewRuns"
                FOR EACH ROW EXECUTE FUNCTION "FillReviewRunGitHubIdentity"();

                CREATE TEMP TABLE "_ReviewRunDuplicates" ON COMMIT DROP AS
                SELECT "Id", "Status", "Stage", "WaitingOn", "UpdatedAt", "KeepId"
                FROM (
                    SELECT "Id", "Status", "Stage", "WaitingOn", "UpdatedAt", first_value("Id") OVER pr AS "KeepId"
                    FROM "ReviewRuns"
                    WHERE "Status" <> 'Closed' AND "GitHubRepo" IS NOT NULL AND "GitHubPrNumber" IS NOT NULL
                    WINDOW pr AS (PARTITION BY "GitHubRepo", "GitHubPrNumber" ORDER BY "StartedAt" DESC, "Id" DESC)
                ) AS ranked
                WHERE "Id" <> "KeepId";

                UPDATE "ReviewRuns" AS r
                SET "Status" = 'Closed', "Stage" = 'Done', "WaitingOn" = NULL, "UpdatedAt" = transaction_timestamp()
                FROM "_ReviewRunDuplicates" AS d WHERE r."Id" = d."Id";

                UPDATE "ReviewGates" AS g
                SET "Status" = 'Done', "DecidedAt" = transaction_timestamp(),
                    "Reasons" = array_append(g."Reasons", 'superseded by duplicate-intake cleanup')
                FROM "_ReviewRunDuplicates" AS d
                WHERE g."RunId" = d."Id" AND g."Status" <> 'Done';

                UPDATE "ReviewNodeExecutions" AS n
                SET "Status" = 'Skipped', "CompletedAt" = transaction_timestamp(),
                    "Summary" = concat_ws(E'\n', nullif(n."Summary", ''), 'Closed: superseded by duplicate-intake cleanup')
                FROM "_ReviewRunDuplicates" AS d
                WHERE n."RunId" = d."Id" AND n."Status" IN ('Pending', 'Running', 'Waiting');

                INSERT INTO "ReviewNodeExecutions"
                    ("Id", "RunId", "NodeId", "Iteration", "Sequence", "Kind", "Status", "Outcome",
                     "InputTokens", "OutputTokens", "CostUsd", "CompletedAt", "Summary", "OutputJson")
                SELECT gen_random_uuid(), "Id", 'review-run-close', 0, 0, 'Completion', 'Succeeded', 'close',
                    0, 0, 0, transaction_timestamp(), 'superseded by duplicate-intake cleanup',
                    jsonb_build_object('actor', 'system:migration', 'reason', 'superseded by duplicate-intake cleanup',
                        'at', transaction_timestamp(), 'supersededByRunId', "KeepId", 'previousStatus', "Status",
                        'previousStage', "Stage", 'previousWaitingOn', "WaitingOn", 'previousUpdatedAt', "UpdatedAt")
                FROM "_ReviewRunDuplicates";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRuns_GitHubRepo_GitHubPrNumber_Open",
                table: "ReviewRuns",
                columns: new[] { "GitHubRepo", "GitHubPrNumber" },
                unique: true,
                filter: "\"Status\" <> 'Closed' AND \"GitHubRepo\" IS NOT NULL AND \"GitHubPrNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The keys are derived. Keep the run closures and their audit when removing them.
            migrationBuilder.Sql(
                """
                SET LOCAL lock_timeout = '5s';
                DROP TRIGGER "TR_ReviewRuns_FillGitHubIdentity" ON "ReviewRuns";
                DROP FUNCTION "FillReviewRunGitHubIdentity"();
                DROP FUNCTION "NormalizeReviewGitHubRepo"(text);
                """);

            migrationBuilder.DropIndex(
                name: "IX_ReviewRuns_GitHubRepo_GitHubPrNumber_Open",
                table: "ReviewRuns");

            migrationBuilder.DropColumn(
                name: "GitHubPrNumber",
                table: "ReviewRuns");

            migrationBuilder.DropColumn(
                name: "GitHubRepo",
                table: "ReviewRuns");
        }
    }
}
