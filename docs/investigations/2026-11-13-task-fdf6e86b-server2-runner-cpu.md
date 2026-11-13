# Server2 runner CPU, 2026-11-13

Task fdf6e86b. Active trace of the Linux session runner container under real card-task load. No product code changed. Desktop AppHost, the Antiphon server, Postgres, and the live Windows qualification were not sampled.

## Where

Container hostname `0c7fc078dc23`, pid 1 is `docker-init` running `antiphon-dind-entrypoint.sh dotnet Antiphon.SessionRunner.dll`. The session runner is pid 351, `dotnet Antiphon.SessionRunner.dll`, listening on 8080. Diagnostics socket `/tmp/dotnet-diagnostic-351-2027693007-socket` was in this pid namespace, so `dotnet-counters` and `dotnet-trace` attached locally. CLK_TCK is 100. The host has 24 cores (`/proc/stat` and `nproc`). Container uptime at the start of the sample was about 6 h; host uptime was about 235 days.

The cgroup leaf from `/proc/351/cgroup` (`cpu,cpuacct:/docker/0c7fc078dc23...`) is not present on the mounted cgroup fs. `/sys/fs/cgroup/cpu,cpuacct/cpuacct.usage` tracked the host, not the container. Container CPU below is the sum of `/proc/<pid>/stat` utime+stime deltas inside this pid namespace. `nr_throttled` stayed 0.

## Window

Process sample every 5 s, 180 samples, 13:43:16Z to 13:58:19Z (895 s). At 13:43Z `GET /sessions` listed 7 sessions: 5 Running (codex `a32a4889`, grok `012fd5b2`, `bce31b71`, `23617b22`, `e6b0188d`) and 2 Exited. A claude session (`3f467ec8`, pid 7404) appeared during the window. That is the requested 5-7 live agent tasks. There was no minute with zero running sessions, so a true idle-runner cost was not measured.

## Who used the CPU

Host `/proc/stat`: 11.313 busy cores of 24 (47.14%). Container process sum: 9.281 cores, 82% of that host busy time.

| Role | Cores | Share of container |
|---|---:|---:|
| VBCSCompiler pid 30376 | 5.330 | 57.4% |
| dotnet test | 1.402 | 15.1% |
| checkpoint tool | 0.614 | 6.6% |
| MSBuild nodes | 0.500 | 5.4% |
| pwsh | 0.462 | 5.0% |
| other dotnet | 0.260 | 2.8% |
| grok (all) | 0.238 | 2.6% |
| session runner pid 351 | 0.184 | 2.0% |
| PtyHost (27 processes) | 0.080 | 0.9% |
| trace tools | 0.044 | 0.5% |
| claude | 0.041 | 0.4% |
| codex | 0.039 | 0.4% |
| `dotnet msbuild -getItem:ProjectReference` | 0.025 | 0.3% |
| nested dockerd + containerd | 0.001 | 0.0% |
| git, node | ~0 | 0 |

VBCSCompiler command:

`/usr/share/dotnet/sdk/10.0.404/Roslyn/bincore/VBCSCompiler -pipename:lr8A_fXYWY2oCHA1t0eWHW60AzOJaTAYmXgTv4p7f0M`

ppid 1, RSS 9887.8 MB at the last sample. Per-minute cores (pid alive at both ends): 6.34, 4.61, 0.11, 0.01, 4.79, 8.49, 11.57, 11.96, 5.41, 0.11, 0.03, 0.05, 8.90, 13.00. It is compile bursts. Quiet minutes are ~0.01 core. Peak minute 13:56:23Z-13:57:24Z was 13.00 cores.

Largest other processes: `Antiphon.Tests` exec on task-49ae044d 0.638 cores, `Antiphon.Tests` exec on task-3e20dd08 0.400 cores, checkpoint execute pid 23284 0.350 cores. The runner at 0.184 cores is smaller than one test process.

Session trees (descendants still parented under the pty host; VBCSCompiler is orphaned at pid 1 so its 5.330 cores sit in "orphan", which totals 6.567): grok `23617b22` 1.194, grok `bce31b71` 0.922, this trace session `e6b0188d` 0.142, codex `a32a4889` 0.115, claude `3f467ec8` 0.115, grok `012fd5b2` 0.042. About twenty other pty-host session ids were 0.000 cores. Combined PtyHost RSS was 2204 MB. That is memory, not CPU, and it is not filed.

Agent CLIs themselves are small. The CPU attributed to a session is mostly the dotnet test and pwsh it spawned. The dominant process is the shared compiler, which no longer has a session parent.

## Runner internals

`dotnet-counters` 10.0.750501, `EventCounters\System.Runtime`, pid 351, 300 one-second samples starting 13:42:46Z.

| Counter | Result |
|---|---|
| CPU Usage (%) | mean 0.699, p50 0.689, p95 1.048, max 1.488. This is percent of the 24-core machine: p50 0.165 cores, p95 0.252 cores, max 0.357 cores |
| Allocation rate | mean 11.5 MB/s, p50 10.7 MB/s, p95 20.7 MB/s, max 46.8 MB/s |
| Threadpool completed items | p50 439/s, p95 537/s, max 601/s |
| Threadpool queue length | p50 0, max 2 |
| Threadpool threads | mean 8.19 (7-9) |
| Gen 0 GC | p50 2/s (mean 1.61) |
| Gen 2 GC | p50 0 (mean 0.013/s) |
| GC heap | mean 238 MB (163-315) |
| LOH | mean 264 MB (236-284), stable |
| Working set | mean 440 MB (421-458) |
| Exception count | mean 0.8/s (sum of per-second rates 240 over 300 s) |
| Lock contention | mean 0.78/s |
| GC pause | mean 2.3 ms/s, max 15.1 ms in one second |
| Active timers | 17 |

