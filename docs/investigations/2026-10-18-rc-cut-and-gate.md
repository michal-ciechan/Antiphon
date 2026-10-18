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

## Windows lane (finished)

The pinned checkout `C:\Antiphon\releases\checkout` is an independent clone of `https://github.com/michal-ciechan/Antiphon` at `a1d24b6fc92d45ae5274b418bf54c6b8819a8697`. It is not the canonical checkout and not a linked worktree. `.antiphon-nightly-owned` was written so the ownership check can accept it. The run uses `-NoSync`, so it does not fetch, reset, or clean, and it does not take the self-reexec hop. HEAD was verified equal to the candidate SHA before the process started. `-ExpectedSha` is still passed through.

Launcher: `C:\Antiphon\releases\start-rc-2026-10-18.cmd` (outside the repo), started with `Win32_Process.Create` so it is not a child of this session. `PATH` puts `C:\Program Files\PowerShell\7` first.

| Field | Value |
|---|---|
| Host | Windows desktop |
| Profile / trigger | `rc` / `rc` |
| Run id | `42d2c78424dd4612af43eed3afeaecc2` |
| SHA | `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` |
| Started | `2026-10-18T16:47:34.4931667Z` |
| Completed | `2026-10-18T18:20:45.8192994Z` |
| Phase | `final` |
| pwsh pid | `38396` (exited; parent cmd `18564`; nightly-tests child `10828`) |
| Exit | `exitCode` 3, `testExit` 1, `reportExit` 3 |
| Result | `coverageComplete` false, `testsPassed` false, `reportDelivered` false, `succeeded` false, `creditKind` empty |
| State | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\state\last-run.json` |
| Console log | `C:\Antiphon\releases\rc-2026-10-18-nightly-nosync.log` |
| Run logs | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\logs\2026-10-18-1647-42d2c784` |

The run has exited. No second Windows lane was started. The last console line is the final JSON above (`creditKind` empty). `nightly-report.ps1` printed `REPORTING: could not file card` and `truncated card census on status Done cannot prove absence`. The unfiled body is `card.md` in the run log directory. `reportExit` 3 is that filing failure. `testExit` 1 is the test phase. This run does not earn rc-release credit.

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

`Antiphon.Tests.exe` pid `16296` started at 17:49:53 local. The profile watchdog for `antiphon` is `3600000` ms. The process was gone by 18:52, the diagnostic file's last record is 18:49:43 local, and `antiphon-all-tests.log` was closed at 18:50:00. `antiphon-all.trx` was not written. `summary.json` records `result` FAIL, `exitCode` 1, `timedOut` true, `coverageComplete` false, evidence `missing-trx`. The wall clock matches the watchdog.

`--no-progress` prints failures and skips only. Counts below are from the diagnostic log's last flushed state per test uid (2733 uids) plus the console failure lines. `FailedTestNodeStateProperty` occurs 0 times: the failure records were not flushed before the process ended, so the 72 console failures are not an extra 72 on top of the 2575 passes.

| Outcome | Count | Source |
|---|---|---|
| Passed | 2575 | diagnostic, last flushed state |
| Skipped | 4 | diagnostic and console, same four names |
| Failed, terminal diagnostic record | 0 | diagnostic |
| No flushed terminal state | 154 | diagnostic last state `InProgress` |
| Failed lines | 72 | `antiphon-all-tests.log` |

Skipped: `ClaudeAdapterIntegrationTests.Full_round_trip_via_seam_returns_response_text`, `ClaudeEffortPromptCanaryTests.Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer`, `CodexAdapterIntegrationTests.Full_round_trip_via_the_production_runner_adapter_returns_the_models_answer`, `PhoneHomeCompletedCardTransportTests.C1032_TransportQueueRecipientReplay`.

71 of these 72 console failures failed again at `61117dd1` (inherited). `Exhausted_boot_retries_are_late_confirmed_by_the_transcript_record` passed there (new). None of the brief's named suspects appear in the console log: `C1192_Npm_volume_sentinel`, `C1191_Fixture_failure`, `C1105_Git_audit`, `C599_CloneOwnership`, `C599_ReportLane`, `LandDelivery`.

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
- `Application\AgentSessionLaunchFailureTests.cs :: Exhausted_boot_retries_are_late_confirmed_by_the_transcript_record` (new; stack frame `AgentSessionLaunchFailureTests.cs:2144`)
- `TestHelpers\TranscriptHotPathFixture.cs :: Prepared_plans_keep_the_partial_index_paths`
- `TestHelpers\TranscriptHotPathFixture.cs :: Repeated_hot_reads_do_not_add_transcript_sequential_scans`
- `TestHelpers\TranscriptHotPathFixture.cs :: Uuid_membership_seeks_by_session_and_uuid`

