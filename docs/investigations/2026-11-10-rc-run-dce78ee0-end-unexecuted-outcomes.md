# rc run dce78ee0: end-of-run unexecuted outcomes (2026-11-10)

Read-only look at the ended Windows Full run. Nothing was started, stopped, or written under `C:\Antiphon`. No build or test was run. Groups 130, 3, 86, and 87 were left alone.

## Run

| Field | Value |
|---|---|
| Run id | `dce78ee0a35a4b08be30d8e0e8ef0295` (log dir `2026-11-09-1306-dce78ee0`) |
| Candidate | `rc/2026-11-14` = `7bee43614495293afbae1b0f4add019d11dacb9c` |
| Log dir | `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0` |
| Started | `2026-11-09T13:06:31.4323139Z` |
| Completed | `2026-11-10T05:48:20.8336920Z` |
| Duration | `60109.401` s (16.7 h) |
| Platform / scope | `windows` / `Full` |
| Top-level | `outcome=TESTS`, `succeeded=false`, `coverageComplete=false`, `testsPassed=false` |
| Reasons | 31 entries at `summary.json` lines 50268-50299 |

The 31 reasons are 18 `unexecuted-outcome:` lines, 12 `process-exit-8` lines, and one `timeout session-runner`. The `unexecuted-outcome` lines name 25 methods. `CheckpointColdBuildTests.C1734_Cold_build_preserves_native_obj_and_terminal_receipt` is listed twice because the `tool` and `script` argument rows share one method identity. `VolumeRetirementApplyGateTests` is one line of seven methods. Each `process-exit-8` sits on the chunk whose only test was skipped.

TUnit records `SkipTestException` as TRX `outcome="NotExecuted"` with `DebugTrace` `Skipped: <text>`, and the console says `skipped`. `ConvertTo-NightlyNormalizedOutcome` maps `notexecuted` to `NotExecuted`, and `ConvertTo-NightlyCoverageVerdict` then emits `unexecuted-outcome:` and sets `coverageComplete=false`. A chunk whose counters are `executed=0` / `notExecuted=1` exits 8 (`Zero tests ran`), which the verdict records as `process-exit-8`.

## Answers

(a) Every unexecuted test is an expected Windows skip. The same `SkipTestException` text would be produced on every Windows Full run that does not supply a live-lane context. The coverage rule counts that `NotExecuted` row as unexecuted. Skip texts are in the table.

(b) None of the unexecuted tests are a real gap. Each one was discovered, started its skip path, and finished as `NotExecuted` in a few tens of milliseconds (the S7 sync skip took 5.6 s). No row is missing, `InProgress`, or undiscovered.

(c) The twelve exit-8 chunks are single-test profile chunks. Each TRX has `total=1`, `executed=0`, `notExecuted=1`, `ResultSummary` `Failed`, and `Exit code indicates failure: '8'`. The console says `Test run summary: Zero tests ran` and `skipped: 1`. The cause is the expected skip, not a crash or an empty discovery.

(d) Rerunning only these chunks would not settle the run. The skips and the twelve exit-8 results come back the same way. `session-runner` `all-group-4` is the only chunk a rerun could change, and only if the post-completion hang does not recur. The tests in that chunk already passed.

There is no credited named-chunk rerun. `nightly-run.ps1` takes `-Suites` (whole suites) and has no `-Chunks`. Failed-first in `scripts/lib/nightly-dispatch.ps1` reorders a new complete Full attempt (prior failures, then unfinished, then passed). Its comment says this is a preference for a new complete attempt, never execution resume. A pass with `coverageComplete=false` stays unfinished. `launch-rc.ps1` reads a launch record and runs `nightly-run.ps1` through the build slot, with a fresh `-RunId` equal to the supervisor id, and refuses while `run.lock` has a live owner. `start-supervisor.ps1` starts `supervise-rc.ps1` from the repository set copied into an owned task directory. Do that only after the native lock is free. This run already has `completedAt`. A new Full run needs its own run id. `PriorFailureSweep` is a resolver mode inside the history library, not a launch switch that publishes partial credit. Scoped lanes stay unauthorized while that plan is in progress. Gate credit still requires one Full run on one candidate SHA, policy, and run id.

## session-runner timeout

| Field | Value |
|---|---|
| Chunk | `session-runner` / `all-group-4` |
| Log | `session-runner-all-group-4-tests.log` |
| TRX | `session-runner-all-group-4.trx` |
| Started | `2026-11-09T22:33:18.7869242Z` |
| Timeout phase | `lastPhase=timeout`, `why=timeout`, `lastPhaseUtc=2026-11-09T23:03:20.1053292Z` |
| Wall | `runWallSeconds=1801.264` |
| Harness row | `exitCode=1`, `timedOut=true`, `result=FAIL`, chunk `evidenceReasons=["process-exit-1"]` |
| Top-level reason | `timeout session-runner` |
| Test host | Console `Passed!` `total: 119` `failed: 0` `succeeded: 119` `skipped: 0` `duration: 8m 23s 629ms` |
| TRX summary | `outcome="Completed"`, `executed=119`, `passed=119`, `notExecuted=0` |
| Diagnostic tail | `session-runner-all-group-4-execution\log_261109223319006.diag` ends with `total ran: 119`, `total failed: 0`, and `request execute stop` |
| Slowest test | `C1065_LocalSourceModesVerifyFreshAtRelease` at 175.879 s |

