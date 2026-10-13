# CARD-1219 test design: remote transcript-bind and transcript-fault events on the phone-home path

Date: 2026-10-13. Stage: TestDesign; next stage: Code. TestDesign task `56f322b0` (bound run: the
task token was present; nothing from the environment is reproduced here). Plan:
`docs/superpowers/plans/2026-10-13-card-1219-remote-transcript-events-plan.md` at
`cc0067c8102e32e7953d5dbb402976690614cd6d` (this branch starts at that commit; the plan lands with
the first Code slice). Inspected checkout: `cc0067c81`; the branch carries no source change. This
document is a design only: no production or test code, no builds, no test runs; counts below are
read from source. Operator decisions are taken at the plan's recommendations: OD-1 replay window
900 s, OD-2 one Code task for S1+S2, OD-3/OD-4 follow-up cards are the orchestrator's to file.

Session checklist: the design adds or changes no session that waits for input; the CARD-1083
release checklist does not apply.

## Audit of plan decisions D-1..D-8

| Decision | Checked against | Verdict and correction |
|---|---|---|
| D-1 mapper branches, no contract change | `RunnerContractMapper.cs:75-124` (five branches, `return null` tail; `Json` is `JsonSerializerDefaults.Web`); `SessionRunnerHttpClient.cs:1007-1032` (SSE shape); `SessionRunnerDtos.cs:119-131,142-151` (`SessionRunnerEvent` slots: Output, Exited, Transcript, Adopted, TranscriptFault, TranscriptBound, AgentStatus, SubscriptionReady; there is no `Started` slot); `SessionRunnerContracts.cs:895-911` (runner records with defaults `UnboundSeconds = 0`, `Repeat = 1`) | Holds. One fact the plan did not state and the tests depend on: the runner hub serialises with default options (`SessionRunnerRuntime.cs:4475`, `JsonSerializer.Serialize(payload)`), and `PhoneHomeConnectionService.SendEventAsync` (`:359-364`) re-reads that text into a `JsonElement`, so the wire spelling is PascalCase (`"SessionId"`, `"UnboundSeconds"`). The Web options parse it case-insensitively. The mapper tests therefore cover both spellings and the pump tests emit the hub spelling. |
| D-2 replay guard keyed on the parsed record, per runner, in memory | `PhoneHomeRecoveryPump.cs:25` (`_cycles` keyed by runner id), `:28` and `:442-447` (`ConnectionState` is per connection through a `ConditionalWeakTable`); records are positional `sealed record`s, so value equality covers each field and the `EqualityContract` keeps fault and bound keys from colliding | Holds, with one correction for the house rule "monotonic time for deadlines": the guard owns the connection clock and stamps keys with `TimeProvider.GetTimestamp()`, compared through `GetElapsedTime`, instead of taking a `DateTimeOffset now`. Signature: `TranscriptIncidentReplayGuard(TimeProvider clock, TimeSpan window, int capacity = 512)` with `bool Admit(SessionRunnerTranscriptFaultEvent)` and `bool Admit(SessionRunnerTranscriptBoundEvent)`. `Admit` runs under a lock: during a reconnect handoff the ending pump and the new pump can overlap on the same runner id. `FakeTimeProvider.Advance` moves its timestamp with its wall clock, so the unit tests below stay as designed. |
| D-3 window as a setting | `PhoneHomeRunnerSettings.cs` (`PhoneHomeRunnerSettingsRules.ValidateGlobal`, `:273-300`, carries the `CatchUpRetrySeconds must be at least 1.` rule for both legacy and map mode); `PhoneHomeRunnerSettingsValidator.cs` is a wrapper that calls the rules | Holds; file list corrected. The property and the rule both go in `PhoneHomeRunnerSettings.cs` (`ValidateGlobal`, so map-mode runners are covered); `PhoneHomeRunnerSettingsValidator.cs` is not edited. Message: `PhoneHomeRunner:TranscriptIncidentReplayWindowSeconds must be at least 1.` |
| D-4 dispatch after the owner check | `PhoneHomeRecoveryPump.cs:356-400` (`OwnerMatchesAsync` at `:377`, scope at `:380`, the three arms at `:382-387`, per-event catch and `ReleaseEvent` in `finally`); `Program.cs:758` registers `TranscriptBindingIncidentService` as a singleton, so `scope.ServiceProvider.GetRequiredService` resolves in production; `SessionRunnerEventPump.cs:421-434` is the local call shape | Holds. Added binding for the refused-replay log so a test can read it: Debug level, message template `Phone-home {EventName} for session {SessionId} on runner {RunnerId} epoch {Epoch} is a replay inside {WindowSeconds}s; not recorded again`. |
| D-5 local lane untouched | `TranscriptBindingIncidentTests.cs`: 15 `[Test]` methods, no `[Arguments]`, `[NotInParallel("TranscriptBindingIncidents")]` | Holds. |
| D-6 exact binds stay silent | `TranscriptTailer.cs:693-699` publishes bound only when `how is not Exact and not Sidecar`; `CodexTranscriptTailer.cs:514-518` publishes for any non-sidecar bind | Holds. |
| D-7 `SessionAgentStatus` stays unmapped | `RunnerContractMapper.ParseEvent` has no branch for it; `Unknown_event_name_still_returns_null` pins the null | Holds. |
| D-8 no outage reconstruction | `PhoneHomeRecoveryPump.CatchUpAsync` (`:295-347`) reads List and transcripts only | Holds; the replay test states what a reconnect does and does not deliver. |

Two further corrections to the plan's test sketches:

- The plan's red signal for the foreign-owner case ("incident rows for the foreign session") is wrong
  as written: a session with no owning agent takes the service's unowned branch, which writes no
  incident row and raises a standalone alert instead (`TranscriptBindingIncidentService.cs:55-83`).
  The foreign sessions below are seeded with an owning task row, so a bypassed owner check writes
  a real incident row, and the test also asserts zero alerts for those session ids.
- `live.PendingEvents` is incremented when the receive loop accepts a frame
  (`PhoneHomeLiveConnection.cs:456-467`) and decremented in the pump's `finally`
  (`ReleaseEvent`, `:544`). A wait on `PendingEvents == 0` alone can pass before the frame has
  arrived. Each wait below pairs it with a positive observable that proves the frame of interest
  was processed: the incident row for a positive case, the refused-replay Debug line for a refusal,
  and an ordering sentinel (a `SessionOutput` frame on the owned session, observed as an
  `AgentTextDelta` on the harness event bus) for a frame that must leave no trace. The event
  channel is a single FIFO reader, so a sentinel observed after the frame proves the frame was
  fully handled. No fixed delay and no retry anywhere.

