# CARD-0242 — Codex writes `turn_aborted`; the normalizer drops it

**Status:** Root cause confirmed by reconstructing the closed normalizer switch against the Codex protocol and two public measured rollouts. No live Esc was pressed on this runner (no `CODEX_HOME` sessions here). No product code was changed.

**Date:** 2026-10-17. Code read on `feat/card-task-58bc3539` at `9a438ce572ec7279b703434c65828a960ada966c`. The cited Antiphon files are byte-identical on `origin/master` `7804e6efea1d767b2647ca4b9d8363903ef4a1da` (`git diff --numstat` empty). Installed CLI on this runner: `codex-cli 0.160.0` at `/usr/local/bin/codex`.

## Verdict

An Esc (or any other `Op::Interrupt`) while a Codex process stays up is an Antiphon ingestion gap, not a missing CLI signal. Codex persists `event_msg` / `payload.type = turn_aborted`. `CodexTranscriptNormalizer` maps only six payload types and returns nothing for every other type, so the row never becomes a `TurnEnd`. The session stays mid-turn. After the model has already spoken, `TaskDeadlinePolicy` then fails the open task on the 20-minute model-wait clock. A silent first prompt is the CARD-1151 boot episode instead, and that one is detection only.

The boundary that already means "idle, not a report" is a `TurnEnd` whose `StopReason` is exactly `cancelled`. Mapping `turn_aborted` to `end_turn` would settle the partial narration as `Succeeded` (CARD-0159).

## What the card asked

1. The normalizer still has no abort arm. Confirmed below.
2. An aborted live turn should become that `cancelled` boundary so working/idle, the WhenIdle flush, and settlement all see it immediately. The task stays `Working`. It does not wait out the phase deadline, and it does not succeed on the text written before the abort.
3. The signal exists in Codex's rollout. The normalizer does not read it.

## The drop

`Normalize` accepts only `type == event_msg`, then switches on `payload.type` (`src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs:160-178`):

| Payload type | Result |
|---|---|
| `user_message`, `agent_message`, `agent_reasoning`, `item_completed`, `token_count` | content or usage |
| `task_complete` | `TurnEnd`, `StopReason` synthesized as `end_turn` (`:314-349`, literal at `:343`) |
| anything else, including `turn_aborted` | `[]` |

The class remark at `:44-50` is still true of `task_complete`: that row has no `stop_reason` field, and an `error` object on it is still stored as `end_turn` plus the API-error fields. CARD-0159 recorded the same fact and the strand (`docs/superpowers/plans/2026-08-30-card-0159-settlement-positive-evidence-plan.md:138-140`). That note did not name `turn_aborted`. The 2026-08-20 fixtures this repo captured never contain one. Their payload types are only `task_started`, `task_complete`, `token_count`, `user_message`, `agent_message`, `item_completed`, and `thread_settings_applied` (plus `session_meta`, `turn_context`, `response_item`). The completed-turn shape is `tests/Antiphon.SessionRunner.Tests/Fixtures/codex-tui-turn.jsonl:13`: `event_msg` / `task_complete`, with `turn_id` and no `stop_reason`.

`Rows_v1_does_not_map_produce_nothing` pins the drop for the types those fixtures do contain (`tests/Antiphon.SessionRunner.Tests/CodexTranscriptNormalizerTests.cs:314-328`). A `turn_aborted` line takes the same default arm.

## The signal Codex already writes

Upstream `EventMsg` is `#[serde(tag = "type", rename_all = "snake_case")]`. Variant `TurnAborted(TurnAbortedEvent)` therefore serializes as `type: turn_aborted`. `TurnAbortedEvent` carries optional `turn_id`, required `reason`, optional `error`, and optional `duration_ms`. `TurnAbortReason` is `interrupted`, `replaced`, `review_ended`, or `budget_limited`. `Op::Interrupt` is documented as the submission that produces this event. Rollout policy returns true for `EventMsg::TurnAborted`, so the row is persisted, in the same `event_msg` + `payload` envelope as `task_complete`.

Measured outside this repo, both before the 2026-08-20 fixtures:

- openai/codex#38395 (2026-08-13). Esc shortly after submit. The rollout contained `event_msg: task_started` then `event_msg: turn_aborted` (`duration_ms: 865`). The process stayed up; a later prompt was appended to the same file.
- openai/codex#20696 (2026-05-01). Session JSONL order: `task_started`, assistant activity, `turn_aborted`, then a new `task_started`.

On this runner the `codex-cli 0.160.0` binary contains the literals `turn_aborted`, `TurnAborted`, `review_ended`, `budget_limited`, and `task_complete`. An upstream unit test accepts a row with only `{"type":"turn_aborted","reason":"interrupted"}` (`turn_id` absent).

Reconstructed persisted line, not a capture from this machine:

```json
{"timestamp":"2026-08-13T15:24:10.773Z","type":"event_msg","payload":{"type":"turn_aborted","turn_id":"01a0abort","reason":"interrupted","duration_ms":865}}
```

## Why the delegate then sits until a deadline

A `TurnEnd` is what ends a turn. `TranscriptWorkingState` (`src/Antiphon.SessionRunner/TranscriptWorkingState.cs:41-46`) and the server query (`server/Infrastructure/Data/TranscriptWorkingStateQuery.cs:16-19`) both treat `Kind == TurnEnd` as an end, with no stop-reason check. Activity after the previous end — the `UserPrompt`, `Thinking`, or `AssistantText` already ingested for this turn — keeps `SessionMessageQueueService.IsWorkingAsync` true.

