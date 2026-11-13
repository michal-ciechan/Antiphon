# 2026-11-13 Antiphon desktop CPU attribution: server vs runner, and where (CARD-1886..1890)

Investigate task 1540fb0b. Desktop only. Only passive reads were used while live qualification run 97150a0b was in flight: no profiler, no attach, no dump, no ETW. The running build was `ab7af12c` (server and runner). Code citations are to `origin/master` `b57583ce`.

## Answer

The **Antiphon server uses about 16 times the session runner's CPU**: 0.92-0.95 cores against 0.05-0.06 cores. The largest Antiphon-driven consumer is **Postgres**, which the server drives. Postgres reached 1.3-3.4 cores in `docker stats`, and over the measured window it averaged 0.73 cores of statement execution time, 55% of that JIT compilation. The host itself was saturated at 91-99% of 8 cores (42% privileged time, 5.7 GB available, 42 GB commit). Most of that was the qualification run's compilers and test hosts.

## Measurements

### Per-process CPU, 6 min `Get-Process` deltas (13:44-13:50Z, 37 samples) and 60 s `Get-Counter` (13:51Z, 6 samples)

| Role | Cores (Get-Counter) | Cores (Get-Process) | Share of 8 |
|---|---:|---:|---:|
| csc (qualification and delegate builds, short-lived) | 1.64 | 1.34 | 17-21% |
| vmmem (WSL/Docker VM: Postgres plus test PG) | 1.32 | not readable | 17% |
| **Antiphon.Server** (PID 105444, started 05:54Z) | **0.95** | **0.92** | 12% |
| Antiphon.Tests (live qualification host) | 0.86 | 0.39 | 5-11% |
| MsMpEng (Defender) | 0.40 | not readable | 5% |
| claude CLIs (13 processes) | 0.23 | 0.20 | 3% |
| dcpctrl (AppHost proxy, owns 17202/17203) | 0.15 | 0.15 | 2% |
| WmiPrvSE | 0.14 | - | 2% |
| com.docker.backend | 0.14 | 0.16 | 2% |
| **Antiphon.SessionRunner** (PID 101512) | **0.05** | **0.06** (p50 0.05, p95 0.11 per 10 s) | 0.7% |
| pty hosts (12) | 0.01 | 0.02 | <0.3% |
| node/vite | 0.03 | 0.03 | 0.4% |

`Processor(_Total)` averaged 99.3% over the counter minute (91.5% in a first, discarded pass). Of that, 42% was privileged time and 1.8% interrupt time.

`docker stats` at about 13:50Z gave `antiphon-postgres` 133%, 344% and 309% (1 core = 100%). The test PG container from the qualification run (`affectionate_cartwright`, image `antiphon-test-pg:…`) read 88-96%, and Caddy 4-7%.

The runner's own `/host-stats` (13:39:54Z) recorded host CPU of 99.4% (30 min average 86.7%). It attributed only 0.55 cores in total to the processes it lists: runner 0.17 at that instant, sessions and pty hosts the rest.

### Live sessions

The runner `/sessions` listed 18 entries: 12 Running, 6 Exited. That gives about 0.004-0.005 runner cores per live session. At 0.95 cores the server costs about 0.08 cores per live session. Most of that sits in Postgres-backed per-session loops (below).

### Postgres (pg_stat_statements; reset 2026-11-11 17:45:28Z, read 13:52Z; about 44.1 h)

- Total execution time was 116,320 s, an average of 0.73 cores, across 1,430 statements and 36.2M calls.
- JIT time across all statements was **63,764 s (55%)**.
- A 120 s delta between 13:54:11 and 13:58:13Z recorded 201.9 s of execution, **1.68 core-equivalents**.

