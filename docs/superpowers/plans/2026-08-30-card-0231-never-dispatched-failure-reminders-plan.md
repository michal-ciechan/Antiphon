# CARD-0231 — a failure before the first check-in gets reminded on the check ramp, and counts until acknowledged

- **Card:** CARD-0231 — *A task that fails before its first check-in is armed gets no periodic
  visibility, only a one-time note*
- **Task:** `ac1acfa1` (Plan, Frontier, Shared) · 2026-08-30 · code re-read on master `5be33f7`
- **Related:** CARD-0047 (check-in ramp), CARD-0220 (S3 landed `FailAndNotifyAsync` in the dispatch
  catch), CARD-0221 (the build whose dispatch failed), CARD-0035 (attention feed), CARD-0055
  (a note is `Sent` only on a confirmed transcript record), CARD-0091 (parked-message sweep),
  CARD-0132 (status-poll read receipts)

## Verdict up front

**Recommend option 1 + the working half of option 2, both small; not option 3.** Concretely:

1. **Arm the existing `NextCheckAt`/`CheckCount` columns on a task that fails before it was ever
   dispatched**, and add one sweep to `AgentTaskDispatcher.TickAsync` that walks the same
   Fibonacci ramp (5, 10, 15, 25, 40, 60 … minutes, 10 steps) and **re-sends the
   `[task xxxx failed]` note only while nothing shows the caller has heard** — the note's own
   `Sent` status, a `-Status` poll, or a UI read all count as hearing. It is a reminder, not a
   check: no probe, no interpreter, no model call, no session (a Failed pre-dispatch task has
   none, and `AgentTaskCheckService.RunCheckAsync` refuses settled tasks by design at
   `AgentTaskCheckService.cs:141`).
2. **Promote the attention row from "context" to "counted" while the reminder is armed.** Today a
   pre-dispatch failure *is* in the feed as `RecentFailure`, but that kind is excluded from every
   count a human sees (`AttentionSummaryDto.From`, `HomePage.NeedsAttentionBadge`,
   `OrchestratorPage`, `AttentionPanel` — all filter `kind !== 'RecentFailure'`), lives in a group
   that renders **collapsed** by default, and the orchestrator bundle tells the agent *"Do not poll
   and do not wait"* (`server/Bundles/orchestrator.md:11`). So option 2 as the card imagines it —
   "the row already exists" — has exactly the gap the card warns about: a signal nobody is
   prompted to look at. The fix is one new `AttentionKind` (`FailureUnacknowledged`, severity
   Error) emitted **instead of** `RecentFailure` for a Failed, never-dispatched task whose reminder
   is still armed. The reminder machinery's own state is the predicate, so the row appears the
   moment the failure is recorded and disappears the moment anything acknowledges it.

Option 3 is rejected on the card's own evidence plus two facts found here: the caller session
(`cefed08a`) was `Running` the entire 10.5 hours — `CallerIsListeningAsync` would have said yes on
every tick — and this deployment has **zero alert sinks** (`AlertMinSeverity` is null on every
`ChatChannels` row), so "raise an alert" reaches nobody either. The one-shot note is the only
signal that exists for this shape, and CARD-0055's own history is that a single WhenIdle note can
sit unsubmitted for 104 minutes or be lost outright.

No schema change. No new subsystem. One build slice (server sweep + arming + attention kind +
client kind mapping + tests); the plan says where each line goes.

## What the code does today (verified against `5be33f7`)

The card's description is accurate. Details that matter for the design:

- **`ArmFirstCheck`** (`AgentTaskDispatcher.cs:1829`) — called from three places, all after a
  successful launch/reuse (`:1741`, `:2482`, `:2591`). Sets
  `NextCheckAt = dispatchedAt + clamp(ExpectedDurationMinutes, 1, 1440)`, `CheckCount = 0`. Declines
  `ReplyTo != Session` and `Role == Check`.
