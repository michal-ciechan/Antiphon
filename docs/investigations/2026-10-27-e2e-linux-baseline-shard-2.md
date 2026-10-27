# E2E Linux baseline, shard 2 of 3

Linux server2 baseline of `tests/Antiphon.E2E`. This is not the Windows release gate. No product or test source was changed.

- Candidate: `origin/rc/2026-10-27` at `9f00008facfc48e75555c08df59887019b3f8dfe` (fetched; HEAD matched before this doc).
- Branch: `feat/card-task-f203aea8`. Worktree: `/work/worktrees/task-f203aea8`.
- Client bundle: `npm ci` and `npm run build` in `client/` through build slot `e2e-s2-client-dist` (exit 0). `client/dist/index.html` was absent before that build.
- E2E build: one `dotnet build tests/Antiphon.E2E/Antiphon.E2E.csproj --property:OutputPath=bin-e2e-s2/ --property:UseAppHost=false -nodeReuse:false` through build slot `e2e-s2-build` (exit 0, 0 errors, 39 warnings). DLL: `tests/Antiphon.E2E/bin-e2e-s2/Antiphon.E2E.dll`.
- Each class ran that output with `--no-build` and `--treenode-filter "/*/*/<ClassName>/*"` under its own build slot. No class hit the 25-minute cap.
- The suite started its own isolated session runner on a random loopback port. Production port 17204 was not targeted. `ANTIPHON_HEADED_TESTS`, `ANTIPHON_BROKER_TESTS`, `ANTIPHON_CHANNEL_OUTBOUND_ISOLATED`, and `ANTIPHON_DISTILLER_APPLY_CANARY` stayed unset.
- Playwright: `playwright.ps1 install chromium` exited 0. Cache already had `chromium-1243` and `chromium_headless_shell-1243`. `libglib-2.0.so.0` is present (`/lib/x86_64-linux-gnu/libglib-2.0.so.0`). The 2026-10-17 CARD-1305 D-5 missing-library failure did not reproduce for the browser classes in this shard.
- Shard wall clock for the ten class runs: 12:39:22Z to 12:50:36Z (about 11m 14s), then two short reruns.

## Shard

`dotnet run --no-build -- --list-tests` (no treenode filter) reported 177 tests. That listing prints display names only. Class names are the `TypeName` values from the same `--list-tests` run's diagnostic log (`--diagnostic --diagnostic-output-directory`), which is the TUnit 1.44 discovery record. Constructor suffixes were stripped. Simple names were sorted with ordinal comparison and numbered from 0. This shard is `index % 3 == 2` (10 classes, 36 tests).

| index | class | tests | shard |
|---:|---|---:|---|
| 0 | AgentE2ETests | | |
| 1 | AgentTaskLandDeliveryE2ETests | | |
| 2 | BoardE2ETests | 10 | yes |
| 3 | CardCliE2ETests | | |
| 4 | ChannelE2ETests | | |
| 5 | ChannelOutboundIsolatedTests | 3 | yes |
| 6 | ChromiumRuntimeSmokeTests | | |
| 7 | ContractSnapshotTests | | |
| 8 | CreatedResourceTests | 3 | yes |
| 9 | DelegationPipelineE2ETests | | |
| 10 | DelegationSequencingE2ETests | | |
| 11 | DirectoryAutocompleteE2ETests | 1 | yes |
| 12 | DispatchBaseWarningDeliveryE2ETests | | |
| 13 | DispatchConcurrencySettingsE2ETests | | |
| 14 | IsolatedSessionRunnerTeardownTests | 2 | yes |
| 15 | LandDeliveryFixtureProbeTests | | |
| 16 | OrchestratorE2ETests | | |
| 17 | OutputDistillationApplyCanaryTests | 1 | yes |
| 18 | OutputDistillationCanaryGuardTests | | |
| 19 | PipelineGlanceE2ETests | | |
| 20 | ProjectDeleteE2ETests | 8 | yes |
| 21 | PtyBackendEnvGuardTests | | |
| 22 | ReleaseGateIsolationTests | | |
| 23 | ReleaseGateReportDeliveryTests | 2 | yes |
| 24 | ScheduleCliE2ETests | | |
| 25 | SmokeE2ETests | | |
| 26 | TestClassificationGuardTests | 1 | yes |
| 27 | WorkflowDeleteTests | | |
| 28 | WorkflowOutputTests | | |
| 29 | WorktreeRetirementDeliveryE2ETests | 5 | yes |

Classes ran smallest discovered count first, then ordinal name. The results table stays in shard-index order.

## Results

Counts and wall time are the TRX `Counters` and `Times` (`finish - start`). Exit 8 is the testing-platform code for every selected test skipped. Exit 2 is a failed test.

Shard totals: 33 executed, 27 passed, 6 failed, 3 skipped. No timeouts.

