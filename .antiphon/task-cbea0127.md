# Task cbea0127 — restore rc migration id 20261030141651

D-1 is fixed on `feat/card-task-cbea0127`. The CARD-1619 exit-signal migration uses rc id `20261030141651_AddSessionExitSignalEvidence` for the migration file and the `[Migration]` attribute. The Designer model body is the merged post-CARD-1610 model. `AppDbContextModelSnapshot` is unchanged. `dotnet ef migrations has-pending-model-changes` reported no pending model changes.

Remote tip after this report commit is recorded in the caller message. Code commits:

- Base `b1c6889fa7fb7ca420e71f0c7ac20a38f49a6477`
- Test `a17b97ba4555e2ca34129a6af22c05bb75ecab88`
- Fix `20f0801bdbcb50fc9e0ea7699fb530a7aef6e667`
- Docs `02de28eb0b324e20a33ff8a2a174c87d4e549cd7`

## What the two directions prove

Shared test Postgres only (`postgres:16-alpine` via Testcontainers). No desktop, server2, or other host database was contacted.

- Rc ledger. History holds `20261030141651_AddSessionExitSignalEvidence` and the four nullable columns (`RunAttempts.ExitSignal`, `AgentSessions.ExitSignal`, `AgentSessions.ExitStopIntentJson` varchar(4096), `AgentIncidents.ExitSignal`). The three CARD-1610 migrations are absent. `MigrateAsync` reaches head with no Postgres 42701 and applies `20261030114738_AddTaskCeilingSupervision`, `20261030143219_AddTaskSupervisionQueuePurpose`, and `20261030214946_CloseTaskSupervisionReportWatches`.
- Master ledger. CARD-1610 is applied and the exit migration is absent. `MigrateAsync` applies `20261030141651` even though later CARD-1610 ids are already in history. Applied history then equals `GetMigrations()` and contains the rc id. `20261031180000_AddSessionExitSignalEvidence` is absent. The migration list has exactly one `AddSessionExitSignalEvidence` class, and its attribute id is the rc id.

## Red, then green

Red at `a17b97ba4555e2ca34129a6af22c05bb75ecab88`, filter of the three new methods: total 3, failed 3, succeeded 0, 48s 842ms. Lease `ea531a2f-4c8a-4fcc-a7d3-92743f77d06b`. TRX `/tmp/cbea-red-results/cbea-red.trx`.

- `Migration_list_uses_the_rc_exit_signal_id`: expected `20261030141651`, saw `20261031180000`.
- `Rc_ledger_upgrades_to_head_and_applies_card1610`: Postgres 42701, column `ExitSignal` of relation `RunAttempts` already exists, from `Migrator.MigrateAsync`.
- `Master_ledger_applies_rc_exit_migration_out_of_order`: head applied `20261031180000` in order, so history lacked `20261030141651`. That failure is an assertion miss.

Green at `20f0801bdbcb50fc9e0ea7699fb530a7aef6e667`:

- Id tests: total 3, failed 0, succeeded 3, skipped 0, 48s 108ms. Lease `5571e807-c698-41c3-9427-4537e4b881b4`.
- Combined suite: total 25, failed 0, succeeded 25, skipped 0, 2m 31s. Lease `f6a23349-17fd-4053-8727-3d9ba532e66a`. Persistence 8, id 3, schema snapshot 3, roster repair 3 (Explicit, Costs, Mirror), census 5 (C1404, C1577, C1584, C1410, G2865), NativeLinuxPolicyWitnessTests 3.
- Policy witness 3/3 in SessionRunner.Tests (lease `05478693-2667-4a0e-ba88-a3483752b972`), PtyHost.Tests (`9c91503c-dbf1-4bbb-9502-39d2cdda552c`), and Agents.Pty.Tests (`c85b8c59-f35a-450a-a23b-38ea17c152bc`). Antiphon.Tests covered the fourth suite in the combined run.

## Mutant

Uncommitted. Removed the `ExitStopIntentJson` `AddColumn` from Up of `server/Migrations/20261030141651_AddSessionExitSignalEvidence.cs`. Fresh output `bin-cbea-mut/`. Build lease `54487c13-4c98-432d-aaf3-bc663d0ef875`, 0 errors, 1408 warnings.

- Card1619ExitPersistenceTests: total 8, failed 8, succeeded 0, 1m 08s. Lease `cb8bba0c-ef64-4a63-92a3-be63bf7e93a2`. Every failure is Postgres 42703, column `ExitStopIntentJson` of relation `AgentSessions` does not exist.
- TestSchemaSnapshotTests on that same dll: total 3, failed 0, succeeded 3, 2m 05s. Lease `2e7d52a8-a3d7-4ff6-af1a-1527cc07512a`.

Restored with `git checkout HEAD -- server/Migrations/20261030141651_AddSessionExitSignalEvidence.cs`. The mutant was not committed. `ExitStopIntentJson` is back in Up and Down.

## Other checks

- `dotnet ef migrations has-pending-model-changes` (tool pin 9.0.20, lease `1d5c01a0-d31f-4b87-b559-e2870b1cbc52`): "No changes have been made to the model since the last migration." Exit 0. Ran on the committed model before the mutant edit.
- Land graph `-Check`: approved, count 69, phase pass2, digest `E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806`. `-Update` was not run. Canary directory was absent at cleanup.
- `check-evidence-diff` base `5aea2708fca4e196ae1915b6101c5287cb8c7cea` head `02de28eb0b324e20a33ff8a2a174c87d4e549cd7`: commits 130, entries 7, violations 0.
- `git merge-tree --write-tree origin/master HEAD` after fetch: `origin/master` still `5aea2708fca4e196ae1915b6101c5287cb8c7cea`. Clean tree `0e9f72a77eeadcf7a83fa7d5da5157c7381ec2d3`. No merge was made.
- Docs `02de28eb0b324e20a33ff8a2a174c87d4e549cd7` ports the four rc investigation records (c1379, c514, two reportbudget files) from `origin/rc/2026-10-27`.
- Alternate `bin-cbea-*` and `bin-land-audit` directories (135) were removed with literal paths. `antiphon_pgdata` was not touched.

## A database that already holds 20261031180000

None is expected. The reviewer's read-only desktop check held neither id, and server2 runs no Postgres. No master database was found with `20261031180000` applied. If a scratch or qualification database has that history row, replace it with `20261030141651_AddSessionExitSignalEvidence` before starting this build, and keep a single row. Leave Up as the four column adds. An idempotent Up would still fail `scripts/deploy-local.ps1` because the rc id would look database-only.

## Publication

This task does not tag, publish, or cut an rc. The brief forbids tags and publish. The next stage is a short Final review of D-1, then land to master. A later rc cut is a separate step after that land.