- **Check sweep** (`RunScheduledChecksAsync`, `:1462`) selects
  `Status ∈ {Dispatched, Working} && NextCheckAt != null && NextCheckAt <= now && ReplyTo == Session
  && ParentSessionId != null && Role != Check`. Disarms when `CallerIsListeningAsync` (`:1599`,
  parent session `Created|Starting|Running`) is false. Re-arms **before** running via
  `ClaimCheckAsync` (`:1580`, a conditional `ExecuteUpdate` keyed on the values it read). Hands ids
  to `AgentTaskCheckQueue`; the worker (`AgentTaskCheckService.RunCheckAsync`) returns
  `AlreadySettled` for any settled status and `NoRecipient` without a parent session. Its probe
  (`DelegateCheckProbe.GatherAsync`) is null-safe on `AgentSessionId` but every fact it gathers is
  about a session.
- **Dispatch catch** (`:400–410`) — CARD-0220 S3 is live: `FailAndNotifyAsync(task, "Dispatch
  failed before a session existed: …", "dispatch", ct)`, filtered
  `when (ex is not OperationCanceledException || !ct.IsCancellationRequested)`.
- **`FailAndNotifyAsync`** (`:1316`) — `FailAsync` (status Failed, `CompletedAt`, a `Failed`
  event) → `RemoveEphemeralAgentAsync` → `SaveChangesAsync` → if `ReplyTo == Session`, enqueue
  `BuildCompletionNote(task, settings, reason)` to the parent session **WhenIdle**, origin
  `Delegation`, conversation key `task:{RootTaskId:N}`, `SourceTaskId = task.Id`,
  `ContentDigest = DelegationNoteDigest.Compute(reason)`; an enqueue exception is logged and
  swallowed → `AgentTaskChanged`. Best-effort by design.
- **What "the note landed" means today.** `SessionQueuedMessage.Status` is `Sent` only when a
  matching `UserPrompt` transcript record was confirmed (CARD-0055/0024); `Pending` with
  `DeliveryAttempts >= MaxDeliveryAttempts` (3) is *parked* and excluded from every automatic
  redelivery; `Canceled` is a human Drop (the CARD-0091 sweep never touches a row with
  `SourceTaskId`, `ParkedMessageSweepService.cs:52`). `EnqueueAsync` stores `ContentDigest` but
  does **not** dedupe on it — only the CARD-0132 polled-shrink path reads it — so a second enqueue
  of the same note is a second row.
- **Acknowledgement signals that already exist on `AgentTask`:**
  `LastPolledResultAt`/`LastPolledResultHash` — stamped by `AgentTaskService.GetAsync` when the
  polling session is the parent and the task is settled; the "report" it hashes is
  `Result ?? FailureReason` (`AgentTaskService.cs:553`), so `delegate.ps1 -Status <id>` from the
  orchestrator on a *failed* task counts. `ReadAt` — `POST /api/agent-tasks/{id}/read`
  (`MarkReadAsync`, wired from the client drawer via `agentTasks.ts:323`). `Retried` — `RetryAsync`
  requeues the same row (`Status = Queued`), which takes it out of any Failed-only filter.
- **Attention** (`AttentionService.GetAsync`, `:127–155`): `RecentFailure` = Failed with
  `CompletedAt` in the last 24 h, newest 20, severity **Warning**, actions Retry/OpenDrawer, and
  it *already includes* pre-dispatch failures (there is no session/agent requirement — the row
  passes `AgentSessionId` null). Excluded from `AttentionSummaryDto.Open`; rendered in the
  `failures` group, `collapsed: true` (`attentionVisuals.ts:167`). The feed is pull-only:
  `GET /api/attention` and `/summary`, polled by the client every 15 s
  (`attention.ts:129`); no SignalR event, no alert, no channel line is ever produced from it.
