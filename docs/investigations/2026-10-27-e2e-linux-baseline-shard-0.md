# E2E Linux baseline, shard 0 of 3

Server2 Linux baseline for `tests/Antiphon.E2E`. This is not the release gate.
The Windows rc profile remains the gate. Windows-only classes are recorded as
skip or fail with that cause. Product and test source were not changed.

## Base

- Ref: `origin/rc/2026-10-27`
- Tip: `9f00008facfc48e75555c08df59887019b3f8dfe` (fetched; worktree HEAD matches)
- Branch: `feat/card-task-cdc0a3b5`
- Host: server2 Linux (Debian 12 container, Docker 27.5.1, libglib-2.0 present)
- Shard rule: every class from `TUnit --list-tests` (no filter), ordinal sort,
  number from 0, keep `index % 3 == 0`
- Per-class cap: 25 minutes wall, then the process is stopped and the class is
  recorded as timeout
- Task budget: 120 minutes
- Isolated output: `bin-e2elinux0/` (forward slash). Removed at the end.
- Runner: the suite's own `IsolatedSessionRunner` on a random loopback port.
  Production runner 17204 and the live server are not used.

## Build

- `client/dist`: `npm ci` and `npm run build` under build slot
  `e2e-linux-s0-client-dist`, lease `d128857a-eaa1-47fb-9cbc-aa100834edc6`,
  exit 0, `dist/index.html` present.
- E2E assembly: `dotnet build tests/Antiphon.E2E/Antiphon.E2E.csproj -c Debug
  --property:OutputPath=bin-e2elinux0/ --property:UseAppHost=false` under
  build slot `e2e-linux-s0-build`, lease `535f3066-fd88-4089-9d04-634d96001da9`,
  waited 180s, exit 0, 39 warnings, 0 errors.
  DLL: `tests/Antiphon.E2E/bin-e2elinux0/Antiphon.E2E.dll`.
- Playwright: `playwright.ps1 install chromium` exit 0. Chromium revision
  `chromium-1243` is in the user Playwright cache. `libglib-2.0.so.0` is on
  the loader path.
- Inherited task id and token, headed flags, distiller canary flags, broker
  and channel-outbound opt-ins are cleared for every class run.

## Discovery

`dotnet run --no-build -- --list-tests` reported 177 tests. That stdout lists
display names only. Class identity is the `TestMethodIdentifierProperty`
(`Namespace` + `TypeName`) in the diagnostic log of that same unfiltered
`--list-tests` run. Thirty distinct classes, sorted ordinal on the fully
qualified name, numbered from 0. Shard 0 is `index % 3 == 0`.

| index | class | shard |
|------:|-------|-------|
| 0 | Antiphon.E2E.AgentE2ETests | 0 |
| 1 | Antiphon.E2E.AgentTaskLandDeliveryE2ETests | |
| 2 | Antiphon.E2E.BoardE2ETests | |
| 3 | Antiphon.E2E.CardCliE2ETests | 0 |
| 4 | Antiphon.E2E.ChannelE2ETests | |
| 5 | Antiphon.E2E.ChannelOutboundIsolatedTests | |
| 6 | Antiphon.E2E.ChromiumRuntimeSmokeTests | 0 |
| 7 | Antiphon.E2E.ContractSnapshotTests | |
| 8 | Antiphon.E2E.CreatedResourceTests | |
| 9 | Antiphon.E2E.DelegationPipelineE2ETests | 0 |
| 10 | Antiphon.E2E.DelegationSequencingE2ETests | |
| 11 | Antiphon.E2E.DirectoryAutocompleteE2ETests | |
| 12 | Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests | 0 |
| 13 | Antiphon.E2E.DispatchConcurrencySettingsE2ETests | |
| 14 | Antiphon.E2E.Fixtures.IsolatedSessionRunnerTeardownTests | |
| 15 | Antiphon.E2E.Fixtures.PtyBackendEnvGuardTests | 0 |
| 16 | Antiphon.E2E.LandDeliveryFixtureProbeTests | |
| 17 | Antiphon.E2E.OrchestratorE2ETests | |
| 18 | Antiphon.E2E.OutputDistillationApplyCanaryTests | 0 |
| 19 | Antiphon.E2E.OutputDistillationCanaryGuardTests | |
| 20 | Antiphon.E2E.PipelineGlanceE2ETests | |
| 21 | Antiphon.E2E.ProjectDeleteE2ETests | 0 |
| 22 | Antiphon.E2E.ReleaseGateIsolationTests | |
| 23 | Antiphon.E2E.ReleaseGateReportDeliveryTests | |
| 24 | Antiphon.E2E.ScheduleCliE2ETests | 0 |
| 25 | Antiphon.E2E.SmokeE2ETests | |
| 26 | Antiphon.E2E.WorkflowDeleteTests | |
| 27 | Antiphon.E2E.WorkflowOutputTests | 0 |
| 28 | Antiphon.E2E.WorktreeRetirementDeliveryE2ETests | |
| 29 | Antiphon.TestSupport.TestClassificationGuardTests | |

Run order is the fast Windows, skip, and opt-in classes first, then the
browser and CLI classes, so a shared host failure is visible before the
25-minute caps. Each class uses `--treenode-filter "/*/*/<simple name>/*"`
on the built DLL with `--no-build`.

## Status

Class runs are in progress. The table below is filled as each class returns.

## Classes

| # | class | executed | passed | failed | skipped | wall | notes |
|---|-------|----------|--------|--------|---------|------|-------|

## Failures

None recorded yet.

## Tests over 30 seconds

None recorded yet.
