# CARD-0267 — Attention row for an aging undelivered caller note

**Date:** 2026-08-31
**Status:** Plan only — no implementation or tests run.

## Outcome and decisions

`BriefUndelivered` is the right pattern but covers a different route: a dispatched task's brief is
Pending on the delegate's own working session. CARD-0264/0285 found a completed-task or check
**note** Pending on the task's **caller** session. The task may already be settled; a busy caller
simply has not reached a `WhenIdle` delivery point.

Add `AttentionKind.CallerNoteUndelivered` as a Warning-level, read-time attention row. It is one
row per qualifying queue message (identified by its existing `MessageId`), with no persisted
incident, acknowledgement flag, or background sweep. `GET /api/attention` derives it on every
existing read; the client already polls/invalidate-refreshes that projection every 15 seconds.

The row appears when a note is strictly older than the existing
`Delegation:DeliveryFailTimeoutMinutes` (10 minutes by default) and disappears on the next
projection when it is Sent or Canceled. Reuse that threshold, Warning severity, pure-projection
lifecycle, and no-hysteresis convention from `BriefUndelivered`; do not add a setting or hosted
service. Append the enum member as numeric value `18` — `AttentionKind` values are a client
contract and must never be renumbered.

The predicate deliberately does not call `IsWorkingAsync`. The caller may be genuinely busy,
falsely considered working, or idle with a stalled attempt; the signal is that its note has not
arrived, not a claim about why. Do not exclude a parked note: `ParkedMessage` remains the separate,
more specific delivery-failure diagnosis, just as it is for other Pending queue rows.

## Provenance and eligibility

In `AttentionService`, add `BuildCallerNoteUndeliveredItemsAsync(now, ct)` beside the existing
queue-derived `BuildParkedMessageItemsAsync` pass and invoke it from `GetAsync`. Its no-tracking
candidate query should retain only rows with:

- `Status == Pending`;
- `Origin == Delegation` or `Origin == Check`; and
- `CreatedAt < now - DeliveryFailTimeout`.

Recover each source task without parsing presentation text:

- Delegation completion/failure-reminder notes already persist `SourceTaskId` in
  `AgentTaskReplyService.DeliverToParentAsync` and the failure-reminder enqueue path.
- Check notes deliberately leave `SourceTaskId` null, but their
  `AgentTaskCheckService.ConversationKey(task.Id)` plus `TryParseCheckConversationKey` are the
  established durable parsing contract. Parse that key after the small candidate query and load
  the distinct task ids in one no-tracking task query.

Keep a candidate only when the recovered task has `ReplyTo == Session`, a `ParentSessionId`, and
`task.ParentSessionId == message.AgentSessionId`. This is the caller test: it excludes a launch
brief queued to `task.AgentSessionId`, unrelated/unparseable Check rows, and non-session routes.
Do not change the queue schema or duplicate Check provenance into `SourceTaskId`.

For each qualifying message return `CallerNoteUndelivered`, Warning, the recovered `TaskId`, caller
`SessionId`, and queue `MessageId`. Use the source task title; name origin, Pending state, caller
session, and age in the headline; use `CreatedAt` as stable `SinceUtc`; and provide only
`OpenDrawer`. The drawer exposes the durable task status/report. Never offer `SendNow`: bypassing
`WhenIdle` may type into a busy caller composer and would recreate the CARD-0266/CARD-0132 hazard.
The row does not retry, cancel, kill, settle, or otherwise mutate work.

No migration is required. The existing partial `IX_SessionQueuedMessages_OpenChannelCorrelations`
index begins with `Origin, Status`, already serving this narrow Pending-origin candidate lookup.
Do not add a field, index, endpoint, SignalR event, queue-delivery change, or alert sink. Amend the
`DeliveryFailTimeoutMinutes` XML documentation only enough to record this second delivery-grace
consumer; do not change the setting's value or watchdog behavior.

## Implementation slices

1. **Server contract and read-time projection**

   - In `server/Application/Dtos/AttentionDtos.cs`, append and document
     `CallerNoteUndelivered = 18` as a caller-session Delegation/Check note still Pending past the
     shared delivery grace. State that it is detection only.
   - In `server/Application/Services/AttentionService.cs`, add the batch projection above and call
     it beside the parked-message pass. Reuse `Duration`, `Evidence`, `DeliveryFailTimeout`, and
     task-drawer action conventions. Keep every query `AsNoTracking`; do not call
     `SessionMessageQueueService.IsWorkingAsync`.
   - Update the affected comment in `server/Application/Settings/DelegationSettings.cs` so the
     single clock accurately describes both the first-prompt watchdog and this caller-note row.

