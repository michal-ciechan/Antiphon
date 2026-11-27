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

            migrationBuilder.AddColumn<string>(name: "GitHubAuthority", table: "ReviewRuns", type: "text", nullable: true);
            migrationBuilder.AddColumn<long>(name: "GitHubRepositoryId", table: "ReviewRuns", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsDemo", table: "ReviewRuns", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<Guid>(name: "ReviewerRunId", table: "AgentTasks", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "ReviewerNodeId", table: "AgentTasks", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ReviewerAttempt", table: "AgentTasks", type: "integer", nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "ReviewRuns" AS r SET "IsDemo" = true
                WHERE EXISTS (SELECT FROM "ReviewIterations" i WHERE i."RunId" = r."Id" AND i."Iteration" = 1
                    AND i."HeadSha" IS NULL)
                  AND EXISTS (SELECT FROM "ReviewNodeExecutions" n WHERE n."RunId" = r."Id" AND n."Iteration" = 1
                    AND n."NodeId" = 'intake' AND n."Status" = 'Succeeded');

                -- No numeric repository id was persisted before this migration. Use the
                -- historical PR URL (or legacy input URL) solely to group historical rows.
                UPDATE "ReviewRuns" AS r
                SET "GitHubRepo" = substring(lower(btrim(s."Repo", E' \t\r\n') COLLATE "C")
                        from '^(?:https?://[^/?#]+/)?([a-z0-9_.-]+/[a-z0-9_.-]+)(?:/(?:pull/)?[0-9]+)?/?(?:[?#].*)?$'),
                    "GitHubPrNumber" = s."PrNumber",
                    "GitHubAuthority" = CASE WHEN origin.web_root IS NULL OR origin.web_root IN
                        ('https://github.com', 'http://github.com', 'https://api.github.com')
                        THEN 'https://api.github.com' ELSE origin.web_root || '/api/v3' END
                FROM "ReviewSubjects" AS s
                CROSS JOIN LATERAL (SELECT coalesce(
                    substring(lower(s."Url" COLLATE "C") from '^https?://[a-z0-9.-]+(?::[0-9]+)?'),
                    substring(lower(btrim(s."Repo") COLLATE "C") from '^https?://[a-z0-9.-]+(?::[0-9]+)?')) AS web_root) origin
                WHERE s."Id" = r."SubjectId" AND s."Host" = 'GitHub' AND s."PrNumber" > 0 AND NOT r."IsDemo";

                CREATE TEMP TABLE "_ReviewRunDuplicates" ON COMMIT DROP AS
                SELECT "Id", "Status", "Stage", "WaitingOn", "UpdatedAt", "KeepId"
                FROM (
                    SELECT "Id", "Status", "Stage", "WaitingOn", "UpdatedAt", first_value("Id") OVER pr AS "KeepId"
                    FROM "ReviewRuns"
                    WHERE "Status" <> 'Closed' AND NOT "IsDemo" AND "GitHubAuthority" IS NOT NULL AND "GitHubPrNumber" IS NOT NULL
                        AND ("GitHubRepositoryId" IS NOT NULL OR "GitHubRepo" IS NOT NULL)
                    WINDOW pr AS (PARTITION BY "GitHubAuthority", "GitHubRepositoryId",
                        CASE WHEN "GitHubRepositoryId" IS NULL THEN "GitHubRepo" END, "GitHubPrNumber"
                        ORDER BY "StartedAt" DESC, "Id" DESC)
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

                -- Older executions saved the handle only on returning from task creation. Retain
                -- links that can be proved by both the handle and the task's review brief.
                WITH recorded AS (
                    SELECT n."Id", n."RunId", (n."OutputJson" ->> 'handle')::uuid AS task_id,
                        CASE n."OutputJson" ->> 'attempts' WHEN '2' THEN 2 ELSE 1 END AS attempt
                    FROM "ReviewNodeExecutions" n
                    WHERE n."Kind" = 'Agent'
                        AND n."OutputJson" ->> 'handle' ~* '^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$'
                )
                UPDATE "AgentTasks" AS t
                SET "ReviewerRunId" = a."RunId", "ReviewerNodeId" = a."Id", "ReviewerAttempt" = a.attempt
                FROM recorded a
                WHERE a.task_id = t."Id"
                    AND position('for the Antiphon review engine (run ' || a."RunId"::text || ',' IN t."Goal") > 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRuns_GitHubIdentity_Open",
                table: "ReviewRuns",
                columns: new[] { "GitHubAuthority", "GitHubRepositoryId", "GitHubPrNumber" },
                unique: true,
                filter: "\"Status\" <> 'Closed' AND NOT \"IsDemo\" AND \"GitHubAuthority\" IS NOT NULL AND \"GitHubRepositoryId\" IS NOT NULL AND \"GitHubPrNumber\" IS NOT NULL");
            migrationBuilder.CreateIndex(
                name: "IX_ReviewRuns_GitHubLegacyIdentity_Open",
                table: "ReviewRuns",
                columns: new[] { "GitHubAuthority", "GitHubRepo", "GitHubPrNumber" },
                unique: true,
                filter: "\"Status\" <> 'Closed' AND NOT \"IsDemo\" AND \"GitHubRepositoryId\" IS NULL AND \"GitHubAuthority\" IS NOT NULL AND \"GitHubRepo\" IS NOT NULL AND \"GitHubPrNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(name: "IX_AgentTasks_ReviewerAttempt", table: "AgentTasks",
                columns: new[] { "ReviewerRunId", "ReviewerNodeId", "ReviewerAttempt" }, unique: true,
                filter: "\"ReviewerRunId\" IS NOT NULL");
            migrationBuilder.AddForeignKey(name: "FK_AgentTasks_ReviewRuns_ReviewerRunId", table: "AgentTasks",
                column: "ReviewerRunId", principalTable: "ReviewRuns", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The keys are derived. Keep the run closures and their audit when removing them.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            migrationBuilder.DropForeignKey(name: "FK_AgentTasks_ReviewRuns_ReviewerRunId", table: "AgentTasks");
            migrationBuilder.DropIndex(name: "IX_AgentTasks_ReviewerAttempt", table: "AgentTasks");
            migrationBuilder.DropColumn(name: "ReviewerAttempt", table: "AgentTasks");
            migrationBuilder.DropColumn(name: "ReviewerNodeId", table: "AgentTasks");
            migrationBuilder.DropColumn(name: "ReviewerRunId", table: "AgentTasks");

            migrationBuilder.DropIndex(
                name: "IX_ReviewRuns_GitHubIdentity_Open",
                table: "ReviewRuns");
            migrationBuilder.DropIndex(
                name: "IX_ReviewRuns_GitHubLegacyIdentity_Open",
                table: "ReviewRuns");

            migrationBuilder.DropColumn(name: "GitHubAuthority", table: "ReviewRuns");
            migrationBuilder.DropColumn(name: "GitHubRepositoryId", table: "ReviewRuns");
            migrationBuilder.DropColumn(name: "IsDemo", table: "ReviewRuns");

            migrationBuilder.DropColumn(
                name: "GitHubPrNumber",
                table: "ReviewRuns");

            migrationBuilder.DropColumn(
                name: "GitHubRepo",
                table: "ReviewRuns");
        }
    }
}
