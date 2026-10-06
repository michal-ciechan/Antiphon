# CARD-1087 CP-5 repair (task 704bd0f7)

Outcome: the host-census row was over the unchanged 20-minute cap because the twelve methods themselves take about 22 minutes. The plan now splits them into CP-5 and CP-5b. Both rows, CP-7, and the narrow registry guard are green at `37530ed0231b42cbdad4fcfad4c8b55a43ea2888` with clean verified source receipts. No timeout or assertion was loosened. No production code changed.

Landing owner remains Code task 13c7dc46. This dispatch is the follow-up Code owner 704bd0f7. Branch `feat/card-task-704bd0f7`. Worktree `/work/worktrees/task-704bd0f7`. Plan `docs/superpowers/plans/2026-10-06-card-1087-recycle-census-plan.md`. Restart: none.

## Diagnosis

The failed CP-5 row at `9c0fa80459cfc60e8c78a069d1d4e987f008ec41` (run `20261006-060408-5b3a`) was killed at 1200.9 seconds with no TRX and no PHASES line. The executor held the test-host slot from 06:27:41 to 06:47:41. The build was reused. Slot wait was 0. The console was the TUnit banner only. Two task-owner HTTP reads of 12 seconds each are not the 20 minutes.

`Microsoft.Testing.Platform` 2.1.0 parses `/*/*/RemoteScriptContractTests/(C1008_*)|(C1087_*)` as four segments: `.*`, `.*`, `RemoteScriptContractTests`, and an OR of `C1008_.*` and `C1087_.*`. `MatchesFilter` accepts only those method names in that class. It rejects the wrapper `C1087_*` / `C1008_*` methods and `C1008_Real_docker_comparison`. `--list-tests` prints the whole assembly even for a one-method filter, so it was not used as selection evidence.

Isolated TRX durations at that SHA, each `total: 1` and Passed, from `/tmp/c1087-timing/<method>/run.trx`:

| Method | TRX duration |
|---|---|
| C1008_Recycle_resume_requires_matching_receipt | 00:05:14.772 |
| C1008_Recycle_receipt_records_disk_and_partial_failure | 00:04:09.402 |
| C1008_Recycle_refuses_references_and_unknown_census | 00:04:03.942 |
| C1008_Recycle_refuses_uninspectable_git | 00:02:29.394 |
| C1008_Recycle_exact_default_volumes | 00:01:46.157 |
| C1008_Recycle_refuses_unpublished_and_dirty_work | 00:01:35.948 |
| C1008_Retire_temp_rechecks_absence_and_retirement | 00:00:50.614 |
| C1087_Host_census_filters_and_names_cause | 00:00:33.594 |
| C1008_Recycle_preserves_tmp_copyup | 00:00:25.445 |
| C1008_Retire_temp_reclaims_below_cache_disk_gate | 00:00:23.906 |
| C1008_Recycle_audits_work_as_1654 | 00:00:16.958 |
| C1008_Recycle_dry_run_never_mutates | 00:00:03.710 |

TUnit engine durations for the same runs sum to 1327 seconds (the three longest 812 seconds, the other nine 515 seconds). That is past 20 minutes, so one row cannot finish. Compose-model caching cannot remove the bash work those durations already measure.

## Plan split

Commit `37530ed0231b42cbdad4fcfad4c8b55a43ea2888` replaces the single CP-5 filter with two exact rows. Both reuse `bin-c1087-b` (Build `CP-3`, After `all`). The importer accepted 8 rows and printed no warnings. The 20-minute `--row-timeout` is unchanged.

- CP-5: the three longest methods. Min 3. EstimatedMinutes 14. Covers R-4.
- CP-5b: the other nine, including `C1087_Host_census_filters_and_names_cause`. Min 9. EstimatedMinutes 9. Covers V-6 and R-4.

## Closed run

Run `20261006-080705-6ee4`. Tool bootstrap build slot `b9f71264-54cb-4a08-b9be-3bb7e8bd7d86` waited=0s held=4s, then `dotnet exec tools/Antiphon.Checkpoints/bin-c1087-tool/Antiphon.Checkpoints.dll` (not a second slot). Test build slot `82b8ab1e-6f76-4937-a0fe-6d430a01fab6` label `build:bin-c1087-b` waited=0s. Row slots waited=0s: CP-5 `5a927a87-ad74-4c58-8c2c-9968724af597`, CP-5b `5b822e86-a89a-4ba6-b9dc-feab0b2ab0d6`, CP-7 `23569d15-32b4-4fff-a399-db0e3b01c043`. Verdict GREEN exit 0. `bin-c1087-a` was unused. `bin-c1087-b` buildSource=verified.

