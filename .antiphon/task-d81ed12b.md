# CARD-1076 S5 Code report (task d81ed12b)

Outcome: S5 (D-7 client queue reasons, docs for D-2..D-6, V-15) is implemented and the final-SHA rows are green on Linux. The client renders `repositoryLease`, `remotePrep`, and the already-emitted `hostBudget`, including `heldBy`. An unknown future reason stays the queued line and is not echoed. Server queue reasons were not reimplemented. Tested source `fe9e3f73d44cc41010f24738319bb0df71fc4947`. Branch `feat/card-task-d81ed12b`. Worktree `/work/worktrees/task-d81ed12b`. Landing owner / original Code task `d81ed12b`. Base `e839feda98ce3d97b6329bcd4a1030f6299460cd`. Plan `docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md`. restart: none (client and docs, not activated).

## Commit

- `fe9e3f73d44cc41010f24738319bb0df71fc4947` CARD-1076 S5: render lease, remote-prep and host-budget queue reasons

## Behaviour

`AgentTaskPipelineQueueReason` gains `repositoryLease`, `remotePrep`, and `hostBudget`. Compact form: `lease ~<short>` / `lease`, `prep ~<short>` / `prep`, `host full`. Long form: `waiting: repository lease held by task-<short> — <title>` or `... another process`; `waiting: remote workspace preparation (behind task-<short> — <title>)` or `... (branch push and mirror)`; `waiting: runner host at its budget`. A runtime reason outside the union returns the awaiting-dispatch line and does not print the raw token. The switches stay exhaustive for members of the union.

Docs in `docs/orchestration-loop.md`, `docs/ops-http.md`, and `docs/antiphon-api.md`: `Delegation:RemotePrepPushBudgetMinutes` default 20 floor 1; live `remote-prep-push` does not fence and a dead, reused, or unknown record still names `scripts/recover-repository-children.ps1`; one push at a time per repository; a captured-baseline re-arm takes no lease; the two queue reasons. The preparer push is `git push origin <branch>` with no `-u`, so the desktop task branch does not track origin. `BehindTaskId` is the enqueue-time snapshot (CARD-1093).

## Red first (dirty tree, not receipts)

Client filter `pipelineStageModel homeTasksModel TaskCard` before the implementation: 14 failed, 98 passed. The new cases received `undefined` or an empty queue line. `C1076_remote_prep_push_contract_is_documented` before the docs: 1 failed, missing `RemotePrepPushBudgetMinutes`. After the edits, the same client filter passed 112/112 and the docs class passed 4/4 on `bin-c1076-red` with `--no-build`. Those runs are not the SHA-validated receipt.

## Checkpoint (run 20261006-150125-1cb9)

Tool bootstrap `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1076-tool/ --property:UseAppHost=false` under slot `a61a566c-1e23-416a-adc4-58b755e9f581` waited=0s held=5s. That bootstrap is the explained unlisted build. The `run` was `dotnet exec` of that DLL, not a second slot. `--serial` and `TUNIT_MAX_PARALLEL_TESTS=1`. CP-7 (vitest, no Postgres) finished during the `bin-c1076-b` build. CP-3 through CP-6 did not overlap. Host Debian GNU/Linux 12. Verdict GREEN exit=0. Wall 5m29s. `unlisted: none (the tool ran no other build or test command)`.

```
CHECKPOINT CP-7 commit=fe9e3f73d44cc41010f24738319bb0df71fc4947 build=n/a filter=pwsh -File scripts/test-client.ps1 pipelineStageModel homeTasksModel TaskCard executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=fe9e3f73d44cc41010f24738319bb0df71fc4947 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=fe9e3f73d44cc41010f24738319bb0df71fc4947 build=ok filter=/*/*/(RemoteWorkspacePreparerTests*)|(DispatcherRemotePrepStarvationTests*)|(PhoneHomeTaskDispatchProjectionTests*)/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-d81ed12b/.antiphon/c1076-s5/20261006-150125-1cb9/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=fe9e3f73d44cc41010f24738319bb0df71fc4947 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=101.6891621s startup=40.6258497s testsWall=43.1374815s teardown=1.5867681s hostWall=85.3500982s
CHECKPOINT CP-4 commit=fe9e3f73d44cc41010f24738319bb0df71fc4947 build=reused filter=/*/*/(AgentTaskDispatcherPredicateTests*)|(DispatchHoldLedgerTests*)|(DelegationLeaseSettingsTests*)|(RunnerBranchContractDocumentationTests*)/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-d81ed12b/.antiphon/c1076-s5/20261006-150125-1cb9/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=fe9e3f73d44cc41010f24738319bb0df71fc4947 sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=0s startup=4.54234s testsWall=0.2039587s teardown=0.6813015s hostWall=5.4276s
CHECKPOINT CP-5 commit=fe9e3f73d44cc41010f24738319bb0df71fc4947 build=reused filter=/*/*/AgentTaskPipelineStatusTests/* executed=66 passed=66 failed=0 skipped=0 trx=/work/worktrees/task-d81ed12b/.antiphon/c1076-s5/20261006-150125-1cb9/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=fe9e3f73d44cc41010f24738319bb0df71fc4947 sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=0s startup=41.4679184s testsWall=28.2870862s teardown=1.43579s hostWall=71.1907943s
CHECKPOINT CP-6 commit=fe9e3f73d44cc41010f24738319bb0df71fc4947 build=reused filter=/*/*/DispatchHoldVisibilityTests/(lease_hold_traces_once_per_holder_and_names_the_running_land*)|(lease_fence_is_named_from_the_provider*)|(unknown_lease_holder_is_stable_text*)|(C672_lease_hold_registers_a_waiter_and_dispatch_clears_it*)|(C672_held_aged_carries_the_per_class_wait_ledger*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-d81ed12b/.antiphon/c1076-s5/20261006-150125-1cb9/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=fe9e3f73d44cc41010f24738319bb0df71fc4947 sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=0s startup=38.8026534s testsWall=11.8213799s teardown=3.1356337s hostWall=53.7596668s
```

