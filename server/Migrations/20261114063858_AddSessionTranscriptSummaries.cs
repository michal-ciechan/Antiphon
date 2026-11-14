using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionTranscriptSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.CreateTable(
                name: "SessionTranscriptSummaries",
                columns: table => new
                {
                    AgentSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Valid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    NextBackfillAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    NewestEffectiveTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LastTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndSequence = table.Column<long>(type: "bigint", nullable: true),
                    EndTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HasActivity = table.Column<bool>(type: "boolean", nullable: false),
                    HasNullTimestampActivity = table.Column<bool>(type: "boolean", nullable: false),
                    ActivityTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastTurnEndSequence = table.Column<long>(type: "bigint", nullable: true),
                    LastTurnTitleSequence = table.Column<long>(type: "bigint", nullable: true),
                    LastUserPromptSequence = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionTranscriptSummaries", x => x.AgentSessionId);
                    table.ForeignKey(
                        name: "FK_SessionTranscriptSummaries_AgentSessions_AgentSessionId",
                        column: x => x.AgentSessionId,
                        principalTable: "AgentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionTranscriptSummaries_Incomplete",
                table: "SessionTranscriptSummaries",
                columns: new[] { "NextBackfillAttemptAt", "AgentSessionId" },
                filter: "NOT \"Valid\" OR \"SchemaVersion\" <> 1");

            // No transcript rewrite or backfill. Only recognized valid summaries fold new rows.
            migrationBuilder.Sql("""
            CREATE FUNCTION c1915_insert() RETURNS trigger LANGUAGE plpgsql AS $body$
            DECLARE
                session_id uuid;
                previous_row "SessionTranscriptSummaries"%ROWTYPE;
            BEGIN
                -- Statement order is arbitrary. Lock keys deterministically before folding each session.
                FOR session_id IN SELECT DISTINCT "AgentSessionId" FROM new_entries ORDER BY 1 LOOP
                    INSERT INTO "SessionTranscriptSummaries"
                        ("AgentSessionId", "SchemaVersion", "Valid", "Count", "LastSequence", "HasActivity", "HasNullTimestampActivity")
                    SELECT s."Id", 1, false, 0, 0, false, false FROM "AgentSessions" s WHERE s."Id" = session_id
                    ON CONFLICT ("AgentSessionId") DO NOTHING;
                    SELECT * INTO previous_row FROM "SessionTranscriptSummaries"
                    WHERE "AgentSessionId" = session_id FOR UPDATE;
                    IF NOT FOUND THEN CONTINUE; END IF;
                    IF NOT previous_row."Valid" OR previous_row."SchemaVersion" <> 1 THEN
                        UPDATE "SessionTranscriptSummaries" SET "Valid" = false, "NextBackfillAttemptAt" = NULL
                        WHERE "AgentSessionId" = session_id AND ("Valid" OR "NextBackfillAttemptAt" IS NOT NULL);
                        CONTINUE;
                    END IF;
                    -- An empty summary has no sequence floor: bigint minimum is a legal first row.
                    IF previous_row."Count" > 0 AND EXISTS (
                        SELECT 1 FROM new_entries WHERE "AgentSessionId" = session_id AND "Sequence" <= previous_row."LastSequence"
                    ) THEN
                        UPDATE "SessionTranscriptSummaries" SET "Valid" = false, "NextBackfillAttemptAt" = NULL
                        WHERE "AgentSessionId" = session_id;
                        CONTINUE;
                    END IF;
                    -- Frozen post-CARD-1887 classification. Never consult mutable application constants.
                    WITH classified AS MATERIALIZED (
                        SELECT a.*,
                            ("Kind" IN ('TurnEnd', 'SessionRestartBoundary')
                             OR ("Kind" = 'CompactBoundary' AND "Text" IS NOT NULL AND strpos("Text", '(manual)') > 0)
                             OR ("Kind" = 'UserPrompt' AND "Text" IS NOT NULL AND "Text" LIKE '[Request interrupted%')) AS is_end,
                            (a."Kind" NOT IN ('TurnEnd', 'TurnTitle', 'SessionRestartBoundary', 'QueuedUserPrompt',
                                             'QueueEnqueue', 'QueueDequeue', 'QueueRemove', 'CompactBoundary')
                             AND (a."Kind" <> 'UserPrompt' OR a."Text" IS NULL OR
                                 (a."Text" NOT LIKE '<command-name>%'
                                  AND a."Text" NOT LIKE '<local-command-stdout>%'
                                  AND a."Text" NOT LIKE 'This session is being continued from a previous conversation%'
                                  AND a."Text" NOT LIKE '[Request interrupted%'))) AS is_activity
                        FROM new_entries a WHERE a."AgentSessionId" = session_id
                    ), folded AS (
                        SELECT count(*) AS inserted_count, max(COALESCE("Timestamp", "CreatedAt")) AS newest,
                            max("Sequence") FILTER (WHERE is_end) AS end_seq,
                            max("Timestamp") FILTER (WHERE is_end) AS end_time,
                            max("Sequence") FILTER (WHERE "Kind" = 'TurnEnd') AS turn_end,
                            max("Sequence") FILTER (WHERE "Kind" = 'TurnTitle') AS title,
                            max("Sequence") FILTER (WHERE "Kind" = 'UserPrompt') AS prompt
                        FROM classified
                    ), last_row AS (
                        SELECT * FROM classified ORDER BY "Sequence" DESC LIMIT 1
                    ), activity AS (
                        SELECT count(*) > 0 AS has_activity,
                            count(*) FILTER (WHERE c."Timestamp" IS NULL) > 0 AS has_null,
                            max(c."Timestamp") AS activity_time
                        FROM classified c CROSS JOIN folded f
                        WHERE c.is_activity AND (f.end_seq IS NULL OR c."Sequence" > f.end_seq)
                    )
                    UPDATE "SessionTranscriptSummaries" s SET
                        "Count" = s."Count" + f.inserted_count,
                        "NewestEffectiveTimestamp" = GREATEST(s."NewestEffectiveTimestamp", f.newest),
                        "LastSequence" = l."Sequence", "LastEntryId" = l."Id", "LastKind" = l."Kind",
                        "LastTimestamp" = l."Timestamp", "LastCreatedAt" = l."CreatedAt",
                        "EndSequence" = COALESCE(f.end_seq, s."EndSequence"),
                        "EndTimestamp" = GREATEST(s."EndTimestamp", f.end_time),
                        "HasActivity" = CASE WHEN f.end_seq IS NOT NULL THEN a.has_activity ELSE s."HasActivity" OR a.has_activity END,
                        "HasNullTimestampActivity" = CASE WHEN f.end_seq IS NOT NULL THEN a.has_null ELSE s."HasNullTimestampActivity" OR a.has_null END,
                        "ActivityTimestamp" = CASE WHEN f.end_seq IS NOT NULL THEN a.activity_time ELSE GREATEST(s."ActivityTimestamp", a.activity_time) END,
                        "LastTurnEndSequence" = GREATEST(s."LastTurnEndSequence", f.turn_end),
                        "LastTurnTitleSequence" = GREATEST(s."LastTurnTitleSequence", f.title),
                        "LastUserPromptSequence" = GREATEST(s."LastUserPromptSequence", f.prompt),
                        "NextBackfillAttemptAt" = NULL
                    FROM folded f CROSS JOIN last_row l CROSS JOIN activity a
                    WHERE s."AgentSessionId" = session_id;
                END LOOP;
                RETURN NULL;
            END;
            $body$;

                        CREATE FUNCTION c1915_update() RETURNS trigger LANGUAGE plpgsql AS $body$
            DECLARE session_id uuid;
            BEGIN
                FOR session_id IN
                    SELECT "AgentSessionId" FROM old_entries UNION SELECT "AgentSessionId" FROM new_entries ORDER BY 1
                LOOP
                    INSERT INTO "SessionTranscriptSummaries"
                        ("AgentSessionId", "SchemaVersion", "Valid", "Count", "LastSequence", "HasActivity", "HasNullTimestampActivity")
                    SELECT s."Id", 1, false, 0, 0, false, false FROM "AgentSessions" s WHERE s."Id" = session_id
                    ON CONFLICT ("AgentSessionId") DO NOTHING;
                    PERFORM 1 FROM "SessionTranscriptSummaries" WHERE "AgentSessionId" = session_id FOR UPDATE;
                    UPDATE "SessionTranscriptSummaries" SET "Valid" = false, "NextBackfillAttemptAt" = NULL
                    WHERE "AgentSessionId" = session_id;
                END LOOP;
                RETURN NULL;
            END;
            $body$;

            CREATE FUNCTION c1915_delete() RETURNS trigger LANGUAGE plpgsql AS $body$
            DECLARE session_id uuid;
            BEGIN
                FOR session_id IN SELECT DISTINCT "AgentSessionId" FROM old_entries ORDER BY 1 LOOP
                    -- Never recreate a row while the parent session's cascade is deleting it.
                    UPDATE "SessionTranscriptSummaries" SET "Valid" = false, "NextBackfillAttemptAt" = NULL
                    WHERE "AgentSessionId" = session_id;
                END LOOP;
                RETURN NULL;
            END;
            $body$;

            CREATE FUNCTION c1915_truncate() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                UPDATE "SessionTranscriptSummaries" SET "Valid" = false, "NextBackfillAttemptAt" = NULL;
                RETURN NULL;
            END;
            $body$;

            CREATE TRIGGER c1915_insert AFTER INSERT ON "TranscriptEntries"
                REFERENCING NEW TABLE AS new_entries FOR EACH STATEMENT EXECUTE FUNCTION c1915_insert();
            CREATE TRIGGER c1915_update AFTER UPDATE ON "TranscriptEntries"
                REFERENCING OLD TABLE AS old_entries NEW TABLE AS new_entries FOR EACH STATEMENT EXECUTE FUNCTION c1915_update();
            CREATE TRIGGER c1915_delete AFTER DELETE ON "TranscriptEntries"
                REFERENCING OLD TABLE AS old_entries FOR EACH STATEMENT EXECUTE FUNCTION c1915_delete();
            CREATE TRIGGER c1915_truncate AFTER TRUNCATE ON "TranscriptEntries"
                FOR EACH STATEMENT EXECUTE FUNCTION c1915_truncate();
            """.ReplaceLineEndings("\n"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Durable transcript summaries cannot be discarded by a downgrade; disable the reader instead.");
        }
    }
}
