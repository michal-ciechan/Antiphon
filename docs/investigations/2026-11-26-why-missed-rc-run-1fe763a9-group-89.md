# Why Windows group 89 missed the historical distillation seed

Windows Full run `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267` failed `OutputDistillationMigrationTests.Upgrade_preserves_historical_outcomes_and_nullable_deadlines` in 6.5 s. The caller's excerpt records `DbUpdateException` at the seed save (line 29), with the inner exception truncated. The case passed in run `b8cbfb24` at `2fd61b643ea4231fa732cd4969b5284d43b33f39`.

## Introducing change and the related groups

Caller-report wake mapped three nullable receipt properties on `AgentTask` in `7fbe6b71926da6862f5bd0296f77b22384f53705` and added their columns in `20261125151517_AddCallerReportSettlementReceipt` at `e639d421081165757449374af78ee7a162cc915d`. Merge `fccb17b2b2d755ab0ebfdbbbad2b51cb8ef2be29` published both. EF's current model includes those columns in `INSERT` even when the fixture leaves the values null. A database built at `20261106032116_AddRefOnlyParkWorkspaceSnapshot` has no receipt columns. PostgreSQL reported SQLSTATE `42703` for `AgentTasks.ReceivedReportCompletedAt` in the group 86 and 49 replays. Group 89 has the same failed save, table, schema/model mismatch and first bad pin. Its specific PostgreSQL diagnostic is inferred from that evidence.

| Group | Failure and repair |
|---|---|
| 141 | The retained TRX investigation records two `LiveLaneInventoryTests` assertions for the caller-wake gate dispositions. This was a separate test-roster regression from the same feature. The method-scoped policy and shared fixture overlay are `585258802ac6be0a55c6c2235cad5c5833f14d37` and `793f96e527d1942faa6c62456766fe92884a3a30`. [Group 141](2026-11-26-why-missed-group-141-live-gate-disposition.md) |
| 86 | `CardCleanupSchedulingSchemaTests.C1033_AdditiveUpgradePreservesHistory` used a current-model task insert. `ea6c2abc77eb55d0b5995291aeb5f0dd691d0d7b` changes it to explicit predecessor SQL and binds sibling task seeds to their migration model. [Group 86](2026-11-26-why-missed-group-86-predecessor-seed.md) |
| 49 | `PipelineSpeedIntegrationTests.C1281_B1Schema` failed in `C544World.SeedOwnerAsync`. `ea6c2abc` fixes that call. Task `0bcdcd3a` also added the two-world witness and bound the sibling `LandingSafetyHarness` seed. Master carries the witness `e23c48ec9aca8fcd52bc3fd7544b30aada0b7d13`, harness fix `c1b91e9d23e5c3ade4c23e872c12810fc17128b2`, and derived index refresh `e9f644e4f9d13e1e3c312658c65c69977b0a196a` (the landed equivalent of task tip `b70cc733967b9444a28fd5e20e18857ce86e71a4`). [Group 49](2026-11-26-why-missed-rc-run-1fe763a9-group-49.md) |
| 87 | `HostCleanupOrchestrationTests.AddHostCleanup_upgrades_current_master_without_changing_existing_rows` failed on the historical owner insert. `ea6c2abc` already fixed it. Debug task `5cd0632a` verified it and supplied the investigation landed as `c7221e8568087a7683cb4c619c81184fe71ea5da`. [Group 87](2026-11-26-why-missed-rc-run-1fe763a9-group-87.md) |
| 66 | `LandRequestWriteDiagnosticTests.C883_MigrationPreservesLegacyRows` failed on the historical owner insert. `ea6c2abc` already fixed it. Debug `78e9edc0` ran the exact method on master. [Group 66](2026-11-26-why-missed-rc-run-1fe763a9-group-66.md) |
| 89 | The distillation fixture seeded its owner and twelve legacy outcomes before a rewind. `git log -S CreateReversibleTipSeedContext -- tests/Antiphon.Tests/Application/OutputDistillationMigrationTests.cs` finds `ea6c2abc`, which moves that seed to the reversible-tip context. The rewind, post-rewind update and upgraded assertions still use the same legacy values. |

## Approving reviews and selection gap

