# CARD-1842 S2 Code report (task f2fe07ed)

Outcome: S2 is implemented and green on the Linux lane. It adds the expected-duration roster, the attribute (applied to no class), the exporter, the sync tests and the Nightly sentence.

- Branch `feat/card-task-f2fe07ed`. Worktree `/work/worktrees/task-f2fe07ed`. Base `3db1a43b02246706dc28075fce7e7b5e84804ddb`.
- Original Code task `f2fe07ed` (landing owner).
- Plan: `docs/superpowers/plans/2026-11-09-card-1842-timing-eta.md` (slice S2, row CP-2).
- Prior work: S1 (`0c94fd0e7`/`2ea534baa`) and S4 (`63f146822`/`cd9a18cc8`) were already on master. S2 had no earlier implementation anywhere (`git log --all`).

## Commits

| SHA | What |
|---|---|
| `b7d3169d29a7f6f3c94f0cfb6f015768bd63cf3c` | RED: witnesses, NotImplemented reader scaffold, attribute, csproj links |
| `56a37f9edfc88cd027e90a8c8e23ef2e736681ff` | FIX: reader/comparer, exporter, roster JSON, Nightly sentence (did not compile: missing `using`) |
| `651dd2d4cb47be7ab533cc0b38041bca26502c0b` | FIX: `using Microsoft.CodeAnalysis` |
| `b8f33a029ab017f626cd24b97b5245eab89975a5` | TEST: V-5 also exports a fixture cost row that has hostId/os/measuredRunId. Committed rows have none, so without it a dropped identity copy was invisible |
| `9efd4c1e4e2710597a101cc22047409c24477431` | TEST: `Case.Sensitive` on string `ShouldContain`. Shouldly's default ignores case; this tightens the test |

Tested SHA: `9efd4c1e4e2710597a101cc22047409c24477431`. The report commit follows it and touches only this file.

## Files

- `tests/Shared/ExpectedDurationAttribute.cs`: `[ExpectedDuration(double seconds)]`, class-only. Linked only into Antiphon.Tests and applied nowhere.
- `tests/Shared/ExpectedDurationRoster.cs`:
  - `Load` (format `antiphon-expected-duration-roster-v1`), `ProjectCostFile` and `Differences`.
  - `FindApplications`: a Roslyn syntax scan that ignores comments and strings, and a reflection overload.
  - `Check`: an application must name a roster class, have exactly one numeric literal, and equal the roster seconds exactly.
  - It does not reference PlatformTaxonomy.
- `tests/Shared/expected-duration-roster.json`: 772 classes copied from `scripts/lib/nightly-class-costs.json`. The fields are seconds, basis, and hostId/os/measuredRunId (null today).
- `scripts/export-expected-duration-roster.ps1`: ASCII, ordinal sort, LF output; optional `-CostPath` and `-OutputPath`.
- `tests/Antiphon.Tests/Antiphon.Tests.csproj`: two `Compile Include` lines beside PlatformTaxonomy. These are not EmbeddedResource.
- `tests/Antiphon.Tests/TestHelpers/ExpectedDurationSyncTests.cs`: V-5..V-10. Integration, `ProcessSpawnLimit`, `Timeout(120_000)`.
- `docs/testing-and-build.md` `## Nightly`: the plan's roster and attribute sentences, plus one pointer to re-run the exporter. The S3 tolerance sentence is not added.

## Evidence

