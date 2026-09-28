-- Minimal tables and the relevant production indexes for the trailing ranking query.
CREATE TABLE "ChannelOutboundPublications" (
  "Id" uuid PRIMARY KEY,
  "SessionId" uuid NOT NULL,
  "PromptSequence" bigint NOT NULL,
  "FirstTextSequence" bigint NOT NULL DEFAULT 10,
  "LastTextSequence" bigint NOT NULL,
  "Path" text NOT NULL DEFAULT 'main',
  "Provider" text NOT NULL,
  "ConversationId" text NOT NULL,
  "State" text NOT NULL,
  "PublishedAt" timestamptz DEFAULT now()
);
CREATE UNIQUE INDEX "IX_ChannelOutboundPublications_IntervalTarget"
  ON "ChannelOutboundPublications" ("SessionId", "PromptSequence",
    "FirstTextSequence", "LastTextSequence", "Path", "Provider", "ConversationId");
CREATE TABLE "TranscriptEntries" (
  "AgentSessionId" uuid NOT NULL,
  "Sequence" bigint NOT NULL,
  "Kind" text NOT NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY ("AgentSessionId", "Sequence")
);
