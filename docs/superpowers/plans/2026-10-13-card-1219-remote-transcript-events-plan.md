# CARD-1219 remote transcript-bind and transcript-fault events on the phone-home path

Date: 2026-10-13. Stage: Plan; next stage: TestDesign. Plan task `461e7494-0346-4b7e-bb91-1111a0ba5128`.
Inspected checkout: `00fa6b884a5f127982cff26a677aae74e6666650` (master tip at dispatch; the task
branch carries no source change). This dispatch changes this document only. Every test, count and
positive control below is proposed; none has been executed by Plan. Post-land Mutation for the
parent evidence task was not run and nothing here depends on it. The card's evidence is
`docs/investigations/2026-10-13-card-0073-transcript-binding-status.md` on
`origin/feat/card-task-6813e7cf` at `6b354b445` (read with `git show`; not on master).

The card's premise is right and narrower than it reads. Both phone-home consumers drop the two
runner diagnostic events because `RunnerContractMapper.ParseEvent` returns null for their names
and `PhoneHomeRecoveryPump.PumpEventsAsync` dispatches only Output, Exited and Transcript. The
wire shape does not change: the runner already publishes both events with the records the server
compiles against, so no contract version bump and no capability token are needed, and the
old-runner question reduces to the pre-CARD-0101 fault shape (no `unboundSeconds`/`repeat`
fields), which the records already default. The one thing the card asks for that the runner does
not do today is replay: as read (ground-truth row 5), the runner's event hub is live-only, so a
reconnect does not re-send an event; no test in this card pins that runner-side fact. The replay guard is still designed and tested, because nothing in the wire contract forbids
a duplicate frame and the runner's five-minute fault repeats, which differ in `Repeat`/`UnboundSeconds`,
must still be admitted.

Session checklist: this plan adds or changes no session that waits for input, so the CARD-1083
release checklist does not apply.

## Evidence and ground truth

