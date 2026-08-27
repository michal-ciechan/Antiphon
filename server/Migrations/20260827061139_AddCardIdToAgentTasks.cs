using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0040 S1: the missing edge from a delegated task to the card its work is against, plus a
    /// best-effort backfill of the convention that already existed in prose (measured 2026-08-27 on
    /// the live database: 627 tasks, 397 titles LEADING with a CARD-nnnn identifier).
    ///
    /// <para>The backfill applies the same precedence the runtime binder does, minus the scopes
    /// history cannot reconstruct: a task binds only when its title LEADS with an identifier, it is
    /// not a Check row (Role 11 — those are about a task, not a card), and the identifier resolves
    /// to exactly ONE card, first inside the boards of the projects whose checkout contains the
    /// task's repository, and failing that across every board. Identifiers are unique per BOARD, not
    /// globally, so ambiguity binds NOTHING rather than guessing — two boards on this deployment
    /// both hold CARD-0001..0011. Path comparison is separator- and case-insensitive because the
    /// live rows spell the same tree both ways.</para>
    ///
    /// <para>Down drops the column, so no data restoration is possible or needed.</para>
    /// </summary>
    public partial class AddCardIdToAgentTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CardId",
                table: "AgentTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_CardId",
                table: "AgentTasks",
                column: "CardId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTasks_Cards_CardId",
                table: "AgentTasks",
                column: "CardId",
                principalTable: "Cards",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(BackfillSql);
        }

        /// <summary>
        /// Public so a test can assert the rule rather than re-spell it. Idempotent: it only ever
        /// writes rows whose <c>CardId</c> is still null.
        /// </summary>
        public const string BackfillSql =
            """
            DO $$
            DECLARE bound integer;
            BEGIN
                WITH candidates AS (
                    SELECT
                        t."Id" AS task_id,
                        'CARD-' || lpad(
                            substring(t."Title" FROM '^[[:space:]]*[Cc][Aa][Rr][Dd]-0*([0-9]{1,9})'),
                            4, '0') AS ident,
                        lower(replace(rtrim(COALESCE(NULLIF(t."RepoPath", ''), t."WorkingDirectory"), '/\'), '/', '\')) AS path
                    FROM "AgentTasks" t
                    WHERE t."CardId" IS NULL
                      AND t."Role" <> 11
                      AND t."Title" ~ '^[[:space:]]*[Cc][Aa][Rr][Dd]-[0-9]{1,9}'
                ),
                proj AS (
                    SELECT
                        p."Id" AS project_id,
                        lower(replace(rtrim(p."LocalRepositoryPath", '/\'), '/', '\')) AS root
                    FROM "Projects" p
                    WHERE p."LocalRepositoryPath" IS NOT NULL AND p."LocalRepositoryPath" <> ''
                ),
                scope_b AS (
                    SELECT DISTINCT c.task_id, ca."Id" AS card_id
                    FROM candidates c
                    JOIN proj ON c.path = proj.root OR starts_with(c.path, proj.root || '\')
                    JOIN "Boards" b ON b."ProjectId" = proj.project_id
                    JOIN "Cards" ca ON ca."BoardId" = b."Id" AND ca."Identifier" = c.ident
                ),
                b_pick AS (
                    SELECT task_id, min(card_id::text)::uuid AS card_id
                    FROM scope_b GROUP BY task_id HAVING count(*) = 1
                ),
                scope_c AS (
                    SELECT DISTINCT c.task_id, ca."Id" AS card_id
                    FROM candidates c
                    JOIN "Cards" ca ON ca."Identifier" = c.ident
                    WHERE NOT EXISTS (SELECT 1 FROM b_pick bp WHERE bp.task_id = c.task_id)
                ),
                c_pick AS (
                    SELECT task_id, min(card_id::text)::uuid AS card_id
                    FROM scope_c GROUP BY task_id HAVING count(*) = 1
                ),
                picks AS (
                    SELECT * FROM b_pick
                    UNION ALL
                    SELECT * FROM c_pick
                )
                UPDATE "AgentTasks" t
                SET "CardId" = picks.card_id
                FROM picks
                WHERE t."Id" = picks.task_id;

                GET DIAGNOSTICS bound = ROW_COUNT;
                RAISE NOTICE 'CARD-0040 backfill: bound % task(s) to a card.', bound;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentTasks_Cards_CardId",
                table: "AgentTasks");

            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_CardId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CardId",
                table: "AgentTasks");
        }
    }
}
