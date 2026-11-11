# Why missed: post-land census read CARD-1820 raw-string probes as classes

One committed note for rc run d88de419, chunk antiphon-all-group-131, rc/2026-11-16 =
`6f8741d872d63507827014f972ccf71196722d4a`. Windows reported
`PostLandEarlyWarningTests.Manifest_uses_no_letter_or_and_partitions_unit_classes` 1 failed:
the test census expected `N\nProbeTests` and the plan did not ship it. Extract:
[2026-11-10-run-d88de419-g131-PostLandEarlyWarningTests-red-windows-failure.md](2026-11-10-run-d88de419-g131-PostLandEarlyWarningTests-red-windows-failure.md).
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

## Introducing commit/card

introducing commit/card: `af1c08c73807cf57760f81570c65e5aacac88309` (CARD-1820) added the
`namespace N;` partial `ProbeTests` and the `[Category("Unit")] ProbeTests` raw-string probes
to `tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs`; `2d94f483f` (CARD-1820) tagged
more probe classes `[Category("Unit")]`. The latent scanner defect (first-seen dedup before
categories in `Get-PostLandClasses`, no raw-string awareness in either scanner) is from
CARD-1784 (`549cce24f` and its script).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `dfefd8e0-ee3d-4095-960b-da6237834b44` (stage outcome `d6d761f7`), scope Full, reviewed
  source `cb931f0890126ee1327eb2fc9fe29103e3667fe4`, on server2 Linux, running the CARD-1820
  plan's checkpoint set. The exact filter list was not re-read for this note. That source
  already held both the probes and `PostLandEarlyWarningTests.cs`, and the red is
  content-deterministic, so the Clean verdict means the class was not in what ran.
- Earlier rounds `8ec4caa8`, `7c6f052c`, `135047fd` recorded Found and did not approve.

## Outside the selection

why the failing test was outside that selection (lane/category/path): `[Category("Unit")]`,
both lanes, in `tests/Antiphon.Tests/Scripts/PostLandEarlyWarningTests.cs`. The change was to
another test file's string literals. No impacted-classes row maps a test-source edit to the
post-land census test, and nothing reads `DerivedImpactIndexTests.cs` as a dependency of
`PostLandEarlyWarningTests`: the dependency is the whole `tests/Antiphon.Tests` tree, which
the census walks. The g130 fix `a86c257bd` ran the class on Linux on a base without CARD-1820,
and rc/2026-11-15 (run 6591e378) did not contain CARD-1820, so rc/2026-11-16 was the first
run of the class over the probes. The failure is content-deterministic, not Windows-specific.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/**/*.cs` -> `/*/*/PostLandEarlyWarningTests/Manifest_uses_no_letter_or_and_partitions_unit_classes`

Proposed row id `post-land-census`, not applied (`tests/impacted-classes.json` is pinned by
`ImpactedClassesSelectorTests`). It is one method, about 20 s, Linux is enough. With the fix
on this branch the scanners agree on raw strings and split partials, so the row guards
future scanner drift rather than this exact probe.

## Exemption

same cause as an already written note: none

`2026-11-10-why-missed-post-land-early-warning-g130.md` is the same class but a different
cause (read-only git objects and the en-GB `startedAt` parse, fixed by 01b93066a).
