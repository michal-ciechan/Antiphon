# Why missed: sr profile16 C1514 sampler overhead (Windows)

One committed note for the rc/2026-11-19 (`ab7af12c699039270af7e22a5d7fd794035bc325`) failure of chunk `session-runner-all-profile-16` in Windows run 97150a0b. The Debug task is 53b8f016.

## Failure

`C1514HostLoadNativeOverheadTests.Windows_SamplerOverheadStaysWithinBudget` failed on DESKTOP-KTLKPIF. It started at 2026-11-13T13:11:42Z and failed after 4m43s, so pair 0 of 2 failed. The assertion was at `C1514HostLoadNativeOverheadTests.cs:39`:

```
on.P95TickMs should be less than 25d but was 32.5279d
```

The CPU-points assertion on line 38 passed. That check requires the on/off gap to stay under 1 point of one core.

Windows history of the same test (TRX under `C:\Antiphon\releases\candidates\<rc>\logs`):

| Run (rc) | Outcome | p95TickMs pair 0 / pair 1 |
|---|---|---|
| a6410818 (rc-2026-11-12) | Passed | 0.86 / 1.27 |
| dce78ee0 (rc-2026-11-14) | Passed | 0.91 / 1.15 |
| 6591e378 (rc-2026-11-15) | Passed | 1.18 / 1.04 |
| d88de419 (rc-2026-11-16) | not run (reboot kill) | none |
| ae84478d (rc-2026-11-17) | **Failed** pair 1 | 0.72 / **119.12** (AppHost restart window) |
| 0b292fa4 (rc-2026-11-18) | Passed | 0.70 / 2.62 |
| 97150a0b (rc-2026-11-19) | **Failed** pair 0 | **32.53** / not run |

The steady Windows p95 is 0.7 to 2.6 ms. The two failures are 12x and 45x that value, and both happened when the desktop was under load. Between `56e96eabf` and `ab7af12c6` (the previous cut and this one), nothing changed in `HostStats*`, `HostLoad*`, `Program.cs` or the test. Master has not changed those files since the cut either.

Host load from 13:11 to 13:16Z, read from the production runner's `/host-stats/series?metric=cpu&window=30m`: per-minute average CPU was 60 to 76% on 8 cores, with a minimum of 38%. Available memory was about 6 GB of 32 GB, and swap commit was 41.9 GB. A build slot waited on `memory_floor` (6071 MB available, floor 6144 MB) later, during this Debug task. There were no Defender events or System-log events from 13:00 to 13:20Z, and the host did not reboot (boot was 11/11 17:36Z). `Antiphon.Server` had used about 28,000 CPU-seconds since 05:54Z, roughly one core of continuous load.

## Cause

The cause is a Windows product gap that only shows up under load; it is not a regression in this cut. `HostStatsSamplerService.SampleOnceAsync` logs `HOST_LOAD_TICK elapsedMs`, and the overhead row takes its p95 from that value. Commit `4e2b755d6` (cherry-picked from `1380bb4ada387a22e11361de13b57ecd06be3aae`, CARD-1514, "Charge sampler tick budget to the sampling thread's CPU") moved this value off wall time because wall time "counted time the sampler thread was not running". It did that only for Linux, through `CLOCK_THREAD_CPUTIME_ID`. On Windows, `ThreadCpuMilliseconds()` returned null, so the tick fell back to `Stopwatch` wall time. Wall time charges the tick for scheduler queueing and paging on a busy host. The 25 ms budget is meant to cover only the sample's own work.

Fix (master, branch `feat/card-task-53b8f016`, round 2 `feat/card-task-a9f8cd21`): on Windows, `ThreadCpuMilliseconds()` now uses `QueryThreadCycleTime`. It converts cycles to milliseconds with a running rate, taken as the highest cycles-per-ms of three 2 ms spins. `GetThreadTimes` was not used because it advances only at the 15.6 ms clock tick. The 25 ms budget is unchanged.

