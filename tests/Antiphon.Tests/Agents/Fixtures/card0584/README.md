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

`codex-item.jsonl` and `codex-flat.jsonl` reduce the user/assistant/completion
events in Antiphon.SessionRunner.Tests/Fixtures/codex-tui-turn.jsonl and
codex-exec-turn.jsonl. Content, IDs and clocks are replaced; unrelated fields,
metadata and reasoning are omitted. `claude-prompts.jsonl` uses the string and
text-block FromUser shapes exercised by TranscriptNormalizerTests, plus the
queued_command attachment in Agents/Fixtures/queued-command.jsonl. IDs, clocks
and content are replaced. Tests assert literal line breaks independently of the
matcher, and exercise both Codex dialects in separate normalizer instances.