#### session-runner (ended 19:10 local)

`Antiphon.SessionRunner.Tests.exe` pid `30976` started at 18:50:00 local and was gone by 19:11. The profile watchdog is `1200000` ms. The diagnostic file's last write is 19:10:00 and `session-runner-all-tests.log` closed at 19:10:02. `session-runner-all.trx` was not written. `summary.json` records `result` FAIL, `exitCode` 1, `timedOut` true, `coverageComplete` false, evidence `missing-trx`. Same shape as `antiphon`: the wall clock matches the watchdog, and `FailedTestNodeStateProperty` was not the last flushed state of any uid.

| Outcome | Count | Source |
|---|---|---|
| Passed | 1408 | diagnostic, last flushed state (1552 uids) |
| Skipped | 18 | diagnostic and console |
| Failed, terminal diagnostic record | 0 | diagnostic |
| No flushed terminal state | 126 | diagnostic last state `InProgress` |
| Failed lines | 119 | `session-runner-all-tests.log` |

117 of these 119 console failures failed again at `61117dd1` (inherited). `Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled` and `Websocket_connect_that_hangs_ends_within_the_connect_timeout` passed there (new). Named brief suspects `C1192`, `C1191`, `C1105`, `C599`, and `LandDelivery` do not appear in this log.

Exception groups: 92 `UnauthorizedAccessException`, 15 `InvalidOperationException`, 11 `ShouldAssertException`, 1 `TimeoutException`.

Skipped names: `Arm_A_unknown_resume_is_refused_before_allocation`, `Arm_B_create_is_detected_as_grok`, `Arm_C_resume_of_the_created_directory_is_detected`, `Arm_D_detect_timeout_on_an_idle_shell_keeps_the_pane_for_relaunch`, `C1032_InspectionR3Mode(add-executable)`, `C1032_InspectionR3Mode(remove-executable)`, `C1032_RemovalLinuxIgnoredFifoRefuses`, `C1032_StoreIdentityRefused(unreadable)`, `C461_G083_Foreign_after_runner_inspection`, `C461_G084_External_tree_race_at_teardown`, `C461_G085_Backend_rpc_fence`, `Isolated_backend_closes_only_reviewed_incarnation(False)`, `Isolated_backend_closes_only_reviewed_incarnation(True)`, `Isolated_backend_recovers_lost_close_result`, `Linux_probe_reads_live_proc`, `Live_herdr_creates_reuses_and_refuses_on_a_labelled_tab`, `Owned_tab_and_workspace_getters_support_rename_follow_validation`, `Restart_adopts_same_store_session_and_generation`.

Console failures, `file :: test` (one row has no `in` path in the first 20 lines of its stack):

