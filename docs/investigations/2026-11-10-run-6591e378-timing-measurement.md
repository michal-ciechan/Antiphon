# Run 6591e378 chunk timing against plannedSeconds

CARD-1842 slice S7. Read-only snapshot of the live Windows qualification. The run was still in progress when these numbers were taken, so remaining work and the ETA move as later chunks finish.

- Measured at: `2026-11-10T23:20:04.002Z`
- Run directory: `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378`
- Timing directory: `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\timing-6591e378c04d47bc898cf0927352b01b`
- Plan run id: `6591e378c04d47bc898cf0927352b01b`
- Source commit on the plan: `ac6d00022e940ee3b90584c7d313567b4491c498`
- Host: `DESKTOP-KTLKPIF`, 8 logical CPUs, 31.9 GiB RAM (34298142720 bytes)

## Definitions

- A completed chunk is a top-level `*.timing.json` whose `lastPhase` is `complete`. Any other phase is in flight and is left out of the wall distribution.
- Actual wall is `runWallSeconds`. Intra-chunk `p50Seconds` and `p95Seconds` are per-test percentiles inside that chunk. They are not the chunk wall.
- `plannedSeconds` comes from each `groups-planned-<suite>.json` group. The singleton `groups-planned.json` is the last suite copy and is not added again. The plan attributes those seconds to `scripts/lib/nightly-class-costs.json`.
- Ratio is `runWallSeconds / plannedSeconds` for plannedSeconds greater than zero. Percentiles are Excel PERCENTILE.INC (R-7, linear). The unweighted mean gives each chunk one vote. The duration-weighted ratio is `sum(wall) / sum(plannedSeconds)` over those same chunks.
- Over-estimate means the plan was high (`plannedSeconds - wall` large). Under-estimate means the plan was low (`wall - plannedSeconds` large).
- Observed concurrency is the overlap level that occupied the most time while at least one counted chunk was running. Intervals are timing `startedAt`..`completedAt` for completed chunks, and `startedAt`..snapshot for in-flight chunks. Ends sort before starts at the same timestamp. The naive ETA divides by that integer.
- Remaining planned seconds is the full `plannedSeconds` of every planned chunk that does not yet have a completed timing file. In-flight chunks are still inside that sum.
- Naive ETA = remaining planned seconds x unweighted mean ratio / observed concurrency. The p50 and p95 finish estimates use the same divisor and the unweighted percentile of the per-chunk ratio. That is a single scale factor applied to the whole remainder, not a confidence interval of the mean.

## Host and cost file

| Item | Value |
| --- | --- |
| Hostname | DESKTOP-KTLKPIF |
| Logical CPUs | 8 |
| RAM bytes | 34298142720 |
| RAM GiB | 31.94 |
| Plan costFileDigest | dc4cdfc6a55eab5a174f049d9a92af8c0b59ad3576fb72fa32ab1dc9ea58c934 |
| Source-commit blob SHA-256 | d6d38b7182f8ed5e5ad2f7cd1a992864ce6e5877490a8b5a72015a18ee24c627 |
| Source-commit blob matches plan | false |
| Source-commit platform | windows |
| Source-commit classes | 772 |
| Worktree cost file SHA-256 | dc4cdfc6a55eab5a174f049d9a92af8c0b59ad3576fb72fa32ab1dc9ea58c934 |
| Worktree cost file bytes | 386404 |
| Worktree cost file matches plan | true |
| Worktree platform | windows |
| Worktree classes | 772 |

Chunk plannedSeconds is the sum of per-class seconds from scripts/lib/nightly-class-costs.json (platform windows, one numeric seconds value on each of 772 classes). The plan costFileDigest matches this worktree copy of that file (386404 bytes). The blob of the same path at source commit ac6d00022e940ee3b90584c7d313567b4491c498 is a different file (d6d38b7182f8ed5e5ad2f7cd1a992864ce6e5877490a8b5a72015a18ee24c627, 772 classes, platform windows).

## Progress

