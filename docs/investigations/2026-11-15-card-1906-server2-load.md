# CARD-1906: server2 load and CPU consumers

Investigate task `665816dd-f55c-465a-8e8f-355c279169c1`, 2026-11-15 UTC.
Source baseline: `64e3d78c487d51570f708492ab67b5db6d0de9a9`, equal to remote master when work began. Assigned branch: `feat/card-task-665816dd`.

## Finding and confidence

**Confirmed mechanism: the six-node grant limits an MSBuild driver, not the shared Roslyn compiler.** In today's passive 61.879-second window, the one `VBCSCompiler` used **8.868 cores**, with affinity `0-23`, while the shared broker reported three occupied leases and `maxCpuCount:6`. The compiler was reparented to PID 1. The runner used 0.143 cores in that window. This reconstructs the same mechanism as the stored 2026-11-13 trace, rather than inferring it from a lifetime `ps %CPU` number.

**The host was bursty, not continuously CPU-saturated during this investigation.** Three separated windows averaged 5.617, 12.884 and 3.771 busy cores of 24. The retained half-hour series reached 95.203% CPU and load 52.65, but averaged 34.044% CPU. Load average alone cannot establish that all 24 CPUs were busy. These observations do not prove that load caused each timeout listed on the card, or establish a safe task count.

Other live explanations remain: MSBuild/build work, test/script subprocesses, filesystem waiting, and unobserved host workloads. Background-container names cannot be assigned a CPU number from this namespace. The appropriate next stage is **Decide**, with the compiler escape confirmed and the other choices explicitly uncertain.

## Card and historical evidence

`card.ps1 get CARD-1906 -Board Antiphon` at approximately 04:46Z returned card ID `30ef206a-4501-46a5-ab8c-8ff566170cd5`, board `8988ca03-7414-47ad-b0b6-51556c701703`, InProgress. Its title is “server2 oversubscription: wall-clock test budgets flake; concurrency and load-aware budgets”. Its description cites load 20–77, 5–8 tasks, the compiler's earlier 5.3-core average, and Debug/Review/Merge reports including `6130909c`, `37cb8e72`, `49ae044d`, `1ba2c9cb`, `846b5854`, `bfd0c681`, `19b61d3a`, `b9760da0`, `dace701b`. Those incident reports were not individually replayed here; their causal claims remain card evidence, not fresh reproductions.

`card.ps1 history CARD-1906 -Board Antiphon` returned one revision: Move, 2026-11-15 04:45:03, by `card-transitions`, “Task 665816dd (Investigate, High) dispatched against this card.”

The CARD-1885 stored investigation at commit `428903d83a2ac55c178612e1105a1075f4162803`, branch `feat/card-task-fdf6e86b`, path `docs/investigations/2026-11-13-task-fdf6e86b-server2-runner-cpu.md`, records 180 samples over 895 seconds: host 11.313 cores; visible process sum 9.281; compiler PID 30376 5.330; tests 1.402; checkpoint 0.614; MSBuild workers 0.500; runner 0.184. The compiler's peak minute was 13.00 cores. The card's arithmetic of 23.92 core-minutes above six across fourteen minute buckets is a historical counterfactual, **not measured load reduction after activation**.

CARD-1893 (`1f1d24c5-ea6b-46c5-82e6-14a0e5cf1dcb`, Backlog, zero revisions when read) records dropped history appends and `binding-unknown` on 2026-11-13. The current read below reproduces the binding gap; the collector exception/path mechanism was not diagnosed here.

The requested `2026-11-14-desktop-cpu-trace.md` was not present in reachable history under that exact path. The matching trace commit is `c1963c4c00df597e70a197f1f11b1c55c34177ad`, path `docs/investigations/2026-11-13-desktop-cpu-trace.md`: desktop server 0.99–1.27 cores versus runner 0.07–0.10; server hotspots approximately 0.52 cores in prompt selection and 0.45 in finalizer churn. Those are **desktop** results and are not added to server2 CPU totals.

## Sampling boundary and method