`IsTurnBoundary` (`server/Application/Services/AgentSessionRuntime.cs:498-512`) fires for `end_turn`, `cancelled`, `error`, and Claude's `[Request interrupted` user marker. A dropped row fires none of them, so the WhenIdle flush does not run.

`TaskDeadlinePolicy` applies the phase clock only while that working verdict is true (`server/Application/Services/TaskDeadlinePolicy.cs:184-192`). The last ingested kind selects the clock (`:251-266`):

- `UserPrompt`, `Thinking`, `AssistantText`, `ToolResult` → model wait, default 20 minutes (`server/Application/Settings/DelegationSettings.cs:456`).
- `ToolCall` → local execution, default 90 minutes (`:527`). Codex never emits `ToolCall` (`CodexTranscriptNormalizer.cs:259-264`), so an interrupt during a shell still sees the 20-minute clock when the last stored row is narration or the prompt.
- The role ceiling is 240 minutes from dispatch (`DelegationSettings.cs:431`, policy comment at `TaskDeadlinePolicy.cs:18-20`).

`AgentTaskDispatcher.FailOverdueTasksAsync` (`server/Application/Services/AgentTaskDispatcher.cs:3640`) fails a `Dispatched` or `Working` task once `Breached` is true. Two exceptions matter:

- A boot turn (accepted prompt on this launch clock and no model row since) is detection only. `BootReplyWatch.LoadBootTurnAsync` returns null once `AssistantText`, `Thinking`, `ToolCall`, `ToolResult`, or `TurnEnd` exists (`server/Application/Services/BootReplyWatch.cs:57-62`, `:179`). `TryFailOverdueAsync` then returns without failing (`AgentTaskDispatcher.cs:3755-3761`). CARD-1151 narrowed the card's "until the deadline" sentence for a silent first prompt.
- After any of those model rows, boot facts are absent, the session is still working, and the 20-minute model wait is what fails the task.

A child killed mid-turn is a different path. The tailer records that the file then has no `task_complete` and that nothing here synthesizes one; relaunch writes `SessionRestartBoundary` (`src/Antiphon.SessionRunner/CodexTranscriptTailer.cs:53-55`). This card is the live-process abort.

## What should be stored

One new arm. `payload.type == turn_aborted` emits a single `TurnEnd`:

- `StopReason` is the literal `cancelled` (`TranscriptKinds.StopReasons.Cancelled`). `IsReportBoundary` excludes only that string (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:408-416`, `:474-475`). `interrupted`, `end_turn`, `error`, or null would still be a report boundary.
- `ApiCallId` is `turn_id` when present, and null when the older row omits it. Do not require `reason`.
- Do not set `IsApiError`. An API failure is still the measured `task_complete.error` row, which must stay `end_turn` plus the error fields.
- All four reasons share this arm. `replaced` is submit-during-a-running-turn; the partial text must not settle, and the later `task_complete` still can.

Downstream already does the rest, with no kind check:

- `IsTurnBoundary` flushes the queue on `cancelled` (`AgentSessionRuntime.cs:506`).
- `TaskReportTurnSelector` returns `Interrupted` (`server/Application/Services/TaskReportTurnSelector.cs:151-154`).
- `ExtractMarkedTurnAsync` will not treat it as a report (`AgentTaskReplyService.cs:3329-3332`).
- `RecordInterruptedTurnAsync` writes one warning and leaves the task `Working` (`:1175-1200`). The warning text will say `cancelled`, which is the existing Grok wording.
- Working/idle sees a `TurnEnd`, so the phase deadline disarms. The 240-minute ceiling remains for a task that never finishes.

Do not invent a Claude `[Request interrupted` user row. Codex does not write that text, and a synthetic `UserPrompt` would move the delivery baseline. Do not change `task_complete`. Do not synthesize a boundary for a killed child; `SessionRestartBoundary` already owns that.

## Red-first witness

One new test on `tests/Antiphon.SessionRunner.Tests/CodexTranscriptNormalizerTests.cs`, method `Turn_aborted_is_a_cancelled_boundary_and_not_end_turn`. Feed the reconstructed line above (and a second line with no `turn_id`). Assert one part, `Kind == TurnEnd`, `StopReason == "cancelled"`, `ApiCallId` equal to the turn id when present, and `IsApiError` not true. On today's switch the list is empty, so `ShouldHaveSingleItem` fails.

One filter, and no other:

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-0242-turn-aborted -- dotnet run --project tests/Antiphon.SessionRunner.Tests -- --treenode-filter "/*/*/CodexTranscriptNormalizerTests/Turn_aborted_is_a_cancelled_boundary_and_not_end_turn"
```

The existing cancelled-settlement tests already cover the idle-not-report path once the row exists. This witness is only the ingestion arm.

## Remaining uncertainties

- No Esc was captured on this runner. The JSON envelope is the `task_complete` envelope from the 2026-08-20 fixture plus the upstream `TurnAborted` schema and the two public captures. A headed canary is not required to land the arm; it would only re-confirm a shape those sources already agree on.
- A `SIGKILL` that dies before the flush still leaves no row. That stays the restart-boundary path.
- `turn_aborted.error`, if a future CLI sets it, is ignored by the smallest arm. Today's measured API failure is `task_complete.error`.

## Not done, noted

No code was changed. The fix is the one `turn_aborted` arm above, emitting `TurnEnd` / `cancelled`, witnessed by that single test.
