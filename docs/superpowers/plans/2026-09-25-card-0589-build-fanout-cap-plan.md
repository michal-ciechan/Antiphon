# CARD-0589: a host-level budget for concurrent build/test fan-out, and how a delegate waits for it

Plan task `e730ae22` (Plan, Frontier), written on the server2 Linux runner against `origin/master`
at `e8874bfb` (worktree HEAD is identical). Card revision 3, `InProgress`, importance High.

Related plans: [CARD-0654 dynamic host budgets](2026-09-24-card-0654-dynamic-host-budgets-plan.md)
(session seats per host; plan only, no code on any branch), CARD-0674 (Backlog: weights,
fill-then-overflow, load-aware admission), [CARD-0660 Codex on server2](2026-09-24-card-0660-codex-on-server2-plan.md),
[CARD-0585 checkpoint manifest](2026-09-20-card-0585-checkpoint-manifest-plan.md).

## Summary

The card's premise holds on both hosts: nothing bounds how many `dotnet build` / `dotnet run --project
tests/*` drivers, MSBuild worker nodes, compilers and test hosts run at once across the sessions on a
machine. Every existing limit counts sessions or tasks (`Delegation:MaxConcurrentTasks`, per-role
`RecommendedInFlight`, `PhoneHome:Capacity`), or bounds one process (`ParallelLimiter<ProcessSpawnLimit>`
inside one test host, the per-session Job Object memory cap that the Linux runner refuses anyway).
`run-checkpoint.ps1` removes rebuilds but sets no MSBuild switch and waits for nothing. The
`MSBUILDDISABLENODEREUSE=1` / `UseSharedCompilation=false` guidance exists only as a sentence in
SourceLanding briefs.

The plan adds three layers, cheapest first:

1. **Per-build shape, mechanical (S1):** a repo-root `Directory.Build.rsp` with `-nodeReuse:false`,
   so no invoker on either host leaves MSBuild worker nodes behind. (`-maxcpucount:N` cannot live
   there: the SDK's own bare `-maxcpucount` outranks an auto-response file, see D-2.)
2. **Host build-slot budget, brokered by the session runner (S2–S4):** the one standing Antiphon
   process on each host (desktop `:17204`, server2 container `:8080`) grants a bounded number of
   build/test leases, each carrying the per-build `-maxcpucount` to apply, refuses below a live
   free-memory floor, and reaps leases whose holder process died. `run-checkpoint.ps1` and a new
   `scripts/build-slot.ps1` wrapper take a lease, wait visibly in FIFO order, and release it. The
   desktop land verifier takes a lease too.
3. **Runaway-build watchdog and operator surface (Round 2, S6–S8):** detect-only by default, kill
   opt-in; phone-home read/set of the budget; nightly participation.

Sessions stay uncapped by this card (that is CARD-0654's seat budget); this card caps the builds
those sessions run. The two compose: seats first, then slots, then the memory floor (D-9).

## Ground truth

Read at `e8874bfb` on 2026-09-25. Measurements were taken from inside the server2 runner container
(`docker` cgroup v1, `10ffe8593c0e`) at ~05:00 UTC while four seats were occupied.

| Card / brief assumption | What the code and the host show | Consequence |
|---|---|---|
| "Nothing today caps concurrent Codex build/test process fan-out across simultaneous delegate dispatches." | Confirmed. Caps that exist count sessions or tasks, never build processes: `Delegation:MaxConcurrentTasks` (6 in code, 7 live on the pipeline snapshot: `maxConcurrentTasks: 7, inFlightAgainstCap: 6`), `MaxOpenTasks` 3 per project and `RecommendedInFlight = 1` per role at create time (`DelegationSettings.cs:22,37,332-350`, `DelegationOpenGate.cs:132`), `PhoneHome:Capacity` 10 on server2 (`docker-compose.server2-runner.yml:63`, gate `PhoneHomeCommandDispatcher.cs:360`). | The budget is a new axis (build drivers per host), not a change to any of these. |
| "today's concurrency limits are per AgentTaskRole, not per actual OS-process/memory footprint" | `RolePolicy[*].RecommendedInFlight` (`DelegationSettings.cs:332-350`) and the 409 `concurrency_limit` `axis: role|absolute` (`ConcurrencyLimitException.cs`). No code reads process counts or memory anywhere on the admission path. | D-1, D-5. |
| Brief: "how Codex delegates launch dotnet builds and tests" | They type `dotnet build` / `dotnet run --project tests/...` (or `pwsh -File scripts/run-checkpoint.ps1`) into their own shell. The launch env is server-composed (`AgentRegistry.Resolve` merge order `AgentRegistry.cs:127-160`; delegates add `AgentTaskDispatcher.BuildEnv` `:5314-5333`; runner-bound launches are projected by `PhoneHomeLaunchPolicy.Project` `:199-222`) and the pty child inherits the pty host's environment plus that overlay (`ModernConPtyConnection.cs:518`). Every process under a session shares the session's Windows kill-on-close Job Object (`ModernConPtyConnection.cs:249, :727-745`). No build wrapper, no MSBuild switch, no env var touches fan-out. Codex definition: `codex.cmd --no-alt-screen --dangerously-bypass-approvals-and-sandbox` (`server/appsettings.json:84-88`). | The gate must be something a delegate *runs* (script) plus something that holds without cooperation (rsp, watchdog). |
| Brief: "what already limits process spawns: `ParallelLimiter<ProcessSpawnLimit>`" | One limiter per test assembly, `Limit => 1` (`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs`), applied to 297 classes across five test projects. It serialises child spawns **inside one test host process** and "cannot cap a concurrent Antiphon.Agents.Pty.Tests run" (its own doc comment). It never sees another test host, a build, or another session. | Unchanged; it is not a host mechanism. |
| Brief: "`run-checkpoint.ps1`" | Builds once per row into `bin-<x>/` and runs one filter (`scripts/run-checkpoint.ps1:98-112`). It sets no MSBuild switch, no env, applies no `-maxcpucount`, and never waits. The CARD-0585 manifest reduced rebuild *count* (CARD-0490 ran 19 builds for 8 runs) but each row still fans out to `Environment.ProcessorCount` nodes. Its tests are the offline harness `scripts/test-run-checkpoint.ps1` (dotnet shim, `C585_*` seams) driven by `RunCheckpointScriptTests` (15 methods). | S3 extends the script and the harness; the same shim pattern covers the slot call. |
| Brief: "the MSBuild node reuse settings" | There are none. `Directory.Build.props` only excludes `bin-*/**` and stamps the SHA; `Directory.Build.targets` only trims the file-writes ledger (CARD-0222); no `Directory.Build.rsp` exists; no `MSBUILD*` variable is set by the AppHost (`Antiphon.AppHost/Program.cs`), `run-daemon.ps1`, the runner image (`docker/session-runner-grok/Dockerfile`), `dind-entrypoint.sh` or the compose file. The only mention is the SourceLanding brief sentence "Keep MSBUILDDISABLENODEREUSE=1 and UseSharedCompilation=false" (`DelegationReportFormatter.cs:180`), which no ordinary Code delegate receives. The nightly runs `dotnet build Antiphon.sln -c Debug --nologo` (`scripts/lib/nightly-tests-impl.ps1:385`) and the desktop land verifier runs `dotnet build --artifacts-path <tmp>` then `dotnet run --project tests/Antiphon.Tests` (`AgentTaskLandService.cs:1160-1172`), both with default parallelism and node reuse. | S1 (rsp), S4 (land verifier), S8 (nightly). |
| Brief: "whether the same exposure now exists on the server2 Linux runner (capacity 10, 24 cores)" | Yes, same mechanics, larger headroom. Measured: `nproc` 24; `/proc/meminfo` MemTotal 132,014,068 kB, MemAvailable 97,547,772 kB, swap 542 MB; cgroup memory limit `9223372036854771712` (none), `cpu.cfs_quota_us -1`, `pids.max max`; container `memory.usage_in_bytes` 22.3 GB, `max_usage` 31.5 GB. **Seventeen orphaned MSBuild worker nodes** (`MSBuild.dll /nodemode:1 /nodeReuse:true`, PPID 1, ages 5,822–11,793 s, RSS 220–330 MB each, ≈ 4.7 GB) plus one `VBCSCompiler` (1.39 GB, 11,778 s) were resident, every one carrying the environment of Codex task `611187dd` (session `b5fc55eb`). One Code task was mid-checkpoint: `Antiphon.Tests` driver 740 MB RSS plus ten nested-stack Postgres backends. 4/10 seats occupied. `dotnet` 10.0.401 SDK is in the image. The runner listens on `http://+:8080` inside the container and every session runs in that container as uid 1654. | The Linux runner gets the same broker (S2) with a larger budget (D-4). The rsp alone would already have removed the 6 GB of idle nodes and compiler. |
| Card: "Must budget the ~3.9 GB always-on standing floor as unavailable headroom, not assume a clean machine." | Nothing measures host memory today; the only memory primitive is the per-session `AgentSessions:MemoryLimitMb` Job Object cap (`WindowsJobObject.AssignMemoryLimitedJob`, default 0 = off, `MemoryKilled` exit reason) which `PhoneHomeCommandDispatcher.RejectUnsupportedLaunch` refuses on the runner (`:550`, "Memory limit must be zero"). | D-5: the broker reads live free memory and refuses below a floor, so the always-on floor is measured, not assumed. `MemoryLimitMb` is a rejected alternative (D-1). |
| Card candidate 3: a runaway/zombie build watchdog. | The runner already has `SessionCpuWatchdogService` (kills a *session* only when its transcript proves idle and CPU stays hot, triple-gated) with `IProcessCpuProbe` / `IProcessLivenessProbe` seams and PID-reuse tolerance (`ProcessCpuProbe.cs`), and the server has a daily WMI `ZombieCensusJob` (`WindowsZombieProcessCensus`, `Win32_Process` with `WorkingSetSize`, `KernelModeTime`, `UserModeTime`) that classifies agent-shaped processes and never touches build processes. AGENTS.md: "A stall is a detection/decision state, never an automatic kill." | Round 2 S6 reuses the probe seams; detect-only default, kill opt-in (D-7). |
| Card candidates 2 and 4 (Linux offload; retiring idle standing sessions). | Offload is CARD-0590 (Review) and the CARD-0659/0660 default-runner work; idle-session retirement is out of scope here and not tracked by this card's ask. | Not in this plan; named under Follow-ups. |
| CARD-0654 already gives hosts a budget. | Plan only (`3010e091`), `HostBudget` does not exist in `server/`; its D-10 named load-aware admission out of scope, CARD-0674 tracks it. Its reserved identifiers: `PhoneHomeOperation.SetCapacity = 26`, incident kinds 76/77. | This plan uses `27/28` and incident kinds `78/79` so the two can land in either order (D-9). |
| Runtime facts the gate relies on. | Runner minimal-API endpoints live in `src/Antiphon.SessionRunner/Program.cs` (`/capabilities` `:202`, `/sessions` `:225`); the server talks to the desktop runner through `ISessionRunnerClient` / `SessionRunnerHttpClient` on `SessionRunner:BaseUrl` `http://localhost:17204`; runner hosted services are registered at `Program.cs:45-61`; settings bind `SessionRunnerSettings` / `PhoneHomeSettings` with validators. Desktop daemons are started by `run-daemon.ps1`, which sets a small per-daemon env block (`:168-172`) and rebuilds before each relaunch. | Places S2 and S4 name. |

