# Windows rc live reds at 42cbbd04, 2026-10-19

Task `93971d09`. Branch `feat/card-task-93971d09`. Read-only on `C:\Antiphon\releases\checkout` and the candidate logs. No product, test, or policy file was edited. No suite was rerun. The nightly process was not started, stopped, or signalled.

The profile is not rc-release green. The antiphon 60 minute kill is the third one, and it is the one-slot `ProcessSpawnLimit` lane filling the hour. A 90 or 120 minute antiphon watchdog still leaves the method census unfinished.

## Run

| Field | Value |
|---|---|
| Candidate | `rc-2026-10-18-42cbbd0417a2` |
| Ref | `rc/2026-10-18` |
| SHA | `42cbbd0417a2611f3a690e6671b13c9ba51fbf34` |
| Run id | `7daa77ac9d994e1a8afa619db0db6ae3` |
| Profile / trigger | `rc` / `rc` |
| Started | `2026-10-19T04:07:19.6988642Z` |
| Completed | `2026-10-19T07:52:19.0512545Z` |
| Duration | 13493.785 s (`summary.json`) |
| Phase | `final` |
| Exit | `exitCode` 3, `testExit` 1, `reportExit` 3 |
| Result | `coverageComplete` false, `testsPassed` false, `reportDelivered` false, `succeeded` false, outcome `TESTS` |
| Policy hash | `37593bf0ff6e6da787079234d9c55aa37abd7a08b2737c30746e6b0a2e175744` |
| State | `C:\Antiphon\releases\candidates\rc-2026-10-18-42cbbd0417a2\state\last-run.json` |
| Logs | `C:\Antiphon\releases\candidates\rc-2026-10-18-42cbbd0417a2\logs\2026-10-19-0407-7daa77ac` |

Builds all passed: npm ci, npm run build, client lint, `dotnet build Antiphon.sln` (`summary.json` `builds`, each `exitCode` 0, `timedOut` false). E2E preflight: docker exit 0, disk free 53687091200 (minimum 10737418240), playwright-install exit 0, client bundle present at `client\dist\index.html`.

`reportExit` 3 left `card.md` in the log directory. That filing failure is the same shape as the `1f72e201` gate. It is not a product assertion.

Watchdogs on this SHA, `tests/test-execution-policy.json:737-749`:

| Key | Line | ms |
|---|---:|---:|
| antiphon | 741 | 3600000 |
| session-runner | 742 | 1200000 |
| e2e | 748 | 7200000 |

The `e2e` key is present on this SHA. The `1f72e201` gate had no `e2e` key, so `Get-NightlyWatchdogMs` used the 3600000 fallback. This run is the first of the three gates with a 120 minute e2e cap.

## How a failure was classified

Inherited means the same console name is on the inherited list in `docs/investigations/2026-10-19-rc-regate.md` (branch `feat/card-task-9ca04df4`, tip `c1d124c9`), which itself inherited the `rc/2026-10-15` `61117dd1` list. New-in-rc means it failed on this run and is not on that list. Flake means it failed here, the owning test file has no commit in `1f72e201..42cbbd04`, and the `1f72e201` console did not contain it.

Diagnostic reads keep the last state per UID and carry the timestamp from the previous `pushed data:` line. `TrxExceptionProperty` on the `TestNodeUpdateMessage` line is a failure even when `ErrorTestNodeStateProperty` sits on a later physical line. A generic `TypeName` is stripped at `(`. Pass records that only saw the first physical line reported `InProgress=79` for antiphon; one UID is `InProgress` with a null duration. The other 78 are continuation-line artifacts. They are not hangs.

## Suite verdicts

| Suite | Result | Evidence |
|---|---|---|
| antiphon | FAIL, watchdog | console failures 70; diag wall 3590.9 s; 2768 UIDs; no TRX |
| session-runner | FAIL, watchdog | console failures 30; diag wall 1191.5 s; 1547 UIDs; no TRX |
| pty-host | pass, coverage incomplete | TRX total 147, failed 0, succeeded 116, skipped 31, 22.557 s. `requiredUidCount` 124, `excludedUidCount` 23. Linux skips |
| agents-pty | FAIL | TRX total 702, failed 1, succeeded 634, skipped 67, 2m 07.916 s |
| messaging | pass | TRX total 250, succeeded 250, 50.079 s |
| client | pass | Test files 124 passed (124). Tests 1807 passed (1807). Exit 0 |
| scripts | FAIL | worktree guard 3 passed, 1 failed. Every other script log in the run exits 0 |
| e2e | FAIL, watchdog | console failures 19; log closed 08:52:09 local; no TRX; no test-run summary |
| markdown-pdf | pass, coverage incomplete | TRX total 20, failed 0, succeeded 19, skipped 1, 4.203 s. Skip is `Four_documents_produce_readable_combined_pdf` (`ANTIPHON_HEADED_TESTS`). `requiredUidCount` 16, `excludedUidCount` 4 |

`script-test-deploy-am-service.log` prints `REMOTE DEPLOY VERDICT: failed` on a negative-path case and then exits 0. That line is not a script failure.

## Antiphon 60 minute cap

