# CARD-1422 rc port: channel reply isolation on rc/2026-10-20

Base: rc tip `9662288b252c097bfb3470cba24881cacf0d613e`. Master fix: Final Review 6e5b8a50 clean,
branch `feat/card-task-abec34f2`, commits `8b2213098` (tests) and `f14acbc06` (fix), plan
`docs/superpowers/plans/2026-10-21-card-1422-channel-reply-isolation-plan.md` on master
(`6830d0501`). Decisions, V-1..V-4 and PC-1..PC-4 are the master plan's, unchanged.

Slices: S1 tests (cherry-pick of `8b2213098`, clean), S2 fix and invariant doc (cherry-pick of
`f14acbc06` with conflicts resolved by hand).

## Port differences

- rc's `SessionRuntimeActionCoordinator.ExecuteAsync` returns `Task`, not master's
  `ExecutionDisposition`; the debt branch and the queue re-query use a plain `return`. The
  drain loop continues after either, as master's `Continue` does.
- `AgentSessionRuntime`: only `RoutesReplies`, `ExecuteActionChannelReplyAsync` and
  `ExecuteChannelReplyAsync` are added. The conflict hunk's re-arm members
  (`RearmInterruptedAttemptAsync` and its helpers, CARD-1308/1382/1395) are not brought in.
- `SessionMessageQueueService` and `docs/session-runtime-invariants.md` applied unchanged.

## Verification

Red witness (unlisted, one run): V-1 `catch_up_settles_even_when_channel_reply_dispatch_throws`
at the rc tip, 1 failed (`settled.Status` should be `Succeeded`).

Minimal profile (caller): no whole Unit lane, no channel classes (master CP-3/CP-5 not run).
rc has no `SessionRuntimeActionTemporaryBlockTests`. CP-4 leaves out
`AgentSessionRuntimeTests.Backend_runtime_can_send_input_to_live_runner_session_after_restart`,
which skips off Windows (CARD-1187) and would stop the receipt validating. Min floors are the
argument-expanded counts.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1422rc/` | catch-up | `/*/*/AgentTaskCatchUpSettlementTests/*` | V-1, V-3 | 6 passed, 0 failed/skipped | 6 | 3 |
| CP-2 | S2 | CP-1 | c1422 | `/*/*/SessionRuntimeActionRecoveryTests/C1422_failed_channel_reply_is_retried_in_order_after_the_dispatcher_recovers` | V-2 | 1 passed, 0 failed/skipped | 1 | 2 |
| CP-3 | S2 | CP-1 | coordinator | `/*/Antiphon.Tests.Application/(SessionRuntimeActionRecoveryTests*)\|(SessionRuntimeActionTests*)\|(SessionRuntimeActionRoutingTests*)\|(SessionRuntimeActionDeferralTests*)/*` | V-2, V-4 | 83 passed, 0 failed/skipped | 83 | 12 |
| CP-4 | S2 | CP-1 | runtime | `/*/*/AgentSessionRuntimeTests/(SignalR_AgentTextDelta_routes_to_session_group_only_and_chunks_output*)\|(Buffer_snapshot_reads_from_session_runner_after_backend_runtime_restart*)\|(Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart*)\|(a_clean_process_exit_records_ProcessExit_when_no_prior_source*)\|(an_exit_event_does_not_overwrite_an_OperatorRequest_source*)\|(an_exit_event_backfills_ProcessExit_onto_an_already_closed_row_with_no_source*)\|(a_cpu_spin_watchdog_exit_records_SystemRequest*)\|(HerdrPaneClosed_exit_is_Failed_not_a_clean_stop*)\|(HerdrPaneLeftOpen_exit_is_Failed_and_records_the_warning_incident*)\|(HerdrLaunchDetectTimeout_exit_is_Failed_and_does_not_record_HerdrPaneLeftOpen*)\|(Terminal_exit_stamps_the_typed_evidence*)\|(A_pane_closed_exit_after_DetectTimeout_evidence_keeps_DetectTimeout*)\|(An_operator_stopped_row_gets_no_evidence*)\|(An_exit_event_with_a_stale_generation_is_a_no_op_disposition*)\|(An_exit_event_newer_than_the_row_is_Unknown_and_changes_nothing*)\|(A_legacy_exit_event_without_a_generation_is_declined_and_reported_once*)\|(A_direct_overload_without_generation_cannot_close_a_reused_session*)\|(A_stale_exit_never_backfills_an_already_closed_row*)\|(A_matching_exit_still_backfills_an_already_closed_row*)\|(A_persistence_failure_during_exit_close_publishes_nothing_and_a_matching_snapshot_recovers*)\|(Error_stop_reason_is_an_idle_boundary_stop_sequence_is_not*)\|(Error_turn_end_is_acted_on_once_replay_dedups*)` | V-4 | 28 passed, 0 failed/skipped | 28 | 6 |
