# CARD-0153 — a session that reads "working" and makes no progress is invisible: plan

**Date:** 2026-08-23 · **Card:** CARD-0153 (`0cdef031-31a9-428d-bd8e-46a5793b3549`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `a7db23c`. Every line number below was re-read out of the code on that
commit.

**Established fact, not re-derived here:** the Investigate stage (task `8f1ee70d`, Grok, read-only;
findings recorded on the card 2026-08-23). Its two non-bugs are taken as given and **not touched**:
`tokensIn`/`tokensOut`/`costUsd` on an open task are settlement-only by design, and `-Refine` already
rides the full CARD-0055 delivery path. Its three gaps are taken as ground truth and are what this
plan designs for. Nothing here re-investigates.

**Related:** CARD-0020 (`TaskDeadlinePolicy` — the clocks this plan deliberately does NOT widen),
CARD-0055 / CARD-0056 (the near-misses that set this repo's rule: no kill on incomplete evidence;
pull before you judge), CARD-0082 (idle auto-compact — gap 2), CARD-0083 (provider contract catalog,
`ContextWindowUsage` axis — gap 1), CARD-0035 (the attention view this adds a row to), CARD-0135 /
CARD-0117 (same "a clock measures the wrong thing" genre), CARD-0136 (subscription quota — a
different axis, stays separate), CARD-0137 (the task that sat wedged and whose WIP was recovered by
hand — the procedure in S4 is that improvisation written down).

---

## Verdict up front

1. **Gap 3 is the whole card. Build detection, not intervention.** The live miss was not that a
   session was stuck — sessions get stuck — it was that **nothing said so** for 30+ minutes. Every
   existing clock either requires `working=false` or resets on *any* transcript row, and the stuck
   shape is specifically *"working, rows still landing, nothing novel in them."* The fix is a new
   progress signal that a loop cannot satisfy, a new incident kind (**`TaskProgressStalled = 32`**,
   Warning, role-agnostic, deduped per stall episode), and a new attention row. **No auto-kill, no
   auto-escalate, no auto-compact, no auto-retype.** Five of the last twelve reliability cards in this
   repo are about a kill or a retype fired on evidence that turned out to be wrong (CARD-0055,
   CARD-0056, CARD-0117, CARD-0135, CARD-0149); a stall detector's evidence is *weaker* than any of
   those — "nothing new for a while" is exactly what a slow, legitimate task also looks like from the
   outside — so it does not get a trigger finger. It gets a row the operator can act on, and a
   scripted, no-kill **checkpoint** step so that acting on it cannot lose work.

2. **Gap 1: suppress now, fix properly under its own card.** Option (b), with one piece of (a) that
   is free. The number is wrong for Grok on *three* independent axes and the worst of them — the
   numerator is cumulative session usage, not window occupancy — is not a formula bug, it is a
   **missing measurement**: Grok's `turn_completed.usage` does not carry what a fullness number
   needs, and what does (the `modelState` payload, the `compaction_checkpoint` rows) is not ingested.
   Making it correct means a measurement probe against a live Grok session first, then ingest work in
   the normalizer and the catalog, then the arithmetic. That is a CARD of its own, not a slice of this
   one. What ships here: the fullness loader learns the session's kind, consults the kind's
   `ContextWindowUsage` contract, and returns **null** (the existing "unknown" value every consumer
   already renders as "no badge") for a kind whose ceiling source is `SelfReported` and not wired.
   The catalog reason string for Grok already says this in prose
   (`ProviderContractCatalog.cs:112-115`); the code just stops contradicting it.

3. **Gap 2 needs no implementation. Gap 3 is its fix.** Idle auto-compact is a maintenance sweep
   gated on `working=false` for a reason that still holds (compacting a mid-turn session types into
   a working composer — the CARD-0041 (auto) boundary rule exists because ending a turn mid-turn is
   wrong). A "working-aware stall-triggered compact" would be an automatic intervention on stall
   evidence, which verdict 1 rules out, *and* it would be Claude-only today (Grok declares no
   `/compact`). The card's observation that the wedged session was past its context is, per gap 1,
   not even established — the 215% was a wrong number. What gap 2 actually needs is for the operator
   to be **told** the session is wedged so they can decide whether to `/compact`, checkpoint, or
   kill — which is gap 3. One doc-comment change to `ContextCompactionService` to name this
   explicitly; nothing else.

**One sentence:** add a progress signal that repeated rows cannot fake, raise a deduped Warning
incident and an attention row on it, ship a no-kill checkpoint script for the recovery the operator
already improvised, null out Grok's fullness badge until its inputs are measured under a follow-up
card, and leave auto-compact alone.

---

## What the code does today, re-read on `a7db23c`

### Every clock that could have caught it, and why each one did not

| Clock | Where | Gate that excluded the wedged session |
|---|---|---|
| Auto-escalate stalled | `AgentTaskDispatcher.AutoEscalateStalledAsync` `:321-381` | Role must have `EscalateTo` + `EscalateAfterMinutes`; only `Debug` ships both (`DelegationSettings.cs:519-548`). Clock = `Max(CreatedAt)` over **all** rows (`:348-354`) — a looping row resets it. |
| Delivery watchdog | `FailNeverStartedAsync` (`:159`) | Asks only "did a prompt land after `DispatchedAt`" — yes, hours ago. |
| Dead-session reconciler | `FailDeadSessionTasksAsync` (`:165`) | Session was alive. |
| Overdue deadline | `FailOverdueTasksAsync` `:878` → `TaskDeadlinePolicy.EvaluateAsync` `:113-175` | Ceiling is 240 min wall-clock — the task was ~3 h in. Phase arm is `min(last-entry age, elapsed)` (`:150, :162`) against 20/90 min and **the last entry's age resets on every row** (`LoadLastEntryAsync` `:205`). A loop emitting a row every few minutes never ages past 20 min. |
| PastExpectedIdle | `AttentionService` `:399` | Declines the mid-turn case by construction. |
| Idle auto-compact | `ContextCompactionService` | Requires `working=false` **and** 8 h idle before reading fullness (gap 2). |
| Queue stranded-flush / CPU watchdog | runner side | Both fire on a session that has gone *idle*. |

The partition `TaskDeadlinePolicy`'s class comment describes — ceiling for everything, phase
deadline only while mid-turn — is correct and is **not** the hole. Both of its clocks measure *time
since the last row*. The hole is that **"a row landed" is being read as "progress was made"**, and
for a session spinning on near-identical tool calls those are different facts.

### What the new signal has to work with

`TranscriptEntry` (`server/Domain/Entities/TranscriptEntry.cs:33-39`) already stores `ToolName`,
`ToolInput` and `ToolUseId` per `ToolCall` row, and `Text` per `AssistantText`/`Thinking`/`ToolResult`
row. That is enough to fingerprint a row without touching the normalizers or the runner.

The workspace side: `AgentFilesService.GetFilesAsync(agentId, since, ct)` (`:96`) already
enumerates changed files in the agent's `WorkingDirectory` against a baseline, and
`GetCommitsAsync` (`:182`) lists recent commits with the checkpoint commit flagged. Gap 3's
file-activity arm reads through the same service rather than a second git walker.

### Where incidents and attention rows come from

- Incident with owner: `AgentSupervisorService.RecordIncidentAsync(agentId, sessionId, kind,
  severity, message, failureReason:, ct:)` — the shape `SubscriptionUsageMonitorService.cs:312-321`
  uses, with an in-process `_incidentRaised` dedup set (`:304`). Direct `_db.AgentIncidents.Add`
  is the other shape (`AgentTaskDispatcher.cs:2346`). This plan uses the former through a
  **DB-backed** dedup (S2), not an in-memory set — a server restart must not re-raise every open
  stall.
- `AgentIncidentKind` ends at `ForbiddenTerminalBody = 31` (`AgentIncidentKind.cs:299`). Stored
  as an int on the existing column, so a new value is **no migration** (same note as 31).
- `AttentionKind` ends at `BriefUndelivered = 11` (`AttentionDtos.cs:89`); rows are computed live
  in `AttentionService` in a first-match order (`:336-450`), Overdue at step 8 (`:425`).

### Gap 1's three inputs

```csharp
// server/Application/Services/SessionContextUsage.cs:243-247
private static long TokensOf(TranscriptContextRow row) =>
    (long)(row.InputTokens ?? 0) + (row.CacheReadTokens ?? 0)
    + (row.CacheCreationTokens ?? 0) + (row.OutputTokens ?? 0);
```

- Ceiling: `settings.ResolveCeiling(modelId)` (`:75`) → `ContextWindowSettings.DefaultContextTokens`
  = 200 000 (`ContextWindowSettings.cs:11`) unless a `ModelOverrides` substring matches. Nothing
  for Grok matches.
- Numerator: the latest usage-bearing row (`:61-64`), whose tokens for Grok are copied from
  `turn_completed.usage` (`GrokTranscriptNormalizer.cs:205-221`) — cumulative per the investigation's
  fixture (18.7M after 103 calls).
- Cache: `TokensOf` adds `CacheReadTokens` on top — right for Claude, double for Grok.
- Reset: Grok's `compaction_checkpoint`/`auto_compact_completed` are skipped
  (`GrokTranscriptNormalizer.cs:32-37`), so `IsInvalidator`/`IsAutoCompactBoundary` never see one.

`LoadFullnessAsync` (`:104-159`) takes `(SessionId, EffectiveModelId)` tuples. Its four callers
(`AgentService.cs:205`, `BoardService.cs:69`, `CardService.cs:139`, `CardThreadService.cs:127`) all
have the session row in hand, and `AgentSession.AgentKind` exists (`AgentSession.cs:16`). The
contract axis is `ProviderContractCatalog.For(kind).ContextWindowUsage` with
`State = Degraded, CeilingSource = SelfReported` for Grok (`ProviderContractCatalog.cs:112-115`).
`ContextCompactionService.cs:266` already treats a null `Fullness` as "cannot evaluate" — so nulling
Grok's number does not change the sweep's behaviour for Grok (it never compacted Grok anyway:
no `/compact`).

---

## Design

### D1 — the progress signal (gap 3)

**Definition.** A transcript row is *progress-bearing* when its fingerprint has not been seen in
the session within the look-back window. Fingerprint:

| Kind | Fingerprint input |
|---|---|
| `ToolCall` | `ToolName` + normalized `ToolInput` (whitespace-collapsed, first 2 000 chars) |
| `ToolResult` | normalized `Text`, first 2 000 chars |
| `AssistantText` | normalized `Text`, first 500 chars |
| `Thinking` | **excluded** — thinking is not progress and a spinning model re-thinks the same thing in fresh words |
| `UserPrompt`, `QueuedUserPrompt` | always progress-bearing (somebody steered the session) |
| `TurnEnd`, boundaries, titles | excluded (housekeeping; a turn end makes the session idle and out of scope anyway) |

SHA-256 of the normalized input, hex; computed in memory from rows already loaded. **No new
column** — the detector loads the window's rows (`Sequence`, `Kind`, `ToolName`, `ToolInput`,
`Text` truncated in the projection, `Timestamp`, `CreatedAt`) and fingerprints them in the sweep.
A persisted fingerprint column is a later optimisation if the query ever shows up; at one open task
per few minutes it will not.

