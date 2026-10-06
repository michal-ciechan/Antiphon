# CARD-1108 S2 Code

S2 is implemented and the closed checkpoint is green. `ReclaimScheduledAsync` is the only scheduled entry for the dispatcher sweep and the reconcile job. One run visits each eligible Blocked row at most once, and the job's returned total counts confirmed releases only.

## Identity

- Task / landing owner: `8c28c2c8`
- Branch: `feat/card-task-8c28c2c8`
- Worktree: `/work/worktrees/task-8c28c2c8`
- Assigned base (fast-forward only): `9463e1fba9a086b152df7517cb892f5f6c7e3fd2`
- That base was origin/master at the start of this task. At report time origin/master is `f4ae747a6b8ab6f8f8aeee025cfbd0bcafdeb1b4`. This branch was not rebased.
- Implementation commit, and the SHA the checkpoint tested: `a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2`
- Parent of that commit is the assigned base.
- Plan: `docs/superpowers/plans/2026-10-06-card-1108-park-reclaim-follow-ups-plan.md`
- Card `70855ab6-b670-41fd-b5af-42dfd9d05a03`, In Progress, board Antiphon. Items (2), (3) and (4) are this slice. Items (1), (5) and (6) stay with S1 / later slices.
- Restart: none. No `-Runner` and no `-Platform`.

`GET /api/runner-defaults` (revision 2) names global runner `server2`. `GET /api/session-runners`: desktop accepting, occupied 0; server2 available and draining, acceptingNewWork false, occupied 0; server2-temp accepting, occupied 5. This slice does not dispatch.

## Change

`ReclaimScheduledAsync` (`TerminalRunnerSeatReleaseService.cs`) returns before `LegacyGate` when parking is off or `ReclaimExisting` is off, so a disabled sweep does not query and does not stamp `LegacySweptAt`, `NextLegacySweepAt`, or `LastLegacyReclaim`. Overlap and an interval that has not elapsed return `LegacyReclaimResult.None` and do not stamp. A run that is allowed calls `ReclaimLegacyAsync(32, 3)` and then stamps the interval from the clock reading taken before the primitive. `ReclaimIntervalSeconds` defaults to 120; 0 sweeps on every scheduled call; a negative value is treated as 0. `ReclaimLegacyAsync` stays ungated. Its cap is `min(eligible Blocked count, pageSize * passBudget)` inside `for (pass < passBudget && visited < cap)`. The per-row try/catch, the cursor commit after a caught failure, and `TryHandle` are unchanged. `Released` increments only when `IsConfirmed` sees the attempt's ledger row.

Callers: `AgentTaskDispatcher` pool-release sweep calls `ReclaimScheduledAsync`. `RunnerSlotReconcileJob` adds `.Released` only. Docs: the three pinned sentences in `docs/session-runtime-invariants.md` (callers, one-visit bound, confirmed-release count), plus `ReclaimIntervalSeconds` in `docs/antiphon-api.md` and `docs/ops-http.md`. Pins in `BlockedTaskParkProjectionTests` match those sentences. Defaults stay `Enabled=false` and `ReclaimExisting=false`. S3 (Held backoff) and S4 (single verify) were not edited.

## Visit counts

Red first, production arguments via `ReclaimScheduledAsync`, cap temporarily `pageSize * passBudget` (the PC-4 shape). Five Blocked rows: `first.Visited` should be 5 but was 15 (`G-4`, `BlockedTaskParkReclaimTests.cs:291`). Filter `/*/*/BlockedTaskParkReclaimTests/C1108_ScheduledSweepIsGatedAndBoundedPerRun`. Slot `c1108-red-test` lease `0be7267f-1611-41cc-8d41-3b80a7b57fc6` waited 15s. total 1, failed 1, succeeded 0, skipped 0. The cap was restored to the eligible minimum before any green run. That red was the measurement, not a committed tree.

After, same five-row scheduled call: Visited 5, Eligible 5, Cap 5, Registered 5, Released 0, one park each, the Working row unparked and its session still running. Same clock again: `LegacyReclaimResult.None`, cursor unchanged. After 120s: Visited 5 again, still one park each. Overlap (test holds `LegacyGate`): None, cursor unchanged. `ReclaimIntervalSeconds` 0: two back-to-back calls both Visited 5, second Released 0. Ungated `ReclaimLegacyAsync(2, 3)` on 8 Blocked rows: Visited 6, Eligible 8, Cap 6 (V-23 budget unchanged). Crash on one `ReclaimList:` boundary: first run still Visited 5, poison park 0, other four parks 1, cursor committed, session running; 120s later the poison row parks.

