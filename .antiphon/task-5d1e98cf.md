# CARD-1819 Review round 1 (task 5d1e98cf)

Subject: `origin/feat/card-task-43bdb0ea`, Code owner task 43bdb0ea (Grok author). Reviewed tip
`025fc42bf4a8f984e48c3a7808fa958d3b07b8fc`. Its tested code tip is `669d0cf862a152b8fe6b31b57a5db69550f41fd8`, and
the tip adds only the Code report. Base `9e050cf9600aef15b77d67957fc0cd68826d6b35`. Master `24ee7cb610ece2b1148e68bc232fa58453ebcb4a`.

Verdict: **2 blockers** (D-1 and D-2). Both make a Linux Merge row fail when the new rows select
classes that cannot pass on Linux. Measurements, the floors, the witnesses, and the guards are clean.

## Blockers

### D-1: `e2e-land-delivery` puts a Windows-only E2E class on the Linux Merge e2e row

- **Where:** `tests/impacted-classes.json` kind `e2e-land-delivery`. The Code pins it at
  `ImpactedClassesSelectorTests` (the C1801 `AgentTaskLandNotificationService.cs` path test, new
  `("tests/Antiphon.E2E", "/*/*/(AgentTaskLandDeliveryE2ETests*)|(LandDeliveryFixtureProbeTests*)/*")`
  filter row).
- **Failure:** `-MergeSubset -Path server/Application/Services/AgentTaskLandNotificationService.cs`
  now gives `CP-merge-subset-e2e /*/*/(AgentTaskLandDeliveryE2ETests*)|(ContractSnapshotTests*)|(LandDeliveryFixtureProbeTests*)/*`.
  `ae5b545d9` gives the same row. The Merge process says a `windows: required` row's "Linux row still runs"
  (`docs/orchestration-loop.md:701`). `LandDeliveryFixture.InitializeAsync` begins with
  `OperatingSystem.IsWindows().ShouldBeTrue()` (`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:59`).
  So every `AgentTaskLandDeliveryE2ETests` method fails on server2. The Code/Review non-merge selection
  for any of the four globbed files gets the same Linux E2E filter.
- **Why:** these four files (`AgentTaskLandNotification*.cs`, `LandNotificationReconcileQueries.cs`,
  `AgentTaskLandMonitorService.cs`, `AgentTaskLandNotificationHostedService.cs`) belong to the fixed
  land-notification family. Every batch that touches them would have a red Linux Merge row that no fix
  can turn green. The source note says so too: "It cannot run on server2. A pre-land check on Linux can
  only be the fixture probe."
- **Fix:** keep `AgentTaskLandDeliveryE2ETests` off every Linux `filters` row. Either add a Windows-only
  selector form, with an emitter, witness, and doc line (for example `"windows": "only"`, emitting only into
  `windowsFilters`), or drop the class from the table. If you drop it, keep `e2e-land-delivery-probe` and
  record the E2E class as owed on the Windows lane in the Merge process text. Repin the C1801 path
  assertion and the C1819 witness to the chosen shape. Add a witness that the Linux `filters` never name
  `AgentTaskLandDeliveryE2ETests`.

### D-2: `dpapi-protected-data` puts Linux-skipping classes on the Linux Merge tests row

- **Where:** `tests/impacted-classes.json` kind `dpapi-protected-data`. The triggers are `scripts/capability.ps1`,
  the two class files, and any added `tests/**` line containing `ProtectedData`.
- **Failure:** `-MergeSubset -Path scripts/capability.ps1` adds `(CapabilityScriptTests*)|(DelegateScriptCapabilityTests*)`
  to `CP-merge-subset-tests`. On Linux, `CapabilityScriptTests.issue_does_not_print_the_token_and_dpapi_round_trips`
  calls `Skip.Test` (CapabilityScriptTests.cs:24-25). The five `WriteBlob` methods of `DelegateScriptCapabilityTests`
  also call `Skip.Test` (CARD-1814). A checkpoint row with skipped>0 is not a clean certificate
  (`docs/testing-and-build.md:664`). The validator refuses it as `row_failed`. So the whole fixed
  `CP-merge-subset-tests` row fails validation on server2. The literal also matches the table itself.
  The selector for this review's own diff (`-Range 9e050cf960..HEAD`) selects `dpapi-protected-data`
  because the new JSON line `"ProtectedData"` is an added `tests/**` line. A
  `tests/linux-test-roster.json` regeneration that touches those entries does the same.
- **Why:** none of the existing `windows: required` classes skips on Linux. These two are the first.
  The rows would turn a capability-script batch's Linux Merge into a validation failure, not into evidence.
- **Fix:** use the same Windows-only form as D-1 for this kind. Alternatively, move these classes into
  their own Linux method row that excludes the skipping methods, or keep only `windowsFilters` for them.
  Add a witness that the Linux `filters` for `scripts/capability.ps1` stay free of Linux-skipping methods.
  Consider limiting the `added` glob to `tests/Antiphon.Tests/**/*.cs`, so that the table and the roster
  JSON stop self-selecting.

## Checks (all as asked)

**(1) Merge floors and cost.** I ran `-MergeSubset` with the master table (`git show origin/master:tests/impacted-classes.json`,
via `-Table`) and the tip table on 6 inputs. Every count and filter matches the Code report:

| input | master classes/filters | tip classes/filters | tip windows |
|---|---|---|---|
| no range | 15 / 5 | 15 / 5 (identical filters) | 0 |
| ff7cceaf | 67 / 7 | 67 / 7 (identical) | 0 |
| dcb973d8 | 15 / 5 (tests row 5) | 18 / 5 (tests row 8) | 0 |
| a54a6d4f | 16 / 6 | 17 / 6 | 0 |
| ae5b545d9 | 67 / 7 | 74 / 8 (land-4 = 2 classes) | 2 |
| 0f76e7b0a9 | 19 / 5 | 21 / 5 | 1 |

Land-1 and land-2 are byte-identical in every range. No range grows past what the Code reported.
`import --plan` of the tip `ae5b545d9` Merge rows (8 rows) and the `dcb973d8` Merge rows (5 rows) gave
0 warnings.

Cost of the added classes:
- `LiveLaneRunnerTests` + `NightlyCoreTests`: 5 tests, 66.6 s tests wall, 78.7 s row (CP-R3), deterministic.
  The `nightly-live-lane` globs (`scripts/lib/nightly-*.ps1`, `Antiphon.Nightly/**`, `test-execution-policy.json`,
  `test-nightly-core.ps1`) select them. A plain `tests/**` diff does not. They do go red when a protected
  live-lane input changes without a repin, and that is the miss the operator note wanted caught. Acceptable.
- `TestLaneCategoryGuardTests` on every `tests/Antiphon.Tests/**/*.cs` diff: it is Unit, reads source, and
  is already in the guard batch. It adds seconds. Acceptable.

**(2) Kinds.** All 59 class and method names in the table resolve to real classes and methods under
`tests/` (git grep). `-Audit` reports ok with no empty globs and no missing classes. No new kind uses
`derive`. The cancel-note callers list is complete. Git grep for `LandNotificationState.Canceled` or
`"queue_canceled_unconfirmed"` under `tests/`, on the tip and on master, finds 8 classes, all in
`land-notification`. Glob scope is right apart from D-1 and D-2. `tests/Antiphon.E2E/**/*.cs` maps to one
method. The windows markers are on pty-host-fixture, migration-sql, dpapi, and e2e-land-delivery.
Card1277 and the two migration classes have no Linux skip.

**(3) Witnesses.** The 12 `C1819_*` witnesses are fake `-Path` diffs. The fixed mutant
(`DroppedC1819Selection`) holds no needle, and `ShouldNotBe(ran.Stdout)` guards it. CP-R1 passed 43/43 with
`Should.Throw` around it, so each mutant makes the positive assertion fail.

**(4) Rows left out.** These are fine: spawn-limit-rosters is unchanged, there is no write-behind
blanket, and `RemoteWorkspacePreparerTests` is not added. No docs changed. No contract changed apart from
D-1 and D-2.

**(5) Runs.** All are at `025fc42bf4a8f984e48c3a7808fa958d3b07b8fc`, in checkpoint run
`20261109-214403-b81b`, verdict GREEN. `validate` gives `CHECKPOINT SOURCE VALID rows=4`.
- CP-R1 `/*/*/(ImpactedClassesSelectorTests*)|(MergeHighRadiusSubsetTests*)/*`: executed 43, passed 43
  (ImpactedClassesSelectorTests 331 s, MergeHighRadiusSubsetTests 64 s).
- CP-R2, the full documented guard batch (9 classes including `TestLaneCategoryGuardTests`): 78/78.
- CP-R3 `(LiveLaneRunnerTests*)|(NightlyCoreTests*)`: 5/5.
- CP-R4, the `C1677TempFamilyRegistryTests` impacted row for this diff (SessionRunner.Tests): 3/3.
- Not run on Linux: the `dpapi-protected-data` impacted row for this diff. It would skip (D-2) and
  remains owed on Windows.
- `check-evidence-diff` base..HEAD: exit 0, 4 commits, 1 entry, 0 violations.
- `git merge-tree --write-tree origin/master HEAD`: exit 0, tree `b5888907aec85285f1fcfa47c0649ebd794f913b`.
- `audit-land-verification-graph.ps1 -Check` (lease d93f51f5, held 51 s): exit 0, digest `3ED4A38C...EB46`, count 75.
- No policy JSON changed. The diff vs base is only the table, the two selector test files, and the Code report.

## Advisories (cards, never land holds)

- A-1: `e2e-source-roster` lacks the landing-split-budget note's extra mappings,
  `scripts/fixtures/nightly/c1584-e2e-source-roster.txt` and `scripts/lib/nightly-class-costs.json`, to the
  same method. The note is on master and was not at the base.
- A-2: on its own, `LandGraphEvaluationTests` (155 tests, about 10 min) forms a `land-N` row larger than the
  ~120-test land row. The importer counts classes, so it does not warn. Give that row a 10+ minute
  EstimatedMinutes.
- A-3: `AgentTaskDispatcher.cs` in `land-notification` raises a dispatcher-only Merge from 16 to 20 classes
  and from 6 to 7 filters. The extra four classes include PreDispatchFailureDeliveryTests (45-65 s). This is
  justified by the note, but the file is hot.
- A-4: `AssertSelected` matches the needle anywhere in the raw JSON, not in a `filters` row.

## Owed (pending)

- Windows lane: Card1277PtyHostFixtureTests, Card1773TranscriptPromptStampMigrationTests,
  TranscriptHotPathMigrationTests, CapabilityScriptTests, DelegateScriptCapabilityTests, and AgentTaskLandDeliveryE2ETests.
- The PCs listed by the Code stay pending for SourceLanding Mutation.
