# CARD-1304 round 2 Code evidence

Round 2 closes Review 7415c025 blockers D-1..D-5 (docs/superpowers/plans/2026-11-11-card-1304-review-7415c025.md on feat/card-task-7415c025). The changes are test-only. No file under `server/`, `src/`, `client/`, `tools/` or `scripts/` changed. No existing assertion was loosened and no timeout was widened.

Task: 1ae8dfd4-d0eb-4bc2-a705-a458d13f42a0. Role: Code. Round: Final.
Branch: feat/card-task-1ae8dfd4. Worktree: /work/worktrees/task-1ae8dfd4.
Base (reviewed tip): 185cc85a2685287bfea484ed90e2f71747c9154a.
Plan: docs/superpowers/plans/2026-10-15-card-1259-transcript-pump-verification.md.

## Commits

| SHA | Subject |
|---|---|
| 8c611edfc9968e185803cc3b0d41e9eb061c7256 | close Review 7415c025 blockers D-1..D-5 witnesses |
| 6f6480ca1c1c5b5b61c9203e07fdd413faee2e3d | bind the seat fully and settle owners before counting |
| 38cf27eb447442d503c9bb1aef656a28a1a2ecbb | run the new D-3 and D-4 witnesses first |
| b916545ce4320d32b8d9bc349a2ee2c5b36acd60 | refresh the derived-impact index |
| 9ff50585d1ecc64ee243dc3c444acd6654c2c89f | count the routing class as an owed notification consumer |

There is no production fix SHA. Red is shown by local mutants of production code. Each mutant was env-gated (`C1304_MUTANT`), built once, run per witness, and then restored with `git checkout --` of the three exact server paths. Tested source is clean.

## What each blocker now asserts

- D-1 (`SessionRuntimeActionRoutingTests.cs`, `working`). After the Working-held drain, the status action is retained as a retry-held queue action: `FailureReason = IOException` (not `Deferred`), `DueAt` an hour ahead. Then the TurnEnd is persisted. The test asserts that `SessionStateStore` is not Working, so DueAt is the only block left. The drain must give zero input, a null verdict, and the action still at QueuePhase. The schedule clock then advances, and the existing common path delivers the body once.
- D-2 (`SessionRuntimeActionTests.cs`, `uncooperative-effect`). A `LifecycleProbe` decorates the scoped `AppDbContext` registration (every action scope resolves it) to count scopes created and disposed. It also records `IDelegateSessionStopper.KillAsync`. A `FakeAgentProtocolAdapter` is registered, and the session holds a full runner seat (RunnerId, RunnerStoreId, RunnerCwd). While Stop waits: owners=1, live scopes>0, kills=0, adapter not killed, and the session is Running with the same seat. After the joined stop: owners=0, live scopes=0, kills=0, seat unchanged, and exactly one payload.
- D-3 (`C1259_retry_and_retention_preserve_pending_turn`).
  - `retry-clock` first runs `VerifyFourFailedOwnersAsync`. Four failing owners occupy all four workers at once (OwnerCount==4 at the barrier), then fail. Afterwards OwnerCount is 0, and a later session progresses while the four debts stay at Phase 0, Attempts 1, `IOException`, DueAt = failure+1s. No owner retries before it is due. After the clock passes 100 days, all four recover, each with exactly one payload.
  - `phase-failure` first runs `VerifyEveryPhaseEffectFailureAsync`. One downstream effect failure is injected per phase: UI publish (0), capacity (1), channel route (4), review (5) and task reply (6), each in a fresh fixture. Each asserts Hits==1, the action retained at the failed phase (not acknowledged), Attempts 1, the failure reason, DueAt+1s, no retry before due, a retry at due, the UI payload once, and the action gone. If the injection is removed, Hits==0 and the retained-phase assertion fails. Phases 2, 3 and 7 have no replaceable downstream for these rows; their acknowledgement saves stay covered by `VerifyEveryPhaseAcknowledgementAsync`.
- D-4 (`C1259_missing_or_truncated_prompt_never_confirms`, `old-generation`). `VerifyStaleQueueGenerationLateConfirmAsync` runs first. It seeds an ordinary Claude row whose only change is a stale `LastDeliveryGeneration` (session StartedAt untouched). The row is at cap, at the current floor. A complete UserPrompt plus TurnEnd must late-confirm the row with zero input, attempts unchanged, and StartedAt unchanged.
- D-5 (`parent-handoffs`). After `VerifyParentRoutingAsync`, `VerifyParentHandoffCutsAsync` runs H0-H4 for eligible and busy parents. It uses the durable parent note (a Blocked report with `BlockedTaskParkingOptions.Enabled`), a `LandDeliveryBoundary` and a queue-insert interceptor.
  - H0 throws at `before-enqueue`.
  - H1 fails the keyed queue insert (hit count 1, no row).
  - H2 drops the wake after the queue commit.
  - H3 has the complete parent UserPrompt streamed, then cuts at `receipt-before-save` (note not confirmed).
  - H4 is confirmed before restart.

  Each cut restarts DI over the same clone, re-attaches the parent adapter, advances past the retry, and recovers through `AgentTaskLandNotificationService.ReconcileAsync` and the real queue. A busy parent gets zero input until its TurnEnd. Final checks: note Confirmed with `ConfirmingPromptSequence` equal to the single parent UserPrompt, the same note and queue ids, `SourceTaskId`, attempts 1, and exactly one typed body.

