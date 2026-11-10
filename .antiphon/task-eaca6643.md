# CARD-1819 round 2 — Windows lease skips off the Linux selector row

B-1 is closed. The two methods that throw `SkipTestException` off Windows moved out of `RepositoryMutationLeaseTests` into `RepositoryLeaseWindowsBoundaryTests`, carried by kind `repository-lease-windows-boundary` with `"windows": "only"`. The Linux merge row no longer names a skipping class. The census found no other class to move.

Branch `feat/card-task-eaca6643`. Worktree `/work/worktrees/task-eaca6643`. Original Code task `43bdb0ea`. Repair continues `b6b6e9075a13f7577d3fefaae05b5a8d6a931e10`. Restart: none. Nothing deployed. No land-graph update.

## SHAs

- Red witness: `c1272ef7a7722b55dc5da473efb9482c923448ec`
- Fix and tested code: `d196ac954ba7abe9c2a5147f5ab1215826c32872`
- `tests/test-execution-policy.json` unchanged, so `policyHash` and `PinnedInputDigest` were not resealed.

## What changed

Option (b). A per-method Linux list (option a) would have kept 21 classes and raised the Linux filter count to 6, and the two Windows methods would have been a second windows filter, so `0f76e7b0a9` would have been 21/6/2. The windows-only class stays inside the existing `tests/Antiphon.Tests` windows row, so the range stays 21/5/1.

- `tests/Antiphon.Tests/Infrastructure/RepositoryLeaseWindowsBoundaryTests.cs` holds `C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput`, `OutputHoldingGit`, `C448_V13_WindowsJunctionAndOtherProcessShareTheLease`, and its own `RunChildAsync`. Bodies are the ones that were in `RepositoryMutationLeaseTests`.
- `RepositoryMutationLeaseTests` keeps its Linux cases, including `C448_C24_KilledWorkerLeavesLiveGitChildFenced`, and keeps `RunChildAsync`.
- Kind `repository-lease-windows-boundary` uses the same three globs as `repository-lease-reflected-constructors` and `"windows": "only"`. The Linux kind still names `RepositoryMutationLeaseTests` and has no windows field.
- `C1819_landing_git_selects_repository_mutation_lease_tests` requires the new class on the windows lane and absent from Linux filters. `alsoWindowsOnly` is checked when the primary class stays on Linux.
- No script change and no doc change. The `windows: only` contract is already in `docs/testing-and-build.md`.

## Census

`C1819_linux_kind_classes_do_not_skip_off_windows` walks every class named by a kind that is not `windows: only` or `windows: required`. A method is a violation when it skips or asserts Windows off this host: `!IsWindows` / `!IsOSPlatform(Windows)` then-branch throws `SkipTestException` or calls `Skip.Test`; the else-branch of a positive `IsWindows()` check skips; or the method asserts `IsWindows()` true. A `!IsWindows` branch that only calls `SetUnixFileMode`, a skip that runs on Windows (`if (IsWindows()) throw SkipTestException`), and a `!IsLinux` skip are not violations.

Red at `c1272ef7a`, before the move. Slot `3b09d01a-b8a3-4d4d-bf22-5eda36d7ee32` waited=0s held=15s. total 1, failed 1, succeeded 0, skipped 0. The two violations were `RepositoryMutationLeaseTests/C448_V13_WindowsJunctionAndOtherProcessShareTheLease` and `RepositoryMutationLeaseTests/C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput`. No other class. The in-test mutant replaces `SkipTestException` with `Exception` and expects `Should.Throw<ShouldAssertException>`.

Green at `d196ac954`. Same method plus `C1819_landing_git_selects_repository_mutation_lease_tests`. Slot `a579d402-ee84-401f-b51b-5e79d5efd750` waited=0s held=20s. total 2, failed 0, succeeded 2, skipped 0, duration 16s 659ms. TRX `/tmp/c1819-census-green/census.trx`. The landing witness's mutant replaces `RepositoryMutationLeaseTests` with `DroppedC1819Selection`.

Build of `tests/Antiphon.Tests`, `UseAppHost=false`, native output. Slot `ab9fc261-9309-4488-9ece-11601dbe5a88` waited=0s held=181s maxcpucount=6. 0 errors, 1989 warnings. Time Elapsed 00:02:59.61.

## Ranges (`-MergeSubset -Json`)

classes / Linux filters / windows filters:

| Range | Counts |
|---|---|
| no range | 15 / 5 / 0 |
| `ff7cceaf4f65bed039f5b7f11dd726d27e762607` | 67 / 7 / 0 |
| `dcb973d896f1d12776a796b71466977b22737b79` | 18 / 5 / 0 |
| `a54a6d4f755fa139b41fd7dc8aef456d3d2c0975` | 17 / 6 / 0 |
| `ae5b545d9ac9091f42847734b88bf8e202cf2814` | 73 / 8 / 2 |
| `0f76e7b0a9379345684dbb4f5452073e53eb82f4` | 21 / 5 / 1 |

