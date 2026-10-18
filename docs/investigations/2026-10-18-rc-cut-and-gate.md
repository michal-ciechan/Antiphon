# RC cut and gate, 2026-10-18

Task `9d6824b0`. Branch `feat/card-task-9d6824b0`. No tag was created, moved, or pushed. `publish-release.ps1` and `promote-release.ps1` were not run. The canonical checkout `C:\src\Antiphon` was not switched or pulled. The production runner on port 17204 was not restarted and its sessions were not touched.

## Cut

`origin/master` at the cut was `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` (`feat(CARD-1311): publish vYYYY.MM.DD.N-rc.K and promote the final tag`).

Before the push, `git ls-remote --heads origin rc/2026-10-18` was empty. `refs/heads/rc/2026-10-15` was `61117dd1663d2f47ce4708c18b609b8936491986`.

Created, not forced:

```text
git push origin a1d24b6fc92d45ae5274b418bf54c6b8819a8697:refs/heads/rc/2026-10-18
```

Read-back:

```text
61117dd1663d2f47ce4708c18b609b8936491986	refs/heads/rc/2026-10-15
a1d24b6fc92d45ae5274b418bf54c6b8819a8697	refs/heads/rc/2026-10-18
```

`rc/2026-10-15` was not updated.

## Candidate journal

`scripts/release-cut.ps1 -Ref rc/2026-10-18 -SkipPublish` journalled the long-lived tip and did not push the branch.

| Field | Value |
|---|---|
| Candidate id | `rc-2026-10-18-a1d24b6fc92d` |
| Ref | `rc/2026-10-18` |
| SHA | `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` |
| Journal | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\candidate.json` |
| `pushed` | false |
| `createdAt` | `2026-10-18T16:40:54.7503471Z` |

The same process then refused the test step: `REFUSED: unmarked clone is not a proven nightly checkout` (`unmarked-clone`, exit 3). `release-candidate.ps1` writes `.antiphon-release-owned`. `nightly-run.ps1` accepts only `.antiphon-nightly-owned` and refuses an existing `.git` without it. Re-running `release-cut.ps1` repeats that refusal. Nothing was published.

A direct `nightly-run.ps1 -Profile rc` on a fresh checkout reached `phase=fetch` as run `39c9715c1eb3402da417da276455fa3c` and then exited 3 with the same unmarked-clone refusal. `git clean -fdx` removes the nightly marker, and the self-reexec hop then sees an unmarked clone. That run's owner pids `33212` and `32124` were dead. Its `state\run.lock` was still present; a dead owner is not cleared by the lock helper (`lock-held`). The stale lock file was removed so a later run could acquire it. No native lock was left under `C:\Antiphon\verification`.

## Windows lane (in progress)

The pinned checkout `C:\Antiphon\releases\checkout` is an independent clone of `https://github.com/michal-ciechan/Antiphon` at `a1d24b6fc92d45ae5274b418bf54c6b8819a8697`. It is not the canonical checkout and not a linked worktree. `.antiphon-nightly-owned` was written so the ownership check can accept it. The run uses `-NoSync`, so it does not fetch, reset, or clean, and it does not take the self-reexec hop. HEAD was verified equal to the candidate SHA before the process started. `-ExpectedSha` is still passed through.

Launcher: `C:\Antiphon\releases\start-rc-2026-10-18.cmd` (outside the repo), started with `Win32_Process.Create` so it is not a child of this session. `PATH` puts `C:\Program Files\PowerShell\7` first.

| Field | Value |
|---|---|
| Host | Windows desktop |
| Profile / trigger | `rc` / `rc` |
| Run id | `42d2c78424dd4612af43eed3afeaecc2` |
| SHA | `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` |
| Started | `2026-10-18T16:47:34.4931667Z` |
| Phase at record time | `tests` |
| pwsh pid | `38396` (parent cmd `18564`; nightly-tests child `10828`) |
| State | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\state\last-run.json` |
| Console log | `C:\Antiphon\releases\rc-2026-10-18-nightly-nosync.log` |
| Run logs | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\logs\2026-10-18-1647-42d2c784` |

Do not start a second Windows lane while pid `38396` is alive. At 18:55 local the test phase was still open (`phase=tests`, `exitCode` null, `lastActivityAt` still `2026-10-18T16:47:35Z`). The console log was still the single `running nightly-tests.ps1` line.

### Already observed

`npm ci` in `client` failed before the solution build. `client\node_modules` was absent. The log is `npm-ci.log` (1571 bytes). Both attempts:

```text
Error: Cannot find module 'C:\Antiphon\releases\checkout\client\node_modules\npm\bin\npm-prefix.js'
Error: Cannot find module 'C:\Antiphon\releases\checkout\client\node_modules\npm\bin\npm-cli.js'
code: MODULE_NOT_FOUND
Node.js v24.6.0
```

