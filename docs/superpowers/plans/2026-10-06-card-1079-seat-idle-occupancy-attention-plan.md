# CARD-1079: alert on idle runner seats, host occupancy above the Working count, and orphan slots (detection only)

Date: 2026-10-06. Plan task: `0579e430`. Role Plan, Frontier, runner mirror
`/work/worktrees/task-0579e430` on branch `feat/card-task-0579e430` (fast-forward-only from
`d985af05b`; fast-forwarded to `origin/master` before inspection).
Inspected source: `fb37c36c10f38da4b04de72496ce9c080e993c85` (origin/master at planning time).
Status: Plan complete with the verification design folded in; **next: land** (the brief folds
TestDesign into this dispatch). No production code, settings, cards or sessions were changed.
`GET /api/runner-defaults` and `GET /api/session-runners` are not reachable from the runner
mirror (port 17202 is the desktop); the orchestrator reads them at dispatch. Every checkpoint
row below is a Linux-lane row with no Windows-only behaviour; dispatch with `-Platform Any`
and no `-Runner` pin.

## Outcome and scope

Three read-time attention conditions, each with a `ConditionKey` that disappears when the
state it names disappears, computed from a snapshot a background sampler publishes once a
minute and never from a runner RPC on the request path:

1. `SeatIdle`: a phone-home runner slot that occupies capacity while its bound task is not
   Dispatched or Working (Blocked, terminal, or no task) for longer than
   `Attention:SeatIdleWarningMinutes` (30) is Warning; past `Attention:SeatIdleErrorMinutes`
   (180) it is Error. Evidence names runner, session id, task, attempt, status, age, and
   whether the branch is known to be pushed.
2. `OccupancyDivergence`: per phone-home host, `inFlight` as `GET /api/hosts` computes it minus
   the Dispatched/Working tasks bound to that host, when at least one idle seat is behind the
   number, for longer than the same thresholds.
3. `SlotOrphan`: every slot the runner lists as `orphan=true` that occupies capacity, immediately,
   Warning.

The same sampler writes one durable `HostOccupancySample` row per host per tick, retained
`Attention:OccupancySampleRetentionDays` (14), readable through
`GET /api/hosts/{hostId}/occupancy-samples`, so an audit can answer "how long was server2 at
10/10 with eight idle seats" from rows rather than from task events.

Nothing here releases, kills, parks or dispatches. Release authority stays with CARD-1065 and
the dormant CARD-0667 coordinator. Feed de-noising is CARD-1085; the Blocked clock and
sync-block classification are CARD-1081; instruction lines are CARD-1083.

## Ground truth

