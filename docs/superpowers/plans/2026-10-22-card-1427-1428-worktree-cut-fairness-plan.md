# CARD-1427 + CARD-1428: give queued worktree cuts a turn between lands, and keep the sibling inspection across lease refusals

Source: [docs/investigations/2026-10-21-server2-remote-prep-latency.md](../../investigations/2026-10-21-server2-remote-prep-latency.md)
(ranked fixes 4 and 5). Plan written 2026-10-22 against `121ba4813` (master tip at dispatch).
Verification design is folded in, so the next stage is Code.

## Summary

The card describes the problem as "the land queue has no turn rule". That is not what the code shows. The
turn rule already exists: CARD-0672 D-2 makes a land yield before admission while any dispatch
waits for the lease, for up to `Delegation:LandYieldToDispatchMaxSeconds` (default 90). It did nothing on
2026-10-21 because of the CARD-1428 defect. A queued Worktree task alternates between "lease occupied" and
"same-card base inspection is pending". On every inspection-pending tick, the dispatcher's end-of-tick
`ReconcileDispatch` **deletes the task's lease-waiter registration**, because only tasks refused the
lease on *that* tick count as waiters. The land queue's single worker picks the next land about 1 s
after the previous one releases. If it picks during an inspection-pending tick, it finds no waiters and acquires without yielding.
The 62 s gap at 21:29:23Z was a yield that ended early, when the waiter vanished on the next inspection-pending tick.

So both cards are fixed together:

1. **CARD-1427:** a registered dispatch waiter stays registered while its task's only hold is a
   prerequisite of the cut (the inspection is pending, or the observation went stale at claim). It keeps its original
   `Since`. The land-side yield, its 90 s budget and its FIFO-free re-pick stay as they are.
2. **CARD-1428:** an inspection result is not consumed by being read. It survives `HeldOnLease` and the other
   pre-claim holds, until its intent changes, the claim's own revalidation finds it stale, the task leaves
   the queue, or a 10-minute retention cap expires. Claim-time revalidation under the lease is still the only
   authority. It already exists and is unchanged.

## Code map (file:line at `121ba4813`)

| Piece | Location |
|---|---|
| Lease primitive (OS `landing.lock`, owner registry, release signal) | `server/Infrastructure/Git/RepositoryMutationLease.cs:45-87` (`AcquireAsync`), `:13` (`NotifyReleased` → `RepositoryLeaseWaiters.SignalReady`), `:151-162` (`OwnedLease.DisposeAsync`) |
| Lease contract / purposes | `server/Application/Interfaces/IRepositoryMutationLease.cs:3-52` |
| Waiter registry (dispatch-first turnstile) | `server/Application/Services/RepositoryLeaseWaiters.cs:84-106` (`Register`, keeps `Since` on same purpose), `:124-141` (`ReconcileDispatch`, drops every dispatch waiter not refused this tick), `:51-64` (`TryTakeReady`), `:66-74` (`AllowsPreflightTurn`), `:177-210` (yield budget) |
| Land admission yield | `server/Application/Services/AgentTaskLandService.cs:473-477` (call before the execution and repository leases, `:478-490`), `:1543-1630` (`YieldToDispatchAsync`: empty waiters → `EndYield` + proceed; budget from `FirstYield`; exhausted → one Warning + proceed; else Held `repository_lease_yielded_to_dispatch`), `:591` (`EndYield` at admission), `:435-438` (`EndYield` on terminal) |
| Land queue (one global single reader) | `server/Application/Services/AgentTaskLandQueue.cs:121-162` (`ReadAllAsync` / `TryDequeue`: next request dequeued as soon as the previous returns), sweep `server/Infrastructure/Orchestration/AgentTaskLandSweepHostedService.cs:44-49` (`LandSweepSeconds`, default 5) |
| Dispatcher sibling block | `server/Application/Services/AgentTaskDispatcher.cs:838-882` (`Take` at `:853`; null → `HoldKind.SiblingLanding` + `SiblingInspectionPending` + `TryEnqueue`, `:854-861`) |
| Dispatcher lease refusal | `AgentTaskDispatcher.cs:941-955` (`HeldOnLease` → `HoldKind.Lease`, `RegisterLeaseWaiterAsync` `:1271-1297`) |
| End-of-tick waiter reconcile | `AgentTaskDispatcher.cs:1079-1081`; also `:367`, `:509` (disabled / early return) |
| Lease-release wake | `AgentTaskDispatcher.cs:345-356` (`DispatchReadyWaitersAsync` → `TickCoreAsync(ready)`), called per task at `:637` |
| Claim and claim-time revalidation | `AgentTaskDispatcher.cs:5417-5428` (lease acquire → `HeldOnLease`), `:5482-5511` (intent equality, `CardSiblingInventory` metadata, every `FrozenRefs` SHA; any mismatch → rollback, `NotClaimed`) |
| Hold kinds | `AgentTaskDispatcher.cs:328-332` (`HoldKind`), `:326` (`DispatchOneResult`) |
| Inspection queue | `server/Application/Services/CardSiblingInspectionQueue.cs:44-51` (2 workers), `:55-65` (`Reconcile`, 30 s eviction), `:78-98` (`TryEnqueue`, 32-entry cap counting completed entries), `:100-113` (`Take`: removes the entry, 30 s freshness), `:157-162` (`CompletedAt`) |
| Inspection result and frozen refs | `server/Application/Services/CardSiblingInspectionService.cs:50-58` (`SiblingBaseGuard`), `:149-153` (no sibling rows → no frozen refs), `:157-190` (landing ref, base ref, sibling branch SHAs frozen) |