Expected-but-unmeasured, to be settled by CP-1 rather than assumed: MSBuild's XMake merges auto-response
switches (`MSBuild.rsp`, `Directory.Build.rsp`) at the **lowest** precedence and the SDK's
`dotnet build|run|test` forwarders always prepend a bare `-maxcpucount` (all cores), so a
`-maxcpucount:N` in `Directory.Build.rsp` is expected to be ignored while `-nodeReuse:false` and
`-p:` properties there take effect (nothing on the SDK's line competes with them). CP-1 measures both
claims on the host that runs Code; if `-m:N` in the rsp turns out to be honoured, S1 adds it and
D-2's per-wrapper `-maxcpucount` becomes belt rather than the only cap.

## Decisions

Stated defaults for what the card and brief left open. None needs a fresh approval under the caller's
standing authority ("Can we keep picking up cards throughout the night"): everything here is
reversible configuration and scripts, and nothing kills by default.

### D-1. The budget unit is a build/test **driver lease**, brokered by the session runner on each host

One lease = one `dotnet build`, `dotnet run --project tests/*` (with or without `--no-build`),
`dotnet test` or `dotnet publish` driver invocation, held from just before the driver starts until it
exits. The session runner is the broker: it is the one standing Antiphon process on both hosts
(desktop `Antiphon.SessionRunner.exe` on `:17204`; server2 container runner on `:8080`), it already
owns per-session process custody and the liveness/CPU probes needed to reap a dead holder, and it is
where CARD-0653/0654 put the other host-level surfaces (`/sessions`, seats, capacity).

Rejected:

- **Dispatcher-level admission** (hold the *task* until the host has build headroom): wrong
  granularity. A session builds zero or twenty times over hours; holding a seat for its whole life
  wastes the seats CARD-0654 budgets, and admitting it says nothing about when it builds.
- **Per-session `AgentSessions:MemoryLimitMb`** (Job Object cap): per-session, so ten sessions still
  exceed the host; unsupported on the Linux runner (`Memory limit must be zero`); and it fails the
  *agent* (`MemoryKilled`) instead of making the build wait. Kept as a possible later Windows belt.
- **File-lease directory** (`mkdir` mutex + lease files under `C:\Antiphon\build-slots` /
  `/work/.antiphon/build-slots`): no runtime dependency, but it needs the same PID-liveness and
  PID-reuse logic twice (PowerShell for delegates, C# for the land verifier), has no authority to
  reap, and no natural place to report occupancy. The runner has all three already.
- **OS semaphores**: .NET named `Semaphore` is Windows-only; a Linux POSIX semaphore has no
  PowerShell client. Not cross-platform.
- **Counting processes or memory per session and refusing launches**: racy (nodes appear seconds
  after the driver), and a launch refusal punishes the next session for the current one's build.

### D-2. Per-build shape: `Directory.Build.rsp` carries `-nodeReuse:false`; the per-build `-maxcpucount` comes from the grant

`Directory.Build.rsp` at the repo root (ASCII, one switch per line):

```
-nodeReuse:false
```

MSBuild reads it for every project under the root on both hosts, for every invoker (a delegate's
raw command, the wrappers, the land verifier, the nightly, `run-daemon.ps1`'s rebuild), so worker
nodes exit with the build that made them. Cost: a few seconds of node start-up per build. This alone
would have removed the 4.7 GB of idle nodes measured on server2 and the 161 nodes in the outage's
census.

`-maxcpucount:N` is **not** put in the rsp (expected to be outranked by the SDK's bare
`-maxcpucount`; CP-1 measures). Instead every grant carries `maxCpuCount` and the wrappers insert
`-maxcpucount:N` into the driver's own command line (after the SDK's default, so it wins), for
`dotnet build|test|publish` and for `dotnet run` without `--no-build`. An unleased run (D-6) uses
`-maxcpucount:4`.

`UseSharedCompilation` is left at its default (**on**). Concurrent compilations inside one
`VBCSCompiler` share metadata and peak lower than the same number of in-process `csc`; the outage's
"18 csc holding 11.5 GB" is the in-process shape. The server idles out after ten minutes (1.39 GB
measured here) which the budget tolerates. SourceLanding briefs keep their own
`UseSharedCompilation=false` for custody reasons, unchanged.

Rejected: `BuildInParallel=false` via env (a real fan-out cap for raw commands, but it serialises
project references and roughly doubles build time for every invoker, including the land verifier);
`MSBUILDDISABLENODEREUSE=1` injected into every launch env (equivalent to the rsp for sessions but
misses the land verifier, nightly and operator shells; the rsp covers all of them from one file).

### D-3. The broker's contract

Runner routes (minimal API in `Program.cs`, same style as `/sessions`), contracts in
`Antiphon.SessionRunner.Contracts` (`BuildSlotContracts.cs`):

| Route | Body / answer |
|---|---|
| `POST /build-slots` | body `BuildSlotRequest { pid, processStartUtc, label, sessionId?, taskId? }` → 200 `BuildSlotGrant { leaseId, maxCpuCount, occupied, budget, expiresAtUtc }`; 409 problem `build_slot_busy` with `{ occupied, budget, queuePosition, retryAfterMs }`; 409 `build_slot_memory_floor` with `{ availableMb, floorMb, retryAfterMs }`; 400 `build_slot_invalid` (pid ≤ 0, blank label). When `Enabled=false`: 200 `{ leaseId: null, unlimited: true, maxCpuCount }`. |
| `DELETE /build-slots/{leaseId}` | 204; 404 `build_slot_unknown`. |
| `GET /build-slots` | `BuildSlotListing { enabled, budget, maxCpuCount, occupied, memory { availableMb, floorMb }, leases[] { leaseId, pid, label, sessionId, taskId, grantedAtUtc, expiresAtUtc, holderAlive }, waiters[] { pid, label, sinceUtc, position } }`. |

`BuildSlotBroker` (singleton, concrete class) keeps leases and a FIFO waiter list in memory under
one lock. A grant is issued only to the waiter at the head of the queue (or to a newcomer when the
queue is empty), so a long waiter is never overtaken; a waiter that has not re-polled for
`WaiterSilenceMs` (60 s) is dropped. The sweep (every `SweepIntervalMs`, 30 s, and on every
acquire) reaps a lease whose holder pid is gone or recycled (`IProcessLivenessProbe` with the
existing 2-minute start-time tolerance) and any lease past `LeaseTtlMinutes` (90, above the 25.5-min
full `Antiphon.Tests` run and the 21-min pathological build of CARD-0222), logging each reap with
its label. Nothing is killed.

Settings `SessionRunner:BuildSlots` (`BuildSlotSettings`, validated like `PhoneHomeSettings`):
`Enabled` (true), `MaxConcurrent`, `MaxCpuCount`, `MinAvailableMemoryMb`, `LeaseTtlMinutes` (90),
`RetryAfterMs` (15000), `WaiterSilenceMs` (60000), `SweepIntervalMs` (30000).

Rejected: long-poll (`POST` that blocks until granted) — ties a runner request to a 45-minute wait
and dies with every runner restart; heartbeat-renewed leases — pid liveness already answers "is the
holder alive" without asking the wrapper to run a background job.

### D-4. Budget defaults per host, and why

| Host | `MaxConcurrent` | `MaxCpuCount` | `MinAvailableMemoryMb` | Where set |
|---|---:|---:|---:|---|
| desktop (code default) | 2 | 4 | 6144 | `BuildSlotSettings` defaults |
| server2 | 4 | 6 | 16384 | `docker-compose.server2-runner.yml` `SessionRunner__BuildSlots__*` |

Sizing from the card's numbers. One leased row on the desktop peaks at roughly 4 nodes × 0.3 GB +
compilations ≈ 0.6 GB each (11.5 GB / 18 csc in the census) + a 0.7–1 GB test host + its Postgres
container: about 5–6 GB. Desktop headroom = 31.9 GB − 3.9 GB always-on floor − ~7 GB for seven
in-flight sessions' own agents and nodes − ~6 GB OS/AppHost/server/browser ≈ 15 GB, so two slots
(≈ 12 GB) with margin; the memory floor catches the day the floor is bigger. server2: 97 GB
available with no cgroup cap and other services on the host; four slots at six nodes each stay under
30 GB with a 16 GB floor. Both are ordinary settings an operator raises or lowers without code
(`SessionRunner__BuildSlots__MaxConcurrent`), and Round 2 makes them settable at runtime (S7).

### D-5. Live memory floor, not an assumed one

`IHostMemoryProbe.AvailableBytes` (Windows: `GlobalMemoryStatusEx` `ullAvailPhys`; Linux:
`/proc/meminfo` `MemAvailable`, which in this container is the host's figure, exactly the number
that matters). A grant is refused with `build_slot_memory_floor` while available memory is below
`MinAvailableMemoryMb`, even with free slots. The floor is checked at grant time only; a lease is
never revoked for memory (D-3: nothing is killed). This is how "budget the ~3.9 GB standing floor"
is honoured: whatever the standing sessions hold, the floor is measured at the moment a build asks.

Rejected: a static "reserved for always-on" number (the card's 3.9 GB was 23.2 GB three hours
earlier); per-lease memory reservations (a build's peak is not knowable in advance).

### D-6. How a delegate waits, and the two failure modes

`scripts/lib/build-slot.ps1` exports `Enter-AntiphonBuildSlot` / `Exit-AntiphonBuildSlot`.
`run-checkpoint.ps1` calls them around its build+run (every row, including `-NoBuild`: the test host
and its Postgres are the memory), and `scripts/build-slot.ps1 -Label <what> -- <command...>` wraps
any other build/test driver (Mutation shards, ad hoc builds, `dotnet run --project` with an implicit
build). Endpoint: `$env:ANTIPHON_BUILD_SLOTS_URL`, else `http://localhost:17204/build-slots` on
Windows and `http://127.0.0.1:8080/build-slots` on Linux (the production runners; an isolated runner
sets the variable). Body: the wrapper's own pid and start time, the label, `ANTIPHON_SESSION_ID` /
`ANTIPHON_TASK_ID` when present.

Waiting: poll every `retryAfterMs`; print `BUILD SLOT waiting label=<l> position=<n>
occupied=<o>/<b> elapsed=<m>m` once a minute so the transcript shows the wait (and the CPU watchdog
sees an active turn); on grant print `BUILD SLOT granted lease=<id> waited=<s>s maxcpucount=<n>`;
release in `finally` and print `BUILD SLOT released lease=<id> held=<s>s`. `-SlotWaitMinutes`
default 45.

- **Timeout** (busy or memory floor for the whole wait): exit **4** (new code, `BUILD SLOT timeout
  after 45m position=<n>`), nothing built, no dotnet call. A budget that yields under pressure is not
  a budget; the delegate reports the row as not run and either retries it later or ends `blocked`.
- **Unreachable broker** (connection refused, timeout, or an old runner answering 404 on
  `/build-slots`): after 60 s of retries, continue **unleased** with `BUILD SLOT unleased
  reason=runner_unreachable` and the fallback `-maxcpucount:4`. A delegate stalled behind a
  restarting runner is the CARD-0448 data-loss shape, and the outage needs many concurrent builds,
  which nothing can dispatch while the runner is down. The line is visible in the report for Review.

The lease is per wrapper process, released on exit and reaped on death, so an agent cannot hold a
slot between turns. Offline seams for the harness: `C589_SLOT_SHIM` (a script standing in for the
HTTP call, answering `granted|busy|memory_floor|unreachable` per `C589_SLOT_SCRIPT`),
`C589_SLOT_WAIT_SECONDS`, `C589_SLOT_RETRY_MS`.

Rejected: fail-closed on an unreachable broker (see above); silent unleased fallback (the line is
what lets Review tell a budgeted run from an unbudgeted one).

### D-7. Watchdog: detect by default, kill by opt-in (Round 2)

`BuildProcessWatchdogService` (runner) every 60 s enumerates build-shaped processes (MSBuild
`/nodemode:1` nodes, `VBCSCompiler`, `csc`, `dotnet exec *Tests.dll`, `dotnet run --project tests/*`,
`dotnet build`) whose ancestry reaches a session child this runner owns (Windows: WMI
`ParentProcessId` chain, as `reap-zombie-agents.ps1` I1; Linux: `/proc/<pid>/stat` ppid chain) and
scores efficiency = CPU time / wall time via `IProcessCpuProbe`. A process older than
`MinAgeMinutes` (30) with efficiency below `MinEfficiencyPercent` (10; the outage's oldest csc was
5.7 %) and RSS above `MinRssMb` (256) is reported once per process as a runner event
`BuildProcessStalled` (desktop: incident `AgentIncidentKind.BuildProcessStalled = 78`, Warning, on
the session's agent; attention via the existing `RecentCriticalIncident` row). With
`SessionRunner:BuildWatchdog:KillEnabled=true` (default **false**) the watchdog kills the *driver*
root of that tree (never a node alone, never the agent, never anything outside a session it owns)
and records `BuildProcessKilled = 79`; the delegate sees its build exit non-zero and its lease is
reaped by pid death. Nightly and land-verifier builds are outside sessions and never candidates.

Rejected for Round 1: any automatic kill (AGENTS.md's stall rule; the card's recovery was a
human-authorised kill with a never-kill list, which the ancestry guard reproduces but the default
should stay a decision).

### D-8. Cooperation is required of delegates and enforced by Review, not by the OS

A raw `dotnet build` / `dotnet run --project` typed outside the gate still runs (with `-nodeReuse:false`
from the rsp). `delegate-basics` gains the standing rule (S5), `stage-code` reports slot lines per
row, and `stage-review` names "a build or test driver outside the slot gate" as a checkpoint defect,
the same shape as an unlisted run. The watchdog (D-7) and the memory floor (D-5) are the backstops
for a delegate that does not comply.

Rejected: a PreToolUse hook or a `dotnet` shim on PATH (only works for one agent kind, only in
worktrees, and CARD-0063 already rejected hook-based enforcement for the same reasons).

### D-9. Composition with CARD-0654 / CARD-0674 and identifier reservations

Seats (sessions per host, 0654) are decided by the desktop dispatcher; slots (build drivers per host,
this card) by the runner at build time; the memory floor last. A host with free seats can still
queue builds, and a free slot never admits a session. `GET /build-slots` is the runner-local view
in Round 1; Round 2 adds `PhoneHomeOperation.GetBuildSlots = 27` and `SetBuildBudget = 28`
(persisted on the runner at `PhoneHome:BuildBudgetStatePath`, default `/state/build-budget`,
mirroring 0654 D-3's precedence: persisted file over env), the desktop route
`GET|PUT /api/session-runners/{runnerId}/build-slots`, and `GET /api/session-runners/local/build-slots`
for the desktop runner. 0654's `hosts[]` pipeline block, when it lands, gains `buildSlots` from these.
Incident kinds 78/79, operations 27/28 are reserved here so the two plans land in either order.

### D-10. Out of scope, named

Retiring idle standing sessions (card candidate 4) needs its own operator-authorised policy and
card. Moving suites to Linux is CARD-0590/0659/0660. E2E and the isolated runners keep their own
lanes; they do not call `:17204`. Aspire/`restart-apphost.ps1` builds are operator actions outside
the gate (they still get the rsp).

## Design

### S1 — `Directory.Build.rsp` and the fan-out measurement

Files: `Directory.Build.rsp` (new, root, ASCII, `-nodeReuse:false` and a one-line comment `#`
naming CARD-0589), `tests/Antiphon.Tests/Infrastructure/DirectoryBuildRspTests.cs` (new, Unit: the
file exists, is ASCII, contains `-nodeReuse:false`, contains no `-maxcpucount` unless CP-1 proved it
effective, in which case the test pins the measured value). `docs/testing-and-build.md` gains the
"Build slots (CARD-0589)" section (S5) that records CP-1's measurement.

CP-1 is the measurement: on the host running Code, build `tests/Antiphon.Messaging.Tests` into
`bin-c589m/` while sampling every second the MSBuild node processes (`/nodemode:1`) and any
`VBCSCompiler` started after the build began; assert zero survive ten seconds after the driver
exits; record the peak node count. Then re-run with a temporary `-maxcpucount:2` line in the rsp and
record whether the peak dropped to ≤ 2 (expected: no); the temporary line is removed before commit
and the outcome is written into the section.

### S2 — `BuildSlotBroker`, settings, memory probe, routes (runner)

Files: `src/Antiphon.SessionRunner.Contracts/BuildSlotContracts.cs` (new: `BuildSlotRequest`,
`BuildSlotGrant`, `BuildSlotBusy`, `BuildSlotMemoryFloor`, `BuildSlotListing`, `BuildSlotLease`,
`BuildSlotWaiter`, problem codes `BuildSlotProblemTypes`); `src/Antiphon.SessionRunner/BuildSlotSettings.cs`
(new, with `Validate()` like `PhoneHomeSettings`: `MaxConcurrent ≥ 1`, `MaxCpuCount ≥ 1`,
`MinAvailableMemoryMb ≥ 0`, `LeaseTtlMinutes ≥ 1`); `src/Antiphon.SessionRunner/BuildSlotBroker.cs`
(new: `TryAcquire(BuildSlotRequest, out outcome)`, `Release(Guid)`, `List()`, `Sweep()`;
constructor takes `IOptions<BuildSlotSettings>`, `IProcessLivenessProbe`, `IHostMemoryProbe`,
`TimeProvider`, `ILogger`); `src/Antiphon.SessionRunner/HostMemoryProbe.cs` (new:
`IHostMemoryProbe` + `SystemHostMemoryProbe`, Windows P/Invoke `GlobalMemoryStatusEx`, Linux
`/proc/meminfo`); `BuildSlotSweepService : BackgroundService` (new, timer → `Sweep()`);
`Program.cs`: `Configure<BuildSlotSettings>("SessionRunner:BuildSlots")` + validation, the three
routes, DI for probe/broker/sweep. `docker-compose.server2-runner.yml`: the three
`SessionRunner__BuildSlots__*` values from D-4 with a comment.

Problem answers use the runner's existing problem-details shape (`PhoneHomeAdmissionException`
style codes), so `SessionRunnerHttpClient` can map them.

### S3 — the delegate side: `scripts/lib/build-slot.ps1`, `run-checkpoint.ps1`, `scripts/build-slot.ps1`

`scripts/lib/build-slot.ps1` (new, `#requires -Version 7.0`, ASCII): `Enter-AntiphonBuildSlot
-Label -WaitMinutes -Endpoint -Pid` returning `{ LeaseId; MaxCpuCount; Unleased; Reason;
WaitedSeconds }` or throwing `BuildSlotTimeout`; `Exit-AntiphonBuildSlot -Lease`;
`Add-AntiphonMaxCpuCount -Command <string[]> -MaxCpuCount N` (inserts `-maxcpucount:N` before `--`
for `dotnet build|test|publish` and for `dotnet run` without `--no-build`, unless the command already
carries `-m`/`-maxcpucount`); the `C589_*` seams.

`scripts/run-checkpoint.ps1`: dot-source the lib; acquire after input validation and the fresh
results directory (so an invalid row never waits), before step (3); pass `-maxcpucount:N` on the
build; release in a `finally` around (3)–(4); new `-SlotWaitMinutes` (45), `-NoSlot` (documented
for an operator shell only; prints `BUILD SLOT skipped by -NoSlot`); the `CHECKPOINT` line gains
`slot=<granted|unleased|skipped> waited=<s>s`; exit 4 on slot timeout (trailer as today).

`scripts/build-slot.ps1` (new): `-Label`, `-SlotWaitMinutes`, `-NoSlot`, then `--` and the command;
acquires, applies `Add-AntiphonMaxCpuCount`, runs the command in the foreground with output on the
console, releases, exits with the command's exit code (4 on slot timeout, before running anything).

Harness: `scripts/test-run-checkpoint.ps1` gains five `Test-C589_*` cases (V-4) and a slot shim
body; `scripts/test-build-slot.ps1` (new, same C487 harness libs) with four cases (V-5).

### S4 — the desktop land verifier takes a lease

`ISessionRunnerClient` gains `AcquireBuildSlotAsync(BuildSlotRequest, ct)` → `BuildSlotGrant?`
(null when unreachable or 404) and `ReleaseBuildSlotAsync(Guid, ct)`; `SessionRunnerHttpClient`
implements them against `/build-slots`. `AgentTaskLandService.VerifyWithObserverAsync` gains an
`IBuildSlotGate` parameter (a small adapter over the client with the same wait/timeout/unleased
rules as D-6, using the land timeout budget: wait at most `Landing:BuildSlotWaitMinutes`, 30) and
passes `-maxcpucount:N` to the `dotnet build`; the observer receives the `BUILD SLOT ...` lines so
the landing evidence records them. Unreachable → unleased with the line, never a land failure.

### S5 — bundles and docs

`server/Bundles/delegate-basics.md`: after "BUILD TO AN ALTERNATE OUTPUT PATH", a new bullet
"BUILD AND TEST THROUGH THE HOST BUILD-SLOT GATE" (run-checkpoint takes a slot itself; anything else
via `scripts/build-slot.ps1 -Label <what> -- <command>`; the gate waits visibly and applies the
host's `-maxcpucount`; a raw driver outside it is an unlisted run Review flags; exit 4 is a slot
timeout to report, never to retry unleased; one sentence of why: CARD-0589's 203 processes and
1.1 GB free). `stage-code.md` CHECKPOINTS sentence: report `slot=`/`waited=` per row.
`stage-review.md`: the outside-the-gate defect. Docs: `docs/testing-and-build.md` new section
"Build slots (CARD-0589)" (contract, defaults, wait lines, exit 4, `-NoSlot`, the CP-1 measurement,
the rsp), `docs/docker-stack.md` (server2 values and how to change them), `docs/ops-http.md` (runner
`/build-slots` routes), `docs/orchestration-loop.md` (Cost must add expected slot wait; composition
with seats), AGENTS.md "Tests and builds" one trigger line. `docs/session-runtime-invariants.md` is
untouched (no session semantics change).

### Round 2 (separate Code dispatch after Round 1 lands)

- **S6 — watchdog** (D-7): `BuildProcessWatchdogService`, `BuildWatchdogSettings`
  (`Enabled` true, `KillEnabled` false, `MinAgeMinutes` 30, `MinEfficiencyPercent` 10, `MinRssMb`
  256, `IntervalMs` 60000), `IBuildProcessCensus` (Windows WMI reusing the `ZombieOsProcess`
  shape; Linux `/proc`), runner event `BuildWatchdog` → desktop incidents 78/79; the desktop maps
  the event in the runner event consumer next to `CpuSpinKilled`.
- **S7 — operator surface**: phone-home ops 27/28, `PhoneHome:BuildBudgetStatePath`, desktop
  routes (D-9), `scripts/build-slots.ps1 list|set` (pattern `runner-slots.ps1`), client panel row
  under the existing runner status card.
- **S8 — nightly**: `Invoke-NightlyOwnedProcess -BuildSlot <label>` acquires through the lib for
  `dotnet build Antiphon.sln` and each TUnit suite; a `NightlySeams.BuildSlot` seam; the nightly
  harness case.

### Code rounds

R1 = S1–S5 (one Code dispatch; the runner change and the scripts land together so the scripts'
404-as-unreachable fallback is exercised only during the deploy window). R2 = S6–S8 after R1 has
landed and both runners have been redeployed (`run-daemon.ps1` rebuilds the desktop runner on
relaunch; server2 needs `deploy-parent`).

## Migration and rollback

No database migration in R1 (runner memory only). Rollback levers, independently: delete
`Directory.Build.rsp` (node reuse returns); `SessionRunner__BuildSlots__Enabled=false` (every
acquire answers `unlimited`, wrappers print `BUILD SLOT unlimited` and apply the configured
`maxCpuCount`); `-NoSlot` on a single wrapper call (operator shells). Old runner binary + new
scripts = unleased runs with the line (D-6). New runner + old scripts = no gate, no harm. Post-land
activation check, not inferred from `/health`: `GET http://localhost:17204/build-slots` on the
desktop and `docker exec antiphon-runner-session-runner-1 curl -fsS http://127.0.0.1:8080/build-slots`
on server2 both answer 200 with `budget` equal to the configured value.

## Verification design

Coverage classes: V = new red tests that fail at `e8874bfb` for the stated reason; R = existing
classes that must stay green because a slice touches their code path.

- V-1 S2 broker semantics, `BuildSlotBrokerTests` (new, Unit, `Antiphon.SessionRunner.Tests`, fake
  liveness and memory probes, `FakeTimeProvider`): grants up to `MaxConcurrent`; busy beyond it with
  `queuePosition`/`retryAfterMs`; FIFO (a newcomer is refused while an earlier waiter is at the
  head); a silent waiter is dropped after `WaiterSilenceMs`; release frees a slot; a dead holder
  pid is reaped on the next acquire; a recycled pid (start time later than tolerance) is reaped; TTL
  expiry reaps; memory below the floor refuses `build_slot_memory_floor` even with free slots and
  admits once above; `Enabled=false` answers `unlimited` with `maxCpuCount`; every grant carries
  `MaxCpuCount`. 11 methods; red because the type does not exist.
- V-2 S2 routes, `BuildSlotEndpointTests` (new, `Antiphon.SessionRunner.Tests`, the runner test host
  pattern of `RunnerCapabilitiesTests`): POST grant shape, POST 409 problem shape and code, DELETE 204
  then 404, GET listing counts. 4 methods.
- V-3 S2 settings, `BuildSlotSettingsTests`: rejects `MaxConcurrent 0`, `MaxCpuCount 0`,
  `LeaseTtlMinutes 0`; binds from `SessionRunner:BuildSlots`. 2 methods.
- V-4 S3 `run-checkpoint.ps1`, `RunCheckpointScriptTests` new cases via `test-run-checkpoint.ps1`
  (offline, slot shim): `C589_SlotGranted` (build args carry `-maxcpucount:<grant>`, `BUILD SLOT
  granted` line, release call after the run, `slot=granted` on the CHECKPOINT line),
  `C589_SlotWaitsThenGranted` (busy twice then granted: `BUILD SLOT waiting` lines, `waited=`),
  `C589_SlotTimeout` (busy forever with `C589_SLOT_WAIT_SECONDS=1`: exit 4, no dotnet call, trailer
  `EXIT CODE: 4`), `C589_SlotUnreachable` (shim exits unreachable: `BUILD SLOT unleased
  reason=runner_unreachable`, dotnet called with `-maxcpucount:4`, exit follows the run),
  `C589_NoBuildStillLeases` (a `-NoBuild` row acquires and releases). 5 methods; red because the
  script has no slot step and prints no `BUILD SLOT` line.
- V-5 S3 `scripts/build-slot.ps1`, `BuildSlotScriptTests` (new, `Antiphon.Tests/Scripts`,
  `ScriptHarness.RunHarnessCaseAsync("test-build-slot.ps1", "C589", ...)`): runs the command under
  a lease and propagates its exit code; inserts `-maxcpucount:N` for `dotnet build` and for `dotnet
  run` without `--no-build` but not with it or when `-m` is already present; releases on a failing
  command; timeout exit 4 before running anything; unreachable fallback line. 5 methods.
- V-6 S5 bundles and docs, `InstructionBundleTests` (+1: `delegate-basics` names
  `scripts/build-slot.ps1`, `BUILD SLOT` and exit 4; `stage-review` names the outside-the-gate
  defect) and `CheckpointManifestDocumentationTests` (+2: `testing-and-build.md` has the "Build slots
  (CARD-0589)" section naming `SessionRunner:BuildSlots` and `ANTIPHON_BUILD_SLOTS_URL`; the
  `stage-code` CHECKPOINTS sentence names `slot=`). 3 methods.
- V-7 S1 `DirectoryBuildRspTests` (new, Unit): file present, ASCII, `-nodeReuse:false`; the
  `-maxcpucount` rule per CP-1's outcome. 2 methods.
- V-8 S4 land verifier, `LandVerificationBuildSlotTests` (new, `Antiphon.Tests/Application`, fake
  `ISessionRunnerClient`, existing `ILandingChildObserver` recording): a grant is acquired before
  `dotnet build` and the build args carry `-maxcpucount:<grant>`; release follows the test run and
  also follows a build failure; an unreachable client (null grant) still builds, with the
  `BUILD SLOT unleased` observer line and no land failure. 3 methods.
- V-9 S2+S3 cross-process proof, `BuildSlotEndToEndTests` (new, `Antiphon.SessionRunner.Tests`,
  `[ParallelLimiter<ProcessSpawnLimit>]`): boots the runner test host with `MaxConcurrent=1`, starts
  two `scripts/build-slot.ps1 -Label a|b -- pwsh -NoProfile -c "<append timestamp to a shared file;
  Start-Sleep 5>"` with `ANTIPHON_BUILD_SLOTS_URL` at the test host, asserts the second printed
  `BUILD SLOT waiting`, both ran, and their intervals do not overlap; a second test kills the first
  wrapper mid-hold and asserts the broker reaps the lease so the waiter is granted. 2 methods.
- R-1 runner classes adjacent to the broker and routes: `PhoneHomeCommandDispatcherTests` (34),
  `RunnerCapabilitiesTests` (4), `SessionCpuWatchdogTests` (3), `RunnerCapacityCountTests` (3).
- R-2 `AgentTaskLandVerifierTests` (6) and the land admission/boundary classes in CP-5.
- R-3 `DelegateBundleLaunchTests` (15) for the unchanged env contract; `AgentBundleAttachmentTests` (22).
- R-4 the whole `Antiphon.Tests` Unit lane (stage-code Final round default).

Red-first rule: each V test is committed red with the failing assertion quoted in the commit
message before its slice's production change; a test that passes before the change is a stub
(CARD-0585 rule 4) and is rewritten. `[Category]` per `TestLaneCategoryGuardTests`; classes that
spawn a process carry `[ParallelLimiter<ProcessSpawnLimit>]` and are added to
`ProcessSpawnLimitTests.Process_spawning_classes_carry_the_limiter` (`RunCheckpointScriptTests`
already is; add `BuildSlotScriptTests`, `BuildSlotEndToEndTests` in their own assembly's list).

Platform notes: every R1 row runs on Linux or Windows. On Linux `run-checkpoint.ps1` adds
`UseAppHost=false` itself (CARD-0671). CP-1 records which host measured it. R2's watchdog census has a
Windows (WMI) and a Linux (`/proc`) arm; each arm's test executes only on its platform and the other
is reported as not run with the reason.

### Checkpoints

Isolated outputs `bin-c589r/` (`Antiphon.SessionRunner.Tests`), `bin-c589a/` (`Antiphon.Tests`),
`bin-c589m/` (CP-1's measured build), forward slash; one build per project per round, every other
row `--no-build`. `Min` = `[Test]` methods in the named classes at `e8874bfb` (`grep -c '^\s*\[Test'`)
plus the new methods, minus a 5 % floor allowance. `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests`
are never co-scheduled. Rows are run with `scripts/run-checkpoint.ps1` (which, once S3 is committed,
takes a slot itself; until then the row is reported `slot=n/a`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Messaging.Tests -> bin-c589m/` (measured build, not a TUnit row) | rsp-measure | `pwsh -NoProfile -Command` sampling loop: record `t0`; `dotnet build tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c589m/ --nologo`; sample `Get-Process` every 1 s for processes whose command line contains `/nodemode:1` or is `VBCSCompiler` with StartTime ≥ t0; 10 s after exit count survivors | S1, D-2 | survivors = 0; peak node count recorded; with a temporary `-maxcpucount:2` rsp line the recorded peak (expected > 2, i.e. ignored) is written into the docs section; temporary line removed before commit | n/a | 6 |
| CP-2 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c589r/` | build-slot-broker | `/*/*/(BuildSlotBrokerTests*)\|(BuildSlotEndpointTests*)\|(BuildSlotSettingsTests*)/*` | V-1, V-2, V-3 | all listed, 0 failed | 16 | 9 |
| CP-3 | S2 | CP-2 | runner-adjacent | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(RunnerCapabilitiesTests*)\|(SessionCpuWatchdogTests*)\|(RunnerCapacityCountTests*)/*` | R-1 | all listed, 0 failed | 42 | 6 |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c589a/` | checkpoint-scripts | `/*/*/(RunCheckpointScriptTests*)\|(BuildSlotScriptTests*)/*` | V-4, V-5 | all listed, 0 failed; every `C589_*` case's PASS inventory | 24 | 14 |
| CP-5 | S4 | CP-4 | land-verifier | `/*/*/(LandVerificationBuildSlotTests*)\|(AgentTaskLandVerifierTests*)\|(AgentTaskLandAdmissionTests*)\|(AgentTaskLandBoundaryTests*)/*` | V-8, R-2 | all listed, 0 failed | 17 | 8 |
| CP-6 | S1, S5 | CP-4 | bundles-docs | `/*/*/(InstructionBundleTests*)\|(CheckpointManifestDocumentationTests*)\|(DelegateBundleLaunchTests*)\|(AgentBundleAttachmentTests*)\|(DirectoryBuildRspTests*)\|(ProcessSpawnLimitTests*)/*` | V-6, V-7, R-3 | all listed, 0 failed | 82 | 6 |
| CP-7 | S2, S3 | CP-2 | slot-end-to-end | `/*/*/BuildSlotEndToEndTests/*` | V-9 | all listed, 0 failed; the second wrapper's output contains `BUILD SLOT waiting` | 2 | 5 |
| CP-8 | all R1 | CP-4 | unit-lane | `/*/*/*/*[Category=Unit]` | R-4 | ≥ 2990 executed, 0 failed (last measured 3021 total / 2993 passed / 28 skipped at `c18a6c67`) | 2900 | 12 |
| CP-9 | S5 | n/a | docs-named | `git grep -n -e "build-slot.ps1" -e "SessionRunner:BuildSlots" -e "ANTIPHON_BUILD_SLOTS_URL" -e "BUILD SLOT" -- AGENTS.md docs/testing-and-build.md docs/docker-stack.md docs/ops-http.md docs/orchestration-loop.md server/Bundles/delegate-basics.md server/Bundles/stage-code.md server/Bundles/stage-review.md` | S5 | ≥ 12 matching lines across ≥ 6 files, exit 0 | n/a | 1 |
| CP-10 | R2 S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c589w/` | build-watchdog | `/*/*/(BuildProcessWatchdogTests*)\|(BuildProcessCensusTests*)\|(SessionCpuWatchdogTests*)/*` | R2 V (watchdog: candidate scoring with fake census/probe, detect-only default emits one event per process, `KillEnabled` kills the driver root only, ancestry outside owned sessions never a candidate; census arm for the current platform) | all listed, 0 failed; the other platform's census test reported not run with reason | 12 | 9 |
| CP-11 | R2 S7 | `tests/Antiphon.Tests -> bin-c589s/` | build-slots-surface | `/*/*/(PhoneHomeBuildSlotOperationTests*)\|(SessionRunnerBuildSlotEndpointTests*)\|(PhoneHomeTaskCreateTests*)/*` | R2 V (ops 27/28 round trip, persisted budget precedence, desktop routes, 409 mapping) | all listed, 0 failed | 10 | 10 |
| CP-12 | R2 S8 | CP-11 | nightly-slot | `/*/*/NightlyVerificationContractTests/*` | R2 V (nightly harness case `C487_BuildSlot`: build and each suite acquire through the seam, unleased fallback recorded in `last-run.json`) | all listed, 0 failed | 20 | 6 |
| CP-13 | R2 S7 | n/a | client-runner-card | `pwsh -File scripts/test-client.ps1 sessionRunners` | R2 V (client: build-slot row renders budget/occupied) | `CLIENT TESTS EXIT CODE: 0` | n/a | 3 |

The pipe characters inside `Filter` cells are table escapes; the command line uses a plain `|`,
quoted as [docs/testing-and-build.md](../../testing-and-build.md#combined-class-filters-card-0403)
shows. Run each row with `scripts/run-checkpoint.ps1 -Name CP-n -Project <project> -OutputPath
bin-c589x/ -Filter '<filter>' -MinExecuted <Min> -Expect <classes> -ResultsRoot .antiphon/c589-checkpoints`
(`-NoBuild` for reuse rows). CP-1 and CP-9 are non-TUnit rows reported by hand in the CHECKPOINT
line shape with their own assertion. CP-7 starts pwsh children and must not be co-scheduled with
another spawning row.

### Cost

R1 checkpoints: 6 + 9 + 6 + 14 + 8 + 6 + 5 + 12 + 1 = **67** minutes, plus slot waits once S3 is in
(none expected while this host holds four seats). Authoring R1: S1 ~0.5 h, S2 ~3 h (broker, probe,
routes, 17 tests), S3 ~3 h (lib, two scripts, two harnesses, 10 cases), S4 ~1.5 h, S5 ~1 h; about
9 h. R2 checkpoints 9 + 10 + 6 + 3 = **28** minutes; authoring about 6 h.

## Follow-ups (not in this card)

- Idle standing-session retirement policy (card candidate 4): a new card, operator-authorised by
  design, never age-only (CARD-0203).
- Per-session Windows Job memory belt (`AgentSessions:MemoryLimitMb` at 16 GB): revisit once the
  slot gate has a month of `GET /build-slots` history.
- CARD-0654/0674: when `hosts[]` lands, fold `buildSlots` into it and let load-aware admission read
  the same `IHostMemoryProbe`.
- `VBCSCompiler` keep-alive tuning if the idle server shows up in the memory floor refusals.

## Open defaults (stated, not asked)

- D-4's numbers (2/4/6 GB desktop; 4/6/16 GB server2) are the first setting, tuned from
  `GET /build-slots` and the floor refusals, not derived from a benchmark.
- Slot wait 45 minutes; unreachable grace 60 seconds; lease TTL 90 minutes; exit code 4.
- The desktop broker is the production runner at `:17204`; isolated runners set
  `ANTIPHON_BUILD_SLOTS_URL`. Tests never call the production URL (harness shim; `BuildSlotEndToEndTests`
  points at its own host).
- Round 2's watchdog ships detect-only; turning `KillEnabled` on is an operator setting.
