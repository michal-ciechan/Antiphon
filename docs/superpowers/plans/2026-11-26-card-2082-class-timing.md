# CARD-2082 qualification timing by owning assembly and class

The [verification design](2026-11-26-card-2082-repository-invariants-test-design.md#qualification-timing-and-cross-platform-outcomes) uses this matrix to cost the proposed invariant selection. Each Windows cell gives **passed/failed/not executed · total / longest case seconds** from the retained TRX aggregate. An em dash means no observed class row. A zero is an observed count. Every observed Windows row has a recorded duration. SPLIT includes all historical cases of the parent, including behavioral cases that keep their current lane.

B = `b8cbfb24` at `2fd61b643ea4231fa732cd4969b5284d43b33f39`. The run ended with a test failure and incomplete coverage for native groups 129 and 142. F = `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267`. **F is partial**. There was no summary and 1,281 known native groups had completeness flags when the export was taken. The last column considers only observed Windows failures. A Linux replay verdict refers to the same failed method and source pin.

Linux cells use the same counts and seconds. **B** and **F** identify the matching Windows SHA. **H** is a measurement at another SHA. Each tag selects the most recently completed qualifying receipt for that class in its cohort. The historical pool uses successful checkpoints with clean source, verified builds and matching TRX counts. The named diagnostic replays also supply failed cases. Filters may cover part of the class. Read the L key below before comparing cases. The table includes independently documented same-case replays where the native cut omitted them. A missing Linux row records a coverage gap.

## Antiphon.Agents.Pty.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial | Linux, selected receipts | Windows-only failure observed? |
|---|---|---|---|---|---|
| Antiphon.Agents.Pty.Tests.ConPtyEnvironmentIsolationGuardTests | MOVE | 6/0/0 · 0.7364782 / 0.7199111 | — | — | No observed failure |
| Antiphon.Agents.Pty.Tests.LiveClaudeEvidenceWiringTests | SPLIT | 3/0/0 · 16.0327249 / 8.6808275 | — | — | No observed failure |
| Antiphon.Agents.Pty.Tests.LiveClaudeExecutableWiringTests | SPLIT | 3/0/0 · 1.1074087 / 0.8561263 | — | — | No observed failure |
| Antiphon.Agents.Pty.Tests.LiveClaudeInventoryTests | MOVE | 1/0/0 · 1.4036368 / 1.4036368 | — | — | No observed failure |
| Antiphon.Agents.Pty.Tests.ProcessSpawnLimitTests | SPLIT | 2/0/0 · 0.0014386 / 0.0012727 | — | H 2/0/0 · 0.0113842 / 0.007899 [L26](#linux-l26) | No observed failure |
| Antiphon.Agents.Pty.Tests.UnixPtyDeliveryEnvelopeTests | SPLIT | — | — | — | No Windows receipt |
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | MOVE | 3/0/0 · 0.5436091 / 0.2357205 | — | H 1/0/0 · 0.2668955 / 0.2668955 [L45](#linux-l45) | No observed failure |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 0.1007904 / 0.1007904 | — | — | No observed failure |

## Antiphon.E2E

| Fully qualified class | Decision | Windows B | Windows F · partial | Linux, selected receipts | Windows-only failure observed? |
|---|---|---|---|---|---|
| Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests | SPLIT | 3/0/0 · 57.2694873 / 33.6158903 | — | — | No observed failure |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 0.0490789 / 0.0490789 | — | — | No observed failure |

## Antiphon.GitInProcess.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial | Linux, selected receipts | Windows-only failure observed? |
|---|---|---|---|---|---|
| Antiphon.Tests.Infrastructure.GitInProcessAssemblyTests | MOVE | 3/0/0 · 8.5709067 / 6.450049 | — | — | No observed failure |

## Antiphon.MarkdownPdf.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial | Linux, selected receipts | Windows-only failure observed? |
|---|---|---|---|---|---|
| Antiphon.MarkdownPdf.Tests.FakeBrowserSolutionReleaseTests | SPLIT | 1/0/0 · 16.0128726 / 16.0128726 | — | — | No observed failure |

## Antiphon.Messaging.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial | Linux, selected receipts | Windows-only failure observed? |
|---|---|---|---|---|---|
| Antiphon.Messaging.Tests.Contract.ContractSchemaTests | SPLIT | 2/0/0 · 0.0269672 / 0.0149286 | — | — | No observed failure |
| Antiphon.Messaging.Tests.Gateway.LibrarySufficiencyTests | SPLIT | 7/0/0 · 0.1295824 / 0.0991976 | — | — | No observed failure |
| Antiphon.Messaging.Tests.PublishWorkflowSanityTests | MOVE | 2/0/0 · 0.035036 / 0.0181944 | — | — | No observed failure |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 0.0233745 / 0.0233745 | — | — | No observed failure |

## Antiphon.PtyHost.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial | Linux, selected receipts | Windows-only failure observed? |
|---|---|---|---|---|---|
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | MOVE | 3/0/0 · 0.276233 / 0.1395751 | — | H 1/0/0 · 0.1040465 / 0.1040465 [L43](#linux-l43) | No observed failure |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 0.038427 / 0.038427 | — | — | No observed failure |

## Antiphon.Resilience.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial | Linux, selected receipts | Windows-only failure observed? |
|---|---|---|---|---|---|
| Antiphon.Tests.Infrastructure.Resilience.ResilienceOptionsAndTelemetryTests | SPLIT | 10/0/0 · 0.1347563 / 0.0787948 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.ResilienceAssemblyTests | MOVE | 2/0/0 · 28.786069 / 23.4040685 | — | — | No observed failure |

## Antiphon.Server.Git.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial | Linux, selected receipts | Windows-only failure observed? |
|---|---|---|---|---|---|
| Antiphon.Tests.Infrastructure.ServerGitAssemblyTests | MOVE | 3/0/0 · 6.6255928 / 6.6174382 | — | H 3/0/0 · 3.5584775 / 3.5158117 [L28](#linux-l28) | No observed failure |

## Antiphon.SessionRunner.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial | Linux, selected receipts | Windows-only failure observed? |
|---|---|---|---|---|---|
| Antiphon.SessionRunner.Tests.C0824WindowsSlotHolderTests | SPLIT | 4/0/0 · 13.6312475 / 6.9420866 | 4/0/0 · 16.9613974 / 9.5569036 | — | No observed failure |
| Antiphon.SessionRunner.Tests.C0824WorkspaceSlotTimeoutBudgetTests | SPLIT | 20/0/0 · 0.006535 / 0.0047065 | — | H 20/0/0 · 0.0375237 / 0.0075695 [L01](#linux-l01) | No observed failure |
| Antiphon.SessionRunner.Tests.C1677TempFamilyRegistryTests | SPLIT | 3/0/0 · 0.4638604 / 0.4465431 | — | — | No observed failure |
| Antiphon.SessionRunner.Tests.C1722GitTimeoutBudgetTests | SPLIT | 101/0/0 · 0.0180995 / 0.0062787 | — | H 98/0/0 · 0.0414692 / 0.0131916 [L01](#linux-l01) | No observed failure |
| Antiphon.SessionRunner.Tests.C1818GcOwnerRobustnessTests | SPLIT | 10/0/0 · 5.8653091 / 2.8919058 | — | H 10/0/0 · 4.269845 / 2.4377107 [L07](#linux-l07) | No observed failure |
| Antiphon.SessionRunner.Tests.CustodyNamespaceBootstrapTests | SPLIT | — | — | H 8/0/0 · 3.0176121 / 2.3275944 [L01](#linux-l01) | No Windows receipt |
| Antiphon.SessionRunner.Tests.HostStatsProbeParseTests | SPLIT | 11/0/0 · 0.0394929 / 0.0095057 | — | H 11/0/0 · 0.3221023 / 0.0625936 [L01](#linux-l01) | No observed failure |
| Antiphon.SessionRunner.Tests.HostStatsSamplerTests | SPLIT | 7/0/0 · 0.0406475 / 0.0161381 | — | H 5/0/0 · 0.1004935 / 0.0295737 [L01](#linux-l01) | No observed failure |
| Antiphon.SessionRunner.Tests.HostTopConsumerTests | SPLIT | 31/0/0 · 14.7605753 / 3.4421332 | — | — | No observed failure |
| Antiphon.SessionRunner.Tests.ProcessSpawnLimitTests | SPLIT | 2/0/0 · 0.0847244 / 0.0844447 | — | H 2/0/0 · 0.1310925 / 0.1207894 [L27](#linux-l27) | No observed failure |
| Antiphon.SessionRunner.Tests.PtyHostDeploymentGuardTests | SPLIT | 3/0/0 · 0.037252 / 0.0207637 | — | — | No observed failure |
| Antiphon.SessionRunner.Tests.StandingRunnerCwdTests | SPLIT | 25/0/0 · 0.0383279 / 0.0136702 | — | — | No observed failure |
| Antiphon.SessionRunner.Tests.WindowsShellGuardAuditTests | MOVE | 3/0/0 · 0.5507068 / 0.2741637 | — | H 3/0/0 · 0.4942485 / 0.3169339 [L01](#linux-l01) | No observed failure |
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | MOVE | 3/0/0 · 0.5631951 / 0.2036858 | — | H 1/0/0 · 1.0459509 / 1.0459509 [L44](#linux-l44) | No observed failure |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 0.1332916 / 0.1332916 | — | H 1/0/0 · 0.3446085 / 0.3446085 [L03](#linux-l03) | No observed failure |

## Antiphon.Tests

| Fully qualified class | Decision | Windows B | Windows F · partial | Linux, selected receipts | Windows-only failure observed? |
|---|---|---|---|---|---|
| Antiphon.Tests.Agents.AgentRegistrySettingsTests | SPLIT | 23/0/0 · 0.386933 / 0.0450052 | — | — | No observed failure |
| Antiphon.Tests.Agents.GrokLinuxBlockingPromptTests | SPLIT | 11/0/0 · 0.175271 / 0.0523162 | — | — | No observed failure |
| Antiphon.Tests.ApiKeys.ProviderEndpointProjectionTests | SPLIT | 36/0/0 · 0.0771094 / 0.0236083 | — | — | No observed failure |
| Antiphon.Tests.Application.AdditiveMigrationRuleWitnessTests | MOVE | 8/0/0 · 11.0892528 / 8.4716002 | — | — | No observed failure |
| Antiphon.Tests.Application.AgentContextContractTests | MOVE | 2/0/0 · 0.0190406 / 0.0123524 | — | — | No observed failure |
| Antiphon.Tests.Application.AgentReplyStyleTests | SPLIT | 22/0/0 · 2.9034854 / 2.300478 | — | — | No observed failure |
| Antiphon.Tests.Application.AgentTaskGetPollMetricsTests | SPLIT | 11/0/0 · 0.4067011 / 0.0668746 | — | — | No observed failure |
| Antiphon.Tests.Application.AgentTaskInternalDecisionMigrationTests | SPLIT | 2/0/0 · 81.3288977 / 41.1107212 | — | — | No observed failure |
| Antiphon.Tests.Application.AgentTaskLandApprovalPersistenceTests | SPLIT | 10/0/0 · 151.0056595 / 66.2311318 | — | — | No observed failure |
| Antiphon.Tests.Application.AgentTaskLandingPersistenceTests | SPLIT | 2/0/0 · 66.5946586 / 63.545051 | — | — | No observed failure |
| Antiphon.Tests.Application.AreaMapContractTests | MOVE | 6/0/0 · 0.2604048 / 0.2280537 | — | — | No observed failure |
| Antiphon.Tests.Application.BlockedTaskParkProjectionTests | SPLIT | 2/0/0 · 0.1324904 / 0.1061099 | — | — | No observed failure |
| Antiphon.Tests.Application.BlockedTaskParkStateTests | SPLIT | 2/0/0 · 42.6987024 / 28.7512022 | — | — | No observed failure |
| Antiphon.Tests.Application.BootStallDocumentationTests | MOVE | 5/0/0 · 0.0967738 / 0.0473519 | — | — | No observed failure |
| Antiphon.Tests.Application.C0824DesktopLegacyReleaseSettingsTests | SPLIT | 34/0/0 · 1.6609413 / 1.1497702 | — | — | No observed failure |
| Antiphon.Tests.Application.C0824RefOnlyIdentityTests | SPLIT | 7/0/0 · 39.2904957 / 24.4556871 | — | — | No observed failure |
| Antiphon.Tests.Application.C0824RefOnlySnapshotMigrationTests | SPLIT | 4/0/0 · 14.6209811 / 5.3267485 | — | — | No observed failure |
| Antiphon.Tests.Application.C1514HostLoadMigrationTests | SPLIT | 12/0/0 · 3.106943 / 1.0931258 | — | — | No observed failure |
| Antiphon.Tests.Application.C1677RetentionSettingsTests | SPLIT | 18/0/0 · 0.3381035 / 0.1740606 | — | — | No observed failure |
| Antiphon.Tests.Application.C1744G2PlacementTests | SPLIT | 2/0/0 · 3.4648612 / 1.8540729 | — | — | No observed failure |
| Antiphon.Tests.Application.Card1619ExitMigrationIdTests | SPLIT | 3/0/0 · 51.5913386 / 25.773595 | — | — | No observed failure |
| Antiphon.Tests.Application.Card1619ExitPersistenceTests | SPLIT | — | — | — | No Windows receipt |
| Antiphon.Tests.Application.CardCleanupSchedulingSchemaTests | SPLIT | 1/0/0 · 57.8391607 / 57.8391607 | 0/1/0 · 75.2494592 / 75.2494592 | F 0/1/0 · 42.045 / 42.045 [L55](#linux-l55) | F · no: all observed Failed rows reproduced on Linux at this pin |
| Antiphon.Tests.Application.CardFilePrivacyDocumentationTests | MOVE | 1/0/0 · 0.0583031 / 0.0583031 | — | — | No observed failure |
| Antiphon.Tests.Application.CardRelationshipSchemaTests | SPLIT | 3/0/0 · 4.881739 / 3.5219084 | — | — | No observed failure |
| Antiphon.Tests.Application.CardWorktreeCleanupRecoveryTests | SPLIT | 6/0/0 · 103.1016185 / 37.731056 | — | — | No observed failure |
| Antiphon.Tests.Application.ChannelOutboundContractTests | SPLIT | 2/0/0 · 0.2036254 / 0.1770198 | — | — | No observed failure |
| Antiphon.Tests.Application.ChannelOutboundDurabilitySchemaTests | SPLIT | 4/0/0 · 17.6112364 / 17.2125188 | — | — | No observed failure |
| Antiphon.Tests.Application.ChannelOutboundStorageTests | SPLIT | 33/0/0 · 11.6751581 / 4.127882 | — | — | No observed failure |
| Antiphon.Tests.Application.CheckCompactionFactEquivalenceTests | SPLIT | 3/0/0 · 0.3452138 / 0.1546904 | — | — | No observed failure |
| Antiphon.Tests.Application.CheckpointManifestDocumentationTests | MOVE | 10/0/0 · 0.6422121 / 0.1894483 | 10/0/0 · 0.5532357 / 0.3943483 | — | No observed failure |
| Antiphon.Tests.Application.CodexGpt62LadderTests | SPLIT | 4/0/0 · 0.0201197 / 0.0120596 | — | — | No observed failure |
| Antiphon.Tests.Application.CommitOnSettleDocumentationTests | MOVE | 2/0/0 · 0.0230213 / 0.0153405 | — | — | No observed failure |
| Antiphon.Tests.Application.D2AdvisoryDocumentationTests | MOVE | 7/0/0 · 0.4984053 / 0.1098188 | — | — | No observed failure |
| Antiphon.Tests.Application.DelegateScriptCapabilityTests | SPLIT | 7/0/0 · 5.9489318 / 1.4704627 | — | — | No observed failure |
| Antiphon.Tests.Application.DelegationAllowedRootsFileTests | SPLIT | 1/0/0 · 0.0020428 / 0.0020428 | — | — | No observed failure |
| Antiphon.Tests.Application.DelegationCapabilityContractTests | SPLIT | 2/0/0 · 0.0644733 / 0.0517561 | — | — | No observed failure |
| Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests | SPLIT | 382/0/0 · 333.0388644 / 9.5767793 | — | — | No observed failure |
| Antiphon.Tests.Application.DispatchBaseNotificationTests | SPLIT | 57/0/0 · 270.7160821 / 90.3730501 | — | — | No observed failure |
| Antiphon.Tests.Application.DispatchBaseWarningRosterTests | MOVE | 11/0/0 · 2.4060082 / 0.6797194 | — | — | No observed failure |
| Antiphon.Tests.Application.DispatchConcurrencyMigrationTests | SPLIT | 3/0/0 · 128.7221396 / 63.9962185 | — | — | No observed failure |
| Antiphon.Tests.Application.DispatchHoldLedgerTests | SPLIT | 29/0/0 · 0.0770742 / 0.0406976 | — | — | No observed failure |
| Antiphon.Tests.Application.EfLambdaClockGuardTests | MOVE | 10/0/0 · 2.7228243 / 1.9785561 | — | — | No observed failure |
| Antiphon.Tests.Application.Grok47RoutingNameCensusTests | MOVE | 1/0/0 · 0.007872 / 0.007872 | — | — | No observed failure |
| Antiphon.Tests.Application.HerdrPlacementSettingsTests | SPLIT | 9/0/0 · 128.3368956 / 75.7600774 | — | H 9/0/0 · 124.456956 / 82.5340463 [L06](#linux-l06) | No observed failure |
| Antiphon.Tests.Application.HostCleanupOrchestrationTests | SPLIT | 2/0/0 · 30.3080511 / 30.303654 | — | — | No observed failure |
| Antiphon.Tests.Application.InstructionBundleTests | SPLIT | 78/0/0 · 2.3101894 / 0.4868335 | — | — | No observed failure |
| Antiphon.Tests.Application.InstructionFileStampTests | SPLIT | 15/0/0 · 0.2420885 / 0.0505726 | — | — | No observed failure |
| Antiphon.Tests.Application.InstructionFragmentTests | SPLIT | 7/0/0 · 0.0899194 / 0.0262624 | — | — | No observed failure |
| Antiphon.Tests.Application.KeepSixDocumentationTests | MOVE | 3/0/0 · 0.0585556 / 0.0418612 | — | — | No observed failure |
| Antiphon.Tests.Application.LandGraphCommandTests | SPLIT | 22/0/0 · 0.6998729 / 0.1663029 | — | — | No observed failure |
| Antiphon.Tests.Application.LandGraphEvaluationTests | SPLIT | 159/0/1 · 556.6583176 / 82.5686353 | — | — | No observed failure |
| Antiphon.Tests.Application.LandGraphLandRefreshTests | SPLIT | 19/0/0 · 36.9726973 / 13.2677318 | 19/0/0 · 92.2551059 / 30.8585975 | — | No observed failure |
| Antiphon.Tests.Application.LandingProtocolHarnessTests | SPLIT | 18/0/0 · 108.0022055 / 9.111558 | — | — | No observed failure |
| Antiphon.Tests.Application.LandNonCodeSkipTests | SPLIT | 131/0/0 · 478.2017 / 64.5282106 | — | — | No observed failure |
| Antiphon.Tests.Application.LandRequestWriteDiagnosticTests | SPLIT | 15/0/0 · 116.4570302 / 58.7984705 | — | — | No observed failure |
| Antiphon.Tests.Application.LandVerificationFailureMigrationTests | SPLIT | 1/0/0 · 5.689933 / 5.689933 | — | — | No observed failure |
| Antiphon.Tests.Application.LaunchModelArgumentAppenderTests | SPLIT | 2/0/0 · 0.0347648 / 0.0175675 | — | — | No observed failure |
| Antiphon.Tests.Application.MechanicalPcContractTests | MOVE | 11/0/0 · 0.1934829 / 0.0446294 | — | H 11/0/0 · 1.0280866 / 0.1477698 [L49](#linux-l49) | No observed failure |
| Antiphon.Tests.Application.MidTurnInterruptDocumentationTests | MOVE | 1/0/0 · 0.0048161 / 0.0048161 | — | H 1/0/0 · 0.0032497 / 0.0032497 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Application.OrchestratorBundleOwnershipTests | MOVE | 11/0/0 · 0.077008 / 0.0360716 | — | — | No observed failure |
| Antiphon.Tests.Application.OrchestratorInstructionsGuidanceTests | MOVE | 6/0/0 · 0.2886588 / 0.0989133 | — | — | No observed failure |
| Antiphon.Tests.Application.PerTaskFinalUnitLaneWitnessTests | MOVE | 1/0/0 · 0.0261657 / 0.0261657 | — | — | No observed failure |
| Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests | SPLIT | 1/0/0 · 2.0343841 / 2.0343841 | — | H 2/0/0 · 3.8984934 / 2.641036 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Application.PipelineDefinitionDocumentationTests | MOVE | 1/0/0 · 0.0043265 / 0.0043265 | — | — | No observed failure |
| Antiphon.Tests.Application.PipelineHandoffRouteDocumentationTests | MOVE | 4/0/0 · 0.0390688 / 0.0216643 | — | — | No observed failure |
| Antiphon.Tests.Application.PipelineSpeedIntegrationTests | SPLIT | 48/0/0 · 336.024724 / 28.4140338 | — | — | No observed failure |
| Antiphon.Tests.Application.PoolClaudeEffortMigrationTests | SPLIT | 1/0/0 · 46.2711155 / 46.2711155 | — | — | No observed failure |
| Antiphon.Tests.Application.PostLandMutationAdmissionTests | SPLIT | 27/0/0 · 481.9165685 / 51.9222802 | — | — | No observed failure |
| Antiphon.Tests.Application.PostLandMutationContractTests | MOVE | 30/0/0 · 0.0866019 / 0.0563957 | — | — | No observed failure |
| Antiphon.Tests.Application.PostLandRetrospectiveContractTests | MOVE | 4/0/0 · 0.0197727 / 0.0105308 | — | — | No observed failure |
| Antiphon.Tests.Application.ProviderEndpointDocumentationTests | MOVE | 3/0/0 · 0.055962 / 0.0305372 | — | — | No observed failure |
| Antiphon.Tests.Application.PublishedRoutingDocumentationTests | MOVE | 9/0/0 · 0.2999188 / 0.1169474 | — | — | No observed failure |
| Antiphon.Tests.Application.ReceiptScanDestinationTests | SPLIT | 34/0/0 · 0.0228814 / 0.0134685 | — | H 34/0/0 · 0.0551081 / 0.0287422 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Application.ReleaseBranchModelDocumentationTests | MOVE | 3/0/0 · 0.050989 / 0.0200885 | — | — | No observed failure |
| Antiphon.Tests.Application.ReleaseGateActivationTests | SPLIT | 7/0/0 · 2.000713 / 1.1979425 | — | — | No observed failure |
| Antiphon.Tests.Application.RepairSourceDocumentationTests | MOVE | 3/0/0 · 0.0216651 / 0.0101951 | — | — | No observed failure |
| Antiphon.Tests.Application.RequestedModelAliasMigrationTests | SPLIT | 1/0/0 · 5.6248503 / 5.6248503 | — | — | No observed failure |
| Antiphon.Tests.Application.ReviewRoundsPolicyTests | SPLIT | 30/0/0 · 0.0333458 / 0.0096266 | — | — | No observed failure |
| Antiphon.Tests.Application.ReviewTestQualityChecklistWitnessTests | MOVE | 4/0/0 · 0.6764457 / 0.3509118 | — | — | No observed failure |
| Antiphon.Tests.Application.RunnerAlarmWiringTests | SPLIT | 1/0/0 · 0.5316626 / 0.5316626 | — | — | No observed failure |
| Antiphon.Tests.Application.RunnerBranchContractDocumentationTests | MOVE | 10/0/0 · 1.3446987 / 0.7296776 | — | H 10/0/0 · 1.4527541 / 0.9212158 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Application.RunnerDefaultGuidanceTests | MOVE | 4/0/0 · 0.0223326 / 0.0083019 | — | — | No observed failure |
| Antiphon.Tests.Application.RunnerReadSourcePinTests | MOVE | 1/0/0 · 0.0118075 / 0.0118075 | — | — | No observed failure |
| Antiphon.Tests.Application.ScopedVerificationInstructionTests | MOVE | 17/0/0 · 0.3327362 / 0.0752117 | — | — | No observed failure |
| Antiphon.Tests.Application.SequentialOverrideWordingTests | MOVE | 4/0/0 · 0.3115675 / 0.0895603 | — | — | No observed failure |
| Antiphon.Tests.Application.SessionRunnerAbsenceEvidenceDocumentationTests | MOVE | 1/0/0 · 0.1452859 / 0.1452859 | — | H 1/0/0 · 0.0269119 / 0.0269119 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Application.SessionTranscriptSummaryMigrationTests | SPLIT | 3/0/0 · 33.1386003 / 33.124518 | — | — | No observed failure |
| Antiphon.Tests.Application.SettlementSyncDebtSchemaTests | SPLIT | 1/0/0 · 4.223898 / 4.223898 | — | — | No observed failure |
| Antiphon.Tests.Application.SettlementSyncRecoveryTests | SPLIT | 30/0/0 · 230.3379649 / 27.3997538 | — | — | No observed failure |
| Antiphon.Tests.Application.SiblingInspectionDeadlineWitnessTests | SPLIT | 1/0/0 · 0.0674787 / 0.0674787 | — | — | No observed failure |
| Antiphon.Tests.Application.SpecialistRoleContractTests | SPLIT | 2/0/0 · 4.3861377 / 4.385907 | — | — | No observed failure |
| Antiphon.Tests.Application.SpecialistRoutingScriptTests | SPLIT | 3/0/0 · 10.5187429 / 5.4664801 | — | — | No observed failure |
| Antiphon.Tests.Application.StableBuildOutputDocumentationTests | MOVE | 39/0/0 · 0.170343 / 0.0370215 | 39/0/0 · 0.3609181 / 0.0677063 | — | No observed failure |
| Antiphon.Tests.Application.StandingBootDocumentationTests | MOVE | 3/0/0 · 0.0909195 / 0.0471221 | — | H 3/0/0 · 0.0288381 / 0.0178196 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Application.StandingPipelinePolicyDocumentationTests | MOVE | 12/0/0 · 0.7934047 / 0.2259365 | — | — | No observed failure |
| Antiphon.Tests.Application.StandingRestartEscalationTests | SPLIT | 52/0/0 · 83.1329168 / 18.3169273 | — | — | No observed failure |
| Antiphon.Tests.Application.StandingSessionOwnershipTests | SPLIT | 5/0/0 · 12.7323564 / 5.222331 | — | — | No observed failure |
| Antiphon.Tests.Application.StandingSpecialistRoutingMigrationTests | SPLIT | 4/0/0 · 57.6239455 / 46.6826229 | — | — | No observed failure |
| Antiphon.Tests.Application.TaskPlatformGuidanceTests | MOVE | 5/0/0 · 0.093754 / 0.0758856 | — | H 5/0/0 · 0.6424575 / 0.4545592 [L49](#linux-l49) | No observed failure |
| Antiphon.Tests.Application.TaskPlatformPersistenceTests | SPLIT | 3/0/0 · 38.0337731 / 35.4955946 | — | — | No observed failure |
| Antiphon.Tests.Application.UnixDeliveryCeilingTests | SPLIT | 9/0/0 · 0.0554168 / 0.0301635 | — | — | No observed failure |
| Antiphon.Tests.Application.WhyMissedWriteUpContractTests | MOVE | 1/0/0 · 0.0103048 / 0.0103048 | — | — | No observed failure |
| Antiphon.Tests.Application.WorktreeBaseSelectionTests | SPLIT | 16/0/0 · 21.599781 / 6.4295633 | — | — | No observed failure |
| Antiphon.Tests.Application.WorktreeResidueScriptTests | SPLIT | 3/0/0 · 0.1105292 / 0.0427434 | — | — | No observed failure |
| Antiphon.Tests.Checkpoints.BuildSlotClientClockGuardTests | MOVE | — | 12/0/0 · 2.8572274 / 1.656625 | — | No observed failure |
| Antiphon.Tests.Checkpoints.CheckpointAdmissionContractTests | SPLIT | 10/1/0 · 61.8383647 / 10.1070494 | 11/0/0 · 69.3869261 / 13.1827706 | B 1/0/0 · 3.1687626 / 3.1687626 [L50](#linux-l50)<br>F 4/0/0 · 5.7892979 / 2.9616736 [L40](#linux-l40) | B · yes: same failed Windows method passed on Linux at the B pin |
| Antiphon.Tests.Checkpoints.CheckpointCallerBudgetTests | SPLIT | 9/0/0 · 12.0013783 / 1.5581338 | 9/0/0 · 12.9469838 / 1.6626534 | B 9/0/0 · 3.0702669 / 0.4676162 [L21](#linux-l21) | No observed failure |
| Antiphon.Tests.Checkpoints.CheckpointReadStallWitnessTests | SPLIT | 9/0/0 · 0.0285705 / 0.0071828 | 9/0/0 · 0.0222772 / 0.0072883 | — | No observed failure |
| Antiphon.Tests.Checkpoints.CheckpointRepeatDocumentationTests | MOVE | 1/0/0 · 0.0185418 / 0.0185418 | — | — | No observed failure |
| Antiphon.Tests.Checkpoints.CheckpointTempUsageTests | SPLIT | 2/0/0 · 3.663723 / 2.288585 | 2/0/0 · 3.9024122 / 2.4986874 | — | No observed failure |
| Antiphon.Tests.Checkpoints.EmbeddedCheckpointReaderTests | SPLIT | 23/0/0 · 59.6375543 / 23.5037182 | 23/0/0 · 54.8007254 / 20.4872809 | — | No observed failure |
| Antiphon.Tests.Checkpoints.OutputCleanupTests | SPLIT | 39/0/0 · 70.8729209 / 5.757517 | — | H 45/0/4 · 64.4290814 / 16.9783721 [L23](#linux-l23) | No observed failure |
| Antiphon.Tests.Checkpoints.PlanCoverageBindingTests | SPLIT | 5/2/0 · 133.3127231 / 84.4332692 | 7/0/0 · 302.3751736 / 246.9636063 | B 0/2/0 · 189.955 / 113.077 [L54](#linux-l54)<br>F 7/0/0 · 488.9803833 / 342.1187637 [L39](#linux-l39)<br>H 2/0/0 · 279.209398 / 210.7845659 [L37](#linux-l37) | B · no: all observed Failed rows reproduced on Linux at this pin |
| Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests | SPLIT | 4/0/0 · 6.1190515 / 3.0195994 | 4/0/0 · 2.7581404 / 1.675735 | — | No observed failure |
| Antiphon.Tests.Checkpoints.PlanCoveragePcTests | SPLIT | 8/0/0 · 6.1544315 / 6.0730854 | 8/0/0 · 7.1178137 / 7.0441421 | — | No observed failure |
| Antiphon.Tests.Infrastructure.AmServiceDeployReadinessTests | SPLIT | 7/0/0 · 103.6921234 / 57.9905697 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.AppDbContextModelTests | MOVE | 3/0/0 · 14.0891516 / 6.3925878 | — | B 3/0/0 · 21.3477152 / 9.2216108 [L21](#linux-l21) | No observed failure |
| Antiphon.Tests.Infrastructure.AppHostBrokerSourceGuardTests | MOVE | 2/0/0 · 0.0671163 / 0.0514883 | 2/0/0 · 0.3828744 / 0.3618719 | — | No observed failure |
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | MOVE | 11/0/0 · 0.3653283 / 0.3263324 | — | B 11/0/0 · 0.4128942 / 0.255341 [L20](#linux-l20)<br>F 11/0/0 · 0.5387657 / 0.3034883 [L38](#linux-l38)<br>H 11/0/0 · 0.4375493 / 0.2101446 [L49](#linux-l49) | No observed failure |
| Antiphon.Tests.Infrastructure.C1744G1DesktopWinComposeTests | SPLIT | 2/0/0 · 0.0600368 / 0.0381304 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.Card1451PlanFoldTests | MOVE | 4/0/0 · 0.1320274 / 0.0483387 | 4/0/0 · 2.0856674 / 1.208244 | — | No observed failure |
| Antiphon.Tests.Infrastructure.CardFileRepositoryTryKillTests | SPLIT | 1/0/0 · 0.3010993 / 0.3010993 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.CheckpointToolProjectContractTests | MOVE | 7/0/0 · 0.0921262 / 0.0204104 | — | H 4/0/0 · 0.0610279 / 0.0198743 [L02](#linux-l02) | No observed failure |
| Antiphon.Tests.Infrastructure.CodexRootExecContractTests | MOVE | 2/0/0 · 10.7715261 / 10.7521592 | — | H 4/0/0 · 35.529094 / 35.0359569 [L17](#linux-l17) | No observed failure |
| Antiphon.Tests.Infrastructure.CodexRunnerImageContractTests | SPLIT | 21/0/0 · 5.3681573 / 1.5414724 | — | H 21/0/0 · 8.7273308 / 6.4554294 [L17](#linux-l17) | No observed failure |
| Antiphon.Tests.Infrastructure.CustodyHelperContractTests | SPLIT | 9/0/0 · 0.0123514 / 0.0045433 | — | F 11/0/0 · 0.3343017 / 0.0412763 [L42](#linux-l42)<br>H 11/0/0 · 0.2524033 / 0.035089 [L32](#linux-l32) | No observed failure |
| Antiphon.Tests.Infrastructure.DbRateSamplerTests | SPLIT | 6/0/0 · 0.131854 / 0.0550976 | 6/0/0 · 0.1584628 / 0.0719395 | — | No observed failure |
| Antiphon.Tests.Infrastructure.DindRunnerContractTests | SPLIT | 25/0/0 · 1.8015739 / 1.0880793 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.DirectoryBuildRspTests | MOVE | 2/0/0 · 0.004167 / 0.0031303 | 2/0/0 · 0.0042395 / 0.0036429 | — | No observed failure |
| Antiphon.Tests.Infrastructure.DockerDeliveryObservationTests | SPLIT | 31/0/0 · 0.0797203 / 0.0407386 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.DockerStackContractTests | SPLIT | 132/0/0 · 17.4623536 / 4.2952291 | — | H 132/0/0 · 2.9623804 / 0.1556432 [L32](#linux-l32) | No observed failure |
| Antiphon.Tests.Infrastructure.DockerStackDocumentationTests | MOVE | 22/0/0 · 0.4300168 / 0.101381 | — | H 22/0/0 · 2.9647868 / 0.1836537 [L32](#linux-l32) | No observed failure |
| Antiphon.Tests.Infrastructure.DotnetToolManifestContractTests | MOVE | 2/0/0 · 0.0087491 / 0.0074109 | 2/0/0 · 0.0355514 / 0.034027 | — | No observed failure |
| Antiphon.Tests.Infrastructure.E2ECopyOwnedPtyHostAppHostContractTests | MOVE | 1/0/0 · 0.0617726 / 0.0617726 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.EvidencePolicyWorkflowTests | MOVE | 4/0/0 · 0.1548822 / 0.0439588 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.ExpectationWatchdogJobTests | SPLIT | 7/0/0 · 2.9428118 / 1.7481464 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.GrokRunnerImageContractTests | MOVE | 26/0/0 · 12.4016674 / 1.8638888 | — | H 26/0/0 · 0.4348853 / 0.0325178 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Infrastructure.GrokSha256PinTests | SPLIT | — | — | — | No Windows receipt |
| Antiphon.Tests.Infrastructure.HangfireLeaseDocumentationTests | SPLIT | 2/0/0 · 0.0109127 / 0.007231 | 2/0/0 · 0.067675 / 0.065108 | H 2/0/0 · 0.0096959 / 0.0082169 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Infrastructure.HangfireStorageSelectionTests | SPLIT | 13/0/0 · 7.9837732 / 2.3426172 | — | H 13/0/0 · 3.2679367 / 0.9660152 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointAuthorityTests | SPLIT | 17/0/0 · 36.9220933 / 4.9383231 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.HostCleanupCheckpointExtractionTests | SPLIT | 3/0/0 · 0.7470814 / 0.3921783 | 3/0/0 · 0.9570753 / 0.6705067 | — | No observed failure |
| Antiphon.Tests.Infrastructure.JqRunnerImageContractTests | MOVE | 2/0/0 · 0.0734836 / 0.0625087 | — | H 18/0/0 · 2.2642728 / 1.3012486 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Infrastructure.LandGraphAuditProjectInstanceTests | MOVE | 1/0/0 · 0.0203594 / 0.0203594 | 1/0/0 · 0.0128323 / 0.0128323 | — | No observed failure |
| Antiphon.Tests.Infrastructure.LogRetentionTests | SPLIT | 25/0/0 · 1.0834658 / 0.2475426 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.OrchestratorInstructionsMigrationShapeTests | SPLIT | 1/0/0 · 4.2009682 / 4.2009682 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.PackageVersionPinContractTests | MOVE | 2/0/0 · 0.3487956 / 0.3422931 | 2/0/0 · 0.8790691 / 0.872531 | — | No observed failure |
| Antiphon.Tests.Infrastructure.PlanCheckpointClassSelectionGuardTests | MOVE | — | 2/0/0 · 10.1183843 / 9.9677951 | F 2/0/0 · 43.9225756 / 42.7725089 [L38](#linux-l38)<br>H 2/0/0 · 16.6578801 / 15.3154473 [L47](#linux-l47) | No observed failure |
| Antiphon.Tests.Infrastructure.PostgresJitPolicyTestsSetup | SPLIT | 9/0/0 · 0.0375536 / 0.0132126 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.PostgresPoolConfigurationTests | SPLIT | 1/0/0 · 0.0647475 / 0.0647475 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.RcBridgeProbePlatformTests | SPLIT | 6/0/0 · 0.0390851 / 0.0106539 | — | H 6/0/1 · 0.0233629 / 0.0178887 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests | SPLIT | 32/0/0 · 26.6486636 / 6.1975828 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.RunnerPushCredentialDocsTests | MOVE | 1/0/0 · 0.0550364 / 0.0550364 | — | H 1/0/0 · 0.005532 / 0.005532 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Infrastructure.Server2ProductionServerContractTests | SPLIT | 12/0/0 · 0.1685587 / 0.0214872 | — | H 15/0/0 · 21.7722296 / 19.375371 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Infrastructure.Server2SidecarStandingAgentsTests | SPLIT | 5/0/0 · 0.0137272 / 0.0058916 | 5/0/0 · 0.0343091 / 0.0225154 | H 5/0/0 · 0.1179356 / 0.063671 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Infrastructure.Server2StagingServerContractTests | SPLIT | 10/0/0 · 0.0126346 / 0.0035304 | — | H 10/0/0 · 0.0297049 / 0.0111373 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Infrastructure.ServerGitProcessTests | SPLIT | 23/0/0 · 1.6456008 / 1.4564802 | — | H 23/0/0 · 81.9410932 / 80.5890742 [L31](#linux-l31) | No observed failure |
| Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests | SPLIT | 34/0/0 · 6.545449 / 2.1421647 | — | H 34/0/0 · 0.3941853 / 0.1107093 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.Infrastructure.SessionRunnerGrokLineEndingContractTests | MOVE | 1/0/0 · 0.0663227 / 0.0663227 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.ShellFixtureLineEndingContractTests | MOVE | 2/0/0 · 1.1369181 / 0.5820877 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | SPLIT | 5/0/0 · 23.2691065 / 5.2131264 | — | H 5/0/0 · 39.9795585 / 9.1053025 [L47](#linux-l47) | No observed failure |
| Antiphon.Tests.Infrastructure.SourceRevisionStampContractTests | SPLIT | 16/0/0 · 24.8568672 / 24.7467749 | — | — | No observed failure |
| Antiphon.Tests.Infrastructure.TestFixtureCleanupGuardTests | MOVE | 1/0/0 · 8.7725952 / 8.7725952 | — | B 1/0/0 · 16.7921444 / 16.7921444 [L22](#linux-l22)<br>H 1/0/0 · 20.8182093 / 20.8182093 [L49](#linux-l49) | No observed failure |
| Antiphon.Tests.Infrastructure.ZombieCensusPlatformTests | SPLIT | 7/0/0 · 0.117807 / 0.0313389 | — | H 8/0/1 · 0.0111609 / 0.0026025 [L52](#linux-l52) | No observed failure |
| Antiphon.Tests.LandDeliveryWaitRoutingTests | MOVE | 4/0/0 · 0.088165 / 0.0453619 | — | — | No observed failure |
| Antiphon.Tests.Migrations.PipelineDefinitionMigrationTests | SPLIT | 13/0/0 · 143.8664811 / 39.5759196 | — | — | No observed failure |
| Antiphon.Tests.ProcessSpawnLimitTests | SPLIT | 57/0/0 · 15.9681632 / 5.6097765 | — | H 57/0/0 · 13.9489854 / 6.0445988 [L35](#linux-l35) | No observed failure |
| Antiphon.Tests.Review.AbfConformanceTests | SPLIT | 42/0/0 · 1.4667153 / 0.0605266 | — | — | No observed failure |
| Antiphon.Tests.Review.ReviewEngineMigrationTests | SPLIT | 1/0/0 · 5.0045653 / 5.0045653 | — | B 1/0/0 · 9.2810606 / 9.2810606 [L21](#linux-l21) | No observed failure |
| Antiphon.Tests.Scripts.AgentContextCheckTests | SPLIT | 5/0/0 · 0.1042724 / 0.0229679 | — | — | No observed failure |
| Antiphon.Tests.Scripts.AntiphonLogsTests | SPLIT | 10/0/0 · 0.1803846 / 0.0323348 | — | — | No observed failure |
| Antiphon.Tests.Scripts.AutostartInstallTests | SPLIT | 9/0/0 · 0.0628542 / 0.0178701 | — | — | No observed failure |
| Antiphon.Tests.Scripts.AwayDigestSendTests | SPLIT | 5/0/0 · 0.2498769 / 0.0679696 | — | — | No observed failure |
| Antiphon.Tests.Scripts.BootstrapCheckTests | SPLIT | 28/0/0 · 0.7801236 / 0.1034812 | — | B 28/0/0 · 2.654697 / 0.4948735 [L22](#linux-l22) | No observed failure |
| Antiphon.Tests.Scripts.BuildSlotScriptTests | SPLIT | 22/0/0 · 3.8948766 / 1.3061744 | — | — | No observed failure |
| Antiphon.Tests.Scripts.C1008PlatformContractTests | SPLIT | 6/0/0 · 33.2247506 / 23.7288655 | — | H 4/0/0 · 2.4695255 / 1.5364738 [L08](#linux-l08) | No observed failure |
| Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests | MOVE | 5/0/0 · 3.9955307 / 2.8797803 | 5/0/0 · 3.4973219 / 2.834777 | — | No observed failure |
| Antiphon.Tests.Scripts.C1744G3PhoneHomeSupervisionTests | SPLIT | 41/0/0 · 0.2861688 / 0.0548622 | — | — | No observed failure |
| Antiphon.Tests.Scripts.C1960WorkspaceSlotsScriptTests | SPLIT | 14/0/0 · 59.2617346 / 5.4573308 | — | — | No observed failure |
| Antiphon.Tests.Scripts.C487HarnessRootDefaultTests | SPLIT | 1/0/0 · 0.7814653 / 0.7814653 | 1/0/0 · 1.1377093 / 1.1377093 | — | No observed failure |
| Antiphon.Tests.Scripts.CapabilityScriptTests | SPLIT | 8/0/0 · 0.1610715 / 0.0649643 | — | — | No observed failure |
| Antiphon.Tests.Scripts.Card0490BootDiskTests | SPLIT | 15/0/0 · 2.5572987 / 2.1027198 | — | — | No observed failure |
| Antiphon.Tests.Scripts.CardCommandTests | SPLIT | 11/0/0 · 0.148624 / 0.0246242 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ChangeAwareReleaseTests | SPLIT | 4/0/0 · 30.5993888 / 10.4510044 | — | H 4/0/0 · 9.9090426 / 2.7959616 [L29](#linux-l29) | No observed failure |
| Antiphon.Tests.Scripts.CheckpointTaskScriptTests | SPLIT | 9/0/0 · 33.1473135 / 10.8829002 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ClassCostMeasurementTests | SPLIT | 6/0/0 · 0.41394 / 0.09279 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ClaudeTokenRefreshOptInTests | SPLIT | 6/0/0 · 5.5113977 / 1.1948963 | — | H 6/0/0 · 8.5584157 / 2.4957541 [L17](#linux-l17) | No observed failure |
| Antiphon.Tests.Scripts.ClientModeTests | SPLIT | 9/0/0 · 0.0357206 / 0.0075767 | — | — | No observed failure |
| Antiphon.Tests.Scripts.CodexTestResidueTests | SPLIT | 6/0/0 · 1.075861 / 0.7953323 | — | — | No observed failure |
| Antiphon.Tests.Scripts.CommitOnSettleScriptTests | SPLIT | 13/0/0 · 15.5004133 / 5.2317755 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ComplexityChainScriptTests | SPLIT | 21/0/0 · 0.1097477 / 0.0199057 | — | — | No observed failure |
| Antiphon.Tests.Scripts.DaemonBuildCheckTests | SPLIT | 5/0/0 · 0.0359982 / 0.0188765 | — | — | No observed failure |
| Antiphon.Tests.Scripts.DeployAmServiceTests | SPLIT | 66/0/0 · 2.1145335 / 0.3590352 | — | — | No observed failure |
| Antiphon.Tests.Scripts.DeployLocalTests | SPLIT | 28/0/0 · 0.5395762 / 0.0563528 | — | B 28/0/0 · 2.5692079 / 0.7444474 [L22](#linux-l22) | No observed failure |
| Antiphon.Tests.Scripts.DerivedImpactIndexTests | SPLIT | 27/0/0 · 53.9358994 / 14.5292077 | — | B 27/0/0 · 153.0348282 / 42.5142931 [L20](#linux-l20)<br>F 27/0/0 · 137.5854349 / 39.1787363 [L38](#linux-l38)<br>H 2/0/0 · 1.3712699 / 1.0328425 [L53](#linux-l53) | No observed failure |
| Antiphon.Tests.Scripts.DispatchConcurrencyScriptTests | SPLIT | 6/0/0 · 0.0525672 / 0.0197233 | — | — | No observed failure |
| Antiphon.Tests.Scripts.DistillerCommandTests | SPLIT | 5/0/0 · 0.0238332 / 0.0092321 | — | — | No observed failure |
| Antiphon.Tests.Scripts.DockerStackVerifyTests | SPLIT | 14/0/0 · 0.3648555 / 0.0616408 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ExpectedPlatformSkipRosterTests | SPLIT | 2/0/0 · 5.0403956 / 4.9329285 | 2/0/0 · 4.7515579 / 4.6693382 | F 2/0/0 · 3.9899046 / 3.8695266 [L42](#linux-l42)<br>H 2/0/0 · 17.5902906 / 17.4722224 [L05](#linux-l05) | No observed failure |
| Antiphon.Tests.Scripts.GithubSyncScriptTests | SPLIT | 8/0/0 · 0.0162163 / 0.0051297 | — | — | No observed failure |
| Antiphon.Tests.Scripts.HangfireDashboardCommandTests | SPLIT | 7/0/0 · 0.0184968 / 0.0041496 | — | — | No observed failure |
| Antiphon.Tests.Scripts.HerdrPaneScriptTests | SPLIT | 10/0/0 · 7.8357526 / 1.5102592 | — | — | No observed failure |
| Antiphon.Tests.Scripts.HostJqFixtureClosureTests | SPLIT | 2/0/0 · 0.0404979 / 0.0224652 | 2/0/0 · 0.0373623 / 0.0223724 | — | No observed failure |
| Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests | SPLIT | — | — | H 40/0/0 · 580.6363984 / 144.5079955 [L17](#linux-l17) | No Windows receipt |
| Antiphon.Tests.Scripts.ImpactedClassesCensusTests | SPLIT | 9/0/0 · 3.0393311 / 2.7606103 | — | H 9/0/0 · 2.69848 / 2.4848416 [L24](#linux-l24) | No observed failure |
| Antiphon.Tests.Scripts.ImpactedClassesSelectorTests | SPLIT | 53/0/0 · 48.8618557 / 6.6640944 | — | H 53/0/0 · 52.952251 / 9.7446568 [L29](#linux-l29) | No observed failure |
| Antiphon.Tests.Scripts.ImpactIndexRegenerationTests | SPLIT | 12/0/0 · 0.0134388 / 0.0028234 | — | — | No observed failure |
| Antiphon.Tests.Scripts.IncrementalSelectionSoundnessTests | SPLIT | 1/1/0 · 2.9838537 / 2.4632912 | 4/0/0 · 33.8129979 / 30.9981739 | B 1/0/0 · 5.6669723 / 5.6669723 [L50](#linux-l50)<br>F 4/0/0 · 16.9963545 / 8.0442002 [L39](#linux-l39)<br>H 4/0/0 · 6.2112884 / 4.0199533 [L48](#linux-l48) | B · yes: same failed Windows method passed on Linux at the B pin |
| Antiphon.Tests.Scripts.KeepSixCommandTests | SPLIT | 13/0/0 · 0.0306261 / 0.005647 | — | — | No observed failure |
| Antiphon.Tests.Scripts.LinuxOnlyExpectedSkipClosureTests | MOVE | 1/0/0 · 0.7919441 / 0.7919441 | 1/0/0 · 0.8240775 / 0.8240775 | H 1/0/0 · 1.2183191 / 1.2183191 [L05](#linux-l05) | No observed failure |
| Antiphon.Tests.Scripts.LinuxServerRestartGateTests | SPLIT | — | — | — | No Windows receipt |
| Antiphon.Tests.Scripts.LiveLaneEvidenceTests | SPLIT | 3/0/0 · 25.6244645 / 12.565395 | 3/0/0 · 37.4974299 / 27.0479345 | — | No observed failure |
| Antiphon.Tests.Scripts.LiveLaneExecutableTests | SPLIT | 4/0/0 · 0.8096922 / 0.7163053 | 4/0/0 · 0.9277776 / 0.8029085 | — | No observed failure |
| Antiphon.Tests.Scripts.LiveLaneFingerprintTests | SPLIT | 7/0/0 · 120.5808233 / 35.3109498 | — | — | No observed failure |
| Antiphon.Tests.Scripts.LiveLaneInventoryTests | SPLIT | 5/0/0 · 49.6649532 / 24.4376301 | 3/2/0 · 44.9372894 / 22.9299046 | F 0/2/0 · 22.2467039 / 11.1240109 [L51](#linux-l51)<br>H 4/0/0 · 40.4286705 / 35.5638084 [L18](#linux-l18) | F · no: all observed Failed rows reproduced on Linux at this pin |
| Antiphon.Tests.Scripts.LiveLaneIsolationTests | SPLIT | 10/0/0 · 3.6006494 / 1.1419066 | — | — | No observed failure |
| Antiphon.Tests.Scripts.LiveLaneRunnerTests | SPLIT | 4/0/0 · 32.183009 / 13.0850537 | 4/0/0 · 36.2420403 / 13.914586 | H 4/0/0 · 65.9534278 / 28.0800023 [L48](#linux-l48) | No observed failure |
| Antiphon.Tests.Scripts.MergeHighRadiusSubsetTests | SPLIT | 6/0/0 · 5.3514253 / 1.6960382 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ModelAvailabilityScriptTests | SPLIT | 9/0/0 · 0.0245661 / 0.0057979 | — | — | No observed failure |
| Antiphon.Tests.Scripts.NativeLinuxPolicyWitnessMutationTests | MOVE | 12/0/0 · 49.5891987 / 7.6955279 | — | B 12/0/0 · 233.9601994 / 28.4452609 [L20](#linux-l20)<br>F 12/0/0 · 246.0676137 / 34.2630488 [L38](#linux-l38)<br>H 12/0/0 · 125.9658602 / 18.5573516 [L36](#linux-l36) | No observed failure |
| Antiphon.Tests.Scripts.Nightly.AntiphonCliLauncherTests | SPLIT | 47/0/0 · 82.0068473 / 4.923303 | — | H 41/0/1 · 148.0226009 / 13.1230551 [L29](#linux-l29) | No observed failure |
| Antiphon.Tests.Scripts.Nightly.BuildSlotLauncherTests | SPLIT | 5/0/0 · 16.217712 / 6.8702864 | — | — | No observed failure |
| Antiphon.Tests.Scripts.Nightly.CheckpointLauncherTests | SPLIT | 6/0/0 · 14.4588282 / 7.7352546 | — | — | No observed failure |
| Antiphon.Tests.Scripts.Nightly.CheckpointToolCommandTests | SPLIT | 4/0/0 · 0.188007 / 0.088004 | — | H 4/0/0 · 0.7368562 / 0.2380339 [L23](#linux-l23) | No observed failure |
| Antiphon.Tests.Scripts.Nightly.LiveLaneCensusTests | SPLIT | 6/0/0 · 2.2034902 / 2.1303822 | — | H 6/0/0 · 102.459174 / 102.330732 [L29](#linux-l29) | No observed failure |
| Antiphon.Tests.Scripts.Nightly.LiveLaneDiscoveryTests | SPLIT | 6/0/0 · 3.3445131 / 1.8482463 | — | — | No observed failure |
| Antiphon.Tests.Scripts.Nightly.LiveLaneGateScanTests | SPLIT | 3/0/0 · 12.7823463 / 12.6321019 | — | — | No observed failure |
| Antiphon.Tests.Scripts.Nightly.NightlyChunkRunnerTests | SPLIT | 14/0/0 · 1.7492656 / 0.7323658 | — | H 15/0/0 · 4.2895723 / 2.83068 [L48](#linux-l48) | No observed failure |
| Antiphon.Tests.Scripts.Nightly.NightlyHealthTests | SPLIT | 47/0/0 · 2.8999058 / 2.3329113 | — | — | No observed failure |
| Antiphon.Tests.Scripts.Nightly.NightlyReportTests | SPLIT | 48/0/0 · 1.0855176 / 0.2954528 | — | — | No observed failure |
| Antiphon.Tests.Scripts.Nightly.NightlyTestsPlanTests | SPLIT | 9/0/0 · 0.6637615 / 0.2982738 | — | — | No observed failure |
| Antiphon.Tests.Scripts.Nightly.NightlyTestsRunTests | SPLIT | 40/0/0 · 9.2465964 / 1.988507 | — | — | No observed failure |
| Antiphon.Tests.Scripts.Nightly.ToolchainFloorTwinTests | SPLIT | 8/0/0 · 0.2263242 / 0.1140075 | — | — | No observed failure |
| Antiphon.Tests.Scripts.NightlyCoreGoldenTests | SPLIT | 30/0/0 · 3.1106752 / 0.5486813 | — | H 30/0/0 · 28.38583 / 2.6367665 [L48](#linux-l48) | No observed failure |
| Antiphon.Tests.Scripts.NightlyDiscoveryParserTests | SPLIT | 9/0/0 · 0.4708868 / 0.1519755 | — | — | No observed failure |
| Antiphon.Tests.Scripts.NightlyNativeOwnershipTests | SPLIT | 9/1/0 · 28.0351072 / 15.2208511 | 11/0/0 · 28.9338971 / 15.2478219 | F 1/0/10 · 0.2629009 / 0.2255441 [L41](#linux-l41) | B · Windows-native failure: this case is excluded on Linux |
| Antiphon.Tests.Scripts.NightlyPolicyTests | SPLIT | 6/0/0 · 0.4471658 / 0.3652506 | — | — | No observed failure |
| Antiphon.Tests.Scripts.NightlyScriptsTests | SPLIT | 8/0/0 · 6.3111732 / 2.4057659 | — | H 8/0/0 · 1.1169012 / 0.6931211 [L29](#linux-l29) | No observed failure |
| Antiphon.Tests.Scripts.NightlySummaryWitnessTests | SPLIT | 2/0/0 · 0.3810981 / 0.3591286 | 2/0/0 · 0.4363581 / 0.4213648 | — | No observed failure |
| Antiphon.Tests.Scripts.NightlyVerificationContractTests | SPLIT | 40/0/0 · 5.922337 / 1.8010625 | 40/0/0 · 6.3179607 / 1.8305581 | — | No observed failure |
| Antiphon.Tests.Scripts.OrchestrationWatcherTests | SPLIT | 25/0/0 · 0.0824378 / 0.014122 | — | — | No observed failure |
| Antiphon.Tests.Scripts.OsWitnessCheckTests | SPLIT | 6/0/0 · 61.6934415 / 19.8841894 | — | — | No observed failure |
| Antiphon.Tests.Scripts.PipelineDefinitionScriptTests | SPLIT | 8/0/0 · 0.0515311 / 0.0319006 | — | — | No observed failure |
| Antiphon.Tests.Scripts.PostgresSnapshotJobTests | SPLIT | — | — | — | No Windows receipt |
| Antiphon.Tests.Scripts.PostgresSnapshotTests | SPLIT | — | — | — | No Windows receipt |
| Antiphon.Tests.Scripts.PostLandEarlyWarningTests | SPLIT | 23/0/0 · 34.2296745 / 8.7141799 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests | SPLIT | 2/0/0 · 11.6810043 / 7.5554537 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ProjectCommandTests | SPLIT | 7/0/0 · 0.0443137 / 0.0164341 | — | — | No observed failure |
| Antiphon.Tests.Scripts.PtyHostReaperTests | SPLIT | 15/0/0 · 0.1337275 / 0.0217127 | — | B 15/0/0 · 6.4419609 / 0.6252594 [L22](#linux-l22) | No observed failure |
| Antiphon.Tests.Scripts.PwshPrivateCacheTests | SPLIT | 17/0/0 · 10.0861036 / 1.5909318 | — | — | No observed failure |
| Antiphon.Tests.Scripts.QualificationCheckoutSourceTests | SPLIT | 3/0/0 · 0.7348053 / 0.6725344 | — | — | No observed failure |
| Antiphon.Tests.Scripts.QualificationGraphTests | SPLIT | 8/0/0 · 1.3464473 / 0.7549563 | — | — | No observed failure |
| Antiphon.Tests.Scripts.QualificationStopTests | SPLIT | 13/0/0 · 3.4690172 / 0.597915 | — | — | No observed failure |
| Antiphon.Tests.Scripts.QualificationSupervisorTests | SPLIT | 17/0/0 · 3.638232 / 1.8072307 | — | — | No observed failure |
| Antiphon.Tests.Scripts.QualificationTimingTests | SPLIT | 23/0/6 · 8.7080768 / 2.18376 | — | H 29/0/0 · 5.9524485 / 1.6214137 [L29](#linux-l29) | No observed failure |
| Antiphon.Tests.Scripts.RcBoundaryChunkIdTests | SPLIT | 11/0/0 · 0.0871936 / 0.039224 | — | — | No observed failure |
| Antiphon.Tests.Scripts.RcCoverageAdmissionTests | SPLIT | 1/0/0 · 0.14878 / 0.14878 | 1/0/0 · 0.279113 / 0.279113 | — | No observed failure |
| Antiphon.Tests.Scripts.RcDirtyLaunchOrderTests | SPLIT | 6/0/0 · 11.4201404 / 3.5657421 | — | — | No observed failure |
| Antiphon.Tests.Scripts.RcForcedFullTests | SPLIT | 3/0/0 · 0.4273443 / 0.2241458 | 4/0/0 · 0.1803805 / 0.0804206 | H 4/0/0 · 3.5984777 / 1.2787565 [L48](#linux-l48) | No observed failure |
| Antiphon.Tests.Scripts.RcFullTimingContractTests | SPLIT | 10/0/0 · 10.4580826 / 8.6184197 | — | — | No observed failure |
| Antiphon.Tests.Scripts.RcFullWatchdogHarnessTests | SPLIT | 1/0/0 · 0.167542 / 0.167542 | 1/0/0 · 0.2293881 / 0.2293881 | — | No observed failure |
| Antiphon.Tests.Scripts.RcFullWatchdogTests | SPLIT | 4/0/0 · 1.0120734 / 0.5866309 | 4/0/0 · 0.5778647 / 0.3983132 | — | No observed failure |
| Antiphon.Tests.Scripts.RcIncrementalShadowTests | SPLIT | 12/0/0 · 0.4892711 / 0.3688046 | — | — | No observed failure |
| Antiphon.Tests.Scripts.RcInputOwnersMapTests | SPLIT | 34/0/0 · 122.9310322 / 14.0631364 | — | H 34/0/0 · 17.5282067 / 2.3416451 [L25](#linux-l25) | No observed failure |
| Antiphon.Tests.Scripts.RcQualificationStatusTests | SPLIT | 4/0/0 · 0.6757916 / 0.5330743 | — | — | No observed failure |
| Antiphon.Tests.Scripts.RcRosterRepairTests | SPLIT | 3/0/0 · 0.3770794 / 0.1533436 | — | — | No observed failure |
| Antiphon.Tests.Scripts.RcRunEvidenceTests | SPLIT | 10/0/0 · 10.7604203 / 3.8962226 | — | H 10/0/0 · 3.146648 / 1.3795149 [L30](#linux-l30) | No observed failure |
| Antiphon.Tests.Scripts.RcStoppedRunSeedTests | SPLIT | 31/0/0 · 20.0822981 / 3.9145874 | — | H 31/0/0 · 6.0568881 / 2.2949861 [L30](#linux-l30) | No observed failure |
| Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests | SPLIT | 3/0/0 · 180.1406572 / 180.1382172 | 3/0/0 · 200.620145 / 200.6107073 | — | No observed failure |
| Antiphon.Tests.Scripts.RegisteredCallerScriptTests | SPLIT | 5/0/0 · 1.3582694 / 1.2927202 | 5/0/0 · 1.3578454 / 1.3092319 | — | No observed failure |
| Antiphon.Tests.Scripts.RegisterPostgresSnapshotTests | SPLIT | — | — | — | No Windows receipt |
| Antiphon.Tests.Scripts.ReleaseAuthorityTests | SPLIT | 6/0/0 · 0.0234207 / 0.0085531 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ReleaseCutTests | SPLIT | 8/0/0 · 2.9854237 / 1.3834322 | — | H 9/0/0 · 8.156688 / 2.4670744 [L48](#linux-l48) | No observed failure |
| Antiphon.Tests.Scripts.ReleaseGateAuthorityContractTests | SPLIT | 3/0/0 · 13.6312113 / 7.2718989 | — | H 3/0/0 · 2.4415669 / 1.2680671 [L30](#linux-l30) | No observed failure |
| Antiphon.Tests.Scripts.ReleaseGateE2EEvidenceTests | SPLIT | 5/0/0 · 0.4265485 / 0.3566046 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ReleaseGateFollowupTests | SPLIT | 2/0/0 · 11.3169742 / 10.8015008 | — | H 2/0/0 · 2.2388639 / 2.1901605 [L30](#linux-l30) | No observed failure |
| Antiphon.Tests.Scripts.ReleaseGatePlatformTests | SPLIT | 8/0/0 · 14.41843 / 4.344903 | — | H 2/0/0 · 2.8290446 / 2.1415762 [L33](#linux-l33) | No observed failure |
| Antiphon.Tests.Scripts.ReleaseGatePolicyTests | SPLIT | 4/0/0 · 0.6613183 / 0.3474348 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | SPLIT | 15/0/0 · 142.9087322 / 14.1402699 | — | H 15/0/0 · 35.2378608 / 3.8882001 [L30](#linux-l30) | No observed failure |
| Antiphon.Tests.Scripts.ReleaseGateRegistrationTests | SPLIT | 6/0/0 · 0.3925487 / 0.1518326 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ReleaseGateRunTests | SPLIT | 10/0/0 · 9.4270461 / 4.0526129 | — | H 9/0/1 · 3.714556 / 2.6766232 [L30](#linux-l30) | No observed failure |
| Antiphon.Tests.Scripts.ReleaseGateStatusTests | SPLIT | 1/0/0 · 1.5256233 / 1.5256233 | — | H 1/0/0 · 0.1434954 / 0.1434954 [L30](#linux-l30) | No observed failure |
| Antiphon.Tests.Scripts.ReleaseImpactEnvelopeTests | SPLIT | 12/0/0 · 23.2300451 / 4.1884728 | — | H 1/0/0 · 1.6435396 / 1.6435396 [L46](#linux-l46) | No observed failure |
| Antiphon.Tests.Scripts.ReleaseImpactRuntimeTests | SPLIT | 4/0/0 · 1.9203444 / 1.7378822 | 4/0/0 · 1.5338984 / 1.3208319 | — | No observed failure |
| Antiphon.Tests.Scripts.ReleaseImpactScriptTests | SPLIT | 11/0/0 · 0.9735151 / 0.4480511 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ReleasePromoteTests | SPLIT | 3/0/0 · 9.0694127 / 7.0708528 | 3/0/0 · 10.3856273 / 7.7666939 | H 3/0/0 · 9.7567436 / 8.4400104 [L31](#linux-l31) | No observed failure |
| Antiphon.Tests.Scripts.ReleasePublishTests | SPLIT | 10/0/0 · 34.9417963 / 16.747183 | — | H 10/0/0 · 10.4956541 / 4.8771551 [L31](#linux-l31) | No observed failure |
| Antiphon.Tests.Scripts.ReleaseSelectionHopSeedTests | SPLIT | 3/0/0 · 2.4606047 / 2.2699251 | 3/0/0 · 2.7794229 / 2.5956614 | H 3/0/0 · 0.357572 / 0.2749837 [L31](#linux-l31) | No observed failure |
| Antiphon.Tests.Scripts.RemoteScriptContractTests | SPLIT | 278/0/1 · 1232.1682986 / 54.6647272 | — | H 5/0/0 · 590.2579745 / 283.6740195 [L12](#linux-l12) | No observed failure |
| Antiphon.Tests.Scripts.RepositoryChildRecoveryCommandTests | SPLIT | 20/0/0 · 0.613762 / 0.4187411 | — | — | No observed failure |
| Antiphon.Tests.Scripts.RetiredTempContainerHostTests | SPLIT | — | — | H 22/0/0 · 769.5737684 / 148.2956469 [L13](#linux-l13) | No Windows receipt |
| Antiphon.Tests.Scripts.RetiredTempContainerScriptTests | SPLIT | 13/0/0 · 122.2335369 / 45.1581403 | — | H 16/0/0 · 212.9365616 / 78.5887818 [L14](#linux-l14) | No observed failure |
| Antiphon.Tests.Scripts.RollingProductionMountTests | SPLIT | — | — | H 12/0/0 · 51.6433961 / 11.2418356 [L17](#linux-l17) | No Windows receipt |
| Antiphon.Tests.Scripts.RollingVolumeRecycleCensusChurnTests | SPLIT | — | — | H 1/0/0 · 0.3554935 / 0.3554935 [L16](#linux-l16) | No Windows receipt |
| Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests | SPLIT | 29/0/2 · 1183.1566766 / 166.1139802 | — | H 80/0/0 · 2890.6055132 / 275.8797618 [L16](#linux-l16) | No observed failure |
| Antiphon.Tests.Scripts.RoutingPinScriptTests | SPLIT | 17/0/0 · 0.1933716 / 0.0594003 | — | — | No observed failure |
| Antiphon.Tests.Scripts.RunnerDrainScriptTests | SPLIT | 6/0/0 · 0.1511164 / 0.1059392 | — | — | No observed failure |
| Antiphon.Tests.Scripts.RunnerLinuxCompilerPolicyTests | SPLIT | — | — | — | No Windows receipt |
| Antiphon.Tests.Scripts.RunnerRestartHealthTests | SPLIT | 61/0/0 · 0.6590056 / 0.0645185 | — | — | No observed failure |
| Antiphon.Tests.Scripts.RunnerSlotsCommandTests | SPLIT | 5/0/0 · 0.0688063 / 0.0321588 | 5/0/0 · 0.0563794 / 0.0218484 | — | No observed failure |
| Antiphon.Tests.Scripts.ScheduleCommandTests | SPLIT | 12/0/0 · 0.040036 / 0.0253491 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ScriptHarnessProcessContractTests | SPLIT | 31/0/0 · 0.1855749 / 0.0349024 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ScriptModelSpawnLaneTests | MOVE | 34/0/0 · 1.7083406 / 0.7607215 | — | — | No observed failure |
| Antiphon.Tests.Scripts.SeedSmokeTargetingPackContractTests | SPLIT | 4/0/0 · 0.0120397 / 0.0056522 | 4/0/0 · 0.0133204 / 0.0082937 | H 4/0/0 · 0.0189238 / 0.0125095 [L17](#linux-l17) | No observed failure |
| Antiphon.Tests.Scripts.SelectorLineEndingAttributeTests | MOVE | 1/0/0 · 0.0652509 / 0.0652509 | 1/0/0 · 0.0834895 / 0.0834895 | H 1/0/0 · 0.0170503 / 0.0170503 [L48](#linux-l48) | No observed failure |
| Antiphon.Tests.Scripts.Server2PostgresComposeTests | SPLIT | — | — | — | No Windows receipt |
| Antiphon.Tests.Scripts.Server2PostgresDeployTests | SPLIT | — | — | — | No Windows receipt |
| Antiphon.Tests.Scripts.ServerDockerInputsTests | SPLIT | 36/0/0 · 0.8994846 / 0.0485484 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ServerRepositoryMirrorsTests | SPLIT | 16/0/0 · 0.3263908 / 0.1104884 | — | — | No observed failure |
| Antiphon.Tests.Scripts.StageValueReportTests | SPLIT | 8/0/0 · 0.016246 / 0.0064434 | — | — | No observed failure |
| Antiphon.Tests.Scripts.TestDataPruneCommandTests | SPLIT | 5/0/0 · 0.0686765 / 0.0283125 | 5/0/0 · 0.0506444 / 0.0180602 | — | No observed failure |
| Antiphon.Tests.Scripts.TestSelectionCatalogTests | SPLIT | 3/0/0 · 0.370537 / 0.3468596 | — | H 3/0/0 · 0.1759578 / 0.1081938 [L19](#linux-l19) | No observed failure |
| Antiphon.Tests.Scripts.TestSelectionDependencyTests | SPLIT | 3/0/0 · 23.1117322 / 8.9742479 | — | H 3/0/0 · 2.6289651 / 1.0370617 [L19](#linux-l19) | No observed failure |
| Antiphon.Tests.Scripts.TestSelectionTests | SPLIT | 11/0/0 · 43.131992 / 13.7894866 | — | H 11/0/0 · 9.4082703 / 3.4360798 [L31](#linux-l31) | No observed failure |
| Antiphon.Tests.Scripts.TestTraceSummaryTests | SPLIT | 7/0/0 · 0.0556074 / 0.0202686 | — | — | No observed failure |
| Antiphon.Tests.Scripts.ToolchainFloorWarningTests | SPLIT | 11/0/0 · 1.2623938 / 0.9738151 | — | — | No observed failure |
| Antiphon.Tests.Scripts.VerifyDindRunnerScriptTests | SPLIT | 16/0/0 · 0.1438514 / 0.0367835 | — | — | No observed failure |
| Antiphon.Tests.Scripts.VerifyPhoneHomeGrokScriptTests | SPLIT | 9/0/0 · 0.7300983 / 0.2069375 | — | — | No observed failure |
| Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests | SPLIT | 6/0/7 · 9.6956019 / 4.5069031 | — | H 13/0/0 · 301.4532987 / 85.7619649 [L34](#linux-l34) | No observed failure |
| Antiphon.Tests.Scripts.VolumeRetirementHostProofTests | SPLIT | 27/0/0 · 166.952543 / 27.6671082 | — | H 29/0/0 · 142.1489321 / 21.8223623 [L09](#linux-l09) | No observed failure |
| Antiphon.Tests.Scripts.VolumeRetirementPublicationTests | SPLIT | 3/0/0 · 74.219293 / 33.0040505 | — | H 13/0/0 · 596.0761922 / 77.569224 [L10](#linux-l10) | No observed failure |
| Antiphon.Tests.Scripts.VolumeRetirementReceiptTests | SPLIT | — | — | H 10/0/0 · 735.3006649 / 208.4677751 [L11](#linux-l11) | No Windows receipt |
| Antiphon.Tests.Scripts.VolumeRetirementWrapperTests | SPLIT | 6/0/0 · 105.2710817 / 62.5754311 | — | H 7/0/0 · 396.9730967 / 248.7548337 [L15](#linux-l15) | No observed failure |
| Antiphon.Tests.Scripts.WatchedTestRunTests | SPLIT | 5/0/0 · 0.1012762 / 0.0450476 | 5/0/0 · 0.1260087 / 0.0644807 | — | No observed failure |
| Antiphon.Tests.Scripts.WatchQualTests | SPLIT | 16/0/0 · 0.0990246 / 0.0208699 | — | — | No observed failure |
| Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests | SPLIT | 7/0/0 · 72.0776181 / 24.4459119 | 8/0/0 · 82.1595472 / 28.539057 | B 6/0/1 · 86.3036885 / 39.2965983 [L20](#linux-l20)<br>F 7/0/1 · 127.5643562 / 56.4898549 [L38](#linux-l38)<br>H 7/0/1 · 78.4379378 / 35.312133 [L36](#linux-l36) | No observed failure |
| Antiphon.Tests.Scripts.ZombieAgentReaperTests | SPLIT | 12/0/0 · 0.2566183 / 0.0630924 | — | B 12/0/0 · 3.2901566 / 0.4614337 [L22](#linux-l22) | No observed failure |
| Antiphon.Tests.TestHelpers.BuildOutputNestingTests | MOVE | 15/0/0 · 1.0980429 / 0.5676043 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.C0824TransportLimiterTests | MOVE | 10/0/0 · 0.042726 / 0.0047525 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.DelegationHarnessCensusTests | MOVE | 7/0/0 · 7.364903 / 3.4446522 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.DescendantLeakScopeTests | SPLIT | 5/0/0 · 4.6004554 / 1.9237212 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.E2EArtifactPathGuardTests | MOVE | 6/0/0 · 2.4996072 / 1.8832782 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.EntityScalarSnapshotConsumerGuardTests | SPLIT | 5/0/0 · 3.4236436 / 3.3847315 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.ExpectedDurationSyncTests | MOVE | 6/0/0 · 3.80064 / 3.0734606 | — | H 6/0/0 · 4.9288959 / 3.6560846 [L04](#linux-l04) | No observed failure |
| Antiphon.Tests.TestHelpers.LinkedTestHelperClosureTests | MOVE | 3/0/0 · 2.8009145 / 2.6444905 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.MergeArtifactGuardTests | MOVE | 2/0/0 · 0.0948462 / 0.0936202 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.NativeWindowsFixtureGateTests | MOVE | 5/0/0 · 0.9088702 / 0.3554608 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.OwnedTempLeakTests | SPLIT | 14/0/0 · 2.5306167 / 0.6370728 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.RcManifestIntegrationTests | SPLIT | 6/0/0 · 140.1927947 / 96.1356196 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.SlowTestTripwireTests | SPLIT | 2/0/0 · 0.0112772 / 0.008044 | — | H 2/0/0 · 0.0756334 / 0.0429703 [L47](#linux-l47) | No observed failure |
| Antiphon.Tests.TestHelpers.TestAppHostPathTests | SPLIT | 5/0/0 · 0.0050808 / 0.0018413 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | SPLIT | 45/0/0 · 47.196753 / 12.4824439 | — | B 45/0/0 · 83.3216055 / 14.3721163 [L20](#linux-l20)<br>F 45/0/0 · 87.7835059 / 20.8550714 [L38](#linux-l38)<br>H 45/0/0 · 74.8417732 / 14.3460675 [L47](#linux-l47) | No observed failure |
| Antiphon.Tests.TestHelpers.TestDbFixtureLazyInitializationTests | SPLIT | 14/0/0 · 114.5688765 / 30.7542796 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.TestDbFixtureLifecycleTests | SPLIT | 34/0/0 · 3.4410157 / 2.0672568 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.TestDurationTripwireTests | SPLIT | 17/0/0 · 0.8529104 / 0.2204032 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests | MOVE | 1/0/0 · 2.9156103 / 2.9156103 | — | H 1/0/0 · 1.7547737 / 1.7547737 [L47](#linux-l47) | No observed failure |
| Antiphon.Tests.TestHelpers.TestMigrationFingerprintTests | SPLIT | 3/0/0 · 0.3802433 / 0.2967136 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.TestPhaseSummaryTests | SPLIT | 8/0/0 · 6.5451777 / 4.0076443 | — | — | No observed failure |
| Antiphon.Tests.TestHelpers.TestTimingOperationLabelTests | SPLIT | 2/0/0 · 1.5483893 / 1.5153058 | — | H 2/0/0 · 4.6528662 / 2.3291375 [L31](#linux-l31) | No observed failure |
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | MOVE | 3/0/0 · 5.2560133 / 2.4416709 | — | H 3/0/0 · 21.0918781 / 10.2258756 [L47](#linux-l47) | No observed failure |
| Antiphon.TestSupport.TestClassificationGuardTests | MOVE | 1/0/0 · 1.1774997 / 1.1774997 | — | B 1/0/0 · 6.6377921 / 6.6377921 [L20](#linux-l20)<br>F 1/0/0 · 13.8694214 / 13.8694214 [L38](#linux-l38)<br>H 1/0/0 · 3.9875876 / 3.9875876 [L47](#linux-l47) | No observed failure |

## Native groups with incomplete coverage

The table preserves all flagged group IDs from the export. Append each listed suffix to the literal prefix. Ranges are inclusive. A blank suffix denotes the prefix itself.

| Run | Group prefix | Suffixes | Flags |
|---|---|---|---|
| b8cbfb24 | `antiphon-all-group-` | 129, 142 | incomplete-coverage |
| 1fe763a9 | `agents-pty-all-group-` | 1–4 | missing-trx |
| 1fe763a9 | `agents-pty-all-profile-` | 10–18, 23–24, 45–54, 75–78 | missing-trx |
| 1fe763a9 | `antiphon-all-group-` | 1–7, 9–47, 49–53, 55–85, 87–90, 92–111, 115–124, 126–134, 136–140, 143–147 | missing-trx |
| 1fe763a9 | `antiphon-all-group-` | 8 | planned-count-mismatch |
| 1fe763a9 | `antiphon-all-profile-` | 51–52, 132–141, 175–229, 232–244, 396–433, 454–458, 463–477, 486–533, 536–558, 952–1027, 1113, 1506–1514, 1526–1545, 1561–1570, 1585, 1590–1610, 1625–1663, 1668–1676, 1678–1681, 1716–1717, 1722–1723, 1743–1744, 1768–1769, 1776–1805, 1819–1829, 1835–1851, 1859–1864, 1874–1885, 1906–1920, 1923–1929, 1973–1994, 1997–1999, 2071–2080, 2134–2137, 2144–2160, 2227–2412, 2414–2426, 2429–2458, 2474–2484, 2507–2537, 2587–2593 | missing-trx |
| 1fe763a9 | `antiphon-all-split-` | 1–19 | missing-trx |
| 1fe763a9 | `e2e-all-group-` | 1–13 | missing-trx |
| 1fe763a9 | `e2e-all-split-` | 24 | missing-trx, no-completion-time, reported-missing-trx |
| 1fe763a9 | `e2e-all-split-` | 25–64 | missing-trx |
| 1fe763a9 | `git-in-process-all` |  | missing-trx |
| 1fe763a9 | `markdown-pdf-all-group-` | 1 | missing-trx |
| 1fe763a9 | `markdown-pdf-all-profile-` | 2–12 | missing-trx |
| 1fe763a9 | `messaging-all` |  | missing-trx |
| 1fe763a9 | `observability-all` |  | missing-trx |
| 1fe763a9 | `pty-host-all-group-` | 1–2 | missing-trx |
| 1fe763a9 | `pty-host-all-profile-` | 1–42, 58–69 | missing-trx |
| 1fe763a9 | `resilience-all` |  | missing-trx |
| 1fe763a9 | `server-git-all` |  | missing-trx |
| 1fe763a9 | `session-runner-all-group-` | 1–3, 5–7 | missing-trx |
| 1fe763a9 | `session-runner-all-profile-` | 18–19, 25–59, 72–77, 95–141, 185, 189–219, 262–264 | missing-trx |

## Linux receipts

Each L key identifies the actual Linux source SHA and selected checkpoint row or investigation. Direct TRX receipts were also checked for native result counts and definition-to-result joins. An artifact hash below is the SHA-256 of the corresponding TRX.

| Key | Run/row or investigation | Source SHA | Filter or method selection | TRX SHA-256 |
|---|---|---|---|---|
| <a id="linux-l01"></a>L01 | `20261106-075233-9edf/CP-3` | `4eb141231c8c1166704e958a4535dd23476cd380` | `/*/*/*/*[Category=Unit]` | `ca7b3d8268b4cc1698da00ef0be06324e6a632d69b4826659041e5a1303ad95d` |
| <a id="linux-l02"></a>L02 | `20261107-022238-5d4b/CP-GUARDS` | `87bf11823b977703c7f7fd7ce3fe9a29977fd089` | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(CheckpointToolProjectContractTests*)/*` | `f34be6db9c855f0e9764edee9f6b5ba2324a02abff120e4a2f6bd876ca1d0c4c` |
| <a id="linux-l03"></a>L03 | `20261107-093220-718b/CP-4` | `aea7cd0a3de883a0a9b4e60cae70dfa8df2323f5` | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuard*)/*` | `6ceb9a1ac1ee3ac7f1e7a6fdb95d553303f621137d2e6cd6b4c3c47dfc1e2f25` |
| <a id="linux-l04"></a>L04 | `20261121-172416-b2db/CP-2` | `528fb6a8f07b5d82527df90090391607baab33ad` | `/*/*/ExpectedDurationSyncTests/(RosterMatchesCostFile*)\|(TreeHasNoExpectedDurationAttribute*)\|(AttributeAgreesWithRoster*)\|(AttributeOnlyClassFails*)\|(DeriveStaysAPlatformLabel*)\|(DocPinsTheRosterSentence*)` | `56c0c02a9ad795e38b7bdb046e658b2d0d64f7e364f1b857f4821c7014177be0` |
| <a id="linux-l05"></a>L05 | `20261123-020638-5d08/CP-6` | `d231b35ccde3ac8a942df7b4dd161874904f8abd` | `/*/*/(CompletedCardWorktreeRemovalTests*)\|(LandingRemovalPolicyControlTests*)\|(WorktreeGuardedCleanupTests*)\|(WorktreeNoFollowDeleteTests*)\|(LinuxOnlyExpectedSkipClosureTests*)\|(ExpectedPlatformSkipRosterTests*)/*` | `e1a891b8dd01a1e012bd339c4e2ca1184caa44387aa44d9cf4998b2fd7165d61` |
| <a id="linux-l06"></a>L06 | `20261123-053028-07ea/CP-4` | `eefad7d1802defd9043980ca9e985438f50c3291` | `/*/*/(AgentAttachHerdrTests*)\|(AgentControlServiceIntegrationTests*)\|(AgentCreateRaceTests*)\|(AgentCreateSupervisionTests*)\|(AgentRemoteControlGateTests*)\|(AgentServiceIntegrationTests*)\|(AgentSessionBackendTests*)\|(AgentSessionLaunchFailureTests*)\|(AgentSessionServiceIntegrationTests*)\|(AgentStartRecoveryTests*)\|(AgentSupervisionTests*)\|(AgentSystemPromptLaunchTests*)\|(AgentTaskPoolTests*)\|(BoardServiceIntegrationTests*)\|(ContextCompactionAgentTests*)\|(DiagnosticsBundleServiceTests*)\|(HerdrAlwaysOnChannelParityTests*)\|(HerdrLabelFollowConcurrencyTests*)\|(HerdrPlacementSettingsTests*)\|(HerdrSupervisionBackoffTests*)\|(PhoneHomeStandingLaunchTests*)\|(ProjectCascadeForeignKeyTests*)\|(RemoteExecutableLaunchTests*)\|(SessionHealthTests*)/*` | `cf7d939ec76780052af022949a0da158af1123604103bb9106fc6a551f468ac9` |
| <a id="linux-l07"></a>L07 | `20261123-104906-ec02/CP-10` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/(C1818GcOwnerRobustnessTests*)\|(ProcessSpawnLimitTests*)/*` | `991280e6871ed4cb71a251d1684f18ef64379a2e23396f53ea0f73046af07687` |
| <a id="linux-l08"></a>L08 | `20261123-104906-ec02/CP-12` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/C1008PlatformContractTests/(C1050_Guard_matches_native_platform*)\|(C1050_Remote_entries_guard_before_work*)\|(C1050_Rolling_entries_guard_before_work*)\|(C1050_Audit_rejects_empty_or_incomplete_outcomes*)` | `2d3214502e34cba5430ddb0b414152fc82de43fbe7bc7f7425500b1c03c8843d` |
| <a id="linux-l09"></a>L09 | `20261123-104906-ec02/CP-15` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/VolumeRetirementHostProofTests/*` | `b786351941ffbdefc1ad38e7f9eb1f16987d5ca1ef839f50e0179e1f07ffc646` |
| <a id="linux-l10"></a>L10 | `20261123-104906-ec02/CP-16` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/VolumeRetirementPublicationTests/(Origin_graph_corpus_seed_*)\|(Host_*)\|(Origin_salvage_graph_passes_the_full_offline_audit*)\|(Local_salvage_refs_are_protected_roots*)` | `406fd6196275deefaf9f9158dee578ee4fb8beb3eb95c3312b439712feeff9b3` |
| <a id="linux-l11"></a>L11 | `20261123-104906-ec02/CP-17` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/VolumeRetirementReceiptTests/(Preview_*)\|(Receipt_*)\|(Proof_*)\|(Volume_*)\|(Freshness_*)\|(Legacy_*)\|(Manual_*)\|(Changed_*)\|(Every_*)` | `04d6d0fe568fdc4b972a24f04097c573287020630c98ae22ec01c157f875c5e3` |
| <a id="linux-l12"></a>L12 | `20261123-104906-ec02/CP-2` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/RemoteScriptContractTests/(C1008_Recycle_audits_work_as_1654*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)\|(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Recycle_reaps_dirty_scratch*)\|(C1008_Recycle_audits_promisor_checkout*)` | `f5519eea238ba0e1512927d103f1fc18b6928f63c544c3a4e6661395110ab10b` |
| <a id="linux-l13"></a>L13 | `20261123-104906-ec02/CP-22` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/RetiredTempContainerHostTests/*` | `d8be28fe7278f87cb79eb17ea9ced718794cc840f6a9f9bb949e88823becae6d` |
| <a id="linux-l14"></a>L14 | `20261123-104906-ec02/CP-23` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/RetiredTempContainerScriptTests/*` | `acc49ed8fe856df2e23e536100c135f285a0716efb5dd1cabb04ac47c963cb8c` |
| <a id="linux-l15"></a>L15 | `20261123-104906-ec02/CP-28` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/VolumeRetirementWrapperTests/*` | `3ba45ef95831b25ed7c46891960058923700f5f4444cd9bb54f30626ba0b01c3` |
| <a id="linux-l16"></a>L16 | `20261123-104906-ec02/CP-7` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/(RollingVolumeRecycleCensusChurnTests*)\|(RollingVolumeRecycleScriptTests*)/*` | `29599dafc7120ff8c68ebd46a316679aa860b266c966d30253ff5142f13996e6` |
| <a id="linux-l17"></a>L17 | `20261123-104906-ec02/CP-9` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/(C1008HostFixtureSetupInsertionTests*)\|(CheckpointImportTests*)\|(ClaudeTokenRefreshOptInTests*)\|(CodexRootExecContractTests*)\|(CodexRunnerImageContractTests*)\|(DeployServer2ApiDefaultTests*)\|(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(GrokRunnerImageContractTests*)\|(HangfireLeaseDocumentationTests*)\|(HangfireStorageSelectionTests*)\|(HostJqPrerequisiteScriptTests*)\|(JqRunnerImageContractTests*)\|(ProcessSpawnLimitTests*)\|(RollingProductionMountTests*)\|(RunCheckpointSourceScriptTests*)\|(RunnerPushCredentialDocsTests*)\|(SeedSmokeTargetingPackContractTests*)\|(Server2ProductionServerContractTests*)\|(Server2SidecarStandingAgentsTests*)\|(Server2StagingServerContractTests*)\|(ServerImagePushCredentialTests*)/*` | `c58f750ef74f70ac69c37edaa0cf76c20a4c3d502b24f4829337bc969deb95b7` |
| <a id="linux-l18"></a>L18 | `20261123-124150-ad6b/CP-41` | `a4b82ffb23253f00dd1bd5a980ba97a44c4cef77` | `/*/*/(DerivedImpactIndexTests*)\|(LiveLaneInventoryTests*)/*` | `d2e89f6e219c047476a6e16773f143984e99825e959bd03c0d784c6a19a31d78` |
| <a id="linux-l19"></a>L19 | `20261123-155950-cc36/CP-8` | `4e9b7efdbea4af35340bfcc55ca7bc74db1c3286` | `/*/*/(ChangeAwareReleaseTests*)\|(DerivedImpactIndexTests*)\|(ExpectationObservationAdapterTests*)\|(ImpactedClassesCensusTests*)\|(ImpactedClassesSelectorTests*)\|(IncrementalSelectionSoundnessTests*)\|(InterimVerificationReadinessTests*)\|(LandGeneratedArtifactTests*)\|(RcFailedFirstExecutionTests*)\|(RcInputManifestTests*)\|(RcProjectSelectionTests*)\|(RcRunEvidenceTests*)\|(RcStoppedRunSeedTests*)\|(ReleaseGateFollowupTests*)\|(ReleaseImpactEnvelopeTests*)\|(ReleaseSelectionHopSeedTests*)\|(TestSelectionCatalogTests*)\|(TestSelectionDependencyTests*)\|(TestSelectionExecutionTests*)\|(TestSelectionTests*)\|(TimeoutAuditTests*)/*` | `301071be905333842876b4181322ae131e3472f974b52f3d26b267794927c7cd` |
| <a id="linux-l20"></a>L20 | `20261125-142218-704b/CP-1` | `2fd61b643ea4231fa732cd4969b5284d43b33f39` | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(DerivedImpactIndexTests*)\|(C1729LayeringGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(NativeLinuxPolicyWitnessMutationTests*)/*` | `7ca2636c1914d912ecb8c108cd4af6aff64094c088022868f0bc5ee068b5efb5` |
| <a id="linux-l21"></a>L21 | `20261125-142218-704b/CP-3` | `2fd61b643ea4231fa732cd4969b5284d43b33f39` | `/*/*/(ChannelOutboundProbeModelTests*)\|(ProbeModelComparisonTests*)\|(AppDbContextModelTests*)\|(ReviewEngineMigrationTests*)\|(CheckpointCallerBudgetTests*)\|(ScriptHarnessHandleProbeTests*)\|(ScriptHarnessLinuxOwnershipTests*)/*` | `97ac4d4543a0f472258a43e764f747b8bc156e2d9f25499db608ad554ec7f51d` |
| <a id="linux-l22"></a>L22 | `20261125-142218-704b/CP-4` | `2fd61b643ea4231fa732cd4969b5284d43b33f39` | `/*/*/(OrchestratorWorkspaceWarningTests*)\|(TestFixtureCleanupGuardTests*)\|(ReviewMirrorCheckoutTests*)\|(DesktopPostgresTests*)\|(DeployLocalTests*)\|(PgQpsTests*)\|(MeasureSessionStateCacheScriptTests*)\|(PtyHostReaperTests*)\|(ZombieAgentReaperTests*)\|(BootstrapCheckTests*)\|(VerifyDevStackSmokeTests*)/*` | `f54f57c4dcfe6a6bcdf198305aab00bd7bb9f4a3c3725d39049ab2bdaa479ab2` |
| <a id="linux-l23"></a>L23 | `20261125-145444-fd84/CP-4` | `43855daf07739d5b301a517cba33bfa90ec80b5d` | `/*/*/(CheckpointReceiptSafetyTests*)\|(CheckpointTerminalReceiptTests*)\|(HostCleanupCheckpointReceiptTests*)\|(ImpactedClassesCensusTests*)\|(OutputCleanupTests*)\|(ProcessSpawnLimitTests*)\|(CheckpointToolCommandTests*)/*` | `4968f81df7f6bff1bdbee3551f02e726104fb7950fe037784be9f422156f9c0c` |
| <a id="linux-l24"></a>L24 | `20261125-152827-e3d7/CP-11` | `82ad13fb1aaadcf7f9f76095d2a817ec2f1def59` | `/*/*/(CardFilePrivacyGitTests*)\|(CardFilePrivacyPathTests*)\|(ImpactedClassesCensusTests*)\|(IncrementalSelectionSoundnessTests*)\|(LandGeneratedArtifactTests*)\|(ProcessSpawnLimitTests*)/*` | `560b9876fa8cad62dc705bc126e48cdedc8e137b337f8f2edb61d8a97252278d` |
| <a id="linux-l25"></a>L25 | `20261125-152827-e3d7/CP-12` | `82ad13fb1aaadcf7f9f76095d2a817ec2f1def59` | `/*/*/(RcBridgeProbePlatformTests*)\|(RcInputManifestTests*)\|(RcInputOwnersMapTests*)\|(ReleaseImpactEnvelopeTests*)\|(TestSelectionExecutionTests*)/*` | `8c5c319535023b33fe919662c0de1b4c891a92bab6d941fe18aabe7f2c4a191f` |
| <a id="linux-l26"></a>L26 | `20261125-152827-e3d7/CP-14` | `82ad13fb1aaadcf7f9f76095d2a817ec2f1def59` | `/*/*/(ProcessSpawnLimitTests*)/*` | `1981f51ed318a04ef2283d4417c12a99b1136f93ac58af97618d1fba682d64b2` |
| <a id="linux-l27"></a>L27 | `20261125-152827-e3d7/CP-15` | `82ad13fb1aaadcf7f9f76095d2a817ec2f1def59` | `/*/*/(ProcessSpawnLimitTests*)/*` | `9c5fb7b8c24005aeb373abcd3508ccf39e09ccef94c400fc13b15e55cee2ecdd` |
| <a id="linux-l28"></a>L28 | `20261125-152827-e3d7/CP-4` | `82ad13fb1aaadcf7f9f76095d2a817ec2f1def59` | `/*/*/(GitIndexLockTests*)\|(ServerGitAssemblyTests*)/*` | `6f886eec8429e4b2c07679e851fdeb6cf3331f25b5b5436be9c59f578e8a341a` |
| <a id="linux-l29"></a>L29 | `20261125-152827-e3d7/CP-5` | `82ad13fb1aaadcf7f9f76095d2a817ec2f1def59` | `/*/*/(AntiphonCliLauncherTests*)\|(C1733AllocatorFixtureTests*)\|(ChangeAwareReleaseTests*)\|(ImpactedClassesSelectorTests*)\|(LiveLaneCensusTests*)\|(LiveLaneRunnerTests*)\|(NightlyCoreGoldenTests*)\|(NightlyRunParityTests*)\|(NightlyScriptsTests*)\|(QualificationTimingTests*)/*` | `7a13100b0de3d79791d3fc0a87737cb98f2832e865ea8d7d695cbf620ccd58c6` |
| <a id="linux-l30"></a>L30 | `20261125-152827-e3d7/CP-6` | `82ad13fb1aaadcf7f9f76095d2a817ec2f1def59` | `/*/*/(RcFailedFirstExecutionTests*)\|(RcProjectSelectionTests*)\|(RcRunEvidenceTests*)\|(RcStoppedRunSeedTests*)\|(ReleaseCutTests*)\|(ReleaseGateAuthorityContractTests*)\|(ReleaseGateFollowupTests*)\|(ReleaseGatePublicationAuthorityTests*)\|(ReleaseGateRunTests*)\|(ReleaseGateStatusTests*)/*` | `cd6ff5b4b95f51e3828e0ae7195ca3b5b13bf3a9d15e9c91370f1ca6f8357c84` |
| <a id="linux-l31"></a>L31 | `20261125-152827-e3d7/CP-7` | `82ad13fb1aaadcf7f9f76095d2a817ec2f1def59` | `/*/*/(ReleasePromoteTests*)\|(ReleasePublishTests*)\|(ReleaseSelectionHopSeedTests*)\|(ReleaseTagAllocatorTests*)\|(ServerGitProcessTests*)\|(TestSelectionTests*)\|(TestTimingOperationLabelTests*)\|(TimeoutAuditTests*)/*` | `55fb9f08e004d1df9314b42d58be92fa4c22633c50cc1c2595e50f695d835a78` |
| <a id="linux-l32"></a>L32 | `20261125-205319-5b8a/CP-5` | `4a2014a4a57d900ec24dd20405f0ca2ff396a9ff` | `/*/*/(CustodyHelperContractTests*)\|(DockerStackContractTests*)\|(DockerStackDocumentationTests*)/*` | `121ec4a248c7cc58ed2813f179b23f42a4c20101714d020b5a67c2ab5ecea0be` |
| <a id="linux-l33"></a>L33 | `20261125-205319-5b8a/CP-6` | `4a2014a4a57d900ec24dd20405f0ca2ff396a9ff` | `/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` | `3ebd790257b63e21e0480eaabe569550fab39cf4750bc6742385d81307fe57f7` |
| <a id="linux-l34"></a>L34 | `20261126-030930-6641/CP-1` | `0627c97672e3a9a076af53cc9a32d4336b2df02d` | `/*/*/VolumeRetirementApplyGateTests/*` | `73d374a6ffdfa9332086bed94d2887d3ae6bd7cee1ef37a523734a7dd0af0277` |
| <a id="linux-l35"></a>L35 | `20261126-035349-6918/CP-G2` | `46417306d6c85d6ce2a1e1bdad734fa68c1e6b0a` | `/*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(ProcessSpawnLimitTests*)/*` | `f6b9b728323f3340b903ddb310569682998d84e99d4792770d7cbf9003ffc570` |
| <a id="linux-l36"></a>L36 | `20261126-065938-5e6e/CP-1` | `0b07453e48b7eaab1985ec0f7537b056a4b80ed3` | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(DerivedImpactIndexTests*)\|(C1729LayeringGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(NativeLinuxPolicyWitnessMutationTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | `2fe1d84ee88b8bc719d61d24efed713348768c9ddfb0a580be46224af3ab063f` |
| <a id="linux-l37"></a>L37 | `20261126-065938-5e6e/CP-9` | `0b07453e48b7eaab1985ec0f7537b056a4b80ed3` | `/*/*/PlanCoverageBindingTests/(historical_plans_bind_without_suppression*)\|(historical_row_timeout_checklist_binds_the_advisory_method*)` | `22ab6a2ac45e474e177c5592b7f2e807f342660f6479b339029eb3d55859c30c` |
| <a id="linux-l38"></a>L38 | `20261126-074134-4c4f/CP-1` | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(DerivedImpactIndexTests*)\|(C1729LayeringGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(NativeLinuxPolicyWitnessMutationTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | `90208c7d5ff30dfc303eb861ad4ce79395c60d1a540e5cbc866ea37bd4d32b02` |
| <a id="linux-l39"></a>L39 | `20261126-074134-4c4f/CP-2` | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/(PlanCoverageBindingTests*)\|(IncrementalSelectionSoundnessTests*)\|(StatusSnapshotScriptTests*)\|(TaskProgressStallSweepTests*)\|(SessionRuntimeActionTests*)/*` | `8749445310c7d45d30fd7b6134ef68f93dd8e9454983f0ffc7f11fcbdd0b39d7` |
| <a id="linux-l40"></a>L40 | `20261126-074134-4c4f/CP-3` | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/(CheckpointAdmissionContractTests*)\|(AgentSessionRuntimeTests*)\|(TurnEndGapRestartDeliveryTests*)/(C1616_*)\|(C1259_*)\|(C488_Wakeup_cut_holds_the_row_only_when_in_process_hints_are_dropped*)` | `abac721244b624c840a889521ce9989aae24ed5f152cbb348ea3784f9f13a6ca` |
| <a id="linux-l41"></a>L41 | `20261126-074134-4c4f/CP-4` | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/NightlyNativeOwnershipTests/*` | `b701803c2197948d08722af725316a42ba8715a499b83134e193691cba38d44d` |
| <a id="linux-l42"></a>L42 | `20261126-074134-4c4f/CP-5` | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/(LinuxOwnedGitProcessTests*)\|(CustodyHelperContractTests*)\|(BlockedReplyReportRetentionTests*)\|(CallerWakeTests*)\|(ExpectedPlatformSkipRosterTests*)/*` | `bdc7d50df668fd063ae395c7bf6fc26dac1b2e9d1436d48729de1b4d7aa859c5` |
| <a id="linux-l43"></a>L43 | `20261126-082946-85f4/CP-1` | `cf4f2681dd65f136c3200784248c649099e61a73` | `/*/*/(Card2060PlacementHandshakeTests*)\|(NativeLinuxPolicyWitnessTests*)/(SlowFirstPlacementSucceedsAndStuckPeerExpires*)\|(Policy_rows_require_pinned_attribute*)` | `81d649e9daa658581e6583cf46c395701c93feb712ff666ca9068fedfe12f6e4` |
| <a id="linux-l44"></a>L44 | `20261126-082946-85f4/CP-3` | `cf4f2681dd65f136c3200784248c649099e61a73` | `/*/*/NativeLinuxPolicyWitnessTests/Policy_rows_require_pinned_attribute` | `857b4a25930432ca4a84f89fe350d03976f18cb47c7965559c74efb9feed0068` |
| <a id="linux-l45"></a>L45 | `20261126-082946-85f4/CP-4` | `cf4f2681dd65f136c3200784248c649099e61a73` | `/*/*/NativeLinuxPolicyWitnessTests/Policy_rows_require_pinned_attribute` | `913523d3a38d48f4d2c17529e8770882ed59cc8bc2d77802426cea8d15304d69` |
| <a id="linux-l46"></a>L46 | `20261126-092752-ebbe/CP-1` | `1d788ccd82febd9de97cd91514ce3e6a89564027` | `/*/*/ReleaseImpactEnvelopeTests/P2CanonicalBytes` | `21222f07b136658c51b2a08cfd1cd4c2db30e962fbd596acc6a3d7de3f93e2df` |
| <a id="linux-l47"></a>L47 | `20261126-092752-ebbe/CP-3` | `1d788ccd82febd9de97cd91514ce3e6a89564027` | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestLaneCategoryGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | `ab12191871ac93ac8305195aff8d7bab432b2c0327601c22732ab00efd9bd61a` |
| <a id="linux-l48"></a>L48 | `20261126-092752-ebbe/CP-Compat` | `1d788ccd82febd9de97cd91514ce3e6a89564027` | `/*/*/(SelectorLineEndingAttributeTests*)\|(RcProjectSelectionTests*)\|(RcChunkInputDigestTests*)\|(RcForcedFullTests*)\|(NightlyChunkRunnerTests*)\|(IncrementalSelectionSoundnessTests*)\|(ReleaseCutTests*)\|(LiveLaneRunnerTests*)\|(NightlyCoreGoldenTests*)/*` | `6b4d03a3aaff54be864898d6e76b55f82aeaa378f5d2c4634202007ffbea4e6e` |
| <a id="linux-l49"></a>L49 | `20261126-092752-ebbe/CP-Guard` | `1d788ccd82febd9de97cd91514ce3e6a89564027` | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | `8b9da921c3dbcccd1bed4c2968ded3d2de9016e81f6e5af01e5808562839db0e` |
| <a id="linux-l50"></a>L50 | `C2082-B-linux-20261126-113021-680e/C2082-B-linux` | `2fd61b643ea4231fa732cd4969b5284d43b33f39` | `/*/*/(CheckpointAdmissionContractTests)\|(IncrementalSelectionSoundnessTests)/(C1616_admission_timeout_publishes_readable_nonexecuted_receipt)\|(Recorded_owner_map_status_matches_the_committed_map)` | `0eed1d0f994ed225fab5ff3bc7fe18213a3d5bce7ae3989337fe0895188b67eb` |
| <a id="linux-l51"></a>L51 | `C2082-F-linux-20261126-114248-2870/C2082-F-linux` | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition)\|(ExplicitGatesAreNeverFullRequired)` | `9bbacd1f87b477a85b5d384bd8ba1698b03e3033a216474e066a70657e62a437` |
| <a id="linux-l52"></a>L52 | `CP-4-20261125-164126-ee1e/CP-4` | `ac8bf731e34b9dfc9a2bc615a2010cb55a9db0be` | `/*/*/(GrokRunnerImageContractTests*)\|(HangfireLeaseDocumentationTests*)\|(HangfirePostgreSqlStorageTests*)\|(HangfireStorageSelectionTests*)\|(JqRunnerImageContractTests*)\|(MidTurnInterruptDocumentationTests*)\|(PhoneHomeBackpressureTests*)\|(PhoneHomeCompletedCardTransportTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeEpochAgreementTests*)\|(PhoneHomeMirrorJoinTests*)\|(PhoneHomeRollingRunnerTests*)\|(PhoneHomeSpillTransportTests*)\|(PhoneHomeTaskDispatchProjectionTests*)\|(RcBridgeProbePlatformTests*)\|(ReceiptScanDestinationTests*)\|(RemoteWorkspacePreparerTests*)\|(RemoteWorkspaceReadinessTests*)\|(RemoteWorktreeMirrorTests*)\|(RunnerBranchContractDocumentationTests*)\|(RunnerCodexCliEvidenceTests*)\|(RunnerPushCredentialDocsTests*)\|(RunnerSettlementSyncTests*)\|(RunnerSyncLeaseFairnessTests*)\|(Server2ProductionServerContractTests*)\|(Server2SidecarStandingAgentsTests*)\|(Server2StagingServerContractTests*)\|(ServerImagePushCredentialTests*)\|(SessionRunnerAbsenceEvidenceDocumentationTests*)\|(StandingBootDocumentationTests*)\|(VolumeRetirementApplyGateTests*)\|(ZombieCensusPlatformTests*)/*` | `b97dccdc7f4b99e16c97157fd5b5fab22ca75b524416c4790b92143eb101e42f` |
| <a id="linux-l53"></a>L53 | `CP-LINUX-20261126-075652-672c/CP-LINUX` | `1cc664b3e36ac10de7299948e657a2b682e94b81` | `/*/*/DerivedImpactIndexTests/(regenerate_command_reports_changed_then_unchanged*)\|(regenerate_command_removes_staging_file_when_move_fails*)` | `90afcc4460426224a9f5882892d30c835431273b42e23d3071d744c4fae8ccf7` |
| <a id="linux-l54"></a>L54 | [Group 113 Linux reproduction](2026-11-26-card-2082-repository-invariants-plan.md) | `2fd61b643ea4231fa732cd4969b5284d43b33f39` | `historical_plans_bind_without_suppression, historical_row_timeout_checklist_binds_the_advisory_method` | transcribed case result |
| <a id="linux-l55"></a>L55 | [Group 86 Linux reproduction](../../investigations/2026-11-26-why-missed-group-86-predecessor-seed.md) | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | `C1033_AdditiveUpgradePreservesHistory` | transcribed case result |

## Exact-pin Linux replays

These method filters answer the cross-platform comparison. Both builds were made from clean, verified source. The B row passed and the F row reproduced the historical gate failures.

```text
CHECKPOINT C2082-B-linux commit=2fd61b643ea4231fa732cd4969b5284d43b33f39 build=ok filter=/*/*/(CheckpointAdmissionContractTests)|(IncrementalSelectionSoundnessTests)/(C1616_admission_timeout_publishes_readable_nonexecuted_receipt)|(Recorded_owner_map_status_matches_the_committed_map) executed=2 passed=2 failed=0 skipped=0 trx=/work/evidence/invariant-qualification/fc2be934-linux-b/C2082-B-linux-20261126-113021-680e/run.trx slot=granted waited=0s dirty=0 source=2fd61b643ea4231fa732cd4969b5284d43b33f39 sourceState=clean buildSource=verified
CHECKPOINT C2082-F-linux commit=0cfc4ac1ffb41f5af159a0c1db423fe119950267 build=ok filter=/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition)|(ExplicitGatesAreNeverFullRequired) executed=2 passed=0 failed=2 skipped=0 trx=/work/evidence/invariant-qualification/fc2be934-linux-f/C2082-F-linux-20261126-114248-2870/run.trx slot=granted waited=75s dirty=0 source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 sourceState=clean buildSource=verified
```
