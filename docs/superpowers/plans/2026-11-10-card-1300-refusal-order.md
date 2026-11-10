# CARD-1300 refusal order

Ordinary creates decide `card_implementation_closed` and `card_admission_changed` before the capacity preview and before BaseTask git errors. Capacity preview stays before git for an open, unchanged card. Handoff and SourceLanding keep that decision inside the card transaction.

## Decisions

- D-1. For an ordinary create (`handoffActionId` and `SourceLandingOperationId` both null) of an implementation role, `RefuseImplementationAdmission` runs on a fresh card read before `PreviewCanCreateAsync` and `PrepareWorktreeBaseAsync`. A closed card that is also at capacity returns `card_implementation_closed`. A foreign BaseTask on a closed card returns `card_implementation_closed` with zero git calls.
- D-2. The locked recheck stays. A change that commits during git can still surface as a base error; the lock covers a change during git. Already-closed wins over a concurrent fence change.
- D-3. Workflow-run-only change stays a benign admission from the locked row. Board, identifier, and Review verification policy changes refuse with `card_admission_changed`. Archived stays `card_implementation_closed`.
- D-4. This task's closed runs are build-slot commands on the worktree native output (`UseAppHost=false`). They are not checkpoint-tool rows and do not use an alternate `OutputPath`. Do not run `tools/Antiphon.Checkpoints` for CP-1 or CP-2.

## Verification design

Red witness: `d42d5372c4a9a6968232683d370eb20050599d3b` (tests only, production still refused after capacity and git).

V-1. `C1300_ClosedCardPrecedesCapacityAndBase` capacity, base, and git: `card_implementation_closed`, git `Calls == 0`.
V-2. `C1300_AdmissionChangePrecedesCapacityAndBase` capacity and base: `card_admission_changed`, git `Calls == 0`, review policy stored as FullOnly.
V-3. `C1289_ConcurrentCardEdit` board, identifier, and review-policy refuse; workflow admits the new run.
V-4. `C1289_GitLockDuration`, `C1289_CardLockOrder`, `C1288_ClosedCardBindingForms`, and `C1303_R4_ConcurrentCodeCreatesSurviveTransitionSweep` stay green. The extra pre-preparation projection is after `AdmissionPreReadPause` releases.
V-5. `T0442_V17_existing_create_guards_precede_source_selection` keeps `Calls == 0`, including concurrency.
R-1. Batched guards, one run: classification policy, classification guard, slow-registry merge, slow-tripwire, native Linux policy witness, mechanical PC contract, task platform guidance, layering.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | n/a | card-1300-touched | not a checkpoint-tool row; build-slot native output `/*/*/(PipelineSpeedIntegrationTests*)\|(AgentTaskWorktreeBaseCreateTests*)/*` | V-1 V-2 V-3 V-4 V-5 | 0 failed | n/a | 40 |
| CP-2 | S1 | n/a | card-1300-guards | not a checkpoint-tool row; build-slot native output `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | R-1 | 0 failed | n/a | 15 |

CP-1 includes all of `AgentTaskWorktreeBaseCreateTests` because a TUnit 1.44 OR cannot name one method of a second class without a slash inside parentheses, which the importer rejects. That class has six methods. V-5 is `T0442_V17_existing_create_guards_precede_source_selection`.
