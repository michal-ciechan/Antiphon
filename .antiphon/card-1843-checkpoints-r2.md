# CARD-1843 re-cut checkpoints, CP-3 amendment (task 3b0e3e9e)

Run 20261112-020429-7aee CP-3 executed 253/253 green but was roster-miss: SolutionShapeTests is only a
fixture literal in DerivedImpactIndexTests (no real class). CP-3b drops it; the other classes are unchanged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-3b | S1 | tests/Antiphon.Tests/Antiphon.Tests.csproj -> bin-c1843-b/ | impacted | `/*/*/(AdditiveMigrationRuleWitnessTests*)\|(AdvisoryChangePolicyTests*)\|(CheckpointManifestDocumentationTests*)\|(DockerStackDocumentationTests*)\|(ExpectedDurationSyncTests*)\|(GrokRunnerImageContractTests*)\|(PerTaskFinalUnitLaneWitnessTests*)\|(PipelineSpeedIntegrationTests*)\|(ProviderEndpointProjectionTests*)\|(RepositoryMutationLeaseTests*)\|(ReviewTestQualityChecklistWitnessTests*)\|(RunnerPushCredentialDocsTests*)\|(Server2StagingServerContractTests*)\|(StableBuildOutputDocumentationTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | 0 failed | 250 | 15 |
