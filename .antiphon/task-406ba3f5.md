# CARD-1097 S4b repair report (task 406ba3f5)

A refused parked continuation stays Queued when the newest-warning lookup or the `park_resume_refused` insert fails. Caller cancellation still propagates. Checkpoint run `20261007-154402-10f0` is green at tested source `3c9e10effa04962ffe73c89b54c2a93c72bb27e0`.

Landing owner remains `c0caf152-6827-4310-a534-c1e7c538b54b`. This task does not land. Branch `feat/card-task-406ba3f5`, worktree `/work/worktrees/task-406ba3f5`. Plan table was not edited. No card edit. No migration.

## Fix

`WarnParkResumeOncePerReasonAsync` catches a telemetry failure on the lookup and on the insert. `IsBestEffortWarningFailure` is true unless the exception is an `OperationCanceledException` and the dispatch token is already canceled. A failed lookup is treated as no previous warning, and the insert is still attempted. `RemoteWarnAsync` is unchanged, so other remote-warning call sites keep their current catch.

## Other writes on the refusal path

The park-row read, the inspection lease, and `RefuseParkedResumeAsync` are admission decisions. They are pre-existing and were not swallowed. No other new write was added on the S4 refusal path. Other `RemoteWarnAsync` call sites still let a non-caller `OperationCanceledException` escape. That is pre-existing and outside this refusal path, and it was not widened.

`docs/session-runtime-invariants.md` was not changed. The success-path sentence that a warning is written once per reason stays true. A failed lookup can write an extra warning. The known-limits line already omits item 1.

## Local red/green proof

These runs are method-scoped proof for this Code repair. They do not discharge PC-9.

| Run | Lease | Waited | Held | Result |
|---|---|---:|---:|---|
| Reviewed dispatcher build (`9b63017d` production, new tests) | `a3336edf-ca72-46b7-b273-f3fc8b370d1d` | 0s | 133s | 0 errors |
| Reviewed dispatcher tests `C1097Telemetry_*` | `bc1e64a3-2de8-4511-8c8e-ab73b8072e77` | 0s | 84s | total 3, failed 2, succeeded 1 |
| Restored fix build | `cf0fad96-7acd-4b70-97f2-6da733fec226` | 15s | 89s | 0 errors |
| Restored fix tests `C1097*` | `65fa6ee2-44f6-4e29-bfe6-9ec791fb2efc` | 0s | 60s | total 4, failed 0, succeeded 4 |
| Both try/catch blocks removed, build | `d11702f1-9f6f-4f1c-a634-27c9cb07fb7f` | 0s | 54s | 0 errors |
| Both try/catch blocks removed, `C1097Telemetry_*` | `99af4314-a2d1-44f5-9a22-656fee991d90` | 0s | 56s | total 3, failed 2, succeeded 1 |
| Predicate returns true for every exception, build | `664803c9-de2e-4eca-b80b-5326d26e0d6a` | 0s | 50s | 0 errors |
| Predicate returns true, caller-cancellation test | `f66718e5-1a8b-4aea-a64b-9af2bb7ff69e` | 0s | 54s | total 1, failed 1 |

On `9b63017d` and with both try/catch blocks removed, the lookup timeout terminalizes the task (`Failures` 1) and the insert cancellation escapes through `FailAndNotifyAsync`. Caller cancellation already propagates, so that mutant leaves test 3 green. The swallow-all predicate fails test 3: `DispatchAsync` did not throw `OperationCanceledException`. Source was restored after both mutants. The checkpoint build below is the restored source.

## Ordinary scope

The whole Unit lane was not run. The brief's closed list replaces the generic Final Unit profile. One checkpoint-tool run, one isolated `UseAppHost=false` build (`bin-c1097b/`), serial rows. Selection: `docs/investigations/2026-10-07-card-1097-park-resume-warning-telemetry.md`. Driver bootstrap lease `7422321f-e3dc-4e3a-a5fe-e27a9f7eae56`, waited 0s, held 5s, 0 errors, 1 warning. Import of that note: 10 rows, exit 0. The checkpoint run was not wrapped in a second build slot.

`BlockedTaskParkResumeTests` is the class that holds the Queued/NotClaimed refusal test. CP-17 executed that class with the park family (18 results: release 3, sync 2, resume 7, delivery 6). CP-16 executed only `C1097_RefusedParkedResumeWarnsOncePerReason`. CP-36 executed the three `C1097Telemetry_*` methods. `BlockedTaskParkProjectionTests` is inside CP-18 and pins `docs/session-runtime-invariants.md`. `RunnerBranchContractDocumentationTests` is CP-93 and pins the same document.

Covered plan IDs from this closed list: V-8 (CP-16 1/1 and CP-36 3/3), R-3 (CP-17 18/18), V-13/R-4/R-5 (CP-18 7/7), R-6 (CP-19 7/7). S1, S2, S3, S5, and S6 V/R IDs are outside this repair's closed list.

PC-9 and every other PC stay pending for method-scoped SourceLanding Mutation.

## Checkpoint lines

Run `20261007-154402-10f0`, wall 15m25s, 10 green, 0 red, 0 skipped, exit 0. Source state clean, build source verified, dirty 0. Every row `slot=granted waited=0s`.

