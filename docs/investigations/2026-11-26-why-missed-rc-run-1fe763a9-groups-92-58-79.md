# Why Windows groups 92, 58, and 79 missed historical task seeds

All five exact migration tests passed against test PostgreSQL on the master SHA fetched before
the run, `608e21e47745f5e7e78a615101237f6dd19d4eee`. Group 86's existing repair
`ea6c2abc77eb55d0b5995291aeb5f0dd691d0d7b` covers all five, including the group 89 recheck.

## Introducing change and repair

Windows Full run `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267` reported
`DbUpdateException` at the historical seed saves in groups 92, 58, and 79. The same four cases
passed in `b8cbfb24` at `2fd61b643ea4231fa732cd4969b5284d43b33f39`.

The caller-report wake change mapped three receipt properties on `AgentTask` in
`7fbe6b71926da6862f5bd0296f77b22384f53705`. Migration `20261125151517_AddCallerReportSettlementReceipt`
in `e639d421081165757449374af78ee7a162cc915d` added the columns. Merge
`fccb17b2b2d755ab0ebfdbbbad2b51cb8ef2be29` published both. The failing pin seeded
`AgentTasks` through the current EF model while the database was at
`20261106032116_AddRefOnlyParkWorkspaceSnapshot`. EF sent the receipt columns in the insert
even with null values. PostgreSQL `42703` on `AgentTasks.ReceivedReportCompletedAt` was
observed in the group 86 and 49 replays and is inferred for these Windows failures from the
seed locations and model/schema comparison. [Group 86 replay](2026-11-26-why-missed-group-86-predecessor-seed.md) · [Group 49 replay](2026-11-26-why-missed-rc-run-1fe763a9-group-49.md)

`git log -S CreateReversibleTipSeedContext` on the four test files and
`tests/Antiphon.Tests/TestHelpers/MigrationTestDatabase.cs` identifies `ea6c2abc` as both the
factory introduction and the change to all five call sites. The seed context limits the
`AgentTask` property set to the reversible tip's migration model. The migration steps and
upgraded assertions still use the current model.

## Review and selection gap

Interim Review `16611b04-cefb-495c-8c99-c09736e8011b` derived a 62-class migration roster and
executed the two-case C0824/RequestedModelAlias comparison. Final/Full Review
`ede19f30-78ba-4c49-8a27-93377614c75d` at `9218d74123abfec3cb79d8ddad96b45f4d689793`
ran retention and alias (29), two caller-wake receipt and claim cases, the six-class guard
(57), and two separate native caller-read cases. Merge added host-load and migration-chain
coverage. Those selections omitted these Integration methods under
`tests/Antiphon.Tests/Application`. Their dependency on the new receipt mapping was through
a shared EF insert on an older schema. [Executed Review and Merge selections](2026-11-26-why-missed-rc-run-1fe763a9-group-49.md#approving-reviews-and-merge)

Both earlier source sweeps list all four newly reported methods as already migration-bound.
The group 87 sweep lists the classes and the count for the two decision cases. The group 89
sweep identifies the exact mirror and workspace methods and both decision cases. They were
source checks on master after `ea6c2abc`. The late Windows failures were executions at the
earlier rc pin. The group 89 catalog witness compares PostgreSQL column additions against a
reviewed table/column inventory. A fixture reverting to the current EF seed could leave that
catalog comparison green because the receipt columns are already reviewed. Exact upgrade
execution checks whether the individual writers still work. [Group 87 sweep](2026-11-26-why-missed-rc-run-1fe763a9-group-87.md#sweep) · [Group 89 sweep](2026-11-26-why-missed-rc-run-1fe763a9-group-89.md#derived-seed-sweep)

## Cheapest pre-land check

For `server/Migrations/**`, `server/Domain/Entities/AgentTask*.cs`, and
`server/Infrastructure/Data/AppDbContext*.cs`, follow historical schema factories to their
task inserts and select the affected exact migration methods. The checkpoint filters below
use `/*/Antiphon.Tests.Application/<class>/<method>` and each runs one test.

| Row | Windows group | Class | Method | Result |
| --- | --- | --- | --- | --- |
| CP-1 | 92 | `CardWorktreeMirrorBindingTests` | `C1032_SchemaLegacyUpgrade` | Passed |
| CP-2 | 58 | `AgentTaskInternalDecisionMigrationTests` | `Old_dispatch_event_upgrades_with_no_invented_session_binding` | Passed |
| CP-3 | 58 | `AgentTaskInternalDecisionMigrationTests` | `Card0407_V05_pre_feature_row_upgrades_to_a_null_policy_with_legacy_fields_intact` | Passed |
| CP-4 | 79 | `WorkerWorkspaceDefaultMigrationTests` | `C458_DownThenUpPreservesLegacyWorkspace` | Passed |
| CP-5 | 89 | `OutputDistillationMigrationTests` | `Upgrade_preserves_historical_outcomes_and_nullable_deadlines` | Passed |

The checkpoint tool built once and reused the verified build for four rows. All five slots
were granted. All five receipts validated at the tested SHA, and the TRX files confirmed the
exact executed identities and PostgreSQL fixture startup. The receipt archive
`/work/evidence/task-da0c421c/master` contains each row's `report.json` and `run.trx`, the
build-source stamp, and `validation.log` with the original CHECKPOINT lines and file hashes.
Master advanced during execution to `3a7d0116f4e0a3c1e273312f77a05db541f3a991`. A path
comparison found the four test files, the seed and database fixture helpers, the server
migrations and model, and the checkpoint tool byte-identical between the two SHAs. The
receipts are bound to `608e21e47745f5e7e78a615101237f6dd19d4eee`.

## Ordered rc backport

All six patches applied in sequence via `git apply --cached --3way` to a temporary index
initialized from `rc/2026-12-06` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267`.
Each step had no unmerged entries and the final staged diff passed `git diff --cached --check`.
The final tree was `62a4462b645d868d4955c96dcd0a04572666bce9`. The four ported test
files matched the verified master files byte for byte.

1. `ea6c2abc77eb55d0b5995291aeb5f0dd691d0d7b` binds the historical seeds for groups 86, 49, 87, 66, 89, 92, 58, and 79.
2. `e23c48ec9aca8fcd52bc3fd7544b30aada0b7d13` adds the group 49 task-world witness.
3. `c1b91e9d23e5c3ade4c23e872c12810fc17128b2` binds the group 49 landing harness and extends the factory from step 1.
4. `585258802ac6be0a55c6c2235cad5c5833f14d37` scopes the group 141 live-gate dispositions by method.
5. `793f96e527d1942faa6c62456766fe92884a3a30` shares the caller-wake overlay with the group 141 offline witness.
6. `e9f644e4f9d13e1e3c312658c65c69977b0a196a` is the landed index refresh for the two group 49 test-helper edges. The task-branch refresh `b70cc733967b9444a28fd5e20e18857ce86e71a4` also carries two unrelated release-script edges.

The preflight transcript is `/work/evidence/task-da0c421c/rc-backport-index.log`. Regenerate
the derived index against the integrated rc tree and qualify all nine named groups on the
next Windows candidate. [Group 141 inventory failures](2026-11-26-why-missed-group-141-live-gate-disposition.md)

## Exemption

Same historical seed failure as [group 86](2026-11-26-why-missed-group-86-predecessor-seed.md).
