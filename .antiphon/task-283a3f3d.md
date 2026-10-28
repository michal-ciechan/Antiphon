# Review 283a3f3d: supervisor tick tracker clear (subject 28c5ff1a)

Reviewed source: feat/card-task-28c5ff1a at d75b70029e4f3e16eba21ec478572b32c2b4310e (witness + sweep order 3bc232253f6f411249ee67cdc9b5a26c3eeb1cc2, detach d75b7002).
The subject's actual parent is 68565db92359eadea14180c745c0a28711a36872, an rc ancestor 19 commits behind the brief's base 34a7d0b8f. The author report `.antiphon/task-28c5ff1a.md` is not on the subject branch.
Platform: server2/Linux. Desktop/Windows was not reachable, so the Windows qualification failure has not been re-run on Windows.

## Verdict

The fix is correct and I found no regression in 226 supervisor-tick tests. One test-adequacy defect (D-1) needs a small test-only Code slice.

## (1) ChangeTracker.Clear() scope

Clear() is acceptable here.
- When Clear runs in the TickAsync catch, every earlier agent has already finished its SaveChanges and Commit (CompleteAsync or explicit saves). The failed agent's `await using` transaction has already been disposed and rolled back. So the only tracked entries left are the failed agent's Modified/Added rows. They are exactly what the next agent's SaveChanges would replay. I confirmed that replay on Linux: the next agent's save failed.
- The context is effectively tick-scoped with re-query per agent. SuperviseAsync starts with StartRefusalAsync (all AsNoTracking), then `HerdrSupervisionStateService.ObserveAsync` (agent read AsNoTracking, state re-queried and reloaded), then `_db.Entry(agent).ReloadAsync`. Reload on a detached list instance queries the store values and sets the entry Unchanged, which re-attaches it. Nothing holds an Agent or state reference across iterations except the `agents` list, and its entries are reloaded before use.
- Identity-conflict risk: the only tracked Agent loads in the supervision path are LockAgentAsync (AgentControlService:177/549/1022/1264) and the start path at AgentControlService:1110. Both load the agent being supervised, which is already re-attached by then. No path tracks a different, later AlwaysOn agent, so a later agent's Reload cannot hit "another instance with the same key".
- Partial multi-entity save: EF keeps every entry in its pre-save state when SaveChanges throws. Clear detaches all of them, so nothing is left half-detached.
- Shared scope: after TickAsync, the hosted service runs PruneIncidentsAsync (ExecuteDelete only, independent of the tracker). Same-scope work before the tick (ModelAvailability.SweepExpiredAsync and the specialist reconcilers) saves its own changes.
- Precedent: the class already calls `_db.ChangeTracker.Clear()` in its fallback paths without a scope factory (RecordStartFailureInFreshScopeAsync, the StartSupervisedRestartAsync refusal path). This is the first Clear on the outer tick context in production, which has a scope factory.
- A context per agent would be cleaner but is a much larger change. Clear in the catch is a proportionate fix.

## (2) Sweep order (CreatedAt, Id)

- Previous order: unspecified Postgres heap order. No caller, test or S1/S3 admission code depends on it. The 226 tests below are green.
- No starvation: TickAsync visits every AlwaysOn agent on every tick, and there is no per-tick budget. Capacity grants are chosen by CapacityRecoveryService.ReconcileAsync, not by sweep order. Order only changes the sequence within a tick.
- The fix does not need the ordering. The witness needs it to be deterministic, because it can only go red when the failed agent comes before a later agent in the same tick. Commit 3bc23225 says so: "Oldest-first order makes that later row deterministic".
- The cost is negligible (a few AlwaysOn rows), and the idiom matches PipelineHandoffActionService:109. I judge it acceptable as a determinism change that enables the witness, not an unneeded product change. Removing it would leave the witness unable to go red reliably.

## (3) Interaction with S3 Round A (a8eb1f8b)

