# Why the Windows rc sweep missed group 90

Windows run `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267` reported `DbUpdateException` from the seed in two cases in `antiphon-all-group-90`. The supplied run summary records both passing in `b8cbfb24`.

| Method in `CapacityRecoveryCompatibilityTests` | Failing seed on Windows | Duration |
|---|---|---:|
| `Input_body_upgrade_preserves_a_preexisting_event_with_null_body` | Current `AppDbContext.SaveChangesAsync` at line 34, before inserting the legacy event | 5.6 s |
| `Card0412_V22_migrate_forward_preserves_legacy_holds` | Current `AppDbContext.SaveChangesAsync` at line 94, before rewinding to the capacity-recovery predecessor | 6.3 s |

Both old methods create a database at `20261106032116_AddRefOnlyParkWorkspaceSnapshot`, then insert an `AgentTask` using the current EF mapping. The mapping gained the nullable `ReceivedReportCompletedAt`, `ReceivedReportPromptSequence` and `ReceivedReportSessionId` fields in `20261125151517_AddCallerReportSettlementReceipt`, merged by `fccb17b2b2d755ab0ebfdbbbad2b51cb8ef2be29`. Nullable fields still appear in EF inserts. PostgreSQL rejects a column absent from the schema with SQLSTATE 42703. The Windows summary truncated the inner exceptions. The seed sites and commit diff match the 42703 failure reproduced for the sibling cleanup-seed case. [Group 86 reproduction](2026-11-26-why-missed-group-86-predecessor-seed.md)

`ea6c2abc77eb55d0b5995291aeb5f0dd691d0d7b` moves both fixture writes to `MigrationTestDatabase.CreateReversibleTipSeedContext`. That context uses the historical migration's `AgentTask` column set. The event fixture inserts predecessor event columns explicitly. The holds fixture seeds holds through EF at the reversible tip and updates the legacy hold reason using SQL after the rewind.

## Selection gap

Task `622f8d94` used `/*/*/*MigrationTests/*` for Application discovery. Its validated 25-class roster contained 24 classes from `Antiphon.Tests` and one from `Antiphon.Messaging.Tests`. The matching suffix excluded all five methods in `CapacityRecoveryCompatibilityTests`, including the two historical writers. The group 89 investigation had found that class by source behavior, but the exact test receipt in that investigation was for the distillation upgrade. [Group 89 investigation](2026-11-26-why-missed-rc-run-1fe763a9-group-89.md)

The census followed targeted EF `Migrate` and `MigrateAsync` calls, bound and detach helpers, raw SQL, and `IsolatedTestSchema` creation paired with seed writes. I followed helper paths through `DispatchConcurrencyShop`, `C544World`, `LandingSafetyHarness`, `SessionStateTestFixture`, the Claude dialog fixture and the transcript fixture. A second search checked migration-history access and names containing Compatibility, Upgrade, Legacy, Preserves, Rewind, DownThenUp and Migrate. The execution selects a historical variant where a method is parameterized.

## Historical seed inventory

**New** means this class had no receipt in the 25-class sweep. **Prior** means it had a receipt in that sweep. The new sweep includes one case per newly covered class, both group 90 cases and one migration-ledger companion.

