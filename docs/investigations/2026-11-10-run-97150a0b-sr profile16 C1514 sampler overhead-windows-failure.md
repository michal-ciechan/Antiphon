# Run 97150a0b, session-runner/all-profile-16: C1514 sampler overhead (Windows)

Read-only extract from the live Windows Full qualification `97150a0b03454892a10a086837fe8058`
on `rc/2026-11-19` = `ab7af12c699039270af7e22a5d7fd794035bc325` (desktop `DESKTOP-KTLKPIF`,
8 logical cores, 34 GB). Nothing in the live run, AppHost, runner or Docker was touched.

## Verdict

**Load flake caused by a Windows measurement gap, not a regression.** On Windows the
`HOST_LOAD_TICK elapsedMs` value is **wall time**, so the 25 ms p95 budget charges time the
sampler thread spent waiting for a CPU on a saturated host. Commit `4e2b755d6` (2026-11-07,
"Charge sampler tick budget to the sampling thread's CPU") fixed exactly this on Linux with
`CLOCK_THREAD_CPUTIME_ID`, but `ThreadCpuMilliseconds()` returns `null` on every non-Linux OS,
so Windows still falls back to `Stopwatch.GetElapsedTime(tickStarted)`. No sampler or test code
changed between the previous cut and this one.

## The failure

| Field | Value |
|---|---|
| Test | `Antiphon.SessionRunner.Tests.C1514HostLoadNativeOverheadTests.Windows_SamplerOverheadStaysWithinBudget` |
| Chunk | `session-runner/all-profile-16` (the only test in the chunk) |
| Start / end | 2026-11-13T13:11:42.63Z / 13:16:25.77Z |
| Duration | 4 m 43.144 s (283 s = one off child + one on child, so **pair 0** failed) |
| Assertion | `on.P95TickMs.ShouldBeLessThan(25d)` at `C1514HostLoadNativeOverheadTests.cs:39` |
| Expected / actual | `< 25` / `32.5279` |
| Stdout | `[CARD-0206] no leaked pty-hosts` only (the per-pair summary line is printed after the asserts, so offCpu/onCpu/ticks were lost) |

Exact message (TRX):

```
ShouldAssertException: on.Samples.ShouldBeGreaterThanOrEqualTo(12); on.P95TickMs
    should be less than
25d
    but was
32.5279d
```

Stack top frames:

```
at Antiphon.SessionRunner.Tests.C1514HostLoadNativeOverheadTests.MeasureAsync() in ...\C1514HostLoadNativeOverheadTests.cs:line 39
at Antiphon.SessionRunner.Tests.C1514HostLoadNativeOverheadTests.Windows_SamplerOverheadStaysWithinBudget() in ...\C1514HostLoadNativeOverheadTests.cs:line 29
```

Assertions that **passed** before line 39 in pair 0: `(on.CpuPoints - off.CpuPoints) < 1`
(line 38), `on.Samples >= 12`, and inside `RunAsync` `measured.Length >= 20` and the
p95-is-not-the-maximum index check. So the child ran, sampled at cadence and its incremental
CPU stayed under one point; only the per-tick latency percentile was high.

## What ran at the same time

- Qualification chunks are serial. From the run's `timing-97150a0b...` sidecars:
  `antiphon/all-group-126` 13:01:10–13:11:41.83, **profile-16 13:11:41.87–13:16:26.40**,
  `antiphon/all-group-9` 13:16:26.57–13:17:06. No qualification overlap.
- Desktop runner (`%TEMP%\antiphon-logs\session-runner-20261113.log`, `logs\session-runner.log`):
  last build-slot lease released 09:08:41Z; no lease, restart or runner event 13:00–13:19Z.
  Kestrel heartbeat stalls ("thread pool starvation") at 12:54:27Z (1.8 s) and 12:56:24Z (3.1 s).
  Boot time 2026-11-11 17:36:56Z, so no reboot or AppHost-driven runner restart.
- Windows event logs (Defender Operational, System, WindowsUpdateClient) 13:00–13:20Z: no events.
- Production runner host-load history (`%LOCALAPPDATA%\antiphon\host-load`, hostLoadHistoryV1
  spool) has a gap 12:48:25Z → 13:27:09Z (`Host load append unavailable; dropped=1` at 12:48:25Z),
  so there is no sample inside the window. The samples on both sides show a saturated host:

