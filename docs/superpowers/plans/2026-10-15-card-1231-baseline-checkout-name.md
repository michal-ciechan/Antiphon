# CARD-1231: the baseline checkout does not sit under a bin-* folder

Code and landing owner: `1a0b2fd9-2c42-4793-a584-6a91646568ae`.
Task base: `78e4001788e99ebc04f0590a84db18442f6324c6` (`git merge-base origin/master HEAD`).

Observed in CARD-0507 S1 (task b6aecd46, card text): `--baseline` labelled
`TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration` INTRODUCED
while it was red at master `893ddfce`. `BaselineComparer.CheckoutPath` named the detached
baseline checkout after the build id (`<temp>/antiphon-checkpoints-baseline/<run-id>/bin-c507-s1`).
`TestLaneCategoryGuardTests.IsBuildOutput` skips a path with a `bin`, `obj` or `bin-*`
segment (`tests/Antiphon.Tests/TestHelpers/TestLaneCategoryGuardTests.cs`, `IsBuildOutput`),
so the baseline run scanned no file, passed, and `BaselineComparer.Classify` returned
INTRODUCED for an executed name that did not fail. `DelegationHarnessCensusTests`,
`WindowsUnitPlatformAdmissionTests` and `PackageVersionPinContractTests` carry the same
segment filter (`grep -rn 'StartsWith("bin-"' tests`), so the fix is in the tool, not in
one census.

## Implementation

S1 commits the failing tests with the unchanged checkout name and moves
`Get-NamespaceCensus` (`scripts/lib/checkpoint-usage.ps1`) with the added test method.
S2 names the checkout leaf `baseline-<suffix>`, where `<suffix>` is the build id after its
`bin-` prefix (`bin-c507-s1` -> `baseline-c507-s1`). The build id check
(`OutputCleanup.IsIsolatedOutputId`) and the containment checks stay as they are; the
build output inside the checkout keeps the build id (`<checkout>/<project>/bin-c507-s1/`),
which a census skips as before. S2 also states the naming rule in the Checkpoint runner tool
section of `docs/testing-and-build.md`.

The text-fixture false positive in the same guard (CARD-1155 fixture source read as real
classes) is a separate fix owned by CARD-0507 S1 (`067645805`, `ee218db6c` on master); this
task does not change the guard.

Scope: `tools/Antiphon.Checkpoints/Baseline/BaselineComparer.cs`,
`tests/Antiphon.Tests/Checkpoints/BaselineComparerTests.cs`,
`scripts/lib/checkpoint-usage.ps1` (census count), this plan, its evidence file and the
Checkpoint runner tool section of `docs/testing-and-build.md`. No server, client, database,
delivery, landing or lease change. Restart: none (the tool is launched per run from source).

## Verification design

- V-1: `BaselineComparerTests.red_at_base_census_is_inherited_when_the_census_skips_build_output_folders`.
  Its fake test run imitates the guard's segment filter on the path the tool chooses below
  the temp root: under a `bin`/`obj`/`bin-*` segment it writes a passing TRX for
  `N.Guard.census`, elsewhere a failing one. Expect `INHERITED` and the run's working
  directory leaf `baseline-c507-s1`. Red at S1 (CP-1): the leaf is `bin-c507-s1`, the TRX
  passes and the kind is `INTRODUCED`.
- V-2: `BaselineComparerTests.checkout_path_refuses_unsafe_build_ids` expects the leaf
  `baseline-a` for build `bin-a` (was `bin-a`) and keeps its unsafe-id refusals. Red at S1.
- V-3: the other `BaselineComparerTests` methods keep their assertions and pass at S2.
- V-4: manual acceptance of the S2 run without `--keep-outputs`: exit 0, outputs deleted,
  and both receipt validators accept the tested SHA.
- R-1: `CheckpointNamespaceCensusUsageTests` (CP-2) keeps `Get-NamespaceCensus` aligned
  with the compiled checkpoint cases after the added method.

### Positive controls (pending SourceLanding Mutation)

| PC | Variant | Method-scoped detecting filter |
|---|---|---|
| PC-1 | Name the checkout leaf after the build id again | `/*/*/BaselineComparerTests/red_at_base_census_is_inherited_when_the_census_skips_build_output_folders` |
| PC-2 | Name the leaf `baseline-` plus the whole build id (`baseline-bin-c507-s1`) | `/*/*/BaselineComparerTests/red_at_base_census_is_inherited_when_the_census_skips_build_output_folders` |
| PC-3 | Insert a `bin-<suffix>` parent above a `baseline-<suffix>` leaf | `/*/*/BaselineComparerTests/red_at_base_census_is_inherited_when_the_census_skips_build_output_folders` |

PC-2 is expected to survive the V-1 filter: `baseline-bin-c507-s1` is not a skipped
segment, so the classification stays INHERITED; only the literal leaf assertion detects it.
Mutation records the outcome. Code performs no deliberate mutants.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1231-proof/` | baseline | `/*/*/BaselineComparerTests/*` | V-1, V-2, V-3 | 8 tests; S1 2 failed; S2 8 passed | 8 | 10 | false | n/a |
| CP-2 | S1 | CP-1 | census | `/*/*/CheckpointNamespaceCensusUsageTests/*` | R-1 | 1 passed | 1 | 2 | false | n/a |

### Cost

One isolated test build plus two short rows per run: a red S1 run and a green S2 run,
within the 90-minute task budget. No whole-Unit lane: the brief excludes it; the change is
one path name in the tool's baseline step. Bootstrap the checkpoint tool once through
`scripts/build-slot.ps1` into `tools/Antiphon.Checkpoints/bin-c1231-tool/` (the planned
unlisted build). Import this table to an ignored manifest with `timeouts.rowMinutes: 40`;
run with `--total-timeout 150m`, the exact committed `--expected-source-sha`, no
`--keep-outputs`, and wait until the exit is not 75. Remove a red run's kept output root
by literal path after inspection. Each proof selection runs at most three normal times;
none after green.
