# Windows RC qualification reds at 66e9cfa9f (rc/2026-10-20), 2026-10-21

Read-only investigation of the running Windows qualification (task e9c3b76a, run
ebd2ff29dca945f28a763bb4e7fb23e2, candidate rc-2026-10-20-66e9cfa9fc6b). No test was run,
no process touched, no file under the candidate or state roots written. Evidence is the run's
TRX/diag files, the two earlier rc/2026-10-20 attempts, the rc-2026-10-18 reports, and the
source at the rc tip 66e9cfa9fc6ba0525bd1bcb7b012b74c422767f3. Snapshot taken 17:43Z.

Logs: `C:\Antiphon\releases\candidates\rc-2026-10-20-66e9cfa9fc6b\logs\2026-10-21-1550-ebd2ff29`.
Earlier attempts: `...\rc-2026-10-20-0b455324d2cb\logs\2026-10-21-1107-883e74b2` (0b455324d,
pre-packing `profile-N` chunks, 32 launches done) and `...\rc-2026-10-20-cadf547f7d69\logs\2026-10-21-1250-0f43c3e5`
(cadf547f7, first packed plan, 8 launches done). Prior Windows notes:
`docs/investigations/2026-10-19-rc-windows-live-reds.md` (42cbbd04) and
`C:\Antiphon\releases\task-7b182710\qualification-report.md` (81cfb267a, rc/2026-10-18).

## 1. Group census (27 TRX, 2247 results, 42 failed, 0 watchdog kills so far)

Times UTC. `run` is TRX start to finish; the launch gap between groups is 33 to 89 s (mean 46 s).
Classes per group are in the TRX `TestMethod/@className`; only groups with reds list them.

| G | results | failed | run | classes with reds |
|---|---:|---:|---:|---|
| 1 | 73 | 1 | 76 s | LandDeliveryWaitRoutingTests |
| 2-8 | 86,120,83,113,88,22,96 | 0 | 1-49 s | Agents.*, AgentTui.* (green) |
| 9 | 76 | 1 | 35 s | AgentTui.AgentTuiLaunchResolverTests |
| 10 | 65 | 0 | 18 s | |
| 11 | 27 | 18 | 17 s | AgentTui.RunnerProcessProbeTests (alone) |
| 12 | 44 | 1 | 65 s | Api.KeepSixReminderTests (packed with KeepSixSettings/KeepSixShortfall/others) |
| 13-14 | 71,65 | 0 | 34-41 s | ApiKeys.* |
| 15 | 120 | 6 | 37 s | Application.AdvisoryChangePolicyTests (5), AgentSessionRuntimeActivityTests (1) |
| 16 | 120 | 1 | 51 s | Application.AgentControlServiceIntegrationTests |
| 17 | 99 | 0 | 63 s | |
| 18 | 120 | 5 | 67 s | Application.AgentSystemPromptLaunchTests |
| 19 | 71 | 0 | 121 s | AgentSessionLaunchFailureTests |
| 20 | 45 | 1 | 33 s | Application.AgentSessionRuntimeTests |
| 21 | 57 | 0 | 42 s | |
| 22 | 120 | 3 | 71 s | Application.AgentTaskCatchUpSettlementTests |
| 23 | 120 | 1 | 254 s | Application.AgentTaskCheckSweepTests |
| 24 | 120 | 3 | 82 s | Application.AgentTaskDispatchFailureTests (1), AgentTaskInputFallbackTests (2) |
| 25 | 86 | 0 | 227 s | AgentTaskDeliveryWatchdogTests |
| 26 | 92 | 1 | 497 s | Application.AgentTaskDispatchBaseGuardTests |
| 27 | 48 | 0 | 1968 s | AgentTaskLandAdmissionControlledTests + AgentTaskLandActiveSourceClaimTests (16 Slow nodes, about 2 min each) |

Watchdog: `watchdogs.antiphon` is 3600000 ms per launch (`tests/test-execution-policy.json:762`,
applied at `scripts/lib/nightly-tests-impl.ps1:988`). No group has been killed; group 27 at
32.8 min is the closest. No `missing-trx` row exists yet.

## 2. Every failed test (42)

Duration from the TRX. Message is the first line of `ErrorInfo/Message`.

