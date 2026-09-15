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
| V-12 | all | Existing: `ComplexityWallRerouteTests` (12), `CapacityRecoveryTaskTests` (6), `CapacityRecoveryGrantLivenessTests`, `CapacityRecoveryAttentionTests`, `CapacityRecoveryCompatibilityTests`, `CapacityRecoverySupervisionTests`, `RoutingPinCandidateDispatchTests`, `ModelAvailabilityDispatcherTests`, `ApiErrorRecoveryServiceTests`, `AgentTaskReplyIntegrationTests`, Unit lane. | — | Green with the same executed counts as at `9b914298` (record both). Four Unit failures are known pre-existing at base (CARD-0501 commit `9b914298` message); re-run those four at base only if they appear, do not fix them here. |

### Positive controls (Mutation stage; method-scoped filters)

| PC | Mutation | Expected red |
|---|---|---|
| PC-1 | Remove `&& w.ExecutionKind == task.AgentKind` from `FindUnfinishedCapacityWaitAsync` and disable the `RequeueAsync` supersede call. | V-1 (task stays `Queued`), V-3 (stays `Queued`). |
| PC-2 | Disable only the `RequeueAsync` supersede call (kind-scoping intact). | V-2 red (same-kind wait still matches); V-1 stays green — this pair proves D-2 is load-bearing beyond D-1. |
| PC-3 | Disable the rewalk supersede. | V-4 red (task stays `Queued` behind the fable `WaitingForHold` wait). |
| PC-4 | Disable the resume supersede. | V-5 red (new wait absent; old row still `Grok`). |
| PC-5 | `TraceHeldAsync` returns false whenever the task has any prior `Held` event (the old rule). | V-6b red; V-6 green. |
| PC-6 | Sweep skips the session-ended rule. | V-8 red. |
| PC-7 | Sweep skips the task-terminal rule. | V-9 red. |
| PC-8 | Sweep also cancels `Blocked` owners. | V-10 red (the control). |
| PC-9 | Skip the receipt (`ReceiptDispatchOnAsync` not called). | V-11 red (`Admitted`, `SessionId` null after the tick). |
| PC-10 | Gate skip does not increment `SkippedCapacityWait`. | V-6 red. |

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
