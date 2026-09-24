# CARD-0654: dynamic per-host budgets and runtime runner capacity

Date: 2026-09-24. Plan task: `ca27166b-576a-4171-b099-a516aed3e9d2` (Frontier, Worktree, run on the server2 runner).
Inspected checkout: `57543b855647b45ff866d02e3aa8ee525c0106d0` (= `origin/master` at 17:02Z).
Next: **Code**. Verification design is folded into this plan (the brief asks for red tests and a `### Checkpoints` table).

## Outcome and scope

Two numbers become runtime-editable through the API and a UI panel, persisted, audited, and
applied without a restart or redeploy:

1. **The desktop's per-host budget**: how many process-spawning delegated tasks the dispatcher
   admits on each execution host (`local` = the desktop session runner; each phone-home runner by
   its `runnerId`, today only `server2`).
2. **The runner's own declared capacity**: the seat count the runner enforces in its own launch
   gate, pushed to the runner over phone-home and persisted on the runner, with compose
   `PhoneHome__Capacity` demoted to the initial default.

Effective runner limit = **min(configured budget, live declared capacity)**. Lowering either number
below current occupancy never stops anything; it only holds new admissions. Every change writes an
audit incident and a required reason. `GET /api/agent-tasks/pipeline` reports per-host
in-flight/limit, and Held traces name the host budget (`server2 3/3`).

Round 1 (this Code dispatch) is S1–S4 below. Round 2 (weights / fill-then-overflow placement) and
the card's load-aware admission idea are designed to the extent of naming what they need and are
**not** in this dispatch (D-7, D-10). This plan changes no code and no running configuration.

## Ground truth

Read from `origin/master` on 2026-09-24 and the CARD-0654 board text (revision 5, InProgress).

