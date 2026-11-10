# CARD-1476 Review round 1 (Final): CLEAN

Subject: origin/feat/card-task-67c454a1 tip 0a4ccf82cee487f3861dbb32dcd461a40f352bdf (base 09d80f11a71cffa03edebc2866b561a8d0962fcb; Code owner task 67c454a1).
Range: 74d25bd77 test, ffb5dc8bd fix, 0a4ccf82c docs. Blockers: none.

## (a) Is it safe to dispatch after a failed pin?
- `refs/antiphon/progress/<task>/baseline-<name>` only keeps objects retained. No reader requires a baseline-* pin: the only reference outside the writers is TaskCompletionProgressService:595, which pins again (PinIfPossible). Settlement and RefOnly custody read the settlement-sync / ObservationRef pin, not baseline-*. The baseline SHA is the task branch tip at claim, so it stays reachable through the branch. Progress still computes from ProgressBaselineJson, which is still captured. Warn-and-dispatch matches the card ("surface it") and plan D-4. Refusing the claim would turn a retention-only failure into a dispatch outage.
- Visibility: the warning is an AgentTaskEventType.Warning event on the claim, written in the same tracked context as the claim and committed with it. This is the same channel as the existing `progress=unavailable` capture warning, so it is not only a log line.

## (b) Lease, exceptions, partial rows
- R span: unchanged. A Worktree claim disposes R before the capture, and the pin takes its own TryAcquireAsync.
- Exceptions: RecordPinAsync adds no throw. A failed pin is a string append. Cancellation still throws OperationCanceledException from PinBaselineAsync before the claim commits, exactly as at base.
- Concurrent claims: each task has its own pin namespace. An id collision returns recovery_ref_collision, which now becomes a warning. Under contention the pin's TryAcquireAsync returns null and the pin proceeds, unchanged.
- Partial rows: no new half-written claim row; the events commit with the claim.

## (c) Witnesses and mutants (one build, both applied, then restored)
- PC-4 (drop `claimed.Workspace == WorkspaceMode.Worktree &&`): C1476_shared_claim_keeps_the_lease_through_commit RED, "R was released before the claim commit" (trace: released > before-commit).
- PC-5 (`!pinned.Succeeded` -> `pinned is null`): C1476_failed_baseline_pin_is_reported RED, missing "baseline pin failed: primary-local reason=pin_failed".
- The class's other 3 tests stayed green. The pin witness injects Succeeded=false through the RecordingTaskProgressGit decorator, so the real dispatcher consumption path runs.

## (d) Scope
No Worktree-mode claim change; no migration; no policy json touched.

## Runs at 0a4ccf82c
- build-slot: DispatcherLeaseSpanTests 5/5. Guard batch 115/115 (TestClassificationPolicy 41, ProcessSpawnLimit 57, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3, TestClassificationGuard 1, TestLaneCategoryGuard 1, TaskPlatformGuidance 5).
- Checkpoint run 20261110-080718-81c8, from the ignored extract .antiphon/review-4d05a61d-plan.md:
  - CP-1, the 1462 CP-10 class set incl. DispatcherLeaseSpanTests: executed=122 passed=122.
  - CP-2, the guard batch: executed=115 passed=115.
  - GREEN, sourceState=clean, buildSource=verified. `validate`: CHECKPOINT SOURCE VALID rows=2.
- check-evidence-diff 09d80f11a..HEAD: entries=0 violations=0.
- merge-tree vs origin/master 566d64b17: clean (tree d5788a505). Master has not touched the three source/test files since the base.

## Advisories (cards, never hold the land)
- A-1: the AgentTaskDispatcher non-Code runner comment ("never failed, warned or held for this") predates CARD-1476. A failed pin now records a Warning for non-Code runner tasks too. The behaviour is harmless, but the comment should say the pin warning is the exception.
- A-2: the brief's "135/135" guard-union count is not reproduced. The named Antiphon.Tests classes give 115, including TaskPlatformGuidance 5. ProcessSpawnLimitTests in SessionRunner.Tests and Pty.Tests were not in scope.
- Note: this worktree's native bin/ still holds the mutant build. The checkpoint run used its own cold output.
