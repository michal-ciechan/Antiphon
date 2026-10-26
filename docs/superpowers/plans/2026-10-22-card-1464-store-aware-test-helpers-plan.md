# CARD-1464: store-aware working/idle test helpers

Test-only. Card: `scripts/card.ps1 get CARD-1464 -Board Antiphon`. Census: `docs/investigations/2026-10-22-rc-windows-qualification-interim-reds.md` section 3.B (branch `feat/card-task-77c15d47`, rc-based). Task b3b8a088 (Code, Final), branch `feat/card-task-b3b8a088`, base 8fce73036a83ac817e4ba118801ff936f690103c.

## Mechanism

`SessionMessageQueueService.ReadWorkingAsync` (`server/Application/Services/SessionMessageQueueService.cs:5403`) reads the registered `SessionStateStore` projection when it is enabled; `BridgeQueueHarness` registers the store singleton (`tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:176`). `SessionStateStore.BeginWriteAsync` admits and loads the session (`EnsureLoadedAsync`), so once any read or lease has loaded a session, a transcript row written straight through EF is invisible until the next reconcile. The kill decision (`:5009`, `kill = AlwaysOn && !working && ...`), `FlushIfIdleAsync`, the stranded sweep and the delivery-failure verdict all read that projection. CARD-1373 repaired only `BridgeQueueHarness.MarkWorkingAsync`/`MarkIdleAsync` (store `BeginWriteAsync` + insert + `ReconcileAsync(reset: false)`), leaving the independent helpers and the raw TurnEnd inserts.

## Slices

- S1: one shared helper `tests/Antiphon.Tests/TestHelpers/StoreAwareTranscript.cs` (`InsertAsync`, `MarkWorkingAsync`, `MarkIdleAsync`, `AppendTurnEndAsync`, `SetWorkingAsync`, `InsertTurnAsync`; lease, insert through the harness, reconcile, the CARD-1373 D-1 pattern). `BridgeQueueHarness.MarkWorkingAsync`/`MarkIdleAsync` delegate to it. Conversions:
  - `PostLandMutationDeliveryTests.SetWorkingAsync` (raw "busy"/TurnEnd) and its six call sites (`ConfirmLandReceiptAsync`, `ConfirmQueuedReceiptAsync`, the V09b worker-busy site).
  - `QueuedReceiptAssertions.SetWorkingAsync` publishes through the harness whose queue flushes (`recovered ?? h`); `HoldRecipientBusyAsync` takes that harness; six callers updated.
  - `RunnerAlarmDeliveryTests.SetWorkingAsync` -> `rig.Harness` store.
  - `LandOutcomeDeliveryHarness.MarkBusyAsync`, `LandRefusalDeliveryHarness.MarkWorkingAsync`/`MarkIdleTurnAsync` -> `Caller` store (same row text).
  - `CheckNoteDeliveryHandoffTests` `:494` and `:684`: the raw end_turn after `MarkWorkingAsync` -> `AppendTurnEndAsync`.
  - `SessionMessageQueueDeliveryVerificationTests.C561.cs`: the five `InsertTurnAsync` calls after a `PoisonToolCall` ingested through the runtime (the ingest loads the store Working; the raw end_turn never reached it, so the kill and the stranded sweep were withheld) -> `StoreAwareTranscript.InsertTurnAsync`.
  - `SessionMessageQueueDeliveryVerificationTests.A_working_session_is_not_killed_when_the_record_never_arrives`: the `OnSubmitted` raw "on it" row -> `StoreAwareTranscript.MarkWorkingAsync` (the flush had loaded the store idle before the submit).
- S2: task report `.antiphon/task-b3b8a088.md` folded into the tested tip.

