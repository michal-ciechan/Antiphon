# CARD-1304 Code evidence

CARD-1304 finishes the remaining CARD-1259 verification depth. Production behaviour is unchanged. There is no production fix SHA. The decoupling itself stays as reviewed on the base.

Task: 5a70a9cf-1c71-4b6f-9c23-6c34f1454634. Role: Code. Round: Final.
Branch: feat/card-task-5a70a9cf.
Worktree: /work/worktrees/task-5a70a9cf.
Base (red SHA for behaviour already present): 188ac942166884f060fdcc7cbf3b15f670a27b43.
Plan: docs/superpowers/plans/2026-10-15-card-1259-transcript-pump-verification.md.
Prior finish note: docs/superpowers/plans/2026-10-15-card-1259-finish-evidence.md.
Checkpoint receipt: .antiphon/checkpoints/20261111-031649-942a/report.md.

## Commits

| SHA | Subject |
|---|---|
| 2d77c104da3c85c5d42f7f4c2321b249e46fc56a | deepen the remaining CARD-1259 V subcases |
| 17c48c0e3eeb859c80af0e756bed655fdbf22cff | qualify the cancelled-turn stop reason |
| ace634f90a06506017c9811f41dd098e7c6e1abf | count the synthetic-race catch-up separately |
| 1b296225c8a12365140fa35734cbccc7bba352af | couple the clock and housekeeping witnesses to their gates |
| 1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 | keep the working-state clock hold |
| f5ae5ec47ecfb3e6e8394bca8ec966d4d0e5c630 | refresh the derived-impact index |

Paths versus the base: SessionRuntimeActionRoutingTests.Finish.cs, SessionRuntimeActionRoutingTests.cs, SessionRuntimeActionTests.cs, tests/derived-impact-index.json. No production file changed. tests/test-execution-policy.json did not change, so the nightly policyHash and PinnedInputDigest were not resealed.

## Final selection CP-3..CP-13

One checkpoint-tool run, not wrapped in build-slot. The tool takes its own slot. Command used `--rows CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-13 --serial --expected-source-sha 1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 --keep-outputs` and `--no-build` after the tool bootstrap. Selected estimated minutes sum to 45, so the omitted total timeout derived to 100 minutes. Source was clean. Verdict GREEN exit 0. 11 green, 0 red, 0 skipped. Wall 75m24s, sequential-equivalent 57m00s. Budget wall=4524.728s execution=1869.404s admissionOnly=2655.324s. unlisted: none. Tool bootstrap lease 60c49b99-8efc-4b68-a55c-3f2dc006a3a8 waited=0s held=8s exit 0. CP-1 and CP-2 stay historical assertion-red baselines and were not rewritten. CP-24..CP-27 are the 65a75ada supplement; the card names CP-3..CP-13, and those four rows were not run.

CP-4 executed 9, plan Min 7. The two method prefixes have more arguments than the plan prose counted (S0_blocked_hosted_pump_probe four, C1259_confirming_prompt_and_other_session_progress_before_confirmation five). MinExecuted=7 is met and failed=0. The plan table was not edited.

Unedited receipt lines:

CHECKPOINT CP-3 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=ok filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/(C1259_buffered_turns_route_their_own_recipients_before_flush*)|(C1259_new_working_or_blocked_state_defers_old_idle_action*)|(C1259_compaction_refinement_and_status_cannot_block_ingestion*)|(C1259_review_recovery_uses_authorized_queue_identity_once*)|(C1259_missing_or_truncated_prompt_never_confirms*) executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=ok filter=/*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/(S0_blocked_hosted_pump_probe*)|(C1259_confirming_prompt_and_other_session_progress_before_confirmation*) executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-4/run.trx slot=granted waited=165s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=reused filter=/*/Antiphon.Tests.Application/SessionRuntimeActionTests/(C1259_commit_and_effect_admission_are_atomic*)|(C1259_stream_pull_and_replay_preserve_order*)|(C1259_saturation_keeps_ingestion_live_and_recovers_overflow*)|(C1259_shutdown_joins_and_restart_reconciles_submission*)|(C1259_retry_and_retention_preserve_pending_turn*) executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-5/run.trx slot=granted waited=780s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=reused filter=/*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/(C1259_buffered_turns_route_their_own_recipients_before_flush*)|(C1259_new_working_or_blocked_state_defers_old_idle_action*)|(C1259_compaction_refinement_and_status_cannot_block_ingestion*)|(C1259_review_recovery_uses_authorized_queue_identity_once*)|(C1259_missing_or_truncated_prompt_never_confirms*) executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-6/run.trx slot=granted waited=495s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=reused filter=/*/Antiphon.Tests.Application/AgentSessionRuntimeTests/(C698_same_uuid_different_kinds_survive_replay_across_sessions_and_generations*)|(C698_1025_uuid_keys_are_bounded_and_replays_preserve_every_pair_and_sequence*)|(Error_turn_end_is_acted_on_once_replay_dedups*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-7/run.trx slot=granted waited=600s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=reused filter=/*/Antiphon.Tests.Application/SessionMessageQueueDeliveryVerificationTests/(C514_Multiline_release_keeps_LF_paste_and_separate_Enter*)|(Manual_boundary_flushes_a_message_stranded_before_the_compaction*)|(Manual_boundary_with_an_empty_queue_publishes_no_finished_event*)|(A_clipped_prefix_parks_as_truncated_not_sent*)|(Late_confirm_does_not_promote_a_truncated_body_to_sent*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-8/run.trx slot=granted waited=180s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=reused filter=/*/Antiphon.Tests.Application/ChannelReplyLateConfirmDispatchTests/(Grace_dispatch_keeps_its_confirmed_turn_when_a_newer_turn_completes*)|(Grace_and_runtime_redispatch_share_one_atomic_publication_owner*)|(Caller_cancellation_does_not_release_a_still_publishing_direct_reply*) executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-9/run.trx slot=granted waited=105s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=reused filter=/*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-10/run.trx slot=granted waited=150s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=reused filter=/*/Antiphon.Tests.Application/(SessionStateCommitTests*)|(ReviewReplyDispatcherTests*)|(HerdrStatusDisagreementTests*)/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-11/run.trx slot=granted waited=165s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=reused filter=/*/Antiphon.Tests.Application/(SessionRunnerHeldEventDeliveryTests*)|(AgentTaskMidTurnRefineTests*)/(C1205_Blocked_held_callback_stops_on_host_cancellation*)|(C1205_Blocked_held_callback_stops_at_monotonic_deadline*)|(C1205_Last_transcript_pull_cancellation_preserves_one_output*)|(C1170_LiveUserPromptConfirmsSentRefinementWithoutASyncPump*)|(C1188_UserPromptDoesNotWaitOnTheQueueLockWithoutASentRefinement*)|(C1188_SentRefinementStillWaitsOnTheQueueLockBeforeConfirming*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-12/run.trx slot=granted waited=15s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 build=reused filter=/*/Antiphon.Tests.Application/AgentSessionRuntimeTests/(C561_a_NUL_in_tool_result_text_persists_as_U_FFFD*)|(C1259_deferred_UI_payload_preserves_original_fields_after_restart*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-5a70a9cf/.antiphon/checkpoints/20261111-031649-942a/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 sourceState=clean buildSource=verified

Executed sum of those rows: 35+9+28+35+3+5+8+1+29+7+3 = 163. Failed 0. Skipped 0. Tested SHA for this selection: 1fbdb8c3f4a7d7a1565e7de826b8b5454db53788.

## Three historical failures in the 624-case selection

At 254436119465487996613a892c1b5ff49cbab67d the earlier selection was 621 passed and 3 failed. Those three were re-executed at 1fbdb8c3f4a7d7a1565e7de826b8b5454db53788 and passed. This task did not edit SessionRunnerEventPumpTests.cs or AgentTaskMidTurnRefineTests.cs. The repairs are already in the base. Attribute: pre-existing, not introduced and not fixed by CARD-1304.

- CP-10 C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition: 1 passed, 0 failed. The fixture clears bus.Events before the empty assertion.
- CP-12 C1170_LiveUserPromptConfirmsSentRefinementWithoutASyncPump and C1188_SentRefinementStillWaitsOnTheQueueLockBeforeConfirming: both passed inside the 7/7 row. PrepareSentRefinementAsync drains the page so the seeded row is Sent.

## Witnesses and local mutants

Internal subcases stay inside the owning V methods. No new TUnit method was added. Local mutants were applied, proven, and restored with git checkout of the exact production path. They are not discharged positive controls. PC-1..PC-113 stay pending for Mutation.

- Catch-up creation. Mutant: AgentSessionRuntime.SyncTranscriptAsync wrapped `await Actions().RequestAsync(sessionId, "catchup", ct)` in `if (false)`. Red: queue-lock catch-up count 0 versus 1 (SessionRuntimeActionTests.cs:206, 7s 954ms) and synthetic-race catch-up count 0 versus 1 (line 238, 1s 990ms). Restored production is green inside CP-5 28/28. The removal clause in the coordinator was not mutated; a non-removed catch-up can be re-selected until the 70s wait.
- Housekeeping. The task-bound witness puts the blocked report in both the task-notification user prompt and the assistant text. Mutant: TranscriptKinds.IsTaskNotificationPrompt forced false. Red: task status Blocked versus Working (SessionRuntimeActionRoutingTests.Finish.cs:396, 2s 108ms). Restored production keeps Working, and that argument is inside CP-6 35/35. Mutating only IsHousekeepingPrompt did not fail this witness, because selection uses IsTaskNotificationPrompt first.
- Clock. Shared ScaledTimeProvider(speed 1) is the harness TimeProvider. Advance replaces the three former 1100ms delays. The working variant sets DueAt an hour ahead and drains while the session is still Working; inputs stay empty. A TurnEnd supersedes a status action, so that drain is not held by the DueAt comparison. Disabling the DueAt gate left the shipped assertion green. A stronger shape that ended the turn before the drain also failed on restored production (two inputs: paste body and CR) and was reverted in 1fbdb8c3. That is not a product defect. A wall-time-only fixture is not a completed deterministic-clock qualification.
- Synthetic race keeps sequences [1,2,3], one SessionRestartBoundary, one uuid "live", Peak==1, non-catch-up actions exactly 3, catch-up exactly 1. Green 5/5 at ace634f90 and inside CP-5.
- Overflow skips the one-second Advance. production-capacity still requires entered.Count == 4. Both are inside CP-5 28/28.
- Empty-sync creates a Running parent session and asserts one SessionQueuedMessage for that task. It passed in the 62/63 class run at 17c48c0e and the later routing rows. Parent H0-H4 through ReconcileAsync was not added.
- V-11 forged-target logs both the malicious model tag and the mismatched queue key in sequence. Legacy-upgrade cold DI replaces the common DispatchAsync for that variant only. Both are inside the green routing rows.
- V-5 channel-bound mixed API-error, cancelled stop reason `TranscriptKinds.StopReasons.Cancelled`, API-error stub, and answered-Blocked replay watermark are inside the green task-bound method. They were not given a separate mutant, because a housekeeping failure stops that method before them.

## Derived-impact index

scripts/regenerate-derived-impact-index.ps1 is absent on this base. No new test class was added. Edge membership changed, so the committed index was replaced with DerivedImpactIndexBuilder output (Write stores those Utf8 bytes).

- server/Application/Core/DelegationReportFormatter.cs: kept set adds SessionRuntimeActionRoutingTests and drops AgentTaskSettlementRaceTests. Owed-first now starts with AgentTaskSettlementRaceTests and drops AgentTaskReuseEnqueueTests.
- tests/Antiphon.Tests/TestHelpers/ScaledTimeProvider.cs: adds SessionRuntimeActionRoutingTests (8 classes, under the cap).
- server/Domain/Entities/AgentSession.cs: kept twenty-five unchanged. Owed-first inserts SessionRuntimeActionRoutingTests and drops CapacityRecoveryCompatibilityTests. The routing class was already in the remainder; more AgentSession references moved it into the first eight owed names.

Guard build lease 96877d66-6653-4bc3-9a6d-9b57725366f1 waited=0s held=224s exit 0, 0 errors. Guard run lease af935de8-aa7d-4124-aaf9-6b001834436a waited=75s held=711s exit 2: total 106, failed 1, succeeded 105. The only failure was DerivedImpactIndexTests.committed_index_matches_the_builder. C1584_E2EExpandedBudget passed in that run (5s 960ms), as did the other C1514 methods, MechanicalPcContractTests, TaskPlatformGuidanceTests, C1729LayeringGuardTests, and the policy, slow-registry, tripwire, and native-linux witnesses. Those 105 were not rerun.

After f5ae5ec47, rebuild lease 58808712-7e14-4722-9c0a-4d7f192f72f7 waited=0s held=302s exit 0, 0 errors, 2049 warnings. Index witness lease 17bf8f11-40ac-4661-8133-9a95bebf0df2 waited=0s held=53s exit 0: total 1, failed 0, succeeded 1, duration 48s 445ms. Filter `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder`. Tested SHA for that witness: f5ae5ec47ecfb3e6e8394bca8ec966d4d0e5c630.

## Owed

Linux-testable work that was diagnosed and not implemented:

- V-9 uncooperative-effect scope disposal, stop/kill, park/seat-release and owner counts, plus separate queue, review, and channel effect-before-ack and reconnect cold DI.
- V-10 four worker slots filled with failures, and actual per-phase downstream effect failures beyond the existing acknowledgement-save phases. Four actual queue-confirmation deliveries.
- A non-superseded action whose only block is DueAt, so the retry gate itself fails closed under a mutant.
- V-12 isolated stale queue-attempt-generation proof. Late confirm must keep accepting an ordinary Claude message whose LastDeliveryGeneration differs from the current session; the existing old-generation test depends on that.
- Parent obligation scanner H0-H4 through AgentTaskLandNotificationService.ReconcileAsync.
- Legacy identity-only diagnostic. No producer exists.
- Live desktop, browser, and broker clock acceptance.

GET http://127.0.0.1:17202/api/runner-defaults and /api/session-runners were refused (connection refused). The checkpoint run was local and did not pin a runner. Restart after land: server. This task did not restart or deploy.

PC-1 through PC-113 remain pending for method-scoped SourceLanding Mutation.
