# server2 session runner and build CPU profile (2026-10-31)

Task 32df662c (Investigate). This was read-only profiling inside container `antiphon-runner-session-runner-1`. Nothing was killed, restarted or reconfigured. The operator asked: "Can we run some profile on the session runner on server2? Double check it's the same or different."

## Verdict

**The runner process is not a bottleneck.** It used 0.19 of a core on average (peak 0.29) and about 670 MB, with a thread-pool queue length of 0 and GC pauses taking about 0.4% of wall time. The container's CPU comes from the runner's **children**, not from the runner. Over the window the container averaged 13.7 cores (peak 20.6). The single shared `VBCSCompiler` accounted for 68% of that (9.3 cores on average, peaks of 18). Almost all of its work came from two mutation loops, each running repeated full builds under **one** build-slot lease. Those loops held two of the four host slots for 14 to 38 or more minutes while 7 to 9 waiters queued.

**Same or different:** in shape the runner is the same as the desktop one: 22 to 23 threads, mostly idle, well under one core. In size it is smaller. Over its lifetime it averaged 0.17 of a core, against the desktop's 0.47 of a core (219,336 CPU-s over 129 h), and it uses about 0.67 GB against 1.3 GB. The desktop's hot paths were not measured: this task has no access to desktop files or processes. A desktop trace would be needed to say whether its extra CPU sits on the same paths.

## Method

- **/proc sampler:** `node` read /proc every 10 s, giving 66 samples over 651 s of wall time (16:15:47Z to 16:26:33Z). It recorded per-process utime+stime, RSS, threads, cwd and cmdline, per-thread CPU for pid 303, the runner's fd count, host `/proc/stat` and loadavg. Each process was attributed to a task through its cwd, or its ancestors' cwd, under `/work/worktrees/task-<id>`.
- **dotnet-counters and dotnet-trace 10.0.750501:** the network allowed `dotnet tool install --tool-path <scratch>/dtools`, so both were installed there. Nothing was installed system-wide.
  - `dotnet-counters collect -p 303`: 60 s at 5 s refresh (System.Runtime, AspNetCore.Hosting, System.Net.Http).
  - `dotnet-trace collect -p 303 --profile dotnet-sampled-thread-time`: 30 s, analysed with `dotnet-trace report topN`.
  - An exception-keyword capture: 15 s.
- **Shared slot broker:** `GET http://build-slots:8080/build-slots` was polled every 20 s, 17 times (16:20:45Z to 16:26:05Z).
- The raw evidence stays in session scratch and is not committed (generated evidence rule).

## 1. The runner process (pid 303, `dotnet Antiphon.SessionRunner.dll`, up 40.7 h)

| Metric (651 s window) | min | avg | max |
|---|---:|---:|---:|
| CPU, % of one core (/proc, 10 s) | 11.7 | 18.7 | 29.1 |
| RSS MB | 602 | 669 | 758 |
| Threads | 19 | 23 | 27 |
| Open fds | 281 | 283 | 286 |

| Counter (60 s, 5 s buckets) | avg | max |
|---|---:|---:|
| CPU s/5 s (user+system) | 0.99 (0.20 core) | 1.38 |
| thread_pool.queue.length | 0 | 0 |
| thread_pool.work_item.count /5 s | 1,777 | 2,040 |
| thread_pool thread-count change | 0 | ±1 |
| gc.pause.time s/5 s | 0.02 (0.4%) | 0.04 |
| gen0 / gen1 / gen2 collections /5 s | 8.4 / 1.3 / 0.18 | 23 / 5 / 1 |
| Allocation, MB/5 s | 84 (17 MB/s) | 269 |
| monitor.lock_contentions /5 s | 8 | 32 |
| SocketException /5 s | 33.6 | 190 |
| Committed heap | 492 MB; LOH 296 MB, gen2 181 MB | |