## Fixture facts confirmed

- `PhoneHomeTestHost.StartAsync(clock, connectionString)` injects the clock into
  `PhoneHomeRunnerDirectory` (`PhoneHomeTestHost.cs:116`), which hands it to each
  `PhoneHomeLiveConnection.Clock` (`PhoneHomeRunnerDirectory.cs:381`). `PhoneHomeOutageHarness`
  already starts the host on a `FakeTimeProvider` (`:52-53`). A frozen clock keeps the lease,
  ticket TTL and the owner negative cache from expiring, and keeps the replay guard inside its
  window; nothing in the pump tests advances it. `Microsoft.Extensions.TimeProvider.Testing 9.5.0`
  is already referenced by `tests/Antiphon.Tests`.
- Composition: `BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString,
  AlwaysOn = false, ConfigureServices = s => s.AddSingleton<TranscriptBindingIncidentService>() })`
  on `TestDbFixture.CreateIsolatedSchemaAsync()`. The harness registers `AgentSupervisorService`,
  `IAlertService -> AlertService` and `IAlertRouter -> NullAlertRouter` (`BridgeQueueHarness.cs:198-200`)
  on the isolated schema, which is what the service resolves through its scope factory. Read the
  schema with `new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString))`,
  not `BridgeQueueHarness.CreateContext()` (that one opens the shared database).
- `AlertService.RaiseAsync` inserts one `Alerts` row per raise (`AlertService.cs:35-60`); dedup is
  the router's concern. Alert counts therefore track incident counts one to one for alerting kinds.
- Fault incident text with no channel binding (`TranscriptBindingIncidentService.cs:90-122`):
  `No transcript is bound to this session. {Kind}: {Detail} (candidate {CandidatePath})`,
  `FailureReason = Kind`, `Severity = Warning`; the alert carries
  `DedupKey = supervisor:TranscriptBindFailed:{agentId:D}` and `Title = TranscriptBindFailed: agent supervision`
  (`AgentSupervisorService.cs:568-578`). Bound text: `Transcript bound by {How}: {TranscriptPath}`
  (`:305`), `Severity = Info`, no alert (`raiseAlert: false`). `TranscriptBindingSettings.StuckAfterMinutes`
  defaults to 30, so the `UnboundSeconds` values below (184.5 and 300) raise no `TranscriptBindStuck` row.

## Frozen tests

Fixed literals used throughout (hand-written; the tests do not derive them from production code):

| Name | Value |
|---|---|
| `Sid` | `11111111-2222-4333-8444-555555551219` |
| `FaultDetail` | `no prompt in it matches input delivered to this session` |
| `FaultPath` | `/home/a/.claude/projects/x/37512455.jsonl` |
| `BoundPath` | `/home/a/.codex/sessions/2026/10/13/rollout-abc.jsonl` |
| `OldDetail` | `No cwd-matching transcript candidates after 60s` |
| `FaultMessage` | `No transcript is bound to this session. AdoptionRefused: no prompt in it matches input delivered to this session (candidate /home/a/.claude/projects/x/37512455.jsonl)` |
| `BoundMessage` | `Transcript bound by discovery: /home/a/.codex/sessions/2026/10/13/rollout-abc.jsonl` |
| `ReplayWindow` | 900 s (OD-1) |
| `Epoch0` | `FakeTimeProvider` start `2026-10-13T12:00:00Z` |

### Mapper: `tests/Antiphon.Tests/Application/RunnerContractMapperTranscriptEventTests.cs`

`[Category("Unit")]`, no host, no database. Recipe: call `RunnerContractMapper.ParseEvent(name, json)`
directly. Reachability: the production line is the two new branches in `ParseEvent`; the hub
spelling is produced the way the runner produces it (`JsonSerializer.Serialize(record)` with default
options, `SessionRunnerRuntime.cs:4475`) and the Web spelling with `PhoneHomeFraming.Json`.
Expected records are hand-written `new SessionRunnerTranscriptFaultEvent(...)` /
`new SessionRunnerTranscriptBoundEvent(...)` literals compared by record equality.

| Method | Case and variant | Decisive oracle | Mutation -> red |
|---|---|---|---|
| `Fault_frame_round_trips_every_field` | `new RunnerTranscriptFaultEvent(Sid, TranscriptFaultKinds.AdoptionRefused, FaultDetail, FaultPath, 184.5, 3)` serialised twice: hub spelling `{"SessionId":"11111111-2222-4333-8444-555555551219","Kind":"AdoptionRefused","Detail":"<FaultDetail>","CandidatePath":"<FaultPath>","UnboundSeconds":184.5,"Repeat":3}` and Web spelling (`sessionId`, `kind`, `detail`, `candidatePath`, `unboundSeconds`, `repeat`); one assertion block per spelling | `EventName == "SessionTranscriptFault"`, `SessionId == Sid`, `TranscriptFault == new SessionRunnerTranscriptFaultEvent(Sid, "AdoptionRefused", FaultDetail, FaultPath, 184.5, 3)`; `Output`, `Exited`, `Transcript`, `Adopted`, `TranscriptBound`, `AgentStatus` null; `SubscriptionReady == false` | PC-1 delete the fault branch -> `ParseEvent` null -> red at the `ShouldNotBeNull`; PC-3 pass `UnboundSeconds: 0` -> red at record equality (184.5 expected) |
| `Bound_frame_round_trips_every_field` | `new RunnerTranscriptBoundEvent(Sid, BoundPath, TranscriptBindMethods.Discovery)` serialised in both spellings: hub `{"SessionId":"<Sid>","TranscriptPath":"<BoundPath>","How":"discovery"}` | `EventName == "SessionTranscriptBound"`, `SessionId == Sid`, `TranscriptBound == new SessionRunnerTranscriptBoundEvent(Sid, BoundPath, "discovery")`; the other slots null | PC-2 swap `bound.TranscriptPath` and `bound.How` in the mapped record -> red at record equality |
| `Old_runner_fault_without_repeat_fields_defaults_to_first_report` | Hand-written pre-CARD-0101 payload in the hub spelling: `{"SessionId":"<Sid>","Kind":"TranscriptMissing","Detail":"<OldDetail>","CandidatePath":null}` | `TranscriptFault == new SessionRunnerTranscriptFaultEvent(Sid, "TranscriptMissing", OldDetail, null, 0, 1)`; separately `CandidatePath.ShouldBeNull()`, `UnboundSeconds.ShouldBe(0)`, `Repeat.ShouldBe(1)` | PC-13 map `CandidatePath: fault.CandidatePath ?? ""` -> red at `CandidatePath` null; alternative (contracts file, not the mapper) change `int Repeat = 1` to `0` in `RunnerTranscriptFaultEvent` -> red at `Repeat` |
| `Fault_frame_with_unknown_future_field_still_parses` | Two rows: the hub-spelling fault JSON with `,"FutureField":"x"` inserted before the closing brace; the hub-spelling bound JSON with the same insertion | Fault row: same record as `Fault_frame_round_trips_every_field`; bound row: same record as `Bound_frame_round_trips_every_field`; no exception | PC-14 deserialise the two new branches with `new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }` -> `JsonException` -> red |
| `Unknown_event_name_still_returns_null` | Table: (`SessionRunnerEventNames.SessionAgentStatus`, `{}`), (`"SessionSomethingNew"`, `{}`), (`SessionTranscriptFault`, `null`), (`SessionTranscriptBound`, `null`) | each `ParseEvent` result `ShouldBeNull` | PC-15 on a null deserialisation return `new SessionRunnerEvent(eventName, Guid.Empty)` instead of null in the fault branch -> red at the third row |
| `Malformed_payload_throws_like_the_other_branches` | Table: (`SessionTranscriptFault`, `{not json`), (`SessionTranscriptFault`, `[]`), (`SessionTranscriptBound`, `{not json`), (`SessionTranscriptBound`, `[]`) | each row `Should.Throw<JsonException>(() => ParseEvent(name, json))`; a control row (`SessionOutput`, `{not json`) throws the same type, pinning "like the other branches" | PC-19 wrap the two new branches in `try { ... } catch (JsonException) { return null; }` -> red at the first row |