- **Alerts:** `IAlertService.RaiseAsync` writes an `Alerts` row and routes by
  `ChatChannels.AlertMinSeverity`; live query 2026-08-30: **no channel has one set**. An
  `AgentIncident` needs a non-null `AgentId` (CARD-0220 plan already ruled it out for this shape).
- **Cards:** CARD-0040's transition sweep counts Dispatched/Working/Blocked as active and moves
  nothing on Failed; a Queued task never moved CARD-0221 out of Backlog, and `CardStalled` needs
  7 days In Progress. No card-side signal exists for this shape.
- **Orchestrator bundle:** *"Reports arrive between your turns as `[task <id> done] ...`. Do not
  poll and do not wait — end your turn; the report will reach you."* The agent that dispatched
  the failed task is instructed not to look.

## The incident, re-read from the database

| fact | value |
|---|---|
| task | `73f31a1c` — Worker/Code, High, Grok, Worktree, `ReplyTo = Session`, `ExpectedDurationMinutes = 90`, bound to CARD-0221 |
| created | 2026-08-28 **22:36:49Z** |
| failed | 22:37:44Z (`Failed` event #9; the card's `23:37:44` is the server log's local BST stamp) |
| `DispatchedAt` / `NextCheckAt` / `CheckCount` | null / null / 0 |
| notes to parent | **none** — `SessionQueuedMessages` has no row with this `SourceTaskId` (pre-CARD-0220 `FailAsync` path) |
| parent session `cefed08a` | `Running`, started 2026-08-16, still running on 2026-08-30 — listening the whole time |

**Base rate (whole database, 726 tasks, 45 Failed):** 6 tasks failed with `DispatchedAt` null —
five with the CARD-0220 signature (08-23, 08-24, 08-27, 08-28 ×2) and one config error on 08-18
(`Agents:Definitions:grok:Kind 'Grok' is not a known AgentKind`). Zero of the six produced a
note. Zero pre-dispatch failures in the server log since CARD-0220 deployed (`Failed to dispatch`
count is 0 in `antiphon-20260829.log` and `antiphon-20260830.log`). So: rare (0.8 % of tasks),
clustered on one root cause now fixed, and every past instance was silent.

## Answers to the brief

**1. Is the card's account still accurate?** Yes. Two refinements: the dispatch catch already uses
`FailAndNotifyAsync` (CARD-0220 S3 shipped in `b7c8007`), and the check *worker* — not just the
sweep — refuses settled tasks (`AlreadySettled`), so option 1 cannot literally reuse the
check-interpreter cycle without gutting that guard; the guard is correct and stays.

**2. How is a `RecentFailure` row discovered today?** It is not. There is no push (no SignalR
event, no alert, no channel line); the client polls the feed every 15 s but every human-facing
count excludes the kind and the group is collapsed; the orchestrator session never reads the feed
and is told not to poll. A `RecentFailure` row is discovered only by a person opening the
Orchestrator page's Attention tab and expanding "Recent failures". The card's suspicion is
confirmed: option 2 *as it stands* is the one-shot note's gap in a second costume. It becomes
useful only if the row is promoted into the counted band, which is what S3 below does.

**3. Recommendation:** option 1 as a **reminder sweep** on the existing columns, plus S3. "Still
gets checked" for a task with no session means: on the ramp, re-ask *"did the caller hear?"* and
re-send the failure note if not; never gather facts, never interpret. Exact changes are in §Design.

**4. Verification:** §Tests — a seeded `Failed` task with `DispatchedAt == null` and a parked or
absent note gets a second note from a tick, on the ramp, and stops the moment any acknowledgement
appears.

## Design

### S1 — arm the reminder when a pre-dispatch failure is recorded

In `FailAndNotifyAsync` (`AgentTaskDispatcher.cs:1316`), before its `SaveChangesAsync`:

```csharp
if (task.DispatchedAt is null)          // never had a session: nothing else will ever look
    ArmFailureReminder(task, now);      // NextCheckAt = now + CheckSchedule.NextInterval(_settings, task.ExpectedDurationMinutes, 1); CheckCount = 0
```

`ArmFailureReminder` mirrors `ArmFirstCheck`'s guards (`CheckEnabled`, `ReplyTo == Session`,
`Role != Check`). It deliberately does **not** use `ExpectedDurationMinutes` for the first
interval: that number describes work that never started (90 minutes in the incident). The first
reminder comes at the ramp's base, 5 minutes — the same first gap a check would take after its
ETA — and the ETA is still honoured as the ramp's input for later steps, exactly as checks do.
Scoping by `DispatchedAt is null` keeps the three other `FailAndNotifyAsync` callers unchanged
(see D2).

