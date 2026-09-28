SELECT c."SessionId"
FROM "ChannelOutboundPublications" AS c
WHERE c."State" = 'Published' AND NOT EXISTS (
    SELECT 1
    FROM "ChannelOutboundPublications" AS c0
    WHERE c0."SessionId" = c."SessionId" AND c0."PromptSequence" = c."PromptSequence" AND c0."Provider" = c."Provider" AND c0."ConversationId" = c."ConversationId" AND c0."State" = 'Published' AND c0."LastTextSequence" > c."LastTextSequence") AND ((
    SELECT t."Sequence"
    FROM "TranscriptEntries" AS t
    WHERE t."AgentSessionId" = c."SessionId" AND t."Sequence" > c."PromptSequence" AND (t."Kind" = 'UserPrompt' OR (t."Kind" = 'QueuedUserPrompt' AND EXISTS (
        SELECT 1
        FROM "TranscriptEntries" AS t0
        WHERE t0."AgentSessionId" = c."SessionId" AND t0."Kind" = 'TurnEnd' AND t0."Sequence" > c."PromptSequence" AND t0."Sequence" < t."Sequence")))
    ORDER BY t."Sequence"
    LIMIT 1) IS NULL OR EXISTS (
    SELECT 1
    FROM "TranscriptEntries" AS t1
    WHERE t1."AgentSessionId" = c."SessionId" AND t1."Kind" = 'AssistantText' AND t1."Sequence" > c."LastTextSequence" AND t1."Sequence" < (
        SELECT t2."Sequence"
        FROM "TranscriptEntries" AS t2
        WHERE t2."AgentSessionId" = c."SessionId" AND t2."Sequence" > c."PromptSequence" AND (t2."Kind" = 'UserPrompt' OR (t2."Kind" = 'QueuedUserPrompt' AND EXISTS (
            SELECT 1
            FROM "TranscriptEntries" AS t3
            WHERE t3."AgentSessionId" = c."SessionId" AND t3."Kind" = 'TurnEnd' AND t3."Sequence" > c."PromptSequence" AND t3."Sequence" < t2."Sequence")))
        ORDER BY t2."Sequence"
        LIMIT 1)))
GROUP BY c."SessionId"
ORDER BY (
    SELECT t4."CreatedAt"
    FROM "TranscriptEntries" AS t4
    WHERE t4."AgentSessionId" = c."SessionId"
    ORDER BY t4."Sequence" DESC
    LIMIT 1) IS NOT NULL DESC, (
    SELECT t4."CreatedAt"
    FROM "TranscriptEntries" AS t4
    WHERE t4."AgentSessionId" = c."SessionId"
    ORDER BY t4."Sequence" DESC
    LIMIT 1) DESC, max(c."PublishedAt") DESC, c."SessionId"
LIMIT 100;