| G | class.method | duration | first line |
|---|---|---:|---|
| 1 | LandDeliveryWaitRoutingTests.Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not | 0.12 s | NearestWait(dispatchTests, "every dispatch warning must retain...") should be "UntilProtocolAsync(" but was "UntilAsync(" |
| 9 | AgentTuiLaunchResolverTests.ModelTier_maps_against_the_resolved_profile_kind_and_omits_raw | 3.68 s | codexLaunch.Spec.Args.TakeLast(2) should be ["--model","gpt-6-sol"] but was ["--model","gpt-6.1-sol"] |
| 11 | RunnerProcessProbeTests.Probe_excludes_undeclared_host_environment_and_keeps_declared_values | 0.08 s | result.ExitCode should be 0 but was null |
| 11 | RunnerProcessProbeTests.Probe_preserves_argument_boundaries_without_shell_interpretation | 0.01 s | result.ExitCode should be 0 but was null |
| 11 | RunnerProcessProbeTests.Probe_caps_combined_stdout_and_stderr_and_signals_truncation | 0.01 s | result.OutputTruncated should be True but was False |
| 11 | RunnerProcessProbeTests.Truncated_output_is_discarded_instead_of_returning_secret_fragments | 0.00 s | result.OutputTruncated should be True but was False |
| 11 | RunnerProcessProbeTests.Invalid_utf8_output_is_discarded_as_unusable | 0.00 s | result.OutputTruncated should be True but was False |
| 11 | RunnerProcessProbeTests.Probe_timeout_kills_the_entire_process_tree | 0.01 s | result.TimedOut should be True but was False |
| 11 | RunnerProcessProbeTests.Probe_cancellation_kills_the_tree_and_redacts_secret_canaries | 5.02 s | File.Exists(path) should be True but was False (WaitForFileAsync, line 1300) |
| 11 | RunnerProcessProbeTests.Unconfirmed_primary_cleanup_is_owned_and_completed_by_the_reaper | 0.00 s | result.TimedOut should be True but was False |
| 11 | RunnerProcessProbeTests.Reaper_host_stop_kills_every_already_started_tracked_process | 0.01 s | result.CleanupConfirmed should be False but was True |
| 11 | RunnerProcessProbeTests.Reaper_shutdown_waits_for_a_starting_guard_then_kills_and_rejects_future_starts | 5.02 s | TimeoutException: The operation has timed out. |
| 11 | RunnerProcessProbeTests.Unexpected_exception_after_registration_transfers_process_to_reaper_cleanup | 0.01 s | Task probe.RunAsync(...) should throw ApplicationException but did not |
| 11 | RunnerProcessProbeTests.Late_process_start_after_the_one_second_deadline_is_reaped | 5.04 s | TimeoutException: The operation has timed out. |
| 11 | RunnerProcessProbeTests.Bounded_startup_that_exits_on_stdin_close_is_clean | 0.01 s | result.CleanlyStopped should be True but was False |
| 11 | RunnerProcessProbeTests.Probe_redacts_credential_shaped_output_without_persisting_raw_values | 0.01 s | result.SensitiveOutputDetected should be True but was False |
| 11 | RunnerProcessProbeTests.Probe_redacts_common_credential_names_in_assignments_and_json | 0.00 s | result.SensitiveOutputDetected should be True but was False |
| 11 | RunnerProcessProbeTests.Probe_redacts_many_same_line_assignments_near_the_output_limit_within_budget | 0.01 s | result.SensitiveOutputDetected should be True but was False |
| 11 | RunnerProcessProbeTests.Helper_resolves_the_current_powershell_executable_cross_platform | 0.02 s | result.ExitCode should be 0 but was null |
| 11 | RunnerProcessProbeTests.Bounded_startup_reports_forced_tree_termination_as_not_clean | 0.05 s | result.Error should be "The probe process required forced cleanup." but was "The bounded child environment is unavailable." |
| 12 | KeepSixReminderTests.off_rows_do_not_read_shortfall | 0.23 s | await CountShortfallReadsAsync() should be 0 but was 1 |
| 15 | AdvisoryChangePolicyTests.C1303_R12_LandRefusesUnavailableCorrectedEvidence(changed-blob) | 0.33 s | DirectoryNotFoundException: ...\releases\checkout\.git\antiphon\code-evidence\04228bff\producer-pack-index.json |
| 15 | AdvisoryChangePolicyTests.C1303_R12_LandRefusesUnavailableCorrectedEvidence(missing-bundle) | 0.17 s | same |
| 15 | AdvisoryChangePolicyTests.C1303_R12_LandRefusesUnavailableCorrectedEvidence(missing-blob) | 0.20 s | same |
| 15 | AdvisoryChangePolicyTests.C1281_IncompleteNeverClean | 0.18 s | same |
| 15 | AdvisoryChangePolicyTests.C1281_ReceiptCorrection | 0.23 s | same |
| 15 | AgentSessionRuntimeActivityTests.Output_bursts_write_LastSeenAt_at_most_once_per_interval | 2.84 s | writes.Count should be 2 but was 1 |
| 16 | AgentControlServiceIntegrationTests.Resume_of_a_mid_turn_session_writes_a_restart_boundary_and_queues_the_auto_continue | 0.32 s | InvalidOperationException: Sequence contains no elements. (line 887, SessionRestartBoundary row absent) |
| 18 | AgentSystemPromptLaunchTests.Start_with_system_prompt_append_passes_flag_on_fresh_launch | 0.91 s | await db.AgentIncidents.AnyAsync(...) should be False but was True |
| 18 | AgentSystemPromptLaunchTests.Second_start_on_a_live_session_is_a_no_op_and_does_not_rebootstrap | 0.85 s | ConflictException: Stop the current conversation before selecting another. |
| 18 | AgentSystemPromptLaunchTests.Resume_not_found_fallback_delivers_fresh_bootstrap | 0.27 s | factory.Created.Count should be 2 but was 1 |
| 18 | AgentSystemPromptLaunchTests.Fallback_with_stale_mid_turn_transcript_still_delivers_bootstrap | 0.19 s | ArgumentOutOfRangeException: Index was out of range (Created[1] absent) |
| 18 | AgentSystemPromptLaunchTests.Launch_note_yields_to_an_owed_channel_row | 1.63 s | adapter.SubmittedBodies should be [channelBody] but was [channelBody, restart note] |
| 20 | AgentSessionRuntimeTests.Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart | 2.47 s | rows.Select(r => r.Text) should be ["hello","hi","after relaunch"] but was [] |
| 22 | AgentTaskCatchUpSettlementTests.catch_up_of_a_report_boundary_settles_a_dispatched_task_as_marked | 1.78 s | settled.Status should be Succeeded but was Dispatched |
| 22 | AgentTaskCatchUpSettlementTests.catch_up_settles_when_the_turn_end_was_already_persisted | 1.78 s | settled.Status should be Succeeded but was Dispatched |
| 22 | AgentTaskCatchUpSettlementTests.catch_up_settles_even_when_channel_reply_dispatch_throws | 1.84 s | settled.Status should be Succeeded but was Dispatched |
| 23 | AgentTaskCheckSweepTests.the_flush_cancels_a_settled_check_already_typed_once_before_it_retypes | 0.22 s | stored.Status should be Canceled but was Pending |
| 24 | AgentTaskDispatchFailureTests.a_git_timeout_fails_one_task_not_the_tick | 5.68 s | result.Failures should be 1 but was 0 |
| 24 | AgentTaskInputFallbackTests.Ordinary_spill_errors_keep_their_existing_delivery_behavior(flush) | 2.42 s | reverted.DeliveryAttempts should be 1 but was 0 |
| 24 | AgentTaskInputFallbackTests.Runner_write_failure_types_only_the_durable_api_pointer(flush) | 0.47 s | changed.Body should not contain ".antiphon/inbox/" |
| 26 | AgentTaskDispatchBaseGuardTests.C1213_SourceLandingKeepsLegacyIdentity | 5.60 s | ConflictException: verification_creation_outside_runner_repository |

