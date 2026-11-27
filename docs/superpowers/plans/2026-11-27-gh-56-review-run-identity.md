# GH-56 review-run identity and close verification

GitHub intake returns the existing run when its status is anything other than Closed. Identity is the GitHub host kind, the case-normalized owner and repository, and the PR number. Installation credentials and the current head SHA do not change the identity. The database enforces the invariant across simultaneous requests.

## Verification design

| Witness | Test | Positive control |
|---|---|---|
| V-1 Repeated intake | `ReviewRunIntakeApiTests.Repeated_intake_returns_one_run` | Return the existing run through the Created response. The second request must fail the expected 200 assertion. |
| V-2 Run status and retry | `ReviewRunIntakeApiTests.Every_non_closed_status_keeps_its_run` | Treat Failed as permission to close and replace the run during intake. The Failed cases must fail the existing-run assertion. |
| V-3 Close and later intake | `ReviewRunIntakeApiTests.Close_checks_token_preserves_audit_and_allows_a_new_run` | Save Merged instead of Closed in the close operation. The status assertion must fail. |
| V-4 Parallel intake | `ReviewRunIntakeApiTests.Parallel_intakes_save_one_run_and_return_one_created_response` | Omit the partial unique index. Both requests cross the save barrier and the test must report two Created responses. |
| V-5 Existing duplicates | `ReviewRunIdentityMigrationTests.Up_closes_eight_duplicates_and_Down_preserves_the_history` | Rank by oldest start time first. The migration applies and the newest-run assertion must fail. |
| V-6 Normalization | `ReviewRunIntakeApiTests.Repo_case_and_pull_request_URL_resolve_to_the_same_run` | Reject PR URLs while continuing to normalize owner/name case. The URL intake assertion must fail. |
| R-1 Regression | Review polling, demo workflow, sign-in, current model and original review-engine migration classes | Exercise the existing intake caller, gate path and migrations across the change. |
| R-2 Historical fixture | The two methods that rewind via `MigrationTestDatabase.DetachHostLoadAsync` | Confirm the fixture can detach an empty review schema with the new identity migration applied. |
| R-3 Guard union | Six classification and test-lane guards | The expected roster has 53 executions. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-gh56-green/` | review-run | `/*/*/(ReviewRunIntakeApiTests*)\|(ReviewRunIdentityMigrationTests*)\|(ReviewEngineMigrationTests*)\|(ReviewEngineDatabaseMigrationTests*)\|(ReviewPollingApiTests*)\|(ReviewWorkflowApiTests*)\|(ReviewSignInApiTests*)\|(AppDbContextModelTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6, R-1 | >= 20 executed, 0 failed/skipped | 20 | 14 |
| CP-2 | all | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestLaneCategoryGuardTests*)/*` | R-3 | >= 53 executed, 0 failed/skipped | 53 | 7 |
| CP-3 | all | CP-1 | migration-rewind | `/*/Antiphon.Tests.Application/(C0824RefOnlyIdentityTests*)\|(AgentTaskLandHeldNotificationTests*)/(Mode_round_trips_with_legacy_default*)\|(C641_Upgrade_preserves_existing_held_notification_debt*)` | R-2 | >= 2 executed, 0 failed/skipped | 2 | 5 |