One Blocked row through the job, after the fix: `JobAsync` returned 0, `LastLegacyReclaim` Visited 1, Released 0, confirmed ledger rows 0. The before-count for that single row was not run as its own red; the measured before is the five-row 15.

V-23 releasing run: `Released` equals the confirmed ledger count and equals 2.

## Ordinary V/R

| ID | Outcome |
|---|---|
| V-2 | Passed. `C1108_ScheduledSweepIsGatedAndBoundedPerRun` inside CP-6. |
| V-3 | Passed. `C1108_ReconcileJobCountsOnlyReleases` (return 0, Visited 1, Released 0) and the V-23 `Released == 2` assertion, both inside CP-6. |
| V-4 | Passed. `C1108_DispatcherHookRunsTheGatedSweep` inside CP-6, including both inert fixtures. |
| V-5, V-6 | Not this slice. S3 and S4. |
| R-1 | Passed. Whole `BlockedTaskParkReclaimTests`, 6 passed, 0 failed, 0 skipped. |
| R-2 | Passed. `TerminalRunnerSeatReleaseTests` 39. |
| R-3 | Passed. Release 3, SyncRecovery 2, Resume 3, Delivery 4 (12). |
| R-4 | Passed. Publication 3, RunnerIdentity 2. |
| R-5 | Passed. Projection 2. |
| R-6 | Passed. SweepLifetime 6, Registration 1 (7). |
| R-7 | Passed. RunnerSlotEndpoint 8, PhoneHomeDeferredKill 3, RunnerSlotRules 2 (13). |
| R-8 | Passed. `RunnerSeatOrphanSweepTests` 17. |

Class counts are from each row's TRX `UnitTest` className joined to `UnitTestResult`. Every result outcome was Passed.

## Checkpoint

Run `20261006-225557-e8c0`. Tool: `dotnet exec tools/Antiphon.Checkpoints/bin-c1108-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-06-card-1108-park-reclaim-follow-ups-plan.md --after S2 --rows CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12 --serial --expected-source-sha a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2`. Not wrapped in a second build slot. Exit 0. `--serial` also forced CP-9, whose plan cell is blank. Source state clean, buildSource verified. Wall 15m03s. PHASES show one compile (CP-6 build 106s) and six reused builds. The tool's own summary line says `builds: 5`; that is the manifest cleanup set, quoted below.

```
CHECKPOINT CP-6 commit=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 build=ok filter=/*/*/BlockedTaskParkReclaimTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-8c28c2c8/.antiphon/checkpoints/20261006-225557-e8c0/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 build=reused filter=/*/*/TerminalRunnerSeatReleaseTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-8c28c2c8/.antiphon/checkpoints/20261006-225557-e8c0/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 build=reused filter=/*/*/(BlockedTaskParkReleaseTests*)|(BlockedTaskSyncRecoveryTests*)|(BlockedTaskParkResumeTests*)|(BlockedTaskParkDeliveryTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-8c28c2c8/.antiphon/checkpoints/20261006-225557-e8c0/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 build=reused filter=/*/*/(TaskParkPublicationTests*)|(TaskParkRunnerIdentityTests*)|(BlockedTaskParkProjectionTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-8c28c2c8/.antiphon/checkpoints/20261006-225557-e8c0/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 build=reused filter=/*/*/(DispatcherSweepLifetimeTests*)|(DispatcherSweepLifetimeRegistrationTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-8c28c2c8/.antiphon/checkpoints/20261006-225557-e8c0/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 build=reused filter=/*/*/(RunnerSlotEndpointTests*)|(PhoneHomeDeferredKillTests*)|(RunnerSlotRulesTests*)/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-8c28c2c8/.antiphon/checkpoints/20261006-225557-e8c0/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 build=reused filter=/*/*/RunnerSeatOrphanSweepTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-8c28c2c8/.antiphon/checkpoints/20261006-225557-e8c0/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2 sourceState=clean buildSource=verified
```