Not every calibration error is safe. Descheduling during a calibration spin lowers the measured rate, so it over-charges later ticks; that is the safe direction. Two errors go the other way. The cycle reads bracket the spin, so cycles spent outside the timed window raise the rate and under-charge, but only by a fraction of a millisecond at the 25 ms budget. A cycle counter that scales with core frequency under-charges by the frequency ratio when the core later runs slower than it did during calibration. The invariant TSC on recent x64 CPUs does not scale with frequency.

Round 2 (review `2cef094f`): a failed cycle read, including a read of 0 cycles, is unknown CPU (null). A tick whose end reading failed or did not advance past its start falls back to wall time. Neither case is charged as zero CPU, which would hide a regression.

Red witness: `C1514SamplerTickCpuTests` (Unit). `TickBudgetChargesThreadCpuNotDescheduledWall` uses a probe that sleeps 120 ms. Before the fix, Windows logged at least 120 ms. `ThreadCpuCountsWorkTheThreadDoes` first discards one reading, which forces the Windows calibration out of the measured window. It then checks that a 60 ms spin charges more than 15 ms and no more than 1.5 times the wall. A cycles-per-ms rate that is off by x10 or /10 fails it. Before the fix, Windows returned null. Both tests failed on Windows at `807a989833835a9743bc3f384f6bea520961eb0c` and passed at `a01329049a66a0054b3c97143574e1388601f1b1`. Linux was already green because its code path is unchanged. The round 2 witnesses for failed, zero and stuck readings are in the same class.

## Introducing commit/card

introducing commit/card: `1380bb4ada387a22e11361de13b57ecd06be3aae` (CARD-1514), replayed on master as `4e2b755d6264384cce91452d8f204f75975ef196`. The overhead budget it guards is `4d0105ea1` (N3/N4 witnesses), and the steady-window p95 is `70ddb266c` (from `e62f9f0ca`). The Windows wall-time fallback came in with the Linux-only thread clock. The Windows row passed whenever the desktop was quiet, which hid it.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran. Source: `docs/investigations/2026-11-08-why-missed-c1514-task-load-window.md`. Branches `feat/card-task-d56a0975` and `feat/card-task-a28cac94` contain `1380bb4ad`.

- Review `d56a0975` (round 2, Interim). CP-1 covered the history/window/API classes. It did not cover the overhead row and did not run on Windows.
- Review `a28cac94` (round 3, Interim, diff-only). CP-7 ran `/*/*/(C1514HostLoadNativeTests*)|(C1514HostLoadNativeOverheadTests*)|(HostStatsProbeLiveTests*)|...|(Linux_*)|...`: 7 executed, 7 passed, Linux only. CP-8 (the Windows row) was not rerun on Windows for this diff.
- Review `c7a30141` (master integration, CLEAN). It reran CP-1, CP-2, CP-3, CP-7 and CP-M1, all on Linux.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration, Slow, `tests/Antiphon.SessionRunner.Tests/C1514HostLoadNativeOverheadTests.cs`, method `Windows_*`. On Linux the method skips itself (`Skip.Test`), and every review ran on Linux, so no review executed the Windows branch of `ThreadCpuMilliseconds`. Even a native Windows CP-8 run would have passed on a quiet desktop (p95 about 1 ms). The defect only shows when the host is loaded, which is why the earlier qualification runs passed it. No Unit test pinned the thread-CPU charge on either platform. The commit message measured the Linux symptom ("At load 30 a tick logged 9.7ms") and made no claim about Windows.

## Cheapest pre-land check

The cheapest pre-land check, as a path-glob -> class-filter entry for the impacted-classes table:

`src/Antiphon.SessionRunner/HostStatsSamplerService.cs` -> `/*/*/C1514SamplerTickCpuTests/*`

It is Unit, runs in under 1 s, needs no process spawn and no host load, and goes red on Windows with the old fallback. The derived impact index now maps the sampler source to this class. Run it on the Windows lane for any change to `ThreadCpuMilliseconds` or the tick log.

## Exemption

same cause as an already written note: none. The ae84478d failure (119 ms, AppHost restart, "rerun, no fixer") had the same root cause but no note of its own.