## Ground truth (card assumption vs code)

| # | Card / investigation assumes | Code does | Consequence for the plan |
|---|---|---|---|
| G-1 | No turn rule: the land queue hands the lease straight to the next land (1427) | A turn rule exists (CARD-0672 D-2). A land yields before admission while `RepositoryLeaseWaiters.Snapshot(common)` is non-empty, for up to 90 s per request (`AgentTaskLandService.cs:1543-1630`) | No new priority queue. Fix the input the rule reads |
| G-2 | Waiters are registered while a cut waits | `ReconcileDispatch(heldOnLease, ready)` (`AgentTaskDispatcher.cs:1079-1081`, `RepositoryLeaseWaiters.cs:124-141`) drops a dispatch waiter on any tick where the task was not refused the lease. Inspection-pending ticks are `HoldKind.SiblingLanding`, so the waiter is deleted, and the next refusal re-registers it with a new `Since` | **Root cause of 1427.** The land sees an empty registry about half the time. The yield ends as soon as the registry empties |
| G-3 | The inspection result expires after 30 s | `Take` removes the entry whether or not the result is still fresh (`CardSiblingInspectionQueue.cs:100-113`), so a result is single-use. The 30 s is a second limit | **Root cause of 1428.** Every lease refusal costs a full new inspection round on two workers |
| G-4 | Keeping the result might let a stale observation cut a worktree | Claim-time revalidation under the lease rechecks intent, inventory metadata and every frozen ref (`AgentTaskDispatcher.cs:5482-5511`), and on mismatch it is `NotClaimed` | Retention is safe. Staleness costs one `NotClaimed` and a fresh inspection, never a wrong cut |
| G-5 | A land moving master invalidates every inspection | Only when the card has sibling rows. `rows.Length == 0` returns with no frozen refs (`CardSiblingInspectionService.cs:149-153`). With siblings, the base/landing ref SHA is frozen and a land invalidates it | Retention removes the round trip in the common no-sibling case. With siblings, the stale-at-claim path has to keep the waiter, which is D-3 |
| G-6 | The lease-release wake gives waiters the gap | `SignalReady` → `DispatchReadyWaitersAsync` runs `TickCoreAsync(ready)`, but those tasks hit `Take` → null (already consumed) → inspection pending → waiter dropped by the reconcile at `:1081` with `considered = ready` | Fixed by G-2 and G-3 together. The wake path is not changed |
| G-7 | Waiter `Since` order is FIFO | `Register` keeps `Since` only for an unbroken registration. Re-registration after a drop resets it, so `AllowsPreflightTurn` (`RepositoryLeaseWaiters.cs:66-74`) and the land's hold text see a reset age | Sticky membership also keeps `Since` honest. `AllowsPreflightTurn` is not changed (R-2) |
| G-8 | The land's lease hold itself is the problem (push 40 s, cleanup 70 s) | True, but that is investigation fix 6 (not this card) | Out of scope. Noted under Not doing |

## Decisions

