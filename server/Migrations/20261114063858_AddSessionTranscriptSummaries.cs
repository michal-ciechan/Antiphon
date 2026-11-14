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

            // No transcript rewrite or backfill. Invalid rows are conservative retry work.
            migrationBuilder.Sql("""
            CREATE FUNCTION c1915_insert() RETURNS trigger LANGUAGE plpgsql AS $body$
            DECLARE session_id uuid;
            BEGIN
                FOR session_id IN SELECT DISTINCT "AgentSessionId" FROM new_entries ORDER BY 1 LOOP
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