- The brief's premise does not hold. a8eb1f8b does not touch TickAsync, neither the agents query nor the per-agent catch. Its Clear() calls are in SuperviseAsync's capacity-wait catches (capacity_consume_refused, held, refused codes, transient, generic) and in TryHandleCapacityWaitAsync/StartSupervisedRestartAsync.
- I ran a read-only three-way `git merge-file` in scratch (base 68565db92 = master for both files, ours = a8eb1f8b, theirs = d75b7002). AgentSupervisorService.cs and StandingRestartAccountingTests.cs both merged with 0 conflicts.
- A double clear can happen when a peer inner catch clears and then throws to the outer catch (for example RecordStartFailureInFreshScopeAsync throwing). Clear on an empty tracker is idempotent, so it is harmless.
- The merge needs no manual resolution. Re-run the peer's supervisor classes together with this change after both land.

## (4) Witness

- Red at 3bc23225: `fault.BookkeepingHits should be 2 but was 4` (StandingRestartAccountingTests.cs:210). This is the same signature as Windows run 28bbb58e. The original Failed_outcome_save test was green at 3bc23225.
- Green at d75b7002, 6 runs in total (run 1 plus 5 repeats), each run executing both the witness and the original test: 12/12.

## Runs (all through scripts/build-slot.ps1, UseAppHost=false builds, `dotnet exec` of the built dll)

| Run | SHA | Selection | Result |
|---|---|---|---|
| build tip | d75b7002 | Antiphon.Tests, bin-rv283tip/ | 0 errors, 4m41s |
| run1 | d75b7002 | StandingRestartAccountingTests 22, StandingRestartEscalationTests 52, AgentSupervisionTests 10, CapacityRecoverySupervisionTests 6 | 90/90 passed, 1m56s |
| run2 | d75b7002 | HerdrSupervisionBackoffTests 35, StandingBootAttentionTests 30, StandingBootStatementBudgetTests 11, StandingBootWatchdogTests 31, HerdrAlwaysOnChannelParityTests 26, RcRosterRepairTests 3 | 130 passed, 0 failed, 6 skipped (inherited runtime `Skip.Test` on Linux, e.g. CARD-0865), 1m41s |
| build red | 3bc23225 | Antiphon.Tests, bin-rv283red/ | 0 errors, 3m01s |
| red | 3bc23225 | witness + Failed_outcome_save | 1 failed (witness, 4 vs 2), 1 passed |
| repeat x5 | d75b7002 | witness + Failed_outcome_save | 10/10 passed |
| evidence diff | 68565db92..d75b7002 | check-evidence-diff.ps1 | commits=2 entries=0 violations=0 |

The whole Unit lane was not run (Interim scope, per the brief). PCs stay pending for SourceLanding Mutation. I removed all 66 bin-rv283* directories with a guarded delete confined to the worktree root.

## Defects

### D-1 (adequacy): the witness cannot go red if the later agent breaks after the clear

- Where: tests/Antiphon.Tests/Application/StandingRestartAccountingTests.cs:189-213, Failed_bookkeeping_save_is_not_repeated_for_a_later_always_on_agent.
- Failure: the test only asserts the fault's hit count on the failed subject. It never asserts that the later (bystander) AlwaysOn agent was actually supervised. TickAsync's catch swallows any exception from the bystander, and the interceptor counts only the subject. So the witness stays green if Clear() makes the bystander's supervision throw on every tick, for example a detached-instance Reload or an identity conflict.
- Why: protecting the later agent is the whole point of the fix. The re-attach of detached list instances is the new regression surface in production, which has never cleared the outer tick context before.
- Fix (test-only): after the two ticks, open a fresh context and assert the bystander's bookkeeping committed. For example, `AgentSupervisionStates.FindAsync(later.Id)` exists with `NextRestartAt != null`, and/or exactly one `RestartScheduled` incident for `later.Id`. Before the fix the bystander's save is refused by the interceptor, so the assertion is red at 3bc23225 and green at d75b7002. Commit it red-first per git-common, or show it red against 3bc23225's product code.

## Advisories (non-blocking)

- A-1: V-05 in the card-0466 plan says "Do not require an exact durable failure count during the database outage". The original test's exact `BookkeepingHits.ShouldBe(2)` is stricter than the plan. It was exposed because every test's TickAsync sweeps every AlwaysOn agent in the shared test DB, including other tests' agents. After the fix it is consistent, but cross-test AlwaysOn rows still pass through each test's tick.
- A-2: re-run the Windows qualification class on Windows. The failure was Windows-only, and this review is Linux-only.
- No changed input waits (CARD-1083 not applicable).