| UTC | host CPU % | busy cores of 8 | tracked runner+session processes |
|---|---|---|---|
| 12:47:45–12:48:25 | 58.1–93.2 | 4.65–7.46 | 1 |
| 13:27:09–13:28:34 | 88.8–99.9 | 7.10–7.99 | 25 (runner + ~12 live sessions/pty-hosts) |

  The tracked Antiphon processes account for only 22–51 % of one core at 13:27Z, so most of the
  load is outside the runner's process tree (other Claude/agent sessions, their tools and builds
  not leased through the slot gate, browser, etc.). The exact consumer at 13:11–13:16Z is not
  recorded anywhere I could read.

## History of this test on Windows (measured p95, pairs 0/1)

| Run | rc | chunk | outcome | p95TickMs |
|---|---|---|---|---|
| a6410818 | rc/2026-11-12 | profile-15 | Passed | 0.86 / 1.27 |
| dce78ee0 | rc/2026-11-14 | profile-15 | Passed | 0.91 / 1.15 |
| 6591e378 | rc/2026-11-15 | profile-16 | Passed | 1.18 / 1.04 |
| d88de419 | rc/2026-11-16 | — | not reached (no session-runner TRX contains it) | — |
| ae84478d | rc/2026-11-17 | profile-16 | **Failed** (pair 1, during an AppHost restart window) | 0.72 / **119.12** |
| 0b292fa4 | rc/2026-11-18 | profile-16 | Passed | 0.70 / 2.62 |
| 97150a0b | rc/2026-11-19 | profile-16 | **Failed** (pair 0) | **32.53** / — |

Quiet-host p95 is 0.7–2.6 ms, ten times under the budget. Both failures coincide with external
host load; the earlier one is the AppHost-restart flake already noted by the operator.

## Code involved

- `src/Antiphon.SessionRunner/HostStatsSamplerService.cs:56-93` `SampleOnceAsync`: the whole tick is
  synchronous on one thread (`WindowsHostStatsProbe.Read()` = `GlobalMemoryStatusEx` +
  `GetSystemTimes` + `GetDiskFreeSpaceEx` per volume; self process CPU/working set; store add;
  non-blocking `Channel.TryWrite` to the spool worker). It logs:

  ```csharp
  // Wall time charges time this thread was not running. The tick budget is its own CPU.
  var elapsedMs = cpuStarted is double started && ThreadCpuMilliseconds() is double cpuNow
      ? cpuNow - started
      : Stopwatch.GetElapsedTime(tickStarted).TotalMilliseconds;
  ```

- Same file, `ThreadCpuMilliseconds()`:
  `if (!OperatingSystem.IsLinux() || ClockGetTime(...) != 0) return null;` — Windows always
  takes the wall-time branch.
- The test (`tests/Antiphon.SessionRunner.Tests/C1514HostLoadNativeOverheadTests.cs`) runs a
  sampler-off child then a sampler-on child per pair; ticks exist only in the on child, so the
  25 ms budget is **absolute**, with no same-window baseline. `HostLoad()` returns
  `"unavailable"` on Windows, so the test records no load evidence there.
- History: `4d0105ea1` (witness added), `d76a39cd7` (tiered compilation off), `4e2b755d6`
  (thread CPU, Linux only), `70ddb266c` (steady window, p95 excludes the max). The commit message
  of `4e2b755d6` records the same symptom on Linux: "At load 30 a tick logged 9.7ms while process
  sampling itself was 0.36ms".
- `git diff 56e96eabf..ab7af12c6` touches no file under `src/Antiphon.SessionRunner` and no
  `C1514*` test; the SessionRunner test files that changed are `PtyHostLeakSweep.cs`,
  `RunnerMultilinePromptDeliveryTests.cs`, `RunnerRestartScriptCompatibilityTests.cs`,
  `RunnerSessionGenerationTests.cs`.

## Sibling tests that passed in this run (bound the cause)

All other C1514 host-load classes passed in `session-runner/all-group-3..5`, which rules out a
sampler, spool, authority or wire defect:

- `C1514HostLoadSampleTests`: CadenceAndDeltasUseMonotonicTime, CpuPercentUsesHostDenominator,
  DisabledHistoryKeepsLegacySampling, ProcessCpuDoesNotBecomeHostCpu,
  SourceIdentityAndPayloadAreStable, UnknownMetricsStayNull
- `C1514HostLoadSpoolTests`: AppendSurvivesRestart, CapsPersistExplicitGaps,
  CursorCannotSkipUncommitted, DiskFailurePreservesLiveSampling, PartialTailIsQuarantined,
  ReplayPreservesBootIdentity
