# Why missed: group-56 C0824 ref-only receipt timeout

One committed note for the rc/2026-11-15 run 6591e378 chunk `antiphon-all-group-56` failure of `C0824RefOnlyReceiptTests.Settlement_note_reaches_caller_and_recovers_handoffs`. The Windows TRX extract is `docs/investigations/2026-11-10-run-6591e378-group-56-c0824-ref-only-receipt-windows-failure.md` on `origin/feat/card-task-89a623fb` at `1ab323b15`. The failure message was `TimeoutException: Test 'Settlement_note_reaches_caller_and_recovers_handoffs' timed out after 00:01:20` with `Task Status: WaitingForActivation`. Duration `00:01:21.0422754`. Stdout was only the phase id. No Shouldly text. CARD-1781.

Classification: test fixture timing defect. The nine settlement cuts share one class budget. Linux completions of the same method are 42.739s alone and 26.546s beside the group-56 filter, both inside the old 80s budget. The Windows sample was still awaiting when that budget fired. Product receipt code is unchanged.

## Introducing commit/card

introducing commit/card: `917e7113b572e354b60485d7c3be6478cb1038e0` (CARD-0824)

That commit added `Settlement_note_reaches_caller_and_recovers_handoffs` and the provisional `[Timeout(80_000)]`. It is an ancestor of rc cut `ac6d00022e940ee3b90584c7d313567b4491c498`. Later edits through `ce881d97a94d44435729635ba2b5596b0e8d3761` (2026-11-06, rebuild recipient queue graphs) kept the same 80s budget. The receipt test blob `7b85b2b48f55b884a85a5d5c4c1d8f468bad7541` matches from that rc cut through `origin/master` `3fd231a2e498e463443079c40cf38711c0be8504`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review commit `c34cd706a4bfcb5dc73d022ac2ebe80f7f038f97` on `feat/card-task-fd0ebdc3`, round 1, scope Interim, no blockers. Reviewed source `de1963dd09e5c12951aa1a178cb833c85e69bda4`. Checkpoint run `20261106-125643-ec3f`: CP-1 10/10, CP-2 8/8, CP-3 7/7. CP-2 filter `/*/Antiphon.Tests.Application/(C0824RefOnlyReadTests*)|(C0824RefOnlyDeliverableTests*)|(C0824RefOnlyReceiptTests*)/*` (R1..R3, B1..B4, W1). Run `20261106-130524-57e1`: CP-4 7/7, CP-5 47/47, CP-6 27/27. PC-1..PC-161, Full/Final, native N1, and Unit shards were not run. Operator accepted that Interim for the master-only land (`f2a87256f`, `.antiphon/task-2b1406ee.md`).

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was inside CP-2. Lane Integration, category Slow, path `tests/Antiphon.Tests/Application/C0824RefOnlyReceiptTests.cs`.

CP-2 ran this method on Linux with the read and deliverable classes, and it passed. That selection does not run it beside `GitProcessSpawnLimit` (`AgentTaskLandNestedRepositoryCleanupTests` and `RunnerTaskSettlementTests`). The failing context is the Windows rc chunk filter `/*/Antiphon.Tests.Application/(AgentTaskLandNestedRepositoryCleanupTests*)|(C0824RefOnlyReceiptTests*)|(RunnerTaskSettlementTests*)/*`. No Windows pre-land selection ran this method. The 80s figure was a provisional pack budget, not 2x a measured completion beside that lane.

A Linux rerun of that same group-56 filter on 2026-11-11 passed 43/43. This method's TRX duration was `00:00:26.5456048`. An earlier alone run on the same host was `00:00:42.7387492` at loadavg 14.8 22.9 24.4. Git-lane neighbors in the Linux group-56 run finished within 9.5s, so they did not occupy the host for the whole method. On Windows the same C971 methods took 1.9x-3.9x those Linux durations (median about 3.3x) and overlapped this method for the whole 81s. Scaling 26.546s by 3.3x is about 87s, which is why an 80s budget censors a pack that completes on Linux. Docker was up (container `69300dc55891`). The chunk is outside the 16:46Z-16:55Z Docker-down window. The failure text has no git stderr, `core.autocrlf`, ReadOnly, or path-form error. This is not an expected Windows skip: the test ran and failed.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Application/C0824RefOnlyReceiptTests.cs` -> `/*/Antiphon.Tests.Application/(AgentTaskLandNestedRepositoryCleanupTests*)|(C0824RefOnlyReceiptTests*)|(RunnerTaskSettlementTests*)/*`

A class-alone filter passes in about 43s on Linux and would not have caught this miss. The group-56 filter above is the selection that failed on Windows. On this Linux host it also passes (43/43, this method 26.546s), so a green Linux count does not prove the Windows budget. Read this method's TRX duration from that filter and require the class timeout to be at least 2x a completion measured while the git lane is still overlapping, including a Windows completion when the rc host records one. This note does not edit `tests/impacted-classes.json`.

## Exemption

same cause as an already written note: none

`docs/investigations/2026-11-10-why-missed-c0824-migration-hook-timeout.md` is a different wait: `C0824RefOnlySnapshotMigrationTests` baseline `MigrateAsync` against a 60s hook.

## Repair

Test fixture only. No product code changed. `[Timeout(170_000)]` is 2x the censored Windows lower bound of 81.042s. Each cut writes `c0824-receipt-cut=<name>` and flushes stdout before its first await, so the next censor names the settlement that was in flight. The 170s budget is provisional until a Windows completion of this method is collected.