## Mutant reds (local, not discharged PCs)

All runs went through build-slot leases on the mutant build (lease 5d07f64c-07c7-40f3-9f52-ac78fcc207be, then 2b0a3ba3-028d-46c1-8ab7-78f163f9a0a7 after the reorder).

| Mutant | Production change | Red witness and assertion |
|---|---|---|
| dueat | DrainCoreAsync ignores `action.DueAt > now` | D-1 `working`: `h.Adapter.Inputs` had 2 items (paste body + Enter), RoutingTests.cs:147 |
| owner | WorkAsync never removes `_owners` | D-2: "no owner remains after the joined stop" 1 vs 0; D-3 retry-clock OwnerCount 1 vs 0 |
| scope | DrainCoreAsync never disposes its scope | D-2: "every action scope was disposed by the joined stop" LiveScopes 1 vs 0 |
| kill | worker cancellation calls `IDelegateSessionStopper.KillAsync` | D-2 held: "no session stop/kill request" 1 vs 0 |
| seat | worker cancellation stops the session and clears its seat | D-2 held: Status Stopped vs Running ("no park or stop") |
| ackfail | the retry catch also advances Phase | D-3 phase-failure: "V-10 effect failure phase 0: the failed phase is not acknowledged" (ActionTests.cs:745); D-3 retry-clock debts condition |
| genlate | late confirm refuses any row whose LastDeliveryGeneration differs | D-4: "an ordinary Claude message is late-confirmed ... whatever its attempt generation" null vs LateConfirmed (Finish.cs:309) |
| noteack | ReconcileAsync marks the note Confirmed at enqueue | D-5 H0 eligible: ConfirmingPromptSequence null vs 9 |
| notedrop | ReconcileAsync marks a TaskCompletion note NotRequired | D-5 H0 eligible: "cut ran once" 0 vs 1 |

The first ackfail and genlate runs failed on the older subcases first. The new helpers were moved ahead (38cf27eb4) and re-run, with the reds shown above.

## Runs at the tested tip

- Touched classes at b916545ce4320d32b8d9bc349a2ee2c5b36acd60 (source clean): filter `/*/Antiphon.Tests.Application/(SessionRuntimeActionRoutingTests*)|(SessionRuntimeActionTests*)/*`. Lease 87bdfb5d-e52f-4c27-aba5-e2b7e24a2731, waited=180s, held=163s. Result: total 63, failed 0, skipped 0, 2m39s. Changed cases: working 3.4s, old-generation 1.7s, parent-handoffs 10.4s, uncooperative-effect 0.7s, retry-clock 2.2s, phase-failure 4.7s.
- Batched guards at b916545ce: filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(DerivedImpactIndexTests*)/*`. Lease 202f234e-be1f-44d9-bdc0-518332af93ea, waited=30s, held=769s. Result: total 102, failed 1, 12m46s. The red was `capped_real_paths_keep_the_specific_class`: owed count for AgentTaskLandNotificationService.cs was 13 against the pinned 12. That is caused by the new D-5 consumer, not inherited (the base index owed 12). 9ff50585d pins 13.
- At 9ff50585d1ecc64ee243dc3c444acd6654c2c89f: `/*/*/DerivedImpactIndexTests/(capped_real_paths_keep_the_specific_class*)|(committed_index_matches_the_builder*)`. Lease 2d86c330-61c3-47d7-9340-f513c0103f50, waited=60s. Result: total 2, failed 0. The other 100 guard cases were not rerun (green above, and 9ff50585d changes one integer in one guard).

`tests/test-execution-policy.json` did not change, so the policyHash and PinnedInputDigest were not resealed. The derived-impact index was regenerated with `DerivedImpactIndexBuilder.Write`, using a temporary, uncommitted test host that was deleted before the tested build.

## Budgets

No new TUnit method and no new `[Timeout]`. One new wait: `OwnersSettledAsync` polls up to 10 s for an owner that leaves a few milliseconds after its drain returns. The count assertion follows the poll, so a leaked owner still fails. The other waits reuse the existing `UntilAsync` / `AwaitReadyAsync` bounds. The added cost is about 20 s of sequential test time across the six changed cases.

## Owed / not done

- PC-1..PC-113 stay pending for method-scoped SourceLanding Mutation. The local mutants above are not discharged controls.
- Phases 2, 3 and 7 have no injectable downstream effect for these fixture rows. Their acknowledgement-save failures are the existing per-phase subcase.
- Not attempted: the checkpoint-tool single-SHA CP-3..CP-13 rerun at the new tip; the touched classes were run whole instead.
- Live desktop, browser and broker acceptance stay out of scope.