## 3. Causes, ranked by red count, with evidence

### Cause A. Host PATH longer than the probe's 4000-char ceiling (18 reds, RunnerProcessProbeTests)

Mechanism, reconstructed from source and the host:

- `server/Infrastructure/Agents/Tui/RunnerProcessProbe.cs:18` sets `MaximumEnvironmentValueLength = 4000`.
  Lines 231-243 copy `PATH`, `PATHEXT`, `SystemRoot`, `WINDIR`, `ComSpec`, `TEMP`, `TMP`, `USERPROFILE`
  from the host (line 785) and return `Failure("The bounded child environment is unavailable.")`
  when any value exceeds 4000 chars. A `Failure` never starts the child, so `ExitCode` is null,
  every flag is false, no pid file is written, and the reaper tests time out waiting for a start.
- This desktop's PATH: machine 1101 chars, user 4645 chars, combined 5747 (this shell saw 5833).
  The user PATH alone is over the ceiling, so every probe on this host refuses regardless of session.
- The one test that asserts `result.Error` received exactly that refusal text
  (`Bounded_startup_reports_forced_tree_termination_as_not_clean`, line 996). The other 17 asserts
  are downstream of the same refusal.
- Repeat: identical 18 names and messages on cadf547f7 (group 5); 9 of them on 42cbbd04
  (2026-10-19 note, "Inherited, runner process probe") and 5 on 81cfb267a before its cap kill.
  `RunnerProcessProbeRedactionTests` (same group) passes because it does not spawn.