**Stall verdict** for an open task (`Dispatched`/`Working`, `AgentSessionId != null`,
`DispatchedAt != null`), evaluated by a new read-only `TaskProgressPolicy.EvaluateAsync` next to
`TaskDeadlinePolicy` (same shape: returns a `Verdict?`, writes nothing, both consumers share it):

1. `working = SessionMessageQueueService.IsWorkingAsync(db, sessionId)` — the shared verdict, the
   only one allowed (fourth implementation = defect). `working=false` ⇒ **null**; idle is
   `PastExpectedIdle`'s business.
2. Load rows with `At >= now - LookBackMinutes` (default **45**), `At` resolved the same way
   `TaskDeadlinePolicy.LoadLastEntryAsync` does (`Timestamp ?? CreatedAt`), capped below by
   `DispatchedAt` (a warm-pool session's inherited tail is not this task's).
3. Fewer than `MinRowsInWindow` (default **6**) rows ⇒ **null**. A quiet session is a *slow* session
   and belongs to the phase deadline (a ToolCall with no successor row for 90 min already fails
   there; a model wait with none for 20 min likewise). This is the partition: **the phase deadline
   owns "nothing landed"; this detector owns "things keep landing and none of them is new."**
4. `lastProgressAt` = the latest `At` among progress-bearing rows in the window, where a row is
   progress-bearing iff its fingerprint does not appear in any *earlier* row of the window (first
   occurrence counts, repeats do not).
