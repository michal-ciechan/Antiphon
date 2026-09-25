# CARD-0584 parser replays

`grok-turn.jsonl` reduces the provenance-labelled UserChunkRow, AgentChunkRow and
TurnCompletedRow in Antiphon.SessionRunner.Tests/GrokTranscriptTailerTests.cs
(grok 1.0.5 capture, session 01a01178-bfe3-7493-b326-1785d2ebf7db).
Only the user/assistant/completion shapes remain; usage and unrelated metadata
are omitted. Session/event/prompt IDs, clocks and content are test-owned tokens.
The replay substitutes these fields, runs GrokTranscriptNormalizer and ingests
its parts through AgentSessionRuntime. Newline removal models the historical
composer capture; the parser itself must preserve literal `deploythe` text.
These are offline replays, not a live provider qualification.
