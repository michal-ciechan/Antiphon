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
that move every 5 s (CP-11 against the live host); rollups are correct against synthetic samples
(V-1); the 30-minute graph renders from a 360-point series (V-10); sampling cost is measured, not
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
  whole struct: `TotalPhys`, `AvailPhys`, `TotalPageFile`, `AvailPageFile`), `DriveInfo`, process count from
  `Process.GetProcesses().Length` **only if** CP-4 measures it under 1 ms, else omitted until
  Round 2. No WMI: `System.Management` is not referenced by the runner and one `Win32_Process`
  query is tens of milliseconds.
  The Windows `TotalPageFile` / `AvailPageFile` pair represents commit limit and available
  commit, so S4 docs must label the displayed figures as commit limit and charge, not physical
  swap capacity and use.
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
| `GET /api/hosts/stats` | `HostStatsDto[]`: `hostId`, `displayName`, `platform`, `state`, `observedAt`, `intervalSeconds`, `cores`, `current` (`cpuPercent`, `load1/5/15` or null, `memoryUsedBytes`, `memoryAvailableBytes`, `memoryTotalBytes`, `swapUsedBytes`, `swapTotalBytes`, `disks[] { path, freeBytes, totalBytes }`, `processCount`, `processes[] { name, sessionId?, cpuPercent, workingSetBytes }`), `rollups` (`{ "1m": {avg,max}, "5m": ..., "15m": ..., "30m": ... }` for `cpuPercent`, `load1`, `memoryUsedBytes`), `antiphon` (`tasksInFlight`, `byStage`, `byKind`, `queued`, `held`, `landsPending`, `sessionsLive`, `seatsDeclared`, `buildSlots { occupied, budget, waiters }`) |
| `GET /api/hosts/{hostId}/stats/series?metric=cpu&window=30m` | `{ hostId, metric, window, intervalSeconds, points: [{ t, v }] }`; `metric` ∈ `cpu`, `load`, `memory`; `window` ∈ `1m`, `5m`, `15m`, `30m`; 400 otherwise; 404 unknown host; 409 `phone_home_unavailable` when the runner cannot be asked (the page then keeps what it has) |
| SignalR `HostStatsUpdated` to group `hosts` | the same `HostStatsDto[]` after every tick; published only when the cache changed |

Not `GET /api/hosts`: CARD-0654 (Review) owns that collection for budgets, and this card's
routes nest under it so both can land in either order; when both are live the budget row can
carry a `stats` link later. Rejected: `PublishToAllAsync` every 5 s (every open tab pays for a
page nobody has open; a group with no members costs a dictionary lookup).

`tasksInFlight` remains a current Antiphon counter only. Its historical rollups and `metric=tasks`
series are deferred: the runner does not sample task counts, so accepting that metric would return
a successful empty series. S3 should display the current count without a tasks sparkline.

### D-8. Hosts page: a card per host, inline SVG sparklines, no chart dependency

`client/src/features/hosts/`: `HostsPage.tsx` (route `/hosts`, nav item **Hosts** after
**Agents**), `HostCard.tsx` (name, platform, state badge, current CPU / memory / load / tasks
and seats, a 1/5/15/30 table of avg and max, three `Sparkline`s), `Sparkline.tsx` (inline
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
first). Round 2 (S5–S6) is a second Code dispatch after Round 1 has landed and CP-11 has been
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
  `WindowsHostStatsProbe` gets the same kind of internal seam: `Func<(long Idle, long Kernel, long
  User)?> readSystemTimes`, `Func<(ulong TotalPhys, ulong AvailPhys, ulong TotalPageFile, ulong
  AvailPageFile)?> readMemoryStatus` (the public ctor passes the P/Invokes; null = the call
  returned false). `SystemHostStatsProbe.SelectArm(bool isWindows, bool isLinux)` is the platform
  switch as a pure internal static. V-2 m9, m10 use them on either lane.
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
- **MS-8** `SessionRunnerHttpClient` gains an optional `IOptions<HostStatsSettings>?` ctor
  parameter (after `resilience`) for `RequestTimeoutMs`/`SeriesTimeoutMs`, and uses its existing
  `TimeProvider` for the timeout source, so V-12 constructs it the `SessionRunnerHttpClientHerdrWireTests` way.

Slice ownership of what TestDesign adds: MS-1..MS-4, `HostStatsProbeLiveTests` and
`scripts/measure-host-stats-overhead.ps1` are S1; MS-5..MS-8 and
`SessionRunnerHttpClientHostStatsTests` are S2; `scripts/host-stats-hub-receipt.mjs` is S4.

### Delivery inventory

Host stats are a best-effort, last-value-wins telemetry stream: nothing is durable by design
(D-4), so "recovery" here means the next tick restores the value and the gap is shown as
`stale`/`offline`, never as zero. The durable identity joining every hop is
**`(hostId, sample.At)`** (runner id from the catalogue; `At` from the runner's `TimeProvider`).
No path carries session input, so no UserPrompt transcript applies.

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt |
|---|---|---|---|---|---|
| DP-1 sample | `HostStatsSamplerService.SampleOnceAsync` | `HostStatsStore` ring | none (runner memory; a runner restart empties the ring, accepted in D-4) | next tick; a faulting probe skips one tick (G-26) | `Latest().At` equals the fake clock (V-3 m1); `GET /host-stats` newest `At` (V-4 m1) |
| DP-2 local pull | runner `GET /host-stats` | server `HostStatsCache.Record("desktop", dto)` | none | next 5 s tick; a fault or 3 s timeout marks the host for `stale` (G-53, G-55) | projection `live` with the answered `At` (V-8 m1, m4) |
| DP-3 remote pull | runner dispatcher case 29 over the phone-home socket | `HostStatsCache.Record("server2", dto)` | none | silent peer: request cancelled at `RequestTimeoutMs`, host goes `stale`; disconnect: `offline`, then `live` on reconnect; old runner: `unsupported` | V-8 m2, m3, m5 against the real `PhoneHomeLiveConnection` |
| DP-4 push | `HostStatsPollService` after a tick with `Changed` | browser query cache `['hosts','stats']` and mounted series keys, via `IEventBus.PublishToGroupAsync("hosts", "HostStatsUpdated", list)` | none; the page's `GET /api/hosts/stats` on mount and on reconnect is the resync | publish fault: the tick survives and `Changed` stays set so the next tick republishes (G-59); client reconnect: rejoin + refetch (G-76) | server: `RecordingEventBus` holds one `("hosts","HostStatsUpdated", list)` whose entries carry the answered `At` (V-8 m1, m6); client: `queryClient.getQueryData(['hosts','stats'])` equals the pushed list (V-10 live m2); live: CP-11 hub receipt |
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
  `JoinGroup("hosts")`. Closed by CP-11's live hub receipt (a node `@microsoft/signalr` client joins
  `hosts` on the activated server and must receive two `HostStatsUpdated` lists with advancing
  `observedAt`) — this is the recipient evidence for DP-4.
- Mocked `HubConnectionBuilder` in vitest: proves the hook's `JoinGroup` call, `on('HostStatsUpdated')`
  handler and cache writes; cannot prove wire compatibility of the payload casing. Closed by CP-11
  (same payload read by the real client library) and V-9 m1 (the JSON the API serializes is the
  shape the page reads; both use the one `HostStatsDto`).
- `RecordingLocalClient` for `SessionRunnerHttpClient`: V-8 cannot prove the HTTP single-attempt and
  timeout; V-12 covers `SessionRunnerHttpClient` itself with a stub `HttpMessageHandler`.
- Fake `IHostStatsProbe`: cannot prove the OS reads; CP-3 runs the real probe on the lane's OS.

### Proves it works now

Each row: `V-n: behaviour | layer | test | expected`, then per method the **red-first line**: the
production line whose removal or change turns that method red at the named assertion (a compile
failure before the type exists is not counted as red-first; each method must also be red against a
stubbed body, e.g. `throw new NotImplementedException()` or a constant answer).

