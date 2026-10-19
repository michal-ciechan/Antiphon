# RC watchdog sizing, 2026-10-19

Task `210e914f`. Read-only on the release checkout and the two candidate log trees. No suite was run. `tests/test-execution-policy.json` and `scripts/lib/nightly-tests-impl.ps1` were not edited.

The three suites are not dying because the watchdogs are a little short of a steady run. On both Windows rc gates the owned-process kill lands on the configured limit, and a repeated set of tests is still `InProgress` with no console result, far past the slowest test that did finish. Raising the limits does not produce a TRX while those tests hold the process.

## What fires the kill

`Get-NightlyWatchdogMs` (`scripts/lib/nightly-tests-impl.ps1:66-71`) reads `watchdogs.<suiteId>`. The call site (`:491-492`) passes that value as `TimeoutMilliseconds`, with fallback `3600000` when the key is absent. There is no `chunks` object in `tests/test-execution-policy.json`, so each suite is one unfiltered `all` chunk (`nightly-tests-impl.ps1:402`) and one watchdog. The current values, unchanged since they were introduced in `afae299d4` (CARD-0487, 2026-09-11):

| Key | File line | Milliseconds | Wall |
|---|---|---:|---|
| `antiphon` | `tests/test-execution-policy.json:741` | 3600000 | 60 min |
| `session-runner` | `:742` | 1200000 | 20 min |
| `e2e` | no key | fallback `:492` | 60 min |

Both gates used policy hash `de492ab11aa3a325af46b5447e7f5e2c66d81bb1b84f626bb6f3fea3d3d4b279`. A timeout deletes the process before TUnit writes the TRX, which is the `missing-trx` / `coverageComplete: false` pair in both `summary.json` files. Console output is `--no-progress`: passed tests are not printed. A `failed` or `skipped` line means the test body returned. Diagnostic `Failed` states did not flush (0 `FailedTestNodeStateProperty` last states on every timed-out suite); those console failures stay last-state `InProgress` and must not be counted as still running.

Diagnostic `TimingProperty` clocks are civil time labeled `+00:00`, one hour ahead of the diag line's UTC timestamp. Wall clock and the bins below use the diag line timestamps. `InProgress` on a test that later finished lines up with that test's `StartTime` once the hour offset is removed, so an `InProgress` timestamp is the start, not a discovery broadcast.

## Runs