Interim Review `16611b04-cefb-495c-8c99-c09736e8011b` derived 62 migration classes, with its exact execution limited to the `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` and `RequestedModelAliasDatabaseMigrationTests.Migrate_up_adds_the_nullable_alias_and_refused_down_preserves_it` pair. Both failed and were repaired. Final/Full Review `ede19f30-78ba-4c49-8a27-93377614c75d` at `9218d74123abfec3cb79d8ddad96b45f4d689793` ran retention and alias (29), two caller-wake receipt/claim cases, the six-class guard (57), and two separate native caller-read cases. Merge `9aec88a3-a8a9-419a-8604-685ec7980e32` added the host-load and C0824 chain-pin checks, derived index and expanded budget. Its executed rows were narrower than the migration kind's derived roster. [Executed Review and Merge selections](2026-11-26-why-missed-rc-run-1fe763a9-group-49.md#approving-reviews-and-merge)

The distillation test is in `tests/Antiphon.Tests/Application` with category Integration. Those rows omitted it. Its direct reads and writes concern distillation outcomes and deadlines. The shared task model widened the insert without changing those references. The group 87 sweep enumerated it as already repaired, but no exact group 89 receipt was recorded at that point.

## Every server migration after the reversible tip

Read each migration's `Up` at master `2afced1108e7111de3587f07afa6b2af321f53ba`. The table lists additions. The only existing-table additions that intersect the historical writers below are on `AgentTasks`, `AgentTaskEvents` and `HostLoadSamples`.

