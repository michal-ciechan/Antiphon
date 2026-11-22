# CARD-2057 retirement audit repair after Review round 1

Retiring a work volume requires raw index-byte equality or the exact CRLF rendering of a small tracked blob under an explicit committed declaration. Local attribute files and staged attribute changes have no authority over the exception. Preview and apply share the read-only audit. The CRLF conversion is only attempted when both the working file and indexed blob are at most 16 MiB.

- V-1 requires the original four linked-worktree cases in CP-1. Git must produce the positive checkout. The edited, mixed and undeclared changes must reach the independent byte check and refuse.
- V-2 requires the common-local, linked-local and staged-attribute linked-worktree cases in CP-90 and CP-1. Preview must refuse at the linked checkout's independent byte check with zero removals.
- V-3 requires the boundary case in CP-90 and CP-1. A valid 16 MiB indexed blob has a 16 MiB plus one byte checkout. Preview must refuse with zero removals.
- V-4 requires the exact C1210 source-roster closure and portable siblings in CP-6. CP-91 records the missing roster identity before repair.
- V-5 requires Bash syntax checks of the entry script and generated audit plus the 13 byte boundaries from Review round 1. Run them on owned fixtures. Use no production volumes or containers.
- R-1 covers the five historical C1008 audit methods in CP-2 and three Git encoding methods in CP-3.
- R-2 covers the Final repository guards in CP-4.
- R-3 covers the applicable impact selector results in CP-1, CP-4 through CP-13. Gather exact method and class counts from fresh TRX. Partition the main project selector into rolling, retirement, platform contract, portable and isolated Postgres rows. The Windows-only nested audit methods `C1050_Windows_remote_outcomes_are_exact`, `C1050_Windows_rolling_outcomes_are_exact` and `C1210_Windows_linux_only_outcomes_are_exact` retain their native qualification lane.

