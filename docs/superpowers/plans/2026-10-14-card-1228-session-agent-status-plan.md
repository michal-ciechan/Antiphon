# CARD-1228 `SessionAgentStatus` on the phone-home path

Date: 2026-10-14. Stage: Plan; next stage: decide. Plan task `f624c4e5`. Follow-up of CARD-1219
D-7 / OD-4. Inspected checkout: `88540babcef8413c662a34a07f2e19f7055ca5d9` (master tip at
dispatch; the branch contains `origin/master`). Read-only analysis: no production or test code,
no builds, no test runs; counts are read from source.

Scope: the card asks whether the missing `RunnerContractMapper.ParseEvent` branch and
`PhoneHomeRecoveryPump` handler for `SessionAgentStatus` lose state on a remote (server2) lane,
and for the smallest fix if they do.

Recommendation in one line: **close the card as not needed (option c).** The event has one
producer, the herdr pane pump, and the phone-home lane refuses herdr sessions in code before any
configuration is consulted, so no `SessionAgentStatus` frame is produced on that lane. The state
the event carries is read by polling on both lanes. Details and the smallest future change follow.

Session checklist: this plan adds or changes no session that waits for input, so the CARD-1083
release checklist does not apply.

## Evidence and ground truth

| Card assumption / question | What the code does | Consequence |
|---|---|---|
| (1) Something on the runner produces `SessionAgentStatus`. | `RunnerSession.ApplyHerdrAgentStatus` (`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:2660-2682`) publishes `SessionRunnerEventNames.SessionAgentStatus` with `RunnerAgentStatusEvent(_sessionId, status, previous, observedAtUtc)` when the value changed and `publishEvent` is true. Callers: `HerdrEventPumpService.cs:205` (a `pane.agent_status_changed` event for a tracked pane) and `:221` (the baseline sweep's `pane.get`). `RefreshHerdrSurfaceAsync` (`:2648`) updates the cache with `publishEvent: false`. There is no other `_events.Publish` of that name (`grep SessionAgentStatus`: one runner site). | The producer is the herdr lane only. |
| (1) Payload shape. | `RunnerAgentStatusEvent(Guid SessionId, string AgentStatus, string? PreviousAgentStatus, DateTime ObservedAtUtc)` (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:309-314`). The same value is also carried on `RunnerSessionDto.AgentStatus` / `AgentStatusSinceUtc` (`:233-236`), populated from the cache at `SessionRunnerRuntime.cs:3796-3797`, so List and Get carry it without the event. Pinned on the runner by `HerdrEventPumpTests.Dotted_status_event_updates_the_session_and_publishes_SessionAgentStatus`. | The event is a change notification; the state itself is on the session DTO. |
| (2) The local lane maps it. | `SessionRunnerHttpClient.ParseEvent` (`server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:1032-1042`) maps the four fields into `SessionRunnerAgentStatusEvent` (`server/Application/Dtos/SessionRunnerDtos.cs:136-140`), carried on `SessionRunnerEvent.AgentStatus` (`:151`). `SessionRunnerEventPump.DispatchEventAsync` (`:451-455`) calls `AgentSessionRuntime.ObserveAgentStatusAsync`. Pinned by `SessionRunnerStreamReadinessTests.C1205_First_event_follows_readiness_without_loss("status")` (`:97`, `:396-404`). | The local consumer is one method. |
| (2) What the local consumer does. | `AgentSessionRuntime.ObserveAgentStatusAsync` (`server/Application/Services/AgentSessionRuntime.cs:455-481`): (a) publishes `AgentChanged` through `PublishToAllAsync` for the agent whose `PersistentSessionId` is the session (`:459-468`); (b) when `PreviousAgentStatus` is `"blocked"` and the new status is not, calls `FlushQueueAfterManualCompactionAsync` -> `SessionMessageQueueService.FlushIfIdleAsync` (`:470-480`). Nothing is persisted; the comment at `:449-453` names the flush as the only permitted side effect. | Two effects: a UI invalidation and a promptness nudge. Both are hints, not state. |
| (2) Which behaviours depend on the event. | The client invalidates `['agents','list']`, the agent detail, `['attention']` and `['homeTasks']` on `AgentChanged` (`client/src/hooks/useSignalRInvalidation.ts:97-104`); the badge reads `herdrAgentStatus` from the board DTO (`client/src/features/agents/HerdrStatusBadge.tsx:14`). `FlushIfIdleAsync` (`SessionMessageQueueService.cs:1805-1830`) re-reads working state before delivering. | Without the event: the badge refreshes on the next `AgentChanged` from another cause or the next query refetch; a WhenIdle message pending behind a herdr modal waits for the next flush trigger. |
| (2) Where the server reads the status itself. | Board overlay: `AgentService.AttachRunnerLiveStateAsync` (`server/Application/Services/AgentService.cs:348-399`) copies `runner.AgentStatus` / `AgentStatusSinceUtc` from `_runnerClient.ListAsync` (`:360`, `:391-394`). Delivery gate: `SessionMessageQueueService.cs:4100-4103` reads `TryGetLiveMetadata`, which is `_runnerClient.GetAsync` (`AgentSessionRuntime.cs:1806-1815`); the other `"blocked"` reads (`:544`, `:942`, `:2164`, `SessionMessageQueueService.Expectations.cs:80`) are the same live-metadata shape. Corroboration: `HerdrStatusCorroborationService.SweepAsync` (`:54-57`) selects Running herdr sessions and `TryRaiseAsync` reads `_runner.GetAsync` (`:102`, `:110-111`). | The gating and incident decisions poll the DTO; the event feeds none of them. |
| (3) The remote lane drops the frame. | `RunnerContractMapper.ParseEvent` (`server/Infrastructure/Agents/SessionRunner/RunnerContractMapper.cs:75-148`) has branches for Output, Exited, Adopted, Started, Transcript, TranscriptFault and TranscriptBound and returns null at `:147`. `PhoneHomeRecoveryPump.PumpEventsAsync` (`:369-371`) `continue`s on null before `NoteSessionConfirmed` (`:379`) and the owner check (`:380`). The runner forwards each hub event unchanged (`src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:159`, `:359-364`). `Unknown_event_name_still_returns_null` (`tests/Antiphon.Tests/Application/RunnerContractMapperTranscriptEventTests.cs:127`) pins the null for this name. | Confirmed as the card states. |
| (3) The frame can occur on the remote lane. | **No.** `PhoneHomeRuntimeAdapter.Capabilities` (`src/Antiphon.SessionRunner/PhoneHomeRuntimeAdapter.cs:37-40`) advertises `backends = [SessionBackends.PtyHost]` with no herdr arm. `PhoneHomeCommandDispatcher` (`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:826-830`) refuses a launch whose `Backend` is not pty-host, and any `Herdr` options, with 409 `UnsupportedTarget` before the runtime is reached. The HTTP (local) capabilities advertise herdr only when `SessionRunner:Herdr:Enabled` is true (`src/Antiphon.SessionRunner/Program.cs:435-441`). Deployment agrees: `docker-compose.yml:89` and `docker-compose.server2-runner.yml:95` set `SessionRunner__Herdr__Enabled: "false"`; live `GET /api/session-runners` (2026-10-14) lists server2 (linux, capacity 10) with no `herdr-*` feature token and desktop (windows, capacity 2) with `herdr-attach`, `herdr-named-tab-placement`, `herdr-pane-disposal`. | A herdr session, the only producer, does not run on a phone-home runner. The missing branch is reached by no frame. |
| (3) Which lane each runner uses. | `RoutingSessionRunnerClient.StreamEventsAsync` streams the local client only (`server/Infrastructure/Agents/SessionRunner/RoutingSessionRunnerClient.cs:120-121`); `ListAsync` is local only (`:45-46`); `GetAsync` routes per session binding (`:51-52`, `:129-131`). The runner's own `PhoneHome:Enabled` defaults to false (`src/Antiphon.SessionRunner/appsettings.json`, `PhoneHomeSettings.cs:7`). | Desktop (herdr-capable) is the SSE lane; server2 is the phone-home lane. |
| (3) The server derives the same state another way on a remote lane. | Delivery gate and corroboration read `GetAsync`, which routes to `PhoneHomeRunnerClient` for a remote binding (`RoutingSessionRunnerClient.cs:51-52`); `RunnerContractMapper.Map` (`:44-45`) carries `AgentStatus` / `AgentStatusSinceUtc` from the remote DTO. The board overlay reads `ListAsync`, which is local only (`:45-46`), so a remote session's `HerdrAgentStatus`, `TranscriptBinding` and `HerdrOrigin` overlays stay null whatever the event does (`AgentService.cs:386-397`). The blocked-exit flush has other triggers on the remote lane: the pump dispatches Transcript frames (`PhoneHomeRecoveryPump.cs:389-390`) and a turn boundary flushes the queue (`AgentSessionRuntime.cs:380`, `:401-402`); `FlushIfIdleAsync` is also called from `AgentSessionService.cs:1242`, `AgentChannelService.cs:130`, `GrokRulesRefreshService.cs:64` and `CompletionNoteWorkHostedService.cs:149`. | Gate and incident state: equivalent by polling. UI badge: the overlay, not the event, is the remote gap, and it is moot while herdr is refused there. |
| Replay / idempotence, if a branch were added. | The hub hands a new subscriber a fresh channel with no history (`SessionRunnerRuntime.cs:4439-4451`); CARD-1219's ground truth records the same for the recovery path. `ObserveAgentStatusAsync` publishes an invalidation and a self-guarding flush, both safe to repeat; the event carries `PreviousAgentStatus`, so the server holds no per-session history (`SessionRunnerContracts.cs:305-307`). | A mapping would need no replay guard and nothing distinct from `TranscriptIncidentReplayGuard`. |
| Contract / capability. | `SessionRunnerEvent.AgentStatus` and `RunnerAgentStatusEvent` already exist on both sides; `RunnerContractMapper.Json` is `JsonSerializerDefaults.Web` (`:11`) and the hub spelling is PascalCase (`PhoneHomeConnectionService.cs:361`), both accepted (`Fault_frame_round_trips_every_field` covers the two spellings for the sibling branch). | A mapping would be a server-only change with no wire, version or feature token. |
| Platform. | `GET /api/runner-defaults` revision 2: `globalRunnerId` server2, no kind overrides. `GET /api/session-runners`: desktop (windows, 2), server2 (linux, 10), one stale `server2-temp`. | No checkpoint runs under this plan, so no lane is named. |

## Decision

**(c) Not needed, with (a) as the fallback reading.** Reason, in order:

1. The only producer is a herdr pane session (`SessionRunnerRuntime.cs:2676-2681`, callers `HerdrEventPumpService.cs:205`, `:221`).
2. The phone-home lane refuses herdr launches in the dispatcher (`PhoneHomeCommandDispatcher.cs:826-830`) and advertises pty-host only (`PhoneHomeRuntimeAdapter.cs:40`); the deployed server2 runner also has herdr disabled (`docker-compose.server2-runner.yml:95`) and advertises no herdr feature (live `GET /api/session-runners`).
3. So the missing branch is reached by no frame today, and the observable difference between lanes is nil for this event.
4. If a later card admits herdr on a phone-home runner, the gate, delivery and corroboration decisions read the status by `GetAsync` (`AgentSessionRuntime.cs:1806`, `HerdrStatusCorroborationService.cs:102`), which routes to the remote client. The two things the event adds are a UI invalidation and a flush nudge, and the board badge would stay null on that lane anyway because the overlay lists the local runner only (`RoutingSessionRunnerClient.cs:45-46`). That card owns both the mapping and the overlay.

Rejected: (b) mapping now. It would add a branch no production frame reaches, change the
negative row at `RunnerContractMapperTranscriptEventTests.cs:127`, and leave the remote badge
gap untouched. Rejected: closing on (a) alone; the stronger fact is the refusal, and the overlay
row above shows the lanes are not equivalent for the badge.

## Decisions (D-n)

- **D-1 No mapper branch and no pump arm.** `RunnerContractMapper.cs` and
  `PhoneHomeRecoveryPump.cs` are not edited. The negative row in
  `Unknown_event_name_still_returns_null` stays as the pin that the name maps to null on this lane.
- **D-2 No contract, version or capability change.** `PhoneHomeProtocol.Version`,
  `RunnerCapabilityFeatures` and `RunnerCapabilitiesDto` are untouched. Old runners are unaffected.
- **D-3 No doc edit.** `docs/session-runtime-invariants.md:919` records the CARD-1219 mapping; the
  phone-home herdr refusal is already code (`PhoneHomeCommandDispatcher.cs:828`, `:830`). The card's
  close reason carries the pointer to this plan.
- **D-4 The smallest change is recorded here for the card that admits herdr on phone-home, not
  dispatched.** See the next section.

## Smallest change if a later card admits herdr on the phone-home lane

Recorded so that card needs no new investigation. Not a slice of this plan.

| Piece | Change | Test (new unless noted) |
|---|---|---|
| Mapper | One branch after `SessionTranscriptBound` in `RunnerContractMapper.ParseEvent` with the field mapping of `SessionRunnerHttpClient.ParseEvent:1032-1042`; null record returns null. | `RunnerContractMapperTranscriptEventTests`: a `Status_frame_round_trips_every_field` row under both spellings; replace row 1 of `Unknown_event_name_still_returns_null` with another unknown name. |
| Pump | In `PumpEventsAsync` after `OwnerMatchesAsync`: `else if (parsed.AgentStatus is { } status) await runtime.ObserveAgentStatusAsync(status, ct);`. No replay guard (ground-truth replay row). | A `PhoneHomeTranscriptIncidentTests`-shaped class: a `blocked -> done` frame for an owned session publishes one `AgentChanged` for the owning agent and calls the flush; a foreign-owner frame does neither. |
| Admission | `PhoneHomeCommandDispatcher.cs:826-830` and `PhoneHomeRuntimeAdapter.cs:40` would have to admit and advertise herdr; `SessionDeliveryProfile.cs:80-95` reads the advertised backends for ceilings. | Owned by that card. |
| Overlay | `AgentService.AttachRunnerLiveStateAsync` lists the local runner only (`RoutingSessionRunnerClient.cs:45-46`); a remote herdr session's badge stays null without a per-runner List. | Owned by that card. |

## Operator decisions (OD-n, with recommendation)

- **OD-1** Close CARD-1228 as not needed with this plan as the reason (recommended), or dispatch
  the D-4 table as a Code task now. Recommended: close; the branch would be reached by no frame.
- **OD-2** Whether the local-only board overlay for remote sessions (`AgentService.cs:360`,
  `RoutingSessionRunnerClient.cs:45-46`; affects `TranscriptBinding` and `HerdrOrigin` as well
  as the herdr badge) deserves its own card. The board search for `AttachRunnerLiveState` found
  no card. Recommended: file a one-line Backlog card pointing at this plan's overlay row; it is
  independent of this event.

## Verification

No code changes, so no `### Checkpoints` table and no positive controls. The facts above are
pinned by existing tests, which a Review of the close can cite without running:

| Fact | Named test |
|---|---|
| The runner publishes `SessionAgentStatus` on a herdr pane status change. | `HerdrEventPumpTests.Dotted_status_event_updates_the_session_and_publishes_SessionAgentStatus` |
| The local lane maps and dispatches the status frame. | `SessionRunnerStreamReadinessTests.C1205_First_event_follows_readiness_without_loss("status")` |
| The phone-home mapper returns null for the name. | `RunnerContractMapperTranscriptEventTests.Unknown_event_name_still_returns_null` |

## Ordering and collisions

No slice is dispatched. For the record, the six in-flight branches at dispatch
(`feat/card-task-0166fae1` CARD-0100 test design, `2d7196f7` CARD-0506 S4 review, `567fbd60`
CARD-1032 S8 repair, `5778bc24` CARD-0097 S1 repair, `65ec918e` CARD-0506, `bf9ffadd` CARD-1213
S2) show no diff against `origin/master` in `RunnerContractMapper.cs`, `PhoneHomeRecoveryPump.cs`,
`AgentSessionRuntime.cs`, `SessionRunnerDtos.cs`, `SessionRunnerContracts.cs`,
`SessionRunnerEventPump.cs`, the three CARD-1219 test files or `docs/session-runtime-invariants.md`
(`git diff --stat origin/master...<branch>` on 2026-10-14). The D-4 table, if ever dispatched,
collides with none of them.

--- next stage ---
next: decide
handoff: CARD-1228 needs no code: the only SessionAgentStatus producer is a herdr pane session and the phone-home dispatcher refuses herdr launches (PhoneHomeCommandDispatcher.cs:826-830; adapter advertises pty-host only), so no frame reaches the missing mapper branch; close on OD-1 and consider filing OD-2 (local-only board overlay for remote sessions).
artifact: docs/superpowers/plans/2026-10-14-card-1228-session-agent-status-plan.md
