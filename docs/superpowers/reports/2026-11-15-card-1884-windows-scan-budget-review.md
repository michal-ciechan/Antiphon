# CARD-1884 Final Review, round 1

Verdict: CLEAN. No product blocker.

Reviewed source: `feat/card-task-f9e020bf` at `8951eb01d00716052df3c4e37c22655eebb4d8e9`.
Owner: Debug task `f9e020bf-a511-4ab3-97df-bc69206166e0` (Claude Code, Succeeded, card CARD-1514, branch `feat/card-task-f9e020bf`). This review ran as task `cf50eda6-6a22-4f32-903d-74dfb0bc4b5c` on CARD-1884.
Base: `4ce55f36794f3e2fa904d56ba980f4182d89ab3a`.
Commits: `f1b334ad46595332fddd14b5514f4d2d61beb363` (red witness), `d868e8b59a4400ae70ec4399a6302d5443adc878` (budget after the snapshot), `8951eb01d00716052df3c4e37c22655eebb4d8e9` (why-missed note).
Diff: `src/Antiphon.SessionRunner/HostProcessTable.cs`, `tests/Antiphon.SessionRunner.Tests/HostTopConsumerTests.cs`, `docs/investigations/2026-11-10-why-missed-sr-profile-199-hosttop.md`.

## Product

The 10 ms `ScanBudget` is constructed after `SnapshotNames` returns (`HostProcessTable.cs` lines 515–529). The toolhelp capture and `Process32NextW` walk are finite and follow the host process count. They sit outside the thread-CPU budget and outside the stalled-clock wall fallback. That is the repair for rc `b7812833`: on a desktop of about 500 processes the capture (~8 ms) plus the walk (~9 ms) spent the whole budget inside the walk, so `Read` returned no rows. There is no second cap on the walk. `ScanCap` (512) applies only to the per-process handle reads chosen after the walk. The scan stays off the sampler tick, on one thread named `host-top-consumer-scan`, joined within `ScanJoinBudget` (2 s), and the snapshot handle is disposed when the walk is cancelled. A healthy `QueryThreadCycleTime` clock still stops those handle reads at 10 ms. A missing or stalled clock still fails closed for the read phase (`UnreadableClockProcessCap` 32, plus the wall fallback). A late publish is still dropped when `_epoch` changes (`HostStatsSamplerService.ScanLoop`, lines 229–236). That sampler file is outside this diff.

Retained pids are still chosen first. When `Exhausted` becomes true, `LastReadTruncated` is set and `Read` returns the rows already collected. `Windows_snapshot_capture_does_not_spend_the_scan_budget` and `Windows_reads_stop_at_the_scan_budget_after_the_retained_pid` cover that. An empty list is still returned when the snapshot is null, when snapshot creation throws `IOException`, `UnauthorizedAccessException`, or `InvalidOperationException`, when no pids are present, when every `TryTimes` fails, and when `maxProcesses` is not positive. `HostTopConsumerTracker.Observe` replaces the published ranking with the rows it is given, so an empty list publishes no consumers. The production failure, an empty list because the budget expired during the walk, is gone. A clock jump of at least 10 ms during the id-list copy and sort could still stop before the first handle read. That gap is the sort, and it is not the failure this change repairs.

`IWindowsProcessSource` is internal. The public `WindowsHostProcessTable` constructor wires `ToolhelpProcessSource` plus `HostStatsSamplerService.ThreadCpuMilliseconds` and `ScanCpuBudget`. Off Windows, `Snapshot` returns null. An invalid snapshot handle is disposed. `Toolhelp.Dispose` disposes the snapshot handle. `TryTimes` closes the `OpenProcess` handle in `finally`. Cancellation during the walk throws out of the `using`. `Read` does not swallow `OperationCanceledException`.

## Verification

Runs were leased `scripts/build-slot.ps1` commands with `UseAppHost=false` and the worktree's native output. Plan CP-1 and CP-2 name `bin-c1884-host/` and `bin-c1884-guards/`. Those custom outputs were not used. `reviewedSourceClean` is false. Positive controls stay pending. Mutation stays waived (operator 2026-11-12). Native Windows execution of `Production_factory_observes_this_process_on_windows` stays owed; this Linux host skipped it. The Debug task's native runs remain that witness.

SessionRunner at `8951eb01`, filter `/*/*/(HostTopConsumerTests*)|(HostStats*)|(C1514HostLoad*)|(C1514SamplerTickCpuTests*)|(ProcessSpawnLimitTests*)/*`, lease `dd6d668d-287e-4f27-831f-9780a035aa27`: total 98, failed 1, succeeded 93, skipped 4, duration 9m 30s 758ms. The failure was `C1514HostLoadWireTests.HistoryRoutePreservesCursorAndBounds` timing out at its 10 s class limit under host load. Alone, lease `415d1c1d-58bc-4c7f-b797-c026b5f29397`: total 1, failed 0, duration 3s 576ms. Skips: `Windows_ProductionFactoryPreservesHostSamples`, `Windows_probe_reads_live_system_times`, `Production_factory_observes_this_process_on_windows`, `Windows_SamplerOverheadStaysWithinBudget`.

Exact Linux runs of the two scan-hold siblings, five each, all passed. `Stop_while_scan_is_held_discards_the_late_result`: 3s 035ms, 3s 150ms, 2s 918ms, 2s 856ms, 2s 837ms. `Dispose_twice_while_scan_is_held_drops_the_late_result`: 770ms, 1s 051ms, 766ms, 1s 054ms, 1s 069ms. Filed as advisory CARD-1949. They do not block.

