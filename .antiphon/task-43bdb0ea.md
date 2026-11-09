# CARD-1819 selector rows

Branch `feat/card-task-43bdb0ea`. Worktree `/work/worktrees/task-43bdb0ea`. Base `9e050cf9600aef15b77d67957fc0cd68826d6b35`.

## Commits

- Red: `75d0f154c6193382d9605eba99c06f49ae6fff8a` — twelve fake-diff witnesses, kinds absent.
- Fix: `bf084a0d51aec7a0b5c5c8a71bae79f7928d84ca` — `tests/impacted-classes.json` plus the exact assertions those rows change.
- Mutant: `669d0cf862a152b8fe6b31b57a5db69550f41fd8` — the witness replacement no longer contains the selected name. Tested tip for the green rows.

`tests/test-execution-policy.json` was not edited. policyHash and PinnedInputDigest were not refreshed. `docs/testing-and-build.md` and the Merge process table were not edited: the no-range fixed filters are still the documented strings.

## What was added

New kinds: `pty-host-fixture` (windows), `migration-generated-sql-text` (windows), `repository-lease-reflected-constructors`, `land-graph-precheck`, `land-graph-audit-project`, `test-lane-category`, `e2e-source-roster` (method `C1514LandingSplitBudgetTests/C1584_E2EExpandedBudget`), `dpapi-protected-data` (windows, added literal `ProtectedData`), `e2e-land-delivery` (windows) and `e2e-land-delivery-probe` (Linux), `temp-family-registry` (added literal `TestSessionLogRoot.Create(`), `nightly-live-lane`.

`land-notification` also globs `server/Application/Services/AgentTaskDispatcher.cs` and lists `PreDispatchFailureDeliveryTests`, `AgentTaskLandReceiptTests`, `ExpectationDebtTests`, and `LandReceiptScanCacheTests`.

`spawn-limit-rosters` was already applied by CARD-1807 and was left unchanged.

## Derive was not used

The pre-dispatch note's derive patterns are global in `scripts/impacted-classes.ps1`: one derived set is shared by every kind that has `derive`. A second derive block made a migration `-MergeSubset` grow an eighth filter row (the pinned migration range requires 7). The four cancel-note callers are explicit classes instead. After that, `ff7cceaf^..ff7cceaf` is still kinds `migrations`, 67 classes, 7 filters, land-1 and land-2 unchanged, windows 0.

## Split on purpose

`LandGraphEvaluationTests` is selected only from `tools/Antiphon.LandGraphAudit.Core/**` and `scripts/audit-land-verification*.ps1`. `LandGraphAuditProjectInstanceTests` is the `Antiphon.sln` / `tests/Antiphon.Tests/Antiphon.Tests.csproj` / `tools/Antiphon.LandGraphAudit/**` row. The evaluation class is the slow one; it does not join an ordinary solution edit. `tools/Antiphon.LandGraphAudit/**` does not match the `.Core` tree.

## Not added

- Write-behind `tests/**/*.cs` to the whole classification guard batch. The note's proposal is a pre-push hook card. The blanket would put that batch on every test diff, including the quiet range. `TestLaneCategoryGuardTests` is the C1816 row, and the guard batch already runs it.
- Whole `RemoteWorkspacePreparerTests`. The load-flakes note proposes a flake ledger, not a selector row. The class name does not contain `Land`, so it would enlarge the non-land merge row whenever the preparer changes.
- No separate `landing-split-budget` note was on disk. `e2e-source-roster` covers `C1584_E2EExpandedBudget`.

## Merge subset before / after

No-range: still 15 classes and the five documented filters. Tests filter, land-1, and land-2 are the pinned strings. Floors stay 44 / 34 / 104 on those fixed rows.

Quiet `dcb973d8`: before kinds `linux-only-policy,timing-labels`, 15 classes, 5-class tests filter. After kinds `linux-only-policy,nightly-live-lane,test-lane-category,timing-labels`, 18 classes. Tests filter adds `LiveLaneRunnerTests`, `NightlyCoreTests`, `TestLaneCategoryGuardTests`. Land-1 and land-2 unchanged. Windows 0. Filter count 5.

Remote `a54a6d4f`: before 16 classes, kinds `remote-scripts`. After 17 classes, kinds `remote-scripts,test-lane-category`. Tests filter adds `TestLaneCategoryGuardTests`. Land-1 and land-2 unchanged. Six filters.

Migration `ff7cceaf`: unchanged, 67 classes, 7 filters.

