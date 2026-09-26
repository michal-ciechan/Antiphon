# CARD-0718 — Host stats: 5 s sampling, in-memory rollups, Hosts page and API

- Card: CARD-0718 (`18a35dd7`), Backlog, Normal/Soon.
- Plan task: `18fd01a3` (Plan, Frontier, worktree `feat/card-task-18fd01a3`), written on the
  server2 Linux runner. Ground truth read at `bafc3366`.
- Next stage: TestDesign (the `## Verification design` section below is the draft it refines;
  the `### Checkpoints` table is the closed list a Code dispatch runs).
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