- `C1514HostLoadAuthorityTests`: AuthorityFailureRemainsUnknown,
  AuthorityIdentityAndFreshnessAreChecked, AuthorityReadsAreCachedPerInterval,
  OverrideReadsSharedAuthority
- `C1514HostLoadWireTests`: CapabilitiesAndOrdinalsStayCompatible, HistoryRoutePreservesCursorAndBounds
- Windows probe tests: Windows_cpu_percent_treats_kernel_as_including_idle,
  Windows_probe_maps_a_failed_GetSystemTimes_to_null
- In the same class, `Linux_SamplerOverheadStaysWithinBudget` is `[NativeLinuxOnly]` and does not
  run on Windows. `C1514HostLoadNativeTests` Windows rows were not found in this run's
  session-runner TRX files at read time.

## Deterministic or timing?

**Timing/load.** The failing value is a wall-clock percentile, not returned state; the same
code produced 0.70 and 2.62 ms on the previous cut; the only other failure (119 ms) happened in a
known load window; the host was at 58–93 % CPU just before and 89–99.9 % just after; and the
incremental-CPU and cadence assertions in the same pair passed.

## Proposed smallest fix (for the Debug stage)

Port the Linux fix to Windows rather than raising 25 ms:

1. **Product** (`HostStatsSamplerService.ThreadCpuMilliseconds`): on Windows return the calling
   thread's CPU, for example `GetThreadTimes(GetCurrentThread(), ...)` (kernel + user, 100 ns
   units). `SampleOnceAsync` is synchronous, so start and end are on the same thread. Note that
   `GetThreadTimes` advances at clock-interrupt granularity (about 15.6 ms), so a ~1 ms tick reads
   0 or 15.6 ms; that stays under 25 ms, while a tick with 25 ms or more of real CPU always reads
   at least 15.6 and often 31.2 ms. If finer resolution is wanted, `QueryThreadCycleTime`
   calibrated once against `Stopwatch` is the precise alternative. Keep the wall fallback when the
   call fails.
2. **Windows red witness (deterministic, fast, no load needed)**: a unit test that runs
   `SampleOnceAsync` with a fake `IHostStatsProbe` whose `Read()` does `Thread.Sleep(60)` and
   captures the `HOST_LOAD_TICK` value from a test logger. Today on Windows it logs at least 60 ms
   (red); with the fix it logs under 25 ms (green). Positive control in the same class: a probe
   that spins the CPU for 60 ms must still log at least 25 ms, proving a real CPU regression is
   not hidden.
3. **Diagnostics in the overhead test**: print the per-pair summary line (offCpu, onCpu, samples,
   measuredTicks, the sorted measured ticks and p95) **before** the asserts, and on Windows record
   host load (for example `GetSystemTimes` busy percent over the on window) in `loadBefore` and
   `loadAfter`, so the next failure carries its own load evidence.

Optional, if the operator still wants a wall-based signal: also log `wallMs` per tick and fail on
wall p95 only when the same-window wall-minus-CPU gap is small (the thread was really running),
never by raising the absolute bound.

## Evidence paths (read-only)

- `C:\Antiphon\releases\candidates\rc-2026-11-19-ab7af12c6990\logs\2026-11-13-0558-97150a0b\session-runner-all-profile-16.trx`, `-tests.log`, `.timing.json`, `-selection.discovery.json`, `-execution\log_261113131142160.diag`
- `C:\Antiphon\releases\candidates\rc-2026-11-19-ab7af12c6990\logs\timing-97150a0b03454892a10a086837fe8058\*.timing.json`
- Previous runs: `rc-2026-11-18-56e96eabf698\logs\2026-11-12-1213-0b292fa4\session-runner-all-profile-16.trx`, `rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d\session-runner-all-profile-16.trx`, `rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378\session-runner-all-profile-16.trx`, `rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\session-runner-all-profile-15.trx`, `rc-2026-11-12-5fef2c23b894\logs\2026-11-08-1749-a6410818\session-runner-all-profile-15.trx`
- Desktop runner logs: `%TEMP%\antiphon-logs\session-runner-20261113.log`, `C:\src\Antiphon\logs\session-runner.log`
- Host-load spool: `%LOCALAPPDATA%\antiphon\host-load\chunk-00000000000000037745.jsonl`, `chunk-00000000000000038193.jsonl`