The original 53 content cases took 20m54s in Review round 1. The other audit rows are bounded to the changed safety invariant and adjacent selectors. CP-13 uses the owned test PostgreSQL fixture. Use a minute suffix for the wall budget and join the checkpoint tool until its terminal exit. Native Linux runs the real Bash audit, Git operations and linked-worktree setup with fixture-owned volume paths. The portable C1210 assertions census the Windows policy from source. Restart impact is none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | final | tests/Antiphon.Tests -> bin-c2057-final-1/ | retirement-content | `/*/*/VolumeRetirementContentTests/*` | V-1, V-2, V-3, R-3. All original 53 cases and four new arguments | Zero failures and skips | 57 | 13 | false |
| CP-2 | final | tests/Antiphon.Tests -> bin-c2057-final-2/ | retirement-git | `/*/*/RemoteScriptContractTests/(C1008_Recycle_audits_work_as_1654)\|(C1008_Recycle_refuses_unpublished_and_dirty_work)\|(C1008_Recycle_refuses_uninspectable_git)\|(C1008_Recycle_reaps_dirty_scratch)\|(C1008_Recycle_audits_promisor_checkout)` | R-1. Exact C1008 native audit cases | Zero failures and skips | 5 | 3 | false |
| CP-7 | final | tests/Antiphon.Tests -> bin-c2057-final-7/ | impact-rolling | `/*/*/(RollingVolumeRecycleCensusChurnTests*)\|(RollingVolumeRecycleScriptTests*)/*` | R-3. Rolling recycle scripts and census churn | Zero failures and skips | 2 | 15 | false |
| CP-8 | final | CP-7 | impact-retirement | `/*/*/(C0824BranchDeletionRecycleAuditTests*)\|(C1364BoundLandWaitProofTests*)\|(RetiredTempContainerHostTests*)\|(RetiredTempContainerScriptTests*)\|(VolumeRetirementApplyGateTests*)\|(VolumeRetirementCloseExemptionTests*)\|(VolumeRetirementHostProofTests*)\|(VolumeRetirementPublicationTests*)\|(VolumeRetirementReceiptTests*)\|(VolumeRetirementWrapperTests*)/*` | R-3. Related retirement and publication classes | Zero failures and skips | 10 | 17 | false |
| CP-3 | final | tests/Antiphon.Tests -> bin-c2057-final-3/ | git-encoding | `/*/*/CheckpointGitEncodingScriptTests/C1608_*` | R-1. C1608 Git decoding and configured Git witness | Zero failures and skips | 3 | 1 | false |
| CP-4 | final | tests/Antiphon.Tests -> bin-c2057-final-4/ | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)/*` | R-2, R-3. Policy, mechanical, platform, layering, lane and fixture guards | Zero failures and skips | 85 | 3 | false |
| CP-5 | final | tests/Antiphon.Tests -> bin-c2057-final-5/ | deploy-order | `/*/*/RemoteScriptContractTests/(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)` | R-3. Impacted deployment-order methods | Zero failures and skips | 5 | 1 | false |
| CP-6 | final | tests/Antiphon.Tests -> bin-c2057-final-6/ | windows-roster | `/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Roster_closure_matches_source_gates)\|(C1210_Guard_matches_native_platform)\|(C1210_Native_linux_entries_guard_before_work)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes)\|(C1210_Host_roster_matches_windows_shaped_observation)\|(C1226_Pin_inspection_methods_skip_off_linux)` | V-4, R-3. Windows roster closure and portable siblings | Zero failures and skips | 6 | 1 | false |
| CP-9 | final | CP-7 | impact-portable | `/*/*/(C1008HostFixtureSetupInsertionTests*)\|(CheckpointImportTests*)\|(ClaudeTokenRefreshOptInTests*)\|(CodexRootExecContractTests*)\|(CodexRunnerImageContractTests*)\|(DeployServer2ApiDefaultTests*)\|(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(GrokRunnerImageContractTests*)\|(HangfireLeaseDocumentationTests*)\|(HangfireStorageSelectionTests*)\|(HostJqPrerequisiteScriptTests*)\|(JqRunnerImageContractTests*)\|(ProcessSpawnLimitTests*)\|(RcBridgeProbePlatformTests*)\|(RollingProductionMountTests*)\|(RunnerPushCredentialDocsTests*)\|(SeedSmokeTargetingPackContractTests*)\|(Server2ProductionServerContractTests*)\|(Server2SidecarStandingAgentsTests*)\|(Server2StagingServerContractTests*)\|(ServerImagePushCredentialTests*)\|(ZombieCensusPlatformTests*)/*` | R-3. Remaining main-project portable and Linux class selectors | Zero failures and skips | 23 | 5 | false |
| CP-10 | final | tests/Antiphon.SessionRunner.Tests -> bin-c2057-final-runner/ | impact-runner | `/*/*/(C1818GcOwnerRobustnessTests*)\|(ProcessSpawnLimitTests*)/*` | R-3. GC owner and runner limiter | Zero failures and skips | 2 | 3 | false |
| CP-12 | final | CP-7 | impact-platform | `/*/*/C1008PlatformContractTests/(C1050_Guard_matches_native_platform)\|(C1050_Remote_entries_guard_before_work)\|(C1050_Rolling_entries_guard_before_work)\|(C1050_Audit_rejects_empty_or_incomplete_outcomes)` | R-3. Portable C1050 platform contracts | Zero failures and skips | 4 | 2 | false |
| CP-13 | final | CP-7 | impact-database | `/*/*/HangfirePostgreSqlStorageTests/*` | R-3. Impacted Hangfire storage integration | Zero failures and skips | 1 | 3 | false |
| CP-11 | final | tests/Antiphon.Agents.Pty.Tests -> bin-c2057-final-pty/ | impact-pty | `/*/*/(ProcessSpawnLimitTests*)/*` | R-3. Pty limiter | Zero failures and skips | 1 | 1 | true |

### Positive control candidates

The existing and new CRLF arguments share the method `Linked_worktree_requires_exact_content_for_explicit_crlf_checkout`. SourceLanding Mutation owns isolation and red/restore/green for any commissioned controls.

| ID | Break | Required red |
|---|---|---|
| PC-1 | Read declarations using `check-attr --cached` | Staged attributes falsely authorize the CRLF edit |
| PC-2 | Omit the common `info/attributes` guard | Common-local attributes falsely authorize the CRLF edit |
| PC-3 | Omit the linked-admin `info/attributes` guard | A CRLF mismatch with nonempty linked-local attributes reaches Ready |
| PC-4 | Omit the working-file size limit | The exact CRLF checkout one byte over 16 MiB reaches Ready |
| PC-5 | Remove the new roster entry | C1210 source closure reports the missing method identity |