| Item | Value |
| --- | --- |
| Plan files | 8 |
| Planned chunks | 1154 |
| Planned seconds (all chunks) | 184324.6 (2d 03h 12m 04.6s) |
| Timing files | 628 |
| Completed chunks | 627 |
| Completed and in the plan | 625 |
| Completed but not in the plan | 2 |
| In flight (timing file, phase not complete) | 1 |
| Remaining planned chunks | 529 |
| Remaining planned seconds | 82358.4 (0d 22h 52m 38.4s) |
| Duplicate plan keys skipped | 0 |
| Unreadable timing files | 0 |
| TRX reads that threw | 0 |

Done 625 of 1154 planned chunks. Remaining planned seconds 82358.4.

### In flight

| Chunk | Phase | Started (UTC) | Elapsed seconds | Planned seconds |
| --- | --- | --- | --- | --- |
| antiphon/all-group-45 | run | 2026-11-10T23:09:44Z | 620.0 | 900.0 |

### Completed timings with no plan row

| Chunk | Wall seconds |
| --- | --- |
| client/all | 364.1 |
| scripts/all | 111.6 |

## Wall distribution (every completed chunk)

Wall is `runWallSeconds`.

| n | sum seconds | min | p50 | p90 | p95 | max | mean |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 627 | 36421.8 | 1.1 | 8.9 | 156.2 | 340.0 | 1726.0 | 58.1 |

Sum of chunk walls is serial test time, 0d 10h 07m 01.8s. It is not calendar time.
Median (TRX finish-start minus runWallSeconds) is -0.875 seconds across completed chunks that had both.
buildSeconds is 655.862 on 628 of 628 timing files. The value repeats, so it is not added to wall.
TRX Counters on 625 completed chunks: executed 18067, passed 16947, failed 1120, notExecuted 26. timing.json testCount differed from Counters.executed on 27 chunks.

### Fifteen slowest completed chunks

| Chunk | Wall seconds | Planned seconds | Tests executed | Ratio | Intra-chunk test p50 | Intra-chunk test p95 |
| --- | --- | --- | --- | --- | --- | --- |
| antiphon/all-group-130 | 1726.0 | 900.0 | 438 | 1.918 | 1.412 | 15.602 |
| antiphon/all-group-132 | 956.2 | 81.5 | 137 | 11.733 | 1.188 | 32.317 |
| antiphon/all-profile-1486 | 868.5 | 22.0 | 1 | 39.479 | 858.064 | 858.064 |
| antiphon/all-profile-2176 | 747.9 | 257.8 | 4 | 2.901 | 183.785 | 194.665 |
| antiphon/all-group-126 | 638.6 | 900.0 | 37 | 0.710 | 0.956 | 88.484 |
| antiphon/all-group-129 | 599.4 | 900.0 | 49 | 0.666 | 7.042 | 48.856 |
| e2e/all-split-9 | 556.4 | 1200.0 | 2 | 0.464 | 233.304 | 321.335 |
| session-runner/all-group-4 | 542.6 | 66.0 | 130 | 8.221 | 0.164 | 10.214 |
| antiphon/all-group-134 | 540.8 | 889.7 | 356 | 0.608 | 0.107 | 9.528 |
| antiphon/all-group-131 | 529.2 | 248.5 | 196 | 2.129 | 1.301 | 7.093 |
| antiphon/all-group-48 | 515.9 | 900.0 | 47 | 0.573 |  |  |
| e2e/all-split-7 | 448.6 | 1200.0 | 2 | 0.374 | 199.241 | 248.061 |
| e2e/all-split-34 | 443.1 | 600.0 | 1 | 0.739 | 441.524 | 441.524 |
| antiphon/all-group-61 | 434.3 | 900.0 | 62 | 0.483 | 10.063 | 26.721 |
| e2e/all-split-11 | 429.0 | 1200.0 | 2 | 0.357 | 208.378 | 219.184 |

## Plan error

Ratios use 625 completed planned chunks with plannedSeconds > 0. Excluded: 0 with plannedSeconds <= 0, 0 with no wall.