The thread-level split from `/proc/303/task`, as % of one core over the window: `.NET TP Worker` 16.6, `.NET Sockets` 0.7, `.NET EventPipe` 0.5 (the profiler's own cost), `.NET Timer` 0.2, everything else under 0.05.

The 30 s sampled thread-time trace shows the process waiting:

- 50.5% of thread-time is idle pool workers (`LowLevelLifoSemaphore.WaitNative`).
- 18.1% is `WaitHandle.WaitOne`.
- 12.1% is `Monitor.Wait`.
- 6% each goes to the Heartbeat timer loop, the `FileSystemWatcher` read loop and the socket event loop.

Under 1% of samples run managed code that is not waiting. Within that, the top frames are:

- PhoneHome: `PhoneHomeConnectionWriter.SendAsync`, `PhoneHomeFraming.WriteFrameAsync`, `PhoneHomeConnectionService.DispatchAndReplyAsync` and `ReceiveLoopAsync`, with the WebSocket and TLS (`SslWrite`/`SslRead`) frames under them.
- System.Text.Json serialisation and parsing.
- `SessionRunnerRuntime.RunnerSession.HandleOutput` and `SessionRunnerEventHub.Publish`.

No hot loop appears in session polling, transcript tailing, journal scans, Herdr or pty. The runner had 10 sessions.

The SocketExceptions, about 6.7/s, are caught inside the runner. The runner Serilog file has no socket warning since 06:13Z: the last warnings were phone-home `connect_OperationAborted` retries at 06:12 to 06:13. Without stacks, the exception-keyword capture only placed them on Kestrel socket-connection and HttpClient paths. Their CPU cost is within the 0.2-core total. The source is **unresolved**: a stack-enabled exception trace would settle it.

## 2. Build-side attribution

Host (`/proc/stat`, 24 cores, over the window):

- Busy 76.5% (18.4 cores): user 69.5%, system 6.8%, iowait 0.4%, steal 0.
- Idle per 10 s interval: median 18.6%, p10 5.5%, minimum 2.1%. Only 6 of 66 intervals were under 5% idle.
- load1: average 37.0, range 26.5 to 49.4.
- `MemAvailable`: 86 to 91 GB of 126 GB, against a slot memory floor of 16 GB.

The container averaged 13.7 cores; the other 4.7 busy cores are outside this container.

CPU by process family:

| Family | cores (avg) | share | RSS GiB (end) |
|---|---:|---:|---:|
| VBCSCompiler (1 process) | 9.31 | 67.9% | 12.18 |
| msbuild worker nodes | 1.28 | 9.3% | 0.63 |
| pwsh | 0.84 | 6.1% | 1.85 |
| claude (5) | 0.63 | 4.6% | 1.86 |
| test hosts (8-13) | 0.57 | 4.2% | 2.86 |
| Antiphon.Checkpoints | 0.31 | 2.3% | 0.98 |
| grok (3) | 0.25 | 1.8% | 0.54 |
| dotnet build drivers | 0.23 | 1.6% | 0.35 |
| **runner (303)** | **0.19** | **1.4%** | 0.72 |
| pty-host | 0.07 | 0.5% | 0.56 |
| container total | 13.72 | 100% | 23.74 |

CPU by task. VBCSCompiler is shown on its own row because it is shared and cannot be split by task from /proc.

| Task | cores | share | What it ran |
|---|---:|---:|---|
| (shared VBCSCompiler) | 9.31 | 67.9% | compiles for all builds |
| e76d3792 (Review) | 1.39 | 10.1% | `build-slot.ps1 -Label rev1622-mutants -- bash loop.sh` |
| d63efd0a | 0.94 | 6.9% | `test-nightly-failed-first.ps1` + CP-1 checkpoint |
| 1e52322c (Review) | 0.56 | 4.1% | Antiphon.Tests Unit shard (G-K) |
| 8562dc2a (grok) | 0.50 | 3.7% | `build-slot.ps1 -Label tree-kill-mutants -- bash /tmp/tk8562-mutants.sh` |
| 2a89d63d (Review) | 0.35 | 2.5% | checkpoint run |
| all others | < 0.15 each | | |

Concurrency per sample (min / avg / max):

- msbuild worker nodes: 0 / 19.6 / 28.
- dotnet build drivers: 2 / 2.7 / 3.
- Test hosts: 8 / 11.1 / 13.
- postgres processes: 12 / 17.1 / 25.

All 119 msbuild nodes observed were started with `/nodeReuse:false`. `Directory.Build.rsp` is working: no orphaned nodes.

Build slots, from the shared broker at `ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots`:

- Budget 4, `maxCpuCount` 6, memory floor 16,384 MB.
- 4/4 occupied in all 17 polls, with the **same four leases** throughout and no grant or release in 5.5 min.
- Waiters: 7 to 9. The oldest was waiting from 16:10:53Z, so it had waited at least 15 min.
- The runner's loopback broker (`127.0.0.1:8080/build-slots`) reports 0 occupied. It is not the broker in use (`docs/testing-and-build.md:1082`).
- `GET /build-slots/metrics` on the shared broker answers **HTTP 405**, so the CARD-1489 wait/held p95 cannot be read there.

| Lease label | task | granted (UTC) | held at 16:26 | what is inside |
|---|---|---|---:|---|
| rev1622-mutants | e76d3792 | 15:48:38 | ≥ 38 min | bash loop: `dotnet build tests/Antiphon.Tests` **with no `-maxcpucount`**: 23 msbuild nodes, 3 builds seen in the window |
| rv1e5-unit | 1e52322c | 16:07:57 | ≥ 18 min | test run (light compile) |
| CP-1@bin-d63-seed1 | d63efd0a | 16:08:52 | ≥ 17 min | checkpoint row |
| tree-kill-mutants | 8562dc2a | 16:12:39 | ≥ 14 min | bash loop: `dotnet build server/Antiphon.Server.csproj -maxcpucount:6`, **10 builds in 651 s** |

VBCSCompiler (pid 20495):

- **What it is:** `/usr/share/dotnet/sdk/10.0.403/Roslyn/bincore/VBCSCompiler -pipename:AnbIBB…`, cwd is the Roslyn dir, parent is pid 1 (reparented). It is the **only** VBCSCompiler in the container. The pipe name is derived from the user and the compiler directory, so every worktree's builds share it.
- **When it started:** 14:56:55Z (start tick 19180206 s against uptime 19185116 s). Whoever started it exited and left no surviving outputs from that minute, so **who** started it is not recoverable.
- **Memory:** RSS was 7.5 GB at 16:14, 8.6 GB at 16:19 and 12.2 GB at the end of the window. The VmHWM peak is **18.3 GB**. It ran 104 to 139 threads.
- **CPU:** lifetime 11 h 41 min of CPU in 5,445 s, about 7.7 cores.
- **GC:** `VBCSCompiler.runtimeconfig.json` sets `System.GC.Server: true`. With 24 cores and no cgroup CPU or memory limit (`cpu.max`/`memory.max` unlimited) it runs 24 server-GC heaps.
- **Why it never idles out:** CPU per 10 s swings between 0 and 18 cores, following the loop builds. The 10-minute idle timeout (`docs/testing-and-build.md:1080`) never fires while the loops rebuild every 1 to 4 minutes.

The build-environment policy in effect on server2:

- **CARD-1506 does nothing on Linux.** `scripts/lib/BuildDriverEnvironment.cs:27-29` returns no overrides when `!windows`, and `scripts/lib/build-driver-environment.ps1:6` returns early. So `UseSharedCompilation`, server GC, the MSBuild server and node reuse keep the SDK defaults on server2. `docs/testing-and-build.md:1065-1071` says the same: "Linux is unchanged."
- **Per-build parallelism:** comes only from the grant (`-maxcpucount:6`) that `Add-AntiphonMaxCpuCount` inserts.
- **The loop gap:** `scripts/lib/build-slot.ps1:193-195` leaves the command unchanged when argv[0] is not dotnet. `build-slot.ps1 -- bash loop.sh` is leased, but the builds inside the loop get the SDK's own `-maxcpucount`. CP-1 measured that as `MSBuildNodeCount = 24` on this host (`docs/testing-and-build.md:1080`). This window observed 23 worker nodes under the e76d3792 lease. The docs define a lease as "one driver invocation" (`docs/testing-and-build.md:1082`). A loop of builds under one lease falls outside that contract, and neither the wrapper nor the broker detects it.

**Would capping VBCSCompiler or per-build parallelism help?** Measured evidence:

- **CPU is not the queue's limit.** The host had idle CPU most of the time (median 18.6%, only 6 of 66 intervals under 5%). Memory was nowhere near the floor.
- **Lease hold time is what the waiters queue on.** The four leases did not turn over in 5.5 min, and two of them are multi-build loops.
- **Capping VBCSCompiler would only move the queue.** Its CPU is the compile work those builds would otherwise do in `csc`. Capping it lengthens each loop iteration, so the loop holds its lease longer.
- **Capping per-build parallelism helps fairness, not total throughput.** Concretely, the uncapped e76d3792 loop would stop taking 23 nodes.
- **A real throughput change needs the loops to stop holding a slot across many builds:** either a lease per iteration, or loops counted against the budget per build.

This is inferred from the measurements above. No A/B test was run, because the task is read-only.

## 3. Same or different (server2 versus desktop)

| | server2 runner (measured) | desktop runner (orchestrator figures) |
|---|---|---|
| CPU now | 18.7% of one core (window avg) | ~43% of one core |
| CPU lifetime | 25,343 CPU-s / 146,465 s = 0.17 core | 219,336 CPU-s / 129 h = 0.47 core |
| Memory | 0.60-0.76 GB | 1.3 GB |
| Threads | 19-27 (22 at end) | 22 |
| Host CPU | 1.4% of the container's 13.7 cores | not measured |

Same: one mostly idle process, with a thread count like the desktop's and far under one core. Neither runner is a bottleneck. Different: the desktop runner uses about 2.7 times the CPU and about 2 times the memory. Its hot paths are **not measured**. That needs a desktop `dotnet-trace` on that process, which this task cannot reach. The 1,600% container figure is the runner's **children**. The runner is 1.4% of container CPU, and the shared VBCSCompiler is 68%.

## Top three findings

1. **Mutation loops hold one build-slot lease across many full builds.** Evidence: the four leases were unchanged for 5.5 min; `rev1622-mutants` was held at least 38 min and `tree-kill-mutants` ran 10 Server builds in 651 s; 7 to 9 waiters queued for at least 15 min. This is the main cause of the waits for the other tasks.
2. **A loop lease escapes the `-maxcpucount` grant.** `scripts/lib/build-slot.ps1:193-195` injects only into a dotnet argv[0], so the e76d3792 loop's `dotnet build tests/Antiphon.Tests` ran with 23 msbuild nodes on a 6-node grant.
3. **One shared, server-GC VBCSCompiler carries 68% of container CPU and 12 to 18 GB of RSS.** It never idles out while loops rebuild. The CARD-1506 policy (`BuildDriverEnvironment.cs:27-29`) is Windows-only, so server2 keeps shared compilation and server GC. This is a memory issue, not a throughput issue, at today's 86 to 91 GB available.

## Recommended changes (ranked by expected effect; card-ready)

1. **Build slots:** make mutation and probe loops take one lease per build. Either the wrapper refuses a non-dotnet command without an explicit flag, or a per-iteration helper is provided for loop scripts. Update the reviewer mutant-loop recipe to match. Expected effect: two of four slots stop being held for 14 to 38 or more minutes, and the queue of 7 to 9 waiters turns over.
2. **Build slots:** pass the grant to commands launched through a non-dotnet wrapper, for example an exported `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT` that loop recipes must use, or a `BUILD SLOT` warning when argv[0] is not dotnet. Expected effect: a loop lease would build with at most 6 nodes instead of 24.
3. **server2 build-slots service:** deploy the CARD-1489 `/build-slots/metrics` endpoint to the shared broker, which answers 405 today. Expected effect: acquire-wait and held p95 become measurable, so any change to the budget can be decided on data.
4. **server2 compiler memory:** set a bounded GC for VBCSCompiler in the runner container on Linux. Options are `DOTNET_GCHeapCount` or `DOTNET_GCHeapHardLimit`, or extending the CARD-1506 `gcServer=0` override to Linux. Expected effect: memory only, against a VmHWM of 18.3 GB. No throughput change is expected while MemAvailable stays far above the 16 GB floor.
5. **Measurement follow-up (only after 1 and 2):** once the metrics exist, measure whether raising `SessionRunner__BuildSlots__MaxConcurrent` above 4 pays off, given a median of 18.6% host idle. Do not raise it before loops are counted per build.
6. **Runner (low):** identify the source of the runner's roughly 6.7 caught SocketExceptions per second with a stack-enabled exception trace. Expected effect: diagnostic only, since the cost is inside a 0.2-core total.

Do not cap VBCSCompiler CPU. The evidence says that would lengthen lease hold times and move the queue, not shorten it.

## Remaining uncertainties

- The process that started VBCSCompiler at 14:56:55Z is unknown.
- The SocketException source is unknown.
- The desktop's hot paths are unmeasured.
- The throughput effect of recommendations 1 and 2 is inferred, not A/B measured.
- This is one 11-minute window with this particular mix of six tasks.

## Not done, noted

- Fix idea: a per-iteration lease helper for loop scripts, plus a wrapper warning for a non-dotnet argv[0]. This is left for Plan.
