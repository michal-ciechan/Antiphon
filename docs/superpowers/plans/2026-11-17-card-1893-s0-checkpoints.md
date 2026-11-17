# CARD-1893 S0 checkpoints

Final verification for the base server2 runner `HistoryEnabled: "false"` stop-gap.
One serial run, no `--row-timeout`. CP-1 is the cold `Antiphon.Tests` build.
Impacted-classes rows for `29ba0c5002..` the S0 tip are included. No Windows-only filter.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S0 | tests/Antiphon.Tests -> bin-c1893s0/ | s0-hold | `/*/*/C1893S0RunnerHoldTests/*` | V-1 | all listed, 0 failed | 2 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | S0 | n/a | s0-driver | `ANTIPHON_LINUX_COMPILER_POLICY=0 dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-1} --nologo` | V-2 | exit 0, native output | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | S0 | CP-1 | s0-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | V-3 | all listed, 0 failed; C1210_Windows_linux_only_outcomes_are_exact skips on Linux | 90 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | S0 | CP-1 | s0-named | `/*/*/(TestTimingOperationLabelTests*)\|(ImpactedClassesSelectorTests*)/*` | V-4 | all listed, 0 failed | 30 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | S0 | CP-1 | s0-compose | `/*/*/(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(CodexRunnerImageContractTests*)\|(DindRunnerContractTests*)\|(Server2StagingServerContractTests*)\|(VerifyDindRunnerScriptTests*)/*` | V-5 | all listed, 0 failed | 100 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | S0 | CP-1 | s0-remote | `/*/*/RemoteScriptContractTests/*` | V-5 | all listed, 0 failed | 200 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | S0 | CP-1 | s0-jq | `/*/*/HostJqPrerequisiteScriptTests/*` | V-5 | all listed, 0 failed | 30 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | S0 | CP-1 | s0-recycle | `/*/*/(C0824BranchDeletionRecycleAuditTests*)\|(C1008HostFixtureSetupInsertionTests*)\|(C1008PlatformContractTests*)\|(C1364BoundLandWaitProofTests*)\|(CheckpointImportTests*)\|(RetiredTempContainerHostTests*)\|(RetiredTempContainerScriptTests*)\|(RollingProductionMountTests*)\|(RollingVolumeRecycleCensusChurnTests*)\|(RollingVolumeRecycleScriptTests*)\|(VolumeRetirementApplyGateTests*)\|(VolumeRetirementContentTests*)\|(VolumeRetirementReceiptTests*)\|(VolumeRetirementWrapperTests*)/*` | V-5 | all listed, 0 failed | 180 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | S0 | CP-1 | s0-spawn | `/*/*/ProcessSpawnLimitTests/*` | V-5 | all listed, 0 failed | 30 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | S0 | tests/Antiphon.SessionRunner.Tests -> bin-c1893s0-sr/ | s0-session-runner | `/*/*/(CustodyNamespaceBootstrapTests*)\|(HostStatsProbeParseTests*)\|(ProcessSpawnLimitTests*)/*` | V-5 | all listed, 0 failed | 15 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | S0 | tests/Antiphon.Agents.Pty.Tests -> bin-c1893s0-pty/ | s0-pty | `/*/*/ProcessSpawnLimitTests/*` | V-5 | all listed, 0 failed | 2 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
