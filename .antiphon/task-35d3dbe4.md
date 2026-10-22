# Final Review: CARD-1427/1428 worktree-cut fairness (Code task 3b2c2936)

Reviewed: feat/card-task-3b2c2936 at 1d0597291d04efcfcf59bea0ef90e96fa8256c48 (base 705026da8). Verdict: clean, no defects. Landable.

## Receipt (ordinary scope, Final)
Checkpoint tool run `20261022-044257-a844` on server2 (Debian 12, 24 cores), companion plan
`docs/superpowers/plans/2026-10-22-card-1427-1428-worktree-cut-fairness-checkpoints.md`, source 1d0597291 state=clean buildSource=verified, unlisted: none, verdict GREEN exit=0 (first `wait` exits were 75 while running; no 502 or owner-unverified).

| CP | Filter | executed/passed/failed/skipped |
|---|---|---|
| CP-1 | RepositoryLeaseWaitersTests/* | 7/7/0/0 |
| CP-2 | LeaseTurnScenarioTests/C1427_back_to_back_lands_give_a_waiting_cut_its_turn | 1/1/0/0 |
| CP-3 | SiblingInspectionResultsTests/* | 3/3/0/0 |
| CP-4 | DispatcherSiblingInspectionSchedulingTests/* | 47/47/0/0 |
| CP-5 | AgentTaskLandDispatchYieldTests/* | 10/10/0/0 |

`scripts/validate-checkpoint-receipt.ps1 -Rows CP-1..CP-5`: `CHECKPOINT SOURCE VALID source=1d0597291... rows=5`, exit 0.
`scripts/check-evidence-diff.ps1 -BaseRef 705026da8 -HeadRef HEAD`: commits=8 entries=0 violations=0.
`git merge-tree --write-tree origin/master HEAD` (origin/master 673a8c6d7): clean, tree c3a612eb1.
No Unit lane (per brief). PCs stay pending for method-scoped SourceLanding Mutation.

## Throwaway mutations (scratch detached worktree at the tip, build-slot, removed afterwards)
- M1, sticky waiter removed (`ReconcileDispatch(heldOnLease, ready)` without `prerequisitePending` in AgentTaskDispatcher): filter `DispatcherSiblingInspectionSchedulingTests/C142*` 3 run, 2 red:
  `C1427_inspection_pending_keeps_the_lease_waiter` (LeaseRetention.cs:60 "should have single item") and
  `C1428_stale_observation_at_claim_reinspects_and_keeps_the_waiter` (:145, the same assertion). The third case stayed green, as it should.
- M2, a completed result survives Invalidate (`Invalidate` evicts only in-flight entries): filter `/*/*/*/C1428*` 5 run, 3 red:
  `C1428_a_result_survives_repeated_peeks_until_invalidated` (ResultsTests.cs:49),
  `C1428_lease_refusal_reuses_the_inspection_on_the_next_attempt` (:101 Outstanding),
  `C1428_stale_observation_at_claim_reinspects_and_keeps_the_waiter` (:150 "a fresh owned inspection ran"). Even under M2 the stale result was not cut: the claim recheck refused it again.

## Brief checks
1. Sticky waiter. `ReconcileDispatch` keeps a dispatch waiter only when its task is in this tick's `SiblingInspectionPending` set. Membership never registers a waiter (V-2), and `Register` keeps `Since` on a repeat registration. The waiter is still removed: on dispatch by `Clear(claimed.Id)`; on cancel or terminal at the next full tick, because the task is not queued, so it is not in any held set (considered=null); and when held for any other reason. It is never immortal: it lives only while every full tick finds the inspection pending. The per-request land yield budget (`FirstYield`, `MarkExhausted`, reset only by `EndYield`) is unchanged, so each land request yields at most 90 s. `AllowsPreflightTurn` still ages a waiter out after the cap measured from its original Since.
2. DecideYield is a pure extraction. Old and new orders, case by case: max<=0 or IsEmpty gives EndYield and proceed (the caller check is unchanged; DecideYield's own max<=0 branch is unreachable from there); 0 waiters gives EndYield and proceed; budget spent gives MarkExhausted, with the Warning written iff it was the first exhaustion, then proceed; otherwise Yield with the same waiters and first-yield. The only difference is that `now` is now read just before Snapshot instead of just after, which is immaterial. No other land-service code changed, so the CARD-1379 order/replay paths are untouched. CP-5 passes 10/10.
3. Retained results cannot cut from a stale base. A Peek result is advisory. DispatchOneAsync takes the repository lease (HeldOnLease when refused), then under the row lock rechecks the intent (status, repo, no worktree, no repair/SourceLanding, intent equality with a fresh fallback), the inventory metadata and every frozen ref, including the base ref (CardSiblingInspectionService.cs:163) and landing/sibling branch refs. A mismatch is now `SiblingObservationStale`, which invalidates the result and re-enqueues; Invalidate runs before TryEnqueue, so Begin cannot return the stale entry. Invalidate paths: Dispatched, HeldForRemotePrep, NotClaimed, stale, and both exception catches. Peek evicts a changed intent, a skipped or cancelled result, and anything at or past 10 min. Reconcile evicts tasks no longer queued and expired entries. D-4 (Hold or unknown-integration results are consumed by the hold they cause) is sound: only a fresh observation can lift such a hold, which matches the old Take. Memory: entries are keyed by task id, queued membership prunes them, and retained entries are capped at 256 (oldest first). The 32 capacity counts only in-flight entries, so retained results never block admission, and channel occupancy is at most the in-flight count, so TryWrite cannot fail below the cap.
4. The amended CARD-1349 cases are amended, not loosened. Stale-claim Outstanding goes from 0 to 1 because D-3 deliberately re-enqueues the same intent; the "status/task ID" (row lost, NotClaimed) cases still expect 0. "Expiry" now asserts that the result survives 30 s with no git calls and is evicted at the 10-min cap, which is stricter. "Error" adds an explicit Invalidate that mirrors the dispatcher's unknown-integration consume. "Capacity" blocks the workers so that InFlight==32 is asserted, which is stricter. The fence test excludes git commands issued while a worker is active (see A3). Take becomes Peek with the same assertion.
5. Thread safety. SiblingInspectionResults does everything under one lock, including Result, CompletedAt and Done in Complete. The queue takes its `_sync` and then the results lock in TryEnqueue, and Reconcile takes them one after the other, never inverted, so there is no deadlock. RepositoryLeaseWaiters uses `_gate` per call, and DecideYield issues the same separately locked sequence as the old inline code. Yield budgets are per request, and one land worker handles a request.
6. rc/2026-10-20 (tip ceb6e3d6b) contains CARD-1379. Its CardSiblingInspectionQueue, RepositoryLeaseWaiters and CardSiblingInspectionService are byte-identical to base 705026da8. Its AgentTaskDispatcher and AgentTaskLandService differ from base only in unrelated regions (pre-dispatch failure notifications, CARD-1432 land-ref release, the phase histogram), and YieldToDispatchAsync is identical. The change is semantically compatible, and it should reach rc because it fixes the live dispatch latency on the rc-tracking AppHost. Cherry-pick the 8 commits after the master land.

## Advisories (not defects, disclosed)
- A1 (pre-existing, limits CARD-1428's benefit): AgentTaskDispatcher.cs:640 calls `_siblingInspections.Reconcile(queued)` in the nested ready-waiter tick too. That tick runs from inside each full-tick iteration via DispatchReadyWaitersAsync whenever a lease release signalled ready, and its `queued` is the ready subset of 8 or fewer. It evicts retained results and cancels in-flight inspections of every queued task outside that subset. With more than 8 compatible waiters or queued worktree tasks, a refused cut can lose its retained result or have its re-inspection cancelled repeatedly, and its sticky waiter stays registered meanwhile. Impact is bounded: lands still yield at most 90 s per request, and the claim recheck keeps correctness. Suggested follow-up card: reconcile the inspection store only on full ticks (ready is null).
- A2: a sticky waiter is kept while its inspection is pending even when a later pre-claim check (platform, remote prep, capacity wait) would hold it for another reason. This lasts only until the inspection completes, so it is bounded.
- A3: the CARD-1349 fence test (DispatcherSiblingInspectionSchedulingTests.cs ~191) skips recording git commands while `Queue.Active > 0`. That is sound in its single-task sequence, because the re-enqueue follows the claim's return, but it would hide a claim fence command that ran concurrently with a worker.
