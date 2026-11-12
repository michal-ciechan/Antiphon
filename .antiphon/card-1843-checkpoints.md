# CARD-1843 re-cut checkpoints (task 3b0e3e9e)

Re-cut of feat/card-task-3e96732b on master b02f110fb (which carries CARD-1830). Slice S1 is the
cherry-picked work plus the C1801 why-missed tuple update (944fff9b).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests/Antiphon.Tests.csproj -> bin-c1843-a/ | touched | `/*/*/(ImpactedClassesSelectorTests*)\|(DerivedImpactIndexTests*)\|(MergeHighRadiusSubsetTests*)/*` | V-1 | 0 failed | 70 | 30 |
| CP-2 | S1 | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | R-1 | 0 failed | 70 | 10 |
| CP-3 | S1 | CP-1 | impacted | `/*/*/(AdditiveMigrationRuleWitnessTests*)\|(AdvisoryChangePolicyTests*)\|(CheckpointManifestDocumentationTests*)\|(DockerStackDocumentationTests*)\|(ExpectedDurationSyncTests*)\|(GrokRunnerImageContractTests*)\|(PerTaskFinalUnitLaneWitnessTests*)\|(PipelineSpeedIntegrationTests*)\|(ProviderEndpointProjectionTests*)\|(RepositoryMutationLeaseTests*)\|(ReviewTestQualityChecklistWitnessTests*)\|(RunnerPushCredentialDocsTests*)\|(Server2StagingServerContractTests*)\|(SolutionShapeTests*)\|(StableBuildOutputDocumentationTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | 0 failed | 200 | 25 |
| CP-4 | S1 | tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj -> bin-c1843-sr/ | impacted-runner | `/*/*/C1677TempFamilyRegistryTests/*` | R-2 | 0 failed | 1 | 8 |