| Migration | Table | Columns introduced |
|---|---|---|
| `20261107034430_HostLoadTelemetryS1` | `AgentTaskEvents` (existing) | LoadAttempt, LoadDispatchedAt, LoadQueuedAt, LoadRunnerId, LoadSessionId, LoadStatus |
|  | `AgentTaskLoadWindows` (new) | Id, TaskId, Attempt, PhysicalHostKey, RunnerBootId, WindowOrdinal, StartEventId, EndEventId, StartedAt, EndedAt, DetailsJson, Complete |
|  | `HostLoadHourlyRollups` (new) | Id, PhysicalHostKey, SourceEpoch, HourUtc, Metric, HistogramJson, StatisticsJson |
|  | `HostLoadIngestCursors` (new) | RunnerId, RunnerStoreId, Cursor, GapsJson, Complete, CommittedAt |
|  | `HostLoadObservedEvents` (new) | EventId, ObservedAt |
|  | `HostLoadSampleIdentities` (new) | RunnerStoreId, BootId, Sequence, PayloadHash, IngestedAt |
|  | `HostLoadSamples` (new) | Id, RunnerStoreId, BootId, Sequence, RunnerId, PhysicalHostKey, SourceEpoch, SampledAt, IngestedAt, DerivedAt, IsPrimary, PayloadJson, CountersJson |
|  | `HostLoadTaskMetricBuckets` (new) | WindowId, HourUtc, Metric, HistogramJson |
| `20261107040142_HostLoadSnapshotMembership` | `HostLoadSamples` (existing) | CreatedTransactionId |
|  | `HostLoadHourlyRollups` (existing) | CreatedTransactionId |
| `20261107104114_HostLoadRetentionCatchup` | `HostLoadRetentionStates` (new) | Id, Backlog, UpdatedAt |
| `20261108121426_Card1773TranscriptPromptStampIndex` | None | Concurrent transcript prompt index only |
| `20261110213738_Card1294ShadowCohort` | `CardCompletionShadowEvents` (new) | Id, SchemaVersion, CardId, AcceptanceId, OperationId, ProposedClose, ExpectedClose, Adjudicator, AdjudicatedAt, Actor, RecordedAt |
| `20261114063858_AddSessionTranscriptSummaries` | `SessionTranscriptSummaries` (new) | AgentSessionId, SchemaVersion, Valid, NextBackfillAttemptAt, Count, NewestEffectiveTimestamp, LastSequence, LastEntryId, LastKind, LastTimestamp, LastCreatedAt, EndSequence, EndTimestamp, HasActivity, HasNullTimestampActivity, ActivityTimestamp, LastTurnEndSequence, LastTurnTitleSequence, LastUserPromptSequence |
|  | `SessionTranscriptSummaryMaintenance` (new) | Id, Enabled |
| `20261119045705_AddLandVerificationFailure` | `AgentTaskLandings` (existing) | VerificationFailingProjects, VerificationFailureOutput, VerificationFailureStep |
| `20261120091515_AddRequestedModelAlias` | `AgentTasks` (existing) | RequestedModelAlias |
| `20261122173647_AddReviewEngine` | `ReviewSubjects` (new) | Id, Host, Repo, PrNumber, Title, Url, AuthorLogin, AuthorDisplayName, OpenedByLogin, AuthorResolvedBy, BaseRef, HeadRef, BaseSha, HeadSha, OwnerTeams, OwnerUsers, CreatedAt |
|  | `ReviewWorkflowRevisions` (new) | Id, Name, Hash, GraphJson, CreatedAt |
|  | `ReviewRuns` (new) | Id, SubjectId, Status, Stage, Round, BotOnlyRounds, NeedsHumanApproval, WaitingOn, CostUsd, Categories, BlastReach, BlastExternalContract, BlastData, BlastRollbackAtRelease, BlastReversalAfterMonths, BlastExposure, WorkflowName, WorkflowRevisionId, WorkflowHash, SteerPrompt, BaseTipSha, StartedAt, UpdatedAt |
|  | `ReviewContextPacks` (new) | Id, RunId, Iteration, GroupKey, Hash, PrefixHash, Content, TokenEstimate, CreatedAt |
|  | `ReviewFindings` (new) | Id, RunId, Key, CheckId, CheckWhy, Severity, Flags, BypassesRoundBar, Confidence, FilePath, LineStart, LineEnd, Anchor, Title, Body, Suggestion, Hunk, Status, RoundRaised, Posted, SyncState, ExternalThreadUrl, ExternalCommentId, ExternalThreadId, CreatedAt, UpdatedAt |
|  | `ReviewGates` (new) | Id, RunId, Kind, NodeId, Iteration, Status, TeamKey, AssigneeLogin, AssigneeDisplayName, OpenedAt, DecidedAt, Decision, DecidedBy, SteerPrompt, Reasons |
|  | `ReviewIterations` (new) | Id, RunId, Iteration, Round, Trigger, BaseSha, HeadSha, StartedAt |
|  | `ReviewNodeExecutions` (new) | Id, RunId, NodeId, Iteration, Sequence, Kind, Status, ItemKey, Outcome, Provider, Model, Transport, InputTokens, OutputTokens, CostUsd, CostBasis, InputsHash, StartedAt, CompletedAt, Summary, OutputJson |
|  | `ReviewPackLedgerEntries` (new) | Id, RunId, Iteration, Path, Source, Reason, Score, Tokens, Included |
|  | `ReviewFindingEvents` (new) | Id, FindingId, Action, ActorLogin, ActorDisplayName, ReasonCategory, Scope, Text, SyncState, CreatedAt |
|  | `ReviewGatePasses` (new) | Id, GateId, Login, Reason, At |
| `20261125151517_AddCallerReportSettlementReceipt` | `AgentTasks` (existing) | ReceivedReportCompletedAt, ReceivedReportPromptSequence, ReceivedReportSessionId |

The six `AgentTaskEvents.Load*` columns, three `AgentTaskLandings.Verification*` columns, alias, and three receipt columns are nullable. Both `CreatedTransactionId` additions are required and have the PostgreSQL transaction-ID default. The summary migration also installs and controls trigger functions. The receipt migration backfills qualifying existing task rows.

## Derived seed sweep

Started with tracked C# files matching `MigrateAsync` with a target, target `Migrate`, `GetMigrationName`, upgrade/preservation names, migration test classes, `IMigrator`, `MigrationTestDatabase` and its reversible context (72 first-pass files). Followed schema-detach paths, fixture options and seed helpers into the C883, C1281 and C544 worlds. Read seed sites before and between migration calls, including SQL and indirect service writes. The verdict column is source compatibility against the fetched head for the identified added-column failure. The verification section identifies executed cases.

