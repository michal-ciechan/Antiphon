# CARD-0481: A wall reroute must dispatch on its new candidate, and a capacity wait must end with its consumer

Date: 2026-09-15. Stage: Plan (task `55bd7dc6`, worktree `card-task-55bd7dc6`, base `9b914298` =
origin/master at planning time). Verification design is included below (`## Verification design`),
so the next stage is Code. Based on the Investigate report
[docs/investigations/2026-09-15-card-0481-grok-usage-wall-reroute-stuck-queued.md](../../investigations/2026-09-15-card-0481-grok-usage-wall-reroute-stuck-queued.md)
(task `08203aae`, commit `4f07cdff` on `feat/card-task-08203aae`, not yet on master) and on the live
database read on 2026-09-15 09:30Z.

## Outcome and scope

One root cause, three consequences, one adjacent leak that the same object explains.

1. **The stall (the card).** `ApiErrorRecoveryService` registers the walled session's `LiveSession`
   capacity wait with `TaskId = <this task>` and `ExecutionKind = <the walled kind>`. The CARD-0090
   reroute then rewrites the task's kind/level and requeues it, but nothing ends that wait. The
   CARD-0412 dispatch gate (`AgentTaskDispatcher.cs:646-649`) matches waits by `TaskId` alone and
   redeems against `wait.ExecutionKind`, so the requeued task waits for the **old** kind's grant,
   silently, every tick. `3bfe742a` (Grok → Codex, 24.5 min then cancelled), `0c8a18f7` (fable →
   opus, 68 min, dispatched the second the fable wait was granted).
2. **The silence.** The gate's `continue` writes no event, log line or counter. Independently, the
   tick's once-per-task `everHeld` rule means a task that was ever held for any reason never gets a
   second `Held` event for a new reason; `3bfe742a` had 29 lease-hold events from earlier that day,
   so even a well-placed event would have been muted.
3. **The leak.** No code anywhere sets a wait to `Canceled` or `Superseded`. Waits outlive their
   consumers: today the database holds 18 unfinished waits, 5 `LiveSession` (all five sessions
   Stopped/Failed) and 13 `QueuedTask` (10 name Succeeded/Canceled tasks), every one re-armed
   `grant-expired` hundreds of times, and **all three provider grant slots (Claude, Codex, Grok) are
   currently held by moot waits.** A real capacity waiter on any kind today is starved behind them.
4. **The in-flight half of the leak.** The migration added `LaunchSessionId`/`LaunchReceipt`/
   `DispatchAttemptId`, but no code writes them. A wait redeemed at dispatch stays `Admitted` with
   the old (or no) `SessionId`, so the transcript observer never confirms or progresses it; it
   re-arms every two admission intervals for the task's whole run (`006dbac3`: admitted once on
   2026-09-13, task Succeeded, 123 re-arms since).

In scope: kind-scoping the dispatch gate; ending a task's prior waits at the three places its
candidate changes (wall requeue, dispatch-time rewalk, routing-blocked resume) and on cancel; a
`Held` trace, counter and log for the gate skip, with the once-only rule made once-per-reason; an
orphan sweep in capacity reconciliation for waits whose session or task has ended; a dispatch
receipt so a redeemed wait progresses through the existing transcript path; two doc sentences.

Out of scope, deliberately: CARD-0535 (sibling-base hold then repository lease; different root
cause, and its per-tick lease `Held` event at `AgentTaskDispatcher.cs:2977-2986` is left exactly as
it is); the unexplained "pre-reroute alias resolved 0.4 s after the reroute" observation
(investigation uncertainty 1; harmless, chose the same candidate); waits whose task is `Blocked`
(three Grok rows from 2026-09-02 still cycle; a `Blocked` routing-exhausted task can still be
resumed and redeem, so the sweep leaves `Blocked` owners alone — a bound on zero-admission re-arm
cycles is a separate card); any bounded-wait-then-`Blocked` escalation (D-4 says why); any change to
the granter's FIFO or to `CapacityRecoveryPolicy`.

## Ground truth

Verified against `9b914298` and the local database on 2026-09-15.

