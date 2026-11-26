# Why Windows group 49 missed a historical task-world seed

Windows Full run `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267` failed `PipelineSpeedIntegrationTests.C1281_B1Schema` in 23.607 s. The previous run, `b8cbfb24` at `2fd61b643ea4231fa732cd4969b5284d43b33f39`, reported it passed.

## Introducing commit

The caller-report wake change mapped three receipt properties on `AgentTask` in `7fbe6b71926da6862f5bd0296f77b22384f53705` and added their nullable columns in migration `20261125151517_AddCallerReportSettlementReceipt` at `e639d421081165757449374af78ee7a162cc915d`. Merge `fccb17b2b2d755ab0ebfdbbbad2b51cb8ef2be29` published both. The CARD-2074 follow-up changed tests for migration-chain pins and added no migration.

B1 initializes `C544World` at `20261106032116_AddRefOnlyParkWorkspaceSnapshot` so the test can exercise a reversible downgrade and re-upgrade. EF's current `AgentTask` model sent the new receipt columns in its owner insert, although that database predates them. The earlier alias column was handled by the fixture's alias interceptor, which accounts for the same seed path working at the earlier pin. The exact group 49 replay on Linux returned PostgreSQL `42703` on `AgentTasks.ReceivedReportCompletedAt` from `C544World.SeedOwnerAsync`.

## Approving reviews and Merge

- Interim Review `16611b04-cefb-495c-8c99-c09736e8011b` derived a 62-class downgrade roster. Its executed migration comparison targeted `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` and `RequestedModelAliasDatabaseMigrationTests.Migrate_up_adds_the_nullable_alias_and_refused_down_preserves_it`. Both failed. It recorded the broader roster as pending.
- Approving Final/Full Review `ede19f30-78ba-4c49-8a27-93377614c75d` at `9218d74123abfec3cb79d8ddad96b45f4d689793` reported validated run `20261125-210349-5cbe`. It executed `BlockedReplyReportRetentionTests` and `RequestedModelAlias*` (29), two `CallerWakeTests` receipt and claim cases (2), and the six-class guard (57). The two native caller-read cases passed in a separate run.
- Merge `9aec88a3-a8a9-419a-8604-685ec7980e32` reported the guards plus `DerivedImpactIndexTests` (112), `C1584_E2EExpandedBudget` (1), retention, alias and `C1514HostLoadMigrationTests` (41), the two receipt and claim cases (2), both native caller reads (2), and the C0824 chain-pin case (1). Its extra migration coverage tested chain pins after the merge.

## Outside the selection

`PipelineSpeedIntegrationTests` is an Integration class under `tests/Antiphon.Tests/Application`. The executed rows above omitted it. The [CARD-2082 inventory](../superpowers/plans/2026-11-26-card-2082-invariant-inventory.md) lists the class and identifies compiled EF migrations and the model snapshot among its inputs. The committed derived index at the failing pin lists the class under `server/Domain/Entities/AgentTask.cs`. Replaying `impacted-classes --merge-subset` across the introducing merge also selects it through the migration rule in `CP-merge-subset-tests`. The recorded Merge run used the narrower Review handoff and selected chain-pin cases.

## Cheapest pre-land check

`server/Migrations/**`, `server/Domain/Entities/AgentTask*.cs` or `server/Infrastructure/Data/AppDbContext*.cs` -> `/*/Antiphon.Tests.Application/PipelineSpeedIntegrationTests/(C1281_B1Schema*)|(Task_worlds_seed_their_owners_at_the_selected_migration*)`.

The seed witness creates `C544World` and `LandingSafetyHarness` on both the current and reversible migration tips and reads back their owners. Master already carries group 86 commit `ea6c2abc77eb55d0b5995291aeb5f0dd691d0d7b`, which bound the C544 insert to the reversible migration model. The sibling landing harness now uses that factory as well. `DispatchConcurrencyShop`, `SessionStateTestFixture`, and `TranscriptHotPathFixture` seed other tables on the reversible tip. Existing migration tests that seed `AgentTasks` before re-upgrading were addressed in the group 86 repair.

## Exemption

Same missing-column cause as [group 86](2026-11-26-why-missed-group-86-predecessor-seed.md). Group 49 adds the separate selection evidence and the task-world witness. At witness commit `f7e6a5af3515604db02e1f1409cfa894b187ba0e`, the current-tip case passed and the reversible-tip case failed in `LandingSafetyHarness.SeedAsync` with the same `42703` error. The pinned `rc/2026-12-06` needs the historical seed repair before a subsequent Windows qualification of these rows.
