# Why missed: land-graph land refresh

One committed note per new rc-chunk cause. Path: `docs/investigations/2026-11-08-why-missed-land-graph-land-refresh.md`.
The Debug done report carries the same facts under a `WHY-MISSED` heading.
CARD-1781. First pass was Debug task 5776e965, which could not read the Windows logs.
This pass is Debug task d4e3516d. The Windows evidence is
`docs/investigations/2026-11-08-run-4e0d1159-group-85-windows-failure.md` on
`origin/feat/card-task-cee910dc`.

Qualification run 4e0d1159, chunk `antiphon-all-group-85.trx`, rc/2026-11-11 at
`3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e` (equal to master at the cut). 12 failed
of 508 in the chunk. All 12 are `LandGraphLandRefreshTests`. Two cases in that
class passed: `instructions_say_the_land_path_refreshes_and_bare_update_stays_forbidden`
and `real_audit_changed_definition_refreshes_commits_only_land_graph_files_and_second_check_exits_zero`.

The class file and `scripts/land-graph-land-refresh.ps1` /
`scripts/audit-land-verification-graph.ps1` are unchanged from that cut to
master `f54741ef3bc08baa0493f9e0bebd2425acab6fb5`.

## Introducing commit/card

introducing commit/card: `e2358f2ddc375bca3bb6a0e883895e31be9c7b8d` (CARD-1753)

That commit adds `LandGraphLandRefreshTests`, including `Fixture.Dispose` calling
`Directory.Delete` and `real_audit_property_refusal_does_not_refresh`. It is the
parent of `7ddd79885b87470da1ad2a971c01a7605160bc78`, the cherry-pick of
`e495dc189ba204fe08fbf900a0fe6b7f585cf1f0`, which adds
`scripts/land-graph-land-refresh.ps1`. The pins-path repair
`3fa874306a38238505aeba66c146213b472e5ad3` and the closed-list tip
`cfb9c91b7cf2154a64ca75ac1c08583aa5ccce25` are in the same master batch and are
what the rc cut ran. None of these commits are ancestors of the previous
qualification source `74a8004f405bc54666fea33889839f0ff4d62ec7` (run e737e9fe,
rc/2026-11-10). The class did not exist on that run.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review `af70c827-2d79-49b5-8767-cdd1708fbd3a`, scope Full (recorded
  `ordinaryScopeCompleted=Full`, round Final, outcome Clean), subject
  `a4d84751-bbd6-4f8c-91da-2ddb8c55d7c7`, reviewed source
  `6d6700b5d33101edfd7e500b012cca67618abb51`, runner server2, observed
  platform linux. Checkpoint run 20261108-042252-c6c6: CP-1
  `LandGraphLandRefreshTests` plus four instruction classes (116 passed, 0 failed);
  CP-2 `LandGraphEvaluationTests` (147 passed, 0 failed); CP-3 guard batch,
  eight classes (75 passed, 0 failed).

Earlier reviews did not approve. Review `320f7bd0-13eb-44a1-9db3-f23b2d00260b`
was Interim and Found at `dc95f0b02fff038d08304c1c6b9f7c58f41bda58` (linux).
Review `eb353c94-595d-4ba9-a9ee-a8e09da47c39` was Interim and Found at
`7d3a9763f2242aa23c420e2a5f7671ba763627d1` (linux).

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was
not outside the class filter. `LandGraphLandRefreshTests` is Unit, path
`tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs`, and it was
inside CP-1 of the approving review. That selection ran on the Linux server2
lane and passed. The failing lane is native Windows. Run 4e0d1159 is the first
native Windows execution. Linux `Directory.Delete` ignores a file's read-only
bit, and this host has no system `core.autocrlf=true`.

## Windows causes (group 85)

Two families. Both are test fixtures. No product assertion was loosened.

Family A, 11 tests. Message: `UnauthorizedAccessException: Access to the path
'47a139e2691d0b91fe8cfc80f190e885a30de8' is denied` (object name varies) at
`Fixture.Dispose` (`Directory.Delete(Root, recursive: true)`). Git for Windows
writes loose objects `-r--r--r--`. The stack is cleanup, so those 11 results do
not show whether the assertions passed. `LandGraphEvaluationTests.AuditRepo`
already deletes with `StartRefGit.DeleteDirectory`, which clears the read-only
bit before `File.Delete`. The refresh fixture did not. Linux cannot make
`Directory.Delete` fail on a read-only file in a writable directory, so the red
witness asserts the dispose delegate is `StartRefGit.DeleteDirectory` and then
removes a read-only `.git/objects` file through it.

Family B, 1 test: `real_audit_property_refusal_does_not_refresh`. Message:
`check.Text` should contain `-Update cannot approve it` but was
`audit refused: source differs from HEAD; commit source and pins before auditing`.
That string is thrown only by `Assert-HeadSource` in
`scripts/audit-land-verification-graph.ps1` when `git diff --quiet HEAD` is
nonzero. The output has no pre-check text, so the refusal is that check, not
the property refusal the test expects. The desktop system `core.autocrlf` is
`true`. `AuditRepo.CreateAsync` copied `global.json` from a `w/crlf` checkout,
wrote `Antiphon.sln` with explicit CR LF, and committed before
`ConfigureGit` set `core.autocrlf=false`. A scratch diff on that sequence
returned 0 because the index stat cache hides the byte difference until a
tracked file is re-stat'd. The sibling
`real_audit_changed_definition_refreshes_commits_only_land_graph_files_and_second_check_exits_zero`
uses the same create path and the same first `Assert-HeadSource` and passed, so
this note does not claim the CR LF sequence alone failed line 203. The trigger
that re-stat'd a tracked file on that one test is unproven. The fixture is now
host-independent anyway: LF writes, `.gitattributes` `* -text`, and
`core.autocrlf=false` plus `core.safecrlf=false` before the first add. The
failed-first rerun on the next Windows cut is the proof.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

| path | class filter | windows |
|---|---|---|
| `tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs` | `/*/*/LandGraphLandRefreshTests/*` | required |

Also select `scripts/land-graph-land-refresh.ps1` and
`tests/Antiphon.Tests/Application/LandGraphEvaluationTests.cs` when the refresh
script or `AuditRepo` changes. A Linux run of the class filter cannot see
either Windows cause. A new test class that creates git repos needs this
Windows native lane: the CARD-1782 selector row above, with the Windows flag
set, on the native qualification host. Linux approval of the same filter is
not that lane.

## Exemption

same cause as an already written note: none. This file is that note. Task
5776e965 recorded the same path before the Windows log was readable and made
no repair.