| Statistic | Unweighted per chunk | Weighted by plannedSeconds |
| --- | --- | --- |
| Mean actual/planned | 3.836 | 0.353 |
| p50 ratio | 0.731 | 0.200 |
| p90 ratio | 9.197 |  |
| p95 ratio | 15.773 | 0.812 |

The weighted ratio is the scale that turns the sum of planned seconds into the sum of observed wall. The unweighted mean is the figure the naive ETA uses. Each chunk has one vote, so a short profile chunk moves the mean as much as a 900-second group.

### Short chunks and the unweighted mean

Short means plannedSeconds below 60. Long means 60 or more. Both sets are completed planned chunks with a ratio.

| Set | Chunks | Mean ratio | Sum planned s | Sum wall s |
| --- | --- | --- | --- | --- |
| Planned under 60 s | 467 | 4.957 | 4466.7 | 7232.3 |
| Planned 60 s or more | 158 | 0.523 | 97499.5 | 28713.8 |

The short set contributes 3.704 of the unweighted mean 3.836 (its ratios summed, then divided by every ratio chunk).

### Per suite

| Suite | Plan chunks | Done | Left | Sum planned s | Sum remaining s | Sum wall s | Mean ratio | p50 ratio | p95 ratio | Weighted ratio |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| agents-pty | 29 | 4 | 25 | 391.5 | 13.5 | 201.4 | 3.356 | 0.959 | 9.888 | 0.533 |
| antiphon | 881 | 539 | 342 | 119767.6 | 55272.4 | 25412.9 | 3.896 | 0.733 | 20.651 | 0.394 |
| e2e | 76 | 39 | 37 | 62670.0 | 26870.0 | 8317.8 | 0.234 | 0.184 | 0.383 | 0.232 |
| markdown-pdf | 12 | 0 | 12 | 9.5 | 9.5 | 0.0 |  |  |  |  |
| messaging | 1 | 0 | 1 | 0.0 | 0.0 | 0.0 |  |  |  |  |
| observability | 1 | 0 | 1 | 0.0 | 0.0 | 0.0 |  |  |  |  |
| pty-host | 53 | 2 | 51 | 77.0 | 26.5 | 40.1 | 0.550 | 0.550 | 0.954 | 0.795 |
| session-runner | 101 | 41 | 60 | 1409.0 | 166.5 | 1973.7 | 6.681 | 6.732 | 11.756 | 1.589 |

### Ten largest under-estimates (actual above plan)

| Chunk | Wall seconds | Planned seconds | Actual minus planned | Ratio | Tests executed |
| --- | --- | --- | --- | --- | --- |
| antiphon/all-group-132 | 956.2 | 81.5 | 874.7 | 11.733 | 137 |
| antiphon/all-profile-1486 | 868.5 | 22.0 | 846.5 | 39.479 | 1 |
| antiphon/all-group-130 | 1726.0 | 900.0 | 826.0 | 1.918 | 438 |
| antiphon/all-profile-2176 | 747.9 | 257.8 | 490.1 | 2.901 | 4 |
| session-runner/all-group-4 | 542.6 | 66.0 | 476.6 | 8.221 | 130 |
| antiphon/all-group-133 | 422.6 | 20.5 | 402.1 | 20.615 | 40 |
| antiphon/all-group-131 | 529.2 | 248.5 | 280.7 | 2.129 | 196 |
| session-runner/all-group-3 | 365.9 | 111.0 | 254.9 | 3.296 | 216 |
| antiphon/all-profile-1487 | 171.1 | 22.0 | 149.1 | 7.778 | 1 |
| antiphon/all-group-96 | 290.1 | 144.5 | 145.6 | 2.008 | 205 |

### Ten largest over-estimates (plan above actual)

