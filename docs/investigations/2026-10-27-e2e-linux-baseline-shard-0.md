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

All ten shard classes ran. None hit the 25-minute cap. Totals: 41 executed,
27 passed, 14 failed, 2 skipped. The 14 failures are the Windows delivery
gate and the headed canary opt-in. The app, browser, and CLI classes passed
on the isolated stack.

Wall time is the TRX `Times` finish minus start, in seconds. Skipped is the
TRX `notExecuted` count. TUnit records those rows as `NotExecuted` with a
skip trace.

| # | class | executed | passed | failed | skipped | wall | notes |
|---|-------|----------|--------|--------|---------|------|-------|
| 0 | AgentE2ETests | 2 | 2 | 0 | 0 | 82.901 | passed |
| 3 | CardCliE2ETests | 10 | 10 | 0 | 0 | 274.797 | passed |
| 6 | ChromiumRuntimeSmokeTests | 1 | 1 | 0 | 0 | 2.085 | passed |
| 9 | DelegationPipelineE2ETests | 0 | 0 | 0 | 2 | 0.664 | Windows headed skip |
| 12 | DispatchBaseWarningDeliveryE2ETests | 13 | 0 | 13 | 0 | 1.182 | Windows gate; rerun matched |
| 15 | PtyBackendEnvGuardTests | 1 | 1 | 0 | 0 | 0.500 | passed |
| 18 | OutputDistillationApplyCanaryTests | 1 | 0 | 1 | 0 | 0.872 | opt-in gate; rerun matched |
| 21 | ProjectDeleteE2ETests | 8 | 8 | 0 | 0 | 189.283 | passed |
| 24 | ScheduleCliE2ETests | 1 | 1 | 0 | 0 | 49.464 | passed |
| 27 | WorkflowOutputTests | 4 | 4 | 0 | 0 | 102.640 | passed |

Class wall includes per-test Postgres and app startup. That startup is outside
the case `duration` attribute. Sum of class walls is 704.388 seconds.

## Failures

### DispatchBaseWarningDeliveryE2ETests

All 13 cases failed in the first run (TRX wall 0.854s) and again on the rerun
(TRX wall 1.182s). Same error every time:

`ShouldAssertException: OperatingSystem.IsWindows() should be True but was False`

The assertion is the first line of `LandDeliveryFixture.InitializeAsync`
(`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs`). The app and the
isolated runner are not started. Cause: Windows/Linux gate. The rc profile
already excludes this class on Linux (CARD-1323: Windows ConPTY and native
FakeGrok). A direct run fails instead of skipping. Not a product defect and
not a flake.

Cases: `C540_CollapsedWarningsReachIdleCaller`,
`C508_MismatchWarningReachesCaller`, `C508_MismatchWarningWaitsForCaller`,
`C540_CollapsedWarningsWaitForBusyCaller`,
`C540_ClaimCrashRecoversCollapsedWarnings`,
`C540_ProjectionCrashRecoversOriginalPairs`,
`C540_PreEnqueueCrashRecoversWarnings`,
`C540_PreEnqueueWaitSurvivesSlowMaterialization`,
`C540_EnqueueFailureRetriesWarnings`, `C540_QueueInsertCrashReusesRows`,
`C540_PreTypingCrashRecoversWarnings`, `C540_PostPromptCrashDoesNotRetype`,
`C540_ReceiptCrashDoesNotRetype`.

### OutputDistillationApplyCanaryTests

`Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup` failed
in the first run (case 0.083s, class wall 0.773s) and on the rerun (case
0.040s, class wall 0.872s):

`InvalidOperationException: Both dedicated canary opt-ins are required.`

`DistillerCanaryGuard.ValidateOptIns` throws unless `ANTIPHON_HEADED_TESTS`
and `ANTIPHON_DISTILLER_APPLY_CANARY` are both `1`. This run cleared both,
which is what an automated E2E child is required to do. The Windows assertion
later in the test was not reached. Cause: environment / opt-in gate. The rc
profile already excludes the class (CARD-0599, headed canary). Not a product
defect and not a flake.

### DelegationPipelineE2ETests

Not a failure. Both cases were skipped (`exit 8`): `Skipped: Headed tests
require Windows ConPTY`. The rc profile excludes the class for the same
headed-provider reason (CARD-0599). This is the skip shape the two red
classes do not use.

## Tests over 30 seconds

None. The longest case duration is 12.629s
(`CardCliE2ETests.The_cli_reopens_a_closed_card_by_its_identifier`). Next are
`AgentE2ETests.Agents_page_creates_agent_and_assigns_card_to_queue` at
11.916s and
`CardCliE2ETests.The_cli_creates_reads_edits_moves_and_closes_a_card_by_its_identifier`
at 11.783s.

## What Linux ran

Chromium headless rendered local content. Postgres `16-alpine` was already
present and the passing classes booted the disposable database, the prebuilt
`client/dist`, and the suite's own loopback runner. Agent pages, card CLI,
schedule CLI, workflow output, and project delete passed. Production port
17204 was not used.

`AgentE2ETests` rewrites `docs/screenshots/agents/01-agent-queue-foundation.png`
on success. That tracked file was restored and is not part of this commit.
