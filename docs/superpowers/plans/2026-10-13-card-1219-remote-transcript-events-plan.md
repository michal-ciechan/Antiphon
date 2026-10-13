# CARD-1219 remote transcript-bind and transcript-fault events on the phone-home path

Date: 2026-10-13. Stage: Plan; next stage: TestDesign. Plan task `461e7494-0346-4b7e-bb91-1111a0ba5128`.
Inspected checkout: `00fa6b884a5f127982cff26a677aae74e6666650` (master tip at dispatch). Tests,
counts and positive controls below were proposed by Plan; Plan executed none. Card evidence:
`docs/investigations/2026-10-13-card-0073-transcript-binding-status.md` on
`origin/feat/card-task-6813e7cf` at `6b354b445` (not on master).

Scope: `RunnerContractMapper.ParseEvent` returns null for `SessionTranscriptFault` and
`SessionTranscriptBound`, and `PhoneHomeRecoveryPump.PumpEventsAsync` dispatches only Output, Exited
and Transcript; S1 maps both events and S2 dispatches them. No wire, contract-version or capability
change.

Session checklist: this plan adds or changes no session that waits for input, so the CARD-1083
release checklist does not apply.

## Evidence and ground truth

| Card assumption / evidence claim | What the code actually does | Design consequence |
|---|---|---|
| The runner emits both events for remote sessions. | `TranscriptTailer.cs:693-699` publishes `SessionTranscriptBound` for a bind whose `How` is neither `exact` nor `sidecar`; `CodexTranscriptTailer.cs:514-518` publishes it for a non-sidecar Codex bind (`discovery`); faults publish at `TranscriptTailer.cs:469`, `:955`, `:1026`, `:1077`, `:1126`, `CodexTranscriptTailer.cs:336`, `:720`, `:770`, `:808`, `GrokTranscriptTailer.cs:346`. The hub serialises the record with default `JsonSerializer.Serialize` (`SessionRunnerRuntime.cs:4475`); `PhoneHomeConnectionService.SendEventAsync` (`:359`) forwards name and JSON unchanged. | The server-side mapping consumes the existing `RunnerTranscriptFaultEvent` / `RunnerTranscriptBoundEvent` records. No runner change. |
| "A launch that bound exactly leaves no incident." | An exact Claude bind publishes nothing (`TranscriptTailer.cs:693`), and `TranscriptBindingIncidentService` has no exact-bind incident. | Out of scope (D-6). |
| The mapper drops the events. | `RunnerContractMapper.ParseEvent` (`RunnerContractMapper.cs:75-124`) maps SessionOutput, SessionExited, SessionAdopted, SessionStarted, SessionTranscript and returns null otherwise. The SSE parser `SessionRunnerHttpClient.ParseEvent` (`:1007-1032`) maps both events into `SessionRunnerTranscriptFaultEvent` / `SessionRunnerTranscriptBoundEvent` (`SessionRunnerDtos.cs:119-131`), which `SessionRunnerEvent` already carries (`:149-150`). | S1 adds the two branches to the mapper with the SSE parser's field mapping. `SessionAgentStatus` stays out (D-7). |
| The pump drops them after parsing. | `PhoneHomeRecoveryPump.PumpEventsAsync` (`:356-400`) handles `Output`, `Exited`, `Transcript` only; `PhoneHomeRunnerClient.StreamEventsAsync` (`:555-566`) yields whatever the mapper returns. The SSE pump streams only the local client (`RoutingSessionRunnerClient.cs:120-121`). | S2 adds the two dispatches after the owner check, calling `OnTranscriptFaultAsync` / `OnHeuristicBindAsync`, the methods `SessionRunnerEventPump.cs:421-434` calls (inspection). `Remote_fault_frame_records_the_local_incident_row` asserts one row. |
| Replayed events after a reconnect would duplicate incidents. | `SessionRunnerEventHub.Subscribe` / `SubscribeBounded` create a fresh channel per subscriber with no history (`SessionRunnerRuntime.cs:4439-4474`); `ReleaseUnread` drains without re-queueing (`:4562`). Server catch-up re-reads List and transcripts only (`PhoneHomeRecoveryPump.CatchUpAsync`, `:295-347`). Runner fault re-publication by path (inspection): refusal and no-candidate faults (`TranscriptTailer.cs:1027`, `:1078`) repeat at the `DefaultRefusalFaultRepeat` interval (5 min, `:46`) with `Repeat` and `UnboundSeconds` advanced; `ReportRootFault` (`:940-960`, called at `:572` and `:632`) repeats an equal record; child-exit faults (for example `:1127`) publish once. | The guard keys on the whole parsed record per runner (D-2). An equal record is refused only while the guard still holds its key within the window (`First_fault_is_admitted_and_identical_replay_is_refused`); expiry (`Replay_after_the_window_is_admitted_again`), eviction (`Guard_evicts_the_oldest_key_past_capacity`) and a new pump instance admit again. A record with flipped fields is admitted (`A_genuine_repeat_with_higher_repeat_count_is_admitted`). Outage events are not reconstructed (D-8). |
| DB-backed dedup is possible. | `AgentSupervisorService.RecordIncidentAsync` (`:534-575`) appends unconditionally; the fault message (`TranscriptBindingIncidentService.cs:92-110`) omits `Repeat` and `UnboundSeconds` (`Remote_fault_frame_records_the_local_incident_row` asserts the message for a `Repeat 3`, `UnboundSeconds 184.5` record). `FailureReason` is `fault.Kind` (`TranscriptBindingIncidentTests`, `:55`, `:230`). | Rejected: message- or FailureReason-keyed DB dedup (D-2). |
| Incident text matches local. | The SSE pump and the new pump arms call `TranscriptBindingIncidentService.OnTranscriptFaultAsync` / `OnHeuristicBindAsync` (inspection at the Code tip: `PhoneHomeRecoveryPump.cs:392-407`, `SessionRunnerEventPump.cs:421-434`); owner resolution is DB-only (`SessionOwnerLookup.cs:34-50`). | `Incident_text_matches_the_local_lane` compares the output rows with a direct service call using matching diagnostic fields on a different session. |
| Old runners must keep working. | Phone-home arrived with CARD-0490 (`b6fddd73e`, 2026-09-20), after CARD-0101 (`31020516a`, 2026-08-20); the fault record defaults `UnboundSeconds = 0`, `Repeat = 1` for the older shape. `RunnerContractMapper.Json` is `JsonSerializerDefaults.Web`. | `Old_runner_fault_without_repeat_fields_defaults_to_first_report`, `Fault_frame_with_unknown_future_field_still_parses`, `Unknown_event_name_still_returns_null`. |
| Fake clock is available on this path. | `PhoneHomeLiveConnection.Clock` is the directory's `TimeProvider` (`PhoneHomeRunnerDirectory.cs:380-381`), which `PhoneHomeTestHost.StartAsync(clock:)` injects (`PhoneHomeTestHost.cs:54`, `:116`); `PhoneHomeOutageHarness` already runs the host on a `FakeTimeProvider`. | The guard reads `live.Clock`; window expiry is tested in `Replay_after_the_window_is_admitted_again`. |
| Platform can be inferred from the card. | Card says Any. `GET /api/runner-defaults` revision 2: `globalRunnerId` server2, no kind overrides. `GET /api/session-runners`: desktop (windows, capacity 2), server2 (linux, capacity 10), one stale `server2-temp`. | Rows run on the default placement; omit `-Runner` and `-Platform`. |

