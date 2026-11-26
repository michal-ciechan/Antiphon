# CARD-2082 class inventory

Source pin `771bc1a46afe5893894b050f4da47effb6c758e7`. The [detector](2026-11-26-card-2082-invariant-detector.md) examined 2925 tracked C# sources and 2300 declaring types with direct TUnit tests. 1134 types produced a signal. Four additional fixture controls with zero signals are included for comparison. Harness-only probes under tests/ and linked source under tests/Shared are part of the scan. The non-C# files under tests/ are fixture assets, probe programs or the standalone mutation driver in tests/card-file-privacy-controls.py.

The first two tables are the proposed migration workload. A class in the split table contributes its current-tree assertions and their focused controls. Product cases keep their owning fixtures. The last table retains lower-confidence candidates from the same scan, including helpers that accept a synthetic repository. Each row links the declaration. The detector emits all partial files, inputs and member locations. Tables show up to three representative path hints unless the input has been identified by hand. A literal in a fixture is a search hit, so candidate rows carry no automatic migration authority.

**Reading the timing columns.** Linux cells give observed passed/failed/skipped cases and their summed TRX seconds for the most recent matching row in the inspected clean-source, verified-build, successful Linux checkpoint reports. The receipt key maps to the run and commit below. A narrow row attests only its reported selection. Results from red reports discussed separately in the plan retain their original verdict. The historical Windows column shows serial seconds, node count and run identifier from entries explicitly marked `measured` in [nightly-class-costs.json](../../../scripts/lib/nightly-class-costs.json). The file contains other model prices which are excluded here. Its measured entries can include failures and different node counts. Columns B and F show Windows evidence from the requested b8cbfb24 and 1fe763a9 respectively. A dash means evidence unavailable, including tests introduced after a historical run. See the [plan](2026-11-26-card-2082-repository-invariants-plan.md) for the native group 113, 141 and 142 sources. Coverage, platform eligibility, whole-group wall and p95 require separate evidence.

## Proposed guard fixtures

80 source types.


### Antiphon.Agents.Pty.Tests / Antiphon.Agents.Pty.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [ConPtyEnvironmentIsolationGuardTests](../../../tests/Antiphon.Agents.Pty.Tests/ConPtyEnvironmentIsolationGuardTests.cs) | compiled Agents.Pty test assembly, child-process call sites, IL and isolation attributes | — | — | — | — |
| [LiveClaudeInventoryTests](../../../tests/Antiphon.Agents.Pty.Tests/LiveClaudeInventoryTests.cs) | tests/live-lanes.json and the compiled Agents.Pty listing | — | — | — | — |

### Antiphon.GitInProcess.Tests / Antiphon.Tests.Infrastructure

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [GitInProcessAssemblyTests](../../../tests/Antiphon.GitInProcess.Tests/GitInProcessAssemblyTests.cs) | tests/Antiphon.GitInProcess.Tests/Antiphon.GitInProcess.Tests.csproj, src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj, tests/Antiphon.Tests/TestHelpers/LibGit2ConfigIsolation.cs … | — | — | — | — |

### Antiphon.Messaging.Tests / Antiphon.Messaging.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [PublishWorkflowSanityTests](../../../tests/Antiphon.Messaging.Tests/PublishWorkflowSanityTests.cs) | .github/workflows/publish-nuget.yml | — | — | — | — |

### Antiphon.Resilience.Tests / Antiphon.Tests.Infrastructure

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [ResilienceAssemblyTests](../../../tests/Antiphon.Resilience.Tests/ResilienceAssemblyTests.cs) | tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj, src/Antiphon.Resilience/Antiphon.Resilience.csproj, tests/Antiphon.E2E/Fixtures/PipelineContractScenario.cs … | — | — | — | — |

### Antiphon.Server.Git.Tests / Antiphon.Tests.Infrastructure

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [ServerGitAssemblyTests](../../../tests/Antiphon.Server.Git.Tests/ServerGitAssemblyTests.cs) | src/Antiphon.Server.Git/Antiphon.Server.Git.csproj, server/Antiphon.Server.csproj, tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj … | — | — | — | — |

