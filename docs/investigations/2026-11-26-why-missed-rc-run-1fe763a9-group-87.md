# Why Windows group 87 missed a historical task seed

Windows Full run `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267` failed `HostCleanupOrchestrationTests.AddHostCleanup_upgrades_current_master_without_changing_existing_rows` in 29.9 s. The caller's TRX excerpt records a `DbUpdateException` from `AppDbContext.SaveChangesAsync` at test line 55. The same row passed in Windows run `b8cbfb24`.

## Mechanism

The test builds a database at `MigrationTestDatabase.LastReversibleMigration` (`20261106032116_AddRefOnlyParkWorkspaceSnapshot`). It seeds one `AgentTask`, rewinds to `20261002101556_AddAgentTaskEventInputBody`, and applies `AddHostCleanup`. At the pin it inserted the task through the current `AppDbContext`. EF includes every mapped `AgentTask` column in the insert, including the three nullable receipt columns that `20261125151517_AddCallerReportSettlementReceipt` adds. That migration came from the caller-report wake work: the mapping was added in `7fbe6b71926da6862f5bd0296f77b22384f53705`, the DDL in `e639d421081165757449374af78ee7a162cc915d`, and both merged in `fccb17b2b2d755ab0ebfdbbbad2b51cb8ef2be29`. The historical database does not have those columns.

On Linux, a build of master `07a534f5e04333670c893632b04820fe7e17dd76` reproduced the failure in 24.4 s. For that run the test's seed was set back to the pin's current-model insert. The inner exception was `Npgsql.PostgresException: 42703: column "ReceivedReportCompletedAt" of relation "AgentTasks" does not exist`.

This is the same cause as [group 86](2026-11-26-why-missed-group-86-predecessor-seed.md) and [group 49](2026-11-26-why-missed-rc-run-1fe763a9-group-49.md). Group 86 commit `ea6c2abc77eb55d0b5995291aeb5f0dd691d0d7b` already moved this test's seed to `MigrationTestDatabase.CreateReversibleTipSeedContext`. That context takes its `AgentTask` property set from the `AddRefOnlyParkWorkspaceSnapshot` target model. That commit is on master but not at the pin. The group 87 repair is therefore a Linux verification of that commit, with no new test change.

## Outside the selection

The committed derived impact index caps `server/Domain/Entities/AgentTask.cs` (429 classes) and its explicit list does not name `HostCleanupOrchestrationTests`. The index has no entry for the receipt migration file. The executed review and Merge rows for the caller-report wake work are listed in the group 86 and group 49 notes. None of them selected this Integration class. It reaches the new columns only through a historical schema, which is not visible from its own source references.

## Sweep

Group 86 changed the `AgentTask` writes in historical-schema tests. Group 87 searched again for the same pattern across `tests/`. The search covered every call site of `CreateReversibleTipAsync`, `MigrationTestDatabase.CreateAsync`, a targeted `MigrateAsync` or `Migrate`, `DetachHostLoadAsync` and `DetachCallerReportReceiptAsync`. That was 60 files, and each one was followed through to its seed writes. After `AddRefOnlyParkWorkspaceSnapshot`, only three tables gained columns: `AgentTaskEvents`, `AgentTaskLandings` and `AgentTasks`. The receipt columns on `AgentTasks` are the only ones newer than the passing `b8cbfb24` run. The classification of the historical `AgentTask` seeds:

- Migration-bound seed context, from group 86 on master: `AgentTaskInternalDecisionMigrationTests` (2), `AgentTaskWorktreeBaseMigrationTests`, `CapacityRecoveryCompatibilityTests` (2), `CardWorktreeMirrorBindingTests`, `ChannelOutboundDurabilitySchemaTests`, `ChannelOutboundMigrationTests`, `DispatchConcurrencyMigrationTests`, `HostCleanupOrchestrationTests`, `LandRequestWriteDiagnosticTests`, `OutputDistillationMigrationTests`, `StandingSpecialistRoutingMigrationTests`, `WorkerWorkspaceDefaultMigrationTests`, and `C544World` through `PipelineSpeedIntegrationTests.C1281_B1Schema`.
- Explicit predecessor SQL: `CardCleanupSchedulingSchemaTests.C1033_AdditiveUpgradePreservesHistory`, `C0824RefOnlyIdentityTests`, `AgentTaskLandApprovalPersistenceTests`, `AgentTaskLandingPersistenceTests`, `AgentTaskLandNotificationPersistenceTests`, `PostLandMutationAdmissionTests`, `DispatchBaseNotificationTests.C508_RequestlessNotificationUpgrade`, `WorktreeBaseSelectionTests`, `TaskPlatformPersistenceTests`, `RequestedModelAlias*` and `CallerWakeTests`.
- Separately frozen model: `StandingSessionOwnershipTests` uses its own `PreContinuityDbContext`, built from the `OrderSpecialistHealthRequests` target model.
- Seed at the current tip before a detach and rewind, with no task write before the re-upgrade: `AgentTaskLandHeldNotificationTests.C641_Upgrade_preserves_existing_held_notification_debt`.
- Other reversible-tip fixtures, which seed only tables that gained no later columns: `DispatchConcurrencyShop` (projects), `SessionStateTestFixture`, `TranscriptHotPathFixture`, `ClaudeDialogFixture` (sessions and transcripts), `PipelineDefinitionMigrationTests`, `PoolClaudeEffortMigrationTests`, `HerdrPlacementSettingsTests`, `StandingRestartEscalationTests`, `Card1619ExitPersistenceTests` and `CardVerificationPolicyTests`.

One site remains: `LandingSafetyHarness.InitializeAsync(reversibleTip: true)` still seeds its owner through the current model. No master test calls it with `true`. The group 49 branch `feat/card-task-0bcdcd3a` binds it to the seed context and adds `Task_worlds_seed_their_owners_at_the_selected_migration` as its witness. No other site needed a repair, so group 87 adds no witness of its own.

## Cheapest pre-land check

When `server/Migrations/**`, `server/Domain/Entities/AgentTask*.cs` or `server/Infrastructure/Data/AppDbContext*.cs` changes, add `/*/Antiphon.Tests.Application/HostCleanupOrchestrationTests/AddHostCleanup_upgrades_current_master_without_changing_existing_rows` beside the group 86 `C1033_AdditiveUpgradePreservesHistory` row and the group 49 task-world witness.

## rc

`rc/2026-12-06` is at the failing pin and does not have `ea6c2abc77eb55d0b5995291aeb5f0dd691d0d7b`. That test-only commit cherry-picks onto `0cfc4ac1f` with no conflict (a per-commit `merge-tree` with an explicit `--merge-base`). Before Windows group 49, 86 or 87 is qualified again, rc needs it, plus the group 49 harness fix once that lands.
