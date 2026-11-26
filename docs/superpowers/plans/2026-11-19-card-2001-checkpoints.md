# CARD-2001 checkpoint extract

Code task `da2ad302`. Branch `feat/card-task-da2ad302`.
Base: `36d0c758298b85516a29ee05a13225c15e14fc23`.

Tests only. The C488 wake in `AgentTaskLandNotificationService.ReconcileAsync` stays as landed.
CARD-2001 makes the three review advisories able to fail:

- A-1 `pending-attempt`: a Pending row at one attempt is not woken. A Sent row is not that witness.
- A-2 `same-pass`: a row linked in this pass is woken once at the relink site.
- A-3: the recording queue is drained. A woken zero-attempt row is Sent with one attempt afterwards. An unwoken row keeps the attempt and status it was seeded with.

## Verification design

V-1: `LinkedUnattemptedQueueWakeTests` `linked-pending` records one wake and, after the drain, is Sent with one attempt.
V-2: `pending-attempt` records no wake and stays Pending with one attempt. `one-attempt` (Sent), `held` and `drop-wakeup` record no wake and keep their seeded row.
V-3: `same-pass` records one wake, not two, and the drain claims that row once.
V-4: `AgentTaskLandNotificationRecoveryTests` methods named `C488_*` stay green.
R-1: `TestClassificationPolicyTests` runs once, inside the nine-class guard union. C1729 stays VIOLATIONS 0 / LISTED 162.
R-2: `DerivedImpactIndexTests` alone.
R-3: `WindowsUnitPlatformAdmissionTests` keeps its rostered Linux skip of `C1210_Windows_linux_only_outcomes_are_exact`.

PC-1 pending SourceLanding Mutation: remove the C488 `TryEnqueue`. `linked-pending` must fail on Status Sent.
PC-2 pending SourceLanding Mutation: remove only `DeliveryAttempts == 0`. `pending-attempt` must fail.
PC-3 pending SourceLanding Mutation: remove `linkedBeforeThisPass`. `same-pass` must record two wakes.
Operator waived the Mutation stage (2026-11-12). The Code task proves each assertion with a local production edit, quotes the failure, and reverts it. Those edits are not checkpoint rows. Neither PC is passed.

## Performance budget

CP-1 EstimatedMinutes 15 is the cold-build allowance. Later rows reuse that build. Estimates are provisional, not a measured p95. No green repetition. `ANTIPHON_LINUX_COMPILER_POLICY=0`. Admit a row only when the 1-minute load is below 16. No row is expected to exceed 10 minutes of test execution. The cold build sits inside CP-1's 15.

Manual M-1: no migration. Restart none.
M-2: the wake is not an OS branch. No `-Runner` or `-Platform` pin.
M-3: recompute nightly pins from master and report them. Re-pin only if a guard fails because this change touched its inputs.

### Checkpoints

Run through the checkpoint tool with `--expected-source-sha` set to committed HEAD, `--serial`, and no extra build-slot wrap. The tool leases its own slot. Wait until the exit is not 75.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c2001-wake/ | linked-wake | `/*/*/LinkedUnattemptedQueueWakeTests/*` | V-1, V-2, V-3 | 6 pass, 0 failed/skipped | 6 | 15 | true |
| CP-2 | all | CP-1 | recovery-c488 | `/*/*/AgentTaskLandNotificationRecoveryTests/C488_*` | V-4 | all listed, 0 failed/skipped | 4 | 8 | true |
| CP-3 | all | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-1 | 0 failed/skipped | 81 | 10 | true |
| CP-4 | all | CP-1 | windows-admission | `/*/*/WindowsUnitPlatformAdmissionTests/*` | R-3 | 6 pass; 1 rostered Linux skip | 6 | 6 | true |
| CP-5 | all | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-2 | all listed, 0 failed/skipped | 27 | 8 | true |
