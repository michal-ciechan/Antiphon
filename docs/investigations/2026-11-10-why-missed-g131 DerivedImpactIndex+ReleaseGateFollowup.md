# Why missed: g131 DerivedImpactIndex + ReleaseGateFollowup (run ae84478d)

One committed note for rc run ae84478d, chunk `antiphon-all-group-131`, rc/2026-11-17 =
`3db1a43b02246706dc28075fce7e7b5e84804ddb` (master at the cut). Windows reported
`DerivedImpactIndexTests` 1 failed and `ReleaseGateFollowupTests` 1 failed. The Windows messages
are in `docs/investigations/2026-11-10-run-ae84478d-g131 DerivedImpactIndex+ReleaseGateFollowup-windows-failure.md`
(desktop read, task 2d562b37). Debug task 9eef369d. CARD-1781.

## Failure

Two independent deterministic defects, both platform-independent. Neither is a flake, a load
effect or a Windows-only difference.

| Test | Classification | Linux at `3db1a43b0` |
|---|---|---|
| `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged` | build-graph defect (test prerequisite never built by the qualification) | red: `The application to execute does not exist: .../scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll` |
| `ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity` | test fixture defect (hand suite allowlist drifted from the policy) | red: 7 rows fail on `Unexpected fixture process: .../Antiphon.Server.Git.Tests.exe ...` |

This is not the CARD-1838 stale-index family. `committed_index_matches_the_builder` passed on
Windows (7.8 s) and on Linux at the cut. No class landed after the last regeneration was missing
from the committed index.

1. The regenerate test runs `scripts/regenerate-derived-impact-index.ps1 -NoBuild`. That needs the
   driver project `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` to
   be built already. The driver was not in `Antiphon.sln`, and the qualification builds only
   `dotnet build Antiphon.sln -c Debug` (`scripts/lib/nightly-tests-impl.ps1:1834`).
2. `tests/fixtures/card1336-release-gate-followups.ps1` wraps the synthetic process seam in a
   fail-closed allowlist. It admitted suite executables through a hand regex of eight names. The
   fixture copies the live `tests/test-execution-policy.json`, so the nightly workload it drives
   launches every registered suite. That includes `server-git` (CARD-1546) and `resilience`
   (CARD-1829), and on master also `gitinprocess` (CARD-1830). The regex refused them.

Fixes on `feat/card-task-9eef369d`. The first three are the exact bytes of the desktop read's
branch `feat/card-task-2d562b37` (tree-equal), so the two branches do not conflict:

- `969247369` (= `f1881f6a8`): adds the driver to `Antiphon.sln`. Separately, the fixture now
  admits exactly the `suites[*].project` executables of the copied policy, by case-sensitive
  exact leaf match. Anything else is still refused, and the fixture throws if
  `Antiphon.Tests.exe` or `Antiphon.E2E.exe` is missing from that census.
- `b91c030ca` (= `cfc34b121`): moves the solution entries so they sit clear of master's suite appends.
- `0f391a03d`: new guard `DerivedImpactIndexTests.solution_build_produces_the_regenerate_driver`.
  The test reads `Antiphon.sln` and requires the driver with a `Debug|Any CPU.Build.0` entry.

Evidence on server2 (Linux), `UseAppHost=false`, one build-slot lease each:

| Run | Commit | Result |
|---|---|---|
| Both classes, clean worktree (no driver) | `3db1a43b0` | 28 total, 2 failed (the two above), 11m 31s |
| `dotnet build Antiphon.sln` | `969247369` | exit 0, driver dll produced |
| Both classes + guard union + `ProcessSpawnLimitTests` | `969247369` | 163/163 passed, 14m 16s |
| Driver bin/obj deleted, `dotnet build Antiphon.sln` | `a25f7e33e` | exit 0, driver dll produced |
| Both classes | `a25f7e33e` | 28/28 passed, 13m 36s |
| New guard | `0f391a03d` | 1/1 passed |
| Positive control: base `Antiphon.sln` swapped in | `0f391a03d` | 1/1 failed, `Antiphon.sln does not list the regenerate driver`; restored |
| `regenerate-derived-impact-index.ps1` | `0f391a03d` | `derived-impact-index unchanged` |

The fixture's derived suite list against `origin/master` `b02f110fb` policy gives 11 executables,
including `Antiphon.GitInProcess.Tests.exe`. Master still has the old regex, so master is red
on this row too until the fix lands. `git merge-tree --write-tree origin/master HEAD` is clean.

Native Windows proof is owed: after `dotnet build Antiphon.sln -c Debug` on the desktop, run
`/*/*/(DerivedImpactIndexTests*)|(ReleaseGateFollowupTests*)/*`.

## Introducing commit/card

- Regenerate test: `4f111d1ed0c616b377057e77d23fceca2aa06711` (source `2a512332a`), CARD-1838,
  Code task `c6f8a251`, landed by Merge batch 176 (`c9c34d15`, master `3fd231a2e`).
- C1336 allowlist: the hand regex is CARD-1336 (`1642471e3`, 2026-10-20). The commits that made
  it red registered new suites in `tests/test-execution-policy.json`:
  - `9e7016e2f` (CARD-1546, Code `3d1b7734`, Merge batch 162 `9dfd3ca0`)
  - `1b16bcfe7` (CARD-1829, Code `84e7e4ab`/Debug `c6a7d07b`, Merge batch 180 `36c89ac0`)

  Neither commit is in rc/2026-11-15 (run 6591e378, where C1336 passed).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- CARD-1838: no Review task. Code `c6f8a251` handed off `next: review`, and Merge batch 176 landed
  it directly. Code ran `DerivedImpactIndexTests` 26/26 and the guard batch 77/77. Merge 176
  ran the subject plus guard union, `DerivedImpactIndexTests` included, 222/0/0. Both lanes had
  built the driver first. Code built it for its mutant. Merge ran the regenerate step without
  `-NoBuild` just before the tests.