land-1 and land-2 are unchanged on every range:

- `/*/*/(AgentTaskLandHeldNotificationTests*)|(AgentTaskLandNotificationParkFollowUpTests*)|(AgentTaskLandNotificationPersistenceTests*)|(AgentTaskLandNotificationReconcileLoadTests*)/*`
- `/*/*/(AgentTaskLandNotificationRecoveryTests*)|(AgentTaskLandNotificationRescanTests*)|(AgentTaskLandReceiptWatermarkSafetyTests*)|(AgentTaskLandReceiptWatermarkTests*)/*`

`0f76e7b0a9` Linux `CP-merge-subset-tests` still contains `(RepositoryMutationLeaseTests*)` and does not name the two skip methods or `RepositoryLeaseWindowsBoundaryTests`. Its one windows row is `CP-merge-windows-tests` `/*/*/(RepositoryLeaseWindowsBoundaryTests*)|(ServerImagePushCredentialTests*)|(ServerRepositoryMirrorScriptTests*)/*`.

## import --plan

Linux `filters` only, `EstimatedMinutes` 5 (the documented migration row's 60 is the existing timeout advisory, not this import). 0 warnings, including 0 land-class warnings.

| Range | Rows | Slot | waited | held |
|---|---:|---|---:|---:|
| none | 5 | `81c8a917-27d3-4022-af58-ce84334711c8` | 0s | 1s |
| ff7cceaf | 7 | `f5b2eb4b-1292-4010-a7d3-abb2b6b59145` | 0s | 2s |
| dcb973d8 | 5 | `bb56ae55-75de-406b-afdd-35d278c60160` | 0s | 2s |
| a54a6d4f | 6 | `5eda9b48-397a-4446-beae-b83c45f50124` | 0s | 2s |
| ae5b545d9 | 8 | `aa7f98f3-590c-42eb-a35d-72b5b2d31e49` | 0s | 2s |
| 0f76e7b0a9 | 5 | `8089a2b4-1747-44e6-a2ca-aa0ad2a195f1` | 0s | 1s |

## Ordinary runs at `d196ac954`

Touched `/*/*/(ImpactedClassesSelectorTests*)|(MergeHighRadiusSubsetTests*)/*`. Slot `cd7e6c69-5f8a-47d4-91e8-64ebc6f3a0f7` waited=0s held=494s. total 46, failed 0, succeeded 46, skipped 0, duration 8m 10s 329ms.

Guards in one run: `TestClassificationPolicyTests`, `TestClassificationGuardTests`, `SlowTestRegistryMergeTests`, `SlowTestTripwireTests`, `NativeLinuxPolicyWitnessTests`, `MechanicalPcContractTests`, `TaskPlatformGuidanceTests`, `C1729LayeringGuardTests`, `TestLaneCategoryGuardTests`. Slot `09d66bab-79d8-487a-bd99-5b8ce52a7f07` waited=0s held=128s. total 78, failed 0, succeeded 78, skipped 0, duration 2m 05s 416ms.

`RepositoryLeaseWindowsBoundaryTests` was not executed on Linux. Both methods skip off Windows; a Linux checkpoint row that included them would be red. They are owed on the native Windows lane.

`scripts/check-evidence-diff.ps1 -BaseRef b6b6e9075a13f7577d3fefaae05b5a8d6a931e10 -HeadRef d196ac954ba7abe9c2a5147f5ab1215826c32872` exit 0. commits=2 entries=0 violations=0. The mirror is not shallow.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` exit 0. Tree `00a9150b34560f099bddef0bf722d346d49534bb` against `origin/master` `4a600e9af54c297d8527f472e47fdbc1e9b6f652`. No conflicts.

## Owed on the native Windows lane

`windows: only`: `RepositoryLeaseWindowsBoundaryTests` (`C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput`, `C448_V13_WindowsJunctionAndOtherProcessShareTheLease`), `CapabilityScriptTests`, `DelegateScriptCapabilityTests`, `AgentTaskLandDeliveryE2ETests`.

`windows: required` (Linux row still runs; the Windows causes stay owed): `ServerImagePushCredentialTests`, `ServerRepositoryMirrorScriptTests`, `Card1761RunnerReadPollTests`, `C0824WorkspaceSlotRoleGateTests`, `LandGraphLandRefreshTests`, `Card1277PtyHostFixtureTests`, `Card1773TranscriptPromptStampMigrationTests`, `TranscriptHotPathMigrationTests`.

## Pending

Every PC stays pending for method-scoped SourceLanding Mutation. The in-test mutants above are the witnesses for this repair; they are not a SourceLanding cycle. Next is review. Round 3 is blockers only.