| Card assumption | What the inspected code does | Plan consequence |
|---|---|---|
| The orphan flag is computed on request and read by nobody. | `RunnerSlotService.ListAsync` (`server/Application/Services/RunnerSlotService.cs:32-62`) computes `IsOrphan` (`:29-30`) over `LoadDesktopAsync` (`:387-425`), whose open-task predicate admits only Dispatched/Working (`:396-400`). Consumers: the slots route (`server/Api/Endpoints/SessionRunnerEndpoints.cs:170`) and the operator sweep. No attention builder reads it. | Confirmed. Extract the desktop join into one shared helper so the sampler and the slots route use the same "a seat is owned only by Dispatched/Working" predicate (D-2). |
| `GET /api/hosts` shows `inFlight` with no status breakdown. | `HostEndpoints.ProjectAsync` (`server/Api/Endpoints/HostEndpoints.cs:50-62`): `inFlight = sessions + pendingLaunch + mirrors`, where `sessions` is live `AgentSessions` with `RunnerId == host`. The pipeline route repeats the arithmetic (`AgentTaskPipelineStatusService.cs:162-172`). Local `inFlight` is the Dispatched/Working non-specialist task count with no runner (`HostEndpoints.cs:42-44`), so local divergence is zero by construction. | Divergence is `inFlight - dispatchedWorking` with exactly this `inFlight`, computed from desktop rows, so the row's number is the one the orchestrator already sees (D-5). Local hosts get samples only (D-4). |
| No attention row is keyed on a seat; every age condition excludes Blocked. | `AttentionService.GetAsync` opens on Dispatched/Working only (`server/Application/Services/AttentionService.cs:172-174`); leak builders treat Blocked as an owner (`AttentionService.Leaks.cs:127-129`, `:229-230`). Rows are ordered by severity then `SinceUtc` (`AttentionService.cs:265-271`). Kinds are appended, never renumbered; the highest shipped value is `WorktreeCleanupBacklog = 56` (`server/Application/Dtos/AttentionDtos.cs:341`). | Append `SeatIdle = 57`, `OccupancyDivergence = 58`, `SlotOrphan = 59` (D-14). |
| A snapshot-state pattern exists for runner-derived rows. | `RunnerAlarmState` (`server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs:61-68`) is a `Volatile`/`Interlocked` holder published by `RunnerAlarmHostedService` and read through an optional `AttentionService` constructor parameter (`AttentionService.cs:144-145`, `:239-241`). Tests build `AttentionService` directly with `alarms:` (`tests/Antiphon.Tests/Application/RunnerAlarmAttentionTests.cs:128-135`). | `SeatOccupancyState` follows this shape; `AttentionService` gains optional `seats`/`attention` parameters so every existing harness compiles unchanged (D-1). |
| Runner inventory needs an RPC. | `ISessionRunnerDirectory.GetInventoryAsync` (`server/Application/Interfaces/ISessionRunnerDirectory.cs:58`) returns `Available(sessions)` or `Unavailable(reason)`; the phone-home implementation RPCs the live connection and answers `Unavailable` when the runner is absent, ineligible or lease-expired (`PhoneHomeRunnerDirectory.cs:216-240`). The cached `KnownLiveSessions` holds ids only, no `Status`/`StartedAt` (`PhoneHomeLiveConnection.cs:146-162`). `SingleRunnerDirectory` already implements the inventory call (`tests/Antiphon.Tests/TestHelpers/SingleRunnerDirectory.cs:43-55`). | The sampler calls `GetInventoryAsync` once per runner per tick (the same cost the host-stats poller already pays every 5 s). The cache cannot supply occupancy or age, so it is not used (D-1). |
| Hosts stats are in-memory 1/5/15/30-minute rollups. | `HostStatsPollService` -> `HostStatsCache`; `HostStatsAntiphonDto` (`server/Application/Dtos/HostStatsDtos.cs:5-14`) carries `TasksInFlight`/`SessionsLive` with no persistence; `docs/ops-http.md:127` says so. No `*Occupancy*` entity exists. | New `HostOccupancySample` entity and CLI migration (D-9). |
| `Attention:` settings exist. | No `AttentionSettings` class. The model is `AlarmSettings` + `AlarmSettingsValidator` (`server/Application/Settings/AlarmSettings.cs`, `AlarmSettingsValidator.cs`) registered at `server/Program.cs:202-205`. The one existing warning/error pair lives on `DelegationSettings` (`:596-600`) with the validator enforcing Error > Warning (`:1183-1184`). | New `AttentionSettings` + validator with the same Error > Warning rule (S1). |
| Evidence can say whether the branch is pushed. | `AgentTask` has `WorktreeBranch` (`server/Domain/Entities/AgentTask.cs:202`), `SourceLandingSha` (`:183`), `CommitBaselineSha` (`:594`); none is a push receipt. CARD-1065's `AgentTaskPark` carries `PublicationReceiptId`/`VerifiedRemoteSha` per (TaskId, Attempt) (`server/Domain/Entities/AgentTaskPark.cs:33-36`), written only when parking runs. | `pushed=yes` only from a park row with a receipt for the bound attempt, else `pushed=unknown`; the sweep never runs git (D-10). |
| "How long" can be read from stored rows. | Blocked time is the latest `AgentTaskEvent` of type `Blocked` (`server/Domain/Enums/AgentTaskEnums.cs:138`) by `At` (`AgentTask.cs:690`); settlement time is `CompletedAt` (`AgentTask.cs:423`); a runner slot has `StartedAt` (`server/Application/Dtos/SessionRunnerDtos.cs:8`). | Idle-since is derived from these durable facts, never from first-seen memory (D-3). |
| "Any runner" includes the desktop. | The desktop host's seat unit is the delegated task (`HostStatsPollService.cs:142-149`: `delegatedTasks` vs `sessions`); `Resolve(desktop alias)` returns the local client (`PhoneHomeRunnerDirectory.cs:153-156`). Standing agents, orchestrators and Check seats are desktop sessions with no Dispatched/Working task, so `IsOrphan` would mark every one of them. | Seat rows are scoped to phone-home runner hosts; the local host gets occupancy samples only. Desktop session leaks stay with `PoolDelegateUnreleased`/`SessionUnowned` (D-4, flagged for a human). |
| Migrations are generated. | `docs/project-context.md:125`: CLI only; `.config/dotnet-tools.json` pins `dotnet-ef` 9.0.20; the latest migration is `20261005222935_AddAgentTaskParks`. | `dotnet tool restore` then `dotnet ef migrations add AddHostOccupancySamples --project server` through `scripts/build-slot.ps1`; reported as an unlisted build with that reason. |
| The client renders any kind the server sends. | `client/src/api/attention.ts` is a string union mirroring the enum; `ATTENTION_VISUALS` is `Record<AttentionKind, AttentionVisual>` (compile-time completeness) and `attentionVisuals.test.ts` keeps an `ALL_KINDS` census (currently 23 `it` + 1 `it.each`); `groupOf` falls through severity. Icons already imported: `TbClockPause`, `TbUserOff`, `TbHourglassHigh`, `TbAlertTriangle` among others. | Add the three kinds to the union, visuals and census (S4). |
| Existing tests pin the surfaces this plan touches. | `RunnerSlotRulesTests` (2), `RunnerSlotEndpointTests` (8), `HostEndpointTests` (4), `HostBudgetPipelineTests` (4), `RunnerAlarmAttentionTests` (3), `CardClosedAttentionTests` (4); none uses `[Arguments]` except `DispatchHeldAttentionTests` (not selected). | R-1 to R-6 below. |

## Decisions

Every decision is a stated default. Only D-4 is a real human call; the others follow from the
card and the code.

### D-1. Sampler-published snapshot, not request-time RPC

A scoped `SeatOccupancySampler` runs on a `PeriodicTimer` every
`Attention:OccupancySampleIntervalSeconds` (60), reads each runner's inventory through
`ISessionRunnerDirectory.GetInventoryAsync`, joins it to desktop rows, writes the durable
sample and publishes an immutable `SeatOccupancySnapshot` into a singleton
`SeatOccupancyState`. `AttentionService` projects rows from `SeatOccupancyState.Current`.

Rejected: computing in `GetAsync` with a per-runner List RPC. `/api/attention` and the badge
summary are polled; the leak builders deliberately use the cached inventory and "never an RPC
to an unavailable runner" (`AttentionService.cs:232-235`). Rejected: deriving from
`KnownLiveSessions`. It carries ids only, so neither `OccupiesCapacity` nor seat age exists.

### D-2. One desktop join for the slots route and the sampler

`LoadDesktopAsync` moves to `SeatDesktopJoin.LoadAsync` (internal static) and grows the
fields the sampler needs: latest bound task (id, status, attempt, card, agent, completion),
latest Blocked event time, pooled-warm flag, publication receipt, card's board. `OpenTaskId`
keeps its Dispatched/Working-only meaning so `RunnerSlotsDto` is byte-for-byte unchanged.
Rejected: a second copy of the predicate; the retrospective's design gap was two correct rules
that nobody joined.

### D-3. Idle-since from durable task facts

A seat's idle clock starts at: the latest `Blocked` event `At` when the bound task is Blocked;
`CompletedAt` when it is terminal; the runner's `StartedAt` when no task is bound or the
task is Queued. Rejected: in-memory first-seen (a server restart resets it and hides the seat
for another 30 minutes). Rejected: reading the clock from the sample series (couples the alert
to sampler history and retention).

### D-4. Seat rows for phone-home runners; samples for every host (human call)

