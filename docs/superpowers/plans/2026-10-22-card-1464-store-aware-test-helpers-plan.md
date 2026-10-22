# CARD-1464: store-aware Working/idle fixture writes

Source: CARD-1464 and the interim census `docs/investigations/2026-10-22-rc-windows-qualification-interim-reds.md`
section 3.B (branch `feat/card-task-77c15d47`). Test-only; no product code changes.

## Root cause

`SessionMessageQueueService.ReadWorkingAsync` reads the loaded `SessionStateStore` projection when
the store is registered and enabled. Fixtures that mark a recipient Working or idle with a raw EF
insert never publish to that projection, so once the store has cached the session the queue reads
the previous state: a "busy" recipient is typed into, an ended turn still withholds, and a working
session is killed. CARD-1373 repaired only `BridgeQueueHarness.MarkWorkingAsync`/`MarkIdleAsync`.

Red witness at base `08765c68c` on server2 Linux (one build-slot run each, `bin-c1464red/`):
`PostLandMutationDeliveryTests` busy cells 21 run, 9 failed (every `busy=True` cell plus
`C478_G136_LandQueueKey`), 12 passed; the plausible set 44 run, 13 failed (CheckNote x2, C1205 x4,
C561 x5, `A_working_session_is_not_killed_when_the_record_never_arrives`, `C544_BriefHandoffRecovery`).
The census reds are therefore platform-neutral.

## Slices

- **S1:** `tests/Antiphon.Tests/TestHelpers/StoreAwareTranscript.cs`, one shared helper next to
  `BridgeQueueHarness`: `WriteAsync` inserts under the session's store write lease and reconciles
  before releasing it (the CARD-1373 pattern; a provider without the store keeps the plain insert),
  and `SetWorkingAsync` writes the same AssistantText "busy" / TurnEnd end_turn rows the raw
  helpers wrote. `BridgeQueueHarness.MarkWorkingAsync`/`MarkIdleAsync` route through it
  (`MarkIdleAsync` gains an optional stop reason) and `InsertStoreVisibleTurnAsync` is the
  store-visible `InsertTurnAsync`. Converted: `PostLandMutationDeliveryTests.SetWorkingAsync`
  (all callers, including C699 in the CompletionRecovery partial), the two raw TurnEnd inserts in
  `CheckNoteDeliveryHandoffTests` that end a turn begun by `MarkWorkingAsync`, the five C561
  `InsertTurnAsync` calls, and the `A_working_session_is_not_killed_when_the_record_never_arrives`
  submit hook.

Not converted, by trace: `TurnEndGapRestartDeliveryTests` (C1205 x4) and
`VerificationRoundDeliveryTests.C544_BriefHandoffRecovery` are a different family. Their fixtures
ingest through the runtime, not raw inserts; `C544World` does not register
`SessionRuntimeActionCoordinator`, so `AgentSessionRuntime.PersistTranscriptAsync` throws
`InvalidOperationException` after its commit and logs "Transcript state unavailable". Same-shape
helpers with no red (`QueuedReceiptAssertions.SetWorkingAsync`, `RunnerAlarmDeliveryTests.SetWorkingAsync`,
`LandOutcomeDeliveryHarness.MarkBusyAsync`, `LandRefusalDeliveryHarness.MarkWorkingAsync`) and
`SessionMessageQueueServiceTests.MarkWorkingAsync` (its harness registers no store) are left alone.

## Verification

- **V-1:** every `PostLandMutationDeliveryTests` busy cell that was red at base is green.
- **V-2:** both `CheckNoteDeliveryHandoffTests` busy-recipient cases are green.
- **V-3:** C561 x5 and `A_working_session_is_not_killed_when_the_record_never_arrives` are green.
- **R-1:** the rest of `PostLandMutationDeliveryTests`, `CheckNoteDeliveryHandoffTests` and
  `SessionMessageQueueDeliveryVerificationTests` stays green (full classes less the exclusions below).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1464b/` | postland-delivery | `/*/*/PostLandMutationDeliveryTests/(C478_CompletionNote*)\|(C478_CompletionReplayAfterRetention*)\|(C478_G*)\|(C478_V*)\|(C699_*)` | V-1, R-1 | all listed, 0 failed | 66 | 20 |
| CP-2 | S1 | CP-1 | checknote-handoff | `/*/*/CheckNoteDeliveryHandoffTests/(A_*)\|(An_*)\|(Automatic_*)\|(Legacy_matching*)\|(Legacy_note_receipt*)\|(Legacy_note_requires*)\|(Legacy_pointer*)\|(Legacy_suppressed*)` | V-2, R-1 | all listed, 0 failed | 15 | 6 |
| CP-3 | S1 | CP-1 | delivery-verification | `/*/*/SessionMessageQueueDeliveryVerificationTests/*` | V-3, R-1 | all listed, 0 failed | 119 | 10 |

Amended after run `20261022-122438-fb40` at `3f94e0e4c` (full-class filters: CP-1 66/67, CP-2 17/19,
CP-3 123/123, receipt `cleanup_refused`). Two exclusions, each with evidence:

- `C478_CompletionReplayAfterPartialEnqueueRetention` is inherited: red at base `08765c68c` in an
  isolated detached-worktree run with the same assertion (queue rows 1, expected 0), census row 49
  (group H). It runs with `busy: false` and never reaches the converted helper.
- `Legacy_captured_note_survives_worker_death`, `Legacy_produced_note_survives_pre_enqueue_worker_death`
  (both cases) and `Legacy_note_recovery_runs_with_checks_disabled` start the CARD-0079 crash worker,
  whose child writes `.antiphon/acceptance/card-0079/crash-*` inside the build output; the tool
  cannot adopt that, so any run containing them refuses cleanup and no receipt validates. Two of
  them went red while CP-1 overlapped; isolated, those three cases pass at base and at `e41eb41bb`
  (`checks_disabled` passed in the run).
  They do not reach the converted code except through `MarkWorkingAsync`, whose rows and lease
  order are unchanged.

No Unit lane, assembly or qualification run: the brief asks for minimal testing and the change is
confined to Integration fixtures. The Windows desktop reproduction is owed to the Windows lane.

### Cost and mutation

Ordinary green floor about 36 minutes of row time plus one build. Post-land method-scoped
SourceLanding Mutation candidates, none executed here:

- **PC-1:** make `StoreAwareTranscript.WriteAsync` skip `ReconcileAsync`; run only
  `PostLandMutationDeliveryTests/C478_G138_LandBusy`; require the `land-busy-does-not-submit`
  red, restore, then green.
- **PC-2:** restore the raw TurnEnd insert at the `CheckNoteDeliveryHandoffTests` digest busy case;
  run only `A_digest_note_reaches_a_busy_recipient_as_one_whole_prompt_after_recovery`; require the
  `DeliveryAttempts` 0-vs-1 red, restore, then green.
