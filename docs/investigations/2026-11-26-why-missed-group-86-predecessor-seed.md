# Why Windows group 86 missed a historical task seed

Windows Full run `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267` failed `CardCleanupSchedulingSchemaTests.C1033_AdditiveUpgradePreservesHistory` in 75.249 s. The caller's TRX excerpt records a `DbUpdateException` at `SeedPredecessorAsync`. The same row passed in Windows run `b8cbfb24`.

The Linux replay at the exact `1fe763a9` pin executed one case and failed in the same seed. The build-slot console captured the inner exception, `Npgsql.PostgresException: 42703: column "ReceivedReportCompletedAt" of relation "AgentTasks" does not exist`. The case took 42.045 s.

## Introducing change

The caller-report wake work mapped the receipt fields in `7fbe6b71926da6862f5bd0296f77b22384f53705` and added their DDL in `e639d421081165757449374af78ee7a162cc915d` (`20261125151517_AddCallerReportSettlementReceipt`). It merged in `fccb17b2b2d755ab0ebfdbbbad2b51cb8ef2be29`. The later `6f5b24b7543724333ce549618088b3a17ab7bda1` updated two tests that asserted the migration chain tip.

C1033 builds a database through the last reversible migration, seeds its history, then rewinds further to the cleanup-scheduling predecessor. That starting database predates the caller receipt. EF includes mapped nullable fields in an `AgentTask` insert even when the fixture leaves them null.

## Review and search coverage

- Interim review `16611b04` derived a 62-class migration roster across two projects. It included `CardCleanupSchedulingSchemaTests`. The migration comparison that executed used `/*/*/(C0824RefOnlyIdentityTests*)|(RequestedModelAliasDatabaseMigrationTests*)/(Mode_round_trips_with_legacy_default*)|(Migrate_up_adds_the_nullable_alias_and_refused_down_preserves_it*)`. Both cases failed, and `59721f0269d8b33b5e1ec3519acbf518cfec42ce` repaired them. The review report recorded the wider execution as pending.
- The follow-up in task `550744a3` used `grep -n "class \w*Tests"` on named files including `Application/RequestedModelAliasMigrationTests.cs`, and `grep -rn "class C0824RefOnlyIdentityTests" tests` (transcript sequence 21). Its later search for `class RequestedModelAliasSchemaTests` (sequence 79) and `RequestedModelAlias*` selection reached the sibling in `TestHelpers`. Commit `9218d74123abfec3cb79d8ddad96b45f4d689793` repaired that case. The searches used class names from the finding, so they did not trace `AgentTask` writes from other historical database factories.
- Approving review `ede19f30` recorded Full scope at `9218d741`. Its validated rows covered `BlockedReplyReportRetentionTests` and `RequestedModelAlias*` (29 cases), two caller-wake witnesses (2), and the six-class guard subset (57). The Integration test lived in `Application/CardCleanupSchedulingSchemaTests.cs`, outside those executed selections.

## Repair and pre-land check

C1033 now inserts its historical task using explicit predecessor columns in the seed transaction. The other affected fixtures use a test-only seed context that takes the `AgentTask` property set from the reversible tip's migration model. Their migrators and upgraded assertions still use the current model. Existing raw SQL seeds and the independently frozen standing-session predecessor already isolate their writes from current receipt columns.

For CARD-1782, changes under `server/Migrations/**`, `server/Domain/Entities/AgentTask*.cs`, or `server/Infrastructure/Data/AppDbContext*.cs` can use `/*/Antiphon.Tests.Application/CardCleanupSchedulingSchemaTests/C1033_AdditiveUpgradePreservesHistory` as an exact pre-land witness. For an `AgentTask` mapping change, derive the wider selection from migration call sites across `tests/` and follow helper-created schemas to their seed writes. Include writes made after `CreateReversibleTipAsync` and before a subsequent `MigrateAsync` call.

Earlier same-cause evidence comes from the alias fixtures above. This note records the cleanup fixture's distinct selection gap and the other reversible-tip seed sites.
