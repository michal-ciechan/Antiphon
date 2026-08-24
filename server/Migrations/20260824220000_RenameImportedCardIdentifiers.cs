using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <summary>
    /// CARD-0175 S3: back-fills the identifiers of cards that were created by a tracker import
    /// before the sync stopped writing the tracker's key into <c>Cards.Identifier</c>.
    /// </summary>
    /// <remarks>
    /// Live at authoring time that is exactly eleven GitHub cards, <c>#3</c>-<c>#13</c> on one
    /// board; nothing else in the database has an identifier outside <c>^CARD-[0-9]+$</c> that
    /// also carries an <c>ExternalIssueRef</c>. Those identifiers are the reason those cards
    /// cannot launch an agent at all: <c>WorktreeManager.ValidateCardId</c> rejects <c>#</c>, and
    /// (deliberately, CARD-0175 decision 3) still does — this migration moves the cards to a legal
    /// identifier rather than weakening the validator.
    ///
    /// <para>Data-only, so there is no model change and the snapshot is untouched. Hand-written
    /// because the running daemons lock <c>bin/</c>.</para>
    ///
    /// <para><b>Ordering matters and it is not incidental.</b> This must deploy in the SAME release
    /// as the code that stopped re-asserting <c>Identifier = ExternalKey</c> on every sync
    /// (<c>ExternalTrackerSyncService.UpdateExisting</c>). Shipped alone, the next 30-minute tick
    /// would put every <c>#N</c> straight back.</para>
    ///
    /// <para>Numbering mirrors <c>CardIdentifierAllocator</c> exactly: per board, one past the
    /// highest numeric suffix after the last <c>-</c> across ALL of the board's cards, archived
    /// included (CARD-0005 — the sequence only moves forward). Ordered by <c>CreatedAt</c>, then
    /// the numeric part of the tracker key, so the eleven cards (which share one <c>CreatedAt</c>)
    /// get numbers in tracker order: <c>#3</c> the lowest, <c>#13</c> the highest.</para>
    ///
    /// <para>The old value is NOT lost — it is the card's <c>ExternalIssueRef.ExternalKey</c>, and
    /// CARD-0175 S4 renders it on the card. No <c>CardRevision</c> row is written: the revision
    /// kinds are Move / ContentEdit / Reopen / ArchiveChange, an identifier has never changed
    /// before, and adding a kind for an eleven-row one-off is not worth it.</para>
    ///
    /// <para>The renamed cards' <c>RetrySchedules</c> are deleted. All eleven sat at 3/3 attempts
    /// with <c>NextRetryAt = null</c> — permanently exhausted, and nothing in <c>CardService</c>
    /// ever resets one. Those three failures were this bug's, not the cards'.</para>
    /// </remarks>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260824220000_RenameImportedCardIdentifiers")]
    public partial class RenameImportedCardIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIRST, while the stuck identifiers are still identifiable: drop the renamed cards'
            // retry schedules. All eleven sat at 3/3 with NextRetryAt = null - permanently
            // exhausted - and nothing in CardService ever resets one. Those failures were this
            // bug's, not the cards'. Keyed on the card's own Identifier, NOT on the tracker key:
            // an export-origin card also has a "#14"-shaped ExternalKey and must not be touched.
            migrationBuilder.Sql("""
                DELETE FROM "RetrySchedules" r
                USING "Cards" c
                WHERE r."CardId" = c."Id"
                  AND c."Identifier" !~ '^CARD-[0-9]+$'
                  AND EXISTS (SELECT 1 FROM "ExternalIssueRefs" e WHERE e."CardId" = c."Id");
                """);

            migrationBuilder.Sql("""
                WITH stuck AS (
                    SELECT c."Id",
                           c."BoardId",
                           c."CreatedAt",
                           e."ExternalKey",
                           COALESCE(
                               NULLIF(regexp_replace(e."ExternalKey", '\D', '', 'g'), ''),
                               '0')::bigint AS key_number
                    FROM "Cards" c
                    JOIN "ExternalIssueRefs" e ON e."CardId" = c."Id"
                    WHERE c."Identifier" !~ '^CARD-[0-9]+$'
                ),
                highest AS (
                    SELECT c."BoardId",
                           COALESCE(MAX(
                               CASE
                                   WHEN split_part(c."Identifier", '-', array_length(
                                       string_to_array(c."Identifier", '-'), 1)) ~ '^[0-9]+$'
                                   THEN split_part(c."Identifier", '-', array_length(
                                       string_to_array(c."Identifier", '-'), 1))::bigint
                                   ELSE 0
                               END), 0) AS max_number
                    FROM "Cards" c
                    WHERE c."BoardId" IN (SELECT DISTINCT "BoardId" FROM stuck)
                    GROUP BY c."BoardId"
                ),
                renamed AS (
                    SELECT s."Id",
                           'CARD-' || lpad((
                               h.max_number
                               + row_number() OVER (
                                   PARTITION BY s."BoardId"
                                   ORDER BY s."CreatedAt", s.key_number, s."ExternalKey")
                           )::text, 4, '0') AS new_identifier
                    FROM stuck s
                    JOIN highest h ON h."BoardId" = s."BoardId"
                )
                UPDATE "Cards" c
                SET "Identifier" = r.new_identifier,
                    "UpdatedAt" = now() AT TIME ZONE 'utc',
                    "ConcurrencyToken" = gen_random_uuid()
                FROM renamed r
                WHERE c."Id" = r."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op. Restoring `#N` would put the cards back into the state that
            // cannot launch an agent, and re-freeing the CARD-nnnn numbers just handed out would
            // run the identifier sequence BACKWARDS — the exact failure CARD-0005 exists to
            // prevent, since identifiers are cited in commit messages and docs outside this
            // database. The tracker key was never lost; it is on ExternalIssueRefs.ExternalKey.
        }
    }
}
