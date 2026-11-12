# Run ae84478d group 133: ReleaseSelectionCliTests.BaselineMatchesWorker red on Windows

- Run: ae84478d (Windows Full qualification, desktop), candidate rc/2026-11-17 = 3db1a43b02246706dc28075fce7e7b5e84804ddb
- Chunk: antiphon-all-group-133 (55 tests: 54 passed, 1 failed). Started 2026-11-12T01:24:36Z, completed 01:36:05Z; build 475 s, run wall 689 s.
- Evidence read (read-only): `antiphon-all-group-133.trx`, `antiphon-all-group-133-tests.log`, `antiphon-all-group-133.timing.json`
  in `C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d\`.
- Verdict: **deterministic regression** (semantic merge conflict between two commits in one batch). Not timing, not environment, not Windows-specific.
- Fix: `scripts/lib/Antiphon.Scripts/Baseline.cs` on `feat/card-task-c2cbe0de` (commit cc1a00bb727b42ab16127f312a72952c43981312).

## The failure

`Antiphon.Tests.Scripts.ReleaseSelectionCliTests.BaselineMatchesWorker`, 3 m 12.109 s, Failed.

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: result.ExitCode
    should be
0
    but was
1
...
C487: 12 passed, 5 failed, 17 rows
  RS BaselineMatchesWorker.receipt : route=worker expected=cli
  RS BaselineMatchesWorker.receiptIncompatible : route=worker expected=cli
  RS BaselineMatchesWorker.archiveChoice : route=worker expected=cli
  RS BaselineMatchesWorker.archiveFiltered : route=worker expected=cli
  RS BaselineMatchesWorker.archiveIncompatible : route=worker expected=cli
C487 HARNESS EXIT CODE: 1  (FAIL - do not report this run as green)
```

For each of the five rows, the `default:` and `worker:` answers are byte-identical, and the harness prints `trees equal: True`.
Expected vs actual is therefore only the route: expected `cli` (the compiled `Antiphon.Scripts select-release` answers),
actual `worker` (the CLI exited non-zero and `New-ReleaseSelectionAttempt` fell back to `release-selection-worker.ps1`).
Example row values (receipt): `fallbackReasons=["change-aware-unqualified","selection-authority-changed","unknown-path:src/A/Program.cs"]`,
`baselineSha=d641a7493f0c...`, the same on both routes.

Stack top frames:

```
at Antiphon.Tests.Scripts.ScriptHarness.Validate(...) in tests\Antiphon.Tests\Scripts\ScriptHarness.cs:25
at Antiphon.Tests.Scripts.ScriptHarness.<>c__DisplayClass1_0.<RunHarnessCaseAsync>b__0(...) in ScriptHarness.cs:18
at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(...) in ScriptHarnessProcess.cs:132
```

No stderr, sharing-violation, timeout or poll-expiry lines are in the log. The only other output is
`C487 cleanup clearedReadOnlyFiles=81`.

## Grouping and bounding siblings

Rows that **passed** in the same case: projectAdd, projectRemove, projectRename, globEdges, fullMode, policyChange,
catalogChange, sameCommit, receiptMissing (route cli), and receiptCorrupt, notAncestor, staleBaseline (route worker, as expected).
The sibling case `ReleaseSelectionCliTests.CliMatchesWorker` passed (16 rows, 80.7 s), and so did the
`ReleaseSelectionHopSeedTests` (3).

The five failing rows are exactly the scenarios that **read a valid baseline receipt** written by `Write-RSReceipt`.
`receiptCorrupt` also writes one, but it is refused earlier (`baseline-digest:authority/ledger.json`) and is expected on the worker.
`receiptMissing` and the explicit-B rows never reach the receipt reader. So the cause lies inside the CLI receipt path, after the digest checks.

## What ran at the same time

Nothing relevant. The TRX timeline shows the script-harness tests running one at a time:
RcQualificationWatcherScriptTests.Supervisor_detaches... ran 01:30:22-01:31:20, BaselineMatchesWorker ran alone
01:31:20-01:34:33Z, then CliMatchesWorker ran 01:34:33-01:35:53. There was no concurrent load to blame.

## Cause

