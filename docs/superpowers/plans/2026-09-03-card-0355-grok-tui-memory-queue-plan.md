# CARD-0355 — Grok's TUI-memory follow-up queue needs a canary, not a transcript kind

**Date:** 2026-09-03  
**Card:** CARD-0355 — *Grok prompt queue is TUI-memory-only — CARD-0135 checks have no JSONL kind to see*  
**Status:** plan only; no production code changed  
**Verified against:** `c9f4b2c8`

## Decision

Do **not** add `QueuedUserPrompt`, widen `TranscriptPromptSpan`, or change
`ChannelReplyDispatcher` for Grok. A Grok queued follow-up has no durable record until it drains,
at which point its ordinary `user_message_chunk` already normalizes to `UserPrompt`.

Do **not** treat a queue pane, a screen redraw, or a body that has left the composer as final
delivery confirmation. They are TUI-memory observations, can disappear on process death, and do
not prove that the model received the prompt. In particular, a queued body normally leaves the
composer, so the existing "body still in composer" failure signal is the wrong polarity for this
case.

The current delegate queue and watchdog already have the safe transient state for the normal
Grok race:

1. `AgentTaskDispatcher` queues the task brief as a `SessionQueuedMessage` with origin
   `Delegation` and `WhenIdle`; it does not use `RunnerGrokAdapter.SendPromptAsync`.
2. If the idle check loses its race and Grok accepts Enter as an in-TUI follow-up, the previous
   turn gives `WaitForTranscriptConfirmAsync` an observable baseline. No matching `UserPrompt`
   within its confirm window is `NoTranscriptRecord`, not delivery success.
3. `HandleDeliveryFailureAsync` returns the attempted row to `Pending` and preserves its attempt
   metadata. It does not manufacture a transcript fact.
4. At the 10-minute delivery watchdog, `FailNeverStartedAsync` sees that exact marker-bearing
   delegation row still `Pending` and a working session, so CARD-0117 D8 defers the task rather
   than failing or killing it. On drain, the real `UserPrompt` settles delivery; if the process
   dies before drain, there is no false success to preserve.

Thus the card's feared `FailNeverStarted` result would require a different state transition from
the current path (for example, a queued body remaining `Sent`, or an unobservable-baseline screen
fallback). The headed probe below is the proportional way to establish whether the real TUI has
such a path. It is not authority to promote private UI state into a durable delivery fact.

The parent corpus result remains decisive for Codex: no queue kind or TUI queue equivalent was
observed across 169 rollouts. This card adds no Codex work unless a headed probe finds one.

## S0 — headed Grok queue probe (required evidence gate)

Extend `tests/Antiphon.Agents.Pty.Tests/GrokSubmitWhileWorkingCanaryTests.cs` (or replace its
stale cancellation-only tool-turn arm) with one `[Explicit]`, `[NotInParallel("Headed")]`,
`[ParallelLimiter<ProcessSpawnLimit>]` real-TUI canary. It stays gated by
`ANTIPHON_HEADED_TESTS=1`, uses `GkSession`'s production launch shape and real `GROK_HOME`, and
cleans its uniquely named session directory and temporary cwd in `finally`.

1. Read only the non-secret `[ui].follow_up_behavior` setting used by that `GROK_HOME`. Treat an
   absent value as the vendor default, `queue`; skip with the measured value if the operator has
   selected `steer`. Never edit the user's configuration to manufacture the default, since that
   would no longer be a production-shape canary.
2. Start a long, harmless tool turn (the existing `Start-Sleep` shape is suitable) and wait for a
   `tool_call` update, proving that the predecessor turn is still open.
3. Capture the complete-update count and the rendered screen. Type one generated distinctive
   single-line marker through the normal PTY body path, wait for it to render, and write exactly
   one separate `\r`. Do not re-enter: while the composer is empty, a second Enter sends the top
   queued follow-up immediately and would invalidate the observation.
4. Until the predecessor `turn_completed`, assert that no `user_message_chunk` contains that
   marker. Other ACP progress from the still-running predecessor is expected; "silent" here means
   no durable record of the queued body. Log the row kinds and timestamps needed to establish that
   ordering.
5. Capture the ordinary rendered screen. Then use Grok's documented Windows alternate queue-pane
   chord, `Ctrl+'` (BEL), only in this isolated canary, capture one screen, and toggle it back.
   Log whether the marker or a queue count is visible in either snapshot; do not make an
   unmeasured screen fragment a production predicate.