```
CHECKPOINT CP-5 commit=37530ed0231b42cbdad4fcfad4c8b55a43ea2888 build=ok filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_resume_requires_matching_receipt*)|(C1008_Recycle_receipt_records_disk_and_partial_failure*)|(C1008_Recycle_refuses_references_and_unknown_census*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-704bd0f7/.antiphon/checkpoints/20261006-080705-6ee4/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=37530ed0231b42cbdad4fcfad4c8b55a43ea2888 sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=114.2648507s startup=3.5925486s testsWall=806.3416391s teardown=0.959156s hostWall=810.8933457s
CHECKPOINT CP-5b commit=37530ed0231b42cbdad4fcfad4c8b55a43ea2888 build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_exact_default_volumes*)|(C1008_Recycle_refuses_uninspectable_git*)|(C1008_Recycle_refuses_unpublished_and_dirty_work*)|(C1008_Retire_temp_rechecks_absence_and_retirement*)|(C1087_Host_census_filters_and_names_cause*)|(C1008_Recycle_preserves_tmp_copyup*)|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)|(C1008_Recycle_audits_work_as_1654*)|(C1008_Recycle_dry_run_never_mutates*) executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-704bd0f7/.antiphon/checkpoints/20261006-080705-6ee4/rows/CP-5b/run.trx slot=granted waited=0s dirty=0 source=37530ed0231b42cbdad4fcfad4c8b55a43ea2888 sourceState=clean buildSource=verified
PHASES CP-5b slotWait=0s build=0s startup=3.5976742s testsWall=476.1422004s teardown=0.9976829s hostWall=480.7375578s
CHECKPOINT CP-7 commit=37530ed0231b42cbdad4fcfad4c8b55a43ea2888 build=reused filter=/*/*/RollingVolumeRecycleDockerTests/C1008_Real_docker_comparison* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-704bd0f7/.antiphon/checkpoints/20261006-080705-6ee4/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=37530ed0231b42cbdad4fcfad4c8b55a43ea2888 sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=0s startup=3.9358991s testsWall=669.5173662s teardown=1.6533962s hostWall=675.1066616s
```

TRX names match the rows: CP-5 the three methods above, all Passed; CP-5b the nine methods above, including `C1087_Host_census_filters_and_names_cause`, all Passed; CP-7 `C1008_Real_docker_comparison` Passed.

## V / R

| ID | Result |
|---|---|
| V-6 | Passed. `RemoteScriptContractTests.C1087_Host_census_filters_and_names_cause` in CP-5b, 9 executed / 9 passed / 0 failed / 0 skipped. |
| R-4 | Passed. All 11 `RemoteScriptContractTests.C1008_*` methods: 3 in CP-5 and 8 in CP-5b, 0 failed / 0 skipped. |
| R-6 | Passed at this SHA. CP-7 1/1. |

CP-3 16/16, CP-4 13/13, and CP-6 13/13 remain the prior task's green rows at `9c0fa80459cfc60e8c78a069d1d4e987f008ec41`. This repair does not re-certify them. The only source change after that SHA is the checkpoint table.

Registry, required narrow guard, slot `4d8e8044-841f-4ea8-92d9-5e6b2804ea68` waited=0s held=255s:

```
CHECKPOINT C1087-REGISTRY commit=37530ed0231b42cbdad4fcfad4c8b55a43ea2888 build=ok filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-704bd0f7/.antiphon/c1087-registry/C1087-REGISTRY-20261006-084231-829a/run.trx slot=granted waited=0s dirty=0 source=37530ed0231b42cbdad4fcfad4c8b55a43ea2888 sourceState=clean buildSource=verified
```

Executed: `SlowTestTripwireTests.unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not`, `SlowTestTripwireTests.allowlist_file_exists_and_has_spawn_lane_entries`, `TestClassificationGuardTests.Registry_matches_compiled_metadata`.

## Explained unlisted work

- Diagnostic `Antiphon.Tests` build to `bin-c1087-diag/` at `9c0fa80459cfc60e8c78a069d1d4e987f008ec41`, then twelve one-method `--no-build` runs, to measure the row before splitting it.
- A throwaway `/tmp/c1087-filterprobe` build, slot-gated, to call `TreeNodeFilter` directly. Not a product test.
- Tool bootstrap `bin-c1087-tool/` is the plan's one explained tool build.
- Registry build to `bin-c1087-registry/` is the required guard, not a plan row.

No whole Unit lane. No mutation.

## Positive controls

PC-1 through PC-8 remain pending for method-scoped SourceLanding Mutation. They were not run.

## Evidence

Task base `860cd6a8c121850e9ca8bea7816df6bf17e7f3e1`.

```
EVIDENCE range base=860cd6a8c121850e9ca8bea7816df6bf17e7f3e1 head=37530ed0231b42cbdad4fcfad4c8b55a43ea2888
EVIDENCE result commits=9 entries=0 violations=0 base=860cd6a8c121850e9ca8bea7816df6bf17e7f3e1 head=37530ed0231b42cbdad4fcfad4c8b55a43ea2888
```

That range is the plan split, before this report file. The guard is run again on the pushed tip after this file is committed.
