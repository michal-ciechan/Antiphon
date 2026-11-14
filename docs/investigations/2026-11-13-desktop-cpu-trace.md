# 2026-11-13 desktop CPU trace: Antiphon server vs session runner, and where (task b754552c)

Investigate task b754552c, desktop, after qualification run 97150a0b ended. This is an active follow-up to the passive attribution in `2026-11-13-card-1886-antiphon-cpu-attribution.md`. Only sampling diagnostics were used: `dotnet-counters`, and `dotnet-trace` with the `dotnet-sampled-thread-time` profile. There was no gc-verbose, heap dump or createdump. Processes traced: Antiphon.Server PID 105444 (`C:\src\Antiphon\server\bin\Debug\net9.0\`, started 05:54:33Z, reached through dcpctrl 17202 to :53676) and Antiphon.SessionRunner PID 101512 (17204, started 05:56:58Z). Code citations are to `origin/master` `f22a2c14`. Raw data is in `C:\Antiphon\traces\2026-11-13\` (not committed; its README lists sha256 hashes and commands).

## Answer

- **The server uses about 12-16 times the runner's CPU**: 0.99-1.27 cores against 0.07-0.10. Lifetime average is 66,918 CPU-s over 18 h, about 1.04 cores. This holds with the qualification run finished, so the qualification run is not what drives the server's load.
- **Where the server's ~1 core goes**:
  1. About **0.52 cores (45%)** goes to one pure-CPU loop, `TaskReportTurnSelector.PhantomTransparentFloor` → `NewestRealBetween` → housekeeping string checks. Filed as **CARD-1910**.
  2. About **0.45 cores** goes to the finalizer thread running `DynamicResolver+DestroyScout.Finalize`. This is GC pressure: 226 MB/s allocated and a gen2 collection every 6.5 s. Filed as **CARD-1911**.
  3. Roughly **0.2 cores** goes to EF Core / Npgsql materialisation of 4.6 MB/s read from Postgres. This is already covered by CARD-1887/1888.
- **Logging costs 0.005 cores.** That CPU number was appended to CARD-1890.
- The **runner** is at 0.07-0.10 cores, spread thinly over Kestrel, JSON and `HostLoadSpool`. No runner card is warranted.
- **Postgres** over the 38.7 min window: 0.73 core-equivalents of statement time, 68% of it JIT. The runtime-action claim alone took 1,082 s, 100% JIT, which confirms CARD-1886's claim unchanged.

## Measurements

### Baseline (23:50:55Z, 62.9 s Get-Process deltas, 12 x 5 s Get-Counter)

| Process | Cores |
|---|---:|
| Antiphon.Server 105444 | 0.99 |
| com.docker.backend | 0.14 |
| dcpctrl (17202 proxy) | 0.09 |
| Antiphon.SessionRunner 101512 | 0.07 |
| claude CLIs (largest three) | 0.06 / 0.04 / 0.01 |

- `Processor(_Total)` averaged 69.0% of 8 cores, with 37.0% privileged time. 9.7 GB was available.
- Get-Process cannot read vmmem or MsMpEng. The earlier report measured them with Get-Counter at about 1.3 cores and 0.4 cores. They are **external**: WSL/Docker VM and Defender, not Antiphon code.
- `docker stats` (once): antiphon-postgres 10.15% (live run gone, so far below the 133-344% seen during the run), caddy 4.2%, windmill worker 3.3%, redpanda 0.4%.

### CPU during each collection (Get-Process, 10 s samples)

| Window | Server mean / p50 / max | Runner mean / p50 / max |
|---|---|---|
| server trace 23:57:34Z+5 min | 1.151 / 1.139 / 2.074 | 0.104 / 0.094 / 0.212 |
| runner counters (no server tool attached) | 1.273 / 1.396 / 2.243 | 0.074 / 0.080 / 0.178 |
| runner trace 00:18:19Z+5 min | 0.901 / 0.774 / 1.627 | 0.081 / 0.087 / 0.175 |

**Tracing overhead.** The traced process's CPU is within its own untraced swing: the server ran at 0.90-1.27 cores with no tool attached, and the runner went from 0.074 untraced to 0.081 traced. So sampled-thread-time overhead is below what these measurements can resolve, at under about 0.1 core. The sampler run during the server counters failed on an argument-binding error, so the counters window itself has no per-process overhead sample. The counters' own `cpu.time` gives 1.01 cores for that window.

### Server counters (5 min, 23:52-23:57Z)

- CPU: user 268.5 s and system 33.7 s, so 1.01 cores.
- Allocation: **66.7 GB / 300 s = 226 MB/s**.
- GC: gen0 363, gen1 104, **gen2 46** collections. Last gen2 heap 434 MB, LOH 142 MB, gen2 fragmentation 149 MB. Pause time 5.0 s (1.7%).
- Thread pool: 11,464 work items per 5 s, queue length 0, 31 lock contentions per 5 s. JIT: 2,144 methods.
- Npgsql: **1.38 GB read** (4.6 MB/s) and 84 MB written. About 8.8 + 1.2 pooled connections in use, prepared ratio 0.
- First-chance exceptions, about 12.7/s in all (per 5 min): RepositoryNotFoundException 904, FileNotFoundException 730 paired with Win32Exception 730, OperationCanceled 687, IOException 204, ServiceUnavailable 180, ProviderDialogInputRefused 168, TaskCanceled 139, Conflict 55. Their CPU cost does not stand out in the trace: LibGit2Sharp and process-start frames total under 0.02 cores, so no card was filed. The throw sites are unidentified.
- Kestrel: active requests are essentially 0. HTTP serving is not a CPU factor.

### Runner counters (5 min)

- CPU: user 14.4 s and system 6.8 s, so 0.07 cores.
- Allocation: 1.0 MB/s. GC: gen2 1, pause 0.1 s.
- Gen2 heap 676 MB plus LOH 567 MB, with 246 MB LOH fragmentation. That is memory, not CPU, and is not carded here.
- 5.75 Kestrel connections.

### Server trace aggregation (`docs/investigations/2026-11-13-desktop-cpu-trace-aggregate.py`)

**Method.**
- Every sample in the speedscope export ends in a pseudo-frame: `UNMANAGED_CODE_TIME` 9,118 s, `CPU_TIME` 0.1 s. The script therefore classifies by the last managed frame.
- Wait or poll frames are counted as idle: `LowLevelLifoSemaphore.WaitForSignal`, `WaitHandle.Wait*`, `Monitor.Wait`, `SemaphoreSlim.Wait`, `IOCompletionPoller.Poll`, Confluent `Consume(int32)`, `GC.RunFinalizers`, `ReadFile` and similar.
- Everything else is a CPU candidate. Over 42 threads that came to 441 s = **1.47 cores**, an upper bound against the measured 1.15, because it includes native blocking inside non-wait frames. Rank by it; do not sum it to the measured CPU.

| Owner (innermost match) | CPU-candidate s / 300 s | cores |
|---|---:|---:|
| `TaskReportTurnSelector.SelectAsync → PhantomTransparentFloor → IsTransparentSiblingPhantom → NewestRealBetween` (all of it) | 155.8 | **0.519** |
| of which `NewestRealBetween` exclusive | 79.5 | 0.265 |
| of which `IsHousekeepingRow → TranscriptPromptSpan.IsHousekeepingPrompt → TranscriptKinds.IsLocalCommandRecord` | 71.0 | 0.237 |
| of which `Buffer._Memmove` under `IsLocalCommandRecord` | 65.0 | 0.217 |
| Finalizer thread: `DynamicResolver+DestroyScout.Finalize` | 106.4 | **0.355** |
| Finalizer thread: `GC.ReRegisterForFinalize` | 29.8 | 0.099 |
| EF Core bucket (materialisation, `ClrPropertyGetter`, reader dispose) | 190.9 | 0.636 (includes socket-completion wait inside Npgsql reads; upper bound) |
| JSON (System.Text.Json) | 15.4 | 0.051 |
| Npgsql | 12.0 | 0.040 |
| Kestrel/HTTP | 9.3 | 0.031 |
| DynamicMethod creation / reflection | 6.6 | 0.022 |
| **Serilog / M.E.Logging** | **1.4** | **0.005** |
| Kafka (Confluent) | 0.3 | 0.001 |
| Hangfire | 0.1 | 0.000 |
| SignalR | 0 | 0 |

**Antiphon types below the top two** (innermost Antiphon frame), each at most 0.033 cores:
- `AppDbContext` 0.033
- `AgentSessionRuntime` 0.008
- `InProcessGitStatus` / `WorktreeHealthService` / `GitStatusConfigAgreement` / `InProcessGitReads` about 0.008 each
- `PhoneHomeRunnerDirectory` 0.006
- `SessionRuntimeActionCoordinator` 0.002
- `HostStatsPollService` 0.001

Tailers, Hangfire jobs, SignalR fan-out, the session-state loaders and logging are **not** server-CPU owners. Their cost is in Postgres (CARD-1886/1887/1888), not in the server process.

**Thread-time topN.** `dotnet-trace report topN` with thread-time inclusive of waits:
- Top 4 exclusive (waits): `LowLevelLifoSemaphore.WaitForSignal` 29.4%, `WaitHandle.WaitOneNoCheck` 26.2%, `Monitor.Wait` 9.9%, Confluent `Consume` 6.6%.
- Top real frames: `DestroyScout.Finalize` 1.17%, `TaskReportTurnSelector.NewestRealBetween` 0.87%, `Buffer._Memmove` 0.84%.
- Full lists: `server-topN-exclusive.txt` and `server-topN-inclusive.txt` in the trace folder.

**Tracked DynamicMethod creators.** These total only 10.9 s:
- `PhoneHomeRunnerDirectory.GetBindingAsync`
- `SessionReconciliationService.ReconcileSessionsAsync`
- `AgentTaskLandMonitorService.SweepAsync`
- `AgentSessionRuntime.RecordActivityAsync`
- `HostLoadIngestService.IngestAsync`

The cost is in finalizing the methods, not in creating them.

### Runner trace aggregation

- 25 threads. CPU-candidate total 28.0 s = 0.093 cores.
- By bucket: other 0.036, Kestrel/HTTP 0.031, JSON 0.024, reflection 0.003.
- By Antiphon type: `HostLoadSpool` 0.005, `TranscriptTailer` 0.001, everything else below that.
- No actionable runner hotspot.

### Postgres (pg_stat_statements, read-only snapshots, no reset; 23:50:36Z → 00:29:21Z = 2,325 s)

- Statement execution: 1,699.3 s, **0.73 core-equivalents**. JIT: **1,152.9 s (68%)**. 455,050 calls.

| Statement | exec s | calls | JIT s |
|---|---:|---:|---:|
| Runtime-action claim `SELECT s."Id", s."AcceptedGeneration", …` (CARD-1886) | 1,082.2 | 5,069 | 1,087.7 |
| Transcript `Sequence, Text, Timestamp, Kind … Kind IN (…) ORDER BY` | 80.0 | 1,566 | 0 |
| `-- session-state.identity` UUID probe (CARD-1888) | 66.4 | 14,872 | 0 |
| `-- session-state.identity` prelude | 49.7 | 476 | 1.7 |
| `-- session-state.pins` (CARD-1887) | 48.7 | 39 | 34.9 |
| `SessionQueuedMessages` EXISTS | 38.0 | 1,412 | 24.3 |
| `-- session-state.seed` (CARD-1887) | 31.3 | 48 | 0 |
| `Timestamp, CreatedAt` whole-session transcript | 31.1 | 136 | 0.3 |

The claim's mean execution time was 213 ms, and JIT time exceeded execution time for that statement. This confirms CARD-1886 as written: JIT, not the plan, dominates. No change to CARD-1886/1887/1888 text was needed.

## Cards

| Card | Status | Finding | Expected saving |
|---|---|---|---|
| **CARD-1910** (new, High/Soon) | Backlog | `TaskReportTurnSelector.cs:527-570` `PhantomTransparentFloor`/`IsTransparentSiblingPhantom` call `NewestRealBetween` (`:401-421`) once per newer boundary. That is a full rescan of `raw` with housekeeping string classification per row (`:427-433`, `TranscriptPromptSpan.cs:151`, `SessionRunnerContracts.cs:640`). It is O(boundaries x prompts) and costs 0.52 cores. Sibling of Done CARD-1760 (`LastBefore`). | 0.45-0.5 cores |
| **CARD-1911** (new, Normal/Soon) | Backlog | 226 MB/s allocation, gen2 every 6.5 s, finalizer thread 0.45 cores in DestroyScout; 4.6 MB/s Npgsql reads. The DynamicMethod creator is unidentified (needs a 60 s allocation-tick trace). | 0.35-0.45 cores + GC |
| CARD-1890 (appended, text kept) | Backlog | Logging CPU share measured at 0.005 cores: a volume and retention problem, not a CPU lever. | - |
| CARD-1886/1887/1888/1889 | unchanged | Numbers above are consistent with their claims. | - |

Duplicate search before filing: `card.ps1 search` for 'TaskReportTurnSelector' (hits CARD-1760/1821/1473, all Done and covering different paths), 'PhantomTransparentFloor', 'DestroyScout', 'RepositoryNotFoundException' and 'allocation rate' (no hits).

## Not Antiphon (external)

- vmmem (WSL/Docker VM) at about 1.3 cores during the run. With the run ended, Postgres is at 0.1 cores in `docker stats`.
- MsMpEng (Defender) at about 0.4 cores.
- com.docker.backend 0.14 cores.

## Remaining uncertainties

- The CPU-candidate classification is an upper bound: 1.47 cores against 1.15 measured. Native blocking inside non-wait frames (Npgsql socket completion under EF) inflates the EF bucket. The TaskReportTurnSelector and DestroyScout numbers are synchronous CPU frames and are reliable.
- The server is a **Debug** build (`bin\Debug\net9.0`), so JIT optimisation is off for Antiphon assemblies. Hot Antiphon loops such as CARD-1910 would cost less in Release. That is a deployment choice, so it was noted, not carded.
- The source of the DynamicMethods behind the finalizer churn is unproven (see CARD-1911).
- The throw sites of the 12.7 first-chance exceptions per second are unidentified. Their CPU is small in this trace.
- Runner `/sessions` count at trace time was not recorded, so cost per session is not computed.

## Repeat

```powershell
$server = (Get-Process Antiphon.Server).Id
$runner = (Get-NetTCPConnection -LocalPort 17204 -State Listen).OwningProcess | Select -First 1
$d = 'C:\Antiphon\traces\<date>'; New-Item -ItemType Directory -Force $d | Out-Null; Set-Location $d
$q = "COPY (SELECT queryid, calls, total_exec_time, rows, jit_functions, jit_generation_time, jit_inlining_time, jit_optimization_time, jit_emission_time, shared_blks_hit, shared_blks_read, left(regexp_replace(query,'\s+',' ','g'),300) AS q FROM pg_stat_statements) TO STDOUT WITH CSV HEADER"
docker exec antiphon-postgres psql -U antiphon -d antiphon -c $q > pgss-before.csv
dotnet-counters collect -p $server --counters "System.Runtime,Microsoft.AspNetCore.Hosting,Microsoft.AspNetCore.Server.Kestrel,Npgsql" --refresh-interval 5 --duration 00:05:00 --format csv -o server-counters.csv
dotnet-trace collect -p $server --profile dotnet-sampled-thread-time --duration 00:05:00 -o server.nettrace
dotnet-counters collect -p $runner --counters "System.Runtime,Microsoft.AspNetCore.Hosting,Microsoft.AspNetCore.Server.Kestrel" --refresh-interval 5 --duration 00:05:00 --format csv -o runner-counters.csv
dotnet-trace collect -p $runner --profile dotnet-sampled-thread-time --duration 00:05:00 -o runner.nettrace
docker exec antiphon-postgres psql -U antiphon -d antiphon -c $q > pgss-after.csv
dotnet-trace convert server.nettrace --format speedscope -o server.speedscope.json
python -I <repo>\docs\investigations\2026-11-13-desktop-cpu-trace-aggregate.py server.speedscope.speedscope.json 300 40 > server-cpu-agg.md
```

The server trace was 139 MB of nettrace and 233 MB of speedscope JSON. Conversion and aggregation took about 1 min each.

Not done, noted: the fix ideas are on CARD-1910 (classify each row once, then binary-search a prefix index; make `IsLocalCommandRecord` allocation-free) and CARD-1911 (find and cache the DynamicMethod creator; cut whole-transcript reads).