Tool footer, unedited: `unlisted: none (the tool ran no other build or test command)` / `wall: 15m03s  sequential-equivalent: 13m15s  builds: 5  max-concurrent-builds: 1  rows: 7 green 0 red 0 skipped` / `outputs: deleted bin-c1108-cp1/, bin-c1108-cp6/, bin-c1108-cp13/, bin-c1108-cp18/, bin-c1108-final/` / `verdict: GREEN exit=0`.

Evidence on the implementation commit, before this report file: `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 9463e1fba9a086b152df7517cb892f5f6c7e3fd2 -HeadRef a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2` returned `EVIDENCE result commits=1 entries=0 violations=0`. The same command over base..HEAD after this report commit is in the caller summary.

## Unlisted runs

Each went through `scripts/build-slot.ps1`. No `-NoSlot`.

- Red-first measurement above. Reason: the new bound had to fail on the old three-pass cap before the green cap was kept.
- `c1108-green-test` lease `e708c125-a9ff-4778-a6e0-f62581e93070` waited 0s, held 134s. `/*/*/BlockedTaskParkReclaimTests/*` on `bin-c1108-green/`, `--no-build`. total 6, failed 0, succeeded 6, skipped 0, duration 2m 09s. Reason: prove the restored cap and the three new methods before the checkpoint commit.
- `c1108-green-projection` lease `aed602e2-07a6-4bfa-a2b0-a309469a604c` waited 0s. `/*/*/BlockedTaskParkProjectionTests/*`, same output, `--no-build`. total 2, failed 0. Reason: the rewritten pins.
- Checkpoint driver bootstrap lease `fc557544-24c3-48fe-ae4a-7a9a72d36dc6` waited 0s. `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1108-driver/ --property:UseAppHost=false`. Reason: the row driver. The row run itself took its own slots.
- Registry guard, not a plan row. Reason: the brief requires `TestClassificationGuardTests` and `SlowTestTripwireTests` once, serially. Lease `91acfb4e-2ad1-43a5-8280-b4744cb61238` waited 0s. Filter `/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/*`, `--no-build` on `bin-c1108-green/` (compiled from the tree that was committed as `a4186c148`, with no source edit in between; the checkpoint had already deleted `bin-c1108-cp6/`). total 3, failed 0, succeeded 3, skipped 0. Source roster of that filter is one guard method (`Registry_matches_compiled_metadata`) and two tripwire methods. The run did not emit a TRX, so the 1 and 2 are that roster against the total of 3, not a parsed per-class counter.

No repair round after the intentional red. No whole-Unit lane and no baseline sweep: the brief's closed S2 list overrides the Final profile's whole-Unit sentence.

## Positive controls

Not executed. Pending for method-scoped SourceLanding Mutation:

- PC-1, PC-2 (S1 window). Filter `/*/*/BlockedTaskParkReclaimTests/C1108_LegacyWindowIsServerAnchored`.
- PC-3 ignore `NextLegacySweepAt`: second same-clock call Visited 5, not 0. `C1108_ScheduledSweepIsGatedAndBoundedPerRun`.
- PC-4 `Cap = pageSize * passBudget` without the eligible min: Visited 15, not 5. Same filter. The Code red-first measured that failure and restored the cap. It is not the Mutation cycle.
- PC-5 stamp `LegacySweptAt` before the enabled check: ReclaimExisting-off arm finds it set. `C1108_DispatcherHookRunsTheGatedSweep`.
- PC-6 job adds Visited: `JobAsync` returns 1, not 0. `C1108_ReconcileJobCountsOnlyReleases`.
- PC-7 increment Released on every visit: Released 1 with no confirmed ledger. Same filter.
- PC-8, PC-9, PC-10, PC-11 (S3/S4). Not implemented here.
- CARD-1065 PC-172..PC-181, PC-23, PC-24. Pending. Filters stay in that plan. PC-171 is superseded by PC-1 and PC-2.

## Files in a4186c148

`BlockedTaskParkingOptions.cs`, `TerminalRunnerSeatReleaseService.cs`, `AgentTaskDispatcher.cs`, `RunnerSlotReconcileJob.cs`, `RunnerSeatReleaseFixture.cs`, `BlockedTaskParkReclaimTests.cs`, `BlockedTaskParkProjectionTests.cs`, `docs/session-runtime-invariants.md`, `docs/antiphon-api.md`, `docs/ops-http.md`. 10 files, +294 / -19.
