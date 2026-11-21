# Review round 1: owner-map soundness guard (CARD-1836)

Reviewed source: `eb5e7ead43396148fe5b9f8f4c7b90d185e7258f` on `feat/card-task-9e6138ac` (owner task `9e6138ac-b0f5-4cdb-8fdd-2823ac9d0c88`). Range `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0..eb5e7ead43396148fe5b9f8f4c7b90d185e7258f`. Round 1, Final requested, ordinary scope Interim. Shadow mode only: no activation and no gate credit.

Verdict: blockers. The twelve recorded cases match the committed inventory, and the positive expectation can still fail. The guard stays green when every path is reported selected, and it stays green when `ComposerInputProbe.cs` is removed from the inventory.

## Blockers

### B1. No missed-status witness

Where: `scripts/test-rc-input-owners.ps1` case `HistoricalSelectionSoundness`, and `tests/fixtures/incremental-selection-soundness.json`.

Failure: a lookup that returns selected for every path produces zero failures. The fixture has twelve `currentMap` values and all twelve are `selected`.

Why: the case sets status to selected when any changed path is a present owned input. With every expectation selected, an always-selected lookup satisfies the contract. The repaired test has no witness that a truly unowned path is reported missed.

Fix: add one case whose only changed path is absent from that suite's inventory and whose `currentMap` is `missed`. The composer server paths are already outside the Pty suite: the inventory probe owned six composer paths and did not own `server/Application/Services/SessionMessageQueueService.cs` for `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj`. A one-path case on that suite with `currentMap` `missed` goes red if the lookup returns selected for everything.

### B2. Composer probe row is not pinned

Where: the same case, id `6591e378-composer-probe`.

Failure: deleting the inventory row for `src/Antiphon.Agents.Pty/ComposerInputProbe.cs` leaves the case `selected`.

Why: the case ORs every changed path. At `eb5e7ead43396148fe5b9f8f4c7b90d185e7258f` the Pty suite owns six of the recorded paths, all with reason `project-tree`:

- `src/Antiphon.Agents.Pty/ComposerDeliveryEvidence.cs`
- `src/Antiphon.Agents.Pty/ComposerInputProbe.cs`
- `src/Antiphon.FakeClaude/Program.cs`
- `tests/Antiphon.Agents.Pty.Tests/ClaudeInputProbeCanaryTests.cs`
- `tests/Antiphon.Agents.Pty.Tests/ComposerInputProbeTests.cs`
- `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs`

The five paths that remain still select the suite. The Windows failure was this case reporting missed. The repaired assertion does not lock the composer source file the investigation named.

Fix: assert that `src/Antiphon.Agents.Pty/ComposerInputProbe.cs` is a present input of the Pty suite whose reasons exclude `unmatched-path-fallback` and `unresolved-conservative-all-inputs`. Removing that row must fail even though the sibling paths remain.

Mutant B, flipping the composer expectation to `missed` while the row stays owned, does go red. That direction is intact.

## Ownership path

`Recorded_owner_map_status_matches_the_committed_map` calls `Read-RcInputInventory`, the same function `Get-RcInputManifest` uses to build each snapshot. The selected/missed decision is reimplemented in test-local `Test-RioPresentOwned`. The call passes `DerivedIndex` `$null` and `-SkipUnresolvedBroadening`. The switch is a no-op (`$null = $SkipUnresolvedBroadening`). A production manifest that receives the derived index can only add edges, so the test is stricter in that direction.

The predicate does not call `Get-RcInputManifest`, so it does not apply test-only deferral. `testOnly.enabled` is true. `d88de419-platform-taxonomy` and `34d41b7e-step-deadline` are owned only by `derived-impact-scan` plus the suite's own `project-tree`. Those reasons are not product edges. A historical commit that does not add a test identity can be removed from `dirtySuites` by the manifest while this guard still says selected. The repair note scopes the fixture to ownership before deferral, and the previous root-only evaluator also stopped before deferral. That gap stays an advisory. The blockers above are the fail-open holes in the ownership assertion itself.

The literal-input change in `Read-RcInputInventory` assigns a non-glob target directly and scans `$paths` only for globs. For a literal, the previous code overwrote the scan with that same target. A PowerShell check of the new array form returned one element for a literal path and zero elements for an empty glob scan. Inventory paths come from `git ls-tree` and `Resolve-RcInventoryPath`, both forward-slash. The new branch has no `OperatingSystem` or runtime OS check.

