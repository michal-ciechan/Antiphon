# CARD-1514 Final Review round 1 — Windows sampler tick CPU

Reviewed source `1a79c5119bce4c36e07e81bb43f5030ef072a4bf` on `feat/card-task-53b8f016` (owner task `53b8f016-bb58-4414-8380-be0e2dfaada9`). Range `b57583cee95b75c62edd496247e2361c7cc4e034..1a79c5119bce4c36e07e81bb43f5030ef072a4bf`: red test `807a98983`, fix `a01329049`, index `9d70ca858`, why-missed `1a79c5119`. Four files, +179/−6. Reviewer is not the authoring company.

Verdict: one blocker. The Windows cycle conversion is an acceptable fix for the rc failure (wall-clock fallback, p95 32.5279 ms against the 25 ms budget). `ThreadCpuCountsWorkTheThreadDoes` does not lock that fix down.

Mutation checks were waived by the operator on 2026-11-12. The four named mutants were judged from the tests and were not executed. Positive controls stay pending. This Linux review did not execute `QueryThreadCycleTime` or `Windows_SamplerOverheadStaysWithinBudget`.

## D-1 — spin window and band

Where: `tests/Antiphon.SessionRunner.Tests/C1514SamplerTickCpuTests.cs`, `ThreadCpuCountsWorkTheThreadDoes` (lines 35–45).

Failure: the first `ThreadCpuMilliseconds()` is the value subtracted later, and on Windows that call calibrates before it returns. `CalibrateWindowsCyclesPerMs` busy-waits three times for 2 ms each (`HostStatsSamplerService.cs` lines 285–297) and the cycle count returned to the test was sampled before those waits. The stopwatch starts only after that call returns. The next reading therefore includes at least 6 ms of calibration CPU that the stopwatch never saw. The assertion allows only `wall + 5`. A quiet, fully scheduled Windows host fails correct code by at least 1 ms. A loaded host can deschedule the 60 ms spin enough to pass, which matches the author's green 2/2 on a busy desktop. TUnit runs the class in parallel and the class has no `NotInParallel`. This method's first statement is the cycle read, so it usually wins calibration against `TickBudgetChargesThreadCpuNotDescheduledWall`, which constructs objects first. Either order is legal.

The same band does not kill the named rate-times-10 mutant. A 60 ms spin reported as about 6 ms is still `> 0` and `<= wall + 5`. That is the under-charge direction that hides a 25 ms budget regression.

A post-calibration `QueryThreadCycleTime` failure is also unkilled. The production method returns null when the call returns false, and `SampleOnceAsync` then charges wall time. If the failure check is ignored after a rate has already been stored, the `out` cycle count stays 0 and the method returns 0. `TickBudgetChargesThreadCpuNotDescheduledWall` treats 0 as success (`< 25` and `>= 0`). No test makes the API return false.

