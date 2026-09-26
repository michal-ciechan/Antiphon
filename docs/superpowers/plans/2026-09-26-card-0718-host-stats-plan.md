# CARD-0718 — Host stats: 5 s sampling, in-memory rollups, Hosts page and API

- Card: CARD-0718 (`18a35dd7`), Backlog, Normal/Soon.
- Plan task: `18fd01a3` (Plan, Frontier, worktree `feat/card-task-18fd01a3`), written on the
  server2 Linux runner. Ground truth read at `bafc3366`.
- TestDesign: task `609f77b4` (Opus, server2), refined `## Verification design` in place from
  `fa86dc64`; counts re-read at `bafc3366` (origin/master).
- Next stage: Code Round 1 (S1–S4). The `### Checkpoints` table is the closed list it runs;
  `### Positive controls` is the post-land Mutation list.
- Platform: every checkpoint row runs on either lane. The Windows probe test executes only on
  Windows and is reported as not run with the reason on Linux; the Linux probe test does the
  reverse. No row pins a runner host.

## Summary

Antiphon gets a per-host health stream, sampled about every 5 s **on each session runner**
(the desktop's local runner and server2 over phone-home), kept **in the runner's memory** as a
30-minute ring of raw samples with 1/5/15/30-minute average and max computed from the ring,
**pulled** by the server every 5 s into a small cache, and shown on a new **Hosts** page with a
card per host and inline SVG line graphs, live over SignalR. Nothing is written to the database
per sample; the server's only per-tick database read is one no-tracking query over open tasks
(about twenty rows) that yields tasks in flight by stage, agent kind and host, queued and held
tasks, and pending lands. A missing runner is `offline`, a silent one `stale`, an old runner
`unsupported`; none of them is ever a zero.

The operator's acceptance is carried by four measurable things: both hosts appear with values
that move every 5 s (CP-9 against the live host); rollups are correct against synthetic samples
(V-1); the 30-minute graph renders from a 360-point series (V-7); sampling cost is measured, not
asserted (CP-4: runner process CPU with the sampler on versus off, under 1 %; measured input
today: 0.14 ms per host sample and 1.2 ms per full `/proc` process sweep on server2).

Two rounds. Round 1 (S1–S4) is the whole card's acceptance: probes, store, sampler, runner
routes and phone-home operation, server poll service, cache, API, SignalR push, Hosts page,
docs. Round 2 (S5–S6, a second Code dispatch) adds the cheap-if-cheap extras the card lists
"where cheap": named-process totals (`vmmem`, `Antiphon.Server`, `dotnet` test hosts), all-command
EF rate, repository-lease waits, and the Windows processor queue if a PDH read stays under
budget.

## Ground truth

Read at `bafc3366` on 2026-09-26 from the server2 runner container (24 cores; the host's load
average was 28.7 while this was written).

| Card / brief assumption | What the code and the host show | Consequence |
|---|---|---|
| Brief: "the session runner already has a host memory probe for CARD-0589's build slots, which is worth reusing" | `IHostMemoryProbe` / `SystemHostMemoryProbe` (`src/Antiphon.SessionRunner/HostMemoryProbe.cs`): Windows `GlobalMemoryStatusEx` (the struct already carries `TotalPhys`, `AvailPhys`, `TotalPageFile`, `AvailPageFile`; only `AvailPhys` is returned), Linux `MemAvailable` from `/proc/meminfo`. Registered by `BuildSlotRoutes.AddBuildSlotBroker` as `TryAddSingleton`; the broker reads it at grant time. | S1 widens the probe into `IHostStatsProbe` returning a full `HostSample`; `IHostMemoryProbe` stays as is (the broker keeps its one number) and is implemented by the same class so the two never disagree (D-1). |
| Brief: "how the server gets the data from remote runners over phone-home" | The runner publishes session events through `SessionRunnerRuntime`'s event hub (`Publish<T>(eventName, payload)`, `SessionRunnerRuntime.cs:3549`); the phone-home `EventLoopAsync` forwards them as `Event` frames on a bounded subscription whose overflow **closes the connection** (`PhoneHomeConnectionService.cs:120-190`). On the server, both consumers (`PhoneHomeRecoveryPump.PumpEventsAsync` `:355`, `PhoneHomeRunnerClient.StreamEventsAsync` `:320`) and the local SSE reader (`SessionRunnerHttpClient.StreamEventsAsync` `:635`) hand every event to `RunnerContractMapper.ParseEvent`, which returns null for an unknown name and requires a session id (`SessionRunnerEvent(eventName, SessionId, ...)`). Requests are the other direction: `PhoneHomeLiveConnection.RequestAsync(operation, payload, ct)` (`:299`, default 60 s timeout, `RequestTimeoutFor`), answered by `PhoneHomeCommandDispatcher.DispatchCoreAsync` (`:151-270`; an unknown operation answers `phone_home_unsupported_operation` 400). `PhoneHomeRunnerDirectory.RequestProviderAuthAsync` (`:101`) is the precedent for a typed per-runner read routed through `Resolve(runnerId)` with `ISessionRunnerClient`'s default-implemented members (`GetProviderAuthAsync`, `GetHealthAsync` return null on fakes). | D-3: pull, not push. A new default-implemented `ISessionRunnerClient.GetHostStatsAsync` (null = cannot say), implemented by `SessionRunnerHttpClient` (HTTP) and `PhoneHomeRunnerClient` (operation 29), and a server `HostStatsPollService` that ticks every 5 s. No change to `ParseEvent`, the event hub or the overflow rules. |
| Brief: "the desktop server host, plus every session runner" | `GET /api/session-runners` live: `desktop` (windows, capacity 7 `delegatedTasks`, occupied 2) and `server2` (linux, capacity 10 `sessions`, occupied 5). `GET /api/runner-defaults`: `globalRunnerId` null. The local runner (`SessionRunner:BaseUrl` `http://localhost:17204`) runs **on** the desktop host; `PhoneHomeRunnerDirectory.Local` is that client, `KnownRunnerIds` lists the remote ids, `RunnerPlatformWire.DesktopId = "desktop"`, `SessionRunnerCatalogue.ListAsync` (`server/Application/Services/SessionRunnerCatalogue.cs`) already builds one row per host with two DB counts per remote runner. | The desktop host is sampled by the local runner (same machine); the server adds its own process figures to that host (D-1, D-6). Host ids are the catalogue's runner ids. |
| Card: "a ring buffer of the raw 5 s samples for the last 30 minutes ... running tally per host and metric" | No ring, no rollup, no host metric exists anywhere. The only periodic host reads are the build-slot memory floor (at grant time) and `SessionCpuWatchdogService` (5 s `PeriodicTimer`, per-session `IProcessCpuProbe.TryGetTotalCpuTime` with a 2-minute PID-reuse tolerance, `SessionRunnerSettings.CpuWatchdogIntervalMs`). | S1 `HostStatsStore` (ring of 360, rollups computed from the ring on read, D-2); the sampler reuses `SystemProcessCpuProbe`'s PID guard for per-process figures (D-6). |
| Card: "Nothing is written to the database per sample" | The server restarts on every land (`restart-apphost.ps1`); the runner does not. `SessionStateCommandMetrics` (`server/Infrastructure/Data/`) is a `DbCommandInterceptor` counting tagged EF reads into a `Meter`; `GET /api/diagnostics/session-state` already exposes process CPU/working set/GC for the server. | D-4: the ring lives on the runner, the server holds only the latest snapshot per host. The server's own process figures are read the way the diagnostics route already does. |
| Card: "CPU % (total and per core count); load average on Linux, or processor queue length on Windows; memory used, available and total; swap; disk free on the worktree and state volumes; process count" | Measured here: `nproc` 24; `/proc/stat` first line has the ten jiffy columns; `/proc/loadavg` `28.71 29.75 27.49 13/3789 8436` (the running/total pair is the **host's** process count; `ls /proc` inside the container sees 62 pids, the container's own namespace); `/proc/meminfo` MemTotal 132,014,068 kB, MemAvailable 97,687,336 kB, SwapTotal 542,716 kB, SwapFree 286,400 kB (host figures: no cgroup memory cap, `privileged: true`, `docker-compose.server2-runner.yml`); `/work` and `/state` are the same LV (738 GB, 27 % used), read with `statfs`. 200 iterations of reading `/proc/stat` + `/proc/meminfo` + `/proc/loadavg` + two `statfs` cost **0.143 ms per iteration**; reading every `/proc/<pid>/stat` (62 pids) **1.17 ms**. The runner has no `System.Management` reference (the server does, for `WindowsZombieProcessCensus`'s WMI `Win32_Process` query); `PerformanceCounter` is referenced nowhere. | Linux probe reads exactly those files (D-1). Windows probe uses `GetSystemTimes` + the existing `GlobalMemoryStatusEx` + `DriveInfo`; the processor queue length needs PDH and is Round 2 (D-12). Process count on Linux is the loadavg total. |
| Card: "the Antiphon processes' own CPU and memory (server, runner, PtyHost, test hosts)" | The runner knows its own pid, each live session's `Pid` and `HostPid` (`RunnerSessionDto`, `SessionRunnerContracts.cs:200-209`; `HostPid` is the detached pty-host). It does not know the server's pid or any build/test host pid; finding those needs a process enumeration (`Process.GetProcesses()` on Windows is one `NtQuerySystemInformation` call; on Linux the 1.2 ms `/proc` sweep). | Round 1 reports own process + known session pids + pty hosts; the server contributes its own process; Round 2 adds enumeration-based named totals under a measured budget (D-6). |
| Card: "tasks running in parallel, by stage and agent kind; live sessions and occupied seats against the declared capacity; queued and held tasks and lands; builds in flight; repository-lease wait time; EF queries per second" | Stage = `AgentTaskRole` (Plan/Code/Review/.../Investigate 14/TestDesign 15/Mutation 16), status `AgentTaskStatus` (Queued/Dispatched/Working/Blocked/...), `CapacityWaitRetained` marks a held task, `RunnerId` null/empty = desktop; pending land = `LandRequestedAt != null` (`AgentTaskLandService.SweepAsync` `:723`); the dispatcher's active count is one indexed `CountAsync` per tick (`AgentTaskDispatcher.cs:400-407`); `AgentTaskPipelineStatusService.GetAsync` is the heavy projection (several queries, card joins). Seats: `directory.DeclaredCapacity(id)` and `LiveRemoteSessionIds()` are in memory; `Delegation:MaxConcurrentTasks` is the desktop cap. Build slots: `BuildSlotBroker.List()` is in-process on the runner. Repository lease waits: `RepositoryLeaseWaiters.Snapshot(commonDirectory)` is in memory, keyed by repo. EF rate: only tagged session-state reads are counted today. | D-5: one grouped no-tracking query over open tasks per tick; seats from the directory; build slots come free inside the runner's sample; lease waits and all-command EF rate are Round 2 (S5). Postgres CPU is out (D-12). |
| Card: "`GET /api/hosts` returns the list ... `GET /api/hosts/{id}/series`" | CARD-0654 (Review) plans `GET /api/hosts` → `HostBudgetDto[]` and `PUT /api/hosts/{hostId}/budget`; CARD-0589's plan defers `buildSlots` into that `hosts[]`. Nothing under `/api/hosts` exists at `bafc3366`. CARD-0654 also reserved `PhoneHomeOperation.SetCapacity = 26`, which CARD-0710 has since consumed as `LaunchPlatformConstrained = 26`; CARD-0589 Round 2 reserves 27/28. | D-7: this card owns `GET /api/hosts/stats` and `GET /api/hosts/{hostId}/stats/series`, composable with CARD-0654 in either landing order; operations 29/30 (D-11). |
| Card: "A SignalR push lets the page update live" | One hub (`AntiphonHub`, `/hubs/antiphon`) with `JoinGroup(name)`; services publish through `IEventBus.PublishToGroupAsync` / `PublishToAllAsync` (`server/Infrastructure/Realtime/EventBus.cs`). App-wide events feed `useSignalRInvalidation`'s map; group-scoped pages (`SessionTranscriptPanel`, `SessionTerminal`) open their own `HubConnection` and `invoke('JoinGroup', ...)`. | D-7: `HostStatsUpdated` to group `hosts`; the page joins the group on its own connection and patches the query cache rather than refetching. |
| Card: "graphs ... using the client's existing chart approach" | There is none: no chart library in `client/package.json` (Mantine core/hooks/notifications, react-query, zustand, xterm, tiptap, mermaid, monaco), no `<svg>`/`<polyline>` in `client/src` outside icons. Pages are lazy routes in `App.tsx` under `Layout`'s `NAV_ITEMS`; mobile uses `useMediaQuery('(max-width: 48em)')` and `visibleFrom`/`hiddenFrom="sm"`. Tests: vitest + msw (`client/src/test/mocks/handlers.ts` already stubs `/api/session-runners`), `renderWithProviders` / `renderHookWithProviders` (`client/src/test/utils.ts`), run with `pwsh -File scripts/test-client.ps1 <filter>`; lint is `npm run lint` (`eslint . --max-warnings 0`). | D-8: a 60-line inline SVG `Sparkline` component, no dependency. |
| Runner tests and hosts | `tests/Antiphon.SessionRunner.Tests`: `BuildSlotTestHost` (loopback Kestrel on a random port carrying only `AddBuildSlotBroker` + `MapBuildSlotRoutes`, refusing 17204/8080), `FakeTimeProvider` in use, `PhoneHomeCommandDispatcherTests` (34) construct the dispatcher with a fake `IPhoneHomeRuntimeSurface` and optional ctor seams (`authProbe`). Server: `PhoneHomeTestHost` (`tests/Antiphon.Tests/TestHelpers/`) with `PhoneHomeScriptedPeer.Reply` (script a per-operation reply), `RecordingLocalClient` (fake local `ISessionRunnerClient`), `RunnerCatalogueTests` (4, Integration), `PhoneHomeDirectoryTests` (7, Unit), `PhoneHomeConnectionTests` (23), `PhoneHomeEventPumpTests` (8), `SessionRunnerEventPumpTests` (2), `RunnerSlotEndpointTests` (8). | The V rows reuse these harnesses; no new fixture kind. |
| Resilience (CARD-0717) | Local-runner reads go through `SendReadAsync(path, ResilienceOperations.X, ct)` on the admitted read client with a 120 s total budget and up to 7 attempts; an unknown operation "fails closed to one attempt". | D-3: the 5 s poll is a deliberate single attempt with a 3 s cap on the typed client, not an admitted read; it is listed under "What stays a single attempt" in `docs/resilience.md`. |
| Existing periodic services on the runner | `SessionCpuWatchdogService` (`PeriodicTimer`, `SweepOnceAsync` internal seam for tests), `BuildSlotSweepService` (`Task.Delay(interval, time, ct)` with the injected `TimeProvider`), `HerdrStatusPushService`. `TimeProvider.System` is registered in both processes. | The sampler follows the watchdog's shape: `PeriodicTimer` plus an internal `SampleOnceAsync` the tests drive without the timer. |

## Decisions

Stated defaults; none was asked. Each names what it rejects.

### D-1. Sampling runs in the session runner, one probe seam with a Linux and a Windows arm

`IHostStatsProbe.Read() -> HostSample?` in `src/Antiphon.SessionRunner/HostStatsProbe.cs`, with
`SystemHostStatsProbe` choosing `LinuxHostStatsProbe` or `WindowsHostStatsProbe` at construction
(`OperatingSystem.IsLinux()` / `IsWindows()`), and `null` when the platform is neither or a read
faults. The same class keeps implementing `IHostMemoryProbe.AvailableBytes` for the build-slot
broker, so the memory floor and the Hosts page can never show two different numbers.

- Linux: `/proc/stat` line 1 (user, nice, system, idle, iowait, irq, softirq, steal: total CPU %
  is `1 - Δidle/Δtotal` between two reads; the first tick after start has no delta and reports no
  CPU), `/proc/meminfo` (`MemTotal`, `MemAvailable`, `SwapTotal`, `SwapFree`), `/proc/loadavg`
  (three averages, the running/total pair = host process count), `DriveInfo` for the configured
  volumes (`AvailableFreeSpace`, `TotalSize`), `Environment.ProcessorCount`. Every read is a
  static `Parse*` over lines so the tests feed strings. Inside the server2 container these are
  the host's figures (no cgroup caps, `privileged: true`), which is what the operator wants.
- Windows: `GetSystemTimes` (kernel32; idle/kernel/user 100 ns totals; CPU % from the delta the
  same way, kernel includes idle), `GlobalMemoryStatusEx` (already P/Invoked; now returning the
  whole struct: `TotalPhys`, `AvailPhys`, page file as swap), `DriveInfo`, process count from
  `Process.GetProcesses().Length` **only if** CP-4 measures it under 1 ms, else omitted until
  Round 2. No WMI: `System.Management` is not referenced by the runner and one `Win32_Process`
  query is tens of milliseconds.
- Per-process (D-6): own process, each live session's `Pid` and `HostPid` from
  `SessionRunnerRuntime.List()`, via `Process.GetProcessById` guarded like
  `SystemProcessCpuProbe` (start-time tolerance so a recycled pid is not charged).

Rejected: sampling in the server for the desktop (two OS-reading implementations, and the server
process restarts on every land so the ring would die hourly); WMI (cost, package); a sidecar
script (`Get-Counter`/`top` spawn per tick is the opposite of negligible).

### D-2. The runner owns the ring and the rollups; rollups are computed from the ring on read

`HostStatsStore` (`src/Antiphon.SessionRunner/HostStatsStore.cs`): a fixed ring of
`RetentionMinutes × 60 / IntervalSeconds` slots (360 at the defaults) of `HostSample`, a lock
around writes and snapshot reads, a `TimeProvider` for timestamps. `Latest()` returns the
newest sample; `Rollups(now)` returns, for each metric, `avg` and `max` over the samples whose
timestamp is within 1, 5, 15 and 30 minutes of `now` (a window with no samples is null, never
zero); `Series(metric, window, now)` returns the `(t, v)` points inside the window in time
order. Three hundred and sixty structs are a few tens of kilobytes; a full read is microseconds.

Rejected: incremental running tallies per window (four windows × N metrics of state to keep
consistent under eviction; nothing to gain at n = 360; and "max over the last 5 minutes" cannot
be maintained incrementally without the ring anyway). Rejected: a per-metric ring (one struct per
sample keeps a sample atomic across metrics, which the graph and the table both assume).

### D-3. The server pulls; the runner never pushes host stats

`HostStatsPollService` (`server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs`,
`BackgroundService`, `HostStats:PollIntervalMs` 5000): each tick, for the desktop and every
`directory.KnownRunnerIds` entry, `await client.GetHostStatsAsync(ct)` with a 3 s
`CancelAfter`, in parallel per host, and store the answer or its failure in `HostStatsCache`.

- `ISessionRunnerClient.GetHostStatsAsync(CancellationToken) => Task.FromResult<RunnerHostStatsDto?>(null)`
  default-implemented like `GetProviderAuthAsync`, so no fake changes.
- `SessionRunnerHttpClient`: `GET host-stats` on the typed client, one attempt, 3 s. Not an
  admitted resilience read: a 5 s poll must never sit in a 120 s retry budget, and a missed tick
  is simply the next tick. `docs/resilience.md` lists it under single attempts (S4).
- `PhoneHomeRunnerClient`: `RequestAsync(PhoneHomeOperation.HostStats, null, ct)`; an
  `phone_home_unsupported_operation` error answers `Unsupported`, which the cache records.
- The runner side is `PhoneHomeCommandDispatcher` case `HostStats => Result(request, hostStats.Snapshot())`
  through a new optional ctor seam `IHostStatsSource? hostStats = null` (the pattern of
  `authProbe`); when the seam is null the switch's default `UnsupportedOperation` answer applies.

Rejected: push over the session event hub. `SessionRunnerEvent` requires a session id, the
three server consumers and the SSE parser would all need a non-session branch, the bounded
subscription's overflow closes the connection, and the local SSE path would gain a 5 s event
the transcript pumps have to skip. Rejected: piggybacking the 15 s `Heartbeat` frame (lease
semantics; wrong cadence). Rejected: fanning out from `GET /api/hosts/stats` on request (a page
open would double the traffic and a slow runner would stall the page).

### D-4. The server keeps only the latest snapshot per host; series are read from the runner

`HostStatsCache` (`server/Application/Services/HostStatsCache.cs`, singleton, in memory): per
host id, the last `RunnerHostStatsDto` and `observedAt`, plus the last failure class. Its
projection is the API list (D-7) with `state`:

| state | when |
|---|---|
| `live` | a sample answered within `HostStats:StaleAfterMs` (15000 = 3 ticks) |
| `stale` | a sample exists but is older than that (the runner is up but quiet, or the poll is failing) |
| `offline` | no live connection for a remote id, or the local runner refused the connection |
| `unsupported` | the runner answered unsupported-operation / 404 (an old binary); its `features` lack `hostStatsV1` |

A stale or offline host keeps its last values, marked; it never shows zeros.

`GET /api/hosts/{hostId}/stats/series` asks the runner (`GET host-stats/series?metric=&window=`
/ `PhoneHomeOperation.HostStatsSeries` with `{ metric, window }`) at request time, one attempt,
5 s. The page asks once per metric on mount and after a reconnect; between those it appends the
point carried by each `HostStatsUpdated` push.

Rejected: a server-side ring (dies on every `restart-apphost`, exactly when the operator is
looking; the runner restarts only on deploy, and a runner restart losing 30 minutes is accepted
and documented). Rejected: persisting a 1-minute rollup (the card marks it optional-later; it is
the only thing here that would write to Postgres and CARD-0698 is why we do not).

### D-5. Antiphon's own numbers come from one grouped query and the in-memory directory

On the same tick the poll service runs one no-tracking query:
`AgentTasks.Where(status in Queued/Dispatched/Working/Blocked).Select(t => new { t.Role, t.AgentKind, t.RunnerId, t.Status, t.CapacityWaitRetained, HasLand = t.LandRequestedAt != null })`
(about twenty rows today), and groups in memory into, per host: in flight by `AgentTaskRole`,
in flight by `AgentKind`, queued, held (`CapacityWaitRetained`), pending lands. Seats:
`directory.DeclaredCapacity(id)` and `LiveRemoteSessionIds()` for remote hosts,
`Delegation:MaxConcurrentTasks` and the desktop in-flight count for the desktop (the catalogue's
own predicate, `RunnerId` null or empty). Build slots arrive inside the runner's sample
(`BuildSlotBroker.List()` is in-process there: occupied, budget, waiters, `availableMb`).

This is the plan's only per-tick database read: 0.2 queries/s, indexed on status, materialised
before grouping, no card join. It is visible in `GET /api/diagnostics/session-state`'s
`efReadAttempts` once tagged, so a regression is measurable.

Rejected: calling `AgentTaskPipelineStatusService.GetAsync` per tick (several queries with card
joins, built for a page not a sampler). Rejected: counting inside the dispatcher tick (couples an
observability feature to dispatch; the dispatcher does not run when nothing is queued).

### D-6. Per-process breakdown: known pids in Round 1, named totals in Round 2

Round 1 reports what needs no enumeration: the runner process (CPU %, working set), each live
session's child and pty host by session id, and on the desktop host the server's own process
(read the way `/api/diagnostics/session-state` reads it, merged by the poll service into the
desktop entry as `antiphon.server`). Round 2 (S5) adds one enumeration per tick to sum named
processes: `vmmem`/`vmmemWSL` (Docker/WSL VM), `Antiphon.Server`, `Antiphon.SessionRunner`,
`Antiphon.PtyHost`, `dotnet` test hosts (`testhost`, TUnit hosts by command line only where the
OS gives it for free). S5 keeps the enumeration only if CP-11 measures it under 2 ms per tick;
otherwise the totals are sampled every fourth tick and marked so.

Rejected: a `Process.GetProcesses()` sweep in Round 1 (unmeasured on the desktop; the one place
a 300-process Windows host could make "negligible" untrue). Rejected: Docker stats for the
Postgres container (needs the Docker socket the runner deliberately does not mount).

### D-7. API and SignalR surface

| Route | Answer |
|---|---|
| `GET /api/hosts/stats` | `HostStatsDto[]`: `hostId`, `displayName`, `platform`, `state`, `observedAt`, `intervalSeconds`, `cores`, `current` (`cpuPercent`, `load1/5/15` or null, `memoryUsedBytes`, `memoryAvailableBytes`, `memoryTotalBytes`, `swapUsedBytes`, `swapTotalBytes`, `disks[] { path, freeBytes, totalBytes }`, `processCount`, `processes[] { name, sessionId?, cpuPercent, workingSetBytes }`), `rollups` (`{ "1m": {avg,max}, "5m": ..., "15m": ..., "30m": ... }` for `cpuPercent`, `load1`, `memoryUsedBytes`, `tasksInFlight`), `antiphon` (`tasksInFlight`, `byStage`, `byKind`, `queued`, `held`, `landsPending`, `sessionsLive`, `seatsDeclared`, `buildSlots { occupied, budget, waiters }`) |
| `GET /api/hosts/{hostId}/stats/series?metric=cpu&window=30m` | `{ hostId, metric, window, intervalSeconds, points: [{ t, v }] }`; `metric` ∈ `cpu`, `load`, `memory`, `tasks`; `window` ∈ `1m`, `5m`, `15m`, `30m`; 400 otherwise; 404 unknown host; 409 `phone_home_unavailable` when the runner cannot be asked (the page then keeps what it has) |
| SignalR `HostStatsUpdated` to group `hosts` | the same `HostStatsDto[]` after every tick; published only when the cache changed |

Not `GET /api/hosts`: CARD-0654 (Review) owns that collection for budgets, and this card's
routes nest under it so both can land in either order; when both are live the budget row can
carry a `stats` link later. Rejected: `PublishToAllAsync` every 5 s (every open tab pays for a
page nobody has open; a group with no members costs a dictionary lookup).

### D-8. Hosts page: a card per host, inline SVG sparklines, no chart dependency

`client/src/features/hosts/`: `HostsPage.tsx` (route `/hosts`, nav item **Hosts** after
**Agents**), `HostCard.tsx` (name, platform, state badge, current CPU / memory / load / tasks
and seats, a 1/5/15/30 table of avg and max, four `Sparkline`s), `Sparkline.tsx` (inline
`<svg viewBox="0 0 W H" preserveAspectRatio="none">` with one `<path>` from the points, a
`<title>` for accessibility, the newest point at the right; width 100 %, so it is the mobile
layout too), `hosts.ts` API hooks under `client/src/api/` (`useHostStats`,
`useHostSeries(hostId, metric, window)`, `hostKeys`), and `useHostStatsLive(connection)` which
joins group `hosts` on its own `HubConnection` (the `SessionTranscriptPanel` pattern) and on
each `HostStatsUpdated` writes the list into `['hosts','stats']` and appends `current` to every
mounted `['hosts', id, 'series', metric, window]`. Below 48em the grid is one column and the
rollup table collapses to 1m/30m.

Rejected: `@mantine/charts` (recharts, ~0.5 MB for four line charts; a new dependency for a page
that is deliberately small). Rejected: `mermaid` (already present, not for time series).

### D-9. Settings and rollback

| Section | Keys | Default |
|---|---|---|
| runner `SessionRunner:HostStats` | `Enabled`, `IntervalMs`, `RetentionMinutes`, `ProcessSampling`, `Volumes` (paths; default `[cwd, SessionLogPath]`; server2 compose sets `/work,/state`) | true, 5000, 30, true |
| server `HostStats` | `Enabled`, `PollIntervalMs`, `StaleAfterMs`, `RequestTimeoutMs`, `SeriesTimeoutMs` | true, 5000, 15000, 3000, 5000 |

`Enabled=false` on the runner answers `/host-stats` 404 and the operation unsupported, which
the server shows as `unsupported`; `Enabled=false` on the server stops the poll and the page
shows every host `offline` with the reason `disabled`. Neither needs a migration or a restart
order. Validation follows `BuildSlotSettings.Validate` (positive intervals, retention ≥ 1).

### D-10. The overhead is measured, not asserted

Inputs measured on server2 today: 0.143 ms per host sample, 1.17 ms per `/proc` process sweep.
Expected Round 1 runner work per tick: one host sample plus `Process.GetProcessById` for at most
`Capacity × 2 + 1` pids, under 2 ms, i.e. under 0.05 % of one core at 5 s. CP-4 proves it on the
lane that runs Code: the runner's own `cpuPercent` (its own process, from the same sample) is
averaged over 60 s with the sampler on and with `SessionRunner:HostStats:Enabled=false`
(measured from `GET /host-stats` of a second runner instance whose sampler is on), and the
difference must be under 1 %. The number goes into the docs section with the host and date.

### D-11. Identifiers reserved by this plan

`PhoneHomeOperation.HostStats = 29`, `PhoneHomeOperation.HostStatsSeries = 30` (26 was consumed
by CARD-0710; 27/28 are CARD-0589 Round 2's; CARD-0654's plan must renumber its `SetCapacity`).
Capability feature token `RunnerCapabilityFeatures.HostStatsV1 = "hostStatsV1"`. SignalR event
`HostStatsUpdated`, group `hosts`. Query keys `['hosts','stats']`, `['hosts', id, 'series', metric, window]`.
No `AgentIncidentKind`, no migration, no `ResilienceOperations` entry.

### D-12. Out of scope, named

Placement and backpressure on load (CARD-0674 owns load-aware admission; it will read
`HostStatsCache`), an Attention item for sustained overload (a follow-up card once thresholds
have a week of data), 1-minute rollup persistence, Postgres CPU from `pg_stat_statements`
(CARD-0705, in progress), Windows processor queue length (PDH; Round 2 only if one counter read
is under 1 ms), Docker/WSL and Postgres container CPU beyond the `vmmem` process total (Round 2).

## Slices

Round 1 is one Code dispatch (S1–S4, in order; each slice is committed with its red tests
first). Round 2 (S5–S6) is a second Code dispatch after Round 1 has landed and CP-9 has been
observed live.

### S1 — runner: probes, store, sampler, routes, phone-home operation, capability

Files (all `src/Antiphon.SessionRunner/` unless noted):

- `HostStatsProbe.cs` (new): `IHostStatsProbe`, `HostSample` (readonly record struct:
  `At`, `CpuPercent?`, `Cores`, `Load1/5/15?`, `MemoryTotalBytes`, `MemoryAvailableBytes`,
  `SwapTotalBytes`, `SwapFreeBytes`, `Disks` (path/free/total), `ProcessCount?`, `Processes`
  (name, sessionId?, cpuPercent?, workingSetBytes)), `SystemHostStatsProbe` (platform switch,
  also implements `IHostMemoryProbe`), `LinuxHostStatsProbe` with static `ParseProcStat`,
  `ParseMemInfo`, `ParseLoadAvg`, `WindowsHostStatsProbe` with `GetSystemTimes` P/Invoke.
  `HostMemoryProbe.cs` keeps `IHostMemoryProbe`; `SystemHostMemoryProbe` becomes a thin
  forwarder to keep `BuildSlotRoutes` registration unchanged.
- `HostStatsStore.cs` (new): ring, `Add`, `Latest`, `Rollups(now)`, `Series(metric, window, now)`,
  `Snapshot(now)` (latest + rollups + `buildSlots` from `BuildSlotBroker.List()` when present).
- `HostStatsSettings.cs` (new): `SessionRunner:HostStats` (D-9) with `Validate()`.
- `HostStatsSamplerService.cs` (new): `BackgroundService`, `PeriodicTimer(IntervalMs)`,
  internal `SampleOnceAsync()` that reads the probe, adds process figures for
  `runtime.List()`'s live pids through `IProcessCpuProbe` and `Process.GetProcessById`
  (working set), and `store.Add`; a faulting probe logs once per minute and skips the tick.
- `HostStatsRoutes.cs` (new, the `BuildSlotRoutes` shape): `AddHostStats(configuration)`
  (options, `TryAddSingleton<IHostStatsProbe>`, store, sampler) and `MapHostStatsRoutes()`:
  `GET /host-stats` → `RunnerHostStatsDto`, `GET /host-stats/series?metric=&window=` →
  `RunnerHostSeriesDto`, 400 on a bad metric/window, 404 when disabled.
- `PhoneHomeCommandDispatcher.cs`: ctor seam `IHostStatsSource? hostStats = null`; cases
  `HostStats` and `HostStatsSeries` (D-3); `Program.cs:47` passes the store.
- `Program.cs`: `AddHostStats`, `MapHostStatsRoutes`, `RunnerCapabilityFeatures.HostStatsV1`
  appended to `features` at `:213-216` when enabled.
- `src/Antiphon.SessionRunner.Contracts/`: `PhoneHomeContracts.cs` operations 29/30 and
  `PhoneHomeHostSeriesRequest(string Metric, string Window)`; `SessionRunnerContracts.cs`
  `RunnerHostStatsDto`, `RunnerHostSeriesDto`, `RunnerHostSeriesPoint(DateTimeOffset T, double V)`,
  `RunnerCapabilityFeatures.HostStatsV1`.
- `docker-compose.server2-runner.yml`: `SessionRunner__HostStats__Volumes: "/work,/state"`.

Tests (`tests/Antiphon.SessionRunner.Tests/`): `HostStatsStoreTests`, `HostStatsProbeParseTests`,
`HostStatsSamplerTests`, `HostStatsEndpointTests` (+ `HostStatsTestHost` beside
`BuildSlotTestHost`), `PhoneHomeCommandDispatcherTests` (+3), `RunnerCapabilitiesTests` (+1).

### S2 — server: client members, poll service, cache, API, SignalR

- `server/Application/Interfaces/ISessionRunnerClient.cs`: `GetHostStatsAsync`,
  `GetHostSeriesAsync(metric, window, ct)` default null.
- `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`: both, one attempt,
  `HostStats:RequestTimeoutMs` / `SeriesTimeoutMs` via `CancelAfter` on the typed client.
- `PhoneHomeRunnerClient.cs`: both through `RequestAsync` (29/30); unsupported → a typed
  `HostStatsUnsupportedException` the cache maps to `unsupported`.
- `server/Application/Settings/HostStatsSettings.cs` (+ validator) — D-9.
- `server/Application/Services/HostStatsCache.cs`: `Record(hostId, dto)`, `RecordFailure(hostId, kind)`,
  `Project(now, catalogueRows)` → `IReadOnlyList<HostStatsDto>`, `Changed` flag per tick.
- `server/Application/Services/HostStatsAntiphonCounters.cs`: the one grouped query (D-5)
  plus directory seats; pure grouping in a static `Group(rows)` for the unit test.
- `server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs`: the tick (D-3),
  `TickOnceAsync` internal seam, publishes `HostStatsUpdated` to group `hosts` through
  `IEventBus` when `Changed`.
- `server/Application/Dtos/HostStatsDtos.cs`: `HostStatsDto`, `HostStatsRollupDto`,
  `HostStatsAntiphonDto`, `HostSeriesDto` (D-7).
- `server/Api/Endpoints/HostStatsEndpoints.cs`: `MapHostStatsEndpoints` on
  `app.MapGroup("/api/hosts").WithTags("Hosts")` with `/stats` and `/{hostId}/stats/series`;
  `Program.cs` registration next to `MapSessionRunnerEndpoints` (`:991`).
- `server/appsettings.json`: `HostStats` block.

Tests (`tests/Antiphon.Tests/Application/`): `HostStatsCacheTests` (Unit),
`HostStatsAntiphonCountersTests` (Unit), `HostStatsPollServiceTests` (Integration,
`PhoneHomeTestHost` + `PhoneHomeScriptedPeer.Reply` for 29/30, `RecordingLocalClient` extended
with a scripted host-stats answer, a recording `IEventBus`), `HostStatsEndpointTests`
(Integration, same host, `Http.GetAsync`).

### S3 — client: Hosts page

- `client/src/api/hosts.ts` (+ `hosts.test.tsx`), `client/src/features/hosts/HostsPage.tsx`,
  `HostCard.tsx`, `Sparkline.tsx`, `RollupTable.tsx`, `useHostStatsLive.ts`, `HostsPage.stories.tsx`
  (two hosts, one stale, one offline), `App.tsx` lazy route `hosts`, `Layout.tsx` nav item,
  `client/src/test/mocks/handlers.ts` stubs for `/api/hosts/stats` and the series route.

Tests: `hosts.test.tsx`, `HostsPage.test.tsx`, `Sparkline.test.tsx`, `useHostStatsLive.test.ts`.

### S4 — docs

`docs/ops-http.md` (two rows in the routes table: Hosts list, Hosts series; states and
"never zero"), `docs/antiphon-api.md` (the two routes under the runner block at `:479-483`),
`docs/resilience.md` ("What stays a single attempt": the host-stats poll and series),
`docs/testing-and-build.md` (a short "Host stats (CARD-0718)" subsection with the measured
overhead and the `HostStats` settings table), `docs/session-runtime-invariants.md` only if
TestDesign finds an invariant worth pinning (none expected: the sampler decides nothing).

### S5 — Round 2: named-process totals, EF command rate, lease waits

- Runner: `HostStatsProcessCensus` (one `Process.GetProcesses()` / `/proc` sweep per tick,
  summing `vmmem*`, `Antiphon.*`, `dotnet`/`testhost` by name), gated by `ProcessSampling`
  and by CP-11's measurement (D-6).
- Server: `DbCommandRateMetrics` interceptor (all commands, a counter the poll service
  differences into `efCommandsPerSecond`), `RepositoryLeaseWaiters.SnapshotAll()` → oldest
  wait seconds and waiter count into `antiphon.leaseWaiters` / `leaseWaitSeconds`.
- Windows processor queue length via one PDH counter (`\System\Processor Queue Length`) if a
  read is under 1 ms (measured in CP-11's Windows arm), else dropped with the reason in docs.

### S6 — Round 2: page additions

Process breakdown table per host, EF rate and lease wait tiles; no new routes.

### Migration and rollback

No migration. Rollback is `HostStats:Enabled=false` on the server (page shows `offline`
`disabled`) or `SessionRunner:HostStats:Enabled=false` on a runner (that host shows
`unsupported`); an old runner binary against a new server is `unsupported`, a new runner
against an old server is unpolled and costs nothing but its own sampler. The phone-home
protocol version does not change: an unknown operation was already a typed refusal.

## Verification design

Coverage classes: V = new tests committed red at `bafc3366` (the failing assertion quoted in the
commit message) before the slice's production change; R = existing classes that must stay
green because a slice touches their path. `[Category("Unit")]` / `[Category("Integration")]`
per `TestLaneCategoryGuardTests`; nothing here spawns a process, so no
`ParallelLimiter<ProcessSpawnLimit>` is added.

### Inspection

Bodies read by TestDesign at `bafc3366`, and what each changes in the roster:

- `tests/Antiphon.SessionRunner.Tests/BuildSlotTestHost.cs` (whole) | the shape `HostStatsTestHost`
  copies: `WebApplication.CreateBuilder`, `UseUrls("http://127.0.0.1:0")`, injected
  `IHostMemoryProbe`/`TimeProvider`, `Add*`/`Map*` pair, refuses ports 17204/8080 -> V-4.
- `tests/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs` (head, ctor use) |
  `[Category("Unit")]`, 34 `[Test]`, `new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings{...})`
  plus optional seams; an unknown operation `(PhoneHomeOperation)999` answers `Error` -> V-5 reuses
  `RecordingRuntime` and passes the new `hostStats:` seam by name.
- `tests/Antiphon.SessionRunner.Tests/RunnerCapabilitiesTests.cs` (4 `[Test]`) and
  `src/Antiphon.SessionRunner/Program.cs:205-219` | the `features` list is built **inline in the
  `/capabilities` lambda**, unreachable from a test -> missing setup MS-3 below; the capability
  guard is tested through the extracted helper, not through `Program`.
- `tests/Antiphon.SessionRunner.Tests/SessionCpuWatchdogTests.cs` (head, 3 `[Test]`) | it starts a
  real pty-host session with `Path.Combine(Environment.SystemDirectory, "cmd.exe")` under
  `ParallelLimiter<ProcessSpawnLimit>`; the plan changes neither `IProcessCpuProbe` nor the
  watchdog -> **removed from R-1** (see Out of scope).
- `tests/Antiphon.SessionRunner.Tests/LocalHttpRunner.cs` (ctor) | the isolated-runner argument
  set (`--urls http://127.0.0.1:0`, `SessionLogPath`, Herdr off, watchdog off, `Serilog:LogPath`)
  that CP-4's measurement script copies; it launches `Antiphon.SessionRunner.exe`, so CP-4 launches
  the `.dll` through `dotnet` instead to run on either OS.
- `src/Antiphon.SessionRunner/HostMemoryProbe.cs` (whole) | `ParseMemAvailable` is internal static
  over lines, `GlobalMemoryStatusEx` struct already carries `TotalPhys`/`TotalPageFile` -> V-2's
  memory-floor consistency method.
- `src/Antiphon.SessionRunner/SessionCpuWatchdogService.cs:23-80` | ctor takes the concrete
  `SessionRunnerRuntime`; `SweepOnceAsync` is the internal seam; `PeriodicTimer` without a
  `TimeProvider` -> missing setup MS-2 (the sampler must not take the concrete runtime).
- `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs` (whole: `StartAsync`, `RecordingLocalClient`,
  `PhoneHomeScriptedPeer`) | `StartAsync(clock, connectionString, limits, configureDbContext,
  shutdownTimeout, configured)` registers **no `IEventBus`** and maps only session-runner, operator
  and version endpoints; the peer's `DefaultReply` answers any unlisted operation with `{ ok = true }`,
  which would deserialize into an all-default `RunnerHostStatsDto` of zeros; `SilentFor`,
  `Speak`, `RequestCount`, `WaitForAsync` exist -> missing setup MS-5, MS-6; every V-8/V-9 method
  scripts `Reply` for 29/30 explicitly.
- `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs:55-80`,
  `PhoneHomeRunnerDirectory.cs:100-118` | the precedent for an unsupported answer: an `Error` frame with
  `ErrorCode == PhoneHomeProblemTypes.UnsupportedOperation` becomes a typed exception / 409
  -> V-8 method 3 drives the real client with that frame.
- `server/Infrastructure/Realtime/AntiphonHub.cs:26-36`, `EventBus.cs` | `JoinGroup(string)` accepts
  any group name and `PublishToGroupAsync` is `Clients.Group(group).SendAsync` -> no hub change;
  `Antiphon.Tests` does not reference `Microsoft.AspNetCore.SignalR.Client` (only `Antiphon.E2E`
  does), so the hub hop is a declared substitute (Delivery inventory).
- `tests/Antiphon.Tests/Application/RunnerCatalogueTests.cs:68-110` | `TestDbFixture.CreateIsolatedSchemaAsync()`
  plus `PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString)` and `db.AgentTasks.Add(TaskRow(...))`
  is the seeded-task idiom V-9 method 1 uses.
- `tests/Antiphon.Tests/Application/SessionRunnerEventPumpTests.cs` | 2 `[Test]`, one with four
  `[Arguments]`: **5 executions**, not 2 (R-2's Min).
- `client/src/test/mocks/handlers.ts:36`, `client/src/features/board/SessionTerminal.test.tsx:116`
  (`vi.mock('@microsoft/signalr', ...)`), `scripts/test-client.ps1` header (filter args pass through)
  -> V-10's hook test mocks `HubConnectionBuilder` the SessionTerminal way.
- `docs/testing-and-build.md` Checkpoint manifest, Combined class filters, Mutation PC execution.

Boundaries and where each lands:

| Boundary | Covered by | Or excluded because |
|---|---|---|
| Rollup window edge: exactly `now − 60 s` vs `now − 60.001 s` | V-1 m5 | |
| Window holding fewer samples than capacity; empty window | V-1 m4, m1 | |
| Ring at 359 / 360 / 361 samples | V-1 m2 | |
| Staleness at exactly `StaleAfterMs` vs +1 ms | V-6 m2 | |
| First probe read (no CPU delta) vs second | V-2 m7, CP-3 | |
| `/proc/stat` with 8 vs 10 columns; guest columns nonzero; iowait nonzero | V-2 m1, m2 | |
| Metric present on one OS only (`load` on Windows) | V-1 m6, CP-3 | |
| Host combinations: desktop live + remote live / silent / unsupported / disconnected / local fault | V-8 m1–m5 | |
| Poll disabled (`HostStats:Enabled=false`) vs runner disabled (`SessionRunner:HostStats:Enabled=false`) | V-6 m6, V-4 m4 | |
| `RunnerId` null vs empty vs named | V-7 m3 | |
| Old runner x new server (unsupported) | V-8 m3 (phone-home), V-12 m2 (HTTP 404) | |
| New runner x old server | | nothing polls it; its only cost is its own sampler (CP-4) |
| Windows live probe on the Linux lane and the reverse | CP-3 (one executes per lane) | the other OS cannot be exercised on this lane; reported not run with the reason |
| Two browser tabs in group `hosts` | | SignalR fan-out to a group is framework behaviour; one member proves the path |

Missing setup the Code stage adds (test-only or seam, named so no row is a stub):

- **MS-1** `LinuxHostStatsProbe` internal ctor seam `(Func<string, IReadOnlyList<string>> readLines,
  Func<string, (long Free, long Total)?> statVolume, IReadOnlyList<string> volumes)`; the public ctor
  passes `File.ReadAllLines` and `DriveInfo`. V-2 m7, m8 feed fixture strings through it.
- **MS-2** `HostStatsSamplerService` depends on `IHostStatsProbe`, `HostStatsStore`, `IProcessCpuProbe`,
  a `Func<IReadOnlyList<HostStatsProcessTarget>>` (session id, pid, host pid, started-at; the DI
  registration adapts `SessionRunnerRuntime.List()`), a `Func<int, long?>` working-set reader
  (default `Process.GetProcessById(pid).WorkingSet64`, null on `ArgumentException`), `TimeProvider`
  and options. `ExecuteAsync` takes **one sample immediately**, then `new PeriodicTimer(interval, time)`.
- **MS-3** `HostStatsRoutes.CapabilityFeatures(IReadOnlyList<string> features, HostStatsSettings s)`
  (static, appends `hostStatsV1` only when `s.Enabled`); `Program.cs`'s lambda calls it.
- **MS-4** `HostStatsTestHost` beside `BuildSlotTestHost`: `AddHostStats` + `MapHostStatsRoutes`,
  injected fake `IHostStatsProbe`, `FakeTimeProvider`, settings dictionary, exposes the registered
  `HostStatsSamplerService` so a test calls `SampleOnceAsync` twice; refuses 17204/8080 the same way.
- **MS-5** `PhoneHomeTestHost.StartAsync` gains `Action<IServiceCollection>? configureServices = null`
  and `Action<WebApplication>? mapEndpoints = null` (applied after the existing registrations and
  maps). Existing callers are unchanged; V-9 uses them to register `HostStatsCache`,
  `HostStatsAntiphonCounters`, a `RecordingEventBus` and `MapHostStatsEndpoints`.
- **MS-6** `RecordingLocalClient` gains `RunnerHostStatsDto? HostStats`, `Exception? HostStatsFault`,
  `RunnerHostSeriesDto? HostSeries` and overrides `GetHostStatsAsync` / `GetHostSeriesAsync`
  (throw the fault if set, else return the property); a `RecordingEventBus : IEventBus` test helper
  in `TestHelpers/` records `(group, eventName, payload)` and can be told to throw once.
- **MS-7** `HostStatsPollService` takes an `IHostStatsAntiphonCounters` (interface over the grouped
  query) so V-8 passes a fixed fake; V-9 uses the real one on the isolated schema. The per-host
  request timeout is `new CancellationTokenSource(RequestTimeout, time)` on the injected
  `TimeProvider`, so V-8 advances a `FakeTimeProvider` instead of sleeping.

### Delivery inventory

Host stats are a best-effort, last-value-wins telemetry stream: nothing is durable by design
(D-4), so "recovery" here means the next tick restores the value and the gap is shown as
`stale`/`offline`, never as zero. The durable identity joining every hop is
**`(hostId, sample.At)`** (runner id from the catalogue; `At` from the runner's `TimeProvider`).
No path carries session input, so no UserPrompt transcript applies.

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt |
|---|---|---|---|---|---|
| DP-1 sample | `HostStatsSamplerService.SampleOnceAsync` | `HostStatsStore` ring | none (runner memory; a runner restart empties the ring, accepted in D-4) | next tick; a faulting probe skips one tick (G-21) | `Latest().At` equals the fake clock (V-3 m1); `GET /host-stats` newest `At` (V-4 m1) |
| DP-2 local pull | runner `GET /host-stats` | server `HostStatsCache.Record("desktop", dto)` | none | next 5 s tick; a fault or 3 s timeout marks the host for `stale` (G-43, G-44) | projection `live` with the answered `At` (V-8 m1, m4) |
| DP-3 remote pull | runner dispatcher case 29 over the phone-home socket | `HostStatsCache.Record("server2", dto)` | none | silent peer: request cancelled at `RequestTimeoutMs`, host goes `stale`; disconnect: `offline`, then `live` on reconnect; old runner: `unsupported` | V-8 m2, m3, m5 against the real `PhoneHomeLiveConnection` |
| DP-4 push | `HostStatsPollService` after a tick with `Changed` | browser query cache `['hosts','stats']` and mounted series keys, via `IEventBus.PublishToGroupAsync("hosts", "HostStatsUpdated", list)` | none; the page's `GET /api/hosts/stats` on mount and on reconnect is the resync | publish fault: the tick survives and `Changed` stays set so the next tick republishes (G-47); client reconnect: rejoin + refetch (G-60) | server: `RecordingEventBus` holds one `("hosts","HostStatsUpdated", list)` whose entries carry the answered `At` (V-8 m1, m6); client: `queryClient.getQueryData(['hosts','stats'])` equals the pushed list (V-10 live m2); live: CP-9 hub receipt |
| DP-5 series read | runner `Series(...)` via `GET host-stats/series` / operation 30 | page series key | none | synchronous request; a failure is 409 and the page keeps its points | V-9 m2 (proxied points), V-4 m2, V-5 m2 |

Producer-to-recipient through the real queue: V-8 drives the real `PhoneHomeLiveConnection` (the
WebSocket frame queue, `RequestAsync`, the scripted peer) and the real `PhoneHomeRunnerClient` into
the real cache, with the two hosts in one tick covering **busy recipient** (peer `SilentFor(HostStats)`)
alongside **one already eligible** (desktop answering), and a failure at each handoff: probe fault
(V-3 m2, runner side), local HTTP fault (V-8 m4), remote silence (V-8 m2), remote disconnect and
recovery (V-8 m5), publish fault and republish (V-8 m6), client reconnect (V-10 live m4).

Declared substitutes and what each cannot prove:

- `RecordingEventBus` for `EventBus` + `AntiphonHub`: proves the group name, event name and payload
  the service hands over; cannot prove SignalR routes a group message to a connection that called
  `JoinGroup("hosts")`. Closed by CP-9's live hub receipt (a node `@microsoft/signalr` client joins
  `hosts` on the activated server and must receive two `HostStatsUpdated` lists with advancing
  `observedAt`) — this is the recipient evidence for DP-4.
- Mocked `HubConnectionBuilder` in vitest: proves the hook's `JoinGroup` call, `on('HostStatsUpdated')`
  handler and cache writes; cannot prove wire compatibility of the payload casing. Closed by CP-9
  (same payload read by the real client library) and V-9 m1 (the JSON the API serializes is the
  shape the page reads; both use the one `HostStatsDto`).
- `RecordingLocalClient` for `SessionRunnerHttpClient`: V-8 cannot prove the HTTP single-attempt and
  timeout; V-12 covers `SessionRunnerHttpClient` itself with a stub `HttpMessageHandler`.
- Fake `IHostStatsProbe`: cannot prove the OS reads; CP-3 runs the real probe on the lane's OS.

- **V-1 S1 `HostStatsStoreTests`** (Unit, `FakeTimeProvider`, 9 methods): an empty store
  answers null latest and null rollups (never zero); the 361st sample evicts the first and
  `Series(30m)` has exactly 360 points in time order; rollup `avg` and `max` over 1/5/15/30
  minutes match hand-computed values for a synthetic saw-tooth (`cpu = i % 20`, one sample per
  5 s, 30 minutes), including a window that holds fewer samples than its capacity (2 minutes of
  data: `5m` covers all 24 samples, `1m` the last 12; both asserted against the arithmetic); a sample older than the window is excluded at the boundary
  (`now - 60 s` is in `1m`, `now - 60.001 s` is not); a metric absent from a sample (`load` on
  Windows) yields null rollups, not zero; `Series` for an unknown metric throws
  `ArgumentException`; `Snapshot` carries `intervalSeconds` and `retentionMinutes`; the store is
  safe under a concurrent `Add` / `Rollups` loop (no exception, counts monotone). Red: the type
  does not exist.
- **V-2 S1 `HostStatsProbeParseTests`** (Unit, 6 methods): `ParseProcStat` yields idle/total
  jiffies from the measured line above and CPU % from two readings (busy delta over total delta,
  e.g. `(2000-1000)-(1500-800) / (2000-1000) = 30 %`); a shorter first line (8 columns) still
  parses; `ParseMemInfo` yields total/available/swap from the four lines; `ParseLoadAvg` yields
  three loads and the total process count 3789 from `13/3789`; a missing `MemAvailable` gives
  null available, not zero; `WindowsHostStatsProbe.CpuPercent(idle0, kernel0, user0, idle1, kernel1, user1)`
  (pure, the P/Invoke is behind it) yields `1 - Δidle / (Δkernel + Δuser)`. Red: the parsers do not
  exist.
- **V-3 S1 `HostStatsSamplerTests`** (Unit, 4 methods): `SampleOnceAsync` with a fake probe
  adds one sample with the fake clock's time; a probe that throws leaves the store unchanged and
  the sampler alive (a second call adds); process figures for a fake runtime's live session pid
  come from a fake `IProcessCpuProbe` (CPU % from two consecutive `TotalProcessorTime` deltas over
  the wall interval) and a null CPU sample yields a null percent, not zero; `Enabled=false`
  never calls the probe.
- **V-4 S1 `HostStatsEndpointTests`** (Integration, loopback `HostStatsTestHost`, 4 methods):
  `GET /host-stats` after two `SampleOnceAsync` answers the newest sample and non-null `1m`
  rollups; `GET /host-stats/series?metric=cpu&window=5m` answers the points; `metric=bogus` is
  400 with a problem body; `SessionRunner:HostStats:Enabled=false` answers 404 and the
  `/capabilities` features list lacks `hostStatsV1` (the capability check is a fifth assertion
  inside a `RunnerCapabilitiesTests` addition, R-1).
- **V-5 S1 `PhoneHomeCommandDispatcherTests` +3**: `HostStats` answers a `Result` frame whose
  payload round-trips to `RunnerHostStatsDto`; `HostStatsSeries` with `{ metric: "cpu", window: "1m" }`
  answers points and with `window: "2h"` answers a 400 error frame; a dispatcher constructed
  without the seam answers `phone_home_unsupported_operation` for 29. Red: the enum members do
  not exist (compile), then the switch has no case.
- **V-6 S2 `HostStatsCacheTests`** (Unit, `FakeTimeProvider`, 5 methods): a recorded sample
  projects `live`; advancing past `StaleAfterMs` projects `stale` with the same values (never
  zero); a host with no record and a directory saying not connected projects `offline` with
  `current` null; an `unsupported` failure projects `unsupported`; `Changed` is true after a new
  sample and false after a tick that recorded nothing new.
- **V-7 S2 `HostStatsAntiphonCountersTests`** (Unit, 3 methods): `Group(rows)` over a
  synthetic row set yields per-host `byStage` (Code 2, Review 1), `byKind`, `queued`, `held`
  (only `CapacityWaitRetained`), `landsPending` (only `HasLand`), and a null/empty `RunnerId` is
  the desktop; a Blocked task counts as open but not in flight.
- **V-8 S2 `HostStatsPollServiceTests`** (Integration, `PhoneHomeTestHost`, 4 methods): one
  `TickOnceAsync` with the local recording client answering a sample and the scripted peer
  replying to operation 29 fills both hosts `live` and publishes one `HostStatsUpdated` to group
  `hosts` with two entries; a peer `SilentFor(HostStats)` leaves that host on its last values and,
  after `StaleAfterMs` on the fake clock, `stale`, while the desktop stays `live`; a peer whose
  `Reply` answers `phone_home_unsupported_operation` projects `unsupported`; a tick whose local
  client throws `HttpRequestException` does not fault the service and marks the desktop `stale`
  on the next projection. The local answer is the `RecordingLocalClient`'s new
  `HostStats` property.
- **V-9 S2 `HostStatsEndpointTests`** (server, Integration, same host, 4 methods):
  `GET /api/hosts/stats` answers the cache's projection (state, current, rollups, antiphon
  counters from a seeded task row); `GET /api/hosts/server2/stats/series?metric=cpu&window=30m`
  proxies the scripted peer's operation 30 answer; unknown host is 404; `window=2h` is 400
  before any runner call (`RequestCount(HostStatsSeries)` stays 0).
- **V-10 S3 client** (vitest, `pwsh -File scripts/test-client.ps1 hosts Sparkline HostsPage`):
  `hosts.test.tsx` reads the list and the series hooks against msw; `HostsPage.test.tsx` renders
  one card per host, a `stale` badge with the old values still visible, an `offline` card with
  "no data" and no `0 %`, and the nav item; `Sparkline.test.tsx` builds a path with one `M` and
  359 `L` segments from 360 points and renders a placeholder for an empty series;
  `useHostStatsLive.test.ts` joins group `hosts`, writes a pushed list into the query cache and
  appends the pushed current point to a mounted series key.
- **V-11 S4 docs**: a non-TUnit grep row (CP-8) proving the four docs name the routes, the
  states and the settings.
- **R-1** runner classes adjacent to S1: `PhoneHomeCommandDispatcherTests` (34 → 37),
  `RunnerCapabilitiesTests` (4 → 5), `BuildSlotBrokerTests` (11) and `BuildSlotEndpointTests`
  (4) because `SystemHostMemoryProbe` changes shape, `SessionCpuWatchdogTests` (3) because the
  sampler shares `IProcessCpuProbe`, `PhoneHomeConnectionServiceTests` (10).
- **R-2** server classes adjacent to S2: `PhoneHomeDirectoryTests` (7), `RunnerCatalogueTests`
  (4), `RunnerSlotEndpointTests` (8), `PhoneHomeEventPumpTests` (8), `SessionRunnerEventPumpTests`
  (2), `SessionRunnerCapabilityGateTests` (2), `HttpResilienceRegistrationTests` (7, the typed
  client gained two non-admitted reads).
- **R-3** the whole `Antiphon.Tests` Unit lane.
- **R-4** client lint (`npm run lint`, zero warnings) and the whole vitest suite once (S3 adds a
  route and a nav item that `App.test.tsx` and `Layout` tests observe).

Red-first rule: V-1..V-9 are committed red before their production change; a test that passes
before the change is a stub (CARD-0585 rule 4). V-10's page tests are red because the route
and components do not exist; the msw handlers are added with the tests.

Platform notes: V-2's Linux parsers and Windows `CpuPercent` are pure and run on both lanes.
The live probes are exercised by CP-4 (a real `SampleOnceAsync` on the lane's OS asserting
non-null total memory, cores ≥ 1, and CPU % between 0 and 100 on the second sample) and the
other OS is reported as not run with the reason.

### Checkpoints

Isolated outputs `bin-c718r/` (`Antiphon.SessionRunner.Tests`) and `bin-c718a/`
(`Antiphon.Tests`), forward slash, one build per project per round; `Min` = `[Test]` methods in
the named classes at `bafc3366` plus the new methods, minus a 5 % floor allowance.
`Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` are never co-scheduled. Every row runs through
`scripts/run-checkpoint.ps1` (it takes the build slot itself); the two non-TUnit rows run under
`scripts/build-slot.ps1` where they build.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c718r/` | host-stats-runner | `/*/*/(HostStatsStoreTests*)\|(HostStatsProbeParseTests*)\|(HostStatsSamplerTests*)\|(HostStatsEndpointTests*)/*` | V-1, V-2, V-3, V-4 | all listed, 0 failed | 22 | 9 |
| CP-2 | S1 | CP-1 | runner-adjacent | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(RunnerCapabilitiesTests*)\|(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)\|(SessionCpuWatchdogTests*)\|(PhoneHomeConnectionServiceTests*)/*` | V-5, R-1 | all listed, 0 failed | 66 | 6 |
| CP-3 | S1 | CP-1 | probe-live-lane | `/*/*/HostStatsProbeLiveTests/*` | D-1 live probe on this lane's OS | 1 executed (the other OS's method reports not run with reason), 0 failed; the second sample's `cpuPercent` is in [0,100] | 1 | 2 |
| CP-4 | S1 | n/a | overhead-measure | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c718-overhead -- dotnet run --project src/Antiphon.SessionRunner --no-build --property:OutputPath=bin-c718s/ -- --urls http://127.0.0.1:0` twice (sampler on / `SessionRunner__HostStats__Enabled=false`), 60 s each, reading own-process `cpuPercent` from `GET /host-stats` on the on-instance and from `GET /api/diagnostics`-style process CPU delta for the off-instance; the runner is built once into `bin-c718s/` under the same wrapper | D-10 | on − off < 1 % of one core; both numbers, host and date written into `docs/testing-and-build.md` | n/a | 8 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c718a/` | host-stats-server | `/*/*/(HostStatsCacheTests*)\|(HostStatsAntiphonCountersTests*)\|(HostStatsPollServiceTests*)\|(HostStatsEndpointTests*)/*` | V-6, V-7, V-8, V-9 | all listed, 0 failed | 15 | 12 |
| CP-6 | S2 | CP-5 | server-adjacent | `/*/*/(PhoneHomeDirectoryTests*)\|(RunnerCatalogueTests*)\|(RunnerSlotEndpointTests*)\|(PhoneHomeEventPumpTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerCapabilityGateTests*)\|(HttpResilienceRegistrationTests*)/*` | R-2 | all listed, 0 failed | 36 | 8 |
| CP-7 | S3 | n/a | client | `pwsh -File scripts/test-client.ps1 hosts Sparkline HostsPage useHostStatsLive` then `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c718-lint -- npm --prefix client run lint` | V-10, R-4 | `CLIENT TESTS EXIT CODE: 0`, ≥ 10 tests, lint 0 errors 0 warnings | n/a | 5 |
| CP-8 | S4 | n/a | docs-named | `git grep -n -e "/api/hosts/stats" -e "HostStatsUpdated" -e "hostStatsV1" -e "SessionRunner:HostStats" -e "HostStats:PollIntervalMs" -- docs/ops-http.md docs/antiphon-api.md docs/resilience.md docs/testing-and-build.md` | V-11 | ≥ 8 matching lines across the 4 files, exit 0 | n/a | 1 |
| CP-9 | all R1, landed and activated | n/a | live-hosts | `curl -sS $ANTIPHON_API/api/hosts/stats` twice 10 s apart after `GET /api/version` shows the landed SHA | acceptance "both hosts appear with live values that update about every 5 s" | two entries, both `live`, `observedAt` advanced by ≥ 5 s, server2 `load1` non-null, desktop `load1` null and `cpuPercent` non-null | n/a | 2 |
| CP-10 | all R1 | CP-5 | unit-lane | `/*/*/*/*[Category=Unit]` | R-3 | ≥ 2990 executed, 0 failed (last measured 3021 total / 2993 passed / 28 skipped at `c18a6c67`) | 2900 | 12 |
| CP-11 | R2 S5 | `tests/Antiphon.SessionRunner.Tests -> bin-c718p/` | process-census | `/*/*/(HostStatsProcessCensusTests*)\|(HostStatsSamplerTests*)/*` plus the census timing line the test prints | R2 V (named totals, gating, the per-tick census cost on this lane) | all listed, 0 failed; census cost printed and under 2 ms or the sampler's every-fourth-tick mode asserted | 8 | 6 |
| CP-12 | R2 S5 | `tests/Antiphon.Tests -> bin-c718q/` | ef-rate-lease | `/*/*/(DbCommandRateMetricsTests*)\|(HostStatsAntiphonCountersTests*)\|(RepositoryLeaseWaitersTests*)/*` | R2 V | all listed, 0 failed | 9 | 8 |
| CP-13 | R2 S6 | n/a | client-r2 | `pwsh -File scripts/test-client.ps1 HostsPage HostCard` | R2 V (process table, tiles) | `CLIENT TESTS EXIT CODE: 0` | n/a | 3 |

The pipe characters inside `Filter` cells are table escapes; the command line uses a plain `|`,
quoted as [docs/testing-and-build.md](../../testing-and-build.md#combined-class-filters-card-0403)
shows. Run each TUnit row with `scripts/run-checkpoint.ps1 -Name CP-n -Project <project>
-OutputPath bin-c718x/ -Filter '<filter>' -MinExecuted <Min> -Expect <classes> -ResultsRoot
.antiphon/c718-checkpoints` (`-NoBuild` for reuse rows). CP-4, CP-7, CP-8 and CP-9 are non-TUnit
rows reported by hand in the CHECKPOINT line shape with their own assertion. CP-3's live probe
class carries one `[Test]` per OS that returns early with `Skip.Test("<other os>")` on the other
lane, so its executed count is 1 on either host. CP-9 runs after land and activation
(`GET /api/version` SHA equal to the landed commit; the local runner and server2 both restarted
onto the new binary — server2 through its deploy, which is the only way it gets a new runner).

### Cost

Round 1 checkpoints: 9 + 6 + 2 + 8 + 12 + 8 + 5 + 1 + 2 + 12 = **65** minutes plus slot waits.
Authoring Round 1: S1 ~4 h (two probes, store, sampler, routes, dispatcher cases, 22 tests),
S2 ~3.5 h (cache, counters, poll service, endpoints, 16 tests), S3 ~3 h (page, sparkline, live
hook, stories, 10 tests), S4 ~0.5 h; about 11 h. Round 2 checkpoints 6 + 8 + 3 = **17** minutes;
authoring about 5 h.

## Follow-ups (not in this card)

- An Attention item when a host stays above a load or memory threshold for N minutes: needs a
  week of `HostStatsUpdated` history to pick thresholds; CARD-0674's admission will want the
  same numbers.
- Fold `GET /api/session-runners`' capacity/occupancy and CARD-0654's budgets and CARD-0589's
  `buildSlots` into one `/api/hosts` family once all three exist.
- Runner-restart continuity: a 1-minute rollup file under `SessionLogPath` that the store reloads
  at start, if a 30-minute gap after a deploy turns out to matter.

## Open defaults (stated, not asked)

- The 30-minute retention and 5 s interval are settings; the ring size follows them.
- `cpu` on the graph is total host CPU %, not per-core; `cores` is shown next to it.
- Rollups are plain averages of the raw samples in the window (no time weighting; the
  interval is fixed, so weights would be equal anyway).
- Memory "used" is `total − available` (Linux `MemAvailable`, Windows `AvailPhys`), which is
  what the build-slot floor already reasons about.
- Series points carry the runner's clock (`At` from `TimeProvider`); the page does not adjust
  for skew between hosts, it just labels the axis in the viewer's local time.