5. **File arm:** `lastFileChangeAt` = the newest mtime among files `AgentFilesService` reports changed
   in the task's `WorkingDirectory` since `DispatchedAt`, and the newest commit time from
   `GetCommitsAsync` newer than `DispatchedAt`. Either one newer than `lastProgressAt` replaces it.
   Rationale: the loop that lands rows but no *files* is the exact 2026-08-23 shape; conversely a
   session whose rows look repetitive but whose worktree keeps changing (re-running one test command
   while the *tool under test* edits files) is working. A task with no `WorkingDirectory` or a
   non-git directory skips the arm (not a failure; the transcript arm stands alone).
6. `stalledFor = now - max(lastProgressAt, DispatchedAt)`. `stalledFor >= StallMinutes`
   (default **30**) ⇒ a Verdict with `Summary` naming the row count in the window, the distinct
   fingerprint count, the last novel row's kind and age, and the file arm's result
   ("no file changed since 11:02Z; last commit 09:41Z").

**Why these defaults.** 30 min is the number the card measured as "sat unnoticed"; the operator
noticed at 30+ and said that was too long. 45-min look-back > 30-min stall so the window always
contains the last novel row when the verdict is borderline. `MinRowsInWindow = 6` means "at least
one row every ~7 min" — a session that spins faster than that is the loop; slower is a slow tool and
the phase clock's problem. All three are `DelegationSettings` knobs (`StallDetection` sub-object:
`Enabled`, `StallMinutes`, `LookBackMinutes`, `MinRowsInWindow`, `EscalateToErrorAfterMinutes`).