- CARD-1546: Review `da37e430`, Full, round 1, clean at `f10db184a`. It ran checkpoint
  `20261110-100925-7f01`: CP-3/6/7/8/9 plus the land-graph rows, the guard batch 135/135, and the
  impacted `Antiphon.Tests` rows outside the checkpoints, 12/12. `ReleaseGateFollowupTests` was
  not in it.
- CARD-1829:
  - Review `d85d2e34`, Full, clean at `568111476`: the Resilience suite 58/58 and the guards
    107/107, including `DerivedImpactIndexTests` 24.
  - Review `09847c78`, Full, at `013d29a0a`: the Resilience suite 58/58 and the nine-class
    guard union 78/78. In the same process `regenerate_script_reports_changed_then_unchanged`
    **failed because `Antiphon.DerivedImpactIndex.dll` was not built**. The Review recorded
    that as "outside this card" and moved on. Neither Review ran `ReleaseGateFollowupTests`.

## Outside the selection

why the failing test was outside that selection (lane/category/path):

- `ReleaseGateFollowupTests`: lane Antiphon.Tests Integration, path
  `tests/Antiphon.Tests/Scripts/ReleaseGateFollowupTests.cs`. The fixture's dependency on
  `tests/test-execution-policy.json` is a runtime data read. The derived index (CARD-1820)
  sees only C# type and path edges, which for this class are `ScriptHarness*.cs`. In
  `tests/impacted-classes.json`, `tests/test-execution-policy.json` selects only the
  `nightly-live-lane` row (`LiveLaneRunnerTests`, `NightlyCoreTests`) and the `linux-only-policy`
  methods. So no Review or Merge that registered a suite selected C1336.
- `regenerate_script_reports_changed_then_unchanged`: lane Antiphon.Tests Unit, path
  `tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs`. It was inside every selection, as
  part of the Merge guard union. It was green wherever a lane had built the driver, and the
  Merge regenerate step always builds it right before the tests. Only a checkout whose only
  build is the solution build, which is the rc qualification, lacks the dll. Two lanes saw it
  red and neither filed it. Review `09847c78` recorded it as outside its card. Merge batch 182
  (`d12e11f0`, the lane that pushed this cut, `3db1a43b0`) saw 25 passed, 1 failed, built the
  driver by running the regenerate script, re-ran the method 1/1 and pushed.
- Why the Merge guard union does not prove the index family: it runs after a regenerate step that
  builds and rewrites, so it certifies the Merge worktree, not the qualification build graph.
  For stale bytes the union is sufficient: `committed_index_matches_the_builder` passed here.
  For the driver it was structurally blind. `solution_build_produces_the_regenerate_driver` now
  closes that, because it reads `Antiphon.sln` and does not depend on any lane's build history.

## Family: the mirror-project split

Both reds belong to the mirror-project split family (CARD-1729 umbrella: CARD-1546, CARD-1829,
CARD-1830, plus the CARD-1838 driver project). The split adds projects, and two consumers
enumerate projects by hand:

- The qualification builds only `Antiphon.sln`. Any project a test runs from a prebuilt output
  (`-NoBuild`, `--no-build`, a copied exe) must be listed there with a `Debug|Any CPU.Build.0`
  entry. Selection that would have caught it: `solution_build_produces_the_regenerate_driver`,
  or any lane that runs `DerivedImpactIndexTests` after only `dotnet build Antiphon.sln`.
- C1336 drives the nightly workload over every suite in the policy. Any new test project
  must be in its executable list, which now derives from the policy. Selection that would have
  caught it: `ReleaseGateFollowupTests/C1336_NightlyCandidateIdentity` on any range that
  touches `tests/test-execution-policy.json`. That is every suite registration.

Rule for the next mirror slice: a new test project is in `Antiphon.sln`, and its plan names
the C1336 row.

The driver could instead be built by the test itself, without `-NoBuild`. That was rejected:
it is a nested `dotnet build` of a project that references `Antiphon.Tests`, run from inside
the `Antiphon.Tests` host. It would be a second build under the outer slot and take several
minutes per run. It would also rebuild `Antiphon.Tests` while that assembly is loaded. The
solution entry costs one more project in a build the qualification already runs.

Land graph: on this branch `audit-land-verification-graph.ps1 -Check` refuses only with
`manifest does not match the selected graph` (count 79, unchanged from master; the driver
csproj record already exists since Merge batch 176). The `Antiphon.sln` record changed. That
is the sanctioned `land-graph-land-refresh.ps1` step at Merge.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

- `tests/test-execution-policy.json` -> `/*/*/ReleaseGateFollowupTests/C1336_NightlyCandidateIdentity`
  (about 2.5 min on server2; deterministic on Linux).
- `Antiphon.sln`, `scripts/regenerate-derived-impact-index/**` ->
  `/*/*/DerivedImpactIndexTests/solution_build_produces_the_regenerate_driver` (under 1 s, no
  driver build needed).

Process rule: a Review that sees a red test outside its card files it (a Debug task or a card)
instead of recording it as foreign.

## Exemption

same cause as an already written note: none. The index-drift family named in the brief did not
recur.
