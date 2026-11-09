# Why missed: evaluation budget slow-dotnet shim

One committed note for the master red `LandGraphEvaluationTests.EvaluationBudgetRefusesSlowDotnet`.
Debug task e1f8af02 reproduced it on Linux at master `7f0a8bb1f8542d75bb9d8654d43b0c6fabefb2a6`. CARD-1781.

## Failure

On master the method fails in about 36s. The assertion still requires
`evaluation-failed: evaluating Antiphon.sln exceeded 1 s`. The script output is only:

```
refused
audit refused: land graph pre-check refused
```

The class filter `/*/*/LandGraphEvaluationTests/*` at that same commit is 154 passed, 1 failed, 0 skipped
(155 total). The other failure is this method.

The product budget is unchanged. `scripts/audit-land-verification-graph.ps1` still kills the evaluator
at `-EvaluationBudgetSeconds` and prints `evaluation-failed: evaluating Antiphon.sln exceeded N s`, then
`land graph evaluation refused (exit 2)`. bc96c71be adds two lines to that script, only to pass
`-ToolPath` through. The refusal clock was not edited.

What changed is the pre-check host. Before bc96c71be, `scripts/audit-land-verification-precheck.ps1`
was PowerShell and did not start `dotnet` (the parent has no `FileName = 'dotnet'`). The port makes
that script a wrapper whose `ProcessStartInfo.FileName` is `dotnet` and whose arguments are
`<dll> precheck ...`. The test's PATH shim treats every argument list other than `--version` as the
slow evaluator: it sleeps, prints `refused`, and exits 1. The wrapper accepts exit 1, the graph script
stops with `land graph pre-check refused`, and evaluation never starts.

The wrapper is doing what the port designed. The shim was wrong to intercept the tool host. The fix
forwards an argument equal to `precheck` to the real `dotnet` later on `PATH` and leaves the evaluator
slow. The assertion text is unchanged.

## Introducing commit/card

introducing commit/card: bc96c71bef39761b4dbc5c05e1030312396a0e16 (no card). Direct operator push by
Mike Ciechan, 2026-11-09 13:36:22 +0000, message `perf(land-graph): port the emulation pre-check to C#`.
Single parent `25b347f493cb781b693abc450e7090491c8638ae`, and an ancestor of master
`7f0a8bb1f8542d75bb9d8654d43b0c6fabefb2a6`. No later commit touches the shim, the wrapper, or the
budget script.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- none. Direct push. No Code, Review, or Merge task.

The commit message says the author ran `LandGraphEvaluationTests` locally before pushing. This method
throws `SkipTestException` on Windows (`The slow evaluator is a PATH shim named dotnet; Windows process
start resolves dotnet.exe.`). A Windows run of the class does not execute the shim.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, `Integration`,
`tests/Antiphon.Tests/Application/LandGraphEvaluationScriptTests.cs` (`LandGraphEvaluationTests`).

No selection ran on the push. The fixed CARD-1801 Merge subset does not name this class.
`tests/impacted-classes.json` has no row for `tools/Antiphon.LandGraphAudit.Core/**` or
`scripts/audit-land-verification*.ps1`. The method is Linux-only, so a Windows local run of the class
cannot see it. The commit is the same day as this Debug, before a later nightly Integration pass.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tools/Antiphon.LandGraphAudit.Core/**`, `scripts/audit-land-verification*.ps1` ->
`/*/*/LandGraphEvaluationTests/*`

Proposed CARD-1801 selector row (not added in this test-only fix):

```json
{
  "id": "land-graph-precheck",
  "globs": [
    "tools/Antiphon.LandGraphAudit.Core/**",
    "scripts/audit-land-verification*.ps1"
  ],
  "classes": [
    "LandGraphEvaluationTests"
  ]
}
```

Run that filter on Linux. The class is about 10 minutes after a warm build (measured 10m 22s, 155 tests)
and is serial under `ProcessSpawnLimit`. The one method is about one minute. A Windows run skips it.

## Exemption

same cause as an already written note: none.
