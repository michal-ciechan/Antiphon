# CARD-1514 Final Review round 2 — Windows sampler tick CPU

Reviewed source `27d8bdcf29e5fa9e797e81121b02e6ce5fb212cb` on `feat/card-task-a9f8cd21` (task `a9f8cd21-113f-401e-baf2-86c7e8d57fc8`, Debug, Succeeded). That task's recorded base is explicit SHA `1a79c5119bce4c36e07e81bb43f5030ef072a4bf`; `repairSourceTaskId` is empty. Round 1 owner `53b8f016-bb58-4414-8380-be0e2dfaada9` still points at the blocked SHA. Land `a9f8cd21` at the SHA below. A land of `53b8f016` without `-FromTask a9f8cd21-113f-401e-baf2-86c7e8d57fc8` would publish the round-1 tip.

Range `1a79c5119bce4c36e07e81bb43f5030ef072a4bf..27d8bdcf29e5fa9e797e81121b02e6ce5fb212cb`: seam and red witnesses `eef573a3352da78736ee79553a2bd3bf2c908adb`, product fix `0aa4b0f9f02274d867a7d9c2bbde946d483beb4a`, comment and why-missed `27d8bdcf29e5fa9e797e81121b02e6ce5fb212cb`. Three files, +158/−33. `git diff --check` exit 0. Reviewer is not the authoring company.

Verdict: clean. No product blocker in the diff. Round 1's three defects are fixed: the spin window discards one reading before the stopwatch, a Windows cycle read of 0 is null, and a tick whose end reading is null, equal, or behind its start charges wall time.

Mutation checks were waived by the operator on 2026-11-12. Positive controls stay pending. This Linux review did not execute `QueryThreadCycleTime` or `Windows_SamplerOverheadStaysWithinBudget`. The author's Windows figures (SessionRunner named classes 23/23, guard union 80/80) stand as their native result.

## Seam

`HostStatsSamplerService` stores `threadCpu ?? ThreadCpuMilliseconds` once in the constructor. `HostStatsRoutes` constructs the sampler without that argument, so production uses `ThreadCpuMilliseconds`. `C1514HostLoadSampleTests` and `C1514HostLoadSpoolTests` omit it too and still compile. The delegate is a field. The tick takes no lock and allocates no clock. `WindowsThreadClock` is one static object created at type init. Linux returns from `ThreadCpuMilliseconds` before `Milliseconds()` or `QueryThreadCycleTime`. The Linux expression `time.Seconds * 1000d + time.Nanoseconds / 1_000_000d` is unchanged. `ThreadCpuMilliseconds` was already `internal`. `InternalsVisibleTo` for `Antiphon.SessionRunner.Tests` and `Antiphon.Tests` is untouched.

The new comparison is shared by every OS:

```csharp
var elapsedMs = cpuStarted is double started && _threadCpu() is double cpuNow && cpuNow > started
    ? cpuNow - started
    : Stopwatch.GetElapsedTime(tickStarted).TotalMilliseconds;
```

An advancing reading subtracts as before. A null, equal, or backwards end reading charges wall (over-charge, never zero or negative). A huge forward jump charges the delta, also an over-charge, with no clamp. A 64-bit wrap would look backwards and fall back to wall; that is not a practical case.

On this kernel `CLOCK_THREAD_CPUTIME_ID` resolves to 1 ns (`clock_getres` 0, 1 ns). 0 of 1000 back-to-back reads were equal. A 20 ms sleep accrued 0.000027 s of thread CPU. A 60 ms spin accrued 0.059996 s. A tick that runs advances, so the new comparison does not change Linux numbers here. A genuine 0-delta would charge wall on Linux too. `TickBudgetChargesThreadCpuNotDescheduledWall` passed (`< 25`) on the 120 ms sleep, so that sleep was charged as thread CPU.

`ExecuteAsync` calls `SampleOnceAsync(..., logTick: false)` first. When that opening read succeeds, calibration happens there and later logged deltas exclude the ~6 ms spins. When the first read fails or is 0, calibration is skipped and the next logged tick's opening read calibrates inside itself; that one sample can include ~6 ms. That over-charges once. It does not hide CPU. The author flagged it. It is not a blocker.

## Fallback and the 15 ms / 1.5× band

