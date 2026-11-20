# Why missed: Astra alias migration and the C1584 roster

One note for the two rc/2026-11-30 cut blockers at pin `14ac361e12449303682e10071820e55c0fd6ab26`. The cut note is `5e00ffd553283744e1d7ffd78a4b0a2b227faf6b:docs/investigations/2026-11-20-rc-cut-2026-11-30.md`. The high-radius merge row ran 727 tests and failed 27. Twenty-four of those are the two causes below. Three `LibGit2VsGitWorkingTreeVerdictTests` reds are inherited and out of scope.

## Cause A: `20261120091515_AddRequestedModelAlias`

introducing commit: `09037a515` (Astra selectable model alias).

`RequestedModelAlias` is a nullable `character varying(80)` on `AgentTasks`. Its Down throws. Historical baselines stop at `MigrationTestDatabase.LastReversibleMigration` (`20261106032116_AddRefOnlyParkWorkspaceSnapshot`), which is before this column. Seeds that insert the current `AgentTask` shape into that schema get Postgres 42703. Two tip-rewind tests then call Down and get `An explicit requested model alias cannot be discarded by a downgrade.` The chain witnesses still expected the land-verification migration to be the tip. The delegated-task contract snapshot had no `requestedModelAlias`.

The Astra selectable plan's migration checkpoint was only the shape test:

`docs/superpowers/plans/2026-11-20-astra62-selectable-checkpoints.md` CP-4: `RequestedModelAliasMigrationTests` and `AppDbContextModelTests` (Min 4). It does not name `C0824RefOnlySnapshotMigrationTests`, `C1514HostLoadMigrationTests`, any `CreateReversibleTipAsync` seed, or `ContractSnapshotTests`.

The follow-up fix plan `docs/superpowers/plans/2026-11-20-astra62-selectable-fix-checkpoints.md` says `RequestedModelAliasMigrationTests` is outside its closed list because that fix does not touch the migration. The original plan had already closed the shape row. Neither list is the historical-baseline set or the E2E snapshot.

Short Merge (operator 2026-11-18) does not run the migration-chain classes, the baseline-seed classes, or the E2E contract snapshot. Those sit on the rc/nightly lane. The cut's high-radius row is what executed them.

### Derived-impact index

`tests/derived-impact-index.json` does not map a new migration, or the `AgentTask` column, onto the historical-seed classes or the snapshot test.

- `server/Migrations/20261120091515_AddRequestedModelAlias.cs` is absent. Older migration files are indexed per file and point at their own shape tests. `20261108121426_Card1773TranscriptPromptStampIndex.cs` points at `DerivedImpactIndexTests` and `ImpactedClassesSelectorTests`, not at the chain witnesses.
- `server/Domain/Entities/AgentTask.cs` is capped at 427 classes (owed 402). The stored edge includes `PipelineSpeedIntegrationTests` and `C0824RefOnlyIdentityTests`. It does not include `ContractSnapshotTests` or the historical-seed set.
- `server/Application/Dtos/AgentTaskDtos.cs` is capped. Its stored edge is create/dispatch classes. It does not include `ContractSnapshotTests` or the historical seeds.
- `tests/Antiphon.Tests/TestHelpers/MigrationTestDatabase.cs` does list many historical classes, and that list is itself capped at 25. It omits `DispatchConcurrencyMigrationTests`, `HostCleanupOrchestrationTests`, `OutputDistillationMigrationTests`, `StandingSpecialistRoutingMigrationTests`, `LandRequestWriteDiagnosticTests`, `PipelineSpeedIntegrationTests`, and `ContractSnapshotTests`. The Astra commit did not change this helper, so a merge subset of the migration and the entity does not select it.

### Guard to close the class

The chain assertions and `ContractSnapshotTests.Delegated_task_board_and_drawer_contracts` already fail when the tip or the summary field drifts. They are outside the short selection. Proposed impacted-classes row, not applied (`ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`, so this is not a few-line edit):

```json
{
  "id": "migration-tip-and-task-contract",
  "globs": [
    "server/Migrations/*.cs",
    "server/Domain/Entities/AgentTask.cs",
    "server/Application/Dtos/AgentTaskDtos.cs"
  ],
  "methods": [
    "C0824RefOnlySnapshotMigrationTests/Migration_chain_is_linear_and_snapshot_matches_the_final_model",
    "C1514HostLoadMigrationTests/ChainIsLinearAndSnapshotMatchesModel",
    "RequestedModelAliasMigrationTests/UpIsAdditiveAndDownRefusesDataLoss",
    "ContractSnapshotTests/Delegated_task_board_and_drawer_contracts"
  ]
}
```

That row still would not name every `CreateReversibleTipAsync` seed. Mapping `server/Migrations/**` and `AgentTask.cs` through `MigrationTestDatabase` consumers, with the cap raised past the historical classes, is the coverage that would have selected the 42703 seeds.

## Cause B: C1584 roster

introducing commit: `d3b09ac62` (CARD-2004 A-1).

`DelegationScratchCallerTests.Copied_delegate_loads_the_caller_helper_before_the_server_call` is a new E2E `[Test]`. `C1514LandingSplitBudgetTests.C1584_E2EExpandedBudget` compares `tests/Antiphon.E2E/**/*.cs` to `scripts/fixtures/nightly/c1584-e2e-source-roster.txt` and also checks the census (`195` nodes, `188` required) and `scripts/lib/nightly-class-costs.json`. The new identity was in the source tree and not in the roster.

This is the same miss as [2026-11-09-why-missed-landing-split-budget.md](2026-11-09-why-missed-landing-split-budget.md). That note already proposed:

`tests/Antiphon.E2E/**/*.cs` -> `C1514LandingSplitBudgetTests/C1584_E2EExpandedBudget`

The row was not applied. A short Merge of an E2E file still does not select the Antiphon.Tests census. The cheapest pre-land check remains that one method.

## Not defects

Five failures in the 727-test row are co-run timeouts. They are not product defects. In this session the smaller class run passed `Additive_upgrade_and_empty_downgrade_preserve_legacy_rows`, both `Downgrade_refuses_recorded_snapshot_and_preserves_its_bytes` cases, and all three `UpgradeAndRefusedDowngradePreserveDurableRows` cases. `Card1773TranscriptPromptStampMigrationTests.Retry_finishes_cleanup_after_invalid_index_rename` was the fifth. The cut note records that class passing alone (17 total, 15 passed, 2 failed, and those 2 were the chain assertions). It was not re-run here.