| Historical test or world | Historical writes and mechanism | Breaks with current additions |
|---|---|---|
| `AgentTaskInternalDecisionMigrationTests` (both cases) | Bound `AgentTasks`; explicit predecessor SQL for the old dispatch event and the task result update. | No. |
| `AgentTaskWorktreeBaseMigrationTests.T0442_V18_upgrade_preserves_historical_base_and_roundtrips_intent` | Bound `AgentTasks`. | No. |
| `CapacityRecoveryCompatibilityTests.Input_body_upgrade_preserves_a_preexisting_event_with_null_body`; `Card0412_V22_migrate_forward_preserves_legacy_holds` | Bound `AgentTasks`; predecessor SQL for `AgentTaskEvents` and hold state update, current mapping for `ModelAvailabilityHolds`. | No. |
| `CardWorktreeMirrorBindingTests.C1032_SchemaLegacyUpgrade` | Bound `AgentTasks`; current mapping for the board/card/revision graph. | No. |
| `ChannelOutboundDurabilitySchemaTests.C519_Upgrade_preserves_ordinals_and_history` | Bound `AgentTasks`; current mapping for channels, sessions, outbound deliveries and queued messages. | No. |
| `ChannelOutboundMigrationTests.Upgrade_preserves_history_and_defaults` | Bound `AgentTasks`; explicit predecessor SQL for `ChatChannels`. | No. |
| `DispatchConcurrencyMigrationTests.Upgrade_populated_database_preserves_task_rows`; `Initialize_from_nondefault_config_then_restart` | Bound `AgentTasks`; current projects, sessions, host budgets and runner-routing settings. `DispatchConcurrencyShop.Open(reversibleTip: true)` writes projects, host budgets and routing settings before the rewind. | No. |
| `HostCleanupOrchestrationTests.AddHostCleanup_upgrades_current_master_without_changing_existing_rows` | Bound `AgentTasks`; explicit SQL for `AgentTaskEvents`. | No. |
| `LandRequestWriteDiagnosticTests.C883_MigrationPreservesLegacyRows` | Bound `AgentTasks`; explicit predecessor SQL for `AgentTaskLandRequests`. | No. Independently executed in Debug `78e9edc0`. |
| `OutputDistillationMigrationTests.Upgrade_preserves_historical_outcomes_and_nullable_deadlines` | Bound `AgentTasks`; current mapping for `OutputDistillations` and explicit SQL for legacy `FeedbackNote`. | No. Exact master execution below. |
| `StandingSpecialistRoutingMigrationTests.Card0415_V25_real_upgrade_preserves_primary_tasks_and_holds_without_fabricating_qualification` | Bound `AgentTasks`; current agents and model-availability holds. | No. |
| `WorkerWorkspaceDefaultMigrationTests.C458_UpgradePreservesLegacyRowsAndAddsNullColumns`; `C458_DownThenUpPreservesLegacyWorkspace` | Bound `AgentTasks` and current project mapping at the reversible tip; explicit `Projects` and `AgentTasks` SQL at the direct predecessor. | No. |
| `PipelineSpeedIntegrationTests.C1281_B1Schema`; `Task_worlds_seed_their_owners_at_the_selected_migration(true)` | `C544World` and `LandingSafetyHarness` both bind `AgentTasks`. C544 uses current mappings for the board/card/session setup. B1 updates only legacy task fields and inserts a `StageOutcome` after rewind. | No. |
| `CardCleanupSchedulingSchemaTests.C1033_AdditiveUpgradePreservesHistory` | Explicit `AgentTasks` SQL; current model for board/card/revision, mirror binding, cleanup, endpoints/targets and worktree residue history. | No. |
| `StandingSessionOwnershipTests.Upgrade_backfills_only_unambiguous_owners_and_preserves_recovery_state` | Separately frozen `PreContinuityDbContext` from the `OrderSpecialistHealthRequests` target model. Seeds agents, sessions, worktrees, incidents, tasks and supervision state. | No. |
| `AgentTaskLandApprovalPersistenceTests.C488_MigrationDoesNotInventApproval`; `C498_MigrationIsAdditive` | Explicit predecessor SQL on tasks and land requests. | No. |
| `AgentTaskLandingPersistenceTests.C448_V31_MigrationAndConcurrentOperations` | Explicit predecessor SQL on tasks and events. | No. `AgentTaskLandings` writes in this class occur at head. |
| `AgentTaskLandNotificationPersistenceTests.C467_V17_UpgradeAndLegacyEvidence` | Explicit predecessor SQL on sessions, tasks, queued messages and task events. | No. |
| `DispatchBaseNotificationTests.C508_RequestlessNotificationUpgrade` | Explicit predecessor SQL on tasks, events, land requests and land notifications. | No. |
| `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` | Detach and rewind an empty head clone, then insert the legacy task with explicit SQL. | No. |
| `C0824RefOnlySnapshotMigrationTests` (legacy and refusal cases) | Explicit SQL for sync debts and parks before and between the relevant migrations. | No. |
| `CallerWakeTests.Authenticated_read_before_the_upgrade_still_closes_its_settlement` | Detach empty receipt fields from a head clone, then explicit predecessor task inserts. | No. |
| `RequestedModelAliasDatabaseMigrationTests.Migrate_up_adds_the_nullable_alias_and_refused_down_preserves_it`; `RequestedModelAliasSchemaTests` | Explicit task SQL after applying the alias or enabling its historical compatibility shim. | No. Neither emits receipt columns. |
| `PostLandMutationAdmissionTests.C478_V01_PublicSurfaceAndMigration`; `TaskPlatformPersistenceTests.Legacy_rows_migrate_to_any`; `WorktreeBaseSelectionTests.C508_BaseFieldsUpgradeAndRoundTrip` | Explicit predecessor SQL for `AgentTasks`; the platform case also inserts project and board/card rows. | No. |
| `CheckpointSourceApprovalTests.migration_and_override_preserve_unknown`; `CardVerificationPolicyTests.C544_MigrationKeepsUnknown` | Explicit predecessor SQL for `StageOutcomes`. | No. |
| `CardFilePrivacyMigrationTests.Upgrade_backfills_existing_rows_without_reinterpreting_old_snapshots` | Explicit predecessor SQL on projects, boards, board columns, cards, and revisions. | No. |
| `RoutingPinCandidateTests` (old pin migration) | Explicit predecessor SQL on `RoutingPins`. | No. |
| `C1514HostLoadMigrationTests.UpgradeAndRefusedDowngradePreserveDurableRows` | Explicit SQL for sync debt and for `HostLoadSamples` at telemetry/membership/catchup targets. | No. The test requires the membership default to fill `CreatedTransactionId` for the old insert. |
| `Card1773TranscriptPromptStampMigrationTests` (populated predecessor fixtures) | Current mapping for `AgentSessions`; explicit SQL for `TranscriptEntries`. | No. This migration adds an index. |
| `SessionTranscriptSummaryMigrationTests.Validity_requires_every_invalidation_trigger` | Current mapping for `AgentSessions`; explicit SQL on transcript entries and summary activation/maintenance at the summary migration. | No. |
| `ReviewEngineDatabaseMigrationTests.A_stored_review_row_refuses_the_downgrade_and_the_detach` | Explicit SQL for `ReviewWorkflowRevisions` after its creation, before the head receipt migration. | No. |
| `Card1619ExitPersistenceTests.Migration_preserves_old_rows_and_nullable_contract` | Current mapping for `AgentSessions` at reversible tip. | No. Later migrations add no session columns. |
| `HerdrPlacementSettingsTests.Follow_migration_preserves_existing_labels`; `StandingRestartEscalationTests.C510_migration_starts_without_invented_history` | Current `Agents` and, for standing restart, `AgentSupervisionStates` plus predecessor SQL updates. | No. |
| `PoolClaudeEffortMigrationTests.C1491_nullable_stamp_roundtrips_and_legacy_rows_stay_unknown` | Current `Agents` mapping at the reversible tip, then a PostgreSQL record copy using the real predecessor row type. | No. |
| `PipelineDefinitionMigrationTests` (historical and populated Down fixtures) | Current mapping for the board/card graph, agents, workflow templates, pipeline definitions/revisions and workflow runs at the reversible tip. Predecessor runs/stages and card linkage use explicit SQL. | No. |
| `SessionRuntimeActionTests.C1259_commit_and_effect_admission_are_atomic("replay-after-ack")` | `SessionStateTestFixture` uses current session mappings and the runtime writes transcript/action rows at the reversible tip. | No added columns on those tables. |
| `SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once("legacy-upgrade")` | `ClaudeDialogFixture` and service writes use the current agent, session, transcript, review-thread and queue mappings at reversible tip before rewind. | No added columns on those tables. |
| `TranscriptHotPathMigrationTests` and `TranscriptHotPathFixture` (reversible-tip paths) | Current session mapping; explicit SQL for transcript setup, delivered batches and duplicates before/after rewind. | No. |
| `InboxConversationMigrationTests.Upgrade_preserves_legacy_data_and_enforces_conversation_scoped_identity` | Current `MessagingDbContext.Inbox` into its own predecessor. | No. Its separate migration chain changes inbox indexes, and the server migrations above do not apply. |