Classification: Windows-only, environment (host PATH length) meeting a product hard limit.
Not timing, not load. Linux runners have short PATHs. Fix owner: server (probe should not
inherit an over-long host PATH verbatim, or should raise/trim the bound) plus a test guard that
names the ceiling. Suggested card: one Code card, area agents-tui.

### Cause B. Correction tests read an untracked local evidence pack (5 reds, AdvisoryChangePolicyTests)

- `tests/Antiphon.Tests/Application/AdvisoryChangePolicyTests.Correction.cs:212-217` resolves
  `DelegateScriptRunner.RepoRoot`, takes its git common dir, and reads
  `antiphon/code-evidence/04228bff/producer-pack-index.json` with a pinned SHA-256.
- That path is not tracked (no `04228bff` in `git ls-tree` at the rc tip), it is absent in the
  release checkout (`C:\Antiphon\releases\checkout\.git\antiphon` does not exist) and absent in
  the main checkout (`C:\src\Antiphon\.git\antiphon` has no `code-evidence` directory).
- Repeat: same 5 names on cadf547f7 (group 8), 42cbbd04 and 81cfb267a.

Classification: fixture/environment on both platforms for any fresh clone; passes only on a
machine that still holds the producer pack from the CARD-1281 Code task. Fix owner: tests
(ship the pack as a test fixture under `tests/`, or skip with a named reason when absent).

### Cause C. Stale source pin in LandDeliveryWaitRoutingTests (1 red, fixed on master)

- `tests/Antiphon.Tests/LandDeliveryWaitRoutingTests.cs:41` expects the wait nearest the
  key-retention string in `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs:135` to be
  `UntilProtocolAsync(`. At the rc tip that wait is `f.UntilDispatchAsync(` (line 130), which
  `NearestWait` (lines 51-57) does not recognise, so it falls back to line 97's `UntilAsync(`.
- master commit 13048a2eb "test(CARD-1411): pin the queue-cut wait to UntilDispatchAsync" adds the
  `UntilDispatchAsync(` branch and expectation; it is not on rc/2026-10-20 (the file differs by 9 lines).
- Repeat: same on cadf547f7 group 1 (0b455324d never ran this class).

Classification: real test defect on both platforms. Fix owner: rc line, cherry-pick 13048a2eb.

### Cause D. Stale Codex High literal (1 red, AgentTuiLaunchResolverTests)

- `tests/Antiphon.Tests/AgentTui/AgentTuiLaunchResolverTests.cs:246` expects `gpt-6-sol` for
  `AgentModelLevel.High`; `ModelLevelAliases.ForCodex` returns `gpt-6.1-sol`, which
  `CodexCliObservationTests.cs:52` pins as the High alias. The file is identical on master.