The suite finished green in 8m 24s. The owned process tree was still alive at 1801 s, so the harness recorded timeout, forced exit 1, and `result=FAIL`. This is a hang after the testing platform finished execute, not a slow chunk and not an unexecuted test. Class: `real-gap`. Proposed action: Debug why the `all-group-4` process tree stayed up after the green TRX. A later Full run, or a diagnostic rerun of this chunk after the native lock is free, can re-observe it. It does not clear the skip rows.

## Table

Chunk paths are `<log dir>\<suite>-<chunk>.trx` and the matching `-tests.log`. Outcome is the TRX `UnitTestResult` outcome. Reason is the `DebugTrace` text, which matches the console skip line.

| Test | Chunk | Outcome | Reason | Class | Proposed action |
|---|---|---|---|---|---|
| `Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `antiphon` `all-group-96` (exit 0, pass, 205 succeeded / 1 skipped) | `NotExecuted` | `Skipped: S7 real phone-home recipient fixture requires Linux; Windows handle qualification is separate.` | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_extraction_is_closed` | `antiphon` `all-profile-1963` (exit 8) | `NotExecuted` | `Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Host_proof_admits_only_an_authenticated_private_origin` | `antiphon` `all-profile-2154` (exit 8) | `NotExecuted` | `Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Host_proof_file_omits_credentials` | `antiphon` `all-profile-2155` (exit 8) | `NotExecuted` | `Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Recheck_target_refuses_a_changed_fingerprint` | `antiphon` `all-group-131` (exit 0, pass, 210 succeeded / 7 skipped) | `NotExecuted` | `Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.` | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Valid_token_refuses_protected_names_and_keeps_host_state` | `antiphon` `all-group-131` | `NotExecuted` | same CARD-1050 text | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.State_volume_named_in_the_plan_is_refused_before_delete` | `antiphon` `all-group-131` | `NotExecuted` | same CARD-1050 text | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Manual_apply_refuses_until_the_operation_token_matches` | `antiphon` `all-group-131` | `NotExecuted` | same CARD-1050 text | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Refused_apply_keeps_the_ready_preview` | `antiphon` `all-group-131` | `NotExecuted` | same CARD-1050 text | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Scripted_apply_refuses_until_the_operation_token_matches` | `antiphon` `all-group-131` | `NotExecuted` | same CARD-1050 text | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Resume_refuses_until_the_operation_token_matches` | `antiphon` `all-group-131` | `NotExecuted` | same CARD-1050 text | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.SessionRunner.Tests.RunnerChildClaudeTokenInheritanceTests.Claude_launch_seeds_trust_in_the_runner_config_dir` | `session-runner` `all-group-5` (exit 0, pass, 67 succeeded / 1 skipped) | `NotExecuted` | `Skipped: Claude trust spawn uses a claude symlink to sh.` | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs` | `antiphon` `all-group-5` (exit 0, pass, 1 succeeded / 1 skipped) | `NotExecuted` | `Skipped: 20 missing-context` | expected-skip | Count this Full-run live-lane skip as `Skipped`. `RequireExecutable` throws `SkipTestException` from `LiveClaudeRefusal` (`message` `20 missing-context`) when the launch request and context are null. The headed `ANTIPHON_HEADED_TESTS` check is later and was not reached. |
| `Antiphon.Tests.Application.C0824RefOnlySyncTests.Reported_tip_and_mirror_publication_keep_existing_contract` | `antiphon` `all-group-90` (exit 0, pass, 348 succeeded / 1 skipped) | `NotExecuted` | `Skipped: S7 real phone-home recipient fixture requires Linux; Windows handle qualification is separate.` | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Checkpoints.CheckpointColdBuildTests.C1734_Cold_build_preserves_native_obj_and_terminal_receipt` `(tool)` | `antiphon` `all-group-109` (exit 0, pass, 281 succeeded / 3 skipped) | `NotExecuted` | `Skipped: Exec target builds the Linux native helper` | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Checkpoints.CheckpointColdBuildTests.C1734_Cold_build_preserves_native_obj_and_terminal_receipt` `(script)` | `antiphon` `all-group-109` | `NotExecuted` | `Skipped: Exec target builds the Linux native helper` | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Checkpoints.CheckpointColdBuildTests.C1734_Real_tampering_keeps_a_red_terminal_report` | `antiphon` `all-group-109` | `NotExecuted` | `Skipped: Exec target builds the Linux native helper` | expected-skip | Count `Skipped:` `NotExecuted` as `Skipped` |
| `Antiphon.Tests.Application.LandGraphEvaluationTests.EvaluationBudgetRefusesSlowDotnet` | `antiphon` `all-profile-947` (exit 8) | `NotExecuted` | `Skipped: The slow evaluator is a PATH shim named dotnet; Windows process start resolves dotnet.exe.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B2_ReusedInodeIsANewDirectoryIdentity` | `antiphon` `all-profile-1787` (exit 8) | `NotExecuted` | `Skipped: Inode reuse is the Linux identity; Windows file ids are a separate native witness.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_BytesChangedDuringTheWalkAreKept` | `antiphon` `all-profile-1788` (exit 8) | `NotExecuted` | `Skipped: The mid-walk receipt checks are the Linux no-follow deleter; Windows needs a native witness.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_LateRepositoryMarkerAboveTheWalkStopsIt` | `antiphon` `all-profile-1789` (exit 8) | `NotExecuted` | `Skipped: The late-marker walk is the Linux no-follow deleter.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_LateRepositoryMarkerStopsTheWalk` | `antiphon` `all-profile-1790` (exit 8) | `NotExecuted` | `Skipped: The late-marker walk is the Linux no-follow deleter.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_NameAddedToAnUnwalkedDirectoryHolds` | `antiphon` `all-profile-1791` (exit 8) | `NotExecuted` | `Skipped: The mid-walk receipt checks are the Linux no-follow deleter; Windows needs a native witness.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_NameAddedToTheWalkedDirectoryHolds` | `antiphon` `all-profile-1792` (exit 8) | `NotExecuted` | `Skipped: The mid-walk receipt checks are the Linux no-follow deleter; Windows needs a native witness.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_WriteThroughAnOpenHandleDuringTheHashIsKept` | `antiphon` `all-profile-1796` (exit 8) | `NotExecuted` | `Skipped: The inotify-timed writer is Linux; the Windows deny-write hash is C1717_B3_WindowsOpenWriterRefusesTheDelete.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_D5_ExchangedSymlinkDoesNotDeleteOutsideFiles` | `antiphon` `all-profile-1797` (exit 8) | `NotExecuted` | `Skipped: The ancestor exchange uses Linux renameat2.` | expected-skip | Same rule, and do not emit `process-exit-8` for an all-skip chunk |
| `(no test)` `timeout session-runner` | `session-runner` `all-group-4` | TRX `Completed`, 119 passed, 0 not executed; harness exit 1, `timedOut=true`, `result=FAIL` | Process tree still alive at 1801 s after an 8m 24s green run | real-gap | Debug the post-completion hang |

## Exit-8 chunks

| Chunk | Sole test | Cause |
|---|---|---|
| `antiphon-all-profile-1963` | `C1105_Resume_extraction_is_closed` | Expected Linux skip, TUnit exit 8 because zero tests ran |
| `antiphon-all-profile-2154` | `C1008_Host_proof_admits_only_an_authenticated_private_origin` | Expected Linux skip, TUnit exit 8 |
| `antiphon-all-profile-2155` | `C1008_Host_proof_file_omits_credentials` | Expected Linux skip, TUnit exit 8 |
| `antiphon-all-profile-947` | `EvaluationBudgetRefusesSlowDotnet` | Expected Windows shim skip, TUnit exit 8 |
| `antiphon-all-profile-1787` | `C1717_B2_ReusedInodeIsANewDirectoryIdentity` | Expected Linux skip, TUnit exit 8 |
| `antiphon-all-profile-1788` | `C1717_B3_BytesChangedDuringTheWalkAreKept` | Expected Linux skip, TUnit exit 8 |
| `antiphon-all-profile-1789` | `C1717_B3_LateRepositoryMarkerAboveTheWalkStopsIt` | Expected Linux skip, TUnit exit 8 |
| `antiphon-all-profile-1790` | `C1717_B3_LateRepositoryMarkerStopsTheWalk` | Expected Linux skip, TUnit exit 8 |
| `antiphon-all-profile-1791` | `C1717_B3_NameAddedToAnUnwalkedDirectoryHolds` | Expected Linux skip, TUnit exit 8 |
| `antiphon-all-profile-1792` | `C1717_B3_NameAddedToTheWalkedDirectoryHolds` | Expected Linux skip, TUnit exit 8 |
| `antiphon-all-profile-1796` | `C1717_B3_WriteThroughAnOpenHandleDuringTheHashIsKept` | Expected Linux skip, TUnit exit 8 |
| `antiphon-all-profile-1797` | `C1717_D5_ExchangedSymlinkDoesNotDeleteOutsideFiles` | Expected Linux skip, TUnit exit 8 |

`summary.json` has exactly these twelve `"exitCode": 8` suite rows (the later copies are the nested duplicate of the same rows).