### S2 — the reminder sweep

New `RemindUnacknowledgedFailuresAsync` registered in `TickAsync` right after `"scheduled checks"`
(`:208`), through `RunSweepAsync` so a throw costs only itself:

```
select AgentTasks where Status == Failed && DispatchedAt == null
   && NextCheckAt != null && NextCheckAt <= now
   && ReplyTo == Session && ParentSessionId != null && Role != Check
order by NextCheckAt
```

Per row, in this order (each arm ends with `ClaimCheckAsync`, so two ticks cannot both act):

| condition | action |
|---|---|
| any note with `SourceTaskId == task.Id && Origin == Delegation` is `Sent`, **or** `LastPolledResultAt != null`, **or** `ReadAt != null`, **or** such a note is `Canceled` (a human dropped it) | **acknowledged** → `ClaimCheckAsync(task, nextCheckAt: null, …)`; Information log |
| `!CallerIsListeningAsync(task)` | disarm, same log line the check sweep uses (`Checks on task … stopped — its caller session … is gone`) |
| a note exists, `Pending`, `DeliveryAttempts < MaxDeliveryAttempts` | **in flight** (caller mid-turn, WhenIdle not yet flushed) → advance the schedule only; send nothing — a second row here is the CARD-0055 double-type in a new place |
| otherwise (no note row — the enqueue threw; or parked) | `checkNumber = CheckCount + 1`; `budgetSpent = checkNumber >= CheckMaxCount`; `ClaimCheckAsync(task, budgetSpent ? null : now + NextInterval(…, checkNumber), checkNumber)`; enqueue `BuildCompletionNote(task, _settings, task.FailureReason, workspaceNote: $"reminder {checkNumber}/{CheckMaxCount} — the first failure note did not reach you")` with the same origin/key/`SourceTaskId`/digest as the original; add an `AgentTaskEventType.Warning` event `Failure reminder #n queued: first note <absent|parked>`; on the last one append `final reminder — the {CheckMaxCount}-reminder budget is spent` |

Ramp: 5, 10, 15, 25, 40, 60, 60, 60, 60 minutes → the tenth and last reminder ~5.6 h after the
failure. After that the task still shows in the feed (S3 keeps it counted until acknowledged —
the ramp stopping is not an acknowledgement).

Reuses: `ClaimCheckAsync`, `CallerIsListeningAsync`, `CheckSchedule.NextInterval`,
`DelegationReportFormatter.BuildCompletionNote`, `DelegationNoteDigest.Compute`. New code is one
sweep method (~60 lines) and one arm helper.

**Invariant to pin, not assume:** `RunScheduledChecksAsync` must keep filtering
`Status ∈ {Dispatched, Working}` — a Failed row with `NextCheckAt` set is now a legal state and
must never reach the check worker (it would return `AlreadySettled` harmlessly, but a claimed
check that does nothing is a wasted ramp step and a misleading log line).

### S3 — the row counts while the reminder is armed

`AttentionKind.FailureUnacknowledged = 15`, severity **Error** (lands in the "Broken" group —
*"Something failed and has not been picked up"* is precisely this), actions Retry + OpenDrawer.
In `AttentionService.GetAsync`, split the `failed` list: rows with `DispatchedAt == null &&
NextCheckAt != null` become `FailureUnacknowledged` (title = task title, summary = `Failed before
dispatch; no completion note has reached session <short id> — reminder n/10`, evidence =
`FailureReason`); the rest stay `RecentFailure`. The predicate is the reminder's own state, so:

- appears at the failure, not after a delay (the row is armed inside `FailAndNotifyAsync`);
- disappears on any acknowledgement (S2 disarms) — including a drawer open, because
  `MarkReadAsync` sets `ReadAt`;
- never appears for `ReplyTo == None` (nothing is armed — nobody is waiting on it);
- is not subject to the 24 h `RecentFailure` window — an unacknowledged failure does not become
  history by ageing.

Client: add `'FailureUnacknowledged'` to the kind union in `client/src/api/attention.ts` and an
entry in `attentionVisuals.ts`'s kind table (icon/label); grouping by severity needs no change.
Nothing else in the client filters by kind except the `RecentFailure` exclusions, which now do the
right thing by construction.

### Rejected alternatives

- **A real check-interpreter cycle on the Failed task.** Nothing to probe (no session, no
  transcript, no queue, no git), the worker refuses settled tasks on purpose, and it would spend a
  model call to restate `FailureReason`.
- **`IAlertService.RaiseAsync` from the catch.** Five lines, but it reaches nobody on this
  deployment (no sinks) and, once a sink exists, every Warning goes there — CARD-0220 D3 already
  declined it for that reason.
- **An `AgentIncident`.** Needs an `AgentId`; a pre-dispatch failure has none (CARD-0220 plan).
- **Automatic retry of a failed dispatch.** Declined in CARD-0220; a config error would retry into
  the same wall on every ramp step, and the CARD-0220 shape needed a self-heal, not a retry.
- **New columns (`ReminderCount`, `RemindAt`).** `NextCheckAt`/`CheckCount` are unused on a Failed
  row and their sweep already filters by status; a migration would buy nothing but a second ramp
  to keep in step.
- **Making `RecentFailure` itself count.** Turns every ordinary failure of the day into a badge —
  the exact thing CARD-0035 chose against — and cannot distinguish "heard and moved on" from
  "never heard".