| Claim (card, brief, investigation, or a tempting shortcut) | Evidence | Consequence |
|---|---|---|
| Card: "the actual redispatch never fired"; maybe no sweep picks up the requeued row. | `AgentTaskDispatcherHostedService.cs:38-45` ticks every `PollIntervalSeconds` (5 s); `TickAsync` loads every Queued row (`AgentTaskDispatcher.cs:327-330`) and reached the gate for `3bfe742a` on every tick; `continue` at `:646-649`. | The fix is at the gate and the requeue, not a new sweep. Being Queued *is* the redispatch request (investigation §"What is supposed to happen"). |
| Brief: the gate matches by `TaskId` only and ignores kind. | `HasUnfinishedCapacityWaitAsync` `:795-806` (`w.TaskId == task.Id \|\| w.ConsumerKey == "task:<id>"`, four terminal states excluded); `TryRedeemCapacityWaitAsync` `:809-837` loads the same shape and calls `RedeemAsync(wait.Id, wait.ActionKey, wait.ExecutionKind, …)`. `ApiErrorRecoveryService.cs:454-467` registers the session wait with `ExecutionKind = session.AgentKind`, `TaskId = openTaskId`. | D-1 adds `ExecutionKind == task.AgentKind` to both queries. |
| Tempting: kind-scoping alone fixes the card. | `0c8a18f7` (2026-09-13) was fable → opus: both `ClaudeCode`. Its wait `006dbac3` has `ExecutionKind = 1`, the same as the rerouted task's kind. Kind-scoping still matches it; the 68-minute stall stays. | D-2 (end the task's waits on requeue) is required, not optional. Kind-scoping is defence in depth for a missed supersede site. |
| Brief: supersede "when `RequeueOnWallAsync` reroutes". | Three sites change a task's candidate in place: `RequeueOnWallAsync` → `RequeueAsync` (`AgentTaskService.cs:2027-2076`, `:2179-2237`); `TryRewalkQueuedChainAsync` (`AgentTaskDispatcher.cs:717-760`, kind/level rewritten on a Queued row, then falls through to the gate); `ResumeRoutingBlockedAsync` (`:838-905`, calls `EnsureWaitOnAsync` with the chosen kind, but `EnsureWaitCoreAsync` `:330-350` re-finds the existing unfinished `task:` row by `ConsumerKey` and returns it with its **old** `ExecutionKind`/`RequestedAlias` unchanged). | One helper, three callers plus `CancelAsync` (D-2, D-3). |
| Brief: the skip writes no event, no log, no Blocked. | `:646-649` is a bare `continue`; `TickResult` (`:211-222`) has no capacity counter; the hosted service logs only dispatched/concurrency/scope/failures (`AgentTaskDispatcherHostedService.cs:46-51`). | D-4. |
| Tempting: add a `Held` event at the gate under the existing `everHeld` rule. | `everHeld` is loaded once per tick from all prior `Held` events (`:374-380`) and every site writes only on `everHeld.Add(task.Id)` (`:450`, `:483`, `:555`, `:596`, `:622`). `3bfe742a` already had 29 `Held: repository mutation lease…` events (17:50–17:53Z); a gate event under this rule would never have been written for it. | D-5: once per **reason change**, not once per task. |
| Brief: "5 known leaked `LiveSession` rows". | DB 2026-09-15 09:30Z, unfinished non-cursor waits = 18. `LiveSession` 5: `b749f188`, `8840f21c`, `f489ecb2` (no task; session Failed), `e86f8a7b` (task `3bfe742a` Canceled; session Stopped), `006dbac3` (task `0c8a18f7` Succeeded; session Stopped). `QueuedTask` 13: 8 for Canceled tasks, 2 for Succeeded, 3 for Blocked (`58b504c4`, `58c746cc`, `6a1fd14f`, Grok, 2026-09-02). All 18 `OutcomeReason = grant-expired`; `AdmissionCount = 0` except `006dbac3 = 1`; `ActionOrdinal` up to 1135. `CapacityRecoveryProviderStates`: Claude → `f489ecb2`, Codex → `0d37f8ea` (task Succeeded), Grok → `362da1ae` (task Blocked). | The leak is not `LiveSession`-specific; D-6 covers both consumer kinds by owner liveness. The sweep also repairs the 18 rows on its first pass and frees all three grant slots. |
| Investigation: "the reconcile pass skips terminal tasks only for `task:` keys". | That pass is `ReconcileCompatibilityAsync` (`CapacityRecoveryService.cs:634-779`); it *creates* waits (`LegacyAvailable`) and its terminal check only stops creation. `grep "State = CapacityRecoveryWaitState.(Canceled\|Superseded)"` over `server/` finds zero assignments. | D-6 is new code, not a widened branch. The compat pass only considers Running/Starting sessions and Queued/Blocked/retained-Working tasks, so it cannot recreate what the sweep cancels. |
| Tempting: a task's wait progresses when the task dispatches. | `RedeemAsync` `:236-301` sets `Admitted` and nothing more. `ObserveTranscriptAsync` `:526-555` finds waits by `w.SessionId == sessionId`; a `task:` wait's `SessionId` is null (registered while Queued) or the dead session; `DispatchAttemptId`/`LaunchSessionId`/`LaunchReceipt` appear only in the migration (`server/Migrations/20260907213845_Card0412CapacityRecovery.cs:215-217`). `RearmStalledAdmissionsAsync` `:966-1014` re-arms an `Admitted` wait after two intervals, spending no admission, forever. | D-7 writes the receipt so the existing observer terminalizes the wait. |
| The retained-Working return path shares `TryRedeemCapacityWaitAsync`. | `:387-410`: `retained.CapacityWaitId` is the session wait (`AgentTaskReplyService.cs:1080-1097`), same task, same session, same kind. | D-1 cannot break it; `CapacityRecoveryTaskTests.Card0412_V19_*` are the regression guard. |
| A `session:` wait might be what restarts an AlwaysOn agent. | `AgentSupervisorService.TryHandleCapacityWaitAsync` `:396-412` looks up by `AgentId` then `agent:<id>` (`StandingStart`, registered at `:327`). The `LiveSession` registration (`ApiErrorRecoveryService.cs:454-467`) and the compat pass set no `AgentId`. | D-6's session-ended cancel cannot strand a standing restart. V-10 keeps an `agent:` wait as a control. |
| `CreateRefusal` waits could be matched by the gate after a "transfer". | `RegisterCreateRefusalWaitAsync` (`AgentTaskService.cs:2656-2690`) registers `refusal:<session>:<digest>` with `TaskId = null`; `RefusalDigest` is referenced nowhere else in `server/`. No transfer exists. | D-1 changes nothing for refusal waits. |
| CARD-0535 shares the root cause. | Investigation §CARD-0535: sibling-base hold (`:619`) then 95 per-tick lease `Held` events written inside `DispatchOneAsync` (`:2977-2986`, not gated by `everHeld`), dispatched 70 s after the lease cleared. | Not conflated. D-5 changes the once-only rule for the six `everHeld` sites only; the lease event is untouched. |
| Latency after the fix. | Dispatcher tick 5 s; supervisor tick 10 s (`SupervisionSettings.TickSeconds = 10`), `ReconcileAsync` runs each tick before `supervisor.TickAsync` (`AgentSupervisorHostedService.cs:146`). | A reroute dispatches on the next dispatcher tick (pre-CARD-0412 behaviour: 4–5 s). An orphaned wait is cancelled within one supervisor tick of its session/task ending. |
| Existing coverage of the reroute itself. | `ComplexityWallRerouteTests` (12 tests) drive `AgentTaskReplyService.OnTurnEndAsync` with a real `ApiErrorRecoveryService` + `CapacityRecoveryService` but no dispatcher and assert no wait state; `CapacityRecoveryTaskTests.CreateDispatcher` drives the real `TickAsync` with a warm agent. | The new class combines the two (S1). |

## Decisions

### D-1. The dispatch gate matches a wait on (task, current kind)

`HasUnfinishedCapacityWaitAsync` and `TryRedeemCapacityWaitAsync` add `&& w.ExecutionKind ==
task.AgentKind`. A wait on another kind's clock is not this dispatch's wait, by construction: the
gate must never spend a Codex dispatch waiting on Grok's grant.

Rejected: leaving the query as is and relying on D-2 alone. A missed or racing supersede would
recreate the silent stall; the gate is the last line and should be correct on its own terms.

### D-2. A requeue ends every capacity episode that names the task

`RequeueAsync` (retry, escalate, wall reroute) calls
`CapacityRecoveryService.SupersedeTaskWaitsOnAsync(_db, task.Id, reason, ct)` right after
`StopDelegateAsync`, with `reason = $"requeued:{type}:attempt-{task.Attempt + 1}"` (e.g.
`requeued:Rerouted:attempt-2`). Every unfinished wait with `TaskId == task.Id` or `ConsumerKey ==
"task:<id>"` becomes `Superseded` (`Outcome = "Superseded"`, `OutcomeReason = reason`, `Version++`);
a provider state whose `GrantedWaitId` is one of them has its grant cleared under that kind's
provider lock, so the slot is reissued on the next reconcile instead of expiring two intervals later.