`summary.json` records `timedOut: true` and `missing-trx` for antiphon. The diagnostic `log_261019042145000.diag` spans `2026-10-19 04:21:45 +01:00` through `05:21:35 +01:00`, wall 3590.9 s, against the 3600000 ms watchdog. The same cap killed `a1d24b6f` and `1f72e201`.

Last-state counts: UIDs 2768, `Error` 70 (matches `excLines` 70 and the console), `Passed` 2599, `Skipped` 4, `None` 16, `InProgress` 79 of which one has a null duration. Body-time sum of returned tests 7607.6 s. Percentiles of returned durations: p50 0.557 s, p90 5.926 s, p99 43.239 s, max 77.832 s. Twenty returned tests are at or above 60 s. None of them is the kill.

The single duration-null UID, admitted on the kill timestamp, age 0:

`AgentTaskLandBareRepositoryCleanupTests.C1211_NestedTrackedHeadKeepsExistingAdmission`

That method did not return. It is not a console failure. A different method of the same class did return and fail (`C1211_PublicationPreservesBareRepository`, below).

Admit / terminal counts by five-minute bin (minutes from the first diagnostic timestamp):

| Minute | Admitted | Terminal |
|---:|---:|---:|
| 0 | 2073 | 2004 |
| 5 | 76 | 73 |
| 10 | 62 | 58 |
| 15 | 11 | 9 |
| 20 | 5 | 5 |
| 25 | 4 | 4 |
| 30 | 6 | 6 |
| 35 | 20 | 20 |
| 40 | 21 | 18 |
| 45 | 53 | 49 |
| 50 | 428 | 418 |
| 55 | 9 | 9 |

The suite was still admitting in minute 55. Opening rate about 415 UIDs/min for five minutes, then about 13 UIDs/min, with a second burst near minute 50 (about 85/min). Overall about 46 UIDs/min (2768 / 3590.9 s). Body sum / wall is about 2.12, so most of the hour is one lane with a little overlap, not a stalled process.

### The lane is ProcessSpawnLimit

`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:15` is `public int Limit => 1`. The comment at lines 6-8 says it is the in-assembly gate: at most one process-spawning test at a time. Applied as a class-level `[ParallelLimiter<ProcessSpawnLimit>]`. A column-0 count of that attribute under `tests/Antiphon.Tests` is 356.