Six methods: the plan's five named methods (its "field pass-through" case lives inside
`Fault_frame_round_trips_every_field` as PC-3), the forward-compatibility case widened to both
events, and the malformed-payload case added because D-1 states that behaviour ("throws like the
other branches; absorbed by the pump's per-event catch") and nothing else pins it. A silent null
there would drop a corrupt frame without counting an `EventFailures` increment, which is the
fail-closed rule: unknown names keep today's null, corrupt payloads surface.

### Guard: `tests/Antiphon.Tests/Application/TranscriptIncidentReplayGuardTests.cs`

`[Category("Unit")]`. Recipe: `var clock = new FakeTimeProvider(Epoch0); var guard = new
TranscriptIncidentReplayGuard(clock, TimeSpan.FromSeconds(900));` (capacity 3 in the eviction test).
`F` is `new SessionRunnerTranscriptFaultEvent(Sid, "AdoptionRefused", FaultDetail, FaultPath, 0, 1)`;
`B` is `new SessionRunnerTranscriptBoundEvent(Sid, BoundPath, "discovery")`. Each table is a
sequence of `Admit` calls with a hand-written expected bool; the assertion names the step.

| Method | Sequence (expected) | Mutation -> red |
|---|---|---|
| `First_fault_is_admitted_and_identical_replay_is_refused` | `F` (true); `F with { }` (false; a distinct instance with equal value); `F` (false); `B` (true); `B with { }` (false) | PC-6 `Admit` returns true unconditionally -> red at step 2 |
| `A_genuine_repeat_with_higher_repeat_count_is_admitted` | `F` (true); then one flip each from `F`, each expected true: `{ Repeat = 2, UnboundSeconds = 300 }` (the runner's five-minute repeat), `{ Kind = "TranscriptMissing" }`, `{ Detail = "other detail" }`, `{ CandidatePath = null }`, `{ SessionId = <other guid> }`; then `F` (false) | PC-7 key faults on `(SessionId, Kind)` only -> red at the first flip |
| `Replay_after_the_window_is_admitted_again` | `F` (true); `clock.Advance(899 s)`; `F` (false); `clock.Advance(1 s)` (elapsed 900); `F` (true); `F` (false). Rule: refused while elapsed < window | PC-9 ignore elapsed time (treat a seen key as inside the window) -> red at step 3 |
| `Bound_identical_replay_is_refused_and_a_different_path_is_admitted` | `B` (true); `B` (false); `B with { TranscriptPath = "/home/a/.codex/sessions/2026/10/13/rollout-def.jsonl" }` (true); `B with { How = "fork" }` (true); `F` (true; same session id, different record type); `B` (false) | PC-16 key bound events on `SessionId` only -> red at step 3 |
| `Guard_evicts_the_oldest_key_past_capacity` | capacity 3; `F1..F4` differ only in `Repeat` 1..4: `F1` (true), `F2` (true), `F3` (true), `F4` (true, evicts `F1`), `F1` (true, evicts `F2`), `F4` (false), `F2` (true, evicts `F3`), `F3` (true, evicts `F4`), `F1` (false) | PC-17 remove eviction -> red at step 5 |

Eviction rule bound for Code: a new key past capacity evicts the oldest admitted key; a re-admitted
key is re-stamped and becomes the newest.

### Pump: `tests/Antiphon.Tests/Application/PhoneHomeTranscriptIncidentTests.cs`

`[Category("Integration")]`. Shared recipe (a private static helper in the class, no new helper file):

1. `schema = await TestDbFixture.CreateIsolatedSchemaAsync()`; harness as in "Fixture facts";
   `clock = new FakeTimeProvider(Epoch0)`; `host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString)`;
   `db` on the schema; `logs = new CapturingLoggerProvider()` with a `LoggerFactory`; pump built as
   `PhoneHomeEventPumpTests.Pump(host, h, logger)` builds it (settings `Enabled`, `AllowedRunnerId = "grok-linux"`,
   `StandingAgentId`, `HostWorkspaceRoot`, `SharedSecret = host.Secret`; the replay window stays at its default 900).
2. `SeedRemoteAsync(db, runnerId, storeId, ownerAgentId)`: an `AgentSession` as `PhoneHomeEventPumpTests.SeedAsync`
   (`RunnerId`, `RunnerStoreId`, `RunnerCwd = "/work"`) plus an `AgentTask` row as
   `Delegate_task_session_fault_records_incident_on_the_task_agent` seeds it (`AgentId = ownerAgentId`,
   `AgentSessionId = session.Id`, `Status = Dispatched`, `Ephemeral = true`). The owned session is
   `remote = SeedRemoteAsync(db, "grok-linux", host.StoreId, h.AgentId)`.
3. `peer = await host.ConnectPeerAsync(); live = await host.WaitLiveAsync();` then `pump.PumpEventsAsync(live, cts.Token)`.
4. `EmitAsync(peer, epoch, eventName, record)` writes `new PhoneHomeFrame(PhoneHomeFrameKind.Event, epoch, Guid.Empty,
   EventName: eventName, Payload: JsonSerializer.SerializeToElement(record))` with default options,
   which is the hub spelling. `Sentinel(peer, epoch, remote.Id, seq)` is `PhoneHomeEventPumpTests.EmitOutputAsync`.
5. Oracles read with `AsNoTracking`: `FaultRows(sid)` = count of `AgentIncidents` with
   `SessionId == sid && Kind == TranscriptBindFailed`; `BoundRows(sid)` likewise for
   `TranscriptBoundByDiscovery`; `AlertsFor(sid)` = count of `Alerts` with `SessionId == sid`;
   `Refused()` = count of `logs.Entries` with `Level == LogLevel.Debug` and `Message` containing
   `is a replay inside`; `Deltas(sid)` as `PhoneHomeEventPumpTests.Deltas`.
   `WaitUntilAsync(Func<Task<bool>>)` polls at 20 ms for at most 5 s (the siblings' bound) and
   throws on timeout with the predicate name.

| Method | Steps | Decisive oracle | Mutation -> red |
|---|---|---|---|
| `Remote_fault_frame_records_the_local_incident_row` | emit `SessionTranscriptFault` with `new RunnerTranscriptFaultEvent(remote.Id, "AdoptionRefused", FaultDetail, FaultPath, 184.5, 3)` on `live.Epoch`; wait `FaultRows(remote.Id) >= 1 && live.PendingEvents == 0` | `FaultRows == 1`; the row has `AgentId == h.AgentId`, `SessionId == remote.Id`, `Severity == Warning`, `FailureReason == "AdoptionRefused"`, `ExitCode == null`, `Message == FaultMessage`; exactly one alert with `DedupKey == $"supervisor:TranscriptBindFailed:{h.AgentId:D}"`, `Severity == Warning`, `SessionId == remote.Id`, `AgentId == h.AgentId`; no `TranscriptBindStuck` row; `pump.EventFailures(live) == 0`; `Refused() == 0` | PC-4 remove the fault arm -> the wait times out and `FaultRows == 1` red |
| `Remote_bound_frame_records_an_info_incident_without_alert` | emit `SessionTranscriptBound` with `new RunnerTranscriptBoundEvent(remote.Id, BoundPath, "discovery")`; wait `BoundRows(remote.Id) >= 1 && live.PendingEvents == 0` | `BoundRows == 1`; `Severity == Info`, `Message == BoundMessage`, `FailureReason == null`, `AgentId == h.AgentId`; `AlertsFor(remote.Id) == 0`; `pump.EventFailures(live) == 0` | PC-5 remove the bound arm -> `BoundRows == 1` red |
| `Replayed_frames_across_a_reconnect_record_once` | (a) on peer A emit fault `F1` (`Repeat 1`, `UnboundSeconds 0`) and bound `B`; wait `FaultRows >= 1 && BoundRows >= 1 && PendingEvents == 0`; assert 1 and 1. (b) `peer.Socket.Abort()`; wait `!live.SocketOpen`; dispose A; `peerB = host.ConnectPeerAsync()`; wait `host.Directory.SnapshotLive() is { } l && !ReferenceEquals(l, live)`; `liveB = SnapshotLive()!`; `pump.PumpEventsAsync(liveB, ...)` on the same pump instance. (c) emit identical `F1` and `B` on `liveB.Epoch`; wait `Refused() >= 2 && liveB.PendingEvents == 0`; assert `FaultRows == 1`, `BoundRows == 1`, `Refused() == 2`. (d) emit `F1` twice more; wait `Refused() >= 4 && PendingEvents == 0`; assert `FaultRows == 1`. (e) emit `F1 with { Repeat = 2, UnboundSeconds = 300 }`; wait `FaultRows >= 2 && PendingEvents == 0` | after (e): `FaultRows == 2`, `BoundRows == 1`, alerts with the fault dedup key `== 2`, `Refused() == 4`, `pump.EventFailures(liveB) == 0` | PC-8 hold the guard in `ConnectionState` (per connection) -> (c) records a second fault row: `FaultRows == 1` red; PC-6 (`Admit` unconditionally true) also reds (c) |
| `Foreign_owner_transcript_events_never_record` | table of three sessions, each seeded with an owning task row pointing at `h.AgentId` so a bypassed owner check writes a real row: `foreignRunner = SeedRemoteAsync(db, "other-runner", Guid.NewGuid(), h.AgentId)`, `foreignStore = SeedRemoteAsync(db, "grok-linux", Guid.NewGuid(), h.AgentId)`, `unknown = Guid.NewGuid()` (no rows); emit fault and bound for each of the three on `live.Epoch` (six frames), then `Sentinel(remote.Id, 1)`; wait `Deltas(remote.Id) >= 1 && live.PendingEvents == 0` | incident rows with `SessionId` in the three `== 0`; `AlertsFor` each of the three `== 0` (covers the unowned dedup key as well); `pump.EventFailures(live) == 0`; `Refused() == 0` | PC-10 dispatch the two arms before `OwnerMatchesAsync` -> incident rows for `foreignRunner` and `foreignStore` (owned through their task rows) and an unowned alert for `unknown` -> red at the incident count |
| `Incident_text_matches_the_local_lane` | sessions `A` and `Bsess` both `SeedRemoteAsync(..., "grok-linux", host.StoreId, h.AgentId)`; through the pump for `A`: fault `new RunnerTranscriptFaultEvent(A, "AdoptionRefused", FaultDetail, FaultPath, 184.5, 3)` and bound `new RunnerTranscriptBoundEvent(A, BoundPath, "discovery")`; wait `FaultRows(A) >= 1 && BoundRows(A) >= 1 && PendingEvents == 0`; direct, as `SessionRunnerEventPump.cs:427` and `:433` call it: `service.OnTranscriptFaultAsync(new SessionRunnerTranscriptFaultEvent(Bsess, "AdoptionRefused", FaultDetail, FaultPath, 184.5, 3), ct)` and `service.OnHeuristicBindAsync(new SessionRunnerTranscriptBoundEvent(Bsess, BoundPath, "discovery"), ct)` with `service = h.Provider.GetRequiredService<TranscriptBindingIncidentService>()` | for the fault pair and the bound pair: `Kind`, `Severity`, `Message`, `FailureReason`, `ExitCode` equal between the `A` row and the `Bsess` row; and the literal checks `A`'s fault `Message == FaultMessage`, `A`'s bound `Message == BoundMessage`; the two fault alerts share `Title == "TranscriptBindFailed: agent supervision"` and `Severity` | PC-11 replace the pump's service call with `AgentSupervisorService.RecordIncidentAsync(..., "Remote: " + message)` -> `Message` mismatch red |

The direct-call side uses hand-written records, not `RunnerContractMapper.ParseEvent`, so the
comparison is between lanes rather than the mapper against itself; the literal `FaultMessage` and
`BoundMessage` checks catch a drift that moved both sides together.

### Settings: `tests/Antiphon.Tests/Application/PhoneHomeRunnerSettingsValidatorTests.cs`

One added method, `Replay_window_below_one_second_is_refused`, using the file's `Settings(...)` helper:

| Row | Input | Expected |
|---|---|---|
| 1 | `Settings(s => s.TranscriptIncidentReplayWindowSeconds = 0)` | contains `PhoneHomeRunner:TranscriptIncidentReplayWindowSeconds must be at least 1.` |
| 2 | `Settings(s => s.TranscriptIncidentReplayWindowSeconds = -1)` | contains the same message |
| 3 | `Settings(s => s.TranscriptIncidentReplayWindowSeconds = 1)` | empty |
| 4 | `new PhoneHomeRunnerSettings().TranscriptIncidentReplayWindowSeconds` | `900`; `Settings(_ => { })` validates empty |
| 5 | `RollingRunnerSettings.Pair("s", "s")` with `TranscriptIncidentReplayWindowSeconds = 0` (map mode) | contains the same message |

Mutations: PC-12 delete the rule -> rows 1, 2 and 5 red; PC-18 place the rule in the legacy branch
instead of `ValidateGlobal` -> row 5 red.

### Docs sentence (Code, S2)

After the Preserved Gotcha #40 (CARD-0006) paragraph in `docs/session-runtime-invariants.md`, one
sentence: "Remote (phone-home) runners deliver the same two events through `RunnerContractMapper`
and `PhoneHomeRecoveryPump`, recorded through the same service after the owner check and a
per-runner replay guard (CARD-1219); pinned by `RunnerContractMapperTranscriptEventTests`,
`TranscriptIncidentReplayGuardTests` and `PhoneHomeTranscriptIncidentTests`."

## Verification design

### Inspection

- `RunnerContractMapper.cs` (whole file) | branches -> V-1, V-2; null tail -> V-2
- `SessionRunnerHttpClient.cs:985-1040` | SSE shape mirrored -> V-1; not edited (D-5)
- `SessionRunnerDtos.cs:100-160`; `SessionRunnerContracts.cs:860-960, 1013-1026` | record shapes, defaults, event names, bind methods -> V-1, V-2
- `PhoneHomeRecoveryPump.cs` (whole file) | `PumpEventsAsync`, `OwnerMatchesAsync`, `_cycles`, `ConnectionState` -> V-4, V-5, V-6, R-2
- `PhoneHomeLiveConnection.cs:85-110, 445-475, 540-560` | `Clock`, pending accounting, `ReleaseEvent` -> wait design
- `PhoneHomeRunnerDirectory.cs:262-270, 376-384` | version gate (no bump), clock injection -> excluded from scope (D-1), fixture fact
- `SessionRunnerRuntime.cs:4465-4485`; `PhoneHomeConnectionService.cs:359-364` | hub spelling -> V-1 both-spelling variant
- `PhoneHomeRunnerClient.cs:555-566`; `RoutingSessionRunnerClient.cs:120-121` | single consumer -> V-4 single-row assertion
- `TranscriptBindingIncidentService.cs` (whole file); `AgentSupervisorService.cs:534-578`; `AlertService.cs:35-60`; `SessionOwnerLookup.cs` | text, dedup key, alert-per-raise, owner resolution -> V-4, V-5 oracles
- `PhoneHomeRunnerSettings.cs` (whole file); `PhoneHomeRunnerSettingsValidator.cs` | rule placement -> V-7; validator file not edited
- `SupervisionSettings.cs` (`TranscriptBindingSettings`) | stuck threshold 30 min -> no stuck row in V-4/V-5
- `TranscriptTailer.cs:686-702`; `CodexTranscriptTailer.cs:508-522` | publish conditions -> D-6 holds; exact binds excluded
- `Program.cs:758, 910` | service and pump registrations -> production reachability of the new arms
- Tests read in full: `PhoneHomeEventPumpTests.cs` (8), `TranscriptBindingIncidentTests.cs` (15), `PhoneHomeRunnerSettingsValidatorTests.cs` (7), `QuotaEvidenceWireTests.cs` (4); helpers `PhoneHomeTestHost.cs`, `BridgeQueueHarness.cs:40-100, 198-200`, `PhoneHomeOutageHarness.cs:24-77`, `MockEventBus.cs:15-30` | fixtures and census -> Checkpoints
- `docs/testing-and-build.md:392-460` | checkpoint schema -> Checkpoints

### Delivery inventory

One changed asynchronous delivery path. Durable identity: the parsed record
(`SessionId, Kind, Detail, CandidatePath, UnboundSeconds, Repeat` or `SessionId, TranscriptPath, How`),
which the recipient row carries as `SessionId`, `Kind`, `FailureReason` and `Message`.

| Hop | Producer | Destination | Persistence boundary | Recovery | Observable receipt |
|---|---|---|---|---|---|
| 1 | Runner tailer `_events.Publish` (hub, default JSON) | `PhoneHomeConnectionService.SendEventAsync` -> WebSocket event frame | none (live only; the hub keeps no history) | fault: the runner's five-minute repeat re-publishes with `Repeat + 1`; bound: not re-sent | the frame on the server socket (not a receipt of anything) |
| 2 | `PhoneHomeLiveConnection` receive loop (`_pendingEvents++`, channel write) | `PhoneHomeRecoveryPump.PumpEventsAsync` (single FIFO reader per connection) | none; `PendingEvents` returns to 0 in the pump's `finally` | a handler exception is caught per event and the event released (CARD-0679 D-2, R-2); a socket drop ends the channel and the next connection gets a fresh pump (R-2) | `PendingEvents == 0` paired with the row or log evidence below, not on its own |
| 3 | pump arm (after `OwnerMatchesAsync`, after `guard.Admit`) | `TranscriptBindingIncidentService.OnTranscriptFaultAsync` / `OnHeuristicBindAsync` | `db.SaveChangesAsync` in the service scope: `AgentIncidents` row (+ `Alerts` row for the fault) | the service's write-failure backstop raises a standalone alert (R-3, `A_fault_whose_incident_cannot_be_written...`); an unowned session raises the unowned alert (R-3) | the `AgentIncidents` row read back with `AsNoTracking` on the isolated schema; the `Alerts` row for the fault |

Producer-to-recipient tests through the real queue (a real Kestrel WebSocket host, the real
`PhoneHomeLiveConnection` channel, the real pump, the real service, a real Postgres schema):

- busy recipient and one already eligible: `Replayed_frames_across_a_reconnect_record_once` step (a)
  emits two frames back to back and steps (c)-(d) queue four more behind each other; each is
  observed through its own receipt (row or refusal line) with the channel order fixed.
- crash / handoff recovery: the same test aborts the socket between (a) and (c) and proves the
  next connection delivers to the same recipient with the per-runner guard intact. Frames lost
  while the socket is down are not reconstructed (D-8, OD-3) and no test claims otherwise.
- enqueue failure at hop 3: inherited `A_fault_whose_incident_cannot_be_written_names_the_database_error_and_still_alerts`
  (R-3) covers the write failure for the method the pump now calls; `Incident_text_matches_the_local_lane`
  (PC-11) proves the pump calls that method and not a copy.

Substitutes and what they cannot prove: `PhoneHomeScriptedPeer` stands in for the runner's
`PhoneHomeConnectionService` and the hub. It proves the server side of hop 1 with the hub's JSON
spelling (the mapper test builds that spelling from the real `RunnerTranscriptFaultEvent` with the
same default options the hub uses) but does not prove that a tailer on a Linux runner publishes the
event for a given launch; that is runner-side behaviour already pinned by `TranscriptAdoptionSafetyTests`
and is unchanged by this card. No session input is delivered anywhere in this design, so no
`UserPrompt` transcript evidence applies. A frame send, `PendingEvents`, or a Debug line is used
only as an ordering fence; the delivery verdict in each test is the recipient row.

### Proves it works now

- V-1: both frames map into the SSE parser's records under both JSON spellings with each field passed through and the other slots null | Unit | `RunnerContractMapperTranscriptEventTests.Fault_frame_round_trips_every_field`, `.Bound_frame_round_trips_every_field` | record equality against the hand-written records; the other slots null
- V-2: an old-runner fault payload defaults `UnboundSeconds 0`, `Repeat 1`, null candidate; an unknown member is ignored on both events; unknown names and null payloads return null; a corrupt payload throws `JsonException` | Unit | `.Old_runner_fault_without_repeat_fields_defaults_to_first_report`, `.Fault_frame_with_unknown_future_field_still_parses`, `.Unknown_event_name_still_returns_null`, `.Malformed_payload_throws_like_the_other_branches` | as tabled
- V-3: the guard admits a record once per window, admits a record that differs in any field, re-admits at the window on the connection clock, keys bound events on the whole record, and is bounded | Unit | the five `TranscriptIncidentReplayGuardTests` methods | the expected bool at each step
- V-4: a remote fault for an owned session records the local `TranscriptBindFailed` row and alert; a remote bound records the Info `TranscriptBoundByDiscovery` row with no alert; the text equals the local lane's | Integration | `PhoneHomeTranscriptIncidentTests.Remote_fault_frame_records_the_local_incident_row`, `.Remote_bound_frame_records_an_info_incident_without_alert`, `.Incident_text_matches_the_local_lane` | row fields and `FaultMessage` / `BoundMessage`
- V-5: identical frames on the same epoch or after a reconnect record once and are logged as refused; a genuine repeat records again | Integration | `.Replayed_frames_across_a_reconnect_record_once` | `FaultRows` 1 then 2, `Refused()` 4
- V-6: frames for a session owned by another runner, another store, or no row record nothing and raise nothing | Integration | `.Foreign_owner_transcript_events_never_record` | zero rows and zero alerts behind the sentinel
- V-7: the window setting is validated in both configuration modes and defaults to 900 | Unit | `PhoneHomeRunnerSettingsValidatorTests.Replay_window_below_one_second_is_refused` | the exact message, the default

### Guards the regression

- R-1: the widened mapper keeps the transcript event and the Web options unchanged | `QuotaEvidenceWireTests` (4): `Phone_home_zone_survives_ingestion_and_storage` asserts `ParseEvent(SessionTranscript, ...)` still yields `ApiErrorTimeZoneId`
- R-2: owner, catch-up, overflow, failing-event, restart and owner-cache behaviour of the pump is unchanged | `PhoneHomeEventPumpTests` (8): `Pump_survives_a_failing_event_and_releases_the_rest` asserts `EventFailures == 1` and `PendingEvents == 0`; `Owner_lookups_are_cached_per_connection` asserts the lookup counts
- R-3: local-lane incident text, severity, escalation, ClaimRevoked and backstop rules are unchanged | `TranscriptBindingIncidentTests` (15): `Transcript_fault_event_creates_incident_and_alert` asserts `FailureReason == AdoptionRefused` and the dedup key; `The_escalation_does_not_re_fire_within_its_repeat_window` asserts three repeats record three rows
- R-4: existing settings rules are unchanged | `PhoneHomeRunnerSettingsValidatorTests` (7 existing): `Defaults_are_the_documented_server2_shape`, `Two_entries_with_the_same_secret_and_host_root_validate`

### Guard inventory

- G-1: D-1 fault branch present in `ParseEvent` | PC-1
- G-2: D-1 bound branch maps `TranscriptPath` and `How` to the right slots | PC-2
- G-3: D-1 `UnboundSeconds` and `Repeat` pass through unchanged | PC-3
- G-4: D-4 fault arm dispatches to `OnTranscriptFaultAsync` | PC-4
- G-5: D-4 bound arm dispatches to `OnHeuristicBindAsync` | PC-5
- G-6: D-2 an identical record inside the window is refused | PC-6
- G-7: D-2 the fault key includes `Repeat`, `UnboundSeconds`, `Detail`, `CandidatePath`, `SessionId`, `Kind` | PC-7
- G-8: D-2 the guard lives per runner id and survives a reconnect | PC-8
- G-9: D-2 the window expires on the connection clock | PC-9
- G-10: D-4 the owner check precedes recording | PC-10
- G-11: D-4 the remote lane records through the same service method as the local lane | PC-11
- G-12: D-3 the window is validated to be at least 1 | PC-12
- G-13: D-1 a null `CandidatePath` and the contract defaults survive the mapper | PC-13
- G-14: D-1 unknown members on either event are ignored | PC-14
- G-15: D-1 unknown names and null payloads return null | PC-15
- G-16: D-2 the bound key includes `TranscriptPath` and `How` | PC-16
- G-17: D-2 the guard evicts the oldest key past capacity | PC-17
- G-18: D-3 the rule sits in `ValidateGlobal`, so map-mode configurations are checked | PC-18
- G-19: D-1 a corrupt payload on either new branch throws (counted by the pump as an event failure) rather than returning a silent null | PC-19

Guards 19, mapped 19, missing 0, duplicate PC maps 0. Untested-by-design, with reason: the lock
inside `Admit` (D-2 correction) is a construction requirement Review checks by reading; a race test
would be timing-dependent and is not designed in. The refused-replay path releasing its event is the
existing `finally` (CARD-0679), not a new guard; the `PendingEvents == 0` waits exercise it.

### Positive controls

All PCs are specified and stay pending (post-land Mutation is paused). Each runs method-scoped with
the exact filter named, red then restore then green, against a scratch copy; mutations in the same
method are separate cycles, mutations in different files may share a cycle.

- PC-1: break G-1 by deleting the `SessionTranscriptFault` branch from `RunnerContractMapper.ParseEvent`; expect `/*/*/RunnerContractMapperTranscriptEventTests/Fault_frame_round_trips_every_field` red at `ShouldNotBeNull`.
- PC-2: break G-2 by swapping `bound.TranscriptPath` and `bound.How` in the mapped record; expect `/*/*/RunnerContractMapperTranscriptEventTests/Bound_frame_round_trips_every_field` red at the record equality.
- PC-3: break G-3 by passing `UnboundSeconds: 0` in the fault branch; expect `/*/*/RunnerContractMapperTranscriptEventTests/Fault_frame_round_trips_every_field` red at the record equality (184.5 expected).
- PC-4: break G-4 by removing the `TranscriptFault` arm from `PumpEventsAsync`; expect `/*/*/PhoneHomeTranscriptIncidentTests/Remote_fault_frame_records_the_local_incident_row` red at `FaultRows == 1` after the wait times out.
- PC-5: break G-5 by removing the `TranscriptBound` arm; expect `/*/*/PhoneHomeTranscriptIncidentTests/Remote_bound_frame_records_an_info_incident_without_alert` red at `BoundRows == 1`.
- PC-6: break G-6 by making `Admit` return true unconditionally; expect `/*/*/TranscriptIncidentReplayGuardTests/First_fault_is_admitted_and_identical_replay_is_refused` red at step 2.
- PC-7: break G-7 by keying faults on `(SessionId, Kind)`; expect `/*/*/TranscriptIncidentReplayGuardTests/A_genuine_repeat_with_higher_repeat_count_is_admitted` red at the `Repeat = 2` flip.
- PC-8: break G-8 by holding the guard in `ConnectionState`; expect `/*/*/PhoneHomeTranscriptIncidentTests/Replayed_frames_across_a_reconnect_record_once` red at `FaultRows == 1` in step (c).
- PC-9: break G-9 by ignoring elapsed time in the guard; expect `/*/*/TranscriptIncidentReplayGuardTests/Replay_after_the_window_is_admitted_again` red at step 3.
- PC-10: break G-10 by dispatching the two arms before `OwnerMatchesAsync`; expect `/*/*/PhoneHomeTranscriptIncidentTests/Foreign_owner_transcript_events_never_record` red at the incident count for the three sessions.
- PC-11: break G-11 by replacing the pump's service call with `AgentSupervisorService.RecordIncidentAsync(..., "Remote: " + message)`; expect `/*/*/PhoneHomeTranscriptIncidentTests/Incident_text_matches_the_local_lane` red at the `Message` comparison.
- PC-12: break G-12 by deleting the rule from `ValidateGlobal`; expect `/*/*/PhoneHomeRunnerSettingsValidatorTests/Replay_window_below_one_second_is_refused` red at row 1.
- PC-13: break G-13 by mapping `CandidatePath: fault.CandidatePath ?? ""`; expect `/*/*/RunnerContractMapperTranscriptEventTests/Old_runner_fault_without_repeat_fields_defaults_to_first_report` red at `CandidatePath.ShouldBeNull()`.
- PC-14: break G-14 by deserialising the two new branches with `UnmappedMemberHandling = Disallow`; expect `/*/*/RunnerContractMapperTranscriptEventTests/Fault_frame_with_unknown_future_field_still_parses` red with `JsonException`.
- PC-15: break G-15 by returning `new SessionRunnerEvent(eventName, Guid.Empty)` on a null fault deserialisation; expect `/*/*/RunnerContractMapperTranscriptEventTests/Unknown_event_name_still_returns_null` red at the `"null"` row.
- PC-16: break G-16 by keying bound events on `SessionId` only; expect `/*/*/TranscriptIncidentReplayGuardTests/Bound_identical_replay_is_refused_and_a_different_path_is_admitted` red at step 3.
- PC-17: break G-17 by removing eviction; expect `/*/*/TranscriptIncidentReplayGuardTests/Guard_evicts_the_oldest_key_past_capacity` red at step 5.
- PC-18: break G-18 by moving the rule into the legacy branch of `Validate`; expect `/*/*/PhoneHomeRunnerSettingsValidatorTests/Replay_window_below_one_second_is_refused` red at row 5.
- PC-19: break G-19 by catching `JsonException` in the two new branches and returning null; expect `/*/*/RunnerContractMapperTranscriptEventTests/Malformed_payload_throws_like_the_other_branches` red at the first row's `Should.Throw`.

Mutation runs break/red/restore/green after land; Code runs the V/R rows; Review judges before land.

### Out of scope

- Exact-bind receipts (D-6): no event exists on either lane; a parity design cannot test one.
- `SessionAgentStatus` on phone-home (D-7, OD-4): a different consumer; its null mapping is pinned only as a negative row.
- Outage reconstruction from the catch-up inventory (D-8, OD-3): not built; the replay test documents the loss.
- A race test on the guard lock: timing-dependent; Review reads the lock.
- A real runner process on Linux publishing the events: runner-side behaviour, unchanged, owned by `TranscriptAdoptionSafetyTests`.
- Lease expiry by advancing the host clock under a live socket: the window is proved at unit level; the host clock stays frozen.
- Windows: no test needs an OS; no `-Platform`, no `EstimatedMinutesWindows` column.

### Checkpoints

Census from source at `cc0067c81` (`[Test]` attributes, no `[Arguments]`): `QuotaEvidenceWireTests` 4,
`PhoneHomeEventPumpTests` 8, `PhoneHomeRunnerSettingsValidatorTests` 7 (8 after S2),
`TranscriptBindingIncidentTests` 15; new classes 6, 5 and 5. Assembly `Antiphon.Tests`, namespace
`Antiphon.Tests.Application`, so `/*/*/<Class>/*` is exact. No row runs a namespace or the Unit lane.
Integration rows need the shared test Postgres (isolated schema per test) and a loopback Kestrel host.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1219-s1/` | mapper-transcript-events | `/*/*/RunnerContractMapperTranscriptEventTests/*` | V-1, V-2 | all listed, 0 failed | 6 | 8 | false |
| CP-2 | S1 | CP-1 | quota-wire-regression | `/*/*/QuotaEvidenceWireTests/*` | R-1 | all listed, 0 failed | 4 | 2 | true |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1219-s2/` | replay-guard | `/*/*/TranscriptIncidentReplayGuardTests/*` | V-3 | all listed, 0 failed | 5 | 8 | false |
| CP-4 | S2 | CP-3 | pump-transcript-incidents | `/*/*/PhoneHomeTranscriptIncidentTests/*` | V-4, V-5, V-6 | all listed, 0 failed | 5 | 4 | true |
| CP-5 | S2 | CP-3 | pump-regression | `/*/*/PhoneHomeEventPumpTests/*` | R-2 | all listed, 0 failed | 8 | 4 | true |
| CP-6 | S2 | CP-3 | settings-validator | `/*/*/PhoneHomeRunnerSettingsValidatorTests/*` | V-7, R-4 | all listed, 0 failed | 8 | 1 | false |
| CP-7 | S2 | CP-3 | local-lane-regression | `/*/*/TranscriptBindingIncidentTests/*` | R-3 | all listed, 0 failed | 15 | 5 | true |

Run through the checkpoint tool: `run --plan <this file> --after S1 --expected-source-sha <S1 commit>
--serial --max-wait 55s`, then `wait` until the exit is not 75; the same for `--after S2`. The tool
bootstrap is one unlisted, build-slot-gated isolated build (`bin-c1219-tool/`, `UseAppHost=false` on
Linux). Each `bin-c1219-*` directory is removed before the task ends. Nothing else is built or run.

### Cost

- Ordinary V/R floor (Code): 32 estimated minutes = CP-1 8 + CP-2 2 + CP-3 8 + CP-4 4 + CP-5 4 +
  CP-6 1 + CP-7 5 (two isolated builds, about 6 minutes each, inside CP-1 and CP-3).
- PC floor (Mutation, pending): 19 PCs. PCs in the same file do not share a cycle: seven in
  `RunnerContractMapper.cs` (PC-1, 2, 3, 13, 14, 15, 19), five in `TranscriptIncidentReplayGuard.cs`
  (PC-6, 7, 9, 16, 17), five in `PhoneHomeRecoveryPump.cs` (PC-4, 5, 8, 10, 11), two in
  `PhoneHomeRunnerSettings.cs` (PC-12, 18). Batched across the four files, seven cycles cover all
  19 (the mapper file sets the count). A cycle is about 15 estimated minutes (mutate, isolated
  build about 6, method-scoped runs about 1, restore, build about 6, green runs about 1):
  7 x 15 = 105 minutes. Unbatched, 19 x 14 = 266 minutes; batching saves about 160 minutes.
- Total (estimated): tool bootstrap build 8 + V/R 32 + PC 105 = 145 minutes, of which Code pays 40
  and Mutation pays 105 later.

## Code slice binding (OD-2)

One Code task carries S1 and S2 as two commits in that order, on the default placement (omit
`-Runner` and `-Platform`). Authoring: 17 new test methods, two mapper branches, the guard class,
two pump arms with the per-runner holder, one setting with its rule, one docs sentence; the plan's
60-90 minute authoring estimate stands, so `-ExpectAbout 100` minutes (authoring plus the 32-minute
floor plus the bootstrap build) rather than the 45-60 the brief hoped for. Two tasks would not
shorten the total: the checkpoint table needs both builds either way and S1 alone records nothing.
Scope string as the plan's, with the validator file dropped from the edit list but kept in scope
by the `PhoneHomeRunnerSettings*.cs` glob:
`server/Infrastructure/Agents/SessionRunner/RunnerContractMapper.cs,server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs,server/Infrastructure/Agents/SessionRunner/TranscriptIncidentReplayGuard.cs,server/Application/Settings/PhoneHomeRunnerSettings*.cs,tests/Antiphon.Tests/Application/RunnerContractMapperTranscriptEventTests.cs,tests/Antiphon.Tests/Application/PhoneHomeTranscriptIncidentTests.cs,tests/Antiphon.Tests/Application/TranscriptIncidentReplayGuardTests.cs,tests/Antiphon.Tests/Application/PhoneHomeRunnerSettingsValidatorTests.cs,docs/session-runtime-invariants.md`.
Checkpoint reference line for the brief: `checkpoints: docs/superpowers/plans/2026-10-13-card-1219-test-design.md@<this commit> section "### Checkpoints"`.

--- next stage ---
next: code
handoff: Implement CARD-1219 S1 then S2 as two commits per the test design (guard owns the connection clock with GetTimestamp, rule in ValidateGlobal, hub-spelling frames, sentinel-fenced waits), run CP-1..CP-7 through the checkpoint tool, remove bin-c1219-*; one task, ExpectAbout 100.
artifact: docs/superpowers/plans/2026-10-13-card-1219-test-design.md
