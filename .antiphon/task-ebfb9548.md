# Review ebfb9548: CARD-1836 S3a (round 2, diff only)

Subject: origin/feat/card-task-91085723. Reviewed tip 9ec9b850cbbd79dd9bfe180f60d2dae6dde1416e. Round-2 diff base 553dd32f60589ad1794729785411de1d213a4096 (round 1: Review 16c6dbc2, blocker D-1).
Original Code/landing owner: 3ca2caa6-dae9-4738-a352-bcb58ce85f62 (named in the repair plan).

Verdict: CLEAN. D-1 is fixed. Both previously surviving mutants (M-a, M-b) now go red, and the three round-1 kills still go red. There are no blockers and no regressions.

## Diff scope (553dd32f6..9ec9b850c)

- scripts/test-rc-input-manifest.ps1 (+55/-1): fixture-only changes. The diff adds a per-edit baseline, the `isolated-fanout-change` and `fanout-no-fallback` checks, a runner inventory check for shared inputs, a wildcard-only `build/wildcard-only.txt` case, and a committed-map case that loads `scripts/lib/rc-input-owners.json` from source HEAD.
- docs/superpowers/plans/2026-11-13-card-1836-s3a-fanout-repair.md (new): the repair plan, with CP-2 and CP-90.
- The library, the committed ownership map, the TUnit wrapper and any launch or Full-mode path are unchanged. Inert check: `git grep rc-input-manifest|Get-RcInputManifest|rc-input-owners` at the tip finds only the lib, the harness and RcInputManifestTests.cs.

## Checkpoint evidence

Run 20261113-042126-e6d9 at 9ec9b850c: sourceState=clean, buildSource=verified, verdict GREEN, `validate` CHECKPOINT SOURCE VALID rows=3.
The run used plan extract `.antiphon/review-ebfb9548-plan.md`. CP-2 and CP-90 are copied from the repair plan, with estimates raised for a loaded host. CP-93 adds the brief's DerivedImpactIndexTests guard and reuses the same build.

| CP | executed | passed | failed | skipped | wall |
|---|---:|---:|---:|---:|---|
| CP-2 RcInputManifestTests | 4 | 4 | 0 | 0 | 41.8 s (cold build 390 s) |
| CP-90 guard union (10 classes) | 135 | 135 | 0 | 0 | 101.8 s |
| CP-93 DerivedImpactIndexTests | 27 | 27 | 0 | 0 | 650.3 s |

Total wall 24m44s. The tool reported `unlisted: none`. One unlisted build was run under build-slot for a stated reason: the derived-index driver prebuild (scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj, 7m05s). It is the CP-93 `regenerate_script_*` precondition and is not part of Antiphon.sln. The checkpoint tool was also built under build-slot (12 s).

## Mutants

Method: a scripts-only `git archive` of the tip was committed as a scratch Git repository, because the harness reads the map from source HEAD. Each mutant got its own copy with one literal replacement, committed, then ran through `pwsh test-rc-input-manifest.ps1 -Case <c>`. No dotnet driver was involved. The tip copy is the control, and all 4 cases pass there.

| Mutant | Change | Result |
|---|---|---|
| M-a | `foreach ($id in @($root.consumers)[0])` (first explicit consumer only) | KILLED: ProductAndHelperFanout `fanout:tests/Shared/Helper.cs`, `fanout-inventory:tests/Shared/Helper.cs` |
| M-a2 | per input, add only when no suite owns it yet (global first owner) | KILLED: fanout plus wildcard-dirty/inventory (runner, other), committed-map-all-projects-dirty, committed-map-owned:* |
| M-b | `*` resolves to `@(@($suites.Values)[0])` | KILLED: `wildcard-dirty:runner/other`, `wildcard-inventory:*`, `committed-map-all-projects-dirty`, `committed-map-owned:*` |
| M1 | unknown-path Add removed | KILLED: DynamicAndUnownedAreDirty `unknown-path-all-dirty` |
| M3 | index edge does not union input record | KILLED: PartialIndexNeverSubtracts `positive-index-unions-input-record` |
| M3b | positive index clears the dirty set | KILLED: PartialIndexNeverSubtracts `partial-index-keeps-conservative-consumers`, `index-cannot-subtract:runner/server` |

Every other case stays green under each mutant, which is the expected localisation. The round-1 M-a survival came from accumulated edits, and the per-edit baseline closes it: Helper.cs is now the only change, so `runner` can only become dirty through the shared-helper owner.

## Vacuity (committed-map and wildcard checks cannot pass empty)

The committed map was mutated at scratch HEAD.

| Variant | Result |
|---|---|
| V0 `roots: []` (empty map) | RED: `committed-map-no-fallback`, `committed-map-wildcard-coverage-nonempty` |
| V1 `suiteProjects` matches nothing (empty project list) | RED: library throws `input-suites-empty` (harness exit 1, no pass row) |
| V2 `**` becomes `nothing/**` (owns nothing) | RED: `committed-map-no-fallback`, `committed-map-wildcard-coverage-nonempty` |
| V3 `*` becomes a single explicit project | RED: `committed-map-all-projects-dirty`, `committed-map-wildcard-coverage-nonempty` |
| W1 fixture `build/**`->`*` rule removed (wildcard has no owner) | RED: `wildcard-no-fallback`, and every `fanout-no-fallback:*` |

The census check (`committed-map-suite-census`) and the coverage counter (`covered -gt 0`) together refuse a missing or empty suite set. The `unresolved.Count -eq 0` checks stop the dirty-all fallback from masking missing owners.

## Other gates

- check-evidence-diff 553dd32f6..9ec9b850c: commits=2 entries=0 violations=0, exit 0. Over the whole slice, cc48c7005..9ec9b850c: commits=9 entries=0 violations=0.
- merge-tree with origin/master 272ee17c0: clean, tree 4d84b4bace84898d613d139e36b8bbca5ac92c57.
- Full is unchanged and the library is inert: no launch, qualification or `-SelectionMode Full` file is in the slice.
- Performance: the fixture body runs in about 31.7 s for 4 tests (CP-2 testsWall), inside the 45 s child and 60 s TUnit budgets.

## Advisories

None new. Round-1 advisories A-1 to A-4 stand as filed.

## PCs

None are defined by this slice. M-a and M-b remain pending for method-scoped SourceLanding Mutation; this scratch evidence does not discharge them.
