# CARD-1593 S3 Round A Review (f36dda07): supervisor atomic consume

Clean, with no regression and no new fail-open. Reviewed `feat/card-task-a8eb1f8b` at
`8a8b9bde3d10e3170f86a915611820f53c03b829`. The branch was cut from `fe73cfd2eed86de506a6a307e870c44b05b94451`,
not from 86d21b50 as the brief says, and it contains 13 commits. Scope was **Interim**: the whole Unit lane was not run, per the brief.

The author's report is in their worktree (`/work/worktrees/task-a8eb1f8b/.antiphon/task-a8eb1f8b.md`), not committed on the branch.

## Runs

There was one build at the tip, `bin-rf36` (lease a37fed2d, 4m50s, 0 errors, UseAppHost=false). Each run below is `dotnet exec --no-build` inside `scripts/build-slot.ps1`. The checkpoint tool was not used.

| Run | Lease | Classes (count) | Result |
|---|---|---|---|
| tip-1 | 7c1e4945 | StandingRestartEscalationTests 52, AgentSupervisionTests 10, StandingRestartAccountingTests 21, CapacityRecoverySupervisionTests 23, CapacityLockOrderTests 17, CapacityAdmissionLifecycleTests 28 | 151/0/0 skipped, 4m05s |
| tip-2 | be562cda | AgentControlServiceIntegrationTests 33, AgentTaskDispatchBaseGuardTests 97, CapacityRecoveryQueueTests 8, CapacityRecoveryTaskTests 6, ReceiptFailureDeliveryTests 21, WallRerouteDispatchTests 15 | 180/0/0, 1m29s |
| tip-3 (start-path regression, my choice) | fa6685fb | AgentStartRecoveryTests 4, HerdrSupervisionBackoffTests 35, SpecialistStartIntentTests 4, StandingContinuityRecoveryTests 11, StandingSessionOwnershipTests 5, StandingSessionRecoveryHttpTests 1, StandingSessionSelectionTests 39, StandingSessionSwitchConcurrencyTests 24 | 120 passed / 3 failed |
| red (pre-product `cd65d33c9`, build dc9abb7d) | dfba1752 | CapacityRecoverySupervisionTests, CapacityLockOrderTests, AgentStartRecoveryTests | 26 passed / 18 failed |

These runs reproduce the author's checkpoint counts: CP-1 45 (28+17), CP-2 36 (21+15), CP-3 97, CP-4 23 and CP-5 62. CP-6 is the supervisor-start case in CapacityLockOrderTests, and it is green.

The 3 tip-3 reds are inherited Linux reds by construction. They are `AgentStartRecoveryTests.Interactive_start_failure_marks_agent_failed_not_working` and `Observed_exit_closes_session_and_resets_agent` (0 and 1). Each fails with `Agent executable 'cmd.exe' was not found on PATH`, raised from `AgentExecutableResolver.cs:95` before any changed code, and the same 3 fail identically at the pre-product commit.

The witnesses were red before the product change. At `cd65d33c9` (the tests plus S3a, but not S3b) all 18 rows the plan calls "red at base" failed. Three rows passed there, and the plan labels each of them a guard: V-S3-5 `start_throw_charges_one_attempt`, V-S3-5b `not-committed`, and V-S3-11 `supervisor-start`. The author's positive controls back those three rows, but those controls stay pending for the method-scoped SourceLanding Mutation.

Existing tests were not weakened. The only edits to existing test files are an optional `configure` callback in `AgentSupervisionTests.cs:587,678` and `partial` on `CapacityRecoverySupervisionTests.cs:19`. `CapacityLockOrderTests.cs` adds lines only. Card0412_D8, C510_S2_*, V17, V16 and V18 are unedited and green.

`scripts/check-evidence-diff.ps1 -Repository . -BaseRef fe73cfd2e… -HeadRef 8a8b9bde3…` exited 0: 13 commits, 0 entries, 0 violations.

## Priority findings

**(1) Lock order and IO.** The supervisor tick disposes its transaction (`AgentSupervisorService.cs:231-232`) before `TryHandleCapacityWaitAsync`, so it holds no lock while it enters the start.

The reservation (`AgentControlService.cs:564-570`) takes locks in this order: session rows `FOR UPDATE` (rank 4), the agent row (rank 5), its own writes and the session save, then the consume. The consume (`RedeemOnAsync`, `:206`) takes the provider advisory xact lock (rank 7) and writes the wait and provider rows (rank 8), and then the transaction commits. That is monotone against the S1 table.

The runner call `ListAsync` (`:549`), the composition and the Herdr resolve all run before `BeginTransactionAsync`. The enqueue runs after the commit. `LandDeliveryBoundary` is a production no-op singleton (`LandDeliveryBoundary.cs:6`, `Program.cs:493`). No runner or network await sits inside the reservation.

The other new transactions are also safe. `ConsumeCardAdmissionAsync` runs after an autocommit save with no ambient transaction (`:347-348`). `ReleaseAgentReservationAsync` and `ChargeFailedStartAsync` each run in their own scope after the reservation is gone, taking the provider lock and then the wait rows. Placing the consume late is what makes the S1 order hold: the author's early-placement mutant hung because it took rank 7 before rank 4. The committed placement passed V-S3-11 (agent row free of a provider key; COMMIT is the only command after the provider UPDATE).

