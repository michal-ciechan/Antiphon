# Why missed: post-land census reads raw string fixtures (group 131)

One committed note for rc run d88de419, chunk antiphon-all-group-131, rc/2026-11-16 =
`6f8741d872d63507827014f972ccf71196722d4a`. Windows reported `PostLandEarlyWarningTests`
1 failed in `Antiphon.Tests`. Group 131 is the schedule slot of old group 130, and the
group-130 fix `01b93066ae49a3ab2b57730bce4622fe72e7858c` (CARD-1784) is in this cut.
The sibling Windows read was not on origin when this was written, so the failing method
is named from a Linux reproduction at the same SHA. CARD-1781.

## Failure

This is a different test and a different cause from group 130. The group-130 causes were
the en-GB `startedAt` parse and the read-only git object on dispose. Neither is involved.

`Manifest_uses_no_letter_or_and_partitions_unit_classes` is red on Linux at
`6f8741d87` (21 run, 1 failed, 20 passed). It is deterministic and platform-independent,
so it is the one Windows failure: any other failure in the class would make the count two.
The assertion is `actualKeys.ShouldBe(expectedKeys)` at line 286. The test census expects
`N` / `ProbeTests`, and the script's plan does not have it.

Both censuses read every line of `tests/Antiphon.Tests/**/*.cs`, including lines inside
raw string literals. `DerivedImpactIndexTests` holds fixture text with `namespace N;` and
`public partial class ProbeTests` (no category), then a `[Category("Unit")]` fixture
`public sealed class ProbeTests` that the scan also files under `N`. The script
(`Get-PostLandClasses`) dedupes on the first occurrence of a key, whatever its category,
so it keeps the non-Unit partial. The test census dedupes only Unit occurrences, so it
keeps the Unit one. The two lists differ by that one fake class.

The same scan gives the real lane a phantom shard. `ProcessSpawnLimitTests` fixture text
declares `namespace Example;` and a Unit `UnlimitedTests`, so the real plan carried
`unit-ns-Example` with filter `/*/Example/*/*[Category=Unit]`. That filter matches no
test, and a zero-executed shard is `incomplete` (see `Zero_executed_shard_is_incomplete`).
Fixture namespaces also leaked onto the real classes after them in the same file.

Classification: test-and-product defect, not a timeout and not an expected Windows skip.
The fix masks lines inside or on the edge of a raw string literal (a run of three or more
`"`; an odd count toggles) in both `Get-PostLandClasses` and the test census. On the real
tree the plan drops 15 fixture entries (`Example.UnlimitedTests`, five
`Antiphon.Tests.Checkpoints` fixture classes, nine `Antiphon.Tests.Scripts` fixture
classes) and adds none. The red witness `Classes_inside_raw_string_fixtures_plan_no_shard`
was red at `e555789bafa44d81277b472809bf1aa8b0d87787` (a `/*/Phantom/*/*[Category=Unit]`
shard) and is green with the fix.

## Introducing commit/card

introducing commit/card: `2d94f483f13da3e960ad4b423c832e4c3aaa386d` (CARD-1820), cherry-picked
from `cb931f0890126ee1327eb2fc9fe29103e3667fe4`. It added `[Category("Unit")]` to the
`forced` `ProbeTests` fixture after the `namespace N;` partial fixtures from
`af1c08c73807cf57760f81570c65e5aacac88309` (CARD-1820). Before it, both censuses skipped that fixture. The census itself
is from `549cce24fa463a73f8a33a82e86b536b9affe82e` (CARD-1784). `2d94f483f` is not in the
group-130 cut `ac6d00022`, which is why the method passed there.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `dfefd8e0-ee3d-4095-960b-da6237834b44`, scope Full, reviewed source
  `cb931f0890126ee1327eb2fc9fe29103e3667fe4`. CP-1
  `/*/*/(DerivedImpactIndexTests*)|(ImpactedClassesSelectorTests*)/*` at 64/64. CP-G
  nine-class guards at 78/78, checkpoint `20261110-182815-10cf`. `ProcessSpawnLimitTests`
  at 57/57. Outcome Clean.

Rounds 1 to 3 (`8ec4caa8-6c65-4239-9017-40f22b2daa88`, `7c6f052c-d303-4f59-a88b-c57dcf9ade4d`,
`135047fd-33dc-4d42-9597-85b86d4dc2b9`) recorded no review evidence and predate `cb931f08`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Unit lane,
`[Category("Unit")]`, `tests/Antiphon.Tests/Scripts/PostLandEarlyWarningTests.cs`. The edit
was fixture text in `tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs`. The
post-land manifest test reads the whole tests tree as data, and nothing maps a test-file
edit to it. The CARD-1820 selection ran only the derived-index, selector, guard and
spawn-limit classes. CARD-1784 was already on that reviewed source, so a Linux run of the
class at `cb931f08` would have shown the red.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/**/*.cs`, `scripts/lib/post-land-early-warning.ps1` -> `/*/*/PostLandEarlyWarningTests/Manifest_uses_no_letter_or_and_partitions_unit_classes`

Proposed row id `post-land-manifest-census`, not applied. It is one method of about 15 to 25
seconds on Linux. With raw string lines masked, fixture text no longer moves it.
A Windows pre-land run is not required for this cause.

## Exemption

same cause as an already written note: none

`docs/investigations/2026-11-10-why-missed-post-land-early-warning-g130.md` is the same class
with a different cause.