| Chunk | Wall seconds | Planned seconds | Planned minus actual | Ratio | Tests executed |
| --- | --- | --- | --- | --- | --- |
| e2e/all-group-1 | 60.1 | 1500.0 | 1439.9 | 0.040 | 5 |
| e2e/all-split-63 | 155.6 | 1500.0 | 1344.4 | 0.104 | 3 |
| antiphon/all-group-17 | 86.9 | 1339.5 | 1252.6 | 0.065 | 54 |
| antiphon/all-group-21 | 43.4 | 1218.0 | 1174.6 | 0.036 | 94 |
| antiphon/all-group-124 | 371.5 | 1517.3 | 1145.8 | 0.245 | 8 |
| e2e/all-split-17 | 114.2 | 1200.0 | 1085.8 | 0.095 | 2 |
| e2e/all-split-1 | 139.3 | 1200.0 | 1060.7 | 0.116 | 2 |
| e2e/all-split-2 | 148.2 | 1200.0 | 1051.8 | 0.124 | 2 |
| e2e/all-split-3 | 150.4 | 1200.0 | 1049.6 | 0.125 | 2 |
| e2e/all-split-4 | 190.9 | 1200.0 | 1009.1 | 0.159 | 2 |

## Concurrency and ETA

| Item | Value |
| --- | --- |
| Completed intervals in the sweep | 627 |
| Completed chunks with no usable interval | 0 |
| In-flight intervals held open to the snapshot | 1 |
| Peak chunks overlapping | 1 |
| Time-weighted mean overlap (includes idle gaps in the span) | 0.998 |
| Time-weighted mean overlap while at least one chunk ran | 1.000 |
| Observed concurrency (mode while busy) | 1 |
| Share of busy time at that mode | 100.0% |
| Span of the sweep | 0d 10h 19m 05.0s |

Observed concurrency is 1: that many chunks overlapped for 100.0% of the time at least one chunk was running. Peak overlap was 1. The sweep span is 0d 10h 19m 05.0s and the sum of completed walls is 0d 10h 07m 01.8s.

| Estimate | Scale | Seconds from snapshot | Clock | Finish (UTC) |
| --- | --- | --- | --- | --- |
| Naive (mean ratio / observed concurrency) | 3.836 | 315922.5 | 3d 15h 45m 22.5s | 2026-11-14T15:05:26Z |
| p50 ratio / observed concurrency | 0.731 | 60166.4 | 0d 16h 42m 46.4s | 2026-11-11T16:02:50Z |
| p95 ratio / observed concurrency | 15.773 | 1299000.6 | 15d 00h 50m 00.6s | 2026-11-26T00:10:04Z |
| Weighted ratio / observed concurrency | 0.353 | 29033.7 | 0d 08h 03m 53.7s | 2026-11-11T07:23:57Z |
| Weighted p50 / observed concurrency | 0.200 | 16507.8 | 0d 04h 35m 07.8s | 2026-11-11T03:55:11Z |
| Weighted p95 / observed concurrency | 0.812 | 66903.4 | 0d 18h 35m 03.4s | 2026-11-11T17:55:07Z |
| Mean ratio / busy-time mean overlap | 3.836 | 315922.5 | 3d 15h 45m 22.5s | 2026-11-14T15:05:26Z |
| Per-suite weighted ratios / observed concurrency | 0.344 | 28318.0 | 0d 07h 51m 58.0s | 2026-11-11T07:12:02Z |

The naive row is remaining planned seconds times the unweighted mean ratio, divided by the observed concurrency. The p50 and p95 rows swap that mean for the unweighted percentile of the per-chunk ratio distribution. Those three rows are the brief formula. The weighted rows replace the scale with sum(wall) / sum(plannedSeconds), which reproduces the time already spent. The per-suite row scales each suite remainder by that suite weighted ratio (the global weighted ratio when the suite has no completed chunk) and divides by the same concurrency. In-flight planned seconds stay fully inside the remainder, so none of these clocks credit time already elapsed on an open chunk.

## Reproduce

```powershell
pwsh -NoProfile -File scripts/measure-run-6591e378-timings.ps1
```

The script only reads the run directory and the timing directory. It writes this report and nothing under the qualification logs.