`AgentTaskLandActiveSourceClaimTests` (`AgentTaskLandActiveSourceClaimTests.cs:20-23`) is `[Category("Integration")]`, `[Category("Slow")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, and has no `[NotInParallel]`. Its 16 `C448_V29_ActiveSourceClaimHoldsEveryMode` rows are the tests at or above 60 s that passed.

Class wall span versus body sum. Span close to sum means the class ran one test at a time. Span much smaller than sum means the cases overlapped.

| Class | n | err | body s | max s | span s |
|---|---:|---:|---:|---:|---:|
| AgentTaskLandActiveSourceClaimTests | 16 | 0 | 1135 | 78 | 1157 |
| AgentTaskDispatchBaseGuardTests | 92 | 1 | 481 | 41 | 625 |
| AgentTaskLandAdoptionTests | 21 | 0 | 436 | 40 | 453 |
| AgentTaskLandBareRepositoryCleanupTests | 11 | 1 | 327 | 50 | 327 |
| AgentTaskLandAdoptionConcurrencyTests | 7 | 0 | 293 | 48 | 294 |
| LaunchEnvLayersIntegrationTests | 24 | 0 | 260 | 12 | 21 |
| AgentTaskLandApprovalRecoveryTests | 94 | 0 | 254 | 49 | 310 |
| TranscriptHotPathQueryTests | 4 | 3 | 215 | 76 | 215 |
| AgentSessionLaunchFailureTests | 71 | 0 | 214 | 61 | 214 |
| AgentTaskLandAdmissionTests | 8 | 0 | 210 | 32 | 209 |
| TranscriptHotPathMigrationTests | 11 | 6 | 172 | 73 | 172 |
| LandRefusalDeliveryWitnessTests | 24 | 1 | 170 | 23 | 170 |

The limiter classes in that table whose spans match their body sums cannot share the one slot: 1157 + 625 + 453 + 327 + 294 + 310 + 209 = 3375 s of the 3591 s wall (94%). `LaunchEnvLayersIntegrationTests` overlapped (span 21 s against 260 s of body) and is not that lane. `TranscriptHotPathQueryTests` and `TranscriptHotPathMigrationTests` are keyed `[NotInParallel]` lanes, a few minutes each, and they returned.

Keyed `[NotInParallel]` after stripping `(...)` from the type name: 365 classes, 1107 returned tests matched. Largest body sums: unkeyed parallel label 1566 tests / 4628 s (this label includes the land classes, which are serial through `ProcessSpawnLimit`); `AgentQueue` 298 s; `MessageQueue` 277 s; `AgentSessionLaunchFailure` 224 s; `TranscriptHotPathQueries` 215 s; `Board` 176 s; `TranscriptHotPathMigrations` 172 s; `c1196-land-refusal` 170 s; `card-0505-advisory-lock` 169 s. The unkeyed `[NotInParallel]` queue is below that list, under about 33 s of body time. It is not the cap.

Returned tests at or above 60 s (20):

- Sixteen `C448_V29_ActiveSourceClaimHoldsEveryMode` rows, all Passed, 62.8–77.8 s (Fresh / AlreadyPresent / ResumePublication / CleanupRetry, canonical / alias, follow-up / merge-helper).
- `Interactive_sign_in_block_persists_LaunchBlock_and_the_named_reason`, Passed, 61.0 s.
- `Repeated_hot_reads_do_not_add_transcript_sequential_scans`, failed, 76.0 s.
- `Populated_prechange_upgrade_preserves_all_rows_and_uuid_kinds`, failed, 72.9 s.
- `Prepared_plans_keep_the_partial_index_paths`, failed, 68.0 s.

Antiphon skips, same four as the `1f72e201` gate: `Full_round_trip_via_seam_returns_response_text`, `Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer`, `C1032_TransportQueueRecipientReplay`, `Full_round_trip_via_the_production_runner_adapter_returns_the_models_answer`.

### Would 90 or 120 minutes finish it

There is no antiphon discovery JSON. The kill happens before the end-of-run flush, same as the two earlier gates. A source census of `tests/Antiphon.Tests` on this checkout counts 12089 `[Test]` methods, 7054 `[Arguments]` rows, and a heuristic 17696 cases. The heuristic can mis-count argument expansion. Reached UIDs are 2768, in line with the hangs report's 2771 at `1f72e201`. The reached set is not the census.

At the overall 46 UIDs/min, 90 minutes reaches about 4160 UIDs and 120 minutes about 5550. At the post-warmup rate (695 admits in the 3291 s after the first five minutes, about 12.7/min), an extra 30 minutes adds about 380 UIDs and an extra 60 minutes about 760. Both rates leave the 12089-method census unfinished. The opening 415/min did not hold after minute 5, because the limiter lane took the process.

`AgentTaskLandActiveSourceClaimTests` is the largest single slice: 16 cases, 0 errors, span 1157 s, about 32% of the hour. Dropping that green matrix frees about 19 minutes. The other admitted limiter classes still occupy the rest of the hour, and 356 limiter classes exist. Excluding that one class does not make the suite fit.

This updates `docs/investigations/2026-10-19-rc-watchdog-sizing.md`. That note said to raise `antiphon` off 3600000 only after the named classes return and the wall exceeds 50 minutes. On this run the classes return, the longest returned test is 78 s, and the wall hits the cap with admissions still happening. The remainder still does not fit in 90 or 120 minutes. The raise is not the remedy.

## Session-runner 20 minute cap

`timedOut: true`, `missing-trx`. Diagnostic wall 1191.5 s (cap 1200 s), from `05:21:50 +01:00` to `05:41:41 +01:00`. UIDs 1547, `Error` 30, `Passed` 1491, `Skipped` 17. One duration-null UID, age 0, on the kill timestamp:

`CompletedCardWorkspaceRemovalTests.C1017_RunnerFreshFinalInspection`

`CompletedCardWorkspaceRemovalTests` is n=88, err=4, body sum 243 s, span 1140 s, max returned sibling 14 s. The span is the class spread across the suite, not a 19 minute serial block. The longest returned test is `WorkspaceSourceVerificationWireTests.C1065_LocalSourceModesVerifyFreshAtRelease`, Passed, 158.3 s. `BlockedParkWireTests` returned at 90.3 s and 83.3 s.

The source census heuristic (1040 methods, 1380 cases) is below the 1547 UIDs reached. The reached count matches the prior gates' expanded suite (about 1548). Treat this run as the full suite, killed on the last admit. The two Codex launch-policy methods and the latency probes are not the 20 minute problem; probes returned (class max 32 s in the duration table).

A watchdog of 1800000 ms at `tests/test-execution-policy.json:742` is long enough for that last admit to return and for the TRX to be written. 1500000 ms is the smaller step that also covers a sibling-scale last test. Either number leaves the 30 assertion reds in place. The sizing note's precondition (raise only after `CodexWindowsLaunchPolicyTests` returns) is met on this log.

## E2e 120 minute cap

`timedOut: true`, evidence reason `missing-trx`. The trx path in `summary.json` is `e2e-all.trx`. That file is absent. `e2e-all-tests.log` is 9477906 bytes, last write `2026-10-19T08:52:09` local. The diagnostic `log_261019055207499.diag` is 49064214 bytes, last write `08:51:51` local. The process start seen while it was alive was `06:52:07` local (pid 426544 at the 08:47:51 snapshot, cpu 611.6 s, about 116 minutes into the 120 minute cap). At `08:54:06` the process was gone. The log has 19 `failed` lines and no `Test run summary`.

A snapshot of the still-growing stdout at `2026-10-19T08:47:51+01:00` had 18 of those 19. The finished log adds `C540_PreTypingCrashRecoversWarnings`. The tail of the diagnostic was still publishing node updates, including `WorktreeRetirementDeliveryE2ETests.C459_LaunchReservationRecoveryRetainsOwnership`, `AgentE2ETests.Agents_page_creates_agent_and_assigns_card_to_queue` (a `TimingProperty` is on that record), and `BoardE2ETests.Board_user_can_edit_workflow_md_and_reload_persists_version`. The suite had moved past the C540 console failures. This task did not histogram the 49 MB diagnostic, so there is no e2e UID census and no recommended replacement for 7200000.

The hangs report's three names are still red, and the 120 minute window reached more rows of the same assertions plus the C540 waits. All 19 returned. They are not hangs.

## Antiphon console failures (70)

68 of 68 inherited names recurred. No inherited name was missing. Two names are outside that list.

### Flake (1)

`Interrupted_Grok_start_recovers_committed_runner_receipt_once_before_work(False)`, 3s 901ms. `ConflictException`: duplicate value on `IX_Projects_Name` ("another operation changed agent data"). First frame `AgentService.CreateAsync`, `server/Application/Services/AgentService.cs:631`. The test file has no commit in `1f72e201..42cbbd04`, and this name is absent from the `1f72e201` console.

### New-in-rc, stable (1)

`C1211_PublicationPreservesBareRepository(obj/head-only, head-only)`, 5s 057ms. `UnauthorizedAccessException`: access denied removing directory `762c69ea31f95db2ca5f82abc63edb10d1cf46`. First frame `System.IO.FileSystem.RemoveDirectoryRecursive`. The regate note called this unclassified (present at `61117dd1`, not re-run there) and it failed again at `1f72e201`. Same red on this tip. The owning file has no commit in `1f72e201..42cbbd04`.

### Inherited, foreign key on card reopen (12)

`PostgresException` 23503, `FK_CardWorktreeCleanups_CardRevisions_DoneRevisionId`. First frame `Npgsql.Internal.NpgsqlConnector.ReadMessageLong`. The first test's own frame is `CardCorrectionIntegrationTests.Reopen_writes_one_revision_with_the_superseded_terminal_facts_and_clears_them` at `CardCorrectionIntegrationTests.cs:1492`, reached through `CleanupProjectsByTempRootAsync` at line 2141.

- `Reopen_writes_one_revision_with_the_superseded_terminal_facts_and_clears_them` (3s 567ms)
- `Reopen_defaults_to_the_backlog_column_and_never_spawns` (1s 033ms)
- `Reopen_of_an_archived_card_is_refused_until_unarchive` (1s 272ms)
- `Reopen_with_a_stale_token_is_a_conflict_and_writes_no_revision` (845ms)
- `A_card_in_a_terminal_column_can_still_be_corrected` (483ms)
- `A_reopened_card_recloses_with_a_fresh_completion` (757ms)
- `A_terminal_move_records_both_the_revision_and_the_terminal_reason` (563ms)
- `A_terminal_move_stores_a_reason_far_longer_than_the_old_thousand_character_column` (761ms)
- `A_terminal_move_rejection_names_the_reopen_endpoint` (766ms)
- `Reopen_deletes_no_review_checkpoint` (1s 430ms)
- `Reopen_of_a_live_card_or_into_a_terminal_column_or_without_a_reason_is_rejected` (996ms)
- `Reopen_when_no_live_column_exists_is_a_conflict` (1s 122ms)

### Inherited, missing TranscriptEntries column (5)

`DbUpdateException` while saving. Inner `PostgresException` 42703: column `ApiErrorTimeZoneId` of relation `TranscriptEntries` does not exist (`antiphon-all-tests.log` around lines 373, 425, 457, 1088, 1232). First frame `Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync`.

- `Retry_preserves_a_valid_index_and_replaces_two_invalid_indexes(0)` (17s 784ms), test frame `TranscriptHotPathMigrationTests.cs:32`
- `Retry_preserves_a_valid_index_and_replaces_two_invalid_indexes(1)` (13s 121ms), same frame
- `Retry_preserves_a_valid_index_and_replaces_two_invalid_indexes(2)` (3s 624ms), same frame
- `Retry_replaces_all_three_interrupted_invalid_indexes` (10s 694ms), same frame
- `Populated_prechange_upgrade_preserves_all_rows_and_uuid_kinds` (1m 12s 918ms), test frame `TranscriptHotPathMigrationTests.cs:217`

`Down_up_roundtrip_preserves_data_and_old_query_compatibility` (4s 887ms) is the sixth migration failure and a different assert: `ShouldAssertException: await FingerprintAsync(f.ConnectionString)`, first test frame `TranscriptHotPathMigrationTests.cs:243`.

### Inherited, release-checkout code evidence path (5)

`DirectoryNotFoundException`: `C:\Antiphon\releases\checkout\.git\antiphon\code-evidence\04228bff\producer-pack-index.json`. First frame `SafeFileHandle.CreateFile`.

- `C1303_R12_LandRefusesUnavailableCorrectedEvidence(missing-blob)` (576ms)
- `C1303_R12_LandRefusesUnavailableCorrectedEvidence(changed-blob)` (1s 090ms)
- `C1303_R12_LandRefusesUnavailableCorrectedEvidence(missing-bundle)` (605ms)
- `C1281_ReceiptCorrection` (991ms)
- `C1281_IncompleteNeverClean` (728ms)

### Inherited, runner process probe (9)

All in `RunnerProcessProbeTests.cs`. First frame is the test method.

- `Probe_timeout_kills_the_entire_process_tree` (76ms), `ShouldAssertException: result.TimedOut`, line 173
- `Probe_redacts_common_credential_names_in_assignments_and_json` (29ms), `result.SensitiveOutputDetected`, line 587
- `Probe_redacts_credential_shaped_output_without_persisting_raw_values` (7ms), `result.SensitiveOutputDetected`, line 562
- `Late_process_start_after_the_one_second_deadline_is_reaped` (5s 030ms), `TimeoutException`, line 490
- `Bounded_startup_that_exits_on_stdin_close_is_clean` (4ms), `result.CleanlyStopped`, line 536
- `Reaper_host_stop_kills_every_already_started_tracked_process` (5ms), `result.CleanupConfirmed`, line 304
- `Unconfirmed_primary_cleanup_is_owned_and_completed_by_the_reaper` (5ms), `result.TimedOut`, line 256
- `Invalid_utf8_output_is_discarded_as_unusable` (27ms), `result.OutputTruncated`, line 146
- `Probe_caps_combined_stdout_and_stderr_and_signals_truncation` (5ms), `result.OutputTruncated`, line 97

### Inherited, hot-path membership assert (3)

`ShouldAssertException: Runtime.TryGetTranscriptPersistFailure(SessionIds[0], out _)`. First frame `TranscriptHotPathFixture.MembershipAsync`, `TranscriptHotPathFixture.cs:129`.

- `Repeated_hot_reads_do_not_add_transcript_sequential_scans` (1m 15s 955ms)
- `Prepared_plans_keep_the_partial_index_paths` (1m 07s 976ms)
- `Uuid_membership_seeks_by_session_and_uuid` (31s 056ms)

### Inherited, phone-home (1)

`C1032_TransportClientUnsupported` (34s 098ms). `TimeoutException`: "Phone-home live connection was not accepted." First frame `PhoneHomeTestHost.WaitLiveAsync`, `PhoneHomeTestHost.cs:196`.

### Inherited, the rest (32)

Each line is name, duration, exception, first useful frame.

- `ModelTier_maps_against_the_resolved_profile_kind_and_omits_raw` (8s 999ms). `ShouldAssertException: codexLaunch.Spec.Args.TakeLast(2)`. `AgentTuiLaunchResolverTests.cs:246`.
- `Resume_of_a_mid_turn_session_writes_a_restart_boundary_and_queues_the_auto_continue` (2s 521ms). `InvalidOperationException: Sequence contains no elements`. `ShapedQueryCompilingExpressionVisitor.SingleAsync`.
- `C1196_Lost_wakeup_is_recovered_by_the_stranded_queue(busy)` (3s 472ms). `ShouldAssertException` on `ReadPromptsAsync`. `LandRefusalDeliveryWitnessTests.cs:239`.
- `Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart` (75ms). `ShouldAssertException: rows.Select(r => r.Text)`. `AgentSessionRuntimeTests.cs:234`.
- `Output_bursts_write_LastSeenAt_at_most_once_per_interval` (1s 290ms). `ShouldAssertException: writes.Count`. `AgentSessionRuntimeActivityTests.cs:45`.
- `Fallback_with_stale_mid_turn_transcript_still_delivers_bootstrap` (1s 987ms). `ArgumentOutOfRangeException` on `List.get_Item`.
- `Resume_not_found_fallback_delivers_fresh_bootstrap` (1s 467ms). `ShouldAssertException: factory.Created.Count`. `AgentSystemPromptLaunchTests.cs:327`.
- `catch_up_settles_when_the_turn_end_was_already_persisted` (150ms). `ShouldAssertException: settled.Status`. `AgentTaskCatchUpSettlementTests.cs:69`.
- `catch_up_settles_even_when_channel_reply_dispatch_throws` (107ms). `settled.Status`. `AgentTaskCatchUpSettlementTests.cs:52`.
- `catch_up_of_a_report_boundary_settles_a_dispatched_task_as_marked` (250ms). `settled.Status`. `AgentTaskCatchUpSettlementTests.cs:38`.
- `Runner_write_failure_types_only_the_durable_api_pointer(flush)` (4s 176ms). `ShouldAssertException: changed.Body`. `AgentTaskInputFallbackTests.cs:120`.
- `Launch_note_yields_to_an_owed_channel_row` (3s 441ms). `ShouldAssertException: adapter.SubmittedBodies`. `AgentSystemPromptLaunchTests.cs:397`.
- `Ordinary_spill_errors_keep_their_existing_delivery_behavior(flush)` (1s 294ms). `ShouldAssertException: reverted.DeliveryAttempts`. `AgentTaskInputFallbackTests.cs:396`.
- `Second_start_on_a_live_session_is_a_no_op_and_does_not_rebootstrap` (3s 267ms). `ConflictException`: "Stop the current conversation before selecting another." `AgentControlService.StartAsync`, `AgentControlService.cs:177`.
- `Start_with_system_prompt_append_passes_flag_on_fresh_launch` (2s 598ms). `ShouldAssertException` on `AgentIncidents.AnyAsync`. `AgentSystemPromptLaunchTests.cs:130`.
- `C1169_AgedScreenOnlyRefinementFollowsInterruptedSentRecovery(head-only)` (557ms), `(no-prompt-enter-only)` (318ms), `(no-prompt-retype)` (407ms), `(complete-prompt)` (176ms). All `ShouldAssertException: row.Status`. `AgentTaskMidTurnRefineTests.cs:511`.
- `C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt(flush-transport-failure-then-retry)` (138ms), `row.DeliveryAttempts`, `AgentTaskMidTurnRefineTests.cs:268`.
- `C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt(cancelled-then-no-prompt)` (168ms), `(cancelled-then-head-only-prompt)` (134ms), `(cancelled-then-prompt)` (128ms). All `row.Status`, `AgentTaskMidTurnRefineTests.cs:273`.
- `both_reuse_messages_enqueue_when_nothing_throws` (107ms). `ShouldAssertException: messages.Count`. `AgentTaskReuseEnqueueTests.cs:179`.
- `C1213_SourceLandingKeepsLegacyIdentity` (5s 210ms). `ConflictException: verification_creation_outside_runner_repository`. `RemoteVerificationWorkspace.RequirePrefix`, `RemoteVerificationWorkspace.cs:110`.
- `C1065_FullAnswerRequiresNewSessionUserPrompt` (14s 554ms). `ShouldAssertException: f.Submitted` (submitted text does not contain the task brief). `BlockedTaskParkDeliveryTests.cs:45`.
- `C514_Working_modal_waits_for_operator_then_converts_without_replay` (651ms). `ShouldAssertException: h.Adapter.SubmittedBodies`. `RemoteControlRecoveryPtyIntegrationTests.cs:162`.
- `C1065_ParkedReviewReplyBindsFreshEvidence` (7s 561ms) and `C1104_ParkedReviewReplyReceiptSpillsToPointerAtProductionChunkSize` (7s 650ms). `ShouldAssertException: (await f.ParkAsync()).State`. `BlockedTaskParkDeliveryTests.ReviewWorld.StartAsync`, `BlockedTaskParkDeliveryTests.cs:949`.
- `a_busy_standing_agent_makes_the_next_task_wait_and_take_it_after_the_first_settles` (4s 493ms). `secondNow.Status`. `AgentTaskStandingAgentDispatchTests.cs:134`.
- `an_unmarked_turn_on_the_standing_session_is_nudged_and_still_leaves_the_agent_alone` (1s 780ms). `stored.ReportNudgedAt`. `AgentTaskStandingAgentDispatchTests.cs:398`.
- `a_marked_turn_on_the_standing_session_settles_the_pinned_task_and_leaves_the_agent_alone` (1s 926ms). `settled.Status`. `AgentTaskStandingAgentDispatchTests.cs:353`.

Name count: 1 flake + 1 new-in-rc + 12 FK + 5 schema + 1 fingerprint + 5 evidence path + 9 probe + 3 hot-path + 1 phone-home + 32 rest = 70. The 68 inherited names are that total without the flake and the C1211 red.

## Session-runner console failures (30)

29 match the regate inherited list. `A_live_tailer_that_loses_its_claim_stops_reading_and_resumes_discovery` (unclassified on the `1f72e201` gate) did not fail here.

### New-in-rc (1)

`Websocket_connect_that_hangs_ends_within_the_connect_timeout` (145ms). `ShouldAssertException` on `Task.WaitAsync(TimeSpan.FromSeconds(3))`. First frame `ContinuationResultTaskFromResultTask.InnerInvoke`. The regate note classified this new: it passed when re-run at `61117dd1` and failed at `1f72e201`. It failed again here.

### Inherited, Windows pin and publication (9)

- `V32_InspectPosixPaths` (83ms) and `V13_InspectPathSafety` (83ms). `ShouldAssertException: OperatingSystem.IsLinux()`. Additional information "CP-4a requires native Linux qualification". `AgentPinWorkspaceStoreTests.cs:19` and `:54`.
- `V34_InspectProtocolAdmission` (151ms). `result.Reason` should be `pin_cwd_invalid` but was `pin_platform_unsupported`. `AssertRefused`, `AgentPinWorkspaceStoreTests.cs:172` (test at line 156).
- `V14_PublicationReplay` (162ms). `result["Status"]` expected 2, got 1. `AgentPinPublicationTests.cs:79`.
- `V14_PublicationFences` (7ms). `result["Status"]` expected 0, got 1, reason `pin_platform_unsupported`. `Receipt`, `AgentPinPublicationTests.cs:315`.
- `V34_PublicationAdmission` (39ms). `result["Reason"]` should be `pin_cwd_invalid` but was `pin_platform_unsupported`. `Refused`, `AgentPinPublicationTests.cs:309`.
- `V14_PublicationSerialization` (10s 019ms). `TimeoutException`. `AgentPinPublicationTests.cs:268`.
- `V15_PublicationCleanup` (16ms) and `V13_PublicationCustody` (12ms). `result["Reason"]` is `pin_platform_unsupported`. `Refused`, `AgentPinPublicationTests.cs:309`.

### Inherited, Unix checkpoint selected on Windows (15)

`InvalidOperationException: Unix checkpoint selected on Windows`. First frame `UnixPtyArgvAdmissionTests.RequireUnix`, `UnixPtyArgvAdmissionTests.cs:284`. All under 3 ms except where noted; these are 0–2 ms.

- `Nul_request_is_refused_before_effects(tracked)`, `(grok)`, `(grok_payload)`, `(plain)`
- `Platform_and_backend_boundaries_keep_existing_verdicts(unknown_backend)`, `(herdr_nul)`, `(platform_mismatch)`, `(platform_invalid)`
- `Malformed_inputs_reach_existing_runtime_validation`
- `Null_exe_keeps_existing_validation_message_on_launch_routes(ordinary)`, `(platform_constrained)`
- `Nul_refusal_is_named_on_both_launch_routes(platform_constrained)`, `(ordinary)`
- `Phone_home_nul_refusal_is_named(launch)`, `(platform_constrained)`

### Inherited, workspace identity (5)

- `C1032_TransportDispatchReplay` (1s 939ms). `ShouldAssertException: legacy.ErrorCode`. `CompletedCardWorkspaceTransportTests.cs:174`.
- `C1032_InspectionHoldShapes(other-repository, stale_mirror_binding)` (1s 281ms). `UnauthorizedAccessException` on a temp `.git` path. First frame `SafeFileHandle.CreateFile`.
- `C1032_InspectionHoldShapes(case-twin, case_ambiguous_content)` (1s 212ms). `ShouldAssertException: Directory.GetFiles(bin).Length`. `CompletedCardWorkspaceInspectionRepairTests.cs:144`.
- `C1017_RunnerRepositoryIdentity` (5s 023ms). `UnauthorizedAccessException` on a temp `.git` path. `CompletedCardWorkspaceS5Tests.cs:96`. First frame `SafeFileHandle.CreateFile`.
- `C1065_RepositoryIdentityReadIsBoundToSessionCheckout` (1s 491ms). `ShouldAssertException: identity.RepositoryIdentity`. `WorkspaceSourceVerificationWireTests.cs:33`.

Name count: 1 + 9 + 15 + 5 = 30.

## Agents-pty (1, inherited)

`A_doubled_quote_argument_splits_for_a_native_parser_and_not_for_dotnet` (393ms). `ShouldAssertException: looseRaw` should contain `--append-system-prompt?a "quoted" word` and the actual line was the echo-args shred `--echo-args --echo-argv-strict --append-system-prompt a "quoted" word`. First frame `FakeClaudeContractTests.cs:1691`. Same red as the regate list and the first gate.

## Scripts (1, inherited)

`script-test-apphost-main-worktree-guard.log`: `FAIL test setup or execution - git worktree add failed with exit 128`. `CARD-0273 main-worktree guard: 3 passed, 1 failed`. Exit code 1.

## E2e console failures (19)

The first three are the hangs report's unclassified set, red again, so new-in-rc and stable. The extra matrix rows and the C540 waits were not reached on the 60 minute gate. Every captured row of a given assertion failed the same way, so they are new-in-rc observations, not flakes.

### Exit code at the land fixture (6)

`ShouldAssertException: process.ExitCode`. First frame `LandDeliveryFixture.RequestAsync`, `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:178`.

- `C488_ReviewToLandReceiptMatrix(idle, Apply)` (2m 51s 667ms). This is the hangs-report row. That report's message at the same line was HTTP 409 `review_evidence_source_not_clean`. This run's extracted message is `process.ExitCode`.
- `C488_ReviewToLandReceiptMatrix(busy, spill)` (2m 32s 398ms)
- `C488_ReviewToLandReceiptMatrix(busy, Apply)` (2m 32s 138ms)
- `C488_ReviewToLandReceiptMatrix(busy, raw)` (3m 05s 713ms)
- `C488_ReviewToLandReceiptMatrix(idle, spill)` (2m 41s 910ms)
- `C488_ReviewToLandReceiptMatrix(idle, raw)` (2m 36s 645ms)

### Missing completion-scan observation (2)

`ShouldAssertException: Directory.GetFiles(..., "completion-scan-*.observation.json")`. The first test frame printed for both is `AgentTaskLandDeliveryE2ETests.C467_V28_LostFlushWakeupRecoversOnIdleCaller` at `AgentTaskLandDeliveryE2ETests.cs:154`. The two tests share that assertion; the extractor bound `C488_ApprovalLostFlushRecovers` to the same frame.

- `C467_V28_LostFlushWakeupRecoversOnIdleCaller` (46s 052ms)
- `C488_ApprovalLostFlushRecovers` (45s 742ms)

### Stale review text (1)

`C488_ApprovalOutcomeReceiptMatrix(stale-A)` (2m 32s 531ms). `ShouldAssertException: text`. First frame `LandDeliveryFixture.RefuseStaleReviewAsync`, `Fixtures/LandDeliveryFixture.Review.cs:406`.

### C540 quiet-60s waits (10)

`TimeoutException` from `Antiphon.TestSupport.ProgressAwareWait.UntilAsync`, `tests/Shared/ProgressAwareWait.cs:38`. Each returned after progress, then 60 s quiet. Not a hang.

- `C540_PreEnqueueCrashRecoversWarnings` (1m 35s 568ms): before-enqueue, quiet 60 s after 67.0 s, progress 46
- `C540_CollapsedWarningsReachIdleCaller` (1m 37s 751ms): two committed reduced dispatch intents, quiet after 73.7 s, progress 45
- `C540_ClaimCrashRecoversCollapsedWarnings` (1m 37s 104ms): dispatch-warning-claim-committed, quiet after 67.2 s, progress 46
- `C540_QueueInsertCrashReusesRows` (1m 37s 555ms): queue-inserted, quiet after 67.1 s, progress 46
- `C540_EnqueueFailureRetriesWarnings` (1m 42s 052ms): two committed reduced dispatch intents, quiet after 84.2 s, progress 45
- `C540_ProjectionCrashRecoversOriginalPairs` (1m 35s 866ms): dispatch-warning-before-commit, quiet after 67.5 s, progress 46
- `C540_ReceiptCrashDoesNotRetype` (1m 36s 452ms): receipt-before-save, quiet after 67.3 s, progress 46
- `C540_PostPromptCrashDoesNotRetype` (1m 36s 858ms): queue-before-verdict, quiet after 67.4 s, progress 46
- `C540_CollapsedWarningsWaitForBusyCaller` (1m 43s 832ms): two committed reduced dispatch intents, quiet after 84.1 s, progress 45
- `C540_PreTypingCrashRecoversWarnings` (1m 38s 842ms): queue-before-typing, quiet after 67.6 s, progress 46

Name count: 6 + 2 + 1 + 10 = 19.

## Ranked actions

These are recommendations. Nothing in this list was edited.

1. Leave `antiphon` at 3600000 (`tests/test-execution-policy.json:741`). This is the third identical cap kill. Tests return (max 78 s) and the suite is still admitting at minute 55. The mechanism is `ProcessSpawnLimit.Limit == 1`: admitted limiter spans already take 3375 of 3591 seconds, and 356 class-level limiter attributes exist. At about 46 UIDs/min overall, and about 13/min after warmup, 90 and 120 minutes do not reach a ~12089-method census. Operator decision: split or exclude the process-spawn lane from the rc profile (a `chunks` quarantine so the rest of the assembly can write a TRX), knowing `coverageComplete` still requires the quarantined classes. Excluding only `AgentTaskLandActiveSourceClaimTests` frees about 19 minutes and does not finish the suite. Keep the land classes in some profile; dropping `AgentTaskLandDeliveryE2ETests` from e2e drops land proof, and the same caution applies to the antiphon land classes.

2. Session-runner policy, rc line: raise `session-runner` from 1200000 to 1800000 at line 742 (1500000 is enough for a sibling-scale last test). The expanded suite was reached and the process died at 1191.5 s on `C1017_RunnerFreshFinalInspection`. The raise writes the TRX. It does not fix the 30 assertion reds.

3. Assertion clusters are rc-line code, separate from the watchdog. They fail on this rc tip. A master-only land does not move `rc/2026-10-18`. Clusters, in the order they dominate the console: `FK_CardWorktreeCleanups_CardRevisions_DoneRevisionId` (12), missing `TranscriptEntries.ApiErrorTimeZoneId` (5), release-checkout `.git/antiphon/code-evidence/.../producer-pack-index.json` (5), `RunnerProcessProbeTests` asserts (9), Windows pin / `pin_platform_unsupported` and `RequireUnix` (24 session-runner), workspace `.git` access and identity (5 session-runner), doubled-quote agents-pty (1), worktree-guard `git worktree add` exit 128 (1). E2e `process.ExitCode` at `LandDeliveryFixture.cs:178`, the missing `completion-scan-*.observation.json`, `RefuseStaleReviewAsync` text, and the ten `ProgressAwareWait` quiet-60s timeouts are the same kind of code item.

4. Leave `e2e` at 7200000 until this diagnostic is censused. The cap was reached, 19 failures had already returned, later classes were still publishing node updates, and there is no TRX. A new number from this note would be a guess.

5. `Interrupted_Grok_start_recovers_committed_runner_receipt_once_before_work(False)` is a flake (duplicate `IX_Projects_Name`, source unchanged, absent at `1f72e201`). `C1211_PublicationPreservesBareRepository` and `Websocket_connect_that_hangs_ends_within_the_connect_timeout` stay new-in-rc.

Client, messaging, and pty-host produced no assertion red. Pty-host and markdown-pdf coverage stays incomplete because of the recorded Linux / headed exclusions.

## Not done, noted

No product, test, or policy file was edited; no suite was rerun; the live nightly process was not stopped. A fix that splits `ProcessSpawnLimit` out of the rc antiphon chunk, or that raises the session-runner watchdog, belongs to a later Code task.

## Remaining uncertainties

- The antiphon and session-runner expected totals are a source census and a prior-gate UID count. Neither suite wrote discovery JSON.
- The e2e diagnostic was not reduced to a UID histogram. The last records show the suite still publishing `WorktreeRetirementDeliveryE2ETests`, `AgentE2ETests`, and `BoardE2ETests` when the file stopped. How many UIDs remained is unread.
- `C488_ApprovalLostFlushRecovers` shares its captured stack frame with `C467_V28_LostFlushWakeupRecoversOnIdleCaller` at line 154. The exception text is the completion-scan miss for both.
- `reportExit` 3 and `card.md` were not re-investigated. The prior gate already recorded the filing failure.