| Card assumption | What the code does at the inspected SHA | Consequence for this plan |
|---|---|---|
| One dispatcher-wide cap counts desktop and server2 tasks together. | Since CARD-0653 the dispatcher's `active` query (`AgentTaskDispatcher.cs:373-381`) counts only rows with empty `RunnerId`, and the queued gate (`:484-487`) skips runner-bound tasks. Runner-bound tasks are held by `RemoteHoldForAsync` (`:881-888`) on `DeclaredCapacity` versus `CountRunnerOccupancyAsync` (sessions Created/Starting/Running/Stopping + prepared-unlaunched tasks + in-flight mirrors). | `Delegation:MaxConcurrentTasks` is already the desktop-only budget. This card generalises it into a per-host row and makes the runner's limit `min(budget, declared)`; it does not re-split anything. |
| The split is complete. | Two paths still count every host: `CapacityRecoveryService.TryClaimCountedSlotAsync` (`:693-698`, used by the retained-return loop at dispatcher `:463`) and `AgentTaskPipelineStatusService` `inFlightRows` (`:114-116`) feeding `InFlightAgainstCap` and `QueueReasonConcurrencyCap`, although the DTO comment claims dispatcher parity. | S2 fixes both with the same host classification (`RunnerId` empty = local). |
| Runner capacity is `PhoneHome__Capacity` (2) in compose. | Compose `docker-compose.server2-runner.yml:54` is `"10"` (CARD-0653 commit `8bbeb1fb`); server bound `PhoneHomeRunner:MaxCapacity` default 10 (`PhoneHomeRunnerSettings.cs:31`). The runner reads `IOptions<PhoneHomeSettings>.Value` once at boot (`Program.cs:48-57`) and enforces `OwnedSessionCount >= _settings.Capacity` under the mutation lock (`PhoneHomeCommandDispatcher.cs:250-258`). Server-side `PhoneHomeLiveConnection.Capacity` is get-only, copied from the registration ticket (`PhoneHomeRunnerDirectory.cs:155-158, 219-223`). | Both sides are boot-fixed today. A server-only override cannot raise the limit (the runner's own gate would refuse with `phone_home_capacity`), so the runner must receive and persist the new value (D-3). |
| Capacity could come back in the heartbeat or registration reply. | Heartbeats are one-way `PhoneHomeFrameKind.Heartbeat` frames with no reply (`PhoneHomeConnectionService.cs:256-271`, `PhoneHomeLiveConnection.cs:154-156`). The registration response is only read at (re)connect. | A push needs a new request operation; the reply path and registration are the wrong carriers (D-3 rejected alternatives). |
| Runtime-editable settings need a new mechanism. | `ModelAvailability` (PUT `/api/model-availability/{kind}/{alias}`) and `RoutingPin` are DB-backed operator controls consulted live by the dispatcher; `DelegationSettings` is injected as `IOptions<>.Value` and captured in fields (`AgentTaskDispatcher.cs:34,115`; pipeline status `:41,47`), so options reload would not reach them anyway. | Follow the ModelAvailability shape: entity + service + PUT, read per tick (D-4). |
| Force-release's operator token is the auth pattern. | `RequireOperator` (`SessionRunnerEndpoints.cs:133-140`) gates only the two kill routes; the token lives in an owner-only file and the browser client never sends it (no `Operator-Token` reference under `client/src`). | Budget and capacity writes are non-destructive and reversible; they use the same no-token shape as model-availability and routing pins, with a required reason and an audit incident instead (D-6). |
| Audit is a new concept. | CARD-0653 audits through `AgentIncident` rows (`RunnerSlotForceReleased = 74`, `RunnerSlotReleaseIntent = 75`) with `Message`, `FailureReason` and `Severity`. Held traces are `AgentTaskEvent` rows of type `Held`, deduplicated per detail (`TraceHeldAsync`). | Reuse both: incidents for operator changes, Held events for per-task holds naming the host budget (D-9). |
| CARD-0659's default runner is in play (S1 on master). | `DefaultRunnerRoutingPolicy.Decide` selects `Delegation:DefaultRunnerId` at create time when the shape is eligible and `Resolve` succeeds; it never consults occupancy. Its plan's D-6 says "Full server2 never becomes an automatic local overflow trigger" and defers weights/overflow to CARD-0654. | Round 1 leaves placement as it is and makes the resulting hold visible. Overflow is Round 2 (D-7). |
| Per-role and per-project caps are separate. | `DelegationOpenGate` applies `MaxOpenTasks` per project and `RecommendedInFlightFor(role)` at create time, host-agnostic. | Unchanged; documented composition in D-8. |
| The runner directory knows the hosts. | `PhoneHomeRunnerDirectory.KnownRunnerIds` and `PhoneHomeProtocol.LocalRunnerId = "local"` exist; `DeclaredCapacity` returns null unless the connection is live, dispatch-eligible and unexpired. | Host list = `local` + `KnownRunnerIds`; an offline runner keeps the existing `RunnerUnavailable` hold ahead of any budget check. |
| The UI has a natural home. | `OrchestratorPage.tsx` tabs: cards, delegations, pipeline (`PipelineStagesPanel`), history, attention (`ModelAvailabilityPanel`, `ComplexityChainPanel`, `AttentionPanel`). `client/src/api/modelAvailability.ts` is the react-query + `apiPut` pattern; msw handlers live in `client/src/test/mocks/handlers.ts`. | `HostsPanel` above `PipelineStagesPanel` on the pipeline tab; `client/src/api/hosts.ts` mirrors `modelAvailability.ts`. |

## Decisions

Stated defaults for anything the card left open are enumerated as D-n; none needs a fresh
approval under the caller's standing authority ("make that more dynamic via UI/api").

### D-1. One persisted `HostBudget` row per execution host

Entity `HostBudget` (table `HostBudgets`): `HostId` (PK, string ≤ 64: `local` or a runner id),
`MaxInFlight` (int?, null = no budget row value; fall back to `Delegation:MaxConcurrentTasks` for
`local`, unbounded for a runner), `Reason` (≤ 400), `UpdatedAt`, `Revision` (int, +1 per write).
Migration `AddHostBudgets`. No `Weight` column yet (Round 2 adds its own migration).

`MaxInFlight = 0` is legal and means "admit nothing new on this host" (a drain switch).
Upper bound 512 (the value the test-suite uses as unbounded). Unknown host ids are 404; the
known set is `local` plus `ISessionRunnerDirectory.KnownRunnerIds`.

Rejected: `IOptionsMonitor` + appsettings reload (no API/UI, no runner push, and the consumers
capture `.Value`); a column on `RoutingPin`/`ModelAvailabilityHold` (wrong domain); a JSON blob
setting (no per-row audit or revision).

### D-2. Effective limit = min(configured budget, live declared capacity)

`HostBudgetService.EffectiveAsync(hostId)` returns `HostLimit(HostId, Configured, Declared,
Effective, Source)`:

- `local`: `Declared` is null; `Effective = Configured ?? Delegation:MaxConcurrentTasks`;
  `Source = budget | config`.
- runner: `Declared = ISessionRunnerDirectory.DeclaredCapacity(runnerId)`; when null the host is
  not admitting anyway (the existing `RunnerUnavailable` gate runs first and is unchanged);
  otherwise `Effective = Configured is null ? Declared : min(Configured, Declared)`.

The runner's declared capacity stays the hard ceiling (card text); the budget can only lower it.

### D-3. Runner capacity is pushed with a new phone-home operation and persisted on the runner

Contracts: `PhoneHomeOperation.SetCapacity = 26`, `PhoneHomeSetCapacityRequest(int Capacity,
string Reason)`, `PhoneHomeSetCapacityResponse(int Capacity, bool Persisted, string? Path)`.

Runner: a mutable singleton `RunnerCapacityState` (initial value: persisted file if present, else
`PhoneHome:Capacity`), file `PhoneHome:CapacityStatePath` default `/state/runner-capacity`,
written atomically the way `PhoneHomeStoreIdentity` writes the store id. `PhoneHomeCommandDispatcher.LaunchAsync`
and `PhoneHomeConnectionService` registration read the state, not `_settings.Capacity`.
`SetCapacity` runs under the mutation lock, validates `1 ≤ capacity`, persists, then applies; a
persist failure answers `runner_internal_error` and leaves the old value.

Server: `PhoneHomeRunnerDirectory.SetDeclaredCapacityAsync(runnerId, capacity, reason)`: bound by
`PhoneHomeRunner:MaxCapacity` (422 `phone_home_capacity` above it), sends `SetCapacity` through
the live connection, and on a successful reply updates the live connection's capacity under
`_gate` (the property gains a directory-only setter). Error mapping mirrors `RequestProviderAuthAsync`:
`phone_home_unavailable` (409) when not live; `phone_home_unsupported_operation` (409) for an older
runner binary; request timeout → 409 `phone_home_request_timeout`. No change is recorded on any
failure. A reconnecting runner re-registers with its persisted value, so the server's view survives
runner restarts; a server restart learns it from the next registration.

Precedence on the runner: persisted file > `PhoneHome__Capacity`. If an operator later lowers
`PhoneHomeRunner:MaxCapacity` below the persisted value, registration is refused exactly as today
(CARD-0604 D-14); recovery is raising the server bound or deleting `/state/runner-capacity`. The
server never persists a value it did not itself bound, so this cannot happen from the API.

Rejected: heartbeat reply (no reply frame exists); registration response (applies only on
reconnect, so a lowered value would not protect the runner until it dropped); server-only override
without telling the runner (raising would fail every launch above the boot value with
`phone_home_capacity`); restarting the runner container from the server (a redeploy by another name).

### D-4. Budgets are read live, not from `IOptions`

`HostBudgetService` is scoped and reads the `HostBudgets` table once per dispatcher tick and once
per pipeline read (one small table; the tick already runs many queries). `Delegation:MaxConcurrentTasks`
remains the `local` fallback so an empty table behaves exactly as master. No cache, no reload
event, no restart.

### D-5. Lowering holds new admissions only; nothing is killed

Budgets are consulted only at the dispatcher's admission points: the retained-return loop, the
queued local gate, `TryClaimCountedSlotAsync`, and `RemoteHoldForAsync`. Dispatched/Working tasks
and live sessions are never touched by a budget change, and `SetCapacity` on the runner only
changes the launch gate. Occupancy above the limit is reported, not corrected:
`local 8/6 (over budget; draining)`. A red test asserts that lowering `local` to 1 with three
Working tasks leaves all three Working and records no runner stop, kill or release call.

### D-6. API surface and authorization

| Route | Body / answer | Rules |
|---|---|---|
| `GET /api/hosts` | `HostBudgetDto[]`: `hostId`, `kind` (`local`/`runner`), `configuredMaxInFlight`, `declaredCapacity`, `effectiveLimit`, `inFlight`, `occupiedBreakdown` (runner: sessions / pendingLaunch / inFlightMirrors), `available`, `dispatchEligible`, `source`, `reason`, `updatedAt`, `revision` | Read-only; same numbers the dispatcher uses. |
| `PUT /api/hosts/{hostId}/budget` | `{ maxInFlight: int|null, reason: string }` → `HostBudgetDto` | 404 unknown host; 422 out of range or missing reason; null clears to the config fallback. |
| `GET /api/session-runners/{runnerId}/capacity` | `{ runnerId, declaredCapacity, maxCapacity, configuredMaxInFlight, effectiveLimit, occupied, available, dispatchEligible }` | Read-only. |
| `PUT /api/session-runners/{runnerId}/capacity` | `{ capacity: int, reason: string }` → same DTO | 422 above `MaxCapacity` or < 1; 409 `phone_home_unavailable` / `phone_home_unsupported_operation` / `phone_home_request_timeout` with no change. |
| `GET /api/agent-tasks/pipeline` | gains `hosts: HostLimitSummaryDto[]` (`hostId`, `inFlight`, `effectiveLimit`, `configured`, `declared`, `source`); `inFlightAgainstCap` becomes local-only | Queue reason for a runner-bound row held on its host is `HostBudget` with detail `server2 3/3`. |

No operator token: these writes are reversible and non-destructive, and the browser has no way to
present the file token. A required `reason` and an audit incident are the accountability, the same
posture as model-availability and routing-pin writes. Rejected: reusing `RequireOperator` (would
make the UI control impossible without a token-entry field, which would then live in browser
storage).

### D-7. Placement policy in Round 1 is CARD-0659's, made visible; overflow is Round 2

Round 1: explicit `-Runner` > `Delegation:DefaultRunnerId` when eligible > local, unchanged. A
default-runner task on a full runner stays Queued with a Held trace `Held: host 'server2' budget
3/3 (configured 3, runner declares 10); waiting for a seat.` and the pipeline shows the host at
its limit. Rejected for Round 1: consulting occupancy at create time (racy across ticks, and it
rewrites 0659's tested `Decide` contract and its D-6).

Round 2 (follow-on card, to be filed by the caller): `HostBudget.Weight` (int?, default null) and
`Delegation:PlacementPolicy = FillDefaultThenOverflow | Proportional`; the dispatcher (not create)
re-places an unpinned runner-eligible task from a full default runner to `local` when `local` has
headroom, recording `runner_budget_full` on the task timeline; `DefaultRunnerRoutingPolicy` and 0659
D-6 are amended there. The Round 1 entity and DTOs are shaped so Round 2 adds columns, not tables.

### D-8. Composition with per-role and per-project gates

Create-time gates (`MaxOpenTasks` per project, `RecommendedInFlightFor(role)`, `-IgnoreConcurrencyLimit`)
are host-agnostic and run first; host budgets are dispatch-time and run second. A task admitted
by the create gates can still queue on its host budget, and a host with headroom never admits a
task the create gates refused. Documented in `docs/orchestration-loop.md` and `docs/docker-stack.md`.

### D-9. Audit and traces

`AgentIncidentKind.HostBudgetChanged = 76` and `RunnerCapacityChanged = 77`. Success rows are
`Severity = Info` with `Message = "Host 'server2' budget 10 -> 3 (effective 3, runner declares 10): <reason>"` /
`"Runner 'server2' capacity 10 -> 6 (runner persisted at /state/runner-capacity): <reason>"`. A
refused capacity push is `Severity = Warning`, same kind, `FailureReason = <problem code>`, so an
operator sees the attempt. Per-task holds keep using `AgentTaskEvent.Held` with the new host-budget
detail, deduplicated per detail as today. `AttentionService` is not changed; incidents already
surface through the existing incident feed.

### D-10. Out of scope, named

Load-aware admission (hold local launches while CPU is saturated) and the readiness-timeout
retry from the card's evidence need a host load probe and a launch-policy change; they are a
separate card. Codex on server2 is CARD-0660. Defender exclusions are an operator action.

## Code slices

Branch: the Code task's own branch off `origin/master`. Commit each slice; the red scaffold of a
slice is its own commit before the green one. Isolated output path `bin-c654/` (forward slash).

### S1. Entity, migration, service, incident kinds (server)

- `server/Domain/Entities/HostBudget.cs`; `AppDbContext.HostBudgets`; migration
  `server/Migrations/2026092417xxxx_AddHostBudgets.cs`; `AgentIncidentKind` 76/77.
- `server/Application/Services/HostBudgetService.cs`: `ListAsync`, `EffectiveAsync(hostId)`,
  `UpsertAsync(hostId, maxInFlight, reason)` (validation, revision, incident), `KnownHostIds`.
- `server/Application/Dtos/HostBudgetDtos.cs`.
- Tests (`tests/Antiphon.Tests/Application/HostBudgetServiceTests.cs`, Integration, isolated
  schema, `PhoneHomeTestHost` + scripted peer for the runner side): effective = min; null budget
  falls back to `Delegation:MaxConcurrentTasks` for `local` and to declared for a runner; 0 is
  legal; 513 and −1 are 422; missing reason is 422; unknown host is 404; each write adds one
  `HostBudgetChanged` incident naming old, new and reason; revision increments.

### S2. Admission and visibility (server)

- `AgentTaskDispatcher`: replace the three `_settings.MaxConcurrentTasks` reads with the local
  effective limit from `HostBudgetService`; `RemoteHoldForAsync` uses the runner's effective limit;
  `DispatchHoldDetails.HostBudget(hostId, occupied, effective, configured, declared)`; `HoldKind.HostBudget`
  replaces `RunnerCapacity` for the runner path (the old detail text is kept as a fallback when no
  budget row exists so existing CARD-0653 assertions still hold — see CP-4 roster).
- `CapacityRecoveryService.TryClaimCountedSlotAsync`: count only `RunnerId` empty rows (parity fix).
- `AgentTaskPipelineStatusService` / DTO: `hosts` summary, local-only `InFlightAgainstCap`,
  `QueueReasonHostBudget` for runner-bound rows; the panel's cap text unchanged for local.
- Tests (`HostBudgetAdmissionTests`, dispatcher harness in the `DispatchHoldVisibilityTests` shape;
  `AgentTaskPipelineStatusTests` additions):
  lowering `local` to 1 with three Working tasks holds the fourth with the local detail and leaves
  the three Working with zero runner stop/kill/release calls on the recording client; runner budget
  3 with declared 10 holds the fourth runner-bound task with `server2 3/3 (configured 3, runner
  declares 10)`; budget 0 holds every task on that host; raising the budget releases the hold on
  the next tick; retained-return honours the local limit and ignores runner-bound rows; pipeline
  `hosts` reports both hosts and `inFlightAgainstCap` excludes runner-bound rows.

### S3. Runner capacity push (contracts, runner, server)

- `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`: operation 26 and the two records.
- Runner: `RunnerCapacityState.cs` (file-backed), `PhoneHomeSettings.CapacityStatePath`,
  `PhoneHomeCommandDispatcher` `SetCapacity` branch + `LaunchAsync` reads the state,
  `PhoneHomeConnectionService` registers with the state, `Program.cs` wiring.
- Server: `PhoneHomeLiveConnection.Capacity` directory-settable; `PhoneHomeRunnerDirectory.SetDeclaredCapacityAsync`;
  `ISessionRunnerDirectory` default member; endpoints GET/PUT `/api/session-runners/{runnerId}/capacity`
  in `SessionRunnerEndpoints.cs`; `RunnerCapacityChanged` incident on success and on refusal.
- Tests, runner (`tests/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs`,
  `PhoneHomeConnectionServiceTests.cs`): `SetCapacity_persists_and_bounds_the_next_launch`
  (red on master: the operation answers `phone_home_unsupported_operation`); `SetCapacity_below_one_is_refused_and_keeps_the_old_value`;
  `Registration_sends_the_persisted_capacity_over_the_compose_default`.
- Tests, server (`tests/Antiphon.Tests/Application/RunnerCapacityEndpointTests.cs`, scripted peer):
  PUT 6 → the peer receives `SetCapacity{6}` and `DeclaredCapacity` is 6 without a reconnect;
  PUT 11 → 422 and no frame sent; peer `SilentFor(SetCapacity)` → 409 timeout, capacity unchanged,
  Warning incident; peer replying `phone_home_unsupported_operation` → 409 same code, unchanged;
  GET reports `effectiveLimit = min(budget, declared)` after a budget row of 3 is written.

### S4. Hosts API, UI panel, docs

- `server/Api/Endpoints/HostEndpoints.cs` (`GET /api/hosts`, `PUT /api/hosts/{hostId}/budget`),
  registered in `Program.cs`; `HostEndpointTests.cs` (404 unknown host, 422 reason, round trip,
  pipeline `hosts` agrees with `GET /api/hosts`).
- `client/src/api/hosts.ts` (`useHosts`, `usePutHostBudget`, `usePutRunnerCapacity`, 15 s refetch);
  `client/src/features/orchestrator/HostsPanel.tsx` (one row per host: in-flight/limit, editable
  budget with reason, editable runner capacity, available/dispatch-eligible badge, source tag,
  over-budget marker); mounted above `PipelineStagesPanel` on the pipeline tab; msw handlers;
  `HostsPanel.test.tsx` (renders both hosts, submits a budget PUT with the reason, shows the 409
  problem code from a refused capacity push, shows the over-budget marker).
- Docs: `docs/ops-http.md` (new rows), `docs/antiphon-api.md` (routes and pipeline fields),
  `docs/docker-stack.md` (compose value is the initial default; persisted file precedence;
  recovery when `MaxCapacity` is lowered), `docs/orchestration-loop.md` (D-8 composition),
  `docs/session-runtime-invariants.md` (a budget change never stops a session).

## Verification design

### Test boundaries and red evidence

Isolated Postgres schemas (`TestDbFixture.CreateIsolatedSchemaAsync`), `PhoneHomeTestHost` with
`PhoneHomeScriptedPeer` for every runner interaction, the recording local client for "no kill"
assertions, and the dispatcher harness already used by `DispatchHoldVisibilityTests`. No live
runner, no paid agent, no production port. Runner-side tests run in
`tests/Antiphon.SessionRunner.Tests` (its own project and build row). Client tests run under
`scripts/test-client.ps1`. Each slice commits its tests plus only the declarations needed to
compile against unchanged behaviour, runs its red row, then implements and runs its green row.
A red row must fail on a named behaviour assertion; a missing type, a build error or zero
discovered tests is not red evidence. Existing CARD-0653 classes (`RunnerSlotEndpointTests`,
`RunnerSlotRulesTests`, `PhoneHomeDirectoryTests`) and the CARD-0412 `CapacityRecoveryTaskTests`
are regressions in the final row.

### Coverage

| ID | Invariant | Evidence |
|---|---|---|
| V-1 | Effective limit is min(configured, declared); null falls back; 0 drains; bounds and reason enforced; unknown host 404; audit + revision per write. | `HostBudgetServiceTests` |
| V-2 | Lowering a budget below occupancy holds new admissions only; running tasks and sessions untouched; raising releases next tick; retained-return honours the local limit. | `HostBudgetAdmissionTests` |
| V-3 | Held trace names the host budget; pipeline `hosts` and local-only `inFlightAgainstCap`; runner-bound rows report `HostBudget`. | `HostBudgetAdmissionTests`, `AgentTaskPipelineStatusTests` |
| V-4 | Runner applies and persists `SetCapacity`, next launch is bounded by it, registration sends the persisted value; below 1 refused. | runner tests |
| V-5 | Server PUT capacity bounds by `MaxCapacity`, pushes, updates the live view without reconnect, and records nothing on unavailable/unsupported/timeout. | `RunnerCapacityEndpointTests` |
| V-6 | Hosts API and panel round trip; refused push is shown; over-budget marker. | `HostEndpointTests`, `HostsPanel.test.tsx` |
| R-1 | CARD-0653 seat accounting, force-release and directory registration bounds unchanged. | regression row |
| R-2 | CARD-0412 counted-slot claim still gates retained returns after the parity fix. | regression row |

### Later Mutation controls

PC-1 `min` → `max` in `EffectiveAsync` (V-1 red). PC-2 drop the `RunnerId` predicate in
`TryClaimCountedSlotAsync` (V-2 retained-return red). PC-3 skip the file write in
`RunnerCapacityState.Apply` (V-4 persisted-registration red). PC-4 update the live capacity before
the peer answers (V-5 silent-peer red). Method-scoped filters only.

### Cost and execution rules

Ordinary Code floor is the sum of `EstimatedMinutes` (26). `Antiphon.Tests` and
`Antiphon.SessionRunner.Tests` never run concurrently. Delete every `bin-c654` directory at the end.

### Checkpoints

`Sx-red` is the committed compilable scaffold; `Sx` the committed implementation.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c654/` | budget-red | `/*/*/HostBudgetServiceTests/*` | V-1 red | all listed; effective/audit assertions red, no build/fixture errors | 7 | 3 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c654/` | budget-green | `/*/*/HostBudgetServiceTests/*` | V-1 | all listed, 0 failed/skipped | 7 | 3 |
| CP-3 | S2-red | `tests/Antiphon.Tests -> bin-c654/` | admission-red | `/*/*/HostBudgetAdmissionTests/*` | V-2, V-3 red | all listed; hold/trace/no-kill assertions red, no build/fixture errors | 6 | 3 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c654/` | admission-green | `/*/*/(HostBudgetAdmissionTests*)\|(AgentTaskPipelineStatusTests*)\|(DispatchHoldVisibilityTests*)/*` | V-2, V-3 | all listed, 0 failed/skipped | 6 | 4 |
| CP-5 | S3-red | `tests/Antiphon.SessionRunner.Tests -> bin-c654/` | runner-red | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(PhoneHomeConnectionServiceTests*)/*` | V-4 red | all listed; `SetCapacity_*` and persisted-registration assertions red (unsupported-operation answer), no build errors | 3 | 2 |
| CP-6 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c654/` | runner-green | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(PhoneHomeConnectionServiceTests*)/*` | V-4 | all listed, 0 failed/skipped | 3 | 2 |
| CP-7 | S3 | `tests/Antiphon.Tests -> bin-c654/` | capacity-green | `/*/*/(RunnerCapacityEndpointTests*)\|(PhoneHomeDirectoryTests*)/*` | V-5, R-1 | all listed, 0 failed/skipped (its red run is the same filter on the S3-red commit, reported as CP-7 rerun 1) | 5 | 3 |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c654/` | hosts-api | `/*/*/HostEndpointTests/*` | V-6 | all listed, 0 failed/skipped | 4 | 2 |
| CP-9 | S4 | n/a | client | `pwsh -File scripts/test-client.ps1 -Filter HostsPanel` | V-6 | 4 tests, 0 failed; lint 0 errors | n/a | 2 |
| CP-10 | all | CP-8 | regressions | `/*/*/(RunnerSlotEndpointTests*)\|(RunnerSlotRulesTests*)\|(CapacityRecoveryTaskTests*)\|(AgentTaskConcurrencyLimitTests*)/*` | R-1, R-2 | all listed, 0 failed/skipped | 12 | 2 |

The Code brief points at this table as
`checkpoints: docs/superpowers/plans/2026-09-24-card-0654-dynamic-host-budgets-plan.md@<plan commit> section "### Checkpoints"`.