| Gate | SHA | Run | Log |
|---|---|---|---|
| First | `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` | `42d2c784` | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\logs\2026-10-18-1647-42d2c784` |
| Second | `1f72e20158b99434cb6398fb7efbbd133d1c5569` | `d081f742` | `C:\Antiphon\releases\candidates\rc-2026-10-18-1f72e20158b9\logs\2026-10-18-2318-d081f742` |

The first gate's e2e suite did not time out. `summary.json` outcome is `PREFLIGHT`, suite detail `e2e-prerequisite-failed`: `npm ci` exit 1 and `client-bundle` exit 1 for `client\dist\index.html`. There is no `e2e-all-tests.log`. The second gate's prerequisite passed, and that is the only e2e execution.

Same-host suites that did finish in the second gate: messaging 0.8 min, pty-host 0.4 min, agents-pty 2.1 min, markdown-pdf 0.1 min. The machine was not stuck for every suite. Native suites run one at a time, so the antiphon stall is inside `Antiphon.Tests`.

## Antiphon.Tests (watchdog 60 min)

| | First `42d2c784` | Second `d081f742` |
|---|---|---|
| Diag | `16:49:54.831Z`–`17:49:43.281Z` (59.81 min) | `23:33:06.248Z`–`00:32:55.282Z` (59.82 min) |
| UIDs with a state | 2366 | 2375 |
| Passed / skipped / InProgress | 2205 / 4 / 153 | 2229 / 4 / 138 |
| Console failed (body returned) | 72 | 69 |
| InProgress with no console line | 80 | 69 |
| Of those, still running at >=20 min | 64 | 55 |

Completed-test duration on the second run: p50 0.44s, p90 6.0s, p99 30.8s, max 75s in the diag (slowest console failure 86s). A test still `InProgress` at 20 minutes is past 15 times the slowest test that finished.

Terminal results by 5 minutes, second run: 0–5m 1815, 5–10m 27, 10–15m 34, 15–20m 2, then a trickle, 50–55m 295, 55–60m 8. The first run has the same shape (0–5m 1780, 55–60m 287). Opening rate is 363 terminals/minute. After minute 5 the second run finished 418 tests in 55 minutes (7.6/minute). New UIDs were still appearing in minute 59.

Source `[Test]` lines at the release checkout `1f72e201` (`tests/Antiphon.Tests`, 1384 files): 12383. The gate saw 2375 UIDs, about a fifth of those methods. There is no discovery JSON: discovery runs after the exe, and the kill prevents it. `[Test]` lines are not an expanded TUnit census (session-runner below expands 1146 methods to about 1400 UIDs).

Duration to finish, second run:

- At the opening rate, 12383 / 363 per minute = 34 minutes, inside the current 60 minute watchdog.
- At the rate the hour actually sustained, the unseen ~10000 methods need about 22 hours, and the tests already `InProgress` for most of the hour have no finish time.
- "Seen UIDs only, overall average" (37.3/minute, 142 not terminal) projects 64 minutes. That denominator is only tests that had already started, and the average is dominated by the first five minutes. It is not a finish time.

About 53 of the second run's silent `InProgress` tests started in the first 15 minutes and were still `InProgress` at the kill. The same classes do it on the first gate. Largest groups on the second run, with no console line:

| Class | Silent InProgress >=10 min | Longest |
|---|---:|---:|
| `RunnerClaudeAdapterEffortPromptTests` | 10 | 59 min |
| `KeepSixReminderTests` | 9 | 60 min |
| `DirectSessionRunnerClientDisposalTests` | 6 | 59 min |
| `CodexAdapterLocalShellTests` | 4 | 54 min |
| `AgentTaskDispatchBaseGuardTests` (`C540_ProbeCensus`, three UIDs) | 3 | 47 min |
| `RunnerGrokAdapterReadyTestsPty` | 3 | 58 min |

Also silent since the first two minutes on both gates: `KeepSixSettingsTests`, `KeepSixShortfallTests`, `SmokeTests`, `RolePolicyEndpointTests`, `OrchestratorInstructionsEndpointTests`, `ExpectationStatusTests.C650_Status_requires_authorized_board_and_paginates`, `OperatorShutdownEndpointTests.Shutdown_202_reports_the_starting_launch_count`, `AgentTuiLaunchResolverTests` (one method), `AgentPinnedInstructionCompositionTests.V32_DormantRegistration`, `DelegationBriefCeilingPtyTests`, `DispatchConcurrencyPipelineTests`, `RemoteControlRecoveryPtyIntegrationTests`, `AgentTaskInputFallbackTests`. `RunnerClaudeAdapterEffortPromptTests.An_effort_dialog_that_never_clears_fails_within_its_budget` is one of the tests that did not return.

`EffectiveSettingsPipelineTests` (three methods, started at minute 50, age 10 min) and one `SessionQueueReceiptPlumbingTests` case started at minute 50 are the live tail, not the hour-long set.

## Session-runner (watchdog 20 min)

| | First `42d2c784` | Second `d081f742` |
|---|---|---|
| Diag | `17:50:01.117Z`–`18:10:00.042Z` (19.98 min) | `00:33:11.448Z`–`00:52:59.964Z` (19.81 min) |
| UIDs | 1397 | 1391 |
| Passed / skipped / InProgress | 1292 / 18 / 85 | 1336 / 17 / 36 |
| Console failed | 119 | 31 |
| InProgress matched to a console failure | 78 | 29 |
| InProgress with no console line | 7 | 7 |

Source `[Test]` lines: 1146. Both runs saw the expanded suite (about 1400 UIDs). Completed duration, second run: p50 0.05s, p90 1.7s, p99 10.4s, max 156s (first run max 162s). Terminal bins, second run: 0–5m 155+6+48, then the bulk at 5–10m, 15–20m still 7+9. The suite is still completing at the kill.

The 29 (second) and 78 (first) console failures returned in well under 10s and only look old because the `Failed` diag record never flushed. The first run's 119 failures are the UnauthorizedAccess cluster already recorded in `docs/investigations/2026-10-18-rc-cut-and-gate.md`. After that cluster shrank, the silent set did not.

Silent on both gates, `InProgress` from the first second to the kill (1187–1197s, about 7 times the longest test that finished):

- `CodexWindowsLaunchPolicyTests.Quote_expansion_is_counted_not_estimated`
- `CodexWindowsLaunchPolicyTests.The_incident_fixture_is_accepted_on_the_node_path`

Also silent, started at minute 15–16, age 203–299s at the kill (about twice the longest completed test, not the whole window): four `TranscriptIngestionLatencyProbeTests` (`Appended_prompts_reach_the_event_hub_under_concurrent_file_load` twice, `Cold_tail_records_latency_to_a_prompt_after_existing_history` twice). One further test was admitted in the last second (`C1032_RemovalEligiblePhysical` on the second run, `C1032_RemovalLateFileIdentityRefuses` on the first).

At 68 terminals/minute the already-seen suite is a minute from done. That rate does not describe the two policy tests, which have used the entire 20 minutes twice and have no finish. The four probes, if they end near their current age, need a few minutes past the kill. A 20 minute cap therefore misses a short legitimate tail and, separately, never outlasts the two hangs.

## E2E (fallback 60 min, second gate only)

Diag `01:03:12.151Z`–`02:00:56.145Z` (57.73 min). 74 UIDs: 37 passed, 3 skipped, 34 `InProgress`. Passed-test diag durations are under 0.3s. The three console failures all returned and are `AgentTaskLandDeliveryE2ETests`: `C488_ReviewToLandReceiptMatrix` 185s, `C467_V28_LostFlushWakeupRecoversOnIdleCaller` 46s, `C488_ApprovalLostFlushRecovers` 46s.

The other 31 `InProgress` rows are the same class, with no console line. They were admitted across the hour (first silent one at minute 3, then roughly one every two minutes) and none returned. 23 were already past 20 minutes at the kill; the oldest silent case, `C494_ReviewReceiptRejectsFalseEvidence`, had been `InProgress` for 55 minutes. Sibling cases that do return do so in 46–185s. After the opening minute, terminal completions stop. Source `[Test]` lines in `tests/Antiphon.E2E`: 108, of which `AgentTaskLandDeliveryE2ETests.cs` has 24; argument rows expand that class past the method count. Other E2E classes were not reached.

There is no rate at which this suite finishes. The post-warmup completion rate is zero, and the admitted land cases have already run many times their siblings' failure time.

## Previous successful runs, and the policy then

There is no successful full-suite duration to copy.

- `C:\Antiphon\releases\candidates` contains only these two rc candidates. Neither wrote an antiphon, session-runner, or e2e TRX.
- `C:\Antiphon\nightly\logs` has two directories, both 2026-09-04, both `selectedSuites: ["client"]` on `feat/card-task-2aab4b1c`. `2026-09-04-1735` outcome `BUILD` (259s). `2026-09-04-1748` outcome `TESTS` (465s). `C:\Antiphon\nightly\last-run.json` is that second run, `succeeded: false`. `last-complete-green.json` is absent under both `C:\Antiphon\nightly` and `C:\Antiphon\releases`.
- `docs/testing-and-build.md` (Nightly) already records the 2026-09-11 CARD-0487 audit: the Windmill nightly is not an operational full-suite backstop, and the latest local `last-run.json` is that September 4 client run.
- The watchdog numbers have not changed since `afae299d4`. `ff9760079` (2026-10-16) only added `markdown-pdf: 1200000`. `e2e` has never had a key. The historical "Antiphon.Tests is about 25.5 minutes" note is not a duration these logs reproduce; the opening burst alone is 5 minutes and 1800 tests, and the run is still admitting tests at minute 59.

## Ranked recommendation

1. Fix the tests that do not return. That is what keeps the process alive until the kill, and what a longer watchdog will sit on. The repeated set is: `RunnerClaudeAdapterEffortPromptTests`, the KeepSix `AntiphonWebAppFactory` classes (`KeepSixReminderTests`, `KeepSixSettingsTests`, `KeepSixShortfallTests`), `DirectSessionRunnerClientDisposalTests`, `CodexAdapterLocalShellTests`, `RunnerGrokAdapterReadyTestsPty`, `AgentTaskDispatchBaseGuardTests.C540_ProbeCensus`, `SmokeTests`, `RolePolicyEndpointTests`, plus the smaller antiphon classes listed above; `CodexWindowsLaunchPolicyTests` (the two methods named above); and `AgentTaskLandDeliveryE2ETests`. No policy line fixes these.

2. Do not raise `antiphon` off `3600000` (`tests/test-execution-policy.json:741`) as the remedy. At the rate the suite runs before those classes stall, the `[Test]` count fits in about 34 minutes. At the rate the stalled hour actually ran, even `7200000` does not cover the unseen methods. Change `:741` only after a run in which those classes return and the measured wall exceeds 50 minutes.

3. After the two `CodexWindowsLaunchPolicyTests` methods return, raise session-runner from `1200000` to `1800000` at `tests/test-execution-policy.json:742`. The four latency probes were still in their first 4–5 minutes when the 20 minute kill fired. `1800000` does not help while the two policy tests stay `InProgress` for the whole window, which they did on both gates.

4. Do not lengthen e2e. Adding `"e2e": <ms>` next to the other keys (the object ends at `tests/test-execution-policy.json:748`) or editing the `3600000` fallback at `scripts/lib/nightly-tests-impl.ps1:492` only changes how long `AgentTaskLandDeliveryE2ETests` is allowed to sit without a result. The cases that return already do so in under 4 minutes.

5. Splitting a suite across workers is not how these runs get a TRX. The fast tests already finish in the first minutes; the kill is a handful of classes. A `chunks` map (read at `nightly-tests-impl.ps1:298-302`, defaulting to one chunk at `:402`, watchdog per chunk at `:492`) could quarantine those classes so the rest of the assembly writes a TRX. It does not make the quarantined classes pass, and the rc profile's `coverageComplete` still requires them.

## Not done, noted

A WebApplicationFactory that never becomes ready would explain the KeepSix classes starting together and never returning; the logs do not show the fixture's wait.

## Remaining uncertainties

- No discovery census exists for the three killed suites, so 12383 is a `[Test]` line count, not the TUnit UID total.
- Why each hung class does not return (deadlock, a child process, a fixture, a budget that does not fire) is not in these logs. What is in the logs is that the same methods are still `InProgress`, with no console line, at the kill, on both gates, orders of magnitude past siblings that finished.
- The four `TranscriptIngestionLatencyProbeTests` cases might be slow rather than stuck; they had been running about twice the longest completed session-runner test, not twenty minutes.