The `npm.cmd` on this host is `C:\Program Files\nodejs\npm.cmd`. This was not compared with `rc/2026-10-15` at `61117dd1`, so it is not classified as inherited. The runner did not stop on it. Diagnosis of this failure is a separate task.

`dotnet build Antiphon.sln` then succeeded: 0 errors, 1136 warnings, elapsed 00:02:14.10 (`dotnet-build.log`).

#### antiphon (ended 18:50 local)

`Antiphon.Tests.exe` pid `16296` started at 17:49:53 local. The profile watchdog for `antiphon` is `3600000` ms. The process was gone by 18:52, the diagnostic file's last record is 18:49:43 local, and `antiphon-all-tests.log` was closed at 18:50:00. `antiphon-all.trx` was not written. The nightly suite verdict stays in memory until the test phase ends, so `timedOut` is not on disk yet. The wall clock matches the watchdog.

`--no-progress` prints failures and skips only. Counts below are from the diagnostic log's last flushed state per test uid (2733 uids) plus the console failure lines. `FailedTestNodeStateProperty` occurs 0 times: the failure records were not flushed before the process ended, so the 72 console failures are not an extra 72 on top of the 2575 passes.

| Outcome | Count | Source |
|---|---|---|
| Passed | 2575 | diagnostic, last flushed state |
| Skipped | 4 | diagnostic and console, same four names |
| Failed, terminal diagnostic record | 0 | diagnostic |
| No flushed terminal state | 154 | diagnostic last state `InProgress` |
| Failed lines | 72 | `antiphon-all-tests.log` |

Skipped: `ClaudeAdapterIntegrationTests.Full_round_trip_via_seam_returns_response_text`, `ClaudeEffortPromptCanaryTests.Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer`, `CodexAdapterIntegrationTests.Full_round_trip_via_the_production_runner_adapter_returns_the_models_answer`, `PhoneHomeCompletedCardTransportTests.C1032_TransportQueueRecipientReplay`.

The 72 console failures are unclassified (inherited versus new) while this lane is still running. A one-test check at `61117dd1` waits until pid `38396` has exited, so it does not take the build slot or the native lock under the live gate. None of the brief's named suspects appear in the console log: `C1192_Npm_volume_sentinel`, `C1191_Fixture_failure`, `C1105_Git_audit`, `C599_CloneOwnership`, `C599_ReportLane`, `LandDelivery`.

Exception groups: 37 `ShouldAssertException`, 12 `PostgresException` (`FK_CardWorktreeCleanups_CardRevisions_DoneRevisionId` on the card-correction tests), 8 `UnauthorizedAccessException`, 5 `DbUpdateException`, 5 `DirectoryNotFoundException` (missing `checkout\.git\antiphon\code-evidence\...\producer-pack-index.json`), 2 `ConflictException`, 1 `ArgumentOutOfRangeException`, 1 `InvalidOperationException`, 1 `TimeoutException`.

Console failures, `file :: test`:

- `AgentTui\AgentTuiLaunchResolverTests.cs :: ModelTier_maps_against_the_resolved_profile_kind_and_omits_raw`
- `AgentTui\RunnerProcessProbeTests.cs :: Bounded_startup_that_exits_on_stdin_close_is_clean`
- `AgentTui\RunnerProcessProbeTests.cs :: Invalid_utf8_output_is_discarded_as_unusable`
- `AgentTui\RunnerProcessProbeTests.cs :: Late_process_start_after_the_one_second_deadline_is_reaped`
- `AgentTui\RunnerProcessProbeTests.cs :: Probe_caps_combined_stdout_and_stderr_and_signals_truncation`
- `AgentTui\RunnerProcessProbeTests.cs :: Probe_redacts_common_credential_names_in_assignments_and_json`
- `AgentTui\RunnerProcessProbeTests.cs :: Probe_redacts_credential_shaped_output_without_persisting_raw_values`
- `AgentTui\RunnerProcessProbeTests.cs :: Probe_timeout_kills_the_entire_process_tree`
- `AgentTui\RunnerProcessProbeTests.cs :: Reaper_host_stop_kills_every_already_started_tracked_process`
- `AgentTui\RunnerProcessProbeTests.cs :: Unconfirmed_primary_cleanup_is_owned_and_completed_by_the_reaper`
- `Application\AdvisoryChangePolicyTests.Correction.cs :: C1281_IncompleteNeverClean`
- `Application\AdvisoryChangePolicyTests.Correction.cs :: C1281_ReceiptCorrection`
- `Application\AdvisoryChangePolicyTests.Correction.cs :: C1303_R12_LandRefusesUnavailableCorrectedEvidence(changed-blob)`
- `Application\AdvisoryChangePolicyTests.Correction.cs :: C1303_R12_LandRefusesUnavailableCorrectedEvidence(missing-blob)`
- `Application\AdvisoryChangePolicyTests.Correction.cs :: C1303_R12_LandRefusesUnavailableCorrectedEvidence(missing-bundle)`
- `Application\AgentControlServiceIntegrationTests.cs :: Resume_of_a_mid_turn_session_writes_a_restart_boundary_and_queues_the_auto_continue`
- `Application\AgentSessionRuntimeActivityTests.cs :: Output_bursts_write_LastSeenAt_at_most_once_per_interval`
- `Application\AgentSessionRuntimeTests.cs :: Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart`
- `Application\AgentSystemPromptLaunchTests.cs :: Fallback_with_stale_mid_turn_transcript_still_delivers_bootstrap`
- `Application\AgentSystemPromptLaunchTests.cs :: Launch_note_yields_to_an_owed_channel_row`
- `Application\AgentSystemPromptLaunchTests.cs :: Resume_not_found_fallback_delivers_fresh_bootstrap`
- `Application\AgentSystemPromptLaunchTests.cs :: Second_start_on_a_live_session_is_a_no_op_and_does_not_rebootstrap`
- `Application\AgentSystemPromptLaunchTests.cs :: Start_with_system_prompt_append_passes_flag_on_fresh_launch`
- `Application\AgentTaskCatchUpSettlementTests.cs :: catch_up_of_a_report_boundary_settles_a_dispatched_task_as_marked`
- `Application\AgentTaskCatchUpSettlementTests.cs :: catch_up_settles_even_when_channel_reply_dispatch_throws`
- `Application\AgentTaskCatchUpSettlementTests.cs :: catch_up_settles_when_the_turn_end_was_already_persisted`
- `Application\AgentTaskDispatchBaseGuardTests.CardBranchNames.cs :: C1213_SourceLandingKeepsLegacyIdentity`
- `Application\AgentTaskInputFallbackTests.cs :: Ordinary_spill_errors_keep_their_existing_delivery_behavior(flush)`
- `Application\AgentTaskInputFallbackTests.cs :: Runner_write_failure_types_only_the_durable_api_pointer(flush)`
- `Application\AgentTaskMidTurnRefineTests.cs :: C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt(cancelled-then-head-only-prompt)`
- `Application\AgentTaskMidTurnRefineTests.cs :: C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt(cancelled-then-no-prompt)`
- `Application\AgentTaskMidTurnRefineTests.cs :: C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt(cancelled-then-prompt)`
- `Application\AgentTaskMidTurnRefineTests.cs :: C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt(flush-transport-failure-then-retry)`
- `Application\AgentTaskMidTurnRefineTests.cs :: C1169_AgedScreenOnlyRefinementFollowsInterruptedSentRecovery(complete-prompt)`
- `Application\AgentTaskMidTurnRefineTests.cs :: C1169_AgedScreenOnlyRefinementFollowsInterruptedSentRecovery(head-only)`
- `Application\AgentTaskMidTurnRefineTests.cs :: C1169_AgedScreenOnlyRefinementFollowsInterruptedSentRecovery(no-prompt-enter-only)`
- `Application\AgentTaskMidTurnRefineTests.cs :: C1169_AgedScreenOnlyRefinementFollowsInterruptedSentRecovery(no-prompt-retype)`
- `Application\AgentTaskReuseEnqueueTests.cs :: both_reuse_messages_enqueue_when_nothing_throws`
- `Application\AgentTaskStandingAgentDispatchTests.cs :: a_busy_standing_agent_makes_the_next_task_wait_and_take_it_after_the_first_settles`
- `Application\AgentTaskStandingAgentDispatchTests.cs :: a_marked_turn_on_the_standing_session_settles_the_pinned_task_and_leaves_the_agent_alone`
- `Application\AgentTaskStandingAgentDispatchTests.cs :: an_unmarked_turn_on_the_standing_session_is_nudged_and_still_leaves_the_agent_alone`
- `Application\CardCorrectionIntegrationTests.cs :: A_card_in_a_terminal_column_can_still_be_corrected`
- `Application\CardCorrectionIntegrationTests.cs :: A_reopened_card_recloses_with_a_fresh_completion`
- `Application\CardCorrectionIntegrationTests.cs :: A_terminal_move_records_both_the_revision_and_the_terminal_reason`
- `Application\CardCorrectionIntegrationTests.cs :: A_terminal_move_rejection_names_the_reopen_endpoint`
- `Application\CardCorrectionIntegrationTests.cs :: A_terminal_move_stores_a_reason_far_longer_than_the_old_thousand_character_column`
- `Application\CardCorrectionIntegrationTests.cs :: Reopen_defaults_to_the_backlog_column_and_never_spawns`
- `Application\CardCorrectionIntegrationTests.cs :: Reopen_deletes_no_review_checkpoint`
- `Application\CardCorrectionIntegrationTests.cs :: Reopen_of_a_live_card_or_into_a_terminal_column_or_without_a_reason_is_rejected`
- `Application\CardCorrectionIntegrationTests.cs :: Reopen_of_an_archived_card_is_refused_until_unarchive`
- `Application\CardCorrectionIntegrationTests.cs :: Reopen_when_no_live_column_exists_is_a_conflict`
- `Application\CardCorrectionIntegrationTests.cs :: Reopen_with_a_stale_token_is_a_conflict_and_writes_no_revision`
- `Application\CardCorrectionIntegrationTests.cs :: Reopen_writes_one_revision_with_the_superseded_terminal_facts_and_clears_them`
- `Application\LandRefusalDeliveryWitnessTests.cs :: C1196_Lost_wakeup_is_recovered_by_the_stranded_queue(busy)`
- `Application\PhoneHomeCompletedCardTransportTests.cs :: C1032_TransportClientUnsupported`
- `Application\RemoteControlRecoveryPtyIntegrationTests.cs :: C514_Working_modal_waits_for_operator_then_converts_without_replay`
- `Application\RunnerSeatReleaseFixture.cs :: C1065_ContinueAndPrerequisiteReplyUseGuardedPath`
- `Application\RunnerSeatReleaseFixture.cs :: C1065_FullAnswerRequiresNewSessionUserPrompt`
- `Application\RunnerSeatReleaseFixture.cs :: C1065_ParkedReviewReplyBindsFreshEvidence`
- `Application\RunnerSeatReleaseFixture.cs :: C1065_PinnedFollowupCannotStealParkedIdentity`
- `Application\RunnerSeatReleaseFixture.cs :: C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply`
- `Application\RunnerSeatReleaseFixture.cs :: C1104_ParkedReviewReplyReceiptSpillsToPointerAtProductionChunkSize`
- `Application\TranscriptHotPathMigrationTests.cs :: Down_up_roundtrip_preserves_data_and_old_query_compatibility`
- `Application\TranscriptHotPathMigrationTests.cs :: Populated_prechange_upgrade_preserves_all_rows_and_uuid_kinds`
- `Application\TranscriptHotPathMigrationTests.cs :: Retry_preserves_a_valid_index_and_replaces_two_invalid_indexes(0)`
- `Application\TranscriptHotPathMigrationTests.cs :: Retry_preserves_a_valid_index_and_replaces_two_invalid_indexes(1)`
- `Application\TranscriptHotPathMigrationTests.cs :: Retry_preserves_a_valid_index_and_replaces_two_invalid_indexes(2)`
- `Application\TranscriptHotPathMigrationTests.cs :: Retry_replaces_all_three_interrupted_invalid_indexes`
- `TestHelpers\BridgeQueueHarness.cs :: Exhausted_boot_retries_are_late_confirmed_by_the_transcript_record`
- `TestHelpers\TranscriptHotPathFixture.cs :: Prepared_plans_keep_the_partial_index_paths`
- `TestHelpers\TranscriptHotPathFixture.cs :: Repeated_hot_reads_do_not_add_transcript_sequential_scans`
- `TestHelpers\TranscriptHotPathFixture.cs :: Uuid_membership_seeks_by_session_and_uuid`

#### session-runner (in progress at 18:55)

`Antiphon.SessionRunner.Tests.exe` pid `30976` (parent `10828`) started at 18:50:00 local. Its watchdog is `1200000` ms. At 18:55:48 cpu was 35 and the working set was 200MB. `session-runner-all-tests.log.stdout.tmp` and `.stderr.tmp` were still 0 bytes, and `session-runner-all.trx` was not written yet.

## Linux lane

Not started. `server2` has git and no host `pwsh` or `dotnet`. The rc profile entry (`release-cut.ps1` / `nightly-run.ps1`) runs `npm.cmd` through `%ComSpec%`, which is the Windows profile. The production runner container was not used. A hand-rolled `dotnet` roster would not be the rc profile and would not earn rc-release credit.

## Resume

Watch pid `38396`. The current suite process at 18:55 was pid `30976` (`Antiphon.SessionRunner.Tests`). When `38396` exits, read the last JSON line of `C:\Antiphon\releases\rc-2026-10-18-nightly-nosync.log` and `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\state\last-run.json`. Suite logs and TRX stay under the run log directory above. Do not publish, tag, or start a second Windows run at this SHA until that process has exited.
