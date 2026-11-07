# CARD-1757 HostStatsSampler per-process query

Windows `Process.WorkingSet64` reads one pid by snapshotting every process. The sampler did that once per sampled pid on every 5 s tick. This plan replaces that read. Sample identity stays the same: pid, start time, working set, and CPU. A recycled pid still blanks CPU only. A missing or exited pid still blanks both, and the sampler still stores a missing working set as 0.

## Decisions

- D-1. On Windows, open the pid once with `PROCESS_QUERY_LIMITED_INFORMATION` and read `K32GetProcessMemoryInfo` and `GetProcessTimes` from that handle. No `NtQuerySystemInformation` snapshot.
- D-2. On Linux, keep one `Process.GetProcessById` (`/proc/<pid>`) for both figures. Do not add a second read.
- D-3. The working set from that read is reused only when the next call is `WorkingSet` for the same pid on the same `SamplerProcessQuery` instance. The reuse is consumed immediately. Nothing is kept for a later tick. Maximum extra staleness is zero sample intervals.
- D-4. `IProcessCpuProbe` stays `SystemProcessCpuProbe`, so `SessionCpuWatchdogService` is unchanged. The sampler gets its own `SamplerProcessQuery` for CPU and working set.
- D-5. For the current process on Windows, CPU time is `Environment.CpuUsage.TotalTime`, matching `Process.TotalProcessorTime`. Other pids use kernel plus user time from `GetProcessTimes`.
- D-6. The 2-minute pid-reuse window is duplicated on the sampler query. `ProcessCpuProbe.cs` is not edited. A start time at most 2 minutes after the recorded start still returns CPU. A later start returns CPU null and still returns the working set.
- D-7. Code executed the checkpoint filters below through `scripts/build-slot.ps1` and the worktree's native output (`UseAppHost=false`, no `OutputPath`). The checkpoint tool's cold `bin-` outputs were not used: the task brief requires that native output, and CP-1 was already green. The `Build` column names the cold output a later tool run would use. Do not repeat the green CP-1 selection.

## Verification design

- V-1. `Working_set_reader_does_not_snapshot_the_system_once_per_process` reads the production working-set method and `ReadWindows`. It fails if the sampler snapshots with `GetProcessById` / `WorkingSet64` / `GetProcesses` / `NtQuerySystemInformation`, or if the Windows reader drops `OpenProcess`, `K32GetProcessMemoryInfo`, or `GetProcessTimes`. It also requires `AddHostStats` to leave `IProcessCpuProbe` as `SystemProcessCpuProbe` and the sampler's probe as `SamplerProcessQuery`.
- V-2. `Production_query_reads_this_process_once` constructs the production `SamplerProcessQuery`, reads this process, and requires one query for CPU plus the immediate working set. A start 2 minutes before the real start still returns CPU. A start 3 minutes before blanks CPU only and still returns a working set, in one query. A missing pid blanks both in one query. One sampler tick of this process plus a dead host pid costs two queries, and the next tick costs two more.
- R-1. Existing host-stats sampler, parse, endpoint, and store tests stay green.
- R-2. Policy, classification, slow-test, native-Linux witness, mechanical contract, platform guidance, and layering guard classes stay green in one run.

Red at base: V-1 failed on `84ecf16079d409138db7e0ae32d41e93e705c183` because `WorkingSet` contained `GetProcessById` (1 failed, 30 succeeded). Local mutants, restored before the fix commit: putting `GetProcessById` back failed V-1; dropping the same-tick cache made V-2 see 2 queries instead of 1. Mutation owns the deliberate pack. Both witnesses stay pending.

### Checkpoints

totalTimeoutMinutes: 30

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1757-host/ | host-stats | `/*/*/(HostStatsSamplerTests*)\|(HostStatsProbeParseTests*)\|(HostStatsEndpointTests*)\|(HostStatsStoreTests*)/*` | V-1, V-2, R-1 | 31 executed, 0 failed, 0 skipped | 31 | 2 |
| CP-2 | S1 | tests/Antiphon.Tests -> bin-c1757-guards/ | guards | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | R-2 | 75 executed, 0 failed, 0 skipped | 75 | 8 |