Why all of them, not only other-kind ones: the `LiveSession` wait belongs to the session the requeue
just killed, and CARD-0412 already treats waits as attempt-scoped ("No unresolved recovery callback
can overwrite a newer task attempt"); every other attempt-scoped field is reset in the same method.
The same-kind case (`0c8a18f7`) is only fixed this way. The new attempt is then ordinary work: if its
candidate is held, the tick's model-held block registers a fresh `task:` wait (`WaitingForHold`) and
pacing applies; if not, it dispatches on the next tick, which is what the CARD-0090 reroute promised
and what both pre-CARD-0412 reroutes did.

Rejected: (a) transferring the wait to the new candidate (`HoldAlreadyCleared = true`) — paces a
kind that was never observed blocked, costs at least one admission interval plus a supervisor tick,
and today joins a FIFO polluted by dead waits; (b) kind-scoping only — see ground truth row 3.

### D-3. The two other in-place candidate changes, and cancel, use the same helper

- `TryRewalkQueuedChainAsync`: after `task.AgentKind/ModelLevel` are rewritten, supersede with
  `reason = "rerouted-at-dispatch"`, keeping any wait already on the chosen `(kind, alias)`.
- `ResumeRoutingBlockedAsync`: before `EnsureWaitOnAsync`, supersede with
  `reason = "resumed-routing-blocked"`, keeping a wait already on the chosen `(kind, alias)`, so the
  registration that follows creates a fresh `RoutingBlockedTask` wait carrying the chosen kind and
  alias instead of re-finding the old row.
- `CancelAsync`: after `StopDelegateAsync`, supersede with `reason = "task-canceled"` (the `task:`
  wait dies with the cancel; the killed session's wait follows through D-6 within a tick).

Helper shape:

```csharp
public async Task<int> SupersedeTaskWaitsOnAsync(
    AppDbContext db, Guid taskId, string reason, CancellationToken ct,
    (AgentKind Kind, string? Alias)? keep = null)
```

Same-context like `EnsureWaitOnAsync`/`BumpWaveOnAsync`: for each affected `ExecutionKind`, an
owned-or-ambient transaction with `TakeProviderLockAsync`, the state change, `ClearGrant` if the
provider's `GrantedWaitId` is in the set, `SaveChangesAsync`, commit when owned. Returns the count.
No `AgentIncident`; one structured `LogInformation` per wait (wait id, consumer key, kind, reason).

Rejected: teaching `EnsureWaitCoreAsync` to supersede on a kind/alias mismatch. It would cover the
resume site but neither the wall requeue (the session key is never re-registered) nor the rewalk
fall-through (no registration happens), so the explicit calls are needed anyway; one mechanism is
easier to reason about than two.

### D-4. A gate skip leaves a trace; it does not Block

On a skip the tick: increments a new `TickResult.SkippedCapacityWait` (appended positional member,
default 0; the hosted service's debug line names it); stamps `task.CapacityWaitId = wait.Id` when it
differs (no `CapacityWaitRetained`, no `CapacityWaitReason`; `Card0412_V19_historical_capacity_wait_id_still_counts`
already proves a bare `CapacityWaitId` changes no counting) so `GET /api/agent-tasks/{id}` shows the
wait a Queued task is behind; writes one `Held` event through D-5 with detail
`waiting for {ExecutionKind} capacity ({RequestedAlias ?? "any alias"}); capacity wait {short id} not yet granted.`
(no state or due time in the text, so re-arm cycles do not produce new events); logs one Information
line on the transition. The existing "released" line fires when the task later dispatches.

The gate is refactored to one query: `FindUnfinishedCapacityWaitAsync(task)` returning the wait, and
`TryRedeemCapacityWaitAsync(task, wait, path)`; the retained-return loop keeps a thin overload.

Rejected: bounded-wait-then-`Blocked`. After D-1/D-2 a gate skip is only ever a same-kind wait for a
kind that is genuinely held, which is CARD-0022/0412's intended behaviour with its own escalation
(`Exhausted` → `CapacityRecoveryExhausted` attention). A `Blocked` transition would turn every
legitimate six-hour Grok hold into a human question and would fight `ResumeRoutingBlockedAsync`.
Rejected: a per-tick event (twelve an hour per waiting task; the CARD-0063 reasoning stands).

### D-5. The once-only `Held` rule becomes once per reason change

`everHeld: HashSet<Guid>` becomes `lastHeld: Dictionary<Guid, string>` = the newest `Held` event
detail per queued task, loaded once per tick (select `AgentTaskId, At, Id, Detail` for the queued
ids and reduce client-side to the latest by `At` then `Id`; bounded by queued × Held rows, which the
existing query already loads). One helper replaces the six inline blocks:

```csharp
private async Task<bool> TraceHeldAsync(
    AgentTask task, string detail, Dictionary<Guid, string> lastHeld, CancellationToken ct)
```

Writes and saves a `Held` event and returns true iff `detail` differs from the task's last one; each
site logs on true exactly as it does today. The "released" log checks `lastHeld.ContainsKey`.
`TryRewalkQueuedChainAsync` drops its unused `everHeld` parameter. Detail strings at the six sites
are unchanged, so existing tests keep passing; a task held on scope, then dispatched, held again on
scope with the same holder writes once as before. Only the lease event in `DispatchOneAsync` stays
outside the rule (CARD-0535's).

### D-6. Reconciliation cancels waits whose consumer has ended

`CapacityRecoveryService.CancelOrphanedWaitsAsync(ct)` runs in `ReconcileAsync` after
`ReconcileCompatibilityAsync` and before `RearmStalledAdmissionsAsync`/`GrantReadyAsync`, so a freed
grant is reissued in the same pass. Batch-bounded by `ReconciliationBatchSize`. Rules:

| Consumer | Orphan when | `OutcomeReason` |
|---|---|---|
| `LiveSession` (`session:` key) | session row missing, or `Status` is `Stopped` or `Failed` | `session-ended` / `owner-missing` |
| `QueuedTask`, `RoutingBlockedTask` (`task:` key, or any wait whose `TaskId` names the task) | task row missing, or `Status` is `Succeeded`, `Failed` or `Canceled` | `task-terminal` / `owner-missing` |

Not orphaned: `Created`, `Starting`, `Running`, `Stopping` sessions; `Queued`, `Dispatched`,
`Working`, `Blocked` tasks; `StandingStart`, `PendingQueue`, `CreateRefusal` consumers; the
`card-0412-compatibility` cursor. Each cancel: `State = Canceled`, `Outcome = "Canceled"`,
`Version++`, `UpdatedAt`; per affected kind, provider lock + `ClearGrant` when the grant points at a
cancelled wait; one `LogInformation` per wait naming wait id, consumer key, kind, `BlockedAt`,
`ActionOrdinal` and reason. No incidents (the first pass would write eighteen).

Why a sweep and not hooks at every terminal site: Succeeded/Failed are written by the reply settle,
the dispatcher's fail paths, deadline sweeps and land; one pass is one place, it is idempotent, and
it also repairs what is already leaked. `CancelAsync` gets the synchronous call (D-3) because the
operator is watching that one.

### D-7. A dispatch that redeemed a wait receipts it

In `TickAsync`, immediately after `DispatchOneAsync` returns true, when `_capacityRecovery` is
enabled and `task.AgentSessionId` is set: `ReceiptDispatchOnAsync(_db, task.Id, task.AgentKind,
launchedSessionId, ct)` finds the task's `Admitted` wait (same query as the gate, kind-scoped) and
sets `SessionId = LaunchSessionId = launchedSessionId`, `DispatchAttemptId = Guid.NewGuid()`,
`State = StartAccepted`, `Outcome = "StartAccepted"`, `OutcomeReason = "Dispatch"`, `Version++`. From
there the existing `ObserveTranscriptAsync` confirms the first `UserPrompt` and marks `Progressed` on
the first clean `TurnEnd`, exactly as it does for a session wait. `StartAccepted` is not a grant
candidate and is not re-armed, so the kind's grant slot is free for the task's whole run.

`claimed` inside `DispatchOneAsync` is the same tracked instance as `task` (identity resolution on
the tracking query), so `task.AgentSessionId` holds the launched session id at that point; V-11
proves it rather than assuming it.

Rejected: marking `Progressed` at dispatch — CARD-0412's confirmation is the prompt, and the
`StartAccepted`/`Running` states exist for this step. Rejected: leaving it to D-6 — that fires at
settle, hours later, while the slot is being wasted.

### D-8. Two documentation sentences

`docs/session-runtime-invariants.md` Gotcha #76 (CARD-0412 bullet) gains: *"A task's prior waits
end when its candidate changes (wall requeue, dispatch-time rewalk, routing-blocked resume) or it is
cancelled (`Superseded`, reason `requeued:*` / `rerouted-at-dispatch` / `resumed-routing-blocked` /
`task-canceled`); the dispatch gate matches a wait only on the task's current kind; a redeemed
dispatch receipts its wait (`StartAccepted`) so the transcript progresses it; reconciliation cancels
waits whose session or task has ended (CARD-0481)."* `docs/orchestration-loop.md` "Complexity
chains" paragraph gains: *"A usage-wall reroute is ordinary work on the new candidate: the walled
session's capacity wait ends with the session, and the requeued row dispatches on the next tick
unless the new candidate is itself held (CARD-0481)."*

## Slices

Each slice is one commit with its tests green before the next starts. Files are repo-relative.

### S1 — Kind-scoped gate and supersede on candidate change (D-1, D-2, D-3)

- `server/Application/Services/CapacityRecoveryService.cs`: add `SupersedeTaskWaitsOnAsync`.
- `server/Application/Services/AgentTaskDispatcher.cs`: `:795-837` kind-scope both queries (and the
  D-4 refactor to `FindUnfinishedCapacityWaitAsync` + `TryRedeemCapacityWaitAsync(task, wait, path)`
  can land here to avoid touching the gate twice); `:717-760` rewalk calls the helper after the
  kind/level rewrite; `:838-905` resume calls the helper before `EnsureWaitOnAsync`.
- `server/Application/Services/AgentTaskService.cs`: `RequeueAsync` `:2179` calls the helper after
  `StopDelegateAsync`; `CancelAsync` `:1719` likewise.
- Tests: new `tests/Antiphon.Tests/Application/WallRerouteDispatchTests.cs` (V-1 … V-5), reusing
  `ComplexityWallRerouteTests`' harness and seeds (promote `WallRerouteHarness`,
  `SeedChainAsync`/`SeedHardChainAsync`, `SeedWorkingChainTaskAsync`, `SeedSessionAndAgentAsync`,
  `StampSessionModelAsync`, `SeedApiErrorStubTurnAsync`, `SeedHoldAsync` to an internal
  `tests/Antiphon.Tests/TestHelpers/WallRerouteFixture.cs`; the existing class keeps passing through
  the moved members) and `CapacityRecoveryTaskTests.CreateDispatcher` (make it `internal static`).
  The walled-Grok arm needs the seeded session and task stamped `AgentKind = Grok` with
  `EffectiveModelId = "grok-4.6"` and the stub text `"Grok Build usage balance exhausted [after 1 retries]"`
  (`UsageLimitWallParser.cs:106` recognises it). Dispatch is observed through warm reuse
  (`ModelAvailabilityDispatcherTests.SeedWarmAgentAsync`), so the chain's fallback candidate must be
  `ClaudeCode` in these tests (Grok → ClaudeCode/High reproduces "walled kind ≠ new kind" without a
  Codex runner).

### S2 — Visible gate skip (D-4, D-5)

- `server/Application/Services/AgentTaskDispatcher.cs`: `TickResult` `:211-222` (+`SkippedCapacityWait`);
  `:374-380` `lastHeld` load; `TraceHeldAsync`; the six sites `:450`, `:483`, `:555`, `:596`, `:622`
  and the gate `:646-649`; the "released" check `:685-689`; `TryRewalkQueuedChainAsync` signature.
- `server/Infrastructure/Orchestration/AgentTaskDispatcherHostedService.cs:46-51`: include the new
  counter in the debug line.
- Tests: V-6, V-6b in `WallRerouteDispatchTests`; existing `ModelAvailabilityDispatcherTests`,
  `RoutingPinCandidateDispatchTests` and any test asserting a single `Held` event stay green (the
  detail strings do not change).

### S3 — Orphan sweep (D-6)

- `server/Application/Services/CapacityRecoveryService.cs`: `CancelOrphanedWaitsAsync`, wired into
  `ReconcileAsync` `:47-55`.
- Tests: new `tests/Antiphon.Tests/Application/CapacityWaitOrphanSweepTests.cs` (V-8 … V-10) on
  `CapacityRecoveryTestSupport.CreateService` (fake clock; `GrantReadyAsync` then `ReconcileAsync`).

### S4 — Dispatch receipt (D-7)

- `server/Application/Services/CapacityRecoveryService.cs`: `ReceiptDispatchOnAsync`.
- `server/Application/Services/AgentTaskDispatcher.cs`: the block after `DispatchOneAsync` returns
  true (`:651`).
- Tests: V-11 in `WallRerouteDispatchTests` (dispatcher + `ObserveTranscriptAsync`).

### S5 — Docs and evidence (D-8)

- `docs/session-runtime-invariants.md`, `docs/orchestration-loop.md` as in D-8.
- Append a `## Build evidence` section to this plan: filters run, expanded counts, PC outcomes, and
  the post-land database check (below).

Post-land check (once the server restarts with the change): within two supervisor ticks,
`select count(*) from "CapacityRecoveryWaits" where "State" not in (7,9,10,12) and "ConsumerKey" <> 'card-0412-compatibility'`
drops from 18 to 3 (the three Blocked-owner Grok rows), and every `CapacityRecoveryProviderStates`
row has `GrantedWaitId` null or pointing at a wait whose owner is live. Record both in the evidence
section.

## Verification design

Build once into an isolated output and run the Unit lane plus the named integration classes; inspect
the fresh TRX for executed method names and nonzero counts (`docs/testing-and-build.md` §Fast lane,
§Combined class filters). Every new integration test is `[Category("Integration")]` on an isolated
schema; none spawns a process (warm reuse only), so no `ParallelLimiter` is needed.

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c481/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c481/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c481-unit
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c481/ -- --treenode-filter '/*/*/(WallRerouteDispatchTests*)|(CapacityWaitOrphanSweepTests*)|(ComplexityWallRerouteTests*)|(CapacityRecoveryTaskTests*)|(CapacityRecoveryGrantLivenessTests*)|(CapacityRecoveryAttentionTests*)|(CapacityRecoveryCompatibilityTests*)|(CapacityRecoverySupervisionTests*)|(RoutingPinCandidateDispatchTests*)|(ModelAvailabilityDispatcherTests*)|(ApiErrorRecoveryServiceTests*)|(AgentTaskReplyIntegrationTests*)/*' --report-trx --report-trx-filename c481.trx --results-directory .antiphon/c481-int
```

Delete every `bin-c481` directory before finishing (CARD-0448).

### V-n matrix

| V | Slice | Class.Method | Arrange | Assert |
|---|---|---|---|---|
| V-1 | S1 | `WallRerouteDispatchTests.Grok_wall_reroute_to_Claude_dispatches_on_the_next_tick` | Chain `(Grok, Frontier) → (ClaudeCode, High)`; Working task on a Grok session; Grok wall stub; reply → Queued/ClaudeCode; warm Claude agent in the workspace; `TickAsync`. | After the reply: exactly one `LiveSession` wait with `TaskId = task`, `ExecutionKind = Grok` exists (defect precondition). After the tick: task `Dispatched` to the warm agent; the wait is `Superseded` with `OutcomeReason = "requeued:Rerouted:attempt-2"`; no new wait for the task; `TickResult.SkippedCapacityWait == 0`. |
| V-2 | S1 | `…Same_kind_wall_reroute_fable_to_opus_dispatches_on_the_next_tick` | Hard chain (fable → opus → grok); fable wall; warm Claude agent; tick. | Same as V-1 with `ExecutionKind = ClaudeCode` on the superseded wait. This is the row kind-scoping alone cannot pass (PC-2). |
| V-3 | S1 | `…Gate_ignores_an_unfinished_wait_on_another_kind` | Queued ClaudeCode task; hand-seeded unfinished `LiveSession` wait `TaskId = task`, `ExecutionKind = Grok`, `State = Ready`; no Grok grant; warm Claude agent; tick. | Task `Dispatched`; the Grok wait untouched (state unchanged) — D-1 alone. Control: same wait with `ExecutionKind = ClaudeCode` and no grant → task stays `Queued`, one `Held` event (see V-6). |
| V-4 | S1 | `…Dispatch_time_rewalk_supersedes_the_old_alias_wait` | Queued task on fable with an unfinished `task:` `QueuedTask` wait registered for fable (`WaitingForHold`, hold on fable); opus free; warm Claude agent; tick. | Rewalk event `fable held → opus … at dispatch`; the fable wait `Superseded` (`rerouted-at-dispatch`); task `Dispatched` in the same tick. |
| V-5 | S1 | `…Routing_blocked_resume_registers_the_chosen_candidate` | Routing-exhausted `Blocked` chain task with an existing unfinished `task:` wait on `(Grok, grok-4.6)`; chain now offers `(ClaudeCode, High)`; tick. | Old wait `Superseded` (`resumed-routing-blocked`); a new `RoutingBlockedTask` wait exists with `ExecutionKind = ClaudeCode`, `RequestedAlias = "opus"`; task `Queued`. Control: existing wait already on `(ClaudeCode, opus)` is kept (same row id, not superseded). |
| V-5b | S1 | `…Cancel_supersedes_the_task_wait` | Queued task with an unfinished `task:` wait that holds the kind's grant; `CancelAsync`. | Wait `Superseded` (`task-canceled`); provider `GrantedWaitId` null. |
| V-6 | S2 | `…Gate_skip_writes_one_Held_event_and_counts` | V-3's control arrangement (same-kind wait, no grant); tick three times. | Exactly one `Held` event whose detail contains `waiting for ClaudeCode capacity` and the wait's short id; `task.CapacityWaitId == wait.Id`; each tick's `SkippedCapacityWait == 1`; `Dispatched == 0`. |
| V-6b | S2 | `…Prior_lease_hold_does_not_mute_the_capacity_trace` | As V-6 with three pre-seeded `Held: repository mutation lease…` events on the task. | The capacity `Held` event is still written (four `Held` events total); a fourth tick adds none. |
| V-6c | S2 | `…Scope_hold_still_traces_once` (guard) | Two Shared tasks with overlapping scope, one Working; tick twice. | One `Held` event for the waiting task (unchanged behaviour under D-5). |
| V-8 | S3 | `CapacityWaitOrphanSweepTests.Session_ended_wait_is_cancelled_and_its_grant_reissued` | Two `LiveSession` waits on ClaudeCode: A (`BlockedAt` older, session `Stopped`), B (session `Running`); `GrantReadyAsync` grants A; then `ReconcileAsync`. | A `Canceled` (`session-ended`); provider `GrantedWaitId == B` after the same reconcile; B unchanged otherwise. |
| V-9 | S3 | `…Terminal_task_wait_is_cancelled` | `QueuedTask` waits for tasks Succeeded, Failed, Canceled, and one whose task row is missing. | All four `Canceled` (`task-terminal` ×3, `owner-missing`); versions incremented once. |
| V-10 | S3 | `…Live_owners_are_kept` (controls) | Waits for: a `Running` session; a `Queued` task; a `Blocked` task; an `agent:` `StandingStart` wait whose agent's session is `Stopped`; a `Working` retained task. | None changes state or version. |
| V-10b | S3 | `…Sweep_is_batch_bounded_and_idempotent` | 150 orphaned waits, `ReconciliationBatchSize = 100`. | First pass cancels 100, second the remaining 50, third changes nothing. |
| V-11 | S4 | `WallRerouteDispatchTests.Redeemed_dispatch_receipts_the_wait_and_the_transcript_progresses_it` | Queued task with a `task:` wait granted for its kind; warm Claude agent; tick (redeems, dispatches); then `ObserveTranscriptAsync(newSession, UserPrompt, seq 1)` and `(TurnEnd, isApiError:false)`. | After the tick: wait `StartAccepted`, `SessionId == LaunchSessionId == task.AgentSessionId`, `DispatchAttemptId` set, `AdmissionCount == 1`. After the prompt: `PromptConfirmed`. After the turn end: `Progressed`. Control: a wait that was not redeemed (task dispatched on an unheld kind with no wait) — no wait is created. |
| V-13 | S4 | `WallRerouteDispatchTests.Receipt_throw_after_launch_leaves_the_task_dispatched` | V-11 `withWait=true` arrangement; dispatcher `DbContext` interceptor throws on the receipt `StartAccepted` save. | Tick `Dispatched == 1`, `Failures == 0`; task stays `Dispatched` with `AgentSessionId` set and `FailureReason` null; wait remains `Admitted` (receipt skipped). |
| V-12 | all | Existing: `ComplexityWallRerouteTests` (12), `CapacityRecoveryTaskTests` (6), `CapacityRecoveryGrantLivenessTests`, `CapacityRecoveryAttentionTests`, `CapacityRecoveryCompatibilityTests`, `CapacityRecoverySupervisionTests`, `RoutingPinCandidateDispatchTests`, `ModelAvailabilityDispatcherTests`, `ApiErrorRecoveryServiceTests`, `AgentTaskReplyIntegrationTests`, Unit lane. | — | Green with the same executed counts as at `9b914298` (record both). Four Unit failures are known pre-existing at base (CARD-0501 commit `9b914298` message); re-run those four at base only if they appear, do not fix them here. |

### Positive controls (Mutation stage; method-scoped filters)

| PC | Mutation | Expected red |
|---|---|---|
| PC-1 | Remove `&& w.ExecutionKind == task.AgentKind` from `FindUnfinishedCapacityWaitAsync` and disable the `RequeueAsync` supersede call. | V-1 (task stays `Queued`), V-3 (stays `Queued`). |
| PC-2 | Disable only the `RequeueAsync` supersede call (kind-scoping intact). | V-1 red (`ended.State == Superseded` and `OutcomeReason == "requeued:Rerouted:attempt-2"`) and V-2 red (same-kind wait still matches). D-1-vs-D-2 isolation rests on V-3 (`Gate_ignores_an_unfinished_wait_on_another_kind`), not on V-1 staying green. |
| PC-3 | Disable the rewalk supersede. | V-4 red (task stays `Queued` behind the fable `WaitingForHold` wait). |
| PC-4 | Disable the resume supersede. | V-5 red (new wait absent; old row still `Grok`). |
| PC-5 | `TraceHeldAsync` returns false whenever the task has any prior `Held` event (the old rule). | V-6b red; V-6 green. |
| PC-6 | Sweep skips the session-ended rule. | V-8 red. |
| PC-7 | Sweep skips the task-terminal rule. | V-9 red. |
| PC-8 | Sweep also cancels `Blocked` owners. | V-10 red (the control). |
| PC-9 | Skip the receipt (`ReceiptDispatchOnAsync` not called). | V-11 red (`Admitted`, `SessionId` null after the tick). |
| PC-10 | Gate skip does not increment `SkippedCapacityWait`. | V-6 red. |
| PC-11 | Remove the `SaveChangesAsync` after `EnsureWaitOnAsync` on the model-hold skip (`AgentTaskDispatcher` `:536-538`). | `WallRerouteDispatchTests.Repeated_model_hold_persists_the_new_wait_without_duplicate_trace` red (the fresh wait is not persisted). |
| PC-12 | Unwrap the receipt `try/catch` so `ReceiptDispatchOnAsync` exceptions fall through to the generic dispatch catch. | V-13 red (task `Failed`, `Failures == 1`, reason starts with `Dispatch failed before a session existed`). |

Zero tests or a build error is not red. Restore each mutation and re-run the same filter green
before the next PC; refresh the restored file's timestamp (CARD-0403 note).

## Risks and how the slices bound them

- **A supersede racing a redemption.** The helper and `RedeemAsync` both take the kind's provider
  advisory lock inside a transaction; whichever commits second sees the other's state (`RedeemAsync`
  refuses on `grant-mismatch` after a cleared grant). V-5b covers the grant-clear half.
- **D-5 changing event counts in unrelated tests.** Detail strings are untouched; the only
  behavioural difference is a second `Held` event when the reason text changes. V-6c guards the
  common single-reason case; the combined run (V-12) is the regression net.
- **The sweep cancelling something live.** Owner sets are explicit allow-lists (D-6 table) and V-10
  holds five controls including the standing-agent shape.
- **`task.AgentSessionId` not populated when `DispatchOneAsync` returns.** V-11 asserts the receipt's
  `SessionId` equals the task's, so a false assumption fails the slice rather than leaking silently.
- **Investigation branch not on master.** The plan links the report at its commit; landing S5 should
  cherry-pick or land `4f07cdff` first so the link resolves on master.

## Follow-ups filed elsewhere, not here

- Bound on zero-admission re-arm cycles (a wait re-armed 1135 times with no admission should
  eventually reach `Exhausted` or attention) — new card.
- The per-tick lease `Held` event and the sibling-base hold's silence — CARD-0535.
- The 0.4 s stale-alias rewalk after a reroute — investigation uncertainty 1; no card until it
  changes an outcome.

## Build evidence (Code task 00222dc7)

Implementation and every specified ordinary V check are complete. All **19 new expanded
cases pass**. Final distinct ordinary coverage is **2,714 cases: 2,700 passed, 13 inherited
failures, one skip**. This is not an all-green result: V-12 remains inherited red.
No deliberate PC has run; ordinary read-only Review is next.

- Original Code task / landing owner: `00222dc7`.
- Branch: `feat/card-task-00222dc7`.
- Exact worktree: `C:\Antiphon\worktrees\card-task-00222dc7`.
- Base: `06889a0afb56309e1348abc5066f8a4ebd0f7d06`. Its production/test code is identical to
  `9b91429801e60cd2df7f701ed17ef78c9656154d`; the diff contains three documentation files only.
- Final tested source/test commit: `c7493cb1a176ddd2851fdafbc3ddaad55f888a66`.
  The final evidence commit changes this section only.
- Full combined I verified `afeb884237e5c4c8774b38ed034abccd2c2fe6e0`; the later commit changes
  only the new receipt test's setup. T reruns that entire class; U runs at the final tested commit.
- Evidence root: `C:\Antiphon\worktrees\card-task-00222dc7\.antiphon\c481-evidence`.
  Raw TRX, logs, parsed JSON, `verification-matrix.json`, `filters.json`, baseline comparisons,
  and SHA-256 `trx-manifest.json` are retained there. `summarize.py` regenerates parsed JSON.
- Restart target: **server**. Code did not land or deploy. Caller retains landing ownership,
  records the companion verification obligation, and commissions SourceLanding Mutation after
  ordinary Review, publication and activation.

### Builds, filters and actual executions

Builds used `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c481/ --nologo`.
Each slice/checkpoint was committed and pushed before its build/test run; sources were frozen
until each owned command finished. The final build was reused for T and U without rebuilding.
All builds passed (final build: 233 existing warnings, zero errors). No timeout or assertion was
loosened. Build logs are `base-build.log`, `s1-build.log`, `s2-build.log`, `s3-build.log`,
`s3-fix-build.log`, `final-build.log`, and `final-fix-build.log`.

Run recipe, using a fresh results directory for each invocation:

```powershell
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c481/ -- --treenode-filter '<filter below>' --report-trx --report-trx-filename '<name>.trx' --results-directory '.antiphon/c481-evidence/<fresh-directory>'
```

- **U**: `/*/*/*/*[Category=Unit]`
- **I**: `/*/*/(WallRerouteDispatchTests*)|(CapacityWaitOrphanSweepTests*)|(ComplexityWallRerouteTests*)|(CapacityRecoveryTaskTests*)|(CapacityRecoveryGrantLivenessTests*)|(CapacityRecoveryAttentionTests*)|(CapacityRecoveryCompatibilityTests*)|(CapacityRecoverySupervisionTests*)|(RoutingPinCandidateDispatchTests*)|(ModelAvailabilityDispatcherTests*)|(ApiErrorRecoveryServiceTests*)|(AgentTaskReplyIntegrationTests*)/*`
- **T**: `/*/*/(WallRerouteDispatchTests*)|(CapacityRecoveryAcceptanceTests*)/*`
- **S1**: `/*/*/WallRerouteDispatchTests/*`
- **S2**: `/*/*/(WallRerouteDispatchTests*)|(RoutingPinCandidateDispatchTests*)|(ModelAvailabilityDispatcherTests*)|(CapacityRecoveryAcceptanceTests*)/*`
- **S3**: `/*/*/(CapacityWaitOrphanSweepTests*)|(CapacityRecoveryGrantLivenessTests*)|(CapacityRecoveryAttentionTests*)|(CapacityRecoveryAcceptanceTests*)|(CapacityRecoveryCompatibilityTests*)|(CapacityRecoverySupervisionTests*)|(WallRerouteDispatchTests*)/*`
- **G**: `/*/*/CapacityRecoveryGrantLivenessTests/*`

| Execution | Tested commit | Expanded outcome | Fresh TRX under evidence root |
|---|---|---|---|
| Base U | `06889a0a` | 2,460: 2,455 pass / 4 fail / 1 skip | `base-unit/unit.trx` |
| Base I, omitting the two new classes | `06889a0a` | 220: 205 pass / 15 fail | `base-int/baseline.trx` |
| S1 | `9bfdfa8e` | 7/7 pass | `s1/s1.trx` |
| S2 | `6cf406ee` | 39/39 pass | `s2/s2.trx` |
| S3 | `75525578` | 57: 51 pass / 6 fixture precision failures | `s3/s3.trx` |
| G, after fixture precision repair | `56860672` | 12/12 pass | `s3-grant-fix/grant.trx` |
| I | `afeb8842` | 239: 229 pass / 9 inherited failures / 1 receipt fixture setup failure | `final-int/c481.trx` |
| T, after receipt setup repair | `c7493cb1` | 28/28 pass: WallRerouteDispatchTests 13, CapacityRecoveryAcceptanceTests 15 | `final-target/target.trx` |
| U | `c7493cb1` | 2,460: 2,455 pass / 4 inherited failures / 1 skip | `final-unit/unit.trx` |

Base I used the exact I expression above with only `(WallRerouteDispatchTests*)` and
`(CapacityWaitOrphanSweepTests*)` omitted. Actual method names and nonzero counts were inspected
in every TRX. Final integration coverage combines I with the newer T result for the repaired
class: **254 distinct cases, 245 pass / 9 inherited failures**. Actual executions, including
superseded setup failures, are recorded separately above and are not relabeled green.

Coverage amendment: `CapacityRecoveryAcceptanceTests` also exercises ReconcileAsync's stalled
admission path, so this named class was added. S2 measured its pre-sweep 15/15 baseline and T
verified its final 15/15. The new sweep needs live owner rows in grant-expiry/admission fixtures;
those fixtures now model unresponsive live consumers rather than orphaned owners. The task owner
is Dispatched so compatibility does not create a replacement episode after MarkProgressed.

Six original grant-class failures came from moving a fake clock backwards to September 8.
The fixture now starts at the current fake instant, normalized to PostgreSQL microseconds;
all relative durations and exact assertions remain. The receipt test's first I execution used
separate clocks for registration and grant, making the wait briefly future-due; T uses one clock.
No production repair was needed for either test setup issue.

### Coverage-to-class counts

| Class | Base expanded outcome | Final expanded outcome / command |
|---|---|---|
| `WallRerouteDispatchTests` | new | 13: 13 pass / 0 fail (T) |
| `CapacityWaitOrphanSweepTests` | new | 6: 6 pass / 0 fail (I) |
| `ComplexityWallRerouteTests` | 11: 9 pass / 2 fail | 11: 9 pass / 2 fail (I) |
| `CapacityRecoveryTaskTests` | 6: 6 pass / 0 fail | 6: 6 pass / 0 fail (I) |
| `CapacityRecoveryGrantLivenessTests` | 12: 6 pass / 6 fail | 12: 12 pass / 0 fail (I) |
| `CapacityRecoveryAttentionTests` | 3: 3 pass / 0 fail | 3: 3 pass / 0 fail (I) |
| `CapacityRecoveryCompatibilityTests` | 4: 4 pass / 0 fail | 4: 4 pass / 0 fail (I) |
| `CapacityRecoverySupervisionTests` | 6: 6 pass / 0 fail | 6: 6 pass / 0 fail (I) |
| `RoutingPinCandidateDispatchTests` | 11: 11 pass / 0 fail | 11: 11 pass / 0 fail (I) |
| `ModelAvailabilityDispatcherTests` | 3: 3 pass / 0 fail | 3: 3 pass / 0 fail (I) |
| `ApiErrorRecoveryServiceTests` | 36: 29 pass / 7 fail | 36: 29 pass / 7 fail (I) |
| `AgentTaskReplyIntegrationTests` | 128: 128 pass / 0 fail | 128: 128 pass / 0 fail (I) |
| `CapacityRecoveryAcceptanceTests` | 15/15 pass (S2) | 15: 15 pass / 0 fail (T) |

The plan estimated 12 ComplexityWallRerouteTests cases; both base and final actually execute
**11**, with the same method inventory. Unit executes 2,460 at both source states.

### Every V / R ID

| ID | Actual ordinary outcome | Shared command |
|---|---|---|
| V-1 | PASS, 1 expanded case(s) | T |
| V-2 | PASS, 1 expanded case(s) | T |
| V-3 | PASS, 1 expanded case(s) | T |
| V-4 | PASS, 1 expanded case(s) | T |
| V-5 | PASS, 2 expanded case(s) | T |
| V-5b | PASS, 1 expanded case(s) | T |
| V-6 | PASS, 1 expanded case(s) | T |
| V-6b | PASS, 1 expanded case(s) | T |
| V-6c | PASS, 1 expanded case(s) | T |
| V-8 | PASS, 3 expanded case(s) | I |
| V-9 | PASS, 1 expanded case(s) | I |
| V-10 | PASS, 1 expanded case(s) | I |
| V-10b | PASS, 1 expanded case(s) | I |
| V-11 | PASS, 2 expanded case(s) | T |
| V-12 | INHERITED RED: existing classes 226 pass / 9 fail, Unit 2,455 pass / 4 fail / 1 skip | I + T + U |

No V-7 or R-n IDs are defined in this plan. The additional ordinary guard
`WallRerouteDispatchTests.Repeated_model_hold_persists_the_new_wait_without_duplicate_trace`
passes once in T. A repeated hold reason cannot suppress persistence of a fresh attempt's wait.

### Remaining inherited failures and timing

Unit's four failure messages match base **exactly** (`unit-baseline-comparison.json`):

- `Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata`.
- `Antiphon.Tests.Application.ScopedVerificationInstructionTests.C487_G142`.
- `Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration`.
- `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G068`.

The first, lane-category and classification-policy failures name the unclassified
HerdrPaneDisposalEndpointTests. The stage-order test expects obsolete Code-to-Mutation wording.
The unchanged skip is AgentTuiSecretProtectorTests.Restored_key_file_symlink_is_rejected_without_mutating_target.

All nine remaining integration failures were executed at unchanged base before implementation
(`integration-baseline-comparison.json`; full assertions in base/final JSON):

- `ComplexityWallRerouteTests.Non_chain_task_fails_on_Fable_5_as_today`.
- `ComplexityWallRerouteTests.Required_pinned_task_is_untouched_on_a_Fable_5_wall`.
- `ApiErrorRecoveryServiceTests.Wall_parks_after_three_deaths`.
- `ApiErrorRecoveryServiceTests.Claude_production_shape_session_limit_uses_AssistantText_not_the_6h_fallback`.
- `ApiErrorRecoveryServiceTests.Session_limit_stub_schedules_one_resume_at_reset_plus_padding`.
- `ApiErrorRecoveryServiceTests.Codex_TurnEnd_text_without_AssistantText_still_parses_session_limit`.
- `ApiErrorRecoveryServiceTests.Empty_wall_adopt_is_repaired_when_a_later_call_supplies_the_real_text`.
- `ApiErrorRecoveryServiceTests.Grok_402_stub_writes_a_fallback_hold_for_grok_4_6_and_never_enqueues`.
- `ApiErrorRecoveryServiceTests.Fable_5_stub_writes_a_fallback_hold_and_does_not_enqueue`.

The two ComplexityWallRerouteTests assertions expect Failed but observe Working. The seven API
recovery failures concern dated reset/fallback timestamps or pre-existing recovery-reason
expectations. Those production paths and assertions were not changed by this card.

Duration tripwire ran on I, T and U: respectively **24, 1 and 15** unlisted >=5-second rows,
all existing test methods; each command exited 1. Both new classes have **zero** >=5-second rows
in final evidence (I/T). Full rows are `tripwire-int.log`, `tripwire-target.log`, `tripwire-unit.log`.
No timeout, assertion or slow-test allowlist was widened.

### Pending Mutation inventory and coverage gaps

Every row below is **pending**, including the indicated comparison/control variants. No deliberate
mutant, red/restore/green cycle or Mutation discovery pass was executed by Code.

| PC | Pending exact-method ordinary target / variants |
|---|---|
| PC-1 | V-1 and V-3, remove kind scope plus requeue supersession |
| PC-2 | V-1 and V-2 intended red; D-1-vs-D-2 isolation is V-3, not V-1 staying green |
| PC-3 | V-4, disable rewalk supersession |
| PC-4 | V-5: old-candidate false arm intended red; already-chosen true arm control |
| PC-5 | V-6b intended red; V-6 unchanged-reason control |
| PC-6 | V-8: Stopped, Failed and missing-session variants |
| PC-7 | V-9: Succeeded, Failed, Canceled and missing-task owner cases |
| PC-8 | V-10: Blocked owner plus all live/excluded-consumer controls |
| PC-9 | V-11: withWait=true intended red; withWait=false no-wait control |
| PC-10 | V-6, omitted SkippedCapacityWait increment |
| PC-11 | Repeated_model_hold_persists_the_new_wait_without_duplicate_trace, remove the hold-skip SaveChangesAsync |
| PC-12 | V-13, unwrap the receipt try/catch |

Noticed gaps for ordinary Review and post-land Mutation discovery:

1. PC-2 expected-red now lists both V-1 and V-2. Cross-kind **dispatch** can still succeed
   under PC-2 (kind-scoping alone); the wait-state assertions in V-1 do not. D-1-vs-D-2
   isolation is V-3, which ignores an unfinished wait on another kind with no requeue.
2. PC-11 covers the extra durable-registration SaveChanges that
   `Repeated_model_hold_persists_the_new_wait_without_duplicate_trace` requires.
3. No planned test forces supersede-versus-redemption races, owner reactivation between sweep
   discovery and cancellation, or a cold-launch/early-transcript/crash cut around the dispatch
   receipt. V-11 uses warm reuse and direct calls to the real transcript observer, as designed.
   V-13 forces the receipt save to throw and keeps the launched task Dispatched.
4. The named retained-return regression class covers counting/claiming and hold registration;
   it does not directly drive the retained-return redemption loop.
5. V-8's new grantee necessarily changes Ready to ActionPending and increments Version. The
   test checks those exact grant effects plus unchanged ownership and admission count; the
   plan's phrase 'unchanged otherwise' must not be read as an unchanged state/version.

### Cleanup and post-land acceptance

All owned commands finished. All **15** producer-owned `bin-c481` directories were removed,
and a final recursive read found zero remaining. `owned-output.txt` and `output-cleanup.json`
record exact paths. Automatic review rejected recursive directory deletion; cleanup completed
through `dotnet clean`, explicit residual generated files, and explicit nonrecursive removal
of verified empty directories. `output-clean.log` / `output-after-clean.json` retain that evidence.
Raw test evidence remains under the evidence root; no source or test edit followed final U/T.

Post-land database census is **pending** caller-owned publication and server activation. Within
two supervisor ticks, record the actual unfinished wait count and confirm every outstanding grant
has a live owner. The plan's 18-to-3 forecast is a dated fleet snapshot, not an assertion against
later live traffic. The three retained Blocked-owner waits may legitimately retain grants.
Do not claim all provider grants must become null. Restart: **server**; original landing owner:
**00222dc7**. Ordinary read-only Review precedes land; all PCs remain post-land obligations.

## Build evidence (Code task 83db9844)

Review-defect repair on `04feae10`. Receipt throw after launch no longer fails the running
task; PC-2 expected-red now lists V-1 with V-2; PC-11 covers the hold-skip `SaveChangesAsync`;
PC-12 covers the new receipt catch. V-13 added. No deliberate PC ran.

- Repair Code task: `83db9844`. Original Code / landing owner: `00222dc7`.
- Branch: `feat/card-task-83db9844`.
- Exact worktree: `C:\Antiphon\worktrees\card-task-83db9844`.
- Tested source: `f54e81d7373c85205a97f83fd1c34d9fab6c3cc7` (`b3793c4d` fix + this plan
  correction). This evidence commit does not change production or tests.
- Evidence root: `C:\Antiphon\worktrees\card-task-83db9844\.antiphon\c481r-evidence`.
- Restart target: **server**. Do not land or deploy from this repair; Review this worktree,
  then land `00222dc7` (or this branch if the caller rebases the owner onto it).

Build: `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c481r/ --nologo`
(0 errors, 233 existing warnings). Same isolated output, `--no-build`, for T then I then U.

| Execution | Filter | Expanded outcome | Fresh TRX |
|---|---|---|---|
| T | `/*/*/WallRerouteDispatchTests/*` | 14/14 pass (includes V-13) | `target/target.trx` |
| I | plan named classes plus `CapacityRecoveryAcceptanceTests` | 255: 246 pass / 9 inherited fail | `int/c481.trx` |
| U | `/*/*/*/*[Category=Unit]` | 2,460: 2,455 pass / 4 inherited fail / 1 skip | `unit/unit.trx` |

I class counts: WallRerouteDispatchTests 14/14; CapacityWaitOrphanSweepTests 6/6;
ComplexityWallRerouteTests 11: 9 pass / 2 fail; CapacityRecoveryTaskTests 6/6;
CapacityRecoveryGrantLivenessTests 12/12; CapacityRecoveryAttentionTests 3/3;
CapacityRecoveryCompatibilityTests 4/4; CapacityRecoverySupervisionTests 6/6;
RoutingPinCandidateDispatchTests 11/11; ModelAvailabilityDispatcherTests 3/3;
ApiErrorRecoveryServiceTests 36: 29 pass / 7 fail; AgentTaskReplyIntegrationTests 128/128;
CapacityRecoveryAcceptanceTests 15/15.

| ID | Actual ordinary outcome | Shared command |
|---|---|---|
| V-1 | PASS, 1 | T, I |
| V-2 | PASS, 1 | T, I |
| V-3 | PASS, 1 | T, I |
| V-4 | PASS, 1 | T, I |
| V-5 | PASS, 2 | T, I |
| V-5b | PASS, 1 | T, I |
| V-6 | PASS, 1 | T, I |
| V-6b | PASS, 1 | T, I |
| V-6c | PASS, 1 | T, I |
| V-8 | PASS, 3 | I |
| V-9 | PASS, 1 | I |
| V-10 | PASS, 1 | I |
| V-10b | PASS, 1 | I |
| V-11 | PASS, 2 | T, I |
| V-13 | PASS, 1 | T, I |
| V-12 | INHERITED RED: I 9 fail, U 4 fail / 1 skip | I + U |

No R-n IDs. Same nine integration failures and four Unit failures as `00222dc7`. The skip is
`AgentTuiSecretProtectorTests.Restored_key_file_symlink_is_rejected_without_mutating_target`.

Tripwire: I 24 unlisted >=5s (existing methods; WallRerouteDispatchTests 0 slow rows in I);
T 1 (cold-start `Grok_wall_reroute_to_Claude_dispatches_on_the_next_tick`); U 39 unlisted
existing methods. No timeout, assertion or allowlist widened. PC-1..PC-12 remain pending.

## Round-2 repair verification design (Code task 7928a7de)

Base: `eec7d4245865a6aae2164f18781e3612d5672f22`. Original landing owner remains
`00222dc7`; this repair is on `feat/card-task-7928a7de` in
`C:\Antiphon\worktrees\card-task-7928a7de`. Restart target: server.

Receipt persistence now detaches only its abandoned wait on failure, before releasing
its provider lock. The task and unrelated dispatcher changes remain usable. The fault
used by V-13 is single-use, so PC-12 can reach the generic failure persistence path.

Additional ordinary coverage (no deliberate mutants in Code):

| ID | Class.method | Required evidence |
|---|---|---|
| V-14 | `WallRerouteDispatchTests.Failed_receipt_cannot_resurrect_a_concurrently_superseded_wait` | Actual receipt save failure, a second context supersedes under the provider lock, then the original dispatcher context persists a warning. Superseded state, outcome, version and empty receipt fields survive; the warning commits and the task stays Dispatched. |
| V-15 | `ReceiptFailureDeliveryTests.Receipt_failure_preserves_complete_brief_acceptance` | Busy=false/true. Real dispatcher and queue; the fake TUI produces UserPrompt only from submitted composer bytes. Eligible recipient accepts inline; busy recipient remains Pending with zero submissions until a real turn-end flush. Whole prompt matches the produced brief. |
| V-16 | `ReceiptFailureDeliveryTests.Receipt_failure_recovers_the_same_queue_row_after_service_recreation` | queue-committed, attempt-committed, prompt-accepted cuts, each combined with failed capacity receipt. EF interceptors cut after insert commit, after Sent/attempt commit before typing, or after accepted prompt before verdict commit. Dispose the service provider, recreate services against the same schema, retain the recipient composer, then invoke the real stranded sweep. Same queue ID/body/sequence, task ID/attempt/session and wait ID/action/admission survive; exactly one complete prompt. Accepted-before-verdict becomes LateConfirmed without another submission. |

Delivery inventory: producer `AgentTaskDispatcher.DeliverReuseMessagesAsync`;
destination the selected warm delegate's session; durable `SessionQueuedMessage`
(`ExecutionTaskId` joins `AgentTask.Id` and `CapacityRecoveryWait.TaskId`, task attempt 3);
recovery `SessionMessageQueueService.FlushStrandedQueuesAsync`; receipt is the complete
matching destination UserPrompt after the queue attempt floor. The failed capacity
receipt has no DispatchAttemptId/LaunchSessionId: tests explicitly keep these absent
instead of claiming that queue acceptance repaired capacity bookkeeping. The real queue
uses a fake protocol adapter, not a live paid provider or a native pty process.

Coverage-to-class: retain all 13 named integration classes from the prior repair and
add `ReceiptFailureDeliveryTests`. Unit remains mandatory. Run all together into one
fresh integration TRX using the documented parenthesized class OR filter, and Unit in
its own fresh TRX against the same `bin-c481r2/` build. No namespace/assembly expansion.
Use an offset over the real clock for queue recovery deadlines, retaining production
attempt/confirmation assertions. The duration tripwire follows both runs.

Pending post-land Mutation additions (all existing PC-1 through PC-12 also remain pending):

| PC | Exact-method target / variants | Deliberate defect and intended red |
|---|---|---|
| PC-13 | V-14 exact method | Remove abandoned wait detachment: unrelated warning save resurrects StartAccepted over Superseded. |
| PC-14 | V-15 exact method, busy=false and busy=true | Skip the reuse brief enqueue: missing producer queue row / complete prompt. Also invert/bypass the queue busy gate: busy=true's zero-submission guard fails; busy=false is the control. |
| PC-15 | V-16 exact method, queue-committed and attempt-committed | Remove delegation-brief discovery from the stranded sweep: pending/interrupted row remains unaccepted after service recreation. |
| PC-16 | V-16 exact method, prompt-accepted | Bypass transcript late-confirm during interrupted-Sent recovery: wrong verdict or duplicate submission, rather than one LateConfirmed prompt. |

These are process-local crash-boundary persistence cuts, not worker-kill custody proof.
The pre-insert reuse crash gap remains a separate existing contract: without a committed
queue row the delivery watchdog fails the task (`AgentTaskDeliveryWatchdogTests`), it
does not reconstruct the lost brief. This repair does not claim automatic pre-insert
replay. Cold-launch/native-transcript ingestion and the prior sweep/redemption race
coverage gaps remain visible to Review and Mutation discovery.

V-17 closes the pre-insert outcome-delivery inventory:
`ReceiptFailureDeliveryTests.Receipt_failure_before_enqueue_reports_the_lost_brief_after_service_recreation`
(busyCaller=false/true) fails the actual brief insert, then the capacity receipt. The
producer leaves Dispatched with no queue row, is disposed, and the recreated dispatcher's
real delivery watchdog runs after its unchanged ten-minute window. It fails the same
attempt with `never delivered`, produces a real caller queue row keyed by SourceTaskId
and root conversation, and the busy/eligible caller receives one complete matching
UserPrompt. The failed task's original wait is canceled by the real orphan sweep.
Thus pre-insert recovery delivers the failure outcome; it does not claim the missing
worker brief was accepted or automatically replay it. No native child is launched.

PC-17 (pending): exact V-17 method, busyCaller=false/true; remove the watchdog's failure
note enqueue. Intended red is missing caller queue row / complete UserPrompt. The busy
arm also protects against premature delivery. Coverage remains in the same new class.