`SeatIdle`, `SlotOrphan` and `OccupancyDivergence` are computed for hosts whose `HostLimit`
is a runner (every host but `local`). The local host still gets a `HostOccupancySample` with
`inFlight` = Dispatched/Working non-specialist tasks and `dispatchedWorking` equal to it.
Reason: the desktop's seat is the task, standing-agent sessions would all read orphan, and
local divergence is zero by construction. This narrows the card's "any runner"; a human may
widen it later by letting the sampler list the local client, which is a one-line change behind
the same join.

### D-5. Divergence is the orchestrator's number, gated on an idle seat

`inFlight` is `sessions + pendingLaunch + mirrors` exactly as `HostEndpoints.ProjectAsync`
computes it (sessions are live `AgentSessions` with `RunnerId == host`, `pendingLaunch` is
Queued tasks with a `RemoteWorktreePath` and no session, `mirrors` is
`RemoteWorkspacePreparer.InFlightCount`). `dispatchedWorking` is the count of Dispatched/Working
`AgentTasks` with `RunnerId == host`. Idle seats are the desktop-side live sessions on the host
whose latest task is not Dispatched/Working and that are not pooled warm; the row needs at least
one and its age is the oldest idle seat's idle-since. Divergence made only of pending launches
and mirrors emits no row: `DispatchHeld` already names those holds. All components appear in
the evidence. Rejected: alarming on any positive divergence (every remote dispatch would fire
for a few minutes).

### D-6. Orphan rows only for slots that occupy capacity

An Exited slot the runner still remembers reads `orphan=true` today but holds nothing; it gets
no row. Rejected: rows for every orphan-flagged slot.

### D-7. Three independent rows, no suppression

An idle Blocked seat produces a `SlotOrphan` row at once and a `SeatIdle` row after 30 minutes;
the host produces one `OccupancyDivergence` row. Keys: `slot-orphan:{runnerId}:{sessionId:N}`,
`seat-idle:{runnerId}:{sessionId:N}`, `occupancy-divergence:{hostId}`. Each evidence names the
others. Rejected: `SeatIdle` superseding `SlotOrphan`, because the orphan key would clear while
the orphan state persists. Collapsing per seat is CARD-1085.

### D-8. Severity ladder

`SeatIdle` and `OccupancyDivergence`: no row below `SeatIdleWarningMinutes`, Warning from it,
Error from `SeatIdleErrorMinutes`. `SlotOrphan`: Warning. Never Critical: Critical means "only
a human answer moves this" and these rows ask for a look, not an answer.

### D-9. Default-on, rollback flag, bounded retention

`Attention:SeatWatchEnabled` defaults true (the card is detection and the retrospective's finding
was silence). `OccupancySampleIntervalSeconds` 60, `OccupancySampleRetentionDays` 14, pruning
one bounded `DELETE` per tick. Volume: three hosts at 1440 rows a day is about 4,300 rows a day
and about 60,000 at retention. Rejected: default-off; rejected: write-on-change only (an audit
wants a regular series).

### D-10. Pushed is a fact or unknown

`pushed=yes` only when an `AgentTaskPark` row for the bound task's attempt has a
`PublicationReceiptId`; otherwise `pushed=unknown`. The sweep never runs git or reads a
worktree.

### D-11. Actions name existing verbs only

A `SeatIdle` or `SlotOrphan` row whose bound task is Blocked offers `Reply`, `Cancel`,
`OpenDrawer`; otherwise `OpenDrawer`; `OpenAgent` is added when the agent is known. No
`KillSession`, no release.

### D-12. Read-only audit route

`GET /api/hosts/{hostId}/occupancy-samples?from=&to=&limit=` returns newest-first rows for
the host; default window is the last 24 hours, `limit` defaults to 500 and caps at 2000;
unknown host is 404 through `HostBudgetService.EffectiveAsync`. No write route.

### D-13. Rows from the latest snapshot whatever its age

Evidence carries `observedAt`. A fourth "sampler stale" kind is not added. Thresholds are
30 minutes or more, so a snapshot a few minutes old cannot produce a wrong escalation, and
silence when the sampler dies is the failure mode the card exists to remove.

### D-14. Kind names and numbers

`SeatIdle = 57`, `OccupancyDivergence = 58`, `SlotOrphan = 59`, appended after shipped 56.

## Implementation slices

Slices are sequenced: S2 uses S1 types, S3 uses S2 state, S4 uses S2's entity and S3's kinds.
Run them as four Code tasks in order, each verifying its own checkpoint rows, or as one Code
task that commits each slice before its rows.

### S1: settings, pure rules, durable entity, shared desktop join (55 minutes)