- **Widening to every `FailAndNotifyAsync` caller now.** A task that ran had checks and a session
  the caller has been hearing about; its lost completion note is a real but different gap (a
  settled task's note goes silent). Left as D2.

## Decisions for the operator

- **D1 — first reminder at the ramp base (5 min), recommended; or at `ExpectedDurationMinutes`
  like `ArmFirstCheck`.** The ETA describes the work; the work never started. 5 minutes also means
  the common outcome — the note was merely waiting for the orchestrator's turn to end — is
  resolved on the first look, and costs one query.
- **D2 — scope: `DispatchedAt == null` only (recommended), or every `FailAndNotifyAsync` caller
  (dead-session reconciler, overdue deadline).** The narrow scope is the card's; the wide one is a
  one-line change to the arm site and a filter tweak, and could ship later under its own card once
  a lost post-run note is actually observed.
- **D3 — acknowledgement set.** Recommended: note `Sent`, note `Canceled`, `LastPolledResultAt`,
  `ReadAt`. Say if a human Drop should *not* count (then a dropped note re-sends on the ramp).
- **D4 — severity of `FailureUnacknowledged`: Error (recommended) vs Warning.** Warning would put
  it in "Suspect" and still count; Error says what it is.

## Slices

| slice | what | area | size |
|---|---|---|---|
| S0 | tests first (below), red on master | `delegation` tests | small |
| S1 | `ArmFailureReminder` + call in `FailAndNotifyAsync` | `delegation` (server) | ~20 lines |
| S2 | `RemindUnacknowledgedFailuresAsync` + registration in `TickAsync` | `delegation` (server) | ~70 lines |
| S3 | `AttentionKind.FailureUnacknowledged` server + client mapping | `attention` (server + client) | ~40 lines |
| S4 | close-out: `docs/orchestration-loop.md` §4 one paragraph; AGENTS.md gotcha one bullet; card | `docs` | small |

One build task, Worktree, Code role, `-ExpectAbout 45`. No migration. Verify with the named tests
only; no full-suite run is needed for this footprint.

## Tests

`tests/Antiphon.Tests/Application/AgentTaskDispatchFailureTests.cs` (existing harness:
`CreateHarness`, `SeedParentSessionAsync`, `SeedQueuedWorktreeTaskAsync`; `[NotInParallel("AgentQueue")]`):

- `a_dispatch_failure_arms_a_reminder` — after the existing failing tick: `NextCheckAt ==
  CompletedAt + 5 min`, `CheckCount == 0`. Red on master (`NextCheckAt` null).
- `a_lost_failure_note_is_re_sent_on_the_ramp` — seed Failed, `DispatchedAt` null, `NextCheckAt`
  in the past, **no** note row; tick; exactly one Delegation note with `SourceTaskId == task.Id`,
  header starting `[task xxxx failed]` containing `reminder 1/10`; `CheckCount == 1`;
  `NextCheckAt == now + 10 min`; one `Warning` event.
- `a_parked_failure_note_is_re_sent` — same with a note at `DeliveryAttempts == 3`, `Pending`.
- `a_pending_note_is_not_duplicated` — note `Pending`, `DeliveryAttempts == 0`: no new row,
  schedule advanced.
- `a_sent_note_disarms_the_reminder`, `a_status_poll_disarms_the_reminder`
  (`LastPolledResultAt` set), `a_read_disarms_the_reminder` (`ReadAt`), `a_dropped_note_disarms`
  (`Canceled`) — `NextCheckAt` null after the tick, no new row.
- `a_gone_caller_disarms_the_reminder` — parent session `Stopped`.
- `the_reminder_budget_ends_and_says_so` — `CheckCount == 9`: one final note containing
  `final reminder`, `NextCheckAt` null.
- `a_reminder_is_never_armed_for_a_board_only_task` — `ReplyTo == None`.

`AgentTaskCheckSweepTests.cs`: `a_failed_row_with_a_due_check_is_never_claimed_as_a_check` — seed
Failed + due `NextCheckAt`; `RunScheduledChecksAsync` returns 0 and the queue receives nothing.

`AttentionServiceTests.cs`: `a_never_dispatched_failure_still_armed_is_FailureUnacknowledged`
(Error, counted in `AttentionSummaryDto.Open`, not in the `RecentFailure` set) and
`once_disarmed_it_is_a_RecentFailure`.

Client: `attentionVisuals.test.ts` — `FailureUnacknowledged` groups to `broken`;
`AttentionPanel.test.tsx` — it is counted in `open`.

Live smoke after deploy: `delegate.ps1 -Worktree -Dir <a non-git temp dir> -Goal x` from an
orchestrator session → `[task … failed]` note lands → within 5 min the task's timeline shows no
reminder and the feed shows no `FailureUnacknowledged` row. Then Drop the note from the queue UI
before it is delivered and watch the reminder arrive at +5 min and the row sit in "Broken" until
`-Status` is run.

## What was NOT determined

- Whether the orchestrator's completion note, once `Sent`, is ever *acted on* — `Sent` means the
  text reached the transcript as a prompt, which is the strongest signal available; nothing can
  tell whether the agent did anything with it. Out of scope; CARD-0159's plan covers report
  evidence on the delegate side.
- Whether any of the five earlier CARD-0220-shape failures were noticed sooner than 73f31a1c by
  other means (the operator's memory, not the database).
- The exact rendering (icon/label) for the new kind — a build-time choice inside
  `attentionVisuals.ts`'s existing table.

## Environment / cleanup

Investigation only: no build, no tests run, no `bin-*` directories created. Working tree touched
only by this file.