CP-7 console: pipelineStageModel 44, homeTasksModel 37, TaskCard 31, 112 passed, 0 failed, `CLIENT TESTS EXIT CODE: 0`.

## Class counts

| Class | Results |
|---|---|
| RemoteWorkspacePreparerTests | 23 passed (19 existing + 2 C1076 + 2 C672 arms) |
| DispatcherRemotePrepStarvationTests | 4 passed |
| PhoneHomeTaskDispatchProjectionTests | 8 passed |
| AgentTaskDispatcherPredicateTests | 17 passed (8 C672_LaunchesPreparedMirror + 9 C1076_RearmsPreparedWorktree) |
| DispatchHoldLedgerTests | 23 passed (22 existing + V-12) |
| DelegationLeaseSettingsTests | 11 passed (8 existing + 3 V-13) |
| RunnerBranchContractDocumentationTests | 4 passed (3 existing + V-15) |
| AgentTaskPipelineStatusTests | 66 passed (58 existing + 8 S4) |
| DispatchHoldVisibilityTests (R-9 methods) | 5 passed |

## Registry guard (explained, not a plan row)

Same SHA. `scripts/run-checkpoint.ps1` takes its own slot. No new Slow class. `slow-tests-allowlist.txt` was not edited.

```
CHECKPOINT C1076-S5-GUARD commit=fe9e3f73d44cc41010f24738319bb0df71fc4947 build=ok filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d81ed12b/.antiphon/c1076-s5-guard/C1076-S5-GUARD-20261006-150738-b61e/run.trx slot=granted waited=0s dirty=0 source=fe9e3f73d44cc41010f24738319bb0df71fc4947 sourceState=clean buildSource=verified
```

Slot `75cd3951-540a-4ceb-8698-14793df0229d` waited=0s held=114s. Methods: `Registry_matches_compiled_metadata`, `unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not`, `allowlist_file_exists_and_has_spawn_lane_entries`.

## Ordinary V/R this round

| ID | Outcome |
|---|---|
| V-5, V-6, V-7, R-1, R-7, R-10 | Passed inside CP-3 (35/35) |
| V-8, V-12, V-13, V-15, R-5, R-6, R-11, R-12 | Passed inside CP-4 (55/55) |
| V-9, V-10, V-11, R-4 | Passed inside CP-5 (66/66) |
| R-9 | Passed inside CP-6 (5/5) |
| V-14 | Passed inside CP-7 (112/112) |

V-1..V-4, R-2, R-3, and R-8 are the S1 rows (CP-1, CP-2). This brief's closed list is CP-3..CP-7. They were not re-run and are not marked passed here. The whole Unit lane was not run (brief: no whole-Unit, no baseline sweep). CARD-1102 and CARD-1096 were not in these filters.

Repair rounds used: 0. No assertion, budget, or timeout was widened.

## Positive controls pending for Mutation

PC-1..PC-12 stay pending. PC-12 is `compactQueueReason` returning `queued` for `repositoryLease`; the red witness is V-14 (`lease ~<short>`). Mutation was not run.

## Evidence

`scripts/check-evidence-diff.ps1` over `e839feda98ce3d97b6329bcd4a1030f6299460cd`..`fe9e3f73d44cc41010f24738319bb0df71fc4947`: commits=1 entries=0 violations=0. Isolated `bin-c1076-*` directories were deleted, including the tool bootstrap and the green-run `builds/bin-c1076-b` marker.