| File | Change | Verification |
|---|---|---|
| `server/Application/Settings/AttentionSettings.cs` (new) | `SectionName = "Attention"`; `SeatWatchEnabled = true`; `SeatIdleWarningMinutes = 30`; `SeatIdleErrorMinutes = 180`; `OccupancySampleIntervalSeconds = 60`; `OccupancySampleRetentionDays = 14`; `InventoryTimeoutMs = 3000`. | V-5 |
| `server/Application/Settings/AttentionSettingsValidator.cs` (new) | Warning > 0; Error > Warning; interval 10..3600; retention 1..365; timeout > 0. | V-5 |
| `server/Program.cs` | Register validator and options next to `Alarms` (`:202-205`). | CP-1 build |
| `server/Domain/Entities/HostOccupancySample.cs` (new) | `Id`, `HostId` (200), `SampledAt`, `InventoryState` (32: `listed`, `unavailable`, `local`), `InventoryReason` (256, nullable), `InFlight`, `DispatchedWorking`, `Sessions`, `PendingLaunch`, `InFlightMirrors`, `IdleSeats`, `PooledWarmSeats`, `OrphanSlots`, `EffectiveLimit?`, `DeclaredCapacity?`, `OldestIdleSince?`. | V-7 |
| `server/Infrastructure/Data/AppDbContext.cs` | `DbSet<HostOccupancySample> HostOccupancySamples` after `AgentTaskParks` (`:126`); key, max lengths, index `(HostId, SampledAt)`. | CP-1 build |
| `server/Migrations/<ts>_AddHostOccupancySamples.cs` (+ Designer, snapshot) | CLI-generated only. | V-6 (schema applied by `TestDbFixture`) |
| `server/Application/Services/SeatOccupancyProjection.cs` (new, static) | `SeatClass Classify(bool occupies, bool desktopLive, bool pooledWarm, AgentTaskStatus? bound)` -> `Exited`, `PooledWarm`, `Active`, `IdleBlocked`, `IdleTerminal`, `IdleUnbound`; `DateTime IdleSince(SeatClass, DateTime? blockedAt, DateTime? completedAt, DateTime runnerStartedAt)`; `AlertSeverity? Severity(TimeSpan age, AttentionSettings)`; `int Divergence(int inFlight, int dispatchedWorking)`; `string Pushed(bool receipt)`. Reuses `RunnerSlotService.OccupiesCapacity` and `IsOrphan`. | V-1 to V-4 |
| `server/Application/Services/SeatDesktopJoin.cs` (new, internal static) | `LoadAsync(AppDbContext, Guid[] sessionIds, ct)` -> `Dictionary<Guid, SeatDesktopRow>`: `Live`, `Status`, `OpenTaskId` (Dispatched/Working only), `LatestTask` (id, status, attempt, role, cardId, boardId, agentId, completedAt), `BlockedAt`, `PooledWarm`, `PublicationReceipt`. One query per table, no card text. | V-6 |
| `server/Application/Services/RunnerSlotService.cs` | `LoadDesktopAsync` delegates to `SeatDesktopJoin`; `ListAsync` output unchanged. | R-1, R-2 |
| `tests/Antiphon.Tests/Application/SeatOccupancyProjectionTests.cs` (new, Unit) | V-1 to V-4. | CP-1 |
| `tests/Antiphon.Tests/Application/AttentionSettingsValidatorTests.cs` (new, Unit) | V-5. | CP-1 |
| `tests/Antiphon.Tests/Application/SeatDesktopJoinTests.cs` (new, Integration, isolated schema) | V-6. | CP-1 |

### S2: snapshot, state, sampler, hosted service (55 minutes)

