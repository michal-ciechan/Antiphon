namespace Antiphon.Tests.TestHelpers;

// Frozen post-CARD-1887 seed at ca8737e78170c9a441e216408da5fab636c34ca0.
// Classification is frozen too; this oracle has no production-query dependency.
internal static class SessionStateSeedBaseline
{
    internal const string EndPredicate =
        "\"Kind\" IN ('TurnEnd', 'SessionRestartBoundary')\n" +
        "OR (\"Kind\" = 'CompactBoundary' AND \"Text\" IS NOT NULL AND strpos(\"Text\", '(manual)') > 0)\n" +
        "OR (\"Kind\" = 'UserPrompt' AND \"Text\" IS NOT NULL AND \"Text\" LIKE '[Request interrupted%')";

    internal const string ActivityPredicate = """
        a."Kind" NOT IN ('TurnEnd', 'TurnTitle', 'SessionRestartBoundary', 'QueuedUserPrompt',
                        'QueueEnqueue', 'QueueDequeue', 'QueueRemove', 'CompactBoundary')
        AND (a."Kind" <> 'UserPrompt' OR a."Text" IS NULL OR
            (a."Text" NOT LIKE '<command-name>%'
             AND a."Text" NOT LIKE '<local-command-stdout>%'
             AND a."Text" NOT LIKE 'This session is being continued from a previous conversation%'
             AND a."Text" NOT LIKE '[Request interrupted%'))
        """;

    // Enumerate open tasks once per statement, rather than revisiting the task heap
    // for every requested session. Membership stays current at the statement snapshot.
    internal const string OpenTaskPins = """
        WITH open_tasks AS MATERIALIZED (
            SELECT "AgentSessionId", "ParentSessionId" FROM "AgentTasks"
            WHERE "Status" IN (0, 1, 2, 3)
        ), task_pins AS MATERIALIZED (
            SELECT "AgentSessionId" AS id FROM open_tasks WHERE "AgentSessionId" IS NOT NULL
            UNION
            SELECT "ParentSessionId" FROM open_tasks WHERE "ParentSessionId" IS NOT NULL
        )
        """;

    // Trusted enum literals, matching the persisted int representation. This is pinning,
    // never liveness: a phone-home outage must not evict an active conversation's evidence.
    internal const string Pinned = """
        (s."Status" IN (1, 2, 3)
         OR s."Id" IN (SELECT id FROM task_pins)
         OR EXISTS (SELECT 1 FROM "SessionQueuedMessages" q WHERE q."AgentSessionId" = s."Id"
             AND (q."Status" = 0 OR (q."Status" = 1 AND (q."DeliveryVerdict" IS NULL
                 OR (q."Origin" = 1 AND q."ChannelReplySettledAt" IS NULL)))))
         OR EXISTS (SELECT 1 FROM "AgentTaskLandNotifications" n WHERE n."ParentSessionId" = s."Id"
             AND n."ConfirmedAt" IS NULL AND n."State" <> 4)
         OR EXISTS (SELECT 1 FROM "AgentTaskDispatchWarningIntents" i WHERE i."ParentSessionId" = s."Id"
             AND i."MaterializedAt" IS NULL AND i."InitialState" <> 4))
        """;

    internal const string Sql = $"""
        {OpenTaskPins}
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
                AND ({EndPredicate}) ORDER BY t."Sequence" DESC LIMIT 1
        ) end_seq ON true
        LEFT JOIN LATERAL (
            SELECT t."Timestamp" FROM "TranscriptEntries" t WHERE t."AgentSessionId" = requested."SessionId"
                AND ({EndPredicate}) AND t."Timestamp" IS NOT NULL
            ORDER BY t."Timestamp" DESC LIMIT 1
        ) end_ts ON true
        LEFT JOIN LATERAL (
            SELECT count(*) > 0 AS "HasActivity", count(*) FILTER (WHERE a."Timestamp" IS NULL) > 0 AS "HasNullTimestampActivity",
                max(a."Timestamp") AS "ActivityTimestamp"
            FROM "TranscriptEntries" a WHERE a."AgentSessionId" = requested."SessionId"
                AND (end_seq."Sequence" IS NULL OR a."Sequence" > end_seq."Sequence")
                AND {ActivityPredicate}
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

}