- **V-1: ring, windows and rollups are exact and never invent zeros | runner Unit, `FakeTimeProvider` |
  `HostStatsStoreTests` (9) | all green.** Fixture: `HostSample` factory with `cpu = i % 20`,
  one sample per 5 s at `At(i) = t0 + 5i s`, and `now` = the last sample's `At` **+ 5 s** (the
  next tick), so with an inclusive cutoff a window of `w` seconds holds exactly `w / 5` samples.
  - m1 `Empty_store_answers_null_latest_and_null_rollups`: `Latest()` null, every window of
    `Rollups(now)` null. Red: `Rollups` returning `new(0, 0)` for an empty window.
  - m2 `Ring_evicts_oldest_at_capacity_and_series_is_time_ordered`: after 359/360 samples
    `Series(cpu, 30m)` has 359/360 points; after 361 the first `At` is `At(1)` and the points
    are strictly ascending. Red: capacity `RetentionMinutes * 60 / IntervalSeconds` (+1), or
    returning slots in physical order without rotating from the head.
  - m3 `Rollups_match_hand_computed_avg_and_max_for_a_sawtooth`: 360 samples; `1m` avg 13.5
    (i = 348..359 -> 8..19), max 19; `5m` (60 samples) avg 9.5, max 19; `30m` avg 9.5, max 19;
    and a second store holding i = 0..358 (newest value 18) has `5m` max 19. Red: `avg` computed
    as `Max`, or `max` taking the newest value (the 359-sample assertion).
  - m4 `Partial_window_averages_only_the_samples_it_holds`: 24 samples (2 minutes, `cpu = i`);
    `5m` avg 11.5 over all 24, `15m` and `30m` the same, `1m` avg 17.5 over i = 12..23 (i = 12
    sits exactly on `now − 60 s` and is in). Red: dividing by the window's capacity (60, 180,
    360 samples) instead of the held count.
  - m5 `Window_boundary_includes_sixty_seconds_and_excludes_beyond`: a sample at `now − 60 s` is
    in `1m`; one at `now − 60.001 s` is not (two stores, one sample each, value 7). Red: the
    cutoff comparison `At >= now − window` changed to `>` (first assertion) or the cutoff widened
    by one interval (second assertion).
  - m6 `Metric_absent_from_samples_yields_null_rollups`: samples with `Load1 = null` give null
    `load1` rollups while `cpuPercent` rollups are non-null. Red: `?? 0` on the metric selector.
  - m7 `Series_rejects_unknown_metric_and_window`: `Series("bogus", ...)` and a window of
    `2h` throw `ArgumentException`. Red: the default arm of the metric switch returning `cpu`.
  - m8 `Snapshot_carries_interval_and_retention_and_copies`: `Snapshot(now)` has
    `IntervalSeconds` 5, `RetentionMinutes` 30; a `Series` result taken before another `Add` is
    unchanged after it. Red: returning a lazy view over the ring (no `ToArray()`).
  - m9 `Writers_and_readers_wait_for_the_store_gate`: the test holds `lock (store.Gate)` (internal)
    on its own thread, starts `Add` and `Rollups` on the thread pool, asserts neither completes
    within 200 ms, releases, asserts both complete within 5 s. Red: removing `lock (_gate)` in `Add`
    (first assertion) or in the read path (second). Replaces the draft's concurrent-loop method,
    which could not go red deterministically (a stub under CARD-0585 rule 4).
- **V-2: the OS figures are parsed and differenced correctly on both platforms | runner Unit, pure |
  `HostStatsProbeParseTests` (10) | all green on either lane.** TestDesign pins D-1's CPU formula:
  idle = `idle + iowait` (columns 4, 5), total = the first **eight** columns (user, nice, system,
  idle, iowait, irq, softirq, steal); `guest`/`guest_nice` are already inside `user`/`nice` and
  are excluded.
  - m1 `ProcStat_cpu_percent_is_busy_delta_over_total_delta`: `cpu 600 0 200 700 100 0 0 0 0 0`
    then `cpu 1000 0 400 1300 200 0 100 0 50 0`: Δtotal 1400, Δidle 700 -> 50.0 %. Red: idle
    without iowait (57.14 %), or guest summed into total (51.72 %).
  - m2 `ProcStat_short_first_line_still_parses`: an 8-column `cpu` line parses to the same totals.
    Red: an index read of column 9 without a length check (throws).
  - m3 `MemInfo_yields_total_available_and_swap`: the server2 figures from Ground truth give
    total 132,014,068 KiB x 1024, available 97,687,336 KiB x 1024, swap total 542,716 KiB x 1024,
    swap free 286,400 KiB x 1024. Red: kB not multiplied by 1024, or `SwapFree` read as `SwapTotal`.
  - m4 `MemInfo_without_MemAvailable_yields_null_available`: available null, total still set. Red:
    `?? 0` (or a `MemFree` fallback) on the available field.
  - m5 `LoadAvg_yields_three_loads_and_host_process_count`: `28.71 29.75 27.49 13/3789 8436` ->
    28.71, 29.75, 27.49, process count 3789. Red: taking the running count (13) before the slash.
  - m6 `Windows_cpu_percent_treats_kernel_as_including_idle`: `CpuPercent(idle 0->600, kernel
    0->800, user 0->200)` = 40.0 %. Red: `Δidle` added to the denominator (62.5 %).
  - m7 `First_read_has_no_cpu_and_second_read_has_the_delta`: `LinuxHostStatsProbe` over MS-1 with
    the m1 lines served in order: first `Read()` has `CpuPercent` null and non-null memory; second
    has 50.0. Red: a zero baseline for the first read (reports a since-boot percentage).
  - m8 `Linux_probe_memory_floor_and_sample_read_the_same_meminfo`: over MS-1, the
    `IHostMemoryProbe.AvailableBytes` of the probe equals `Read().MemoryAvailableBytes` (both
    97,687,336 x 1024), and on a `ServiceCollection` built with `AddHostStats` and
    `AddBuildSlotBroker` **in both call orders** the resolved `IHostMemoryProbe` is the same instance
    as `IHostStatsProbe` (so `AddHostStats` registers `AddSingleton<IHostMemoryProbe>(sp =>
    (IHostMemoryProbe)sp.GetRequiredService<IHostStatsProbe>())`, which beats the broker's
    `TryAddSingleton` either way). Red: `SystemHostMemoryProbe` left as the broker's independent
    registration, or `AvailableBytes` reading `MemFree`.
  - m9 `Platform_switch_selects_the_arm_for_each_os`: `SystemHostStatsProbe.SelectArm(isWindows,
    isLinux)` (internal static) returns a `WindowsHostStatsProbe` for (true, false), a
    `LinuxHostStatsProbe` for (false, true), null for (false, false); constructing either arm does
    no OS call. Red: the Linux branch missing from the switch (the live probe answers null on
    server2), or the two branches swapped.
  - m10 `Windows_probe_maps_a_failed_GetSystemTimes_to_null`: over MS-1's Windows seam, a
    `readSystemTimes` answering null (the P/Invoke returned false) gives `CpuPercent` null and
    keeps the memory figures; a `readMemoryStatus` answering null gives null memory fields, not 0.
    Red: a failed `GetSystemTimes` mapped to zeros (then 0 % or a since-boot percentage).
- **V-3: the sampler adds one timestamped sample per tick, survives faults and never charges an
  unknown process | runner Unit, MS-2 fakes, `FakeTimeProvider` | `HostStatsSamplerTests` (4) |
  all green.**
  - m1 `SampleOnce_adds_one_sample_at_the_fake_clock_time`: one call -> `store.Latest().At` equals
    `time.GetUtcNow()` (fake clock set to 2026-09-26T12:00:00Z). Red: `DateTimeOffset.UtcNow`
    instead of `_time.GetUtcNow()`.
  - m2 `Faulting_probe_leaves_store_unchanged_and_sampler_alive`: the probe throws
    `IOException` once: `Should.NotThrowAsync(SampleOnceAsync)`, `store.Latest()` still null; second
    call adds. Red: removing the `try/catch` around the probe read.
  - m3 `Session_process_cpu_is_delta_over_wall_and_null_sample_is_null`: target pid 4242 with fake
    CPU 1.0 s then 3.5 s across 5 s of fake time -> 50.0 % of one core (not divided by `Cores`); working set from the MS-2 reader; a
    second target whose probe answers null has `CpuPercent` null (not 0) and is still listed. Red:
    `?? TimeSpan.Zero` on the probe answer, or dividing by the configured interval instead of the
    measured wall delta (assert with a 6 s gap: 41.67 %).
  - m4 `Disabled_sampler_never_calls_the_probe`: `Enabled=false`, `StartAsync`, advance 20 s,
    `StopAsync`: probe call count 0. Red: removing the `if (!settings.Enabled) return;` at the top
    of `ExecuteAsync` (the immediate first sample then calls the probe).
- **V-4: the runner routes answer the store and refuse bad input | runner Integration, MS-4 |
  `HostStatsEndpointTests` (4) | all green.**
  - m1 `Host_stats_answers_newest_sample_and_one_minute_rollups`: two `SampleOnceAsync` 5 s apart;
    `GET /host-stats` newest `At` = second sample's, `rollups["1m"].cpuPercent` non-null with avg
    of the two fake values. Red: the route serializing `store.Latest()` only (no rollups) or the
    first slot.
  - m2 `Series_route_answers_points_in_window`: three samples, the first 6 minutes before the
    other two (fake clock advanced between `SampleOnceAsync` calls); `?metric=cpu&window=5m` -> 2
    points, ascending, `window` echoed `5m`; `window=30m` -> 3. Red: ignoring the `window` query
    value.
  - m3 `Bad_metric_or_window_is_400_problem`: `metric=bogus` and `window=2h` -> 400 with a
    `application/problem+json` body. Red: removing the route's validation (the store's
    `ArgumentException` surfaces as 500).
  - m4 `Disabled_host_stats_is_404`: host started with `SessionRunner:HostStats:Enabled=false` ->
    404 for both routes. Red: mapping the routes unconditionally.
  - The capability arm is `RunnerCapabilitiesTests.Host_stats_feature_is_advertised_only_when_enabled`
    (R-1, Unit, MS-3): `CapabilityFeatures(existing, Enabled=true)` ends with `hostStatsV1`,
    `Enabled=false` does not contain it, and the existing entries are preserved in order. Red:
    appending the token unconditionally.