- `(no path) :: C1065_RepositoryIdentityReadIsBoundToSessionCheckout`
- `Antiphon.SessionRunner.Tests\AgentPinPublicationTests.cs :: V13_PublicationCustody`
- `Antiphon.SessionRunner.Tests\AgentPinPublicationTests.cs :: V14_PublicationFences`
- `Antiphon.SessionRunner.Tests\AgentPinPublicationTests.cs :: V14_PublicationReplay`
- `Antiphon.SessionRunner.Tests\AgentPinPublicationTests.cs :: V14_PublicationSerialization`
- `Antiphon.SessionRunner.Tests\AgentPinPublicationTests.cs :: V15_PublicationCleanup`
- `Antiphon.SessionRunner.Tests\AgentPinPublicationTests.cs :: V34_PublicationAdmission`
- `Antiphon.SessionRunner.Tests\AgentPinWorkspaceStoreTests.cs :: V13_InspectPathSafety`
- `Antiphon.SessionRunner.Tests\AgentPinWorkspaceStoreTests.cs :: V32_InspectPosixPaths`
- `Antiphon.SessionRunner.Tests\AgentPinWorkspaceStoreTests.cs :: V34_InspectProtocolAdmission`
- `Antiphon.SessionRunner.Tests\CodexAuthProbeTests.cs :: Symlinked_auth_file_is_not_presence_but_a_linked_home_is_judged_by_its_file`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_GuardedOperationIsDistinct`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerAttemptIdentity`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerBranchIdentity`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerDigestReplay`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerDirtySource`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerFreshFinalInspection`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerHeadIdentity`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerIgnoredPolicy`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerIntentBeforeIo`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerNoFollow`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerNoForceBypass`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerPathIdentity`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerPreservationProof`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerProcessOwnerHolds`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerReadoptionFence`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerRepositoryIdentity`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerSourceLandingExcluded`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerStoreIdentity`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_RunnerTaskIdentity`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1017_UnknownProcessOwnershipHolds`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_FenceAttachOrders`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_FenceCreationOrders`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_FenceHerdrReadoptionOrders`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_FenceStartOrders`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_FenceStartupAdoptionOrders`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_FenceUnknownJournal`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_InspectionHoldShapes` (42 argument cases: admin-custom lock, admin-gc pid, admin-HEAD lock, admin-index lock, assume-clean, assume-dirty, bare, BISECT_LOG, both-flags, case-twin, CHERRY_PICK_HEAD, common-gc pid, common-index lock, common-MERGE_HEAD, common-packed-refs lock, common-refs/heads/side lock, common-shallow lock, detached, evidence, flagged-newline-path, gitlink, ignored-bare, ignored-nested, link, locked-registration, merge, nested, other-repository, protected, prunable-registration, rebase-apply, rebase-merge, REVERT_HEAD, sequencer, shallow, skip-clean, skip-dirty, sparse, staged, tracked, unborn, untracked)
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_InspectionPolicyParity`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_InspectionR3Bare` (case-head, case-objects, case-refs, directory-head, missing-head, missing-objects, missing-refs)
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_InspectionR3Filter` (changed, unchanged)
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_InspectionR3History` (custom-replace, grafts, replace)
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_InspectionReadOnlyBound`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_InspectionReadOnlyGitCalls`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_InspectionSourceProof`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_RemovalComponentRecovery`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_RemovalEligiblePhysical`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_RemovalLateRootIdentityRefuses`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_RemovalOriginalReplacement`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_TransportDefaultInert`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_TransportDispatchReplay`
- `Antiphon.SessionRunner.Tests\CompletedCardRunnerFixture.cs :: C1032_TransportRequiredPayload`
- `Antiphon.SessionRunner.Tests\GrokRulesHttpAcceptanceTests.cs :: Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled` (new)
- `Antiphon.SessionRunner.Tests\PhoneHomeConnectionServiceTests.cs :: Websocket_connect_that_hangs_ends_within_the_connect_timeout` (new)
- `Antiphon.SessionRunner.Tests\UnixPtyArgvAdmissionTests.cs :: Malformed_inputs_reach_existing_runtime_validation`
- `Antiphon.SessionRunner.Tests\UnixPtyArgvAdmissionTests.cs :: Nul_refusal_is_named_on_both_launch_routes` (ordinary, platform_constrained)
- `Antiphon.SessionRunner.Tests\UnixPtyArgvAdmissionTests.cs :: Nul_request_is_refused_before_effects` (grok_payload, grok, plain, tracked)
- `Antiphon.SessionRunner.Tests\UnixPtyArgvAdmissionTests.cs :: Null_exe_keeps_existing_validation_message_on_launch_routes` (ordinary, platform_constrained)
- `Antiphon.SessionRunner.Tests\UnixPtyArgvAdmissionTests.cs :: Phone_home_nul_refusal_is_named` (launch, platform_constrained)
- `Antiphon.SessionRunner.Tests\UnixPtyArgvAdmissionTests.cs :: Platform_and_backend_boundaries_keep_existing_verdicts` (herdr_nul, platform_invalid, platform_mismatch, unknown_backend)

The grouped rows above are the 119 console lines. `C1032_InspectionHoldShapes` is 42 cases, `C1032_InspectionR3Bare` is 7, `C1032_InspectionR3Filter` is 2, `C1032_InspectionR3History` is 3, and the `UnixPtyArgvAdmissionTests` rows expand to 15.

#### pty-host (passed)

`Antiphon.PtyHost.Tests` finished in 22s 545ms and wrote `pty-host-all.trx` (230019 bytes, 19:10:25) plus `pty-host-all.discovery.json`. The console summary and the TRX agree.

