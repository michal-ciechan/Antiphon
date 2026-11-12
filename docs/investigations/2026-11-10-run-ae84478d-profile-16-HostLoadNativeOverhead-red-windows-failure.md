# Run ae84478d: session-runner-all-profile-16 red (C1514HostLoadNativeOverheadTests, Windows)

- Run: ae84478d (Windows Full qualification, desktop DESKTOP-KTLKPIF), rc/2026-11-17 = `3db1a43b02246706dc28075fce7e7b5e84804ddb`.
- Chunk: `session-runner-all-profile-16` (1 test, 1 failed).
- Evidence (read-only): `C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d\session-runner-all-profile-16{.trx,-tests.log,.timing.json,-selection.discovery.json,-execution\log_261112093719958.diag}` and `...\logs\timing-ae84478dd83d4cde8f537ba8303794fb\owner-session-runner-all-profile-16-ae84478dd83d4cde8f537ba8303794fb-1.json`.
- Verdict: **LOAD-INDUCED, not a defect.** The only failing assertion is the tick-latency p95 of the last measured child, and that child ran from start to finish inside the concurrent AppHost restart (build + launch). Recommendation: rerun this one chunk on the next cut. No code change. No budget change.

## Failed test

`Antiphon.SessionRunner.Tests.C1514HostLoadNativeOverheadTests.Windows_SamplerOverheadStaysWithinBudget`. Categories: Integration, Slow. Timeout: 720 s. The class has `ParallelLimiter<ProcessSpawnLimit>`.

- Outcome: Failed. Duration: `00:09:25.5158628`. Start: `2026-11-12T09:37:20.4138544Z`. End: `2026-11-12T09:46:45.9297172Z`.
- Exact message (TRX):

  ```
  ShouldAssertException: on.Samples.ShouldBeGreaterThanOrEqualTo(12); on.P95TickMs
      should be less than
  25d
      but was
  119.1206d
  ```

  Shouldly quotes the whole source line 39 (`on.Samples.ShouldBeGreaterThanOrEqualTo(12); on.P95TickMs.ShouldBeLessThan(25d);`). The assertion that failed is `on.P95TickMs < 25`. Expected `< 25 ms`. Actual `119.1206 ms`. `on.Samples >= 12` is evaluated first on the same line, so it passed. The CPU-points assertion on line 38 also passed for this pair.
- Stack top frames:

  ```
  at Antiphon.SessionRunner.Tests.C1514HostLoadNativeOverheadTests.MeasureAsync() in ...\C1514HostLoadNativeOverheadTests.cs:line 39
  at Antiphon.SessionRunner.Tests.C1514HostLoadNativeOverheadTests.Windows_SamplerOverheadStaysWithinBudget() in ...\C1514HostLoadNativeOverheadTests.cs:line 29
  --- TUnit internals omitted ---
  ```

- StdOut (complete):

  ```
  C1514 overhead pair=0 offCpuPoints=0.013020180208543623 onCpuPoints=0.10416223708073294 samples=24 measuredTicks=24 p95TickMs=0.7233 spoolBytes=47306 authorityRequests=16 loadBefore=unavailable loadAfter=unavailable
  [CARD-0206] no leaked pty-hosts
  ```

  The test prints a pair's summary line only after all of that pair's assertions pass. Pair 0 printed its line, so pair 0 passed. **Pair 1 failed**, and its line is absent. There was no stderr. The MTP diag log has nothing between test start (09:37:20.365) and the failure push (09:46:45.991). The run used `exit code 2` (test failure), not a crash or timeout.

## What the test measures

Line numbers refer to `tests/Antiphon.SessionRunner.Tests/C1514HostLoadNativeOverheadTests.cs` at `3db1a43b0`.

- There are two pairs. Each pair starts a real `Antiphon.SessionRunner` child with HostStats off, then another child with HostStats on. The children run with `DOTNET_TieredCompilation=0`.
- Each child waits 20 s of warm-up, then measures for 120 s.
- For the `on` child, the test reads every `HOST_LOAD_TICK elapsedMs=` line logged after warm-up (24 ticks at the 5 s cadence). The p95 is nearest-rank: `index = ceil(.95*24)-1 = 22`, which is the second-largest tick.
- `elapsedMs` is the wall-clock duration of one sampler tick. When other work saturates the CPUs, the scheduler preempts the tick thread, and this number grows. A p95 of 119 ms therefore means at least two of the 24 steady ticks took 119 ms or longer.
- `HostLoad()` returns `unavailable` on Windows (it reads only `/proc/loadavg`). The test therefore recorded no host load. The chunk timing sidecar has no host CPU or memory fields either (`buildSlot: not-used`, no load columns). The timing root has no host-sampler file. **No direct host CPU or memory sample exists for this window.** The load attribution below rests on the timeline.

## Timeline: the failing child sat exactly inside the AppHost restart

The owner sidecar `owner-session-runner-all-profile-16-...-1.json` records the four `dotnet` children:

| Child | PID | Started (Z) | Exited (Z) | Role | Result |
|---|---|---|---|---|---|
| 1 | 20244 | 09:37:20.59 | 09:39:41.50 | pair 0 off | ok |
| 2 | 43660 | 09:39:41.52 | 09:42:02.88 | pair 0 on | p95 0.7233 ms, passed |
| 3 | 95920 | 09:42:02.90 | 09:44:24.09 | pair 1 off | ok |
| 4 | 41540 | **09:44:24.11** | **09:46:45.87** | pair 1 on | **p95 119.1206 ms, failed** |