2. **Client type and presentation**

   - Add `'CallerNoteUndelivered'` to `client/src/api/attention.ts`, with the same caller-note
     semantics as the server. Query keys, the 15-second refetch, grouping, navigation, and action
     plumbing already support a task-scoped Warning row and need no mechanism change.
   - Add a concise Warning visual in `client/src/features/attention/attentionVisuals.ts`: label
     **Caller note waiting**, using the existing hourglass/mail visual family and a hint that silence
     means the caller has not received the note, not that the delegate is still running.
   - Add the string to `ALL_KINDS` in
     `client/src/features/attention/attentionVisuals.test.ts`; add one
     `client/src/features/attention/AttentionPanel.test.tsx` row proving the title, Pending-age
     headline, and new badge render. No component action code changes are needed because
     `OpenDrawer` already targets `TaskId`.

3. **Orchestrator instruction, not duplicate documentation**

   - Put this two-sentence contract in `server/Bundles/orchestrator.md`, after the current
     completion-note paragraph:

     > Do not treat the absence of a `[task … done]` note as evidence that the delegate is still
     > running: completion and check notes are WhenIdle and can wait behind your turn. When the
     > answer matters, read the task row or `delegate.ps1 -Status`; the eventual note is only a
     > delayed, possibly report-withheld echo.

   - Do **not** repeat it in `docs/orchestration-loop.md`. That document says launch bundles are
     the canonical standing instructions and warns that repeated rules drift. Its existing checking
     commands remain supporting operational detail; the bundle is what future orchestrators receive.

4. **Attention projection regression tests**

   Extend `tests/Antiphon.Tests/Application/AttentionServiceTests.cs` and its id-scoped `Scenario`
   helpers. Preserve the suite's shared-Postgres discipline: every assertion must filter to seeded ids.

   - Seed a **Succeeded** task with distinct delegate and caller sessions, then an 11+ minute
     Delegation Pending row on the caller with `SourceTaskId`. Assert one Warning row with task,
     caller session, message id, stable created-at `SinceUtc`, and `OpenDrawer`. This proves the
     signal reaches settled work, which the open-task pass cannot see.
   - Seed the Check equivalent with `SourceTaskId == null` and the canonical
     `AgentTaskCheckService.ConversationKey(task.Id)`; assert it produces the same kind. This pins
     the supported check provenance path rather than a body-marker parser.
   - Cover negative boundaries: under/exact grace, Sent, Canceled, an unparseable Check key, and a
     Delegation message on the delegate session rather than `ParentSessionId` produce no new kind.
     The final case prevents a queued launch brief being misreported as an unheard caller note.
   - Re-read after changing a qualifying row to Sent/Canceled and assert disappearance. This pins
     the no-sticky-row lifecycle; retain `ParkedMessage` coverage rather than filtering max-attempt
     Pending notes from the new predicate.

5. **Grok and Codex timestamp-order regression guards**

   Add tests only; do not change normalizers or `IsWorkingAsync`. CARD-0285 measured that each kind
   emits its turn end at a timestamp strictly later than preceding activity, unlike Claude's
   same-record pair. These guards stop a future transcript-format change silently reopening the
   CARD-0264 equal-timestamp defeat shape.

   - In `tests/Antiphon.SessionRunner.Tests/GrokTranscriptTailerTests.cs`, add a focused regular
     `turn_completed` test using the captured real Grok rows. Assert the emitted `TurnEnd` follows
     its immediate activity (`AssistantText` in the fixture) and that its timestamp is strictly
     greater, not merely greater-or-equal. Leave the cancelled trailing-chunk test unchanged: it
     intentionally proves the separate older-post-end backfill shape.
   - In `tests/Antiphon.SessionRunner.Tests/CodexTranscriptNormalizerTests.cs`, add the parallel
     captured-rollout test. For the TUI fixture (and the flat fixture if the small loop remains
     readable), assert each `task_complete`-derived `TurnEnd` immediately follows its last activity
     and has a strictly later timestamp. Do not weaken the assertion to turn identity or `>=`.

## Validation for the later code pass

Run the focused `AttentionServiceTests` and focused Grok/Codex normalizer tests with
`dotnet run --project tests/<ProjectName> -- --treenode-filter ...`, then run
`pwsh -File scripts/test-client.ps1`. Do not use `dotnet test`, and do not run `Antiphon.Tests`
concurrently with `Antiphon.Agents.Pty.Tests`; full verification runs those projects sequentially.

## Explicit non-goals

- No `MessageSendMode.Now`, synthetic prompt, side channel, queue flush change, or attempt to wake
  a busy caller.
- No per-kind `IsWorkingAsync` branch, transcript normalization change, or reopening of CARD-0264.
- No migration, background attention sweep, alert sink, or acknowledgement lifecycle.
- `AgentTasks.Status` / `CompletedAt` remains the finish fact; the queued completion turn is an
  eventual delivery echo.