`WindowsThreadCycleClock.Milliseconds` returns null when the reader returns false or the cycle count is 0, including after a rate is already stored. `Calibrate` still returns 0 on a failed read and the next call retries because the stored rate stays `<= 0`.

The fallback tests inject `Func<double?>`. The queue is `[5, 10, end]`. The unlogged warmup consumes 5. The logged tick starts at 10 and sleeps 40 ms. End null, end 0, and end 10 each log `>= 40`, and the queue is empty afterwards. Those three tests depend on `Thread.Sleep(40)` lasting at least 40 ms. They do not read the real thread clock.

`WindowsCalibrationMeasuresTheRunningCycleRate` scripts cycles as `3 * Stopwatch.GetTimestamp()` and expects `3 * Frequency / 1000`, band 0.9–1.5. The three 2 ms spins are real; the assertion is a Stopwatch-to-Stopwatch ratio, so host load does not move it except for the bracket bias the band allows. The null and zero tests calibrate, then flip the scripted reader, and assert null. `ThreadCpuCountsWorkTheThreadDoes` is the real-clock spin: discard one reading, spin until 60 ms of wall, require `> 15` and `<= 1.5 * wall`. `TickBudgetChargesThreadCpuNotDescheduledWall` is the pre-existing real-clock sleep.

A ×10 rate charges a fully scheduled 60 ms spin at about 6 ms and fails `> 15`. A /10 rate charges about 10× the on-CPU time and fails the ceiling whenever that time is above about 9 ms of a ~60 ms wall (`10 * cpu <= 1.5 * wall`). The author's measured /10 case (441 ms charged against about 44 ms real) fails the ceiling. The /10 survival band is about 1.5–9 ms of CPU inside a 60 ms wall, which is extreme starvation. The three injected fallbacks and the two null/zero tests do not read the rate. A /10 of the near-zero CPU of the 120 ms sleep can still pass `< 25`. The author's "best×10 and best/10 each fail 5 of 8" was not re-run; mutation is waived. The two rate assertions kill both mutants on a normally scheduled spin. That mismatch does not hold the land.

Descheduling lowers a correct cycle charge and cannot trip the 1.5× ceiling, because on-CPU time is at most wall. It fails the floor only when the thread is on-CPU for 15 ms or less of the 60 ms wall. On an invariant TSC the frequency ratio is 1, so turbo does not move the ceiling. On a counter that scales with frequency, a 60 ms spin at 1/4 of the calibration frequency charges 15 ms and `ShouldBeGreaterThan(15)` fails. The ceiling trips only when the spin's frequency exceeds 1.5× the calibrated rate, which means all three 2 ms calibration spins ran below about 67% of the later spin's frequency. After the discarded warmup, calibration and the measured spin are both busy, so the ratio stays near 1. The 1.5× band matches the bracket bias the calibration test already allows. The qualification failure was p95 wall 32.5 ms against 25 ms, which this floor still rejects when the thread actually ran.

The `Calibrate` comment and the why-missed round-2 paragraph now match that: deschedule over-charges, bracket reads under-charge by a fraction of a millisecond, a frequency-scaled counter under-charges by the frequency ratio, and an invariant TSC does not scale.

## Verification

Leased runs, native default output, `UseAppHost=false`, `TEMP=/tmp`. No checkpoint manifest, so `reviewedSourceClean` is false. No plan in the range, so `check-os-witnesses.ps1` was not run. The changed branch is the Windows arm; the seam tests construct `WindowsThreadCycleClock` and ran on Linux. `windowsFilters` is empty and `owed` is empty. `C1514HostLoadNativeOverheadTests` is in the `host-stats` selector (Integration, Slow) and was left out of this commissioned list, same as round 1. Its Windows method remains the author's native row.

At `27d8bdcf29e5fa9e797e81121b02e6ce5fb212cb`:

- SessionRunner build lease `9e0d96c2-f01a-4f80-8acb-71bc1276f098`, 0 errors. Run lease `e2230d67-f000-446d-b475-0bc6ea26ad2d`, 34 succeeded, 0 failed, 0 skipped, 2s 343ms. Per class: `C1514SamplerTickCpuTests` 8, `C1514HostLoadSampleTests` 6, `HostStatsSamplerTests` 7, plus selector extras `C1514HostLoadSpoolTests` 6 and `RunnerCapabilitiesTests` 7.
- Antiphon.Tests build lease `4e3d1d5e-f260-45a2-81de-e8dde5887d0e`, 0 errors. Guard lease `2088d701-a8cb-4f55-ae82-e9d295ecf885`, 53 succeeded, 0 failed, 0 skipped, 1m 17s 278ms. Per class: `TestClassificationPolicyTests` 42, `TestClassificationGuardTests` 1, `SlowTestRegistryMergeTests` 5, `SlowTestTripwireTests` 2, `NativeLinuxPolicyWitnessTests` 3. This is the five-class batch, not the nine-class union of 78.
- Derived-impact regen lease `0dfdb84d-f684-4d62-bc74-7993d7e7d905`, unchanged, windowsOnly 31, linuxOnly 1. Index sha256 `1e2d648d7e7fc7a2c8aa9049da258943eefeaf58e4234e65ddd67a0f2a449c85`.

`impacted-classes` on the round range: kinds `host-stats` only. No migrations row, so the downgrade selector count is 0. `check-evidence-diff.ps1` over `b57583cee95b75c62edd496247e2361c7cc4e034..27d8bdcf29e5fa9e797e81121b02e6ce5fb212cb`: 7 commits, 0 entries, 0 violations.

`origin/master` is `e4c45f4b4bb58870bb8684087a6d218e1b988cbe`. `git merge-tree --write-tree origin/master HEAD` exit 0, tree `0ba309b8bf5aafc4ab16c9d7365c54463ac13257`. Cherry-picking only the three round-2 commits onto that master conflicts, because their parent is not on master. Cherry-picking the seven commits from `807a989833835a9743bc3f384f6bea520961eb0c` through `27d8bdcf29e5fa9e797e81121b02e6ce5fb212cb` applies cleanly at `/tmp/c1514-r2-master` tip `bb04ca147d9850091b70844fa87a7b4ea2f06ae9`. `HostStatsSamplerService.cs` blob `7cdccf828c279c4681a997ce9f2189d0dc67a71c` and `C1514SamplerTickCpuTests.cs` blob `51d03a4b848a27d455718467f8e06cc623e58438` match the reviewed tip.

On that cherry-pick, after index regen:

- Regen lease `462b980f-922d-47b9-a223-41d4089a5df0`, unchanged, windowsOnly 31, linuxOnly 1. Merged index sha256 `db7b5dfd1f4c8bb053fe7a5745f3f16b95f68640505e83813ff7232e8239806f` (different bytes from the tip index; the generator agrees).
- SessionRunner.Tests build lease `ea4e358b-27eb-4a56-8a6e-3661d83c96fc`, 0 errors. Run lease `b78dd9a8-bee2-49ce-8cf2-cdf05077c0bb`, 21 succeeded, 0 failed, 0 skipped, 3s 135ms: tick 8, sample 6, sampler 7.
- Antiphon.Tests build lease `9bbada9e-5451-45f5-b2ae-e509419ed214`, 0 errors. Guard plus `DerivedImpactIndexTests` lease `bc655d75-73ae-47da-a49d-4c9ad8682331`, 80 succeeded, 0 failed, 0 skipped, 5m 03s 325ms: the five classes above (42+1+5+2+3) and `DerivedImpactIndexTests` 27. The committed-index test does a full-byte compare, so this is the regen witness.

Runner defaults revision 2 names server2 as the global runner. This review did not pass `-Runner`. Scratch trees `/tmp/c1514-r2-master`, `/tmp/c1514-r2-sr`, `/tmp/c1514-r2-guards`, `/tmp/c1514-r2-sr-master`, `/tmp/c1514-r2-guards-master`, and the round-1 index trees were left in place.

Class timeout remains 10 s. The new spin is 60 ms and the fallback sleeps are 40 ms. Measured class runs were seconds. No new poll-then-assert gap, no `ParallelLimiter` change, no plan performance budget in this diff.

## --- review evidence ---

subjectTaskId: a9f8cd21-113f-401e-baf2-86c7e8d57fc8
reviewedSourceSha: 27d8bdcf29e5fa9e797e81121b02e6ce5fb212cb
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 2
reviewedRange: 1a79c5119bce4c36e07e81bb43f5030ef072a4bf..27d8bdcf29e5fa9e797e81121b02e6ce5fb212cb
