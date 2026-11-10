# CARD-1820 Selector v2: derived reverse-reference index

Plan for the pre-land net. CARD-1784 stays the Linux post-land backstop. This card does not add a pre-push hook, does not edit stage bundles, and does not change `tests/impacted-classes.json` schema 1.

Base: `9e050cf9600aef15b77d67957fc0cd68826d6b35`. Runner defaults were read on 2026-11-09 (`GET /api/runner-defaults` revision 2; `GET /api/session-runners` returned three runners). Checkpoints name the lane. They pass no `-Runner` and no `-Platform`.

No slice adds a session that waits for input. Parking and CARD-1083 do not apply.

The only CARD-1782 plan file on this tree is `docs/superpowers/plans/2026-11-08-card-1782-round3-repair.md`. CARD-1783 and CARD-1801 landed as the script and the Merge section below, without a plan file in `docs/superpowers/plans`.

## Ground truth

| The card assumes | What the code does | Consequence |
|---|---|---|
| Every high-blast row is hand-written. | `tests/impacted-classes.json` schema 1, 18 kinds. `scripts/impacted-classes.ps1` refuses an unknown root or kind property (`:275-277`, witness `malformed_table_is_refused`). | v2 state lives in sibling files. The hand table stays schema 1. |
| A test that names a path or type the table omits is invisible. | Match is glob or `added.literals` only (`:670-690`). The migrations `derive` scan is the only generated set, and only after the migrations kind matches (`:708-724`). | `dff78584d6` changes `LandingGit.cs` and selects four other classes, not `RepositoryMutationLeaseTests`. |
| `added.literals` can carry risky tokens. | Two kinds use it: `timing-labels` (`BeginPhase(`, `BeginFixturePhase(`) and `spawn-limit-rosters` (`ParallelLimiter<`). `linux-only-policy` also sets `removed: true`. | New tokens go in `tests/keyword-triggers.json`, same added-line walker. |
| Rosters go stale when a test is added. | `c1584-e2e-source-roster.txt`, `ProcessSpawnLimitTests.StrictClasses`, `tests/linux-test-roster.json`, `tests/test-execution-policy.json`, and the selector table have no registry. | `fabda59573` selects `ProcessSpawnLimitTests` and not `TestLaneCategoryGuardTests`. |
| Reports should show what ran. | The script prints `kinds`, `classes`, `filters`, `windowsFilters`. Nothing requires a Code, Review, or Merge report to paste them. | S4 pins the paste in the owner docs. |
| A why-missed note's proposed row is checked. | `why-missed-template.md` is prose. `WhyMissedWriteUpContractTests` pins five phrases and does not read JSON. | S5 adds a ledger block and a witness. Historical notes stay grandfathered. |
| Stage bundles can grow a receipt sentence. | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` caps stage-review at 2600 and other stage bundles at 2500 (`InstructionBundleTests.cs:927-932`). Measured bytes: `stage-code.md` 2477, `stage-review.md` 2579. | Bundles stay byte-identical. Headroom is 23 and 21. |
| The selector walk is cheap enough to parse every test on each call. | One `-Range` call reads every `tests/**/*.cs` (2574 tracked files) and took 11.4-24.1 s (n=19, median 17.0 s) on this host. | The Roslyn index is committed. The call reads JSON. |

## Intended behaviour

### Before

```
git diff --name-only
  -> kind globs and added.literals (impacted-classes.ps1:670-690)
  -> hand classes, hand methods, migrations derive (455-500, 708-724)
  -> one treenode filter per project (943-967)
-MergeSubset unions the fixed 14 classes and two ReleaseGatePlatformTests methods (862-882)
```

A changed test file selects a class only when some kind glob or literal matches. `57bbfc99d1` touches only `Card1773TranscriptPromptStampMigrationTests.cs` and selects nothing.

### After

```
hand match (unchanged)
  union classes declared in a changed tests/**/*.cs file
  union classes from tests/derived-impact-index.json whose edge hits a changed path
  union tests/keyword-triggers.json hits on added lines
  union tests/roster-registry.json hits
  -> same filters and windowsFilters shape
  -> plus derivedClasses, keywordIds, rosterIds, skippedByCap
