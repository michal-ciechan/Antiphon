# CARD-1103 S3b reply-guidance repair

Closed checkpoint selection for the mark-read repair. CP-12..CP-15 are copied from
`docs/superpowers/plans/2026-10-07-card-1108-1124-followups-plan.md`. CP-R5 is the new
admission regression. CP-R1 is the registry guard. CP-R6 is the runner-branch doc class
named by the repair brief. One isolated build. Serial, because these rows share Postgres.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-12 | S3 | `tests/Antiphon.Tests -> bin-c1103-s3b/` | guidance-s3 | `/*/*/BlockedTaskParkDeliveryTests/C1103_*` | V-6, V-7 | exact 1 result, 0 failed/skipped | 1 | 5 | true |
| CP-13 | S3 | CP-12 | delivery-s3 | `/*/*/BlockedTaskParkDeliveryTests/*` | R-3 | exact 5 results (4 + 1 new), 0 failed/skipped | 5 | 5 | true |
| CP-14 | S3 | CP-12 | publication-s3 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | V-13, R-4, R-5 | exact 7 results, 0 failed/skipped | 7 | 2 | true |
| CP-15 | S3 | CP-12 | followup-s3 | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(RemotePoolFollowUpAdmissionTests*)/*` | R-3, R-9 | exact 11 results (3 + 2 + 3 + 3), 0 failed/skipped | 11 | 5 | true |
| CP-R5 | S3 | CP-12 | admission-s3b | `/*/*/BlockedTaskParkReplyAdmissionTests/C1103_RemotePoolReplyNamesOnlyAdmittedRelease` | R1 | exact 1 result, 0 failed/skipped | 1 | 8 | true |
| CP-R1 | S3 | CP-12 | registry-s3b | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | registry | exact 3 results, 0 failed/skipped | 3 | 2 | true |
| CP-R6 | S3 | CP-12 | branch-doc-s3b | `/*/*/RunnerBranchContractDocumentationTests/*` | branch contract | exact 5 results, 0 failed/skipped | 5 | 2 | true |