| Card assumption / evidence claim | What the code actually does | Design consequence |
|---|---|---|
| The runner emits both events for remote sessions. | `TranscriptTailer.cs:693-699` publishes `SessionTranscriptBound` for every bind whose `How` is neither `exact` nor `sidecar`; `CodexTranscriptTailer.cs:514-518` publishes it for every non-sidecar Codex bind (always `discovery`); faults publish at `TranscriptTailer.cs:469`, `:955`, `:1026`, `:1077`, `:1126`, `CodexTranscriptTailer.cs:336`, `:720`, `:770`, `:808`, `GrokTranscriptTailer.cs:346`. The hub serialises the record with default `JsonSerializer.Serialize` (`SessionRunnerRuntime.cs:4475`); `PhoneHomeConnectionService.SendEventAsync` (`:359`) forwards name and JSON unchanged. | The server-side mapping consumes the existing `RunnerTranscriptFaultEvent` / `RunnerTranscriptBoundEvent` records. No runner change. |
| "A launch that bound exactly leaves no incident." | An exact Claude bind publishes nothing on any lane (`TranscriptTailer.cs:693`), and `TranscriptBindingIncidentService` has no exact-bind incident. The local lane records zero rows for an exact bind by design. | Out of scope; parity with local means exact binds stay silent on both lanes. D-6 names a follow-up card if an exact-bind receipt is wanted. |
| The mapper drops the events. | `RunnerContractMapper.ParseEvent` (`RunnerContractMapper.cs:75-124`) maps SessionOutput, SessionExited, SessionAdopted, SessionStarted, SessionTranscript and returns null otherwise. The SSE parser `SessionRunnerHttpClient.ParseEvent` (`:1007-1032`) maps both events into `SessionRunnerTranscriptFaultEvent` / `SessionRunnerTranscriptBoundEvent` (`SessionRunnerDtos.cs:119-131`), which `SessionRunnerEvent` already carries (`:149-150`). | S1 adds the two branches to the mapper, mirroring the SSE parser field for field. `SessionAgentStatus` is also unmapped on phone-home; D-7 keeps it out of this card. |
| The pump drops them after parsing. | `PhoneHomeRecoveryPump.PumpEventsAsync` (`:356-400`) handles `Output`, `Exited`, `Transcript` only; `PhoneHomeRunnerClient.StreamEventsAsync` (`:555-566`) yields whatever the mapper returns. The SSE pump streams only the local client (`RoutingSessionRunnerClient.cs:120-121`), so no second consumer dispatches phone-home incidents. | S2 adds the two dispatches after the owner check, calling the same service methods the SSE pump calls (`SessionRunnerEventPump.cs:421-434`). Exactly one consumer records; `Remote_fault_frame_records_the_local_incident_row` asserts a single row. |
| Replayed events after a reconnect would duplicate incidents. | `SessionRunnerEventHub.Subscribe` / `SubscribeBounded` create a fresh channel per subscriber with no history (`SessionRunnerRuntime.cs:4439-4474`); `ReleaseUnread` drains, never re-queues (`:4562`). Server catch-up re-reads List and transcripts only (`PhoneHomeRecoveryPump.CatchUpAsync`, `:295-347`); there is no bound/fault catch-up. A fault is re-published by the runner every `DefaultRefusalFaultRepeat` (5 min, `TranscriptTailer.cs:46`) with `Repeat` incremented and `UnboundSeconds` grown. | With no hub history, a duplicate frame would be a transport-level repeat of an identical record. The guard keys on the whole parsed record (value equality) per runner, so a genuine repeat (different `Repeat`/`UnboundSeconds`) is a different key and is admitted (`A_genuine_repeat_with_higher_repeat_count_is_admitted`). The guard is an in-memory cache and that key is a volatile admission identity, not a persisted one: it stamps a record before the incident service is called, and an equal record is admitted again after the window, after eviction at capacity, or by a new pump instance (for example after a server restart). Events lost during an outage are not reconstructed (D-8). |
| Idempotence can be DB-backed. | `AgentSupervisorService.RecordIncidentAsync` (`:534-575`) appends unconditionally; the fault message composed at `TranscriptBindingIncidentService.cs:92-110` carries Kind, Detail and candidate path but not `Repeat`, so two genuine five-minute repeats with the same Detail write rows whose descriptive fields (`Kind`, `Severity`, `Message`, `FailureReason`) are identical; only the generated `AgentIncident.Id` and `CreatedAt` differ, and the ordinary `TranscriptBindFailed` row stores neither `Repeat` nor `UnboundSeconds` (only the separate `TranscriptBindStuck` escalation text mentions them). `FailureReason` is `fault.Kind`, asserted by `TranscriptBindingIncidentTests` (`:55`, `:230`). | A message- or FailureReason-keyed DB dedup would suppress genuine repeats or change local text. Rejected (D-2). |
| "Incident text parity with local." | Both the SSE pump and the new pump dispatch call `TranscriptBindingIncidentService.OnTranscriptFaultAsync` / `OnHeuristicBindAsync`; owner resolution is DB-only (`SessionOwnerLookup.cs:34-50`) and works the same for a remote session row. | Parity is by construction; `Incident_text_matches_the_local_lane` proves it against a direct service call with the same mapped record. |
| Old runners must keep working. | Every phone-home-capable runner postdates CARD-0101 (`31020516a`, 2026-08-20) since phone-home arrived with CARD-0490 (`b6fddd73e`, 2026-09-20); the fault record defaults `UnboundSeconds = 0`, `Repeat = 1` for the older shape. `RunnerContractMapper.Json` is `JsonSerializerDefaults.Web` (case-insensitive, unknown members ignored). | `Old_runner_fault_without_repeat_fields_defaults_to_first_report` and `Fault_frame_with_unknown_future_field_still_parses` pin both directions; unknown names still return null (`Unknown_event_name_still_returns_null`). |
| Fake clock is available on this path. | `PhoneHomeLiveConnection.Clock` is the directory's `TimeProvider` (`PhoneHomeRunnerDirectory.cs:380-381`), which `PhoneHomeTestHost.StartAsync(clock:)` injects (`PhoneHomeTestHost.cs:54`, `:116`); `PhoneHomeOutageHarness` already runs the host on a `FakeTimeProvider`. Advancing that clock past `LeaseSeconds` expires the connection lease. | The guard reads `live.Clock`; window expiry is proved in the guard's unit tests, not by advancing the host clock under a live socket. |
| Platform can be inferred from the card. | Card says Any. `GET /api/runner-defaults` revision 2: `globalRunnerId` server2, no kind overrides. `GET /api/session-runners`: desktop (windows, capacity 2), server2 (linux, capacity 10), one stale `server2-temp`. | Every row runs on the default placement; omit `-Runner` and `-Platform`. Nothing here needs Windows. |

## Decisions

- **D-1 Mapper branches, no contract change.** `RunnerContractMapper.ParseEvent` gains two
  branches, placed after `SessionTranscript`, that deserialise `RunnerTranscriptFaultEvent` and
  `RunnerTranscriptBoundEvent` with `RunnerContractMapper.Json` and return
  `new SessionRunnerEvent(eventName, SessionId, TranscriptFault: ...)` /
  `(..., TranscriptBound: ...)` with every field passed through, the same shape as
  `SessionRunnerHttpClient.ParseEvent`. A null deserialisation returns null like the other
  branches; a malformed payload throws like the other branches and is absorbed by the pump's
  per-event catch (CARD-0679 D-2). `PhoneHomeProtocol.Version`, `RunnerCapabilityFeatures` and
  `RunnerCapabilitiesDto` are untouched. Rejected: a feature token such as
  `transcriptDiagnosticsV1` (the runner has emitted these frames since CARD-0490; a token would
  only describe the server's own parser); a protocol version bump (would refuse every deployed runner
  at `PhoneHomeRunnerDirectory.cs:266` for a server-only change).
