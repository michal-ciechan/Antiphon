# CARD-1205 S5 partial unbound Mutation candidate

Owner: c0e86d50-00e6-4942-b303-7bc44122c640.
Source branch: feat/card-task-c0e86d50. Ordinary tested implementation:
4139dec15f3421f29923f5a7790e22b1dbd0a49e.
This is **unbound, incomplete and not commissioning-ready**. S5 has only T37
(two cache variants); T38..T46 and full CP-5 remain Code work. No SourceLanding
operation, payload digest or verified deliberate red is claimed. Mutation owns
all intentional changes/red/restore/green and missing-control discovery after
ordinary Code, Review and original-owner landing.

| PC | Proposed defect / variants | Method in TurnEndGapRestartDeliveryTests | Status |
|---|---|---|---|
| PC-84 | Suppress Sync recovered-boundary queue wakeup; cache enabled/disabled | C1205_Restart_delivers_five_gap_held_completions_once | Pending; method exists, defect red not executed |
| PC-85 | Suppress watchdog due admission with SSE unchanged | C1205_Watchdog_delivers_while_event_stream_remains_healthy | Pending; test missing |
| PC-86 | Skip boot sweep ScheduleFlush for destination | C1205_Restart_after_transcript_commit_recovers_lost_wakeup | Pending; test missing |
| PC-87 | Discard matched notification receipt | C1205_Receipt_restart_confirms_without_retyping | Pending; test missing |
| PC-88 | Skip late receipt check/requeue completed attempt | C1205_Receipt_restart_confirms_without_retyping | Pending; test missing |
| PC-89 | Bypass fresh Working gate | C1205_Busy_tail_keeps_completion_obligations_pending | Pending; test missing |
| PC-90 | Drop producer wakeup after keyed admission | C1205_Already_eligible_caller_receives_completion | Pending; test missing |
| PC-91 | Omit settlement completion obligation; settled-committed busy/eligible | C1205_Restart_recovers_each_delivery_handoff | Pending; test missing |
| PC-92 | Exclude RetryPending scanner; note-insert busy/eligible | C1205_Restart_recovers_each_delivery_handoff | Pending; test missing |
| PC-93 | Do not recover/save existing QueueMessageId; note-committed busy/eligible | C1205_Restart_recovers_each_delivery_handoff | Pending; test missing |
| PC-94 | Remove full-channel RequestSweep; same-epoch control and restart arms, busy/eligible | C1205_Restart_recovers_each_delivery_handoff | Pending; test missing |
| PC-95 | Exclude interrupted Sent retry; render-committed busy/eligible | C1205_Restart_recovers_each_delivery_handoff | Pending; test missing |
| PC-96 | Treat Unknown as RecoveredIdle; wrong-generation/pull-fault | C1205_Unknown_probe_keeps_obligations_held | Pending; test missing |
| PC-97 | Match header/prefix; header-only/truncated | C1205_Only_complete_destination_receipts_confirm | Pending; test missing |
| PC-98 | Remove destination prompt filter; wrong destination | C1205_Only_complete_destination_receipts_confirm | Pending; test missing |
| PC-99 | Remove attempt floor; below-floor prompt | C1205_Only_complete_destination_receipts_confirm | Pending; test missing |
| PC-100 | Bypass fresh Working with earlier idle recovery verdict | C1205_Flush_rechecks_a_new_turn_after_recovery | Pending; test missing |

Future PC-84's precise method selection:
`/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_delivers_five_gap_held_completions_once`.
Its required red is missing five complete receipts/confirmations, not a hang,
fixture error or build error. The real boot recovery is drained while Working;
the 15-minute fallback is never advanced to mask that control. Binding and
independent mutant execution remain Mutation work. All inherited S1..S4 controls
also remain pending; this document discharges none.