**`CommandTimeout = 0` is only conditionally safe (A-1).** The reread (`AgentControlService.cs:1047`) runs only on the gated S-4 path. That means a capacity start whose COMMIT threw.

- It waits without a bound on the agent row. Its only cancellation is the hosted `stoppingToken`.
- It still holds the involved sessions' in-process queue locks while it waits. They are released in the `finally` at `:994`.
- While it waits, the whole serial supervisor loop stalls: capacity reconcile, compaction, api-error and the watchdog sweeps (`AgentSupervisorHostedService.cs:146-241`).
- Under the no-lock-across-IO invariant, the holder is the in-doubt backend itself, which finishes. So this is not a regression or a fail-open.
- The remaining risk is a commit that never finishes, or a holder of the agent row that waits on one of those queue locks. Either would be an indefinite hang where base threw after 30 s.

Recommendation: a bounded timeout, for example 2-5 minutes. That still outlasts the 30 s stand-in, and a timeout falls back to S-3 safely, because the charge refuses `not-action-pending` if the commit really happened.

**(2) Exactly-once.** I walked every exit:

- **S-0 skips.** The early returns run before any consume: launch-queue owned (`:205`), live resume (`:578`), duplicate key, and compaction block. The rollback releases everything, and nothing is spent.
- **S-1** (`capacity_consume_refused`). Neither the pre-check (`:640-650`) nor the consume (`:212-213`) writes anything, and the reservation rolls back. The outer catch (`AgentSupervisorService.cs:240-244`) neither charges nor releases.
- **S-2 and S-2h.** These release the wait with the count unchanged.
- **S-3.** It records the failure, then charges once. A throw after the consume committed (in a later save, an event publish, or the fresh alert) makes the charge refuse `not-action-pending`, so there is no double spend.
- **S-T** (40P01/40001/55P03). Everything rolls back, nothing is charged, and the next tick retries.
- **S-4 gated.** Committed means one enqueue. Not committed means the exception is rethrown to S-T or S-3.
- **S-0c card.** The consume runs after the spawn and the pointer save. A swallowed failure is logged as a Warning and leaves the wait ActionPending with its grant, which expires through D-12. A re-spawn is possible only if the card session dies inside the grant TTL, and then it costs at most one extra admission.

I found no spend without a launch other than the S-3 charge, which is intended; no launch without a spend on the gated arms; and no wait stranded Admitted or Reserved.

The ungated S-4 narrowing (`f60a8dbe`, `:222`) is **acceptable**. An ungated commit throw does exactly what base did at `:166-169`: it propagates, nothing is enqueued, and the streak increments, which the C510 sync-after-commit pin requires. That is a missing improvement, not a regression. A committed-but-unacked ungated start still leaves a pointer that is not enqueued, as base did.

**(3) The departures.**

- **Pre-check mismatch as S-1.** Correct per D-S3-1. It leaves the wait for grant reconcile rather than cancelling it.
- **`standing_start_intent_revoked` on the S-2 list.** It is thrown only when the intent is Suspended or Liveness-latched. After the release, the next tick stops at `AgentSupervisorService.cs:154` (Suspended or Latched means `CompleteAsync(false)`), so there is no loop. Once the agent is unsuspended, one ungated restart follows, which is the intended unpaced path.
- **The charge on a throwing start.** It gives exactly one charge per failed start, and V-S3-5 reaches Exhausted on the third charge, so MaxAttempts still holds.
- **Card S-0c catch.** Acceptable and logged (`AgentControlService.cs:325-337`).
- **`previous:true` ladders at `Failures == before+1`.** This matches `RestartFailurePolicy.Observe` at `:225`, which base also runs.

**(4) Regression.** All the named classes are green, and so are 8 more start-path classes apart from the inherited reds above. The ungated restart path (`AgentSupervisorService.cs:362`, `admission: null`) differs from base only by the plumbing rename and an added `_db.ChangeTracker.Clear()` on the scoped HoldRefusedFresh branch (`:562`).

StandingRestartAccountingTests `Failed_outcome_save_recovers_as_unknown_without_fresh_authority` seeds no capacity wait, so it runs only the ungated path. It is green here on Linux. This change is master only and is not on rc, so it cannot cause the rc Windows failure (BookkeepingHits 4 vs 2) that 28c5ff1a is investigating.

## Advisories (no Review rerun needed)

- **A-1.** Bound the S-4 reread timeout; see (1).
- **A-2.** The S-T catch (`AgentSupervisorService.cs:454-458`) swallows the exception without logging. A deadlock that recurs every tick would retry with no backoff and no trace. Add a Warning or Debug log.
- **A-3.** `RecoverLostCommitAsync` uses `throw commitEx;` (`AgentControlService.cs:255,279,283`), which resets the stack trace. Use `ExceptionDispatchInfo.Capture(commitEx).Throw()`.
- **A-4.** `AgentSupervisionState.CapacityRecoveryActionKey` is now written (`:921`) but never read, because the pre-check clause was deleted. It is harmless, but it is a dead field to remove or document in Round B.
- **A-5.** `_db.ChangeTracker.Clear()` in the capacity catches detaches the other agents the tick loaded (`:105`). They re-attach through `Entry(agent).ReloadAsync` at `:137`, and the base non-scoped branch already did this, so it works. It is just worth knowing.

## Cleanup

I deleted my `bin-rf36` and `bin-rf36red` output directories and my scratch worktree. Nothing of the subject's was touched. I did not run `git merge-tree --write-tree`.
