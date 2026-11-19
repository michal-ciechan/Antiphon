# C488 linked unattempted queue wake

Code owner: task `a18cc863`. Branch `feat/card-task-a18cc863`.
Base: `42d7af1f33cfd0236f3bbcdcea57c60bf948e98d`.

A hosted reconcile wakes a queue row that was already linked, still Pending, with zero delivery attempts, and past its hold, unless `DropWakeup("completion", note.Id)` is true. A charged attempt stays unwoken (CARD-1892). The Review receipt timeout records that row. The Windows E2E lane is not run here.

## Verification design

V-1: `LinkedUnattemptedQueueWakeTests` wakes only `linked-pending`. `one-attempt`, `held` (future `HoldUntil`) and `drop-wakeup` stay empty. The witness was red at `60665a342fc33ddb6ef224087f49621830758508` before the production wake: 4 executed, 1 failed (`linked-pending` flush list empty), 3 passed.
V-2: Existing `AgentTaskLandNotificationService.ReconcileAsync` callers stay green: recovery, persistence, reconcile-load, park follow-up, and `CompletionNoteDurableSweepTests`.
V-3: CARD-1892 composerless attempt tests stay green. `DeliveryAttempts == 0` is what keeps a claimed Sent attempt out of this wake.
R-1: Classification, layering, mechanical, platform and lane guards, including `TestClassificationPolicyTests` once. C1729 stays VIOLATIONS 0 / LISTED 162. `WindowsUnitPlatformAdmissionTests` keeps its Linux skip of `C1210_Windows_linux_only_outcomes_are_exact`.
R-2: `DerivedImpactIndexTests` alone.
R-3: Recompute nightly pins, `land-graph -Check`, `scripts/check-evidence-diff.ps1` over base..HEAD, and `git merge-tree --write-tree origin/master HEAD`. No `-Update` and no merge.

`CompletionNoteWorkQueueTests` does not call `ReconcileAsync`. `AgentTaskLandNotificationRescanTests` does not call it and carries a 600s timeout. Both stay out of this table.

Manual M-1: `tests/Antiphon.E2E` compiles on Linux. It is Windows-native (fakegrok + ConPTY) and is not executed here.
M-2: The wake is not an OS branch. No `-Runner` or `-Platform` pin.
M-3: No migration. Restart none from this task.

PC-1 pending SourceLanding Mutation: remove the linked-row `TryEnqueue`. CP-1 `linked-pending` must fail on an empty flush.
PC-2 pending SourceLanding Mutation: drop the `DeliveryAttempts == 0` gate. CP-1 `one-attempt` must wake and fail the empty assertion.
Operator waived mutation execution (2026-11-12). Neither PC is passed.

## Performance budget

CP-1 EstimatedMinutes 15 is the cold-build allowance required by the brief. Later rows reuse that build. Estimates are provisional, not a measured p95. No green repetition. `ANTIPHON_LINUX_COMPILER_POLICY=0`. Admit a row only when the 1-minute load is below 16.

The red witness and the Linux E2E compile are unlisted: red-first confirmation before the fix, and a compile of a Windows-only fixture that has no Linux test row.

### Checkpoints

Run through the checkpoint tool with `--expected-source-sha` set to committed HEAD, `--serial`, and no extra build-slot wrap. The tool leases its own slot. Wait until the exit is not 75.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c488-wake/ | linked-wake | `/*/*/LinkedUnattemptedQueueWakeTests/*` | V-1 | 4 pass, 0 failed/skipped | 4 | 15 | true |
| CP-2 | all | CP-1 | recovery-core | `/*/*/AgentTaskLandNotificationRecoveryTests/(C488_*)\|(C467_V08_Destination*)\|(C467_V10_*)\|(C467_V14_*)\|(C467_V09_*)` | V-2 | all listed, 0 failed/skipped | 13 | 8 | true |
| CP-3 | all | CP-1 | recovery-attempt | `/*/*/AgentTaskLandNotificationRecoveryTests/(C481_*)\|(C467_V08_Retry*)\|(C550_*)` | V-2 | all listed, 0 failed/skipped | 17 | 8 | true |
| CP-4 | all | CP-1 | recovery-legacy | `/*/*/AgentTaskLandNotificationRecoveryTests/(Legacy_*)\|(C1705_*)` | V-2 | all listed, 0 failed/skipped | 19 | 8 | true |
| CP-5 | all | CP-1 | persistence-park | `/*/*/(AgentTaskLandNotificationPersistenceTests*)\|(AgentTaskLandNotificationReconcileLoadTests*)\|(AgentTaskLandNotificationParkFollowUpTests*)/*` | V-2 | all listed, 0 failed/skipped | 22 | 8 | true |
| CP-6 | all | CP-1 | durable-sweep | `/*/*/CompletionNoteDurableSweepTests/*` | V-2 | all listed, 0 failed/skipped | 1 | 4 | true |
| CP-7 | all | CP-1 | claimed-attempt | `/*/*/SessionMessageQueueInterruptedAttemptTests/(C1892_Composerless_*)\|(C1892_Notice_*)` | V-3 | all listed, 0 failed/skipped | 9 | 6 | true |
| CP-8 | all | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-1 | 81 pass, 0 failed/skipped | 81 | 8 | true |
| CP-9 | all | CP-1 | windows-admission | `/*/*/WindowsUnitPlatformAdmissionTests/*` | R-1 | 6 pass; 1 rostered Linux skip | 6 | 6 | true |
| CP-10 | all | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-2 | all listed, 0 failed/skipped | 27 | 15 | true |