Reviewed-SHA guards, `--no-build`, all exit 0: `TestClassificationPolicyTests` 42/0 (lease `91d58e0a-82c7-4238-9d70-ef875e4a3adc`, 57s 118ms); `TestClassificationGuardTests` 1/0 (`d2cae2be-a5cf-419f-bd91-7491ffe12255`, 11s 583ms); `SlowTestRegistryMergeTests` 5/0 (`bd06a4c0-8a3e-4508-9c7e-dd8fae8c0c24`, 16s 860ms); `SlowTestTripwireTests` 2/0 (`bc727152-8f6d-428b-b1ba-3655e8fb14a4`, 8s 171ms); `NativeLinuxPolicyWitnessTests` 3/0 (`44f1e80b-b648-4957-b669-90d89881f508`, 7s 757ms).

Composed scratch tree `/tmp/card-1884-review-compose` is `origin/master` `1bd7b2ff62411fe0f784df425d4949c850ad5cd7` plus cherry-picks of the three commits, tip `3521981e6af1a6e6801db857b5fe7f1ccc1884d0`. `scripts/regenerate-derived-impact-index.ps1` twice (driver build, then `-NoBuild`): both printed `derived-impact-index unchanged` (leases `bb1b1d6e-33b8-4e30-b49b-02e146e3e272`, `2a5050f1-16b4-4524-8343-9df8689880a1`). Recomputed `policyHash` `b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d` and `PinnedInputDigest` `26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e`. `LegacyExclusionDigest` `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a` is the constant `NativeLinuxPolicyWitnessTests` checks; that class passed on both trees.

`Antiphon.Tests` build on the composed tree: 0 errors, 2094 warnings, 3m 04.58s, lease `79dc8449-91c2-4563-90bb-5d03ffe899a1`. Load at the union start: `12.53 17.21 17.06`. Serial union, each under 10 minutes, all exit 0:

| Class | Total | Failed | Succeeded | Skipped | Duration | Lease |
|---|---:|---:|---:|---:|---|---|
| TestClassificationPolicyTests | 42 | 0 | 42 | 0 | 1m 00s 494ms | dc140c70-9110-4dd2-8934-e88d3be188bd |
| TestClassificationGuardTests | 1 | 0 | 1 | 0 | 6s 544ms | 8683843c-c2b6-4860-ae38-044c00ead4ff |
| SlowTestRegistryMergeTests | 5 | 0 | 5 | 0 | 10s 054ms | 8085d7d5-71fb-437c-b069-d0ee4b43ab0d |
| SlowTestTripwireTests | 2 | 0 | 2 | 0 | 4s 878ms | 7ef2f007-5850-428e-895a-2e26dd681b47 |
| NativeLinuxPolicyWitnessTests | 3 | 0 | 3 | 0 | 7s 809ms | 27e7450d-9a34-49d6-b4bf-d963eeb018e9 |
| DerivedImpactIndexTests | 27 | 0 | 27 | 0 | 3m 11s 976ms | 6f92c663-f6aa-4741-92bb-4a46cb05b57b |
| MechanicalPcContractTests | 11 | 0 | 11 | 0 | 4s 470ms | 904af5af-a752-4529-b97f-976919031bfc |
| TaskPlatformGuidanceTests | 5 | 0 | 5 | 0 | 5s 279ms | 20d02378-01ed-4237-96c8-caa8473f7fff |
| C1729LayeringGuardTests | 11 | 0 | 11 | 0 | 14s 818ms | 0a5724f4-80d7-45d3-977b-d2a5fcd3e3c4 |
| TestLaneCategoryGuardTests | 1 | 0 | 1 | 0 | 5s 333ms | ac463525-04e4-46c7-9e05-dc5b0a782fc6 |
| WindowsUnitPlatformAdmissionTests | 7 | 0 | 6 | 1 | 29s 114ms | 8fd235cc-c306-44cd-b779-3c83fcd68460 |

The one skip is `C1210_Windows_linux_only_outcomes_are_exact` (CARD-1210 Windows placement). Union total 115, failed 0, succeeded 114, skipped 1.

`scripts/impacted-classes.ps1 -Range 4ce55f36..8951eb01`: kinds empty, filter `/*/*/(HostTopConsumerTests*)/*`. `-MergeSubset` reports `migrations=False`. Downgrade selector count 0.
`scripts/check-evidence-diff.ps1`: commits=3, entries=0, violations=0, exit 0.
`git merge-tree --write-tree origin/master HEAD`: tree `074fc07dd0a7ca11dcd69d3ada5a9f015c60f8dc`, exit 0, against `origin/master` `1bd7b2ff62411fe0f784df425d4949c850ad5cd7`.

Plan CP-2 also names `HostTopConsumerIngestTests` and `ProcessSpawnLimitTests` in `Antiphon.Tests`. Those classes are outside this diff and outside the brief's union, so they were not re-run. The leased SessionRunner selection covers the CP-1 class prefixes (98 executed, floor 69) with the one load timeout retried green.

## Advisories

CARD-1949 records the two scan-hold tests. On the loaded Windows desktop during Debug they failed 1 of 5 via `entered.Wait(2s)`, before and after this fix, on `ScriptedTable`. Linux did not reproduce that.

`HistoryRoutePreservesCursorAndBounds` timed out once inside the 98-test run and passed alone. That is a load timeout of an unrelated CARD-1514 wire test. It was not filed.

The walk's cost follows the process count, on one cancellable background thread every scan interval (default 5 s). The why-missed note already records the measured desktop cost. No second card.

Land still runs `scripts/regenerate-derived-impact-index.ps1` before `DerivedImpactIndexTests`. On this master the composed regeneration left the bytes unchanged, and merge-tree will not flag a stale index by itself.

PC-1 through PC-4 stay pending. The native Windows production-factory witness stays owed.