### Antiphon.SessionRunner.Tests / Antiphon.SessionRunner.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [WindowsShellGuardAuditTests](../../../tests/Antiphon.SessionRunner.Tests/WindowsShellGuardAuditTests.cs) | tests/Antiphon.SessionRunner.Tests/C1022BackendLaunchTests.cs, tests/Antiphon.SessionRunner.Tests/GrokRulesHttpAcceptanceTests.cs, tests/Antiphon.SessionRunner.Tests/RunnerCustodyTests.cs … | 3/0/0 · 0.494 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Application

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AdditiveMigrationRuleWitnessTests](../../../tests/Antiphon.Tests/Application/AdditiveMigrationRuleWitnessTests.cs) | docs/project-context.md, docs/testing-and-build.md, server/Bundles/stage-code.md … | — | — | — | — |
| [AgentContextContractTests](../../../tests/Antiphon.Tests/Application/AreaMapTests.cs) | AGENTS.md | — | — | — | — |
| [AreaMapContractTests](../../../tests/Antiphon.Tests/Application/AreaMapTests.cs) | FindRepoRoot, tracked path in Map | — | — | — | — |
| [BootStallDocumentationTests](../../../tests/Antiphon.Tests/Application/BootStallDocumentationTests.cs) | docs/session-runtime-invariants.md, docs/orchestration-loop.md, docs/agent-kinds.md … | — | — | — | — |
| [CardFilePrivacyDocumentationTests](../../../tests/Antiphon.Tests/Application/CardFilePrivacyDocumentationTests.cs) | .gitignore, server/Bundles/board-api.md, docs/card-file-privacy.md … | — | — | — | — |
| [CheckpointManifestDocumentationTests](../../../tests/Antiphon.Tests/Application/CheckpointManifestDocumentationTests.cs) | docs/testing-and-build.md, scripts/run-checkpoint.ps1, docs/orchestration-loop.md … | — | — | — | — |
| [CommitOnSettleDocumentationTests](../../../tests/Antiphon.Tests/Application/CommitOnSettleDocumentationTests.cs) | AGENTS.md, docs/antiphon-api.md, docs/orchestration-loop.md … | — | — | — | — |
| [D2AdvisoryDocumentationTests](../../../tests/Antiphon.Tests/Application/D2AdvisoryDocumentationTests.cs) | docs/orchestration-loop.md | — | — | — | — |
| [DispatchBaseWarningRosterTests](../../../tests/Antiphon.Tests/Application/DispatchBaseWarningRosterTests.cs) | docs/superpowers/plans/2026-10-11-card-1155-dispatch-warning-e2e-roster-plan.md, docs/superpowers/plans/2026-10-08-test-hardening-batch-1137-1134-1130-1126-1152-plan.md, tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs | — | — | — | — |
| [EfLambdaClockGuardTests](../../../tests/Antiphon.Tests/Application/EfLambdaClockGuardTests.cs) | server/**/*.cs EF expression and call sites | — | — | — | — |
| [Grok47RoutingNameCensusTests](../../../tests/Antiphon.Tests/Application/Grok47RoutingNameCensusTests.cs) | tests/Antiphon.Tests/Application/GrokLaunchArgsTests.cs, tests/Antiphon.Tests/Application/ModelAvailabilityCreateTests.cs, tests/Antiphon.Tests/Application/ApiErrorRecoveryServiceTests.cs | — | — | — | — |
| [KeepSixDocumentationTests](../../../tests/Antiphon.Tests/Application/KeepSixDocumentationTests.cs) | server/Bundles/orchestrator.md, scripts/lib/Antiphon.Nightly/KeepSix.cs, scripts/keep-six.ps1 … | — | — | — | — |
| [MechanicalPcContractTests](../../../tests/Antiphon.Tests/Application/MechanicalPcContractTests.cs) | docs/mechanical-pc-contract.md, docs/orchestration-loop.md, .claude/skills/antiphon-delegate/SKILL.md … | 11/0/0 · 0.982 [20261126-035349-6918/CP-G2](#linux-receipt-keys) | — | — | — |
| [MidTurnInterruptDocumentationTests](../../../tests/Antiphon.Tests/Application/MidTurnInterruptDocumentationTests.cs) | docs/session-runtime-invariants.md, docs/orchestration-loop.md | — | — | — | — |
| [OrchestratorBundleOwnershipTests](../../../tests/Antiphon.Tests/Application/OrchestratorBundleOwnershipTests.cs) | server/Bundles/orchestrator.md, docs/orchestration-loop.md | — | — | — | — |
| [OrchestratorInstructionsGuidanceTests](../../../tests/Antiphon.Tests/Application/OrchestratorInstructionsGuidanceTests.cs) | docs/antiphon-api.md, docs/ops-http.md, docs/orchestration-loop.md … | — | — | — | — |
| [PerTaskFinalUnitLaneWitnessTests](../../../tests/Antiphon.Tests/Application/PerTaskFinalUnitLaneWitnessTests.cs) | server/Bundles/stage-code.md, server/Bundles/stage-review.md, server/Bundles/orchestrator.md … | — | — | — | — |
| [PipelineDefinitionDocumentationTests](../../../tests/Antiphon.Tests/Application/PipelineDefinitionDocumentationTests.cs) | docs/antiphon-api.md, docs/ops-http.md, docs/agent-card-lifecycle.md … | — | — | — | — |
| [PipelineHandoffRouteDocumentationTests](../../../tests/Antiphon.Tests/Application/PipelineHandoffRouteDocumentationTests.cs) | docs/antiphon-api.md, docs/ops-http.md, server/Api/Endpoints/PipelineHandoffActionEndpoints.cs … | — | — | — | — |
| [PostLandMutationContractTests](../../../tests/Antiphon.Tests/Application/PostLandMutationContractTests.cs) | docs/orchestration-loop.md, .claude/skills/antiphon-delegate/SKILL.md, AGENTS.md … | — | — | — | — |
| [PostLandRetrospectiveContractTests](../../../tests/Antiphon.Tests/Application/PostLandRetrospectiveContractTests.cs) | .claude/skills/antiphon-orchestrator/SKILL.md, docs/orchestration-loop.md, AGENTS.md | — | — | — | — |
| [ProviderEndpointDocumentationTests](../../../tests/Antiphon.Tests/Application/ProviderEndpointDocumentationTests.cs) | docs/agent-credentials.md, docs/agent-kinds.md, docs/** | — | — | — | — |
| [PublishedRoutingDocumentationTests](../../../tests/Antiphon.Tests/Application/PublishedRoutingDocumentationTests.cs) | docs/orchestration-loop.md, docs/agent-kinds.md, docs/ai-agent-tui-configuration.md | — | — | — | — |
| [ReleaseBranchModelDocumentationTests](../../../tests/Antiphon.Tests/Application/ReleaseBranchModelDocumentationTests.cs) | docs/release-gates.md, AGENTS.md, docs/orchestration-loop.md … | — | — | — | — |
| [RepairSourceDocumentationTests](../../../tests/Antiphon.Tests/Application/RepairSourceDocumentationTests.cs) | docs/orchestration-loop.md, docs/ops-http.md, server/Bundles/orchestrator.md … | — | — | — | — |
| [ReviewTestQualityChecklistWitnessTests](../../../tests/Antiphon.Tests/Application/ReviewTestQualityChecklistWitnessTests.cs) | docs/testing-and-build.md, server/Bundles/stage-review.md, docs/orchestration-loop.md … | — | — | — | — |
| [RunnerBranchContractDocumentationTests](../../../tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs) | docs/orchestration-loop.md, docs/session-runtime-invariants.md, .claude/skills/antiphon-orchestrator/SKILL.md … | — | — | — | — |
| [RunnerDefaultGuidanceTests](../../../tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs) | server/Bundles/orchestrator.md, docs/superpowers/plans/2026-09-25-card-0710-task-platform-placement-plan.md, server/Bundles/stage-plan.md … | — | — | — | — |
| [RunnerReadSourcePinTests](../../../tests/Antiphon.Tests/Application/RunnerReadSourcePinTests.cs) | server/Application/Services/SessionMessageQueueService.cs, server/Application/Services/SessionMessageQueueService.Expectations.cs, server/Application/Services/SessionMessageQueueService.DispatchBrief.cs | — | — | — | — |
| [ScopedVerificationInstructionTests](../../../tests/Antiphon.Tests/Application/ScopedVerificationInstructionTests.cs) | docs/testing-and-build.md, docs/superpowers/plans/2026-09-11-card-0487-scoped-dispatch-testing-plan.md | — | — | — | — |
| [SequentialOverrideWordingTests](../../../tests/Antiphon.Tests/Application/SequentialOverrideWordingTests.cs) | docs/antiphon-api.md, scripts/lib/Antiphon.Nightly/DelegateCli.cs, server/Bundles/orchestrator.md | — | — | — | — |
| [SessionRunnerAbsenceEvidenceDocumentationTests](../../../tests/Antiphon.Tests/Application/SessionRunnerAbsenceEvidenceDocumentationTests.cs) | docs/session-runtime-invariants.md, docs/ops-http.md, docs/antiphon-api.md … | — | — | — | — |
| [StableBuildOutputDocumentationTests](../../../tests/Antiphon.Tests/Application/StableBuildOutputDocumentationTests.cs) | AGENTS.md, TESTING.md, docs/testing-and-build.md … | — | — | — | — |
| [StandingBootDocumentationTests](../../../tests/Antiphon.Tests/Application/StandingBootDocumentationTests.cs) | docs/session-runtime-invariants.md, docs/orchestration-loop.md, docs/agent-kinds.md … | — | — | — | — |
| [StandingPipelinePolicyDocumentationTests](../../../tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs) | AGENTS.md, docs/orchestration-loop.md, server/Bundles/orchestrator.md … | — | — | — | — |
| [TaskPlatformGuidanceTests](../../../tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs) | server/Bundles/stage-investigate.md, server/Bundles/stage-plan.md, server/Bundles/stage-test-design.md … | 5/0/0 · 0.480 [20261126-035349-6918/CP-G2](#linux-receipt-keys) | — | — | — |
| [WhyMissedWriteUpContractTests](../../../tests/Antiphon.Tests/Application/WhyMissedWriteUpContractTests.cs) | docs/orchestration-loop.md, docs/investigations/why-missed-template.md, .claude/skills/antiphon-rc-qualification/SKILL.md | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Checkpoints

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [BuildSlotClientClockGuardTests](../../../tests/Antiphon.Tests/Checkpoints/BuildSlotClientClockGuardTests.cs) | tests/** | — | — | — | — |
| [CheckpointRepeatDocumentationTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs) | docs/testing-and-build.md, docs/orchestration-loop.md, server/Bundles/stage-code.md … | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Infrastructure

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AppHostBrokerSourceGuardTests](../../../tests/Antiphon.Tests/Infrastructure/AppHostBrokerSourceGuardTests.cs) | Antiphon.AppHost/Program.cs, server/appsettings.json, src/Antiphon.Messaging.FakeGateway/appsettings.json … | — | — | — | — |
| [C1729LayeringGuardTests](../../../tests/Antiphon.Tests/Infrastructure/C1729LayeringGuardTests.cs) | server/**/*.cs, tests/Antiphon.Tests/Layering/{layers,allowlist}.txt and tracked server paths | 11/0/0 · 0.539 [20261126-074134-4c4f/CP-1](#linux-receipt-keys) | — | — | — |
| [Card1451PlanFoldTests](../../../tests/Antiphon.Tests/Infrastructure/Card1451PlanFoldTests.cs) | docs/superpowers/plans/2026-10-22-card-1432-repository-maintenance-plan.md, server/Infrastructure/Git/LandingGit.cs, tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs | — | — | — | — |
| [CheckpointToolProjectContractTests](../../../tests/Antiphon.Tests/Infrastructure/CheckpointToolProjectContractTests.cs) | tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj, src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj, src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj … | 4/0/0 · 0.061 [20261107-022238-5d4b/CP-GUARDS](#linux-receipt-keys) | — | — | — |
| [CodexRootExecContractTests](../../../tests/Antiphon.Tests/Infrastructure/CodexRootExecContractTests.cs) | docker/session-runner-grok/dind-entrypoint.sh, docs/docker-stack.md, scripts/lib/Antiphon.Nightly/PhoneHomeGrokVerify.cs | — | — | — | 20 / 4 / 8f055f87 |
| [DirectoryBuildRspTests](../../../tests/Antiphon.Tests/Infrastructure/DirectoryBuildRspTests.cs) | Directory.Build.rsp | — | — | — | — |
| [DockerStackDocumentationTests](../../../tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs) | docs/apphost-runbook.md, docs/bootstrap.md, docs/docker-stack.md … | 22/0/0 · 3.0 [20261125-205319-5b8a/CP-5](#linux-receipt-keys) | — | — | — |
| [DotnetToolManifestContractTests](../../../tests/Antiphon.Tests/Infrastructure/DotnetToolManifestContractTests.cs) | .config/dotnet-tools.json, server/Antiphon.Server.csproj | — | — | — | — |
| [E2ECopyOwnedPtyHostAppHostContractTests](../../../tests/Antiphon.Tests/Infrastructure/E2ECopyOwnedPtyHostAppHostContractTests.cs) | tests/Antiphon.E2E/Antiphon.E2E.csproj, src/Antiphon.PtyHost/Antiphon.PtyHost.csproj | — | — | — | — |
| [EvidencePolicyWorkflowTests](../../../tests/Antiphon.Tests/Infrastructure/EvidencePolicyWorkflowTests.cs) | .github/workflows/evidence-policy.yml, global.json | — | — | — | 7 / 4 / 8f055f87 |
| [GrokRunnerImageContractTests](../../../tests/Antiphon.Tests/Infrastructure/GrokRunnerImageContractTests.cs) | docker/session-runner-grok/Dockerfile, docs/docker-stack.md, docs/testing-and-build.md … | — | — | — | — |
| [JqRunnerImageContractTests](../../../tests/Antiphon.Tests/Infrastructure/JqRunnerImageContractTests.cs) | docker/session-runner-grok/Dockerfile, docs/docker-stack.md, docker/session-runner-grok/verify-codex-image.sh | 1/0/0 · 0.317 [20261122-183140-57a1/CP-1](#linux-receipt-keys) | — | — | — |
| [LandGraphAuditProjectInstanceTests](../../../tests/Antiphon.Tests/Infrastructure/LandGraphAuditProjectInstanceTests.cs) | tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj, tests/Antiphon.Tests/Antiphon.Tests.csproj | — | — | — | — |
| [PackageVersionPinContractTests](../../../tests/Antiphon.Tests/Infrastructure/PackageVersionPinContractTests.cs) | tracked project, props and targets package references | — | — | — | — |
| [PlanCheckpointClassSelectionGuardTests](../../../tests/Antiphon.Tests/Infrastructure/PlanCheckpointClassSelectionGuardTests.cs) | docs/superpowers/plans/*.md, declarations under tests/, tests/checkpoint-retired-selections.json | 2/0/0 · 43.9 [20261126-074134-4c4f/CP-1](#linux-receipt-keys) | — | — | — |
| [RunnerPushCredentialDocsTests](../../../tests/Antiphon.Tests/Infrastructure/RunnerPushCredentialDocsTests.cs) | docs/agent-credentials.md, scripts/refresh-server2-github-token.ps1, docs/docker-stack.md … | — | — | — | — |
| [SessionRunnerGrokLineEndingContractTests](../../../tests/Antiphon.Tests/Infrastructure/SessionRunnerGrokLineEndingContractTests.cs) | .gitattributes | — | — | — | — |
| [ShellFixtureLineEndingContractTests](../../../tests/Antiphon.Tests/Infrastructure/ShellFixtureLineEndingContractTests.cs) | scripts/fixtures/c1192-workspace-contract.sh, scripts/fixtures/c913-image-contract.sh, scripts/fixtures/c913-seed-contract.sh … | — | — | — | — |
| [TestFixtureCleanupGuardTests](../../../tests/Antiphon.Tests/Infrastructure/TestFixtureCleanupGuardTests.cs) | tests/Antiphon.E2E/LandDeliveryFixtureProbeTests.cs, tests/Antiphon.GitInProcess.Tests/LibGit2VsGitWorktreeListTests.cs, tests/Antiphon.SessionRunner.Tests/BlockedParkWireTests.cs … | 1/0/0 · 11.3 [20261126-035349-6918/CP-G2](#linux-receipt-keys) | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Scripts

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [C1514LandingSplitBudgetTests](../../../tests/Antiphon.Tests/Scripts/C1514LandingSplitBudgetTests.cs) | tests/Shared/TestClassificationGuardTests.cs, scripts/fixtures/nightly/c1584-e2e-source-roster.txt, scripts/fixtures/nightly/c1404-group10-listing.txt … | — | — | — | 33 / 4 / 8f055f87 |
| [LinuxOnlyExpectedSkipClosureTests](../../../tests/Antiphon.Tests/Scripts/LinuxOnlyExpectedSkipClosureTests.cs) | tests/Antiphon.Tests/Infrastructure/RcBridgeProbePlatformTests.cs, tests/Antiphon.Tests/Infrastructure/ZombieCensusPlatformTests.cs, tests/Antiphon.Tests/Scripts/PwshPrivateCacheTests.cs … | — | — | — | — |
| [NativeLinuxPolicyWitnessMutationTests](../../../tests/Antiphon.Tests/Scripts/NativeLinuxPolicyWitnessMutationTests.cs) | tests/test-execution-policy.json and compiled legacy exclusion digest | 12/0/0 · 246 [20261126-074134-4c4f/CP-1](#linux-receipt-keys) | — | — | — |
| [ScriptModelSpawnLaneTests](../../../tests/Antiphon.Tests/Scripts/ScriptModelSpawnLaneTests.cs) | tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs, tests/Antiphon.Tests/Scripts/RollingProductionMountTests.cs, tests/Antiphon.Tests/Scripts/RollingVolumeRecycleCensusChurnTests.cs … | — | — | — | — |
| [SelectorLineEndingAttributeTests](../../../tests/Antiphon.Tests/Scripts/SelectorLineEndingAttributeTests.cs) | scripts/lib/Antiphon.Nightly/TestSelection.cs, scripts/lib/Antiphon.Nightly/TestSelectionCatalog.cs, scripts/lib/Antiphon.Nightly/TestSelectionGit.cs … | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.TestHelpers

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [C0824TransportLimiterTests](../../../tests/Antiphon.Tests/TestHelpers/C0824TransportLimiterTests.cs) | .gitignore, docker-compose.dev.yml, docker-compose.runner-grok.yml … | — | — | — | — |
| [DelegationHarnessCensusTests](../../../tests/Antiphon.Tests/TestHelpers/DelegationHarnessCensusTests.cs) | tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs, tests/Antiphon.Tests/TestHelpers/DelegationTestServicesTests.cs, tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs … | — | — | — | 8 / 7 / 8f055f87 |
| [E2EArtifactPathGuardTests](../../../tests/Antiphon.Tests/TestHelpers/E2EArtifactPathGuardTests.cs) | tests/Antiphon.E2E/** | — | — | — | — |
| [ExpectedDurationSyncTests](../../../tests/Antiphon.Tests/TestHelpers/ExpectedDurationSyncTests.cs) | scripts/lib/nightly-class-costs.json, tests/Shared/ExpectedDurationRoster.cs, test declarations and docs | — | — | — | — |
| [LinkedTestHelperClosureTests](../../../tests/Antiphon.Tests/TestHelpers/LinkedTestHelperClosureTests.cs) | tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj, tests/Antiphon.Tests/TestHelpers/**, tests/Antiphon.Tests/** | — | — | — | — |
| [MergeArtifactGuardTests](../../../tests/Antiphon.Tests/TestHelpers/MergeArtifactGuardTests.cs) | tracked git ls-files paths | — | — | — | — |
| [NativeWindowsFixtureGateTests](../../../tests/Antiphon.Tests/TestHelpers/NativeWindowsFixtureGateTests.cs) | tests/Antiphon.Tests/Infrastructure/WorktreeLockDiagnosticsWindowsTests.cs, tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs, tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs | — | — | — | — |
| [TestLaneCategoryGuardTests](../../../tests/Antiphon.Tests/TestHelpers/TestLaneCategoryGuardTests.cs) | declaring project test sources and their Unit/Integration attributes | 1/0/0 · 3.9 [20261126-050312-e408/CP-2](#linux-receipt-keys) | — | — | 1 / 1 / 8f055f87 |

### Shared / Antiphon.TestSupport

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [NativeLinuxPolicyWitnessTests](../../../tests/Shared/NativeLinuxPolicyWitnessTests.cs) | compiled host metadata, tests/test-execution-policy.json, tests/Shared/NativeLinuxWindowsBaseline.json and marker source | SessionRunner.Tests 3/0/0 · 1.4 [20261107-093220-718b/CP-4](#linux-receipt-keys)<br>Tests 3/0/0 · 26.5 [20261126-050312-e408/CP-2](#linux-receipt-keys) | — | — | — |
| [TestClassificationGuardTests](../../../tests/Shared/TestClassificationGuardTests.cs) | own assembly metadata versus its checked-in slow-tests-allowlist.txt | SessionRunner.Tests 1/0/0 · 0.345 [20261107-093220-718b/CP-4](#linux-receipt-keys)<br>Tests 1/0/0 · 13.9 [20261126-074134-4c4f/CP-1](#linux-receipt-keys) | — | — | — |

## Fixtures with a current-tree assertion to extract or isolate

243 source types.


### Antiphon.Agents.Pty.Tests / Antiphon.Agents.Pty.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [ProcessSpawnLimitTests](../../../tests/Antiphon.Agents.Pty.Tests/ProcessSpawnLimitTests.cs) | own assembly types, limiter attributes and the expected process-spawning fixtures | — | — | — | — |

### Antiphon.MarkdownPdf.Tests / Antiphon.MarkdownPdf.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [FakeBrowserSolutionReleaseTests](../../../tests/Antiphon.MarkdownPdf.Tests/FakeBrowserSolutionReleaseTests.cs) | scripts/build-slot.ps1, tests/Antiphon.MarkdownPdf.FakeBrowser/**, tests/Antiphon.MarkdownPdf.Tests/** | — | — | — | — |

### Antiphon.Messaging.Tests / Antiphon.Messaging.Tests.Contract

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [ContractSchemaTests](../../../tests/Antiphon.Messaging.Tests/Contract/ContractSchemaTests.cs) | docs/messaging/contract/v1/*.json and generated messaging schemas | — | — | — | — |

### Antiphon.Messaging.Tests / Antiphon.Messaging.Tests.Gateway

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [LibrarySufficiencyTests](../../../tests/Antiphon.Messaging.Tests/Gateway/LibrarySufficiencyTests.cs) | src/Antiphon.Messaging.Service/appsettings.json, server/appsettings.json, src/Antiphon.Messaging.FakeGateway/Antiphon.Messaging.FakeGateway.csproj … | — | — | — | — |

### Antiphon.Resilience.Tests / Antiphon.Tests.Infrastructure.Resilience

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [ResilienceOptionsAndTelemetryTests](../../../tests/Antiphon.Resilience.Tests/ResilienceOptionsAndTelemetryTests.cs) | server/appsettings.json, server/Program.cs | — | — | — | — |

### Antiphon.SessionRunner.Tests / Antiphon.SessionRunner.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [C0824WindowsSlotHolderTests](../../../tests/Antiphon.SessionRunner.Tests/C0824WindowsSlotHolderTests.cs) | src/Antiphon.SessionRunner/WindowsWorkspaceSlotProcessProbe.cs | — | — | — | — |
| [C0824WorkspaceSlotTimeoutBudgetTests](../../../tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotTimeoutBudgetTests.cs) | compiled attribute roster, directory walk in C0824_BudgetTableCoversEveryCustodyCase | 20/0/0 · 0.038 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [C1677TempFamilyRegistryTests](../../../tests/Antiphon.SessionRunner.Tests/C1677TempFamilyRegistryTests.cs) | RepoRoot, tracked path in RepoRoot | — | — | — | — |
| [C1722GitTimeoutBudgetTests](../../../tests/Antiphon.SessionRunner.Tests/C1722GitTimeoutBudgetTests.cs) | .gitignore | 98/0/0 · 0.041 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [C1818GcOwnerRobustnessTests](../../../tests/Antiphon.SessionRunner.Tests/C1818GcOwnerRobustnessTests.cs) | src/Antiphon.SessionRunner/Program.cs, docs/session-runtime-invariants.md, docs/docker-stack.md | — | — | — | — |
| [CustodyNamespaceBootstrapTests](../../../tests/Antiphon.SessionRunner.Tests/CustodyNamespaceBootstrapTests.cs) | docker-compose.server2-runner.yml, docker-compose.server2-runner.temp.yml, docker-compose.runner-grok.yml … | 8/0/0 · 3.0 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [HostStatsProbeParseTests](../../../tests/Antiphon.SessionRunner.Tests/HostStatsProbeParseTests.cs) | docker-compose.server2-runner.yml | 11/0/0 · 0.322 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [HostStatsSamplerTests](../../../tests/Antiphon.SessionRunner.Tests/HostStatsSamplerTests.cs) | src/Antiphon.SessionRunner/HostStatsSamplerService.cs, src/Antiphon.SessionRunner/SamplerProcessQuery.cs | 5/0/0 · 0.100 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [HostTopConsumerTests](../../../tests/Antiphon.SessionRunner.Tests/HostTopConsumerTests.cs) | src/Antiphon.SessionRunner/HostProcessTable.cs | — | — | — | — |
| [ProcessSpawnLimitTests](../../../tests/Antiphon.SessionRunner.Tests/ProcessSpawnLimitTests.cs) | own assembly types, limiter attributes and the expected process-spawning fixtures | 2/0/0 · 0.181 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [PtyHostDeploymentGuardTests](../../../tests/Antiphon.SessionRunner.Tests/PtyHostDeploymentGuardTests.cs) | scripts/autostart-session-runner.ps1, scripts/lib/Antiphon.Nightly/SessionRunnerAutostart.cs, Antiphon.AppHost/Program.cs … | — | — | — | — |
| [StandingRunnerCwdTests](../../../tests/Antiphon.SessionRunner.Tests/StandingRunnerCwdTests.cs) | tests/Antiphon.SessionRunner.Tests/** | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Agents

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AgentRegistrySettingsTests](../../../tests/Antiphon.Tests/Agents/AgentRegistrySettingsTests.cs) | server/appsettings.json, appsettings.json.example | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.ApiKeys

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [ProviderEndpointProjectionTests](../../../tests/Antiphon.Tests/ApiKeys/ProviderEndpointProjectionTests.cs) | server/Application/Services/ProviderEndpointSettings.cs, server/Application/Services/ProviderEndpoint.cs, docs/agent-credentials.md … | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Application

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AgentReplyStyleTests](../../../tests/Antiphon.Tests/Application/AgentReplyStyleTests.cs) | server/Bundles/style-phone.md | — | — | — | — |
| [AgentTaskGetPollMetricsTests](../../../tests/Antiphon.Tests/Application/AgentTaskGetPollMetricsTests.cs) | server/Api/Endpoints/AgentTaskEndpoints.cs, server/Api/Endpoints/DiagnosticsEndpoints.cs, server/Program.cs | — | — | — | — |
| [BlockedTaskParkProjectionTests](../../../tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs) | docs/session-runtime-invariants.md, docs/ops-http.md, docs/antiphon-api.md … | — | — | — | — |
| [C0824DesktopLegacyReleaseSettingsTests](../../../tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseSettingsTests.cs) | server/appsettings.json | — | — | — | — |
| [C0824RefOnlySnapshotMigrationTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlySnapshotMigrationTests.cs) | compiled server migration IDs, CARD-0824 window and EF model snapshot | — | — | — | — |
| [C1514HostLoadMigrationTests](../../../tests/Antiphon.Tests/Application/C1514HostLoadMigrationTests.cs) | compiled server migration IDs, historical CARD-1514 window and EF model snapshot | — | — | — | — |
| [C1677RetentionSettingsTests](../../../tests/Antiphon.Tests/Application/C1677RetentionSettingsTests.cs) | server/appsettings.json, scripts/release-cut.ps1 | — | — | — | — |
| [C1744G2PlacementTests](../../../tests/Antiphon.Tests/Application/C1744G2PlacementTests.cs) | FindRepoRoot, RepoRoot, repoRoot in Entry | — | — | — | — |
| [Card1619ExitMigrationIdTests](../../../tests/Antiphon.Tests/Application/Card1619ExitMigrationIdTests.cs) | GetMigrations, assembly inventory in Migration_list_uses_the_rc_exit_signal_id | — | — | — | — |
| [CardCleanupSchedulingSchemaTests](../../../tests/Antiphon.Tests/Application/CardCleanupSchedulingSchemaTests.cs) | server/Migrations/20261012112137_AddCardCleanupScheduling.cs, server/appsettings.json | — | — | — | 19 / 1 / 8f055f87 |
| [CardWorktreeCleanupRecoveryTests](../../../tests/Antiphon.Tests/Application/CardWorktreeCleanupRecoveryTests.cs) | server/appsettings.json | — | — | — | 177 / 6 / 8f055f87 |
| [ChannelOutboundContractTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundContractTests.cs) | server/Antiphon.Server.csproj, tools/Antiphon.MarkdownPdf/Antiphon.MarkdownPdf.csproj, server/Application/Services/AgentWorkspaceProvisioner.cs … | — | — | — | — |
| [ChannelOutboundStorageTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundStorageTests.cs) | tests/Antiphon.Tests/Application/ChannelOutboundStorageTests.cs, tests/Antiphon.Tests/Fixtures/Card0418/combined.pdf, tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [CheckCompactionFactEquivalenceTests](../../../tests/Antiphon.Tests/Application/CheckCompactionFactEquivalenceTests.cs) | server/Application/Services/CheckCompactionContinuationService.cs, server/Application/Services/CompactionTranscriptFacts.cs | — | — | — | — |
| [CodexGpt62LadderTests](../../../tests/Antiphon.Tests/Application/CodexGpt62LadderTests.cs) | docs/agent-kinds.md, docker/session-runner-grok/Dockerfile | — | — | — | — |
| [DelegateScriptCapabilityTests](../../../tests/Antiphon.Tests/Application/DelegateScriptCapabilityTests.cs) | scripts/lib/Antiphon.Nightly/DelegateCli.cs, tests/test-execution-policy.json | — | — | — | 6 / 6 / 8f055f87 |
| [DelegationAllowedRootsFileTests](../../../tests/Antiphon.Tests/Application/DelegationAllowedRootsFileTests.cs) | server/appsettings.json | — | — | — | — |
| [DelegationCapabilityContractTests](../../../tests/Antiphon.Tests/Application/DelegationCapabilityContractTests.cs) | docs/ops-http.md, docs/agent-kinds.md, server/Bundles/board-api.md | — | — | — | — |
| [DelegationDispatchRecoveryBoundaryTests](../../../tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentEvidence.cs) | docs/session-runtime-invariants.md, server/Application/Settings/DelegationSettings.cs | — | — | — | — |
| [DispatchHoldLedgerTests](../../../tests/Antiphon.Tests/Application/DispatchHoldLedgerTests.cs) | docs/ops-http.md | — | — | — | — |
| [HerdrPlacementSettingsTests](../../../tests/Antiphon.Tests/Application/HerdrPlacementSettingsTests.cs) | server/Migrations/** | 9/0/0 · 124 [20261123-053028-07ea/CP-4](#linux-receipt-keys) | — | — | 21 / 9 / 79b742de |
| [InstructionBundleTests](../../../tests/Antiphon.Tests/Application/InstructionBundleTests.cs) | .claude/skills/antiphon-orchestrator/SKILL.md, .claude/skills/antiphon-delegate/SKILL.md, server/Application/Services/ModelLevelAliases.cs … | — | — | — | — |
| [InstructionFragmentTests](../../../tests/Antiphon.Tests/Application/InstructionFragmentTests.cs) | server/Bundles/Fragments/git-common.md | — | — | — | — |
| [LandGraphCommandTests](../../../tests/Antiphon.Tests/Application/LandGraphCommandTests.cs) | scripts/lib/Antiphon.Nightly/LandAuditTool.cs, scripts/lib/Antiphon.Nightly/LandGraphAuditRun.cs, scripts/lib/Antiphon.Nightly/LandGraphLandRefresh.cs … | — | — | — | — |
| [LandGraphEvaluationTests](../../../tests/Antiphon.Tests/Application/LandGraphEvaluationCanaryEntryTests.cs) | tools/Antiphon.LandGraphAudit.Core/LandGraphCanaryRemoval.cs, scripts/lib/Antiphon.Nightly/LandGraphAuditRun.cs, scripts/lib/Antiphon.Nightly/LandGraphLandRefresh.cs … | — | — | — | 826 / 104 / 79b742de |
| [LandGraphLandRefreshTests](../../../tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs) | server/Application/Services/LandVerificationBuildGraph.manifest.txt, server/Application/Services/LandVerificationBuildGraph.evaluation.txt, server/Application/Services/LandVerificationBuildGraph.pins.txt … | — | — | — | — |
| [LandNonCodeSkipTests](../../../tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs) | C1385_RegistryMatchesNarrowGraph reads tracked build definitions and land-graph records at HEAD | — | — | — | — |
| [LandingProtocolHarnessTests](../../../tests/Antiphon.Tests/Application/LandingProtocolHarnessTests.cs) | tests/Antiphon.Tests/Application/** | — | — | — | 94 / 18 / 79b742de |
| [LaunchModelArgumentAppenderTests](../../../tests/Antiphon.Tests/Application/LaunchModelArgumentAppenderTests.cs) | server/Application/Services/AgentControlService.cs, server/Application/Services/AgentTaskDispatcher.cs | — | — | — | — |
| [PipelineSpeedIntegrationTests](../../../tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.Admission.cs) | docs/agent-card-lifecycle.md, server/Application/Services/AgentTaskService.cs, docs/superpowers/plans/2026-10-15-card-1281-cheap-slices-checkpoints.md | — | — | — | 403 / 39 / 79b742de |
| [ReceiptScanDestinationTests](../../../tests/Antiphon.Tests/Application/ReceiptScanDestinationTests.cs) | docs/session-runtime-invariants.md | — | — | — | — |
| [ReleaseGateActivationTests](../../../tests/Antiphon.Tests/Application/ReleaseGateActivationTests.cs) | server/appsettings.json, server/Program.cs | — | — | — | 8 / 7 / 8f055f87 |
| [ReviewRoundsPolicyTests](../../../tests/Antiphon.Tests/Application/ReviewRoundsPolicyTests.cs) | docs/orchestration-loop.md, AGENTS.md | — | — | — | — |
| [RunnerAlarmWiringTests](../../../tests/Antiphon.Tests/Application/RunnerAlarmWiringTests.cs) | directory walk, tracked path in program_wires_the_loop_the_observers_and_the_default_exclusion | — | — | — | — |
| [SettlementSyncRecoveryTests](../../../tests/Antiphon.Tests/Application/SettlementSyncRecoveryTests.cs) | server/Application/Services/SettlementSyncRecoveryService.cs | — | — | — | 305 / 29 / 79b742de |
| [SiblingInspectionDeadlineWitnessTests](../../../tests/Antiphon.Tests/Application/SiblingInspectionDeadlineWitnessTests.cs) | server/Application/Settings/GitSettings.cs, server/Application/Services/AgentTaskLandService.cs | — | — | — | — |
| [SpecialistRoleContractTests](../../../tests/Antiphon.Tests/Application/SpecialistRoleContractTests.cs) | server/** | — | — | — | 4 / 2 / 8f055f87 |
| [SpecialistRoutingScriptTests](../../../tests/Antiphon.Tests/Application/SpecialistRoutingScriptTests.cs) | scripts/lib/Antiphon.Nightly/SpecialistRoutingCli.cs | — | — | — | 11 / 2 / 8f055f87 |
| [UnixDeliveryCeilingTests](../../../tests/Antiphon.Tests/Application/UnixDeliveryCeilingTests.cs) | docs/superpowers/plans/2026-11-09-card-1224-linux-delivery-envelope-receipt.md, docs/docker-stack.md, docs/session-runtime-invariants.md … | — | — | — | — |
| [WorktreeResidueScriptTests](../../../tests/Antiphon.Tests/Application/WorktreeResidueScriptTests.cs) | scripts/lib/Antiphon.Nightly/WorktreeResidue.cs | — | — | — | 1 / 1 / 8f055f87 |

### Antiphon.Tests / Antiphon.Tests.Checkpoints

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [CheckpointAdmissionContractTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointAdmissionContractTests.cs) | _bmad/_config/manifest.yaml | 4/0/0 · 5.8 [20261126-074134-4c4f/CP-3](#linux-receipt-keys) | — | — | 10 / 3 / 8f055f87 |
| [CheckpointCallerBudgetTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointCallerBudgetTests.cs) | scripts/test-live-lane-isolation.ps1, scripts/test-live-lane-evidence.ps1 | 9/0/0 · 3.1 [20261125-142218-704b/CP-3](#linux-receipt-keys) | — | — | — |
| [CheckpointReadStallWitnessTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointReadStallWitnessTests.cs) | src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs, scripts/lib/checkpoint-file-reader.ps1, src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs | — | — | — | — |
| [CheckpointTempUsageTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs) | assembly inventory, compiled attribute roster in full_roster_excludes_explicit_cases | — | — | — | 24 / 8 / 8f055f87 |
| [EmbeddedCheckpointReaderTests](../../../tests/Antiphon.Tests/Checkpoints/EmbeddedCheckpointReaderTests.cs) | scripts/run-checkpoint.ps1, scripts/lib/checkpoint-file-reader.ps1 | — | — | — | — |
| [OutputCleanupTests](../../../tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs) | docs/testing-and-build.md, tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs, tests/** … | 45/0/4 · 64.4 [20261125-145444-fd84/CP-4](#linux-receipt-keys) | — | — | 271 / 49 / 8f055f87 |
| [PlanCoverageBindingTests](../../../tests/Antiphon.Tests/Checkpoints/PlanCoverageBindingTests.cs) | seven historical plans, selected test sources, checklist values and linked Compile items | 2 failed · 76.878 + 113.077 at 2fd61b64<br>7/0/0 · 489 [20261126-074134-4c4f/CP-2](#linux-receipt-keys) | 2 failed, 48.712 + 84.433 s, g113 | — | 10 / 6 / 8f055f87 |
| [PlanCoverageGoldenTests](../../../tests/Antiphon.Tests/Checkpoints/PlanCoverageGoldenTests.cs) | docs/superpowers/plans/2026-09-30-card-0780-retire-temp-date-parse-plan.md, scripts/c590-real.ps1, tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/provenance.json … | — | — | — | 10 / 4 / 8f055f87 |
| [PlanCoveragePcTests](../../../tests/Antiphon.Tests/Checkpoints/PlanCoveragePcTests.cs) | tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Infrastructure

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AmServiceDeployReadinessTests](../../../tests/Antiphon.Tests/Infrastructure/AmServiceDeployReadinessTests.cs) | Dockerfile, tools/**, src/** | — | — | — | 87 / 6 / 8f055f87 |
| [C1744G1DesktopWinComposeTests](../../../tests/Antiphon.Tests/Infrastructure/C1744G1DesktopWinComposeTests.cs) | docker-compose.server2-server.yml, scripts/deploy-server2-server.ps1 | — | — | — | — |
| [CardFileRepositoryTryKillTests](../../../tests/Antiphon.Tests/Infrastructure/CardFileRepositoryTryKillTests.cs) | server/Infrastructure/Git/CardFileRepository.cs, server/Infrastructure/Git/OwnedChildMarker.cs | — | — | — | — |
| [CodexRunnerImageContractTests](../../../tests/Antiphon.Tests/Infrastructure/CodexRunnerImageContractTests.cs) | tests/Antiphon.Tests/Infrastructure/DockerStackDocuments.cs, docker/session-runner-grok/Dockerfile, docker/session-runner-grok/verify-codex-image.sh … | — | — | — | — |
| [CustodyHelperContractTests](../../../tests/Antiphon.Tests/Infrastructure/CustodyHelperContractTests.cs) | docker/session-runner-grok/antiphon-custody-placement.c, docker/session-runner-grok/dind-entrypoint.sh, docker/session-runner-grok/Dockerfile … | 11/0/0 · 0.334 [20261126-074134-4c4f/CP-5](#linux-receipt-keys) | — | — | — |
| [DbRateSamplerTests](../../../tests/Antiphon.Tests/Infrastructure/DbRateSamplerTests.cs) | server/Infrastructure/Data/PgStatStatementsSnapshot.sql | — | — | — | — |
| [DindRunnerContractTests](../../../tests/Antiphon.Tests/Infrastructure/DindRunnerContractTests.cs) | docker-compose.server2-runner.temp.yml, src/Antiphon.SessionRunner/RunnerWorkspaceService.cs, docker/session-runner-grok/github-credential.sh … | — | — | — | — |
| [DockerDeliveryObservationTests](../../../tests/Antiphon.Tests/Infrastructure/DockerDeliveryObservationTests.cs) | tests/Antiphon.DockerStack.Fixture/provision-observer.sql | — | — | — | — |
| [DockerStackContractTests](../../../tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs) | docker-compose.server2-runner.temp.yml, scripts/c590-remote.sh, Dockerfile … | 132/0/0 · 3.0 [20261125-205319-5b8a/CP-5](#linux-receipt-keys) | — | — | — |
| [GrokSha256PinTests](../../../tests/Antiphon.Tests/Infrastructure/GrokSha256PinTests.cs) | docker/session-runner-grok/Dockerfile | — | — | — | — |
| [HangfireLeaseDocumentationTests](../../../tests/Antiphon.Tests/Infrastructure/HangfireDurableStorageTests.cs) | server/Program.cs, docs/docker-stack.md | — | — | — | — |
| [HangfireStorageSelectionTests](../../../tests/Antiphon.Tests/Infrastructure/HangfireDurableStorageTests.cs) | server/appsettings.json, docker-compose.yml, Antiphon.AppHost/Program.cs … | — | — | — | — |
| [HostCleanupCheckpointAuthorityTests](../../../tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointAuthorityTests.cs) | _bmad/_config/manifest.yaml | — | — | — | — |
| [HostCleanupCheckpointExtractionTests](../../../tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointExtractionTests.cs) | src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj, tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj, tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj | — | — | — | — |
| [LogRetentionTests](../../../tests/Antiphon.Tests/Infrastructure/LogRetentionTests.cs) | server/Program.cs, src/Antiphon.SessionRunner/Program.cs, src/Antiphon.Messaging.FakeGateway/appsettings.json … | — | — | — | — |
| [OrchestratorInstructionsMigrationShapeTests](../../../tests/Antiphon.Tests/Infrastructure/OrchestratorInstructionsMigrationShapeTests.cs) | GetMigrations, HasPendingModelChanges in State_table_and_session_column_match_the_snapshot | — | — | — | 6 / 1 / 8f055f87 |
| [PostgresJitPolicyTestsSetup](../../../tests/Antiphon.Tests/Infrastructure/PostgresJitPolicyTests.cs) | server/appsettings.json, server/Program.cs, tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs … | — | — | — | — |
| [PostgresPoolConfigurationTests](../../../tests/Antiphon.Tests/Infrastructure/PostgresPoolConfigurationTests.cs) | server/appsettings.json, Antiphon.AppHost/appsettings.json, docker-compose.yml | — | — | — | — |
| [RcBridgeProbePlatformTests](../../../tests/Antiphon.Tests/Infrastructure/RcBridgeProbePlatformTests.cs) | docs/docker-stack.md, docs/agent-kinds.md, docs/session-runtime-invariants.md … | — | — | — | — |
| [RepositoryMutationLeaseTests](../../../tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs) | server/Infrastructure/Git/RepositoryChildJournal.cs, server/Infrastructure/Git/RepositoryChildJournalInspector.cs, tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs … | — | — | — | 171 / 22 / 8f055f87 |
| [Server2ProductionServerContractTests](../../../tests/Antiphon.Tests/Infrastructure/Server2ProductionServerContractTests.cs) | docker-compose.server2-server.yml, scripts/deploy-server2-server.ps1, scripts/restart-linux-server.ps1 … | — | — | — | — |
| [Server2SidecarStandingAgentsTests](../../../tests/Antiphon.Tests/Infrastructure/Server2SidecarStandingAgentsTests.cs) | docker-compose.server2-server.yml, docker-compose.server2-staging-server.yml, scripts/deploy-server2-server.ps1 … | — | — | — | — |
| [Server2StagingServerContractTests](../../../tests/Antiphon.Tests/Infrastructure/Server2StagingServerContractTests.cs) | docker-compose.server2-staging-server.yml, scripts/deploy-server2-staging-server.ps1, scripts/deploy-server2.ps1 … | — | — | — | — |
| [ServerGitProcessTests](../../../tests/Antiphon.Tests/Infrastructure/ServerGitProcessTests.cs) | server/Infrastructure/Git/LandingGit.cs, server/Infrastructure/Git/GitService.cs, server/Infrastructure/Git/WorktreeManager.cs … | — | — | — | — |
| [ServerImagePushCredentialTests](../../../tests/Antiphon.Tests/Infrastructure/ServerImagePushCredentialTests.cs) | docker-compose.server2-land-credential.yml, scripts/lib/Antiphon.Nightly/ServerRepositoryMirrors.cs, docker/server/gitconfig … | — | — | — | — |
| [SlowTestRegistryMergeTests](../../../tests/Antiphon.Tests/Infrastructure/SlowTestRegistryMergeTests.cs) | tests/Antiphon.Tests/slow-tests-allowlist.txt, .gitattributes, tests/Antiphon.SessionRunner.Tests/slow-tests-allowlist.txt … | 5/0/0 · 52.4 [20261126-050312-e408/CP-2](#linux-receipt-keys) | — | — | 39 / 5 / 8f055f87 |
| [SourceRevisionStampContractTests](../../../tests/Antiphon.Tests/Infrastructure/SourceRevisionStampContractTests.cs) | Directory.Build.props, Directory.Build.targets, scripts/run-daemon.ps1 … | — | — | — | — |
| [ZombieCensusPlatformTests](../../../tests/Antiphon.Tests/Infrastructure/ZombieCensusPlatformTests.cs) | docs/bootstrap.md, docs/docker-stack.md | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [ProcessSpawnLimitTests](../../../tests/Antiphon.Tests/ProcessSpawnLimitTests.cs) | assembly-local spawn limiter attributes, expected type lists and the main-project checkpoint tool source census | 57/0/0 · 13.9 [20261126-035349-6918/CP-G2](#linux-receipt-keys) | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Scripts

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AgentContextCheckTests](../../../tests/Antiphon.Tests/Scripts/AgentContextCheckTests.cs) | AGENTS.md, scripts/lib/Antiphon.Nightly/AgentContextCheck.cs, scripts/lib/Antiphon.Nightly/BootstrapCheck.cs | — | — | — | — |
| [AntiphonLogsTests](../../../tests/Antiphon.Tests/Scripts/AntiphonLogsTests.cs) | docs/logs.md, scripts/lib/Antiphon.Nightly/AntiphonLogs.cs | — | — | — | — |
| [AutostartInstallTests](../../../tests/Antiphon.Tests/Scripts/AutostartInstallTests.cs) | scripts/autostart-session-runner.ps1, scripts/autostart-apphost.ps1, scripts/watchdog-apphost.ps1 … | — | — | — | — |
| [AwayDigestSendTests](../../../tests/Antiphon.Tests/Scripts/AwayDigestSendTests.cs) | server/Api/Endpoints/DigestEndpoints.cs, scripts/lib/Antiphon.Nightly/AwayDigestSend.cs, scripts/release-cut.ps1 | — | — | — | — |
| [BootstrapCheckTests](../../../tests/Antiphon.Tests/Scripts/BootstrapCheckTests.cs) | docker-compose.dev.yml, client/src/App.test.tsx, client/index.html … | 28/0/0 · 2.7 [20261125-142218-704b/CP-4](#linux-receipt-keys) | — | — | — |
| [BuildSlotScriptTests](../../../tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs) | scripts/build-slot.ps1, scripts/lib/build-driver-environment.ps1, scripts/fixtures/c589-slot-shim.ps1 … | — | — | — | — |
| [C1008PlatformContractTests](../../../tests/Antiphon.Tests/Scripts/C1008PlatformContractTests.cs) | tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs, tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs | — | — | — | — |
| [C1744G3PhoneHomeSupervisionTests](../../../tests/Antiphon.Tests/Scripts/C1744G3PhoneHomeSupervisionTests.cs) | scripts/lib/Antiphon.Nightly/WindowsPhoneHome.cs, scripts/restart-apphost.ps1, scripts/lib/Antiphon.Nightly/AppHostRestart.cs … | — | — | — | — |
| [C1960WorkspaceSlotsScriptTests](../../../tests/Antiphon.Tests/Scripts/C1960WorkspaceSlotsScriptTests.cs) | scripts/lib/Antiphon.Nightly/WorkspaceSlots.cs | — | — | — | — |
| [C487HarnessRootDefaultTests](../../../tests/Antiphon.Tests/Scripts/C487HarnessRootDefaultTests.cs) | scripts/lib/** | — | — | — | — |
| [CapabilityScriptTests](../../../tests/Antiphon.Tests/Scripts/CapabilityScriptTests.cs) | tests/test-execution-policy.json, scripts/lib/Antiphon.Nightly/CapabilityCli.cs | — | — | — | 2 / 2 / 8f055f87 |
| [Card0490BootDiskTests](../../../tests/Antiphon.Tests/Scripts/Card0490BootDiskTests.cs) | tests/fixtures/card0490-linux/assets.lock.json, docker-compose.dev.yml, docker-compose.runner-grok.yml … | — | — | — | — |
| [CardCommandTests](../../../tests/Antiphon.Tests/Scripts/CardCommandTests.cs) | scripts/lib/Antiphon.Nightly/CardCli.cs | — | — | — | — |
| [ChangeAwareReleaseTests](../../../tests/Antiphon.Tests/Scripts/ChangeAwareReleaseTests.cs) | client/src/App.tsx, tests/Antiphon.Tests/Agents/CodexResponseAnalyzerTests.cs, scripts/release-cut.ps1 … | — | — | — | 569 / 3 / 8f055f87 |
| [CheckpointTaskScriptTests](../../../tests/Antiphon.Tests/Scripts/CheckpointTaskScriptTests.cs) | scripts/lib/Antiphon.Nightly/CheckpointTask.cs | — | — | — | — |
| [ClassCostMeasurementTests](../../../tests/Antiphon.Tests/Scripts/ClassCostMeasurementTests.cs) | scripts/lib/nightly-class-costs.json, scripts/lib/Antiphon.Nightly/ClassCostRefresh.cs, scripts/release-cut.ps1 | — | — | — | — |
| [ClaudeTokenRefreshOptInTests](../../../tests/Antiphon.Tests/Scripts/ClaudeTokenRefreshOptInTests.cs) | scripts/c590-real.ps1, scripts/verify-docker-stack.ps1, scripts/c590-remote.sh … | — | — | — | — |
| [ClientModeTests](../../../tests/Antiphon.Tests/Scripts/ClientModeTests.cs) | scripts/lib/**, docker-compose.dev.yml, docker-compose.runner-grok.yml … | — | — | — | — |
| [CodexTestResidueTests](../../../tests/Antiphon.Tests/Scripts/CodexTestResidueTests.cs) | scripts/lib/Antiphon.Nightly/CodexTestResidue.cs | — | — | — | — |
| [CommitOnSettleScriptTests](../../../tests/Antiphon.Tests/Scripts/CommitOnSettleScriptTests.cs) | AGENTS.md, scripts/** | — | — | — | 9 / 11 / 8f055f87 |
| [ComplexityChainScriptTests](../../../tests/Antiphon.Tests/Scripts/ComplexityChainScriptTests.cs) | scripts/lib/Antiphon.Nightly/ComplexityChainCli.cs, scripts/lib/Antiphon.Nightly/StagePinMigration.cs, scripts/lib/Antiphon.Nightly/OrchestratorWorkspace.cs … | — | — | — | 24 / 17 / 8f055f87 |
| [DaemonBuildCheckTests](../../../tests/Antiphon.Tests/Scripts/DaemonBuildCheckTests.cs) | src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj, scripts/lib/Antiphon.Nightly/DaemonBuildCheck.cs, scripts/release-cut.ps1 | — | — | — | — |
| [DeployAmServiceTests](../../../tests/Antiphon.Tests/Scripts/DeployAmServiceTests.cs) | server/appsettings.json, src/Antiphon.Messaging.Service/appsettings.json, Dockerfile … | — | — | — | — |
| [DeployLocalTests](../../../tests/Antiphon.Tests/Scripts/DeployLocalTests.cs) | server/Migrations/AppDbContextModelSnapshot.cs, server/Migrations/** | 28/0/0 · 2.6 [20261125-142218-704b/CP-4](#linux-receipt-keys) | — | — | — |
| [DerivedImpactIndexTests](../../../tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs) | tests/derived-impact-index.json, tracked code and project graph, tests/impacted-classes.json, OS overrides | 27/0/0 · 138 [20261126-074134-4c4f/CP-1](#linux-receipt-keys) | — | — | — |
| [DispatchConcurrencyScriptTests](../../../tests/Antiphon.Tests/Scripts/DispatchConcurrencyScriptTests.cs) | scripts/lib/Antiphon.Nightly/DispatchConcurrencyCli.cs | — | — | — | 16 / 6 / 8f055f87 |
| [DistillerCommandTests](../../../tests/Antiphon.Tests/Scripts/DistillerCommandTests.cs) | scripts/lib/Antiphon.Nightly/DistillerLedger.cs | — | — | — | — |
| [DockerStackVerifyTests](../../../tests/Antiphon.Tests/Scripts/DockerStackVerifyTests.cs) | scripts/c590-real.ps1, tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [ExpectedPlatformSkipRosterTests](../../../tests/Antiphon.Tests/Scripts/ExpectedPlatformSkipRosterTests.cs) | tests/expected-platform-skips.json and its source method identities | 2/0/0 · 4.0 [20261126-074134-4c4f/CP-5](#linux-receipt-keys) | — | — | — |
| [GithubSyncScriptTests](../../../tests/Antiphon.Tests/Scripts/GithubSyncScriptTests.cs) | scripts/lib/Antiphon.Nightly/GithubSync.cs, scripts/github-sync.ps1 | — | — | — | — |
| [HangfireDashboardCommandTests](../../../tests/Antiphon.Tests/Scripts/HangfireDashboardCommandTests.cs) | server/Api/Endpoints/OperatorEndpoints.cs, scripts/lib/Antiphon.Nightly/HangfireDashboard.cs | — | — | — | — |
| [HerdrPaneScriptTests](../../../tests/Antiphon.Tests/Scripts/HerdrPaneScriptTests.cs) | scripts/lib/Antiphon.Nightly/HerdrPane.cs | — | — | — | 19 / 10 / 8f055f87 |
| [HostJqFixtureClosureTests](../../../tests/Antiphon.Tests/Scripts/HostJqFixtureClosureTests.cs) | scripts/deploy-server2.ps1, scripts/c590-real.ps1, scripts/c1008-host-proof.ps1 … | — | — | — | — |
| [HostJqPrerequisiteScriptTests](../../../tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs) | scripts/server2-host-jq.sh, scripts/fixtures/c1008-recycle-cases.json, scripts/fixtures/c994-production-compose-model.mjs … | 1/0/0 · 95.4 [20261106-225046-362a/CP-BASE](#linux-receipt-keys) | — | — | — |
| [ImpactIndexRegenerationTests](../../../tests/Antiphon.Tests/Scripts/ImpactIndexRegenerationTests.cs) | scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj | — | — | — | — |
| [ImpactedClassesCensusTests](../../../tests/Antiphon.Tests/Scripts/ImpactedClassesCensusTests.cs) | tests/derived-impact-index.json, tests/impacted-classes.json, tests/** … | 9/0/0 · 1.5 [20261125-145444-fd84/CP-4](#linux-receipt-keys) | — | — | — |
| [ImpactedClassesSelectorTests](../../../tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs) | scripts/restart-apphost.ps1, scripts/lib/windows-phone-home-env.ps1, server/Application/Exceptions/InputDeliveryUncertainException.cs … | — | — | — | — |
| [IncrementalSelectionSoundnessTests](../../../tests/Antiphon.Tests/Scripts/IncrementalSelectionSoundnessTests.cs) | tests/fixtures/incremental-selection-soundness.json, RC owner maps and both project graphs | 4/0/0 · 17.0 [20261126-074134-4c4f/CP-2](#linux-receipt-keys) | owner-map method failed, g142 | — | — |
| [KeepSixCommandTests](../../../tests/Antiphon.Tests/Scripts/KeepSixCommandTests.cs) | scripts/lib/Antiphon.Nightly/KeepSix.cs | — | — | — | — |
| [LinuxServerRestartGateTests](../../../tests/Antiphon.Tests/Scripts/LinuxServerRestartGateTests.cs) | scripts/restart-apphost.ps1, scripts/autostart-apphost.ps1, Antiphon.AppHost/Program.cs … | — | — | — | — |
| [LiveLaneEvidenceTests](../../../tests/Antiphon.Tests/Scripts/LiveLaneEvidenceTests.cs) | scripts/test-live-lane-evidence.ps1, tests/Antiphon.Tests/Agents/ClaudeDialogCanaryDiagnostics.cs, tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs … | — | — | — | — |
| [LiveLaneInventoryTests](../../../tests/Antiphon.Tests/Scripts/LiveLaneInventoryTests.cs) | tests/live-lanes.json, tests/test-execution-policy.json, compiled discovery and tracked test gate sites | 2 gate methods failed at 0cfc4ac1 | 2 gate methods passed | same 2 failed, g141 | — |
| [LiveLaneRunnerTests](../../../tests/Antiphon.Tests/Scripts/LiveLaneRunnerTests.cs) | tests/live-lane-checkpoints/live-claude-core-windows.yaml and eight protected inputs in scripts/test-live-lane-run.ps1 | 1/0/0 · 15.6 [20261125-205319-5b8a/CP-7](#linux-receipt-keys) | — | — | — |
| [MergeHighRadiusSubsetTests](../../../tests/Antiphon.Tests/Scripts/MergeHighRadiusSubsetTests.cs) | docs/orchestration-loop.md, docs/testing-and-build.md, server/Infrastructure/Git/ServerGitIdentity.cs | — | — | — | — |
| [ModelAvailabilityScriptTests](../../../tests/Antiphon.Tests/Scripts/ModelAvailabilityScriptTests.cs) | scripts/lib/Antiphon.Nightly/ModelAvailabilityCli.cs | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Scripts.Nightly

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AntiphonCliLauncherTests](../../../tests/Antiphon.Tests/Scripts/Nightly/AntiphonCliLauncherTests.cs) | scripts/lib/antiphon-cli.ps1, scripts/nightly-health.ps1, scripts/nightly-run.ps1 … | — | — | — | — |
| [BuildSlotLauncherTests](../../../tests/Antiphon.Tests/Scripts/Nightly/BuildSlotLauncherTests.cs) | scripts/build-slot.ps1, scripts/lib/antiphon-cli.ps1, scripts/** | — | — | — | — |
| [CheckpointLauncherTests](../../../tests/Antiphon.Tests/Scripts/Nightly/CheckpointLauncherTests.cs) | scripts/run-checkpoint.ps1, scripts/validate-checkpoint-receipt.ps1, scripts/lib/antiphon-cli.ps1 … | — | — | — | — |
| [CheckpointToolCommandTests](../../../tests/Antiphon.Tests/Scripts/Nightly/CheckpointToolCommandTests.cs) | tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj, tools/Antiphon.Checkpoints/Program.cs, Directory.Build.props … | 4/0/0 · 0.737 [20261125-145444-fd84/CP-4](#linux-receipt-keys) | — | — | — |
| [LiveLaneCensusTests](../../../tests/Antiphon.Tests/Scripts/Nightly/LiveLaneCensusTests.cs) | tests/test-execution-policy.json, scripts/lib/nightly-common.ps1, scripts/lib/Antiphon.Nightly/NightlyPolicy.cs … | — | — | — | — |
| [LiveLaneGateScanTests](../../../tests/Antiphon.Tests/Scripts/Nightly/LiveLaneGateScanTests.cs) | tracked test declarations and tests/live-lanes.json | — | — | — | — |
| [NightlyChunkRunnerTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyChunkRunnerTests.cs) | tests/test-execution-policy.json, scripts/lib/Antiphon.Nightly/NightlySuiteRunner.cs, scripts/lib/Antiphon.Nightly/NativeProcessOwner.cs … | — | — | — | — |
| [NightlyHealthTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyHealthTests.cs) | scripts/windmill/antiphon-nightly-readiness.json, scripts/windmill/antiphon-nightly-readiness.schedule.json, scripts/windmill/** | — | — | — | — |
| [NightlyReportTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyReportTests.cs) | scripts/fixtures/nightly/tunit-fail-tail.log, scripts/fixtures/nightly/vitest-fail-tail.log, scripts/lib/Antiphon.Nightly/NightlyReport.cs … | — | — | — | — |
| [NightlyTestsPlanTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyTestsPlanTests.cs) | scripts/lib/nightly-class-costs.json, scripts/release-cut.ps1 | — | — | — | — |
| [NightlyTestsRunTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyTestsRunTests.cs) | tests/test-execution-policy.json, scripts/fixtures/nightly/c487-probe/discovery.diag, scripts/fixtures/nightly/c487-probe/execution.diag … | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Scripts

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [NightlyCoreGoldenTests](../../../tests/Antiphon.Tests/Scripts/NightlyCoreGoldenTests.cs) | scripts/lib/nightly-class-costs.json, scripts/fixtures/nightly/stopped-seed/groups-planned-antiphon.json, .gitignore … | — | — | — | — |
| [NightlyNativeOwnershipTests](../../../tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs) | RepoRoot, tracked path in C1045_ClearedEntriesAreAbsent | 1/0/10 · 0.263 [20261126-074134-4c4f/CP-4](#linux-receipt-keys) | — | — | 41 / 9 / 8f055f87 |
| [NightlyPolicyTests](../../../tests/Antiphon.Tests/Scripts/NightlyPolicyTests.cs) | tests/test-selection-policy.json, tests/Antiphon.Tests/Antiphon.Tests.csproj, tests/Antiphon.E2E/Antiphon.E2E.csproj … | — | — | — | — |
| [NightlyScriptsTests](../../../tests/Antiphon.Tests/Scripts/NightlyScriptsTests.cs) | .gitattributes, scripts/nightly-run.ps1, scripts/nightly-health.ps1 … | — | — | — | — |
| [NightlySummaryWitnessTests](../../../tests/Antiphon.Tests/Scripts/NightlySummaryWitnessTests.cs) | scripts/lib/Antiphon.Nightly/NightlyTests.cs, scripts/lib/Antiphon.Nightly/NightlyRunSummary.cs | — | — | — | — |
| [NightlyVerificationContractTests](../../../tests/Antiphon.Tests/Scripts/NightlyVerificationContractTests.C545.cs) | scripts/nightly-watchdog/antiphon-nightly-watchdog.service.template, scripts/nightly-watchdog/qual.example.json, src/Antiphon.NightlyWatchdog/Antiphon.NightlyWatchdog.csproj … | — | — | — | 45 / 38 / 8f055f87 |
| [OrchestrationWatcherTests](../../../tests/Antiphon.Tests/Scripts/OrchestrationWatcherTests.cs) | tests/Antiphon.Tests/Scripts/Fixtures/orchestration-synthetic.trx, scripts/lib/Antiphon.Nightly/TrxFailures.cs, scripts/lib/Antiphon.Nightly/WatchTasks.cs … | — | — | — | — |
| [OsWitnessCheckTests](../../../tests/Antiphon.Tests/Scripts/OsWitnessCheckTests.cs) | docs/testing-and-build.md, scripts/lib/Antiphon.Nightly/OsWitnessCheck.cs, .gitignore … | — | — | — | — |
| [PipelineDefinitionScriptTests](../../../tests/Antiphon.Tests/Scripts/PipelineDefinitionScriptTests.cs) | scripts/lib/Antiphon.Nightly/PipelineDefinitionCli.cs | — | — | — | — |
| [PostLandEarlyWarningTests](../../../tests/Antiphon.Tests/Scripts/PostLandEarlyWarningTests.cs) | tests/post-land-unit-shards.json, docs/testing-and-build.md, docs/orchestration-loop.md … | — | — | — | — |
| [ProcessSpawnTimeoutPolicyTests](../../../tests/Antiphon.Tests/Scripts/ProcessSpawnTimeoutPolicyTests.cs) | RepoRoot, tracked path in ReadPortfolio | — | — | — | 17 / 2 / 8f055f87 |
| [ProjectCommandTests](../../../tests/Antiphon.Tests/Scripts/ProjectCommandTests.cs) | scripts/lib/Antiphon.Nightly/ProjectCli.cs | — | — | — | — |
| [PtyHostReaperTests](../../../tests/Antiphon.Tests/Scripts/PtyHostReaperTests.cs) | scripts/lib/Antiphon.Nightly/PtyHostReaper.cs, docs/testing-and-build.md, docs/ops-http.md | 15/0/0 · 6.4 [20261125-142218-704b/CP-4](#linux-receipt-keys) | — | — | — |
| [PwshPrivateCacheTests](../../../tests/Antiphon.Tests/Scripts/PwshPrivateCacheTests.cs) | tests/Antiphon.Tests/Scripts/PwshPrivateCacheTests.cs, tests/Antiphon.Tests/Scripts/ScriptHarnessLinuxOwnershipTests.cs, tests/Antiphon.SessionRunner.Tests/C1793SessionPwshCacheTests.cs | — | — | — | — |
| [QualificationCheckoutSourceTests](../../../tests/Antiphon.Tests/Scripts/QualificationCheckoutSourceTests.cs) | .gitignore | — | — | — | — |
| [QualificationGraphTests](../../../tests/Antiphon.Tests/Scripts/QualificationGraphTests.cs) | scripts/fixtures/qualification-graph/s1/execute.valid.json, scripts/fixtures/qualification-graph/s1/catalog.json, scripts/fixtures/qualification-graph/s1/catalog.sha256 … | — | — | — | — |
| [QualificationStopTests](../../../tests/Antiphon.Tests/Scripts/QualificationStopTests.cs) | scripts/lib/Antiphon.Nightly/QualificationStop.cs, docs/release-gates.md | — | — | — | — |
| [QualificationSupervisorTests](../../../tests/Antiphon.Tests/Scripts/QualificationSupervisorTests.cs) | scripts/lib/Antiphon.Nightly/RcQualificationRecords.cs, scripts/lib/Antiphon.Nightly/QualificationSupervisor.cs, scripts/lib/Antiphon.Nightly/QualificationStop.cs … | — | — | — | — |
| [QualificationTimingTests](../../../tests/Antiphon.Tests/Scripts/QualificationTimingTests.Runs.cs) | scripts/lib/Antiphon.Nightly/ReleaseCut.cs, scripts/release-cut.ps1, scripts/fixtures/nightly/tests-regression/selection-attempt.json … | — | — | — | — |
| [RcCoverageAdmissionTests](../../../tests/Antiphon.Tests/Scripts/RcCoverageAdmissionTests.cs) | tests/Antiphon.PtyHost.Tests/LinuxCustodyReleaseHostTests.cs, tests/Antiphon.Agents.Pty.Tests/Card1685LinuxCustodyRunnerTests.cs, tests/Antiphon.Agents.Pty.Tests/LinuxCgroupContainmentTests.cs … | — | — | — | — |
| [RcDirtyLaunchOrderTests](../../../tests/Antiphon.Tests/Scripts/RcDirtyLaunchOrderTests.cs) | scripts/release-cut.ps1, scripts/nightly-run.ps1, scripts/lib/Antiphon.Nightly/ReleaseCut.cs … | — | — | — | — |
| [RcForcedFullTests](../../../tests/Antiphon.Tests/Scripts/RcForcedFullTests.cs) | global.json, scripts/lib/rc-trusted-projects.json, scripts/release-cut.ps1 | — | — | — | — |
| [RcFullTimingContractTests](../../../tests/Antiphon.Tests/Scripts/RcFullTimingContractTests.cs) | scripts/lib/nightly-class-costs.json, scripts/fixtures/nightly/stopped-seed/groups-planned-antiphon.json, scripts/fixtures/nightly/stopped-seed/groups-planned-e2e.json … | — | — | — | — |
| [RcFullWatchdogHarnessTests](../../../tests/Antiphon.Tests/Scripts/RcFullWatchdogHarnessTests.cs) | scripts/lib/Antiphon.Nightly/QualificationHeartbeat.cs, scripts/fixtures/nightly/stopped-seed/groups-planned-antiphon.json, scripts/release-cut.ps1 | — | — | — | — |
| [RcFullWatchdogTests](../../../tests/Antiphon.Tests/Scripts/RcFullWatchdogTests.cs) | scripts/lib/Antiphon.Nightly/NightlyChunkRunner.cs, scripts/lib/Antiphon.Nightly/QualificationHeartbeat.cs, scripts/release-cut.ps1 | — | — | — | — |
| [RcIncrementalShadowTests](../../../tests/Antiphon.Tests/Scripts/RcIncrementalShadowTests.cs) | scripts/release-cut.ps1, scripts/nightly-run.ps1, scripts/** | — | — | — | — |
| [RcInputOwnersMapTests](../../../tests/Antiphon.Tests/Scripts/RcInputOwnersMapTests.cs) | tests/Antiphon.Tests/Antiphon.Tests.csproj, tests/Antiphon.E2E/Antiphon.E2E.csproj, tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj … | — | — | — | — |
| [RcQualificationStatusTests](../../../tests/Antiphon.Tests/Scripts/RcQualificationStatusTests.cs) | scripts/lib/Antiphon.Nightly/RcQualificationRecords.cs, scripts/lib/Antiphon.Nightly/RcQualificationStatus.cs, scripts/lib/Antiphon.Scripts/RcQualificationStatusCommand.cs … | — | — | — | 9 / 3 / 8f055f87 |
| [RcRosterRepairTests](../../../tests/Antiphon.Tests/Scripts/RcRosterRepairTests.cs) | scripts/lib/nightly-class-costs.json, scripts/release-cut.ps1 | — | — | — | 6 / 3 / 8f055f87 |
| [RcRunEvidenceTests](../../../tests/Antiphon.Tests/Scripts/RcRunEvidenceTests.cs) | scripts/lib/Antiphon.Nightly/NightlyTests.cs, scripts/lib/Antiphon.Nightly/NightlyRun.cs, scripts/lib/Antiphon.Nightly/NightlyChunkRunner.cs … | — | — | — | — |
| [RcStoppedRunSeedTests](../../../tests/Antiphon.Tests/Scripts/RcStoppedRunSeedTests.cs) | scripts/lib/Antiphon.Nightly/NightlySeedCommands.cs, scripts/fixtures/nightly/stopped-seed/owner.json, scripts/nightly-run.ps1 … | — | — | — | — |
| [RefreshGithubTokenScriptTests](../../../tests/Antiphon.Tests/Scripts/RefreshGithubTokenScriptTests.cs) | docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md, scripts/refresh-server2-github-token.ps1, docs/** | — | — | — | 143 / 3 / 8f055f87 |
| [ReleaseAuthorityTests](../../../tests/Antiphon.Tests/Scripts/ReleaseAuthorityTests.cs) | tests/test-execution-policy.json, scripts/release-cut.ps1 | — | — | — | — |
| [ReleaseCutTests](../../../tests/Antiphon.Tests/Scripts/ReleaseCutTests.cs) | scripts/release-cut.ps1, scripts/nightly-run.ps1, scripts/fixtures/nightly/tests-regression/selection-attempt.json … | — | — | — | — |
| [ReleaseGateAuthorityContractTests](../../../tests/Antiphon.Tests/Scripts/ReleaseGateAuthorityContractTests.cs) | scripts/fixtures/c599-policy-v2.json, scripts/release-cut.ps1, tests/test-execution-policy.json | — | — | — | 34 / 3 / 8f055f87 |
| [ReleaseGateE2EEvidenceTests](../../../tests/Antiphon.Tests/Scripts/ReleaseGateE2EEvidenceTests.cs) | scripts/lib/Antiphon.Nightly/NightlySuiteRunner.cs, client/index.html, tests/Antiphon.E2E/Antiphon.E2E.csproj … | — | — | — | 5 / 3 / 8f055f87 |
| [ReleaseGateFollowupTests](../../../tests/Antiphon.Tests/Scripts/ReleaseGateFollowupTests.cs) | scripts/nightly-run.ps1, scripts/fixtures/nightly/tests-regression/selection-attempt.json, scripts/windmill/antiphon-release-candidates.json … | — | — | — | 14 / 2 / 8f055f87 |
| [ReleaseGatePlatformTests](../../../tests/Antiphon.Tests/Scripts/ReleaseGatePlatformTests.Census.cs) | tests/test-selection-policy.json, tests/test-execution-policy.json, tests/Shared/NativeLinuxPolicyWitness.cs … | 2/0/0 · 2.8 [20261125-205319-5b8a/CP-6](#linux-receipt-keys) | — | — | 31 / 3 / 8f055f87 |
| [ReleaseGatePolicyTests](../../../tests/Antiphon.Tests/Scripts/ReleaseGatePolicyTests.cs) | scripts/release-cut.ps1, tests/Antiphon.E2E/Antiphon.E2E.csproj, tests/Antiphon.Tests/Antiphon.Tests.csproj | — | — | — | 11 / 6 / 8f055f87 |
| [ReleaseGatePublicationAuthorityTests](../../../tests/Antiphon.Tests/Scripts/ReleaseGatePublicationAuthorityTests.cs) | scripts/fixtures/c599-policy-v2.json, scripts/release-cut.ps1, tests/test-execution-policy.json | — | — | — | — |
| [ReleaseGateRegistrationTests](../../../tests/Antiphon.Tests/Scripts/ReleaseGateRegistrationTests.cs) | scripts/windmill/antiphon-nightly-tests.json, scripts/windmill/antiphon-release-candidates.json, scripts/windmill/antiphon-release-candidates.schedule.json … | — | — | — | 15 / 4 / 8f055f87 |
| [ReleaseGateRunTests](../../../tests/Antiphon.Tests/Scripts/ReleaseGateRunTests.cs) | scripts/fixtures/nightly/tests-regression/selection-attempt.json, scripts/nightly-run.ps1, global.json … | — | — | — | 30 / 9 / 8f055f87 |
| [ReleaseGateStatusTests](../../../tests/Antiphon.Tests/Scripts/ReleaseGateStatusTests.cs) | scripts/lib/Antiphon.Nightly/ReleasePublish.cs, scripts/lib/Antiphon.Nightly/ReleaseGateHost.cs, scripts/release-cut.ps1 … | — | — | — | 10 / 2 / 8f055f87 |
| [ReleaseImpactEnvelopeTests](../../../tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs) | scripts/lib/Antiphon.Nightly/ImpactedClasses.cs, scripts/lib/Antiphon.Nightly/ImpactedClassesInputs.cs, scripts/lib/Antiphon.Nightly/ReleaseImpactEvidence.cs … | — | — | — | — |
| [ReleaseImpactRuntimeTests](../../../tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeTests.cs) | tests/selector-runtime-inputs.json, tests/Antiphon.Tests/Antiphon.Tests.csproj, tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs … | — | — | — | — |
| [ReleaseImpactScriptTests](../../../tests/Antiphon.Tests/Scripts/ReleaseImpactScriptTests.cs) | tests/Antiphon.Tests/Antiphon.Tests.csproj, scripts/lib/Antiphon.Scripts/Program.cs, tests/Antiphon.Tests/Scripts/LiveLaneFingerprintTests.cs … | — | — | — | — |
| [ReleaseSelectionHopSeedTests](../../../tests/Antiphon.Tests/Scripts/ReleaseSelectionHopSeedTests.cs) | scripts/fixtures/nightly/tests-regression/selection-attempt.json, global.json, scripts/lib/Antiphon.Nightly/TestSelection.cs … | — | — | — | — |
| [RemoteScriptContractTests](../../../tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs) | scripts/fixtures/c1192-workspace-contract.sh, docker-compose.server2-runner.yml, scripts/fixtures/c913-receipts.ps1 … | — | — | — | — |
| [RepositoryChildRecoveryCommandTests](../../../tests/Antiphon.Tests/Scripts/RepositoryChildRecoveryCommandTests.cs) | scripts/lib/Antiphon.Nightly/RepositoryChildRecovery.cs | — | — | — | — |
| [RetiredTempContainerScriptTests](../../../tests/Antiphon.Tests/Scripts/RetiredTempContainerScriptTests.cs) | scripts/c590-command.ps1, scripts/c590-real.ps1, scripts/c1008-host-proof.ps1 … | — | — | — | — |
| [RollingVolumeRecycleCensusChurnTests](../../../tests/Antiphon.Tests/Scripts/RollingVolumeRecycleCensusChurnTests.cs) | scripts/fixtures/c1008-recycle-real-cases.mjs, scripts/c590-remote.sh | — | — | — | — |
| [RollingVolumeRecycleScriptTests](../../../tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs) | scripts/fixtures/c1008-recycle-cases.json, scripts/test-deploy-server2-jq.ps1, scripts/deploy-server2.ps1 … | — | — | — | — |
| [RoutingPinScriptTests](../../../tests/Antiphon.Tests/Scripts/RoutingPinScriptTests.cs) | server/Bundles/stage-mutation.md, scripts/** | — | — | — | 13 / 16 / 8f055f87 |
| [RunnerDrainScriptTests](../../../tests/Antiphon.Tests/Scripts/RunnerDrainScriptTests.cs) | scripts/lib/Antiphon.Nightly/RunnerDrain.cs | — | — | — | — |
| [RunnerLinuxCompilerPolicyTests](../../../tests/Antiphon.Tests/Scripts/RunnerLinuxCompilerPolicyTests.cs) | scripts/build-slot.ps1, scripts/lib/antiphon-cli.ps1, scripts/fixtures/c1906-compiler-policy-compose.mjs … | — | — | — | — |
| [RunnerRestartHealthTests](../../../tests/Antiphon.Tests/Scripts/RunnerRestartHealthTests.cs) | RepoRoot in Documented_arguments_execute_the_expected_mode | — | — | — | — |
| [RunnerSlotsCommandTests](../../../tests/Antiphon.Tests/Scripts/RunnerSlotsCommandTests.cs) | scripts/lib/Antiphon.Nightly/RunnerSlots.cs | — | — | — | — |
| [ScheduleCommandTests](../../../tests/Antiphon.Tests/Scripts/ScheduleCommandTests.cs) | scripts/lib/Antiphon.Nightly/ScheduleCli.cs | — | — | — | — |
| [ScriptHarnessProcessContractTests](../../../tests/Antiphon.Tests/Scripts/ScriptHarnessProcessContractTests.cs) | tests/Antiphon.ScriptHarnessHost/HarnessRoot.cs | — | — | — | — |
| [SeedSmokeTargetingPackContractTests](../../../tests/Antiphon.Tests/Scripts/SeedSmokeTargetingPackContractTests.cs) | scripts/c590-remote.sh, docker/session-runner-grok/verify-codex-image.sh, docker/session-runner-grok/Dockerfile | — | — | — | — |
| [ServerDockerInputsTests](../../../tests/Antiphon.Tests/Scripts/ServerDockerInputsTests.cs) | docker/tests/Dockerfile.dockerignore, .dockerignore, .github/workflows/ci.yml … | — | — | — | — |
| [StageValueReportTests](../../../tests/Antiphon.Tests/Scripts/StageValueReportTests.cs) | scripts/lib/Antiphon.Nightly/StageValueReport.cs | — | — | — | — |
| [TestDataPruneCommandTests](../../../tests/Antiphon.Tests/Scripts/TestDataPruneCommandTests.cs) | scripts/lib/Antiphon.Nightly/TestDataPrune.cs | — | — | — | — |
| [TestSelectionCatalogTests](../../../tests/Antiphon.Tests/Scripts/TestSelectionCatalogTests.cs) | tests/test-selection-policy.json, scripts/fixtures/nightly/c1375-probe/Probe.csproj, tests/Antiphon.TestTiming.Probe/Antiphon.TestTiming.Probe.csproj … | — | — | — | 11 / 3 / 8f055f87 |
| [TestSelectionDependencyTests](../../../tests/Antiphon.Tests/Scripts/TestSelectionDependencyTests.cs) | src/Antiphon.Agents.Pty/PtyAgentRunner.cs, src/Antiphon.Agents.Pty/ClaudeDetectors.cs, tests/Antiphon.Messaging.Tests/FakeSlack/FakeSlackServer.cs … | — | — | — | 202 / 3 / 8f055f87 |
| [TestSelectionTests](../../../tests/Antiphon.Tests/Scripts/TestSelectionTests.cs) | client/src/App.tsx, tests/Antiphon.Tests/Agents/CodexResponseAnalyzerTests.cs, Directory.Build.props … | — | — | — | 248 / 8 / 8f055f87 |
| [TestTraceSummaryTests](../../../tests/Antiphon.Tests/Scripts/TestTraceSummaryTests.cs) | tests/Shared/TestTrace.cs, scripts/lib/Antiphon.Nightly/TestTraceSummary.cs, scripts/release-cut.ps1 | — | — | — | — |
| [ToolchainFloorWarningTests](../../../tests/Antiphon.Tests/Scripts/ToolchainFloorWarningTests.cs) | docker/git-minimum-version.sh, global.json, scripts/lib/Antiphon.Nightly/NightlyTests.cs … | — | — | — | — |
| [VerifyDindRunnerScriptTests](../../../tests/Antiphon.Tests/Scripts/VerifyDindRunnerScriptTests.cs) | docker-compose.server2-runner.yml, docker/session-runner-grok/Dockerfile, docker/session-runner-grok/antiphon-custody-common.sh … | — | — | — | — |
| [VerifyPhoneHomeGrokScriptTests](../../../tests/Antiphon.Tests/Scripts/VerifyPhoneHomeGrokScriptTests.cs) | docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile, docker-compose.dev.yml … | — | — | — | 6 / 6 / 8f055f87 |
| [VolumeRetirementApplyGateTests](../../../tests/Antiphon.Tests/Scripts/VolumeRetirementApplyGateTests.cs) | scripts/c590-remote.sh, scripts/c590-real.ps1, scripts/deploy-server2.ps1 … | — | — | — | — |
| [VolumeRetirementHostProofTests](../../../tests/Antiphon.Tests/Scripts/VolumeRetirementHostProofTests.cs) | scripts/c590-command.ps1, scripts/c1609-host-proof.ps1, scripts/c590-real.ps1 … | — | — | — | — |
| [VolumeRetirementReceiptTests](../../../tests/Antiphon.Tests/Scripts/VolumeRetirementReceiptTests.cs) | scripts/c1008-retirement.sh, scripts/fixtures/c1609-manual-cases.mjs, scripts/c590-remote.sh … | — | — | — | — |
| [VolumeRetirementWrapperTests](../../../tests/Antiphon.Tests/Scripts/VolumeRetirementWrapperTests.cs) | scripts/deploy-server2.ps1, scripts/fixtures/c1609-wrapper-v2-cases.mjs, scripts/retire-runner-volumes.ps1 … | — | — | — | — |
| [WatchQualTests](../../../tests/Antiphon.Tests/Scripts/WatchQualTests.cs) | scripts/lib/Antiphon.Nightly/RcQualificationStatus.cs, scripts/lib/Antiphon.Nightly/RcQualificationRecords.cs, scripts/lib/Antiphon.Scripts/RcQualificationStatusCommand.cs … | — | — | — | — |
| [WatchedTestRunTests](../../../tests/Antiphon.Tests/Scripts/WatchedTestRunTests.cs) | scripts/lib/Antiphon.Nightly/WatchedTestRun.cs, docs/testing-and-build.md | — | — | — | — |
| [WindowsUnitPlatformAdmissionTests](../../../tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs) | WindowsUnitSkipRoster, test sources, platform attributes and tests/test-execution-policy.json | 7/0/1 · 128 [20261126-074134-4c4f/CP-1](#linux-receipt-keys) | — | — | 33 / 5 / 8f055f87 |
| [ZombieAgentReaperTests](../../../tests/Antiphon.Tests/Scripts/ZombieAgentReaperTests.cs) | scripts/lib/Antiphon.Nightly/ZombieAgentReaper.cs | 12/0/0 · 3.3 [20261125-142218-704b/CP-4](#linux-receipt-keys) | — | — | — |

### Antiphon.Tests / Antiphon.Tests.TestHelpers

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [DescendantLeakScopeTests](../../../tests/Antiphon.Tests/TestHelpers/DescendantLeakScopeTests.cs) | tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs | — | — | — | — |
| [EntityScalarSnapshotConsumerGuardTests](../../../tests/Antiphon.Tests/TestHelpers/EntityScalarSnapshotConsumerGuardTests.cs) | tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs, tests/Antiphon.Tests/Application/ReviewEvidenceRecoveryTests.cs, tests/Antiphon.Tests/Application/CardFilePrivacySyncAcceptanceTests.cs … | — | — | — | 4 / 5 / 8f055f87 |
| [OwnedTempLeakTests](../../../tests/Antiphon.Tests/TestHelpers/OwnedTempLeakTests.cs) | scripts/lib/Antiphon.Nightly/ReviewScratch.cs, scripts/** | — | — | — | — |
| [RcManifestIntegrationTests](../../../tests/Antiphon.Tests/TestHelpers/RcManifestIntegrationTests.cs) | scripts/lib/Antiphon.Scripts/SummarizeTestPhasesCommand.cs, tests/test-execution-policy.json, tests/Antiphon.MarkdownPdf.Tests/Antiphon.MarkdownPdf.Tests.csproj … | — | — | — | — |
| [SlowTestTripwireTests](../../../tests/Antiphon.Tests/TestHelpers/SlowTestTripwireTests.cs) | tests/Antiphon.Tests/slow-tests-allowlist.txt | 2/0/0 · 0.115 [20261126-050312-e408/CP-2](#linux-receipt-keys) | — | — | — |
| [TestAppHostPathTests](../../../tests/Antiphon.Tests/TestHelpers/TestAppHostPathTests.cs) | tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs | — | — | — | — |
| [TestClassificationPolicyTests](../../../tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs) | compiled classification attributes and checked-in project slow registries | 45/0/0 · 87.8 [20261126-074134-4c4f/CP-1](#linux-receipt-keys) | — | — | 74 / 39 / 8f055f87 |
| [TestDbFixtureLifecycleTests](../../../tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycleTests.cs) | assembly inventory, directory walk, tracked path in Worker_mode_list_names_every_owned_child_worker | — | — | — | — |
| [TestDurationTripwireTests](../../../tests/Antiphon.Tests/TestHelpers/TestDurationTripwireTests.cs) | scripts/lib/**, scripts/**, scripts/release-cut.ps1 | — | — | — | 17 / 16 / 8f055f87 |
| [TestMigrationFingerprintTests](../../../tests/Antiphon.Tests/TestHelpers/TestMigrationFingerprintTests.cs) | GetMigrations, MigrationIds, assembly inventory in Migration_ids_are_the_ones_ef_applies_in_order | — | — | — | — |
| [TestPhaseSummaryTests](../../../tests/Antiphon.Tests/TestHelpers/TestPhaseSummaryTests.cs) | scripts/release-cut.ps1 | — | — | — | 20 / 7 / 8f055f87 |
| [TestTimingOperationLabelTests](../../../tests/Antiphon.Tests/TestHelpers/TestTimingOperationLabelTests.cs) | tests/**/*.cs timing call sites and the kept-operation label allowlist | 2/0/0 · 0.641 [20261123-070247-05e4/CP-12](#linux-receipt-keys) | — | — | — |

## Product and fixture controls that stay with their owners

55 source types.


### Antiphon.Agents.Pty.Tests / Antiphon.Agents.Pty.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [ClaudeGoldenReplayTests](../../../tests/Antiphon.Agents.Pty.Tests/ClaudeGoldenReplayTests.cs) | tests/Antiphon.Agents.Pty.Tests/golden/claude-turn.golden.txt | — | — | — | — |
| [CodexV0160StartupTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexV0160StartupTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-queue-draft.txt, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-idle.txt, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-running.txt | — | — | — | — |
| [LinuxCgroupContainmentTests](../../../tests/Antiphon.Agents.Pty.Tests/LinuxCgroupContainmentTests.cs) | docker/session-runner-grok/antiphon-custody-common.sh, docker/session-runner-grok/antiphon-custody-enter.sh | — | — | — | — |
| [LiveClaudeIsolationWiringTests](../../../tests/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests.cs) | tests/Antiphon.Agents.Pty.Tests/probes/settings-source-witness.mjs, .claude/settings.json, AGENTS.md … | — | — | — | — |

### Antiphon.E2E / Antiphon.E2E

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AgentE2ETests](../../../tests/Antiphon.E2E/AgentE2ETests.cs) | docs/screenshots/agents/01-agent-queue-foundation.png, client/index.html | — | — | — | — |
| [AgentTaskLandDeliveryE2ETests](../../../tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs) | FindRepositoryRoot, RepoPath, directory walk in C467_V26_HardCrashAfterQueueInsertReusesRow | — | — | — | — |
| [ContractSnapshotTests](../../../tests/Antiphon.E2E/ContractSnapshotTests.cs) | client/src/test/fixtures/contract/agent-files.json, client/src/test/fixtures/contract/review-threads.json, client/src/test/fixtures/contract/file-content-work.json … | — | — | — | — |
| [OutputDistillationCanaryGuardTests](../../../tests/Antiphon.E2E/OutputDistillationCanaryGuardTests.cs) | private fixture | — | — | — | — |

### Antiphon.GitInProcess.Tests / Antiphon.Tests.Infrastructure

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [LibGit2VsGitWorkingTreeVerdictTests](../../../tests/Antiphon.GitInProcess.Tests/LibGit2VsGitWorkingTreeVerdictTests.cs) | .gitignore, .gitattributes | — | — | — | — |

### Antiphon.MarkdownPdf.Tests / Antiphon.MarkdownPdf.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [MarkdownPdfRealBrowserTests](../../../tests/Antiphon.MarkdownPdf.Tests/MarkdownPdfRealBrowserTests.cs) | docs/features/012-review-engine/design.md, tests/Antiphon.Tests/Fixtures/Card0418/manifest.json, tests/Antiphon.Tests/Fixtures/Card0418/combined.pdf … | — | — | — | — |

### Antiphon.Messaging.Tests / Antiphon.Messaging.Tests.Client

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [KafkaOutboundPayloadTests](../../../tests/Antiphon.Messaging.Tests/Client/KafkaOutboundPayloadTests.cs) | docs/features/012-review-engine/design.md, tests/Antiphon.Tests/Fixtures/Card0418/combined.pdf | — | — | — | — |

### Antiphon.Messaging.Tests / Antiphon.Messaging.Tests.Contract

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [WireContractTests](../../../tests/Antiphon.Messaging.Tests/Contract/WireContractTests.cs) | tests/Antiphon.Messaging.Tests/Fixtures/live-channel-message.json, tests/Antiphon.Messaging.Tests/Fixtures/live-channel-reply.json | — | — | — | — |

### Antiphon.SessionRunner.Tests / Antiphon.SessionRunner.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [C1677RetentionInventoryTests](../../../tests/Antiphon.SessionRunner.Tests/C1677RetentionInventoryTests.cs) | directory walk in Tree | 3/0/0 · 0.568 [20261106-075233-9edf/CP-2](#linux-receipt-keys) | — | — | — |
| [HerdrGkpLaunchGuardTests](../../../tests/Antiphon.SessionRunner.Tests/HerdrGkpLaunchGuardTests.cs) | private fixture | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Agents

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [TranscriptNormalizerTests](../../../tests/Antiphon.Tests/Agents/TranscriptNormalizerTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/compact-boundary.jsonl, tests/Antiphon.Tests/Agents/Fixtures/split-final-response.jsonl, tests/Antiphon.Tests/Agents/Fixtures/api-error-stubs.jsonl … | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Application

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AgentTaskInputFallbackTests](../../../tests/Antiphon.Tests/Application/AgentTaskInputFallbackTests.cs) | assembly inventory in StartAsync | — | — | — | 27 / 13 / 79b742de |
| [AgentTaskLandSourceFreshnessTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandSourceFreshnessTests.cs) | tests/Antiphon.Tests/Antiphon.Tests.csproj, tests/Antiphon.Tests/**, scripts/build-slot.ps1 | — | — | — | — |
| [AreaMapLoaderTests](../../../tests/Antiphon.Tests/Application/AreaMapTests.cs) | antiphon.areas.json | — | — | — | — |
| [C0824RefOnlyConsumerTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyConsumerTests.cs) | .gitattributes | — | — | — | — |
| [CardFileBoardLookupTests](../../../tests/Antiphon.Tests/Application/CardFileBoardLookupEventTests.cs) | directory walk, git ls-files, tracked path in PrepareRemovedExportAsync | — | — | — | 77 / 45 / 79b742de |
| [CardFileIgnoreAcceptanceTests](../../../tests/Antiphon.Tests/Application/CardFileIgnoreAcceptanceTests.cs) | .gitignore | — | — | — | 30 / 11 / 8f055f87 |
| [CardFileIgnoreTests](../../../tests/Antiphon.Tests/Application/CardFileIgnoreTests.cs) | .gitignore | — | — | — | 21 / 8 / 8f055f87 |
| [CardFilePrivacyGitTests](../../../tests/Antiphon.Tests/Application/CardFilePrivacyGitTests.cs) | .gitignore | — | — | — | 54 / 17 / 8f055f87 |
| [CardFilePrivacyHostedServiceTests](../../../tests/Antiphon.Tests/Application/CardFilePrivacyHostedServiceTests.cs) | directory walk, tracked path in Real_bounded_tick_matches_manual_and_dry_cleanup_for_equivalent_state | — | — | — | 8 / 3 / 8f055f87 |
| [CardFilePrivacyOwnershipAcceptanceTests](../../../tests/Antiphon.Tests/Application/CardFilePrivacyOwnershipAcceptanceTests.cs) | .gitignore | — | — | — | 44 / 16 / 8f055f87 |
| [CardFilePrivacyOwnershipTests](../../../tests/Antiphon.Tests/Application/CardFilePrivacyOwnershipTests.cs) | .gitignore | — | — | — | 28 / 5 / 8f055f87 |
| [CardFilePrivacySyncAcceptanceTests](../../../tests/Antiphon.Tests/Application/CardFilePrivacySyncAcceptanceTests.cs) | .gitignore | — | — | — | 111 / 35 / 79b742de |
| [CardFilePrivacySyncTests](../../../tests/Antiphon.Tests/Application/CardFilePrivacySyncTests.cs) | .gitignore | — | — | — | 101 / 23 / 8f055f87 |
| [CardTaskFileServiceTests](../../../tests/Antiphon.Tests/Application/CardTaskFileServiceTests.cs) | .gitattributes, docs/**, docker-compose.dev.yml … | — | — | — | 91 / 21 / 8f055f87 |
| [HostCleanupWorktreeInventoryTests](../../../tests/Antiphon.Tests/Application/HostCleanupWorktreeInventoryTests.cs) | private fixture | — | — | — | — |
| [PlanCatalogServiceTests](../../../tests/Antiphon.Tests/Application/PlanCatalogServiceTests.cs) | docs/superpowers/specs/2026-08-16-card-0035-stuck-work-view.md, docs/superpowers/specs/2026-08-11-card-0019-card-correction.md, docs/superpowers/specs/2026-07-19-pty-host-split.md … | — | — | — | — |
| [ScopeResolverAreaTests](../../../tests/Antiphon.Tests/Application/AreaMapTests.cs) | server/Program.cs, AGENTS.md | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Checkpoints

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [CheckpointFixtureGitEnvironmentTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointFixtureGitEnvironmentTests.cs) | .gitignore | — | — | — | — |
| [CheckpointImportTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointImportTests.cs) | tests/Antiphon.Tests/Checkpoints/Fixtures/plan-table-0688.md, tests/Antiphon.Tests/Checkpoints/Fixtures/plan-table-illustrative.md, scripts/test-client.ps1 … | 34/0/0 · 6.1 [20261107-022238-5d4b/CP-2](#linux-receipt-keys) | — | — | — |
| [CheckpointReceiptSafetyTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointReceiptSafetyTests.cs) | directory walk in MutatedChild | 10/0/0 · 153 [20261125-145444-fd84/CP-4](#linux-receipt-keys) | — | — | 176 / 8 / 8f055f87 |
| [CheckpointShapeConflictTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointShapeConflictTests.cs) | .gitignore | — | — | — | — |
| [CheckpointTrackedBaselineTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointTrackedBaselineTests.cs) | .gitignore | — | — | — | — |
| [PlanCoverageCensusTests](../../../tests/Antiphon.Tests/Checkpoints/PlanCoverageCensusTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 11 / 12 / 8f055f87 |
| [PlanCoverageCommandTests](../../../tests/Antiphon.Tests/Checkpoints/PlanCoverageCommandTests.cs) | tests/Antiphon.Tests/Checkpoints/Fixtures/plan-table-legacy-min.md, tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/c999-frozen-plan.md.txt, tests/Antiphon.Tests/Checkpoints/** | 10/0/0 · 20.6 [20261107-022238-5d4b/CP-2](#linux-receipt-keys) | — | — | 16 / 10 / 8f055f87 |
| [RowVerbTests](../../../tests/Antiphon.Tests/Checkpoints/RowVerbTests.cs) | scripts/fixtures/c585-failures.trx, scripts/fixtures/c585-zero.trx, scripts/fixtures/c585-green.trx … | — | — | — | — |
| [TrxReportTests](../../../tests/Antiphon.Tests/Checkpoints/TrxReportTests.cs) | scripts/fixtures/c585-green.trx, tests/Antiphon.Tests/Checkpoints/Fixtures/c723-failure-with-stack.trx | 7/0/0 · 0.565 [20261107-022238-5d4b/CP-2](#linux-receipt-keys) | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Infrastructure

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [GitMinimumVersionGuardTests](../../../tests/Antiphon.Tests/Infrastructure/GitMinimumVersionGuardTests.cs) | docker/git-minimum-version.sh | — | — | — | — |
| [InProcessObservationPinTests](../../../tests/Antiphon.Tests/Infrastructure/InProcessObservationPinTests.cs) | private fixture | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Scripts

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [CacheSeedNpmOwnershipTests](../../../tests/Antiphon.Tests/Scripts/CacheSeedNpmOwnershipTests.cs) | scripts/c590-remote.sh, tests/Antiphon.Tests/Scripts/Fixtures/cache-seed-npm-ownership.sh | — | — | — | 3 / 3 / 8f055f87 |
| [ClaudeSessionCleanupTests](../../../tests/Antiphon.Tests/Scripts/ClaudeSessionCleanupTests.cs) | scripts/fixtures/claude-sessions-2026-08-22.json, scripts/fixtures/claude-sessions-live-bridges.json, scripts/lib/Antiphon.Nightly/ClaudeSessionCleanup.cs | — | — | — | — |
| [DeployServer2ApiDefaultTests](../../../tests/Antiphon.Tests/Scripts/DeployServer2ApiDefaultTests.cs) | docs/docker-stack.md, scripts/deploy-server2.ps1 | — | — | — | — |
| [QualificationEtaTests](../../../tests/Antiphon.Tests/Scripts/QualificationEtaTests.cs) | directory walk, tracked path in OutstandingSumsPlannedSeconds | — | — | — | — |
| [RcInputManifestTests](../../../tests/Antiphon.Tests/Scripts/RcInputManifestTests.cs) | global.json, client/package-lock.json, Dockerfile … | — | — | — | — |
| [RcProjectSelectionTests](../../../tests/Antiphon.Tests/Scripts/RcProjectSelectionTests.cs) | Directory.Build.props, tests/derived-impact-index.json | — | — | — | 78 / 4 / 8f055f87 |
| [RefreshGithubTokenRemoteCommandTests](../../../tests/Antiphon.Tests/Scripts/RefreshGithubTokenRemoteCommandTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | — |
| [RunCheckpointSourceScriptTests](../../../tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs) | scripts/fixtures/c585-failures.trx, scripts/fixtures/c585-zero.trx, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | 6/0/0 · 350 [20261107-093220-718b/CP-2](#linux-receipt-keys) | — | — | — |
| [ScriptsLaneRepairTests](../../../tests/Antiphon.Tests/Scripts/ScriptsLaneRepairTests.cs) | src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs | — | — | — | — |
| [StatusSnapshotScriptTests](../../../tests/Antiphon.Tests/Scripts/StatusSnapshotScriptTests.cs) | directory walk in Files | 2/0/0 · 1.6 [20261126-074134-4c4f/CP-2](#linux-receipt-keys) | — | — | — |
| [VerifyCodexImageCommandTests](../../../tests/Antiphon.Tests/Scripts/VerifyCodexImageCommandTests.cs) | docker/session-runner-grok/Dockerfile, docker/stack/init-state.sh, docker/session-runner-grok/verify-codex-image.sh | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.TestHelpers

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [LinuxTestRosterTests](../../../tests/Antiphon.Tests/TestHelpers/LinuxTestRosterTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | 5 / 6 / 8f055f87 |

## Additional candidate sites to check at the migration pin

760 source types.


### Antiphon.Agents.Pty.Tests / Antiphon.Agents.Pty.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [C1022TypedInputTests](../../../tests/Antiphon.Agents.Pty.Tests/C1022TypedInputTests.cs) | tests/Antiphon.Agents.Pty.Tests/probes/stdin-probe.js | — | — | — | — |
| [ClaudeCompactionCanaryTests](../../../tests/Antiphon.Agents.Pty.Tests/ClaudeCompactionCanaryTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/compact-full-manual.jsonl, tests/Antiphon.Tests/Agents/Fixtures/compact-boundary.jsonl | — | — | — | — |
| [ClaudeDialogObservationTests](../../../tests/Antiphon.Agents.Pty.Tests/ClaudeDialogObservationTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0156-signed-out.txt, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0156-trust-prompt.txt, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0156-update-modal.txt … | — | — | — | — |
| [ClaudeHookAdditionalContextCanaryTests](../../../tests/Antiphon.Agents.Pty.Tests/ClaudeHookAdditionalContextCanaryTests.cs) | .claude/settings.json, CLAUDE.md | — | — | — | — |
| [ClaudeLocalCommandCanaryTests](../../../tests/Antiphon.Agents.Pty.Tests/ClaudeLocalCommandCanaryTests.cs) | directory walk in Clear_writes_a_command_record_and_no_turn_completion_after_it | — | — | — | — |
| [ClaudeRemoteControlAtStartupCanaryTests](../../../tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs) | .claude/settings.json, tests/Antiphon.Agents.Pty.Tests/probes/settings-source-witness.mjs | — | — | — | — |
| [ClaudeSignalCanaryTests](../../../tests/Antiphon.Agents.Pty.Tests/ClaudeSignalCanaryTests.cs) | tests/Antiphon.Agents.Pty.Tests/golden/claude-signals.baseline.json | — | — | — | — |
| [ClaudeVerifiedDeliveryTests](../../../tests/Antiphon.Agents.Pty.Tests/ClaudeVerifiedDeliveryTests.cs) | directory walk in C1313_DeliveryDiscovery | — | — | — | — |
| [CodexAccountBootstrapTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexAccountBootstrapTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/captures.json | — | — | — | — |
| [CodexCanaryTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexCanaryTests.cs) | directory walk in Rollouts | — | — | — | — |
| [CodexComposerCanaryTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexComposerCanaryTests.cs) | directory walk in Rollouts | — | — | — | — |
| [CodexDoneDetectionCanaryTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexDoneDetectionCanaryTests.cs) | directory walk in Rollouts | — | — | — | — |
| [CodexMcpBootProbeTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexMcpBootProbeTests.cs) | AGENTS.md | — | — | — | — |
| [CodexReadyTrackerTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexReadyTrackerTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/captures.json | — | — | — | — |
| [CodexReadyWaitTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexReadyWaitTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/captures.json, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/n1.txt, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0156-update-modal.txt … | — | — | — | — |
| [CodexStartupReadinessTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexStartupReadinessTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/captures.json, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/n1.txt, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/n2.txt … | — | — | — | — |
| [CodexV0156StartupPromptTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexV0156StartupPromptTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/captures.json, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0156-ready.txt, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0156-signed-out.txt … | — | — | — | — |
| [CodexV0160FixtureTests](../../../tests/Antiphon.Agents.Pty.Tests/CodexV0160FixtureTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-done.txt, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-idle.txt, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-running.txt | — | — | — | — |
| [LiveClaudeEvidenceWiringTests](../../../tests/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests.cs) | tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs, tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractLiveTests.cs, tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractTests.cs … | — | — | — | — |
| [LiveClaudeExecutableWiringTests](../../../tests/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests.cs) | tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractLiveTests.cs, tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs, tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs … | — | — | — | — |
| [OrchestratorWorkspaceLayoutCanaryTests](../../../tests/Antiphon.Agents.Pty.Tests/OrchestratorWorkspaceLayoutCanaryTests.cs) | AGENTS.md, CLAUDE.md | — | — | — | — |
| [PtyBackendContractTests](../../../tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs) | tests/Antiphon.Agents.Pty.Tests/probes/stdin-probe.js | — | — | — | — |
| [PtyBracketedPasteContractTests](../../../tests/Antiphon.Agents.Pty.Tests/PtyBracketedPasteContractTests.cs) | tests/Antiphon.Agents.Pty.Tests/probes/stdin-probe.js | — | — | — | — |
| [PtyInputChunkingTests](../../../tests/Antiphon.Agents.Pty.Tests/PtyInputChunkingTests.cs) | tests/Antiphon.Agents.Pty.Tests/probes/stdin-probe.js | — | — | — | — |
| [PtyInputLossExperiments](../../../tests/Antiphon.Agents.Pty.Tests/PtyInputLossExperiments.cs) | tests/Antiphon.Agents.Pty.Tests/probes/stdin-probe.js | — | — | — | — |
| [PtyPasteMarkerExperiments](../../../tests/Antiphon.Agents.Pty.Tests/PtyPasteMarkerExperiments.cs) | tests/Antiphon.Agents.Pty.Tests/probes/stdin-probe.js | — | — | — | — |
| [SubmitEvidenceTests](../../../tests/Antiphon.Agents.Pty.Tests/SubmitEvidenceTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-idle.txt, tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-running.txt | — | — | — | — |
| [UnixPtyDeliveryEnvelopeTests](../../../tests/Antiphon.Agents.Pty.Tests/UnixPtyDeliveryEnvelopeTests.cs) | docs/superpowers/plans/2026-11-09-card-1224-linux-delivery-envelope-receipt.md, docs/superpowers/plans/** | — | — | — | — |
| [WindowsPtyArgvNativeTests](../../../tests/Antiphon.Agents.Pty.Tests/WindowsPtyArgvNativeTests.cs) | tests/Antiphon.Agents.Pty.Tests/probes/argv-echo.js | — | — | — | — |

### Antiphon.E2E / Antiphon.E2E

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [BoardE2ETests](../../../tests/Antiphon.E2E/BoardE2ETests.cs) | docs/screenshots/toonsharp-antiphon/36-spawn-legacy-worktree-running.png, docs/screenshots/toonsharp-antiphon/33-stopped-session-overlay-resume.png, docs/screenshots/toonsharp-antiphon/35-claude-session-recovery-prompt.png … | — | — | — | — |
| [CardCliE2ETests](../../../tests/Antiphon.E2E/CardCliE2ETests.cs) | src/**, client/index.html | — | — | — | — |
| [ChannelE2ETests](../../../tests/Antiphon.E2E/ChannelE2ETests.cs) | client/index.html | — | — | — | — |
| [ChannelOutboundIsolatedTests](../../../tests/Antiphon.E2E/ChannelOutboundIsolatedTests.Composed.cs) | docs/features/012-review-engine/design.md, tests/Antiphon.Tests/Fixtures/Card0418/combined.pdf, Dockerfile … | — | — | — | — |
| [DelegationPipelineE2ETests](../../../tests/Antiphon.E2E/DelegationPipelineE2ETests.cs) | .claude/skills/antiphon-delegate/SKILL.md, scripts/antiphon.cmd | — | — | — | — |
| [DelegationScratchCallerTests](../../../tests/Antiphon.E2E/DelegationScratchCallerTests.cs) | .claude/skills/antiphon-delegate/SKILL.md, scripts/antiphon.cmd | — | — | — | — |
| [DelegationSequencingE2ETests](../../../tests/Antiphon.E2E/DelegationSequencingE2ETests.cs) | .claude/skills/antiphon-delegate/SKILL.md, scripts/antiphon.cmd | — | — | — | — |
| [DirectoryAutocompleteE2ETests](../../../tests/Antiphon.E2E/DirectoryAutocompleteE2ETests.cs) | client/index.html | — | — | — | — |
| [DispatchBaseWarningDeliveryE2ETests](../../../tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs) | FindRepositoryRoot, directory walk in C508_MismatchWarningWaitsForCaller | — | — | — | — |
| [DispatchConcurrencySettingsE2ETests](../../../tests/Antiphon.E2E/DispatchConcurrencySettingsE2ETests.cs) | client/index.html | — | — | — | — |

### Antiphon.E2E / Antiphon.E2E.Fixtures

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AntiphonAppFixtureHostPathTests](../../../tests/Antiphon.E2E/Fixtures/AntiphonAppFixtureHostPathTests.cs) | server/appsettings.json | — | — | — | — |
| [IsolatedSessionRunnerTeardownTests](../../../tests/Antiphon.E2E/Fixtures/IsolatedSessionRunnerTeardownTests.cs) | directory walk in SweepCrashedRunsAsync | — | — | — | — |

### Antiphon.E2E / Antiphon.E2E

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [LandDeliveryFixtureProbeTests](../../../tests/Antiphon.E2E/LandDeliveryFixtureProbeTests.cs) | FindRepositoryRoot, directory walk in Verifier_evidence_heartbeats_in_flight_stops_at_its_budget_and_records_the_end | — | — | — | — |
| [OrchestratorE2ETests](../../../tests/Antiphon.E2E/OrchestratorE2ETests.cs) | client/index.html | — | — | — | — |
| [OutputDistillationApplyCanaryTests](../../../tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs) | docs/** | — | — | — | — |
| [PipelineGlanceE2ETests](../../../tests/Antiphon.E2E/PipelineGlanceE2ETests.cs) | client/index.html | — | — | — | — |
| [ProjectDeleteE2ETests](../../../tests/Antiphon.E2E/ProjectDeleteE2ETests.cs) | client/index.html | — | — | — | — |
| [ReleaseGateIsolationTests](../../../tests/Antiphon.E2E/ReleaseGateIsolationTests.cs) | client/index.html | — | — | — | — |
| [ReleaseGateReportDeliveryTests](../../../tests/Antiphon.E2E/ReleaseGateReportDeliveryTests.cs) | client/index.html | — | — | — | — |
| [ReviewEngineE2ETests](../../../tests/Antiphon.E2E/ReviewEngineE2ETests.cs) | client/index.html | — | — | — | — |
| [ScheduleCliE2ETests](../../../tests/Antiphon.E2E/ScheduleCliE2ETests.cs) | client/index.html | — | — | — | — |
| [SmokeE2ETests](../../../tests/Antiphon.E2E/SmokeE2ETests.cs) | client/index.html | — | — | — | — |
| [WorkflowDeleteTests](../../../tests/Antiphon.E2E/WorkflowDeleteTests.cs) | client/index.html | — | — | — | — |
| [WorkflowOutputTests](../../../tests/Antiphon.E2E/WorkflowOutputTests.cs) | client/index.html | — | — | — | — |
| [WorktreeRetirementDeliveryE2ETests](../../../tests/Antiphon.E2E/WorktreeRetirementDeliveryE2ETests.cs) | FindRepositoryRoot in Root | — | — | — | — |

### Antiphon.GitInProcess.Tests / Antiphon.Tests.Infrastructure

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [LibGit2VsGitDifferentialTests](../../../tests/Antiphon.GitInProcess.Tests/LibGit2VsGitDifferentialTests.cs) | tracked path in For_each_ref_formats_match_git | — | — | — | — |

### Antiphon.IO.WriteBehind.Tests / Antiphon.IO.WriteBehind.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [WriteBehindFileSystemTests](../../../tests/Antiphon.IO.WriteBehind.Tests/WriteBehindFileSystemTests.cs) | directory walk in OtherOperationsSeeTheQueuedWrite | — | — | — | — |

### Antiphon.Messaging.Tests / Antiphon.Messaging.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [FakeServerPortTests](../../../tests/Antiphon.Messaging.Tests/FakeServerPortTests.cs) | tracked path in MapEndpoints | — | — | — | — |
| [SlackChannelAdapterTests](../../../tests/Antiphon.Messaging.Tests/SlackChannelAdapterTests.cs) | tracked path in MapEndpoints | — | — | — | — |
| [SlackResilienceTests](../../../tests/Antiphon.Messaging.Tests/SlackResilienceTests.cs) | tracked path in MapEndpoints | — | — | — | — |

### Antiphon.PtyHost.Tests / Antiphon.PtyHost.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [Card0490NativeExecutionTests](../../../tests/Antiphon.PtyHost.Tests/Card0490NativeExecutionTests.cs) | tests/fixtures/card0490-linux/assets.lock.json | — | — | — | — |
| [Card1619ExitWireTests](../../../tests/Antiphon.PtyHost.Tests/Card1619ExitWireTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |

### Antiphon.SessionRunner.Tests / Antiphon.SessionRunner.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AgentPinPublicationTests](../../../tests/Antiphon.SessionRunner.Tests/AgentPinPublicationTests.cs) | directory walk in V13_PublicationCustody | — | — | — | — |
| [BuildSlotEndToEndTests](../../../tests/Antiphon.SessionRunner.Tests/BuildSlotEndToEndTests.cs) | scripts/build-slot.ps1 | — | — | — | — |
| [C0824RunnerBranchRefsTests](../../../tests/Antiphon.SessionRunner.Tests/C0824RunnerBranchRefsTests.cs) | tracked path in Create | 14/0/0 · 1.5 [20261106-075233-9edf/CP-2](#linux-receipt-keys) | — | — | — |
| [C0824WorkspaceSlotColdResetTests](../../../tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotColdResetTests.cs) | directory walk in Tree | 12/0/0 · 4.9 [20261106-075233-9edf/CP-2](#linux-receipt-keys) | — | — | — |
| [C0824WorkspaceSlotCustodyTests](../../../tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotCustodyTests.cs) | directory walk in Tree | 19/0/0 · 8.4 [20261106-075233-9edf/CP-2](#linux-receipt-keys) | — | — | — |
| [C0824WorkspaceSlotOverflowTests](../../../tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotOverflowTests.cs) | directory walk in Tree | 12/0/0 · 7.5 [20261106-075233-9edf/CP-2](#linux-receipt-keys) | — | — | — |
| [C0824WorkspaceSlotRehomeTests](../../../tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests.cs) | directory walk, tracked path in C2061_HiddenSubmoduleEditHoldsQuarantine | 27/0/0 · 24.5 [20261126-074134-4c4f/CP-8](#linux-receipt-keys) | — | — | — |
| [C0824WorkspaceSlotResetTests](../../../tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests.cs) | directory walk in C0824_FreeSlotDetachedAtPublishedShaWithNoTaskRefs | 10/0/0 · 5.0 [20261106-075233-9edf/CP-2](#linux-receipt-keys) | — | — | — |
| [C1514HostLoadNativeOverheadTests](../../../tests/Antiphon.SessionRunner.Tests/C1514HostLoadNativeOverheadTests.cs) | directory walk in RunAsync | — | — | — | — |
| [C1514HostLoadNativeTests](../../../tests/Antiphon.SessionRunner.Tests/C1514HostLoadNativeTests.cs) | tracked path in VerifyAsync | — | — | — | — |
| [C1514HostLoadSpoolTests](../../../tests/Antiphon.SessionRunner.Tests/C1514HostLoadSpoolTests.cs) | directory walk in PartialTailIsQuarantined | — | — | — | — |
| [C1677RetentionDispatchTests](../../../tests/Antiphon.SessionRunner.Tests/C1677RetentionDispatchTests.cs) | directory walk in Tree | 11/0/0 · 1.2 [20261106-075233-9edf/CP-2](#linux-receipt-keys) | — | — | — |
| [C1677RetentionEngineTests](../../../tests/Antiphon.SessionRunner.Tests/C1677RetentionEngineTests.cs) | .gitignore | 81/0/0 · 15.1 [20261106-075233-9edf/CP-2](#linux-receipt-keys) | — | — | — |
| [C1677RetentionNativeProcessTests](../../../tests/Antiphon.SessionRunner.Tests/C1677RetentionNativeProcessTests.cs) | directory walk in Tree | 14/0/0 · 3.7 [20261106-075233-9edf/CP-2](#linux-receipt-keys) | — | — | — |
| [C1677TempSweepDispatchTests](../../../tests/Antiphon.SessionRunner.Tests/C1677TempSweepDispatchTests.cs) | directory walk in Mode_reaches_the_sweep_and_dry_run_never_deletes | — | — | — | — |
| [C1677TempSweepNativeTests](../../../tests/Antiphon.SessionRunner.Tests/C1677TempSweepNativeTests.cs) | directory walk in Control_character_name_is_refused_before_any_family_match | — | — | — | — |
| [C1677TempSweepTests](../../../tests/Antiphon.SessionRunner.Tests/C1677TempSweepTests.cs) | directory walk in Old_registered_entry_is_swept | — | — | — | — |
| [C1817RunnerRepositoryMaintenanceTests](../../../tests/Antiphon.SessionRunner.Tests/C1817RunnerRepositoryMaintenanceTests.cs) | tracked path in C1817_gc_child_strips_inherited_git_config | — | — | — | — |
| [C1893CollectorDiagnosticsTests](../../../tests/Antiphon.SessionRunner.Tests/C1893CollectorDiagnosticsTests.cs) | tracked path in Missing_default_data_directory_never_resolves_against_application_directory | — | — | — | — |
| [C1896TempSweepPolicyTests](../../../tests/Antiphon.SessionRunner.Tests/C1896TempSweepPolicyTests.cs) | directory walk in KeptIntact | — | — | — | — |
| [Card1619LinuxCustodyStopIntentTests](../../../tests/Antiphon.SessionRunner.Tests/Card1619LinuxCustodyStopIntentTests.cs) | directory walk in Explicit_release_records_signal_without_fabricating_custody | 2/0/0 · 7.4 [20261126-044237-3c1a/CP-2](#linux-receipt-keys) | — | — | — |
| [Card1653SocketDirTests](../../../tests/Antiphon.SessionRunner.Tests/Card1653SocketDirTests.cs) | directory walk in Unsafe_directory_fails_the_launch_before_spawn | — | — | — | — |
| [CodexTranscriptTailerTests](../../../tests/Antiphon.SessionRunner.Tests/CodexTranscriptTailerTests.cs) | tests/Antiphon.SessionRunner.Tests/Fixtures/codex-tui-turn.jsonl | — | — | — | — |
| [CompletedCardWorkspaceRemovalTests](../../../tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceFixturePortabilityTests.cs) | .gitattributes | — | — | — | — |
| [GrokRulesFileStoreTests](../../../tests/Antiphon.SessionRunner.Tests/GrokRulesFileStoreTests.cs) | directory walk in Rules_content_preserves_control_newlines_unicode_and_tail_beyond_1000_lines | 11/0/0 · 0.311 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [GrokRulesStoreFailureTests](../../../tests/Antiphon.SessionRunner.Tests/GrokRulesStoreFailureTests.cs) | directory walk in Storage_failure_refuses_before_any_child_or_pane_effect | 6/0/0 · 0.917 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [GrokTranscriptTailerTests](../../../tests/Antiphon.SessionRunner.Tests/GrokTranscriptTailerTests.cs) | docs/project-context.md, tests/Antiphon.SessionRunner.Tests/Fixtures/grok-transport-error.jsonl, tests/Antiphon.SessionRunner.Tests/Fixtures/grok-transport-error-blank.jsonl … | — | — | — | — |
| [HerdrAttachTests](../../../tests/Antiphon.SessionRunner.Tests/HerdrAttachTests.cs) | directory walk, tracked path in Antiphon_agent_session_is_not_evidence_of_native_id | — | — | — | — |
| [HerdrNamedTabPlacementTests](../../../tests/Antiphon.SessionRunner.Tests/HerdrNamedTabPlacementTests.cs) | directory walk in SnapshotLastPane | — | — | — | — |
| [HerdrPaneDisposalIdentityTests](../../../tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalIdentityTests.cs) | tracked path in C461_G024_Native_source_antiphon | — | — | — | — |
| [HerdrPaneDisposalServiceTests](../../../tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalGuardTests.cs) | directory walk, tracked path in Protocol20_preview_is_read_only_and_redacts_process_arguments | — | — | — | — |
| [LinuxRunnerCustodyLifecycleTests](../../../tests/Antiphon.SessionRunner.Tests/LinuxRunnerCustodyLifecycleTests.cs) | docker/session-runner-grok/antiphon-custody-enter.sh, docker/session-runner-grok/antiphon-custody-common.sh | 6/0/0 · 5.4 [20261126-044237-3c1a/CP-2](#linux-receipt-keys) | — | — | — |
| [PushCredentialPolicyFileTests](../../../tests/Antiphon.SessionRunner.Tests/PushCredentialPolicyFileTests.cs) | directory walk in Write_creates_the_parent_and_replaces_an_existing_file | 3/0/0 · 0.014 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [PushProbeOutcomeTests](../../../tests/Antiphon.SessionRunner.Tests/PushProbeOutcomeTests.cs) | docs/agent-credentials.md | 5/0/0 · 0.022 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [RunnerAbsenceEvidenceRuntimeTests](../../../tests/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidenceRuntimeTests.cs) | directory walk in C1153_Closed_identity_refuses_delayed_creation | — | — | — | — |
| [RunnerAbsenceEvidenceTests](../../../tests/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidenceTests.cs) | directory walk in C1153_Closure_survives_storage_failure | 81/0/0 · 17.7 [20261106-075233-9edf/CP-3](#linux-receipt-keys) | — | — | — |
| [RunnerStartupReadinessTests](../../../tests/Antiphon.SessionRunner.Tests/RunnerStartupReadinessTests.cs) | docs/investigations/2026-09-16-card-0544-evidence/observations.json | — | — | — | — |
| [RunnerWorkspaceBindingTests](../../../tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests.cs) | tracked path in Create | — | — | — | — |
| [RunnerWorkspaceServiceTests](../../../tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests.cs) | tracked path in Create | — | — | — | — |
| [TranscriptTailerCompactionTests](../../../tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/compact-boundary.jsonl | — | — | — | — |
| [UnixPtyArgvAdmissionTests](../../../tests/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests.cs) | directory walk in Nul_request_is_refused_before_effects | — | — | — | — |
| [WorkspaceParkPublicationTests](../../../tests/Antiphon.SessionRunner.Tests/WorkspaceParkPublicationTests.cs) | .gitignore | — | — | — | — |
| [WorkspaceSourceVerificationWireTests](../../../tests/Antiphon.SessionRunner.Tests/WorkspaceSourceVerificationWireTests.cs) | .gitignore | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.AgentTui

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AgentTuiApiTests](../../../tests/Antiphon.Tests/AgentTui/AgentTuiApiTests.cs) | directory walk in ReadLogs | — | — | — | — |
| [AgentTuiSecretProtectorTests](../../../tests/Antiphon.Tests/AgentTui/AgentTuiSecretProtectorTests.cs) | compiled attribute roster, directory walk in Configured_protector_rechecks_persisted_key_presence | — | — | — | — |
| [RunnerProcessProbeTests](../../../tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs) | directory walk in DeleteScratchBestEffort | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Agents

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [Card0168InteractiveProbeTests](../../../tests/Antiphon.Tests/Agents/Card0168InteractiveProbeTests.cs) | .claude/settings.json | — | — | — | — |
| [ClaudeAdapterEffortPromptTests](../../../tests/Antiphon.Tests/Agents/ClaudeAdapterEffortPromptTests.cs) | tracked path in FallbackDirectory | — | — | — | 24 / 5 / 79b742de |
| [ClaudeDialogWatchdogCanaryTests](../../../tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs) | .claude/settings.json, AGENTS.md, CLAUDE.md … | — | — | — | — |
| [ClaudeEffortPromptCanaryTests](../../../tests/Antiphon.Tests/Agents/ClaudeEffortPromptCanaryTests.cs) | .claude/settings.json | — | — | — | — |
| [ClaudeHerdrRealCliStubProxyCanaryTests](../../../tests/Antiphon.Tests/Agents/ClaudeHerdrRealCliStubProxyCanaryTests.cs) | .claude/settings.json | — | — | — | — |
| [ClaudeRealCliStubProxyCanaryTests](../../../tests/Antiphon.Tests/Agents/ClaudeRealCliStubProxyCanaryTests.cs) | .claude/settings.json | — | — | — | — |
| [CodexAccountBootstrapFailureTests](../../../tests/Antiphon.Tests/Agents/CodexAccountBootstrapFailureTests.cs) | directory walk in Adapter_records_the_bounded_outcome_without_logging_the_screen | — | — | — | — |
| [CodexBootPointerCanaryTests](../../../tests/Antiphon.Tests/Agents/CodexBootPointerCanaryTests.cs) | directory walk in TryDelete | — | — | — | — |
| [CodexBootWedgeProbeTests](../../../tests/Antiphon.Tests/Agents/CodexBootWedgeProbeTests.cs) | directory walk in ReadDiagnostics | — | — | — | — |
| [CodexCommandLengthSessionTests](../../../tests/Antiphon.Tests/Agents/CodexCommandLengthSessionTests.cs) | directory walk in TryDelete | — | — | — | — |
| [CodexHerdrRealCliStubProxyCanaryTests](../../../tests/Antiphon.Tests/Agents/CodexHerdrRealCliStubProxyCanaryTests.cs) | AGENTS.md | — | — | — | — |
| [CodexRealCliStubProxyCanaryTests](../../../tests/Antiphon.Tests/Agents/CodexRealCliStubProxyCanaryTests.cs) | directory walk, repoPath in CreateGraph | — | — | — | — |
| [GrokFreshWorktreeQualificationTests](../../../tests/Antiphon.Tests/Agents/GrokFreshWorktreeQualificationTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/card0778/startup-frames.json | — | — | — | — |
| [GrokHerdrRealCliStubProxyCanaryTests](../../../tests/Antiphon.Tests/Agents/GrokHerdrRealCliStubProxyCanaryTests.cs) | AGENTS.md | — | — | — | — |
| [GrokLinuxBlockingPromptTests](../../../tests/Antiphon.Tests/Agents/GrokLinuxBlockingPromptTests.cs) | docs/investigations/2026-10-03-card-1006-linux-grok-frames.json, tests/Antiphon.Tests/Agents/Fixtures/card1006/linux-blocking-frames.json, tests/Antiphon.Tests/Agents/Fixtures/card1006/synthetic-blocking-frames.json … | — | — | — | — |
| [GrokLinuxStartupReadinessTests](../../../tests/Antiphon.Tests/Agents/GrokLinuxStartupReadinessTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/card1004/linux-startup-frames.json | — | — | — | — |
| [GrokRealCliStubProxyCanaryTests](../../../tests/Antiphon.Tests/Agents/GrokRealCliStubProxyCanaryTests.cs) | RepoPath, repoPath, tracked path in B_server_session_binds_transcript_and_whenidle_confirms_via_stub | — | — | — | — |
| [GrokSizeCaptureTests](../../../tests/Antiphon.Tests/Agents/GrokSizeCaptureTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/captures.json | — | — | — | — |
| [GrokStartupCaptureStoreTests](../../../tests/Antiphon.Tests/Agents/GrokStartupCaptureStoreTests.cs) | directory walk in C2047_Expiry_keeps_recent_captures_and_ignores_unowned_files | — | — | — | — |
| [GrokStartupReadinessTests](../../../tests/Antiphon.Tests/Agents/GrokStartupReadinessTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/card0778/startup-frames.json | — | — | — | — |
| [GrokStartupSizeTests](../../../tests/Antiphon.Tests/Agents/GrokStartupSizeTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/card0778/startup-frames.json, tests/Antiphon.Tests/Agents/Fixtures/card0861/startup-sizes.json | — | — | — | — |
| [ProviderEndpointSentinelCanaryTests](../../../tests/Antiphon.Tests/Agents/ProviderEndpointSentinelCanaryTests.cs) | directory walk, repoPath in CreateGraph | — | — | — | 7 / 2 / 79b742de |
| [RunnerCodexAdapterReadyTests](../../../tests/Antiphon.Tests/Agents/RunnerCodexAdapterReadyTests.cs) | directory walk in Timeout_stores_the_last_frame_and_logs_only_its_path | — | — | — | — |
| [RunnerGrokAdapterReadyTests](../../../tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/card0778/startup-frames.json, tests/Antiphon.Tests/Agents/Fixtures/card0861/startup-sizes.json, tests/Antiphon.Tests/Agents/Fixtures/card1004/linux-startup-frames.json | — | — | — | — |
| [RunnerGrokAdapterReadyTestsPty](../../../tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs) | docs/investigations/2026-09-16-card-0544-evidence/observations.json | — | — | — | — |
| [RunnerGrokAdapterSignInPromptTests](../../../tests/Antiphon.Tests/Agents/RunnerGrokAdapterSignInPromptTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/card0778/startup-frames.json | — | — | — | — |
| [RunnerGrokAdapterTrustPromptTests](../../../tests/Antiphon.Tests/Agents/RunnerGrokAdapterTrustPromptTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/card0778/startup-frames.json | — | — | — | 6 / 4 / 79b742de |
| [SessionRunnerRuntimeTests](../../../tests/Antiphon.Tests/Agents/SessionRunnerRuntimeTests.cs) | directory walk in DeleteDirectoryBestEffort | — | — | — | 6 / 5 / 79b742de |
| [SpecialistToolPolicyTests](../../../tests/Antiphon.Tests/Agents/SpecialistToolPolicyTests.cs) | .claude/settings.json | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Api

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [CallerWakeTests](../../../tests/Antiphon.Tests/Api/CallerWakeTests.Native.cs) | scripts/hooks/orchestrator-caller-wake.mjs, scripts/hooks/orchestrator-investigation-hook.mjs, scripts/hooks/orchestrator-investigation.mjs … | 5/0/0 · 15.5 [20261126-074134-4c4f/CP-5](#linux-receipt-keys) | — | — | — |
| [ExpectationStatusTests](../../../tests/Antiphon.Tests/Api/ExpectationStatusTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | 22 / 3 / 79b742de |

### Antiphon.Tests / Antiphon.Tests.ApiKeys

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [LaunchEnvLayersIntegrationTests](../../../tests/Antiphon.Tests/ApiKeys/LaunchEnvLayersIntegrationTests.cs) | directory walk in DeleteTree | — | — | — | 147 / 33 / 79b742de |

### Antiphon.Tests / Antiphon.Tests.Application

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AbandonedLandingReconcilerTests](../../../tests/Antiphon.Tests/Application/AbandonedLandingReconcilerTests.cs) | RepoPath in SeedTask | — | — | — | 32 / 7 / 8f055f87 |
| [AdvisoryChangePolicyTests](../../../tests/Antiphon.Tests/Application/AdvisoryChangePolicyTests.Correction.cs) | AGENTS.md, CLAUDE.md, docs/testing-and-build.md … | — | — | — | 48 / 11 / 8f055f87 |
| [AgentAttachHerdrTests](../../../tests/Antiphon.Tests/Application/AgentAttachHerdrTests.cs) | RepoPath in Restamps_the_agents_own_stopped_row_with_the_same_id | 18/0/0 · 9.4 [20261123-053028-07ea/CP-4](#linux-receipt-keys) | — | — | — |
| [AgentBundleAttachmentTests](../../../tests/Antiphon.Tests/Application/AgentBundleAttachmentTests.cs) | AGENTS.md | — | — | — | — |
| [AgentControlServiceIntegrationTests](../../../tests/Antiphon.Tests/Application/AgentControlServiceIntegrationTests.cs) | directory walk, repoPath in NewProject | 2/0/0 · 3.0 [20261123-053028-07ea/CP-9](#linux-receipt-keys) | — | — | — |
| [AgentNamePinTests](../../../tests/Antiphon.Tests/Application/AgentNamePinTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [AgentPinnedInstructionServiceTests](../../../tests/Antiphon.Tests/Application/AgentPinnedInstructionServiceTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 126 / 18 / 8f055f87 |
| [AgentReportStoreBudgetTests](../../../tests/Antiphon.Tests/Application/AgentReportStoreBudgetTests.cs) | .gitignore | — | — | — | — |
| [AgentReportStoreLoadTests](../../../tests/Antiphon.Tests/Application/AgentReportStoreLoadTests.cs) | .gitignore | — | — | — | — |
| [AgentReportStoreTests](../../../tests/Antiphon.Tests/Application/AgentReportStoreTests.cs) | .gitignore | — | — | — | 43 / 28 / 79b742de |
| [AgentSessionBackendTests](../../../tests/Antiphon.Tests/Application/AgentSessionBackendTests.cs) | directory walk in DeleteDirectoryBestEffort | 24/0/0 · 69.9 [20261123-053028-07ea/CP-4](#linux-receipt-keys) | — | — | 36 / 24 / 79b742de |
| [AgentSessionCancelledTurnTests](../../../tests/Antiphon.Tests/Application/AgentSessionCancelledTurnTests.cs) | repoPath in CreateCard | — | — | — | 181 / 16 / 79b742de |
| [AgentSessionRuntimeTests](../../../tests/Antiphon.Tests/Application/AgentSessionRuntimeTests.Persist.cs) | directory walk in DeleteDirectoryBestEffort | 2/0/0 · 13.0 [20261126-074134-4c4f/CP-3](#linux-receipt-keys) | — | — | 108 / 47 / 79b742de |
| [AgentSessionServiceIntegrationTests](../../../tests/Antiphon.Tests/Application/AgentSessionServiceIntegrationTests.cs) | RepoPath, directory walk, repoPath in AgentSessionService_turn_completion_timeout_marks_session_failed_and_retry_reuses_worktree | 22/0/1 · 15.3 [20261123-053028-07ea/CP-4](#linux-receipt-keys) | — | — | — |
| [AgentSystemPromptLaunchTests](../../../tests/Antiphon.Tests/Application/AgentSystemPromptLaunchTests.cs) | AGENTS.md | 1/0/0 · 3.9 [20261123-053028-07ea/CP-9](#linux-receipt-keys) | — | — | 54 / 28 / 79b742de |
| [AgentTaskAgentKindTests](../../../tests/Antiphon.Tests/Application/AgentTaskAgentKindTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [AgentTaskCardBindingTests](../../../tests/Antiphon.Tests/Application/AgentTaskCardBindingTests.cs) | RepoPath, directory walk in SeedTaskAsync | — | — | — | 87 / 19 / 79b742de |
| [AgentTaskCheckScheduleTests](../../../tests/Antiphon.Tests/Application/AgentTaskCheckScheduleTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [AgentTaskCommitEndpointTests](../../../tests/Antiphon.Tests/Application/AgentTaskCommitChildChainTests.cs) | .gitignore | — | — | — | 97 / 39 / 79b742de |
| [AgentTaskConcurrencyLimitTests](../../../tests/Antiphon.Tests/Application/AgentTaskConcurrencyLimitTests.cs) | RepoPath, directory walk in a_stuck_finding_is_named_on_the_occupant | — | — | — | 105 / 25 / 79b742de |
| [AgentTaskDeadSessionReconciliationTests](../../../tests/Antiphon.Tests/Application/AgentTaskDeadSessionReconciliationTests.cs) | RepoPath, directory walk, repoPath in a_dead_session_with_zero_transcript_and_a_worktree_commit_recovers_without_killing | — | — | — | — |
| [AgentTaskDecisionQuestionIntegrationTests](../../../tests/Antiphon.Tests/Application/AgentTaskDecisionQuestionIntegrationTests.cs) | scripts/**, .gitattributes | — | — | — | 19 / 16 / 79b742de |
| [AgentTaskDecisionQuestionTests](../../../tests/Antiphon.Tests/Application/AgentTaskDecisionQuestionTests.cs) | .gitattributes, scripts/** | — | — | — | — |
| [AgentTaskDeliveryWatchdogTests](../../../tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogC714Tests.cs) | RepoPath, directory walk, repoPath in zero_transcript_plus_worktree_commit_recovers_succeeded_and_does_not_kill | — | — | — | 227 / 86 / see cost source |
| [AgentTaskDetailBlockedContextTests](../../../tests/Antiphon.Tests/Application/AgentTaskDetailBlockedContextTests.cs) | directory walk in DeleteTree | — | — | — | 29 / 10 / 8f055f87 |
| [AgentTaskDispatchBaseGuardTests](../../../tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.Capacity.cs) | RepoPath, directory walk, repoPath in C1213_SourceLandingKeepsLegacyIdentity | — | — | — | 497 / 92 / see cost source |
| [AgentTaskDispatchFailureTests](../../../tests/Antiphon.Tests/Application/AgentTaskDispatchFailureTests.cs) | RepoPath, directory walk, repoPath in SeedQueuedWorktreeTaskAsync | — | — | — | — |
| [AgentTaskDispatcherPredicateTests](../../../tests/Antiphon.Tests/Application/AgentTaskDispatcherPredicateTests.cs) | RepoPath in C672_LaunchesPreparedMirror | — | — | — | — |
| [AgentTaskInputSpillTests](../../../tests/Antiphon.Tests/Application/AgentTaskInputSpillTests.cs) | directory walk, tracked path in Local_desktop_refinement_keeps_its_file_and_pointer_contract | — | — | — | 41 / 15 / 8f055f87 |
| [AgentTaskInternalDecisionLifecycleTests](../../../tests/Antiphon.Tests/Application/AgentTaskInternalDecisionLifecycleTests.cs) | .gitattributes | — | — | — | 11 / 4 / 8f055f87 |
| [AgentTaskInternalDecisionMigrationTests](../../../tests/Antiphon.Tests/Application/AgentTaskInternalDecisionMigrationTests.cs) | .gitattributes | — | — | — | 53 / 2 / 8f055f87 |
| [AgentTaskLandActiveSourceClaimTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandActiveSourceClaimTests.cs) | RepoPath in AssertLandFirstAdmissionAsync | — | — | — | 1920 / 16 / see cost source |
| [AgentTaskLandAdmissionControlledTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandAdmissionControlledTests.cs) | RepoPath, tracked path in C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther | — | — | — | 304 / 24 / 79b742de |
| [AgentTaskLandAdmissionTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandAdmissionTests.cs) | RepoPath in C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther | — | — | — | — |
| [AgentTaskLandAdoptionTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandAdoptionTests.cs) | RepoPath in C753_ReviewedRepairAdoptionObeysLocalCasAndRemoteLease | — | — | — | 723 / 21 / 79b742de |
| [AgentTaskLandApprovalPersistenceTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandApprovalPersistenceTests.cs) | GetMigrations, HasPendingModelChanges, RepoPath in C498_MigrationIsAdditive | — | — | — | 175 / 10 / 8f055f87 |
| [AgentTaskLandApprovalRecoveryTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandApprovalRecoveryTests.cs) | tracked path in .ctor | — | — | — | — |
| [AgentTaskLandApprovalRequestTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandApprovalRequestTests.cs) | RepoPath in C488_SupersededEvidenceRefuses | — | — | — | 96 / 24 / 79b742de |
| [AgentTaskLandBareRepositoryCleanupTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandBareRepositoryCleanupTests.cs) | RepoPath, directory walk in SeedBareAsync | — | — | — | 725 / 15 / 79b742de |
| [AgentTaskLandBoundaryControlledTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandBoundaryControlledTests.cs) | RepoPath, tracked path in C448_V10_EachAcknowledgedBoundaryRechecksSource | — | — | — | — |
| [AgentTaskLandBoundaryTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandBoundaryTests.cs) | RepoPath in C448_V10_EachAcknowledgedBoundaryRechecksSource | — | — | — | 866 / 38 / 79b742de |
| [AgentTaskLandConcurrencyControlledTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandConcurrencyControlledTests.cs) | RepoPath, tracked path in C448_V14_EveryModeHonoursWriterAndLeaseHolds | — | — | — | 212 / 22 / 79b742de |
| [AgentTaskLandConcurrencyTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandConcurrencyTests.cs) | RepoPath in C448_V14_EveryModeHonoursWriterAndLeaseHolds | — | — | — | — |
| [AgentTaskLandContractEndpointTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandContractEndpointTests.cs) | RepoPath in C498_TaskGetExposesFailureDiagnostics | — | — | — | — |
| [AgentTaskLandDispatchYieldTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandDispatchYieldTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 300 / 10 / 8f055f87 |
| [AgentTaskLandFailureDiagnosticTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandFailureDiagnosticTests.cs) | tracked path in .ctor | — | — | — | 192 / 26 / 79b742de |
| [AgentTaskLandHalfResetTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs) | RepoPath in C883_FreshRequestRejectsWitnessIdentity | — | — | — | — |
| [AgentTaskLandHalfResetWindowsTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandHalfResetWindowsTests.cs) | git ls-files in CheckoutTrackedFixtureFilesWithCrLfAsync | — | — | — | 158 / 4 / 8f055f87 |
| [AgentTaskLandHeldNotificationTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandHeldNotificationTests.cs) | GetMigrations, RepoPath in C641_Upgrade_preserves_existing_held_notification_debt | — | — | — | 119 / 12 / 79b742de |
| [AgentTaskLandHoldVisibilityTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandHoldVisibilityTests.cs) | RepoPath in C467_V03_RealWriterLeaseAndEpisodeMatrix | — | — | — | 279 / 19 / 79b742de |
| [AgentTaskLandNestedRepositoryCleanupTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandNestedRepositoryCleanupTests.cs) | RepoPath, directory walk in AdministrativeBytesAsync | — | — | — | 448 / 8 / 8f055f87 |
| [AgentTaskLandNotificationPersistenceTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandNotificationPersistenceTests.H5.cs) | GetMigrations in C467_V17_UpgradeAndLegacyEvidence | — | — | — | 225 / 12 / 79b742de |
| [AgentTaskLandNotificationRecoveryTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandNotificationRecoveryTests.C1705.cs) | directory walk in RunPairAsync | — | — | — | 112 / 43 / 79b742de |
| [AgentTaskLandPreparationIdentityTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandPreparationIdentityTests.cs) | RepoPath in C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation | — | — | — | 473 / 16 / 79b742de |
| [AgentTaskLandPublicationTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.Delivery.cs) | directory walk, tracked path in .ctor | — | — | — | — |
| [AgentTaskLandReceiptTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandReceiptTests.Scan.cs) | directory walk in C467_V12_CatchUpAndRecoverReceiptWithoutRetyping | — | — | — | 67 / 42 / 79b742de |
| [AgentTaskLandRecoveryTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandRecoveryTests.cs) | directory walk, tracked path in .ctor | — | — | — | 673 / 22 / 79b742de |
| [AgentTaskLandRefusedRetryTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandRefusedRetryTests.cs) | FindRepoRoot, RepoRoot, repoRoot in Create | — | — | — | 638 / 15 / 79b742de |
| [AgentTaskLandRegistrationSafetyTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandRegistrationSafetyTests.cs) | git ls-files in CaptureAsync | — | — | — | 216 / 7 / 8f055f87 |
| [AgentTaskLandRegistrationSafetyWindowsTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandRegistrationSafetyWindowsTests.cs) | git ls-files in CaptureAsync | — | — | — | 69 / 4 / 8f055f87 |
| [AgentTaskLandRemoteSourceRetentionATests](../../../tests/Antiphon.Tests/Application/AgentTaskLandRemoteSourceRetentionATests.cs) | .claude/settings.json | — | — | — | 176 / 8 / 79b742de |
| [AgentTaskLandRemoteSourceRetentionBTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandRemoteSourceRetentionBTests.cs) | RepoPath in C448_V21_RefusalNeverDeletesRemoteSource | — | — | — | 186 / 8 / 79b742de |
| [AgentTaskLandRemoteSourceRetentionDTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandRemoteSourceRetentionDTests.cs) | tests/Antiphon.Tests/Antiphon.Tests.csproj, tests/Antiphon.Tests/** | — | — | — | 151 / 8 / 79b742de |
| [AgentTaskLandRemovalMatrixTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandRemovalMatrixTests.cs) | .claude/settings.json | — | — | — | 1415 / 52 / 79b742de |
| [AgentTaskLandRequestTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandRequestTests.cs) | RepoPath in C753_ExplicitReviewedRecoveryKeepsTerminalOwnerStatus | — | — | — | 361 / 30 / 79b742de |
| [AgentTaskLandSourcePersistenceTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandSourcePersistenceTests.cs) | RepoPath, tracked path in NewPending | — | — | — | 395 / 35 / 79b742de |
| [AgentTaskLandStageOutcomeTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandStageOutcomeTests.cs) | RepoPath, directory walk, tracked path in unreadable_push_endpoint_refuses_before_any_stage | — | — | — | 451 / 14 / 79b742de |
| [AgentTaskLandSweepTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandSweepTests.cs) | RepoPath in SeedPendingAsync | — | — | — | 24 / 7 / 8f055f87 |
| [AgentTaskLandTargetRaceFakeTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandTargetRaceFakeTests.cs) | tracked path in .ctor | — | — | — | 70 / 6 / 8f055f87 |
| [AgentTaskLandVerifierTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandVerifierTests.cs) | tests/Antiphon.Tests/Antiphon.Tests.csproj, tests/Antiphon.Tests/** | — | — | — | 105 / 8 / 79b742de |
| [AgentTaskLandingPersistenceTests](../../../tests/Antiphon.Tests/Application/AgentTaskLandingPersistenceTests.cs) | GetMigrations in C448_V31_MigrationAndConcurrentOperations | — | — | — | 34 / 2 / 8f055f87 |
| [AgentTaskMidTurnRefineTests](../../../tests/Antiphon.Tests/Application/AgentTaskMidTurnRefineTests.cs) | tests/Antiphon.Tests/Fixtures/grok-empty-composer.txt | — | — | — | — |
| [AgentTaskOverdueDeadlineTests](../../../tests/Antiphon.Tests/Application/AgentTaskOverdueDeadlineTests.cs) | directory walk, tracked path in TryDeleteTree | — | — | — | — |
| [AgentTaskPipelineStatusTests](../../../tests/Antiphon.Tests/Application/AgentTaskPipelineStatusC100Tests.cs) | docs/superpowers/plans/2026-09-03-card-0146-stage-pipeline-handoff-plan.md | — | — | — | 730 / 69 / 79b742de |
| [AgentTaskPoolTests](../../../tests/Antiphon.Tests/Application/AgentTaskPoolTests.cs) | directory walk in DeleteTree | 61/0/0 · 19.6 [20261123-053028-07ea/CP-4](#linux-receipt-keys) | — | — | — |
| [AgentTaskProjectScopeTests](../../../tests/Antiphon.Tests/Application/AgentTaskProjectScopeTests.cs) | RepoPath, directory walk in SeedTaskAsync | — | — | — | 36 / 6 / 8f055f87 |
| [AgentTaskRefineTests](../../../tests/Antiphon.Tests/Application/AgentTaskRefineTests.cs) | directory walk in an_oversized_refinement_spills_to_a_file_and_types_a_pointer | — | — | — | 7 / 9 / 8f055f87 |
| [AgentTaskReplyIntegrationTests](../../../tests/Antiphon.Tests/Application/AgentTaskReplyC527EmptyRecoveryTests.cs) | .gitignore, docs/superpowers/plans/2026-09-07-card-0417-channel-reply-conciseness-plan.md, tests/Antiphon.Tests/Agents/Fixtures/split-final-response.jsonl … | — | — | — | 562 / 341 / 79b742de |
| [AgentTaskReviewEvidenceTests](../../../tests/Antiphon.Tests/Application/AgentTaskReviewEvidenceTests.cs) | RepoPath in C488_EvidenceCoordinatesImmutable | — | — | — | 66 / 16 / 8f055f87 |
| [AgentTaskScopeOverlapCreateTests](../../../tests/Antiphon.Tests/Application/AgentTaskScopeOverlapCreateTests.cs) | RepoPath, directory walk, repoPath in SeedRunningAsync | — | — | — | 7 / 6 / 8f055f87 |
| [AgentTaskScopedListEndpointTests](../../../tests/Antiphon.Tests/Application/AgentTaskScopedListEndpointTests.cs) | RepoPath, repoPath in TaskRow | — | — | — | — |
| [AgentTaskScopedListTests](../../../tests/Antiphon.Tests/Application/AgentTaskScopedListTests.cs) | RepoPath, repoPath in TaskRow | — | — | — | — |
| [AgentTaskServiceIntegrationTests](../../../tests/Antiphon.Tests/Application/AgentTaskServiceIntegrationTests.cs) | .gitattributes | — | — | — | — |
| [AgentTaskSettlementRaceTests](../../../tests/Antiphon.Tests/Application/AgentTaskSettlementRaceTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [AgentTaskStallEscalationTests](../../../tests/Antiphon.Tests/Application/AgentTaskStallEscalationTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [AgentTaskStandingAgentDispatchTests](../../../tests/Antiphon.Tests/Application/AgentTaskStandingAgentDispatchTests.cs) | RepoPath, directory walk, repoPath in T6_a_disabled_or_unvalidated_profile_fails_before_worktree_or_session | — | — | — | 24 / 10 / 8f055f87 |
| [AgentTaskWorktreeBaseCreateTests](../../../tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseCreateTests.cs) | RepoPath, directory walk in T0442_V13_source_identifier_has_existing_guid_and_short_id_rules | — | — | — | 138 / 23 / 79b742de |
| [AgentTaskWorktreeBaseMigrationTests](../../../tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseMigrationTests.cs) | RepoPath in TaskRow | — | — | — | 23 / 1 / 8f055f87 |
| [AgentTaskWorktreeBaseResolverTests](../../../tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseResolverTests.cs) | RepoPath, directory walk in T0442_V03_card_guid_and_git_common_directory_define_identity | — | — | — | 290 / 67 / 79b742de |
| [AgentTaskWorktreeBaseSyncDebtTests](../../../tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseSyncDebtTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 58 / 10 / 79b742de |
| [AgentTaskWorktreeContinuityTests](../../../tests/Antiphon.Tests/Application/AgentTaskWorktreeContinuityTests.cs) | RepoPath, directory walk, tracked path in T0442_V27_git_facts_keep_the_creation_base_after_adoption | — | — | — | 283 / 8 / 79b742de |
| [AgentTaskWorktreeLockOutcomeTests](../../../tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs) | RepoPath in AddUnlandedSiblingAsync | — | — | — | — |
| [AgentWorkspaceProvisionerTests](../../../tests/Antiphon.Tests/Application/AgentWorkspaceProvisionerTests.cs) | .claude/settings.json, AGENTS.md | — | — | — | — |
| [BlockedReplyReportRetentionTests](../../../tests/Antiphon.Tests/Application/BlockedReplyReportRetentionTests.cs) | RepoPath, tracked path in CreateAsync | 25/0/0 · 56.7 [20261126-074134-4c4f/CP-5](#linux-receipt-keys) | — | — | 175 / 19 / 79b742de |
| [BlockedTaskParkDeliveryTests](../../../tests/Antiphon.Tests/Application/BlockedTaskParkDeliveryTests.cs) | FindRepoRoot, RepoPath, RepoRoot in StartAsync | — | — | — | 611 / 6 / 79b742de |
| [BlockedTaskParkReplyAdmissionTests](../../../tests/Antiphon.Tests/Application/BlockedTaskParkReplyAdmissionTests.cs) | RepositoryRoot, tracked path in CreateEvidenceFile | — | — | — | 1068 / 63 / 79b742de |
| [BlockedTaskParkResumeTests](../../../tests/Antiphon.Tests/Application/BlockedTaskParkResumeTests.cs) | RepoPath in C1065_ResumePreservesSourceAndAdmissionRefusals | — | — | — | 553 / 7 / 8f055f87 |
| [BlockedTaskParkStateTests](../../../tests/Antiphon.Tests/Application/BlockedTaskParkStateTests.cs) | HasPendingModelChanges in C1065_EpisodeIdentitySurvivesRestart | — | — | — | 26 / 2 / 8f055f87 |
| [BlockedTaskSyncRecoveryTests](../../../tests/Antiphon.Tests/Application/BlockedTaskSyncRecoveryTests.cs) | RepoPath in C1802_LeaseBusyCompleteReportReleasesItsSeatWithParkingOff | — | — | — | 211 / 4 / 8f055f87 |
| [BoardServiceIntegrationTests](../../../tests/Antiphon.Tests/Application/BoardServiceIntegrationTests.cs) | directory walk, repoPath in NewProject | 9/0/0 · 12.6 [20261123-053028-07ea/CP-4](#linux-receipt-keys) | — | — | 12 / 9 / 8f055f87 |
| [BootWedgeRelaunchParityTests](../../../tests/Antiphon.Tests/Application/BootWedgeRelaunchParityTests.cs) | directory walk in DeleteTree | — | — | — | 10 / 2 / 8f055f87 |
| [C0824BranchRefsDeliveryTests](../../../tests/Antiphon.Tests/Application/C0824BranchRefsDeliveryTests.cs) | RepoPath in Task_ | — | — | — | — |
| [C0824DesktopLegacyReleaseNativeTests](../../../tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseNativeTests.cs) | src/** | — | — | — | — |
| [C0824DesktopLegacyReleasePolicyTests](../../../tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs) | RepoPath, directory walk, tracked path in AssertStatusReadHoldsTree | — | — | — | — |
| [C0824DesktopLegacyReleaseSwitchTests](../../../tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseSwitchTests.cs) | RepoPath, git ls-files in Late_switch_on_ref_only_mirror_continuation_sends_no_remove | 10/0/0 · 25.8 [20261106-065517-9cdf/REP-1](#linux-receipt-keys) | — | — | — |
| [C0824PublishedBranchDeletionTests](../../../tests/Antiphon.Tests/Application/C0824PublishedBranchDeletionTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [C0824RefOnlyBoundaryTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyBoundaryTests.cs) | RepoPath in CreateAsync | — | — | — | — |
| [C0824RefOnlyCleanupTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyCleanupTests.cs) | RepoPath, tracked path in Persistent_reports_survive_ref_only_cleanup | — | — | — | — |
| [C0824RefOnlyContinuationTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyContinuationTests.cs) | RepoPath, git ls-files, tracked path in Park_resume_never_adds_a_desktop_worktree | — | — | — | — |
| [C0824RefOnlyDebtTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyDebtTests.cs) | RepoPath, tracked path in Recovery_confirms_only_recorded_sha_and_attempt | — | — | — | — |
| [C0824RefOnlyDeliverableTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyDeliverableTests.cs) | docs/** | — | — | — | — |
| [C0824RefOnlyGateTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyGateTests.cs) | .claude/settings.json | — | — | — | — |
| [C0824RefOnlyIdentityTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyIdentityTests.cs) | GetMigrations, RepoPath, tracked path in Missing_repository_never_uses_identity_as_git_cwd | — | — | — | — |
| [C0824RefOnlyLandingTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyLandingTests.Delivery.cs) | RepoPath in Late_land_uses_pinned_source_without_touching_decoy | — | — | — | — |
| [C0824RefOnlyNativeTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyNativeTests.cs) | RepoPath, tracked path in Windows_two_tasks_and_case_normalization_never_alias | — | — | — | — |
| [C0824RefOnlyPreparationTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyPreparationTests.cs) | RepoPath, tracked path in NewTask | — | — | — | — |
| [C0824RefOnlyProducerLifecycleTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyProducerLifecycleTests.Lifecycle.cs) | RepoPath, tracked path in CreateAsync | — | — | — | — |
| [C0824RefOnlyProvisionTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyProvisionTests.cs) | RepoPath, tracked path in NewTask | — | — | — | — |
| [C0824RefOnlyReadTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyReadTests.cs) | RepoPath, tracked path in CreateAsync | — | — | — | — |
| [C0824RefOnlyReceiptTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlyReceiptTests.cs) | RepoPath, tracked path in CreateAsync | — | — | — | — |
| [C0824RefOnlySyncTests](../../../tests/Antiphon.Tests/Application/C0824RefOnlySyncTests.cs) | RepoPath, tracked path in Pushed_tip_advances_only_owned_ref | — | — | — | — |
| [C0824WindowsSlotCheckoutTests](../../../tests/Antiphon.Tests/Application/C0824WindowsSlotCheckoutTests.cs) | RepoPath in Admitted_windows_runner_records_a_sentinel_and_no_card_task_directory | — | — | — | — |
| [C0824WorkspaceSlotRoleGateTests](../../../tests/Antiphon.Tests/Application/C0824WorkspaceSlotRoleGateTests.cs) | .gitignore | 3/0/0 · 48.1 [20261125-142218-704b/CP-2](#linux-receipt-keys) | — | — | — |
| [C0824WorkspaceSlotRoleSettingsTests](../../../tests/Antiphon.Tests/Application/C0824WorkspaceSlotRoleSettingsTests.cs) | .claude/settings.json | — | — | — | — |
| [C0824WorkspaceSlotServerTests](../../../tests/Antiphon.Tests/Application/C0824WorkspaceSlotServerTests.cs) | .gitignore | 1/0/0 · 8.2 [20261126-074134-4c4f/CP-6](#linux-receipt-keys) | — | — | — |
| [C1677DiskAlarmTests](../../../tests/Antiphon.Tests/Application/C1677DiskAlarmTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [C1677RetentionServerTests](../../../tests/Antiphon.Tests/Application/C1677RetentionServerTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [C1677TempSweepServerTests](../../../tests/Antiphon.Tests/Application/C1677TempSweepServerTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [C1708ReflogSalvageTests](../../../tests/Antiphon.Tests/Application/C1708ReflogSalvageTests.cs) | RepoPath, RepoRoot, directory walk in SalvageSourceAsync | 30/0/0 · 78.9 [20261106-220208-f4c6/CP-4](#linux-receipt-keys) | — | — | — |
| [C1729ParkFingerprintTests](../../../tests/Antiphon.Tests/Application/C1729ParkFingerprintTests.cs) | tracked path in Digest_matches_the_pinned_vector | — | — | — | — |
| [CapacityAdmissionLifecycleTests](../../../tests/Antiphon.Tests/Application/CapacityAdmissionLifecycleTests.C1679.cs) | RepoPath, directory walk, repoPath in QueuedWorktree | — | — | — | — |
| [CapacityLockOrderTests](../../../tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [CapacityRecoveryRefusalTests](../../../tests/Antiphon.Tests/Application/CapacityRecoveryRefusalTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [CapacityRecoveryTaskTests](../../../tests/Antiphon.Tests/Application/CapacityRecoveryTaskTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [Card1619ExitConsumerTests](../../../tests/Antiphon.Tests/Application/Card1619ExitConsumerTests.cs) | FindRepoRoot, RepoRoot, repoRoot in Create | — | — | — | — |
| [Card1619ExitPersistenceTests](../../../tests/Antiphon.Tests/Application/Card1619ExitPersistenceTests.cs) | GetMigrations in Migration_preserves_old_rows_and_nullable_contract | — | — | — | — |
| [Card1619StopAttributionTests](../../../tests/Antiphon.Tests/Application/Card1619StopAttributionTests.cs) | FindRepoRoot, RepoRoot, repoRoot in Create | — | — | — | — |
| [CardClosedAttentionTests](../../../tests/Antiphon.Tests/Application/CardClosedAttentionTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 23 / 4 / 8f055f87 |
| [CardCorrectionIntegrationTests](../../../tests/Antiphon.Tests/Application/CardCorrectionIntegrationTests.cs) | directory walk, repoPath in NewProject | — | — | — | — |
| [CardDiagnosisSweepTests](../../../tests/Antiphon.Tests/Application/CardDiagnosisSweepTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [CardDoneWorktreeCleanupTests](../../../tests/Antiphon.Tests/Application/CardDoneWorktreeCleanupTests.cs) | RepoPath, tracked path in C1213_OwnershipBothForms | — | — | — | 477 / 20 / 8f055f87 |
| [CardFileGitFailureAcceptanceTests](../../../tests/Antiphon.Tests/Application/CardFileGitFailureAcceptanceTests.cs) | directory walk in DeleteTree | — | — | — | 40 / 7 / 8f055f87 |
| [CardFilePrivacyConcurrencyTests](../../../tests/Antiphon.Tests/Application/CardFilePrivacyConcurrencyTests.cs) | directory walk, git ls-files, tracked path in Revocation_waits_for_inflight_commit_then_next_sync_removes_export | — | — | — | 34 / 10 / 8f055f87 |
| [CardFilePrivacyMigrationTests](../../../tests/Antiphon.Tests/Application/CardFilePrivacyMigrationTests.cs) | GetMigrations in Upgrade_backfills_existing_rows_without_reinterpreting_old_snapshots | — | — | — | 13 / 1 / 8f055f87 |
| [CardFilePrivacyPersistenceTests](../../../tests/Antiphon.Tests/Application/CardFilePrivacyPersistenceTests.cs) | .gitignore | — | — | — | 2 / 2 / 8f055f87 |
| [CardPlatformTests](../../../tests/Antiphon.Tests/Application/TaskPlatformPersistenceTests.cs) | FindRepoRoot, RepoRoot, repoRoot in SeedBoardAsync | — | — | — | 14 / 3 / 8f055f87 |
| [CardRelationshipSchemaTests](../../../tests/Antiphon.Tests/Application/CardRelationshipSchemaTests.cs) | GetMigrations, HasPendingModelChanges in C100_Duplicate_pair_is_refused_by_the_unique_index | — | — | — | 14 / 3 / 8f055f87 |
| [CardReviewServiceIntegrationTests](../../../tests/Antiphon.Tests/Application/CardReviewServiceIntegrationTests.cs) | RepoPath, repoPath, tracked path in CardPrApi_open_pushes_branch_and_creates_pr | — | — | — | 8 / 8 / 8f055f87 |
| [CardSpawnModelArgumentTests](../../../tests/Antiphon.Tests/Application/CardSpawnModelArgumentTests.cs) | directory walk in DeleteDirectoryBestEffort | — | — | — | 48 / 15 / 8f055f87 |
| [CardTaskSettlementTests](../../../tests/Antiphon.Tests/Application/CardTaskSettlementTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 30 / 8 / 8f055f87 |
| [CardThreadEndpointTests](../../../tests/Antiphon.Tests/Application/CardThreadEndpointTests.cs) | CLAUDE.md, server/Program.cs | — | — | — | — |
| [CardThreadServiceTests](../../../tests/Antiphon.Tests/Application/CardThreadServiceTests.cs) | RepoRoot, directory walk in A_card_with_no_repository_says_nobody_asked_rather_than_reporting_nothing | — | — | — | 46 / 12 / 8f055f87 |
| [CardVerificationPolicyTests](../../../tests/Antiphon.Tests/Application/CardVerificationPolicyTests.cs) | directory walk in DeleteTree | — | — | — | 30 / 5 / 8f055f87 |
| [CardWorktreeMirrorCleanupTests](../../../tests/Antiphon.Tests/Application/CardWorktreeMirrorCleanupTests.Recovery.cs) | directory walk in C1032_ReportAbsentAllowsCleanup | — | — | — | — |
| [ChannelFollowUpAttachmentTests](../../../tests/Antiphon.Tests/Application/ChannelFollowUpAttachmentTests.cs) | RepoPath in SeedBundleTaskAsync | — | — | — | 57 / 26 / 79b742de |
| [ChannelInboundRecoveryTests](../../../tests/Antiphon.Tests/Application/ChannelInboundRecoveryTests.C1716.cs) | directory walk in C593_Restart_DrainsToEligibleAndBusyRecipients | — | — | — | — |
| [ChannelMachineTurnTextTests](../../../tests/Antiphon.Tests/Application/ChannelMachineTurnTextTests.cs) | RepoPath in SeedBundleTaskAsync | — | — | — | — |
| [ChannelOutboundCaptureTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundCaptureTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [ChannelOutboundComposedTransportTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundComposedTransportTests.cs) | docs/features/012-review-engine/design.md, tests/Antiphon.Tests/Fixtures/Card0418/combined.pdf, tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | 4 / 1 / 8f055f87 |
| [ChannelOutboundDeadlineTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundDeadlineMatrixTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | — |
| [ChannelOutboundDeliveryTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundDeliveryTests.Identity.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json, docs/features/012-review-engine/design.md, tests/Antiphon.Tests/Fixtures/Card0418/combined.pdf | — | — | — | — |
| [ChannelOutboundDiscoveryTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundDiscoveryTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 22 / 20 / 8f055f87 |
| [ChannelOutboundDispatchIntegrationTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundDispatchIntegrationTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/combined.pdf, tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [ChannelOutboundDurabilitySchemaTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundDurabilitySchemaTests.cs) | GetMigrations, HasPendingModelChanges in C519_Upgrade_preserves_ordinals_and_history | — | — | — | 26 / 4 / 8f055f87 |
| [ChannelOutboundEndpointTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundEndpointTests.cs) | directory walk in Legacy_renderer_keys_warn_once_on_fresh_host_without_browser | — | — | — | 6 / 2 / 8f055f87 |
| [ChannelOutboundFailureRecordingTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundFailureRecordingTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 8 / 8 / 8f055f87 |
| [ChannelOutboundMaterializationTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundMaterializationTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json, docker-compose.dev.yml, docker-compose.runner-grok.yml … | — | — | — | — |
| [ChannelOutboundMetadataRepairTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundMetadataRepairTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 7 / 6 / 8f055f87 |
| [ChannelOutboundRecoveryTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundRecoveryTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/combined.pdf, tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | 2/0/0 · 87.5 [20261125-142218-704b/CP-2](#linux-receipt-keys) | — | — | 201 / 18 / 8f055f87 |
| [ChannelOutboundRetentionTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundRetentionTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 6 / 6 / 8f055f87 |
| [ChannelOutboundRetryPolicyTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundRetryPolicyTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [ChannelOutboundTrailingRecoveryTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundTrailingRecoveryTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 10 / 8 / 8f055f87 |
| [ChannelOutboundUnifiedPathTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundUnifiedPathTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 19 / 8 / 8f055f87 |
| [ChannelOutboundUnifiedTransportTests](../../../tests/Antiphon.Tests/Application/ChannelOutboundUnifiedTransportTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | 1277 / 17 / 79b742de |
| [ChannelPromptCorrelationTests](../../../tests/Antiphon.Tests/Application/ChannelPromptCorrelationTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/card0584/grok-turn.jsonl | — | — | — | — |
| [ChannelPromptCorrelationUnitTests](../../../tests/Antiphon.Tests/Application/ChannelPromptCorrelationUnitTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/card0584/claude-prompts.jsonl, tests/Antiphon.Tests/Agents/Fixtures/card0584/codex-item.jsonl, tests/Antiphon.Tests/Agents/Fixtures/card0584/codex-flat.jsonl … | — | — | — | — |
| [ChannelReplyDurabilityTests](../../../tests/Antiphon.Tests/Application/ChannelReplyDurabilityTests.cs) | tests/Antiphon.SessionRunner.Tests/Fixtures/grok-transport-error.jsonl | — | — | — | 25 / 25 / 79b742de |
| [CheckCompactionAutomaticRestartTests](../../../tests/Antiphon.Tests/Application/CheckCompactionAutomaticRestartTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [CheckCompactionContinuationTests](../../../tests/Antiphon.Tests/Application/CheckCompactionContinuationTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 9 / 2 / 8f055f87 |
| [CheckCompactionCrashTests](../../../tests/Antiphon.Tests/Application/CheckCompactionCrashTests.cs) | directory walk in WaitForHeldAsync | — | — | — | 102 / 9 / 79b742de |
| [CheckInterpreterProvisionerTests](../../../tests/Antiphon.Tests/Application/CheckInterpreterProvisionerTests.cs) | .claude/settings.json | — | — | — | — |
| [CheckNoteDeliveryHandoffTests](../../../tests/Antiphon.Tests/Application/CheckNoteDeliveryHandoffTests.H5.cs) | directory walk, tracked path in ReadSpilledNoteAsync | — | — | — | — |
| [CheckSpecialistLaunchPolicyTests](../../../tests/Antiphon.Tests/Application/CheckSpecialistLaunchPolicyTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [CheckpointSourceApprovalTests](../../../tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs) | GetMigrations, RepoPath, directory walk in SeedRecoveryAsync | — | — | — | 491 / 4 / 8f055f87 |
| [ClaudeCredentialProbeDispatcherTests](../../../tests/Antiphon.Tests/Application/ClaudeCredentialProbeDispatcherTests.cs) | directory walk in DeleteTree | — | — | — | 47 / 6 / 8f055f87 |
| [ClaudeEffortByRoleDispatchTests](../../../tests/Antiphon.Tests/Application/ClaudeEffortByRoleDispatchTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [ClosedCardSweepTests](../../../tests/Antiphon.Tests/Application/ClosedCardSweepTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 8 / 2 / 8f055f87 |
| [ClosingReportNudgeLifecycleTests](../../../tests/Antiphon.Tests/Application/ClosingReportNudgeLifecycleTests.cs) | directory walk in DeleteTree | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Application.Codex

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [CodexCliObservationGapTests](../../../tests/Antiphon.Tests/Application/CodexCliObservationGapTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 439 / 7 / 8f055f87 |
| [CodexCliObservationTests](../../../tests/Antiphon.Tests/Application/CodexCliObservationTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 529 / 8 / 8f055f87 |

### Antiphon.Tests / Antiphon.Tests.Application

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [CodexDelegateDispatchTests](../../../tests/Antiphon.Tests/Application/CodexDelegateDispatchTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [CodexPhoneHomeCreateTests](../../../tests/Antiphon.Tests/Application/CodexPhoneHomeCreateTests.cs) | FindRepoRoot, RepoRoot, repoRoot in SeedFailedRemoteCodexAsync | — | — | — | 51 / 6 / 8f055f87 |
| [CommitOnSettlePolicyTests](../../../tests/Antiphon.Tests/Application/CommitOnSettlePolicyTests.cs) | directory walk in DeleteTree | — | — | — | 16 / 12 / 8f055f87 |
| [CompletionHeaderOverlapTests](../../../tests/Antiphon.Tests/Application/DelegationScopePolicyTests.cs) | tracked path in Task | — | — | — | — |
| [CompletionWarningDeliveryTests](../../../tests/Antiphon.Tests/Application/CompletionWarningDeliveryTests.cs) | RepoPath, directory walk, tracked path in SettlePlanAsync | — | — | — | 233 / 11 / 79b742de |
| [ComplexityCreateTests](../../../tests/Antiphon.Tests/Application/ComplexityCreateTests.cs) | directory walk in DeleteTree | — | — | — | 112 / 14 / 8f055f87 |
| [ComplexityDispatcherTests](../../../tests/Antiphon.Tests/Application/ComplexityDispatcherTests.cs) | directory walk in DeleteTree | — | — | — | 64 / 9 / 8f055f87 |
| [ComplexityWallRerouteTests](../../../tests/Antiphon.Tests/Application/ComplexityWallRerouteTests.cs) | directory walk in DeleteTree | — | — | — | 83 / 11 / 8f055f87 |
| [ContinuationSettlementTests](../../../tests/Antiphon.Tests/Application/ContinuationSettlementTests.cs) | directory walk, tracked path in DeleteTree | — | — | — | 295 / 10 / 79b742de |
| [DataRetentionServiceTests](../../../tests/Antiphon.Tests/Application/DataRetentionServiceTests.cs) | directory walk in C768_ConfirmedPayloadExpiresAfterReceiptGrace | — | — | — | — |
| [DefaultRunnerCreateTests](../../../tests/Antiphon.Tests/Application/DefaultRunnerCreateTests.cs) | FindRepoRoot, RepoRoot, repoRoot in Create | — | — | — | 33 / 7 / 8f055f87 |
| [DefaultRunnerEligibilityTests](../../../tests/Antiphon.Tests/Application/DefaultRunnerEligibilityTests.cs) | FindRepoRoot, RepoRoot, repoRoot in Create | — | — | — | 9 / 3 / 8f055f87 |
| [DefaultRunnerPinTests](../../../tests/Antiphon.Tests/Application/DefaultRunnerPinTests.cs) | FindRepoRoot, RepoPath, RepoRoot in SourceLanding_uses_selected_host_custody | — | — | — | 43 / 6 / 8f055f87 |
| [DefaultRunnerRerouteTests](../../../tests/Antiphon.Tests/Application/DefaultRunnerRerouteTests.cs) | directory walk in DeleteTree | — | — | — | 102 / 16 / 8f055f87 |
| [DelegateBranchDeliveryTests](../../../tests/Antiphon.Tests/Application/DelegateBranchDeliveryTests.cs) | RepoPath, tracked path in C1213_OldRunnerReceivesLegacy | — | — | — | 371 / 7 / 79b742de |
| [DelegateCheckProbeTests](../../../tests/Antiphon.Tests/Application/DelegateCheckProbeTests.cs) | server/Application/Services/DelegateCheckProbe.cs | — | — | — | — |
| [DelegateScriptWorktreeBaseTests](../../../tests/Antiphon.Tests/Application/DelegateScriptWorktreeBaseTests.cs) | RepoPath, directory walk in SeedKeptSiblingAsync | — | — | — | 20 / 4 / 8f055f87 |
| [DelegationCapabilityTests](../../../tests/Antiphon.Tests/Application/DelegationCapabilityTests.cs) | tracked path in capability_prefix_neighbour_is_not_within_root | — | — | — | 18 / 20 / 8f055f87 |
| [DelegationReportFormatterTests](../../../tests/Antiphon.Tests/Application/DelegationUnitTests.cs) | server/Bundles/delegate-basics.md | — | — | — | — |
| [DelegationScopeHoldTests](../../../tests/Antiphon.Tests/Application/DelegationScopeHoldTests.cs) | client/src/App.tsx | — | — | — | — |
| [DelegationScopeLeaseTests](../../../tests/Antiphon.Tests/Application/DelegationUnitTests.cs) | client/src/App.tsx | — | — | — | — |
| [DelegationWorkspaceBoundaryTests](../../../tests/Antiphon.Tests/Application/DelegationUnitTests.cs) | tracked path in directory_inside_an_allowed_root_is_within_it | — | — | — | — |
| [DelegationWorktreeTests](../../../tests/Antiphon.Tests/Application/DelegationWorktreeC527ReceiptTests.cs) | .gitignore | — | — | — | — |
| [DeliverableBundleServiceTests](../../../tests/Antiphon.Tests/Application/DeliverableBundleServiceTests.cs) | src/**, docs/** | — | — | — | — |
| [DesktopLaunchMemoryAdmissionTests](../../../tests/Antiphon.Tests/Application/DesktopLaunchMemoryAdmissionTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [DiagnoseProvisionerTests](../../../tests/Antiphon.Tests/Application/DiagnoseProvisionerTests.cs) | .claude/settings.json | — | — | — | 10 / 12 / 8f055f87 |
| [DiagnosticsBundleServiceTests](../../../tests/Antiphon.Tests/Application/DiagnosticsBundleServiceTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json, server/** | 2/0/0 · 25.9 [20261123-053028-07ea/CP-4](#linux-receipt-keys) | — | — | 10 / 2 / 8f055f87 |
| [DispatchBaseInspectionDiagnosticsTests](../../../tests/Antiphon.Tests/Application/DispatchBaseInspectionDiagnosticsTests.cs) | RepoPath in SourceAsync | — | — | — | 190 / 12 / 79b742de |
| [DispatchBaseNotificationTests](../../../tests/Antiphon.Tests/Application/DispatchBaseNotificationTests.Coverage.cs) | GetMigrations, RepoPath, directory walk in C1349_InventoryOverflowKeepsProjectDefaultObservation | — | — | — | — |
| [DispatchConcurrencyAdmissionTests](../../../tests/Antiphon.Tests/Application/DispatchConcurrencyAdmissionTests.cs) | directory walk in DeleteTree | — | — | — | 29 / 8 / 8f055f87 |
| [DispatchConcurrencyDispatchTests](../../../tests/Antiphon.Tests/Application/DispatchConcurrencyDispatchTests.cs) | directory walk in DeleteTree | — | — | — | 22 / 8 / 8f055f87 |
| [DispatchConcurrencyMigrationTests](../../../tests/Antiphon.Tests/Application/DispatchConcurrencyMigrationTests.cs) | GetMigrations in RewindAsync | — | — | — | 35 / 3 / 8f055f87 |
| [DispatchConcurrencyPipelineTests](../../../tests/Antiphon.Tests/Application/DispatchConcurrencyPipelineTests.cs) | RepoPath in SeedTaskAsync | — | — | — | 15 / 5 / 8f055f87 |
| [DispatchHoldAttributionTests](../../../tests/Antiphon.Tests/Application/DispatchHoldAttributionTests.cs) | RepoPath, directory walk in SeedQueuedAsync | — | — | — | — |
| [DispatchHoldVisibilityTests](../../../tests/Antiphon.Tests/Application/DispatchHoldVisibilityTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 44 / 31 / 79b742de |
| [DispatchSiblingInspectionTests](../../../tests/Antiphon.Tests/Application/DispatchSiblingInspectionTests.cs) | RepoPath, directory walk in C1349_InspectionLimits | — | — | — | 154 / 32 / 79b742de |
| [DispatcherLeaseSpanTests](../../../tests/Antiphon.Tests/Application/DispatcherLeaseSpanTests.cs) | RepoPath, directory walk in SourceAsync | — | — | — | 28 / 3 / 8f055f87 |
| [DispatcherSiblingInspectionSchedulingTests](../../../tests/Antiphon.Tests/Application/DispatcherSiblingInspectionSchedulingTests.BriefRecovery.cs) | RepoPath, directory walk in QueueTask | — | — | — | — |
| [ExpectationEpisodeTests](../../../tests/Antiphon.Tests/Application/ExpectationEpisodeTests.cs) | RepoPath in AddFencedAsync | — | — | — | 44 / 5 / 8f055f87 |
| [ExpectationEscalationTests](../../../tests/Antiphon.Tests/Application/ExpectationEscalationTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | 63 / 9 / 8f055f87 |
| [ExpectationObservationAdapterTests](../../../tests/Antiphon.Tests/Application/ExpectationObservationAdapterTests.cs) | RepoPath, tracked path in C650_Live_dead_and_unknown_journals_do_not_grant_recovery | — | — | — | 167 / 12 / 8f055f87 |
| [ExpectationSnapshotTests](../../../tests/Antiphon.Tests/Application/ExpectationSnapshotTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 31 / 7 / 8f055f87 |
| [ExpectationWatchdogScenarioTests](../../../tests/Antiphon.Tests/Application/ExpectationWatchdogScenarioTests.cs) | RepoPath in C650_Overnight_fence_nudges_and_pages_without_task_notes | — | — | — | 35 / 4 / 8f055f87 |
| [GatedCommitServiceTests](../../../tests/Antiphon.Tests/Application/GatedCommitC527RecipeTests.cs) | .gitignore | — | — | — | 160 / 71 / 79b742de |
| [GhostSeatReleaseTests](../../../tests/Antiphon.Tests/Application/GhostSeatReleaseTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [GitIgnorePreviewTests](../../../tests/Antiphon.Tests/Application/GitIgnorePreviewTests.cs) | server/Program.cs, .gitignore | — | — | — | — |
| [GitWorkspaceServiceIndexLockTests](../../../tests/Antiphon.Tests/Application/GitWorkspaceServiceIndexLockTests.cs) | directory walk in DeleteTree | — | — | — | 12 / 3 / 8f055f87 |
| [GrokComposerScreenTests](../../../tests/Antiphon.Tests/Application/GrokComposerScreenTests.cs) | tests/Antiphon.Tests/Fixtures/grok-empty-composer.txt | — | — | — | — |
| [GrokCredentialProbeDispatcherTests](../../../tests/Antiphon.Tests/Application/GrokCredentialProbeDispatcherTests.cs) | directory walk in DeleteTree | — | — | — | 27 / 3 / 8f055f87 |
| [GrokDelegateDispatchTests](../../../tests/Antiphon.Tests/Application/GrokDelegateDispatchTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [GrokDelegateEndToEndTests](../../../tests/Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs) | directory walk in DeleteTree | — | — | — | 262 / 6 / 8f055f87 |
| [GrokDeliveryShapeTests](../../../tests/Antiphon.Tests/Application/GrokDeliveryShapeTests.cs) | tracked path in The_default_pointer_fuses_the_spill_path_with_the_next_line_when_joined | — | — | — | — |
| [GrokRulesAutoCompactionCalibrationTests](../../../tests/Antiphon.Tests/Application/GrokRulesAutoCompactionCalibrationTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [GrokRulesCompactionAcceptanceTests](../../../tests/Antiphon.Tests/Application/GrokRulesCompactionAcceptanceTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [GrokRulesCompactionRecoveryTests](../../../tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs) | tests/Antiphon.SessionRunner.Tests/Fixtures/grok-1.0.13-auto-compaction.jsonl | — | — | — | 11 / 11 / 8f055f87 |
| [GrokRulesCompactionTests](../../../tests/Antiphon.Tests/Application/GrokRulesCompactionTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | 6 / 2 / 8f055f87 |
| [GrokRulesDispatchAcceptanceTests](../../../tests/Antiphon.Tests/Application/GrokRulesDispatchAcceptanceTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [GrokRulesFailureTests](../../../tests/Antiphon.Tests/Application/GrokRulesFailureTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | 14 / 7 / 8f055f87 |
| [GrokRulesHerdrAcceptanceTests](../../../tests/Antiphon.Tests/Application/GrokRulesHerdrAcceptanceTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [GrokRulesInitializationTests](../../../tests/Antiphon.Tests/Application/GrokRulesInitializationTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | — |
| [GrokRulesLaunchRefusalTests](../../../tests/Antiphon.Tests/Application/GrokRulesLaunchRefusalTests.cs) | directory walk in DeleteDirectoryBestEffort | 3/0/2 · 3.2 [20261123-070247-05e4/CP-10](#linux-receipt-keys) | — | — | 10 / 5 / 8f055f87 |
| [GrokRulesLiveEvidenceTests](../../../tests/Antiphon.Tests/Application/GrokRulesLiveEvidenceTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/grok-rules-live-inline-1.0.13.json | — | — | — | — |
| [GrokRulesLiveMappedDispatchTests](../../../tests/Antiphon.Tests/Application/GrokRulesLiveMappedDispatchTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [GrokRulesNativeReadWireTests](../../../tests/Antiphon.Tests/Application/GrokRulesNativeReadWireTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [GrokRulesReplayMatrixTests](../../../tests/Antiphon.Tests/Application/GrokRulesReplayMatrixTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | — |
| [GrokRulesTransactionTests](../../../tests/Antiphon.Tests/Application/GrokRulesTransactionTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.SessionRunner.Tests/Fixtures/grok-1.0.13-auto-compaction.jsonl … | — | — | — | 6 / 3 / 8f055f87 |
| [GrokStartupReadyOrderingTests](../../../tests/Antiphon.Tests/Application/GrokStartupReadyOrderingTests.cs) | directory walk in Unready_launch_cleans_up_and_keeps_work_pending | — | — | — | 12 / 2 / 8f055f87 |
| [HerdrAlwaysOnChannelParityTests](../../../tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.StandingRecovery.cs) | tests/Antiphon.Tests/Agents/Fixtures/card1004/linux-startup-frames.json | 4/0/0 · 34.4 [20261123-175537-70a3/CP-7](#linux-receipt-keys) | — | — | 78 / 26 / 79b742de |
| [HerdrPlacementPreflightTests](../../../tests/Antiphon.Tests/Application/HerdrPlacementPreflightTests.cs) | directory walk in DeleteDirectoryBestEffort | — | — | — | — |
| [HerdrSupervisionBackoffTests](../../../tests/Antiphon.Tests/Application/HerdrSupervisionBackoffTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | 35/0/0 · 12.5 [20261123-053028-07ea/CP-4](#linux-receipt-keys) | — | — | — |
| [HomeTaskServiceIntegrationTests](../../../tests/Antiphon.Tests/Application/HomeTaskServiceIntegrationTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 56 / 15 / 8f055f87 |
| [HostCleanupOrchestrationTests](../../../tests/Antiphon.Tests/Application/HostCleanupOrchestrationTests.cs) | GetMigrations, HasPendingModelChanges in AddHostCleanup_upgrades_current_master_without_changing_existing_rows | — | — | — | 16 / 2 / 8f055f87 |
| [HostStatsEndpointTests](../../../tests/Antiphon.Tests/Application/HostStatsEndpointTests.cs) | tracked path in Hosts_stats_answers_the_cache_projection_with_counters | — | — | — | 9 / 5 / 8f055f87 |
| [InstructionFileStampTests](../../../tests/Antiphon.Tests/Application/InstructionFileStampTests.cs) | AGENTS.md, CLAUDE.md, docs/orchestration-loop.md … | — | — | — | — |
| [InterimVerificationLandGuardTests](../../../tests/Antiphon.Tests/Application/InterimVerificationLandGuardTests.cs) | directory walk, tracked path in .ctor | — | — | — | 106 / 13 / 79b742de |
| [InterimVerificationPolicyTests](../../../tests/Antiphon.Tests/Application/InterimVerificationPolicyTests.cs) | RepoPath, directory walk, tracked path in C544_SelectionPath | — | — | — | 95 / 19 / 8f055f87 |
| [LandAuditToolResolverTests](../../../tests/Antiphon.Tests/Application/LandAuditToolResolverTests.cs) | tools/Antiphon.LandGraphAudit/Program.cs, tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj, global.json … | — | — | — | — |
| [LandDispatchHandoffTests](../../../tests/Antiphon.Tests/Application/LandDispatchHandoffTests.cs) | directory walk in DeleteTree | — | — | — | 16 / 1 / 8f055f87 |
| [LandEvidenceWarningDeliveryTests](../../../tests/Antiphon.Tests/Application/LandEvidenceWarningDeliveryTests.cs) | tracked path in .ctor | — | — | — | — |
| [LandGeneratedArtifactTests](../../../tests/Antiphon.Tests/Application/LandGeneratedArtifactTests.cs) | scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj, Directory.Build.props, Directory.Build.targets … | — | — | — | — |
| [LandGraphAuditCleanupTests](../../../tests/Antiphon.Tests/Application/LandGraphAuditCleanupTests.cs) | directory walk, tracked path in ReadOnlyGitObjectsAreDeleted | — | — | — | — |
| [LandGraphCanaryToleranceTests](../../../tests/Antiphon.Tests/Application/LandGraphCanaryToleranceTests.cs) | tracked path in C1678_a_copied_file_missing_from_the_canary_is_refused_by_the_copy_set_alone | — | — | — | — |
| [LandMergeBranchWitnessTests](../../../tests/Antiphon.Tests/Application/LandMergeBranchWitnessTests.cs) | RepoPath, directory walk, repoPath in NewTask | — | — | — | — |
| [LandMergeWriterTests](../../../tests/Antiphon.Tests/Application/LandMergeWriterTests.cs) | RepoPath in C753_MergeHelperOnlyHoldsLandWithConfirmedLivePrompt | — | — | — | — |
| [LandRefRetentionTests](../../../tests/Antiphon.Tests/Application/LandRefRetentionTests.cs) | RepoPath, tracked path in SeedTask | — | — | — | — |
| [LandRefusalDeliveryWitnessTests](../../../tests/Antiphon.Tests/Application/LandRefusalDeliveryWitnessTests.cs) | tracked path in .ctor | — | — | — | 122 / 24 / 79b742de |
| [LandReplacementApprovalTests](../../../tests/Antiphon.Tests/Application/LandReplacementApprovalTests.cs) | RepoPath in AddReviewAsync | — | — | — | 266 / 5 / 8f055f87 |
| [LandRequestWriteDiagnosticTests](../../../tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs) | GetMigrations, RepoPath in C883_MigrationPreservesLegacyRows | — | — | — | 173 / 15 / 79b742de |
| [LandRestartReplayTests](../../../tests/Antiphon.Tests/Application/LandRestartReplayTests.cs) | RepoPath in C1379_OldHeldReplaysBeforeConcurrentNewRequest | — | — | — | 35 / 1 / 8f055f87 |
| [LandVerificationBuildSlotTests](../../../tests/Antiphon.Tests/Application/LandVerificationBuildSlotTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | — |
| [LandVerificationFailureMigrationTests](../../../tests/Antiphon.Tests/Application/LandVerificationFailureMigrationTests.cs) | GetMigrations, HasPendingModelChanges in C1995_UpIsAdditiveAndDownRefusesDataLoss | — | — | — | — |
| [LandVerificationLeaseWindowTests](../../../tests/Antiphon.Tests/Application/LandVerificationLeaseWindowTests.cs) | directory walk, tracked path in C1388_TrackedTreeChangeRefusesResume | — | — | — | 149 / 7 / 8f055f87 |
| [LandWarmArtifactsTests](../../../tests/Antiphon.Tests/Application/LandWarmArtifactsTests.cs) | scripts/fixtures/nightly/stopped-seed/owner.json, Directory.Build.props, tests/Shared/** | — | — | — | 111 / 12 / 8f055f87 |
| [LandingProtocolGuardTests](../../../tests/Antiphon.Tests/Application/LandingProtocolGuardTests.cs) | RepoPath, tracked path in TaskCoordinate | — | — | — | 64 / 23 / 79b742de |
| [LegacyCheckNotePublicationTests](../../../tests/Antiphon.Tests/Application/LegacyCheckNotePublicationTests.H5.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 91 / 17 / 79b742de |
| [MatrixChildReceiptTests](../../../tests/Antiphon.Tests/Application/MatrixChildReceiptTests.cs) | .gitignore | — | — | — | 18 / 4 / 8f055f87 |
| [MatrixDefaultComplexityCreateTests](../../../tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | 164 / 36 / 79b742de |
| [ModelAvailabilityCreateTests](../../../tests/Antiphon.Tests/Application/ModelAvailabilityCreateTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [ModelAvailabilityDispatcherTests](../../../tests/Antiphon.Tests/Application/ModelAvailabilityDispatcherTests.cs) | directory walk in DeleteTree | — | — | — | 4 / 3 / 8f055f87 |
| [ModelAvailabilityManualTests](../../../tests/Antiphon.Tests/Application/ModelAvailabilityManualTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [ModelLevelAliasDisplayTests](../../../tests/Antiphon.Tests/Application/DelegationKindDisplayTests.cs) | tracked path in NewTask | — | — | — | — |
| [MutationAdmissionTests](../../../tests/Antiphon.Tests/Application/MutationAdmissionTests.cs) | directory walk in DeleteTree | — | — | — | 25 / 25 / 79b742de |
| [MutationPipelineTests](../../../tests/Antiphon.Tests/Application/MutationPipelineTests.cs) | docs/superpowers/plans/2026-09-09-card-0470-code-mutation-split-plan.md | — | — | — | 8 / 8 / 8f055f87 |
| [OrchestratorInvestigationDetectorTests](../../../tests/Antiphon.Tests/Application/OrchestratorInvestigationDetectorTests.cs) | server/Application/Services/AgentControlService.cs, scripts/hooks/__tests__/fixtures/cefed08a-cold-assignment-policy.jsonl, scripts/hooks/__tests__/fixtures/cefed08a-cold-cleanup-script.jsonl … | — | — | — | — |
| [OrchestratorServiceIntegrationTests](../../../tests/Antiphon.Tests/Application/OrchestratorServiceIntegrationTests.cs) | directory walk in DeleteDirectoryBestEffort | — | — | — | — |
| [OrchestratorStateProjectionTests](../../../tests/Antiphon.Tests/Application/OrchestratorStateProjectionTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 95 / 14 / 8f055f87 |
| [OrchestratorWorkspaceLayoutTests](../../../tests/Antiphon.Tests/Application/OrchestratorWorkspaceLayoutTests.cs) | AGENTS.md, CLAUDE.md | — | — | — | — |
| [OrchestratorWorkspaceWarningTests](../../../tests/Antiphon.Tests/Application/OrchestratorWorkspaceWarningTests.cs) | AGENTS.md | 10/0/0 · 51.3 [20261125-142218-704b/CP-4](#linux-receipt-keys) | — | — | 9 / 5 / 8f055f87 |
| [OutboundConversionManifestTests](../../../tests/Antiphon.Tests/Application/OutboundConversionManifestTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/combined.pdf, tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [OutboundConversionManifestTestsPump](../../../tests/Antiphon.Tests/Application/OutboundConversionManifestTestsPump.cs) | tests/Antiphon.Tests/Fixtures/Card0418/combined.pdf, tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [OutboundConversionTaskTests](../../../tests/Antiphon.Tests/Application/OutboundConversionTaskTests.cs) | RepoPath in Ordinary_creation_links_one_pinned_internal_worker_without_source_inheritance | — | — | — | — |
| [OutputDistillationDeliveryTests](../../../tests/Antiphon.Tests/Application/OutputDistillationDeliveryTests.cs) | server/Program.cs | — | — | — | 328 / 78 / 79b742de |
| [OutputDistillationProducerTests](../../../tests/Antiphon.Tests/Application/OutputDistillationProducerTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [OutputDistillerProvisionerTests](../../../tests/Antiphon.Tests/Application/OutputDistillerProvisionerTests.cs) | .claude/settings.json | — | — | — | — |
| [ParkedMessageSweepServiceTests](../../../tests/Antiphon.Tests/Application/ParkedMessageSweepServiceTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [PendingSettlementDeliveryTests](../../../tests/Antiphon.Tests/Application/PendingSettlementDeliveryTests.cs) | RepoPath, tracked path in CreateAsync | — | — | — | — |
| [PhoneHomeCompletedCardTransportTests](../../../tests/Antiphon.Tests/Application/PhoneHomeCompletedCardTransportTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 31 / 2 / 8f055f87 |
| [PhoneHomeRollingRunnerTests](../../../tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerDrainTests.cs) | RepoPath, RepoRoot, tracked path in StartAsync | — | — | — | 97 / 30 / 79b742de |
| [PhoneHomeTaskCreateTests](../../../tests/Antiphon.Tests/Application/PhoneHomeTaskCreateTests.cs) | RepoRoot, repoRoot, tracked path in Service | — | — | — | 22 / 3 / 8f055f87 |
| [PhoneHomeTaskDispatchProjectionTests](../../../tests/Antiphon.Tests/Application/PhoneHomeTaskDispatchProjectionTests.DispatchSave.cs) | directory walk in DeleteTree | — | — | — | 33 / 18 / 8f055f87 |
| [PhoneHomeTranscriptIncidentTests](../../../tests/Antiphon.Tests/Application/PhoneHomeTranscriptIncidentTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | 32 / 5 / 8f055f87 |
| [PinnedAgentKindTests](../../../tests/Antiphon.Tests/Application/PinnedAgentKindTests.cs) | directory walk in DeleteTree | — | — | — | 8 / 4 / 8f055f87 |
| [PipelineHandoffPilotTests](../../../tests/Antiphon.Tests/Application/PipelineHandoffPilotTests.cs) | directory walk, tracked path in DeleteTree | — | — | — | 196 / 12 / 8f055f87 |
| [PolicyRefreshServiceTests](../../../tests/Antiphon.Tests/Application/PolicyRefreshServiceTests.cs) | AGENTS.md | — | — | — | — |
| [PoolClaudeEffortMigrationTests](../../../tests/Antiphon.Tests/Application/PoolClaudeEffortMigrationTests.cs) | HasPendingModelChanges in C1491_nullable_stamp_roundtrips_and_legacy_rows_stay_unknown | — | — | — | 5 / 1 / 8f055f87 |
| [PoolProviderProjectionStampTests](../../../tests/Antiphon.Tests/Application/PoolProviderProjectionStampTests.cs) | directory walk in DeleteTree | — | — | — | 25 / 3 / 8f055f87 |
| [PostLandMutationAdmissionTests](../../../tests/Antiphon.Tests/Application/PostLandMutationAdmissionTests.cs) | GetMigrations, RepoPath, directory walk in C478_V01_PublicSurfaceAndMigration | — | — | — | 561 / 27 / 79b742de |
| [PostLandMutationCleanupTests](../../../tests/Antiphon.Tests/Application/PostLandMutationCleanupTests.cs) | RepoPath, directory walk in C478_G091_Repository | — | — | — | — |
| [PostLandMutationCustodyTests](../../../tests/Antiphon.Tests/Application/PostLandMutationCustodyTests.cs) | RepoPath, directory walk in C478_ConfirmedSourceCreatesExactSnapshotAndRetainsIdentity | — | — | — | 1225 / 56 / 79b742de |
| [PostLandMutationDeliveryTests](../../../tests/Antiphon.Tests/Application/PostLandMutationDeliveryTests.CompletionRecovery.cs) | RepoPath, directory walk in BuildAsync | — | — | — | — |
| [PostLandMutationWorktreeTests](../../../tests/Antiphon.Tests/Application/PostLandMutationWorktreeTests.cs) | RepoPath, directory walk in C478_G064_RetryIdentity | — | — | — | 673 / 33 / 79b742de |
| [PreDispatchFailureDeliveryTests](../../../tests/Antiphon.Tests/Application/PreDispatchFailureDeliveryTests.cs) | RepoPath, directory walk in Preflight_failure_obligation_commits_with_its_claim | — | — | — | — |
| [ProjectReadinessBatchTests](../../../tests/Antiphon.Tests/Application/ProjectReadinessBatchTests.cs) | directory walk in DeleteTree | 2/0/0 · 13.0 [20261123-053028-07ea/CP-2](#linux-receipt-keys) | — | — | 10 / 2 / 8f055f87 |
| [ProjectReadinessTests](../../../tests/Antiphon.Tests/Application/ProjectReadinessTests.PathIdentity.cs) | AGENTS.md, CLAUDE.md, src/** | 83/0/0 · 330 [20261123-053028-07ea/CP-2](#linux-receipt-keys) | — | — | — |
| [ProjectSetupServiceTests](../../../tests/Antiphon.Tests/Application/ProjectSetupServiceTests.cs) | .gitignore, src/** | 11/0/0 · 18.2 [20261123-053028-07ea/CP-2](#linux-receipt-keys) | — | — | 21 / 11 / 8f055f87 |
| [ProviderQuotaRefusalAcceptanceTests](../../../tests/Antiphon.Tests/Application/ProviderQuotaRefusalAcceptanceTests.cs) | docs/superpowers/plans/fixtures/card-0719-transcript-tails.json | — | — | — | 29 / 23 / 8f055f87 |
| [PublishedCardCompletionTests](../../../tests/Antiphon.Tests/Application/PublishedCardCompletionTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 137 / 7 / 8f055f87 |
| [ReceiptFailureDeliveryTests](../../../tests/Antiphon.Tests/Application/ReceiptFailureDeliveryTests.cs) | directory walk in DeleteTree | — | — | — | 175 / 21 / 79b742de |
| [RefocusBriefIdentityTests](../../../tests/Antiphon.Tests/Application/RefocusBriefIdentityTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | 66 / 24 / 8f055f87 |
| [RemotePoolFollowUpAdmissionTests](../../../tests/Antiphon.Tests/Application/RemotePoolFollowUpAdmissionTests.cs) | FindRepoRoot, RepoRoot, repoRoot in CreateAsync | — | — | — | 16 / 3 / 8f055f87 |
| [RemoteWorkspacePreparerTests](../../../tests/Antiphon.Tests/Application/RemoteWorkspacePreparerTests.cs) | RepoPath in C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease | — | — | — | 162 / 27 / 79b742de |
| [RemoteWorktreeMirrorTests](../../../tests/Antiphon.Tests/Application/RemoteWorktreeMirrorTests.cs) | RepoPath in Task | — | — | — | — |
| [RepairSourceDispatchTests](../../../tests/Antiphon.Tests/Application/RepairSourceDispatchTests.cs) | RepoPath, directory walk in SeedOwnerAsync | — | — | — | 337 / 41 / 79b742de |
| [RepairSourceLandRefusalTests](../../../tests/Antiphon.Tests/Application/RepairSourceLandRefusalTests.cs) | directory walk in DeleteTree | — | — | — | 146 / 6 / 8f055f87 |
| [RepairSourceRecoveryLandingTests](../../../tests/Antiphon.Tests/Application/RepairSourceRecoveryLandingTests.cs) | RepoPath, directory walk in AddReviewAsync | — | — | — | 100 / 4 / 79b742de |
| [RepairSourceSettlementTests](../../../tests/Antiphon.Tests/Application/RepairSourceSettlementTests.cs) | directory walk in DeleteTree | — | — | — | 520 / 36 / 79b742de |
| [RepositoryMaintenanceTests](../../../tests/Antiphon.Tests/Application/RepositoryMaintenanceTests.cs) | RepoPath, directory walk, tracked path in SeedTask | — | — | — | — |
| [RequestedModelAliasMigrationTests](../../../tests/Antiphon.Tests/Application/RequestedModelAliasMigrationTests.cs) | GetMigrations, HasPendingModelChanges in UpIsAdditiveAndDownRefusesDataLoss | — | — | — | — |
| [RequiredSingleCandidateRefusalTests](../../../tests/Antiphon.Tests/Application/RequiredSingleCandidateRefusalTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | 28 / 6 / 8f055f87 |
| [ReviewEvidenceConsistencyTests](../../../tests/Antiphon.Tests/Application/ReviewEvidenceConsistencyTests.cs) | directory walk, tracked path in C788_BothWarningsCoexistWithOtherHeaderWarnings | — | — | — | 239 / 31 / 79b742de |
| [ReviewEvidenceDeliveryTests](../../../tests/Antiphon.Tests/Application/ReviewEvidenceDeliveryTests.cs) | directory walk, tracked path in DeleteTree | — | — | — | 230 / 17 / 79b742de |
| [ReviewEvidenceRebindingTests](../../../tests/Antiphon.Tests/Application/ReviewEvidenceRebindingTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 18 / 15 / 8f055f87 |
| [ReviewEvidenceRecoveryEndpointTests](../../../tests/Antiphon.Tests/Application/ReviewEvidenceRecoveryEndpointTests.cs) | RepoPath, tracked path in TaskCredentialAsync | — | — | — | 64 / 6 / 8f055f87 |
| [ReviewEvidenceRecoveryTests](../../../tests/Antiphon.Tests/Application/ReviewEvidenceRecoveryTests.cs) | RepoPath, tracked path in C1043_PreparedCoordinatesChanged | — | — | — | 239 / 21 / 79b742de |
| [ReviewEvidenceResettlementTests](../../../tests/Antiphon.Tests/Application/ReviewEvidenceResettlementTests.cs) | RepoPath, tracked path in CreateAsync | — | — | — | 358 / 10 / 79b742de |
| [ReviewEvidenceSettlementTests](../../../tests/Antiphon.Tests/Application/ReviewEvidenceSettlementTests.cs) | RepoPath, directory walk, tracked path in C807_WarningPreservesOtherWarnings | — | — | — | 72 / 11 / 79b742de |
| [ReviewLoopTests](../../../tests/Antiphon.Tests/Application/ReviewLoopTests.cs) | docs/** | — | — | — | 195 / 18 / 79b742de |
| [RolePolicyInvalidValuesCreateTests](../../../tests/Antiphon.Tests/Application/RolePolicyInvalidValuesCreateTests.cs) | FindRepo, RepoRoot, repoRoot in an_explicit_level_does_not_consult_an_undefined_policy_level | — | — | — | 16 / 7 / 8f055f87 |
| [RolePolicyToolBundleDispatchTests](../../../tests/Antiphon.Tests/Application/RolePolicyToolBundleDispatchTests.cs) | FindRepo, RepoRoot, repoRoot in .ctor | — | — | — | 16 / 8 / 8f055f87 |
| [RoutingExhaustedPlacementTests](../../../tests/Antiphon.Tests/Application/RoutingExhaustedPlacementTests.cs) | FindRepoRoot, RepoRoot, repoRoot in CreateAsync | — | — | — | 52 / 54 / 79b742de |
| [RoutingPinCandidateCreateTests](../../../tests/Antiphon.Tests/Application/RoutingPinCandidateCreateTests.cs) | directory walk in DeleteTree | — | — | — | 20 / 17 / 8f055f87 |
| [RoutingPinCandidateDispatchTests](../../../tests/Antiphon.Tests/Application/RoutingPinCandidateDispatchTests.cs) | directory walk in DeleteTree | — | — | — | 70 / 12 / 8f055f87 |
| [RoutingPinCreateTests](../../../tests/Antiphon.Tests/Application/RoutingPinCreateTests.cs) | directory walk in DeleteTree | — | — | — | 84 / 13 / 8f055f87 |
| [RoutingPinDispatcherTests](../../../tests/Antiphon.Tests/Application/RoutingPinDispatcherTests.cs) | directory walk in DeleteTree | — | — | — | 26 / 3 / 8f055f87 |
| [RunnerAlarmCoordinatorTests](../../../tests/Antiphon.Tests/Application/RunnerAlarmCoordinatorTests.cs) | RepoPath, directory walk, repoPath in journal_findings_are_published_per_repository_and_cleared_when_recovered | — | — | — | 12 / 11 / 8f055f87 |
| [RunnerAlarmHostedServiceTests](../../../tests/Antiphon.Tests/Application/RunnerAlarmHostedServiceTests.cs) | directory walk, tracked path in PlantDeadAsync | — | — | — | 8 / 6 / 8f055f87 |
| [RunnerCatalogueTests](../../../tests/Antiphon.Tests/Application/RunnerCatalogueTests.cs) | FindRepoRoot, RepoRoot, repoRoot in Create | — | — | — | 12 / 4 / 8f055f87 |
| [RunnerCompletionProgressTests](../../../tests/Antiphon.Tests/Application/RunnerCompletionProgressTests.cs) | RepoPath, tracked path in CreateAsync | — | — | — | 65 / 11 / 8f055f87 |
| [RunnerDefaultMigrationTests](../../../tests/Antiphon.Tests/Application/RunnerDefaultTests.cs) | FindRepoRoot, GetMigrations, RepoRoot in Persisted_clear_and_override_survive_legacy_edits_and_restart | — | — | — | 63 / 5 / 8f055f87 |
| [RunnerDefaultPlacementTests](../../../tests/Antiphon.Tests/Application/RunnerDefaultTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 60 / 9 / 8f055f87 |
| [RunnerDefaultSettingsTests](../../../tests/Antiphon.Tests/Application/RunnerDefaultTests.cs) | FindRepoRoot, RepoRoot, repoRoot in Create | — | — | — | 26 / 6 / 8f055f87 |
| [RunnerDefaultsWireTests](../../../tests/Antiphon.Tests/Application/RunnerDefaultTests.cs) | FindRepo, RepoRoot, repoRoot in Changing_defaults_does_not_move_queued_task | — | — | — | 35 / 5 / 8f055f87 |
| [RunnerGrokCredentialProbeDispatcherTests](../../../tests/Antiphon.Tests/Application/RunnerGrokCredentialProbeDispatcherTests.cs) | directory walk in DeleteTree | — | — | — | 22 / 3 / 8f055f87 |
| [RunnerRetireJobTests](../../../tests/Antiphon.Tests/Application/RunnerRetireJobTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | 32 / 4 / 8f055f87 |
| [RunnerSettlementSyncTests](../../../tests/Antiphon.Tests/Application/RunnerSettlementSyncTests.cs) | RepoPath, directory walk, tracked path in Competing_retirement_is_fenced_for_the_whole_sync | — | — | — | 441 / 30 / 79b742de |
| [RunnerSyncLeaseFairnessTests](../../../tests/Antiphon.Tests/Application/RunnerSyncLeaseFairnessTests.cs) | RepoPath, tracked path in CreateAsync | — | — | — | 183 / 7 / 8f055f87 |
| [RunnerTaskSettlementTests](../../../tests/Antiphon.Tests/Application/RunnerTaskSettlementTests.cs) | docs/** | — | — | — | 424 / 32 / 79b742de |
| [ScheduleCardActionTests](../../../tests/Antiphon.Tests/Application/ScheduleCardActionTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [ScopeDriftTests](../../../tests/Antiphon.Tests/Application/ScopeDriftTests.cs) | server/Application/Services/SessionMessageQueueService.cs, src/Antiphon.Agents.Pty/ComposerDeliveryEvidence.cs, server/Application/Services/AgentTaskService.cs … | — | — | — | — |
| [SessionMessageQueueCodexPointerTests](../../../tests/Antiphon.Tests/Application/SessionMessageQueueCodexPointerTests.cs) | .antiphon/fixtures/card-1521/partial-boot-pointers.approved.json | — | — | — | 43 / 27 / 79b742de |
| [SessionMessageQueueGrokPtyIntegrationTests](../../../tests/Antiphon.Tests/Application/SessionMessageQueueGrokPtyIntegrationTests.cs) | tests/Antiphon.Tests/Agents/Fixtures/card0778/startup-frames.json | — | — | — | 92 / 5 / 8f055f87 |
| [SessionMessageQueueSpillTests](../../../tests/Antiphon.Tests/Application/SessionMessageQueueSpillTests.cs) | directory walk in Now_mode_oversize_types_the_pointer_and_persists_no_queue_row | — | — | — | 10 / 7 / 8f055f87 |
| [SessionRuntimeActionRoutingTests](../../../tests/Antiphon.Tests/Application/SessionRuntimeActionRoutingTests.Finish.cs) | GetMigrations in C1259_review_recovery_uses_authorized_queue_identity_once | — | — | — | 115 / 35 / 79b742de |
| [SessionRuntimeActionTests](../../../tests/Antiphon.Tests/Application/SessionRuntimeActionTests.cs) | GetMigrations in C1259_commit_and_effect_admission_are_atomic | 30/0/0 · 246 [20261126-074134-4c4f/CP-2](#linux-receipt-keys) | — | — | 103 / 28 / 79b742de |
| [SessionTranscriptSummaryMigrationTests](../../../tests/Antiphon.Tests/Application/SessionTranscriptSummaryMigrationTests.cs) | GetMigrations in Current | — | — | — | — |
| [SettlementSyncDebtLandingTests](../../../tests/Antiphon.Tests/Application/SettlementSyncDebtLandingTests.cs) | RepoPath in AddReviewAsync | — | — | — | 39 / 1 / 8f055f87 |
| [SettlementSyncDebtSchemaTests](../../../tests/Antiphon.Tests/Application/SettlementSyncDebtSchemaTests.cs) | HasPendingModelChanges in C1082_SchemaMigrationMatchesModel | — | — | — | 4 / 1 / 8f055f87 |
| [SharedWriterLeaseProjectionTests](../../../tests/Antiphon.Tests/Application/SharedWriterLeaseProjectionTests.cs) | client/src/App.tsx | — | — | — | — |
| [SiblingToolReportBoundaryTests](../../../tests/Antiphon.Tests/Application/SiblingToolReportBoundaryTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [SlashCommandMenuReconciliationTests](../../../tests/Antiphon.Tests/Application/SlashCommandMenuReconciliationTests.cs) | FindRepoRoot, repoRoot in BuildCatalog | — | — | — | — |
| [SourceBundleManifestTests](../../../tests/Antiphon.Tests/Application/SourceBundleManifestTests.cs) | RepoPath in BuildAsync | — | — | — | 35 / 2 / 8f055f87 |
| [SpecialistToolPolicyLaunchTests](../../../tests/Antiphon.Tests/Application/SpecialistToolPolicyLaunchTests.cs) | .claude/settings.json | — | — | — | — |
| [SpecialistToolPolicyTests](../../../tests/Antiphon.Tests/Application/SpecialistToolPolicyTests.cs) | .claude/settings.json | — | — | — | — |
| [StandingRestartEscalationTests](../../../tests/Antiphon.Tests/Application/StandingRestartEscalationTests.Audit.cs) | GetMigrations, HasPendingModelChanges in C510_migration_starts_without_invented_history | — | — | — | — |
| [StandingSessionOwnershipTests](../../../tests/Antiphon.Tests/Application/StandingSessionOwnershipTests.cs) | GetMigrations, HasPendingModelChanges, RepoPath in C561_model_and_migration_agree | — | — | — | 11 / 5 / 8f055f87 |
| [StandingSessionSelectionTests](../../../tests/Antiphon.Tests/Application/StandingSessionSelectionTests.Composition.cs) | AGENTS.md | — | — | — | — |
| [StandingSpecialistRoutingMigrationTests](../../../tests/Antiphon.Tests/Application/StandingSpecialistRoutingMigrationTests.cs) | HasPendingModelChanges in Card0415_V25_real_upgrade_preserves_primary_tasks_and_holds_without_fabricating_qualification | — | — | — | 67 / 4 / 8f055f87 |
| [StartRefAvailabilityTests](../../../tests/Antiphon.Tests/Application/StartRefAvailabilityTests.cs) | directory walk, tracked path in The_create_fetch_is_journaled_and_holds_the_repository_lease | — | — | — | 47 / 13 / 8f055f87 |
| [StartRefRepairAdoptionTests](../../../tests/Antiphon.Tests/Application/StartRefRepairAdoptionTests.cs) | RepoPath in CreateAsync | — | — | — | 200 / 8 / 8f055f87 |
| [SubscriptionQuotaGateDispatchTests](../../../tests/Antiphon.Tests/Application/SubscriptionQuotaGateTests.cs) | directory walk in DeleteTree | — | — | — | 3 / 1 / 8f055f87 |
| [SubscriptionUsageParserTests](../../../tests/Antiphon.Tests/Application/SubscriptionUsageParserTests.cs) | tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-status-panel.txt | — | — | — | — |
| [TaskCeilingCreateBindingTests](../../../tests/Antiphon.Tests/Application/TaskCeilingCreateBindingTests.cs) | FindRepoRoot, RepoRoot, repoRoot in Create | — | — | — | — |
| [TaskCompletionContinuationTests](../../../tests/Antiphon.Tests/Application/TaskCompletionContinuationTests.cs) | RepoPath in World | — | — | — | — |
| [TaskCompletionProgressPolicyTests](../../../tests/Antiphon.Tests/Application/TaskCompletionProgressPolicyTests.cs) | RepoPath in World | — | — | — | — |
| [TaskLaunchTokenBoundaryTests](../../../tests/Antiphon.Tests/Application/TaskLaunchTokenBoundaryTests.Harness.cs) | directory walk in DeleteTree | — | — | — | 37 / 5 / 8f055f87 |
| [TaskParkPublicationTests](../../../tests/Antiphon.Tests/Application/TaskParkPublicationTests.cs) | RepoPath in CreateAsync | — | — | — | 69 / 4 / 8f055f87 |
| [TaskParkRunnerIdentityTests](../../../tests/Antiphon.Tests/Application/TaskParkRunnerIdentityTests.cs) | RepoPath in CreateAsync | — | — | — | 39 / 2 / 8f055f87 |
| [TaskPlatformDispatchTests](../../../tests/Antiphon.Tests/Application/TaskPlatformDispatchTests.cs) | FindRepoRoot, RepoRoot, directory walk in Retry_preserves_host_and_requirement | — | — | — | 109 / 11 / 8f055f87 |
| [TaskPlatformPersistenceTests](../../../tests/Antiphon.Tests/Application/TaskPlatformPersistenceTests.cs) | FindRepoRoot, GetMigrations, RepoRoot in Create | — | — | — | 42 / 3 / 8f055f87 |
| [TaskPlatformPlacementTests](../../../tests/Antiphon.Tests/Application/TaskPlatformPlacementTests.cs) | FindRepoRoot, RepoRoot, repoRoot in C796_Resolved_codex_kind_is_admitted | — | — | — | 25 / 15 / 8f055f87 |
| [TaskPlatformWireTests](../../../tests/Antiphon.Tests/Application/TaskPlatformWireTests.cs) | FindRepoRoot, RepoRoot, repoRoot in SeedCardAsync | — | — | — | 37 / 4 / 8f055f87 |
| [TaskProgressGitTests](../../../tests/Antiphon.Tests/Application/TaskProgressGitTests.cs) | directory walk, tracked path in C1082_LeaseBusyObservationReportsAdvertisedTip | — | — | — | 28 / 11 / 8f055f87 |
| [TaskProgressPolicyFileArmTests](../../../tests/Antiphon.Tests/Application/TaskProgressPolicyFileArmTests.cs) | directory walk, tracked path in C499_R21_AFailedSubProbeMarksTheArmUnavailableAndKeepsPositives | — | — | — | 7 / 8 / 8f055f87 |
| [TaskSupervisionVisibilityTests](../../../tests/Antiphon.Tests/Application/TaskSupervisionVisibilityTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [TaskWorktreeRetirementTests](../../../tests/Antiphon.Tests/Application/TaskWorktreeRetirementTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 148 / 40 / 79b742de |
| [TerminalRunnerSeatReleaseTests](../../../tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs) | RepoPath in Answer_admission_guards_preserve_the_accepted_reply | — | — | — | 917 / 39 / 79b742de |
| [TerminalTaskResidueTests](../../../tests/Antiphon.Tests/Application/TerminalTaskResidueTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [TurnEndGapRestartDeliveryTests](../../../tests/Antiphon.Tests/Application/TurnEndGapRestartDeliveryTests.cs) | directory walk in DeleteTree | 2/0/0 · 22.2 [20261126-074134-4c4f/CP-3](#linux-receipt-keys) | — | — | — |
| [VerificationCleanupServiceTests](../../../tests/Antiphon.Tests/Application/VerificationCleanupServiceTests.cs) | RepoPath, directory walk in BuildAsync | — | — | — | 70 / 3 / 8f055f87 |
| [VerificationRoundDeliveryTests](../../../tests/Antiphon.Tests/Application/VerificationRoundDeliveryTests.cs) | directory walk, tracked path in C1605_RegenerationTimeoutAdvancesRetryAndContinuesScan | — | — | — | 257 / 20 / 79b742de |
| [VerificationRoundDispatchTests](../../../tests/Antiphon.Tests/Application/VerificationRoundDispatchTests.cs) | directory walk in DeleteTree | — | — | — | 65 / 9 / 8f055f87 |
| [VerificationRoundSettlementTests](../../../tests/Antiphon.Tests/Application/VerificationRoundSettlementTests.cs) | RepoPath, directory walk in SeedOwnerAsync | — | — | — | 52 / 10 / 8f055f87 |
| [WallRerouteDispatchTests](../../../tests/Antiphon.Tests/Application/WallRerouteDispatchTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [WindowsGrokRoutingPolicyTests](../../../tests/Antiphon.Tests/Application/WindowsGrokRoutingPolicyTests.cs) | FindRepoRoot, RepoRoot, repoRoot in Create | — | — | — | — |
| [WorkerWorkspaceDefaultMigrationTests](../../../tests/Antiphon.Tests/Application/WorkerWorkspaceDefaultMigrationTests.cs) | AGENTS.md | — | — | — | 50 / 2 / 8f055f87 |
| [WorkflowDefinitionLoaderTests](../../../tests/Antiphon.Tests/Application/WorkflowDefinitionLoaderTests.cs) | directory walk in DeleteDirectoryBestEffort | — | — | — | — |
| [WorkspaceInfoGitIntegrationTests](../../../tests/Antiphon.Tests/Application/WorkspaceInfoTests.cs) | RepoRoot, directory walk in Batch_lookup_dedupes_paths_and_degrades_missing_dirs_to_not_a_repo | — | — | — | 12 / 4 / 8f055f87 |
| [WorktreeBaseSelectionTests](../../../tests/Antiphon.Tests/Application/WorktreeBaseSelectionTests.SiblingWarnings.cs) | GetMigrations, RepoPath, directory walk in C508_CherryContainmentMatrix | — | — | — | 67 / 16 / 79b742de |
| [WorktreeDefaultAdmissionTests](../../../tests/Antiphon.Tests/Application/WorktreeDefaultAdmissionTests.cs) | RepoPath, directory walk, tracked path in FreshWorkerUsesWorktree | — | — | — | 35 / 6 / 8f055f87 |
| [WorktreeDefaultCallerTests](../../../tests/Antiphon.Tests/Application/WorktreeDefaultCallerTests.cs) | .gitignore, scripts/nightly-run.ps1, docs/** | — | — | — | 20 / 8 / 8f055f87 |
| [WorktreeDefaultContinuationTests](../../../tests/Antiphon.Tests/Application/WorktreeDefaultContinuationTests.cs) | RepoPath, directory walk in ExplicitWorktreePinRefuses | — | — | — | 54 / 8 / 8f055f87 |
| [WorktreeHealthServiceTests](../../../tests/Antiphon.Tests/Application/WorktreeHealthServiceTests.cs) | RepoPath, tracked path in sweep_upserts_then_clears_when_the_shape_is_gone_and_never_prunes | — | — | — | 7 / 7 / 8f055f87 |
| [WorktreeResidueRecoveryTests](../../../tests/Antiphon.Tests/Application/WorktreeResidueRecoveryTests.cs) | RepoPath, directory walk, tracked path in PriorHashAsync | — | — | — | 487 / 22 / 8f055f87 |
| [WorktreeResidueSweepTests](../../../tests/Antiphon.Tests/Application/WorktreeResidueSweepTests.cs) | RepoPath, directory walk, git ls-files in execute_never_treats_legacy_landed_event_as_cleanup_authority | — | — | — | 23 / 15 / 8f055f87 |
| [WorktreeRetirementRaceTests](../../../tests/Antiphon.Tests/Application/WorktreeRetirementRaceTests.cs) | RepoPath, tracked path in C459_AnswerReservesWorkspace | — | — | — | 119 / 54 / 79b742de |
| [WorktreeStartRefDispatchTests](../../../tests/Antiphon.Tests/Application/WorktreeStartRefDispatchTests.cs) | directory walk in DeleteTree | — | — | — | 185 / 30 / 79b742de |

### Antiphon.Tests / Antiphon.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [CachingChatClientTests](../../../tests/Antiphon.Tests/CachingChatClientTests.cs) | directory walk in Record_mode_always_caches_response | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Checkpoints

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [C1307CliCancellationTests](../../../tests/Antiphon.Tests/Checkpoints/C1307CliCancellationTests.cs) | RepoRoot in Main_interrupt_reaches_command_and_unsubscribes | 5/0/0 · 7.5 [20261107-093220-718b/CP-2](#linux-receipt-keys) | — | — | 32 / 5 / 8f055f87 |
| [CheckpointBoundedReaderTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointBoundedReaderTests.cs) | scripts/test-checkpoint-bounded-readers.mjs | — | — | — | — |
| [CheckpointBuildBindingPathTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointBuildBindingPathTests.cs) | tests/** | 3/0/0 · 2.3 [20261125-145444-fd84/CP-3](#linux-receipt-keys) | — | — | 10 / 3 / 8f055f87 |
| [CheckpointColdBuildTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointColdBuildTests.cs) | src/Antiphon.PtyHost/Antiphon.PtyHost.csproj, .gitignore, Directory.Build.props … | 3/0/0 · 69.7 [20261107-093220-718b/CP-1](#linux-receipt-keys) | — | — | — |
| [CheckpointConfinementLedgerTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointConfinementLedgerTests.cs) | .gitignore | 21/0/0 · 65.6 [20261125-145444-fd84/CP-3](#linux-receipt-keys) | — | — | 272 / 20 / 8f055f87 |
| [CheckpointFixtureGitEncodingTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointFixtureGitEncodingTests.cs) | git ls-files in fixture_git_stdout_keeps_utf8_under_an_oem_default | — | — | — | — |
| [CheckpointGitEncodingScriptTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointGitEncodingScriptTests.cs) | directory walk, git ls-files in Run | — | — | — | — |
| [CheckpointLargeStampTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointLargeStampTests.cs) | .gitignore | — | — | — | 87 / 6 / 8f055f87 |
| [CheckpointOutputConfinementTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointOutputConfinementTests.cs) | .gitignore, Directory.Build.props, scripts/refresh-server2-github-token.ps1 … | 1/0/1 · 18.8 [20261123-081710-d7c6/CP-1](#linux-receipt-keys) | — | — | 25 / 6 / 8f055f87 |
| [CheckpointReceiptStallBudgetTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointReceiptStallBudgetTests.cs) | scripts/lib/checkpoint-file-reader.ps1 | — | — | — | — |
| [CheckpointRecoveryLinuxTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryLinuxTests.cs) | RepoRoot, directory walk, tracked path in RepoRoot | — | — | — | — |
| [CheckpointRecoveryWindowsTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs) | RepoRoot, directory walk, tracked path in RepoRoot | — | — | — | — |
| [CheckpointRepeatBuildIsolationTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointRepeatBuildIsolationTests.cs) | tests/Antiphon.Checkpoints.FilterHost/**, tests/Antiphon.Checkpoints.RepeatHost/Antiphon.Checkpoints.RepeatHost.csproj | — | — | — | — |
| [CheckpointRepeatHostTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointRepeatHostTests.cs) | RepoRoot, tracked path in RunHost | 3/0/0 · 7.7 [20261107-022238-5d4b/CP-REPEAT](#linux-receipt-keys) | — | — | 6 / 3 / 8f055f87 |
| [CheckpointRunOwnershipTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointRunOwnershipTests.cs) | directory walk in deleting_root_rejects_registration | — | — | — | — |
| [CheckpointSelectedOutputTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointSelectedOutputTests.cs) | .gitignore | — | — | — | 158 / 3 / 8f055f87 |
| [CheckpointShadowCustodyTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointShadowCustodyTests.cs) | directory walk in separate_wait_removes_only_the_sealed_tool_copy | — | — | — | 22 / 12 / 8f055f87 |
| [CheckpointSourceExecutionTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs) | _bmad/_config/manifest.yaml | 16/0/0 · 141 [20261107-093220-718b/CP-2](#linux-receipt-keys) | — | — | 79 / 9 / 8f055f87 |
| [CheckpointSourceStateTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs) | .gitignore | — | — | — | 92 / 8 / 8f055f87 |
| [CheckpointStampSafetyTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointStampSafetyTests.cs) | directory walk, tracked path in import_and_coverage_refuse_blank_identities_without_writes | — | — | — | — |
| [CheckpointStatePublicationTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointStatePublicationTests.cs) | directory walk in StoreScenario | 2/0/0 · 13.6 [20261107-093220-718b/CP-2](#linux-receipt-keys) | — | — | 86 / 1 / 8f055f87 |
| [CheckpointTempLifecycleTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointTempLifecycleTests.cs) | directory walk in passing_host_reports_are_removed_with_the_owned_sandbox | — | — | — | — |
| [CheckpointTerminalReceiptTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointTerminalReceiptTests.cs) | .gitignore | 10/0/0 · 153 [20261125-145444-fd84/CP-4](#linux-receipt-keys) | — | — | — |
| [CheckpointToolCopyCleanupTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs) | directory walk in automatic_recovery_preserves_all_evidence | 16/0/0 · 12.7 [20261107-093220-718b/CP-2](#linux-receipt-keys) | — | — | — |
| [CheckpointToolSourceTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointToolSourceTests.cs) | directory walk in tiny_source_is_really_copied_to_the_launched_image | — | — | — | 4 / 2 / 8f055f87 |
| [CheckpointTypeCacheTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointTypeCacheTests.cs) | scripts/lib/checkpoint-file-reader.ps1 | — | — | — | — |
| [CheckpointWarmBuildTests](../../../tests/Antiphon.Tests/Checkpoints/CheckpointWarmBuildTests.cs) | .gitignore | 1/0/0 · 1.6 [20261125-170459-d549/CP-1](#linux-receipt-keys) | — | — | — |
| [EvidenceFolderTests](../../../tests/Antiphon.Tests/Checkpoints/EvidenceFolderTests.cs) | RepoRoot, tracked path in host_and_git_snapshots_present | 1/0/0 · 1.3 [20261125-170459-d549/CP-2](#linux-receipt-keys) | — | — | 26 / 6 / 8f055f87 |
| [PhaseJournalKillDiagnosticTests](../../../tests/Antiphon.Tests/Checkpoints/PhaseJournalKillDiagnosticTests.cs) | tests/Antiphon.Tests/** | — | — | — | — |
| [PlanCoverageHandleTests](../../../tests/Antiphon.Tests/Checkpoints/PlanCoverageHandleTests.cs) | tracked path in WriteWorld | — | — | — | — |
| [PlanCoverageParserTests](../../../tests/Antiphon.Tests/Checkpoints/PlanCoverageParserTests.cs) | tracked path in WriteWorld | — | — | — | — |
| [PreflightCommandLineBudgetTests](../../../tests/Antiphon.Tests/Checkpoints/PreflightCommandLineBudgetTests.cs) | git ls-files in LsFiles | — | — | — | — |
| [RerunFilterHostTests](../../../tests/Antiphon.Tests/Checkpoints/RerunFilterHostTests.cs) | RepoRoot, directory walk, tracked path in parenthesized_class_filter_runs_both_methods_on_a_real_host | — | — | — | 2 / 1 / 8f055f87 |
| [RowStreamingTests](../../../tests/Antiphon.Tests/Checkpoints/RowStreamingTests.cs) | tests/Antiphon.Tests/** | — | — | — | — |
| [RunSchedulerTests](../../../tests/Antiphon.Tests/Checkpoints/RunSchedulerTests.cs) | tests/** | 19/0/0 · 9.0 [20261125-145444-fd84/CP-3](#linux-receipt-keys) | — | — | — |
| [RunStateStoreTests](../../../tests/Antiphon.Tests/Checkpoints/RunStateStoreTests.cs) | directory walk in readers_never_see_a_partial_file | — | — | — | 4 / 1 / 8f055f87 |
| [ShadowCopyTests](../../../tests/Antiphon.Tests/Checkpoints/ShadowCopyTests.cs) | src/** | — | — | — | 2 / 1 / 8f055f87 |
| [TestPhaseCustodyTests](../../../tests/Antiphon.Tests/Checkpoints/TestPhaseCustodyTests.cs) | scripts/lib/nightly-core.ps1 | — | — | — | 2 / 1 / 8f055f87 |
| [TestSetTimingTests](../../../tests/Antiphon.Tests/Checkpoints/TestSetTimingTests.cs) | directory walk in C1313_ProducerReceipts | — | — | — | — |
| [TimingJournalTests](../../../tests/Antiphon.Tests/Checkpoints/TimingJournalTests.cs) | directory walk, tracked path in C1313_RootCustody | — | — | — | — |
| [UnsafeDeleteRecoveryTests](../../../tests/Antiphon.Tests/Checkpoints/UnsafeDeleteRecoveryTests.cs) | directory walk in replacement_loss_keeps_a_recoverable_generation | — | — | — | — |
| [XmlInputSecurityTests](../../../tests/Antiphon.Tests/Checkpoints/XmlInputSecurityTests.cs) | tracked path in coverage_project_reader_refuses_declarations_after_an_accepted_control | 4/0/0 · 1.9 [20261107-022238-5d4b/CP-2](#linux-receipt-keys) | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Infrastructure

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AppDbContextModelTests](../../../tests/Antiphon.Tests/Infrastructure/AppDbContextModelTests.cs) | HasPendingModelChanges in Current_model_matches_migration_snapshot | 3/0/0 · 21.3 [20261125-142218-704b/CP-3](#linux-receipt-keys) | — | — | 16 / 3 / 8f055f87 |
| [C1307CancellationTests](../../../tests/Antiphon.Tests/Infrastructure/C1307CancellationTests.cs) | RepoRoot in Command | — | — | — | — |
| [CompletedCardWorktreeRemovalTests](../../../tests/Antiphon.Tests/Infrastructure/CompletedCardWorktreeRemovalTests.cs) | .claude/settings.json | — | — | — | 942 / 37 / 8f055f87 |
| [DockerDeliveryBarrierTests](../../../tests/Antiphon.Tests/Infrastructure/DockerDeliveryBarrierTests.cs) | directory walk in Foreign_arm_is_not_activated | — | — | — | — |
| [ExpectationWatchdogJobTests](../../../tests/Antiphon.Tests/Infrastructure/ExpectationWatchdogJobTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | — |
| [GitDiffSpikeTests](../../../tests/Antiphon.Tests/Infrastructure/GitDiffSpikeTests.cs) | _bmad-output/planning-artifacts/architecture.md, docs/features/012-review-engine/design.md, _bmad-output/planning-artifacts/prd.md | — | — | — | 16 / 5 / 8f055f87 |
| [GitServiceTests](../../../tests/Antiphon.Tests/Infrastructure/GitServiceTests.cs) | _bmad-output/planning-artifacts/architecture.md | — | — | — | — |
| [GitTreeTemplateTests](../../../tests/Antiphon.Tests/Infrastructure/GitTreeTemplateTests.cs) | directory walk, git ls-files in ScratchGitRepo_seeded_copy_is_the_repository_the_git_steps_make | — | — | — | — |
| [GithubCredentialHelperTests](../../../tests/Antiphon.Tests/Infrastructure/GithubCredentialHelperTests.cs) | directory walk in Helper_ignores_store_and_erase_and_writes_nothing | — | — | — | — |
| [HostCleanupCheckpointCustodyTests](../../../tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointCustodyTests.cs) | git ls-files in Git_failure_and_deadline_refuse_before_mutation | — | — | — | — |
| [KanbanPersistenceTests](../../../tests/Antiphon.Tests/Infrastructure/KanbanPersistenceTests.cs) | RepoPath in CreateKanbanGraph | — | — | — | 9 / 6 / 8f055f87 |
| [LandRecoveryCheckoutTests](../../../tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs) | .gitattributes | — | — | — | 241 / 34 / 8f055f87 |
| [LandRecoveryIgnoredNestedRepositoryTests](../../../tests/Antiphon.Tests/Infrastructure/LandRecoveryIgnoredNestedRepositoryTests.cs) | git ls-files in C971_NestedRepositoryMatrix | — | — | — | 114 / 15 / 8f055f87 |
| [LandRecoveryIgnoredObstructionTests](../../../tests/Antiphon.Tests/Infrastructure/LandRecoveryIgnoredObstructionTests.cs) | git ls-files in AssertRefusedAsync | — | — | — | 77 / 7 / 8f055f87 |
| [LandVerificationWindowNativeTests](../../../tests/Antiphon.Tests/Infrastructure/LandVerificationWindowNativeTests.cs) | tracked path in C1379_PreflightUsesARealReleasedMutationHandle | — | — | — | 18 / 3 / 8f055f87 |
| [LandingGitProfileTests](../../../tests/Antiphon.Tests/Infrastructure/LandingGitProfileTests.cs) | git ls-files in C1432_histogram_merges_in_add | — | — | — | — |
| [LandingGitTests](../../../tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs) | directory walk, git ls-files, tracked path in C1076_UpdateRefsStdinBreakClearsTheJournal | — | — | — | 288 / 58 / 8f055f87 |
| [LandingRegistrationFreshnessTests](../../../tests/Antiphon.Tests/Infrastructure/LandingRegistrationFreshnessTests.cs) | git ls-files in CaptureAsync | — | — | — | 54 / 8 / 8f055f87 |
| [LandingRemovalPolicyControlTests](../../../tests/Antiphon.Tests/Infrastructure/LandingRemovalPolicyControlTests.cs) | .claude/settings.json | 1/0/2 · 1.0 [20261123-081710-d7c6/CP-2](#linux-receipt-keys) | — | — | — |
| [LandingSourceFreshnessTests](../../../tests/Antiphon.Tests/Infrastructure/LandingSourceFreshnessTests.cs) | tracked path in .ctor | — | — | — | 117 / 21 / 8f055f87 |
| [LinuxOwnedGitProcessTests](../../../tests/Antiphon.Tests/Infrastructure/LinuxOwnedGitProcessTests.cs) | tracked path in FindRoot | 46/0/0 · 13.2 [20261126-074134-4c4f/CP-5](#linux-receipt-keys) | — | — | — |
| [OperatorTokenFileTests](../../../tests/Antiphon.Tests/Infrastructure/OperatorTokenFileTests.cs) | directory walk in Concurrent_first_use_readers_all_get_the_one_token_and_none_throw | — | — | — | 3 / 4 / 8f055f87 |
| [RepositoryChildJournalInspectorTests](../../../tests/Antiphon.Tests/Infrastructure/RepositoryChildJournalInspectorTests.cs) | directory walk, tracked path in a_children_path_that_is_a_file_is_one_malformed_finding | — | — | — | — |
| [RepositoryChildRecoveryTests](../../../tests/Antiphon.Tests/Infrastructure/RepositoryChildRecoveryTests.cs) | directory walk, tracked path in C448_D1_RecoveryPreservesLiveOrAmbiguousEvidence | — | — | — | 116 / 15 / 8f055f87 |
| [RepositoryFenceObserverTests](../../../tests/Antiphon.Tests/Infrastructure/RepositoryFenceObserverTests.cs) | directory walk, tracked path in PlantDeadAsync | — | — | — | 1 / 1 / 8f055f87 |
| [RepositoryLeaseWindowsBoundaryTests](../../../tests/Antiphon.Tests/Infrastructure/RepositoryLeaseWindowsBoundaryTests.cs) | directory walk, tracked path in C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput | — | — | — | — |
| [RepositoryMutationLeaseDescribeTests](../../../tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseDescribeTests.cs) | directory walk, tracked path in describe_is_null_on_empty_children_and_names_a_non_json_journal_file | — | — | — | 1 / 1 / 8f055f87 |
| [RepositoryMutationLeaseOwnerTests](../../../tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseOwnerTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 16 / 8 / 8f055f87 |
| [RoutingBindingCacheTests](../../../tests/Antiphon.Tests/Infrastructure/RoutingBindingCacheTests.cs) | Dockerfile, src/Antiphon.Messaging.Service/Dockerfile | — | — | — | — |
| [SettledWorktreeRemovalTests](../../../tests/Antiphon.Tests/Infrastructure/SettledWorktreeRemovalTests.cs) | .claude/settings.json | — | — | — | 668 / 78 / 8f055f87 |
| [WorkspaceHookRunnerTests](../../../tests/Antiphon.Tests/Infrastructure/WorkspaceHookRunnerTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [WorktreeGuardedCleanupTests](../../../tests/Antiphon.Tests/Infrastructure/WorktreeGuardedCleanupTests.cs) | RepoPath, directory walk, git ls-files in C665_EmptyUnlistedJunctionTargetSurvivesRemoval | — | — | — | 1593 / 49 / 8f055f87 |
| [WorktreeManagerGitIntegrationTests](../../../tests/Antiphon.Tests/Infrastructure/WorktreeManagerTests.cs) | .gitattributes | — | — | — | 46 / 15 / 8f055f87 |
| [WorktreeManagerStartRefLocalOnlyTests](../../../tests/Antiphon.Tests/Infrastructure/WorktreeManagerStartRefLocalOnlyTests.cs) | tracked path in CreateRepoAsync | — | — | — | 6 / 2 / 8f055f87 |
| [WorktreeNoFollowDeleteTests](../../../tests/Antiphon.Tests/Infrastructure/WorktreeNoFollowDeleteTests.cs) | directory walk in AssertLateMarkerStopsTheWalk | 1/0/1 · 0.076 [20261123-081710-d7c6/CP-3](#linux-receipt-keys) | — | — | — |
| [ZombieCensusServiceTests](../../../tests/Antiphon.Tests/Infrastructure/ZombieCensusServiceTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [LandDeliveryWaitRoutingTests](../../../tests/Antiphon.Tests/LandDeliveryWaitRoutingTests.cs) | tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs, tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs, tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs … | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Migrations

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [PipelineDefinitionMigrationTests](../../../tests/Antiphon.Tests/Migrations/PipelineDefinitionMigrationTests.cs) | HasPendingModelChanges in Down_removes_post_A_runs_then_restores_gen1_columns_and_Up_returns_to_head | — | — | — | 68 / 13 / 8f055f87 |

### Antiphon.Tests / Antiphon.Tests.Review

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AbfConformanceTests](../../../tests/Antiphon.Tests/Review/AbfConformanceTests.cs) | docs/specs/block-format/conformance/valid/004-decision-request.abf, docs/specs/block-format/conformance/** | — | — | — | — |
| [ReviewDelegationRunnerApiTests](../../../tests/Antiphon.Tests/Review/ReviewDelegationRunnerApiTests.cs) | RepoPath in The_reviewer_is_a_read_only_review_task_at_the_head_and_its_report_completes_the_node | 1/0/0 · 7.8 [20261125-142218-704b/CP-2](#linux-receipt-keys) | — | — | — |
| [ReviewEngineMigrationTests](../../../tests/Antiphon.Tests/Review/ReviewEngineMigrationTests.cs) | GetMigrations, HasPendingModelChanges in Up_creates_only_the_detached_tables_and_Down_refuses | 1/0/0 · 9.3 [20261125-142218-704b/CP-3](#linux-receipt-keys) | — | — | — |
| [ReviewMirrorCheckoutGitTests](../../../tests/Antiphon.Tests/Review/ReviewMirrorCheckoutTests.cs) | directory walk in TryDelete | — | — | — | — |
| [ReviewMirrorCheckoutTests](../../../tests/Antiphon.Tests/Review/ReviewMirrorCheckoutTests.cs) | RepoPath in A_github_repo_without_a_local_path_uses_its_lower_cased_mirror_and_a_row_or_a_local_subject_does_not | 10/0/0 · 0.775 [20261125-142218-704b/CP-4](#linux-receipt-keys) | — | — | — |
| [ReviewSignInApiTests](../../../tests/Antiphon.Tests/Review/ReviewSignInApiTests.cs) | directory walk in Signing_out_ends_the_session | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Scripts

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [AppHostWatchdogTests](../../../tests/Antiphon.Tests/Scripts/AppHostWatchdogTests.cs) | docker-compose.dev.yml | — | — | — | — |
| [AppHostWorktreeGuardTests](../../../tests/Antiphon.Tests/Scripts/AppHostWorktreeGuardTests.cs) | RepoRoot, tracked path in Dev_aspire_from_a_linked_worktree_refuses_before_any_lock_or_docker | — | — | — | — |
| [AstraFixesBTests](../../../tests/Antiphon.Tests/Scripts/AstraFixesBTests.cs) | scripts/release-cut.ps1 | — | — | — | — |
| [C0824BranchDeletionRecycleAuditTests](../../../tests/Antiphon.Tests/Scripts/C0824BranchDeletionRecycleAuditTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [C1364BoundLandWaitProofTests](../../../tests/Antiphon.Tests/Scripts/C1364BoundLandWaitProofTests.cs) | scripts/fixtures/c1008-recycle-cases.json, scripts/fixtures/c994-production-compose-model.mjs | — | — | — | 57 / 6 / 8f055f87 |
| [C1733AllocatorFixtureTests](../../../tests/Antiphon.Tests/Scripts/C1733AllocatorFixtureTests.cs) | scripts/fixtures/c599-policy-v2.json, scripts/release-cut.ps1, tests/test-execution-policy.json | — | — | — | — |
| [CacheFixtureCompletionTests](../../../tests/Antiphon.Tests/Scripts/CacheFixtureCompletionTests.cs) | scripts/fixtures/c913-seed-contract.sh, scripts/fixtures/c1192-workspace-contract.sh, scripts/fixtures/c1315-fail-closed.sh … | — | — | — | 37 / 19 / 8f055f87 |
| [CardFileBootstrapCheckTests](../../../tests/Antiphon.Tests/Scripts/CardFileBootstrapCheckTests.cs) | .gitignore | — | — | — | — |
| [ClientChecksTests](../../../tests/Antiphon.Tests/Scripts/ClientChecksTests.cs) | client/package-lock.json, client/tsconfig.app.json, client/tsconfig.node.json … | — | — | — | — |
| [DaemonSupervisorTests](../../../tests/Antiphon.Tests/Scripts/DaemonSupervisorTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [DelegateScriptBindDiagnosisTests](../../../tests/Antiphon.Tests/Scripts/DelegateScriptBindDiagnosisTests.cs) | directory walk in DeleteTree | — | — | — | 13 / 6 / 8f055f87 |
| [DevAspireTests](../../../tests/Antiphon.Tests/Scripts/DevAspireTests.cs) | docker-compose.dev.yml | — | — | — | — |
| [DevRestartTests](../../../tests/Antiphon.Tests/Scripts/DevRestartTests.cs) | server/Antiphon.Server.csproj | — | — | — | — |
| [DevStackTests](../../../tests/Antiphon.Tests/Scripts/DevStackTests.cs) | docker-compose.dev.yml | — | — | — | — |
| [DockerDeliveryRecoveryCommandTests](../../../tests/Antiphon.Tests/Scripts/DockerDeliveryRecoveryCommandTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [DockerStackSmokeCommandTests](../../../tests/Antiphon.Tests/Scripts/DockerStackSmokeCommandTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [DockerTestCommandTests](../../../tests/Antiphon.Tests/Scripts/DockerTestCommandTests.cs) | scripts/c590-remote.sh, tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [EvidenceApprovedFixtureGuardTests](../../../tests/Antiphon.Tests/Scripts/EvidenceApprovedFixtureGuardTests.cs) | RepoRoot, tracked path in RepoRoot | — | — | — | — |
| [EvidenceDeletionGuardTests](../../../tests/Antiphon.Tests/Scripts/EvidenceDeletionGuardTests.cs) | RepoRoot, tracked path in RepoRoot | — | — | — | — |
| [EvidenceDiffGuardTests](../../../tests/Antiphon.Tests/Scripts/EvidenceDiffGuardTests.cs) | RepoRoot, tracked path in RepoRoot | — | — | — | — |
| [EvidenceGitFixtureTests](../../../tests/Antiphon.Tests/Scripts/EvidenceGitFixtureTests.cs) | RepoRoot, tracked path in RepoRoot | — | — | — | 1 / 1 / 8f055f87 |
| [EvidenceSupplementalDeletionGuardTests](../../../tests/Antiphon.Tests/Scripts/EvidenceSupplementalDeletionGuardTests.cs) | RepoRoot, tracked path in RepoRoot | — | — | — | — |
| [LiveLaneAttestationTests](../../../tests/Antiphon.Tests/Scripts/LiveLaneAttestationTests.cs) | scripts/test-live-lane-attestation.ps1, .claude/settings.json, AGENTS.md … | — | — | — | — |
| [LiveLaneExecutableTests](../../../tests/Antiphon.Tests/Scripts/LiveLaneExecutableTests.cs) | tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs, scripts/lib/live-lane-outcome.ps1, tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs … | — | — | — | — |
| [LiveLaneFingerprintTests](../../../tests/Antiphon.Tests/Scripts/LiveLaneFingerprintTests.cs) | scripts/test-live-lane-fingerprint.ps1 | — | — | — | — |
| [LiveLaneIsolationTests](../../../tests/Antiphon.Tests/Scripts/LiveLaneIsolationTests.cs) | CLAUDE.md, AGENTS.md, .claude/settings.json … | — | — | — | — |
| [LiveLaneOutcomeTests](../../../tests/Antiphon.Tests/Scripts/LiveLaneOutcomeTests.cs) | scripts/test-live-lane-outcome.ps1 | — | — | — | — |
| [LiveLaneReceiptTests](../../../tests/Antiphon.Tests/Scripts/LiveLaneReceiptTests.cs) | scripts/test-live-lane-receipt.ps1, .claude/settings.json, AGENTS.md … | — | — | — | — |
| [LongPathsCheckoutScriptTests](../../../tests/Antiphon.Tests/Scripts/LongPathsCheckoutScriptTests.cs) | scripts/nightly-run.ps1 | — | — | — | — |
| [MeasureSessionStateCacheScriptTests](../../../tests/Antiphon.Tests/Scripts/MeasureSessionStateCacheScriptTests.cs) | directory walk in Wrong_baseline_identity_is_refused | 19/0/0 · 12.3 [20261125-142218-704b/CP-4](#linux-receipt-keys) | — | — | 51 / 16 / 8f055f87 |
| [MechanicalPcRehearsalTests](../../../tests/Antiphon.Tests/Scripts/MechanicalPcRehearsalTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | 52 / 10 / 8f055f87 |
| [MigrateStagePinsScriptTests](../../../tests/Antiphon.Tests/Scripts/MigrateStagePinsScriptTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | 29 / 10 / 8f055f87 |

### Antiphon.Tests / Antiphon.Tests.Scripts.Nightly

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [LiveLaneDiscoveryTests](../../../tests/Antiphon.Tests/Scripts/Nightly/LiveLaneDiscoveryTests.cs) | tests/live-lanes.json, tests/fixtures/live-lanes/full-discovery-before.json | — | — | — | — |
| [NightlyArgsTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyArgsTests.cs) | tracked path in A_list_flag_splits_at_commas_trims_and_drops_empty_entries | — | — | — | — |
| [NightlyLockFormatTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyLockFormatTests.cs) | RepoRoot, tracked path in C1396_WriteHeldLockReadable | — | — | — | — |
| [NightlyRunParityTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyRunParityTests.cs) | global.json, tests/test-execution-policy.json, tests/test-selection-policy.json … | — | — | — | — |
| [NightlyRunTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyRunTests.cs) | scripts/nightly-run.ps1, scripts/**, scripts/release-cut.ps1 | — | — | — | — |
| [NightlyTestHelpersTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyTestHelpersTests.cs) | scripts/nightly-run.ps1, scripts/lib/Antiphon.Scripts/NightlyTestsCommand.cs, scripts/lib/nightly-common.ps1 … | — | — | — | — |
| [NightlyTestsParityTests](../../../tests/Antiphon.Tests/Scripts/Nightly/NightlyTestsParityTests.cs) | scripts/fixtures/nightly/tests-regression/stopped-seed.json, docker/git-minimum-version.sh, global.json … | — | — | — | — |
| [TestTimingsTwinTests](../../../tests/Antiphon.Tests/Scripts/Nightly/TestTimingsTwinTests.cs) | directory walk in The_writer_appends_each_event_once_and_refuses_conflicts_and_bad_components | — | — | — | — |
| [ToolchainFloorTwinTests](../../../tests/Antiphon.Tests/Scripts/Nightly/ToolchainFloorTwinTests.cs) | docker/git-minimum-version.sh, global.json | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.Scripts

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [NightlyDiscoveryParserTests](../../../tests/Antiphon.Tests/Scripts/NightlyDiscoveryParserTests.cs) | scripts/fixtures/nightly/c487-probe/discovery.diag, scripts/fixtures/nightly/c487-probe/execution.diag, scripts/release-cut.ps1 … | — | — | — | 10 / 6 / 8f055f87 |
| [NightlyExecutionDiagnosticTests](../../../tests/Antiphon.Tests/Scripts/NightlyExecutionDiagnosticTests.cs) | scripts/fixtures/nightly/c487-probe/execution.diag, scripts/fixtures/nightly/c487-probe/discovery.diag, scripts/fixtures/nightly/d13-diagnostic-records.json … | — | — | — | 10 / 7 / 8f055f87 |
| [NightlyPrerequisiteTests](../../../tests/Antiphon.Tests/Scripts/NightlyPrerequisiteTests.cs) | scripts/release-cut.ps1, tests/test-execution-policy.json | — | — | — | 6 / 2 / 8f055f87 |
| [OrchestratorInvestigationHookSuiteTests](../../../tests/Antiphon.Tests/Scripts/OrchestratorInvestigationHookSuiteTests.cs) | scripts/hooks/**, scripts/release-cut.ps1 | — | — | — | — |
| [OrchestratorWorkspaceCommandTests](../../../tests/Antiphon.Tests/Scripts/OrchestratorWorkspaceCommandTests.cs) | CLAUDE.md, .claude/settings.json, AGENTS.md … | — | — | — | — |
| [OuterRowBuildStampTests](../../../tests/Antiphon.Tests/Scripts/OuterRowBuildStampTests.cs) | repositoryRoot, tracked path in CreateWithoutAssemblyDirectory | — | — | — | — |
| [PgQpsTests](../../../tests/Antiphon.Tests/Scripts/PgQpsTests.cs) | server/Infrastructure/Data/PgStatStatementsSnapshot.sql | 39/0/0 · 1.4 [20261125-142218-704b/CP-4](#linux-receipt-keys) | — | — | — |
| [PhoneHomeRunnerStartTests](../../../tests/Antiphon.Tests/Scripts/PhoneHomeRunnerStartTests.cs) | src/Antiphon.SessionRunner/** | — | — | — | — |
| [PosixHarnessToolTests](../../../tests/Antiphon.Tests/Scripts/PosixHarnessToolTests.cs) | scripts/test-postgres-snapshot.ps1, scripts/test-cutover-postgres-server2.ps1, scripts/** | — | — | — | — |
| [PostgresJitWindowTests](../../../tests/Antiphon.Tests/Scripts/PostgresJitWindowTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [ProjectScriptWorkspaceTests](../../../tests/Antiphon.Tests/Scripts/ProjectScriptWorkspaceTests.cs) | AGENTS.md | — | — | — | 8 / 9 / 8f055f87 |
| [QualificationHeartbeatTests](../../../tests/Antiphon.Tests/Scripts/QualificationHeartbeatTests.cs) | scripts/fixtures/nightly/stopped-seed/groups-planned-antiphon.json, scripts/fixtures/nightly/stopped-seed/groups-planned-session-runner.json, scripts/release-cut.ps1 | — | — | — | — |
| [RcBoundaryChunkIdTests](../../../tests/Antiphon.Tests/Scripts/RcBoundaryChunkIdTests.cs) | tests/Antiphon.Tests/Layering/layers.txt, server/Infrastructure/Resilience/NpgsqlTransientFailureClassifier.cs | — | — | — | — |
| [RcBoundaryPackTests](../../../tests/Antiphon.Tests/Scripts/RcBoundaryPackTests.cs) | scripts/fixtures/rc-boundary-pack-golden/separation.json, scripts/fixtures/rc-boundary-pack-golden/separation-scripts.json, scripts/fixtures/rc-boundary-pack-golden/repack-ctor.json … | — | — | — | — |
| [RcChunkInputDigestTests](../../../tests/Antiphon.Tests/Scripts/RcChunkInputDigestTests.cs) | tracked path in UnknownAndCaseSetChangesAreDirty | — | — | — | — |
| [RcDirtyOrderCSharpTests](../../../tests/Antiphon.Tests/Scripts/RcDirtyOrderCSharpTests.cs) | tests/Antiphon.Tests/Scripts/Fixtures/card-1836-s6c-rc-port-golden.json | — | — | — | — |
| [RcFailedFirstExecutionTests](../../../tests/Antiphon.Tests/Scripts/RcFailedFirstExecutionTests.cs) | .gitignore, scripts/fixtures/nightly/c1410-antiphon-listing.txt, scripts/fixtures/nightly/c1514-failed-first-planned.json … | — | — | — | — |
| [RcFailedFirstPlannerTests](../../../tests/Antiphon.Tests/Scripts/RcFailedFirstPlannerTests.cs) | scripts/fixtures/nightly/c1410-antiphon-listing.txt, scripts/fixtures/nightly/c1514-failed-first-planned.json, scripts/fixtures/nightly/stopped-seed/groups-planned-antiphon.json … | — | — | — | — |
| [RcFailureHistoryTests](../../../tests/Antiphon.Tests/Scripts/RcFailureHistoryTests.cs) | scripts/fixtures/nightly/stopped-seed/groups-planned-antiphon.json, scripts/fixtures/nightly/c1410-antiphon-listing.txt, scripts/lib/nightly-class-costs.json … | — | — | — | — |
| [RcHistoricalAdmissionTests](../../../tests/Antiphon.Tests/Scripts/RcHistoricalAdmissionTests.cs) | scripts/lib/rc-input-owners.json | — | — | — | — |
| [RcHistoricalEvidenceTests](../../../tests/Antiphon.Tests/Scripts/RcHistoricalEvidenceTests.cs) | tracked path in SourceRootValue | — | — | — | — |
| [RcIncrementalLedgerTests](../../../tests/Antiphon.Tests/Scripts/RcIncrementalLedgerTests.cs) | tracked path in Id | — | — | — | — |
| [RcLedgerDurationTests](../../../tests/Antiphon.Tests/Scripts/RcLedgerDurationTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | — |
| [RcLedgerStatusTests](../../../tests/Antiphon.Tests/Scripts/RcLedgerStatusTests.cs) | scripts/fixtures/nightly/core-golden/plan.json | — | — | — | — |
| [RcPortWrapperCasingTests](../../../tests/Antiphon.Tests/Scripts/RcPortWrapperCasingTests.cs) | tracked path in SourceRootValue | — | — | — | — |
| [RcReleaseBaselineTests](../../../tests/Antiphon.Tests/Scripts/RcReleaseBaselineTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [RcShadowDirtyDifferentialTests](../../../tests/Antiphon.Tests/Scripts/RcShadowDirtyDifferentialTests.cs) | tests/Antiphon.Tests/Scripts/Fixtures/card-1836-s6c-rc-port-golden.json | — | — | — | — |
| [RcShadowSampleCSharpTests](../../../tests/Antiphon.Tests/Scripts/RcShadowSampleCSharpTests.cs) | tests/Antiphon.Tests/Scripts/Fixtures/card-1836-s6c-rc-port-golden.json | — | — | — | — |
| [RegisteredCallerScriptTests](../../../tests/Antiphon.Tests/Scripts/RegisteredCallerScriptTests.cs) | directory walk in DeleteTree | — | — | — | — |
| [ReleaseImpactEvaluationTests](../../../tests/Antiphon.Tests/Scripts/ReleaseImpactEvaluationTests.cs) | .claude/settings.json, .gitignore, .gitattributes … | — | — | — | — |
| [ReleasePromoteTests](../../../tests/Antiphon.Tests/Scripts/ReleasePromoteTests.cs) | scripts/lib/Antiphon.Nightly/ReleasePromote.cs, scripts/fixtures/c599-policy-v2.json, scripts/release-cut.ps1 … | — | — | — | — |
| [ReleasePublishTests](../../../tests/Antiphon.Tests/Scripts/ReleasePublishTests.cs) | scripts/lib/Antiphon.Nightly/ReleaseCut.cs, scripts/release-cut.ps1, tests/test-execution-policy.json | — | — | — | — |
| [ReleaseSelectionAttemptTests](../../../tests/Antiphon.Tests/Scripts/ReleaseSelectionAttemptTests.cs) | scripts/fixtures/nightly/tests-regression/selection-attempt.json, tests/Antiphon.Tests/Fixtures/Card0418/manifest.json, tests/rc-project-categories.json … | — | — | — | — |
| [ReleaseStatusTests](../../../tests/Antiphon.Tests/Scripts/ReleaseStatusTests.cs) | .gitignore, scripts/refresh-server2-github-token.ps1, tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs … | — | — | — | — |
| [ReleaseTagAllocatorTests](../../../tests/Antiphon.Tests/Scripts/ReleaseTagAllocatorTests.cs) | scripts/fixtures/c599-policy-v2.json, scripts/release-cut.ps1, tests/test-execution-policy.json | — | — | — | 407 / 7 / 8f055f87 |
| [RetireTempRunnerTimestampTests](../../../tests/Antiphon.Tests/Scripts/RetireTempRunnerTimestampTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json, scripts/verify-docker-stack.ps1 | — | — | — | 3 / 4 / 8f055f87 |
| [RetiredTempContainerDockerTests](../../../tests/Antiphon.Tests/Scripts/RetiredTempContainerDockerTests.cs) | scripts/fixtures/c994-retired-temp-real-cases.mjs, scripts/fixtures/c1008-recycle-cleanup.mjs | — | — | — | — |
| [RetiredTempContainerHostTests](../../../tests/Antiphon.Tests/Scripts/RetiredTempContainerHostTests.cs) | scripts/fixtures/c1008-recycle-cases.json, scripts/fixtures/c994-production-compose-model.mjs | — | — | — | — |
| [ReviewEngineScriptsTests](../../../tests/Antiphon.Tests/Scripts/ReviewEngineScriptsTests.cs) | server/** | — | — | — | — |
| [RollingProductionMountTests](../../../tests/Antiphon.Tests/Scripts/RollingProductionMountTests.cs) | scripts/fixtures/c994-production-compose-model.mjs, scripts/fixtures/c1008-recycle-cases.json, scripts/fixtures/c1105-compose-v218-config.json | — | — | — | — |
| [RollingVolumeRecycleDockerTests](../../../tests/Antiphon.Tests/Scripts/RollingVolumeRecycleDockerTests.cs) | scripts/fixtures/c1008-recycle-real-cases.mjs, scripts/fixtures/c1008-recycle-cleanup.mjs | — | — | — | — |
| [RunCheckpointRepeatScriptTests](../../../tests/Antiphon.Tests/Scripts/RunCheckpointRepeatScriptTests.cs) | tests/Antiphon.Checkpoints.RepeatHost/Antiphon.Checkpoints.RepeatHost.csproj | 1/0/0 · 10.8 [20261107-022238-5d4b/CP-R3-9](#linux-receipt-keys) | — | — | 82 / 4 / 8f055f87 |
| [RunnerRestartOsTests](../../../tests/Antiphon.Tests/Scripts/RunnerRestartOsTests.cs) | tracked path in Real_platform_stops_only_this_checkouts_runner_and_guards_identity | — | — | — | — |
| [ScriptHarnessSharingTests](../../../tests/Antiphon.Tests/Scripts/ScriptHarnessSharingTests.cs) | scripts/fixtures/c889-c578-observation.ps1 | — | — | — | 2 / 1 / 8f055f87 |
| [ServerRepositoryMirrorsTests](../../../tests/Antiphon.Tests/Scripts/ServerRepositoryMirrorsTests.cs) | docker-compose.dev.yml, docker-compose.runner-grok.yml, docker/session-runner-grok/Dockerfile … | — | — | — | — |
| [TestClientFilterTests](../../../tests/Antiphon.Tests/Scripts/TestClientFilterTests.cs) | client/src/features/attention/attentionVisuals.test.ts, client/tsconfig.node.json, docker-compose.dev.yml … | — | — | — | 54 / 1 / 8f055f87 |
| [VolumeRetirementCloseExemptionTests](../../../tests/Antiphon.Tests/Scripts/VolumeRetirementCloseExemptionTests.cs) | scripts/c1008-retirement.sh, scripts/c590-remote.sh, scripts/retire-runner-volumes.ps1 | — | — | — | — |
| [VolumeRetirementContentTests](../../../tests/Antiphon.Tests/Scripts/VolumeRetirementContentTests.cs) | .gitignore, scripts/c1008-retirement.sh, server/** … | — | — | — | — |
| [VolumeRetirementPublicationTests](../../../tests/Antiphon.Tests/Scripts/VolumeRetirementPublicationTests.cs) | scripts/c1008-retirement.sh, scripts/c1008-content-audit.sh, scripts/c590-remote.sh … | — | — | — | — |

### Antiphon.Tests / Antiphon.Tests.TestHelpers

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [BuildOutputNestingTests](../../../tests/Antiphon.Tests/TestHelpers/BuildOutputNestingTests.cs) | directory walk in Own_output_stays_under_file_ceiling | — | — | — | — |
| [CheckCompactionWorkerLifetimeTests](../../../tests/Antiphon.Tests/TestHelpers/CheckCompactionWorkerLifetimeTests.cs) | directory walk in AssertDisposalEndsChildAsync | — | — | — | 39 / 2 / 8f055f87 |
| [ControlledLandingGitTests](../../../tests/Antiphon.Tests/TestHelpers/ControlledLandingGitTests.cs) | git ls-files, tracked path in C475_UnsupportedArgumentsCannotSucceedOrMutate | 2/0/0 · 0.167 [20261123-005040-8761/CP-3](#linux-receipt-keys) | — | — | — |
| [ControlledLandingRemovalReadTests](../../../tests/Antiphon.Tests/TestHelpers/ControlledLandingRemovalReadTests.cs) | directory walk, git ls-files, tracked path in Tracked_index_and_commit_reads_match_real_git_before_and_after_set_aside | — | — | — | — |
| [RcManifestJournalTests](../../../tests/Antiphon.Tests/TestHelpers/RcManifestJournalTests.cs) | directory walk, tracked path in LegacySeedAndReplacementOrder | — | — | — | — |
| [RcManifestRecoveryTests](../../../tests/Antiphon.Tests/TestHelpers/RcManifestRecoveryTests.cs) | directory walk, tracked path in KillAtBarrier | — | — | — | — |
| [RcManifestScaleTests](../../../tests/Antiphon.Tests/TestHelpers/RcManifestScaleTests.cs) | directory walk, tracked path in ScaleSamples | — | — | — | — |
| [ReportBudgetWorkspacePortabilityTests](../../../tests/Antiphon.Tests/TestHelpers/ReportBudgetWorkspacePortabilityTests.cs) | .gitignore | — | — | — | — |
| [StagedHelperBuildTests](../../../tests/Antiphon.Tests/TestHelpers/StagedHelperBuildTests.cs) | Directory.Build.props, Directory.Build.targets, tests/Antiphon.Tests/Antiphon.Tests.csproj | — | — | — | — |
| [TestDbBakedImageLifecycleTests](../../../tests/Antiphon.Tests/TestHelpers/TestDbBakedImageLifecycleTests.cs) | assembly inventory in Compute | — | — | — | — |
| [TestDbFixtureLazyInitializationTests](../../../tests/Antiphon.Tests/TestHelpers/TestDbFixtureLazyInitializationTests.cs) | directory walk, tracked path in PriorHashAsync | — | — | — | — |
| [TestDbFixtureSchemaCacheLifecycleTests](../../../tests/Antiphon.Tests/TestHelpers/TestDbFixtureSchemaCacheLifecycleTests.cs) | MigrationIds, assembly inventory in Cache_miss_migrates_captures_and_publishes | — | — | — | — |
| [TestPhaseSupervisorTests](../../../tests/Antiphon.Tests/TestHelpers/TestPhaseSupervisorTests.cs) | scripts/lib/nightly-class-costs.json, scripts/release-cut.ps1 | — | — | — | 8 / 2 / 8f055f87 |
| [TestSchemaCacheTests](../../../tests/Antiphon.Tests/TestHelpers/TestSchemaCacheTests.cs) | tests/Antiphon.Tests/Fixtures/Card0418/manifest.json | — | — | — | — |
| [TestSchemaSnapshotTests](../../../tests/Antiphon.Tests/TestHelpers/TestSchemaSnapshotTests.cs) | tracked path in Restored_store_clones_and_program_migrate_is_a_version_check | — | — | — | 95 / 3 / 8f055f87 |
| [TestTimingLifecycleTests](../../../tests/Antiphon.Tests/TestHelpers/TestTimingLifecycleTests.cs) | directory walk in C1514T_NativeLifecycle | — | — | — | 2 / 1 / 8f055f87 |
| [TestTimingRecorderTests](../../../tests/Antiphon.Tests/TestHelpers/TestTimingRecorderTests.cs) | directory walk, tracked path in C1514T_RecorderJournalMatchesTheTimingReader | — | — | — | — |

### Antiphon.TimeoutAudit.Tests / Antiphon.TimeoutAudit.Tests

| Class | Repository input or detection site | Linux observations · seconds | Windows B | Windows F | Earlier Windows seconds / nodes / run |
|---|---|---|---|---|---|
| [TimeoutAuditTests](../../../tests/Antiphon.TimeoutAudit.Tests/TimeoutAuditTests.cs) | scripts/lib/nightly-class-costs.json, tests/test-execution-policy.json | — | — | — | — |

## Linked declarations

- `Antiphon.TestSupport.NativeLinuxPolicyWitnessTests` compiles in `Antiphon.Agents.Pty.Tests`, `Antiphon.PtyHost.Tests`, `Antiphon.SessionRunner.Tests`, `Antiphon.Tests`.
- `Antiphon.TestSupport.TestClassificationGuardTests` compiles in `Antiphon.Agents.Pty.Tests`, `Antiphon.E2E`, `Antiphon.Messaging.Tests`, `Antiphon.PtyHost.Tests`, `Antiphon.SessionRunner.Tests`, `Antiphon.Tests`.
- `Antiphon.IO.WriteBehind.Tests.WriteBehindFileSystemTests` compiles in `Antiphon.IO.WriteBehind.Tests`, `Antiphon.Tests`.
- `Antiphon.TimeoutAudit.Tests.TimeoutAuditTests` compiles in `Antiphon.Tests`, `Antiphon.TimeoutAudit.Tests`.

## Linux receipt keys

File names within each retained checkpoint are `report.json` and `rows/<CP>/run.trx`. Match TRX `testId` to `TestDefinitions/UnitTest/TestMethod` including its codeBase. Identical bare names in different assemblies have separate rows. These are historical measurements.

| Run and row | Source commit | Actual filter |
|---|---|---|
| 20261106-065517-9cdf/REP-1 | `cc591873b3eefc7c72046f102e0663530af01446` | `/*/*/C0824DesktopLegacyReleaseSwitchTests/Late_switch_on_ref_only_mirror_continuation_sends_no_remove` |
| 20261106-075233-9edf/CP-2 | `4eb141231c8c1166704e958a4535dd23476cd380` | `/*/*/(C0824RunnerBranchRefsTests*)\|(C0824WorkspaceSlotColdResetTests*)\|(C0824WorkspaceSlotNativeRehomeTests*)\|(C0824WorkspaceSlotOverflowTests*)\|(C0824WorkspaceSlotProcessScanTests*)\|(C0824WorkspaceSlotRehomeTests*)\|(C0824WorkspaceSlotResetTests*)\|(C1677RetentionDispatchTests*)\|(C1677RetentionEngineTests*)\|(C1677RetentionInventoryTests*)\|(C1677RetentionNativeProcessTests*)\|(C0824WorkspaceSlotCustodyTests*)/*` |
| 20261106-075233-9edf/CP-3 | `4eb141231c8c1166704e958a4535dd23476cd380` | `/*/*/*/*[Category=Unit]` |
| 20261106-220208-f4c6/CP-4 | `5b2e7aab481832bccc256d2269a1a99fae10a88a` | `/*/*/(C1708ReflogSalvageTests*)\|(LiveLaneRunnerTests*)\|(SlowTestRegistryMergeTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` |
| 20261106-225046-362a/CP-BASE | `0798c501a28f4eb6e08a6f8cdbd185105c66ee5c` | `/*/Antiphon.Tests.Scripts/HostJqPrerequisiteScriptTests*/C1025_Preflight_is_fresh_for_each_entry` |
| 20261107-022238-5d4b/CP-2 | `87bf11823b977703c7f7fd7ce3fe9a29977fd089` | `/*/Antiphon.Tests.Checkpoints/(CheckpointManifestTests*)\|(CheckpointImportTests*)\|(TrxReportTests*)\|(XmlInputSecurityTests*)\|(PlanCoverageCommandTests*)/*` |
| 20261107-022238-5d4b/CP-GUARDS | `87bf11823b977703c7f7fd7ce3fe9a29977fd089` | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(CheckpointToolProjectContractTests*)/*` |
| 20261107-022238-5d4b/CP-R3-9 | `87bf11823b977703c7f7fd7ce3fe9a29977fd089` | `/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatFailurePartialAndInvalidInput` |
| 20261107-022238-5d4b/CP-REPEAT | `87bf11823b977703c7f7fd7ce3fe9a29977fd089` | `/*/Antiphon.Tests.Checkpoints/CheckpointRepeatHostTests*/*` |
| 20261107-093220-718b/CP-1 | `aea7cd0a3de883a0a9b4e60cae70dfa8df2323f5` | `/*/*/CheckpointColdBuildTests/*` |
| 20261107-093220-718b/CP-2 | `aea7cd0a3de883a0a9b4e60cae70dfa8df2323f5` | `/*/*/(C1307CliCancellationTests*)\|(WaitCommandTests*)\|(CheckpointToolCopyCleanupTests*)\|(CheckpointStatePublicationTests*)\|(CheckpointAppTests*)\|(ReportWriterTests*)\|(CheckpointSourceExecutionTests*)\|(RunCheckpointSourceScriptTests*)/*` |
| 20261107-093220-718b/CP-4 | `aea7cd0a3de883a0a9b4e60cae70dfa8df2323f5` | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuard*)/*` |
| 20261122-183140-57a1/CP-1 | `655f8ae6c488fc76184facbad69ee75b70667a5a` | `/*/*/JqRunnerImageContractTests/C1054_Jq_row_records_found_and_resolved_paths` |
| 20261123-005040-8761/CP-3 | `b0473c5245c474755a3e69922cdfe0186d15e4a7` | `/*/*/ControlledLandingGitTests/C688_UnregisterDropsHeadFilesWithoutTouchingSetAsideOrRecreatedPath` |
| 20261123-053028-07ea/CP-2 | `eefad7d1802defd9043980ca9e985438f50c3291` | `/*/*/(ProjectReadinessBatchTests*)\|(ProjectReadinessCollationTests*)\|(ProjectReadinessTests*)\|(ProjectSetupServiceTests*)\|(WorkingDirectoryIdentityTests*)/*` |
| 20261123-053028-07ea/CP-4 | `eefad7d1802defd9043980ca9e985438f50c3291` | `/*/*/(AgentAttachHerdrTests*)\|(AgentControlServiceIntegrationTests*)\|(AgentCreateRaceTests*)\|(AgentCreateSupervisionTests*)\|(AgentRemoteControlGateTests*)\|(AgentServiceIntegrationTests*)\|(AgentSessionBackendTests*)\|(AgentSessionLaunchFailureTests*)\|(AgentSessionServiceIntegrationTests*)\|(AgentStartRecoveryTests*)\|(AgentSupervisionTests*)\|(AgentSystemPromptLaunchTests*)\|(AgentTaskPoolTests*)\|(BoardServiceIntegrationTests*)\|(ContextCompactionAgentTests*)\|(DiagnosticsBundleServiceTests*)\|(HerdrAlwaysOnChannelParityTests*)\|(HerdrLabelFollowConcurrencyTests*)\|(HerdrPlacementSettingsTests*)\|(HerdrSupervisionBackoffTests*)\|(PhoneHomeStandingLaunchTests*)\|(ProjectCascadeForeignKeyTests*)\|(RemoteExecutableLaunchTests*)\|(SessionHealthTests*)/*` |
| 20261123-053028-07ea/CP-9 | `eefad7d1802defd9043980ca9e985438f50c3291` | `/*/*/(AgentControlServiceIntegrationTests*)\|(AgentSystemPromptLaunchTests*)/(Start_returns_409_model_disabled_when_the_agent_alias_is_held*)\|(Start_of_a_haiku_agent_succeeds_while_fable_is_held*)\|(Interactive_launch_maps_frontier_level_to_fable*)` |
| 20261123-070247-05e4/CP-10 | `a8a6c6f35c7ca1175465d89d93ea42eed390f57e` | `/*/*/GrokRulesLaunchRefusalTests/*` |
| 20261123-070247-05e4/CP-12 | `a8a6c6f35c7ca1175465d89d93ea42eed390f57e` | `/*/*/(ProjectReadinessCollationTests*)\|(TestTimingOperationLabelTests*)/*` |
| 20261123-081710-d7c6/CP-1 | `d413d4058e9944dff14747bd69cc93d0c7bf2beb` | `/*/*/CheckpointOutputConfinementTests/(windows_confined_build_leaves_use_app_host_unset*)\|(unevaluable_import_is_refused_before_the_build*)` |
| 20261123-081710-d7c6/CP-2 | `d413d4058e9944dff14747bd69cc93d0c7bf2beb` | `/*/*/LandingRemovalPolicyControlTests/(C665_LockedFileMidDeleteResumesOnLaterPass*)\|(C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded*)\|(C665_DisposableOnlyProceedsToRemoval*)` |
| 20261123-081710-d7c6/CP-3 | `d413d4058e9944dff14747bd69cc93d0c7bf2beb` | `/*/*/WorktreeNoFollowDeleteTests/(C1717_B3_WindowsOpenWriterRefusesTheDelete*)\|(C1717_B3_OpenWriterRewriteTruncatesEarlierBytes*)` |
| 20261123-175537-70a3/CP-7 | `bb5bdb87149047270e6303593825bac7ed7b1e18` | `/*/*/HerdrAlwaysOnChannelParityTests/Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply` |
| 20261125-142218-704b/CP-2 | `2fd61b643ea4231fa732cd4969b5284d43b33f39` | `/*/*/(ChannelOutboundRecoveryTests*)\|(ReviewDelegationRunnerApiTests*)\|(ProductionRunnerIsolationTests*)\|(C0824WorkspaceSlotRoleGateTests*)/(Running_converter_reconciles_after_dispatcher_death*)\|(Native_gate_read_retries_a_sharing_violation*)\|(The_reviewer_is_a_read_only_review_task_at_the_head_and_its_report_completes_the_node*)\|(The_shared_host_reads_no_user_secrets_store*)\|(None_stops_new_leases_while_releases_continue*)\|(Removed_role_slot_is_released_normally*)` |
| 20261125-142218-704b/CP-3 | `2fd61b643ea4231fa732cd4969b5284d43b33f39` | `/*/*/(ChannelOutboundProbeModelTests*)\|(ProbeModelComparisonTests*)\|(AppDbContextModelTests*)\|(ReviewEngineMigrationTests*)\|(CheckpointCallerBudgetTests*)\|(ScriptHarnessHandleProbeTests*)\|(ScriptHarnessLinuxOwnershipTests*)/*` |
| 20261125-142218-704b/CP-4 | `2fd61b643ea4231fa732cd4969b5284d43b33f39` | `/*/*/(OrchestratorWorkspaceWarningTests*)\|(TestFixtureCleanupGuardTests*)\|(ReviewMirrorCheckoutTests*)\|(DesktopPostgresTests*)\|(DeployLocalTests*)\|(PgQpsTests*)\|(MeasureSessionStateCacheScriptTests*)\|(PtyHostReaperTests*)\|(ZombieAgentReaperTests*)\|(BootstrapCheckTests*)\|(VerifyDevStackSmokeTests*)/*` |
| 20261125-145444-fd84/CP-3 | `43855daf07739d5b301a517cba33bfa90ec80b5d` | `/*/*/(RunSchedulerTests*)\|(CheckpointAppTests*)\|(CheckpointConfinementLedgerTests*)\|(CheckpointBuildBindingPathTests*)\|(RowRunnerTests*)\|(ProcessDriverTests*)\|(EvidenceFolderTests*)/*` |
| 20261125-145444-fd84/CP-4 | `43855daf07739d5b301a517cba33bfa90ec80b5d` | `/*/*/(CheckpointReceiptSafetyTests*)\|(CheckpointTerminalReceiptTests*)\|(HostCleanupCheckpointReceiptTests*)\|(ImpactedClassesCensusTests*)\|(OutputCleanupTests*)\|(ProcessSpawnLimitTests*)\|(CheckpointToolCommandTests*)/*` |
| 20261125-170459-d549/CP-1 | `0e634a2b289b1ef8a9a68367e7d9791ac8a8bec5` | `/*/Antiphon.Tests.Checkpoints/CheckpointWarmBuildTests/ordinary_warm_replay_requires_a_verified_binding_and_gate` |
| 20261125-170459-d549/CP-2 | `0e634a2b289b1ef8a9a68367e7d9791ac8a8bec5` | `/*/Antiphon.Tests.Checkpoints/EvidenceFolderTests/generated_replay_preserves_native_property_arguments` |
| 20261125-205319-5b8a/CP-5 | `4a2014a4a57d900ec24dd20405f0ca2ff396a9ff` | `/*/*/(CustodyHelperContractTests*)\|(DockerStackContractTests*)\|(DockerStackDocumentationTests*)/*` |
| 20261125-205319-5b8a/CP-6 | `4a2014a4a57d900ec24dd20405f0ca2ff396a9ff` | `/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` |
| 20261125-205319-5b8a/CP-7 | `4a2014a4a57d900ec24dd20405f0ca2ff396a9ff` | `/*/*/LiveLaneRunnerTests/WrapperOnlyPaidManifest` |
| 20261126-035349-6918/CP-G2 | `46417306d6c85d6ce2a1e1bdad734fa68c1e6b0a` | `/*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(ProcessSpawnLimitTests*)/*` |
| 20261126-044237-3c1a/CP-2 | `46417306d6c85d6ce2a1e1bdad734fa68c1e6b0a` | `/*/*/(LinuxRunnerCustodyLifecycleTests*)\|(Card1619LinuxCustodyStopIntentTests*)/*` |
| 20261126-050312-e408/CP-2 | `abc7b957674cc99cf7d9ff01f2e0e6d4be1c5b3b` | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestLaneCategoryGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` |
| 20261126-074134-4c4f/CP-1 | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(DerivedImpactIndexTests*)\|(C1729LayeringGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(NativeLinuxPolicyWitnessMutationTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` |
| 20261126-074134-4c4f/CP-2 | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/(PlanCoverageBindingTests*)\|(IncrementalSelectionSoundnessTests*)\|(StatusSnapshotScriptTests*)\|(TaskProgressStallSweepTests*)\|(SessionRuntimeActionTests*)/*` |
| 20261126-074134-4c4f/CP-3 | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/(CheckpointAdmissionContractTests*)\|(AgentSessionRuntimeTests*)\|(TurnEndGapRestartDeliveryTests*)/(C1616_*)\|(C1259_*)\|(C488_Wakeup_cut_holds_the_row_only_when_in_process_hints_are_dropped*)` |
| 20261126-074134-4c4f/CP-4 | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/NightlyNativeOwnershipTests/*` |
| 20261126-074134-4c4f/CP-5 | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/(LinuxOwnedGitProcessTests*)\|(CustodyHelperContractTests*)\|(BlockedReplyReportRetentionTests*)\|(CallerWakeTests*)\|(ExpectedPlatformSkipRosterTests*)/*` |
| 20261126-074134-4c4f/CP-6 | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/C0824WorkspaceSlotServerTests/C2061_SettledQuarantineRoundTripWithLinuxHolder` |
| 20261126-074134-4c4f/CP-8 | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/(C0824WorkspaceSlotRehomeTests*)\|(LinuxCustodyProbeTests*)\|(Card2060PlacementLedgerTests*)/*` |