This session runs inside container `0c7fc078dc23`, with 24 logical CPUs, `CLK_TCK=100`, page size 4096. `/proc/<pid>` exposes this container's PID namespace. `/proc/stat` and `/proc/loadavg` expose host totals; the implementation documents that distinction at [LinuxHostStatsProbe.cs:5](../../src/Antiphon.SessionRunner/LinuxHostStatsProbe.cs#L5) and computes CPU from the first eight counters at [line 132](../../src/Antiphon.SessionRunner/LinuxHostStatsProbe.cs#L132).

The current mountinfo explicitly binds `/sys/fs/cgroup/cpu,cpuacct` to `/docker/0c7fc078dc232e8c80bde972199f015ed3d17ff94e77ef1bc118a86c191f0a46`, matching `/proc/self/cgroup` and all observed consumer processes. The mounted root's `cpuacct.usage` is therefore treated as the **runner subtree** counter, including short-lived children. Sibling host cgroups are not exposed. Quota was `-1`, period `100000`, shares `1024`, cpuset `0-23`. `nr_periods`, `nr_throttled`, and `throttled_time` stayed zero. Unlike the earlier report's description of its cgroup ambiguity, today's attribution has the mount root retained as evidence; no historical cgroup value has been reinterpreted.

CPU cores = `delta(utime+stime) / 100 / elapsed monotonic seconds`. Host busy cores = `(delta first-eight total - delta idle - delta iowait) / 100 / seconds`. Cgroup cores = `delta cpuacct.usage / 1e9 / seconds`. Host percent uses the host counter total, so tick quantization can make busy cores differ slightly from `24 * percent / 100`.

Sample A has endpoint snapshots only. B/C have approximately one-second process scans. PID plus start ticks prevents reuse attribution. New processes contribute their observed ticks; exited children can lose their final unsampled interval and processes born/exited between scans are missed. Role sums are lower bounds, with the residual retained. Only executable names, roles, counter data and a small allowlist of compiler-policy variables were saved; raw command lines/environment were not saved. The initial ad-hoc classifier matched prompt text in CLI argv; reduction corrected CLI names before classification. Dotnet processes with observed MSBuild children are counted as build drivers. Ambiguous dotnet processes remain separate.

No workloads were launched for these measurements. The required guard build/run occurs **after** the passive windows and must not be credited to them.

## Three separated samples

All times on 2026-11-15 UTC. A/B/C correspond to ignored raw evidence labels `s1`/`s3`/`s4`; `s2` is a supplementary interval between A and B.

| Quantity | A 04:49:14.225–04:49:44.559 | B 04:52:04.414–04:53:06.294 | C 04:55:08.902–04:56:09.993 |
|---|---:|---:|---:|
| Monotonic duration, s | 30.333836 | 61.879036 | 61.089825 |
| Host busy cores / CPU % | 5.617 / 23.403% | 12.884 / 53.769% | 3.771 / 15.718% |
| Host iowait % | 1.747% | 0.640% | 1.168% |
| Load1 start → end | 9.30 → 8.17 | 27.09 → 18.45 | 10.14 → 6.43 |
| Load5 start → end | 16.31 → 15.35 | 19.28 → 17.87 | 14.62 → 12.71 |
| Load15 start → end | 20.89 → 20.43 | 21.07 → 20.44 | 18.92 → 17.99 |
| Runner cgroup cores | 5.057 | 12.340 | 3.261 |
| Host minus runner cgroup cores | 0.560 | 0.544 | 0.510 |
| Observed process CPU cores, lower bound | 1.794 | 11.952 | 2.912 |
| Cgroup CPU not assigned to sampled processes | 3.263 | 0.388 | 0.349 |
| Compiler PID 8658 cores | 0.047 | **8.868** | 0.009 |
| Runner PID 351 cores | 0.169 | 0.143 | 0.132 |
| Shared broker occupied / budget, at start | 2 / 4 | 3 / 4 | 3 / 4 |
| Shared broker waiters, at start | 0 | 0 | 0 |
| Available memory / admission floor, MiB | 100652 / 16384 | 95666 / 16384 | 99225 / 16384 |

All named processes below share the runner cgroup described above. A misses most short-lived work, so its stable-process ranking is not a complete CPU attribution.