| Class | Historical seed | Sweep |
|---|---|---|
| `AgentTaskInternalDecisionMigrationTests` | Bound task context. Predecessor SQL inserts a dispatch event and updates the task result. | Prior |
| `AgentTaskLandApprovalPersistenceTests` | Explicit predecessor SQL for tasks and land requests. | New |
| `AgentTaskLandHeldNotificationTests` | Full-schema seed and debt generation, followed by a rewind and re-upgrade. All application-row writes precede the rewind. | New |
| `AgentTaskLandNotificationPersistenceTests` | Explicit predecessor SQL for sessions, tasks, queued messages and events. | New |
| `AgentTaskLandingPersistenceTests` | Explicit predecessor SQL for tasks and events. EF landing writes occur after upgrade. | New |
| `AgentTaskWorktreeBaseMigrationTests` | Bound task context. | Prior |
| `C0824RefOnlyIdentityTests` | Detaches and rewinds an empty clone. Inserts task with explicit predecessor SQL. | New |
| `C0824RefOnlySnapshotMigrationTests` | Explicit SQL for sync debt and park rows across migration targets. | Prior |
| `C1514HostLoadMigrationTests` | Explicit SQL for sync debt and host load samples across the telemetry, membership and catchup targets. Membership supplies the new field's default. | Prior |
| `CallerWakeTests` | Detaches empty receipt fields from a clone, then inserts tasks with predecessor SQL. | New |
| `CapacityRecoveryCompatibilityTests` | Bound task context, reversible-tip hold mapping, predecessor SQL for the input event and hold update. | New, both cases |
| `Card1619ExitPersistenceTests` | EF session seed at the reversible tip. | New |
| `Card1773TranscriptPromptStampMigrationTests` | EF session seed and predecessor transcript SQL. The migration adds a concurrent index. | Prior |
| `CardCleanupSchedulingSchemaTests` | Explicit task SQL. EF seeds the board/card graph, mirror binding, cleanup, endpoint/target and worktree residue history. | New |
| `CardFilePrivacyMigrationTests` | Explicit predecessor SQL for projects, boards, columns, cards and revisions. | Prior |
| `CardVerificationPolicyTests` | Explicit predecessor SQL for stage outcomes. | New |
| `CardWorktreeMirrorBindingTests` | Bound task context. EF seeds the board/card/revision graph. | New |
| `ChannelOutboundDurabilitySchemaTests` | Bound task context. EF seeds channels, sessions, outbound deliveries and queued messages. | New |
| `ChannelOutboundMigrationTests` | Bound task context and explicit predecessor channel SQL. | Prior |
| `CheckpointSourceApprovalTests` | Explicit predecessor SQL for stage outcomes. | New |
| `DispatchBaseNotificationTests` | Explicit predecessor SQL for tasks, events, land requests and notifications. | New |
| `DispatchConcurrencyMigrationTests` | Bound task context. EF seeds projects, sessions, host budgets and runner routing. `DispatchConcurrencyShop` also seeds the configuration at the reversible tip. | Prior |
| `HerdrPlacementSettingsTests` | EF agent seed at the reversible tip. | New |
| `HostCleanupOrchestrationTests` | Bound task context and explicit event SQL. | New |
| `InboxConversationMigrationTests` | EF inbox seed at the predecessor of the conversation-scoped identity migration, in the separate messaging migration chain. | Prior |
| `LandRequestWriteDiagnosticTests` | Explicit predecessor SQL for land requests. | New |
| `OutputDistillationMigrationTests` | Bound task context, EF distillation seed and explicit SQL for the legacy feedback field. | Prior |
| `PipelineDefinitionMigrationTests` | EF board/card graph, agents, workflow templates, pipeline definitions/revisions and workflow runs at reversible tip. Explicit predecessor SQL for runs/stages and card linkage. | Prior |
| `PipelineSpeedIntegrationTests` | Bound task contexts in both landing worlds. EF seeds the board/card/session graph. The B1 case updates legacy task fields and writes a stage outcome after rewind. | New |
| `PoolClaudeEffortMigrationTests` | EF agent seed at the reversible tip, then a PostgreSQL record copy using the predecessor row type. | Prior |
| `PostLandMutationAdmissionTests` | Explicit predecessor SQL for tasks. | New |
| `RequestedModelAliasDatabaseMigrationTests` | Explicit task SQL after applying the alias migration. | Prior |
| `RequestedModelAliasSchemaTests` | Explicit predecessor task SQL with the alias compatibility interceptor. | New |
| `ReviewEngineDatabaseMigrationTests` | Explicit review workflow revision SQL after the review tables are created and before the receipt migration. | Prior |
| `RoutingPinCandidateTests` | Explicit predecessor routing pin SQL. | New |
| `SessionRuntimeActionRoutingTests` | The Claude dialog fixture and service write agents, sessions, transcript, review threads and queued messages at the reversible tip. | New |
| `SessionRuntimeActionTests` | The session fixture and runtime write sessions, transcript and action rows at the reversible tip. | New |
| `SessionTranscriptSummaryMigrationTests` | EF session seed and explicit transcript and summary-activation/maintenance SQL at the summary migration. | Prior |
| `StandingRestartEscalationTests` | EF seeds agents and supervision state at the reversible tip, then explicit SQL updates the predecessor state. | New |
| `StandingSessionOwnershipTests` | Frozen predecessor `PreContinuityDbContext` seeds agents, sessions, worktrees, incidents, tasks and supervision state. | New |
| `StandingSpecialistRoutingMigrationTests` | Bound task context plus EF agents and model availability holds. | Prior |
| `TaskPlatformPersistenceTests` | Explicit predecessor SQL for tasks, projects and board/card rows. | New |
| `TranscriptHotPathMigrationTests` | The transcript fixture uses EF sessions and explicit transcript batches and duplicates at the reversible tip and across the rewind. | Prior |
| `WorkerWorkspaceDefaultMigrationTests` | Bound task context and EF project mapping at the reversible tip. Explicit project/task SQL at the direct predecessor. | Prior |
| `WorktreeBaseSelectionTests` | Explicit predecessor SQL for tasks. | New |