`AgentTaskLandHeldNotificationTests.C641_Upgrade_preserves_existing_held_notification_debt` seeds and generates debt on a fully migrated clone, detaches empty later objects, rewinds and re-upgrades without an intermediate insert. `AgentTuiPersistenceTests.Migration_downgrades_the_five_tables_and_four_columns_cleanly`, `RunnerDefaultMigrationTests.Existing_schema_upgrades_and_legacy_remote_imports_once` and the CARD-1619 ledger witnesses don't seed application rows on their historical baselines. The ledger witnesses insert migration-history metadata. `AgentTuiModelArgumentMigrationTests`, `CommitOnSettleMigrationTests`, and `HostTopConsumerUpgradeReplayTests` emulate old data shapes on upgraded schemas. The secret and Grok rules migrators exercise product transitions, not targeted EF schemas. Source-only migration-operation checks and incidental upgrade-named tests likewise have no historical application-row insert.

## Cheapest pre-land check

`server/Migrations/**`, `server/Infrastructure/Data/AppDbContext*.cs`, and model mappings for the audited tables → `/*/*/HistoricalMigrationSeedCoverageTests/Post_tip_columns_on_historical_seed_tables_have_coverage`. Use the migration impacted-classes row for the affected historical writers. The witness walks the post-tip migrations on disposable PostgreSQL and compares actual catalog additions to the audited writers, including raw migration DDL. A new required or nullable column on an already existing seeded table calls for an updated seed or evidence that the old writer is compatible. The permanent positive control adds a required column to `OutputDistillations` in the disposable database and checks that the real comparison names that column.

