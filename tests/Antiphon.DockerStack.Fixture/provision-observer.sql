-- SELECT-only observer. No schema migration and no write grant.
GRANT SELECT ON "AgentSessions" TO observer;
GRANT SELECT ON "SessionQueuedMessages" TO observer;
GRANT SELECT ON "TranscriptEntries" TO observer;