```

Hand kinds, the fixed Merge subset, the land-class split under `-MergeSubset` only, schema 1, and the empty-glob audit stay. The declaration edge and the index are whole classes, except the one method edge in D-6. A whole class still wins over a method row of the same class.

`skippedByCap` lists a changed path whose edges were dropped at generation. The classes are not selected.

### Files

| File | Change |
|---|---|
| `scripts/impacted-classes.ps1` | After the match loop (`:670-690`) and inside both payloads (`:918-960`), union the three sibling indexes. Class walk (`:455-500`) already records declarations; reuse it for the declaration edge. `Get-Project` (`:211-216`) assigns the project. |
| `tests/impacted-classes.json` | Unchanged. |
| `tests/derived-impact-index.json` | New committed index. |
| `tests/keyword-triggers.json` | New reviewed token list. |
| `tests/roster-registry.json` | New roster list. |
| `tests/why-missed-ledger-grandfather.txt` | Closed list of existing why-missed notes. |
| `tests/Antiphon.Tests/Checkpoints/SharedCheckoutSourceCensus.cs` | Unchanged. The index builder follows its parse-without-compilation pattern (`:48-49`). |
| `tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs` | Unchanged pins (`:47-85`). New behaviour is new classes. |
| `docs/testing-and-build.md` | Impacted-classes section (`:335-353`) gains the sibling files and the what-ran paste. |
| `docs/orchestration-loop.md` | Merge process (`:691-699`) names the paste. Why-missed section gains the ledger sentence beside the five items. |
| `docs/investigations/why-missed-template.md` | Ledger block. The five phrases stay. |
| `.claude/skills/antiphon-rc-qualification/SKILL.md` | Same ledger sentence in the why-missed paragraph the contract test slices. |
| `server/Bundles/stage-code.md`, `stage-review.md` | Unchanged. |

## Decisions

### D-1. Committed index, drift witness

The generator is an internal C# type in `Antiphon.Tests` using `CSharpSyntaxTree.ParseText` with no compilation. It writes `tests/derived-impact-index.json`. `DerivedImpactIndexTests` rebuilds in memory and fails when the bytes differ. The selector script only reads the file.

Rejected: generating inside every selector call. The current regex walk of 2574 files is already 11.4-24.1 s. A Roslyn parse on that path was not measured and stays off the call. Rejected: an uncommitted in-memory cache. The script's contract is no cache (`maximumStaleness` 0 relative to the files it reads). The witness makes a stale committed index fail before land.

### D-2. Edges

For each `*Tests` class under `tests/**/*.cs`:

1. Declaration. Computed at selection time from the diff, not stored. A changed test file selects the classes declared in it.
2. Path literal. A string literal of at least 8 characters that equals a tracked repo path, slashes normalized.
3. Type name. An identifier equal to a class declared under `src/`, `server/`, or `tests/` selects that class's file.
4. One hop. Path literals and dotted type names in a directly referenced type's file. No second hop.
5. Script string. A string containing a dotted name of a production class (`Antiphon.Server.Infrastructure.Git.LandingGit`) selects that class's file.

`RepositoryMutationLeaseTests` names `LandingGit` at `RepositoryMutationLeaseTests.cs:144` and writes `RepositoryLeaseCrashWorker.CrashScript` at `:577`. Both edges select it when `LandingGit.cs` changes. `C1677TempFamilyRegistryTests` names `RunnerTempSweep` (`C1677TempFamilyRegistryTests.cs:34`). `RunnerTempSweep.cs:72` stores the path `tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs`. The hop selects the test when that producer file changes. The `de3c599912` added line is the prefix `"c1778-trust"`, which is not the literal `TestSessionLogRoot.Create(`.

Rejected: a text scan for `WorkingDirectory =` or `Process.Start`. `SharedCheckoutSourceCensus` (`:9-11`) records that a text scan treats path reads and other types' `WorkingDirectory` as process starts. `C1810_repo_root_read_or_other_working_directory_is_not_that_use` is the witness that stays green.

### D-3. Cap 25 and an allowlist

A path with more than 25 test classes is omitted from the index and listed in `cappedPaths`. The selector reports it in `skippedByCap` and selects nothing from it. These paths are allowlisted even below the cap, because a hit selects a multi-minute class on an ordinary edit:

- `Antiphon.sln` (`LandGraphEvaluationTests.cs` and `LandGraphEvaluationScriptTests.cs` both contain that literal)
- `Directory.Build.targets`
- `Directory.Build.props`
- `global.json`

The allowlist does not apply to roster triggers. An `Antiphon.sln` edit still selects the shape guard (D-8).

Rejected: a cap of 4 to mirror the land-row warning. That would drop real single-purpose helpers. 25 is the generation-time bound; S1 measures the dropped set and records it in the index header.

### D-4. Keyword list

`tests/keyword-triggers.json`. The selector reuses `Get-AddedText` (`:649-657`) and `Test-LiteralLine` (`:660-667`).

| id | Added line under | Literal | Selects |
|---|---|---|---|
| `newline-generated-text` | `tests/**` | a line that contains `ShouldBe` or `ShouldContain`, and `\n"` or `\r\n"` | `GeneratedTextNewlineGuardTests` |
| `protected-data` | `tests/**` or `scripts/**` | `ProtectedData` | `DpapiPlatformGuardTests` |
| `session-log-root` | `tests/**` | `TestSessionLogRoot.Create(` | `C1677TempFamilyRegistryTests` |

Existing hand literals stay in the hand table. `ParallelLimiter<`, `BeginPhase(`, `BeginFixturePhase(`, and `[NativeLinuxOnly(` are not copied.

`GeneratedTextNewlineGuardTests` fails when an added assertion pins generated text to a hardcoded newline. That is the Linux catch for `57bbfc99d1`: the class itself passes on Linux, and the guard fails on the diff text. `DpapiPlatformGuardTests` fails when a `ProtectedData` call site has neither an `OperatingSystem.IsWindows()` skip nor a `platform: linux` exclusion row. Both guards are Unit source scans.

Rejected: a keyword on `[Test]` under `tests/Antiphon.E2E`. The c1584 roster owns that tree (D-7). Rejected: `public sealed class` as a keyword. The lane and classification rosters own it.

### D-5. Declaration edge is uncapped

Editing a test file selects the classes declared in that file, including a raw string the lane guard will also see. This is how `86902399c0` selects `CapabilityScriptTests` and how `a930059d8b` selects `NightlyCoreTests`. The cap applies to reverse edges, not to the file that declares the class.

### D-6. Land-graph method edge

When every changed path is under `tools/Antiphon.LandGraphAudit.Core/**` or matches `scripts/audit-land-verification*.ps1`, the derived hit is the method `LandGraphEvaluationTests/EvaluationBudgetRefusesSlowDotnet`. When the test file itself changes, the whole class is selected. The class is about 10 m 22 s (155 tests, serial under `ProcessSpawnLimit`, from the evaluation-budget note). The one method is about one minute there.

Rejected: always selecting the whole class. A tool-only edit would add ten minutes to every such Merge.

### D-7. Roster registry

`tests/roster-registry.json` lists each oracle, the tree it enumerates, the guard class, and the trigger (`added-file` or `added-class-line`).

| id | Oracle | Tree or inputs | Guard | Trigger |
|---|---|---|---|---|
| `c1584-e2e` | `scripts/fixtures/nightly/c1584-e2e-source-roster.txt` | `tests/Antiphon.E2E/**/*.cs` | `C1514LandingSplitBudgetTests` | added file |
| `lane-category` | `TestLaneCategoryGuardTests` scan | `tests/Antiphon.Tests/**/*.cs` | `TestLaneCategoryGuardTests` | added line matching a class declaration |
| `classification` | `TestClassificationGuardTests` | `tests/**/*.cs` | `TestClassificationGuardTests` | added file that declares a class |
| `spawn-census` | `ProcessSpawnLimitTests` source census | `tests/Antiphon.Tests/Checkpoints/**` | `ProcessSpawnLimitTests` | added file |
| `nightly-pin` | protected nightly inputs | `scripts/lib/nightly-*.ps1`, `scripts/lib/NightlyCore.cs`, `scripts/lib/Antiphon.Nightly/**`, `tests/test-execution-policy.json`, `scripts/test-nightly-core.ps1` | `NightlyCoreTests`, `LiveLaneRunnerTests` | any change |
| `linux-roster` | `tests/linux-test-roster.json` | the file itself | `NativeLinuxPolicyWitnessTests` | the file changes |
| `selector-table` | `tests/impacted-classes.json` | the file itself | `ImpactedClassesSelectorTests` | the file changes |

`RosterRegistryTests` fails when a registry path is missing, a guard class is missing, or `git ls-files` finds a tracked `*roster*.txt` or `*roster*.json` with no entry. `StrictClasses` is the `spawn-census` entry, pointed at `ProcessSpawnLimitTests.cs`, because it is a member rather than a file.

An added E2E test file selects `C1514LandingSplitBudgetTests`. That is the group-130 shape from the card (run `dce78ee0` is not in this tree, so it has no replay row). `fabda59573` and `c8fc4a413c` add class-declaration lines and select `TestLaneCategoryGuardTests`. `6a371bc388` adds `tests/Antiphon.IO.WriteBehind.Tests/WriteBehindFileSystemTests.cs` and selects `TestClassificationGuardTests`.

Rejected: selecting `NativeLinuxPolicyWitnessTests` on every new test class. The witness is the policy hash, and a new class is not a policy edit. `protected-data` covers the DPAPI shape.

### D-8. Solution shape guard

`LandGraphAuditProjectShapeTests` reads `Antiphon.sln` as text. It fails when a `Project` line is not followed by `EndProject`, or when `Antiphon.LandGraphAudit` is listed twice. The roster trigger is any change to `Antiphon.sln`. The ten-build parallel loop that produces CS2012 stays out of pre-land scope, as the land-graph note already measured.

Rejected: selecting `LandGraphEvaluationTests` on every solution edit. D-3 allowlists the sln for that reason.

### D-9. What-ran paste lives in the owner docs

A Code, Review, or Merge report pastes `kinds`, `classes`, `skippedByCap`, and `windowsFilters` from one `-Json` run of the selector on its diff. `WhatRanReceiptContractTests` pins those four names in `docs/testing-and-build.md` (impacted-classes section) and in the Merge process section of `docs/orchestration-loop.md`. The stage bundles stay unchanged (R-2).

Rejected: a sentence in `stage-code.md` or `stage-review.md`. The caps leave 23 and 21 characters.

### D-10. Ledger block, historical notes grandfathered

The template gains one fenced block:

```json
{
  "ledger": "why-missed",
  "proposedRowId": "",
  "coveredBy": "hand",
  "reason": ""
}
```

`coveredBy` is `hand`, `derived`, `keyword`, `roster`, `merge-subset`, or `cannot`. `hand` requires `proposedRowId` to be an id in `tests/impacted-classes.json`. `cannot` requires a non-empty `reason`. The other four require a non-empty `reason` naming the rule. `WhyMissedLedgerTests` feeds fixture notes. Notes listed in `tests/why-missed-ledger-grandfather.txt` (every `docs/investigations/*why-missed*.md` present when S5 starts) are exempt until they contain `"ledger": "why-missed"`. A note that is not grandfathered and has no block fails.

The five CARD-1781 phrases stay in the loop section, the rc skill, and the template. A new sentence is added beside them: `a machine-readable ledger block names the proposed row id or why it cannot be a row`. `WhyMissedWriteUpContractTests.rc_failure_loop_names_the_five_why_missed_items` keeps its five-item array. A second test pins the new sentence in the same three slices.

Rejected: rewriting the 2026-11-09 notes inside this card. The replay table is the census. Backfill is a later docs pass.

### D-11. No pre-push hook

Direct pushes to master still skip every stage. The operator-nightly note's hook stays a separate card. This plan records that limit in the replay.

### D-12. No migration

No database, no model, no `Down`.

## Replay

Measured on 2026-11-09 with `scripts/impacted-classes.ps1 -Range <parent>..<sha> -Json` at this tree, plus `git show --stat` of each commit. "Today" is whether the failing class is in `classes`. Selector time is that one call.

The 17 rows are the distinct failures in the 14 `docs/investigations/2026-11-09-why-missed-*` notes. The six `*windows-failure.md` notes repeat the e2e, spawn, pty, lease, transcript, and pre-dispatch rows. `6a371bc388` is both the write-behind miss and the policy edit behind the pin. The observation tips `251dee88e11e` (only `ChannelBridgeTests.cs`) and `ccd3909780` (only `scripts/lib/rc-project-selection.ps1` and its test script) are not introducers: today's selector selects nothing on either, in 18.9 s and 11.4 s.

| # | Failure | Introducing commit | stat | Today | v2 | Red if run |
|---|---|---|---|---|---|---|
| 1 | `AgentTaskLandDeliveryE2ETests` busy arm | `4d91773c122186930a3da485dd2a3221a1e960cd` | 4 files, land-notification service, queries, hosted service, reconcile-load tests. 16.9 s. kinds `land-notification`, 9 classes | no | no edge. The E2E file does not name those types. The probe that names the hosted service was added later (`680fecc474`) | no |
| 2 | `ProcessSpawnLimitTests` limiter census | `be4c317a38568a37b15d41ae985fee6f8054ef1d` | `PhaseJournalKillDiagnosticTests.cs` plus `scripts/test-run-checkpoint.ps1`. 13.2 s. kind `spawn-limit-rosters` | yes | roster `spawn-census` | yes |
| 3 | `Card1277PtyHostFixtureTests` Claude wait | `5e21b61a2d3b05d919edee81d71e43dd65f34751` | the new test file plus its plan. 17.7 s. kind `spawn-limit-rosters`, class not selected | no | declaration selects the new class | no. Serial Linux passed. The race needs a loaded Windows chunk |
| 4 | `RepositoryMutationLeaseTests` reflected `LandingGit` | `dff78584d636fa49eca37652f2c3f14a48002a92` | 11 files including `LandingGit.cs`. 20.2 s. kinds `server-git-start-sites`, `server-image-push-credentials`, `server-repository-mirror`, `spawn-limit-rosters`, 4 classes | no | type edge. The test constructs `LandingGit` (`:144`, `:206`) | yes. The crash script fails. Class about 56 s |
| 5 | `Card1773TranscriptPromptStampMigrationTests` hardcoded `\n` | `57bbfc99d1810c99f2bf6e1a928427918c05d1fd` | that file only, 4 insertions. 13.0 s. no kind | no | declaration selects the class, which passes on Linux. Keyword `newline-generated-text` selects the guard | yes, the guard. The Windows class stays green on Linux |
| 6 | `PreDispatchFailureDeliveryTests` canceled confirm | `ae5b545d9ac9091f42847734b88bf8e202cf2814` | 12 files including the notification service and the Card1773 migration. 24.1 s. kinds `land-notification`, `migrations`, 62 classes | no | type edge. The test calls `AgentTaskLandNotificationService` (`PreDispatchFailureDeliveryTests.cs:142`) | yes. Class 45-65 s. Linux fails the stale expectation |
| 7 | `C1677TempFamilyRegistryTests` missing `c1778-trust` | `de3c59991297c9d49d18bba0b7ad6d25ea270156` | 7 files. The producer hunk is one line in `PtyHostLeakSweep.cs`. 16.4 s. no kind | no | one-hop path edge through `RunnerTempSweep.cs:72` | yes. Class under 1 s |
| 8 | `CapabilityScriptTests` DPAPI on Linux | `86902399c084f51e2cc7021dcf1cbaa9f7353c00` | 9 files, adds the class. 17.0 s. kind `spawn-limit-rosters` | no | declaration, and keyword `protected-data` | yes. The new class throws on Linux |
| 9 | `LandGraphEvaluationTests.EvaluationBudgetRefusesSlowDotnet` | `bc96c71bef39761b4dbc5c05e1030312396a0e16` | 11 files including the precheck script and `LandGraphEvaluationScriptTests.cs`. 21.7 s. no kind | no | declaration plus the path literal `scripts/audit-land-verification-precheck.ps1` | yes, on Linux. Whole class because the test file changed (D-6) |
| 10 | LandGraphAudit compiled twice (CS2012) | `f5cc4facfacdc4e4a72a6d098d225305624dac12` | `Antiphon.sln`, the tool csproj, `SolutionAudit.cs`. 12.5 s. no kind | no | shape guard (D-8). Derived index ignores the sln (D-3) | yes for an unclosed or doubled project. The parallel race loop is not run |
| 11 | `C1759_HostedScanSkipsUndueUnlinkedNotes` | `6db9130dfd4ced7a9751fe673628e4f5138bb651` | adds the test file, 279 lines. 23.0 s. kind `spawn-limit-rosters` | no | declaration selects the class. The class is already in the fixed Merge subset | no. It passes when run alone |
| 12 | `RemoteWorkspacePreparerTests` cancel wait | `1d3c34ff062f6cebcffa60110baaa8dc09e3b4b4` | 11 files including the new test. 15.8 s. kinds `migrations`, `statement-budgets`, 56 classes | no | declaration | no. The failure is a load flake that passes alone |
| 13 | Nightly pin witness stale | `6a371bc38888a77325fbdc24e55e61850ed05e68` (protected input; the red was seen at `251dee88e11e` and `ccd3909780`) | 13 files including `tests/test-execution-policy.json`. 21.9 s. kind `linux-only-policy`, 1 class | no | roster `nightly-pin` | yes, about 1 minute, when a stage runs it |
| 14 | projectWalk hidden-folder | `a930059d8b82f259549925120fda3abf196fa08e` | `NightlyCore.cs`, `nightly-policy.ps1`, `test-nightly-core.ps1`, `NightlyCoreTests.cs`. 21.5 s. no kind | no | declaration selects `NightlyCoreTests` | yes on Linux |
| 15 | `WriteBehindFileSystemTests` had no lane category | `6a371bc38888a77325fbdc24e55e61850ed05e68` | adds `tests/Antiphon.IO.WriteBehind.Tests/WriteBehindFileSystemTests.cs` | no | roster `classification` | yes |
| 16 | Raw-string classes, CARD-1810 | `fabda595738775bfc98b4a487dce5543288e7883` | census plus `ProcessSpawnLimitTests.cs`. 17.4 s. kind `spawn-limit-rosters`, 1 class | no | roster `lane-category` on the added class lines | yes. Guard 5.5 s |
| 17 | Raw-string classes, CARD-1816 | `c8fc4a413cafc76f5e52c2262ba9419794f196ad` | `C1816OutsideFolderProbe.cs` plus the census tests. 13.0 s. same kind | no | roster `lane-category` | yes |

Precursor `19e6c73957bc01e36f2b0b85229e6f0c0ed5e76c` (CARD-1483 S1, 6 files including `LandGraphEvaluationTests.cs`, 12.1 s, no kind) is the TFM edit. The rc failure is row 10. v2 selects `LandGraphEvaluationTests` on this precursor because the test file changed.

Catch rate, counting a row once: 13 of 17 would go red if the v2 selection were executed (rows 2, 4, 5, 6, 7, 8, 9, 10, 13, 14, 15, 16, 17). Rows 1, 3, 11, and 12 have no failing edge or pass when selected. Today's selector selects the failing class for 1 of 17 (row 2 only).

Of the 13, rows 9, 13, 14, and 15 were direct pushes with no stage. The pipeline that already runs the selector would have caught 9 of 17. The four direct pushes stay red until an operator hook exists (D-11).

Added cost when the selection is run: one extra class or one source guard per catch. The multi-minute additions are `LandGraphEvaluationTests` at about 10 m 22 s when its file changes, the one-minute method when only the tool or precheck script changes (D-6), and the nightly pin pair at about 1 minute. `RepositoryMutationLeaseTests` is about 56 s. `PreDispatchFailureDeliveryTests` is 45-65 s. The lane guard is 5.5 s. The temp-family class is under 1 s. No catch adds `AgentTaskLandDeliveryE2ETests`.

## Verification design

Witnesses go red before the production change in the same slice. Each witness has one mutant. Mutation runs after land, method-scoped, and is not a checkpoint row.

| Id | Witness | Mutant | Doc pin |
|---|---|---|---|
| V-1 | Declaration: a diff that only adds a `*Tests` class selects that class | Delete the declaration union. V-1 stays green | |
| V-2 | Path or type edge: `LandingGit.cs` selects `RepositoryMutationLeaseTests` on a fixture diff | Drop type edges. V-2 stays green | |
| V-3 | One hop: `PtyHostLeakSweep.cs` selects `C1677TempFamilyRegistryTests` | Drop hop edges. V-3 stays green | |
| V-4 | Dotted script name `Antiphon.Server.Infrastructure.Git.LandingGit` selects the lease class | Ignore dotted strings. V-4 stays green | |
| V-5 | A path with 26 fixture classes is absent from the index and present in `cappedPaths` | Raise the cap to 1000. V-5 stays green | |
| V-6 | `Antiphon.sln` selects no derived class | Remove the allowlist entry. V-6 stays green | |
| V-7 | An added `ShouldBe("...\n")` line selects `GeneratedTextNewlineGuardTests`, and the guard fails on that line | Remove the newline token. V-7 stays green | |
| V-8 | An added `ProtectedData` line with no Windows skip selects `DpapiPlatformGuardTests`, and the guard fails | Remove the token. V-8 stays green | |
| V-9 | An added `WorkingDirectory = CheckpointFixtures.RepoRoot` line selects no keyword | Add that literal to the keyword file. V-9 fails | |
| V-10 | An added file under `tests/Antiphon.E2E` selects `C1514LandingSplitBudgetTests` | Remove the c1584 entry. V-10 stays green | |
| V-11 | An added `public sealed class` line under `tests/Antiphon.Tests` selects `TestLaneCategoryGuardTests` | Remove the lane roster. V-11 stays green | |
| V-12 | A tracked `*roster*.txt` missing from the registry fails the witness | Return success for an unknown roster file. V-12 stays green | |
| V-13 | The impacted-classes section and the Merge section contain `kinds`, `classes`, `skippedByCap`, and `windowsFilters` | Delete `skippedByCap` from one doc. V-13 fails | V-13 |
| V-14 | A fixture note with `coveredBy: hand` and an unknown id fails; `cannot` with a reason passes; a grandfather path with no block passes | Accept any id. V-14 stays green | |
| V-15 | The five phrases and the ledger sentence are in the loop section, the rc skill, and the template | Delete the ledger sentence from the template. V-15 fails | V-15 |
| R-1 | A migration-only fixture diff still selects the hand migrations classes and the derive set | | |
| R-2 | `stage-code.md` and `stage-review.md` bytes stay 2477 and 2579 | | R-2 |
| R-3 | Schema 2 is still refused | | |
| R-4 | `-MergeSubset` on a derived hit still emits the fixed classes | | |

Existing tests affected: `ImpactedClassesSelectorTests` (the hand table and the bundle sentences). The slices do not edit that table or those bundles, so the class is in CP-1 to prove it stayed green. `WhyMissedWriteUpContractTests` gains one method and keeps the five-item array. No public DTO, route, operation label, remote script, or statement budget changes. The impacted-classes row for this diff is none: no current kind glob matches `scripts/impacted-classes.ps1`. The checkpoints name the classes anyway.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1820-s1/` | derived-index | `/*/*/(DerivedImpactIndexTests*)\|(ImpactedClassesSelectorTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6, R-1, R-3, R-4 | 0 failed | 41 | 15 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1820-s2/` | keyword-guards | `/*/*/(KeywordTriggerTests*)\|(GeneratedTextNewlineGuardTests*)\|(DpapiPlatformGuardTests*)/*` | V-7, V-8, V-9 | 0 failed | 6 | 8 | true |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1820-s3/` | rosters | `/*/*/(RosterRegistryTests*)\|(LandGraphAuditProjectShapeTests*)/*` | V-10, V-11, V-12 | 0 failed | 5 | 8 | true |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1820-s4/` | what-ran | `/*/*/(WhatRanReceiptContractTests*)/*` | V-13, R-2 | 0 failed | 2 | 8 | true |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1820-s5/` | ledger | `/*/*/(WhyMissedLedgerTests*)\|(WhyMissedWriteUpContractTests*)/*` | V-14, V-15 | 0 failed | 3 | 8 | true |

`ImpactedClassesSelectorTests` is 12 methods plus 13 arguments on `malformed_table_is_refused` (25 executions). `DerivedImpactIndexTests` is 16 methods after the S1 repair. CP-1 Min is 41, that sum. The S1 class run measured 12m 17s for those two classes plus `MergeHighRadiusSubsetTests`, so the CP-1 estimate is 15 minutes. Later rows' Min values are the floors of the witnesses named above. Code raises Min when the class has more tests. It does not lower it.

Suffix check: none of these class names is a prefix of another. `LandGraphAuditProjectShapeTests` contains `Land` once, under the four-class warning.

## Performance budget

Standards: `docs/test-performance-standards.md` (R5, about 2x measured p95) and `docs/testing-and-build.md` performance section.

Selector `-Range` on this host, n=19, min 11.4 s, median 17.0 s, max 24.1 s. That sample includes git diff and the full tests walk. The call budget after this card is 60 s, above 2x the measured max. S1 records one post-change sample in the slice report. A sample over 60 s is a failed budget, not a reason to raise the number inside the slice.

The Roslyn rebuild inside `DerivedImpactIndexTests` has no measured p95. Allowance 60 s for the witness, which is one parse of the tests tree per CP-1 run, shared for every witness in that class (one rebuild, then in-memory compares). If the first measured rebuild exceeds 60 s, S1 stops and reports the number. It does not move the parse onto the selector call.

Checkpoint row allowance is 8 minutes, matching the CARD-1782 repair's cold-build allowance (that plan recorded a 337 s round-2 build). Row deadline is `max(15, 3 * EstimatedMinutes)` = 24 minutes, under the 45-minute advisory, so import emits no timeout warning. These are allowances. The tests themselves are source scans plus the existing selector script. Published durations used for the replay cost, not re-timed here: lane guard 5.5 s, temp-family class under 1 s, lease class about 56 s, pre-dispatch class 45-65 s, land-graph class 10 m 22 s, nightly pin pair about 1 minute.

Code floor for the closed table is the sum of `EstimatedMinutes`: 40 minutes, plus authoring. Each slice runs its own CP row. No explicit `totalTimeoutMinutes`, so the tool default `max(30, 2 * sum + 10)` applies.

Phases: CP-1 pays the index rebuild once. CP-2 and CP-3 are source guards with no process spawn beyond the test host. CP-4 and CP-5 read files. No shared Postgres. No Slow row.

## Slices

Each slice is one commit, red witness first, then the behaviour, then the checkpoint row for that `After` group.

### S1. Derived index (about 90 minutes)

Add `DerivedImpactIndexBuilder` and `DerivedImpactIndexTests` (V-1 through V-6, R-1, R-3, R-4). Commit the generated `tests/derived-impact-index.json`. Teach `scripts/impacted-classes.ps1` the declaration union and the index read. Output the new JSON fields as empty lists when nothing matches, so existing consumers still parse.

### S2. Keyword triggers and the two guards (about 90 minutes)

Add `tests/keyword-triggers.json`, `KeywordTriggerTests`, `GeneratedTextNewlineGuardTests`, and `DpapiPlatformGuardTests` (V-7, V-8, V-9). Wire the added-line match.

### S3. Roster registry and the solution shape guard (about 90 minutes)

Add `tests/roster-registry.json`, `RosterRegistryTests`, and `LandGraphAuditProjectShapeTests` (V-10, V-11, V-12). Wire roster selection.

### S4. What-ran paste (about 60 minutes)

Document the four field names in the impacted-classes section and the Merge process. Add `WhatRanReceiptContractTests` (V-13, R-2). No bundle edit.

### S5. Ledger (about 75 minutes)

Add the template block, the sixth sentence in the three contract files, `tests/why-missed-ledger-grandfather.txt`, and `WhyMissedLedgerTests` (V-14, V-15).

## Migrations

None.

## Risks and operator steps

- The index can select a slow class the hand table never named. D-3 and D-6 are the controls. S1 prints `cappedPaths` and the index byte size in the slice report. A file over 2_000_000 bytes fails the drift witness so the slice narrows edges before it commits.
- One hop misses a helper that neither the test nor the helper's direct callees name. Row 1 is that shape: the E2E class does not name the hosted service. The Linux probe added after the repair is the forward edge. The Windows E2E class stays on the rc roster and is not auto-selected.
- Rows 3, 11, and 12 pass when selected. v2 does not claim them.
- Rows 9, 13, 14, and 15 were direct pushes. No selector runs until a stage or a future hook does.
- `C1514LandingSplitBudgetTests` is Integration and reads the nightly planner. CP-3's witness uses a fixture tree and does not run that class. A real E2E file add will put the class on the selection, at the cost already paid when that class is run (its own plan). The witness asserts the class name in the selector output.
- Windows owed by this card: nothing new is executed here. Existing `windowsFilters` rows stay owed on a native Windows lane. The historical Windows E2E busy arm and the pty race stay rc-lane failures. The newline and DPAPI catches are Linux source guards.
- Operator-only, not in these slices: a pre-push hook for direct master pushes (D-11), and a docs backfill of ledger blocks onto the grandfathered notes.

## Existing tests

`ImpactedClassesSelectorTests` pins the hand table, the bundle sentences, and the migrations derive set. Point at that class for CARD-1782. No other current row matches this card's files.