- **D-1 Fix the turnstile's input, not the lease primitive.** A waiter registered by a lease refusal
  stays registered across a later tick whose only hold for that task is a cut prerequisite.
  `ReconcileDispatch` gains `IReadOnlySet<Guid>? prerequisitePending` (default null = today's
  behaviour). A dispatch waiter in that set is kept untouched, with the same `Since` and the same compatibility
  flag. A task in that set that is *not* already registered is **not** registered, because only a real
  lease refusal registers. *Rejected:* a priority flag inside `RepositoryMutationLease` (the OS lock is
  the authority, and CARD-0672 deliberately kept the turnstile outside the primitive). A fair FIFO lease
  shared by lands and cuts (lands would need to block in-process on a lock they currently probe, which
  changes land replay and hold semantics). Sleeping the land queue one dispatch tick after each land
  (a fixed delay on every land even with no waiter, and still racy against inspection timing).
- **D-2 A distinct hold kind.** Add `HoldKind.SiblingInspectionPending` for the `siblingGuard is null`
  branch (`AgentTaskDispatcher.cs:854-861`) and the new stale-observation branch (D-3). Real sibling
  holds (`siblingGuard.Hold`, `!PendingKnown`) stay `SiblingLanding`, because a task blocked on an unlanded
  sibling is not waiting for the lease and must not hold a land back. The end-of-tick reconcile
  passes `heldThisTick.Where(Kind == SiblingInspectionPending)` as `prerequisitePending`. The hold
  detail strings and queue-reason mapping (`DispatchHoldDetails.ClassOf`) are unchanged.
- **D-3 Stale observation at claim keeps the turn.** `DispatchOneAsync` returns a new
  `DispatchOneResult.SiblingObservationStale` instead of `NotClaimed` when the revalidation at
  `:5482-5511` fails. The tick handles it by invalidating the retained result (D-4), calling `TryEnqueue`
  with the same intent, and recording `HoldKind.SiblingInspectionPending` with
  `DispatchHoldDetails.SiblingInspectionPending`. Any other `NotClaimed` (lost race, released-seat
  check, …) is unchanged.
- **D-4 Inspection results are peeked, not taken.** Extract the per-task entry lifetime from
  `CardSiblingInspectionQueue` into `SiblingInspectionResults` (new,
  `server/Application/Services/SiblingInspectionResults.cs`, pure, with a `TimeProvider`). It has `Begin(intent)`,
  `Complete(taskId, result)`, `Peek(intent)`, `Invalidate(taskId)`, `Reconcile(queued)` and
  `InFlightCount`. The queue keeps its workers, channel and cancellation and delegates entry state
  to the store. `Peek` returns the completed, uncancelled result for an equal intent and does not
  remove it. If the intent differs, the entry is evicted (as today). The dispatcher calls `Invalidate` on
  `Dispatched`, `HeldForRemotePrep`, `SiblingObservationStale`, `NotClaimed` and the failure catch, so a
  result is reused only by a task that is still pre-claim. *Rejected:* keying freshness on observed
  ref SHAs read on the tick, because the tick runs no git by design (CARD-1349), and claim-time revalidation already
  does that check under the lease.
- **D-5 Retention bound.** A completed result is valid for at most `RetainedResultMaxAge` = 10 minutes
  from completion (a constant in the store, not a setting). After that, `Peek` evicts it and returns null, and
  the dispatcher re-enqueues as it does today. 10 minutes covers about 2 typical lands (165-543 s) and bounds how old
  the advisory sibling *warnings* can be. Their correctness is already pinned by metadata and frozen-ref
  revalidation. *Rejected:* keeping the 30 s limit (that is the defect). No cap at all (a task parked
  behind a long outage would reuse an hour-old warning text).
- **D-6 Capacity counts in-flight work only.** `TryEnqueue`'s 32-entry refusal counts entries that are not
  completed (`InFlightCount`). Completed retained results are bounded separately at 256, and the oldest completed
  entry is evicted first. Without this, retention would push the 33rd queued worktree task into
  `SiblingInspectionCapacity`.
- **D-7 Land side unchanged.** `YieldToDispatchAsync`, the 90 s per-request budget (`FirstYield` /
  `MarkExhausted` / `EndYield`), the Held code `repository_lease_yielded_to_dispatch`, the one
  exhaustion Warning, the single global land reader and the sweep re-pick are all unchanged. The
  scenario witness (V-3) drives them through an extracted pure decision (D-8), so no land-service
  DB harness is needed.
