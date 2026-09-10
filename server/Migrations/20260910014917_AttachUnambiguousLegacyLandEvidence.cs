using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AttachUnambiguousLegacyLandEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLegacy",
                table: "AgentTaskLandNotifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);
            // Attach only existing, uniquely attributable transport evidence. Never enqueue history.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE c467_legacy_candidates ON COMMIT DROP AS
                SELECT e."Id" AS event_id, e."AgentTaskId" AS task_id, e."At" AS event_at,
                    q."Id" AS queue_id, q."AgentSessionId" AS session_id, q."Body" AS body,
                    q."CreatedAt" AS queued_at,
                    count(*) OVER (PARTITION BY e."Id") AS event_matches,
                    count(*) OVER (PARTITION BY q."Id") AS queue_matches
                FROM "AgentTaskEvents" e JOIN "AgentTasks" t ON t."Id" = e."AgentTaskId"
                JOIN "SessionQueuedMessages" q ON q."AgentSessionId" = t."ParentSessionId"
                    AND q."ConversationKey" = 'land:' || replace(t."Id"::text, '-', '')
                    AND q."Origin" = 3 AND q."SourceLandNotificationId" IS NULL
                    AND q."Body" = e."Detail" AND q."CreatedAt" >= e."At"
                    AND q."CreatedAt" <= e."At" + interval '5 minutes'
                WHERE e."LandRequestId" IS NULL AND e."Type" IN (21,22,24,29,30) AND t."ReplyTo" = 1
                    AND NOT EXISTS (SELECT 1 FROM "AgentTaskLandNotifications" n WHERE n."SourceEventId" = e."Id");
                CREATE TEMP TABLE c467_legacy_links ON COMMIT DROP AS
                SELECT c.*, gen_random_uuid() AS request_id, gen_random_uuid() AS note_id,
                    encode(sha256(convert_to(c.session_id::text || E'\n' || c.body, 'UTF8')), 'hex') AS digest
                FROM c467_legacy_candidates c WHERE event_matches = 1 AND queue_matches = 1
                    AND NOT EXISTS (SELECT 1 FROM "AgentTaskEvents" later
                        WHERE later."AgentTaskId" = c.task_id AND later."Type" IN (21,22,24,29,30)
                        AND (later."At", later."Id") > (c.event_at, c.event_id));
                INSERT INTO "AgentTaskLandRequests" ("Id", "TaskId", "RequestedAt", "ReplyTo", "ParentSessionId",
                    "State", "IsPending", "TerminalEventId", "Attempt", "LastEvaluatedAt", "LastProgressAt",
                    "HighestProgress", "HoldEpisode", "ConcurrencyToken")
                SELECT request_id, task_id, event_at, 1, session_id, 4, false, event_id, 0, event_at, event_at,
                    -2, 0, gen_random_uuid() FROM c467_legacy_links;
                INSERT INTO "AgentTaskLandNotifications" ("Id", "IsLegacy", "RequestId", "TaskId", "SourceEventId",
                    "Kind", "ReplyTo", "ParentSessionId", "Body", "ContentDigest", "CreatedAt", "NextAttemptAt",
                    "EnqueueAttempts", "State", "QueueMessageId", "EnqueuedAt", "ConcurrencyToken")
                SELECT note_id, true, request_id, task_id, event_id, 3, 1, session_id, body, digest, event_at,
                    event_at, 0, 2, queue_id, queued_at, gen_random_uuid() FROM c467_legacy_links;
                UPDATE "SessionQueuedMessages" q SET "SourceLandNotificationId" = l.note_id,
                    "SourceTaskId" = l.task_id, "ContentDigest" = l.digest, "NoteHeader" = split_part(l.body, E'\n', 1)
                FROM c467_legacy_links l WHERE q."Id" = l.queue_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "SessionQueuedMessages" q SET "SourceLandNotificationId" = NULL,
                    "SourceTaskId" = NULL, "ContentDigest" = NULL, "NoteHeader" = NULL
                FROM "AgentTaskLandNotifications" n WHERE n."IsLegacy" AND q."SourceLandNotificationId" = n."Id";
                CREATE TEMP TABLE c467_legacy_requests ON COMMIT DROP AS
                    SELECT "RequestId" FROM "AgentTaskLandNotifications" WHERE "IsLegacy";
                DELETE FROM "AgentTaskLandNotifications" WHERE "IsLegacy";
                DELETE FROM "AgentTaskLandRequests" WHERE "Id" IN (SELECT "RequestId" FROM c467_legacy_requests);
                """);
            migrationBuilder.DropColumn(
                name: "IsLegacy",
                table: "AgentTaskLandNotifications");
        }
    }
}