`Baseline.PinnedPolicy` (scripts/lib/Antiphon.Scripts/Baseline.cs, reached only from `Receipt`) rebuilt the required rc suite set from
the fixed `RcSuites` list plus `markdown-pdf` and `observability`. It then refused (`NotHandled baseline:policy-rc-suites`) unless the
policy's rc `requiredSuites` matched that set exactly. Its PowerShell twin, `ConvertFrom-ReleaseAuthorityPolicyBytes`
(scripts/lib/release-authority.ps1), also adds:

- `server-git` once `tests/Antiphon.Server.Git.Tests/...csproj` is declared (9e7016e2f, CARD-1546)
- `resilience` once `tests/Antiphon.Resilience.Tests/...csproj` is declared (1b16bcfe7, CARD-1829)
- on master only, `git-in-process` once `tests/Antiphon.GitInProcess.Tests/...csproj` is declared (0686117e5, CARD-1830)

The rc policy (`tests/test-execution-policy.json` at 3db1a43b0, which the harness copies into its fixture repository) requires
`[..., markdown-pdf, observability, server-git, resilience]`. The CLI therefore refused every receipt, and the worker answered instead.

This is the same family as group 131 (a hand-written suite list that missed the new mirrored test projects).
It is a different list, though: group 131 fixed `tests/fixtures/card1336-release-gate-followups.ps1` on feat/card-task-2d562b37,
and this one is the C# mirror in `Baseline.cs`. The two do not overlap.

## Fix

`Baseline.cs` now carries a `DeclaredProjectSuites` table of (project, suite) pairs in the PowerShell's order: observability,
server-git, resilience and git-in-process. Each suite is added when its project is in `projectDispositions`, the same rule as the
PowerShell. No assertion was changed. The table is conditional, so the same commit is correct on rc (which has no GitInProcess project).
`Baseline.cs` is byte-identical on rc 3db1a43b0 and on the fix base, so the commit cherry-picks cleanly onto rc. `git merge-tree` reports it landable on master b02f110fb.

## Native Windows proof (desktop)

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label ... -- pwsh -NoProfile -File scripts/test-release-selection-cli.ps1 -Case BaselineMatchesWorker`
at cc1a00bb7 (Windows 10 19045, pwsh 7): `C487: 17 passed, 0 failed, 17 rows`, `HARNESS EXIT CODE: 0`. The run waited 330 s for a build slot on the host memory floor.
A local red at the base was queued, but it was stopped while still waiting for a slot and never ran. The rc run's red above is the red witness.

## Why missed

1. **Semantic merge conflict inside one batch.** 6f8741d87 ("select-release answers baseline attempts") was written against
   the policy before the new suites existed. It was committed 2026-11-10 09:36:22Z, and 9e7016e2f (Server.Git suite) followed at 09:38:51Z.
   Neither branch saw the other. The two edit different files (`Baseline.cs` vs `release-authority.ps1` and the policy), so there
   were no conflict hunks to trigger conflict-hunk tests at Merge. 1b16bcfe7 and 0686117e5 then repeated the PowerShell-only
   registration pattern, and neither touched the C# mirror.
2. **The impact index cannot see the dependency.** `tests/derived-impact-index.json` has no edge from `scripts/lib/release-authority.ps1`,
   `scripts/lib/Antiphon.Scripts/Baseline.cs`, `scripts/lib/release-selection.ps1`, `scripts/test-release-selection-cli.ps1` or
   `tests/test-execution-policy.json` to `ReleaseSelectionCliTests`. The class reaches its script through a string literal, and
   `ScriptHarnessProcess.cs` is a capped path (36 classes, 11 owed). Impact-selected Merge and task checks never chose this test
   for the suite-registration commits.
3. **The per-suite registration checklist omits the CLI mirror.** The registration commits updated the .sln, release-authority.ps1,
   seed/history lists and eight test scripts, but not `Baseline.cs`, which re-implements the same rule in C#. Nothing points from
   the PowerShell rule to its C# twin, other than the comment this fix adds.

Follow-ups (not done here):
(a) Add impact edges from the files above to `ReleaseSelectionCliTests`, or make script-harness classes declare their script inputs.
(b) Drive the mirrored-project-to-suite rule from one source, such as the policy's `projectDispositions` plus a suite field, so that
`release-authority.ps1`, `Baseline.cs` and the hand-written test lists stop drifting. That is a card for the orchestrator.
