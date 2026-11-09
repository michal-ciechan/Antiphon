# CARD-1819 round 1 repair

`"windows": "only"` keeps `AgentTaskLandDeliveryE2ETests`, `CapabilityScriptTests`, and `DelegateScriptCapabilityTests` out of every Linux filter and in `windowsFilters`. The ProtectedData added glob no longer matches the table. Touched classes 45/45 and guards 78/78 on the fix.

Branch `feat/card-task-c649b7a2`. Worktree `/work/worktrees/task-c649b7a2`. Repair base `025fc42bf4a8f984e48c3a7808fa958d3b07b8fc` (origin/feat/card-task-43bdb0ea).

| role | sha |
|---|---|
| earlier witness (census setup, not the product red) | `89317fcc95c7d3028e44446d63f0b54bf07c0d99` |
| red | `8d46c481775d7eabe82999ae6ea9567fd6c5c2c9` |
| fix / tested | `7a83645762962f0ee92a2525ed188a50e72bc862` |

The report commit is docs-only. Tested code is the fix SHA.

## Change

`scripts/impacted-classes.ps1` accepts `"windows": "required"` or `"windows": "only"` and refuses anything else with `windows must be 'required' or 'only'` (the old needle `windows must be 'required'` still matches). `required` still runs on Linux and is copied into `windowsFilters`. `only` is listed in `kinds` and `windowsFilters` and is omitted from Linux `classes` / `filters`. No other kind changed from `required`.

`tests/impacted-classes.json`:

- `dpapi-protected-data`: `"windows": "only"`, added glob `tests/Antiphon.Tests/**/*.cs` (was `tests/**`).
- `e2e-land-delivery`: `"windows": "only"`. `e2e-land-delivery-probe` (`LandDeliveryFixtureProbeTests`) stays a Linux kind.

Docs: one sentence each in `docs/testing-and-build.md` and `docs/orchestration-loop.md`. The malformed-value phrase now names `required` or `only`. Fixed filter strings and the Merge checkpoint table were not rewritten. `tests/test-execution-policy.json` was not edited. policyHash and PinnedInputDigest were not refreshed.

## Red

At `8d46c48177`, filter `/*/*/(ImpactedClassesSelectorTests*)|(MergeHighRadiusSubsetTests*)/*` was not the whole class: the five new or retargeted methods. TRX `/tmp/card-1819-red2/card-1819-red.trx`. Counters total 5, executed 5, passed 0, failed 5. Test host: failed 5, succeeded 0, skipped 0, duration 43s 414ms. Slot `4535e603-b051-4fb5-80ca-26b84064f0e2` waited=0s held=47s. Rebuild slot `b2dbf05c-a46c-4ee3-86fb-cf4d3397b4b8` waited=0s (combined shell 229s).

Failures, all the intended product red:

- `C1801_non_merge_land_notification_filter_names_every_class`: Linux e2e filter still contained `AgentTaskLandDeliveryE2ETests`.
- `C1819_land_notification_host_selects_the_delivery_e2e_and_the_linux_probe`: Linux filters contained `AgentTaskLandDeliveryE2ETests`.
- `C1819_capability_script_selects_the_dpapi_classes_on_windows`: Linux filters contained `CapabilityScriptTests`.
- `C1819_protected_data_literal_does_not_select_the_table`: a `ProtectedData` line in `tests/impacted-classes.json` selected `dpapi-protected-data`.
- `C1819_windows_only_lane_stays_out_of_linux_filters`: `"windows": "only"` was refused (`windows must be 'required'`).

`89317fcc95` was the first witness push. Its protected-data case failed because the scratch repo had no class census (`AppDbContextModelTests` was not found), so it was not the product red. `8d46c48177` adds the census stubs and a one-kind table. No assertion was loosened.

Each witness mutates the selected name to `DroppedC1819Selection` and `ShouldNotBe` guards the replacement. The green run includes those `Should.Throw` mutants.

## Green (tested SHA)

Build `tests/Antiphon.Tests` `UseAppHost=false`, native output. Slot `4c4527e4-05c8-4893-9ea5-e454ad75ab38` waited=0s held=199s maxcpucount=6. 0 errors, 1989 warnings. Time Elapsed 00:03:17.93.

Touched classes `/*/*/(ImpactedClassesSelectorTests*)|(MergeHighRadiusSubsetTests*)/*`. Slot `d491429a-a000-415d-a151-04d360f81e65` waited=0s held=514s maxcpucount=6. TRX `/tmp/card-1819-touched/card-1819-touched.trx`. total 45, failed 0, succeeded 45, skipped 0, duration 8m 31s 475ms.

Guards in one run: `TestClassificationPolicyTests`, `TestClassificationGuardTests`, `SlowTestRegistryMergeTests`, `SlowTestTripwireTests`, `NativeLinuxPolicyWitnessTests`, `MechanicalPcContractTests`, `TaskPlatformGuidanceTests`, `C1729LayeringGuardTests`, `TestLaneCategoryGuardTests`. Slot `de799011-817c-4821-88b7-da57b2eafa95` waited=45s held=169s maxcpucount=6. TRX `/tmp/card-1819-guards/card-1819-guards.trx`. total 78, failed 0, succeeded 78, skipped 0, duration 2m 46s 622ms.

`-Audit` on the fix: ok, emptyGlobs empty, missingClasses empty.

## Ranges (`-MergeSubset -Json`)

Land-1 and land-2 match the documented strings on every input. Linux filters contain none of `AgentTaskLandDeliveryE2ETests`, `CapabilityScriptTests`, `DelegateScriptCapabilityTests`.

