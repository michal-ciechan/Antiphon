# Why missed: nightly commits pushed directly to master skipped every stage selection

One committed note for three master reds that stopped the rc/2026-11-13 cut checks twice: Merge 63cdf1df at
`251dee88e11ee48da66c34aa636f57c4b3e3eb41`, and again at `ccd3909780fe3e7a177effac627a6e25be42f0d7`. CARD-1781.
Debug task 4f9a7df9 reproduced and fixed them on Linux at master `ccd3909780fe3e7a177effac627a6e25be42f0d7`.

## Failure

1. Stale live-lane pin. `LiveLaneRunnerTests.WrapperOnlyPaidManifest` was red with `C1545 WrapperOnlyPaidManifest/full-inputs`
   and `crlf-inputs`. `$script:PinnedInputDigest` in `scripts/test-live-lane-run.ps1` was still `b507aab8...` (set by e9679ba96).
   The tree printed `c5cf05b5...` at 251dee88e and `b499df30...` at ccd390978. The `policyHash` in
   `tests/test-execution-policy.json` stayed equal to its computed value (`7e54681b...`), so only the pin was stale.
2. Lane-xor on `WriteBehindFileSystemTests`. This caused 9 guard-batch reds. It has its own note,
   [2026-11-09-why-missed-write-behind-category](2026-11-09-why-missed-write-behind-category.md). The fix is commit 337624e9d (Debug b73aeb1d),
   which is cherry-picked here.
3. `NightlyCoreTests.CoreMatchesFallback` `projectWalk` was red on every Linux run (17/18 rows). The case marked `hidden/` with
   `FileAttributes.Hidden`. Linux ignores that attribute, so both walks kept `hidden/Skip.csproj`. The product is consistent
   across the two walks: the compiled `NightlyRepository` walk and the `Get-ChildItem -Recurse -File` fallback both prune what .NET
   reports as Hidden, which is the attribute on Windows and a leading dot on Linux. The bug was in the test only. The case now names
   the folder `.hidden` and keeps the attribute, and the assertion is unchanged.

## Introducing commit/card

introducing commit/card: no card for any of the three. These are the operator's direct pushes to master:

- Pin: the protected inputs changed in 93a64b5bb, 25581a30e, a930059d8, eb7f9c68c, 6a371bc38, 5428f0511, cdb34950e, 68e276a9b,
  3cc2a4c7e, 9e1ec564b, a1459528c, b99b3ead4, 77e0ca3db and 4eb2e20b9. The protected inputs are `nightly-common`, `nightly-policy`,
  `nightly-coverage`, `nightly-core.ps1` and `test-execution-policy.json`.
- Lane-xor: 6a371bc38888a77325fbdc24e55e61850ed05e68.
- projectWalk: a930059d8b82f259549925120fda3abf196fa08e.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- none. Every commit was a direct push, with no Code, Review or Merge task.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, `Integration`, `tests/Antiphon.Tests/Scripts/`
(LiveLaneRunnerTests, NightlyCoreTests). The guards are in the Unit lane (`tests/Shared/TestClassificationGuardTests.cs`).

No selection ran before any of these pushes. Whoever pushes directly to master skips the per-task Final selection and the Merge
final-tree run. The operator's local runs were most likely on Windows, where the Hidden attribute works, so the projectWalk red is
Linux-only. The pin witness is the only thing that ties a nightly-input edit to the live-lane commission, and it runs in the
Integration lane. Nightly and rc qualification only see these reds after the push. The rc cut smoke found them, so the rc stayed safe,
but the cut was stopped twice.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/lib/nightly-*.ps1`, `scripts/lib/Antiphon.Nightly/**`, `tests/test-execution-policy.json`, `scripts/test-nightly-core.ps1` ->
`/*/*/(LiveLaneRunnerTests*)|(NightlyCoreTests*)/*` (Linux and Windows; about 1 minute after a build)

Proposed card, which no card found by `card.ps1 search 'pre-push'` or `'direct push'` covers: "Pre-push gate for direct master pushes".

- Add an opt-in `pre-push` hook for `refs/heads/master`. It builds `tests/Antiphon.Tests` under a build slot and runs:
  - the classification guard batch;
  - the CARD-1801 fixed Merge subset;
  - the selector rows above whenever the pushed range touches nightly or live-lane inputs.
- It refuses the push on any red. `--no-verify` stays the operator's explicit override.
- It prints the `-PrintInputDigest` value next to the stored pin when a protected input changed.

This would have caught all three reds. The projectWalk red needs the Linux leg (server2) to be seen, and the other two show on both
platforms. Until the hook exists, any operator push that changes a protected nightly or live-lane input makes the pin stale again.
The Merge that lands this note must recompute the pin at land time.

## Exemption

same cause as an already written note: lane-xor is [2026-11-09-why-missed-write-behind-category](2026-11-09-why-missed-write-behind-category.md).
The pin and projectWalk causes record none.