The concurrent desktop AppHost restart ran 09:44:24Z–09:47:40Z, per the caller and the operator restart log. Local logs corroborate this:

- `C:\src\Antiphon\logs\apphost-launcher-7e74b84d115c42ba93b0de7c4e304e9c.{err,out}` were written 09:44:32Z–09:46:43Z. The launcher took `BUILD SLOT granted ... maxcpucount=4`, ran restore, ran `npm` client install, and built the AppHost.
- `apphost.log` records the AppHost build: `LIVE_TIMING ... "operation":"build","elapsedMs":127536.5107`. It then records Aspire startup, and its last write is at 09:47:24Z.
- `apphost.pid` was written at 09:45:06Z.

Child 4's measured window was about 09:44:45Z–09:46:45Z (20 s warm-up after "Now listening"). That window lies wholly inside the restart's restore, npm and `maxcpucount=4` build phase. All three earlier children (09:37–09:44) ran before the restart began, and pair 0's p95 was 0.72 ms.

## Passing siblings and history (these bound the cause)

- Same test, same run: **pair 0 passed** (off 0.013 CPU points, on 0.104 CPU points, 24 samples, p95 0.7233 ms). Pair 1's CPU-points and samples assertions also passed. Only the latency percentile failed, under load.
- Same class: `Linux_SamplerOverheadStaysWithinBudget` is `[NativeLinuxOnly]`. It was not selected in this Windows chunk (the discovery file lists only the Windows node), so it gives no Windows evidence.
- Earlier Windows runs of the same test, all **Passed**:

| Run | Chunk | Duration | Pair 0 p95 tick | Pair 1 p95 tick | CPU points (on) |
|---|---|---|---|---|---|
| a6410818 (rc/2026-11-12) | session-runner-all-profile-15 | 9:26.35 | 0.86 ms | 1.2687 ms | 0.065 / 0.052 |
| dce78ee0 (rc/2026-11-14) | session-runner-all-profile-15 | 9:25.24 | 0.9056 ms | 1.1501 ms | 0.078 / 0.143 |
| 6591e378 (rc/2026-11-15 `ac6d00022e94`) | session-runner-all-profile-16 | 9:25.16 | 1.177 ms | 1.0351 ms | 0.130 / 0.117 |
| **ae84478d (rc/2026-11-17 `3db1a43b0`)** | session-runner-all-profile-16 | 9:25.52 | 0.7233 ms | **119.1206 ms** | 0.104 / (passed) |

- The 6591e378 final-result note (`origin/feat/card-task-d6579463` `7a3c45c3f`, `docs/investigations/2026-11-11-run-6591e378-final-result.md`, line 2185) lists `session-runner-all-profile-16` among the chunks "whose summary result is `pass`".
- Idle steady-state p95 across 8 measured pairs: **0.72–1.27 ms**. The worst is about 20x under the 25 ms budget. The failing value is about 94x the idle maximum.
- Test duration p95 (4 Windows runs): about 566 s (565.2–566.4 s). The 720 s timeout is not near.
- Sampler code: `git diff ac6d00022e94 3db1a43b0 -- src/Antiphon.SessionRunner` touches only `HerdrDisposalRedactor.cs`, `PhoneHomeCommandDispatcher.cs`, `PhoneHomeConnectionService.cs`, `PhoneHomeSettings.cs` and `StandingCheckoutLink.cs`. It touches no HostStats or sampler file. `git log` between the cuts for `C1514HostLoad*` is empty. **No regression candidate exists in the measured code path.**

## Classification

**Timing/load, not deterministic.** Evidence:

1. The failing quantity is a wall-clock tick latency, not returned state.
2. The CPU-overhead and sample-count assertions passed in the same pair.
3. The pair before it, in the same process and test, measured 0.72 ms.
4. The failing child's lifetime (09:44:24.11–09:46:45.87) coincides with the AppHost restart (09:44:24–09:47:40), which included a `maxcpucount=4` build that took 127.5 s.
5. There is no code change in the sampler between a passing cut and this one.
6. The test passed on three earlier Windows runs with 1 ms-class p95.

## Recommendation

- **Rerun `session-runner-all-profile-16` once on the next cut.** Do not run an AppHost or runner restart, or any other build, on the desktop during its roughly 9.5 min window. Make no code change.
- **No budget change.** The idle p95 (≤1.27 ms) is about 20x below the 25 ms budget, not within 2x of it. The budget is fine. It simply cannot tolerate a co-scheduled heavy build, and it should not.

## Why missed (operator rule 2026-11-08)

Nothing was missed in code or tests. This red is a qualification-scheduling collision, not an escaped defect. The test is load-sensitive by design. The qualification has no host-quiet guard for load-sensitive chunks, so an operator-side AppHost restart can land on one. Two process gaps made the attribution rely on the timeline:

1. The Windows `HostLoad()` path records `unavailable`.
2. The timing sidecar carries no host CPU or memory sample.

Optional follow-ups (cards, not part of this rc), worth filing if this repeats:

- (a) Record Windows host load in the C1514 output line, for example system CPU through `GetSystemTimes`, sampled before and after each pair.
- (b) Have the orchestrator's AppHost restart check `active-chunk.json` and defer while a `C1514HostLoad*` chunk is running, or have the qualification mark that chunk host-exclusive.

Native Windows proof: none owed for a code fix, because there is none. The rerun on the next cut is the proof.