| Outcome | Count |
|---|---|
| Total | 147 |
| Passed | 116 |
| Failed | 0 |
| Skipped | 31 |

TRX outcome `NotExecuted` is 31, matching the skipped count. Log: `pty-host-all-tests.log`. `summary.json` records `result` pass, `exitCode` 0, `timedOut` false, `coverageComplete` false. `requiredUidCount` is 124 and `excludedUidCount` is 23. The coverage gap is one `unexecuted-outcome` list of Linux-only skips (Card0490 native, LinuxCgroup, LinuxPty, ShadowCopy). Those skips are not assertion reds.

#### agents-pty (failed)

`Antiphon.Agents.Pty.Tests` finished in 2m 08s 175ms. The console summary and `agents-pty-all.trx` agree. `summary.json` records `result` FAIL, `exitCode` 2, `timedOut` false, `coverageComplete` false.

| Outcome | Count |
|---|---|
| Total | 702 |
| Failed | 1 |
| Passed | 634 |
| Skipped | 67 |

The one failed test is `FakeClaudeContractTests.A_doubled_quote_argument_splits_for_a_native_parser_and_not_for_dotnet` (`tests\Antiphon.Agents.Pty.Tests\FakeClaudeContractTests.cs:1691`, 252ms on this run). It failed again at `61117dd1` (inherited).

Coverage gaps, not assertion reds: `missing-required-class PtyPasteMarkerExperiments`, and `missing-expanded-row` for `Markers_reaching_the_child_with_and_without_decset_2004`, `Markers_by_conpty_backend`, `Host_selftest`, and `Write_shape_across_window_sizes`. `requiredUidCount` is 656 and `excludedUidCount` is 105. The remaining evidence is `process-exit-2` plus an `unexecuted-outcome` list of Linux, headed, and oracle skips.

#### messaging (passed)

`Antiphon.Messaging.Tests` finished in 1m 08s 168ms. Console and `messaging-all.trx` agree: total 250, failed 0, succeeded 250, skipped 0. `summary.json` records `result` pass, `exitCode` 0, `requiredUidCount` 250, `excludedUidCount` 0, `evidenceReasons` empty.

#### client (failed)

Vitest in `client-all-tests.log`: Test Files 2 failed | 122 passed (124). Tests 3 failed | 1803 passed | 1 skipped (1807). `CLIENT TESTS EXIT CODE: 1`. `summary.json` records `result` FAIL, `exitCode` 1, `timedOut` false. The three failures:

- `src/features/orchestrator/KeepSixPanel.test.tsx` > `renders off, healthy, and under-target badges with the counts and an active hold`. `toHaveTextContent` expected `2026-10-18T12:00:00.000Z` and received `model quota (expired)` (line 118). That title is absent at `61117dd1`, so it is new. It was not executed there.
- `src/features/settings/SettingsPage.test.tsx` > `does not fetch routing queries until the Routing tab is opened`. `AssertionError: expected 15 to be 1`. The title exists at `61117dd1`. Not re-run. Unclassified.
- `src/features/settings/SettingsPage.test.tsx` > `?tab=routing selects the tab and mounts routing queries without fetching templates`. `AssertionError: expected 15 to be 1`. The title exists at `61117dd1`. Not re-run. Unclassified.

Client tests were not re-run because the `npm ci` failure is a separate diagnosis.

#### scripts (failed)

`summary.json` records `result` FAIL, `exitCode` 1, `timedOut` false. The only failing script log is `script-test-apphost-main-worktree-guard.log`: three PASS lines, then `FAIL test setup or execution - git worktree add failed with exit 128`. `CARD-0273 main-worktree guard: 3 passed, 1 failed`. The same script at `61117dd1` exited 1 with those same lines (inherited). Neither log included the git stderr. The other script logs in the run directory exited 0: apphost-graceful-stop, apphost-lock-age, apphost-probe-class, cleanup-claude-sessions, cleanup-codex-test-residue (`passed=23 failed=0`), client-mode, deploy-am-service, deploy-nightly-watchdog, hooks, nightly-report, reap-zombie-agents (`passed=40 failed=0`), stage-value-report.

#### e2e (did not run)

`summary.json` records `result` FAIL, `exitCode` 1, `skipped` false, `coverageComplete` false, `detail` `e2e-prerequisite-failed`. Preflight `playwright-install` exited 0. Preflight `client-bundle` exited 1 because `C:\Antiphon\releases\checkout\client\dist\index.html` is missing. `e2e-playwright-install.log` is 0 bytes. No e2e test ran. This follows the `npm ci` failure. It is not classified as an inherited or new product red. `npm ci` stays unclassified.