`/proc/351/task` over the 895 s, by thread name: `.NET TP Worker` 0.129 cores, `.NET EventPipe` 0.043 cores (the 5-minute trace's own thread, spread across the 15-minute window), `.NET Sockets` 0.007, `.NET Timer` 0.001, everything else ~0. No thread held a core. Runner CPU per minute stayed between 0.12 and 0.28 while VBCSCompiler swung from 0.01 to 13.00, so the runner does not track compile load.

`dotnet-trace` 10.0.750501, `--profile dotnet-sampled-thread-time`, 5 minutes, 54 MB nettrace. Exclusive top frames are parked threads, not Antiphon methods: `LowLevelLifoSemaphore.WaitNative` 40.44%, `Monitor.Wait` 17.83%, `WaitHandle.WaitOneNoCheck` 17.83%, `Watcher.Read` 5.94%, `ManualResetEventSlim.Wait` 5.94%, `GC.RunFinalizers` 4.73%. Those percentages sum near 100% and match blocked threadpool, lock, and file-watcher threads, so this profile is not a CPU ranking. The Antiphon frames that appear are each at or under 0.03% exclusive: `PhoneHomeOperation.TryReadAsObject` 0.03%, `SessionRunnerRuntime.HandleOutput` 0.02% (`SessionRunnerRuntime.cs:4458`, appends the pty chunk, trims the live buffer, publishes `SessionOutput`), `SessionRunnerEventHub.Publish` 0.01%, `PhoneHomeEventWindow.ReserveAsync` 0.01%, `OptimizedInboxTextEncoder.Encode` 0.01%, `JsonDocument.TryParseValue` 0.01%. No busy loop, no single-thread spinner, no `Task.Delay(0)` poll showed up.

`HandleOutput` and the 300 ms transcript tailers (`TranscriptTailer.cs:38`, `GrokTranscriptTailer.cs:34`, `CodexTranscriptTailer.cs:63`) are consistent with ~440 threadpool completions per second costing only 0.13 cores. Not filed.

Host-stats at 13:48:49Z agreed on the runner (22.4% of one core) and disagreed with the host picture in the way the probe is written: `cpuPercent` 34.33 is `/proc/stat` (`LinuxHostStatsProbe.cs:6-8`, `:116-122`). 30-minute series, 360 points: avg 32.78%, p50 20.80%, p95 87.45%, min 6.19%, max 95.63%. `processCount` 3750 is the host. `ReadTargets` (`HostStatsSamplerService.cs:174-195`) lists only the runner and live session/pty-host pids, so VBCSCompiler is invisible. `/host-stats/series` has no per-process metric (a missing metric or window is 400).

Build-slot broker at 13:53:09Z: budget 4, `maxCpuCount` 6, occupied 3 (CP-6, CP-8, CP-93). The runner's own `/host-stats` `buildSlots.occupied` was 0 at 13:48:49Z because that field is the in-process broker, not `http://build-slots:8080`.

## Mechanism

`BuildDriverEnvironment.GetOverrides` returns no variables when the process is not Windows (`scripts/lib/BuildDriverEnvironment.cs:27-29`). `UseSharedCompilation=false`, server-GC off, and MSBuild node reuse off are the Windows policy from CARD-1506, which says Linux/server2 behaviour stays unchanged. CARD-0795 already recorded this same VBCSCompiler at 4.5-6.8 cores on server2 and did not ship `-p:UseSharedCompilation=false`: quiet serial `Antiphon.Tests` builds were 278 s against 153 s (81.7% slower), and D-4 stops above 50%. The 2026-11-13 numbers are the same mechanism, still in place, now with a 9.9 GB working set and a 13-core minute while three build slots were leased. The per-lease `maxCpuCount` of 6 does not bound the one shared compiler.

Capping that process at 6 cores would have removed 2.49+5.57+5.96+2.90+7.00 = 23.92 core-minutes across the 14 minute buckets, about 1.7 cores off the average, and cut the peak minute from 13.0 to 6. Quiet minutes would be unchanged. That is the opposite shape from CARD-0795, which slowed the serial case.

## Cards

- CARD-1884 Host-stats reports host CPU and misses VBCSCompiler. Measurement gap.
- CARD-1885 Cap server2 VBCSCompiler at the build-slot CPU count.

## Not done, noted

Runner allocation is about 11 MB/s and the LOH holds about 264 MB, but no method owns enough CPU to be a separate optimisation. Twenty-seven pty hosts hold about 2.2 GB RSS at 0.08 cores combined. `/tmp` held 113,199 entries (46,983 `clr-debug-pipe-*`, 23,470 `dotnet-diagnostic-*`); one readdir finished in under a second, so it is not shown as CPU. A zero-session runner sample was not taken.

## Uncertainties

Container CPU is the pid-namespace sum because the cgroup leaf file is not mounted. Short-lived processes can miss up to one 5 s sample at each end. Minute rows omit a pid that was not alive at both ends; the role table does not. The trace profile samples parked threads, so its percentages are not CPU share. Desktop server CPU is unknown.
