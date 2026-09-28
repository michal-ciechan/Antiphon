TRUNCATE "ChannelOutboundPublications", "TranscriptEntries";
WITH params AS (SELECT :sessions::integer AS sessions, :turns::integer AS turns)
INSERT INTO "TranscriptEntries" ("AgentSessionId", "Sequence", "Kind")
SELECT ('00000000-0000-0000-0000-' || lpad(s::text, 12, '0'))::uuid,
       (turn - 1) * 20 + row_offset,
       CASE row_offset WHEN 1 THEN 'UserPrompt' WHEN 10 THEN 'AssistantText'
                   WHEN 20 THEN 'TurnEnd' ELSE 'ToolResult' END
FROM params, generate_series(1, params.sessions) AS s,
     generate_series(1, params.turns) AS turn,
     generate_series(1, 20) AS row_offset;
WITH params AS (SELECT :sessions::integer AS sessions, :turns::integer AS turns)
INSERT INTO "ChannelOutboundPublications" ("Id", "SessionId", "PromptSequence", "Provider", "ConversationId", "State", "LastTextSequence")
SELECT md5(s::text || '-' || turn::text)::uuid,
       ('00000000-0000-0000-0000-' || lpad(s::text, 12, '0'))::uuid,
       (turn - 1) * 20 + 1, 'telegram', 'chat-' || s::text, 'Published',
       (turn - 1) * 20 + 10
FROM params, generate_series(1, params.sessions) AS s,
     generate_series(1, params.turns) AS turn;
ANALYZE "TranscriptEntries";
ANALYZE "ChannelOutboundPublications";
