using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0175 S3: rename imported cards whose Identifier is not CARD-shaped (the 11 GitHub
    /// imports #3–#13 on this deployment) to the next free CARD-nnnn on their board, ordered by
    /// CreatedAt then the key's number so #3 gets the lowest new number. Deletes their exhausted
    /// RetrySchedules — those three failures were the bug's, not the cards'. Rotates UpdatedAt and
    /// ConcurrencyToken. Down is a no-op: identifiers must not run backwards (CARD-0005).
    /// Hand-written (running daemons lock bin/).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260824220000_RenameImportedCardIdentifiers")]
    public partial class RenameImportedCardIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH to_rename AS (
                    SELECT
                        c."Id" AS "CardId",
                        c."BoardId",
                        ROW_NUMBER() OVER (
                            PARTITION BY c."BoardId"
                            ORDER BY
                                c."CreatedAt",
                                CASE
                                    WHEN e."ExternalKey" ~ '^#[0-9]+$'
                                        THEN CAST(substring(e."ExternalKey" FROM 2) AS integer)
                                    WHEN e."ExternalKey" ~ '[0-9]+$'
                                        THEN CAST((regexp_match(e."ExternalKey", '([0-9]+)$'))[1] AS integer)
                                    ELSE 0
                                END,
                                c."Identifier"
                        ) AS rn
                    FROM "Cards" c
                    INNER JOIN "ExternalIssueRefs" e ON e."CardId" = c."Id"
                    WHERE c."Identifier" !~ '^CARD-[0-9]+$'
                ),
                highest AS (
                    SELECT
                        "BoardId",
                        COALESCE(
                            MAX(
                                CASE
                                    WHEN substring("Identifier" FROM '[^-]*$') ~ '^[0-9]+$'
                                        THEN CAST(substring("Identifier" FROM '[^-]*$') AS integer)
                                    ELSE 0
                                END
                            ),
                            0
                        ) AS max_n
                    FROM "Cards"
                    GROUP BY "BoardId"
                ),
                renamed AS (
                    UPDATE "Cards" c
                    SET
                        "Identifier" = 'CARD-' || lpad((h.max_n + r.rn)::text, 4, '0'),
                        "UpdatedAt" = NOW() AT TIME ZONE 'utc',
                        "ConcurrencyToken" = gen_random_uuid()
                    FROM to_rename r
                    INNER JOIN highest h ON h."BoardId" = r."BoardId"
                    WHERE c."Id" = r."CardId"
                    RETURNING c."Id"
                )
                DELETE FROM "RetrySchedules" s
                USING renamed r
                WHERE s."CardId" = r."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Identifiers are cited outside the database (CARD-0005). Running this sequence
            // backwards would re-issue numbers other cards already hold.
        }
    }
}
