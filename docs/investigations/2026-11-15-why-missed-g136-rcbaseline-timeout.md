# Why missed: g136 rc-baseline ScriptHarness timeout on Windows

Debug task 4a63f1cf, 2026-11-15. CARD-1947 (thin timeout headroom), CARD-1781 (why-missed write-up).

Windows Full rc run `160200cb` on `rc/2026-11-22` (`a71c45ab4d57`), chunk `antiphon-all-group-136`:
`RcReleaseBaselineTests.AncestorReleaseThenSameLineFallback` failed with
`TimeoutException: ScriptHarness test-rc-baseline.ps1 case AncestorReleaseThenSameLineFallback exceeded
00:00:45 execution budget` after 45.137 s (20:55:13.884-20:55:59.022Z). The other 91 tests of the chunk passed.

## Evidence

### The run (TRX `antiphon-all-group-136.trx`, `-tests.log`, `.timing.json`)

Chunk 20:46:27-20:56:15Z, build 526.9 s, run wall 589.3 s, 92 tests, p95 32.4 s. The chunk runs its
process-spawning tests one at a time (`ParallelLimiter<ProcessSpawnLimit>`): the failing case had the chunk
to itself (previous test ended 20:55:13.884, next started 20:55:59.028), so the only contention was the
desktop itself (AppHost, other tasks' builds, Defender). RcHistoricalAdmissionTests were not in this chunk.

| Class | Method | Run 160200cb (s) | Outcome |
|---|---|---|---|
| RcReleaseBaselineTests | MissingBaselineIsFull | 14.263 | Passed |
| RcReleaseBaselineTests | CrossPlatformMovedAndCorruptRefRefuse | 12.289 | Passed |
| RcReleaseBaselineTests | AncestorReleaseThenSameLineFallback | 45.137 | Failed (budget) |

### Native profile of the case (desktop, live run still going, one process at a time)

The harness script was run directly (`pwsh -File scripts/test-rc-baseline.ps1 -Case
AncestorReleaseThenSameLineFallback`) from an instrumented scratch copy that logs every
`Invoke-TestSelectionGit` call and its milliseconds, through `scripts/build-slot.ps1`.

Unfixed (master `a71c45ab4`), five runs in a row, script level (no TUnit host):

| Run | Wall (s) | Git calls | Seconds in git | Passed |
|---|---|---|---|---|
| 1 | 58.38 | 288 | 41.64 | yes |
| 2 | 56.37 | 288 | 39.76 | yes |
| 3 | 54.85 | 288 | 39.00 | yes |
| 4 | 55.51 | 288 | 40.05 | yes |
| 5 | 59.87 | 288 | 43.51 | yes |

Every unfixed run exceeds the 45 s budget; the case only passes on a quieter host (Linux: 3/3 alone in
Merge 237).

Where the time goes (unfixed run 1, 288 git calls, 41.6 s of 58.4 s in git):

- every git call is two Windows process spawns: the hardened runner first asks
  `git config --get-regexp` for filter/textconv drivers (0 ms cache by default), then runs the command;
  about 72 ms per spawn on the loaded desktop;
- the case makes five `Get-RcReleaseBaseline` proposals over up to four pointers; each eligible pointer is
  read through `Read-RcAdmission`, and the chosen one is read again. Each read lists the whole tree of the
  pointer's commit and of the current commit (`Read-RcInputInventory`) and twice for the algorithm digest:
  90 `ls-tree -r -z --full-tree` calls over only two commits, 63 `merge-base X X`, 46 `rev-parse X^{tree}`,
  21 `config --get remote.origin.url`, 14 `show -s --format=%ct`;
- fixture setup (repository, two S5b admission corpora) about 9 s; proposals 13.4, 12.9, 8.8, 4.1 and
  about 8 s; the rest is pwsh startup and PowerShell JSON/digest work.

No call hangs, no lock, no O(n^2) walk: the cost is linear in proposals x pointers x admission reads,
and every repeated read is of an immutable object (full commit id).

## Classification

(b) needless work, in the library rather than in the test: the same immutable git reads are repeated per
pointer and per admission hop, each paying two process spawns. (a) follows from it: at about 58 s natively
on a loaded Windows desktop the 45 s budget could not hold. Not (c): no defect in the selection logic.

## Fix (commit 623dbeda81f856de7fb0683cf9a0579327649693)

- `scripts/lib/test-selection.ps1` `Invoke-TestSelectionGit`: the existing per-process memo of immutable
  reads (cat-file of a full id, `merge-base --is-ancestor` of two full ids) also keeps answered
  `ls-tree -r -z --full-tree <id>`, `rev-parse <id>^{tree}`, `merge-base <id> <id>` and
  `show -s --format=%ct <id>`. Refusals and missing objects are still asked every time.
- `scripts/lib/rc-baseline.ps1` `Get-RcReleaseBaseline`: reuses the driver-settings answer for its own call
  (60 s per directory) and restores the caller's setting, exactly like `Get-TestSelection`.
Fixed code, same case, script level, five runs: 27.40, 29.07, 28.71, 28.46, 28.30 s; 89 git calls,
7.6-8.5 s in git (all 89 with the cached driver answer). The rest (about 20 s) is PowerShell JSON, digest and
manifest work, which is CPU bound and therefore what live-run load stretches.

Through TUnit on commit 623dbeda (45 s budget unchanged), `/*/*/RcReleaseBaselineTests/AncestorReleaseThenSameLineFallback`
five times in a row: 26.77 Passed, 46.97 Failed (budget), 33.20 Passed, 24.00 Passed, 46.23 Failed (budget). The
work reduction alone was not enough while the live run held the desktop CPU at up to 100%.

Budget (commit 13098e5dcd2af76ddc111aaf347a12f2c9c7a4ca, `RcReleaseBaselineTests` only): ScriptHarness execution
45 s -> 70 s, cleanup 10 s, method `Timeout` 60 s -> 90 s. Reason: about 2x the measured p95 of the fixed case on
the loaded Windows desktop (passing samples 24.0-33.2 s; p95 about 33 s). RcHistoricalAdmissionTests keep 45 s.

After both commits, through TUnit on 13098e5d:

| Selection | Run | Method | Seconds | Outcome |
|---|---|---|---|---|
| `/*/*/RcReleaseBaselineTests/AncestorReleaseThenSameLineFallback` | 1-5 | AncestorReleaseThenSameLineFallback | 21.96, 26.88, 26.07, 25.30, 25.09 | 5 Passed |
| `/*/*/RcReleaseBaselineTests/*` | 1 | CrossPlatformMovedAndCorruptRefRefuse | 11.87 | Passed |
| | | AncestorReleaseThenSameLineFallback | 24.72 | Passed |
| | | MissingBaselineIsFull | 12.88 | Passed |
| `/*/*/RcHistoricalAdmissionTests/*` | 1 | ShaLabelWithoutBuildProofIsUnproven | 6.61 | Passed |
| | | IncompleteChunkAndCaseAliasNeverCarry | 6.46 | Passed |
| | | ChangedEnvelopeHistoryAndOriginChainRefuse | 15.21 | Passed |
| | | FullProvenanceAdmitsAndReverifies | 10.57 | Passed |
| | | EveryIngredientMustMatch | 7.84 | Passed |
| | | ImportIsInertAndWritesUnprovenShard | 8.39 | Passed |
| | | CollisionAndAlgorithmCoverageForceFull | 16.55 | Passed |

ChangedEnvelopeHistoryAndOriginChainRefuse, 35 of 45 s alone on Linux in Merge 237 (CARD-1947), now takes
15.2 s on Windows: the memo also covers the admission reader. Linux was not run (no Linux host in this task).

## Introducing commit/card

introducing commit/card: d7c793f191d408dc166064e4fa92d3e33b19e366 and 9af3e309a3457da13969b51e987b18e4e5b8bb01
(CARD-1836 S7a, plan `docs/superpowers/plans/2026-11-15-card-1836-s7a-checkpoints.md`), landed by Merge 237
(4ce55f36794f3e2fa904d56ba980f4182d89ab3a, 2026-11-15).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 33039320 (the approving Review of S7a; scope as dispatched, not re-read here), selection
  `/*/*/RcReleaseBaselineTests/*` (plan CP-7) on Linux (server2) only.
  The plan's performance budget says "each method <=30s" provisionally and its OS audit records "metadata
  platform comparisons are exercised with Windows and Linux inputs, not native Windows execution".

## Outside the selection

why the failing test was outside that selection (lane/category/path): the class was in the Review
selection, but only on Linux; lane `Integration` (Windows Full only through the rc run),
category `Integration`, path `tests/Antiphon.Tests/Scripts/RcReleaseBaselineTests.cs`. The cost is
Windows-only process-spawn cost (two spawns per git call), so a Linux green said nothing about the
Windows margin. Merge 237 had already measured 35 of 45 s for an admission case alone on Linux and
filed CARD-1947 without a Windows sample.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/lib/rc-baseline.ps1` -> `/*/*/RcReleaseBaselineTests/*` run once natively on Windows, with the
per-method wall time compared against the ScriptHarness budget (fail the check below 1.5x headroom).

## Exemption

same cause as an already written note: none. CARD-1947 records the same thin-headroom family on Linux.