- **D-8 Extract the land's yield decision.** Move the pure part of `YieldToDispatchAsync`
  (`:1546-1575`) into `RepositoryLeaseWaiters.DecideYield(string common, Guid requestId,
  DateTimeOffset now, int maxSeconds)` → `LandYieldDecision { Proceed, Yield, ProceedExhausted }` plus
  the waiter list. The land service keeps the DB writes (Held row, Warning) and calls the decision.
  This is a behaviour-preserving refactor, pinned by the existing `AgentTaskLandDispatchYieldTests` (CP-5).

### Bounds (no starvation either way)

- **Land delay:** at most `LandYieldToDispatchMaxSeconds` (90 s) per land request per admission.
  The budget starts at the request's first yield and is never reset by waiter churn. Sticky
  waiters make yields last up to the bound more often, and never past it. Worst case for a queue of N lands
  is N × 90 s of extra wall time, and only while cuts are actually waiting. That is the CARD-0672 trade-off, already documented.
- **Cut delay:** a cut that registered while land L(k) held the lease is admitted before L(k+1) once the
  release wake (or the next tick) finds a valid retained result (no siblings), or a fresh inspection that finishes inside
  L(k+1)'s 90 s yield. If the inspection overruns 90 s, L(k+1) proceeds with its one Warning, and L(k+2)
  yields with a fresh budget. So a cut waits at most one land plus one yield in the normal case, and never
  indefinitely.
- **Many cuts:** the land yields until the waiter set is empty or 90 s. About 15 s per cut means about 6 cuts per
  yield window. Any left over go in the next land's window.

### Interaction with land replay and ordering (docs/orchestration-loop.md §land, CARD-0641/0672)

- The yield runs only **before admission** (`:475`, before the execution lease). An admitted attempt,
  a CARD-1379 reacquisition inside an admitted attempt, and a replay of an already-admitted
  operation never yield. Admission ends the budget (`:591`), so a retried request yields afresh, as
  documented. Nothing here changes that.