- **D-2 Replay guard keyed on the parsed record, per runner, in memory.** New
  `TranscriptIncidentReplayGuard` (`server/Infrastructure/Agents/SessionRunner/`) with
  `bool Admit(SessionRunnerTranscriptFaultEvent fault, DateTimeOffset now)` and
  `bool Admit(SessionRunnerTranscriptBoundEvent bound, DateTimeOffset now)`. The key is the record
  itself (C# record value equality: for a fault `SessionId, Kind, Detail, CandidatePath,
  UnboundSeconds, Repeat`; for a bind `SessionId, TranscriptPath, How`). That key is a volatile
  admission identity held in memory by one pump instance; it is not the persisted identity of the
  incident row (`AgentIncident.Id` is generated by the write, and the row keeps `SessionId`,
  kind and message as correlation fields, not the whole key). An equal record is refused only
  while this pump instance still holds its key and its stamp is younger than
  `TranscriptIncidentReplayWindowSeconds`; anything else is admitted and stamped, before the
  incident service is called (a failed service call keeps its stamp, so a refused replay is not
  evidence that the first admission was written).
  Capacity is bounded (`Capacity = 512` keys per runner, oldest evicted; an evicted record is
  admitted again even inside its window). The pump
  keeps one guard per `runnerId` beside `_cycles`, so a reconnect (new `PhoneHomeLiveConnection`,
  new epoch) shares the guard. Rejected: per-connection state in `ConnectionState` (a reconnect is
  the replay case); DB dedup on `(SessionId, Kind, Message)` (suppresses genuine five-minute
  repeats, whose message omits `Repeat`); encoding a replay key into `FailureReason` or the
  message (changes local rows and the existing `FailureReason == Kind` assertions); a durable
  guard table (as read, the runner hub holds no history, so no known producer replays across a
  server restart; a restart, a new pump instance, eviction or window expiry re-admits an equal
  record, and this card accepts that).
- **D-3 Window as a setting.** `PhoneHomeRunnerSettings.TranscriptIncidentReplayWindowSeconds`
  (default 900, validator: at least 1, a value below the runner's five-minute repeat is legal but
  pointless and is not enforced). Rejected: a hard-coded constant (an operator cannot widen it
  when a runner is observed re-sending; the settings class already carries the pump's other knobs).
- **D-4 Dispatch after the owner check, before nothing else.** In `PumpEventsAsync`, after
  `OwnerMatchesAsync`, the two new arms sit beside the existing three:
  `else if (parsed.TranscriptFault is not null && guard.Admit(parsed.TranscriptFault, live.Clock.GetUtcNow()))`
  calls `scope.ServiceProvider.GetRequiredService<TranscriptBindingIncidentService>().OnTranscriptFaultAsync(...)`;
  the bound arm calls `OnHeuristicBindAsync`. A refused replay is a Debug log line naming the
  session, event name and runner, and still releases the event. `NoteSessionConfirmed` already
  runs for both (they are not Exited). The service's own catch blocks are unchanged and the pump does not
  wrap them again: on a failed fault write the fault handler logs an Error and attempts a
  best-effort standalone alert (itself caught if it fails), and an unowned fault raises the
  unowned standalone alert; the bound handler only logs, both for a failed write and for an
  unowned session, and raises no alert. Rejected: a new remote-specific recording path
  (parity would then be a test obligation instead of a construction fact); recording the row
  without the owner check (a foreign runner's frame must not write an incident on this server;
  `Foreign_owner_or_epoch_events_never_reach_runtime` is the existing rule).
- **D-5 Local lane untouched.** `SessionRunnerHttpClient.cs`, `SessionRunnerEventPump.cs`,
  `TranscriptBindingIncidentService.cs` and `AgentSupervisorService.cs` are not edited. The SSE
  lane keeps passing every repeat to the service without a guard: its producer is the same hub, so the same
  no-replay reading applies, and adding the guard there would be a behaviour change this card does not
  ask for. `TranscriptBindingIncidentTests` (15 methods) is the regression row.
- **D-6 Exact binds stay silent.** No exact-bind incident is added on either lane. If an
  exact-bind receipt is wanted for remote health reads, file a card against
  `TranscriptBindingIncidentService` and `TranscriptTailer.cs:693` together; the investigation's
  "zero exact-bind incidents is expected" sentence stays true after this card.
- **D-7 `SessionAgentStatus` stays unmapped.** The phone-home mapper also drops the CARD-0162
  herdr status event. It is a different consumer (`AgentSessionRuntime`), outside the card's
  text; the Code report names it for a separate card rather than widening this one.
- **D-8 No outage reconstruction.** Bound/fault events published while the socket is down are
  lost on the remote lane, as output deltas are. Reconstructing a bind from the catch-up inventory
  (`RunnerSessionDto.TranscriptBound`, `TranscriptBindHow`, `TranscriptUnboundReason`) is possible
  and would also cover the pre-fix history, but it is a second recording path with its own dedup
  and ownership rules. Named here as the follow-up if remote incident silence must become a
  positive health signal (OD-3); without it, silence proves neither health nor non-delivery.

## Design

Mapper (S1), two branches with the SSE parser's shape:

```csharp
if (eventName == SessionRunnerEventNames.SessionTranscriptFault)
{
    var fault = JsonSerializer.Deserialize<RunnerTranscriptFaultEvent>(json, Json);
    return fault is null ? null : new SessionRunnerEvent(
        eventName, fault.SessionId,
        TranscriptFault: new SessionRunnerTranscriptFaultEvent(
            fault.SessionId, fault.Kind, fault.Detail, fault.CandidatePath, fault.UnboundSeconds, fault.Repeat));
}
if (eventName == SessionRunnerEventNames.SessionTranscriptBound)
{
    var bound = JsonSerializer.Deserialize<RunnerTranscriptBoundEvent>(json, Json);
    return bound is null ? null : new SessionRunnerEvent(
        eventName, bound.SessionId,
        TranscriptBound: new SessionRunnerTranscriptBoundEvent(bound.SessionId, bound.TranscriptPath, bound.How));
}
```

Guard (S2), one instance per runner id held by the pump:

```csharp
internal sealed class TranscriptIncidentReplayGuard(TimeSpan window, int capacity = 512)
{
    // key: the parsed record (value equality); value: when it was admitted.
    private readonly Dictionary<object, DateTimeOffset> _seen = new();
    private readonly Queue<object> _order = new();
    public bool Admit(SessionRunnerTranscriptFaultEvent fault, DateTimeOffset now) => AdmitKey(fault, now);
    public bool Admit(SessionRunnerTranscriptBoundEvent bound, DateTimeOffset now) => AdmitKey(bound, now);
    // AdmitKey: refuse when seen and now - seenAt < window; otherwise stamp (evicting the oldest past capacity) and admit.
}
```

Pump (S2), inside the existing `else if` chain after `OwnerMatchesAsync`:

```csharp
else if (parsed.TranscriptFault is { } fault)
{
    if (GuardFor(live.RunnerId).Admit(fault, live.Clock.GetUtcNow()))
        await scope.ServiceProvider.GetRequiredService<TranscriptBindingIncidentService>().OnTranscriptFaultAsync(fault, ct);
    else
        _logger.LogDebug("Phone-home {EventName} for session {SessionId} on runner {RunnerId} is a replay; not recorded again", ...);
}
else if (parsed.TranscriptBound is { } bound) { /* same shape with OnHeuristicBindAsync */ }
```

## Slices

Each slice is independently landable. S1 is 30-45 minutes; S2 is 45-60 minutes. S2 depends on
S1's mapper branches (its pump tests drive real frames through the mapper), so S2 lands after S1
or is dispatched as one Code task carrying both commits in order.

### S1 Map both events on the phone-home path

Files: `server/Infrastructure/Agents/SessionRunner/RunnerContractMapper.cs` (two branches);
`tests/Antiphon.Tests/Application/RunnerContractMapperTranscriptEventTests.cs` (new,
`[Category("Unit")]`, no database, no host; 6 methods).

Decisions: D-1. Risks: none on the wire; the only behavioural change downstream of S1 alone is that
`PhoneHomeRunnerClient.StreamEventsAsync` now yields the two events to any caller, and no caller
dispatches them until S2 (ground-truth row 4).

Tests first, case -> mutation -> red:

- Fault frame round trip -> delete the fault branch -> `Fault_frame_round_trips_every_field` red
  (`ParseEvent` returns null). The test serialises
  `new RunnerTranscriptFaultEvent(sid, TranscriptFaultKinds.AdoptionRefused, "no prompt in it matches input delivered to this session", "/home/a/.claude/projects/x/37512455.jsonl", 184.5, 3)`
  once with default `JsonSerializer.Serialize` (the hub's spelling) and once with
  `PhoneHomeFraming.Json`, and asserts for both: `EventName == SessionTranscriptFault`,
  `SessionId == sid`, `TranscriptFault == new SessionRunnerTranscriptFaultEvent(sid, AdoptionRefused, <detail>, <path>, 184.5, 3)`
  (record equality), and `Output`, `Exited`, `Transcript`, `Adopted`, `TranscriptBound`,
  `AgentStatus` all null.
- Bound frame round trip -> swap `TranscriptPath` and `How` in the mapped record ->
  `Bound_frame_round_trips_every_field` red. Same two serialisations of
  `new RunnerTranscriptBoundEvent(sid, "/home/a/.codex/sessions/2026/10/13/rollout-abc.jsonl", TranscriptBindMethods.Discovery)`;
  asserts `TranscriptBound == new SessionRunnerTranscriptBoundEvent(sid, <path>, "discovery")`
  and the other slots null.
- Field pass-through -> hard-code `UnboundSeconds: 0` in the mapper ->
  `Fault_frame_round_trips_every_field` red on the record equality (184.5 expected).
- Old-runner shape -> (no mutation needed for red; the mutation for this case is changing the
  record default `Repeat = 1` to `0` in a scratch copy of the contracts, which is out of the
  mapper's file and is listed as PC-3's alternative) ->
  `Old_runner_fault_without_repeat_fields_defaults_to_first_report` parses
  `{"sessionId":"<sid>","kind":"TranscriptMissing","detail":"No cwd-matching transcript candidates after 60s","candidatePath":null}`
  and asserts `UnboundSeconds == 0`, `Repeat == 1`, `CandidatePath == null`.
- Forward compatibility -> `Fault_frame_with_unknown_future_field_still_parses` parses the
  fault JSON with an added `"futureField":"x"` and asserts the same record as the round trip.
- Unknown names unchanged -> `Unknown_event_name_still_returns_null` asserts
  `ParseEvent(SessionRunnerEventNames.SessionAgentStatus, "{}")` and
  `ParseEvent("SessionSomethingNew", "{}")` both return null, and
  `ParseEvent(SessionTranscriptFault, "null")` returns null.

### S2 Record the incidents in the recovery pump, behind a bounded per-runner replay guard

Files: `server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs` (two dispatch arms,
`GuardFor(runnerId)` dictionary); new
`server/Infrastructure/Agents/SessionRunner/TranscriptIncidentReplayGuard.cs`;
`server/Application/Settings/PhoneHomeRunnerSettings.cs` and
`PhoneHomeRunnerSettingsValidator.cs` (`TranscriptIncidentReplayWindowSeconds`);
`tests/Antiphon.Tests/Application/TranscriptIncidentReplayGuardTests.cs` (new, `[Category("Unit")]`,
`FakeTimeProvider`; 5 methods); `tests/Antiphon.Tests/Application/PhoneHomeTranscriptIncidentTests.cs`
(new, `[Category("Integration")]`, `PhoneHomeTestHost` + `BridgeQueueHarness` with
`TranscriptBindingIncidentService` registered through `ConfigureServices`, the same composition
`TranscriptBindingIncidentTests` uses; 5 methods);
`tests/Antiphon.Tests/Application/PhoneHomeRunnerSettingsValidatorTests.cs` (one added method for
the new bound); `docs/session-runtime-invariants.md` (one sentence after the CARD-0006 paragraph
at line 918 naming the remote lane and `PhoneHomeTranscriptIncidentTests`).

Decisions: D-2, D-3, D-4, D-5. Risks: the integration tests wait on `live.PendingEvents == 0`
(the pump releases an event in `finally` after the handler's `SaveChanges`), the seam
`Pump_survives_a_failing_event_and_releases_the_rest` already uses, bounded by the sibling tests'
five-second `WaitUntilAsync`; no fixed delay and no retry. The host is started with a
`FakeTimeProvider` so `live.Clock` is deterministic; the clock is never advanced under a live
socket (lease expiry). The owner fixture seeds an `AgentSession` with `RunnerId`/`RunnerStoreId`
(as `PhoneHomeEventPumpTests.SeedAsync`) plus an `AgentTask` row pointing at the harness agent
(as `Delegate_task_session_fault_records_incident_on_the_task_agent`), because a remote delegate
session is owned through the task row.

Tests first, case -> mutation -> red:

Guard (unit, fake clock at a fixed instant):

- First admitted, identical replay refused -> make `Admit` return true unconditionally ->
  `First_fault_is_admitted_and_identical_replay_is_refused` red (second call must be false).
- Genuine repeat admitted -> drop `Repeat`/`UnboundSeconds` from the key by keying on
  `(SessionId, Kind)` only -> `A_genuine_repeat_with_higher_repeat_count_is_admitted` red
  (`Repeat 2`, `UnboundSeconds 300` after `Repeat 1`, `UnboundSeconds 0` must be true).
- Window expiry -> never expire (ignore `now`) -> `Replay_after_the_window_is_admitted_again`
  red (advance the fake clock by window + 1 s; the identical record must be admitted).
- Bound keys -> key bound events on `SessionId` only -> `Bound_identical_replay_is_refused_and_a_different_path_is_admitted`
  red (same session, different `TranscriptPath` must be admitted; identical must be refused).
- Bounded memory -> remove eviction -> `Guard_evicts_the_oldest_key_past_capacity` red
  (capacity 3: after four distinct records the first is admitted again; the fourth is refused).

Pump (integration):

- Remote fault records the local row -> remove the fault arm ->
  `Remote_fault_frame_records_the_local_incident_row` red (zero `TranscriptBindFailed` rows). The
  test emits one `SessionTranscriptFault` frame on the live epoch and asserts exactly one
  `AgentIncidents` row with `AgentId == harness agent`, `SessionId == remote session`,
  `Kind == TranscriptBindFailed`, `Severity == Warning`, `FailureReason == "AdoptionRefused"`,
  `Message == "No transcript is bound to this session. AdoptionRefused: <detail> (candidate <path>)"`,
  and one alert with `DedupKey == $"supervisor:TranscriptBindFailed:{agentId}"`.
- Remote bind records the Info row without an alert -> remove the bound arm ->
  `Remote_bound_frame_records_an_info_incident_without_alert` red. Asserts one
  `TranscriptBoundByDiscovery` row, `Severity == Info`,
  `Message == "Transcript bound by discovery: <path>"`, and no alert with the
  `TranscriptBoundByDiscovery` dedup key.
- Replay across a reconnect inside the window (frozen clock, key held) records once, genuine repeat records again -> key the guard per
  connection (`ConnectionState`) -> `Replayed_frames_across_a_reconnect_record_once` red (two
  fault rows after the reconnect). Steps: emit fault (Repeat 1) and bound on peer A; wait
  `PendingEvents == 0`; abort A; connect peer B, wait for a new live connection, start the pump on
  it; emit the identical fault and bound frames; wait; assert 1 and 1; emit the same frame twice
  more on B (same epoch); assert still 1; emit fault with `Repeat 2`, `UnboundSeconds 300`; assert
  2 fault rows. Also red under "`Admit` always true" (PC-6).
- Foreign owner never records -> move the arms above `OwnerMatchesAsync` ->
  `Foreign_owner_transcript_events_never_record` red. A session seeded for `other-runner` with a
  different store id receives both frames; zero incident rows and zero alerts for that session.
- Text parity with the local lane -> replace the service call with a direct
  `AgentSupervisorService.RecordIncidentAsync` carrying a different message ->
  `Incident_text_matches_the_local_lane` red. The test drives one fault JSON through the pump for
  session A and calls `TranscriptBindingIncidentService.OnTranscriptFaultAsync` directly (the SSE
  pump's exact call at `SessionRunnerEventPump.cs:427`) with `RunnerContractMapper.ParseEvent(...)
  .TranscriptFault` for session B, then compares the two rows field by field: `Kind`, `Severity`,
  `Message`, `FailureReason`, `ExitCode`; and the same for one bound JSON.

Settings:

- Validator bound -> delete the validator line -> `Replay_window_below_one_second_is_refused`
  (added to `PhoneHomeRunnerSettingsValidatorTests`) red; the message is
  `PhoneHomeRunner:TranscriptIncidentReplayWindowSeconds must be at least 1.`

## Ordering and collisions

Order: S1 then S2 (S2's integration tests need the mapper). One Code task may carry both as two
commits; two tasks are also fine with S2 dispatched after S1 lands.

| Slice | Files touched | Collides with |
|---|---|---|
| S1 | `server/Infrastructure/Agents/SessionRunner/RunnerContractMapper.cs`; new `tests/Antiphon.Tests/Application/RunnerContractMapperTranscriptEventTests.cs` | None in flight. CARD-1032 S6 (Review in flight, task `75a50129`) edited `PhoneHomeRunnerClient.cs`, `PhoneHomeCommandDispatcher.cs`, `PhoneHomeRuntimeAdapter.cs`, `SessionRunnerRuntime.cs`; the mapper is not in its file list (`2026-10-11-card-1032-runner-mirror-cleanup-plan.md` S6, `2026-10-13-card-1032-s6-code-evidence.md:36`). The mapper's last edits are CARD-0519/quota (`3e53e6583`, `69b93b499`), landed. |
| S2 | `PhoneHomeRecoveryPump.cs`; new `TranscriptIncidentReplayGuard.cs`; `PhoneHomeRunnerSettings.cs`, `PhoneHomeRunnerSettingsValidator.cs`; new `PhoneHomeTranscriptIncidentTests.cs`, new `TranscriptIncidentReplayGuardTests.cs`; `PhoneHomeRunnerSettingsValidatorTests.cs`; `docs/session-runtime-invariants.md` | CARD-1032 touches `PhoneHomeRunnerClient.cs`, not the pump; `PhoneHomeEventPumpTests.cs` is read, not edited (new classes instead); the round-3 repair (R2-F3) later edited three of its inherited methods without renaming any. The two other in-flight Code tasks (CARD-0506 on server2, task `65ec918e`; CARD-0669 on desktop, task `dfaf5740`) published no scope on the task list; the Code dispatcher confirms neither touches `server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs` or `PhoneHomeRunnerSettings*.cs` before dispatch, and defers on a match. The pump's last edits (`3e53e6583`, `c468ce1b2`) are landed. |

Scope string for both Code dispatches: `server/Infrastructure/Agents/SessionRunner/RunnerContractMapper.cs,server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs,server/Infrastructure/Agents/SessionRunner/TranscriptIncidentReplayGuard.cs,server/Application/Settings/PhoneHomeRunnerSettings*.cs,tests/Antiphon.Tests/Application/RunnerContractMapperTranscriptEventTests.cs,tests/Antiphon.Tests/Application/PhoneHomeTranscriptIncidentTests.cs,tests/Antiphon.Tests/Application/TranscriptIncidentReplayGuardTests.cs,tests/Antiphon.Tests/Application/PhoneHomeRunnerSettingsValidatorTests.cs,docs/session-runtime-invariants.md`.

## Operator decisions (OD-n, with recommendation)

- **OD-1** Replay window default. Recommended: 900 s (D-3). Genuine repeats do not depend on this
  value: a runner repeat carries a higher `Repeat` and a longer `UnboundSeconds`, so it is a
  different key and is admitted at any window. The window only bounds how long an equal whole
  record is refused (for a bind: the same `SessionId`, `TranscriptPath` and `How`; a changed
  `How` on the same path is a different key), and only while the same pump instance still holds
  its key (not evicted at capacity, no server restart). Alternative: 60 s (an equal re-bind record
  more than a minute later records again) at the cost of admitting an equal frame delayed by a
  slow reconnect.
- **OD-2** One Code task for S1+S2 (recommended; the mapper diff is small and S2's tests need it)
  versus two tasks. Two tasks only if the dispatcher wants S1 landed first for an immediate partial
  fix; S1 alone records nothing.
- **OD-3** Whether to commission the D-8 catch-up reconstruction as a follow-up card now.
  Recommended: file it, do not block this card on it. After this card, remote incident silence
  still cannot establish either that no event was delivered or that the session is healthy:
  admission, ownership and persistence are separate boundaries. A delivered event can be refused
  by a held replay key, can resolve no owning agent (the fault then raises only an unowned
  standalone alert; the bound only logs), or can fail its incident write after admission (the
  fault then attempts a best-effort standalone alert; the bound only logs).
  `A_fault_whose_incident_cannot_be_written_names_the_database_error_and_still_alerts` shows a
  delivered service call that leaves no incident. Frames sent while the socket is down are lost (D-8).
- **OD-4** Whether to file the D-7 `SessionAgentStatus` phone-home gap as its own card.
  Recommended: yes, as a one-line card pointing at this plan's ground-truth row 3.

## Verification design

### V/R matrix

| ID | Statement | Slice | Proving tests | Rows |
|---|---|---|---|---|
| V-1 | The phone-home mapper maps `SessionTranscriptFault` and `SessionTranscriptBound` frames into the same server records the SSE parser produces, passing every field through under both JSON spellings. | S1 | `RunnerContractMapperTranscriptEventTests.Fault_frame_round_trips_every_field`, `RunnerContractMapperTranscriptEventTests.Bound_frame_round_trips_every_field` | CP-1 |
| V-2 | An old-runner fault payload without `unboundSeconds`/`repeat` parses with defaults 0 and 1; an unknown future field is ignored; unknown event names and null payloads still return null. | S1 | `RunnerContractMapperTranscriptEventTests.Old_runner_fault_without_repeat_fields_defaults_to_first_report`, `RunnerContractMapperTranscriptEventTests.Fault_frame_with_unknown_future_field_still_parses`, `RunnerContractMapperTranscriptEventTests.Unknown_event_name_still_returns_null` | CP-1 |
| V-3 | The replay guard refuses a record equal to one it admitted while the key is held and the window has not elapsed, admits a record that differs in any field, admits the equal record again after the window or after its key is evicted at capacity, and is bounded. | S2 | `TranscriptIncidentReplayGuardTests.First_fault_is_admitted_and_identical_replay_is_refused`, `TranscriptIncidentReplayGuardTests.A_genuine_repeat_with_higher_repeat_count_is_admitted`, `TranscriptIncidentReplayGuardTests.Replay_after_the_window_is_admitted_again`, `TranscriptIncidentReplayGuardTests.Bound_identical_replay_is_refused_and_a_different_path_is_admitted`, `TranscriptIncidentReplayGuardTests.Guard_evicts_the_oldest_key_past_capacity` | CP-3 |
| V-4 | A remote fault frame for an owned session records the same `TranscriptBindFailed` row and alert a local launch records; a remote bound frame records the same Info `TranscriptBoundByDiscovery` row with no alert. | S2 | `PhoneHomeTranscriptIncidentTests.Remote_fault_frame_records_the_local_incident_row`, `PhoneHomeTranscriptIncidentTests.Remote_bound_frame_records_an_info_incident_without_alert`, `PhoneHomeTranscriptIncidentTests.Incident_text_matches_the_local_lane` | CP-4 |
| V-5 | With the clock inside the window and the key held, identical frames replayed on the same epoch or after a reconnect record once; a genuine repeat (`Repeat` incremented) records again. | S2 | `PhoneHomeTranscriptIncidentTests.Replayed_frames_across_a_reconnect_record_once` | CP-4 |
| V-6 | Frames for a session owned by another runner or store never record an incident or alert. | S2 | `PhoneHomeTranscriptIncidentTests.Foreign_owner_transcript_events_never_record` | CP-4 |
| V-7 | The replay window setting is validated at startup. | S2 | `PhoneHomeRunnerSettingsValidatorTests.Replay_window_below_one_second_is_refused` | CP-6 |
| R-1 | The existing phone-home transcript wire test still passes with the widened mapper. | S1 | `QuotaEvidenceWireTests` (4 existing) | CP-2 |
| R-2 | The recovery pump's existing owner, catch-up, overflow, failing-event and restart behaviour is unchanged. | S2 | `PhoneHomeEventPumpTests` (8 existing; since R2-F3 `Live_buffer_overflow_withholds_ready_and_recovers` asserts the `phone_home_event_overflow` close of an eligible connection and recovery of the next) | CP-5 |
| R-3 | The local lane's incident, severity, escalation and ClaimRevoked rules are unchanged. | S2 | `TranscriptBindingIncidentTests` (15 existing) | CP-7 |
| R-4 | The existing settings validator rules are unchanged. | S2 | `PhoneHomeRunnerSettingsValidatorTests` (7 existing) | CP-6 |

### Positive controls (all pending; SourceLanding Mutation, method-scoped)

| PC | Exact detecting filter | Mutation (scratch copy) | Expected red |
|---|---|---|---|
| PC-1 | `/*/*/RunnerContractMapperTranscriptEventTests/Fault_frame_round_trips_every_field` | Delete the `SessionTranscriptFault` branch from `RunnerContractMapper.ParseEvent` | `ParseEvent` result is null |
| PC-2 | `/*/*/RunnerContractMapperTranscriptEventTests/Bound_frame_round_trips_every_field` | Swap `bound.TranscriptPath` and `bound.How` in the mapped record | record equality on `TranscriptBound` |
| PC-3 | `/*/*/RunnerContractMapperTranscriptEventTests/Fault_frame_round_trips_every_field` | Pass `UnboundSeconds: 0` instead of `fault.UnboundSeconds` | record equality (184.5 expected) |
| PC-4 | `/*/*/PhoneHomeTranscriptIncidentTests/Remote_fault_frame_records_the_local_incident_row` | Remove the `TranscriptFault` arm from `PumpEventsAsync` | zero `TranscriptBindFailed` rows |
| PC-5 | `/*/*/PhoneHomeTranscriptIncidentTests/Remote_bound_frame_records_an_info_incident_without_alert` | Remove the `TranscriptBound` arm from `PumpEventsAsync` | zero `TranscriptBoundByDiscovery` rows |
| PC-6 | `/*/*/TranscriptIncidentReplayGuardTests/First_fault_is_admitted_and_identical_replay_is_refused` | `Admit` returns true unconditionally | second `Admit` expected false |
| PC-7 | `/*/*/TranscriptIncidentReplayGuardTests/A_genuine_repeat_with_higher_repeat_count_is_admitted` | Key faults on `(SessionId, Kind)` only | `Repeat 2` expected admitted |
| PC-8 | `/*/*/PhoneHomeTranscriptIncidentTests/Replayed_frames_across_a_reconnect_record_once` | Hold the guard in `ConnectionState` instead of per runner id | two fault rows after the reconnect |
| PC-9 | `/*/*/TranscriptIncidentReplayGuardTests/Replay_after_the_window_is_admitted_again` | Ignore `now` in `AdmitKey` | identical record expected admitted after window + 1 s |
| PC-10 | `/*/*/PhoneHomeTranscriptIncidentTests/Foreign_owner_transcript_events_never_record` | Dispatch the two arms before `OwnerMatchesAsync` | incident rows for the foreign session |
| PC-11 | `/*/*/PhoneHomeTranscriptIncidentTests/Incident_text_matches_the_local_lane` | Replace the service call with `AgentSupervisorService.RecordIncidentAsync(..., "Remote: " + message)` | `Message` mismatch |
| PC-12 | `/*/*/PhoneHomeRunnerSettingsValidatorTests/Replay_window_below_one_second_is_refused` | Delete the validator line | no failure message |

### Checkpoints

All rows run on the default placement (server2 per `GET /api/runner-defaults`; omit `-Runner`
and `-Platform`). Integration rows need the shared test Postgres and are serial because they
open a real WebSocket host and an isolated schema. No row runs a namespace or the Unit lane.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1219-s1/` | mapper-transcript-events | `/*/*/RunnerContractMapperTranscriptEventTests/*` | V-1, V-2 | all listed, 0 failed | 6 | 8 | false |
| CP-2 | S1 | CP-1 | quota-wire-regression | `/*/*/QuotaEvidenceWireTests/*` | R-1 | all listed, 0 failed | 4 | 2 | true |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1219-s2/` | replay-guard | `/*/*/TranscriptIncidentReplayGuardTests/*` | V-3 | all listed, 0 failed | 5 | 8 | false |
| CP-4 | S2 | CP-3 | pump-transcript-incidents | `/*/*/PhoneHomeTranscriptIncidentTests/*` | V-4, V-5, V-6 | all listed, 0 failed | 5 | 4 | true |
| CP-5 | S2 | CP-3 | pump-regression | `/*/*/PhoneHomeEventPumpTests/*` | R-2 | all listed, 0 failed | 8 | 4 | true |
| CP-6 | S2 | CP-3 | settings-validator | `/*/*/PhoneHomeRunnerSettingsValidatorTests/*` | V-7, R-4 | all listed, 0 failed | 8 | 1 | false |
| CP-7 | S2 | CP-3 | local-lane-regression | `/*/*/TranscriptBindingIncidentTests/*` | R-3 | all listed, 0 failed | 15 | 5 | true |

Build and run through the checkpoint tool (`run --plan <this file> --after S1 --expected-source-sha
<full committed SHA> --serial --max-wait 55s`, then `wait` until the exit is not 75), one run per
committed slice group. The tool bootstrap is one unlisted, build-slot-gated isolated build
(`bin-c1219-tool/`, `UseAppHost=false` on Linux); every `bin-c1219-*` directory is removed before
the task ends.

### Cost

Rows sum to 32 estimated minutes (S1: 10; S2: 22). `-ExpectAbout` for a single Code task carrying
both slices is 32 minutes plus 60-90 minutes of authoring; for two tasks, S1 is 10 + 30-45 and S2
is 22 + 45-60.

--- next stage ---
next: test-design
handoff: Audit D-1..D-8 against the named files, fix the exact method names, JSON literals and expected message strings for the six mapper tests, five guard tests, five pump tests and one validator test, confirm the PendingEvents==0 wait and the FakeTimeProvider host start are sufficient without a sleep, then release one Code task for S1+S2 (OD-2) on the default placement.
artifact: docs/superpowers/plans/2026-10-13-card-1219-remote-transcript-events-plan.md
