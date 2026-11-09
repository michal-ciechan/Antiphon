# CARD-1784 S1 round 2 code repair

Outcome: the real build-slot path returns only an int, a stale lock is reclaimed by rename, a reused pid is stale, and a fresh TRX with zero executed tests stays incomplete. CP-1 is green at the fix.

Task d705ed31. Role Code. Round Final. Card CARD-1784 (cd08cc7a-fed5-4beb-bbf4-64025a7616a6). Original Code task / landing owner 3442cc1d. Branch feat/card-task-d705ed31. Worktree /work/worktrees/task-d705ed31. Base 6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647. Continued origin/feat/card-task-3442cc1d. Review subject was dfecf4e4.

## SHAs

| Role | SHA |
|---|---|
| Red (witnesses only) | ee4258bbb91e10dd04b550bb05ee4d86bbeea578 |
| Fix (tested source) | 287fc145771e1850a22d681a1c49e44712d2017f |
| Tested | 287fc145771e1850a22d681a1c49e44712d2017f |
| Report tip | the commit that adds this file; it does not change the tested source |

Range names: scripts/lib/post-land-early-warning.ps1 and tests/Antiphon.Tests/Scripts/PostLandEarlyWarningTests.cs. tests/test-execution-policy.json is unchanged, so the nightly policyHash and PinnedInputDigest were not refreshed.

## What changed

Invoke-PostLandSlot takes -Command and -LogPath. The native `pwsh -File` call appends to the lane slot log (`*>> $LogPath`) or pipes to Out-Host, then returns only `$LASTEXITCODE` as an int. Build exit 0 runs shards and writes a completed receipt. Build exit 4 writes the incomplete `build=4` receipt and skips shards. A shard exit is an int on the receipt.

Enter-PostLandLock, after the optional POST_LAND_WARNING_RECLAIM_DELAY_MS sleep, moves a stale lock to a unique `*.reclaim` name, re-checks that copy, and deletes only that claim. If the claim is no longer stale it is moved back and the function returns false.

A live pid whose process start time (UTC) is more than 2 seconds after the lock startedAt is stale. The current process stays ours. A missing start time stays busy.

Complete-PostLandOutcome treats a parsed TRX with executed 0 as an unknown shard (`new-reds=unknown`, state incomplete).

The new tests are methods on the existing [Category("Unit")] class PostLandEarlyWarningTests. They drive POST_LAND_WARNING_SLOT_SCRIPT, not the shim. A-8 is the exclusive-reclaim witness.

Audit of both scripts: the only native call that leaked output into a return value was `& pwsh` inside Invoke-PostLandSlot. The entry script's Start-Process assigns $null. Invoke-PostLandProcess captures streams into an object. There is no `& dotnet` and no `& git`.

A-10 was left. Pinned-doc detection is a filename regex over tests/**/*.cs, so a doc read through directory enumeration is not caught, and the Windows run-mode path belongs to the Windows rc lane. That is not a small fix on this slice.

## CP-1

Tool: `dotnet run --project tools/Antiphon.Checkpoints --no-build --property:UseAppHost=false`, not wrapped in a second build-slot. The tool took its own slot. Source was clean at the fix SHA.

```
CHECKPOINT CP-1 commit=287fc145771e1850a22d681a1c49e44712d2017f build=ok filter=/*/*/PostLandEarlyWarningTests/* executed=20 passed=20 failed=0 skipped=0 trx=/work/worktrees/task-d705ed31/.antiphon/checkpoints-c1784/20261109-225219-d84b/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=287fc145771e1850a22d681a1c49e44712d2017f sourceState=clean buildSource=verified
```

Run 20261109-225219-d84b. CHECKPOINT-TIMING CP-1 waited=0s executed=174.304s wall=174.363s. PHASES slotWait=0s build=464.9023186s startup=15.4418814s testsWall=157.3450567s teardown=0.5217134s hostWall=173.308658s. Wall 12m41s. Floor 9, executed 20. TRX Counters total=20 executed=20 passed=20 failed=0. All 20 UnitTestResult rows are Passed, including Real_slot_exit_zero_runs_shards_and_completes, Real_slot_exit_four_is_incomplete_and_skips_shards, Real_slot_shard_exit_records_an_int_and_the_failures, Stale_lock_reclaim_is_exclusive, Reused_pid_with_an_older_start_does_not_hold_the_lock, and Zero_executed_shard_is_incomplete. The tool deleted bin-c1784-s1/. Unlisted builds: none.

## Ordinary V/R on this slice

CP-1 covers V-1 through V-8 and R-1. Each named method passed in that TRX: Docs_only_range_skips, Code_range_plans_namespace_shards_and_one_lane, Start_inside_sixty_minutes_is_hourly, New_reds_are_failures_absent_from_the_ancestor_receipt, Summary_line_names_tip_batch_and_new_reds, Non_linux_run_refuses_before_a_slot, Manifest_uses_no_letter_or_and_partitions_unit_classes, Integration_list_drops_unit_classes_and_records_windows_as_owed, Doc_sentences_are_present_in_both_owner_files. V-9 through V-14 and R-2 belong to later slices and were not run here.

