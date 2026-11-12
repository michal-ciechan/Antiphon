# Why missed: g133 ReleaseSelectionCli red (rc run ae84478d)

Run `ae84478d`, `rc/2026-11-17` = `3db1a43b02246706dc28075fce7e7b5e84804ddb`, chunk `antiphon-all-group-133`:
`ReleaseSelectionCliTests` 1 failed on native Windows. The Windows message extract (sibling desktop read) was
not on origin when this was written; the cause below is reproduced on Linux and is platform-independent.

## Failure and cause

`ReleaseSelectionCliTests.BaselineMatchesWorker` (`scripts/test-release-selection-cli.ps1 -Case
BaselineMatchesWorker`) is red on every platform at master `b02f110fb`: 12 passed, 5 failed of 17 rows. The
five rows are the ones that read a full-release receipt (`receipt`, `receiptIncompatible`, `archiveChoice`,
`archiveFiltered`, `archiveIncompatible`), each `route=worker expected=cli` with identical answers and trees.

The compiled `select-release` (`scripts/lib/Antiphon.Scripts/Baseline.cs`, `PinnedPolicy`) mirrors
`Get-ReleaseAuthorityPinnedPolicy` (`scripts/lib/release-authority.ps1`). Its required rc suite set stopped at
the CARD-1564 observability assembly. The worker also adds `server-git` (CARD-1546), `resilience` (CARD-1829)
and `git-in-process` (CARD-1830) once their mirrored projects are declared in `projectDispositions`. With those
suites in the rc profile, the CLI refused every receipt with `policy-rc-suites` (exit 10) and the bridge fell
back to the worker. The answers stayed correct (the worker decides), so this was a route/perf regression that
the parity harness caught, not a wrong selection.

Classification: PRODUCT DEFECT in the scripts CLI (not Windows-specific, not a flake, not a fixture defect).

## Fix

`c81492732` (feat/card-task-2377d73d): `Baseline.cs` adds each mirrored project's suite once
`projectDispositions` declares it, in the worker's order (observability, server-git, resilience,
git-in-process). No assertion or timeout changed.

Proof (server2 Linux, `c81492732`): harness `BaselineMatchesWorker` 17/17, `CliMatchesWorker` 16/16;
`dotnet run --project tests/Antiphon.Tests` with
`/*/*/(ReleaseSelectionCliTests*)|(TestLaneCategoryGuardTests*)|(ProcessSpawnLimitTests*)/*`: 60 passed,
0 failed (2 + 1 + 57). Red before the fix: the same harness at `b02f110fb`, 5 failed of 17. Native Windows
proof is still owed (the same class on the desktop lane).

## Introducing commits

- `6f8741d87` "perf(scripts): select-release answers baseline attempts" (no card id; one of the operator perf
  session's commits, `Claude-Session` 01CTfMEZ...). It was authored on 2026-11-10 at
  09:36, before CARD-1546 landed (`9e7016e2f`, committed 2026-11-10 12:17), and was rebased onto it
  (committed 2026-11-11 10:43). Its port of the rc suite rule never had `server-git`. The parity harness was
  green on the pre-rebase base and was not re-run after the rebase.
- CARD-1829 `1b16bcfe7` (resilience) and CARD-1830 `0686117e5` (git-in-process) each extended
  `release-authority.ps1` without the C# mirror. rc/2026-11-17 carries 1546, 1829 and `6f8741d87`, not 1830;
  master carries all three.

## Approving Reviews and their selection

- `6f8741d87`: no card id and no land record found; it sits on master's first-parent line as a direct push, so no
  pipeline Review selection applies.
- CARD-1546 (plan `docs/superpowers/plans/2026-11-09-card-1546-server-git-project.md`, CP-1..CP-10): the
  ServerGit witnesses, layering, process callers, index lock, `TestSelectionCatalogTests`/`TestSelectionTests`
  C1313 rows, `ImpactedClassesSelectorTests` seed row and land-graph `-Check`. It landed before the CLI
  baseline port existed, so no selection could have caught it then.
- CARD-1830 (plan `docs/superpowers/plans/2026-11-11-card-1830-gitinprocess-tests.md`): CP-10
  `policy-scripts` ran `ReleaseBranchModelGateTests`, `ReleaseGatePlatformTests`, `TestSelectionCatalogTests`,
  `TestSelectionTests`, `ChangeAwareReleaseTests`, `RcFailedFirstExecutionTests` and `RcFailureHistoryTests`
  method rows. CARD-1829 had the same shape.

## Why the failing test was outside the selection

`ReleaseSelectionCliTests` reaches the policy and `release-authority.ps1` only at run time: the harness copies
`tests/test-execution-policy.json` from the repository and dot-sources the release libraries. Nothing in the
C# test names those paths, so `tests/derived-impact-index.json` has no edge from either file to the class
(the policy file's only edges are `ImpactedClassesSelectorTests`, `TimeoutAuditTests`,
`WindowsUnitPlatformAdmissionTests`), and `tests/impacted-classes.json` has no hand row for the compiled
command. A card that adds an rc suite changes exactly those two files and never selects the parity test.

## Cheapest pre-land check

One `tests/impacted-classes.json` kind (it runs in about 3 minutes on server2 per case; both cases ~6 min):

```json
{
  "id": "release-selection-cli",
  "globs": [
    "tests/test-execution-policy.json",
    "tests/test-selection-policy.json",
    "scripts/lib/release-authority.ps1",
    "scripts/lib/release-selection.ps1",
    "scripts/lib/test-selection.ps1",
    "scripts/lib/Antiphon.Scripts/**",
    "scripts/test-release-selection-cli.ps1"
  ],
  "classes": [
    "ReleaseSelectionCliTests"
  ]
}
```

Adding the row changes the derived index's cap slots for those paths, so it needs a derived-index regeneration
in the same change; it is left to a follow-up card rather than folded into this Debug fix.
