# Why missed: CARD-1961 reset methods without C1722 Git budget rows

rc run `f2de5b30c626461099fbfbb73fd8ada8`, rc/2026-11-26 (cut
`29ba0c5002e642c4f4f91805be7e17660a40906d`), chunk `session-runner-all-group-1`:
`C1722GitTimeoutBudgetTests.Budget_table_covers_every_git_method` failed in 0.1 s with
`ShouldAssertException: recorded ... every Git-spawning method needs recorded measurements`.
Debug task 9aa4cf75. The fix changes one test file only.

## Diagnosis: deterministic on every platform, not Windows-specific

`Budget_table_covers_every_git_method`
(`tests/Antiphon.SessionRunner.Tests/C1722GitTimeoutBudgetTests.cs:122-151` at master `f4d78303`)
reflects every `[Test]` method of eleven Git-spawning classes, removes the nine `PureMethods()`,
and requires the result to equal the `[Arguments]` rows of `Git_budget_covers_measured_method`.
`C0824WorkspaceSlotResetTests` has 12 `[Test]` methods and the table had 10 rows for it. The two
missing rows:

- `C0824WorkspaceSlotResetTests.C1961_WarmReleaseClearsNestedKeepNamesUnderRejectedIgnoredParents`
- `C0824WorkspaceSlotResetTests.C1961_UntrackedFileBesideResidueStillQuarantinesUntouched`

The check is pure reflection with no platform branch, so it is red on Linux too. Reproduced on
server2 at master `f4d7830332f652376a84c42b45bc5c8d0f4fa07f` with
`--treenode-filter "/*/*/C1722GitTimeoutBudgetTests/Budget_table_covers_every_git_method"`:
1 total, 1 failed. Both missing names appear in the `should be` list and not in `recorded`.
This is a test defect: the product code is not involved. The test files are identical on
rc/2026-11-26, rc/2026-11-27 (`e229fb4d3`) and master. `git log --all -S` on the two method
names in `C1722GitTimeoutBudgetTests.cs` found no fix anywhere before this branch.

Fix: two rows using five Linux samples of the two methods
(`/*/*/C0824WorkspaceSlotResetTests/C1961_*`, server2 load 10-12). `WarmRelease` took 0.270 to
0.410 s, with p95 (n=5, so the max) 0.410 s. `Untracked` took 0.603 to 0.937 s in four samples
and 7.437 s in the fifth, a first-in-run warmup outlier, so its recorded p95 is 7.437 s. The class
`[Timeout(60_000)]` meets max(60 s, 2 x p95) for both. After the fix, the
`/*/*/C1722GitTimeoutBudgetTests/*` filter ran 100 tests: 100 passed, 0 failed.

## Introducing commit/card

introducing commit/card: `8559efcaf8bd003c7a4b78bede96fb31565b3a57` (CARD-1961)
(`test(CARD-1961): red witness for warm reset of nested keep-list residue`, which added both
methods; `1df526343d` adjusted one; product fix `72d98d936d`). It reached master in merge
batch 262 (C1192/CARD-1961/CARD-1883) and first entered an rc in rc/2026-11-25.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- CARD-1961 Code task fae5c49d ran the reviewed plan's checkpoint extract
  `docs/superpowers/plans/2026-11-17-card-1961-checkpoints.md`, rows CP-1 to CP-5. CP-1 was
  `C0824WorkspaceSlotResetTests/*`. CP-2 was Custody, Rehome, ColdReset, ProcessScan and
  `C1817RunnerRepositoryMaintenanceTests`. CP-3 and CP-4 were the guard union. CP-5 was
  `TestTimingOperationLabelTests` and `ImpactedClassesSelectorTests`. None of these rows
  names `C1722GitTimeoutBudgetTests`.
- Merge batch 262 ran the 2026-11-18 short Merge set: each subject's own witness rows plus the
  `-MergeSubset` selection. No committed Review report for the CARD-1961 code is in the tree.
  The plan review is `docs/superpowers/reviews/2026-11-17-card-1960-1961-plan-review.md`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): SessionRunner Unit lane,
category `Unit`, `tests/Antiphon.SessionRunner.Tests/C1722GitTimeoutBudgetTests.cs`.

The selector already knew about this test. On the CARD-1961 range,
`pwsh -NoProfile -File scripts/impacted-classes.ps1 -Range 8559efcaf8^..72d98d936d`
prints
`/*/*/(C0824WorkspaceSlotProcessScanTests*)|(C0824WorkspaceSlotRehomeTests*)|(C0824WorkspaceSlotResetTests*)|(C1722GitTimeoutBudgetTests*)|(C1817RunnerRepositoryMaintenanceTests*)/*`.
`C1722GitTimeoutBudgetTests` comes from the CARD-1820 reverse edge in
`tests/derived-impact-index.json`, where `C0824WorkspaceSlotResetTests.cs` lists
`C1722GitTimeoutBudgetTests`. The CP-2 row copies the other classes of that output and leaves
out C1722. The Merge did not catch it either, because `-MergeSubset` deliberately emits only the
hand table plus the fixed subset. Derived edges go to the Code and Review output only
(`scripts/impacted-classes.ps1` header, CARD-1820). `tests/impacted-classes.json` has no row that
maps the C0824 slot or C1677 retention test files to C1722. The Linux nightly and rc Full lanes
would have run the class and failed it on Linux too. This debug did not check whether an earlier
run of rc/2026-11-25 reached the class.

The lane that catches it pre-land is the Code/Review Final scope, if that scope runs the
`-Range` selector's derived classes as written. This check is a 0.15 s reflection test.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlot*Tests.cs`, `tests/Antiphon.SessionRunner.Tests/C0824RunnerBranchRefsTests.cs`, `tests/Antiphon.SessionRunner.Tests/C1677Retention*Tests.cs` -> `/*/*/C1722GitTimeoutBudgetTests/*`

Concrete detection improvement: when a plan's checkpoint table omits a class that the
`-Range` selector names in `derivedClasses`, refuse the plan at import or in Review. Plans
otherwise hand-copy the selector output and can drop a reverse edge, as CP-2 did here. A
cheaper alternative is to add reflection-only budget/roster guards such as C1722 to
`-MergeSubset` whenever the derived index names them, because they cost well under a second.
This note does not change `tests/impacted-classes.json`. The row above is the proposal.

## Exemption

none
