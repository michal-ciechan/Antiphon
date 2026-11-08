# Why missed: land-graph land refresh

One committed note per new rc-chunk cause. Path: `docs/investigations/2026-11-08-why-missed-land-graph-land-refresh.md`.
The Debug done report carries the same facts under a `WHY-MISSED` heading.
CARD-1781. Debug task 5776e965.

Qualification run 4e0d1159, chunk `antiphon-all-group-85.trx`, rc/2026-11-11 at
`3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e` (equal to master at the cut). The
desktop files
`C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb\logs\2026-11-08-0936-4e0d1159\antiphon-all-group-85.trx`
and `antiphon-all-group-85-tests.log` are not mounted on this runner. The chunk
summary supplied with the brief is 12 failed in `LandGraphLandRefreshTests`.
The per-test message and stack for run 4e0d1159 were not readable here.

The class file and `scripts/land-graph-land-refresh.ps1` /
`scripts/audit-land-verification-graph.ps1` are unchanged from that cut to
master `f54741ef3bc08baa0493f9e0bebd2425acab6fb5`. A leased Linux run of the
class at that master SHA passed 14/14. No product or test change.

## Introducing commit/card

introducing commit/card: `7ddd79885b87470da1ad2a971c01a7605160bc78` (CARD-1753)

That commit is the cherry-pick of `e495dc189ba204fe08fbf900a0fe6b7f585cf1f0`,
which adds `scripts/land-graph-land-refresh.ps1`. The failing witnesses were
introduced in the parent commit
`e2358f2ddc375bca3bb6a0e883895e31be9c7b8d` (cherry-pick of
`7f0cacf451654f5de83cd86a5a486d6781e5bfa7`). The pins-path repair
`3fa874306a38238505aeba66c146213b472e5ad3` (cherry-pick of
`7d3a9763f2242aa23c420e2a5f7671ba763627d1`) and the closed-list tip
`cfb9c91b7cf2154a64ca75ac1c08583aa5ccce25` (cherry-pick of
`6d6700b5d33101edfd7e500b012cca67618abb51`) are in the same master batch and
are what the rc cut ran. None of these commits are ancestors of the previous
qualification source `74a8004f405bc54666fea33889839f0ff4d62ec7` (run e737e9fe,
rc/2026-11-10). The class did not exist on that run.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review `af70c827-2d79-49b5-8767-cdd1708fbd3a`, scope Full (recorded
  `ordinaryScopeCompleted=Full`, round Final, outcome Clean), subject
  `a4d84751-bbd6-4f8c-91da-2ddb8c55d7c7`, reviewed source
  `6d6700b5d33101edfd7e500b012cca67618abb51`, runner server2, observed
  platform linux. Checkpoint run 20261108-042252-c6c6, one run:
  CP-1 `LandGraphLandRefreshTests` plus `OrchestratorBundleOwnershipTests`,
  `InstructionBundleTests`, `StandingPipelinePolicyDocumentationTests`, and
  `CheckpointRepeatDocumentationTests` (116 passed, 0 failed);
  CP-2 `LandGraphEvaluationTests` (147 passed, 0 failed);
  CP-3 guard batch, eight classes (75 passed, 0 failed).

Earlier reviews did not approve. Review `320f7bd0-13eb-44a1-9db3-f23b2d00260b`
was Interim and Found at `dc95f0b02fff038d08304c1c6b9f7c58f41bda58` (linux):
CP-1 touched Unit classes including the 12 refresh witnesses (113 passed) and
CP-2 the guard batch (75 passed). It did not run the real audit script.
Review `eb353c94-595d-4ba9-a9ee-a8e09da47c39` was Interim and Found at
`7d3a9763f2242aa23c420e2a5f7671ba763627d1` (linux): CP-1 touched classes
116/116, CP-2 guard batch 75/75. `LandGraphEvaluationTests` was not in that
diff-only selection.

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was
not outside the class filter. `LandGraphLandRefreshTests` is Unit, path
`tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs`, and it was
inside CP-1 of the approving review. That selection ran on the Linux server2
lane and passed. The failing lane is native Windows. The previous Windows rc
run (e737e9fe) could not have run this class: the commits above are not in
`74a8004f405bc54666fea33889839f0ff4d62ec7`. No earlier native Windows rerun of
this class was owed and then skipped. Run 4e0d1159 is the first native Windows
execution. The class has 14 tests at this SHA. The chunk summary names 12
failures, so two cases in the class passed or were not in that chunk. Which
two, and the shared message, are in the unreadable TRX.

Linux at master did not reproduce a failure, so no repair was made. A
candidate that fits a 12-and-2 split, and is not demonstrated: every case
except `instructions_say_the_land_path_refreshes_and_bare_update_stays_forbidden`
(no script) and `current_graph_does_not_refresh` (returns on check exit 0
before reading the check text) needs the audit child's captured text.
`scripts/land-graph-land-refresh.ps1` treats a non-zero check whose text does
not match a known phrase as `reason=unapproved-check` (line 261) and replaces
any thrown child error with `land-graph refresh failed` (line 383).
`Invoke-Child` sets `StandardOutputEncoding` to UTF-8 with no BOM and reads
the pipe after `WaitForExit`. The audit script's own child runner does not set
that encoding. That is a Windows pipe-decoding suspect only. The one-second
and other bounds were not widened. No assertion was changed.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs` -> `/*/*/LandGraphLandRefreshTests/*`

Also select `scripts/land-graph-land-refresh.ps1` when that script changes.
A Linux execution of the filter at `f54741ef3bc08baa0493f9e0bebd2425acab6fb5`
was 14 passed, 0 failed (1m 30s) and would not have turned this rc red. The
check that would have is the same filter on the native Windows qualification
host. The guard batch that accompanied the approving review stayed green on
Linux here: 75 passed, 0 failed.

## Exemption

same cause as an already written note: none