| Statement (source) | 44 h exec | 120 s delta |
|---|---|---|
| Runtime-action claim (`SessionRuntimeActionCoordinator.cs:165-182`) | 59,942 s; 170,407 calls; JIT 60,238 s; 3,257,390 JIT functions | 66.7 s; 374 calls; 178 ms/call |
| `-- session-state.seed` (`SessionStateLoader.cs:84`) | 3,845 s; 1,254 ms mean | 29.1 s; 29 calls; 1,003 ms |
| `-- session-state.identity` prelude (`TranscriptIngestPrelude.cs:22-43`) | 1,321 s | 15.2 s; 71 calls; 215 ms |
| `-- session-state.identity` UUID probe | 5,394 s; 1,068,019 calls; 642,995,481 rows (602/call) | 14.4 s; 2,168 calls |
| Full-transcript read (`Sequence, Text, Timestamp, Kind … ORDER BY Sequence`) | 8,636 s; 88,720 calls; 1,503 rows/call | 10.2 s; 166 calls |
| `-- session-state.pins` (`SessionStateLoader.cs:103`) | 4,119 s; 1,561 ms mean; JIT 2,723 s | 8.7 s; 4 calls; 2,174 ms |

**Mechanism (confirmed): the claim query.**

1. `SessionRuntimeActions` had 3 live rows (75 dead, autovacuumed 2,396 times).
2. One `EXPLAIN (ANALYZE, BUFFERS, TIMING OFF)` for session b2f1a869 (11,348 `SessionQueuedMessages` rows) touched 14 shared buffers and took `Execution Time: 995.074 ms`, with `JIT: Functions: 41, Inlining true, Optimization true`.
3. The nested `LateConfirmedChannelMessageIds.Contains` subplans (CARD-1434/1826) are `never executed` at run time. They still push the plan estimate to 6,732,196, above `jit_above_cost=100000` and `jit_inline_above_cost` / `jit_optimize_above_cost=500000` (`jit=on`).
4. So nearly all of the claim's execution time is JIT compilation, repeated about 1-3 times a second.

### Server log (`antiphon-20261113_001.log`, 07:06:30-13:44:30Z)

- 244,266 lines, or 36,800 lines per hour (about 190 MB per day): 192,061 INF, 10,599 WRN, 150 ERR.
- `SessionRuntimeActionCoordinator` wrote 72,134 lines (30%).
- Per-request INF lines (`EndpointMiddleware`, `OkObjectResult`, `CurrentUserMiddleware`) wrote 109,155 lines (45%).
- `GET /api/agent-tasks/{id}` received about 39,000 requests, 38-40 per minute at 13:35-13:44Z. Task 49ae044d was polled 5,616 times.
- One phase-8 action (878325e0, session 51018b06) retried every 30 s for more than 1.6 h at `attempts 31`.
- `LIVE_SLOW_CALL phase=db-query` appeared 4,243 times (p50 1,257 ms, p95 2,252 ms, max 5,627 ms, 5,946 s total), with no statement identity.
- The server log has **no** thread-pool starvation warnings.
- The runner Serilog (`%TEMP%\antiphon-logs\session-runner-20261113.log`) has five Hangfire heartbeat-starvation warnings: 00:37:55 (1.39 s), 05:25:09 (10.73 s), 05:26:37 (2.36 s), 12:54:27 (1.82 s) and 12:56:24 (3.10 s). They coincide with host saturation, not with runner CPU, since the runner itself ran at p95 0.11 cores.

### Host-load history (CARD-1514)

- The server returned `GET /api/hosts/desktop/load/history?resolution=hour` with `items: []` and `gaps: ["binding-unknown"]`. `GET /api/hosts/desktop/load` returned `current: null`, `coverage 0/720` and `sample_stale`.
- The runner spool `%LOCALAPPDATA%\Antiphon\host-load` holds 31 samples: boot 6c20b195 at 12:47:45-12:48:25Z (sequences 3-11), then boot 56b91791 from 13:41:44Z. The cursor ledger lists dozens of single-sequence gaps per boot, meaning missed 5 s ticks.
- **The 12:48-13:27Z hole cannot be reconstructed from stored data.** A 24 h per-process picture is not available from Antiphon telemetry. This is CARD-1889.