Traced and left alone (not this family):
- `TurnEndGapRestartDeliveryTests` / `TurnEndGapDeliveryHarness`: rows enter through the fake runner's transcript events and `Runtime.CatchUpTranscriptAsync` (ingestion publishes under the store lease); `MissEnd`/`publish: false` is the deliberate gap. No raw EF insert.
- `VerificationRoundDeliveryTests.C544_BriefHandoffRecovery`: the census cell fails at the dispatcher tick (`task.Status` Blocked `dispatch_brief_evidence_uncertain`) before the test writes any transcript row (`:192+`); census group F.
- `SessionMessageQueueServiceTests.MarkWorkingAsync`: that harness registers no `SessionStateStore` (`CreateHarnessAsync`), so `ReadWorkingAsync` takes the database path; the raw row is read correctly.
- Pre-load seeds (`CheckNoteDeliveryHandoffTests` "already eligible" end_turn rows before any read; `ObservableHarnessAsync`'s first `InsertTurnAsync`): written before the store loads the session, so the first load reads them. Unchanged.

## Verification design

- V-1 (red witness, direct build-slot run at the base 8fce73036, then CP-1): the `busy=True` cells of `PostLandMutationDeliveryTests` (`C478_V09a_LandCrashMatrix`, `C478_V09c_SettlementCrashMatrix`, `C478_G136_LandQueueKey`, `C478_G138_LandBusy`, `C478_G153_CompletionBusy`) fail `land-busy-does-not-submit` / `SubmittedBodies` at the base and pass with S1, with every `busy=False` sibling still green.
- V-2 (CP-2): `CheckNoteDeliveryHandoffTests` busy-recipient cases reach `DeliveryAttempts == 1` after the store-aware end_turn; the whole class stays green.
- V-3 (CP-3): the three kill-expected `C561` tests kill, the `recovered`/`still-failing` sweep cells deliver once, `A_working_session_is_not_killed_when_the_record_never_arrives` is not killed.
- R-1 (CP-4): `RunnerAlarmDeliveryTests`, `LandRefusalDeliveryWitnessTests`, `AgentTaskLandEnqueueTests`, `AgentTaskLandHeldNotificationTests` (the converted harness helpers) whole classes green.
- R-2 (CP-5): every `QueuedReceiptAssertions` consumer method that passes `busy` or holds the recipient busy stays green.
- Not run here, owed to the Windows lane: the desktop reproduction of the census run (the Windows qualification is live and must not be touched). The whole Unit lane is not a row: the change is confined to Postgres-backed integration helpers; the three Unit-category files that mention `BridgeQueueHarness` (`DelegationTestServicesTests`, `DelegationHarnessCensusTests` rule C, `SpecialistHealthAttentionTests`) reference `NoWorktreeManager`/git registration, not the working helpers. Full `PostLandMutationDeliveryTests` and the six consumer classes beyond the named methods are deferred, not passed (census groups E/H in PostLand are unrelated inherited reds; budget 50 minutes).
- PCs: none (test-only; the guarded defect is the fixtures' own stale-store read, and V-1's base run is its control). Pending for SourceLanding Mutation: zero-PC plan.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1464/` | post-land-busy | `/*/*/PostLandMutationDeliveryTests/(C478_V09a_LandCrashMatrix*)\|(C478_V09c_SettlementCrashMatrix*)\|(C478_G136_LandQueueKey*)\|(C478_G138_LandBusy*)\|(C478_G153_CompletionBusy*)` | V-1 | all 21 cells (12 + 6 + 3), 0 failed/skipped | 21 | 12 |
| CP-2 | S1 | CP-1 | check-note | `/*/*/CheckNoteDeliveryHandoffTests/*` | V-2 | all 7 methods, 0 failed/skipped | 7 | 5 |
| CP-3 | S1 | CP-1 | c561-live-turn | `/*/*/SessionMessageQueueDeliveryVerificationTests/(C561_*)\|(A_working_session_is_not_killed_when_the_record_never_arrives*)` | V-3 | 9 C561 cases + 1, 0 failed/skipped | 10 | 6 |
| CP-4 | S1 | CP-1 | land-harness-consumers | `/*/*/(RunnerAlarmDeliveryTests*)\|(LandRefusalDeliveryWitnessTests*)\|(AgentTaskLandEnqueueTests*)\|(AgentTaskLandHeldNotificationTests*)/*` | R-1 | every method of the four classes, 0 failed/skipped | 35 | 8 |
| CP-5 | S1 | CP-1 | queued-receipt-consumers | `/*/*/(DurableRunnerSpillReceiptTests*)\|(DispatcherRemotePrepStarvationTests*)\|(PendingSettlementDeliveryTests*)\|(RepairSourceSettlementTests*)\|(RemoteWorkspacePreparerTests*)\|(DispatcherSiblingInspectionSchedulingTests*)/(Dispatcher_pointer_survives_queue_binding_and_reaches_a_complete_UserPrompt*)\|(Queued_brief_is_written_by_the_runner_when_a_busy_recipient_becomes_eligible*)\|(Queued_runner_spill_keeps_its_bytes_and_receives_a_complete_UserPrompt*)\|(Silent_remote_tasks_ahead_do_not_delay_an_eligible_local_recipient*)\|(C1082_*)\|(C499_V38*)\|(C499_V39_RepairBriefIsTypedWithTheRepairBlock*)\|(C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease*)\|(C1349_LaterTaskReceivesBriefBeforeInspectionCompletes*)\|(C1349_BriefHandoffRecovers*)` | R-2 | the 13 named methods with their argument cases, 0 failed/skipped | 13 | 10 |

### Cost

Ordinary floor 41 minutes (sum of EstimatedMinutes) plus the red witness build and run.

## RC

Test-only. The touched files are byte-identical on `origin/master` and `origin/rc/2026-10-20` and CARD-1373 is on both, so the S1 commit cherry-picks onto rc without the plan document (rc carries no `docs/superpowers/plans`). The census found these classes red on rc at ceb6e3d6b; master carries the same reds because the files are unchanged.
