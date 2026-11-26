# CARD-2082 qualification timing by owning assembly and class

The [verification design](2026-11-26-card-2082-repository-invariants-test-design.md#qualification-timing-and-cross-platform-outcomes) uses this matrix to cost the proposed invariant selection. Each Windows cell gives **passed/failed/not executed · total / longest case seconds** from the retained TRX aggregate. An em dash means no observed class row. A zero is an observed count. Every observed Windows row has a recorded duration. SPLIT includes all historical cases of the parent, including behavioral cases that keep their current lane.

B = `b8cbfb24` at `2fd61b643ea4231fa732cd4969b5284d43b33f39`. The run ended with a test failure and incomplete coverage for native groups 129 and 142. F = `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267`. **F is partial**. There was no summary and 1,281 known native groups had completeness flags when the export was taken.

## Antiphon.Agents.Pty.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial |
|---|---|---|---|
| Antiphon.Agents.Pty.Tests.ConPtyEnvironmentIsolationGuardTests | MOVE | 6/0/0 · 0.7364782 / 0.7199111 | — |
| Antiphon.Agents.Pty.Tests.LiveClaudeEvidenceWiringTests | SPLIT | 3/0/0 · 16.0327249 / 8.6808275 | — |
| Antiphon.Agents.Pty.Tests.LiveClaudeExecutableWiringTests | SPLIT | 3/0/0 · 1.1074087 / 0.8561263 | — |
| Antiphon.Agents.Pty.Tests.LiveClaudeInventoryTests | MOVE | 1/0/0 · 1.4036368 / 1.4036368 | — |
| Antiphon.Agents.Pty.Tests.ProcessSpawnLimitTests | SPLIT | 2/0/0 · 0.0014386 / 0.0012727 | — |
| Antiphon.Agents.Pty.Tests.UnixPtyDeliveryEnvelopeTests | SPLIT | — | — |
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | MOVE | 3/0/0 · 0.5436091 / 0.2357205 | — |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 0.1007904 / 0.1007904 | — |

## Antiphon.E2E

| Fully qualified class | Decision | Windows B | Windows F · partial |
|---|---|---|---|
| Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests | SPLIT | 3/0/0 · 57.2694873 / 33.6158903 | — |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 0.0490789 / 0.0490789 | — |

## Antiphon.GitInProcess.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial |
|---|---|---|---|
| Antiphon.Tests.Infrastructure.GitInProcessAssemblyTests | MOVE | 3/0/0 · 8.5709067 / 6.450049 | — |

## Antiphon.MarkdownPdf.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial |
|---|---|---|---|
| Antiphon.MarkdownPdf.Tests.FakeBrowserSolutionReleaseTests | SPLIT | 1/0/0 · 16.0128726 / 16.0128726 | — |

## Antiphon.Messaging.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial |
|---|---|---|---|
| Antiphon.Messaging.Tests.Contract.ContractSchemaTests | SPLIT | 2/0/0 · 0.0269672 / 0.0149286 | — |
| Antiphon.Messaging.Tests.Gateway.LibrarySufficiencyTests | SPLIT | 7/0/0 · 0.1295824 / 0.0991976 | — |
| Antiphon.Messaging.Tests.PublishWorkflowSanityTests | MOVE | 2/0/0 · 0.035036 / 0.0181944 | — |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 0.0233745 / 0.0233745 | — |

## Antiphon.PtyHost.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial |
|---|---|---|---|
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | MOVE | 3/0/0 · 0.276233 / 0.1395751 | — |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 0.038427 / 0.038427 | — |

## Antiphon.Resilience.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial |
|---|---|---|---|
| Antiphon.Tests.Infrastructure.Resilience.ResilienceOptionsAndTelemetryTests | SPLIT | 10/0/0 · 0.1347563 / 0.0787948 | — |
| Antiphon.Tests.Infrastructure.ResilienceAssemblyTests | MOVE | 2/0/0 · 28.786069 / 23.4040685 | — |

## Antiphon.Server.Git.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial |
|---|---|---|---|
| Antiphon.Tests.Infrastructure.ServerGitAssemblyTests | MOVE | 3/0/0 · 6.6255928 / 6.6174382 | — |

## Antiphon.SessionRunner.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial |
|---|---|---|---|
| Antiphon.SessionRunner.Tests.C0824WindowsSlotHolderTests | SPLIT | 4/0/0 · 13.6312475 / 6.9420866 | 4/0/0 · 16.9613974 / 9.5569036 |
| Antiphon.SessionRunner.Tests.C0824WorkspaceSlotTimeoutBudgetTests | SPLIT | 20/0/0 · 0.006535 / 0.0047065 | — |
| Antiphon.SessionRunner.Tests.C1677TempFamilyRegistryTests | SPLIT | 3/0/0 · 0.4638604 / 0.4465431 | — |
| Antiphon.SessionRunner.Tests.C1722GitTimeoutBudgetTests | SPLIT | 101/0/0 · 0.0180995 / 0.0062787 | — |
| Antiphon.SessionRunner.Tests.C1818GcOwnerRobustnessTests | SPLIT | 10/0/0 · 5.8653091 / 2.8919058 | — |
| Antiphon.SessionRunner.Tests.CustodyNamespaceBootstrapTests | SPLIT | — | — |
| Antiphon.SessionRunner.Tests.HostStatsProbeParseTests | SPLIT | 11/0/0 · 0.0394929 / 0.0095057 | — |
| Antiphon.SessionRunner.Tests.HostStatsSamplerTests | SPLIT | 7/0/0 · 0.0406475 / 0.0161381 | — |
| Antiphon.SessionRunner.Tests.HostTopConsumerTests | SPLIT | 31/0/0 · 14.7605753 / 3.4421332 | — |
| Antiphon.SessionRunner.Tests.ProcessSpawnLimitTests | SPLIT | 2/0/0 · 0.0847244 / 0.0844447 | — |
| Antiphon.SessionRunner.Tests.PtyHostDeploymentGuardTests | SPLIT | 3/0/0 · 0.037252 / 0.0207637 | — |
| Antiphon.SessionRunner.Tests.StandingRunnerCwdTests | SPLIT | 25/0/0 · 0.0383279 / 0.0136702 | — |
| Antiphon.SessionRunner.Tests.WindowsShellGuardAuditTests | MOVE | 3/0/0 · 0.5507068 / 0.2741637 | — |
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | MOVE | 3/0/0 · 0.5631951 / 0.2036858 | — |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 0.1332916 / 0.1332916 | — |

## Antiphon.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial |
|---|---|---|---|
| Antiphon.Tests.Agents.AgentRegistrySettingsTests | SPLIT | 23/0/0 · 0.386933 / 0.0450052 | — |
| Antiphon.Tests.Agents.GrokLinuxBlockingPromptTests | SPLIT | 11/0/0 · 0.175271 / 0.0523162 | — |
| Antiphon.Tests.ApiKeys.ProviderEndpointProjectionTests | SPLIT | 36/0/0 · 0.0771094 / 0.0236083 | — |
| Antiphon.Tests.Application.AdditiveMigrationRuleWitnessTests | MOVE | 8/0/0 · 11.0892528 / 8.4716002 | — |
| Antiphon.Tests.Application.AgentContextContractTests | MOVE | 2/0/0 · 0.0190406 / 0.0123524 | — |
| Antiphon.Tests.Application.AgentReplyStyleTests | SPLIT | 22/0/0 · 2.9034854 / 2.300478 | — |
| Antiphon.Tests.Application.AgentTaskGetPollMetricsTests | SPLIT | 11/0/0 · 0.4067011 / 0.0668746 | — |
| Antiphon.Tests.Application.AgentTaskInternalDecisionMigrationTests | SPLIT | 2/0/0 · 81.3288977 / 41.1107212 | — |
| Antiphon.Tests.Application.AgentTaskLandApprovalPersistenceTests | SPLIT | 10/0/0 · 151.0056595 / 66.2311318 | — |
| Antiphon.Tests.Application.AgentTaskLandingPersistenceTests | SPLIT | 2/0/0 · 66.5946586 / 63.545051 | — |
| Antiphon.Tests.Application.AreaMapContractTests | MOVE | 6/0/0 · 0.2604048 / 0.2280537 | — |
| Antiphon.Tests.Application.BlockedTaskParkProjectionTests | SPLIT | 2/0/0 · 0.1324904 / 0.1061099 | — |
| Antiphon.Tests.Application.BlockedTaskParkStateTests | SPLIT | 2/0/0 · 42.6987024 / 28.7512022 | — |
| Antiphon.Tests.Application.BootStallDocumentationTests | MOVE | 5/0/0 · 0.0967738 / 0.0473519 | — |
| Antiphon.Tests.Application.C0824DesktopLegacyReleaseSettingsTests | SPLIT | 34/0/0 · 1.6609413 / 1.1497702 | — |
| Antiphon.Tests.Application.C0824RefOnlyIdentityTests | SPLIT | 7/0/0 · 39.2904957 / 24.4556871 | — |
| Antiphon.Tests.Application.C0824RefOnlySnapshotMigrationTests | SPLIT | 4/0/0 · 14.6209811 / 5.3267485 | — |
| Antiphon.Tests.Application.C1514HostLoadMigrationTests | SPLIT | 12/0/0 · 3.106943 / 1.0931258 | — |
| Antiphon.Tests.Application.C1677RetentionSettingsTests | SPLIT | 18/0/0 · 0.3381035 / 0.1740606 | — |
| Antiphon.Tests.Application.C1744G2PlacementTests | SPLIT | 2/0/0 · 3.4648612 / 1.8540729 | — |
| Antiphon.Tests.Application.Card1619ExitMigrationIdTests | SPLIT | 3/0/0 · 51.5913386 / 25.773595 | — |
| Antiphon.Tests.Application.Card1619ExitPersistenceTests | SPLIT | — | — |
| Antiphon.Tests.Application.CardCleanupSchedulingSchemaTests | SPLIT | 1/0/0 · 57.8391607 / 57.8391607 | 0/1/0 · 75.2494592 / 75.2494592 |
| Antiphon.Tests.Application.CardFilePrivacyDocumentationTests | MOVE | 1/0/0 · 0.0583031 / 0.0583031 | — |
| Antiphon.Tests.Application.CardRelationshipSchemaTests | SPLIT | 3/0/0 · 4.881739 / 3.5219084 | — |
| Antiphon.Tests.Application.CardWorktreeCleanupRecoveryTests | SPLIT | 6/0/0 · 103.1016185 / 37.731056 | — |
| Antiphon.Tests.Application.ChannelOutboundContractTests | SPLIT | 2/0/0 · 0.2036254 / 0.1770198 | — |
| Antiphon.Tests.Application.ChannelOutboundDurabilitySchemaTests | SPLIT | 4/0/0 · 17.6112364 / 17.2125188 | — |
| Antiphon.Tests.Application.ChannelOutboundStorageTests | SPLIT | 33/0/0 · 11.6751581 / 4.127882 | — |
| Antiphon.Tests.Application.CheckCompactionFactEquivalenceTests | SPLIT | 3/0/0 · 0.3452138 / 0.1546904 | — |
| Antiphon.Tests.Application.CheckpointManifestDocumentationTests | MOVE | 10/0/0 · 0.6422121 / 0.1894483 | 10/0/0 · 0.5532357 / 0.3943483 |
| Antiphon.Tests.Application.CodexGpt62LadderTests | SPLIT | 4/0/0 · 0.0201197 / 0.0120596 | — |
| Antiphon.Tests.Application.CommitOnSettleDocumentationTests | MOVE | 2/0/0 · 0.0230213 / 0.0153405 | — |
| Antiphon.Tests.Application.D2AdvisoryDocumentationTests | MOVE | 7/0/0 · 0.4984053 / 0.1098188 | — |
| Antiphon.Tests.Application.DelegateScriptCapabilityTests | SPLIT | 7/0/0 · 5.9489318 / 1.4704627 | — |
| Antiphon.Tests.Application.DelegationAllowedRootsFileTests | SPLIT | 1/0/0 · 0.0020428 / 0.0020428 | — |
| Antiphon.Tests.Application.DelegationCapabilityContractTests | SPLIT | 2/0/0 · 0.0644733 / 0.0517561 | — |
| Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests | SPLIT | 382/0/0 · 333.0388644 / 9.5767793 | — |
| Antiphon.Tests.Application.DispatchBaseNotificationTests | SPLIT | 57/0/0 · 270.7160821 / 90.3730501 | — |
| Antiphon.Tests.Application.DispatchBaseWarningRosterTests | MOVE | 11/0/0 · 2.4060082 / 0.6797194 | — |
| Antiphon.Tests.Application.DispatchConcurrencyMigrationTests | SPLIT | 3/0/0 · 128.7221396 / 63.9962185 | — |
| Antiphon.Tests.Application.DispatchHoldLedgerTests | SPLIT | 29/0/0 · 0.0770742 / 0.0406976 | — |
| Antiphon.Tests.Application.EfLambdaClockGuardTests | MOVE | 10/0/0 · 2.7228243 / 1.9785561 | — |
| Antiphon.Tests.Application.Grok47RoutingNameCensusTests | MOVE | 1/0/0 · 0.007872 / 0.007872 | — |
| Antiphon.Tests.Application.HerdrPlacementSettingsTests | SPLIT | 9/0/0 · 128.3368956 / 75.7600774 | — |
| Antiphon.Tests.Application.HostCleanupOrchestrationTests | SPLIT | 2/0/0 · 30.3080511 / 30.303654 | — |
| Antiphon.Tests.Application.InstructionBundleTests | SPLIT | 78/0/0 · 2.3101894 / 0.4868335 | — |
| Antiphon.Tests.Application.InstructionFileStampTests | SPLIT | 15/0/0 · 0.2420885 / 0.0505726 | — |
| Antiphon.Tests.Application.InstructionFragmentTests | SPLIT | 7/0/0 · 0.0899194 / 0.0262624 | — |
| Antiphon.Tests.Application.KeepSixDocumentationTests | MOVE | 3/0/0 · 0.0585556 / 0.0418612 | — |
| Antiphon.Tests.Application.LandGraphCommandTests | SPLIT | 22/0/0 · 0.6998729 / 0.1663029 | — |
| Antiphon.Tests.Application.LandGraphEvaluationTests | SPLIT | 159/0/1 · 556.6583176 / 82.5686353 | — |
| Antiphon.Tests.Application.LandGraphLandRefreshTests | SPLIT | 19/0/0 · 36.9726973 / 13.2677318 | 19/0/0 · 92.2551059 / 30.8585975 |
| Antiphon.Tests.Application.LandingProtocolHarnessTests | SPLIT | 18/0/0 · 108.0022055 / 9.111558 | — |
| Antiphon.Tests.Application.LandNonCodeSkipTests | SPLIT | 131/0/0 · 478.2017 / 64.5282106 | — |
| Antiphon.Tests.Application.LandRequestWriteDiagnosticTests | SPLIT | 15/0/0 · 116.4570302 / 58.7984705 | — |
| Antiphon.Tests.Application.LandVerificationFailureMigrationTests | SPLIT | 1/0/0 · 5.689933 / 5.689933 | — |
| Antiphon.Tests.Application.LaunchModelArgumentAppenderTests | SPLIT | 2/0/0 · 0.0347648 / 0.0175675 | — |
| Antiphon.Tests.Application.MechanicalPcContractTests | MOVE | 11/0/0 · 0.1934829 / 0.0446294 | — |
| Antiphon.Tests.Application.MidTurnInterruptDocumentationTests | MOVE | 1/0/0 · 0.0048161 / 0.0048161 | — |
| Antiphon.Tests.Application.OrchestratorBundleOwnershipTests | MOVE | 11/0/0 · 0.077008 / 0.0360716 | — |
| Antiphon.Tests.Application.OrchestratorInstructionsGuidanceTests | MOVE | 6/0/0 · 0.2886588 / 0.0989133 | — |
| Antiphon.Tests.Application.PerTaskFinalUnitLaneWitnessTests | MOVE | 1/0/0 · 0.0261657 / 0.0261657 | — |
| Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests | SPLIT | 1/0/0 · 2.0343841 / 2.0343841 | — |
| Antiphon.Tests.Application.PipelineDefinitionDocumentationTests | MOVE | 1/0/0 · 0.0043265 / 0.0043265 | — |
| Antiphon.Tests.Application.PipelineHandoffRouteDocumentationTests | MOVE | 4/0/0 · 0.0390688 / 0.0216643 | — |
| Antiphon.Tests.Application.PipelineSpeedIntegrationTests | SPLIT | 48/0/0 · 336.024724 / 28.4140338 | — |
| Antiphon.Tests.Application.PoolClaudeEffortMigrationTests | SPLIT | 1/0/0 · 46.2711155 / 46.2711155 | — |
| Antiphon.Tests.Application.PostLandMutationAdmissionTests | SPLIT | 27/0/0 · 481.9165685 / 51.9222802 | — |
| Antiphon.Tests.Application.PostLandMutationContractTests | MOVE | 30/0/0 · 0.0866019 / 0.0563957 | — |
| Antiphon.Tests.Application.PostLandRetrospectiveContractTests | MOVE | 4/0/0 · 0.0197727 / 0.0105308 | — |
| Antiphon.Tests.Application.ProviderEndpointDocumentationTests | MOVE | 3/0/0 · 0.055962 / 0.0305372 | — |
| Antiphon.Tests.Application.PublishedRoutingDocumentationTests | MOVE | 9/0/0 · 0.2999188 / 0.1169474 | — |
| Antiphon.Tests.Application.ReceiptScanDestinationTests | SPLIT | 34/0/0 · 0.0228814 / 0.0134685 | — |
| Antiphon.Tests.Application.ReleaseBranchModelDocumentationTests | MOVE | 3/0/0 · 0.050989 / 0.0200885 | — |
| Antiphon.Tests.Application.ReleaseGateActivationTests | SPLIT | 7/0/0 · 2.000713 / 1.1979425 | — |
| Antiphon.Tests.Application.RepairSourceDocumentationTests | MOVE | 3/0/0 · 0.0216651 / 0.0101951 | — |
| Antiphon.Tests.Application.RequestedModelAliasMigrationTests | SPLIT | 1/0/0 · 5.6248503 / 5.6248503 | — |
| Antiphon.Tests.Application.ReviewRoundsPolicyTests | SPLIT | 30/0/0 · 0.0333458 / 0.0096266 | — |
| Antiphon.Tests.Application.ReviewTestQualityChecklistWitnessTests | MOVE | 4/0/0 · 0.6764457 / 0.3509118 | — |
| Antiphon.Tests.Application.RunnerAlarmWiringTests | SPLIT | 1/0/0 · 0.5316626 / 0.5316626 | — |
| Antiphon.Tests.Application.RunnerBranchContractDocumentationTests | MOVE | 10/0/0 · 1.3446987 / 0.7296776 | — |
| Antiphon.Tests.Application.RunnerDefaultGuidanceTests | MOVE | 4/0/0 · 0.0223326 / 0.0083019 | — |
| Antiphon.Tests.Application.RunnerReadSourcePinTests | MOVE | 1/0/0 · 0.0118075 / 0.0118075 | — |
| Antiphon.Tests.Application.ScopedVerificationInstructionTests | MOVE | 17/0/0 · 0.3327362 / 0.0752117 | — |
| Antiphon.Tests.Application.SequentialOverrideWordingTests | MOVE | 4/0/0 · 0.3115675 / 0.0895603 | — |
| Antiphon.Tests.Application.SessionRunnerAbsenceEvidenceDocumentationTests | MOVE | 1/0/0 · 0.1452859 / 0.1452859 | — |
| Antiphon.Tests.Application.SessionTranscriptSummaryMigrationTests | SPLIT | 3/0/0 · 33.1386003 / 33.124518 | — |
| Antiphon.Tests.Application.SettlementSyncDebtSchemaTests | SPLIT | 1/0/0 · 4.223898 / 4.223898 | — |
| Antiphon.Tests.Application.SettlementSyncRecoveryTests | SPLIT | 30/0/0 · 230.3379649 / 27.3997538 | — |
| Antiphon.Tests.Application.SiblingInspectionDeadlineWitnessTests | SPLIT | 1/0/0 · 0.0674787 / 0.0674787 | — |
| Antiphon.Tests.Application.SpecialistRoleContractTests | SPLIT | 2/0/0 · 4.3861377 / 4.385907 | — |
| Antiphon.Tests.Application.SpecialistRoutingScriptTests | SPLIT | 3/0/0 · 10.5187429 / 5.4664801 | — |
| Antiphon.Tests.Application.StableBuildOutputDocumentationTests | MOVE | 39/0/0 · 0.170343 / 0.0370215 | 39/0/0 · 0.3609181 / 0.0677063 |
| Antiphon.Tests.Application.StandingBootDocumentationTests | MOVE | 3/0/0 · 0.0909195 / 0.0471221 | — |
| Antiphon.Tests.Application.StandingPipelinePolicyDocumentationTests | MOVE | 12/0/0 · 0.7934047 / 0.2259365 | — |
| Antiphon.Tests.Application.StandingRestartEscalationTests | SPLIT | 52/0/0 · 83.1329168 / 18.3169273 | — |
| Antiphon.Tests.Application.StandingSessionOwnershipTests | SPLIT | 5/0/0 · 12.7323564 / 5.222331 | — |
| Antiphon.Tests.Application.StandingSpecialistRoutingMigrationTests | SPLIT | 4/0/0 · 57.6239455 / 46.6826229 | — |
| Antiphon.Tests.Application.TaskPlatformGuidanceTests | MOVE | 5/0/0 · 0.093754 / 0.0758856 | — |
| Antiphon.Tests.Application.TaskPlatformPersistenceTests | SPLIT | 3/0/0 · 38.0337731 / 35.4955946 | — |
| Antiphon.Tests.Application.UnixDeliveryCeilingTests | SPLIT | 9/0/0 · 0.0554168 / 0.0301635 | — |
| Antiphon.Tests.Application.WhyMissedWriteUpContractTests | MOVE | 1/0/0 · 0.0103048 / 0.0103048 | — |
| Antiphon.Tests.Application.WorktreeBaseSelectionTests | SPLIT | 16/0/0 · 21.599781 / 6.4295633 | — |
| Antiphon.Tests.Application.WorktreeResidueScriptTests | SPLIT | 3/0/0 · 0.1105292 / 0.0427434 | — |
| Antiphon.Tests.Checkpoints.BuildSlotClientClockGuardTests | MOVE | — | 12/0/0 · 2.8572274 / 1.656625 |
| Antiphon.Tests.Checkpoints.CheckpointAdmissionContractTests | SPLIT | 10/1/0 · 61.8383647 / 10.1070494 | 11/0/0 · 69.3869261 / 13.1827706 |
| Antiphon.Tests.Checkpoints.CheckpointCallerBudgetTests | SPLIT | 9/0/0 · 12.0013783 / 1.5581338 | 9/0/0 · 12.9469838 / 1.6626534 |
| Antiphon.Tests.Checkpoints.CheckpointReadStallWitnessTests | SPLIT | 9/0/0 · 0.0285705 / 0.0071828 | 9/0/0 · 0.0222772 / 0.0072883 |
| Antiphon.Tests.Checkpoints.CheckpointRepeatDocumentationTests | MOVE | 1/0/0 · 0.0185418 / 0.0185418 | — |
| Antiphon.Tests.Checkpoints.CheckpointTempUsageTests | SPLIT | 2/0/0 · 3.663723 / 2.288585 | 2/0/0 · 3.9024122 / 2.4986874 |
| Antiphon.Tests.Checkpoints.EmbeddedCheckpointReaderTests | SPLIT | 23/0/0 · 59.6375543 / 23.5037182 | 23/0/0 · 54.8007254 / 20.4872809 |
| Antiphon.Tests.Checkpoints.OutputCleanupTests | SPLIT | 39/0/0 · 70.8729209 / 5.757517 | — |
| Antiphon.Tests.Checkpoints.PlanCoverageBindingTests | SPLIT | 5/2/0 · 133.3127231 / 84.4332692 | 7/0/0 · 302.3751736 / 246.9636063 |
| Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests | SPLIT | 4/0/0 · 6.1190515 / 3.0195994 | 4/0/0 · 2.7581404 / 1.675735 |
| Antiphon.Tests.Checkpoints.PlanCoveragePcTests | SPLIT | 8/0/0 · 6.1544315 / 6.0730854 | 8/0/0 · 7.1178137 / 7.0441421 |
| Antiphon.Tests.Infrastructure.AmServiceDeployReadinessTests | SPLIT | 7/0/0 · 103.6921234 / 57.9905697 | — |
| Antiphon.Tests.Infrastructure.AppDbContextModelTests | MOVE | 3/0/0 · 14.0891516 / 6.3925878 | — |
| Antiphon.Tests.Infrastructure.AppHostBrokerSourceGuardTests | MOVE | 2/0/0 · 0.0671163 / 0.0514883 | 2/0/0 · 0.3828744 / 0.3618719 |
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | MOVE | 11/0/0 · 0.3653283 / 0.3263324 | — |
| Antiphon.Tests.Infrastructure.C1744G1DesktopWinComposeTests | SPLIT | 2/0/0 · 0.0600368 / 0.0381304 | — |
| Antiphon.Tests.Infrastructure.Card1451PlanFoldTests | MOVE | 4/0/0 · 0.1320274 / 0.0483387 | 4/0/0 · 2.0856674 / 1.208244 |
| Antiphon.Tests.Infrastructure.CardFileRepositoryTryKillTests | SPLIT | 1/0/0 · 0.3010993 / 0.3010993 | — |
| Antiphon.Tests.Infrastructure.CheckpointToolProjectContractTests | MOVE | 7/0/0 · 0.0921262 / 0.0204104 | — |
| Antiphon.Tests.Infrastructure.CodexRootExecContractTests | MOVE | 2/0/0 · 10.7715261 / 10.7521592 | — |
| Antiphon.Tests.Infrastructure.CodexRunnerImageContractTests | SPLIT | 21/0/0 · 5.3681573 / 1.5414724 | — |
| Antiphon.Tests.Infrastructure.CustodyHelperContractTests | SPLIT | 9/0/0 · 0.0123514 / 0.0045433 | — |
| Antiphon.Tests.Infrastructure.DbRateSamplerTests | SPLIT | 6/0/0 · 0.131854 / 0.0550976 | 6/0/0 · 0.1584628 / 0.0719395 |
| Antiphon.Tests.Infrastructure.DindRunnerContractTests | SPLIT | 25/0/0 · 1.8015739 / 1.0880793 | — |
| Antiphon.Tests.Infrastructure.DirectoryBuildRspTests | MOVE | 2/0/0 · 0.004167 / 0.0031303 | 2/0/0 · 0.0042395 / 0.0036429 |
| Antiphon.Tests.Infrastructure.DockerDeliveryObservationTests | SPLIT | 31/0/0 · 0.0797203 / 0.0407386 | — |
| Antiphon.Tests.Infrastructure.DockerStackContractTests | SPLIT | 132/0/0 · 17.4623536 / 4.2952291 | — |
| Antiphon.Tests.Infrastructure.DockerStackDocumentationTests | MOVE | 22/0/0 · 0.4300168 / 0.101381 | — |
| Antiphon.Tests.Infrastructure.DotnetToolManifestContractTests | MOVE | 2/0/0 · 0.0087491 / 0.0074109 | 2/0/0 · 0.0355514 / 0.034027 |
| Antiphon.Tests.Infrastructure.E2ECopyOwnedPtyHostAppHostContractTests | MOVE | 1/0/0 · 0.0617726 / 0.0617726 | — |
| Antiphon.Tests.Infrastructure.EvidencePolicyWorkflowTests | MOVE | 4/0/0 · 0.1548822 / 0.0439588 | — |
| Antiphon.Tests.Infrastructure.ExpectationWatchdogJobTests | SPLIT | 7/0/0 · 2.9428118 / 1.7481464 | — |
| Antiphon.Tests.Infrastructure.GrokRunnerImageContractTests | MOVE | 26/0/0 · 12.4016674 / 1.8638888 | — |
| Antiphon.Tests.Infrastructure.GrokSha256PinTests | SPLIT | — | — |
| Antiphon.Tests.Infrastructure.HangfireLeaseDocumentationTests | SPLIT | 2/0/0 · 0.0109127 / 0.007231 | 2/0/0 · 0.067675 / 0.065108 |
| Antiphon.Tests.Infrastructure.HangfireStorageSelectionTests | SPLIT | 13/0/0 · 7.9837732 / 2.3426172 | — |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointAuthorityTests | SPLIT | 17/0/0 · 36.9220933 / 4.9383231 | — |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointExtractionTests | SPLIT | 3/0/0 · 0.7470814 / 0.3921783 | 3/0/0 · 0.9570753 / 0.6705067 |
| Antiphon.Tests.Infrastructure.JqRunnerImageContractTests | MOVE | 2/0/0 · 0.0734836 / 0.0625087 | — |
| Antiphon.Tests.Infrastructure.LandGraphAuditProjectInstanceTests | MOVE | 1/0/0 · 0.0203594 / 0.0203594 | 1/0/0 · 0.0128323 / 0.0128323 |
| Antiphon.Tests.Infrastructure.LogRetentionTests | SPLIT | 25/0/0 · 1.0834658 / 0.2475426 | — |
| Antiphon.Tests.Infrastructure.OrchestratorInstructionsMigrationShapeTests | SPLIT | 1/0/0 · 4.2009682 / 4.2009682 | — |
| Antiphon.Tests.Infrastructure.PackageVersionPinContractTests | MOVE | 2/0/0 · 0.3487956 / 0.3422931 | 2/0/0 · 0.8790691 / 0.872531 |
| Antiphon.Tests.Infrastructure.PlanCheckpointClassSelectionGuardTests | MOVE | — | 2/0/0 · 10.1183843 / 9.9677951 |
| Antiphon.Tests.Infrastructure.PostgresJitPolicyTestsSetup | SPLIT | 9/0/0 · 0.0375536 / 0.0132126 | — |
| Antiphon.Tests.Infrastructure.PostgresPoolConfigurationTests | SPLIT | 1/0/0 · 0.0647475 / 0.0647475 | — |
| Antiphon.Tests.Infrastructure.RcBridgeProbePlatformTests | SPLIT | 6/0/0 · 0.0390851 / 0.0106539 | — |
| Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests | SPLIT | 32/0/0 · 26.6486636 / 6.1975828 | — |
| Antiphon.Tests.Infrastructure.RunnerPushCredentialDocsTests | MOVE | 1/0/0 · 0.0550364 / 0.0550364 | — |
| Antiphon.Tests.Infrastructure.Server2ProductionServerContractTests | SPLIT | 12/0/0 · 0.1685587 / 0.0214872 | — |
| Antiphon.Tests.Infrastructure.Server2SidecarStandingAgentsTests | SPLIT | 5/0/0 · 0.0137272 / 0.0058916 | 5/0/0 · 0.0343091 / 0.0225154 |
| Antiphon.Tests.Infrastructure.Server2StagingServerContractTests | SPLIT | 10/0/0 · 0.0126346 / 0.0035304 | — |
| Antiphon.Tests.Infrastructure.ServerGitProcessTests | SPLIT | 23/0/0 · 1.6456008 / 1.4564802 | — |
| Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests | SPLIT | 34/0/0 · 6.545449 / 2.1421647 | — |
| Antiphon.Tests.Infrastructure.SessionRunnerGrokLineEndingContractTests | MOVE | 1/0/0 · 0.0663227 / 0.0663227 | — |
| Antiphon.Tests.Infrastructure.ShellFixtureLineEndingContractTests | MOVE | 2/0/0 · 1.1369181 / 0.5820877 | — |
| Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | SPLIT | 5/0/0 · 23.2691065 / 5.2131264 | — |
| Antiphon.Tests.Infrastructure.SourceRevisionStampContractTests | SPLIT | 16/0/0 · 24.8568672 / 24.7467749 | — |
| Antiphon.Tests.Infrastructure.TestFixtureCleanupGuardTests | MOVE | 1/0/0 · 8.7725952 / 8.7725952 | — |
| Antiphon.Tests.Infrastructure.ZombieCensusPlatformTests | SPLIT | 7/0/0 · 0.117807 / 0.0313389 | — |
| Antiphon.Tests.LandDeliveryWaitRoutingTests | MOVE | 4/0/0 · 0.088165 / 0.0453619 | — |
| Antiphon.Tests.Migrations.PipelineDefinitionMigrationTests | SPLIT | 13/0/0 · 143.8664811 / 39.5759196 | — |
| Antiphon.Tests.ProcessSpawnLimitTests | SPLIT | 57/0/0 · 15.9681632 / 5.6097765 | — |
| Antiphon.Tests.Review.AbfConformanceTests | SPLIT | 42/0/0 · 1.4667153 / 0.0605266 | — |
| Antiphon.Tests.Review.ReviewEngineMigrationTests | SPLIT | 1/0/0 · 5.0045653 / 5.0045653 | — |
| Antiphon.Tests.Scripts.AgentContextCheckTests | SPLIT | 5/0/0 · 0.1042724 / 0.0229679 | — |
| Antiphon.Tests.Scripts.AntiphonLogsTests | SPLIT | 10/0/0 · 0.1803846 / 0.0323348 | — |
| Antiphon.Tests.Scripts.AutostartInstallTests | SPLIT | 9/0/0 · 0.0628542 / 0.0178701 | — |
| Antiphon.Tests.Scripts.AwayDigestSendTests | SPLIT | 5/0/0 · 0.2498769 / 0.0679696 | — |
| Antiphon.Tests.Scripts.BootstrapCheckTests | SPLIT | 28/0/0 · 0.7801236 / 0.1034812 | — |
| Antiphon.Tests.Scripts.BuildSlotScriptTests | SPLIT | 22/0/0 · 3.8948766 / 1.3061744 | — |
| Antiphon.Tests.Scripts.C1008PlatformContractTests | SPLIT | 6/0/0 · 33.2247506 / 23.7288655 | — |
| Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests | MOVE | 5/0/0 · 3.9955307 / 2.8797803 | 5/0/0 · 3.4973219 / 2.834777 |
| Antiphon.Tests.Scripts.C1744G3PhoneHomeSupervisionTests | SPLIT | 41/0/0 · 0.2861688 / 0.0548622 | — |
| Antiphon.Tests.Scripts.C1960WorkspaceSlotsScriptTests | SPLIT | 14/0/0 · 59.2617346 / 5.4573308 | — |
| Antiphon.Tests.Scripts.C487HarnessRootDefaultTests | SPLIT | 1/0/0 · 0.7814653 / 0.7814653 | 1/0/0 · 1.1377093 / 1.1377093 |
| Antiphon.Tests.Scripts.CapabilityScriptTests | SPLIT | 8/0/0 · 0.1610715 / 0.0649643 | — |
| Antiphon.Tests.Scripts.Card0490BootDiskTests | SPLIT | 15/0/0 · 2.5572987 / 2.1027198 | — |
| Antiphon.Tests.Scripts.CardCommandTests | SPLIT | 11/0/0 · 0.148624 / 0.0246242 | — |
| Antiphon.Tests.Scripts.ChangeAwareReleaseTests | SPLIT | 4/0/0 · 30.5993888 / 10.4510044 | — |
| Antiphon.Tests.Scripts.CheckpointTaskScriptTests | SPLIT | 9/0/0 · 33.1473135 / 10.8829002 | — |
| Antiphon.Tests.Scripts.ClassCostMeasurementTests | SPLIT | 6/0/0 · 0.41394 / 0.09279 | — |
| Antiphon.Tests.Scripts.ClaudeTokenRefreshOptInTests | SPLIT | 6/0/0 · 5.5113977 / 1.1948963 | — |
| Antiphon.Tests.Scripts.ClientModeTests | SPLIT | 9/0/0 · 0.0357206 / 0.0075767 | — |
| Antiphon.Tests.Scripts.CodexTestResidueTests | SPLIT | 6/0/0 · 1.075861 / 0.7953323 | — |
| Antiphon.Tests.Scripts.CommitOnSettleScriptTests | SPLIT | 13/0/0 · 15.5004133 / 5.2317755 | — |
| Antiphon.Tests.Scripts.ComplexityChainScriptTests | SPLIT | 21/0/0 · 0.1097477 / 0.0199057 | — |
| Antiphon.Tests.Scripts.DaemonBuildCheckTests | SPLIT | 5/0/0 · 0.0359982 / 0.0188765 | — |
| Antiphon.Tests.Scripts.DeployAmServiceTests | SPLIT | 66/0/0 · 2.1145335 / 0.3590352 | — |
| Antiphon.Tests.Scripts.DeployLocalTests | SPLIT | 28/0/0 · 0.5395762 / 0.0563528 | — |
| Antiphon.Tests.Scripts.DerivedImpactIndexTests | SPLIT | 27/0/0 · 53.9358994 / 14.5292077 | — |
| Antiphon.Tests.Scripts.DispatchConcurrencyScriptTests | SPLIT | 6/0/0 · 0.0525672 / 0.0197233 | — |
| Antiphon.Tests.Scripts.DistillerCommandTests | SPLIT | 5/0/0 · 0.0238332 / 0.0092321 | — |
| Antiphon.Tests.Scripts.DockerStackVerifyTests | SPLIT | 14/0/0 · 0.3648555 / 0.0616408 | — |
| Antiphon.Tests.Scripts.ExpectedPlatformSkipRosterTests | SPLIT | 2/0/0 · 5.0403956 / 4.9329285 | 2/0/0 · 4.7515579 / 4.6693382 |
| Antiphon.Tests.Scripts.GithubSyncScriptTests | SPLIT | 8/0/0 · 0.0162163 / 0.0051297 | — |
| Antiphon.Tests.Scripts.HangfireDashboardCommandTests | SPLIT | 7/0/0 · 0.0184968 / 0.0041496 | — |
| Antiphon.Tests.Scripts.HerdrPaneScriptTests | SPLIT | 10/0/0 · 7.8357526 / 1.5102592 | — |
| Antiphon.Tests.Scripts.HostJqFixtureClosureTests | SPLIT | 2/0/0 · 0.0404979 / 0.0224652 | 2/0/0 · 0.0373623 / 0.0223724 |
| Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.ImpactedClassesCensusTests | SPLIT | 9/0/0 · 3.0393311 / 2.7606103 | — |
| Antiphon.Tests.Scripts.ImpactedClassesSelectorTests | SPLIT | 53/0/0 · 48.8618557 / 6.6640944 | — |
| Antiphon.Tests.Scripts.ImpactIndexRegenerationTests | SPLIT | 12/0/0 · 0.0134388 / 0.0028234 | — |
| Antiphon.Tests.Scripts.IncrementalSelectionSoundnessTests | SPLIT | 1/1/0 · 2.9838537 / 2.4632912 | 4/0/0 · 33.8129979 / 30.9981739 |
| Antiphon.Tests.Scripts.KeepSixCommandTests | SPLIT | 13/0/0 · 0.0306261 / 0.005647 | — |
| Antiphon.Tests.Scripts.LinuxOnlyExpectedSkipClosureTests | MOVE | 1/0/0 · 0.7919441 / 0.7919441 | 1/0/0 · 0.8240775 / 0.8240775 |
| Antiphon.Tests.Scripts.LinuxServerRestartGateTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.LiveLaneEvidenceTests | SPLIT | 3/0/0 · 25.6244645 / 12.565395 | 3/0/0 · 37.4974299 / 27.0479345 |
| Antiphon.Tests.Scripts.LiveLaneExecutableTests | SPLIT | 4/0/0 · 0.8096922 / 0.7163053 | 4/0/0 · 0.9277776 / 0.8029085 |
| Antiphon.Tests.Scripts.LiveLaneFingerprintTests | SPLIT | 7/0/0 · 120.5808233 / 35.3109498 | — |
| Antiphon.Tests.Scripts.LiveLaneInventoryTests | SPLIT | 5/0/0 · 49.6649532 / 24.4376301 | 3/2/0 · 44.9372894 / 22.9299046 |
| Antiphon.Tests.Scripts.LiveLaneIsolationTests | SPLIT | 10/0/0 · 3.6006494 / 1.1419066 | — |
| Antiphon.Tests.Scripts.LiveLaneRunnerTests | SPLIT | 4/0/0 · 32.183009 / 13.0850537 | 4/0/0 · 36.2420403 / 13.914586 |
| Antiphon.Tests.Scripts.MergeHighRadiusSubsetTests | SPLIT | 6/0/0 · 5.3514253 / 1.6960382 | — |
| Antiphon.Tests.Scripts.ModelAvailabilityScriptTests | SPLIT | 9/0/0 · 0.0245661 / 0.0057979 | — |
| Antiphon.Tests.Scripts.NativeLinuxPolicyWitnessMutationTests | MOVE | 12/0/0 · 49.5891987 / 7.6955279 | — |
| Antiphon.Tests.Scripts.Nightly.AntiphonCliLauncherTests | SPLIT | 47/0/0 · 82.0068473 / 4.923303 | — |
| Antiphon.Tests.Scripts.Nightly.BuildSlotLauncherTests | SPLIT | 5/0/0 · 16.217712 / 6.8702864 | — |
| Antiphon.Tests.Scripts.Nightly.CheckpointLauncherTests | SPLIT | 6/0/0 · 14.4588282 / 7.7352546 | — |
| Antiphon.Tests.Scripts.Nightly.CheckpointToolCommandTests | SPLIT | 4/0/0 · 0.188007 / 0.088004 | — |
| Antiphon.Tests.Scripts.Nightly.LiveLaneCensusTests | SPLIT | 6/0/0 · 2.2034902 / 2.1303822 | — |
| Antiphon.Tests.Scripts.Nightly.LiveLaneDiscoveryTests | SPLIT | 6/0/0 · 3.3445131 / 1.8482463 | — |
| Antiphon.Tests.Scripts.Nightly.LiveLaneGateScanTests | SPLIT | 3/0/0 · 12.7823463 / 12.6321019 | — |
| Antiphon.Tests.Scripts.Nightly.NightlyChunkRunnerTests | SPLIT | 14/0/0 · 1.7492656 / 0.7323658 | — |
| Antiphon.Tests.Scripts.Nightly.NightlyHealthTests | SPLIT | 47/0/0 · 2.8999058 / 2.3329113 | — |
| Antiphon.Tests.Scripts.Nightly.NightlyReportTests | SPLIT | 48/0/0 · 1.0855176 / 0.2954528 | — |
| Antiphon.Tests.Scripts.Nightly.NightlyTestsPlanTests | SPLIT | 9/0/0 · 0.6637615 / 0.2982738 | — |
| Antiphon.Tests.Scripts.Nightly.NightlyTestsRunTests | SPLIT | 40/0/0 · 9.2465964 / 1.988507 | — |
| Antiphon.Tests.Scripts.Nightly.ToolchainFloorTwinTests | SPLIT | 8/0/0 · 0.2263242 / 0.1140075 | — |
| Antiphon.Tests.Scripts.NightlyCoreGoldenTests | SPLIT | 30/0/0 · 3.1106752 / 0.5486813 | — |
| Antiphon.Tests.Scripts.NightlyDiscoveryParserTests | SPLIT | 9/0/0 · 0.4708868 / 0.1519755 | — |
| Antiphon.Tests.Scripts.NightlyNativeOwnershipTests | SPLIT | 9/1/0 · 28.0351072 / 15.2208511 | 11/0/0 · 28.9338971 / 15.2478219 |
| Antiphon.Tests.Scripts.NightlyPolicyTests | SPLIT | 6/0/0 · 0.4471658 / 0.3652506 | — |
| Antiphon.Tests.Scripts.NightlyScriptsTests | SPLIT | 8/0/0 · 6.3111732 / 2.4057659 | — |
| Antiphon.Tests.Scripts.NightlySummaryWitnessTests | SPLIT | 2/0/0 · 0.3810981 / 0.3591286 | 2/0/0 · 0.4363581 / 0.4213648 |
| Antiphon.Tests.Scripts.NightlyVerificationContractTests | SPLIT | 40/0/0 · 5.922337 / 1.8010625 | 40/0/0 · 6.3179607 / 1.8305581 |
| Antiphon.Tests.Scripts.OrchestrationWatcherTests | SPLIT | 25/0/0 · 0.0824378 / 0.014122 | — |
| Antiphon.Tests.Scripts.OsWitnessCheckTests | SPLIT | 6/0/0 · 61.6934415 / 19.8841894 | — |
| Antiphon.Tests.Scripts.PipelineDefinitionScriptTests | SPLIT | 8/0/0 · 0.0515311 / 0.0319006 | — |
| Antiphon.Tests.Scripts.PostgresSnapshotJobTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.PostgresSnapshotTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.PostLandEarlyWarningTests | SPLIT | 23/0/0 · 34.2296745 / 8.7141799 | — |
| Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests | SPLIT | 2/0/0 · 11.6810043 / 7.5554537 | — |
| Antiphon.Tests.Scripts.ProjectCommandTests | SPLIT | 7/0/0 · 0.0443137 / 0.0164341 | — |
| Antiphon.Tests.Scripts.PtyHostReaperTests | SPLIT | 15/0/0 · 0.1337275 / 0.0217127 | — |
| Antiphon.Tests.Scripts.PwshPrivateCacheTests | SPLIT | 17/0/0 · 10.0861036 / 1.5909318 | — |
| Antiphon.Tests.Scripts.QualificationCheckoutSourceTests | SPLIT | 3/0/0 · 0.7348053 / 0.6725344 | — |
| Antiphon.Tests.Scripts.QualificationGraphTests | SPLIT | 8/0/0 · 1.3464473 / 0.7549563 | — |
| Antiphon.Tests.Scripts.QualificationStopTests | SPLIT | 13/0/0 · 3.4690172 / 0.597915 | — |
| Antiphon.Tests.Scripts.QualificationSupervisorTests | SPLIT | 17/0/0 · 3.638232 / 1.8072307 | — |
| Antiphon.Tests.Scripts.QualificationTimingTests | SPLIT | 23/0/6 · 8.7080768 / 2.18376 | — |
| Antiphon.Tests.Scripts.RcBoundaryChunkIdTests | SPLIT | 11/0/0 · 0.0871936 / 0.039224 | — |
| Antiphon.Tests.Scripts.RcCoverageAdmissionTests | SPLIT | 1/0/0 · 0.14878 / 0.14878 | 1/0/0 · 0.279113 / 0.279113 |
| Antiphon.Tests.Scripts.RcDirtyLaunchOrderTests | SPLIT | 6/0/0 · 11.4201404 / 3.5657421 | — |
| Antiphon.Tests.Scripts.RcForcedFullTests | SPLIT | 3/0/0 · 0.4273443 / 0.2241458 | 4/0/0 · 0.1803805 / 0.0804206 |
| Antiphon.Tests.Scripts.RcFullTimingContractTests | SPLIT | 10/0/0 · 10.4580826 / 8.6184197 | — |
| Antiphon.Tests.Scripts.RcFullWatchdogHarnessTests | SPLIT | 1/0/0 · 0.167542 / 0.167542 | 1/0/0 · 0.2293881 / 0.2293881 |
| Antiphon.Tests.Scripts.RcFullWatchdogTests | SPLIT | 4/0/0 · 1.0120734 / 0.5866309 | 4/0/0 · 0.5778647 / 0.3983132 |
| Antiphon.Tests.Scripts.RcIncrementalShadowTests | SPLIT | 12/0/0 · 0.4892711 / 0.3688046 | — |
| Antiphon.Tests.Scripts.RcInputOwnersMapTests | SPLIT | 34/0/0 · 122.9310322 / 14.0631364 | — |
| Antiphon.Tests.Scripts.RcQualificationStatusTests | SPLIT | 4/0/0 · 0.6757916 / 0.5330743 | — |
| Antiphon.Tests.Scripts.RcRosterRepairTests | SPLIT | 3/0/0 · 0.3770794 / 0.1533436 | — |
| Antiphon.Tests.Scripts.RcRunEvidenceTests | SPLIT | 10/0/0 · 10.7604203 / 3.8962226 | — |
| Antiphon.Tests.Scripts.RcStoppedRunSeedTests | SPLIT | 31/0/0 · 20.0822981 / 3.9145874 | — |
| Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests | SPLIT | 3/0/0 · 180.1406572 / 180.1382172 | 3/0/0 · 200.620145 / 200.6107073 |
| Antiphon.Tests.Scripts.RegisteredCallerScriptTests | SPLIT | 5/0/0 · 1.3582694 / 1.2927202 | 5/0/0 · 1.3578454 / 1.3092319 |
| Antiphon.Tests.Scripts.RegisterPostgresSnapshotTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.ReleaseAuthorityTests | SPLIT | 6/0/0 · 0.0234207 / 0.0085531 | — |
| Antiphon.Tests.Scripts.ReleaseCutTests | SPLIT | 8/0/0 · 2.9854237 / 1.3834322 | — |
| Antiphon.Tests.Scripts.ReleaseGateAuthorityContractTests | SPLIT | 3/0/0 · 13.6312113 / 7.2718989 | — |
| Antiphon.Tests.Scripts.ReleaseGateE2EEvidenceTests | SPLIT | 5/0/0 · 0.4265485 / 0.3566046 | — |
| Antiphon.Tests.Scripts.ReleaseGateFollowupTests | SPLIT | 2/0/0 · 11.3169742 / 10.8015008 | — |
| Antiphon.Tests.Scripts.ReleaseGatePlatformTests | SPLIT | 8/0/0 · 14.41843 / 4.344903 | — |
| Antiphon.Tests.Scripts.ReleaseGatePolicyTests | SPLIT | 4/0/0 · 0.6613183 / 0.3474348 | — |
| Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | SPLIT | 15/0/0 · 142.9087322 / 14.1402699 | — |
| Antiphon.Tests.Scripts.ReleaseGateRegistrationTests | SPLIT | 6/0/0 · 0.3925487 / 0.1518326 | — |
| Antiphon.Tests.Scripts.ReleaseGateRunTests | SPLIT | 10/0/0 · 9.4270461 / 4.0526129 | — |
| Antiphon.Tests.Scripts.ReleaseGateStatusTests | SPLIT | 1/0/0 · 1.5256233 / 1.5256233 | — |
| Antiphon.Tests.Scripts.ReleaseImpactEnvelopeTests | SPLIT | 12/0/0 · 23.2300451 / 4.1884728 | — |
| Antiphon.Tests.Scripts.ReleaseImpactRuntimeTests | SPLIT | 4/0/0 · 1.9203444 / 1.7378822 | 4/0/0 · 1.5338984 / 1.3208319 |
| Antiphon.Tests.Scripts.ReleaseImpactScriptTests | SPLIT | 11/0/0 · 0.9735151 / 0.4480511 | — |
| Antiphon.Tests.Scripts.ReleasePromoteTests | SPLIT | 3/0/0 · 9.0694127 / 7.0708528 | 3/0/0 · 10.3856273 / 7.7666939 |
| Antiphon.Tests.Scripts.ReleasePublishTests | SPLIT | 10/0/0 · 34.9417963 / 16.747183 | — |
| Antiphon.Tests.Scripts.ReleaseSelectionHopSeedTests | SPLIT | 3/0/0 · 2.4606047 / 2.2699251 | 3/0/0 · 2.7794229 / 2.5956614 |
| Antiphon.Tests.Scripts.RemoteScriptContractTests | SPLIT | 278/0/1 · 1232.1682986 / 54.6647272 | — |
| Antiphon.Tests.Scripts.RepositoryChildRecoveryCommandTests | SPLIT | 20/0/0 · 0.613762 / 0.4187411 | — |
| Antiphon.Tests.Scripts.RetiredTempContainerHostTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.RetiredTempContainerScriptTests | SPLIT | 13/0/0 · 122.2335369 / 45.1581403 | — |
| Antiphon.Tests.Scripts.RollingProductionMountTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.RollingVolumeRecycleCensusChurnTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests | SPLIT | 29/0/2 · 1183.1566766 / 166.1139802 | — |
| Antiphon.Tests.Scripts.RoutingPinScriptTests | SPLIT | 17/0/0 · 0.1933716 / 0.0594003 | — |
| Antiphon.Tests.Scripts.RunnerDrainScriptTests | SPLIT | 6/0/0 · 0.1511164 / 0.1059392 | — |
| Antiphon.Tests.Scripts.RunnerLinuxCompilerPolicyTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.RunnerRestartHealthTests | SPLIT | 61/0/0 · 0.6590056 / 0.0645185 | — |
| Antiphon.Tests.Scripts.RunnerSlotsCommandTests | SPLIT | 5/0/0 · 0.0688063 / 0.0321588 | 5/0/0 · 0.0563794 / 0.0218484 |
| Antiphon.Tests.Scripts.ScheduleCommandTests | SPLIT | 12/0/0 · 0.040036 / 0.0253491 | — |
| Antiphon.Tests.Scripts.ScriptHarnessProcessContractTests | SPLIT | 31/0/0 · 0.1855749 / 0.0349024 | — |
| Antiphon.Tests.Scripts.ScriptModelSpawnLaneTests | MOVE | 34/0/0 · 1.7083406 / 0.7607215 | — |
| Antiphon.Tests.Scripts.SeedSmokeTargetingPackContractTests | SPLIT | 4/0/0 · 0.0120397 / 0.0056522 | 4/0/0 · 0.0133204 / 0.0082937 |
| Antiphon.Tests.Scripts.SelectorLineEndingAttributeTests | MOVE | 1/0/0 · 0.0652509 / 0.0652509 | 1/0/0 · 0.0834895 / 0.0834895 |
| Antiphon.Tests.Scripts.Server2PostgresComposeTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.Server2PostgresDeployTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.ServerDockerInputsTests | SPLIT | 36/0/0 · 0.8994846 / 0.0485484 | — |
| Antiphon.Tests.Scripts.ServerRepositoryMirrorsTests | SPLIT | 16/0/0 · 0.3263908 / 0.1104884 | — |
| Antiphon.Tests.Scripts.StageValueReportTests | SPLIT | 8/0/0 · 0.016246 / 0.0064434 | — |
| Antiphon.Tests.Scripts.TestDataPruneCommandTests | SPLIT | 5/0/0 · 0.0686765 / 0.0283125 | 5/0/0 · 0.0506444 / 0.0180602 |
| Antiphon.Tests.Scripts.TestSelectionCatalogTests | SPLIT | 3/0/0 · 0.370537 / 0.3468596 | — |
| Antiphon.Tests.Scripts.TestSelectionDependencyTests | SPLIT | 3/0/0 · 23.1117322 / 8.9742479 | — |
| Antiphon.Tests.Scripts.TestSelectionTests | SPLIT | 11/0/0 · 43.131992 / 13.7894866 | — |
| Antiphon.Tests.Scripts.TestTraceSummaryTests | SPLIT | 7/0/0 · 0.0556074 / 0.0202686 | — |
| Antiphon.Tests.Scripts.ToolchainFloorWarningTests | SPLIT | 11/0/0 · 1.2623938 / 0.9738151 | — |
| Antiphon.Tests.Scripts.VerifyDindRunnerScriptTests | SPLIT | 16/0/0 · 0.1438514 / 0.0367835 | — |
| Antiphon.Tests.Scripts.VerifyPhoneHomeGrokScriptTests | SPLIT | 9/0/0 · 0.7300983 / 0.2069375 | — |
| Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests | SPLIT | 6/0/7 · 9.6956019 / 4.5069031 | — |
| Antiphon.Tests.Scripts.VolumeRetirementHostProofTests | SPLIT | 27/0/0 · 166.952543 / 27.6671082 | — |
| Antiphon.Tests.Scripts.VolumeRetirementPublicationTests | SPLIT | 3/0/0 · 74.219293 / 33.0040505 | — |
| Antiphon.Tests.Scripts.VolumeRetirementReceiptTests | SPLIT | — | — |
| Antiphon.Tests.Scripts.VolumeRetirementWrapperTests | SPLIT | 6/0/0 · 105.2710817 / 62.5754311 | — |
| Antiphon.Tests.Scripts.WatchedTestRunTests | SPLIT | 5/0/0 · 0.1012762 / 0.0450476 | 5/0/0 · 0.1260087 / 0.0644807 |
| Antiphon.Tests.Scripts.WatchQualTests | SPLIT | 16/0/0 · 0.0990246 / 0.0208699 | — |
| Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests | SPLIT | 7/0/0 · 72.0776181 / 24.4459119 | 8/0/0 · 82.1595472 / 28.539057 |
| Antiphon.Tests.Scripts.ZombieAgentReaperTests | SPLIT | 12/0/0 · 0.2566183 / 0.0630924 | — |
| Antiphon.Tests.TestHelpers.BuildOutputNestingTests | MOVE | 15/0/0 · 1.0980429 / 0.5676043 | — |
| Antiphon.Tests.TestHelpers.C0824TransportLimiterTests | MOVE | 10/0/0 · 0.042726 / 0.0047525 | — |
| Antiphon.Tests.TestHelpers.DelegationHarnessCensusTests | MOVE | 7/0/0 · 7.364903 / 3.4446522 | — |
| Antiphon.Tests.TestHelpers.DescendantLeakScopeTests | SPLIT | 5/0/0 · 4.6004554 / 1.9237212 | — |
| Antiphon.Tests.TestHelpers.E2EArtifactPathGuardTests | MOVE | 6/0/0 · 2.4996072 / 1.8832782 | — |
| Antiphon.Tests.TestHelpers.EntityScalarSnapshotConsumerGuardTests | SPLIT | 5/0/0 · 3.4236436 / 3.3847315 | — |
| Antiphon.Tests.TestHelpers.ExpectedDurationSyncTests | MOVE | 6/0/0 · 3.80064 / 3.0734606 | — |
| Antiphon.Tests.TestHelpers.LinkedTestHelperClosureTests | MOVE | 3/0/0 · 2.8009145 / 2.6444905 | — |
| Antiphon.Tests.TestHelpers.MergeArtifactGuardTests | MOVE | 2/0/0 · 0.0948462 / 0.0936202 | — |
| Antiphon.Tests.TestHelpers.NativeWindowsFixtureGateTests | MOVE | 5/0/0 · 0.9088702 / 0.3554608 | — |
| Antiphon.Tests.TestHelpers.OwnedTempLeakTests | SPLIT | 14/0/0 · 2.5306167 / 0.6370728 | — |
| Antiphon.Tests.TestHelpers.RcManifestIntegrationTests | SPLIT | 6/0/0 · 140.1927947 / 96.1356196 | — |
| Antiphon.Tests.TestHelpers.SlowTestTripwireTests | SPLIT | 2/0/0 · 0.0112772 / 0.008044 | — |
| Antiphon.Tests.TestHelpers.TestAppHostPathTests | SPLIT | 5/0/0 · 0.0050808 / 0.0018413 | — |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | SPLIT | 45/0/0 · 47.196753 / 12.4824439 | — |
| Antiphon.Tests.TestHelpers.TestDbFixtureLazyInitializationTests | SPLIT | 14/0/0 · 114.5688765 / 30.7542796 | — |
| Antiphon.Tests.TestHelpers.TestDbFixtureLifecycleTests | SPLIT | 34/0/0 · 3.4410157 / 2.0672568 | — |
| Antiphon.Tests.TestHelpers.TestDurationTripwireTests | SPLIT | 17/0/0 · 0.8529104 / 0.2204032 | — |
| Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests | MOVE | 1/0/0 · 2.9156103 / 2.9156103 | — |
| Antiphon.Tests.TestHelpers.TestMigrationFingerprintTests | SPLIT | 3/0/0 · 0.3802433 / 0.2967136 | — |
| Antiphon.Tests.TestHelpers.TestPhaseSummaryTests | SPLIT | 8/0/0 · 6.5451777 / 4.0076443 | — |
| Antiphon.Tests.TestHelpers.TestTimingOperationLabelTests | SPLIT | 2/0/0 · 1.5483893 / 1.5153058 | — |
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | MOVE | 3/0/0 · 5.2560133 / 2.4416709 | — |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 1.1774997 / 1.1774997 | — |