#### markdown-pdf (passed)

`Antiphon.MarkdownPdf.Tests` finished in 3s 755ms. Console and `markdown-pdf-all.trx` agree: total 20, failed 0, succeeded 19, skipped 1. The skip is `Four_documents_produce_readable_combined_pdf` (`Set ANTIPHON_HEADED_TESTS=1` for the real browser PDF gate). `summary.json` records `result` pass, `exitCode` 0, `timedOut` false, `coverageComplete` false, `requiredUidCount` 16, `excludedUidCount` 4. The skip is also an exclusion (Slow/Integration, owner CARD-1313) and is still listed as `unexecuted-outcome`, so coverage stays incomplete. No assertion red.

## Classification at rc/2026-10-15

Inherited means the same case failed again at `61117dd1663d2f47ce4708c18b609b8936491986`. New means it passed there, or the failing title is absent at that commit. Unclassified means the title exists there and this task did not re-run it.

The comparison checkout is the detached worktree `C:\Antiphon\worktrees\classify-61117dd1` at that SHA. `Antiphon.sln` was built there through `scripts/build-slot.ps1` (lease `7fd72bb4-dea2-48e7-95c4-278e56a4de0f`, 0 errors). Console failure names were re-run with `--no-progress --treenode-filter` through lease `e9b9644b-459c-4ef8-90e1-1123e17e5618` (wrapper exit 0, held 355s). `Antiphon.Tests` and `Antiphon.SessionRunner.Tests` ran one after the other. Logs: `C:\Users\lndco\AppData\Local\Temp\classify-61117dd1\`.

| Suite | Gate reds | Inherited | New | Unclassified |
|---|---|---|---|---|
| antiphon | 72 console lines | 71 | 1 | 0 |
| session-runner | 119 console lines | 117 | 2 | 0 |
| agents-pty | 1 | 1 | 0 | 0 |
| scripts | 1 | 1 | 0 | 0 |
| client | 3 | 0 | 1 | 2 |
| npm ci | 1 build | 0 | 0 | 1 |
| e2e | prerequisite only | 0 | 0 | not a product red |

Antiphon filter: 62 method stems, no prefix collision. TUnit at `61117dd1`: total 77, failed 71, succeeded 6, skipped 0, exit 2. Every gate console failure name except `Exhausted_boot_retries_are_late_confirmed_by_the_transcript_record` is in those 71 failed lines. The filter also executed five passing nodes that are not gate console failures. A direct rerun of `/*/*/AgentSessionLaunchFailureTests/Exhausted_boot_retries_are_late_confirmed_by_the_transcript_record` (lease `7b645cbe-bb93-48a0-9893-1ab9e917e7f3`) passed: total 1, failed 0, succeeded 1, duration 32s 707ms, exit 0. That red is new. The method is present at `61117dd1` in `tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs`.

Session-runner filter: 60 method stems. TUnit at `61117dd1`: total 119, failed 117, succeeded 2, skipped 0, exit 2. The 117 failed names are the gate's 119 minus the two new names above. Lock-name cases use U+00B7 in the base log and U+FFFD in the gate console; after normalizing that character, no other name differs.

Agents-pty filter: the doubled-quote test, total 1, failed 1, 374ms, exit 2. Inherited.

`scripts/test-apphost-main-worktree-guard.ps1` at `61117dd1`: exit 1, same `git worktree add` exit 128. Inherited.

The KeepSixPanel title above is absent under `client/` at `61117dd1` (`git grep` found no hit). The two SettingsPage titles are present and were not executed. `npm ci` was not compared with `61117dd1`.

pty-host, messaging, and markdown-pdf have zero Failed outcomes. Their skipped and missing rows are coverage gaps (`coverageComplete` false on pty-host and markdown-pdf), not inherited or new reds.

## Linux lane

Not started by this task. A separate commission owns that lane. `server2` was not used. The production runner container was not used.

## Closed

The Windows rc profile at `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` has finished. State file: `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\state\last-run.json`. No tag was created. `publish-release.ps1` and `promote-release.ps1` were not run. Gate scripts were not edited. No product red was fixed. The canonical checkout and the production runner were not touched. Do not start another Windows run for this candidate from this task.