| class | executed | passed | failed | skipped | wall | notes |
|---|---:|---:|---:|---:|---|---|
| BoardE2ETests | 10 | 10 | 0 | 0 | 271.5s | exit 0. Each test 3.4–9.2s. Wall is mostly shared fixture startup. |
| ChannelOutboundIsolatedTests | 0 | 0 | 0 | 3 | 0.8s | exit 8. Opt-in skip, same message on all three. |
| CreatedResourceTests | 3 | 3 | 0 | 0 | 0.7s | exit 0 |
| DirectoryAutocompleteE2ETests | 1 | 1 | 0 | 0 | 60.3s | exit 0. The test itself was 6.4s. |
| IsolatedSessionRunnerTeardownTests | 2 | 2 | 0 | 0 | 0.8s | exit 0. Isolated runner teardown passed. |
| OutputDistillationApplyCanaryTests | 1 | 0 | 1 | 0 | 0.6s | exit 2. Opt-in gate. Rerun matched. |
| ProjectDeleteE2ETests | 8 | 8 | 0 | 0 | 170.7s | exit 0. Each test 1.0–3.1s. |
| ReleaseGateReportDeliveryTests | 2 | 2 | 0 | 0 | 129.1s | exit 0. One case over 30s. |
| TestClassificationGuardTests | 1 | 1 | 0 | 0 | 0.7s | exit 0 |
| WorktreeRetirementDeliveryE2ETests | 5 | 0 | 5 | 0 | 0.7s | exit 2. Windows gate. Rerun matched. |

Tests at or above 30 seconds:

| class | test | duration | outcome |
|---|---|---:|---|
| ReleaseGateReportDeliveryTests | C599_ReportRecovery | 64.6s | Passed |

## Failures

### OutputDistillationApplyCanaryTests

`Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup` failed in 114ms.

```
InvalidOperationException: Both dedicated canary opt-ins are required.
  at DistillerCanaryGuard.ValidateOptIns (Fixtures/DistillerCanaryApproval.cs:43)
  at OutputDistillationApplyCanaryTests.Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup (OutputDistillationApplyCanaryTests.cs:40)
```

The class is `[Explicit]`, `[Category("Headed")]`, `[Category("OptIn")]`, and `[Category("HeadedCanary")]`. `ValidateOptIns` throws unless `ANTIPHON_HEADED_TESTS` and `ANTIPHON_DISTILLER_APPLY_CANARY` are both `1`. Those variables were unset, which is the unattended lane. The Windows assert at `OutputDistillationApplyCanaryTests.cs:46` was not reached. The class rerun failed the same way (1 failed, 0 passed). Cause: environment opt-in gate, not a flake and not a product failure observed on this run.

### WorktreeRetirementDeliveryE2ETests

All five cases failed the same assert. First case, `C459_AdmittedWorkspaceInputReachesRecipient(idle)`, 72ms:

```
ShouldAssertException: OperatingSystem.IsWindows()
    should be
True
    but was
False
  at LandDeliveryFixture.InitializeAsync (Fixtures/LandDeliveryFixture.cs:59)
  at WorktreeRetirementDeliveryE2ETests.C459_AdmittedWorkspaceInputReachesRecipient (WorktreeRetirementDeliveryE2ETests.cs:25)
```

The other four failed at the same fixture line:

- `C459_AdmittedWorkspaceInputReachesRecipient(busy-then-released)`
- `C459_AdmittedWorkspaceInputReachesRecipient(claim-first)`
- `C459_LaunchReservationRecoveryRetainsOwnership(lost-enqueue)` (`WorktreeRetirementDeliveryE2ETests.cs:71`)
- `C459_LaunchReservationRecoveryRetainsOwnership(unknown-start)` (`WorktreeRetirementDeliveryE2ETests.cs:71`)

`tests/test-execution-policy.json` already excludes this class on Linux (CARD-1323): "Requires Windows ConPTY and native FakeGrok". A direct class filter still runs it, and the fixture asserts Windows instead of skipping. The class rerun was 5 failed, 0 passed, same assert. Cause: Windows/Linux gate, not a flake.

### Skipped, not failed

`ChannelOutboundIsolatedTests` (3): `Skipped: F-5 requires all three isolated opt-ins.` from `ChannelOutboundIsolatedTests.cs` `RequireOptIn`. `ANTIPHON_HEADED_TESTS`, `ANTIPHON_BROKER_TESTS`, and `ANTIPHON_CHANNEL_OUTBOUND_ISOLATED` were unset. Cause: environment opt-in (CARD-1305 D-6). Exit 8 because every selected test was skipped.

## What this host ran

Headless Chromium, the Postgres test container, and the isolated session runner all worked for the green classes in this shard (`BoardE2ETests`, `DirectoryAutocompleteE2ETests`, `ProjectDeleteE2ETests`, `ReleaseGateReportDeliveryTests`, `CreatedResourceTests`, `IsolatedSessionRunnerTeardownTests`, `TestClassificationGuardTests`). The two red classes are the existing Windows assert and the distiller-canary opt-in.
