# Why missed: sibling-inspection wall-clock race in antiphon-all-group-24

One committed note for the `antiphon-all-group-24` failure of the Windows Full rc run
`f2de5b30c626461099fbfbb73fd8ada8` on `rc/2026-11-26` (cut `29ba0c5002e6`, ended
2026-11-18T10:13:28Z, exit 1). Debug task `7d99d53a`.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

## Failure

Five rows in `tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs`:

| Row | Windows duration | Assertion |
|---|---|---|
| `C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics(True,inbox)` | 14.5 s | `terminal.Detail` should contain a sibling token |
| `C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics(True,modern)` | 14.2 s | same |
| `C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics(False,modern)` | 18.2 s | same |
| `C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics(False,inbox)` | 14.3 s | same |
| `HarnessBuiltLandKeepsLongSiblingInsideProductionInspectionDeadline` | 6.7 s | `siblings` should contain the long token |

Classification: **test defect (timing)**. The product behaves as designed.

`AgentTaskLandService.CollectUnlandedSiblingsAsync`
(`server/Application/Services/AgentTaskLandService.cs:1101`) gives the whole sibling inspection one
`WorktreeBaseInspectionTimeoutSeconds` deadline. The default is 5 s
(`server/Application/Settings/GitSettings.cs:46`), set at `AgentTaskLandService.cs:1137-1140` (`CancelAfter` on line 1138). When the
deadline cancels, the method stops and adds `sibling inspection incomplete (inspection_timeout)` or
`sibling inspection unknown (inspection_timeout)` (`:1147`, `:1161`, `:1179`,
`:1202`). Siblings that were not reached are not reported. That is the bounded, truthful
degradation the product intends.

Each long sibling costs three real Git children. `merge-base --is-ancestor`, `rev-list` and `cherry`
are spawned through `DelegationWorktreeService.ObserveContainmentAsync`
(`server/Application/Services/DelegationWorktreeService.cs:182`). `show-ref` and `rev-parse` are answered
in process by libgit2 (`InProcessGitReads`). The Linux shim log below counted the three children. On
Windows, if libgit2 does not answer the 189-character refs, they may fall back to Git and add two more
children per sibling.

- `C443_D9` adds four long siblings, so about 12 Git children must finish inside 5 s.
- The harness witness held its first Git command for a fixed 3 s and then needed the sibling's
  children inside the remaining 2 s.

On Linux a spawn takes tens of milliseconds. On the loaded Windows desktop (Postgres, AppHost and rollout
activity shared the host) it took several hundred. The rows ran 14 to 18 s on Windows, against 10.5 s on
server2. Both tests asserted completeness against a wall-clock budget that they did not control.

### Reproduction on server2 (Linux), base `f4d7830332f652376a84c42b45bc5c8d0f4fa07f`

The filter was `/*/*/AgentTaskWorktreeLockOutcomeTests/(C443_D9_*)|(HarnessBuilt*)`. The shim was a `git`
script first on `PATH` that sleeps `$GIT_SHIM_DELAY` and then execs `/usr/local/bin/git`.

| Run | Result |
|---|---|
| plain | 5/5 passed (D9 10.5 s, harness 6.8 s) |
| shim 0.3 s per spawn | 5/5 passed (187 Git children, 3 per sibling inspection) |
| shim 0.7 s per spawn | **5/5 failed**, with the Windows assertions: `terminal.Detail should contain "<id>:feat/card-task-<id>sss..." but was actually "landed operation=... mode=CleanupRetry source=..."`, and `siblings should contain "<id>:feat/... " but was actually []` |

Shouldly cuts the actual `Detail` at 100 characters, so the Windows TRX cannot show the
`inspection_timeout` warning. The 0.7 s shim run reproduces both assertion messages in the same shape.

### Earlier occurrence

The same cause appeared in rc `5a126f6fc` on 2026-10-23
([groups 15-20 debug](2026-10-23-rc-5a126f6fc-qualification-groups-15-20-debug.md), group 17).
`C443_D9_...(True, modern)` failed there with `terminal.Detail should contain "ce257237:feat/card-task-ce257237sss..."`,
when the harness still used the 2 s null-settings fallback. Task `851cd564` gave the land fixtures the
production `GitSettings` (`9119490be46d6779da13e06229ea369d0251c414`, 5 s), and CARD-1749 made the
null fallback the same constant (`c4592abe2f2a0329bf44d3438e68916e7450d5f6`). That widened the margin but
left the race in place.