- **V-5: operations 29/30 answer over phone-home and an old-shaped runner refuses typed | runner
  Unit | `PhoneHomeCommandDispatcherTests` +3 (34 -> 37) | all green.** A real `HostStatsStore`
  with two samples is the `hostStats:` seam.
  - m1 `Host_stats_operation_answers_the_store_snapshot`: `Result` frame, payload deserializes
    (`PhoneHomeFraming.Json`) to `RunnerHostStatsDto` whose newest `At` and `1m` cpu avg equal the
    store's. Red: the case answering `Result(request, null)` or a fresh empty snapshot.
  - m2 `Host_stats_series_answers_points_and_rejects_a_bad_window`: `{ metric: "cpu", window: "1m" }`
    -> 2 points; `window: "2h"` -> `Error` frame with `StatusCode` 400. Red: the case not catching
    the store's `ArgumentException` (the dispatcher's generic fault path answers 500).
  - m3 `Host_stats_without_the_seam_is_unsupported`: no seam -> `Error` frame with `ErrorCode`
    `phone_home_unsupported_operation` for both 29 and 30. Red: the cases reading
    `hostStats?.Snapshot(...)` and answering `Result` with a null payload instead of falling through
    to `UnsupportedOperation`.
- **V-6: the cache's projection never shows a zero for a missing value | server Unit,
  `FakeTimeProvider` | `HostStatsCacheTests` (6) | all green.** `StaleAfterMs` 15000.
  - m1 `Recorded_sample_projects_live`: `Record("server2", dto)` with the dto's `At` one hour
    behind the fake clock (runner clock skew) -> `live`, `current.cpuPercent` = dto's, `observedAt`
    = fake now. Red: the state switch defaulting to `stale`, or age measured from the dto's `At`.
  - m2 `Old_sample_projects_stale_with_the_same_values`: advance exactly 15000 ms -> still `live`;
    +1 ms -> `stale` with `current` and `rollups` equal to the recorded ones. Red: `age < StaleAfter`
    instead of `<=` (first assertion), or the stale arm nulling `current` (second).
  - m3 `Unrecorded_disconnected_host_projects_offline_with_null_current`: catalogue row for a
    remote id with no record -> `offline`, `current` null, `rollups` null. Red: projecting a
    default-constructed `current` (zeros).
  - m4 `Unsupported_failure_projects_unsupported`: `RecordFailure(id, Unsupported)` -> `unsupported`
    with null `current`. Red: mapping every failure kind to `offline`.
  - m5 `Changed_is_true_after_a_new_sample_and_false_after_an_empty_tick`: new `At` -> `Changed`
    true; `BeginTick()` then recording the same `At` again -> false; a state transition
    `live -> stale` alone -> true. Red: `Changed` returning true unconditionally (second assertion)
    or ignoring state transitions (third).
  - m6 `Disabled_poll_projects_every_host_offline_disabled`: options `Enabled=false` -> every
    catalogue host `offline` with `reason` `disabled`. Red: the disabled check removed (hosts
    project `offline` with reason null).
- **V-7: Antiphon's own counters count only what they name | server Unit, pure |
  `HostStatsAntiphonCountersTests` (3) | all green.** Rows: desktop (`RunnerId` null) Code Working,
  desktop (`""`) Code Dispatched, desktop Review Working, desktop Plan Queued with
  `CapacityWaitRetained`, desktop Investigate Queued without it, desktop Code Blocked with `HasLand`,
  `server2` Mutation Working (kind `grok`), `server2` Review Blocked without `HasLand`.
  - m1 `Group_counts_in_flight_by_stage_and_kind_per_host`: desktop `byStage` {Code 2, Review 1},
    `byKind` by the rows' kinds, `server2` {Mutation 1}, {grok 1}. Red: grouping `byStage` by
    `AgentKind`, or not partitioning by host.
  - m2 `Held_and_lands_count_only_their_flags`: desktop `queued` 2 (held included), `held` 1,
    `landsPending` 1. Red: `held` counting every Queued row (2), or `landsPending` counting every
    Blocked row regardless of the flag (`server2` `landsPending` 1 instead of 0).
  - m3 `Null_or_empty_runner_is_desktop_and_blocked_is_open_not_in_flight`: desktop
    `tasksInFlight` 3 (Working, Dispatched, Working), the Blocked row not in flight. Red: only
    `RunnerId == null` treated as desktop (2), or Blocked counted in flight (4).
