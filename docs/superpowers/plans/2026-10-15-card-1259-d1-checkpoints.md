# CARD-1259 D-1 repair checkpoints

Closed selection from repair brief and Review fe77f665. Whole Unit excluded (CARD-1287). Original CP mapping is retained in Covers. CP-28 is intentional red before the fix. Final runs CP-29 and CP-1..14 at one clean SHA. Existing count 589 plus eight new cases = 597. Use --keep-outputs because S0 writes TestOutput; retain receipts and remove owned compiler roots after executor death. Estimated Final cost 31 minutes; no whole assembly.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-28 | red | tests/Antiphon.Tests -> bin-d1-red/ | deferral-red | /*/Antiphon.Tests.Application/SessionRuntimeActionDeferralTests/* | D-1 | eight expected assertion failures | 8 | 5 | true |
| CP-29 | tip | tests/Antiphon.Tests -> bin-d1-green/ | deferral-green | /*/Antiphon.Tests.Application/SessionRuntimeActionDeferralTests/* | D-1 | all 8 pass | 8 | 5 | false |
| CP-1 | tip | CP-29 | ingress | /*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/(S0_blocked_hosted_pump_probe*)\|(C1259_confirming_prompt_and_other_session_progress_before_confirmation*) | CP-1/CP-4 V-1,V-2 | all 7 pass | 7 | 3 | false |
| CP-2 | tip | CP-1 | action-class | /*/Antiphon.Tests.Application/SessionRuntimeActionTests/* | CP-2/CP-5/CP-16 V-3,V-4,V-8,V-9,V-10 | all 28 pass | 28 | 4 | false |
| CP-3 | tip | CP-1 | routing-class | /*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/* | CP-3/CP-6/CP-18 V-5,V-6,V-7,V-11,V-12 | all 35 pass | 35 | 6 | false |
| CP-4 | tip | CP-1 | transcript-regression | /*/Antiphon.Tests.Application/AgentSessionRuntimeTests/(C698_same_uuid_different_kinds_survive_replay_across_sessions_and_generations*)\|(C698_1025_uuid_keys_are_bounded_and_replays_preserve_every_pair_and_sequence*)\|(Error_turn_end_is_acted_on_once_replay_dedups*) | CP-7 R-1 | all 3 pass | 3 | 2 | false |
| CP-5 | tip | CP-1 | delivery-regression | /*/Antiphon.Tests.Application/SessionMessageQueueDeliveryVerificationTests/(C514_Multiline_release_keeps_LF_paste_and_separate_Enter*)\|(Manual_boundary_flushes_a_message_stranded_before_the_compaction*)\|(Manual_boundary_with_an_empty_queue_publishes_no_finished_event*)\|(A_clipped_prefix_parks_as_truncated_not_sent*)\|(Late_confirm_does_not_promote_a_truncated_body_to_sent*) | CP-8 R-2 | all 5 pass | 5 | 2 | false |
| CP-6 | tip | CP-1 | late-confirm-regression | /*/Antiphon.Tests.Application/ChannelReplyLateConfirmDispatchTests/(Grace_dispatch_keeps_its_confirmed_turn_when_a_newer_turn_completes*)\|(Grace_and_runtime_redispatch_share_one_atomic_publication_owner*)\|(Caller_cancellation_does_not_release_a_still_publishing_direct_reply*) | CP-9 R-3 | all 8 pass | 8 | 2 | false |
| CP-7 | tip | CP-1 | stale-exit-regression | /*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition* | CP-10 R-4 fixture repair | all 1 pass | 1 | 2 | false |
| CP-8 | tip | CP-1 | existing-guard-classes | /*/Antiphon.Tests.Application/(SessionStateCommitTests*)\|(ReviewReplyDispatcherTests*)\|(HerdrStatusDisagreementTests*)/* | CP-11 R-5 | all 29 pass | 29 | 2 | false |
| CP-9 | tip | CP-1 | held-and-refinement-guards | /*/Antiphon.Tests.Application/(SessionRunnerHeldEventDeliveryTests*)\|(AgentTaskMidTurnRefineTests*)/(C1205_Blocked_held_callback_stops_on_host_cancellation*)\|(C1205_Blocked_held_callback_stops_at_monotonic_deadline*)\|(C1205_Last_transcript_pull_cancellation_preserves_one_output*)\|(C1170_LiveUserPromptConfirmsSentRefinementWithoutASyncPump*)\|(C1188_UserPromptDoesNotWaitOnTheQueueLockWithoutASentRefinement*)\|(C1188_SentRefinementStillWaitsOnTheQueueLockBeforeConfirming*) | CP-12 R-5 fixture repair | all 7 pass | 7 | 2 | false |
| CP-10 | tip | CP-1 | original-ui-payload-guard | /*/Antiphon.Tests.Application/AgentSessionRuntimeTests/(C561_a_NUL_in_tool_result_text_persists_as_U_FFFD*)\|(C1259_deferred_UI_payload_preserves_original_fields_after_restart*) | CP-13 R-6 D-8 | all 3 pass | 3 | 2 | false |
| CP-11 | tip | CP-1 | recovery-class | /*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/* | CP-15/CP-22/CP-24 | all 11 pass | 11 | 3 | false |
| CP-12 | tip | CP-1 | channel-classes | /*/Antiphon.Tests.Application/(ChannelReplyLateConfirmDispatchTests*)\|(ChannelReplyDurabilityTests*)/* | CP-16/CP-23/CP-25 | all 45 pass | 45 | 3 | false |
| CP-13 | tip | CP-1 | retention-class | /*/Antiphon.Tests.Application/DataRetentionServiceTests/* | CP-16/CP-26 | all 73 pass | 73 | 3 | false |
| CP-14 | tip | CP-1 | task-class | /*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/* | CP-19/CP-27 | all 334 pass | 334 | 6 | true |


The ordinary checkpoint scheduler may run two rows. Each test process owns its PostgreSQL container (TestDbOperations.CreateAsync); the first four rows additionally use per-test clones. CP-14 remains serial because it owns process-spawn and global-hold fixtures. No native Pty test assembly is co-scheduled.
