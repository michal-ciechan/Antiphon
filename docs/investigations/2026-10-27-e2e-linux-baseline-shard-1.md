# E2E Linux baseline, shard 1 of 3

Host: server2 Linux (Debian 12, linux-x64). This is a baseline, not the Windows release gate. Windows-gated classes are reported as skip or fail. No product or test source was changed.

## Checkout and build

- Branch `feat/card-task-9d128840` at `9f00008facfc48e75555c08df59887019b3f8dfe`, equal to fetched `origin/rc/2026-10-27`.
- Client bundle rebuilt with `npm ci` and `npm run build` in `client/` under build slot `e2e-shard1-client-dist` (lease granted, exit 0). `client/dist/index.html` is from that build.
- E2E assembly built once under build slot `e2e-shard1-build`: `dotnet build tests/Antiphon.E2E/Antiphon.E2E.csproj --property:OutputPath=bin-e2e9d12/ --property:UseAppHost=false -nodeReuse:false`. Exit 0, 0 errors, 39 warnings. Output `tests/Antiphon.E2E/bin-e2e9d12/Antiphon.E2E.dll`.
- Playwright Chromium 153.0.8010.12 (build v1243) installed with that output's `playwright.ps1`. Browsers are under `/home/app/.cache/ms-playwright/`.
- Each class runs from that compiled output with `--no-build` and filter `/*/*/<ClassName>/*`, under a build slot. The fixture starts its own isolated session-runner on a random loopback port and a Testcontainers Postgres. It does not use the production runner.
- Class cap: 25 minutes. A timeout is recorded and the next class continues. A class with failing tests is rerun once. A timeout is not rerun.

## Class split

`dotnet run --no-build -- --list-tests` reported 177 tests. Class names are the diagnostic `TypeName` values (no nested test types). Sorted with `LC_ALL=C` (ordinal). Numbered from 0. This shard is `index % 3 == 1` (10 classes).

| index | mod 3 | class | shard |
|---:|---:|---|---|
| 0 | 0 | AgentE2ETests | |
| 1 | 1 | AgentTaskLandDeliveryE2ETests | 1 |
| 2 | 2 | BoardE2ETests | |
| 3 | 0 | CardCliE2ETests | |
| 4 | 1 | ChannelE2ETests | 1 |
| 5 | 2 | ChannelOutboundIsolatedTests | |
| 6 | 0 | ChromiumRuntimeSmokeTests | |
| 7 | 1 | ContractSnapshotTests | 1 |
| 8 | 2 | CreatedResourceTests | |
| 9 | 0 | DelegationPipelineE2ETests | |
| 10 | 1 | DelegationSequencingE2ETests | 1 |
| 11 | 2 | DirectoryAutocompleteE2ETests | |
| 12 | 0 | DispatchBaseWarningDeliveryE2ETests | |
| 13 | 1 | DispatchConcurrencySettingsE2ETests | 1 |
| 14 | 2 | IsolatedSessionRunnerTeardownTests | |
| 15 | 0 | LandDeliveryFixtureProbeTests | |
| 16 | 1 | OrchestratorE2ETests | 1 |
| 17 | 2 | OutputDistillationApplyCanaryTests | |
| 18 | 0 | OutputDistillationCanaryGuardTests | |
| 19 | 1 | PipelineGlanceE2ETests | 1 |
| 20 | 2 | ProjectDeleteE2ETests | |
| 21 | 0 | PtyBackendEnvGuardTests | |
| 22 | 1 | ReleaseGateIsolationTests | 1 |
| 23 | 2 | ReleaseGateReportDeliveryTests | |
| 24 | 0 | ScheduleCliE2ETests | |
| 25 | 1 | SmokeE2ETests | 1 |
| 26 | 2 | TestClassificationGuardTests | |
| 27 | 0 | WorkflowDeleteTests | |
| 28 | 1 | WorkflowOutputTests | 1 |
| 29 | 2 | WorktreeRetirementDeliveryE2ETests | |

## Results

Counts are from each class TRX. Wall time is the TRX `Times` start-to-finish span. A class with failures is rerun once. Exit 8 is the testing-platform code for a run whose tests were all skipped. No finished class had a test at or over 30 seconds. WorkflowOutputTests, ContractSnapshotTests, and AgentTaskLandDeliveryE2ETests are still running.

| class | executed | passed | failed | skipped | wall | notes |
|---|---:|---:|---:|---:|---|---|
| AgentTaskLandDeliveryE2ETests | | | | | | pending, 25 minute cap |
| ChannelE2ETests | 1 | 1 | 0 | 0 | 55s | pass |
| ContractSnapshotTests | | | | | | pending |
| DelegationSequencingE2ETests | 0 | 0 | 0 | 1 | 1s | Windows gate. Skip: Headed tests require Windows ConPTY. Exit 8. |
| DispatchConcurrencySettingsE2ETests | 1 | 1 | 0 | 0 | 57s | pass |
| OrchestratorE2ETests | 1 | 1 | 0 | 0 | 52s | pass |
| PipelineGlanceE2ETests | 1 | 1 | 0 | 0 | 55s | pass |
| ReleaseGateIsolationTests | 3 | 3 | 0 | 0 | 80s | pass |
| SmokeE2ETests | 2 | 2 | 0 | 0 | 66s | pass, browser to API to DB |
| WorkflowOutputTests | | | | | | running |