| Consumer group, cores | A | B | C |
|---|---:|---:|---:|
| VBCSCompiler | 0.047 | 8.868 | 0.009 |
| pwsh children | 0.118 | 1.242 | **2.224** |
| Dotnet tests/runners classified as test | 0.075 | 0.894 | 0.112 |
| Checkpoint tooling | 0.903 | 0.186 | 0.114 |
| MSBuild workers + recognized build drivers | 0 | 0.043 | 0 |
| Agent CLIs and node (includes this sampler) | 0.364 | 0.272 | 0.214 |
| Antiphon.SessionRunner | 0.169 | 0.143 | 0.132 |
| PtyHost aggregate | 0.105 | 0.089 | 0.082 |
| Nested dockerd + containerd | 0.001 | 0.001 | 0.001 |

Largest individual consumers: in A, checkpoint processes PID 9472 0.595 and 8834 0.234 cores, runner PID 351 0.169; in B, compiler PID 8658 8.868, dotnet test PID 1973 0.506, dotnet test PID 32509 0.226; in C, pwsh PID 23032 0.962, runner PID 351 0.132, dotnet test PID 22726 0.104. Many pwsh processes descending from test PID 32509 account for much of C's group total; they are not extra standing agent seats.

The supplementary interval, 04:50:26.735–04:50:57.561 (30.825944 s), measured host 10.235 cores, runner cgroup 9.697, compiler 2.597, five MSBuild workers 2.968, recognized build-driver work 1.131, pwsh 1.423 and runner 0.168. Worker PIDs 26225–26229 were each 0.555–0.636 cores, parent PID 26097. This is evidence that the dominant group changes with the build phase.

Compiler identity was stable: PID 8658, start ticks `2043440597`, PPID 1, affinity `0-23`; one compiler was observed. No `DOTNET_PROCESSOR_COUNT`, `DOTNET_GCHeapHardLimit`, `ANTIPHON_LINUX_COMPILER_POLICY` or `DOTNET_gcServer` entries appeared in its readable allowlisted environment. Peak observed RSS in B was 6,325,063,680 bytes (5.891 GiB). The approximately one-second peak was 19.805 cores; the full-minute average, not that short peak, is the decisive cap witness. CPU above six is not evidence of a stuck idle spin: C returned to 0.009 cores.

`docker stats --no-stream` and `docker ps` returned no rows on the nested daemon. At 04:54:03Z `docker info` reported `name=0c7fc078dc23`, CPUs 24, containers-running 0. Thus active nested Postgres/Redpanda containers were not observed, but host containers are outside that daemon. No fresh CPU or RSS number is available for host hermes-dashboard, openclaw, Windmill workers, or host Postgres/Redpanda. The brief's “hermes-dashboard 45 GB” is not verified by this investigation. Their combined host CPU plus other outside-subtree work is constrained only by the approximately 0.51–0.56-core mean residual in A/B/C, subject to independently read-counter skew; no individual service attribution follows.

The permitted read-only `ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=5 mc@server2 ...` attempt exited 255: `No ED25519 host key is known for server2 and you have requested strict checking. Host key verification failed.` No trust bypass, host Docker access, signals, restarts, configuration writes or volume changes followed.

## Live admission and telemetry