`AgentTuiPersistenceTests` and `RunnerDefaultMigrationTests` exercise upgrade/downgrade without seeding application rows at their historical baselines. `Card1619ExitMigrationIdTests` writes ledger metadata. `AgentTuiModelArgumentMigrationTests`, `CommitOnSettleMigrationTests` and `HostTopConsumerUpgradeReplayTests` seed legacy values on upgraded schemas. Secret and Grok rule migrators exercise product state transitions. Source-only migration checks describe operations and do not apply them to a historical PostgreSQL schema.

## PostgreSQL witness

Run the exact method `/*/*/HistoricalMigrationSeedCoverageTests/Post_tip_columns_on_historical_seed_tables_have_coverage`. It now enumerates compiled test methods that reference the historical database helper or a target-taking EF migration API, including calls reached through async state machines and fixture methods. It emits the candidate methods with the catalog check. These are conservative candidates. For example, a helper that accepts a reversible-tip flag also appears in tests that use its default head schema, and the compiled `IMigrator` call signature is the same when its optional target is null. Source triage identifies writers and the specific variants to execute. The earlier sweep supplies the separate messaging assembly's receipt.

The catalog check now compares additions on all existing PostgreSQL tables visited from the reversible tip up to head. This catches new columns on newly audited seed tables without requiring a seeded-table list update first. Existing reviewed column additions remain explicit. The synthetic required-column control still exercises the real comparison.

## Verification and rc handoff

The master build and test host at `93e24eac5050f9e0ed1ef8c439c098140ccb25f5` each used granted host slots. The PostgreSQL sweep executed 29 exact test IDs across 28 classes, with 29 passed, 0 failed and 0 skipped in 6m 58s. The group 90 input-event case took 13.05 s and the hold case took 24.81 s. Validation checked the clean source, assembly hashes, exact TRX identities and counters, and the matching phase journal.

- Master [TRX](/work/evidence/task-88067d23/master-sweep/run.trx), [case results](/work/evidence/task-88067d23/master-results.csv) and [validation](/work/evidence/task-88067d23/master-validation.log).
- Red witness on `7396587e42a9d509b3e2837d58f15154bc8d60f7` executed one case. It failed the intended assertion because both group 90 identities were missing. [Red TRX](/work/evidence/task-88067d23/witness-red/run.trx)
- Initial compiled witness on `cbd9293c4519fbfdc954c422daa47b2e7898b170` passed its one case in 3m 39s. Shared fixture bodies were scanned repeatedly. [Initial green TRX](/work/evidence/task-88067d23/witness-green-initial/run.trx)
- Final witness on `17c503b1c04f7452df2bcbc7d9162138331ca683` passed its one case in 64 s with no skips. It scans each reachable body once and emits the same 1,153 candidate methods across 157 classes. The roster contains all 29 executed method identities and all 44 census classes in `Antiphon.Tests`. [Green TRX](/work/evidence/task-88067d23/witness-green/run.trx), [compiled roster](/work/evidence/task-88067d23/witness-green/compiled-methods.txt) and [roster validation](/work/evidence/task-88067d23/roster-validation.log).

`ea6c2abc77eb55d0b5995291aeb5f0dd691d0d7b` is already first in the ordered rc backport. A three-way apply in a temporary git index at the pinned rc SHA completed with no unmerged paths and produced the same group 90 test file as the landed fix. It supplies both group 90 seed repairs. The integrated Windows candidate still needs its qualification receipt. [Pinned rc apply check](/work/evidence/task-88067d23/rc-group90-port.log)