The test checks the audited table inventory. A new historical writer needs its table added to that inventory. A create-table migration establishes that table's first shape. The separate messaging context has its own migration chain.

## Verification and rc backport

The exact group 89 method passed against test PostgreSQL on the fetched master `2afced1108e7111de3587f07afa6b2af321f53ba` with 1 passed, 0 failed and 0 skipped. The native-output build and run used separate granted build slots. The TRX is `/work/evidence/task-96809a63/master-group89.trx`.

The exact red witness at `32b810afe87b9f3efef428609776288ba922b609` executed once on test PostgreSQL and failed its intended assertion, naming only `OutputDistillations.HistoricalSeedRequiredProbe (required)`. The TRX is `/work/evidence/task-96809a63/mutant.trx`. The earlier migration files were unchanged for this control.

The task branch checkpoint rows select the catalog witness, exact group 89 upgrade, the two existing group 49 world cases and their B1 upgrade, then the six-class guard union plus `PlanCheckpointClassSelectionGuardTests`. Receipts are stored under `/work/evidence/task-96809a63/checkpoints`.

The remote `rc/2026-12-06` ref was checked at `0cfc4ac1ffb41f5af159a0c1db423fe119950267`. For groups 86, 49, 87, 66 and 89, apply `ea6c2abc` first. Add the group 49 witness and harness fix from `e23c48ec` and `c1b91e9d`, then regenerate the derived index on rc for the `b70cc733`/`e9f644e4` delta. The individual three-way preflight onto the pinned rc tree is clean for `ea6c2abc` and `e23c48ec`; `c1b91e9d` conflicts on `MigrationTestDatabase.cs` on its own because it extends the factory from `ea6c2abc`. The group 87 and 66 documentation commits alone do not repair rc.

For group 141, carry `585258802` followed by `793f96e52` as a separate port and validate the live-lane inventory pair on the integrated candidate. The first commit's individual three-way preflight is clean. The second relies on the first's catalog and note. The suite groups remain owed on the next Windows qualification.

## Exemption

Same historical seed cause as [group 86](2026-11-26-why-missed-group-86-predecessor-seed.md), with the distillation execution, full migration/seed inventory and future-column witness recorded here.