- The land queue's position reporting (one global reader, one-based waiting positions, `awaiting
  replay` for durable requests not yet replayed) is unchanged. A yielding request is Held and
  re-picked by the sweep. Among several *yielding* lands, the first to be re-picked after the waiters
  clear is admitted. That is existing CARD-0672 behaviour, and correctness does not rest on it, because every land
  rebases onto the target and fast-forwards under the lease. Code must not add ordering logic. If Code
  finds that the sweep re-enqueue changes a documented queue-position guarantee, it reports that as a finding and does not fix it.
- Held notes: the yield is not a caller note (CARD-0641 anchor unchanged). Longer yield episodes
  write no extra Held events, because they are deduplicated on the reason code.
- Session invariants: no session waits for input in this change, and no session lifetime changes. Only
  the queued-task dispatch hold changes. The parking/release checklist (CARD-1083) does not apply.
- Restart: both registries are in memory. After a restart they start empty and are re-armed by the next
  refusal and the next inspection, exactly as today (CARD-0672 note in `RepositoryLeaseWaiters.cs:18-20`, CARD-1349
  "durable Held precedes advisory enqueue").

## Slices

| Slice | Card | Files | Tests | Commit shape |
|---|---|---|---|---|
| S1a | 1427 | `RepositoryLeaseWaiters.cs`: add the `prerequisitePending` parameter (ignored) and `DecideYield` (D-8, extracted). `AgentTaskLandService.cs` calls `DecideYield` | new `tests/Antiphon.Tests/Application/RepositoryLeaseWaitersTests.cs` cases `C1427_prerequisite_pending_keeps_a_registered_waiter_and_its_since`, `C1427_prerequisite_pending_never_registers_a_new_waiter`; new `tests/Antiphon.Tests/Application/LeaseTurnScenarioTests.cs` `C1427_back_to_back_lands_give_a_waiting_cut_its_turn` | `test(CARD-1427): red witnesses …`. The two keep/since cases and the scenario are red, and `never_registers` is green (it guards against over-correction) |
| S1b | 1427 | `RepositoryLeaseWaiters.ReconcileDispatch` honours `prerequisitePending`. `AgentTaskDispatcher.cs`: `HoldKind.SiblingInspectionPending` (D-2), end-of-tick reconcile passes it (`:1079-1081`; the `:367`/`:509` early returns pass none) | as S1a + `C1427_inspection_pending_keeps_the_lease_waiter` (dispatcher, D-2) | `feat(CARD-1427): …` |
| S2a | 1428 | new `SiblingInspectionResults.cs` with **today's** semantics (consuming read, 30 s); `CardSiblingInspectionQueue` delegates to it | new `tests/Antiphon.Tests/Application/SiblingInspectionResultsTests.cs` `C1428_a_result_survives_repeated_peeks_until_invalidated`, `C1428_retained_result_expires_at_the_cap_not_at_30s`, `C1428_completed_results_do_not_consume_enqueue_capacity` | `test(CARD-1428): red witnesses …` (red), with the existing sibling scheduling tests still green (CP-4) |
| S2b | 1428 | store semantics per D-4/D-5/D-6; queue `Take` → `Peek` + `Invalidate`; `AgentTaskDispatcher.cs` `:853` peek, `DispatchOneResult.SiblingObservationStale` (D-3), `Invalidate` on the outcomes named in D-4 | as S2a + dispatcher `C1428_lease_refusal_reuses_the_inspection_on_the_next_attempt`, `C1428_stale_observation_at_claim_reinspects_and_keeps_the_waiter` | `feat(CARD-1428): …` |
| S3 | both | `docs/orchestration-loop.md` §1318 and §1666-1671: waiters persist across inspection-pending and stale-observation holds, and inspection results are reused until stale at claim or 10 min. `docs/investigations/…` is not edited | none (docs) | `docs(CARD-1427,1428): …` |

Order: S1a → S1b → S2a → S2b → S3. S1 and S2 touch different files except `AgentTaskDispatcher.cs`
(different regions: `:854-861`/`:1079-1081` vs `:853`/`:941`/`:5482-5511`). Keep them sequential in one task.

## Verification design

- **V-1 (1427 unit):** after `Register(common, t, dispatch, T0)`, `ReconcileDispatch(stillHeld: {},
  considered: [t], prerequisitePending: {t})` leaves exactly one waiter with `Since == T0`. A second
  reconcile without `t` in either set drops it. Red at S1a because the parameter is ignored.
- **V-2 (1427 unit, guards against over-correction):** `prerequisitePending: {u}` for an unregistered `u` registers nothing.
  `Snapshot` stays empty and `IsEmpty` stays true.
- **V-3 (1427 scenario, fake lease + fake clock):** `LeaseTurnScenarioTests`. It uses `FakeTimeProvider`, an in-memory
  single-holder `FakeRepositoryLease : IRepositoryMutationLease` (one common dir, acquire returns null while held),
  a real `RepositoryLeaseWaiters`, and a land model that calls `DecideYield` before `TryAcquireAsync` and
  holds for 180 s. The dispatcher model ticks every 10 s for one cut task. Its inspection alternates
  pending/ready exactly as observed (pending on the tick after each refusal). Ready-tick plus lease
  refused → `Register`. At the end of each tick it calls `ReconcileDispatch` with the lease-refused set and the
  inspection-pending set. Four lands are queued back to back, each re-picked 1 s after the previous release
  and then every 5 s (LandSweepSeconds). **Assert:** the cut acquires the lease before land 2 is admitted,
  i.e. within one land interval of registering (by T0 + 180 s + 90 s). Land 2 is admitted at most 90 s after
  land 1 releases. All four lands are admitted (no land starvation). Red at S1a: land 2 finds an empty
  registry on an inspection-pending tick and acquires at +1 s, and the cut waits behind all four.
- **V-4 (1427 dispatcher):** `C1427_inspection_pending_keeps_the_lease_waiter` in a new partial
  `tests/Antiphon.Tests/Application/DispatcherSiblingInspectionSchedulingTests.LeaseRetention.cs` on
  `SiblingInspectionFixture` (scratch repo, fake clock), with `configure` replacing `IRepositoryMutationLease`
  by a refusing fake. Tick 1 (result ready, lease refused): the waiter is registered at T1. Advance 31 s
  (pre-S2b the result is gone, so the next tick is inspection pending), then tick 2. The waiter is still registered with
  `Since == T1`. This is the dispatcher-side proof of D-2, and it stays green after S2b, because a forced
  `Invalidate` before tick 2 reproduces the pending state.
- **V-5 (1428 unit, fake clock):** a result completed at T0 is returned by `Peek` at T0+1 s, T0+60 s and
  T0+9 min (three peeks, same instance). It is null after `Invalidate`, and null at T0+10 min (evicted). An
  intent change evicts it. Red at S2a, because a consuming read returns null on the second peek and at
  60 s.
- **V-6 (1428 capacity):** 32 completed results plus one new intent gives `TryEnqueue == true`. 32 in-flight
  entries plus one gives false. Red at S2a.
- **V-7 (1428 dispatcher):** `C1428_lease_refusal_reuses_the_inspection_on_the_next_attempt`. On the fixture,
  with the fake lease refusing on attempt 1 and granting on attempt 2, the worker inspection count is 1
  (`Git.Calls` inspection commands are not repeated) and the second tick claims and cuts.
  `C1428_stale_observation_at_claim_reinspects_and_keeps_the_waiter` moves the frozen base ref between
  inspection and claim (commit on the scratch master inside `onLease`). The result is
  `SiblingObservationStale`, a new inspection is enqueued, and the waiter is kept.
- **V-8 (no regression):** the existing CARD-0672 yield tests, CARD-1349 sibling scheduling tests and
  `RepositoryLeaseWaitersTests` stay green.

### Red witness (S1a / S2a commits, not checkpoint rows)

Code runs V-1+V-3 at S1a and V-5+V-6 at S2a, and reports each as failing for the stated reason before the fix
commit (git-common: commit the failing test before its fix). A build error or zero tests is not
red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1b | `tests/Antiphon.Tests -> bin-c1427/` | waiters-unit | `/*/*/RepositoryLeaseWaitersTests/*` | V-1, V-2, V-8 | all passed (5 existing + 2 new = 7), 0 failed/skipped | 7 | 3 | lane: server2 (default) |
| CP-2 | S1b | CP-1 | lease-turn-scenario | `/*/*/LeaseTurnScenarioTests/C1427_back_to_back_lands_give_a_waiting_cut_its_turn` | V-3 | 1 passed, 0 failed/skipped | 1 | 2 | lane: server2 (default) |
| CP-3 | S2b | `tests/Antiphon.Tests -> bin-c1428/` | inspection-results-unit | `/*/*/SiblingInspectionResultsTests/*` | V-5, V-6 | 3 passed, 0 failed/skipped | 3 | 3 | lane: server2 (default) |
| CP-4 | S2b | CP-3 | dispatcher-sibling-lease | `/*/*/DispatcherSiblingInspectionSchedulingTests/*` | V-4, V-7, V-8 | all passed (existing CARD-1349 cases + 3 new), 0 failed/skipped | 3 | 8 | lane: server2 (default) |
| CP-5 | S2b | CP-3 | land-yield-regression | `/*/*/AgentTaskLandDispatchYieldTests/*` | V-8 (D-7, D-8 refactor) | all passed (existing CARD-0672 cases), 0 failed/skipped | 6 | 8 | lane: server2 (default) |

CP-4's Min is the new-test floor. Code reports the real total. CP-1 and CP-2 re-run at S2b is not
required: S2 does not touch `RepositoryLeaseWaiters`. If it does, re-run CP-1 and say why.

## Risks

- **R-1 Longer land yields.** With sticky waiters, lands yield for the full budget more often. That is bounded at 90 s
  per request and already documented. If an operator sees "yield budget exhausted" Warnings on every land,
  that means cuts are taking more than 90 s (desktop ref bloat, investigation fixes 2 and 3), not that this change is wrong.
- **R-2 `AllowsPreflightTurn` ages.** Sticky `Since` means a waiter older than 90 s stops having FIFO
  priority for CARD-1379 preflight turns (`RepositoryLeaseWaiters.cs:71-72`), where before it was kept "young" by
  re-registration. That is the documented rule applied honestly, and it is not changed here. Review should confirm that no
  preflight test relied on the reset.
- **R-3 Retained warning text.** Sibling *warnings* on a cut may be up to 10 minutes old. The metadata and
  frozen-ref revalidation make them correct for the claimed state, so only the prose can be old.
- **R-4 Stale-observation loop.** With siblings, every land invalidates the frozen base ref, so a cut can
  hit D-3 once per land. That is bounded by the land count and is the same cost as today, minus the lost waiter.

## Not doing (investigation items left to other cards)

Fix 6 (shorten the land's lease hold), fix 7 (release the lease before baseline capture), fix 8 (hold
attribution, a `siblingInspection` queue reason, Information-level timing) and operational fixes 1-3
(pack-refs, gc) are out of scope. Fix 8 would make this change easier to observe live. The orchestrator may
want to file it.