| Read-only response / time | Evidence |
|---|---|
| `/api/session-runners/server2/capacity`, approximately 04:50:10Z | declaredCapacity 10, maxCapacity 10, configuredMaxInFlight null, effectiveLimit 10, occupied 5, available/dispatchEligible true |
| `/api/hosts`, approximately 04:50:10Z | server2 source runner, effectiveLimit 10, inFlight 5, no host override |
| Project pipeline, 04:54:03.7768233Z | project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`; maxParallel 12 from project revision 4; Code/Review combined cap 5, Debug 4; four project tasks open/parallel, host inFlight 4; mode LegacyOpen |
| Same pipeline's top-level maxConcurrentTasks | 2; this is not server2's effective host capacity |
| Shared `/build-slots` during B | enabled true, budget 4, maxCpuCount 6, occupied 3, no waiters, memory floor well below available |
| Runner status | buildVersion `ab7af12c699039270af7e22a5d7fd794035bc325`, from the November 13 build, not this worktree's HEAD |
| Runner `/host-stats` | HTTP 200; host CPU/load and targeted runner/session/pty metrics work; no `topConsumers` field on the observed runtime; in-process buildSlots occupied 0 while shared authority occupied 2–3 |
| Shared `/build-slots/metrics` | HTTP 405, no lifecycle metrics available |
| `/api/hosts/server2/load` | HTTP 200, physicalHostKey/current null, coverage 0, sample_stale, shadow true, unknown command/dispatch pressure |
| Durable raw history, 04:30–04:50Z, limit 720 | HTTP 200, items [], nextCursor null, complete false, gaps [`binding-unknown`] |

The unscoped null-project pipeline default reports maxParallel 6; that is not the Antiphon project's overridden 12. Neither proves a server2 `MaxConcurrentTasks=6` ceiling. Runner-bound tasks use runner/host capacity, not the desktop global cap: [AgentTaskDispatcher.cs:525](../../server/Application/Services/AgentTaskDispatcher.cs#L525), [line 1439](../../server/Application/Services/AgentTaskDispatcher.cs#L1439), [AgentTaskService.cs:2020](../../server/Application/Services/AgentTaskService.cs#L2020). Persisted capacity precedence and operator-only host budgets are documented in [docker-stack.md:1560](../docker-stack.md#L1560) and [ops-http.md:172](../ops-http.md#L172). The operator's approximately seven-task target is a dispatch direction, not a measured seven-seat runtime setting.

The **shared** broker is `http://build-slots:8080/build-slots`, inherited through `ANTIPHON_BUILD_SLOTS_URL`. The runner's own broker count is not the host admission count. The shared compose settings are [docker-compose.server2-runner.yml:167](../../docker-compose.server2-runner.yml#L167): four drivers, six nodes, 16384 MiB, renew liveness. The broker grants based on FIFO slots and available memory, with no CPU/load test in [BuildSlotBroker.cs:103](../../src/Antiphon.SessionRunner/BuildSlotBroker.cs#L103). At most four conforming driver invocations are admitted; four grants times six is nominally 24 MSBuild nodes, **not a hard host CPU quota**. Test hosts and nested children can create their own threads/processes. There was no broker wait or memory pressure in the sampled windows; historical waits cannot be measured from today's unavailable metrics.

