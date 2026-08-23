# CARD-0117 — a reuse `/compact` Codex answers as work, and a session-wide uncorrelated incident that poisons every later task: plan

**Date:** 2026-08-23 · **Card:** CARD-0117 (`8bee3873-162c-4426-b8e6-20ebafacf3ed`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `9b5b1dc`. Every line number and behaviour claim below was re-read out
of the code on that commit — the investigation was written against `9cf7187` and the two arms of
`FailNeverStartedAsync` have not moved.

**Established fact, not re-derived here:**
`docs/investigations/2026-08-23-card-0117-mid-turn-dispatch-drop-investigation.md` (Grok, `537f1ac`).
The card's original hypothesis — a mid-turn CR-swallow of the boot prompt — is **refuted**. The
9a518def brief was never typed: `Pending`, `deliveryAttempts = 0`, still on the queue today. This
plan designs against the investigation's timeline and does not re-open it.

**Related:** CARD-0077 (the reuse `/compact` and `TranscriptPromptSpan`, which this narrows),
CARD-0055 / CARD-0024 (transcript-confirmed delivery, the working-kill guard, "pull before you
judge"), CARD-0056 (a failed launch must not kill work; unclaimed never implies kill),
CARD-0020 S2/S3 (`TaskDeadlinePolicy` — the clock this plan hands the deferred case to),
CARD-0143 / CARD-0141 (`ProviderContract`'s `Forbidden` precedent: a body that must never be typed
to a kind), CARD-0113 (a genuinely different defect — the adapter baseline, not this path).

---

## Verdict up front

**Two independent defects. Neither subsumes the other, and the one that produced the 2026-08-21
loss is not the one the card's title points at.**

1. **The trigger (dominant for this miss).** Reusing an unrelated Shared pool delegate types
   `/compact {goal}` into *every* kind. Claude records that as `<command-name>` /
   `<local-command-stdout>` wrappers plus a raw echo — housekeeping every consumer already skips.
   **Codex records it as an ordinary user message and answers it as a work turn**, measured twice on
   one session (seq 19 and seq 30 of 51ee57fc). That single mistake causes the entire observed
   failure: a multi-minute unattributed work turn, real file edits in a shared checkout under no
   task's correlation, the markered brief blocked `WhenIdle` behind it, and — at that turn's own
   `TurnEnd` — the `DelegateReportUncorrelated` incident. Fix: **do not type a slash command at a
   TUI that does not implement it**, gated on a declared per-kind contract axis (S3).

2. **The misdiagnosis (independent, and general).** `AgentTaskReplyService.RecordUncorrelatedReportAsync`
   (`:163`) dedupes the incident **once per session**, and `FailNeverStartedAsync`'s second arm
   (`AgentTaskDispatcher.cs:480`) asks whether *any* such incident exists **for the session** — not
   whether it belongs to this task. A ten-minute-old incident from an already-settled task therefore
   answers a question about a different task, and the honest arm 1 ("the brief is still queued
   Pending") is never reached. This has nothing to do with compaction: the incident's *documented
   benign cause* is a human typing in a pool delegate's terminal, and one of those poisons every
   task that session ever runs afterwards, forever. Fix: one predicate for "an uncorrelated report
   about THIS task", used by the recorder and by both readers (S1).

**And a third thing the live miss also proves, which neither of the card's two questions names:**
`FailNeverStartedAsync` **killed a session that was visibly working** (seq 37 at 08:37:02, killed
08:38:32) on the strength of negative transcript evidence. That is exactly the pair CARD-0055
forbids. The delivery clock must decline to judge — not widen — when the brief is provably untyped
*and* the session is provably mid-turn, and hand the task to `TaskDeadlinePolicy`, which already
owns "started and running too long", already has three measured clocks, and already does not kill
(S4).

**Answer to the card's question (B) — is a brief timing out behind legitimate prior work
acceptable?** The **timeout** is acceptable and must not be widened. The **kill** is not, and the
**wording** is not. See D10/D11 for why every "extend the clock" variant was rejected.

---

## What the code does today, re-read on `9b5b1dc`

### The reuse path

`AgentTaskDispatcher.DeliverReuseMessagesAsync` (`:2178`): if the previous task on this session has
a different `RootTaskId` and this is not a `Check`, enqueue one line —

```
/compact This session is being handed NEW, unrelated work. Keep only context useful for: {first 300 chars of Goal}
```

— then `FitBriefForTyping` and the brief. Both via `TryEnqueueReuseAsync` (`:2227`) →
`SessionMessageQueueService.EnqueueAsync(..., MessageSendMode.WhenIdle, ..., QueuedMessageOrigin.Delegation)`.
No task marker on the compact. **The kind is looked up (`:2211`) only for `FitBriefForTyping`, after
the compact has already been enqueued** — the compact itself is kind-blind.

### The watchdog

`FailNeverStartedAsync` (`:399`), 10 minutes (`DelegationSettings.DeliveryFailTimeoutMinutes`):

```
CatchUpTranscriptAsync(sessionId)                                     // :425, CARD-0055
started = TranscriptPromptSpan.HasTurnPromptSinceAsync(dispatchedAt)  // :431
if (!started)      -> bind-refusal recovery, capability mismatch, else arm 1 (:456 loads brief status)
else if (any DelegateReportUncorrelated for this SESSION)  -> arm 2 (:480)
else continue
```

Arm 1 is the only arm that names the brief's queue status, and it computes it *inside* the
`!started` branch, so arm 2 can never see it. Both arms then `FailAsync` + `KillAsync` unless
`withholdKill`, which today only CARD-0112's capability mismatch sets.

### Why `started` was true on 9a518def

`TranscriptPromptSpan.IsHousekeepingPrompt` (`:88`) recognises Claude's wrappers, the compaction
continuation prompt, the raw local-command echo (only when a wrapper in the same span named the
command) and task notifications. A Codex rollout `UserMessage` whose text is literally
`/compact This session is being handed NEW…` matches none of them, so it is a real turn prompt and
`started` is true. Arm 1 unreachable; arm 2 fires on a stale incident; the reason blames a mangled
brief that was never typed.

### The two readers already disagree

`AttentionService` (`:322`) already scopes the same incident correctly:

```csharp
uncorrelated.Where(i => i.SessionId == task.AgentSessionId
    && (task.DispatchedAt is null || i.CreatedAt >= task.DispatchedAt))
```

`FailNeverStartedAsync` (`:480`) does not. **The dispatcher is the outlier**, and the two surfaces
can therefore report different things about the same task — the exact defect class
`TranscriptPromptSpan`, `AgentTaskLiveness` and `TaskDeadlinePolicy` were each factored out to
prevent.

But scoping the *read* is only half. The **recorder** dedupes once per session (`:171-174`), so once
one incident exists, a genuinely uncorrelated report on a *later* task is never written at all — and
both readers go blind together. Both halves are needed, or neither works.

---

## Design decisions

**D1 — Do not type a slash command at a kind that has not been measured to implement it as
housekeeping. Declare it, per kind, on `ProviderContract`.**
New axis `RefocusCompact: RefocusCompactContract(State, Reason, string? Command)`, following
`SubscriptionUsagePollContract` exactly: `Command` is the ONLY body this feature may type for this
kind and is null unless `State` is `Supported`. The contract's own stated discipline settles the
values — "nothing defaults to Supported", "`Unknown` behaves as `Unsupported` for enabling
machinery":

| Kind | State | Why |
|---|---|---|
| ClaudeCode | Supported, `/compact` | Records as `<command-name>` / `<local-command-stdout>` + raw echo, all already housekeeping to `IsHousekeepingPrompt`; a manual `CompactBoundary` is a turn END that flushes the queue (CARD-0041). Measured 106 s on the CARD-0077 miss. |
| Codex | **Unsupported**, null | Measured 2026-08-21, session 51ee57fc seq 19 and seq 30: recorded as a plain `UserMessage` and **answered as a work turn**. Codex compaction is automatic and separately marked (`compacted` + `event_msg/context_compacted`), so nothing is lost by not asking. |
| Grok | Unknown, null | Never probed. Grok auto-compacts (`compaction_checkpoint` / `auto_compact_completed`); a manual command has not been measured. |
| OpenCode | Unsupported, null | No structured transcript at all — an extra typed prompt could not be told from the brief afterwards. |
| Raw | Unsupported, null | Not a TUI with commands. |

**D2 — What a non-Supported kind gets instead: one line inside the brief, not a second prompt.**
`DelegationReportFormatter.BuildBrief` gains a refocus line right after the marker header ("This
session previously worked on UNRELATED work — ignore that context; everything you need is in this
brief."). It is marked, correlated, costs no extra turn, and cannot block anything. It goes in
`BuildBrief` **only**, never in `BuildBriefPointer`: the pointer deliberately keeps itself under one
transport chunk ("a pointer that grows past one 1024-byte chunk can lose its own head"), and the
spill file it points at is built from `BuildBrief`, so the line rides along for free.

**D3 — Rejected: keep sending `/compact` everywhere and mark it with an `[antiphon-refocus]`
sentinel that `IsHousekeepingPrompt` learns.** This was the obvious minimal fix and it is wrong. It
repairs the *reading* and leaves the *harm*: Codex would still burn a multi-minute turn, still
produce real file edits in a shared checkout under no task's correlation (the live miss's
`RunnerCapabilityIncidentService.cs` plus two test files), and still block the brief. Marking a bad
turn as housekeeping makes it invisible, which is worse than not causing it. Keep the option
documented behind the axis: if a future kind is measured to *honour* a typed `/compact` while
recording it as a plain prompt, a sentinel becomes the right answer for that kind — and the axis is
where that fact will already be written down.

**D4 — Rejected: keep sending it to Grok because no defect is measured there.** The contract's rule
is that `Unknown` behaves as `Unsupported` for enabling machinery, and typing a slash command a TUI
may not implement is precisely enabling machinery on unmeasured ground. The cost of not sending it
is a slightly larger inherited context on a Grok reuse; the cost of sending it is the Codex outcome.
A probe card can promote Grok to Supported on evidence.

**D5 — One predicate for "an uncorrelated report about THIS task", and the dispatcher filters in
memory so there is exactly one implementation.**
New `internal static class UncorrelatedReportEvidence` with a single rule:

```csharp
internal static bool IsEvidenceFor(AgentTask task, Guid? incidentSessionId, DateTime incidentCreatedAt)
    => incidentSessionId == task.AgentSessionId
       && (task.DispatchedAt is not DateTime d || incidentCreatedAt >= d);
```

Used by `RecordUncorrelatedReportAsync`'s dedup, by `FailNeverStartedAsync`'s arm 2, and by
`AttentionService`'s row 5 (which loses its inline copy of the same expression). The dispatcher
loads the session's `DelegateReportUncorrelated` rows and filters with this method rather than
writing a second, LINQ-shaped copy of the rule — retention caps incidents at 500 per agent and the
sweep only reaches this on a handful of T+10 suspects, so the cost is nil and the divergence risk is
zero. The alert's `DedupKey` (`delegation:uncorrelated:{task.Id}`) is already per-task; this brings
the incident row into line with the alert it fires.

**Note on the granularity this preserves.** The "once per session" comment defends against a
stranded delegate ending turn after turn and burying the first finding. That is still deduped: the
same task's repeated turn-ends all find the incident raised after *its* `DispatchedAt`. What changes
is only that a *different, later* task gets its own row — which is the finding, not the noise.

**D6 — Rejected: `AgentIncident.AgentTaskId` as a new column.** More honest on paper, and it buys
cross-session task queries this feature does not have. Rejected for this fix because it needs a
migration plus a backfill decision for existing rows, and because it would still not fix the case
that matters most: an incident raised by **this task's own** reuse compact is correctly attributed
and *still* yields the "mangled brief" lie while the brief sits `Pending`. D7 is what closes that,
and it closes the stale case too. Both clocks are the server's own `UtcNow()`, so the timestamp
window has no skew to worry about. Revisit the column if the incident ever needs to be queried
task-first.

**D7 — The brief's own queue row outranks the transcript when the watchdog picks its wording.**
Hoist the brief-status projection out of arm 1 and ask it first. A `Pending` brief is direct,
positive evidence about **this task's own** message; `started` is evidence about *some* prompt, and
the incident is evidence about *some* turn. So the arm choice becomes `if (!started || briefNeverTyped)`,
and arm 1 gains a second sentence for the new case: *the session has written prompts since dispatch,
but none of them was this task's brief — it is still queued Pending.* Without this, a session where
the task's **own** compact turn ended before T+10 would still take arm 2 even after S1.

While the projection is being moved, make its evidence string true: today `Pending` reads "so every
delivery attempt failed", and on the live miss `DeliveryAttempts` was **0** — the queue never tried,
because the session was mid-turn at every flush. Project `DeliveryAttempts` too and say which it
was: never attempted / N attempts failed / parked at `MaxDeliveryAttempts`.

**D8 — The delivery clock declines to judge a provably-untyped brief on a provably-working session.
It does not fail, it does not kill, and it does not extend itself.**
Condition, evaluated **after** the existing `CatchUpTranscriptAsync` pull: brief row `Pending`
**and** `SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct)` true. Action: `continue`, log
at Information, and surface it (S5). Every subsequent tick re-evaluates, so it resolves itself three
ways:

- the brief goes `Sent` → the normal arms judge it on real evidence;
- the session goes quiet with the brief still `Pending` → arm 1 fails it, with the kill, exactly as
  today;
- the session never stops → `FailOverdueTasksAsync` fails it. That sweep already covers `Dispatched`
  rows, already pulls before judging, and explicitly **does not kill** ("The session was NOT killed
  — read session {id} for what it was actually doing").

The bound is therefore not open-ended, and it is tight in the direction that matters. A *falsely*
"working" session (any of CLAUDE.md's stranded-working shapes) has an **old** last entry, so
`ModelWaitDeadlineMinutes` (20, measured at ~3× the observed maximum) breaches almost immediately. A
*genuinely* working session refreshes its last entry and is bounded by the role ceiling (240).

**Why this is not the thing CARD-0055 forbids.** CARD-0055's complaint is that the confirm and the
working guard "fail together because they share one dependency" — the stored transcript. This defer
is gated on a row from a **different subsystem**: `SessionQueuedMessages.Status`, written by the
queue's own delivery path. If the brief had in fact been typed and mangled, CARD-0055/CARD-0024 would
have stamped it `Sent` (or `Truncated`-parked), and the defer does not apply. The transcript is
consulted only for the *second* half of the condition, and only in the conservative direction
(working ⇒ wait longer; never working ⇒ act sooner).

**D9 — The kill is withheld while the session is working, on arm 2 as well.** Same CARD-0055 rule,
same reason: negative transcript evidence about a mid-turn session is not grounds to destroy the work
on screen. The task still Fails (arm 2's premise is that a turn ended and could not be attributed),
the session lives, and the failure reason already tells the operator to go read it. This inherits
CARD-0112's existing consequence — `RemoveEphemeralAgentAsync` deletes the pool agent row while the
session lives, leaving an unclaimed-but-visible session. That is the same shape CARD-0056 settled
("unclaimed never implies kill"), and it is strictly better than killing a working delegate.
Droppable independently of the rest of S4.

**D10 — Rejected: raise `DeliveryFailTimeoutMinutes`.** This repo's standing rule is that widening a
timeout to turn red green is how a real defect hides behind the word "flaky". The number is also the
wrong number to change: it is the **delivery** clock ("did work start"), and nothing about a brief
that cannot be typed for 10 minutes gets better at 20.

**D11 — Rejected: extend the clock while a marked own-reuse compact is legitimately running.** This
was the card's own suggestion for (B). It requires a marker on the compact — which S3 deletes for the
only kind that misbehaves — it is compact-specific when the real condition is "the composer is busy
with anything", and it is a *timer* change where D8 is an *evidence* change. D8 covers the compact
case as a special case of the general one, with no new setting.

**D12 — Out of scope: `IsWorkingAsync` is blind to Codex tool calls after a premature
`task_complete`.** `CodexTranscriptNormalizer` (`:246-249`) deliberately skips `CommandExecution` /
`FileChange` / `McpToolCall`, so if Codex ever wrote `task_complete` with tools still running, the
queue would type into a busy composer. The investigation established this is **not** what happened
here (the previous `TurnEnd` was 16 minutes old and the post-08:30 activity was a new `UserPrompt`).
It is a real hole with no measured occurrence; it wants its own card and its own measurement, not a
guess bundled into this fix.

**D13 — Out of scope: the duplicate compact (seq 30 and seq 32, ~6 s apart ≈ `ReEnterIntervalSeconds`).**
Recorded in the investigation as unproven. After S3 the reuse path types no compact into Codex at
all, so the shape cannot recur here; if it is a general CARD-0055 re-Enter defect it will show up on
a body that matters and should be chased there, with evidence.

---

## Slices

Each slice is independently testable and independently committable. S1 and S3 are independent of
each other and can land in either order.

### S1 — one predicate for "an uncorrelated report about THIS task"

- New `server/Application/Services/UncorrelatedReportEvidence.cs` (D5), with the rule and a doc
  comment naming the 2026-08-21 misattribution and the once-per-session dedup that caused it.
- `AgentTaskReplyService.RecordUncorrelatedReportAsync` (`:171-174`): dedup through it.
- `AgentTaskDispatcher.FailNeverStartedAsync` arm 2 (`:480`): load the session's rows, filter through
  it.
- `AttentionService` (`:322`): drop the inline copy, call it.

**Red before:** a stale incident from a settled task fails a later task with the mangled-brief
wording; a second task on a session that already has one never gets its own incident.

### S2 — the brief's own row outranks the transcript in the diagnosis

- Hoist the `SessionQueuedMessages` projection (`:456`) above the arm choice; project
  `DeliveryAttempts` alongside `Status`.
- Arm choice becomes `if (!started || briefNeverTyped)`; arm 1's reason gains the
  prompts-since-dispatch-but-not-ours sentence, and the evidence string distinguishes
  never-attempted / attempts-failed / parked (D7).

**Red before:** a task whose own compact turn ended, with the brief still `Pending`, reports "mangled
in delivery".

### S3 — a `/compact` only reaches a kind that implements it as housekeeping

- `RefocusCompactContract` on `ProviderContract`; five catalog entries with the D1 reasons.
- `DeliverReuseMessagesAsync`: move the session-kind lookup (`:2211`) **above** the compact block;
  send the compact only when `ProviderContractCatalog.For(kind).RefocusCompact` is `Supported`, and
  type only its declared `Command`. A missing session row (null kind) is not evidence and sends
  nothing; the brief's own rendering keeps its existing `?? AgentKind.ClaudeCode` default so no
  current rendering changes.
- `DelegationReportFormatter.BuildBrief` gains the refocus line behind a `bool refocus` argument,
  threaded from `DeliverReuseMessagesAsync` through `FitBriefForTyping` (D2). `BuildBriefPointer` is
  untouched.

**Red before:** a Codex pool reuse enqueues two messages, the first of which is a slash command.

### S4 — the delivery clock stops killing work it cannot read

- D8's defer arm in `FailNeverStartedAsync`, after the pull.
- D9's `withholdKill` when `IsWorkingAsync` is true, on the failing arms.

**Red before:** a working session with a `Pending` brief is failed and killed at T+10.

### S5 — the operator surface for a deferred brief (droppable)

New `AttentionKind.BriefUndelivered = 11`: dispatched, past the delivery grace, brief row still
`Pending`, session working. Computed from live state like its neighbours — **no new incident kind**.
Ordered below `NeverStarted` and above `UncorrelatedReport`. Client: one union member in
`client/src/api/attention.ts`, one entry in `attentionVisuals.ts`, and the kind list in
`attentionVisuals.test.ts`. Without it a deferred task is silent until `Overdue` previews the ceiling
at 80% (192 min).

### S6 — verification and card close-out

Dispatch one small Codex task onto a warm Codex pool delegate that last ran unrelated work, and
confirm from the stored records: exactly one queued message, its body carries the task marker and the
refocus line, the rollout contains no `/compact`, and the task settles normally. Then update
CARD-0117 with what shipped and open the follow-up cards named in D4 (probe Grok's `/compact`) and
D12 (a Codex mid-turn activity signal).

---

## Test coverage

| Slice | Test | Asserts |
|---|---|---|
| S1 | `AgentTaskDeliveryWatchdogTests` — new | An incident raised **before** this task's `DispatchedAt` does not take arm 2; the task fails with arm 1's wording instead. This is the live miss, in one test. |
| S1 | `AgentTaskDeliveryWatchdogTests` — existing `a_task_whose_report_could_never_be_correlated_fails_instead_of_hanging` | Re-anchored: its seeded incident must be stamped **after** `DispatchedAt`, and it must stay green — arm 2 is not being deleted. |
| S1 | `AgentTaskReplyIntegrationTests` — new pair | Two tasks on one session each get their own incident row; one task's repeated turn-ends still produce exactly one. |
| S1 | `AttentionServiceTests` | Unchanged behaviour through the shared predicate — the guard that S1 did not quietly change the view. |
| S2 | `AgentTaskDeliveryWatchdogTests` — new | Prompts exist since dispatch **and** the brief is `Pending` ⇒ arm 1, and the reason says those prompts were not this task's brief. |
| S2 | `AgentTaskDeliveryWatchdogTests` — new | `DeliveryAttempts == 0` reads "never attempted", not "every delivery attempt failed". |
| S3 | `AgentTaskPoolTests.unrelated_work_compacts_the_session_first_focused_on_the_new_task` | Re-anchored to an explicit ClaudeCode kind; stays green. |
| S3 | `AgentTaskPoolTests` — new Codex twin | Unrelated reuse onto a **Codex** delegate enqueues exactly ONE message; it carries the task marker and the refocus line; no body starts with `/`. |
| S3 | `AgentTaskReuseEnqueueTests` | CARD-0077's fault-isolation pair pinned to ClaudeCode so it is not silently vacuous, plus a Codex case where the single enqueue still raises its own incident on throw. |
| S3 | `ProviderContractCatalogTests` | `Every_axis_is_declared_with_a_reason` covers the new axis; a Codex-specific case that `RefocusCompact` is `Unsupported` with the measured reason; a lockstep case that a non-`Supported` kind has a null `Command` and that `DeliverReuseMessagesAsync` types nothing for it. |
| S3 | `DelegationUnitTests` / formatter tests | The refocus line renders in `BuildBrief`, is absent from `BuildBriefPointer`, and reaches the spill file. |
| S3 | `CodexDelegateDispatchTests` | The Codex reuse shape end to end. |
| S4 | `AgentTaskDeliveryWatchdogTests` — new | Working + `Pending` brief ⇒ neither failed nor killed; the same task with the session idle ⇒ failed and killed. |
| S4 | `AgentTaskDeliveryWatchdogTests` — new | `Sent` brief + working ⇒ **not** deferred: the defer is gated on the queue row, not on working alone. |
| S4 | `AgentTaskOverdueDeadlineTests` | A deferred task still fails on `ModelWaitDeadlineMinutes` — the bound D8 leans on, proved rather than asserted in prose. |
| S5 | `AttentionServiceTests`, `attentionVisuals.test.ts`, `AttentionPanel.test.tsx` | The new row appears for the deferred shape and nowhere else. |

Server slices:
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0117/ --treenode-filter "/*/Antiphon.Tests.Application/*/*"`.
S5's client half: `pwsh -File scripts/test-client.ps1`. Delete the ~12 `bin-0117/` directories
afterwards.

---

## What was NOT determined

- **Whether Codex 0.147.0 implements `/compact` at all.** The catalog entry asserts only what was
  measured — that `/compact <focus>` is recorded as a user message and answered as work. It does not
  claim the command is absent, and must not be read that way.
- **Grok's `/compact`.** Unmeasured in either direction; D4 makes that an explicit `Unknown`, not a
  guess, and S6 opens a probe card.
- **The 51 s gap between 9a518def's `DispatchedAt` and the brief's `CreatedAt`.** The investigation's
  hypothesis (the compact's `EnqueueAsync` holding the per-session lock through an evidence/confirm
  window) is untraced. After S3 there is no compact on that path to hold the lock, so it should stop
  occurring; if it does not, it is a separate queue-latency question.
- **Whether 9a518def's compact turn ever wrote a `task_complete` that was never ingested.** Stored
  transcript ends at seq 37. Irrelevant to every fix here.

---

## Environment / cleanup

Plan only — no code, no migration, no schema change, and nothing left running. S5 is the only slice
that touches the client. Nothing in this plan changes `DeliveryFailTimeoutMinutes`,
`ModelWaitDeadlineMinutes`, `LocalExecutionDeadlineMinutes`, `MaxDeliveryAttempts`,
`ReEnterIntervalSeconds` or any other delivery/kill timing constant.