## Ranked findings → cards

1. **CARD-1886** (High/Soon): Postgres JIT on the runtime-action claim and other OLTP statements. 55% of DB time, about 0.4-0.56 core.
2. **CARD-1887** (Normal/Soon): session-state seed/pins take 1.0-2.2 s per call (OR-correlated `AgentTasks` EXISTS). About 0.25 core-equivalent.
3. **CARD-1888** (Normal/Soon): transcript ingest identity re-probe (602 stored rows per call) and whole-session transcript reads. About 0.25 core-equivalent.
4. **CARD-1890** (Normal/Normal): server log volume, 36.8k lines/h, 75% per-request and per-action INF lines. The CPU share needs the trace below.
5. **CARD-1889** (Normal/Soon, measurement gap): no desktop durable load history; the server, Postgres and builds are not in the host-stats process list; `LIVE_SLOW_CALL` carries no query tag.

Not filed, outside Antiphon code: Defender 0.40 cores, and the qualification compilers and test PG at about 3.5 cores, which is expected for the run.

## Sampling trace to take after run 97150a0b ends

Installed now (`dotnet tool list -g`): dotnet-trace 10.0.745401, dotnet-counters 9.0.661903, dotnet-stack, dotnet-dump and dotnet-gcdump. Nothing needs installing.

```powershell
$server = (Get-Process Antiphon.Server).Id
$runner = (Get-NetTCPConnection -LocalPort 17204 -State Listen).OwningProcess
$ts = Get-Date -Format yyyyMMdd-HHmm
# Before: snapshot pg_stat_statements (do not reset); repeat after; diff by queryid.
docker exec antiphon-postgres psql -U antiphon -d antiphon -At -F '|' -c "SELECT queryid, calls, total_exec_time, jit_functions FROM pg_stat_statements" > "pgss-before-$ts.txt"
dotnet-trace collect -p $server --profile dotnet-sampled-thread-time --duration 00:05:00 -o "server-$ts.nettrace"
dotnet-trace collect -p $runner --profile dotnet-sampled-thread-time --duration 00:05:00 -o "runner-$ts.nettrace"
dotnet-counters collect -p $server --counters System.Runtime,Microsoft.AspNetCore.Hosting,Microsoft-AspNetCore-Server-Kestrel,Npgsql --refresh-interval 5 --duration 00:05:00 --format csv -o "server-counters-$ts.csv"
dotnet-counters collect -p $runner --counters System.Runtime --refresh-interval 5 --duration 00:05:00 --format csv -o "runner-counters-$ts.csv"
docker exec antiphon-postgres psql -U antiphon -d antiphon -At -F '|' -c "SELECT queryid, calls, total_exec_time, jit_functions FROM pg_stat_statements" > "pgss-after-$ts.txt"
dotnet-trace convert "server-$ts.nettrace" --format speedscope
```

- Run the two traces one after the other, not together.
- If dotnet-trace 10 rejects the profile name, `--profile cpu-sampling` is the older equivalent.
- Read the top inclusive frames by namespace: `SessionRuntimeActionCoordinator`, `SessionStateLoader`, `Serilog`, Npgsql and `HostStatsPollService`.

## Remaining uncertainties

- Server-internal CPU (0.95 cores) is not yet split by method. Only Postgres time is statement-attributed. That split needs the trace above.
- The claim's 352 ms 44 h mean, against 178 ms in the window, may include periods with `channel` rows. JIT dominance holds in both.
- `Get-Process` and the counters miss processes that started and exited between samples (csc and test hosts are undercounted). vmmem and MsMpEng CPU were readable only through `Get-Counter`.

Not done, noted: the fix ideas are on the cards (JIT off or a split claim, an indexed pin rewrite, a high-water-mark probe, Debug-level request logging, a host-load binding).