The wrapper applies the grant to recognized build verbs, warns that implicit `dotnet run` builds do not receive `-maxcpucount`, and prepares the child before launch: [build-slot.ps1:94](../../scripts/build-slot.ps1#L94). Existing defaults disable node reuse via `Directory.Build.rsp`, while Linux still permits shared compilation: [testing-and-build.md:1308](../testing-and-build.md#L1308). The Linux compiler policy is opt-in only (`ANTIPHON_LINUX_COMPILER_POLICY=1`) at [BuildDriverEnvironment.cs:15](../../scripts/lib/BuildDriverEnvironment.cs#L15) and [line 329](../../scripts/lib/BuildDriverEnvironment.cs#L329). The launcher has its own affinity/runtime environment; preparation failure preserves the stock compiler ([line 358](../../scripts/lib/BuildDriverEnvironment.cs#L358)). The generic server-GC/shared-compilation/MSBuild-server overrides are Windows-only ([line 378](../../scripts/lib/BuildDriverEnvironment.cs#L378)). A node-count grant cannot bound this orphan shared compiler's all-CPU affinity.

The top-consumer work reviewed on master was: `522cd98c29cd357db153869d4c140f040721e015` (rank CPU deltas), `86af086fc17fc3e255223bff803dc3043595059e` (cached off-tick scan), `fa90c443c27a6eb1aa946d7d89687f0b338fbcdd` (owned worker/clock fallback), `652c10417f2f613282ad83966799931e51387d7a` (late publish/stalled-clock red witnesses), and `f4f2c770641cb6b2ff27a3616c751592a131dcf3` (scan-epoch publish and clock repair). Current source bounds scans at eight top rows, 512 PIDs, about 10 ms CPU: [HostProcessTable.cs:28](../../src/Antiphon.SessionRunner/HostProcessTable.cs#L28); delta ranking binds PID/start identity at [HostTopConsumerTracker.cs:98](../../src/Antiphon.SessionRunner/HostTopConsumerTracker.cs#L98); owned scan/publish behavior at [HostStatsSamplerService.cs:179](../../src/Antiphon.SessionRunner/HostStatsSamplerService.cs#L179). These commits are later than the running runner build. Repository source availability does not prove activation or visibility beyond the container's `/proc` namespace.

## Retained half-hour distribution

Server `/api/hosts/server2/stats/series?metric=cpu&window=30m` and the corresponding `metric=load` both returned 360 points at five-second cadence, 04:21:29.0452211–04:51:24.0474603Z. These observations predate the guard build.

| Metric | Mean | Min | Empirical p95 | Max | Additional counts |
|---|---:|---:|---:|---:|---|
| Host CPU % | 34.0441 | 1.6555 | 86.7202 | 95.2027 | 12/360 points >=90% |
| Host load1 | 24.5684 | 7.58 | 42.29 | 52.65 | 175/360 >24; 62/360 >36 |

The load series is a smoothed runnable/uninterruptible-work diagnostic; its p95 is not a per-test latency percentile. Its high value beside lower mean CPU does not by itself distinguish earlier CPU bursts from blocked work. Supplementary sample iowait was 5.551%, higher than A/B/C. No disk-latency or filesystem-stack trace was collected, so moving test data cannot be credited with an expected CPU saving.

## Evidence ranking for the caller's existing options

This is an investigation, not a new fix design or an enablement recommendation. The following ranks **strength of observed CPU attribution**, not promised throughput gains. Settings are existing controls or options named in the brief; none was changed.

| Evidence order | Existing control / location | Measured reduction evidence and risk boundary | Decision ownership |
|---|---|---|---|
| 1: shared compiler | CARD-1885 opt-in in `scripts/lib/BuildDriverEnvironment.cs`; preparation/affinity at lines 329/358 | Largest identified burst: 8.868 cores today, 13.00-core historical minute. Today's interval contains 177.446 CPU-seconds above a hypothetical six-core-minute allowance; this is displaced work, not proven saved CPU. Only one compiler exists in the sampled namespace, so “reduce compiler instances” has no observed duplicate-instance target. Full-solution/concurrent acceptance remains incomplete; build delay is an explicit risk. | Operator activation; separate Plan/Code if design changes |
| 2: build/test admission and MSBuild | Shared compose `SessionRunner__BuildSlots__MaxConcurrent`/`MaxCpuCount`; server host budget and runner capacity | Supplementary workers + build-driver work used about 4.10 cores. Four six-node grants reserve no nominal margin. Broker admission is a real workload-count control, but no paired 4/3-driver or 7/5-task experiment establishes the reduction, throughput cost or safe limit. | Host budgets/capacity are operator-only; Docker settings operator-only |
| 3: test/script subprocesses | Test-row selection/parallelism; wrapper only leases drivers | pwsh group used 2.224 cores in C versus runner 0.132. Reducing standing seats need not remove these descendants or the shared compiler. No stable cores-per-seat estimate exists. | Operator task target; separate test-owner work for any behavioral change |
| Unranked: runtime GC/PGO/server reuse | Existing Windows-only override keys at BuildDriverEnvironment lines 378–393; no Linux TieredPGO control in that policy | No current GC/JIT CPU profile. CARD-1885's stored evidence reports prior `UseSharedCompilation=false` serial builds 278 versus 153 s, 81.7% slower. That is evidence of a real risk, not proof every runtime variant has that cost. | Separate measured decision; no Linux setting change authorized here |
| Unranked: host background containers | Host Compose/cgroup/nice settings; not visible in this checkout's nested Docker daemon | Each container's CPU/RSS, shares and quota are unknown. Current outside-runner residual averages about half a core in A/B/C; the claim that these services dominate this measured CPU window is unsupported. Throughput/latency tradeoffs and settings cannot be ranked without host-daemon observations. | Operator-only host/Docker decision |
| Unranked: test Postgres/Redpanda placement | Testcontainers configuration and host tmpfs/cpuset options named in brief | No active nested container observed. No measured CPU-vs-I/O benefit, memory tradeoff, or isolation impact. Cpusets change available compute; tmpfs changes memory/storage behavior, so neither is evidence of CPU work eliminated. | Operator-only Docker/host settings; separate test-owner design |

The existing CARD-1885 bounded experiment met numeric gates on a **small** project, with only three measured pairs and idle included in minute CPU. It explicitly leaves full solution throughput, concurrent stress, complete GC context, production idle release and Windows qualification unresolved: [2026-11-13-card-1885-debug.md:127](2026-11-13-card-1885-debug.md#L127). Default remains off; this report does not upgrade those gates.

**Seven versus five:** live occupancy was five at approximately 04:50Z and four at 04:54Z. CPU was below capacity on average in every new window, while an uncapped compiler still exceeded six cores. There is no paired seven-seat/five-seat dataset and therefore no defensible percentage saving from changing the target. Sustained matched-window CPU/run-queue pressure with seven active tasks, corresponding leased build/test activity, and improved completion/latency with five would resolve this question. The historical load 20–77 is reason to measure it, not enough to select a new limit. The present measurements do not justify overriding the operator's approximately seven-task direction.

## Three measurements for any subsequent decision

1. **Resource envelope under recorded workload:** host CPU busy, runnable/blocked counts, load1/load5, iowait; compiler per-process minute cores/affinity/RSS; MSBuild/test groups and host-container cgroups. Record source/runtime SHAs, active seats, driver leases and observation coverage. A p95/maximum distribution must cover comparable busy windows, not just quiet means.
2. **Useful throughput and latency:** completed matched build/test rows per hour, with admission wait, driver execution, setup/body/teardown and total wall separated; retain p50/p95 plus sample counts. Track the same work and concurrency so a lower CPU graph caused by doing less work cannot be mistaken for a throughput improvement.
3. **Reliability and displacement:** timeout/failure/rerun counts over identical selected tests, compiler fallback/extra-instance counts, and impact on background-service latency/memory/I/O. Preserve incomplete/unsupported telemetry and original failing receipts. No target numeric improvement is invented from today's unpaired observation.

## Reconstructable counter transcript and provenance

The following unedited counter values suffice to reconstruct the main CPU results without relying on a generated file that disappears with worktree retirement. `compiler_ticks` is utime+stime for identity `8658:2043440597`.

```text
A start monotonic=20439737.499819648 cpuacct.usage=1705347121400982 compiler_ticks=1754827
cpu  3266346502 129897365 470057018 44894923354 139390048 0 61942572 0 0 0
A end   monotonic=20439767.83365612  cpuacct.usage=1705500520785095 compiler_ticks=1754970
cpu  3266360292 129897469 470059633 44894977852 139391320 0 61943103 0 0 0
B start monotonic=20439907.689078342 cpuacct.usage=1706885347647309 compiler_ticks=1828200
cpu  3266490590 129898094 470072494 44895151559 139406099 0 61944981 0 0 0
B end   monotonic=20439969.56811408  cpuacct.usage=1707648951066568 compiler_ticks=1883072
cpu  3266565011 129898423 470076673 44895219160 139407048 0 61945779 0 0 0
C start monotonic=20440092.17722807  cpuacct.usage=1708266904578683 compiler_ticks=1888728
cpu  3266622102 129898866 470085448 44895433553 139418572 0 61947744 0 0 0
C end   monotonic=20440153.267052855 cpuacct.usage=1708466121336181 compiler_ticks=1888782
cpu  3266640078 129899030 470089508 44895555356 139420283 0 61948579 0 0 0
```

Generated snapshots, one-second scans, sanitized GET bodies and local sampling/reduction scripts remain ignored at `/work/worktrees/task-665816dd/.antiphon/evidence-665816dd/`. They are not committed or claimed durable beyond this worktree. Essential counter values, API rows, limitations and results are retained above. Snapshot SHA-256 values:

| File under that evidence root | SHA-256 |
|---|---|
| s1-a.json | a49c6f27891b79da1212fb339383963560d947c9279156be2fae0c5ff2c2a827 |
| s1-b.json | 90e487d70c12b9d3b166fa5609caaaf7b07c0ad710676e6e7b28abafc3a76ba4 |
| s3-a.json | 8bf30deecfaedd8398e1c6b9fc0926609ab095bf564128489ae5e5f7c9bf6242 |
| s3-b.json | fc13a4c3537e4a5e6983815c00ccf9b89055bafa0aa5bad646a40399b0e29872 |
| s4-a.json | 92c11026f5426e068d68c08753e0d7db7ad73788f7f1a2187cfa08f7d91f90de |
| s4-b.json | 32db29e30f1dcc3237934db92ef079b5554c2de1edbae189c20efd90273154c6 |
| cpu-series.json | 22e5f3bfe6ffb5fa0d8253ac58e5c47fd6c379293b6bee64fc20f22b6e7db74b |
| load-series.json | b06d42f029681784891ce2629099612e4a07bbfa5d129c57d22c8cb7a3883275 |
| scope.json | 0c4b6620a2d5aa50998cf9fbb1b67dc227ab4455949a3093b37a6899d8223ac7 |
| project-pipeline.json | 93e02cef0bd1abacd2add9c64127294a32f4146736b97f7806cb430ffcf0e72b |
| history.json | f4a585c202ac0f4769f9383b6dd388f4ca4a7a7fd47ee115a3a01ccf8f495469 |

Repetition uses read-only `/proc` deltas, matching cgroup mount roots, the inherited shared `/build-slots` GET, runner `/host-stats`, server `/api/hosts/server2/load`, `/api/hosts/server2/load/history`, and `/api/hosts/server2/stats/series`. Host-container attribution needs an operator-approved host context with established SSH trust; absence in nested `docker ps` does not supply that evidence.

## Validation and boundaries

Only this Markdown report is a deliverable. No card, product/test source, configuration, policy input, derived index, deployed process, Docker volume, Windows qualification or target branch was changed. Standing authority sufficed for these local read-only diagnostics and task-branch publication; no approval was requested.

At baseline, the stored nightly `policyHash` is `b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d`; the newline-normalized protected-input digest was independently reconstructed as `26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e`; `NativeLinuxPolicyWitness.LegacyExclusionDigest` is `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`. No input requiring a repin changed.

The brief's required guard union will be built/run through separate host build-slot leases after this evidence slice is committed. Actual outcomes will be appended before settlement; no test pass is claimed by this initial report.

## Remaining uncertainty

- Which host containers consumed the outside-runner residual, and their CPU/memory settings. Resolved by three matched host-daemon stats/cgroup/process samples from trusted host access.
- Causality for each historical timeout and the safe task/driver count. Resolved by workload-matched completion/failure and scheduling data; current seats/build phases are not an A/B experiment.
- Full-solution/concurrent/production-idle acceptance of the opt-in compiler policy. The existing reduced experiment does not settle it.
- Durable-history collector append failure details and physical binding configuration. Today's binding gap matches CARD-1893 but does not diagnose its writer exception.
- Short-lived process tail accounting, particularly A's 3.263-core cgroup residual. The cgroup total is retained; invented process attribution is not substituted.
- I/O and GC/JIT contributions during historical stalls. No allocation, GC stack, disk latency or filesystem trace was collected here.

## Not done, noted

Fix ideas supplied by the brief—compiler-cap activation, driver/seat admission changes, GC/PGO/reuse changes, background-container shares/nice, and Testcontainers tmpfs/cpuset placement—remain separate operator/Plan decisions; no fix was designed or implemented in this investigation.

--- next stage ---
next: decide
handoff: Compiler escape reproduced at 8.868 cores under six-node grants; live server2 capacity is 10, shared driver budget 4x6, and durable history remains binding-unknown. Decide how to qualify the existing opt-in cap and admission options; host-container attribution and a 7-versus-5 comparison remain unmeasured.
artifact: docs/investigations/2026-11-15-card-1906-server2-load.md
