# CARD-1464 Code task 5b7d64fd: store-aware fixture Working/idle writes

Branch `feat/card-task-5b7d64fd`, base `08765c68cdd05f4c80be092e06d39abe704c814e`, runner mirror
`/work/worktrees/task-5b7d64fd` (server2, Linux). Test-only; no product code changes.
Plan: `docs/superpowers/plans/2026-10-22-card-1464-store-aware-test-helpers-plan.md`
(validated with `Antiphon.Checkpoints import --plan`: 3 rows; advisory warning only, CP-1
derived timeout 60 min).

Commits:
- `e41eb41bb1c6caee2d77f9674af32837624b49df` test(CARD-1464): shared helper + conversions + plan.
- `3f94e0e4ca298837b46fa76899dfe6db6bbd3f43` first report commit; checkpoint run `20261022-122438-fb40`
  (full-class filters) bound it: CP-1 67 run / 66 passed (inherited red below), CP-2 19 / 17
  (two crash-worker Legacy cases, overlap flake), CP-3 123 / 123, all `slot=granted`,
  `sourceState=clean buildSource=verified`; receipt invalid `cleanup_refused` (the crash-worker child
  wrote `bin-c1464/.antiphon/acceptance/card-0079/crash-*` inside the build output).
- the plan-amendment + report commit after it is the tested tip: exclusions with evidence are in the
  plan; the second run's CHECKPOINT lines are in the final message to the caller.

## Red classification after run 1

- `C478_CompletionReplayAfterPartialEnqueueRetention`: inherited. Detached worktree at base
  `08765c68c`, build-slot run of the three red methods: total 4, failed 1 (this one, same assertion
  `SessionQueuedMessages ... should be 0 but was 1`), Legacy x3 passed. Census row 49. It runs with
  `busy: false`, so it never reaches the converted helper. Not fixed here (group H; needs its own card).
- `Legacy_captured_note_survives_worker_death`, `Legacy_produced_note_survives_pre_enqueue_worker_death(False)`:
  the same filter on the HEAD-source build `bin-c1464red/` (tests identical to `e41eb41bb`): total 4,
  failed 1 (the inherited one), Legacy x3 passed. Red only while CP-1 and CP-2 overlapped
  (25 s crash-worker poll window); repetition 1 of 3.

## Red witness at base 08765c68c (build-slot, `bin-c1464red/`, Linux)

- `PostLandMutationDeliveryTests` busy filter
  `(C478_V09a_LandCrashMatrix*)|(C478_V09c_SettlementCrashMatrix*)|(C478_G138_LandBusy*)|(C478_G153_CompletionBusy*)|(C478_G136_LandQueueKey*)`:
  total 21, failed 9 (V09a lost-wakeup/queue-inserted/before-enqueue True, V09c
  before-submit/after-receipt/after-prompt True, G138, G153, G136), succeeded 12 (every busy=False).
  Assertions: `adapter.Inputs`/`h.Adapter.Inputs should be empty` x6, `SubmittedBodies should contain` x3.
- Plausible set (CheckNote busy x2, C1205_*, C561_*, A_working_session_is_not_killed, C544): total 44,
  failed 13, succeeded 31. So the census reds are platform-neutral and red on Linux master too.

## Change

- New `tests/Antiphon.Tests/TestHelpers/StoreAwareTranscript.cs`: `WriteAsync(provider, session, insert)`
  takes the session's `SessionStateStore.BeginWriteAsync` lease, inserts, `ReconcileAsync(reset:false)`,
  releases (CARD-1373 pattern; no store registered -> plain insert). `SetWorkingAsync` writes the same
  AssistantText "busy" / TurnEnd end_turn rows (with record timestamp) the raw helpers wrote.
- `BridgeQueueHarness.MarkWorkingAsync`/`MarkIdleAsync` route through it (same rows, same lease order;
  `MarkIdleAsync` gains optional `stopReason`); new `InsertStoreVisibleTurnAsync`.
- Converted (same family, traced): `PostLandMutationDeliveryTests.SetWorkingAsync` and all its callers
  incl. `C699_Startup_pages_past_a_full_batch_without_skipping_owed_tasks` (CompletionRecovery partial;
  its "hold delivery at the working boundary" relied on the raw row, census row 43 timeout may be this);
  `CheckNoteDeliveryHandoffTests` raw TurnEnd after `MarkWorkingAsync` (both busy-recipient cases);
  the five C561 tests' `InsertTurnAsync` after the runtime-observed poison tool call (store said Working,
  so the kill was withheld); `A_working_session_is_not_killed_when_the_record_never_arrives` submit hook.

## Traced, NOT the same family (left unchanged)

- `TurnEndGapRestartDeliveryTests` C1205 x4 and `VerificationRoundDeliveryTests.C544_BriefHandoffRecovery`:
  both use `C544World`, ingest through the runtime (no raw insert), and log
  `Transcript state unavailable ... InvalidOperationException` right after a completed
  `transcript.save`. `C544World` registers no `SessionRuntimeActionCoordinator`, so
  `AgentSessionRuntime.PersistTranscriptAsync` -> `Actions().Hint` (`GetRequiredService`) throws after
  commit and the persist reports Empty. Probable fixture-registration drift from the CARD-1259 action
  drain; not fixed or proven here. Needs its own card (Debug/Code).
- Same-shape helpers with no red anywhere: `QueuedReceiptAssertions.SetWorkingAsync`
  (6+ consumer classes), `RunnerAlarmDeliveryTests.SetWorkingAsync`, `LandOutcomeDeliveryHarness.MarkBusyAsync`
  (no callers), `LandRefusalDeliveryHarness.MarkWorkingAsync`/`MarkIdleTurnAsync`; and
  `SessionMessageQueueServiceTests.MarkWorkingAsync`, whose harness registers no store (DB read path).
  Converting them without a red witness is unverifiable churn across unbounded classes; latent, can use
  `StoreAwareTranscript` in a follow-up.

## Other runs (unlisted, with reasons)

- Two compile builds into `bin-c1464red/` (first caught the CompletionRecovery caller).
- Pre-check at `e41eb41bb` on that compiled output, converted cells only: total 40, failed 0.
  Reason: de-risk the single closed checkpoint run that must bind the final tip.
- Checkpoint tool build `tools/Antiphon.Checkpoints -> bin-c1464tool/`.

## Landing / rc

- `git merge-tree --write-tree origin/master(8dbc49eda) HEAD`: clean.
- rc `origin/rc/2026-10-20` (`ceb6e3d6b`): every touched test file and `SessionStateStore.cs` are
  byte-identical to the base; per-commit `merge-tree --merge-base e41eb41bb~1 rc e41eb41bb` is clean.
  The same classes are red there (the census found them at `ceb6e3d6b`). The plan doc can be dropped
  from an rc pick (rc carries no plans).
- Windows desktop reproduction is owed to the Windows lane; nothing was run on the desktop and the
  running Windows qualification was not touched.

## Pending for Mutation

PC-1 and PC-2 as in the plan (`### Cost and mutation`); none executed.