`ae5b545d9` (pre-dispatch): before kinds `land-notification,migrations`, 67 classes, 7 filters, windows 0, the four callers absent. After kinds also `e2e-land-delivery`, `e2e-land-delivery-probe`, `migration-generated-sql-text`, `test-lane-category`; 74 classes; 8 filters. Land-1 and land-2 stay the documented strings. The two new Land-named classes (`AgentTaskLandReceiptTests`, `LandReceiptScanCacheTests`) start land-4 after the existing later land group fills land-3. Windows: `AgentTaskLandDeliveryE2ETests`, plus `Card1773TranscriptPromptStampMigrationTests` and `TranscriptHotPathMigrationTests`.

`0f76e7b0a9` (lease): before 19 classes, kinds `server-git-start-sites,server-image-push-credentials,server-repository-mirror,spawn-limit-rosters`, `RepositoryMutationLeaseTests` absent, 5 filters. After 21 classes, also `repository-lease-reflected-constructors` and `test-lane-category`, `RepositoryMutationLeaseTests` present, still 5 filters. Land-1 and land-2 unchanged.

## Tests

Red witnesses at `75d0f154`: `/*/*/ImpactedClassesSelectorTests/C1819*`, TRX `/tmp/card-1819-c1819-red/c1819-red.trx`, 12 failed, 0 passed.

At `bf084a0d51` (lease `c2f5cd44-9453-4baa-bc58-12006f8c823a`, waited=0s, held=467s, maxcpucount=6): `/*/*/(ImpactedClassesSelectorTests*)|(MergeHighRadiusSubsetTests*)/*`, TRX `/tmp/card-1819-selector/selector.trx`, total 43, passed 31, failed 12. The 12 failures were the in-test mutant: `MissingC1819` + the class name still contained the needle, so `ShouldContain` did not throw. The positive selection had already passed. Build before that run: lease `c31acbbd-8e8f-42d6-9e84-8c74dcc23b40`, waited=0s, held=171s, 0 errors, 1989 warnings.

At `669d0cf862` the twelve witnesses were rerun (lease `7e89d903-0731-4f41-8010-fd07e4af933b`, waited=0s, held=133s): TRX `/tmp/card-1819-c1819-green/c1819-green.trx`, total 12, passed 12, failed 0, duration 2m 09s. The other 31 were not rerun; their source is unchanged by the mutant commit. Rebuild: lease `c18fa5ed-6780-42bf-a890-f6c7abd07d06`, waited=0s, held=184s, 0 errors, 1989 warnings.

Guards at `669d0cf862` (lease `b63bd878-4591-439e-b950-6b21c05bc43c`, waited=0s, held=106s): the eight brief classes plus `TestLaneCategoryGuardTests` (the impacted row for this diff and the documented guard batch). TRX `/tmp/card-1819-guards/guards.trx`, total 78, passed 78, failed 0, duration 1m 42s.

`scripts/check-evidence-diff.ps1 -BaseRef 9e050cf9600aef15b77d67957fc0cd68826d6b35 -HeadRef HEAD`: exit 0, commits=3, entries=0, violations=0.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`: exit 0, tree `176db0088b3992dca3954df059b1cb46db664f3f`. `origin/master` is `24ee7cb610ece2b1148e68bc232fa58453ebcb4a`. No conflicts. Not merged.

Audit of the table: ok, no empty globs, no missing classes.

## Owed on Windows

Not run here. A Linux run of these filters is not the owed lane.

- `Card1277PtyHostFixtureTests` (`pty-host-fixture`)
- `Card1773TranscriptPromptStampMigrationTests` and `TranscriptHotPathMigrationTests` (`migration-generated-sql-text`)
- `CapabilityScriptTests` and `DelegateScriptCapabilityTests` (`dpapi-protected-data`)
- `AgentTaskLandDeliveryE2ETests` (`e2e-land-delivery`)

`LandDeliveryFixtureProbeTests` is the Linux half of that pair and is not a windows row. It was selected, not executed as a class.

## Positive controls

Pending for method-scoped SourceLanding Mutation. The in-test string replacement is the suite's existing mutant, not a Mutation cycle. Each control is: delete the kind, drop `windows: required`, drop the method, drop the `ProtectedData` literal, drop the `TestSessionLogRoot.Create(` literal, or drop one of the four explicit land-notification classes. The matching `C1819_*` witness, or the exact `C1801` / quiet / remote filter assertion, goes red.

Restart: none.