## Decisions

- **D-1 Mapper branches, no contract change.** `RunnerContractMapper.ParseEvent` gains two
  branches after `SessionTranscript` that deserialise `RunnerTranscriptFaultEvent` and
  `RunnerTranscriptBoundEvent` with `RunnerContractMapper.Json` and return
  `new SessionRunnerEvent(eventName, SessionId, TranscriptFault: ...)` /
  `(..., TranscriptBound: ...)`, the shape of `SessionRunnerHttpClient.ParseEvent`. A null
  deserialisation returns null; a malformed payload throws
  (`Malformed_payload_throws_like_the_other_branches`). `PhoneHomeProtocol.Version`,
  `RunnerCapabilityFeatures` and `RunnerCapabilitiesDto` are untouched. Rejected: a feature token
  such as `transcriptDiagnosticsV1`; a protocol version bump.
- **D-2 Replay guard keyed on the parsed record, per runner, in memory.** New
  `TranscriptIncidentReplayGuard` (`server/Infrastructure/Agents/SessionRunner/`), signature as the
  test design's D-2 row. The key is the record (fault: `SessionId, Kind, Detail, CandidatePath,
  UnboundSeconds, Repeat`; bind: `SessionId, TranscriptPath, How`), held in memory by one pump
  instance and not stored in the incident row. `Capacity = 512` keys per runner. Tests: V-3, V-5.
  Rejected: per-connection state in `ConnectionState`; DB dedup on `(SessionId, Kind, Message)`;
  a replay key in `FailureReason` or the message; a durable guard table.
- **D-3 Window as a setting.** `PhoneHomeRunnerSettings.TranscriptIncidentReplayWindowSeconds`
  (default 900, minimum 1; `Replay_window_below_one_second_is_refused`). Rejected: a hard-coded
  constant.
- **D-4 Dispatch after the owner check.** In `PumpEventsAsync`, after `OwnerMatchesAsync`, a fault
  arm calls `TranscriptBindingIncidentService.OnTranscriptFaultAsync` and a bound arm calls
  `OnHeuristicBindAsync` when the guard admits the record; a refused record writes a Debug log line.
  The service is unchanged. `TranscriptBindingIncidentTests` (R-3) covers the fault method's
  unowned and write-failure branches; the bound method's unowned and write-failure branches are
  inspected, not tested. Rejected: a remote-specific recording path; recording without the owner
  check (`Foreign_owner_or_epoch_events_never_reach_runtime` is the existing rule).
- **D-5 Local lane untouched.** `SessionRunnerHttpClient.cs`, `SessionRunnerEventPump.cs`,
  `TranscriptBindingIncidentService.cs` and `AgentSupervisorService.cs` are not edited.
  `TranscriptBindingIncidentTests` (15 methods) is the regression row.
- **D-6 No exact-bind incident.** None is added on either lane. A wanted exact-bind receipt is a
  separate card against `TranscriptBindingIncidentService` and `TranscriptTailer.cs:693`.
- **D-7 `SessionAgentStatus` stays unmapped.** The Code report names it for a separate card.
- **D-8 No outage reconstruction.** Bound/fault events published while the socket is down are not
  reconstructed. Reconstruction from the catch-up inventory (`RunnerSessionDto.TranscriptBound`,
  `TranscriptBindHow`, `TranscriptUnboundReason`) is the OD-3 follow-up.

## Design

Implemented in `RunnerContractMapper.cs`, `TranscriptIncidentReplayGuard.cs` and
`PhoneHomeRecoveryPump.cs`; the test design's D-2 row sets the guard signature.

## Slices

S1 is 30-45 minutes; S2 is 45-60 minutes. S2's pump tests drive frames through S1's mapper, so S2
lands after S1 or both go in one Code task as two commits in order.

### S1 Map both events on the phone-home path

Files: `server/Infrastructure/Agents/SessionRunner/RunnerContractMapper.cs` (two branches);
`tests/Antiphon.Tests/Application/RunnerContractMapperTranscriptEventTests.cs` (new,
`[Category("Unit")]`, no database, no host; 6 methods).

Decisions: D-1.

Tests (literals and oracles: the test design's mapper table):

- `Fault_frame_round_trips_every_field` (PC-1, PC-3)
- `Bound_frame_round_trips_every_field` (PC-2)
- `Old_runner_fault_without_repeat_fields_defaults_to_first_report` (PC-13)
- `Fault_frame_with_unknown_future_field_still_parses` (PC-14)
- `Unknown_event_name_still_returns_null` (PC-15)
- `Malformed_payload_throws_like_the_other_branches` (PC-19)

### S2 Record the incidents in the recovery pump, behind a bounded per-runner replay guard

Files: `server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs` (two dispatch arms,
per-runner guard dictionary); new
`server/Infrastructure/Agents/SessionRunner/TranscriptIncidentReplayGuard.cs`;
`server/Application/Settings/PhoneHomeRunnerSettings.cs` (`TranscriptIncidentReplayWindowSeconds`
and its rule); `tests/Antiphon.Tests/Application/TranscriptIncidentReplayGuardTests.cs` (new,
`[Category("Unit")]`, `FakeTimeProvider`; 5 methods);
`tests/Antiphon.Tests/Application/PhoneHomeTranscriptIncidentTests.cs` (new,
`[Category("Integration")]`, `PhoneHomeTestHost` + `BridgeQueueHarness` with
`TranscriptBindingIncidentService` registered through `ConfigureServices`; 5 methods);
`tests/Antiphon.Tests/Application/PhoneHomeRunnerSettingsValidatorTests.cs` (one added method);
`docs/session-runtime-invariants.md` (one sentence after the CARD-0006 paragraph).

Decisions: D-2, D-3, D-4, D-5. Fixture, waits and oracles: the test design's pump recipe.

Tests:

- Guard: `First_fault_is_admitted_and_identical_replay_is_refused` (PC-6),
  `A_genuine_repeat_with_higher_repeat_count_is_admitted` (PC-7),
  `Replay_after_the_window_is_admitted_again` (PC-9),
  `Bound_identical_replay_is_refused_and_a_different_path_is_admitted` (PC-16),
  `Guard_evicts_the_oldest_key_past_capacity` (PC-17).
- Pump: `Remote_fault_frame_records_the_local_incident_row` (PC-4),
  `Remote_bound_frame_records_an_info_incident_without_alert` (PC-5),
  `Replayed_frames_across_a_reconnect_record_once` (PC-8),
  `Foreign_owner_transcript_events_never_record` (PC-10),
  `Incident_text_matches_the_local_lane` (PC-11).
- Settings: `Replay_window_below_one_second_is_refused` (PC-12, PC-18); message
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

- **OD-1** Replay window default. Recommended: 900 s (D-3). Alternative: 60 s.
- **OD-2** One Code task for S1+S2 (recommended) versus two tasks; S1 alone records nothing.
- **OD-3** Whether to commission the D-8 catch-up reconstruction as a follow-up card now.
  Recommended: file it; do not block this card on it.
- **OD-4** Whether to file the D-7 `SessionAgentStatus` phone-home gap as its own card.
  Recommended: yes, as a one-line card pointing at this plan's ground-truth row 3.

## Verification design

### V/R matrix

| ID | Statement | Slice | Proving tests | Rows |
|---|---|---|---|---|
| V-1 | The phone-home mapper maps `SessionTranscriptFault` and `SessionTranscriptBound` frames into the expected `SessionRunnerTranscriptFaultEvent` / `SessionRunnerTranscriptBoundEvent` records under both JSON spellings. | S1 | `RunnerContractMapperTranscriptEventTests.Fault_frame_round_trips_every_field`, `RunnerContractMapperTranscriptEventTests.Bound_frame_round_trips_every_field` | CP-1 |
| V-2 | An old-runner fault payload without `unboundSeconds`/`repeat` parses with defaults 0 and 1; an unknown future field is ignored; unknown event names and null payloads return null. | S1 | `RunnerContractMapperTranscriptEventTests.Old_runner_fault_without_repeat_fields_defaults_to_first_report`, `RunnerContractMapperTranscriptEventTests.Fault_frame_with_unknown_future_field_still_parses`, `RunnerContractMapperTranscriptEventTests.Unknown_event_name_still_returns_null` | CP-1 |
| V-3 | The replay guard refuses an equal record only while it still holds the key within the window; a record with a flipped field, the equal record after the window, and an evicted key are admitted. | S2 | `TranscriptIncidentReplayGuardTests.First_fault_is_admitted_and_identical_replay_is_refused`, `TranscriptIncidentReplayGuardTests.A_genuine_repeat_with_higher_repeat_count_is_admitted`, `TranscriptIncidentReplayGuardTests.Replay_after_the_window_is_admitted_again`, `TranscriptIncidentReplayGuardTests.Bound_identical_replay_is_refused_and_a_different_path_is_admitted`, `TranscriptIncidentReplayGuardTests.Guard_evicts_the_oldest_key_past_capacity` | CP-3 |
| V-4 | A remote fault frame for an owned session records a `TranscriptBindFailed` row and alert; a remote bound frame records an Info `TranscriptBoundByDiscovery` row with no alert; the compared output fields match a direct service call. | S2 | `PhoneHomeTranscriptIncidentTests.Remote_fault_frame_records_the_local_incident_row`, `PhoneHomeTranscriptIncidentTests.Remote_bound_frame_records_an_info_incident_without_alert`, `PhoneHomeTranscriptIncidentTests.Incident_text_matches_the_local_lane` | CP-4 |
| V-5 | With the clock frozen inside the window and the same pump instance holding the keys, equal frames on the same epoch or after a reconnect record once; the fault with `Repeat` and `UnboundSeconds` advanced records again. | S2 | `PhoneHomeTranscriptIncidentTests.Replayed_frames_across_a_reconnect_record_once` | CP-4 |
| V-6 | Frames for two sessions owned by another runner or store, and for an unknown session, record no incident or alert. | S2 | `PhoneHomeTranscriptIncidentTests.Foreign_owner_transcript_events_never_record` | CP-4 |
| V-7 | The replay window setting is validated at startup. | S2 | `PhoneHomeRunnerSettingsValidatorTests.Replay_window_below_one_second_is_refused` | CP-6 |
| R-1 | The existing phone-home transcript wire test still passes with the widened mapper. | S1 | `QuotaEvidenceWireTests` (4 existing) | CP-2 |
| R-2 | The recovery pump's existing owner, catch-up, overflow, failing-event and restart tests pass. | S2 | `PhoneHomeEventPumpTests` (8 existing; since R2-F3 `Live_buffer_overflow_withholds_ready_and_recovers` asserts the `phone_home_event_overflow` close of an eligible connection and recovery of the next) | CP-5 |
| R-3 | The local lane's incident, severity, escalation and ClaimRevoked tests pass. | S2 | `TranscriptBindingIncidentTests` (15 existing) | CP-7 |
| R-4 | The existing settings validator tests pass. | S2 | `PhoneHomeRunnerSettingsValidatorTests` (7 existing) | CP-6 |

### Positive controls (pending; SourceLanding Mutation, method-scoped)

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

Rows run on the default placement (server2 per `GET /api/runner-defaults`; omit `-Runner`
and `-Platform`). Integration rows need the shared test Postgres and are serial. No row runs a
namespace or the Unit lane.

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
(`bin-c1219-tool/`, `UseAppHost=false` on Linux); each `bin-c1219-*` directory is removed before
the task ends.

### Cost

Rows sum to 32 estimated minutes (S1: 10; S2: 22). `-ExpectAbout` for a single Code task carrying
both slices is 32 minutes plus 60-90 minutes of authoring; for two tasks, S1 is 10 + 30-45 and S2
is 22 + 45-60.

--- next stage ---
next: test-design
handoff: Audit D-1..D-8 against the named files, fix the exact method names, JSON literals and expected message strings for the six mapper tests, five guard tests, five pump tests and one validator test, confirm the PendingEvents==0 wait and the FakeTimeProvider host start are sufficient without a sleep, then release one Code task for S1+S2 (OD-2) on the default placement.
artifact: docs/superpowers/plans/2026-10-13-card-1219-remote-transcript-events-plan.md
