# CARD-1893 S0 checkpoints

Final verification for the base server2 runner `HistoryEnabled: "false"` stop-gap.
One serial run, no `--row-timeout`. CP-1 is the cold `Antiphon.Tests` build.
Impacted-classes rows for `29ba0c5002..` the S0 tip are included. No Windows-only filter.
`RemoteScriptContractTests` and the recycle group are split: each whole filter exceeded the 45-minute derived row deadline with zero assertion failures.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S0 | tests/Antiphon.Tests -> bin-c1893s0b/ | s0-hold | `/*/*/C1893S0RunnerHoldTests/*` | V-1 | all listed, 0 failed | 2 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | S0 | n/a | s0-driver | `ANTIPHON_LINUX_COMPILER_POLICY=0 dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-1} --nologo` | V-2 | exit 0, native output | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | S0 | CP-1 | s0-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | V-3 | all listed, 0 failed; C1210_Windows_linux_only_outcomes_are_exact skips on Linux | 90 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | S0 | CP-1 | s0-named | `/*/*/(TestTimingOperationLabelTests*)\|(ImpactedClassesSelectorTests*)/*` | V-4 | all listed, 0 failed | 30 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | S0 | CP-1 | s0-compose | `/*/*/(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(CodexRunnerImageContractTests*)\|(DindRunnerContractTests*)\|(Server2StagingServerContractTests*)\|(VerifyDindRunnerScriptTests*)/*` | V-5 | all listed, 0 failed | 100 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | S0 | CP-1 | s0-remote-c11 | `/*/*/RemoteScriptContractTests/C11*` | V-5 | all listed, 0 failed | 60 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-12 | S0 | CP-1 | s0-remote-c10 | `/*/*/RemoteScriptContractTests/(C10*)\|(C12*)` | V-5 | all listed, 0 failed | 35 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-13 | S0 | CP-1 | s0-remote-rest-c | `/*/*/RemoteScriptContractTests/(C0*)\|(C8*)\|(C9*)\|(C13*)\|(C15*)\|(C16*)` | V-5 | all listed, 0 failed | 60 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-14 | S0 | CP-1 | s0-remote-named | `/*/*/RemoteScriptContractTests/(Child_project*)\|(Custody_*)\|(Deploy_parent*)\|(Every_case*)\|(Host_*)\|(Lane_detection*)\|(Linux_custody*)\|(Nested_lane*)\|(Persistent_restart*)\|(Retirement_*)\|(Scrub_covers*)\|(Smoke_deletes*)\|(Temp_*)\|(Testing_payload*)` | V-5 | all listed, 0 failed | 20 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | S0 | CP-1 | s0-jq | `/*/*/HostJqPrerequisiteScriptTests/*` | V-5 | all listed, 0 failed | 30 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | S0 | CP-1 | s0-recycle-script | `/*/*/(RollingProductionMountTests*)\|(RollingVolumeRecycleCensusChurnTests*)\|(RollingVolumeRecycleScriptTests*)/*` | V-5 | all listed, 0 failed | 40 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-16 | S0 | CP-1 | s0-recycle-volume | `/*/*/(VolumeRetirementApplyGateTests*)\|(VolumeRetirementContentTests*)\|(VolumeRetirementReceiptTests*)\|(VolumeRetirementWrapperTests*)/*` | V-5 | all listed, 0 failed | 60 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-17 | S0 | CP-1 | s0-recycle-rest | `/*/*/(C0824BranchDeletionRecycleAuditTests*)\|(C1008HostFixtureSetupInsertionTests*)\|(C1008PlatformContractTests*)\|(C1364BoundLandWaitProofTests*)\|(CheckpointImportTests*)\|(RetiredTempContainerHostTests*)\|(RetiredTempContainerScriptTests*)/*` | V-5 | all listed, 0 failed | 70 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | S0 | CP-1 | s0-spawn | `/*/*/ProcessSpawnLimitTests/*` | V-5 | all listed, 0 failed | 30 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-15 | S0 | CP-1 | s0-envelope | `/*/*/ReleaseImpactEnvelopeTests/*` | V-5 | all listed, 0 failed | 6 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | S0 | tests/Antiphon.SessionRunner.Tests -> bin-c1893s0b-sr/ | s0-session-runner | `/*/*/(CustodyNamespaceBootstrapTests*)\|(HostStatsProbeParseTests*)\|(ProcessSpawnLimitTests*)/*` | V-5 | all listed, 0 failed | 15 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | S0 | tests/Antiphon.Agents.Pty.Tests -> bin-c1893s0b-pty/ | s0-pty | `/*/*/ProcessSpawnLimitTests/*` | V-5 | all listed, 0 failed | 2 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