- **V-8: one tick pulls every host through the real transports, isolates failures, and pushes to
  group `hosts` | server Integration, `PhoneHomeTestHost` + scripted peer (real
  `PhoneHomeLiveConnection` and `PhoneHomeRunnerClient`), MS-6/MS-7, `FakeTimeProvider` |
  `HostStatsPollServiceTests` (6) | all green.** The service is constructed by the test over
  `host.Directory`, a `HostStatsCache`, a fixed fake counters source and a `RecordingEventBus`;
  every method scripts `peer.Reply` for `HostStats` (the peer's default reply is `{ ok = true }`).
  Projection rule pinned here (refines D-4): `unsupported` if the last failure is unsupported;
  else `offline` if no sample was ever recorded or the remote id has no live connection; else `live`
  while `now − observedAt <= StaleAfterMs`, else `stale`. `observedAt` is the **server's** clock at
  `Record`, never the runner's `At` (hosts' clocks differ).
  - m1 `Tick_fills_both_hosts_live_and_publishes_once_to_group_hosts`: local answers a sample, the
    peer answers operation 29 with `At` = 2020-01-01 (a skewed runner clock): both `live`; exactly
    one `("hosts", "HostStatsUpdated", list)` with two entries whose `current` values are the
    answered ones; a second tick with identical answers publishes nothing. Red: `PublishToAllAsync`
    or a group name other than `hosts`; staleness computed from the runner's `At` (server2 `stale`
    immediately); publishing without the `Changed` check (two publishes).
  - m2 `Silent_peer_goes_stale_while_desktop_stays_live`: tick 1 both answer; then
    `peer.SilentFor(HostStats)`; tick 2 starts, the test awaits the peer's receipt of the request,
    advances the fake clock by `RequestTimeoutMs` (3000): the tick completes within 10 s of real
    time; advance to 15.001 s after tick 1: server2 `stale` with tick 1's values, desktop `live`
    (it answered tick 2). Red: no per-request `CancellationTokenSource(timeout, time)` (the tick
    waits the connection's 60 s default and the 10 s guard fails), or hosts awaited in sequence
    with a shared token.
  - m3 `Unsupported_peer_projects_unsupported`: the peer replies an `Error` frame with
    `ErrorCode` `phone_home_unsupported_operation`, `StatusCode` 400: server2 `unsupported`,
    `current` null; the desktop `live`. Red: `PhoneHomeRunnerClient.GetHostStatsAsync` returning
    null for an error frame (server2 `offline`) or throwing an untyped exception the poll records
    as unreachable.
  - m4 `Local_client_fault_does_not_fault_the_tick`: `HostStatsFault = new HttpRequestException()`
    on the first tick: `Should.NotThrowAsync(TickOnceAsync)`, desktop `offline` (never sampled),
    server2 `live`; a good tick then a faulting tick plus 15.001 s: desktop `stale` with the good
    values. Red: removing the per-host `try/catch` (the tick throws), or a failure clearing the
    last sample (desktop `offline` in the second half).
  - m5 `Disconnected_peer_projects_offline_and_recovers_live_on_reconnect`: tick 1 live; dispose
    the peer and wait for the directory to drop the connection; tick 2: server2 `offline` with
    tick 1's values still in `current`; connect a new peer (`ConnectPeerAsync`), tick 3: `live`
    with the new values. Red: an offline host projecting `current` null (the "keeps its last
    values" rule broken), or a missing connection recorded as a transient failure (`live`/`stale`
    instead of `offline`).
  - m6 `Publish_failure_does_not_fault_the_tick_and_the_next_tick_republishes`: the bus throws
    on its first publish: the tick completes; a second tick with identical answers publishes
    once. Red: clearing `Changed` before the publish rather than after it succeeds.
- **V-9: the API serves the cache and proxies series only for a known host with a valid query |
  server Integration, `PhoneHomeTestHost` with MS-5, `TestDbFixture.CreateIsolatedSchemaAsync()`
  | `HostStatsEndpointTests` (5) | all green.** Remote id is `host.AllowedRunnerId`.
  - m1 `Hosts_stats_answers_the_cache_projection_with_counters`: seed one Working Code task with
    `RunnerId = host.AllowedRunnerId` and one with `RunnerId = null`; tick once; `GET /api/hosts/stats`
    -> two entries, camelCase `state`, `current.cpuPercent`, `rollups["1m"].cpuPercent.avg`,
    `antiphon.tasksInFlight` 1 each. Red: the endpoint projecting without the counters (`antiphon`
    null), or the counters query not partitioning by runner (2 on the desktop).
  - m2 `Series_proxies_the_peer_operation_30_answer`: peer replies 3 points to `HostStatsSeries`;
    `GET /api/hosts/{id}/stats/series?metric=cpu&window=30m` -> the 3 points, `RequestCount(HostStatsSeries)`
    1, request payload `{ metric: "cpu", window: "30m" }`. Red: the endpoint resolving every id to
    `directory.Local`.
  - m3 `Unknown_host_is_404`: `/api/hosts/nope/stats/series?metric=cpu&window=1m` -> 404, no local
    series call recorded. Red: unknown ids falling through to the local client.
  - m4 `Bad_window_is_400_before_any_runner_call`: `window=2h` and `metric=bogus` -> 400,
    `RequestCount(HostStatsSeries)` 0 and no local series call. Red: validating after the proxy.
  - m5 `Series_for_a_disconnected_runner_is_409_unavailable`: no peer connected -> 409 with
    `phone_home_unavailable`. Red: a disconnected runner answering 200 with an empty series.
- **V-12: the local-runner read is one bounded attempt | server Unit, stub `HttpMessageHandler`
  (the `SessionRunnerHttpClientHerdrWireTests` construction), `FakeTimeProvider` |
  `SessionRunnerHttpClientHostStatsTests` (3) | all green.** MS-8: `SessionRunnerHttpClient` takes
  `IOptions<HostStatsSettings>?` for `RequestTimeoutMs`/`SeriesTimeoutMs`.
  - m1 `Host_stats_read_is_one_attempt`: the handler answers 503: the call fails and the handler
    saw exactly 1 request. Red: routing the read through `SendReadAsync` (the admitted read retries).
  - m2 `Not_found_maps_to_unsupported`: 404 -> `HostStatsUnsupportedException`. Red: 404 mapped
    to null (the cache would show `offline` for an old runner).
  - m3 `Hung_runner_is_cancelled_at_the_request_timeout`: the handler awaits its token; advancing
    the fake clock 3000 ms ends the call with `TimeoutException` while the caller's token is still
    live. Red: removing the `CancelAfter`/timeout source (the call never ends; a 10 s real-time
    guard fails).
- **V-10: the page shows each host's state honestly and stays live | client vitest + msw, mocked
  `@microsoft/signalr` (the `SessionTerminal.test.tsx:116` pattern) | `pwsh -File
  scripts/test-client.ps1 hosts` (matches `src/api/hosts.test.tsx` and every file under
  `src/features/hosts/`; no other client path contains `hosts` at `bafc3366`) | 12 tests green.**
  - `hosts.test.tsx` (2): `useHostStats reads /api/hosts/stats` (two entries); `useHostSeries
    requests metric and window` (msw asserts the query string). Red: a hard-coded `window=30m`.
  - `HostsPage.test.tsx` (4): `renders one card per host`; `a stale host keeps its last values
    under a Stale badge` (text `42 %` and badge `Stale`); `an offline host shows No data and never
    0 %` (no text matching `/\b0 ?%/` inside that card); `the Hosts nav item links to /hosts`.
    Red: `current?.cpuPercent ?? 0` in `HostCard` (third), or the stale arm hiding values (second).
  - `Sparkline.test.tsx` (2): `360 points draw one M and 359 L commands` (count in the `d`
    attribute, newest point at x = W); `an empty series renders the placeholder, not a flat
    line`. Red: `L` emitted for the first point, or an empty series drawing `M0,H`.
  - `useHostStatsLive.test.ts` (3+1 = 4): `joins group hosts on start` (`invoke('JoinGroup', 'hosts')`);
    `a pushed list replaces ['hosts','stats'] without a refetch` (msw request count for
    `/api/hosts/stats` unchanged, `getQueryData` equals the pushed list); `a pushed current point
    is appended to a mounted series key` (length +1, last `t` = pushed `observedAt`);
    `on reconnect it rejoins hosts and refetches` (`onreconnected` handler: second `JoinGroup`,
    one list refetch). Red: `invalidateQueries` instead of `setQueryData` (second), a group name
    other than `hosts` (first), no `onreconnected` handler (fourth).
- **V-11: the docs name the routes, states and settings | docs | CP-10 grep | >= 8 lines.** Red:
  the grep over the four files at `bafc3366` returns 0 lines (none of the tokens exist yet).
- **V-13: the real probe reads this lane's OS | runner Integration (no process spawn) |
  `HostStatsProbeLiveTests` (2 methods, 1 executes per lane) | the lane's method green, the other
  `Skip.Test` with the reason.** Idiom (the `CodexAuthProbeTests.cs:105` shape): each method opens
  with `if (!OperatingSystem.IsLinux()) { Skip.Test("Linux /proc probe; this lane is <os>, where
  <other method> runs."); return; }` (Windows method mirrors it).
  V-13 is live evidence for the P/Invokes and `/proc` paths, not a guard's positive control: the
  guards behind it (platform switch, failure-to-null) are V-2 m9/m10, which run on either lane.
  - `Linux_probe_reads_live_proc`: `new SystemHostStatsProbe(settings)` with `Volumes = [Path.GetTempPath()]`;
    first `Read()`: `MemoryTotalBytes` equals `MemTotal` kB x 1024 read independently from
    `/proc/meminfo` by the test, `0 < MemoryAvailableBytes <= MemoryTotalBytes`, `Cores ==
    Environment.ProcessorCount`, `CpuPercent` null, `Load1` non-null, `ProcessCount > 0`, one disk
    with `0 < Free <= Total`; after `Task.Delay(1000)` a second `Read()` has `CpuPercent` in
    `[0, 100]`. Red: the platform switch in `SystemHostStatsProbe`'s ctor selecting no Linux arm
    (`Read()` null), or `/proc/stat` read once at construction and never again (second `CpuPercent`
    null).
  - `Windows_probe_reads_live_system_times`: same shape; `MemoryTotalBytes >= 1 GiB`,
    `MemoryAvailableBytes <= MemoryTotalBytes`, `SwapTotalBytes >= MemoryTotalBytes` (the commit
    limit includes physical memory), `Load1` null (not 0), second `CpuPercent` in `[0, 100]`. Red:
    the Windows arm missing, or `GetSystemTimes` failure mapped to 0 instead of null.
- **V-14: the sampler costs under 1 % of one core | runner process, OS CPU accounting | CP-4
  (`scripts/measure-host-stats-overhead.ps1`, written by Code in S1) | `mean(on) − mean(off) < 1.0`
  percentage points of one core.** The measurement reads the runner **process's**
  `TotalProcessorTime` from the OS (`/proc/<pid>/stat` on Linux, the process handle on Windows,
  both through .NET `Process`), not the sampler's own report of itself, so a sampler that
  under-reports cannot pass it. It is a threshold measurement, not a mutation-backed test, and
  not a stub: its input is independent of the code under test and it fails on a sampler that does
  per-tick enumeration work D-6 deferred. Step 0's dry run is G-77's check (PC-77).
- **V-15: both hosts appear live in the activated system and the page's push reaches a joined
  client | live server + real SignalR | CP-11 | two entries, both `live`, `observedAt` advancing,
  and two `HostStatsUpdated` receipts.** This is DP-4's recipient evidence (Delivery inventory).
  Red-first: before land, `GET /api/hosts/stats` is 404 and the hub never sends the event.
- **V-12** is listed above V-10 because it closes S2's server surface; numbering is creation order.

### Guards the regression

- **R-1: runner classes on S1's path stay green | `PhoneHomeCommandDispatcherTests` (34 + 3),
  `RunnerCapabilitiesTests` (4 + 1), `BuildSlotBrokerTests` (11), `BuildSlotEndpointTests` (4),
  `PhoneHomeConnectionServiceTests` (10) = 67 executions.** Decisive assertions: the existing
  34 dispatcher methods (unknown operation `(PhoneHomeOperation)999` still `Error`, capacity paths
  untouched by the new ctor parameter); the build-slot memory floor still reads the injected
  `IHostMemoryProbe` (`BuildSlotTestHost` injects one before `AddBuildSlotBroker`, so V-2 m8's
  `AddSingleton` forwarder must not displace an injected probe: `AddHostStats` is not called by
  `BuildSlotTestHost`).
- **R-2: server classes on S2's path stay green | `PhoneHomeDirectoryTests` (7),
  `RunnerCatalogueTests` (4), `RunnerSlotEndpointTests` (8), `PhoneHomeEventPumpTests` (8),
  `SessionRunnerEventPumpTests` (2 methods, 4 `[Arguments]` rows on one: 5 executions),
  `SessionRunnerCapabilityGateTests` (2), `HttpResilienceRegistrationTests` (7) = 41 executions.**
  Decisive: `HttpResilienceRegistrationTests` still sees exactly the admitted operations it lists
  (the host-stats reads are not added to `ResilienceOperations`); the event pumps still ignore
  unknown event names (no host-stats event exists, D-3).
- **R-3: the `Antiphon.Tests` Unit lane | `/*/*/*/*[Category=Unit]` | >= 2900 executed, 0 failed.**
  Static count at `bafc3366`: 225 files with a class-level `[Category("Unit")]`, 2238 `[Test]` and
  1195 `[Arguments]` lines (about 3.2k executions before data-source expansion); last measured run
  3021 total / 2993 executed at `c18a6c67`. This card adds 12 to it (V-6 6, V-7 3, V-12 3). A failure
  that also fails at `bafc3366` is reported as pre-existing, not fixed here.
- **R-4: client lint and the whole vitest suite | `npm run lint` 0 warnings; `test-client.ps1`
  (all 106 files at `bafc3366` plus 4) | exit 0.** Decisive: `App.test.tsx` still renders every
  route with the new lazy `hosts` route and nav item.

Red-first rule: every V method is committed red against a stubbed body before its production
change, and the commit message quotes the failing assertion; a method that passes against the
stub is a stub test (CARD-0585 rule 4). V-13 is red against a `SystemHostStatsProbe` whose `Read()`
returns null. No row in this design is a known stub; the draft's concurrent-loop store method was
one and was replaced (V-1 m9).

### Guard inventory

Safety-critical here means: a guard whose failure would show the operator a false number or a
false state (an invented zero, `live` for a silent host, wrong arithmetic), stall or fault the
poll or the runner, advertise a capability the runner does not have, let a bad request reach a
runner, or let the CP-4 measurement runner touch production. Each guard is split where two lines
can be broken independently, and maps 1:1 to a distinct PC.

| G | Plan ref | Guard | PC |
|---|---|---|---|
| G-1 | D-2 | an empty window answers null, never `{0,0}` | PC-1 |
| G-2 | D-2 | ring capacity is exactly `RetentionMinutes*60/IntervalSeconds` (bounded memory) | PC-2 |
| G-3 | D-2 | `Series` rotates from the oldest slot (time order) | PC-3 |
| G-4 | D-2 | `avg` is the mean of held samples | PC-4 |
| G-5 | D-2 | `max` is the maximum of held samples | PC-5 |
| G-6 | D-2 | a partial window divides by the held count, not its capacity | PC-6 |
| G-7 | D-2 | the cutoff is inclusive (`At >= now − window`) | PC-7 |
| G-8 | D-2 | the cutoff is not widened past the window | PC-8 |
| G-9 | D-2 | a metric absent from samples yields null rollups | PC-9 |
| G-10 | D-2, D-7 | unknown metric/window is rejected, not defaulted | PC-10 |
| G-11 | D-2 | reads return copies, not views of the ring | PC-11 |
| G-12 | D-2 | `Add` takes the store gate | PC-12 |
| G-13 | D-2 | reads take the store gate | PC-13 |
| G-14 | D-1 | Linux idle = idle + iowait | PC-14 |
| G-15 | D-1 | Linux total = first eight columns (guest excluded) | PC-15 |
| G-16 | D-1 | an 8-column `cpu` line parses | PC-16 |
| G-17 | D-1 | `/proc/meminfo` kB are multiplied by 1024 | PC-17 |
| G-18 | D-1 | missing `MemAvailable` is null available | PC-18 |
| G-19 | D-1 | process count is the loadavg total after the slash | PC-19 |
| G-20 | D-1 | Windows kernel time includes idle | PC-20 |
| G-21 | D-1 | the first read has no CPU (no since-boot figure) | PC-21 |
| G-22 | D-1 | the build-slot memory floor and the sample share one probe instance | PC-22 |
| G-23 | D-1 | the platform switch selects the Linux arm on Linux and the Windows arm on Windows | PC-23 |
| G-24 | D-1 | a failed `GetSystemTimes`/`GlobalMemoryStatusEx` is null, not zero | PC-24 |
| G-25 | D-2, D-9 | samples are stamped from the injected `TimeProvider` | PC-25 |
| G-26 | D-1 | a faulting probe is caught; the sampler survives | PC-26 |
| G-27 | D-6 | a null process CPU sample is a null percent (recycled/gone pid never charged) | PC-27 |
| G-28 | D-6 | process CPU % divides by the measured wall delta | PC-28 |
| G-29 | D-9 | `Enabled=false` never calls the probe | PC-29 |
| G-30 | D-7 | `GET /host-stats` answers the newest sample with rollups | PC-30 |
| G-31 | D-7 | the series route honours `window` | PC-31 |
| G-32 | D-7 | the runner route answers 400 for a bad metric/window | PC-32 |
| G-33 | D-9 | a disabled runner answers 404 | PC-33 |
| G-34 | D-9, D-11 | `hostStatsV1` is advertised only when enabled | PC-34 |
| G-35 | D-3 | operation 29 answers the store snapshot | PC-35 |
| G-36 | D-3 | operation 30 with a bad window is a 400 error frame | PC-36 |
| G-37 | D-3 | no seam answers `phone_home_unsupported_operation` | PC-37 |
| G-38 | D-4 | age is measured from the server's `observedAt`, never the runner's `At` | PC-38 |
| G-39 | D-4 | `live` through exactly `StaleAfterMs` (inclusive) | PC-39 |
| G-40 | D-4 | a stale host keeps its last values | PC-40 |
| G-41 | D-4 | a never-sampled host projects null `current`, never zeros | PC-41 |
| G-42 | D-4 | an unsupported failure projects `unsupported` | PC-42 |
| G-43 | D-7 | `Changed` is false when nothing new arrived | PC-43 |
| G-44 | D-7 | a state transition alone sets `Changed` | PC-44 |
| G-45 | D-9 | a disabled poll projects `offline` with reason `disabled` | PC-45 |
| G-46 | D-5 | `byStage` groups by `AgentTaskRole`, partitioned per host | PC-46 |
| G-47 | D-5 | `held` counts only `CapacityWaitRetained` | PC-47 |
| G-48 | D-5 | `landsPending` counts only `LandRequestedAt != null` | PC-48 |
| G-49 | D-5 | a null **or empty** `RunnerId` is the desktop | PC-49 |
| G-50 | D-5 | Blocked is open but not in flight | PC-50 |
| G-51 | D-7 | the push goes to group `hosts`, not to all clients | PC-51 |
| G-52 | D-7 | the poll publishes only when `Changed` | PC-52 |
| G-53 | D-3 | each host request is cancelled at `RequestTimeoutMs` on the injected clock | PC-53 |
| G-54 | D-3 | an unsupported error frame becomes a typed unsupported answer in `PhoneHomeRunnerClient` | PC-54 |
| G-55 | D-3 | one host's failure is caught per host; the tick never faults | PC-55 |
| G-56 | D-4 | a failure does not clear the last good sample | PC-56 |
| G-57 | D-4 | an offline host keeps its last values | PC-57 |
| G-58 | D-4 | a remote id with no live connection is `offline`, not `stale`/`live` | PC-58 |
| G-59 | D-7 | `Changed` is cleared only after a publish succeeds | PC-59 |
| G-60 | D-5, D-7 | the API projection carries the per-host counters | PC-60 |
| G-61 | D-4, D-7 | series requests go to the resolved runner, not always the local one | PC-61 |
| G-62 | D-7 | an unknown host id is 404 and reaches no runner | PC-62 |
| G-63 | D-7 | metric/window are validated before any runner call | PC-63 |
| G-64 | D-7 | a disconnected runner's series is 409 `phone_home_unavailable` | PC-64 |
| G-65 | D-3 | the local host-stats read is one attempt (not an admitted read) | PC-65 |
| G-66 | D-4 | a runner 404 is unsupported, not offline | PC-66 |
| G-67 | D-3 | the local read ends at `RequestTimeoutMs` | PC-67 |
| G-68 | D-8 | the series hook sends the requested window | PC-68 |
| G-69 | D-4, D-8 | the page keeps a stale host's values under a Stale badge | PC-69 |
| G-70 | D-4, D-8 | an offline card shows "No data", never `0 %` | PC-70 |
| G-71 | D-8 | the sparkline path is one `M` then `L` per further point | PC-71 |
| G-72 | D-8 | an empty series renders the placeholder, not a flat line | PC-72 |
| G-73 | D-7, D-8 | the live hook joins group `hosts` | PC-73 |
| G-74 | D-8 | a push is written with `setQueryData`, not a refetch | PC-74 |
| G-75 | D-8 | a push appends to mounted series keys | PC-75 |
| G-76 | D-8 | on reconnect the hook rejoins `hosts` and refetches the list | PC-76 |
| G-77 | D-10 | the CP-4 measurement runner is launched with `PhoneHome__*`/`SessionRunner__*`/`ASPNETCORE_*` scrubbed, `--PhoneHome:Enabled false` and `--urls http://127.0.0.1:0` | PC-77 |

Totals: guards = 77, mapped = 77, missing = 0, duplicate PC maps = 0. Not guards (evidence only):
V-13's live reads and CP-4's overhead number (a measurement with its own threshold), CP-11's
activation check.

### Positive controls

Mutation runs each after land: apply the compiling defect, run **only** the named method with
`--treenode-filter "/*/*/<Class>/<Method>"` (method-level OR matches nothing on this runner, so one
invocation per method), see the named assertion fail, restore (refresh the timestamp), run the
same method green. Batch only PCs in different files and methods. Code runs the V/R rows; Review
judges this list before land. Runner PCs use `tests/Antiphon.SessionRunner.Tests`, server PCs
`tests/Antiphon.Tests`, client PCs `pwsh -File scripts/test-client.ps1 <file> -t "<test name>"`.

| PC | Break (file: compiling defect) | Red method | At |
|---|---|---|---|
| PC-1 | `HostStatsStore.cs`: empty window returns `new HostRollup(0, 0)` | `HostStatsStoreTests.Empty_store_answers_null_latest_and_null_rollups` | rollup `ShouldBeNull` |
| PC-2 | `HostStatsStore.cs`: capacity `+ 1` | `HostStatsStoreTests.Ring_evicts_oldest_at_capacity_and_series_is_time_ordered` | `points.Count.ShouldBe(360)` after 361 |
| PC-3 | `HostStatsStore.cs`: `Series` iterates slots `0..n` instead of from the head | same method as PC-2 | first point `At` = `At(1)` |
| PC-4 | `HostStatsStore.cs`: `Avg = values.Max()` | `HostStatsStoreTests.Rollups_match_hand_computed_avg_and_max_for_a_sawtooth` | `1m` avg 13.5 |
| PC-5 | `HostStatsStore.cs`: `Max = values[^1]` | same method as PC-4 | the 359-sample store's `5m` max 19 (got 18) |
| PC-6 | `HostStatsStore.cs`: divide by `window / interval` | `HostStatsStoreTests.Partial_window_averages_only_the_samples_it_holds` | `5m` avg 11.5 |
| PC-7 | `HostStatsStore.cs`: cutoff `>` instead of `>=` | `HostStatsStoreTests.Window_boundary_includes_sixty_seconds_and_excludes_beyond` | the 60 s sample is in `1m` |
| PC-8 | `HostStatsStore.cs`: cutoff `now − window − interval` | same method as PC-7 | the 60.001 s sample is not in `1m` |
| PC-9 | `HostStatsStore.cs`: selector `s.Load1 ?? 0` | `HostStatsStoreTests.Metric_absent_from_samples_yields_null_rollups` | `load1` rollup `ShouldBeNull` |
| PC-10 | `HostStatsStore.cs`: metric switch `_ => s.CpuPercent` | `HostStatsStoreTests.Series_rejects_unknown_metric_and_window` | `Should.Throw<ArgumentException>` |
| PC-11 | `HostStatsStore.cs`: `Series` returns the `Where(...)` enumerable without `ToArray()` | `HostStatsStoreTests.Snapshot_carries_interval_and_retention_and_copies` | earlier series count unchanged |
| PC-12 | `HostStatsStore.cs`: remove `lock (_gate)` in `Add` | `HostStatsStoreTests.Writers_and_readers_wait_for_the_store_gate` | `add.IsCompleted.ShouldBeFalse()` |
| PC-13 | `HostStatsStore.cs`: remove `lock (_gate)` in the read path | same method as PC-12 | `read.IsCompleted.ShouldBeFalse()` |
| PC-14 | `LinuxHostStatsProbe.cs`: idle = column 4 only | `HostStatsProbeParseTests.ProcStat_cpu_percent_is_busy_delta_over_total_delta` | 50.0 (got 57.14) |
| PC-15 | `LinuxHostStatsProbe.cs`: total sums all columns | same method as PC-14 | 50.0 (got 51.72) |
| PC-16 | `LinuxHostStatsProbe.cs`: read `parts[9]` unconditionally | `HostStatsProbeParseTests.ProcStat_short_first_line_still_parses` | `Should.NotThrow` |
| PC-17 | `LinuxHostStatsProbe.cs`: drop `* 1024` | `HostStatsProbeParseTests.MemInfo_yields_total_available_and_swap` | total bytes |
| PC-18 | `LinuxHostStatsProbe.cs`: available `?? 0` | `HostStatsProbeParseTests.MemInfo_without_MemAvailable_yields_null_available` | `ShouldBeNull` |
| PC-19 | `LinuxHostStatsProbe.cs`: `split('/')[0]` | `HostStatsProbeParseTests.LoadAvg_yields_three_loads_and_host_process_count` | 3789 |
| PC-20 | `WindowsHostStatsProbe.cs`: denominator `Δkernel + Δuser + Δidle` | `HostStatsProbeParseTests.Windows_cpu_percent_treats_kernel_as_including_idle` | 40.0 |
| PC-21 | `LinuxHostStatsProbe.cs`: previous reading initialised to zeros | `HostStatsProbeParseTests.First_read_has_no_cpu_and_second_read_has_the_delta` | first `CpuPercent` null |
| PC-22 | `HostStatsRoutes.cs`: remove the `AddSingleton<IHostMemoryProbe>` forwarder | `HostStatsProbeParseTests.Linux_probe_memory_floor_and_sample_read_the_same_meminfo` | `ShouldBeSameAs` (broker-first order) |
| PC-23 | `HostStatsProbe.cs`: `SelectArm` returns null for Linux | `HostStatsProbeParseTests.Platform_switch_selects_the_arm_for_each_os` | `ShouldBeOfType<LinuxHostStatsProbe>` |
| PC-24 | `WindowsHostStatsProbe.cs`: failed read -> `(0, 0, 0)` | `HostStatsProbeParseTests.Windows_probe_maps_a_failed_GetSystemTimes_to_null` | `CpuPercent` null |
| PC-25 | `HostStatsSamplerService.cs`: `DateTimeOffset.UtcNow` | `HostStatsSamplerTests.SampleOnce_adds_one_sample_at_the_fake_clock_time` | `At` = fake now |
| PC-26 | `HostStatsSamplerService.cs`: remove the probe `try/catch` | `HostStatsSamplerTests.Faulting_probe_leaves_store_unchanged_and_sampler_alive` | `Should.NotThrowAsync` |
| PC-27 | `HostStatsSamplerService.cs`: `?? TimeSpan.Zero` | `HostStatsSamplerTests.Session_process_cpu_is_delta_over_wall_and_null_sample_is_null` | second target `CpuPercent` null |
| PC-28 | `HostStatsSamplerService.cs`: divide by `_interval` | same method as PC-27 | 41.67 at the 6 s gap |
| PC-29 | `HostStatsSamplerService.cs`: remove the `Enabled` early return | `HostStatsSamplerTests.Disabled_sampler_never_calls_the_probe` | calls 0 |
| PC-30 | `HostStatsRoutes.cs`: answer `store.Latest()` without rollups | `HostStatsEndpointTests.Host_stats_answers_newest_sample_and_one_minute_rollups` | `rollups["1m"]` non-null |
| PC-31 | `HostStatsRoutes.cs`: pass `"30m"` instead of the query `window` | `HostStatsEndpointTests.Series_route_answers_points_in_window` | `5m` point count 2 (got 3) |
| PC-32 | `HostStatsRoutes.cs`: remove the validation block | `HostStatsEndpointTests.Bad_metric_or_window_is_400_problem` | status 400 |
| PC-33 | `HostStatsRoutes.cs`: map regardless of `Enabled` | `HostStatsEndpointTests.Disabled_host_stats_is_404` | status 404 |
| PC-34 | `HostStatsRoutes.cs`: `CapabilityFeatures` appends unconditionally | `RunnerCapabilitiesTests.Host_stats_feature_is_advertised_only_when_enabled` | `ShouldNotContain("hostStatsV1")` |
| PC-35 | `PhoneHomeCommandDispatcher.cs`: case 29 `Result(request, null)` | `PhoneHomeCommandDispatcherTests.Host_stats_operation_answers_the_store_snapshot` | payload `At` |
| PC-36 | `PhoneHomeCommandDispatcher.cs`: case 30 without the `ArgumentException` catch | `PhoneHomeCommandDispatcherTests.Host_stats_series_answers_points_and_rejects_a_bad_window` | `StatusCode` 400 |
| PC-37 | `PhoneHomeCommandDispatcher.cs`: null seam answers `Result` with null payload | `PhoneHomeCommandDispatcherTests.Host_stats_without_the_seam_is_unsupported` | `ErrorCode` unsupported |
| PC-38 | `HostStatsCache.cs`: age from `dto.At` | `HostStatsCacheTests.Recorded_sample_projects_live` | state `live` |
| PC-39 | `HostStatsCache.cs`: `age < StaleAfter` | `HostStatsCacheTests.Old_sample_projects_stale_with_the_same_values` | `live` at exactly 15000 ms |
| PC-40 | `HostStatsCache.cs`: stale arm sets `Current = null` | same method as PC-39 | stale `current.cpuPercent` equals the recorded one |
| PC-41 | `HostStatsCache.cs`: never-sampled host projects `new HostStatsCurrentDto()` | `HostStatsCacheTests.Unrecorded_disconnected_host_projects_offline_with_null_current` | `current` null |
| PC-42 | `HostStatsCache.cs`: every failure kind -> `offline` | `HostStatsCacheTests.Unsupported_failure_projects_unsupported` | state `unsupported` |
| PC-43 | `HostStatsCache.cs`: `Changed => true` | `HostStatsCacheTests.Changed_is_true_after_a_new_sample_and_false_after_an_empty_tick` | `Changed` false after the repeat |
| PC-44 | `HostStatsCache.cs`: `Changed` set only by a new `At` | same method as PC-43 | `Changed` true after `live -> stale` |
| PC-45 | `HostStatsCache.cs`: remove the `Enabled` check | `HostStatsCacheTests.Disabled_poll_projects_every_host_offline_disabled` | reason `disabled` |
| PC-46 | `HostStatsAntiphonCounters.cs`: `byStage` keyed by `AgentKind` | `HostStatsAntiphonCountersTests.Group_counts_in_flight_by_stage_and_kind_per_host` | desktop `byStage["Code"]` 2 |
| PC-47 | `HostStatsAntiphonCounters.cs`: `held` = Queued count | `HostStatsAntiphonCountersTests.Held_and_lands_count_only_their_flags` | `held` 1 (got 2) |
| PC-48 | `HostStatsAntiphonCounters.cs`: `landsPending` = Blocked count | same method as PC-47 | `server2` `landsPending` 0 (got 1) |
| PC-49 | `HostStatsAntiphonCounters.cs`: desktop predicate `RunnerId == null` | `HostStatsAntiphonCountersTests.Null_or_empty_runner_is_desktop_and_blocked_is_open_not_in_flight` | desktop `tasksInFlight` 3 (got 2) |
| PC-50 | `HostStatsAntiphonCounters.cs`: in flight includes Blocked | same method as PC-49 | desktop `tasksInFlight` 3 (got 4) |
| PC-51 | `HostStatsPollService.cs`: `PublishToAllAsync` | `HostStatsPollServiceTests.Tick_fills_both_hosts_live_and_publishes_once_to_group_hosts` | recorded group `hosts` |
| PC-52 | `HostStatsPollService.cs`: remove `if (cache.Changed)` | same method as PC-51 | publish count 1 after the second tick |
| PC-53 | `HostStatsPollService.cs`: pass the tick token instead of the per-host timeout source | `HostStatsPollServiceTests.Silent_peer_goes_stale_while_desktop_stays_live` | tick completes within 10 s |
| PC-54 | `PhoneHomeRunnerClient.cs`: `GetHostStatsAsync` returns null on an `Error` frame | `HostStatsPollServiceTests.Unsupported_peer_projects_unsupported` | server2 `unsupported` |
| PC-55 | `HostStatsPollService.cs`: remove the per-host `try/catch` | `HostStatsPollServiceTests.Local_client_fault_does_not_fault_the_tick` | `Should.NotThrowAsync` |
| PC-56 | `HostStatsCache.cs`: `RecordFailure` clears the last sample | same method as PC-55 | desktop `stale` with the good values |
| PC-57 | `HostStatsCache.cs`: offline arm sets `Current = null` when a sample exists | `HostStatsPollServiceTests.Disconnected_peer_projects_offline_and_recovers_live_on_reconnect` | offline `current` equals tick 1's |
| PC-58 | `HostStatsPollService.cs`: a missing connection recorded as `Unreachable` instead of `Offline` | same method as PC-57 | server2 `offline` |
| PC-59 | `HostStatsPollService.cs`: `cache.ClearChanged()` before `PublishToGroupAsync` | `HostStatsPollServiceTests.Publish_failure_does_not_fault_the_tick_and_the_next_tick_republishes` | publish count 1 after the second tick |
| PC-60 | `HostStatsEndpoints.cs`: project with `antiphon: null` | `HostStatsEndpointTests.Hosts_stats_answers_the_cache_projection_with_counters` | `antiphon.tasksInFlight` 1 |
| PC-61 | `HostStatsEndpoints.cs`: series always via `directory.Local` | `HostStatsEndpointTests.Series_proxies_the_peer_operation_30_answer` | `RequestCount(HostStatsSeries)` 1 |
| PC-62 | `HostStatsEndpoints.cs`: unknown id resolved to `directory.Local` | `HostStatsEndpointTests.Unknown_host_is_404` | status 404 |
| PC-63 | `HostStatsEndpoints.cs`: validation moved after the runner call | `HostStatsEndpointTests.Bad_window_is_400_before_any_runner_call` | `RequestCount(HostStatsSeries)` 0 |
| PC-64 | `HostStatsEndpoints.cs`: `phone_home_unavailable` caught into an empty 200 | `HostStatsEndpointTests.Series_for_a_disconnected_runner_is_409_unavailable` | status 409 |
| PC-65 | `SessionRunnerHttpClient.cs`: `GetHostStatsAsync` through `SendReadAsync` | `SessionRunnerHttpClientHostStatsTests.Host_stats_read_is_one_attempt` | handler requests 1 |
| PC-66 | `SessionRunnerHttpClient.cs`: 404 -> `return null` | `SessionRunnerHttpClientHostStatsTests.Not_found_maps_to_unsupported` | `ThrowAsync<HostStatsUnsupportedException>` |
| PC-67 | `SessionRunnerHttpClient.cs`: remove the timeout source | `SessionRunnerHttpClientHostStatsTests.Hung_runner_is_cancelled_at_the_request_timeout` | `TimeoutException` within 10 s |
| PC-68 | `client/src/api/hosts.ts`: `window=30m` literal | `hosts.test.tsx` / `useHostSeries requests metric and window` | msw sees `window=5m` |
| PC-69 | `HostCard.tsx`: stale state renders the offline body | `HostsPage.test.tsx` / `a stale host keeps its last values under a Stale badge` | text `42 %` |
| PC-70 | `HostCard.tsx`: `current?.cpuPercent ?? 0` | `HostsPage.test.tsx` / `an offline host shows No data and never 0 %` | no `/\b0 ?%/` in the card |
| PC-71 | `Sparkline.tsx`: every point emits `L` | `Sparkline.test.tsx` / `360 points draw one M and 359 L commands` | one `M` |
| PC-72 | `Sparkline.tsx`: empty series draws `M0,H L W,H` | `Sparkline.test.tsx` / `an empty series renders the placeholder, not a flat line` | placeholder present, no `path` |
| PC-73 | `useHostStatsLive.ts`: `invoke('JoinGroup', 'host')` | `useHostStatsLive.test.ts` / `joins group hosts on start` | `invoke` called with `hosts` |
| PC-74 | `useHostStatsLive.ts`: `invalidateQueries` instead of `setQueryData` | `useHostStatsLive.test.ts` / `a pushed list replaces ['hosts','stats'] without a refetch` | list request count unchanged |
| PC-75 | `useHostStatsLive.ts`: skip the series append | `useHostStatsLive.test.ts` / `a pushed current point is appended to a mounted series key` | series length +1 |
| PC-76 | `useHostStatsLive.ts`: no `onreconnected` handler | `useHostStatsLive.test.ts` / `on reconnect it rejoins hosts and refetches` | second `JoinGroup` |
| PC-77 | `scripts/measure-host-stats-overhead.ps1`: delete the environment-scrub loop | CP-4 step 0 (`-DryRun`) | output contains no `PhoneHome__` key name |

Client PCs run as `pwsh -File scripts/test-client.ps1 <file> -t "<test name>"` (arguments pass
through to vitest; one test per invocation). PC-77 runs the CP-4 step 0 command only; it starts
no runner.

### Out of scope

- `SessionCpuWatchdogTests` (draft R-1): the plan changes neither `IProcessCpuProbe` nor the
  watchdog (the sampler only consumes the interface), and the class starts a real `cmd.exe` pty
  session from `Environment.SystemDirectory`, so it cannot be an either-lane row.
- Round 2 (S5–S6) and the draft's Round 2 rows (its CP-11..CP-13): their V rows are named only as "R2 V", which is a
  placeholder; Round 2 gets its own TestDesign pass after Round 1 lands and CP-11 has been seen
  live, and its rows are not part of this closed list.
- A server-side SignalR hub test: `Antiphon.Tests` has no `Microsoft.AspNetCore.SignalR.Client`
  reference, and adding one for a single hop is not worth a package change; CP-11's live receipt
  covers the hop (Delivery inventory).
- A Playwright check of `/hosts`: V-10 plus CP-11's hub receipt cover the data path; the visual
  layout (mobile one-column, rollup table collapse) is the operator's acceptance look.
- A per-tick query-count assertion for D-5: the grouping is V-7, the real query V-9 m1; 0.2
  queries/s is not a correctness property and `efReadAttempts` is observable after land.
- New runner x old server, and two browser tabs in one group: see the boundary table.
- D-12's exclusions (placement/backpressure, Attention on overload, rollup persistence, Postgres
  CPU, Windows processor queue, container CPU).

