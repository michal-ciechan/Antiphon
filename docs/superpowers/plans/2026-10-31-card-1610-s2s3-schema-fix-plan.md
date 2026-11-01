# CARD-1610 S2+S3 fix 3 verification

The committed full plan now includes CP-105 (PipelineSpeedIntegrationTests, minimum 39). This repair selects that complete class, CardRelationshipSchemaTests and all 35 sibling classes found by the IMigrator/targeted Database.MigrateAsync scan. No native qualification or full Unit claim.

## Performance budget

One isolated build shared by every row; run with --parallel 4. Four isolated test hosts may overlap, each retaining its own PostgreSQL schema/database and existing assembly process limiter. Existing PostgreSQL template fixture remains assembly-owned; each migration roundtrip requires its own isolated schema. Expected execution under 25 minutes, separate bounded 45-minute slot admission. Estimates and inherited 15-minute row floors are provisional CARD-1404 exceptions, collected only once in this repair. Retain per-test TRX and checkpoint phase timings; uninstrumented external-call spans remain unknown. No selection expected over 30 minutes. Master attribution uses a separate detached master build for the single commissioned RemoteScriptContractTests method; no product fix authorized there. The already-red C1281_B1Schema ran before this source change.

The initial width-2 run 20261031-021401-020d at 0948738178b347d15ace4fee0903d9ef5e47c3c4 was stopped after 12 green rows: measured row throughput forecast completion beyond its 25-minute execution allowance. PipelineSpeedIntegrationTests passed 39/39 in 361 seconds; other completed rows took 50-87 seconds each. Preserve that partial run, its missing terminal report and stop/wait exit 6 as incomplete evidence, not a green certificate. The replacement width-4 run uses the exact same test selection and test source, one fresh output and one build. This scheduling correction is the reason for the extra build and repeated selection; no test/row timeout, assertion or execution allowance is widened. Expected replacement wall is under 20 minutes plus admission; the 25-minute execution allowance remains.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-105 | S2-S4 | `tests/Antiphon.Tests -> bin-c1610-fix3-final/` | server-integration-pipeline-speed-schema | `/*/*/PipelineSpeedIntegrationTests/*` | B1 rollback/upgrade and full affected class | 39 passed; no failed/skipped | 39 | 3 |
| CP-106 | S2-S4 | CP-105 | server-model-drift | `/*/*/CardRelationshipSchemaTests/*` | current model/schema drift | no failed/skipped | 1 | 1 |
| CP-107 | S2-S4 | CP-105 | server-migration-AgentTaskInternalDecisionMigrationTests | `/*/*/AgentTaskInternalDecisionMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-108 | S2-S4 | CP-105 | server-migration-AgentTaskLandApprovalPersistenceTests | `/*/*/AgentTaskLandApprovalPersistenceTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-109 | S2-S4 | CP-105 | server-migration-AgentTaskLandHeldNotificationTests | `/*/*/AgentTaskLandHeldNotificationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-110 | S2-S4 | CP-105 | server-migration-AgentTaskLandingPersistenceTests | `/*/*/AgentTaskLandingPersistenceTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-111 | S2-S4 | CP-105 | server-migration-AgentTaskLandNotificationPersistenceTests | `/*/*/AgentTaskLandNotificationPersistenceTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-112 | S2-S4 | CP-105 | server-migration-AgentTaskWorktreeBaseMigrationTests | `/*/*/AgentTaskWorktreeBaseMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-113 | S2-S4 | CP-105 | server-migration-AgentTuiPersistenceTests | `/*/*/AgentTuiPersistenceTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-114 | S2-S4 | CP-105 | server-migration-CapacityRecoveryCompatibilityTests | `/*/*/CapacityRecoveryCompatibilityTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-115 | S2-S4 | CP-105 | server-migration-CardCleanupSchedulingSchemaTests | `/*/*/CardCleanupSchedulingSchemaTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-116 | S2-S4 | CP-105 | server-migration-CardFilePrivacyMigrationTests | `/*/*/CardFilePrivacyMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-117 | S2-S4 | CP-105 | server-migration-CardVerificationPolicyTests | `/*/*/CardVerificationPolicyTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-118 | S2-S4 | CP-105 | server-migration-CardWorktreeMirrorBindingTests | `/*/*/CardWorktreeMirrorBindingTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-119 | S2-S4 | CP-105 | server-migration-ChannelOutboundDurabilitySchemaTests | `/*/*/ChannelOutboundDurabilitySchemaTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-120 | S2-S4 | CP-105 | server-migration-ChannelOutboundMigrationTests | `/*/*/ChannelOutboundMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-121 | S2-S4 | CP-105 | server-migration-CheckpointSourceApprovalTests | `/*/*/CheckpointSourceApprovalTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-122 | S2-S4 | CP-105 | server-migration-DispatchBaseNotificationTests | `/*/*/DispatchBaseNotificationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-123 | S2-S4 | CP-105 | server-migration-DispatchConcurrencyMigrationTests | `/*/*/DispatchConcurrencyMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-124 | S2-S4 | CP-105 | server-migration-HerdrPlacementSettingsTests | `/*/*/HerdrPlacementSettingsTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-125 | S2-S4 | CP-105 | server-migration-HostCleanupOrchestrationTests | `/*/*/HostCleanupOrchestrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-126 | S2-S4 | CP-105 | server-migration-LandRequestWriteDiagnosticTests | `/*/*/LandRequestWriteDiagnosticTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-127 | S2-S4 | CP-105 | server-migration-OutputDistillationMigrationTests | `/*/*/OutputDistillationMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-128 | S2-S4 | CP-105 | server-migration-PipelineDefinitionMigrationTests | `/*/*/PipelineDefinitionMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-129 | S2-S4 | CP-105 | server-migration-PoolClaudeEffortMigrationTests | `/*/*/PoolClaudeEffortMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-130 | S2-S4 | CP-105 | server-migration-PostLandMutationAdmissionTests | `/*/*/PostLandMutationAdmissionTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-131 | S2-S4 | CP-105 | server-migration-RoutingPinCandidateTests | `/*/*/RoutingPinCandidateTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-132 | S2-S4 | CP-105 | server-migration-RunnerDefaultMigrationTests | `/*/*/RunnerDefaultMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-133 | S2-S4 | CP-105 | server-migration-SessionRuntimeActionRoutingTests | `/*/*/SessionRuntimeActionRoutingTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-134 | S2-S4 | CP-105 | server-migration-SessionRuntimeActionTests | `/*/*/SessionRuntimeActionTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-135 | S2-S4 | CP-105 | server-migration-StandingRestartEscalationTests | `/*/*/StandingRestartEscalationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-136 | S2-S4 | CP-105 | server-migration-StandingSessionOwnershipTests | `/*/*/StandingSessionOwnershipTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-137 | S2-S4 | CP-105 | server-migration-StandingSpecialistRoutingMigrationTests | `/*/*/StandingSpecialistRoutingMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-138 | S2-S4 | CP-105 | server-migration-TaskPlatformPersistenceTests | `/*/*/TaskPlatformPersistenceTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-139 | S2-S4 | CP-105 | server-migration-TranscriptHotPathMigrationTests | `/*/*/TranscriptHotPathMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-140 | S2-S4 | CP-105 | server-migration-WorkerWorkspaceDefaultMigrationTests | `/*/*/WorkerWorkspaceDefaultMigrationTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
| CP-141 | S2-S4 | CP-105 | server-migration-WorktreeBaseSelectionTests | `/*/*/WorktreeBaseSelectionTests/*` | migration-path sibling full class | no failed/skipped | 1 | 1 |