## Red, green, mutants

Red, before the fix, filter of the six new methods, lease ba85436f-cc66-4c95-a64a-d072ca676ce7 waited=0s held=93s. TRX /tmp/c1784-red/c1784-red.trx: total=6 executed=6 passed=0 failed=6. The preceding build lease was 0ef5d5f6-a1a3-42bc-98d0-ad0ba3238604 waited=0s held=185s, exit 0.

Green class, after the fix, `--no-build`, filter `/*/*/PostLandEarlyWarningTests/*`, lease cd52c627-d0db-4b77-9097-4e4fa8b9f119 waited=0s held=128s. TRX /tmp/c1784-green/c1784-green.trx: total=20 executed=20 passed=20 failed=0.

Each mutant was restored with `git checkout -- scripts/lib/post-land-early-warning.ps1` before the next. None was committed.

| Witness | Mutant | Lease | TRX |
|---|---|---|---|
| Three Real_slot_* methods | drop the log redirect | 3d78a0f9-7e0a-4b76-8d68-20149af38ff8 waited=0s held=77s | 3 executed, 0 passed, 3 failed |
| Stale_lock_reclaim_is_exclusive | Remove-Item of the live lock path | ac92723c-d3ab-4658-981c-4f7deb2cb7d3 waited=0s held=13s | 1 executed, 0 passed, 1 failed |
| Reused_pid_with_an_older_start_does_not_hold_the_lock | alive pid returns busy | 4fd22526-8af0-42d6-ba02-eb024935a71d waited=0s held=10s | 1 executed, 0 passed, 1 failed |
| Zero_executed_shard_is_incomplete | drop the executed==0 branch | c9e51d92-3b4e-47bd-8976-5a0c3f8c6db7 waited=0s held=12s | 1 executed, 0 passed, 1 failed |

## Guards

One leased run, lease ed153719-abf4-4e18-9d43-6d860015c923 waited=0s held=160s. Filter union of TestClassificationPolicyTests*, TestClassificationGuardTests*, SlowTestRegistryMergeTests*, SlowTestTripwireTests*, NativeLinuxPolicyWitnessTests*, MechanicalPcContractTests*, TaskPlatformGuidanceTests*, C1729LayeringGuardTests*, TestLaneCategoryGuardTests*. TRX /tmp/c1784-guards/c1784-guards.trx: total=78 executed=78 passed=77 failed=1.

| Class | Passed | Failed |
|---|---:|---:|
| TestClassificationPolicyTests | 41 | 0 |
| TestClassificationGuardTests | 1 | 0 |
| SlowTestRegistryMergeTests | 5 | 0 |
| SlowTestTripwireTests | 2 | 0 |
| NativeLinuxPolicyWitnessTests | 3 | 0 |
| MechanicalPcContractTests | 11 | 0 |
| TaskPlatformGuidanceTests | 5 | 0 |
| C1729LayeringGuardTests | 9 | 0 |
| TestLaneCategoryGuardTests | 0 | 1 |

The failure is TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration. The scan sees five public sealed fixture classes inside a raw string in tests/Antiphon.Tests/ProcessSpawnLimitTests.cs (BareSharedTests, ReadsRootTests, OtherAssignmentTests, ParameterOtherTests, HostTests) without [Category]. That file is identical to base 6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647. origin/master e06bcb3a0c17f4452db8b31ba92a22694ad3d5ab already has [Category("Unit")] on all five. This repair does not patch that file. A land takes master's copy.

## Other gates

check-os-witnesses.ps1 exit 0 against base 6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647 and head 287fc145771e1850a22d681a1c49e44712d2017f. Declaration is the gitignored .antiphon/c1784-os-witness.json. The library matches the scripts/lib restart census. AppHost does not load it, so no Windows restart was run. nativeObligation stays pending for Windows. The entry script's `$linuxHost = $IsLinux` gate was not edited. This report commit is markdown only; the checker was run at the fix SHA.

check-evidence-diff.ps1 -Repository . -BaseRef 6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647 -HeadRef 287fc145771e1850a22d681a1c49e44712d2017f printed `EVIDENCE error reason=shallow_or_unknown_history` and exited 2. `git rev-parse --is-shallow-repository` is true. The mirror was not unshallowed. Both commits are present locally and touch only the ps1 and the cs.

git fetch origin master, then git merge-tree --write-tree origin/master HEAD, exit 0, tree fa2bee84bebadaa46a36572b1cdb55ca871bb891, against origin/master e06bcb3a0c17f4452db8b31ba92a22694ad3d5ab. Not merged.

## Owed

The plan has no PC-n identifiers. The verification-design mutant column for V-1 through V-14, R-1, and R-2 stays pending for method-scoped SourceLanding Mutation. The four Code-stage mutants above were restored and do not discharge that pass. Restart: none. Nothing ran on the desktop. The script's first real server2 lane run is not part of this task. CP-2 through CP-6 are later slices.
