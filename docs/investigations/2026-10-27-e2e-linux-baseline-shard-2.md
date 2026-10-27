# E2E Linux baseline, shard 2 of 3

Linux server2 baseline of `tests/Antiphon.E2E`. This is not the Windows release gate. Windows-gated classes are recorded as skip or fail, not repaired. No product or test source was changed.

- Candidate: `origin/rc/2026-10-27` at `9f00008facfc48e75555c08df59887019b3f8dfe` (fetched; HEAD matches).
- Branch: `feat/card-task-f203aea8`.
- Worktree: `/work/worktrees/task-f203aea8`.
- Client bundle: `npm ci` and `npm run build` in `client/` through build slot `e2e-s2-client-dist` (exit 0). `client/dist/index.html` was absent before that build.
- E2E build: one `dotnet build tests/Antiphon.E2E/Antiphon.E2E.csproj --property:OutputPath=bin-e2e-s2/ --property:UseAppHost=false -nodeReuse:false` through build slot `e2e-s2-build` (exit 0, 0 errors, 39 warnings). DLL: `tests/Antiphon.E2E/bin-e2e-s2/Antiphon.E2E.dll`.
- Each class run uses that compiled output with `--no-build` and `--treenode-filter "/*/*/<ClassName>/*"` under a build slot. Cap: 25 minutes, then the class is stopped and the run continues.
- The suite starts its own isolated session runner on a random loopback port. Production runner port 17204 was not targeted. Headed, broker, and channel-outbound opt-in variables were left unset.
- Playwright: `playwright.ps1 install chromium` exited 0. Browser cache already had `chromium-1243` and `chromium_headless_shell-1243`. `libglib-2.0.so.0` is present on this host (`/lib/x86_64-linux-gnu/libglib-2.0.so.0`), unlike the 2026-10-17 CARD-1305 D-5 note.

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

Execution order is smallest discovered count first, then ordinal name, so a 120-minute budget records more classes before the larger browser classes. The table below stays in shard-index order.

## Results

Runs are in progress. Counts and wall time come from the TRX. Tests at or above 30 seconds are listed under the class.

| class | executed | passed | failed | skipped | wall | notes |
|---|---:|---:|---:|---:|---|---|
| BoardE2ETests | | | | | | pending |
| ChannelOutboundIsolatedTests | | | | | | pending |
| CreatedResourceTests | | | | | | pending |
| DirectoryAutocompleteE2ETests | | | | | | pending |
| IsolatedSessionRunnerTeardownTests | | | | | | pending |
| OutputDistillationApplyCanaryTests | | | | | | pending |
| ProjectDeleteE2ETests | | | | | | pending |
| ReleaseGateReportDeliveryTests | | | | | | pending |
| TestClassificationGuardTests | | | | | | pending |
| WorktreeRetirementDeliveryE2ETests | | | | | | pending |

## Failures

None recorded yet.
