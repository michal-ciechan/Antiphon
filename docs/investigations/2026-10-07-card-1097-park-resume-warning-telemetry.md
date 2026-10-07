# CARD-1097 park-resume warning telemetry repair selection

Repair selection for the S4 warning lookup. The plan table in
`docs/superpowers/plans/2026-10-07-card-1108-1124-followups-plan.md` is not edited.
This note is the checkpoint manifest for the repair. The whole Unit lane is outside
this ordinary scope.

CP-16 keeps the plan filter `C1097_*`, which matches `C1097_RefusedParkedResumeWarnsOncePerReason`
and does not match `C1097Telemetry_*`. CP-36 is the three telemetry methods. CP-17 runs the
park family. Its repaired roster is 18: release 3, sync 2, resume 7 (the prior 4 plus the
three telemetry methods), delivery 6 (the landed C1104 method is already in this tree).
The plan floor of 14 stays the historical minimum; this row's floor is 18 so a missing
method fails the row. CP-17's estimate is 12 minutes because the prior host wall for the
15-result roster was 386 seconds. That changes only this row's derived deadline
(`max(15, 3 * EstimatedMinutes)`), not a test assertion.

`BlockedTaskParkResumeTests` is the class that holds the Queued/NotClaimed refusal test.
`BlockedTaskParkProjectionTests` (inside CP-18) and `RunnerBranchContractDocumentationTests`
(CP-93) pin `docs/session-runtime-invariants.md`. One isolated build, serial rows, same
After group.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-16 | S4 | `tests/Antiphon.Tests -> bin-c1097b/` | warn-s4 | `/*/*/BlockedTaskParkResumeTests/C1097_*` | V-8 | exact 1 result, 0 failed/skipped | 1 | 5 | true |
| CP-36 | S4 | CP-16 | telemetry-s4b | `/*/*/BlockedTaskParkResumeTests/C1097Telemetry_*` | V-8 repair | exact 3 results, 0 failed/skipped | 3 | 5 | true |
| CP-17 | S4 | CP-16 | park-s4 | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-3 | 18 results (3 + 2 + 7 + 6), 0 failed/skipped | 18 | 12 | true |
| CP-18 | S4 | CP-16 | publication-s4 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | V-13, R-4, R-5 | exact 7 results, 0 failed/skipped | 7 | 2 | true |
| CP-19 | S4 | CP-16 | lifetime-s4 | `/*/*/(DispatcherSweepLifetimeTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | R-6 | exact 7 results, 0 failed/skipped | 7 | 3 | true |
| CP-90 | S4 | CP-16 | reclaim-s4b | `/*/*/BlockedTaskParkReclaimTests/*` | parked continuation class | exact 11 results, 0 failed/skipped | 11 | 7 | true |
| CP-91 | S4 | CP-16 | seat-s4b | `/*/*/TerminalRunnerSeatReleaseTests/*` | seat release class | exact 39 results, 0 failed/skipped | 39 | 9 | true |
| CP-92 | S4 | CP-16 | followup-s4b | `/*/*/RemotePoolFollowUpAdmissionTests/*` | launch follow-up class | exact 3 results, 0 failed/skipped | 3 | 2 | true |
| CP-93 | S4 | CP-16 | docs-s4b | `/*/*/RunnerBranchContractDocumentationTests/*` | runtime invariants pin | exact 5 results, 0 failed/skipped | 5 | 1 | true |
| CP-94 | S4 | CP-16 | registry-s4b | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | registry guard | exact 3 results, 0 failed/skipped | 3 | 2 | true |