6. Wait for the predecessor `turn_completed`, then require exactly one marker-bearing
   `user_message_chunk` after that boundary. Save the before/after screen snippets and row-order
   summary through `GkSession.MeasurementLog`; do not retain a session transcript after cleanup.

Run only that named explicit canary after the code slice, serially and with a disposable output
path. It spends real Grok turns; it is not a normal CI verification:

```powershell
$env:ANTIPHON_HEADED_TESTS = '1'
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0355/ -- --treenode-filter "/*/*/GrokSubmitWhileWorkingCanaryTests/*"
```

Delete only the generated, repository-local `bin-card0355` directories after the run. The test's
measurement log is the review evidence; report whether the default screen and the toggled queue
pane exposed a usable marker, rather than inferring either result from the vendor guide.

## S1 — disposition after the probe

### Expected result: ordinary in-memory queue

If the marker is absent from `updates.jsonl` until `turn_completed`, appears once afterwards as a
normal `user_message_chunk`, and the queue row is `Pending` while the session remains working,
land only the headed canary and this evidence update. Production code, transcript normalizers,
`TranscriptPromptSpan`, `ChannelReplyDispatcher`, watchdog timeout, and Codex all remain
unchanged.

The queue pane is not required to be passively visible. A pane that appears only after a toggle is
useful diagnostic evidence for the canary, but cannot be read by Antiphon without typing into a
live session. Production must never toggle it merely to decide whether to kill or settle work.

### Unexpected result: open a narrowly evidenced follow-up

Do not implement a speculative fallback in this card. File a follow-up only if the canary proves
one of these concrete contradictions:

| Observation | Follow-up owner and boundary |
|---|---|
| The marker reaches `user_message_chunk` before the predecessor ends | Grok normalizer/timing investigation; the durable row already exists and must be characterized before any consumer changes. |
| A genuine queued delegation brief stays `Sent` (or becomes screen-confirmed) while no `UserPrompt` exists, allowing the watchdog to fail it | `SessionMessageQueueService` plus `AgentTaskDispatcher`; make the queue attempt revert to `Pending` or add a read-only, body-specific **defer**. It must not mark delivery successful, settle a task, or create a transcript kind. |
| A queue pane/body signal is visible without injecting a key and is the only evidence that prevents a real watchdog failure | `AgentTaskDispatcher` only, after a second headed reproduction. It may withhold the destructive watchdog decision while the same live working turn remains open; it may not be persisted, used for settlement, or survive restart. |
| Codex headed probe finds an equivalent queue | New Codex-specific card; do not couple it to Grok by widening a shared kind filter. |

## Tests and invariants

- Keep `AgentTaskDeliveryWatchdogTests.a_working_session_with_a_pending_brief_is_neither_failed_nor_killed` and `a_sent_brief_on_a_working_session_is_not_deferred` unchanged. Together they pin the important distinction: `Pending` is the queue's unresolved evidence; `Sent` without a durable prompt is not.
- Add one fast `SessionMessageQueueDeliveryVerificationTests` Grok-harness regression only if it
  is not already directly covered: an observable, working Grok session whose marker-bearing
  delivery gets no `UserPrompt` inside the confirmation window must return its row to `Pending`,
  not final `Sent`. Combined with the existing watchdog pair above, this pins the reviewed state
  transition without trying to imitate the vendor queue in FakeGrok.
- Keep the CARD-0135 `QueuedUserPrompt` tests unchanged. They describe Claude's real on-disk
  `queued_command`, not a generic UI concept.
- Keep CARD-0342's Grok tests unchanged: transcript confirmation or sustained composer departure
  governs final `Sent`; redraws, titles, MCP output, quiet time, and queue-pane state do not.
- Do not extend FakeGrok until S0 has recorded the real queue rendering and update order. A fake
  cannot establish a missing vendor record.

## Explicit exclusions

- No new Grok `QueuedUserPrompt`, `QueueEnqueue`, or related JSONL mapping.
- No widening of `TranscriptPromptSpan`, `ChannelReplyDispatcher`, transcript working/idle, or
  settlement.
- No queue-pane toggle, focus movement, or raw-input inspection in production.
- No timeout widening, kill/relaunch policy, or change to the CARD-0055 transcript-confirmation
  invariant.
- No Codex expansion absent a real headed observation.