**Role-agnostic.** `AutoEscalateStalledAsync` is Debug-only because it *acts* (re-runs on a bigger
model; CARD-0020 S4's comment explains why that was not widened "to get health coverage"). This
detector does not act, so the reason not to widen does not apply; a Plan or Code task wedged for 30
minutes is exactly as wedged as a Debug one. Both clocks stay independent: escalation keeps its
own `Max(CreatedAt)` clock (widening it to the fingerprint clock would make escalation *more* eager
on loops, which is an intervention change this card does not make — noted under "Cards to file").

**What is deliberately not a trigger.** Context fullness. For Claude it is a real number and
`> 1.0` is possible and logged (`SessionContextUsage.cs:78-84`), but it is a *cause* hypothesis, not
a stall *observation*, and for Grok it is wrong (gap 1). The stall message **includes** the session's
fullness where one is known ("context 97% of 200k") as a hint to the operator; it never gates.

### D2 — the incident and the attention row (gap 3)

- **`AgentIncidentKind.TaskProgressStalled = 32`.** Doc comment in the enum's house style: what it
  means, that it is detection-only, that it never kills, the dedup rule, "no migration".
- **Severity:** `Warning` at `StallMinutes`; **re-raised as `Error`** once `stalledFor >=
  EscalateToErrorAfterMinutes` (default **90**, = the local-execution deadline, so a task that is
  both wedged and about to be failed by the ceiling has an Error row before the Failed row).
  `Critical` only when the owning agent is **channel-bound** (the CARD-0067/CARD-0055 rule: a human
  is waiting on a dead line), and only at the Error step. Never Critical for an ordinary delegate —
  a wedged build task is a task, not an outage.
- **Dedup per stall episode, DB-backed.** Before raising, query
  `AgentIncidents` for the newest `TaskProgressStalled` row on this `SessionId`; if it exists and
  its `CreatedAt` is **after** `lastProgressAt` (i.e. no novel progress since it was raised), it is
  the same episode — skip, unless the severity step has changed (Warning → Error), in which case
  raise once more at the new severity. If `lastProgressAt` is after the incident, progress resumed
  and then stalled again — a new episode, raise again. This survives restarts (no `_incidentRaised`
  set) and cannot write 12 rows an hour.
- **`FailureReason`** carries the machine-readable summary (`rows=14 distinct=2 lastNovel=ToolCall
  age=38m files=none commits=none`) so a future reader can tell loop-shape from quiet-shape.
- **Sweep host:** a ninth clock in `AgentTaskDispatcher.TickAsync` (`:145-180`),
  `RunSweepAsync("progress stall", DetectStalledProgressAsync, ct)`, placed **after** the overdue
  deadline (a task the deadline is about to fail does not need a stall row on top). The 5 s tick is
  cheap because of the same gate `TaskDeadlinePolicy` uses: a task younger than `StallMinutes` is
  skipped with no query (`:128-133` pattern). `RunSweepAsync` already isolates a throwing sweep and
  counts it in `TickResult.SweepFailures`; the class comment's "eight clocks" becomes nine.
- **Pull before you judge.** The sweep calls `AgentSessionRuntime.CatchUpTranscriptAsync` for a task
  it is *about to raise on* (not for every task, every tick) — the CARD-0055 rule: the live stream is
  not a clock, and six rows landed in one burst on `e809ce65` exactly when the verdict was being
  read. A pull that yields novel rows withholds the incident.
- **`AttentionKind.ProgressStalled = 12`**, computed live from the same `TaskProgressPolicy` verdict
  (no reading of the incident table — the row exists because the condition holds now, not because
  it held once). First-match position: **after `PastExpectedIdle` (step 7) and before `Overdue`
  (step 8)** — a working session with a stall verdict gets the more explanatory row; one without
  falls through to Overdue as today. Actions: `Reply` (steer it), `Cancel`, plus the existing
  `OpenSession`. Headline is the verdict's `Summary`.
- **Client:** the attention rail renders kind 12 with the existing row component (the DTO carries
  kind + headline + actions; nothing kind-specific is needed beyond a label and an icon). No new
  endpoint.

### D3 — what happens to `-Refine` on a wedged session (the card's third bullet)

Nothing new. The investigation established `-Refine` rides CARD-0055's path and behaves exactly as
designed on a session that never goes idle: the message sits Pending (WhenIdle), attempts stay at 1,
and it parks only after three *attempted* deliveries. The card asked whether an unconfirmed
refinement should "escalate"; the answer is that the **session** escalates (D2's row names the
session, and the existing `ParkedMessage`/`BriefUndelivered` rows cover the queue side). Typing a
second "continue" into a wedged composer is CARD-0055's double-send. The stall row's headline
**mentions** a Pending Delegation-origin message when one exists ("1 refinement waiting"), so the
operator sees both facts in one line.

### D4 — recovery: checkpoint, then decide (gap 3, the procedure)

What the operator did by hand on 2026-08-23, written down and half-scripted:

1. **Checkpoint** — in the task's worktree: `git add -A`, commit as
   `wip(checkpoint): CARD-xxxx task <short-id> — stalled <N>m, <M> files, not verified`, push the
   branch. **This never kills anything** and is safe to run on a session that turns out to be
   healthy — a WIP commit on a delegate's branch costs nothing and the delegate's own next commit
   sits on top of it.
2. **Decide** — open the session, read the screen. Healthy-but-slow: dismiss (the attention row
   clears on its own when a novel row lands). Wedged: `delegate.ps1 -Cancel` (existing) or kill
   via the session UI.
3. **Re-dispatch** with the brief naming the checkpoint commit so the fresh delegate starts from it
   rather than from zero.

**Ship step 1 as `scripts/checkpoint-task.ps1 -TaskId <id> [-Push] [-DryRun]`.** It resolves the
task's `WorkingDirectory` from the API (`GET /api/agent-tasks/{id}`), refuses a directory that is
not a git repo or is the **shared** checkout of another active worker (same guard `delegate.ps1`'s
`-Worktree` decision relies on — a checkpoint must not sweep up a colleague's edits), commits
+ optionally pushes, prints the SHA and the suggested re-dispatch line. Steps 2–3 stay manual: they
are the judgement call and the kill, which verdict 1 keeps in human hands. Also add a "Stalled
task recovery" entry to the Gotchas in `CLAUDE.md` pointing at the script — short, the way the
CARD-0145 entry is.

Not built: an API "checkpoint" verb or an auto-checkpoint on stall. Committing into a worktree a
live agent is editing, from a server process, at an arbitrary moment, is a new class of race the
detector has no need to introduce. The script runs when a human chooses to run it.

### D5 — gap 1 scope: suppress for Grok, file the fix

`LoadFullnessAsync`'s tuple becomes `(SessionId, EffectiveModelId, AgentKind)`; `Compute` gains a
`ContextWindowUsageContract contract` argument (resolved by the caller from
`ProviderContractCatalog.For(kind)`). Rule, in `Compute`, before any arithmetic:

> If `contract.State == Degraded && contract.CeilingSource == SelfReported` ⇒ return
> `SessionContextUsageResult(Fullness: null, TokensUsed: <still computed>, Ceiling: <resolved>,
> Model)`, with one Debug log line naming the kind and the reason string.

`TokensUsed` stays populated — it is labelled as a raw sum and is still true (it *is* what the
provider reported); only the ratio is withheld. `Fullness == null` already renders as no badge in
every client consumer (`SessionTabs.tsx`, `AgentRail.tsx`, `AgentsPage.tsx`, `AgentCliModal.tsx`,
`AgentFilesPage.tsx`, `boards.ts` — all read an optional number) and `ContextCompactionService.cs:266`
already skips a null. The four callers pass the kind they already have. No client change. No
migration.

Why not a `ModelOverrides["grok"] = 500_000` entry instead (the cheapest-looking fix): it corrects
one of three wrong inputs and produces a *smaller* wrong number — 18.7M/500k = 37×, "3 740%" — which
is worse than a null because it looks like it might mean something.

Why not (a) now: see verdict 2. The follow-up card's brief is written under "Cards to file" so the
measurement it needs is specified, not re-discovered.

### D6 — gap 2: acknowledged, not built

One change: `ContextCompactionService`'s class comment gains a paragraph stating that the sweep is
idle-time maintenance by design, that a working session is out of scope **even at >100%**, and that
a wedged working session is `TaskProgressStalled`'s business (CARD-0153). The `IdleMinutes` and
`working=false` gates do not move. The card's "why didn't auto-compact trigger" is answered: it was
never going to, and should not have.

### D7 — what does not move

- `TaskDeadlinePolicy` and both of its clocks. Not widened, not re-keyed. The stall detector sits
  beside it, partitioned by `MinRowsInWindow`.
- `AutoEscalateStalledAsync`'s scope and clock.
- `IsWorkingAsync` and its two lockstep mirrors — read, never re-implemented.
- Every kill path. This card adds zero kills.
- `SessionContextUsage`'s `> 1.0` unclamped return (pinned by `SessionContextUsageTests`) — Claude's
  number is right and stays.

---

## Slices

| Slice | Content | Depends on |
|---|---|---|
| **S1** | `TaskProgressPolicy` (fingerprint + verdict, transcript arm only), `DelegationSettings.StallDetection`, `AgentIncidentKind.TaskProgressStalled = 32`, `DetectStalledProgressAsync` sweep with DB dedup + pull-before-raise | — |
| **S2** | File arm: `TaskProgressPolicy` consults `AgentFilesService` (changed files + commits since dispatch) | S1 |
| **S3** | `AttentionKind.ProgressStalled = 12` + `AttentionService` step; client label/icon | S1 |
| **S4** | `scripts/checkpoint-task.ps1` + CLAUDE.md Gotcha entry | — |
| **S5** | Gap 1 suppression (`LoadFullnessAsync` kind + contract gate) | — |
| **S6** | Gap 2 doc-comment; file the two follow-up cards | — |

S1, S4, S5, S6 are independent and can land in any order; S2/S3 follow S1. Each slice is one
commit with its tests; the build agent should commit S1 before starting S2 so a half-built file arm
never blocks the detector.

---

## How we verify this and will not regress it

All `Antiphon.Tests` (TUnit; `dotnet run --project tests/Antiphon.Tests
--property:OutputPath=bin-stall/ --treenode-filter "/*/Antiphon.Tests.Application/*/*"`). Integration
tests use `TestDbFixture` and **scope every assertion to the rows the test made** (the shared-Postgres
rule) — in particular the dedup query must be asserted per `SessionId`, never as a global count.

### S1 — `TaskProgressPolicyTests` (unit, in-memory rows) and `TaskProgressStallSweepTests` (integration)

Pins, each one a named test:

1. **The loop shape is a stall.** 14 rows in 40 min alternating `ToolCall(Read, same path)` /
   `ToolResult(same text)` / `Thinking` → verdict, `distinct == 2`, `stalledFor` ≥ 30 m. This is the
   2026-08-23 shape and is the test that would have been red that morning.
2. **A slow single tool call is NOT a stall.** One `ToolCall` 35 min ago, nothing after →
   **null** (`MinRowsInWindow` not met). Companion assertion: `TaskDeadlinePolicy.EvaluateAsync` on
   the same rows returns the `LocalExecution` verdict — the partition is pinned from both sides.
3. **Distinct tool calls are progress however repetitive the tool is.** 14 `ToolCall(Edit, …)` rows
   each with a different `ToolInput` → null.
4. **Thinking never counts as progress.** 20 `Thinking` rows with different text, nothing else →
   stall (they are all non-progress-bearing; row count satisfies the gate).
5. **A user prompt resets the clock.** Loop shape, then a `UserPrompt` 5 min ago → null.
6. **Idle is out of scope.** Loop shape followed by `TurnEnd` → null (`IsWorkingAsync` false), and
   `IsWorkingAsync` is the *shared* one — assert via a session where the last row is a
   `<local-command-stdout>` record, which a naive "last kind" rule would mis-read.
7. **Warm-pool tail is not this task's.** Loop rows *before* `DispatchedAt`, nothing after → null.
8. **Timestamp tie-break.** Rows whose `Sequence` order runs backwards in time (the 0.27% backfill
   shape) — the verdict uses `Timestamp ?? CreatedAt`, matching `LoadLastEntryAsync`.
9. **Dedup, one episode.** Sweep twice on the same stall → one `TaskProgressStalled` row for that
   session. Three ticks past `EscalateToErrorAfterMinutes` → exactly two rows (Warning, then Error).
10. **Dedup, two episodes.** Stall → novel row → stall again → two Warning rows.
11. **Dedup survives a restart.** Construct a fresh sweep instance over a DB that already holds the
    Warning row → no second Warning.
12. **Pull before raise.** Fake `CatchUpTranscriptAsync` that lands a novel `ToolCall` when called →
    no incident; the sweep's return count is 0 and the log says "withheld: novel rows on pull".
13. **Channel-bound is Critical at the Error step only.** Owner with a bound channel: first raise
    Warning, second raise Critical; unbound owner: Warning then Error.
14. **Nothing is killed.** The session status, the agent status and the task status are unchanged
    after ten ticks on a stalled task — asserted explicitly, in the test name
    (`A_stalled_task_is_never_killed_escalated_or_failed_by_the_stall_sweep`). This is the test
    that guards verdict 1 against a later "helpful" change.
15. **Disabled means silent.** `StallDetection.Enabled = false` → no rows and the sweep returns 0
    without loading a task (assert through a `DbContext` whose `TranscriptEntries` query would throw
    if enumerated — a fake provider, the same way a "no query on the hot path" claim is cheapest to
    pin; the channel-bound lookup reuses `ApiErrorRecoveryService.IsChannelBoundAsync`'s shape).
16. **`TickResult` counts the ninth clock.** A sweep that throws increments `SweepFailures` and the
    other eight still run.

### S2 — file arm (`TaskProgressPolicyFileArmTests`, integration with a temp git repo)

17. Loop-shape transcript **plus** a file written in the worktree 3 min ago → null.
18. Loop-shape transcript plus a commit 3 min ago → null.
19. Loop-shape transcript, file changed 50 min ago (before the look-back) → stall; `Summary` names
    the file age.
20. No `WorkingDirectory` / non-git directory → transcript verdict stands, `Summary` says "no
    workspace arm".
21. A **shared** checkout (not a worktree) still consults the arm but the summary flags it as
    shared — the arm can only ever *withhold* an incident, so a colleague's edits make the detector
    quieter, never louder. Pinned so the direction is explicit.

### S3 — attention (`AttentionServiceTests` additions)

22. A working, stalled task → one `ProgressStalled` row, kind 12, headline == verdict summary,
    actions `Reply`/`Cancel`/`OpenSession`.
23. First-match order: a task that is both stalled and at 85% of the ceiling gets `ProgressStalled`,
    not `Overdue`; the same task idle at the prompt gets `PastExpectedIdle`.
24. With a Pending Delegation-origin message → headline carries "1 refinement waiting".
25. Client: vitest for the rail rendering kind 12 with a label (run via
    `pwsh -File scripts/test-client.ps1`).

### S4 — checkpoint script (`tests/Antiphon.Tests/Scripts/CheckpointTaskScriptTests` — pwsh, temp repo)

26. Dirty worktree → one commit with the `wip(checkpoint): … not verified` subject, tree clean
    after, SHA printed.
27. Clean worktree → exits 0, "nothing to checkpoint", no empty commit.
28. Non-git directory → exits 2 with the refusal message, nothing written.
29. `-DryRun` → lists the files, no commit.
30. Refuses the shared-checkout-with-another-active-worker case (stub the API to return a second
    open task on the same `WorkingDirectory`) → exit 3.

### S5 — gap 1 (`SessionContextUsageTests` additions)

31. `Compute` with a Grok contract (`Degraded`/`SelfReported`) over the investigation's 18.7M-token
    fixture shape → `Fullness == null`, `TokensUsed` populated, `Ceiling` resolved.
32. `Compute` with the Claude contract over the same rows → the existing `> 1.0` unclamped value
    (the existing pin stays green; this test asserts the gate is *kind*-keyed, not value-keyed).
33. A Codex contract (whatever its axis says today) → unchanged from before the change — pinned by
    snapshotting the result for each `AgentKind` so a catalog edit that flips a kind's axis shows up
    here, not in a user's badge.
34. `LoadFullnessAsync` end-to-end with two sessions, one Grok one Claude, same rows → null / number.
35. `ContextCompactionSweepTests`: a Grok session at huge raw tokens and 8 h idle → not compacted,
    no `AutoCompactFailed`, one Debug log — i.e. the sweep's Grok behaviour is unchanged by the null.

### S6

36. A doc-comment change has no test; the two follow-up cards are the deliverable (IDs recorded in
    the commit message).

### What must stay green (the regression set)

`TaskDeadlinePolicyTests`, `AgentTaskOverdueDeadlineTests`, `AttentionServiceTests`,
`SessionContextUsageTests`, `ContextCompactionSweepTests`, `ContextCompactionAgentTests`,
`SessionMessageQueueDeliveryVerificationTests` (nothing here touches delivery, and that is the
point), `SessionReconciliationServiceTests` (no new kill arm). Run these targeted after each slice;
run the `Antiphon.Tests.Application` namespace chunk once at the end.

---

## Cards to file (S6)

1. **Grok context fullness: measure, ingest, compute.** Brief: (i) probe a live Grok 1.0.5 session
   and record what `initialize`'s `modelState` carries and whether `turn_completed.usage.inputTokens`
   is per-call or cumulative (the investigation's fixture says cumulative — confirm on a second
   session and on a session that has compacted); (ii) ingest `modelState` → a per-session ceiling
   (`ContextWindowCeilingSource.SelfReported` becomes *wired*); (iii) ingest
   `compaction_checkpoint`/`auto_compact_completed` as `CompactBoundary` rows so `IsInvalidator`
   resets the number; (iv) a kind-aware `TokensOf` (no cache add for Grok); (v) flip the contract to
   `Supported` and delete D5's gate for Grok — test 33 goes red at that moment by design. Until (i)
   is answered, (ii)–(iv) cannot be specified.
2. **Should `AutoEscalateStalledAsync` use the progress fingerprint clock?** Today it resets on any
   row; on a looping Debug task it will never fire. Re-keying it to `TaskProgressPolicy` is a
   behaviour change to an *intervention* path and wants its own evidence (zero escalations have ever
   fired, per the investigation — is that because nothing stalls, or because the clock cannot see
   the stalls that happen?). Not this card.

## Open questions for the operator (non-blocking; defaults chosen above)

- `StallMinutes = 30`: the measured "too long" figure. 20 would have surfaced the 2026-08-23 stall
  earlier; it also lands inside the 20-min model-wait deadline's territory for chatty sessions.
  Shipped at 30, one config key to move.
- Whether the stall row should also post to the orchestrator's Telegram/notes channel the way
  Critical incidents do. Not in this plan — the attention rail is where the operator looked when
  they found the last one; a chat notification is a separate preference.