- Repeat: all three rc/2026-10-20 attempts that reached it, plus 81cfb267a ("definite
  code/test expectation mismatch" in task-7b182710).

Classification: real test defect on both platforms. Fix owner: tests (or the ladder, if
`gpt-6-sol` is the intended High pin; CARD-0611 bumped High to gpt-6-sol on 2026-09-23 and a
later change moved it to gpt-6.1-sol without updating this literal).

### Cause E. POSIX-only path guard exercised with Windows temp paths (1 red, C1213)

- `server/Infrastructure/Agents/SessionRunner/RemoteVerificationWorkspace.cs:103-110`
  (`RequirePrefix`) throws `verification_creation_outside_runner_repository` for any path that
  contains a backslash or does not start with `/`.
- `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.CardBranchNames.cs:188-194`
  builds the remote workspace from `Directory.CreateTempSubdirectory(...)` under
  `C:\Users\...\Temp`, which the guard rejects by design (the remote workspace is Linux-only).
- Repeat: 81cfb267a ("SourceLanding path admission") and cadf547f7 did not reach it.

Classification: Windows-only assumption in the test. Fix owner: tests (Linux-only guard, or a
POSIX-path fixture for the remote arm).

### Cause F. KeepSix classes share one factory under CARD-1404 packing (1 red, new this run)

- `tests/Antiphon.Tests/Api/KeepSixReminderTests.cs:22-24` uses
  `[ClassDataSource<AntiphonWebAppFactory>(Shared = PerTestSession)]` and
  `[NotInParallel("KeepSixReminder")]`; the key serialises only this class.
  `off_rows_do_not_read_shortfall` (line 189-198) disables every other row and expects zero
  shortfall reads, but group 12 co-hosts `KeepSixSettingsTests` and `KeepSixShortfallTests`
  against the same shared factory, so a sibling's enabled row can be read between
  `DisableOtherRowsAsync` and `RunAsync`.
- Not seen on cadf547f7 (its Api packing differed) nor on single-process runs.

Classification: fixture isolation under multi-class packing (timing/parallel). Mechanism is
plausible from source, not reproduced. Fix owner: tests (one shared `NotInParallel` key across
the KeepSix classes, or isolated schemas).

### Cause G. Windows-only git hook delay does not fire (1 red, AgentTaskDispatchFailureTests)

- `tests/Antiphon.Tests/Application/AgentTaskDispatchFailureTests.cs:84-96` writes a
  `#!/bin/sh` `sleep 30` post-checkout hook via `core.hooksPath` and expects `git worktree add`
  to exceed a 1 s timeout. On this host the tick reported `Failures=0`, `Dispatched` as normal,
  in 5.7 s: the hook did not delay the checkout.
- master changed this file (35215b2ad, CARD-1232) but not this test.

Classification: Windows-only assumption (hook execution semantics under Git for Windows).
Mechanism not reproduced here. Fix owner: tests (Windows guard or a hook that delays on both).

### Cause H. Bridge-harness delivery and settlement cluster, deterministic on Windows, mechanism open (14 reds)

Classes: AgentSystemPromptLaunchTests (5), AgentTaskCatchUpSettlementTests (3),
AgentTaskInputFallbackTests (2), AgentSessionRuntimeActivityTests (1),
AgentControlServiceIntegrationTests (1), AgentSessionRuntimeTests (1), AgentTaskCheckSweepTests (1).

- Every one of these 14 names failed with the same assertion and value on cadf547f7 (those its 8
  groups reached: 9 of 14), on 42cbbd04 (2026-10-19 note lines 245-253) and on 81cfb267a
  (task-7b182710 failure groups). Durations are short (0.2 to 2.8 s) and stable, so this is not
  timing under load.
- Shared shape: a transcript or queue write the test expects did not land (settlement stays
  `Dispatched`, the restart boundary row is absent, transcript rows are empty, the launch note
  typed before the owed turn ended, a fallback adapter was never created). All run through
  `BridgeQueueHarness` or the `AgentSessionRuntime` transcript path.
- `AgentSessionRuntimeTests.cs:216-243` uses the shared default schema while sibling classes use
  isolated ones; with packing it shares the process with other Application classes, which is
  one candidate for the empty row set.

Classification: Windows-only assumption in the harness or runtime path (file/transcript
semantics), deterministic. Root cause not confirmed. Fix owner: one Investigate card for the
BridgeQueueHarness family on Windows, run with a single class per process to separate it from
packing.

## 4. Repeats on the earlier attempts

| Class | 66e9cfa9f (this run) | cadf547f7 (8 groups) | 0b455324d (32 profile chunks) | 81cfb267a / 42cbbd04 |
|---|---:|---:|---:|---|
| RunnerProcessProbeTests | 18 | 18 (same names) | not reached | 5 / 9 |
| AdvisoryChangePolicyTests | 5 | 5 | not reached | 5 / 5 |
| LandDeliveryWaitRoutingTests | 1 | 1 | not reached | n/a (pre-CARD-1411 pin) |
| AgentTuiLaunchResolverTests | 1 | 1 | not reached | 1 / 1 |
| AgentSystemPromptLaunchTests | 5 | 5 | not reached | 5 / listed |
| AgentSessionRuntimeActivityTests | 1 | 1 | not reached | 1 / 1 |
| AgentSessionRuntimeTests | 1 | 1 | not reached | 1 / listed |
| AgentControlServiceIntegrationTests | 1 | 1 | not reached | 1 / listed |
| AgentTaskCatchUpSettlementTests | 3 | not reached | not reached | 3 / 3 |
| AgentTaskInputFallbackTests | 2 | not reached | not reached | 2 / 2 |
| AgentTaskDispatchBaseGuardTests | 1 | not reached | not reached | 1 / listed |
| AgentTaskCheckSweepTests, AgentTaskDispatchFailureTests, KeepSixReminderTests | 1 each | not reached | not reached | not listed |

0b455324d's only reds were three `ClaudeAdapterEffortPromptTests` cases (profile chunk 7); that
class passed in group 2 of this run. cadf547f7 also failed
`AgentReportStoreTests.Publication_exposes_only_a_complete_verified_report`, which passed here
(group 17), so that one is a flake candidate.

## 5. Pending heavy classes and remaining wall time

Discovery roster: 18993 nodes, 7 excluded, 1350 classes. Done through group 27: 141 classes,
about 2020 nodes. Pending: 1209 classes, 16967 nodes, of which 2633 are `Slow` (weight 4 under
CARD-1404; Land/Delivery/Check types double). Classes over the 120 cap launch alone:

| Pending class | nodes | Slow | weight (approx) |
|---|---:|---:|---:|
| Scripts.RemoteScriptContractTests | 499 | 0 | 499 |
| Application.DelegationDispatchRecoveryBoundaryTests | 381 | 0 | 381 |
| Application.AgentTaskReplyIntegrationTests | 334 | 0 | 334 |
| Application.AttentionServiceTests | 167 | 167 | 668 |
| Application.SessionRunnerHeldEventDeliveryTests | 131 | 0 | 131 |
| Application.SessionMessageQueueDeliveryVerificationTests | 123 | 123 | 492 |

Rate observed: 27 launches in 86.1 min (16:13:43Z to 17:39:50Z), 3.2 min per launch including
the 46 s launch gap. Application groups 15-27 averaged 4.5 min of run plus the gap. With about
204 launches remaining (caller's 231 total), the projection is 11 h at the overall mean and
18 h at the Application mean, finishing between 05:00Z and 12:00Z on 2026-10-22.

Watchdog exposure: group 27's 16 Slow Land nodes took about 2 min each. If
`AttentionServiceTests` (167 Slow) or `SessionMessageQueueDeliveryVerificationTests` (123 Slow)
behave similarly, each single-class launch exceeds the 60 min cap and produces a missing-TRX row.
There is no Windows evidence for any of the six classes above completing: 81cfb267a and 42cbbd04
were cap-killed in a single process before reaching them.

## 6. Suggested fix owners (one card per cause)

1. Cause A, server + tests: probe PATH ceiling (18 reds). Highest count, deterministic on every Windows host with a long user PATH.
2. Cause H, Investigate: BridgeQueueHarness family on Windows (14 reds). Needs an isolated repro.
3. Cause B, tests: embed or skip the 04228bff producer pack (5 reds).
4. Cause C, rc line: cherry-pick 13048a2eb (1 red, zero-risk).
5. Cause D, tests: Codex High literal (1 red).
6. Cause E, tests: C1213 remote arm Linux-only (1 red).
7. Cause F, tests: KeepSix NotInParallel key (1 red).
8. Cause G, tests: git-hook delay on Windows (1 red).

## Not done, noted

No test rerun, no edit to the candidate, state, checkout or policy; the running task was not
touched. Fix ideas are the one-line owners above. Linux pass/fail for the 42 names was not read
(no Linux lane evidence is stored under `C:\Antiphon\releases`; the local master nightly under
`C:\Antiphon\nightly` last ran 2026-09-04).

## Remaining uncertainties

- Cause F and Cause G are classified from source and the run's shape, not reproduced.
- Cause H has no confirmed mechanism; the packing-vs-single-process split is untested.
- The remaining-time projection assumes the caller's 231-launch plan and the observed per-launch
  gap; the plan file itself is not persisted in the log directory.
- Whether `AttentionServiceTests` Slow nodes run at group 27's per-node cost is a projection.