| File | Change | Verification |
|---|---|---|
| `server/Application/Services/SeatOccupancySnapshot.cs` (new) | Records `SeatOccupancySnapshot(DateTime GeneratedAt, IReadOnlyList<HostOccupancyObservation> Hosts)` with `Empty`; `HostOccupancyObservation` (host id, kind `local`/`runner`, inventory state and reason, the sample's counters, `EffectiveLimit`, `DeclaredCapacity`, `OldestIdleSince`, `Seats`); `SeatObservation` (runner id, session id, runner status, pid, started-at, occupies, orphan, pooled warm, desktop status, bound task id/status/attempt/role, card id, board id, agent id, class, idle-since, pushed). | V-7, V-8 |
| `server/Infrastructure/Agents/SessionRunner/SeatOccupancyState.cs` (new) | `Current` / `Publish` like `RunnerAlarmState`. | V-7 |
| `server/Application/Services/SeatOccupancySampler.cs` (new, scoped) | Deps: `AppDbContext`, `ISessionRunnerDirectory`, `HostBudgetService`, `SeatOccupancyState`, `IOptions<AttentionSettings>`, `TimeProvider`, `ILogger`, optional `RemoteWorkspacePreparer`. `SampleOnceAsync(ct)`: for each `HostLimit` build the observation (local: task count; runner: desktop-side counters always, runner inventory with the configured timeout, join, classify), add one `HostOccupancySample` per host, prune rows older than retention, `SaveChanges`, publish. A runner inventory failure is `InventoryState = "unavailable"` with the reason and no seats; the desktop-side counters are still sampled. | V-7 to V-12 |
| `server/Infrastructure/Orchestration/SeatOccupancyHostedService.cs` (new) | `BackgroundService`; returns at once when `SeatWatchEnabled` is false; first tick immediately, then `PeriodicTimer(interval, TimeProvider)`; each tick in its own scope; failures logged, never fatal. | V-11 |
| `server/Program.cs` | `AddSingleton<SeatOccupancyState>()`, `AddScoped<SeatOccupancySampler>()`, `AddHostedService<SeatOccupancyHostedService>()` beside `HostStatsPollService` (`:890`). | CP-3 build |
| `tests/Antiphon.Tests/Application/SeatOccupancySamplerTests.cs` (new, Integration) | Isolated schema; `SingleRunnerDirectory(client, remoteRunnerId: "server2")` with a sessions-bearing `ISessionRunnerClient` fake modelled on `AttentionServiceTests.FakeRunnerClient`; a `HostBudget` row (`MaxInFlight = 10`) so `Effective` is set; `FakeTimeProvider`. | CP-3 |

### S3: attention rows (50 minutes)

| File | Change | Verification |
|---|---|---|
| `server/Application/Dtos/AttentionDtos.cs` | Append the three kinds with doc comments naming the keys, thresholds and D-4 scope. | CP-5 build |
| `server/Application/Services/AttentionService.Seats.cs` (new partial) | `BuildSeatIdleItems`, `BuildOccupancyDivergenceItems`, `BuildSlotOrphanItems` over `_seats?.Current`; titles, headlines, evidence and actions as in D-7, D-8, D-11; `SinceUtc` is the idle-since (orphan: idle-since when idle, else runner start). | V-13 to V-20 |
| `server/Application/Services/AttentionService.cs` | Optional constructor parameters `SeatOccupancyState? seats = null`, `IOptions<AttentionSettings>? attention = null`; call the three builders after the alarm rows (`:241-242`). | R-5, R-6 |
| `tests/Antiphon.Tests/Application/SeatOccupancyAttentionTests.cs` (new, Integration) | Isolated schema, `AttentionService` built as `RunnerAlarmAttentionTests` does, snapshots published straight into `SeatOccupancyState`. | CP-5 |

### S4: audit route, client, docs (45 minutes)

| File | Change | Verification |
|---|---|---|
| `server/Application/Dtos/HostOccupancyDtos.cs` (new) | `HostOccupancySampleDto` mirroring the entity. | V-21 |
| `server/Api/Endpoints/HostEndpoints.cs` | `GET /api/hosts/{hostId}/occupancy-samples` per D-12. | V-21 to V-23 |
| `client/src/api/attention.ts` | Add `'SeatIdle' \| 'OccupancyDivergence' \| 'SlotOrphan'`. | V-24 |
| `client/src/features/attention/attentionVisuals.ts` | Three visuals: `SeatIdle` (label "Idle seat", warning, `TbClockPause`), `OccupancyDivergence` (label "Seats above Working", warning, `TbHourglassHigh`), `SlotOrphan` (label "Orphan slot", warning, `TbUserOff`); hints say detection only and name the reclaim verbs. Icons must already be imported in the file. | V-24 |
| `client/src/features/attention/attentionVisuals.test.ts` | Add the three kinds to `ALL_KINDS`; one `it` asserting the three labels are non-empty and that Warning rows land in `review` and an Error `OccupancyDivergence` lands in `broken`. | V-24 |
| `docs/ops-http.md` | New attention row for the three kinds with keys, thresholds, settings and the D-4 scope; `/api/hosts` row gains the occupancy-samples route; the slots row (`:137`) gains "an occupying `orphan=true` slot is a `SlotOrphan` attention row". | review |
| `docs/bootstrap.md` | After the `Alarms:*` lines (`:423-426`): the `Attention:*` settings and the sampler. | review |
| `server/appsettings.json` | `"Attention"` block with the defaults beside `"Alarms"` (`:181`). | review |
| `tests/Antiphon.Tests/Application/HostOccupancySampleEndpointTests.cs` (new, Integration) | `PhoneHomeTestHost.StartAsync(... mapEndpoints: app => app.MapHostEndpoints())` as `HostEndpointTests` does; seed samples directly. | CP-7 |

No new Slow class: every new TUnit class is Unit or Integration on an isolated schema with a
`FakeTimeProvider` and no process spawn, so `tests/Antiphon.Tests/slow-tests-allowlist.txt` is
unchanged and no `ParallelLimiter<ProcessSpawnLimit>` is needed.

## Verification design

### Inspection

| Inspected source | Boundary covered |
|---|---|
| `server/Application/Services/RunnerSlotService.cs` (`OccupiesCapacity`, `IsOrphan`, `ListAsync`, `LoadDesktopAsync`) | Seat predicates reused unchanged; join extraction must leave `RunnerSlotsDto` identical -> V-1, V-2, V-6, R-1, R-2. |
| `server/Api/Endpoints/HostEndpoints.cs`, `AgentTaskPipelineStatusService.cs:162-172` | The `inFlight` arithmetic the sampler must reproduce -> V-7, V-12, R-3, R-4. |
| `server/Application/Services/AttentionService.cs` (constructor, `GetAsync`, ordering), `AttentionService.Alarms.cs`, `AttentionService.Leaks.cs` | Optional-dependency constructor, builder placement, key and evidence conventions -> V-13 to V-20, R-5, R-6. |
| `server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`, `server/Infrastructure/Orchestration/RunnerAlarmHostedService.cs` | State holder and hosted-loop shape -> S2. |
| `server/Application/Interfaces/ISessionRunnerDirectory.cs`, `PhoneHomeRunnerDirectory.cs:216-240`, `tests/.../SingleRunnerDirectory.cs` | Inventory availability semantics and the test double -> V-9. |
| `server/Application/Settings/AlarmSettings*.cs`, `DelegationSettings.cs:596-600`, `:1183-1184` | Settings and validator conventions -> V-5. |
| `server/Domain/Entities/AgentTaskPark.cs:33-36`, `AgentTask.cs:181-205`, `:420-424`, `:681-692` | Durable facts for pushed and idle-since -> V-4, V-6, V-8. |
| `client/src/features/attention/attentionVisuals.ts`, `.test.ts`, `client/src/api/attention.ts` | Kind census and grouping -> V-24. |
| `tools/Antiphon.Checkpoints/Program.cs` (`import`), `docs/testing-and-build.md:383-460`, `:799-811` | Table schema and combined-class filter syntax -> Checkpoints. |

### Delivery inventory

| Behaviour | Delivered by | Proven by |
|---|---|---|
| Seat classification, idle clock, severity ladder | S1 `SeatOccupancyProjection` | V-1 to V-4 |
| Settings validation | S1 `AttentionSettingsValidator` | V-5 |
| One shared desktop join | S1 `SeatDesktopJoin` | V-6, R-1, R-2 |
| Durable per-host sample and published snapshot | S2 sampler | V-7 to V-12 |
| Three attention conditions with clearing keys | S3 | V-13 to V-20, R-5, R-6 |
| Audit route | S4 | V-21 to V-23, R-3, R-4 |
| Client rendering | S4 | V-24 |

### Proves it works now

| ID | Exact test | Required observation |
|---|---|---|
| V-1 | `SeatOccupancyProjectionTests.C1079_Classify_names_blocked_terminal_and_unbound_seats_idle_and_working_seats_active` | `[Arguments]` over the seven `AgentTaskStatus` values plus `null` (8 results): Dispatched/Working -> `Active`; Blocked -> `IdleBlocked`; Succeeded/Failed/Canceled -> `IdleTerminal`; Queued and `null` -> `IdleUnbound`. Labels `c1079-class-<status>`. |
| V-2 | `SeatOccupancyProjectionTests.C1079_Pooled_warm_and_exited_seats_are_never_idle_or_orphan` | `occupies=false` -> `Exited` whatever the task; `pooledWarm=true` -> `PooledWarm` and `IsOrphan` false; a live desktop row with a Blocked task is `IdleBlocked` and `IsOrphan` true. |
| V-3 | `SeatOccupancyProjectionTests.C1079_Severity_ladder_is_null_below_warning_then_warning_then_error` | `[Arguments]` 29 min -> null, 30 -> Warning, 179 -> Warning, 180 -> Error, 1000 -> Error (5 results) against defaults; boundaries are inclusive. |
| V-4 | `SeatOccupancyProjectionTests.C1079_Idle_since_prefers_blocked_event_then_completion_then_runner_start` | `IdleBlocked` with a Blocked event -> that `At`; `IdleBlocked` without one -> runner start; `IdleTerminal` -> `CompletedAt`; `IdleUnbound` -> runner start. |
| V-5 | `AttentionSettingsValidatorTests.C1079_Defaults_pass_and_error_not_above_warning_fails`; `AttentionSettingsValidatorTests.C1079_Interval_retention_and_timeout_bounds_are_enforced` | Defaults succeed; `Error == Warning` and `Error < Warning` each fail naming `Attention:SeatIdleErrorMinutes`; interval 9 and 3601, retention 0 and 366, timeout 0 each fail with the setting named. |
| V-6 | `SeatDesktopJoinTests.C1079_Join_reports_latest_bound_task_blocked_at_pooled_warm_and_receipt`; `SeatDesktopJoinTests.C1079_Join_open_task_is_dispatched_or_working_only` | Seeded: a Running session with two tasks (older Succeeded, newer Blocked with a Blocked event and a park row carrying a `PublicationReceiptId`) -> `LatestTask` is the Blocked one, `BlockedAt` equals the event `At`, `PublicationReceipt` true, `OpenTaskId` null; a pooled-warm agent's session -> `PooledWarm` true; a Working-task session -> `OpenTaskId` set; a Stopped session -> `Live` false. |
| V-7 | `SeatOccupancySamplerTests.C1079_One_sample_per_host_with_in_flight_breakdown_and_dispatched_working` | Hosts `local` and `server2`; server2 seeded with 3 live sessions (1 Working task, 1 Blocked task, 1 no task), 1 pending-launch Queued task with `RemoteWorktreePath`; runner lists the three Running. One row per host: server2 `InFlight=4`, `Sessions=3`, `PendingLaunch=1`, `InFlightMirrors=0`, `DispatchedWorking=1`, `IdleSeats=2`, `OrphanSlots=2`, `EffectiveLimit=10`, `InventoryState="listed"`; `State.Current.Hosts` carries the same numbers. |
| V-8 | `SeatOccupancySamplerTests.C1079_Seats_carry_idle_since_class_orphan_and_pushed_from_durable_facts` | The Blocked seat: `Class=IdleBlocked`, `IdleSince` = Blocked event `At`, `Orphan=true`, `Pushed="yes"` (park receipt seeded); the unbound seat: `IdleUnbound`, `IdleSince` = runner `StartedAt`, `Pushed="unknown"`; the Working seat: `Active`, `Orphan=false`; `OldestIdleSince` is the minimum. |
| V-9 | `SeatOccupancySamplerTests.C1079_Unavailable_inventory_writes_a_sample_without_seats` | Client `ListAsync` throws: server2 row has `InventoryState="unavailable"`, non-empty `InventoryReason`, desktop-side `Sessions`/`InFlight`/`DispatchedWorking`/`IdleSeats` still counted, `OrphanSlots=0`; snapshot host has `Seats.Count == 0`. |
| V-10 | `SeatOccupancySamplerTests.C1079_Prune_removes_samples_older_than_retention` | Seed rows at 15 days and 13 days before now; after one tick the 15-day row is gone, the 13-day row and the new rows remain. |
| V-11 | `SeatOccupancySamplerTests.C1079_Disabled_watch_writes_nothing_and_publishes_nothing` | `SeatWatchEnabled=false`: hosted service `ExecuteAsync` completes without a tick; table empty; `State.Current` is `Empty`. |
| V-12 | `SeatOccupancySamplerTests.C1079_Local_host_sample_counts_tasks_and_has_zero_divergence` | Two Dispatched/Working tasks with no runner, one Blocked, one specialist: `local` row `InFlight=2`, `DispatchedWorking=2`, `IdleSeats=0`, `InventoryState="local"`. |
| V-13 | `SeatOccupancyAttentionTests.C1079_Seat_idle_is_absent_below_the_warning_age` | Snapshot with an `IdleBlocked` seat 29 minutes old: no `SeatIdle` row; a `SlotOrphan` row for the same seat exists. |
| V-14 | `SeatOccupancyAttentionTests.C1079_Seat_idle_warning_names_runner_session_task_status_age_and_pushed` | 30 minutes old: one `SeatIdle`, Warning, `ConditionKey == $"seat-idle:server2:{sessionId:N}"`, `TaskId`/`SessionId`/`CardId`/`BoardId` set, evidence contains `runner=server2`, `session=`, `task=`, `attempt=`, `status=Blocked`, `age=`, `pushed=unknown`, `observedAt=`; actions exactly `[Reply, Cancel, OpenDrawer]`; `SinceUtc` equals the idle-since. |
| V-15 | `SeatOccupancyAttentionTests.C1079_Seat_idle_becomes_error_at_the_error_age` | 180 minutes old: Error; 179: Warning. |
| V-16 | `SeatOccupancyAttentionTests.C1079_Seat_idle_clears_when_the_bound_task_is_working_again` | First read has the key; a second snapshot with the same seat `Active` yields no row with that key and no `SlotOrphan` for it. |
| V-17 | `SeatOccupancyAttentionTests.C1079_Occupancy_divergence_is_one_row_per_host_and_needs_an_idle_seat` | Host A: `InFlight=10`, `DispatchedWorking=2`, two idle seats, oldest 200 minutes -> one row, Error, `occupancy-divergence:A`, headline contains `10` and `2`, evidence lists the breakdown and up to ten seats; host B: `InFlight=3`, `DispatchedWorking=2`, `IdleSeats=0`, `PendingLaunch=1` -> no row. |
| V-18 | `SeatOccupancyAttentionTests.C1079_Slot_orphan_is_immediate_for_occupying_orphans_only` | Orphan seat 1 minute old -> Warning row keyed `slot-orphan:server2:{id:N}`; a pooled-warm seat and an Exited orphan -> no rows. |
| V-19 | `SeatOccupancyAttentionTests.C1079_No_snapshot_or_unavailable_inventory_yields_no_seat_rows` | `seats: null` and `SeatOccupancyState.Empty` -> none of the three kinds; a host with `InventoryState="unavailable"` and no seats -> no `SeatIdle`/`SlotOrphan`, while its divergence row still follows D-5 from the desktop-side idle count. |
| V-20 | `SeatOccupancyAttentionTests.C1079_Summary_counts_the_three_kinds_open` | `AttentionSummaryDto.From(...).Open` rises by exactly the number of seat rows added against the baseline read. |
| V-21 | `HostOccupancySampleEndpointTests.C1079_Samples_return_newest_first_within_the_window` | Three seeded rows; `?from=&to=` selecting two returns them newest first with every field populated. |
| V-22 | `HostOccupancySampleEndpointTests.C1079_Unknown_host_is_404` | `/api/hosts/nope/occupancy-samples` -> 404. |
| V-23 | `HostOccupancySampleEndpointTests.C1079_Limit_caps_rows_and_default_window_is_24_hours` | `limit=2` over five rows returns 2; a row 25 hours old is excluded without `from`; `limit=5000` is clamped to 2000 (assert via a response header or by seeding 2001 rows is not required: assert the clamped value is echoed in the response's `limit` field). |
| V-24 | `attentionVisuals.test.ts` (`ALL_KINDS` census plus `it('CARD-1079 seat kinds are drawable and grouped by severity')`) | Census includes the three kinds; labels non-empty; `homeBucketOf` of a Warning `SeatIdle` and `SlotOrphan` is `review`, of an Error `OccupancyDivergence` is `broken`. |

### Guards the regression

| ID | Exact existing method(s) | Decisive assertion |
|---|---|---|
| R-1 | `RunnerSlotRulesTests.Exited_records_do_not_occupy_and_a_warm_pool_is_not_an_orphan`; `RunnerSlotRulesTests.Pending_release_reconcile_is_scheduled_and_triggered_at_startup` | `OccupiesCapacity`/`IsOrphan` truth table unchanged. |
| R-2 | `RunnerSlotEndpointTests` (8 methods) | The slots route still reports `orphan`, `occupiesCapacity`, `occupied`, `declaredCapacity` and release behaviour identically after the join extraction. |
| R-3 | `HostEndpointTests` (4 methods) | `/api/hosts` and the budget route are unchanged with the new sibling route mapped. |
| R-4 | `HostBudgetPipelineTests` (4 methods) | Pipeline host occupancy arithmetic untouched. |
| R-5 | `RunnerAlarmAttentionTests` (3 methods) | `AttentionService` still constructs with the alarm parameter alone; alarm rows unchanged. |
| R-6 | `CardClosedAttentionTests` (4 methods) | A task-keyed row family is unaffected by the new builders. |

### Guard inventory

| Guard | Invariant / decision | Positive control |
|---|---|---|
| G-1 | Blocked is idle, Dispatched/Working is active (D-3). | PC-1 |
| G-2 | Pooled warm is never idle or orphan (card, D-6). | PC-2 |
| G-3 | Severity boundaries inclusive at both thresholds (D-8). | PC-3 |
| G-4 | Error must exceed Warning (S1). | PC-4 |
| G-5 | `inFlight` includes pending launches (D-5). | PC-5 |
| G-6 | Idle-since for Blocked is the Blocked event (D-3). | PC-6 |
| G-7 | Unavailable inventory is recorded as such, with no seats (D-1). | PC-7 |
| G-8 | Retention prune runs (D-9). | PC-8 |
| G-9 | Disabled watch does nothing (D-9). | PC-9 |
| G-10 | No `SeatIdle` row below the warning age (D-8). | PC-10 |
| G-11 | Key carries runner and session (D-7). | PC-11 |
| G-12 | Divergence needs an idle seat (D-5). | PC-12 |
| G-13 | Orphan rows only for occupying slots (D-6). | PC-13 |
| G-14 | Unknown host is 404 (D-12). | PC-14 |
| G-15 | `limit` caps the result (D-12). | PC-15 |
| G-16 | Every kind is drawable (client). | PC-16 |

### Positive controls

Mutation stage; method-scoped; one compiling mutation each; restore before green.

| PC | Compiling mutation | Exact detecting filter | Expected red |
|---|---|---|---|
| PC-1 | `Classify`: return `Active` for `Blocked`. | `/*/Antiphon.Tests.Application/SeatOccupancyProjectionTests/C1079_Classify_names_blocked_terminal_and_unbound_seats_idle_and_working_seats_active` | The `c1079-class-Blocked` argument row fails. |
| PC-2 | `Classify`: ignore `pooledWarm`. | `/*/Antiphon.Tests.Application/SeatOccupancyProjectionTests/C1079_Pooled_warm_and_exited_seats_are_never_idle_or_orphan` | `PooledWarm` expectation fails. |
| PC-3 | `Severity`: `>` instead of `>=` at the warning threshold. | `/*/Antiphon.Tests.Application/SeatOccupancyProjectionTests/C1079_Severity_ladder_is_null_below_warning_then_warning_then_error` | The 30-minute row expects Warning, gets null. |
| PC-4 | Validator: drop the Error > Warning check. | `/*/Antiphon.Tests.Application/AttentionSettingsValidatorTests/C1079_Defaults_pass_and_error_not_above_warning_fails` | `Error == Warning` unexpectedly succeeds. |
| PC-5 | Sampler: omit `pendingLaunch` from `InFlight`. | `/*/Antiphon.Tests.Application/SeatOccupancySamplerTests/C1079_One_sample_per_host_with_in_flight_breakdown_and_dispatched_working` | `InFlight` is 3, expected 4. |
| PC-6 | `SeatDesktopJoin`: never populate `BlockedAt`. | `/*/Antiphon.Tests.Application/SeatDesktopJoinTests/C1079_Join_reports_latest_bound_task_blocked_at_pooled_warm_and_receipt` | `BlockedAt` null, expected the event time. |
| PC-7 | Sampler: write `"listed"` when inventory is unavailable. | `/*/Antiphon.Tests.Application/SeatOccupancySamplerTests/C1079_Unavailable_inventory_writes_a_sample_without_seats` | `InventoryState` assertion fails. |
| PC-8 | Sampler: skip the prune. | `/*/Antiphon.Tests.Application/SeatOccupancySamplerTests/C1079_Prune_removes_samples_older_than_retention` | 15-day row still present. |
| PC-9 | Hosted service: ignore `SeatWatchEnabled`. | `/*/Antiphon.Tests.Application/SeatOccupancySamplerTests/C1079_Disabled_watch_writes_nothing_and_publishes_nothing` | A row is written. |
| PC-10 | `BuildSeatIdleItems`: emit regardless of age. | `/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/C1079_Seat_idle_is_absent_below_the_warning_age` | A `SeatIdle` row appears at 29 minutes. |
| PC-11 | Key `seat-idle:{runnerId}` without the session. | `/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/C1079_Seat_idle_warning_names_runner_session_task_status_age_and_pushed` | `ConditionKey` mismatch. |
| PC-12 | `BuildOccupancyDivergenceItems`: drop the idle-seat requirement. | `/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/C1079_Occupancy_divergence_is_one_row_per_host_and_needs_an_idle_seat` | Host B gains a row. |
| PC-13 | `BuildSlotOrphanItems`: drop the `OccupiesCapacity` filter. | `/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/C1079_Slot_orphan_is_immediate_for_occupying_orphans_only` | The Exited orphan gains a row. |
| PC-14 | Endpoint: return `200 []` for an unknown host. | `/*/Antiphon.Tests.Application/HostOccupancySampleEndpointTests/C1079_Unknown_host_is_404` | Status 200, expected 404. |
| PC-15 | Endpoint: remove the `Take(limit)`. | `/*/Antiphon.Tests.Application/HostOccupancySampleEndpointTests/C1079_Limit_caps_rows_and_default_window_is_24_hours` | 5 rows, expected 2. |
| PC-16 | `ATTENTION_VISUALS.SeatIdle.label = ''`. | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts -t "CARD-1079"` | The label assertion fails. |

### Out of scope

- Any release, kill, park, cancel or dispatch from these rows (CARD-1065, CARD-0667).
- Collapsing the per-seat rows or the existing `BlockedQuestion` row (CARD-1085).
- A Blocked clock on the task itself or sync-block classification (CARD-1081).
- Bundle and owner-doc instruction lines about Blocked seats (CARD-1083).
- Seat rows for the desktop host (D-4) and a sampler-staleness kind (D-13).
- Windows-specific behaviour: none of the new code touches processes, paths or terminals.

### Execution and evidence

Each slice commits before its rows. Run rows through the checkpoint tool from the worktree
root with the committed SHA:

```powershell
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-06-card-1079-seat-idle-occupancy-attention-plan.md --after S1 --expected-source-sha <sha>
```

then `--after S2`, `--after S3`, `--after S4` (CP-8 is the Vitest row). `wait --max-wait 570s`
returns 75 while running; call `wait` again rather than ending the turn.

The migration is generated by a CLI build outside the table; report it as one unlisted build
with the reason "CLI migration, docs/project-context.md rule 9":

```powershell
dotnet tool restore
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1079-migration -- dotnet ef migrations add AddHostOccupancySamples --project server
```

Run `scripts/check-evidence-diff.ps1 -BaseRef <task base> -HeadRef <pushed sha>` before
reporting. Delete every `bin-c1079-*` directory before finishing. A new test that cannot go
red against the production line it guards is a stub (rule 4).

### Cost

Ordinary Code floor is the sum of `EstimatedMinutes`: **50 minutes** of builds and rows, plus
authoring: S1 55, S2 55, S3 50, S4 45. One Code task per slice at `-ExpectAbout` 65-70 minutes,
or one task at about 255 minutes. Mutation: 16 method-scoped PCs at about 8 minutes each,
**128 minutes**, after land.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1079-s1/` | seat-rules | `/*/Antiphon.Tests.Application/(SeatOccupancyProjectionTests*)\|(AttentionSettingsValidatorTests*)\|(SeatDesktopJoinTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6 | all 8 methods executed (19 results with the V-1 and V-3 argument rows), 0 failed/skipped | 19 | 9 | false |
| CP-2 | S1 | CP-1 | slot-regressions | `/*/Antiphon.Tests.Application/(RunnerSlotRulesTests*)\|(RunnerSlotEndpointTests*)/*` | R-1, R-2 | all 10 methods, 0 failed/skipped | 10 | 4 | false |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1079-s2/` | seat-sampler | `/*/Antiphon.Tests.Application/SeatOccupancySamplerTests/*` | V-7, V-8, V-9, V-10, V-11, V-12 | all 6 methods, 0 failed/skipped | 6 | 9 | false |
| CP-4 | S2 | CP-3 | host-regressions | `/*/Antiphon.Tests.Application/(HostEndpointTests*)\|(HostBudgetPipelineTests*)/*` | R-3, R-4 | all 8 methods, 0 failed/skipped | 8 | 4 | false |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1079-s3/` | seat-attention | `/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/*` | V-13, V-14, V-15, V-16, V-17, V-18, V-19, V-20 | all 8 methods, 0 failed/skipped | 8 | 9 | false |
| CP-6 | S3 | CP-5 | attention-regressions | `/*/Antiphon.Tests.Application/(RunnerAlarmAttentionTests*)\|(CardClosedAttentionTests*)/*` | R-5, R-6 | all 7 methods, 0 failed/skipped | 7 | 4 | false |
| CP-7 | S4 | `tests/Antiphon.Tests -> bin-c1079-s4/` | occupancy-endpoint | `/*/Antiphon.Tests.Application/HostOccupancySampleEndpointTests/*` | V-21, V-22, V-23 | all 3 methods, 0 failed/skipped | 3 | 8 | false |
| CP-8 | S4 | n/a | attention-client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts` | V-24 | every Vitest result in the file passes (the existing 23 `it` plus the `it.each` rows plus the new CARD-1079 `it`), 0 failed/skipped; `ALL_KINDS` lists SeatIdle, OccupancyDivergence and SlotOrphan | n/a | 3 | true |

## Publication and handoff

Plan artifact: this file. Importer validation (tool-only, no tests) is recorded in the commit
message and in the report. Next stage: **land** per the brief; the Code brief points at
`### Checkpoints` of this file at its landed SHA and runs the four slices in order.