### Checkpoints

Round 1 only. Isolated outputs `bin-c718r/` (`tests/Antiphon.SessionRunner.Tests`), `bin-c718a/`
(`tests/Antiphon.Tests`) and `bin-c718s/` (`src/Antiphon.SessionRunner`, CP-4), forward slash, one
build each. `Min` is the exact executed count at `bafc3366` plus this card's new methods (no
allowance: every row but CP-7 names whole classes whose roster is known). Class-level OR filters
only (CARD-0403 syntax); method-level OR is never used. `Antiphon.Tests` and
`Antiphon.Agents.Pty.Tests` are never co-scheduled. TUnit rows run through
`scripts/run-checkpoint.ps1` (it takes the build slot and adds `UseAppHost=false` off Windows);
every non-TUnit command runs under `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <label> -- <command>`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c718r/` | host-stats-runner | `/*/*/(HostStatsStoreTests*)\|(HostStatsProbeParseTests*)\|(HostStatsSamplerTests*)\|(HostStatsEndpointTests*)/*` | V-1, V-2, V-3, V-4 | all listed (9 + 10 + 5 + 4), 0 failed | 28 | 9 |
| CP-2 | S1 | CP-1 | runner-adjacent | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(RunnerCapabilitiesTests*)\|(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)\|(PhoneHomeConnectionServiceTests*)/*` | V-5, R-1 | all listed (37 + 5 + 11 + 4 + 10), 0 failed | 67 | 5 |
| CP-3 | S1 | CP-1 | probe-live | `/*/*/HostStatsProbeLiveTests/*` | V-13 | this OS's method passed, the other `Skip.Test` with its reason (skipped 1), 0 failed | 1 | 2 |
| CP-4 | S1 | `src/Antiphon.SessionRunner -> bin-c718s/` via `build-slot.ps1 -Label c718-overhead-build -- dotnet build src/Antiphon.SessionRunner --property:OutputPath=bin-c718s/` | overhead | step 0: `pwsh -NoProfile -File scripts/measure-host-stats-overhead.ps1 -RunnerDll src/Antiphon.SessionRunner/bin-c718s/Antiphon.SessionRunner.dll -DryRun`; step 1: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c718-overhead -- pwsh -NoProfile -File scripts/measure-host-stats-overhead.ps1 -RunnerDll src/Antiphon.SessionRunner/bin-c718s/Antiphon.SessionRunner.dll -WarmupSeconds 15 -Seconds 60 -Pairs 2` | V-14 | step 0 exit 0 (no scrubbed key name, isolation args present); step 1 exit 0 with an `OVERHEAD` line, delta < 1.0, sanity lines `on-samples>=12` and `off-status=404` | n/a | 9 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c718a/` | host-stats-server | `/*/*/(HostStatsCacheTests*)\|(HostStatsAntiphonCountersTests*)\|(HostStatsPollServiceTests*)\|(HostStatsEndpointTests*)\|(SessionRunnerHttpClientHostStatsTests*)/*` | V-6, V-7, V-8, V-9, V-12 | all listed (7 + 3 + 6 + 5 + 3), 0 failed | 24 | 12 |
| CP-6 | S2 | CP-5 | server-adjacent | `/*/*/(PhoneHomeDirectoryTests*)\|(RunnerCatalogueTests*)\|(RunnerSlotEndpointTests*)\|(PhoneHomeEventPumpTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerCapabilityGateTests*)\|(HttpResilienceRegistrationTests*)/*` | R-2 | all listed (7 + 4 + 8 + 8 + 5 + 2 + 7), 0 failed | 41 | 8 |
| CP-7 | S2 | CP-5 | unit-lane | `/*/*/*/*[Category=Unit]` | R-3 | >= 2900 executed, 0 failed (last measured 2993 executed at `c18a6c67`, + 12 new); a failure that also fails at `bafc3366` is reported pre-existing | 2900 | 5 |
| CP-8 | S3 | n/a | client-hosts | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c718-client -- pwsh -File scripts/test-client.ps1 hosts` | V-10 | `CLIENT TESTS EXIT CODE: 0`, 4 files, 12 tests passed | n/a | 3 |
| CP-9 | S3 | n/a | client-full-lint | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c718-client-full -- pwsh -File scripts/test-client.ps1` then `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c718-lint -- npm --prefix client run lint` | R-4 | `CLIENT TESTS EXIT CODE: 0` over 110 files; lint 0 errors 0 warnings | n/a | 7 |
| CP-10 | S4 | n/a | docs-named | `git grep -n -e "/api/hosts/stats" -e "HostStatsUpdated" -e "hostStatsV1" -e "SessionRunner:HostStats" -e "HostStats:PollIntervalMs" -e "host-stats" -- docs/ops-http.md docs/antiphon-api.md docs/resilience.md docs/testing-and-build.md` | V-11 | >= 8 matching lines, each of the 4 files at least once, exit 0 | n/a | 1 |
| CP-11 | landed and activated | n/a | live-hosts | `curl -sS $ANTIPHON_API/api/version`, then `curl -sS $ANTIPHON_API/api/hosts/stats` twice 10 s apart, then `node scripts/host-stats-hub-receipt.mjs --api $ANTIPHON_API --count 2 --timeout-seconds 20` | V-15 | version SHA = landed commit; two entries both `live`, `observedAt` advanced >= 5 s, server2 `load1` non-null, desktop `load1` null and `cpuPercent` non-null; the receipt prints `RECEIPT 2` with both host ids | n/a | 3 |

The pipe characters inside `Filter` cells are table escapes; the command line uses a plain `|`,
quoted as [docs/testing-and-build.md](../../testing-and-build.md#combined-class-filters-card-0403)
shows. Run each TUnit row as `pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-n -Project
<project> -OutputPath <bin-c718x/> -Filter '<filter>' -MinExecuted <Min> -Expect <classes, comma
separated> -ResultsRoot .antiphon/c718-checkpoints` (`-NoBuild` for the reuse rows CP-2, CP-3,
CP-6, CP-7). CP-4, CP-8, CP-9, CP-10 and CP-11 are non-TUnit rows reported in the `CHECKPOINT`
line shape by hand with their own assertion. CP-7 is the only lane row (`>= N executed`).

**CP-3 per-OS idiom.** `HostStatsProbeLiveTests` has one `[Test]` per OS; each opens with the
`Skip.Test(...)` guard of V-13, so on server2 the Linux method executes and the Windows one is
reported skipped with "Windows GetSystemTimes/GlobalMemoryStatusEx probe; this lane is Linux", and
on the desktop the reverse. The same command runs on either lane. The Linux method compares
`MemoryTotalBytes` with `MemTotal` it reads itself from `/proc/meminfo`; the Windows method
checks the P/Invoke answers' ranges (no second source exists without WMI).

**CP-4 procedure** (`scripts/measure-host-stats-overhead.ps1`, ASCII, written in S1; parameters
`-RunnerDll`, `-WarmupSeconds`, `-Seconds`, `-Pairs`, `-DryRun`). For each instance, in the order
off, on, off, on (`-Pairs 2`), it:

1. creates a fresh temp root under `.antiphon/c718-checkpoints/CP-4/<n>/`;
2. builds a `ProcessStartInfo("dotnet")` with the dll and `--urls http://127.0.0.1:0
   --PhoneHome:Enabled false --SessionRunner:SessionLogPath <root> --SessionRunner:Herdr:Enabled
   false --SessionRunner:CpuWatchdogEnabled false --SessionRunner:HostStats:Enabled <true|false>
   --SessionRunner:HostStats:Volumes:0 <root> --Serilog:LogPath <root>/logs`, and **removes every
   inherited environment key starting `PhoneHome__`, `SessionRunner__` or `ASPNETCORE_`** (the
   server2 container sets `PhoneHome__Enabled`, `PhoneHome__RunnerId`, `PhoneHome__ServerOrigin`,
   `SessionRunner__SessionLogPath` and `ASPNETCORE_URLS`; a measurement runner inheriting them
   would phone home as server2). `-DryRun` prints `ARG <arg>` and `ENVKEY <name>` lines (names,
   never values) and exits; step 0 passes when no `ENVKEY` has a scrubbed prefix and the isolation
   args are present (G-77);
3. starts it, reads `Now listening on: http://127.0.0.1:<port>` from stdout, kills it and exits 2
   if the port is 17204 or 8080;
4. waits `-WarmupSeconds`, then reads `Process.TotalProcessorTime` (OS accounting on both
   platforms) and a `Stopwatch`, waits `-Seconds`, reads both again: `cpu% = ΔCPU / Δwall × 100`
   (of one core);
5. sanity: the on-instance's `GET /host-stats/series?metric=cpu&window=5m` has >= 12 points
   (prints `on-samples=<n>`); the off-instance's `GET /host-stats` is 404 (`off-status=404`);
6. kills the process tree, waits for exit, deletes the temp root.

It prints `OVERHEAD off=<a>,<b> on=<c>,<d> delta=<mean(on) − mean(off)> host=<machine> os=<os>
cores=<n> utc=<date>` and exits 0 when delta < 1.0 and both sanity lines hold, 1 when delta >= 1.0,
2 on a setup failure. It measures the process from outside, so it replaces D-10's draft (which
read the sampler's own `cpuPercent` and a diagnostics-style delta) with one method on both OSes.
The numbers go into `docs/testing-and-build.md`'s Host stats subsection with host and date (S4).

**CP-11 receipt** (`scripts/host-stats-hub-receipt.mjs`, written in S4, about 30 lines): resolves
`@microsoft/signalr` through `createRequire` on `client/package.json`, connects to
`<api>/hubs/antiphon`, invokes `JoinGroup('hosts')`, collects `--count` `HostStatsUpdated`
payloads within `--timeout-seconds`, prints `RECEIPT <n>` and one `HOST <id> <state> <observedAt>`
line per entry of the last payload, and exits 0 only when the count is reached. CP-11 runs after
land and activation (`GET /api/version` SHA equal to the landed commit; the local runner and
server2 both restarted onto the new binary — server2 through its deploy, which is the only way it
gets a new runner); it is run by whoever confirms activation, not by the Code stage.

### Cost

All figures estimated (no build or run was timed by TestDesign; the only measured inputs are
CP-7's 2026-09-10 Unit lane, about 70 s for 1,992 cases, and the plan's 0.143 ms sample cost).

- **Ordinary V/R floor (Code)** = sum of `EstimatedMinutes` = 9 + 5 + 2 + 9 + 12 + 8 + 5 + 3 + 7
  + 1 = **61 minutes** for CP-1..CP-10, builds included (two test-project builds, one runner build),
  plus slot waits. CP-11 (3 min) is post-land activation work, not Code's. Authoring Round 1 stays
  the plan's estimate, about 11 h (S1 now 27 runner tests plus the overhead script, S2 23 server
  tests plus MS-5/MS-6 helpers, S3 12 client tests, S4 docs and the receipt script).
- **PC floor (Mutation)**, 77 controls, method-scoped red/restore/green, batched only across
  different files and methods, so the round count is the largest per-file PC count:
  - runner (PC-1..PC-37, `HostStatsStore.cs` holds 13): 13 rounds x (2 incremental
    `Antiphon.SessionRunner.Tests` builds ~1.5 min + 2 single-method runs ~0.3 min) = **~47 min**;
  - server (PC-38..PC-67, `HostStatsCache.cs` holds 10): 10 rounds x (2 `Antiphon.Tests` builds
    ~4 min + 2 runs ~0.5 min, Postgres-backed for V-9) = **~90 min**;
  - client (PC-68..PC-76, `useHostStatsLive.ts` holds 4): 4 rounds x ~1.4 min = **~6 min**;
  - PC-77 (dry run only): **~1 min**;
  - setup (first build of both test projects, `client/node_modules` present): **~8 min**.
  PC total **~152 min**. Unbatched it would be 37 x 3.6 + 30 x 9 + 9 x 1.4 + 1 ≈ 417 min, so the
  file-disjoint batching saves about 265 min; a SourceLanding Mutation may not shard, so no further
  saving is assumed.
- **Total** = Code ordinary 61 min + ~11 h authoring; Mutation ~152 min; CP-11 3 min post-land.
  Versus the draft's 65 min (which counted its live row): 61 + 3 = 64 min here, with Round 2's
  17 min moved to its own TestDesign; the PC floor is new.

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
