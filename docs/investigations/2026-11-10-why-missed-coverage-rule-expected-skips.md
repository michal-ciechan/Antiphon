# Why missed: coverage rule counts expected Windows skips as unexecuted

One committed note for the first completed Windows Full rc run
`dce78ee0a35a4b08be30d8e0e8ef0295` (log dir `2026-11-09-1306-dce78ee0`,
candidate `rc/2026-11-14` at `7bee43614495293afbae1b0f4add019d11dacb9c`).
The run finished 2026-11-10 05:48Z after 60109s with `succeeded=false` and
`coverageComplete=false`. CARD-1781. No card number is assigned to this repair.

## Failure

`ConvertTo-NightlyCoverageVerdict` treated every TRX `NotExecuted` row as
`unexecuted-outcome`. TUnit records `SkipTestException` that way, with
`DebugTrace` beginning `Skipped:`. All 25 methods in that bucket were expected
Windows skips (Linux host contracts, the S7 phone-home fixture, the Claude
symlink-to-sh launch, C680 missing-context, the C1734 Linux native helper, the
slow-dotnet PATH shim `LandGraphEvaluationTests.EvaluationBudgetRefusesSlowDotnet`,
and the eight C1717 inode, inotify, and renameat2 witnesses). Twelve single-test
profile chunks exited 8 because TUnit prints `Zero tests ran` when the only test
skipped, and the chunk loop then failed the run on `process-exit-8`. A Windows
Full run could not become `coverageComplete`.

The session-runner all-group-4 timeout (green TRX, 119 passed, process tree
still alive at 1801s) is a different cause and is not changed here.

## Introducing commit/card

introducing commit/card: 1d085c76fe523fcf9bc7ff1e3ed5047fe52dfffe (CARD-0487).
`fix(nightly): CARD-0487 reject unexecuted TRX, produce discovery, dedupe enqueue`,
Mike Ciechan, 2026-09-11. The rule is still the one that run read.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- none recorded for this cause. No earlier Windows Full qualification completed,
  so no review selection observed the 25 skips as a finished coverage result.

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was not
outside the Windows Full lane. The 25 methods were discovered, started, and
skipped on that run. What never ran to the end was every earlier Windows Full
qualification, so the coverage summary was never produced. The gate lives in
`scripts/lib/nightly-coverage.ps1` and `scripts/lib/nightly-tests-impl.ps1`.
`tests/impacted-classes.json` row `nightly-live-lane` names `LiveLaneRunnerTests`
and `NightlyCoreTests` only. It does not name a verdict fixture over expected
skips, and this repair does not edit that pinned file.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/lib/nightly-*.ps1`, `tests/expected-platform-skips.json` ->
`/*/*/ExpectedPlatformSkipRosterTests/*`

Run `ConvertTo-NightlyCoverageVerdict` over the last completed run summary as
well: an expected Windows skip must not appear as `unexecuted-outcome`, and an
all-expected chunk that exited 8 must not appear as `process-exit-8`.

Proposed row (not added; `ImpactedClassesSelectorTests` pins the JSON):

```json
{
  "id": "expected-platform-skips",
  "globs": [
    "scripts/lib/nightly-*.ps1",
    "tests/expected-platform-skips.json"
  ],
  "classes": [
    "ExpectedPlatformSkipRosterTests"
  ]
}
```

## Exemption

same cause as an already written note: none.
