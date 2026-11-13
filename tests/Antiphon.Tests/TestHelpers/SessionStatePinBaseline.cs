using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Tests.TestHelpers;

// Frozen production SELECTs at 1fde603cce208ff8a9147715101b3976c737edd0.
// Independent of the replacement task membership and its CTEs.
internal static class SessionStatePinBaseline
{
    internal const string Pinned = """
        (s."Status" IN (1, 2, 3)
         OR EXISTS (SELECT 1 FROM "AgentTasks" task WHERE task."Status" IN (0, 1, 2, 3)
             AND (task."AgentSessionId" = s."Id" OR task."ParentSessionId" = s."Id"))
         OR EXISTS (SELECT 1 FROM "SessionQueuedMessages" q WHERE q."AgentSessionId" = s."Id"
             AND (q."Status" = 0 OR (q."Status" = 1 AND (q."DeliveryVerdict" IS NULL
                 OR (q."Origin" = 1 AND q."ChannelReplySettledAt" IS NULL)))))
         OR EXISTS (SELECT 1 FROM "AgentTaskLandNotifications" n WHERE n."ParentSessionId" = s."Id"
             AND n."ConfirmedAt" IS NULL AND n."State" <> 4)
         OR EXISTS (SELECT 1 FROM "AgentTaskDispatchWarningIntents" i WHERE i."ParentSessionId" = s."Id"
             AND i."MaterializedAt" IS NULL AND i."InitialState" <> 4))
        """;

    internal const string Sql = $"""
        SELECT requested."SessionId", s."Id" IS NOT NULL AS "Exists",
            COALESCE({Pinned}, false) AS "Pinned", s."StartedAt" AS "AcceptedGeneration",
            totals."Count", totals."NewestEffectiveTimestamp",
            COALESCE(last_row."Sequence", 0) AS "LastSequence", last_row."Id" AS "LastEntryId",
            last_row."Kind" AS "LastKind", last_row."Timestamp" AS "LastTimestamp", last_row."CreatedAt" AS "LastCreatedAt",
            end_seq."Sequence" AS "EndSequence", end_ts."Timestamp" AS "EndTimestamp",
            activity."HasActivity", activity."HasNullTimestampActivity", activity."ActivityTimestamp",
            turn_end."Sequence" AS "LastTurnEndSequence", title."Sequence" AS "LastTurnTitleSequence",
            prompt."Sequence" AS "LastUserPromptSequence"
        FROM unnest(@sessionIds) AS requested("SessionId")
        LEFT JOIN "AgentSessions" s ON s."Id" = requested."SessionId"
        LEFT JOIN LATERAL (
            SELECT count(*) AS "Count", max(COALESCE(t."Timestamp", t."CreatedAt")) AS "NewestEffectiveTimestamp"
            FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
        ) totals ON true
        LEFT JOIN LATERAL (
            SELECT t."Id", t."Sequence", t."Kind", t."Timestamp", t."CreatedAt" FROM "TranscriptEntries" t
            WHERE t."AgentSessionId" = requested."SessionId" ORDER BY t."Sequence" DESC LIMIT 1
        ) last_row ON true
        LEFT JOIN LATERAL (
            SELECT t."Sequence" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND ({TranscriptWorkingStateQuery.EndPredicate}) ORDER BY t."Sequence" DESC LIMIT 1
        ) end_seq ON true
        LEFT JOIN LATERAL (
            SELECT t."Timestamp" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND ({TranscriptWorkingStateQuery.EndPredicate}) AND t."Timestamp" IS NOT NULL
            ORDER BY t."Timestamp" DESC LIMIT 1
        ) end_ts ON true
        LEFT JOIN LATERAL (
            SELECT count(*) > 0 AS "HasActivity", count(*) FILTER (WHERE a."Timestamp" IS NULL) > 0 AS "HasNullTimestampActivity",
                max(a."Timestamp") AS "ActivityTimestamp"
            FROM "TranscriptEntries" a WHERE a."AgentSessionId" = requested."SessionId"
                AND (end_seq."Sequence" IS NULL OR a."Sequence" > end_seq."Sequence")
                AND {TranscriptWorkingStateQuery.ActivityPredicate}
        ) activity ON true
        LEFT JOIN LATERAL (
            SELECT t."Sequence" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND t."Kind" = 'TurnEnd' ORDER BY t."Sequence" DESC LIMIT 1
        ) turn_end ON true
        LEFT JOIN LATERAL (
            SELECT t."Sequence" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND t."Kind" = 'TurnTitle' ORDER BY t."Sequence" DESC LIMIT 1
        ) title ON true
        LEFT JOIN LATERAL (
            SELECT t."Sequence" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND t."Kind" = 'UserPrompt' ORDER BY t."Sequence" DESC LIMIT 1
        ) prompt ON true
        """;

    internal const string PinsSql = $"SELECT s.\"Id\" AS \"Value\" FROM \"AgentSessions\" s WHERE {Pinned}";
}
