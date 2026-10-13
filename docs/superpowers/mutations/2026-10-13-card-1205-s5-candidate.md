# CARD-1205 S5 unbound Mutation candidate

Owner: c0e86d50-00e6-4942-b303-7bc44122c640.
Source branch: feat/card-task-c0e86d50. Ordinary tested implementation:
52e9463282afe6c1c782a061753a1b044800c60e.
This is **unbound and requires Review/landing before SourceLanding binding**.
T37..T46 and CP-5=30/CP-6=4 are ordinary green. No SourceLanding
operation, payload digest or verified deliberate red is claimed. Mutation owns
all intentional changes/red/restore/green and missing-control discovery after
ordinary Code, Review and original-owner landing.

| PC | Proposed defect / variants | Method in TurnEndGapRestartDeliveryTests | Status |
|---|---|---|---|
| PC-84 | Suppress Sync recovered-boundary queue wakeup; cache enabled/disabled | C1205_Restart_delivers_five_gap_held_completions_once | Pending; method exists, defect red not executed |
| PC-85 | Suppress watchdog due admission with SSE unchanged | C1205_Watchdog_delivers_while_event_stream_remains_healthy | Pending; method exists, defect red not executed |
| PC-86 | Skip boot sweep ScheduleFlush for destination | C1205_Restart_after_transcript_commit_recovers_lost_wakeup | Pending; method exists, defect red not executed |
| PC-87 | Discard matched notification receipt | C1205_Receipt_restart_confirms_without_retyping | Pending; method exists, defect red not executed |
| PC-88 | Skip late receipt check/requeue completed attempt | C1205_Receipt_restart_confirms_without_retyping | Pending; method exists, defect red not executed |
| PC-89 | Bypass fresh Working gate | C1205_Busy_tail_keeps_completion_obligations_pending | Pending; method exists, defect red not executed |
| PC-90 | Drop producer wakeup after keyed admission | C1205_Already_eligible_caller_receives_completion | Pending; method exists, defect red not executed |
| PC-91 | Omit settlement completion obligation; settled-committed busy/eligible | C1205_Restart_recovers_each_delivery_handoff | Pending; method exists, defect red not executed |
| PC-92 | Exclude RetryPending scanner; note-insert busy/eligible | C1205_Restart_recovers_each_delivery_handoff | Pending; method exists, defect red not executed |
| PC-93 | Do not recover/save existing QueueMessageId; note-committed busy/eligible | C1205_Restart_recovers_each_delivery_handoff | Pending; method exists, defect red not executed |
| PC-94 | Remove full-channel RequestSweep; same-epoch control and restart arms, busy/eligible | C1205_Restart_recovers_each_delivery_handoff | Pending; method exists, defect red not executed |
| PC-95 | Exclude interrupted Sent retry; render-committed busy/eligible | C1205_Restart_recovers_each_delivery_handoff | Pending; method exists, defect red not executed |
| PC-96 | Treat Unknown as RecoveredIdle; wrong-generation/pull-fault | C1205_Unknown_probe_keeps_obligations_held | Pending; method exists, defect red not executed |
| PC-97 | Match header/prefix; header-only/truncated | C1205_Only_complete_destination_receipts_confirm | Pending; method exists, defect red not executed |
| PC-98 | Remove destination prompt filter; wrong destination | C1205_Only_complete_destination_receipts_confirm | Pending; method exists, defect red not executed |
| PC-99 | Remove attempt floor; below-floor prompt | C1205_Only_complete_destination_receipts_confirm | Pending; method exists, defect red not executed |
| PC-100 | Bypass fresh Working with earlier idle recovery verdict | C1205_Flush_rechecks_a_new_turn_after_recovery | Pending; method exists, defect red not executed |

Future PC-84's precise method selection:
`/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_delivers_five_gap_held_completions_once`.
Its required red is missing five complete receipts/confirmations, not a hang,
fixture error or build error. The real boot recovery is drained while Working;
the 15-minute fallback is never advanced to mask that control. Binding and
independent mutant execution remain Mutation work. All inherited S1..S4 controls
also remain pending; this document discharges none.

## Caller-requested family failure controls

The continuation requests one throw-away scratch-copy mutation per distinct
witness family, separate from the systematic variants. They were not executed
by Code: the binding stage contract assigns all deliberate mutations and
red/restore/green to Mutation. The caller may commission a Mutation task with
these exact method-scoped controls; zero tests, fixture/build errors and hangs
are never acceptable red. All rows below are **pending/unbound**, not proved.
The later refinement reaffirms these controls as mandatory and says Mutation is
paused. Code has returned this instruction conflict for caller triage; the active
developer stage contract must change before Code can execute deliberate scratch
mutants. Ordinary green does not prove these witnesses can detect their defects.
The caller subsequently assigned the scratch-family proofs to a separate probe
task and requested Code closure. That resolves this handoff hold; every family
row below remains pending until the probe supplies its actual failing output.

| Family | One proposed decisive control | Exact method-scoped selection | Expected assertion red |
|---|---|---|---|
| FC-1 restart/five-note receipt | PC-84 suppress Sync recovered-boundary wakeup | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_delivers_five_gap_held_completions_once` | Five actual submissions/complete confirmations missing |
| FC-2 healthy-stream independent recovery | PC-85 suppress due admission | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Watchdog_delivers_while_event_stream_remains_healthy` | RecoveredIdle/wakeup/complete receipts missing after due pass |
| FC-3 duplicate suppression | PC-88 ignore completed receipt and requeue/retype | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Receipt_restart_confirms_without_retyping` | Submission count changes or confirming sequence differs |
| FC-4 handoff persistence/rejoin | PC-93 skip existing-key QueueMessageId assignment/save | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Restart_recovers_each_delivery_handoff` | Committed original queue identity fails after note-committed cut |
| FC-5 held wakeup/final idle gate | PC-100 bypass fresh Working read | `/*/*/TurnEndGapRestartDeliveryTests/C1205_Flush_rechecks_a_new_turn_after_recovery` | Nonzero attempt/submission after newer prompt commits |
| FC-6 complete receipt and compatibility | Accept prefix in whole-wire matcher | `/*/*/VerificationRoundDeliveryTests/C544_CompletionReceiptWholeWire` | Invalid prefix wrongly confirms |

Systematic PC-84..100 variants remain reserved for SourceLanding Mutation after
Review and the original Code owner's confirmed landing. These family proposals
do not supply a sourced operation, bound payload digest, executed red, or a clean
Mutation verdict. The S2 held-live-event controls are inherited separate work;
S5 does not claim to rerun or mutate that path.