Why: the red commit does catch the old Windows null (both tests failed at `807a98983` on the author's Windows run, because null falls back to wall and the spin test rejects null). The green band does not catch a high rate, and the window includes one-time calibration.

Fix: call `ThreadCpuMilliseconds()` once and discard it, then start the stopwatch, spin, and subtract. Keep the ceiling at `wall + 5`. Add a floor above the times-10 result of a full spin and below a moderately descheduled one, about `used > 15` on a 60 ms spin. A floor near 30 fails correct code when the thread is scheduled for under half of those 60 ms. Assert that a failed cycle read is null rather than 0; that needs a seam, because the tests cannot force the Win32 call to fail. In the same change, stop saying every calibration error over-charges (advisory below).

## Mutants

| Mutant | Result |
|---|---|
| Charge wall time | Killed. The 120 ms sleep logs at least 120 ms and `TickBudgetChargesThreadCpuNotDescheduledWall` requires `< 25`. |
| Charge zero | Killed. The spin test requires `used > 0`. |
| Calibration rate times 10 | Not killed. About 6 ms still passes `(0, wall + 5]`. |
| Ignore API failure | Not killed on the success path (equivalent while the call succeeds). After a stored rate, a false return that becomes 0 passes the sleep test. |

## Product behavior (not a land block)

`QueryThreadCycleTime` returns cycles the thread executed in user and kernel mode, not wall time. Microsoft’s remark is explicit: do not convert those cycles to elapsed time, because some CPUs scale the timer with frequency and others keep it fixed (Win32 `QueryThreadCycleTime`, Raymond Chen 2016-04-29: x64 uses `RDTSC`, and recent Intel parts keep an invariant rate).

Descheduling during a calibration spin lowers cycles per wall millisecond. Taking the maximum of three samples stays at or below the true rate, so that error over-charges later ticks (false positive). The author’s deschedule claim is right. The comment and the why-missed note say every calibration error over-charges. That is not true of the other two errors:

- The cycle reads sit just outside the `Stopwatch` window (before the timestamp, and again after wall is sampled). That biases the rate slightly high. At a 2 ms spin the untimed calls are a small fraction of the window, well under 1 percent, and at the 25 ms budget that is a fraction of a millisecond. It cannot hide the original 32.5 ms wall reading.
- On a counter that scales with core frequency, the highest of three short spins prefers the fastest sample. A later slower tick under-charges by up to that frequency ratio. On an invariant TSC, which is the practical desktop case, the rate stays fixed and this term drops out.

`GetThreadTimes` at a 15.6 ms tick would read a ~1 ms sample as 0 and hide the regression more thoroughly. Rejecting it is correct. `GetCurrentThread`’s pseudo-handle is used only on the calling thread, is not cached, and is not closed. `BOOL` plus `IntPtr` plus `out ulong` is the right 32/64 shape. `SetLastError` is unset; the code does not read the last error, it returns null. There is no static constructor. A zero rate is stored and retried on the next call. Two threads can both calibrate; the last write wins and neither poisons the type. `SampleOnceAsync` takes both readings before it returns, so a thread-pool hop between ticks does not mix two threads inside one delta. The unlogged warmup in `ExecuteAsync` pays the ~6 ms calibration, so the logged p95 does not include it.

Linux `time.Seconds * 1000d + time.Nanoseconds / 1_000_000d` is the same expression as at `b57583cee`. The branch was rewritten and is equivalent; it is not byte-for-byte the old `if`. The method is `internal`. `InternalsVisibleTo` already named `Antiphon.SessionRunner.Tests` and `Antiphon.Tests` (`Antiphon.SessionRunner.csproj` lines 22–23) and this diff does not change that.

`C1514HostLoadNativeOverheadTests.cs` is not in the diff. Line 39 is still `on.P95TickMs.ShouldBeLessThan(25d)`.

The new tests are `Unit`, timeout 10 seconds, with no skip and no `NativeLinuxOnly`. On Linux they assert the real clock. They passed inside the 19-test run below (0 skipped).

## Verification at `1a79c5119`

Leased runs, native default output, `UseAppHost=false`, `TEMP=/tmp`. No checkpoint manifest, so `reviewedSourceClean` is false.

- SessionRunner `C1514SamplerTickCpuTests`, `C1514HostLoadSampleTests`, `HostStatsSamplerTests`, plus the linked classification guard and native-Linux witness: 19 succeeded, 0 failed, 0 skipped, 1s 912ms. Lease `92214023-8bbc-4d5c-9adc-fcec48765622`.
- Selector extras `C1514HostLoadSpoolTests` and `RunnerCapabilitiesTests`: 13 succeeded, 0 failed, 0 skipped, 2s 459ms. Lease `691448cf-5027-44fb-b9d5-af69906d3fc0`.
- Antiphon.Tests filter (policy, classification guard, slow-registry merge, slow tripwire, native-Linux witness, mechanical PC contract, task-platform guidance, C1729 layering, test-lane category, derived-impact index, release-impact envelope): 116 succeeded, 0 failed, 0 skipped, 4m 09s. Lease `829fb1e6-0509-4cc2-a534-39161fde05c4`. An earlier attempt was aborted by the tool timeout and is not evidence. This count is the commissioned filter, not the nine-class union of 78, and it does not include `ProcessSpawnLimitTests`. The author’s Windows “163” figure included that class and is their number.
- `impacted-classes` on the range: kinds `host-stats` only, migrations not selected, downgrade selector count 0, `windowsFilters` empty, `owed` empty.
- Derived-impact regen (`-NoBuild`) was unchanged at the tip (lease `d0697552-da2b-4f1d-afac-37d32b4d8d65`) and unchanged after cherry-picking all four commits onto `origin/master` `78442d9818f1c5a496bcfe7eb71aa58cd6a84628` (lease `f937afd7-99a1-431c-9610-0492a7bb6ced`). `git cherry` marked all four `+`. Scratch trees `/tmp/c1514-index-tip`, `/tmp/c1514-index-master`, and `/tmp/c1514-index-m78442d9` were left in place.
- `git merge-tree --write-tree origin/master HEAD` exit 0. Tree `ae9ecb37fb1e9ba9f2930ab3f9fa4f44f202a716`. `origin/master` `78442d9818f1c5a496bcfe7eb71aa58cd6a84628`.
- No plan in the range, so `check-os-witnesses.ps1` was not run. The witness was read: the changed branch is the Windows arm of `HostStatsSamplerService.ThreadCpuMilliseconds`, the tests construct that type, and the Linux contract ran. Native `QueryThreadCycleTime` and the overhead row stay pending. No new optional configuration.

OS witness owed, not passed: `Windows_SamplerOverheadStaysWithinBudget` on a later qualification chunk. Linux `Linux_SamplerOverheadStaysWithinBudget` was not in the commissioned re-run.