```
CHECKPOINT CP-16 commit=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 build=ok filter=/*/*/BlockedTaskParkResumeTests/C1097_* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-406ba3f5/.antiphon/c1097b-checkpoints/20261007-154402-10f0/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 sourceState=clean buildSource=verified
PHASES CP-16 slotWait=0s build=115.4561675s startup=40.6439898s testsWall=13.9154988s teardown=1.3664401s hostWall=55.9259434s
CHECKPOINT CP-36 commit=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 build=reused filter=/*/*/BlockedTaskParkResumeTests/C1097Telemetry_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-406ba3f5/.antiphon/c1097b-checkpoints/20261007-154402-10f0/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 sourceState=clean buildSource=verified
PHASES CP-36 slotWait=0s build=0s startup=38.7765331s testsWall=14.5469785s teardown=1.2323369s hostWall=54.5558482s
CHECKPOINT CP-17 commit=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 build=reused filter=/*/*/(BlockedTaskParkReleaseTests*)|(BlockedTaskSyncRecoveryTests*)|(BlockedTaskParkResumeTests*)|(BlockedTaskParkDeliveryTests*)/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-406ba3f5/.antiphon/c1097b-checkpoints/20261007-154402-10f0/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 sourceState=clean buildSource=verified
PHASES CP-17 slotWait=0s build=0s startup=40.8677476s testsWall=263.6192043s teardown=1.659591s hostWall=306.1465428s
CHECKPOINT CP-18 commit=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 build=reused filter=/*/*/(TaskParkPublicationTests*)|(TaskParkRunnerIdentityTests*)|(BlockedTaskParkProjectionTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-406ba3f5/.antiphon/c1097b-checkpoints/20261007-154402-10f0/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 sourceState=clean buildSource=verified
PHASES CP-18 slotWait=0s build=0s startup=41.6020194s testsWall=26.0229439s teardown=1.8425151s hostWall=69.4674782s
CHECKPOINT CP-19 commit=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 build=reused filter=/*/*/(DispatcherSweepLifetimeTests*)|(DispatcherSweepLifetimeRegistrationTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-406ba3f5/.antiphon/c1097b-checkpoints/20261007-154402-10f0/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 sourceState=clean buildSource=verified
PHASES CP-19 slotWait=0s build=0s startup=42.0916535s testsWall=7.6510127s teardown=1.4751994s hostWall=51.2178656s
CHECKPOINT CP-90 commit=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 build=reused filter=/*/*/BlockedTaskParkReclaimTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-406ba3f5/.antiphon/c1097b-checkpoints/20261007-154402-10f0/rows/CP-90/run.trx slot=granted waited=0s dirty=0 source=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 sourceState=clean buildSource=verified
PHASES CP-90 slotWait=0s build=0s startup=40.1009693s testsWall=80.0914019s teardown=1.424473s hostWall=121.6168441s
CHECKPOINT CP-91 commit=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 build=reused filter=/*/*/TerminalRunnerSeatReleaseTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-406ba3f5/.antiphon/c1097b-checkpoints/20261007-154402-10f0/rows/CP-91/run.trx slot=granted waited=0s dirty=0 source=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 sourceState=clean buildSource=verified
PHASES CP-91 slotWait=0s build=0s startup=38.9306557s testsWall=45.0890482s teardown=1.791853s hostWall=85.8115568s
CHECKPOINT CP-92 commit=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 build=reused filter=/*/*/RemotePoolFollowUpAdmissionTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-406ba3f5/.antiphon/c1097b-checkpoints/20261007-154402-10f0/rows/CP-92/run.trx slot=granted waited=0s dirty=0 source=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 sourceState=clean buildSource=verified
PHASES CP-92 slotWait=0s build=0s startup=42.9017968s testsWall=4.6596674s teardown=1.2188411s hostWall=48.7803054s
CHECKPOINT CP-93 commit=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 build=reused filter=/*/*/RunnerBranchContractDocumentationTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-406ba3f5/.antiphon/c1097b-checkpoints/20261007-154402-10f0/rows/CP-93/run.trx slot=granted waited=0s dirty=0 source=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 sourceState=clean buildSource=verified
PHASES CP-93 slotWait=0s build=0s startup=3.0184958s testsWall=0.0146801s teardown=0.4488262s hostWall=3.482002s
CHECKPOINT CP-94 commit=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-406ba3f5/.antiphon/c1097b-checkpoints/20261007-154402-10f0/rows/CP-94/run.trx slot=granted waited=0s dirty=0 source=3c9e10effa04962ffe73c89b54c2a93c72bb27e0 sourceState=clean buildSource=verified
PHASES CP-94 slotWait=0s build=0s startup=3.8678393s testsWall=1.5756063s teardown=0.6016784s hostWall=6.0451242s
```

Evidence diff over base `5a402d6d96da3fff88013faaaf01beb99ce9a98d` through the tested source `3c9e10effa04962ffe73c89b54c2a93c72bb27e0`: commits=2, entries=0, violations=0. This report file is a later commit. The caller summary states the evidence diff over the full range including this file.

## Merge and platform

`origin/master` at fetch time was `16ed20f3af157e0e922a95ab9b4cb90fb2c7e3a2`. `git merge-tree --write-tree HEAD origin/master` exited 0, tree `5c736f4eed50b6bcfb4d08fca966a003889f26b9`. No rebase was performed.

`GET /api/runner-defaults` reports `globalRunnerId=server2` and zero kind defaults. `GET /api/session-runners` lists desktop (available, occupied 0/2), server2 (available, draining, not accepting new work, occupied 0/10), and server2-temp (available, accepting, occupied 5/10). No `-Runner` and no `-Platform` were passed.

## Restart

Server code changed. The caller owns the AppHost restart. The behavior is inert while `BlockedTaskParking:Enabled` is false.

Owned `bin-c1097b/`, `bin-c1097b-red/`, and `bin-c1097b-driver/` directories were removed. Checkpoint TRX and `report.md` remain under `.antiphon/c1097b-checkpoints/20261007-154402-10f0/`.