## Fixture

`tests/fixtures/incremental-selection-soundness.json` is unchanged in this range. `6591e378-composer-probe` `currentMap` `selected` was introduced in `d63fce0464007c06a358f06957ce2632101566b8` and has no later edit. The only fixture edit since that commit is `61eec38f92`, which records `f2de5b30-resilience-mirror` as the known-unsound M1 miss. The twelve expectations were not edited to fit this repair. There is no separate generator; the S2 replay checked the values in.

## Index, merge, selectors

`tests/derived-impact-index.json` changes four counters: `scripts` harness path classes 75 to 76 and owed 50 to 51, and `ScriptHarnessProcess.cs` classes 52 to 53 and owed 27 to 28, with the cap window moving `IncrementalSelectionSoundnessTests` in. That shape matches `DerivedImpactIndexBuilder` when this class starts referencing `ScriptHarness`. The regeneration was not run a second time, so unchanged bytes are not witnessed. Protected nightly digest inputs are outside this diff. The digest was not recomputed. `scripts/audit-land-verification-graph.ps1 -Check` was not started.

`git merge-tree --write-tree origin/master HEAD` is clean. `origin/master` is `4aaeea4760983178357cf99c35bc2f0118247b9c`. Merged tree `1c13fe1cf3465a4d4b29ac738f3bd804d362d262`. The merged manifest keeps the literal shortcut and `HistoricalSelectionSoundness`. Master's extra hunk in that file replaces `Test-RcAddedTestAttribute` with `Test-RcTestIdentityChange` on the deferral path. The historical case was not re-executed on that tree: the 1-minute load stayed above 14 for the five-minute poll. `IncrementalSelectionSoundnessTests.cs` is identical in the merged tree. The merged `*.csproj` diff adds a `Compile` link and a package reference, and no `ProjectReference`. The M1 closure reads project references. It was not rebuilt on the merged tree.

`scripts/check-evidence-diff.ps1` over the range: commits=3, entries=0, violations=0.

`scripts/impacted-classes.ps1` kinds: `spawn-limit-rosters`, `test-lane-category`. The migrations row did not match. Downgrade selector added 0 rows. No owed/capped lines.

`scripts/check-os-witnesses.ps1` needs a plan with `### Checkpoints`. This repair has none, so the checker was not run. The lookup change is portable. `scripts/lib/rc-input-manifest.ps1` matches that checker's `scripts/lib/` restart census; this script is read by the next rc selection, and the change does not alter AppHost startup.

## Runs

One build, native output, `UseAppHost=false`, `TEMP=/tmp`, `ANTIPHON_LINUX_COMPILER_POLICY=0`, load 11.69 when the slot was requested.

- Build `tests/Antiphon.Tests/Antiphon.Tests.csproj`: 0 errors, 2424 warnings, 4m 55s. Lease `6e7e475b-e875-4bc4-afe5-e03f0e2c138c`.
- `/*/Antiphon.Tests.Scripts/IncrementalSelectionSoundnessTests/*`, `--timeout 570s`: total 2, succeeded 2, failed 0, skipped 0, 1m 32s. Lease `a6f52df2-067b-4358-921a-ffef5c9d37e5`.
- Parenthesized guard filter `TestClassificationPolicy*|TestClassificationGuard*|SlowTestRegistryMerge*|SlowTestTripwire*|NativeLinuxPolicyWitnessTests*`, `--timeout 570s`: total 56, succeeded 56, failed 0, skipped 0, 1m 16s. Lease `9407a0db-48b2-4066-8d07-e22de7633433`.

Inventory probe at the reviewed SHA, same `Read-RcInputInventory` and the same predicate: 72.9s, real failures 0, missed expectations 0, mutant A status `selected`, mutant C failures 0. The class execution budget is 120s. This host finished the class in 92s. That is under a 2x allowance for the 73s inventory sample. Advisory only.

No checkpoint manifest and no checkpoint-tool receipt. `reviewedSourceClean` is false. Positive controls: none in this repair; none discharged.

## Advisory

The 120-second harness budget and the deferral gap above do not hold the land. File them if a later round wants them tracked. The blockers do hold the land.