| Run | SHA | Result |
|---|---|---|
| RED, class filter, native build | b7d3169d | total 6, failed 5 (V-5, V-6, V-7, V-8, V-10 NotImplemented/doc), passed 1 (V-9 is a pin by plan design) |
| GREEN class | 651dd2d4, b8f33a02, 9efd4c1e | 6/6 each |
| **CP-2 checkpoint tool** run `20261111-213545-9fa8` | 9efd4c1e | see the line below; `validate` = `CHECKPOINT SOURCE VALID source=9efd4c1e4e2710597a101cc22047409c24477431 rows=1` |
| Guard batch, nine-class union (`TestClassificationPolicy`, `TestClassificationGuard`, `SlowTestRegistryMerge`, `SlowTestTripwire`, `NativeLinuxPolicyWitness`, `MechanicalPcContract`, `TaskPlatformGuidance`, `C1729LayeringGuard`, `TestLaneCategoryGuard`) | 9efd4c1e | 78/78, 0 failed, 0 skipped. TRX has all 9 classes |
| Impacted-classes row `land-graph-audit-project` (csproj glob): `LandGraphAuditProjectInstanceTests` | 9efd4c1e | 1/1 |
| check-evidence-diff base..HEAD | 9efd4c1e | `EVIDENCE result commits=5 entries=0 violations=0` |
| merge-tree vs origin/master (= base 3db1a43b0) | 9efd4c1e | clean, tree 16aa45ac6463cd5edf1efb98ef83ca6b22df5192 |

```
CHECKPOINT CP-2 commit=9efd4c1e4e2710597a101cc22047409c24477431 build=ok filter=/*/*/ExpectedDurationSyncTests/(RosterMatchesCostFile*)|(TreeHasNoExpectedDurationAttribute*)|(AttributeAgreesWithRoster*)|(AttributeOnlyClassFails*)|(DeriveStaysAPlatformLabel*)|(DocPinsTheRosterSentence*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-f2fe07ed/.antiphon/checkpoints/20261111-213545-9fa8/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=9efd4c1e4e2710597a101cc22047409c24477431 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=15s executed=19.859s wall=34.902s
PHASES CP-2 slotWait=15s build=489.0799285s startup=10.5768438s testsWall=7.4784088s teardown=0.6776627s hostWall=18.7329158s
verdict: GREEN exit=0
```

Unlisted runs, all through build-slot:
- The native builds and class runs above: red, green, and mutant evidence.
- The guard batch and the impacted-class row: brief and impacted-classes rule.
- The checkpoint tool build.

Slot waits ranged from 0 to 120 s. The class harness wall is about 9 s.

## Mutants (one per witness, all red, all restored; tree clean afterwards)

| Witness | Mutant | Result |
|---|---|---|
| V-5 RosterMatchesCostFile | roster `AgentTaskLandDeliveryE2ETests` seconds 31800→31801 | red (only targeted tests failed; run A had 4 failed, 2 passed) |
| V-5 (exporter half) | exporter writes `hostId = $null` | red, 1 failed / 5 passed (run B) |
| V-6 TreeHasNoExpectedDurationAttribute | new `tests/Antiphon.Tests/TestHelpers/C1842MutantProbe.cs` with `[Antiphon.TestSupport.ExpectedDuration(600)]` | red (run A) |
| V-7 AttributeAgreesWithRoster | `Check` accepts `abs(diff) <= 1` | red, 2 failed / 4 passed (run C, rebuild) |
| V-8 AttributeOnlyClassFails | `Check` ignores a class absent from the roster | red (run C) |
| V-9 DeriveStaysAPlatformLabel | `Load` calls `PlatformTaxonomy.Derive([])` | red (run A) |
| V-10 DocPinsTheRosterSentence | attribute sentence replaced | red (run A) |

## Owed / pending

- Every PC/variant from this slice is pending for SourceLanding Mutation: V-5 (both halves), V-6, V-7, V-8, V-9 and V-10. This round discharges none of them.
- Plan rows deferred to their slices: S3 (CP-3/4/5: OS split, tolerance sentence V-15, roster re-export with `secondsByOs`), S5 (CP-7), S6 (CP-8/9) and S7 (CP-10). They are not run here and not passed. S1 and S4 are already on master.
- `tests/test-execution-policy.json` is unchanged, so no policyHash or PinnedInputDigest refresh is needed.
- No migration.
- The csproj change may trigger the land-path land-graph refresh at land. land-graph `-Update` was not run, per the brief.
- Windows: nothing is owed for S2. The roster is OS-independent. The plan's Windows step belongs to S7.
- Kept: `.antiphon/checkpoints/20261111-213545-9fa8/` holds the receipt and includes the tool's 1.3 MB `builds/bin-c1842-s2`. All native bin/obj directories were deleted.