| input | classes / filters | windows filters | note |
|---|---|---|---|
| no range | 15 / 5 | 0 | unchanged |
| `ff7cceaf4f65bed039f5b7f11dd726d27e762607` | 67 / 7 | 0 | migrations, unchanged |
| `dcb973d8` `dcb973d896f1d12776a796b71466977b22737b79` | 18 / 5 | 0 | unchanged |
| `a54a6d4f` `a54a6d4f755fa139b41fd7dc8aef456d3d2c0975` | 17 / 6 | 0 | unchanged |
| `ae5b545d9` `ae5b545d9ac9091f42847734b88bf8e202cf2814` | 73 / 8 | 2 | was 74; the dropped class is `AgentTaskLandDeliveryE2ETests`. Filter count stays 8. land-4 is 2 classes. |
| `0f76e7b0a9` `0f76e7b0a9379345684dbb4f5452073e53eb82f4` | 21 / 5 | 1 | unchanged. `RepositoryMutationLeaseTests` stays on Linux. |

`ae5b545d9` windows filters: `/*/*/(AgentTaskLandDeliveryE2ETests*)/*` on `tests/Antiphon.E2E`, and `/*/*/(Card1773TranscriptPromptStampMigrationTests*)|(TranscriptHotPathMigrationTests*)/*` on `tests/Antiphon.Tests`. Linux e2e row is `/*/*/(ContractSnapshotTests*)|(LandDeliveryFixtureProbeTests*)/*`. Non-merge path `server/Application/Services/AgentTaskLandNotificationService.cs` Linux e2e is `/*/*/(LandDeliveryFixtureProbeTests*)/*` and windows is the delivery class. `scripts/capability.ps1` windows is `/*/*/(CapabilityScriptTests*)|(DelegateScriptCapabilityTests*)/*` and those names are absent from the Linux filters.

`import --plan` of those Merge rows: ae5b 8 rows, slot `e9ec264c-4967-4663-99c7-8d300e70fc22` waited=0s held=4s, 0 warnings. dcb 5 rows, slot `608efbd8-c628-43ea-aa82-37c8e2d3b686` waited=0s held=4s, 0 warnings. Generated plans and yaml were not committed.

## Census

Named classes that skip or assert Windows, and what this repair did:

- `CapabilityScriptTests` `Skip.Test` off Windows (`CapabilityScriptTests.cs:25`). `DelegateScriptCapabilityTests` `Skip.Test` off Windows (`DelegateScriptCapabilityTests.cs:190`). Whole classes are `windows: only`. Linux loses the methods that do run there; a skipped method fails a checkpoint row.
- `AgentTaskLandDeliveryE2ETests` goes through `LandDeliveryFixture.InitializeAsync`, and `LandDeliveryFixture.cs:59` is `OperatingSystem.IsWindows().ShouldBeTrue()`. `windows: only`. `LandDeliveryFixtureProbeTests` does not call `InitializeAsync` and stays on the Linux e2e row.
- `Card1277PtyHostFixtureTests`, `Card1773TranscriptPromptStampMigrationTests`, `TranscriptHotPathMigrationTests`: no `Skip.Test` and no `IsWindows().ShouldBeTrue()`. They stay `"windows": "required"` (Linux row still runs, and they are owed on Windows).
- `RepositoryMutationLeaseTests`: `C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput` (line 21) and `C448_V13_WindowsJunctionAndOtherProcessShareTheLease` (line 690) throw `SkipTestException` off Windows. The other `IsWindows` branches set a Unix file mode and run on Linux. The class stays on the Linux row. Moving the whole class to `only` would drop that Linux proof and change `0f76e7b0a9` from 21/5. A method split would change the filter count from 5 to 6. Round 1 accepted this row. Those two methods still skip if that Linux row is executed.
- `IsWindows().ShouldBeTrue()` also exists in `OutputDistillationApplyCanaryTests`, `WorktreeLockDiagnosticsWindowsTests`, and `C1008PlatformContractTests`. None of those classes is a selector kind. No kind was added for them.

## Owed on Windows

Not run on this Linux host. A Linux run of these filters is not the owed lane.

- `Card1277PtyHostFixtureTests`
- `Card1773TranscriptPromptStampMigrationTests`
- `TranscriptHotPathMigrationTests`
- `CapabilityScriptTests`
- `DelegateScriptCapabilityTests`
- `AgentTaskLandDeliveryE2ETests`

`LandDeliveryFixtureProbeTests` is the Linux half and is not a windows row.

## Evidence

`scripts/check-evidence-diff.ps1 -BaseRef 025fc42bf4a8f984e48c3a7808fa958d3b07b8fc -HeadRef HEAD` at the tested SHA: exit 2, `EVIDENCE error reason=shallow_or_unknown_history`. This server2 mirror is shallow. That is not a product failure. `git diff --name-only 025fc42bf4a8f984e48c3a7808fa958d3b07b8fc..7a83645762`:

- `docs/orchestration-loop.md`
- `docs/testing-and-build.md`
- `scripts/impacted-classes.ps1`
- `tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs`
- `tests/impacted-classes.json`

`tests/test-execution-policy.json` is not in that diff.

`git fetch origin master` then `git merge-tree --write-tree origin/master 7a83645762962f0ee92a2525ed188a50e72bc862`: exit 0, tree `eb14042350c27e177f159bcd94d7f6aea376161d`. `origin/master` is `e06bcb3a0c17f4452db8b31ba92a22694ad3d5ab`. No conflicts. Not merged. The tip recheck is in the caller summary if the report commit moves HEAD.

## Positive controls

Pending for method-scoped SourceLanding Mutation. The in-test `DroppedC1819Selection` replacement is the suite's existing mutant, not a Mutation cycle. Restart: none.