## Fix (task branch `feat/card-task-7d99d53a`, test-only)

- `LandingSafetyHarness.LandGitSettings` (new, default `new GitSettings()`, which is 5 s) is read on each land.
  It replaces the hard-coded `Options.Create(new GitSettings())`.
- `LandingGitFixture.FixtureGit.BeforeCommandAsync` (new) is a hook that receives the caller's
  cancellation token.
- `HarnessBuiltLandKeepsLongSiblingInsideProductionInspectionDeadline` now measures the deadline instead
  of racing it. The first Git command waits on the inspection token. The test asserts:
  - the cancellation arrived at least 4.5 s after the call;
  - `siblings` is empty;
  - the `sibling inspection unknown (inspection_timeout)` warning is present.

  Timers fire late under load, not early, so this lower bound is load-proof. A 2 s harness budget
  turns it red: `cancelledAt.Value should be greater than or equal to 00:00:04.5`.
- `DeliverAsync` sets `LandGitSettings.WorktreeBaseInspectionTimeoutSeconds = 60` when it adds siblings.
  This class proves outcome delivery and the sibling summary. The deadline is proved by the test above,
  by `SiblingInspectionDeadlineWitnessTests` and by the dispatch inspection classes. Every assertion is
  unchanged.

After the fix, the 0.7 s shim run gave 5/5 passed. A plain run of `(C443_D9_*)|(HarnessBuilt*)|(C443_*UnlandedSiblingWarning*)`
gave 13/13 passed.

Neither `master` nor `rc/2026-11-26` nor `rc/2026-11-27` (`e229fb4d3`) carries a fix. The three touched
files and `AgentTaskLandService.cs` are byte-identical on all three lines.

## Introducing commit/card

introducing commit/card: `0fdc139230900c27db65c4a8e5a7aca21fe17b12` (CARD-0443, the four-long-sibling
D9 fixture under the bounded inspection deadline) and
`488747e6950bafa6dd07a8e08f2360e0f67a24ce` (task `851cd564`, the 3 s-stall harness witness).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Not retrievable from this runner: `GET /api/agent-tasks/851cd564` gave no record for the short
  id, and no committed review record names task `851cd564` or the CARD-0443 D9 fixture commit. The
  orchestrator can fill in the review ids from the stage-outcomes API. Whatever those selections were,
  any Linux run of the class passes, because the race cannot lose at Linux spawn cost (see the table
  above).

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, categories
`Integration` and `Slow`, path `tests/Antiphon.Tests/Application/AgentTaskWorktreeLockOutcomeTests.cs`.

- **Linux lanes:** the class was in their selection. Linux Git spawns are cheap, so the wall-clock race
  cannot lose there (plain run 5/5 green; the race only loses at about 0.7 s per spawn).
- **Merge lane:** short tests only (2026-11-18), so this `Slow` class never runs.
- **Windows Full:** the only lane where the race loses, because it runs on the shared, loaded desktop.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Application/Services/AgentTaskLandService.cs`, `tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs` ->
`/*/*/AgentTaskWorktreeLockOutcomeTests/(C443_D9_*)|(HarnessBuilt*)`, run on Linux with a slow-Git shim:
a `git` script first on `PATH` that sleeps 0.7 s and then execs the real Git.

Detection improvement: a Linux "slow-git" probe row. It runs real-Git classes that share a product
wall-clock budget with Git children (sibling inspection, `WorktreeBaseInspection`, land budgets) under a
0.5 to 0.7 s per-spawn shim. That turns a Windows-only load race into a deterministic Linux red before rc.
The standing rule for such tests stays: measure a deadline with a lower bound on the cancellation token,
and give completeness tests an explicit budget, never a fixed stall inside the product budget.

## Exemption

none (first note for this cause; the 2026-10-23 group 17 row was the same cause before the 5 s change).
