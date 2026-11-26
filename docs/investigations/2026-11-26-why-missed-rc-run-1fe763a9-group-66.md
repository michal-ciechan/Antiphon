# Why Windows group 66 missed the C883 legacy-row seed

`LandRequestWriteDiagnosticTests.C883_MigrationPreservesLegacyRows` passed on Linux at master `f2c9422cda9bdfd87752a62554e847314938d363`, which already contains the group 86 seed repair `ea6c2abc77eb55d0b5995291aeb5f0dd691d0d7b`. The exact build-slot selection ran one test against test PostgreSQL with 1 passed, 0 failed and 0 skipped. The TRX records 81.681 s for the case and is stored at `/work/evidence/task-78e9edc0/c883-master/run.trx`.

## Failure and repair

Windows Full run `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267` failed in 22.4 s with a `DbUpdateException` from the `SaveChangesAsync` at test line 180. The same case passed in `b8cbfb24` at `2fd61b643ea4231fa732cd4969b5284d43b33f39`. The Windows excerpt ends before the inner exception.

At line 180 the fixture inserts its owner into `AgentTasks` using the current EF model, while the database is at `20261106032116_AddRefOnlyParkWorkspaceSnapshot`, the last reversible migration. Once the task is seeded, the test rewinds to `AddReviewSourceClean` and inserts the legacy request into `AgentTaskLandRequests` with explicit predecessor SQL. The caller-report wake change mapped `ReceivedReportCompletedAt`, `ReceivedReportPromptSequence` and `ReceivedReportSessionId` in `7fbe6b71926da6862f5bd0296f77b22384f53705` and added their columns in migration `20261125151517_AddCallerReportSettlementReceipt` in `e639d421081165757449374af78ee7a162cc915d`. The landing was `fccb17b2b2d755ab0ebfdbbbad2b51cb8ef2be29`.

EF includes those mapped properties in the insert at the failing pin. The schema at the reversible tip predates all three columns. PostgreSQL `42703` on `AgentTasks.ReceivedReportCompletedAt` is the inferred C883 cause from the failing call site and the reproduced error for the same seed pattern in group 86. The group 86 repair changes the C883 task seed to `CreateReversibleTipSeedContext`, which takes its task property set from that migration model. The migration under test and the explicit legacy request insert are identical at the two pins. [Group 86 investigation](2026-11-26-why-missed-group-86-predecessor-seed.md)

## Selection gap

The recorded Review and Merge runs for the caller-report wake change selected retention, alias, caller-wake, guard and migration-chain cases, omitting C883. CARD-2082 lists both the class and the exact method under fixtures with a current-tree assertion to extract or isolate, citing its use of compiled EF migration IDs. The group 87 source sweep also included `LandRequestWriteDiagnosticTests` among the already repaired `AgentTasks` seeds. That sweep followed historical `AgentTasks` writes and verified the HostCleanup case. C883's failing table was therefore in its source inventory, and C883 needed its own execution. [Review and Merge selections](2026-11-26-why-missed-rc-run-1fe763a9-group-49.md#approving-reviews-and-merge) · [CARD-2082 inventory](../superpowers/plans/2026-11-26-card-2082-invariant-inventory.md#fixtures-with-a-current-tree-assertion-to-extract-or-isolate) · [Group 87 sweep](2026-11-26-why-missed-rc-run-1fe763a9-group-87.md#sweep)

## Verification and rc backport

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c883-master-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c883-master-test -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/LandRequestWriteDiagnosticTests/C883_MigrationPreservesLegacyRows' --report-trx --report-trx-filename run.trx --results-directory /work/evidence/task-78e9edc0/c883-master
```

`rc/2026-12-06` still points to the failing pin and needs the existing group 86 commit before group 66 is requalified on Windows. A per-commit `git merge-tree --write-tree --merge-base=ea6c2abc^ origin/rc/2026-12-06 ea6c2abc` found no conflicts.
