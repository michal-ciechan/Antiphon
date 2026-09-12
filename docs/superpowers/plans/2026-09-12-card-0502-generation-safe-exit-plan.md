# CARD-0502: generation-safe exits and bounded re-adoption accounting

Status: planned; verification design appended (TestDesign task ebb71692); Code is next.

Source baseline: `90fd1c7c0b3646bb86f787988167e89134a3b8b6`.
Evidence: full Investigate task `fb103259` report, read through
`pwsh -NoProfile -File scripts/delegate.ps1 -Status fb103259`; card
`0e5b60f2-aa69-44e0-a87b-8fd438624375` on board
`8988ca03-7414-47ad-b0b6-51556c701703`.

## Outcome and scope

An exit from launch A must never close launch B of the same standing conversation.
Carry the already accepted database launch generation through the runner and check it
under the database row lock before applying exit evidence. Apply the same check to
reconciliation's missed-exit path. Count committed Failed-to-Running re-adoptions,
and emit one cap escalation instead of a growing sequence of alleged flaps.

The delivery/launch overlap needs a narrow companion guard: cleanup from launch A
must not kill the runner object for B. Preserve existing delivery confirmation,
standing launch authorization, conversation identity, cap setting and supervision
policy. No queue redesign, transcript replay changes, automatic Fresh, incident-to-card
automation, custody redesign, or production restart belongs to this change.

## Ground truth

| Card assumption or question | Current evidence and design consequence |
| --- | --- |
| Hundreds of process restarts happened before the cap worked. | The full investigation found three successful re-adoptions at 17:10:47, 17:16:17 and 17:18:32 UTC. The cap refused attempt four at 17:26:17. `SessionReAdoptionState.TryRegisterReAdoption` increments even on refusal; later counts reached 856. Count transitions, not probes. |
| The stale runner's missing `34412547` caused this Claude/PtyHost incident. | Its runner changes concern Grok/Herdr. Neither current nor loaded exit handling identifies incarnations. A runner update alone is not an established fix. |
| A reused session ID identifies the process that exited. | `RunnerSessionExitedEvent` has ID, code, reason and sequence only. `SessionRunnerEventPump` passes those to `AgentSessionRuntime`, whose locked query selects the current row by ID. Standing resume updates that row's `StartedAt`. |
| The exact incident's delayed-event ordering was reproduced. | It was not. The Failed writer is proven by the logged `Process exited (KilledByRequest, code 1).` reason; delayed consumption across reuse is a strong diagnosis supported by the code. Deterministic reproduction is required. The event hub does not replay historical SSE events on subscription. |
| There is no usable generation concept. | CARD-0466 already queues immutable `acceptedGeneration = AgentSession.StartedAt`, normalized to PostgreSQL microseconds, and rechecks it in `AgentSessionService`. Reuse that identity. |
| Runner `StartedAt` can be compared with database `StartedAt`. | Runner time comes from child launch/adoption; database time is reservation acceptance. They are different facts. Add a separate echoed field. |
| Protecting only the SSE closer is enough. | `ReconcileSessionsAsync` also applies an Exited snapshot by session ID. `RunnerTerminalSession.KillAsync` also targets only the reused ID. Guard these exact alternate paths. |
| Current cap tests prove bounded reporting. | `Re_adoption_stops_and_escalates_once_the_flap_cap_is_reached` stops at sweep four and expects `CountFor == 4`; it never observes the repeated refusal path. Extend it beyond the cap and check committed state and incident counts. |

## Decisions

### D-1. Reuse the accepted launch timestamp as the generation identifier

The identity is `(SessionId, AcceptedStartedAt)`, where `AcceptedStartedAt` is the
UTC, microsecond-normalized value committed to `AgentSession.StartedAt` at reservation.
It is an opaque equality token, not a clock-skew or elapsed-time test. No new session
table column or EF migration is necessary.

Add nullable, append-only `AcceptedStartedAt` fields to `AgentLaunchSpec`,
`RunnerLaunchRequest`, runner session/exit DTOs and their application equivalents.
`BuildRuntimeLaunchSpecAsync` supplies the reservation's captured value; queued
interactive work must pass its accepted value explicitly, never re-read a replacement
row to fill the field. All actual launch/resume paths use this common composition.
Herdr attach supplies its accepted row generation separately from the child's real
start time; preserve idempotent attachment to an already tracked child.

At a same-row resume, choose a normalized value strictly greater than the prior
generation, even with a frozen/backward clock or two reservations in one microsecond.
Use `max(normalized now, prior generation + one microsecond)` under the existing
reservation lock. Do not change the meaning of runner `StartedAt`, PID start times,
Grok rules generation, or CARD-0478 execution/receipt identity. When a verification
binding is present, its accepted generation and the general launch field must agree;
reject disagreement before native launch rather than create a second authority.

Rejected: a fresh GUID and migration for every session (duplicates an existing accepted
identity); exit timestamp alone (clock comparisons do not identify a process); session
ID, PID, last sequence or delivery timestamp (all have different reuse semantics).

### D-2. Bind identity before a process can emit an exit, and retain it on adoption

Initialize the runner object's immutable accepted generation from the request before
launching, subscribing callbacks or exposing it in the registry. Every exit producer
reads that object's value, never a lookup of the current registry entry by session ID.
Centralize exit-envelope construction to cover natural/kill exits, vanished-process
reconciliation, Herdr callbacks and synthesized terminal records during adoption.

Carry the field through `LaunchMessage` into the PtyHost's launch-pending and launched
manifest, and preserve it in terminal manifest updates. Echo it in the launch response
so a generation-bearing launch cannot report success after a host ignored the field.
For Herdr, retain it in launch-pending, launched and attached sidecars. Restore it before
publishing an adopted or synthesized-exit representation, including Pending recovery.
Keep runner session DTOs populated from the same immutable field.

Old metadata remains readable with null identity. Never stamp the database's current
generation onto an old host, terminal manifest, sidecar or queued exit. Generation
metadata confers no descendant-exit/custody authority and changes none of that protocol.

### D-3. Reject stale evidence before every exit-related side effect

Thread the complete typed exit through `SessionRunnerHttpClient` and the pump. In
`CloseSessionOnExitAsync`, compare non-null accepted generation with the locked,
freshly read database row before changing anything, including terminal-source backfill,
Herdr evidence, agent status, timestamps and incidents.

Return a disposition to `ObserveExitAsync` (applied/current, stale, unknown, missing,
or persistence failure), rather than treating a caught persistence error as acceptance.
Stale/unknown/missing/failure must not publish a current-session `SessionExited` or
`AgentChanged`. Include the accepted generation in valid exit publication for diagnosis.
Preserve existing same-generation clean-exit, CpuSpinKilled, operator/system termination
precedence, card-owned-agent exclusion, and Herdr reason/incident mapping. Duplicate
same-generation exits retain existing idempotent state/evidence behavior.

The comparison and database changes must be one transaction: a comparison before a
later unlocked save is insufficient. Respect the standing reservation lock order when
touching an agent as well as a session; do not add a session-then-agent deadlock against
the existing agent-then-session launch/failure path. Revalidate the agent's pointer
before changing it. A session with no current owner may still close its own matching row.

Reconciliation's Exited arm requires the same exact match. Snapshot evidence must not
be applied after the database generation changed during a probe or wait for a lock.
The Failed-to-Running arm likewise revalidates the generation and Failed status before
counting or restoring anything. A present runner record for another generation is a
mismatch, not evidence that the current process died or that the row should be adopted.
Do not let a stale list's absence close a row accepted after that list was obtained:
retain/recheck the observation's database generation and refresh ambiguous candidates.
Keep unknown-session, starting-grace and launch-ownership semantics separate from exit
reason evidence. Do not use mismatched Running evidence to resume an interrupted launch.

Rejected: filtering only nonzero/KilledByRequest exits, adding sleeps, enlarging the
cap, or a pump-only guard. Each leaves an alternate stale writer or another exit reason.

### D-4. Constrain cleanup at the delivery/launch overlap without weakening authorization

The reported `generation no longer authorized` can be correct: a real delivery failure
may stop the same launch while its boot tail is still running. Do not remove that check,
ignore current-generation failures, or treat all KilledByRequest exits as clean stops.

Capture the accepted generation in `RunnerTerminalSession` before awaiting Start.
Introduce an explicit generation-conditional runner kill operation for adapter launch
cleanup and delivery-failure recovery. Use a distinguishable route, for example
`POST /sessions/{id}/kill-generation` with `expectedAcceptedStartedAt`; do not rely on
an optional query/body field an old runner could ignore on the existing kill route.
Under the runner's existing per-session launch gate, select the object, compare its
identity, and kill only that object. Mismatch/missing identity returns an explicit
non-kill result. Never retry that refusal as an unconditional kill.

Carry the delivery attempt's captured generation through failure handling, including
Mode:Now, and condition its session close writes before and after runner I/O. Existing
delivery locks already serialize ordinary delivery against standing reservation; retain
them, avoid reacquiring the same non-reentrant lock, and cover any continuation that
outlives it with the generation check. Launch failure's existing generation-aware evidence
write remains in force. An old adapter's exit waiter must finish as superseded when GET
names another generation instead of waiting for and borrowing B's eventual exit.

Capture at the transport attempt, not when its failure is finally handled. Recovery of
an earlier persisted attempt without a retained generation cannot authorize a kill;
leave its transcript confirmation/queue disposition intact and decline that destructive
recovery. Do not add a queue-generation persistence redesign to this card merely to
make an old ambiguous attempt eligible for automatic kill.

Keep explicitly requested operator Stop/Kill semantics on the existing path. This is
not an API-wide rewrite of input, resize, task settlement or supervision. Successful
late UserPrompt confirmation still prevents recovery kills and duplicate submission.

### D-5. Make compatibility conservative and explicit

Advertise a runner capability `sessionGenerationV1` only when launch binding, persisted
adoption identity, exit emission and conditional kill all work. Check it before POSTing
a generation-bearing new launch or attach; a missing capability produces the existing
typed runner-capability refusal. A new server must not silently launch an unprotected
incarnation on an old runner. Old servers may omit additive fields; new runners retain
null for those sessions and continue to understand the old protocol.

For existing legacy sessions/events with no accepted generation, decline automatic
exit-derived mutation and re-adoption; a missing token is not a match. Emit bounded
compatibility visibility through the existing alert/log surfaces, keyed to the affected
session for this server uptime. Do not convert unknown evidence into Failed or a
continuity-loss decision. Existing reads and explicit operator controls remain usable.

This deliberately trades automatic reconciliation of unbound legacy exits for avoiding
false failure of a newer conversation. Such a session can temporarily retain stale DB
status until explicitly recovered and launched with a bound generation. A runner restart
cannot retroactively bind an old host's metadata. State this limitation in the runtime
owner and implementation handoff. Do not invent a PID/time heuristic or automatically
restart/stop the affected standing agents as a compatibility workaround.

The caller owns rollout timing. This plan neither orders nor performs the unrelated
stale-runner restart; tests use isolated runners. Supporting metadata and runner/server
changes form one feature and cannot be claimed active from a healthy API alone.

### D-6. Count committed transitions, and latch cap reporting

Retain the in-memory, per-session, per-server-uptime cap and the configured default
of three. Do not reset the count on same-ID resume or ordinary healthy observations.
Restart resets it as today. Normalize negative caps to zero.

Replace the increment-before-admission API with a per-session asynchronous lease/state
that serializes the cap decision and transition completion across scoped reconcilers.
The state holds `SuccessfulReAdoptions` and bounded escalation progress, not a cumulative
attempt counter. A disallowed observation never increments. Reserve one permitted
transition, re-read/lock the DB row, verify still Failed and still the probed generation,
commit the restoration, then increment once before releasing the lease. A failed or
canceled transaction releases without consuming a slot; a publication/alert failure
after commit must not refund a completed transition. Keep the lease held across the
decision/commit boundary; do not hold a C# monitor across awaits.

At the first *subsequent eligible mismatch* after all slots were used, emit the cap
escalation. The third successful restoration is not itself an escalation. Cap zero
allows zero transitions and escalates on the first eligible mismatch. Once the cap
escalation is complete, later sweeps do not issue expensive adoption probes, append
incidents, repeat the cap error log, or raise the same alert every fifteen seconds.
They still participate in the ordinary fleet scan and cannot kill or restore the row.

Use precise text, for example: `Re-adopted this session 3 times during this server
uptime (cap 3). A further Failed/runner-Running mismatch was observed; automatic
re-adoption is stopped.` The success message reports `re-adoption N of cap` only
after the corresponding commit. Do not call a refused observation another flap.

### D-7. Keep escalation retryable without duplicating incidents

Do not reuse `AlertAndIncidentAsync` unchanged across a partially failed restoration:
it saves the service's tracked context and can commit the session mutation as a side
effect of inserting an incident, before the nominal restoration save. Make the session/
agent transition and its success incident an explicit transaction; count after commit;
publish and raise the alert afterward.

For the cap, keep one pending escalation record in the singleton (fixed incident ID,
original time/count and incident/alert completion flags). Insert that incident once in
an isolated context/transaction; retain a successful insert if the alert call fails.
Retry only unfinished work with a fixed small backoff, using the same existing alert
dedup key. Mark reported after successful persistence and alert submission, not before.
After an ambiguous save, check the same incident ID instead of appending another.
An unclaimed session needs the alert but no agent incident. Reset on server restart is
intentional; durable exactly-once alert delivery is outside this card. Test these
failure boundaries so bounded reporting does not mean silently losing the escalation.

## Implementation slices and coverage owners

Implement together, then ordinary Review. These are not independently deployable stages.

| Slice | Production files | Tests to extend or add |
| --- | --- | --- |
| S1: Accepted generation from reservation to runner wire | `server/Application/Services/AgentControlService.cs`, `AgentSessionService.cs`, `AgentSessionLaunchQueue.cs`; `server/Application/Dtos/AgentLaunchSpec.cs`, `SessionRunnerDtos.cs`; `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`; `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs` | New focused `SessionRunnerGenerationWireTests`; generation cases in `AgentControlServiceIntegrationTests`, `AgentSessionLaunchQueueOwnershipTests`; preserve `AgentSessionLaunchFailureTests` and accepted-work ownership coverage. |
| S2: Immutable producer and restart persistence | `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`, `HerdrPaneChild.cs`, `HerdrPaneSidecar.cs`, capability composition in `Program.cs`; `src/Antiphon.PtyHost.Protocol/PtyHostMessages.cs`, `PtyHostManifest.cs`; `src/Antiphon.PtyHost/HostSession.cs` | New `RunnerSessionGenerationTests`; existing `PtyHostAdoptionTests`, `SessionLivenessTests`, `HerdrAdoptionSweepTests`, `HerdrPaneSidecarTests`, `HostSessionPipeTests`. Cover every exit producer, including fast exit and terminal adoption. |
| S3: Consumers and exact cleanup | `server/Application/Services/AgentSessionRuntime.cs`, `SessionReconciliationService.cs`, `AgentSessionService.cs`, `SessionMessageQueueService.cs`; `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs`, `RunnerTerminalSession.cs`, `SessionRunnerHttpClient.cs`; `server/Application/Interfaces/ISessionRunnerClient.cs`; runner runtime and conditional route | New focused `SessionGenerationExitTests`, `SessionGenerationDeliveryOverlapTests`; extend `SessionRunnerEventPumpTests`, `AgentSessionRuntimeTests`, `SessionReconciliationServiceTests`, `SessionMessageQueueDeliveryVerificationTests`. Update affected fake clients mechanically. |
| S4: Count commits and bound escalation | `server/Application/Services/SessionReAdoptionState.cs`, `SessionReconciliationService.cs`; settings comments if wording changes | New unit `SessionReAdoptionStateTests`; extend `SessionReconciliationServiceTests` for counts, repeated refusals, concurrent scans and partial failures. |
| S5: Document operating contract | `docs/session-runtime-invariants.md`; relevant generation wire/legacy note in `docs/herdr-sessions.md` if attach metadata changes | Update contract documentation with the implemented identity and compatibility policy. Do not edit generated `docs/cards/` files or unrelated stale-build guidance. |

Paths in a shared cell after its first prefix refer to that same directory. TestDesign
must resolve new test locations/project ownership and exact methods before implementation;
do not assume all native tests live in `Antiphon.Tests`.

## Deterministic reproduction and TestDesign requirements

TestDesign adds the full `## Verification design`, including guard-to-test mapping,
exact filters, asynchronous producer/consumer evidence inventory, ordinary V/R costs,
pending mutation controls and both numeric cost floors. The following behaviors and
ordering requirements are mandatory, not a substitute for that stage.

1. **Queued A exit after resumed B:** use one session ID and two accepted generations.
   Obtain a real runner-produced serialized exit for A with `KilledByRequest`, code 1,
   and hold its delivery with a controllable channel/barrier. Commit a real same-row
   resume reservation for B, complete its launch, then release A through the real HTTP
   SSE parser, hosted pump and runtime. Verify from a fresh DB context that B and its
   agent remain Running; generation, termination source, exit fields, timestamps and
   typed failure evidence are unchanged. No exit/agent-failure event, stale incident,
   re-adoption or count increase is permitted. A following matching B exit must close
   it correctly. Also release A while B is Starting. No timing sleeps establish order.
2. **Locked write race:** hold the exit consumer immediately before its locked row read,
   commit B, then release it. Reverse the order to prove a matching A close may commit
   before B resumes and does not prevent B. This catches a comparison done outside the
   write transaction. Include owner-pointer change and already-terminal evidence backfill.
3. **Polling fallback:** queue an Exited-A list/GET result, advance the DB to B, then
   release it. Neither close nor interrupted-launch recovery/re-adoption may borrow
   that snapshot. Current B evidence retains existing reconciliation behavior.
4. **Delivery/boot overlap:** force a same-generation delivery failure while launch's
   post-ready tail is paused. Its legitimate authorization refusal still settles A.
   Separately resume B before delayed A exit and cleanup execute: A's conditional kill
   must reach the runner boundary and make zero calls to B's child; B's DB row and boot
   remain usable. Include late UserPrompt confirmation: no recovery kill or resubmit.
5. **Producer and adoption integrity:** every terminal producer carries A, even when the
   registry now contains B; fast child exit before Start returns; live and already-exited
   PtyHost adoption; Herdr live, terminal, Pending and attach paths. Metadata round-trips
   preserve the exact accepted value while actual child `StartedAt` differs. A runner
   restart with fresh service objects must not require an in-memory server mapping.
6. **Compatibility:** deserialize old payloads/metadata; null is not equal to any current
   generation. Old runner capability causes zero generation-bearing launch/attach POSTs;
   conditional-kill 404/refusal never falls back to the unguarded route. No implicit
   Failed transition, auto-restart or repeated compatibility incident. Old-server/new-
   runner payloads remain readable. Test a normalized same-microsecond resume collision.
7. **Accurate cap:** commit three distinct Failed-to-Running restorations, deliberately
   re-fail between them, then perform at least 100 more scoped sweeps using one singleton.
   Count stays three; fourth and later mismatches make zero restorations/kills. Exactly
   one Critical cap incident and completed alert escalation; after latching, no adoption
   probes. Separate cap-zero and negative-cap tests, multiple sessions and restart reset.
8. **Failure/concurrency accounting:** race two reconcilers over one Failed row using
   barriers; only the actual committed transition counts. Fail probe, cancel/fail save,
   supersede generation before commit, and throw after commit during event/alert publish.
   Assert count zero before commit and one after it, with no refund or double count.
   Fail cap incident persistence and alert submission separately; retry converges to one
   incident, a constant count and a delivered escalation without duplicate durable rows.

Use isolated PostgreSQL contexts and fake/local transports with controlled barriers.
Include a bounded local native-host launch/adoption test to prove metadata actually
survives host/runner reconstruction; a fabricated DTO alone cannot prove S2. No real
provider quota or production runner on 17204 is needed. Tests that spawn processes use
their assembly's existing `ParallelLimiter<ProcessSpawnLimit>` and await their children.
Do not freeze a clock in queue tests with time-based waits; use an advancing test clock.

The original deterministic reproduction should fail on the unguarded implementation
by the intended assertion, not by fixture or compilation failure. TestDesign assigns
deliberate control execution to post-land Mutation, following the current stage order.
Controls should remove generation propagation/checks, select current registry identity
for an old event, bypass conditional kill, increment on cap refusal, or mark reporting
complete before a failed save. Keep controls method-scoped and restore them afterward.

## Validation and handoff

This stage changes only this plan; implementation and executable verification belong
to the subsequent Code stage. No live process operations were performed for this plan.
Code uses `dotnet run --project tests/<ProjectName>` for TUnit, producer-owned isolated
outputs and fresh TRX files proving nonzero execution. Run the documented Unit lane and
the named affected integration classes; run Antiphon.Tests, SessionRunner/PtyHost native
groups and Antiphon.Agents.Pty.Tests sequentially when selected. Do not substitute a
full namespace run or `dotnet test`. No frontend behavior changes are planned.

No operator design decision blocks TestDesign. D-1 through D-7 are the selected defaults;
the material compatibility tradeoff is stated in D-5. The caller lands this plan before
dispatching TestDesign so the next worktree contains it. After ordinary implementation
Review, the caller retains ownership of publication, activation and any runner safe
window; a passing health check or unrelated runner refresh is not bug-fix evidence.

## Verification design

TestDesign for the plan above (task `ebb71692`, 2026-09-12, master `c8ddfe14`). D-1 to D-7
and S1 to S5 are unchanged; this section is the contract Code implements and post-land Mutation
executes. Test project ownership is resolved here: server-side consumers, wire client, queue,
reconciliation and cap cases live in `Antiphon.Tests`; runner producers, adoption and the real
runner exe live in `Antiphon.SessionRunner.Tests`; host manifests live in `Antiphon.PtyHost.Tests`.
`Antiphon.SessionRunner` and `Antiphon.Server` already expose internals to `Antiphon.Tests`, and
`Antiphon.SessionRunner.RunnerSession` is `internal`, so no visibility change is needed.

### Inspection

Bodies read in full: `SessionReAdoptionState`, `SessionRunnerEventPump`, `RunnerTerminalSession`,
`ISessionRunnerClient`, `ScriptedSessionRunnerClient`, `SessionRunnerEventPumpTests`,
`AgentSessionLaunchQueueOwnershipTests`, `SpecialistStartIntentTests`, `PtyHostManifest`,
`SessionReconciliationServiceTests` (fixture, `FakeRunnerClient`, seed/cleanup helpers, both
re-adoption tests, the cap test), `AgentSessionRuntimeTests` (exit tests, `SeedRunningSessionAsync`,
`BuildProvider`), `AgentStartRecoveryTests.Observed_exit_closes_session_and_resets_agent`,
`AgentSessionInterruptedLaunchResumeTests` (`ResumeFixture`, `RecordingKillRunner`),
`AgentSessionLaunchFailureTests.LaunchFixture`, `DirectSessionRunnerClient`, `EphemeralHttpListener`,
`SessionRunnerCapabilityGateTests` (`StubFactory`, `StubHandler`), `SessionRunnerHttpClientHerdrWireTests`
(setup), `PtyHostAdoptionTests` (first three tests), `SessionLivenessTests` (sweep test),
`HerdrAdoptionSweepTests` (`BuildRuntime`, `StartHerdrSessionAsync`), `HerdrAttachTests.Runner_restart_readopts_an_attached_sidecar_with_origin_intact`,
`HerdrPaneSidecarTests.save_load_round_trips_atomically_and_load_all_sweeps`,
`HostSessionPipeTests.Launch_streams_output_and_exit_with_manifest`, `RunnerCustodyCrashTests.HttpRunner`,
`TestDbFixture` (template clone). Production regions read: `AgentSessionRuntime.ObserveExitAsync`/`CloseSessionOnExitAsync`/`KillAsync`,
`SessionReconciliationService` (`ReconcileSessionsAsync`, `ResumeInterruptedLaunchesAsync`,
`ReconcileRunnerAliveSessionsAsync`, `TryReAdoptAsync`, `RetryFailedKillAsync`, `AlertAndIncidentAsync`),
`AgentSessionService` (`StartAsync` catch, `LaunchInteractiveAsync`, `LaunchInteractiveProcessAsync`,
`RequireCurrentCheckLaunchAsync`, `KillAndDisposeAsync`, `KillOnAsync`, `ResumeAsync`),
`AgentControlService` standing resume reservation, `AgentSessionLaunchQueue` enqueue/worker,
`SessionMessageQueueService.HandleDeliveryFailureAsync` and its five call sites (`EnqueueDeliveringNowAsync`,
`SendNowAsync`, `DeliverNextLockedAsync`, `EnterOnlyConfirmLockedAsync`, the Mode:Now path),
`SessionRunnerHttpClient` (ctor, `StartAsync` gates, `KillAsync`, `StreamEventsAsync`, `ParseEvent`),
`SessionRunnerRuntime` (`StartAsync` gate, `StartCoreAsync` registry, `RunnerSession` fields and
`StartAsync`, `CreateAdoptedExited`, `CreateAdoptedHerdrExited`, `CreatePendingHerdr`,
`CompletePendingAsExited`, `KillAsync`, `MarkVanishedIfDead`, `HandleExited`), runner `Program.cs`
(`/capabilities`, `/sessions/{id}/kill`, `/events`), `HostSession` manifest writes, `LaunchMessage`,
`HerdrPaneSidecar`, `SessionRunnerContracts` (launch request, DTOs, events, capabilities).

Fixture facts that shape the cases:

- `BridgeQueueHarness` builds the real server graph (`AgentSessionRuntime`, `SessionMessageQueueService`,
  `AgentControlService`, `AgentSessionService`, `AgentSessionLaunchQueue`, `SessionReconciliationService`)
  on the shared test database, or on an isolated clone via `TestDbFixture.CreateIsolatedSchemaAsync()`
  plus `HarnessOptions.ConnectionString` (`SpecialistStartIntentTests` is the model). With
  `FakeAgentProtocolAdapter.RegisterOnStart = h.Runtime` the adapter is a runtime test adapter, so
  `_runtime.KillAsync`/`GetSessionAsync` route to it rather than to `ISessionRunnerClient`.
- `SessionRunnerEventPumpTests.S0_blocked_hosted_pump_probe` is the model for a hosted pump
  (`new SessionRunnerEventPump(scopeFactory, Options.Create(new SessionRunnerSettings { Enabled = true }), logger)`)
  with `ScriptedSessionRunnerClient` and a `GatedEventBus` that holds one publish.
- `SessionRunnerHttpClient.StreamEventsAsync` is the real SSE parser and reads from
  `_httpClientFactory.CreateClient(EventStreamClientName)`; the wire tests build the client as
  `new SessionRunnerHttpClient(new HttpClient(handler), new StubFactory(), Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }))`.
  No test today streams SSE bytes through it.
- `CloseSessionOnExitAsync` has no seam before its `FOR UPDATE` read. The lock-order race is forced
  from PostgreSQL instead: a test transaction holds `SELECT ... FOR UPDATE` on the row and the blocked
  consumer is observed through `pg_stat_activity` (`wait_event_type = 'Lock'` on the consumer's
  backend), never through a sleep.
- `SessionReconciliationServiceTests.BuildService(...)` accepts `reAdoptions`, `alerts`, `eventBus`,
  `ownership`, `time`; `FakeRunnerClient` exposes `Sessions`, `ListError`, `BufferError`, `Probed`,
  `Killed`, `GetOverride`; `SeedWorkingAgentWithSessionAsync` seeds closed rows with `StartedAt = now - 1h`.
- `Re_adoption_stops_and_escalates_once_the_flap_cap_is_reached` runs four sweeps and asserts
  `CountFor == 4`; that number is the defect (increment on refusal). It is superseded (R-9).
- `PtyHostAdoptionTests`, `SessionLivenessTests` and `HerdrAdoptionSweepTests` drive a real
  `SessionRunnerRuntime` with cmd.exe children or `FakeHerdrServer`; a restart is "dispose runtime A,
  build runtime B, `AdoptOrphanedHostsAsync`". `RunnerCustodyCrashTests.HttpRunner` boots the real
  `Antiphon.SessionRunner.exe` on a random loopback port and asserts it is not 17204.
- `HostSessionPipeTests` drives an in-proc `HostSession` over its pipe (`HostHarness`,
  `PipeTestClient`) and asserts manifest fields after exit.
- Missing setup recorded (Code adds these; none exist today): (1) `SseStreamHandler` in
  `tests/Antiphon.Tests/TestHelpers`: an `HttpMessageHandler` whose `GET /events` body is a
  `System.IO.Pipelines.Pipe`-backed stream the test writes SSE frames into, with a dictionary of JSON
  answers for every other route (`/capabilities`, `/sessions`, `/sessions/{id}`,
  `/sessions/{id}/transcript`, `/sessions/{id}/kill-generation`) and a request log; (2) `FakeRunnerClient`
  gains `OnList` (async callback before the list is returned), `OnProbe` (async callback inside
  `GetBufferAsync`, usable as a `Barrier`), `KillGenerationCalls`; (3) `FakeAgentProtocolAdapter`
  and `FakeSessionRunnerClient` gain `KillGenerationCalls` (recorded generations) beside the existing
  `Killed`/`KillCount`; (4) `BuildService` gains `maxReAdoptions` and `logger`; (5) `ThrowOnceSaveInterceptor`
  (`ISaveChangesInterceptor`, throws once on `SavingChangesAsync` or once on `SavedChangesAsync` for the
  ambiguous case); (6) `MockEventBus` and `RecordingAlertService` gain a throw-once switch; (7)
  `RunnerCustodyCrashTests.HttpRunner` is extracted to a shared `LocalHttpRunner` helper in
  `Antiphon.SessionRunner.Tests`; (8) `AgentControlServiceIntegrationTests.BuildHarness` accepts a
  `TimeProvider`; (9) `AgentSessionRuntimeTests.SeedRunningSessionAsync` gains an `acceptedGeneration`
  argument defaulting to the seeded `StartedAt`.

Boundaries covered:

- Event generation against row generation {equal, older (stale), newer than the row (unknown), event
  null with a non-null row, event non-null with a legacy DTO, both null} -> V-1, V-3, V-6, V-21;
  "newer" is treated as unknown (V-6).
- Row status at consumption {Starting, Running, Stopping, Stopped with `Unknown` source (backfill),
  Stopped with `OperatorRequest`} -> V-1, V-2, V-4, V-6, R-3.
- Exit reason {code 0, `KilledByRequest` code 1, `CpuSpinKilled`, `HerdrPaneClosed`, `HerdrPaneLeftOpen`
  (incident), `HerdrLaunchDetectTimeout`} -> V-3, V-6, R-3 (every reason is a no-op on stale; every
  existing mapping holds on match).
- Owner pointer {still names the row, moved to another session, none} -> V-4, V-6, V-19.
- Transport {SSE consumed immediately, SSE delayed behind a gated earlier event, stream severed then
  snapshot, GET refresh of an ambiguous candidate} -> V-1, V-8, V-9, V-10.
- Cap {3, 0, -1} x sweeps {within cap, cap+1, cap+100} x sessions {1, 2} x singleton {same, new} ->
  V-13 to V-16.
- Transition failure {probe, save throws, cancellation, generation superseded before commit, publish
  throws after commit, incident persist throws, alert throws, ambiguous save} x reconcilers {1, 2} ->
  V-17 to V-20.
- Wire {`Features` has token, `Features` null, `Features` without token} x route {launch, attach,
  kill-generation 200 killed, 200 mismatch, 404} -> V-22 to V-25.
- Metadata {pty manifest launch-pending, launched, exited; sidecar launched, attached, pending; old
  file without the field} -> V-26 to V-31.
- Clock at resume {advancing, frozen, backward, same microsecond} -> V-32.
- Excluded boundaries: real Herdr, real Claude/Grok/Codex, the production runner on 17204, browser
  E2E, Grok rules generation, CARD-0478 execution identity (Out of scope).

### Delivery inventory

Durable identity on every path: `(SessionId, AcceptedStartedAt)`, `AcceptedStartedAt` being the
microsecond-normalized UTC value committed to `AgentSession.StartedAt` at reservation.

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt |
|---|---|---|---|---|---|
| P1 exit event, runner to server | `RunnerSession` producers (`HandleExited`, `MarkVanishedIfDead`, `CreateAdoptedExited`, `CreateAdoptedHerdrExited`, `CompletePendingAsExited`, Herdr `Exited` callbacks) on the hub; `GET /events` SSE; `SessionRunnerHttpClient.ParseEvent`; hosted `SessionRunnerEventPump`; `AgentSessionRuntime.ObserveExitAsync` | `AgentSessions` row, owning `Agents` row, SignalR `SessionExited`/`AgentChanged` | the single transaction in `CloseSessionOnExitAsync` with the row locked `FOR UPDATE` | the SSE hub does not replay; recovery is the reconciler's Exited arm on the next sweep under the same exact-match rule | fresh-context read of `Status`, `StartedAt`, `TerminationSource`, `ExitCode`, `EndedAt`, `FailureReason`, `HerdrSupervisionFailureKind`; `MockEventBus.PublishedEvents`; `AgentIncidents` |
| P2 generation-conditional kill, server to runner child | `RunnerTerminalSession` cleanup kill; `AgentSessionService` delivery-failure recovery kill | the runner object whose identity matches, and only its child | none (request/response); the runner selects under the per-session launch gate | explicit non-kill result; never retried unconditionally; operator Stop/Kill and the Stopped-arm retry keep the existing unconditional route | current runner DTO still Running with its child pid alive; `KillGenerationCalls` and `KillCalls` on the recording client/adapter |
| P3 cap escalation | scoped reconciler holding the singleton's one pending escalation record | one `AgentIncidents` row with a fixed ID; one `IAlertService.RaiseAsync` | isolated incident transaction, then the alert | retry only unfinished work on later sweeps with a fixed backoff; an ambiguous save re-checks by the fixed ID | incident count by fixed ID = 1; `RecordingAlertService` Critical raises = 1; escalation flags on the singleton |
| P4 accepted generation, reservation to runner and back | `AgentControlService` reservation, `AgentSessionLaunchQueue`, `SessionRunnerHttpClient` POST | runner object, pty-host manifest, Herdr sidecar, launch response echo | manifest and sidecar files (atomic save) | runner restart adoption restores the value into fresh objects | echoed `AcceptedStartedAt` on the launch response, adopted DTO, every exit payload |

Busy and already-eligible recipients on P1: V-1 (pump idle, A delivered after B) and V-8 (pump busy
behind a gated earlier event with A already in the stream when B commits). Crash and enqueue-failure
recovery at each handoff: V-9 (stream severed with A unread, then a snapshot naming Exited A closes
nothing) and V-7 (persistence failure inside the consumer publishes nothing and a matching snapshot
closes on the next sweep). Session input on P2: the late-confirmation cases V-12 and V-35 require the
matching complete `UserPrompt` transcript row inserted with `BridgeQueueHarness.InsertTranscriptEntryAsync`;
a Sent flag, an accepted request or the fake adapter's submit acknowledgement never satisfies them.
Substitutes and what each cannot prove: `FakeAgentProtocolAdapter` replaces a real TUI and cannot prove
a provider accepted the body (no provider quota is spent; the real-CLI canaries are out of scope);
`FakeHerdrServer` replaces Herdr and cannot prove real pane lifecycle; the in-proc `SessionRunnerRuntime`
with cmd.exe children proves runner objects, manifests and producers but not the daemon build, which
V-25 covers with the real exe on a random port; `SseStreamHandler` replaces Kestrel's `/events` route
and cannot prove keepalive framing, which the real-exe test covers by streaming one event end to end.

### Proves it works now

Every filter is `dotnet run --project tests/<Project> --property:OutputPath=bin-c502/ -- --treenode-filter "..."`
(forward slash). For an `[Arguments]` method filter `/*/*/Class/Method*` and check that the TRX
expanded count equals the argument count. New classes are tagged `[Category("Integration")]` unless
stated `Unit`, per `TestLaneCategoryGuardTests`.

Core reproduction class `tests/Antiphon.Tests/Application/SessionGenerationExitTests.cs`
(`[NotInParallel("Pty")]`, `[ParallelLimiter<ProcessSpawnLimit>]`). Fixture: `TestDbFixture.CreateIsolatedSchemaAsync()`;
`BridgeQueueHarness` with `ISessionRunnerClient` = the real `SessionRunnerHttpClient` over
`SseStreamHandler`, a queue adapter factory of `FakeAgentProtocolAdapter` with `RegisterOnStart`, and a
hosted `SessionRunnerEventPump` started as in `S0_blocked_hosted_pump_probe`. A's serialized exit is
produced by a real in-proc `SessionRunnerRuntime` (cmd.exe long-lived child, `RunnerLaunchRequest.AcceptedStartedAt = A`),
killed with `runtime.KillAsync`, and its hub `SessionExited` JSON captured verbatim; the test asserts the
captured payload has `ExitReason == "KilledByRequest"`, a nonzero `ExitCode` and `AcceptedStartedAt == A`
(the incident's code was 1; the kill's real code is asserted nonzero, not hard-coded). B is a real
same-row resume: seed the row Failed at `StartedAt = A`, call `AgentControlService.StartAsync(agentId, new StartAgentRequest(), ct)`
(`Fresh` false; the handler's `/sessions` answers `[]` so the liveness preflight passes), then
`LaunchQueue.WaitForIdleAsync`. Every assertion reads a fresh `AppDbContext`.

- V-1: queued A exit released after B resumed changes nothing | server integration through the real SSE parser, hosted pump, runtime and PostgreSQL | `SessionGenerationExitTests.C502_V1_queued_A_exit_released_after_B_resumed_changes_nothing` | with B `Running`, write A's captured frame to the stream and wait for the pump's `pump.event` phase log for S; fresh context: `Status == Running`, `StartedAt == B`, `TerminationSource == Unknown`, `ExitCode == null`, `EndedAt == null`, `FailureReason == null`, `RestartFailureKind == null`, `HerdrSupervisionFailureKind` unchanged; agent `Status == Running` and `PersistentSessionId == S`; no `SessionExited` and no `AgentChanged` published after B's launch events; `AgentIncidents` for S unchanged; `SessionReAdoptionState.CountFor(S) == 0`.
- V-2: released while B is Starting | same | `C502_V2_queued_A_exit_released_while_B_is_Starting_changes_nothing` | adapter B held at `ReadyHold`; row `Starting`/`B`; release A; same assertions with `Status == Starting`; release `ReadyHold`; row reaches `Running` with `StartedAt == B`.
- V-3: a matching B exit closes B afterwards | same | `C502_V3_matching_B_exit_closes_B_after_the_stale_A_exit_was_ignored` | continue from V-1; stream a `SessionExited` frame for S with `AcceptedStartedAt == B`, `ExitCode 1`, `KilledByRequest` (the contract record serialized with the runner's web options); `Status == Failed`, `FailureReason` starts with `Process exited (KilledByRequest, code 1)`, `TerminationSource == ProcessExit`, `EndedAt != null`, agent `Failed`; exactly one `SessionExited` whose payload carries `acceptedStartedAt == B`, exactly one `AgentChanged`.
- V-4: locked write race, consumer blocked before B commits | same | `C502_V4_exit_consumer_blocked_on_the_row_lock_sees_B_after_commit` `[Arguments("same-owner")]`, `[Arguments("pointer-moved")]` | a test transaction takes `SELECT ... FOR UPDATE` on S (row Running at A); `runtime.ObserveExitAsync` for A's envelope starts on a task; poll `pg_stat_activity` (bounded 10 s) until that backend reports `wait_event_type = 'Lock'`; the transaction sets `StartedAt = B`, `Status = Starting`, `TerminationSource = Unknown`, `EndedAt = NULL` (and, for `pointer-moved`, repoints the agent to another session) and commits; the consumer's returned disposition is `Stale`; row still `Starting`/`B`; agent untouched; no events.
- V-5: reverse order, matching A close first does not prevent B | same | `C502_V5_matching_A_close_commits_first_and_B_resume_still_succeeds` | row Running at A; `ObserveExitAsync(A)` -> `Failed`, `ProcessExit`, one `SessionExited`; then `StartAsync` resume -> `Starting` then `Running`, `StartedAt == B` and `B > A`, `TerminationSource == Unknown`, `ExitCode == null`, `FailureReason == null`.
- V-6: disposition matrix | `AgentSessionRuntimeTests` (Slow allowlisted) | `An_exit_event_with_a_stale_generation_is_a_no_op_disposition` `[Arguments]` over row {Starting, Running, Stopping} x reason {`ProcessExited` 0, `KilledByRequest` 1, `CpuSpinKilled` -1, `HerdrPaneClosed`, `HerdrPaneLeftOpen`, `HerdrLaunchDetectTimeout`}; `An_exit_event_newer_than_the_row_is_Unknown_and_changes_nothing`; `A_stale_exit_never_backfills_an_already_closed_row` (row Stopped, `TerminationSource Unknown`, `ExitCode null`, event generation A while row at B: `ExitCode` stays null, source stays `Unknown`); `A_matching_exit_still_backfills_an_already_closed_row` (same with matching generation: existing backfill result) | disposition `Stale`/`Unknown`; row, agent, incidents and events unchanged; the matching case keeps `an_exit_event_backfills_ProcessExit_onto_an_already_closed_row_with_no_source` semantics.
- V-7: persistence failure is a disposition | `AgentSessionRuntimeTests` | `A_persistence_failure_during_exit_close_publishes_nothing_and_a_matching_snapshot_recovers` | `ThrowOnceSaveInterceptor` on the runtime's scope; disposition `PersistenceFailed`; no `SessionExited`; row Running; then `SessionReconciliationService.ScanAsync` with a snapshot Exited at the same generation closes it with the existing `Runner reported an exit that was never observed` wording.
- V-8: delayed consumption behind a busy pump (the incident's ordering) | `SessionGenerationExitTests` | `C502_V8_A_exit_already_in_the_stream_behind_a_gated_event_is_consumed_after_B_and_ignored` | write a transcript frame for another session whose `SessionTranscript` publish is held by `GatedEventBus`, then A's frame; commit B and finish its launch; release the gate; V-1 assertions.
- V-9: severed stream, snapshot names A | `SessionGenerationExitTests` | `C502_V9_severed_stream_then_a_snapshot_Exited_A_does_not_close_B` | complete the pipe with A unread; `/sessions` now lists S `Exited`, `KilledByRequest`, `AcceptedStartedAt == A`; run `SessionReconciliationService.ScanAsync` from the harness provider; row Running at B; then the list reports `AcceptedStartedAt == B` and the row closes.
- V-10: polling fallback arms | `SessionReconciliationServiceTests` | `Runner_Exited_snapshot_for_a_superseded_generation_does_not_close_the_row` (Running at B, list Exited at A: unchanged, no alert, no `SessionExited`); `Runner_Running_snapshot_for_a_superseded_generation_does_not_resume_an_interrupted_launch` (Starting at B past grace, unowned, list Running at A: `RecordingLaunchOwnership.Resumes` empty); `A_Failed_row_at_a_newer_generation_is_not_re_adopted_from_older_Running_evidence` (Failed at B, list Running at A: still Failed, `Probed` empty, `CountFor == 0`, no incident); `A_stale_list_absence_does_not_close_a_row_accepted_after_the_list` (`OnList` commits the same-row resume to B and returns a list without S; `GetOverride` answers Running at B; row stays `Starting`/`B`; the refresh GET was made) | decisive assertions as listed.
- V-11: same-generation delivery failure while the launch tail is paused | new `tests/Antiphon.Tests/Application/SessionGenerationDeliveryOverlapTests.cs` (`[NotInParallel("SessionGenerationDeliveryOverlap")]`; `BridgeQueueHarness`, `AlwaysOn = true`, queue adapter factory, adapters registered with `RegisterOnStart`, isolated schema) | `C502_V11_same_generation_delivery_failure_kills_G_and_the_paused_launch_tail_settles_G` | launch G through `LaunchInteractiveAsync` with `notes` so the tail blocks inside `DeliverLaunchNoteAsync` (`OnSubmitted` awaits a `TaskCompletionSource`); a Mode:Now send fails verification (`Send_now_throws_conflict_when_delivery_cannot_be_verified` shape) -> `adapter.KillGenerationCalls == [G]`, `adapter.KillCount == 0`; release the tail: `LaunchInteractiveAsync` throws `ConflictException` code `specialist_start_intent_revoked`; row terminal (`Stopped` or `Failed`) with `TerminationSource == SystemRequest`, `EndedAt != null`, `RestartFailureKind != null`; no second generation kill; agent terminal.
- V-12: B resumed before A's delayed cleanup | same class | `C502_V12_B_resumed_before_A_cleanup_gets_zero_kill_calls_and_boots` | as V-11 to the kill of A; the recording list answers Exited for S; `StartAsync` resume reserves B (`StartedAt == B`), adapter B held at `ReadyHold`; release A's tail: `adapterA.KillGenerationCalls` contains A only (the cleanup call reports mismatch or already-exited), `adapterB.Killed == false`, `adapterB.KillGenerationCalls` empty, `adapterB.Inputs` unchanged; row `Starting`/`B`, `FailureReason == null`, no incident for B; the failed `LaunchInteractiveAsync(A)` writes no evidence; release B -> `Running`, `InteractiveLaunchCompletedAt != null`. Late confirmation arm `[Arguments("late-userprompt")]`: the Mode:Now failure is `NoTranscriptRecord` and the grace window finds the inserted matching `UserPrompt` row -> `KillGenerationCalls` empty, exactly one Enter in `adapter.Inputs`, no incident.
- V-13: accurate cap over one hundred sweeps | `SessionReconciliationServiceTests` | `Re_adoption_counts_committed_transitions_and_latches_after_the_cap` (replaces the four-round test) | rounds 1 to 3 restore then re-fail; rounds 4 to 104 mismatch: `CountFor == 3` on every round from 3 onward, row Failed on every round from 4 onward, `Probed.Count == 4` at the end (three commits plus the first refused mismatch), `Killed` empty; exactly one Critical `SessionReAdopted` incident with `FailureReason == "ReAdoptCapReached"` and the text `Re-adopted this session 3 times during this server uptime (cap 3). A further Failed/runner-Running mismatch was observed; automatic re-adoption is stopped.`; `alerts.For(S)` Critical count == 1; success incidents 1 to 3 say `re-adoption N of 3`, none says 4; the cap error log line appears once in the injected list logger.
- V-14: cap zero and negative | same | `Cap_zero_or_negative_allows_no_transition_and_escalates_on_the_first_mismatch` `[Arguments(0)]`, `[Arguments(-1)]` | sweep 1: row Failed, `CountFor == 0`, one Critical incident whose text says `0 times` and `(cap 0)`; sweeps 2 to 21: no probes, no incidents, no alerts.
- V-15: per-session state and restart reset | same | `Cap_state_is_per_session_and_a_new_singleton_starts_over` | S1 flaps to the latch, S2 restored once: `CountFor(S1) == 3`, `CountFor(S2) == 1`, one Critical incident total; a new `SessionReAdoptionState()` restores S1 again with `CountFor(S1) == 1` and no new Critical.
- V-16: no reset on healthy sweeps or same-row resume | same | `A_healthy_sweep_or_same_id_resume_does_not_reset_the_count` | after two commits, three healthy sweeps and one same-row resume (row bumped to a new generation, Running) leave `CountFor == 2`; a further fail commits the third; the next mismatch latches.
- V-17: two reconcilers race one Failed row | same | `Two_reconcilers_racing_one_Failed_row_commit_exactly_one_transition` | two services on two contexts sharing one singleton; `OnProbe` is a `Barrier(2)`; both `ScanAsync` run concurrently; `CountFor == 1`; exactly one Warning `SessionReAdopted` incident; row Running; no Critical.
- V-18: failed or superseded transitions never count, post-commit publish failure never refunds | same | `A_failed_or_superseded_transition_never_counts_and_a_post_commit_publish_failure_never_refunds` `[Arguments("probe")]`, `("save")`, `("cancel")`, `("superseded")`, `("publish-after-commit")` | probe: `BufferError` -> `CountFor == 0`, row Failed; save: `ThrowOnceSaveInterceptor` -> `CountFor == 0`, row Failed, no success incident; cancel: `OnProbe` cancels the token -> `CountFor == 0`; superseded: `OnProbe` executes an update setting `StartedAt = B`, `Status = Running` -> `CountFor == 0`, row Running at B untouched (`EndedAt`, `ExitCode` as set by the update), no incident; publish-after-commit: `MockEventBus` throws on `SessionStarted` -> `CountFor == 1`, row Running, one success incident, alert raised.
- V-19: escalation retry converges | same | `Cap_escalation_retries_only_unfinished_work_and_converges_to_one_incident` `[Arguments("incident-throws")]`, `("alert-throws")`, `("ambiguous-save")` and `An_unclaimed_capped_session_gets_the_alert_and_no_incident` | incident persist throws once: the next sweep inserts the incident under the fixed ID and raises the alert, totals 1 and 1; alert throws once: incident retained, next sweep raises the alert with no second insert; ambiguous save (`SavedChangesAsync` throws): the next sweep finds the ID and inserts nothing; after completion ten sweeps make zero incident or alert calls and the singleton's escalation reads reported; unclaimed: one Critical alert, zero incidents.
- V-20: lease semantics | new `tests/Antiphon.Tests/Application/SessionReAdoptionStateTests.cs` (`Unit`) | `A_refused_reservation_does_not_increment`, `A_released_lease_without_commit_does_not_consume_a_slot`, `Commit_increments_once_and_only_while_the_lease_is_held`, `Escalation_is_eligible_only_after_all_slots_are_used_and_a_further_mismatch_arrives`, `Cap_zero_escalates_on_the_first_mismatch_and_negative_normalizes_to_zero`, `Two_concurrent_reservations_serialize_and_only_one_commits` | direct assertions on `SuccessfulReAdoptions`, `TryReserveAsync` outcomes and the escalation record.
- V-21: legacy null generation is declined and reported once | `AgentSessionRuntimeTests.A_legacy_exit_event_without_a_generation_is_declined_and_reported_once` (disposition `Missing`, row unchanged, no events, no supervision change, one compatibility log/alert for ten repeats) and `SessionReconciliationServiceTests.A_legacy_runner_DTO_without_a_generation_closes_nothing_re_adopts_nothing_and_alerts_once` (DTO `AcceptedStartedAt == null`: Running row not closed, Failed row not re-adopted, `CountFor == 0`, one compatibility alert across ten sweeps) | as listed.
- V-22: launch POST carries and expects the echo | new `tests/Antiphon.Tests/Agents/SessionRunnerGenerationWireTests.cs` (handler capture, no database) | `A_generation_bearing_launch_posts_acceptedStartedAt_and_maps_the_echo` and `A_launch_response_without_the_echoed_generation_is_refused` | POST body `acceptedStartedAt` string equals A's microsecond ISO form; DTO maps it back; a response without the field throws `ConflictException` with the constant code Code names (`session_generation_not_echoed`) and the session is not reported started.
- V-23: capability gate | same class | `A_runner_without_sessionGenerationV1_refuses_a_generation_bearing_launch_and_attach_before_any_POST` `[Arguments("features-null")]`, `[Arguments("features-without-token")]` | `RunnerCapabilityMismatchException`; `handler.Requests` paths are exactly `["/capabilities"]` for launch and for `AttachHerdrAsync`; `Absent_capability_field_is_no_evidence_and_launches_as_before` (R-12) keeps passing for a spec with no generation.
- V-24: conditional kill wire | same class | `KillGeneration_posts_the_expected_generation_and_never_falls_back` `[Arguments("mismatch-200")]`, `[Arguments("404")]` | request path `sessions/{S}/kill-generation` with body `expectedAcceptedStartedAt`; mismatch returns the explicit non-kill result; 404 maps to `RunnerProblemException`; in both cases no request to `sessions/{S}/kill`.
- V-25: real runner exe | `Antiphon.SessionRunner.Tests`, new `RunnerSessionGenerationTests.C502_V25_real_runner_exe_advertises_the_capability_echoes_and_refuses_a_mismatched_kill` (`LocalHttpRunner`, random port) | `/capabilities.Features` contains `sessionGenerationV1`; `POST /sessions` with `acceptedStartedAt` A echoes A; one SSE `SessionExited` read from `/events` after a kill carries A; `POST /sessions/{id}/kill-generation` with B returns the mismatch result and the child pid is still alive; with A it kills.
- V-26: producers carry A while the registry holds B | `RunnerSessionGenerationTests` (`[NotInParallel("SessionLiveness")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, cmd.exe) | `C502_V26_kill_and_relaunch_publish_their_own_generations` (A killed -> payload A; relaunch with B -> `Get(S).AcceptedStartedAt == B`; `SweepVanishedSessions(StubProbe(false))` -> payload B); `C502_V26_exited_manifest_adoption_for_A_publishes_A_while_B_is_registered` (a manifest for S with A, exited, `KilledByRequest` written into the manifest directory while B runs; `AdoptOrphanedHostsAsync` publishes `SessionExited` with A; `Get(S)` still Running at B); `C502_V26_pending_and_terminal_herdr_objects_publish_their_own_generation` (`CreatePendingHerdr`/`CompletePendingAsExited` and `CreateAdoptedHerdrExited` on sidecars carrying A while B is registered) | payload `AcceptedStartedAt` equals the object's own value in every case.
- V-27: fast child exit before Start returns | same class | `C502_V27_fast_exit_before_Start_returns_carries_the_generation` | `cmd /c exit 3` with A: the exit payload and `Get(S)` carry A with `ExitCode == 3`.
- V-28: pty adoption, live and already exited | `PtyHostAdoptionTests` | `Running_session_survives_runner_restart_with_buffer_and_input_intact` gains `dtoB.AcceptedStartedAt.ShouldBe(A)` and `dtoB.StartedAt.ShouldNotBe(A)` (child start time differs); `Exit_while_runner_down_is_collected_on_adoption_with_the_real_exit_code` and the modern-backend twin assert the collected payload carries A | as listed.
- V-29: vanished-process exit carries the generation | `SessionLivenessTests.Sweep_marks_running_session_with_vanished_process_as_exited_and_publishes_the_missed_event` | payload `AcceptedStartedAt == A`.
- V-30: Herdr paths | `HerdrAdoptionSweepTests.R1_runner_restart_adopts_when_pane_lists_the_child` (adopted DTO carries A), `R2_restored_empty_pane_with_os_dead_is_RestartPresumedDead` (terminal payload carries A), `R6_unreachable_at_restart_with_os_alive_is_pending_then_adopts_when_herdr_returns` (pending DTO and the eventual adopt carry A); `HerdrAttachTests.Attach_binds_a_live_grok_by_argv_and_writes_an_attached_sidecar` (attach request carries the accepted row generation R; sidecar `AcceptedStartedAt == R` while `LaunchedAtUtc != R`; attaching the same pane again is idempotent) and `Runner_restart_readopts_an_attached_sidecar_with_origin_intact` (DTO carries R after restart) | as listed.
- V-31: metadata round trips and old files | `HerdrPaneSidecarTests.save_load_round_trips_atomically_and_load_all_sweeps` (field round-trips to the microsecond) and `A_sidecar_without_the_field_loads_a_null_generation`; `HostSessionPipeTests.Launch_streams_output_and_exit_with_manifest` (`LaunchMessage.AcceptedStartedAt = A` -> `LaunchedMessage.AcceptedStartedAt == A`; the exit manifest carries A) and `A_launch_pending_manifest_carries_the_generation_and_an_old_manifest_loads_null` (`PtyHostManifest` with `LaunchPending = true` round-trips the field; JSON without the field loads `null`) | as listed.
- V-32: strictly greater resume generation under a frozen or backward clock | `AgentControlServiceIntegrationTests.Resume_generation_is_strictly_greater_even_when_the_clock_is_frozen_or_moves_backward` (`BuildHarness` with `FakeTimeProvider`) | first resume with the clock frozen at T: `StartedAt == T + 1 microsecond`; second resume with the clock moved backward: prior + 1 microsecond; third with the clock advanced: the normalized advanced time; the enqueued accepted generation equals the committed value each time (`adapter.StartedAcceptedGeneration`).
- V-33: queued work carries its explicit accepted value | `AgentSessionLaunchQueueOwnershipTests.Queued_launch_carries_the_explicit_accepted_generation_and_never_re_reads_a_replaced_row` | enqueue with G equal to the row: the adapter's started spec carries G; enqueue with G after the row moved to G2: the launch returns without starting (`adapter.Started == false`), no evidence written.
- V-34: binding and launch field must agree | `AgentSessionLaunchFailureTests.A_verification_binding_whose_generation_disagrees_with_the_launch_field_is_rejected_before_the_process_starts` | `adapter.Started == false`; row Failed with `FailureReason` naming the disagreement code; no runner POST.
- V-35: recovery without a retained generation declines the kill | `SessionMessageQueueDeliveryVerificationTests.A_deferred_re_check_of_a_row_sent_without_a_retained_generation_declines_the_recovery_kill` (a row Sent while the session was working, re-checked later through `DeliverNextLockedAsync`'s deferred failure, with no captured generation) and `A_late_UserPrompt_confirmation_prevents_the_generation_conditional_recovery_kill` | first: `adapter.KillGenerationCalls` empty, `adapter.KillCount == 0`, the row's existing revert/park disposition unchanged, the incident detail says the destructive recovery was declined; second: `KillGenerationCalls` empty, exactly one Enter, message Sent.
- V-36: pump threads the typed exit and publishes only on applied | `SessionRunnerEventPumpTests.C502_V36_pump_forwards_the_typed_exit_and_publishes_nothing_for_a_stale_disposition` (`ScriptedSessionRunnerClient.ProduceExit(...)`) | `ObserveExitAsync` receives the full typed event including `AcceptedStartedAt`; a stale event yields no `SessionExited` on `GatedEventBus`; a matching event yields one carrying the generation.

### Guards the regression

- R-1: the incident (a `KilledByRequest` code 1 exit of A consumed after B resumed fails B) | V-1 method; `Status.ShouldBe(SessionStatus.Running)` and `FailureReason.ShouldBeNull()`.
- R-2: a stale exit publishes `SessionExited`/`AgentChanged` for the live session | V-1 method; the `PublishedEvents` assertions.
- R-3: existing exit-reason precedence on a matching generation | `AgentSessionRuntimeTests`: `a_clean_process_exit_records_ProcessExit_when_no_prior_source`, `an_exit_event_does_not_overwrite_an_OperatorRequest_source`, `an_exit_event_backfills_ProcessExit_onto_an_already_closed_row_with_no_source`, `a_cpu_spin_watchdog_exit_records_SystemRequest`, `HerdrPaneClosed_exit_is_Failed_not_a_clean_stop`, `HerdrPaneLeftOpen_exit_is_Failed_and_records_the_warning_incident`, `HerdrLaunchDetectTimeout_exit_is_Failed_and_does_not_record_HerdrPaneLeftOpen`, `A_pane_closed_exit_after_DetectTimeout_evidence_keeps_DetectTimeout`, `An_operator_stopped_row_gets_no_evidence` now called with a matching generation; every existing assertion unchanged.
- R-4: matching snapshots still close | `SessionReconciliationServiceTests.Runner_reported_exit_is_mirrored_to_the_db_session`, `Runner_reported_CpuSpinKilled_exit_records_SystemRequest`, `Runner_reported_HerdrPaneClosed_exit_fails_the_session_not_a_clean_stop`, `Runner_reported_typed_exit_stamps_evidence` with `RunnerRunning`-style DTOs carrying the seeded generation; unchanged assertions.
- R-5: matching Running evidence still resumes an interrupted launch | `Starting_runner_Running_unowned_resumes_the_launch`; `Resumes` contains the session.
- R-6: matching Failed rows still re-adopt, and count one after commit | `Failed_session_the_runner_still_serves_is_re_adopted_and_its_agent_restored` and `An_unclaimed_session_is_re_adopted_and_left_running_for_the_operator` gain `flapState.CountFor(sessionId).ShouldBe(1)`.
- R-7: standing stop still revokes and the failed launch still kills what it started | `SpecialistStartIntentTests.Card0415_V22_human_stop_wins_against_queued_and_inflight_Check_launch` (`Code == "specialist_start_intent_revoked"`, `Killed == true`) and `AgentSessionLaunchFailureTests.Interactive_launch_failure_kills_the_process_before_disposing_it` (kill happens; now recorded on `KillGenerationCalls` with the launch's generation).
- R-8: delivery-failure kill semantics | `SessionMessageQueueDeliveryVerificationTests.Wedged_composer_withholds_enter_reverts_message_and_restarts_always_on_agent` (still kills, now generation-conditional with a match) and `Non_always_on_agent_gets_incident_and_revert_but_no_kill` (still no kill).
- R-9: deliberately changed assertion | `Re_adoption_stops_and_escalates_once_the_flap_cap_is_reached` is removed in favour of V-13; its `CountFor(sessionId).ShouldBe(4)` specified the increment-on-refusal defect, and the committed count after the same four rounds is 3.
- R-10: runner adoption unchanged | every existing assertion in `PtyHostAdoptionTests`, `SessionLivenessTests`, `HerdrAdoptionSweepTests`, `HerdrAttachTests` still holds (the generation assertions are additive).
- R-11: host manifest unchanged | `HostSessionPipeTests` existing fields and `Host_lingers_after_exit_until_shutdown_ack_then_removes_manifest`.
- R-12: a spec without a generation is unaffected by the new gate | `SessionRunnerCapabilityGateTests.Absent_capability_field_is_no_evidence_and_launches_as_before`; `["/capabilities", "/sessions"]`.
- R-13: operator Stop/Kill keeps the unconditional route | `AgentSessionInterruptedLaunchResumeTests.Non_delegate_Starting_row_does_not_attach_and_kills_through_the_runner` (`RecordingKillRunner.Killed` contains the session) and `SessionReconciliationServiceTests.A_stopped_session_the_runner_still_serves_gets_its_kill_re_issued` (`Killed` contains the session; `KillGenerationCalls` empty).

### Guard inventory

- G-1: D-1 queued interactive work passes its accepted generation explicitly; a replaced row is never re-read to fill the field | PC-1
- G-2: D-1 same-row resume commits a generation strictly greater than the prior one (`max(now, prior + 1 microsecond)`) | PC-2
- G-3: D-1 a verification binding's generation must equal the launch field; disagreement is rejected before native launch | PC-3
- G-4: D-2 every runner exit producer reads its own object's immutable generation, never the registry's current entry | PC-4
- G-5: D-2 pty-host launch-pending, launched and exit manifests and the `LaunchedMessage` carry the field | PC-5
- G-6: D-2 the server refuses a generation-bearing launch whose response does not echo the field | PC-6
- G-7: D-2 Herdr launched, attached and pending sidecars restore the generation before any adopted or synthesized publication | PC-7
- G-8: D-2 old metadata loads with a null generation and null is never replaced by the database's current value | PC-8
- G-9: D-3 `CloseSessionOnExitAsync` compares the event generation with the freshly read locked row inside the write transaction before any change | PC-9
- G-10: D-3 stale, unknown, missing and persistence-failure dispositions publish no `SessionExited` or `AgentChanged` | PC-10
- G-11: D-3 the owner pointer is revalidated before the agent row changes | PC-11
- G-12: D-3 the reconciler's Exited arm requires an exact generation match | PC-12
- G-13: D-3 absence from a stale list cannot close a row accepted after the list; ambiguous candidates are refreshed | PC-13
- G-14: D-3 the Failed-to-Running arm revalidates generation and Failed status under the row lock before committing; mismatched Running evidence never re-adopts | PC-14
- G-15: D-3 mismatched Running evidence never resumes an interrupted launch | PC-15
- G-16: D-4 `RunnerTerminalSession` captures the accepted generation before awaiting Start and adapter cleanup kills conditionally on it | PC-16
- G-17: D-4 the runner's kill-generation operation selects under the per-session launch gate, compares identity and kills only that object; mismatch or missing returns an explicit non-kill | PC-17
- G-18: D-4 a conditional-kill refusal or 404 is never retried as an unconditional kill | PC-18
- G-19: D-4 the delivery attempt captures the generation at transport time; a recovery without a retained generation declines the kill | PC-19
- G-20: D-4 a late matching `UserPrompt` confirmation prevents the recovery kill and any resubmit | PC-20
- G-21: D-4 an old adapter's exit waiter finishes as superseded when GET names another generation and never borrows B's exit | PC-21
- G-22: D-5 no generation-bearing launch or attach POST without `sessionGenerationV1` | PC-22
- G-23: D-5 a legacy null generation produces no exit mutation, no re-adoption, no implicit Failed and one bounded compatibility report per session per uptime | PC-23
- G-24: D-6 a disallowed observation never increments; the count is committed transitions only | PC-24
- G-25: D-6 reserve, re-read under lock, verify still Failed and the probed generation, commit, then increment; a failed, canceled or superseded commit releases without consuming a slot; a post-commit publish failure never refunds | PC-25
- G-26: D-6 escalation fires on the first eligible mismatch after all slots are used, cap zero allows nothing and escalates on the first mismatch, negative caps normalize to zero | PC-26
- G-27: D-6 after the latch, no adoption probes, no incidents, no repeated cap logs or alerts, no kill or restore | PC-27
- G-28: D-6 restart resets the count; a same-row resume or healthy observation does not | PC-28
- G-29: D-7 the transition and its success incident are one explicit transaction; `AlertAndIncidentAsync`'s side-effect save is not used across a partial restoration | PC-29
- G-30: D-7 one pending escalation record, its incident inserted once under a fixed ID, retained when the alert fails, reported only after persistence and alert both succeed, ambiguous saves re-checked by ID | PC-30
- G-31: D-6 the per-session lease serializes concurrent reconcilers so exactly one transition commits | PC-31
- G-32: D-4 the interactive launch failure catch writes evidence only when the row still holds the launch's generation (existing CARD-0466 guard the overlap relies on) | PC-32

guards=32, mapped=32, missing=0, duplicate PC mappings=0.

### Positive controls

Each PC: apply the mutation, run only the named method with `--treenode-filter "/*/*/<Class>/<Method>"`
under `--property:OutputPath=bin-pc/`, confirm the named assertion is the red one in a fresh TRX,
restore (refresh the restored file's write time), run green. Mutation reports break, red, restore and
green for every row after land; Code implements the tests and runs V/R; ordinary Review judges them
before land. PC-4, PC-5, PC-7, PC-16 through PC-18 and PC-21 exercise process-spawning or
`ProcessSpawnLimit` classes and run serially; the rest may be batched when they touch different files
and methods. Rows sharing a file (PC-9/PC-10/PC-11 in `AgentSessionRuntime`; PC-12 through PC-15 and
PC-24 through PC-31 in `SessionReconciliationService`/`SessionReAdoptionState`) run in separate batches.

- PC-1: break G-1 by passing `null` for `acceptedGeneration` from the queue worker so `LaunchInteractiveAsync` re-reads the row; expect `AgentSessionLaunchQueueOwnershipTests.Queued_launch_carries_the_explicit_accepted_generation_and_never_re_reads_a_replaced_row` red at `adapter.Started.ShouldBeFalse()`.
- PC-2: break G-2 by assigning `previous.StartedAt = resumeNow` without the `max(..., prior + 1 microsecond)`; expect `AgentControlServiceIntegrationTests.Resume_generation_is_strictly_greater_even_when_the_clock_is_frozen_or_moves_backward` red at `StartedAt.ShouldBeGreaterThan(previous)` on the frozen step.
- PC-3: break G-3 by deleting the binding-versus-field comparison before native launch; expect `AgentSessionLaunchFailureTests.A_verification_binding_whose_generation_disagrees_with_the_launch_field_is_rejected_before_the_process_starts` red at `adapter.Started.ShouldBeFalse()`.
- PC-4: break G-4 by making `CreateAdoptedExited` publish the registry's current object's generation for that session ID instead of the manifest's; expect `RunnerSessionGenerationTests.C502_V26_exited_manifest_adoption_for_A_publishes_A_while_B_is_registered` red at `payload.AcceptedStartedAt.ShouldBe(A)`.
- PC-5: break G-5 by dropping the field from the launched manifest and `LaunchedMessage` in `HostSession.LaunchCoreAsync`; expect `HostSessionPipeTests.Launch_streams_output_and_exit_with_manifest` red at `launched.AcceptedStartedAt.ShouldBe(A)`.
- PC-6: break G-6 by skipping the echo comparison in `SessionRunnerHttpClient.StartAsync`; expect `SessionRunnerGenerationWireTests.A_launch_response_without_the_echoed_generation_is_refused` red at `Should.ThrowAsync<ConflictException>`.
- PC-7: break G-7 by not restoring the sidecar's generation in `AdoptHerdrAsync` (leave the object's value null); expect `HerdrAdoptionSweepTests.R1_runner_restart_adopts_when_pane_lists_the_child` red at `dto.AcceptedStartedAt.ShouldBe(A)`.
- PC-8: break G-8 by stamping `DateTime.UtcNow` when a loaded manifest has no generation; expect `HostSessionPipeTests.A_launch_pending_manifest_carries_the_generation_and_an_old_manifest_loads_null` red at `loaded.AcceptedStartedAt.ShouldBeNull()`.
- PC-9: break G-9 by comparing against an `AsNoTracking` read taken before `BeginTransactionAsync` instead of the locked row; expect `SessionGenerationExitTests.C502_V4_exit_consumer_blocked_on_the_row_lock_sees_B_after_commit` (`same-owner`) red at `Status.ShouldBe(SessionStatus.Starting)`.
- PC-10: break G-10 by publishing `SessionExited` in `ObserveExitAsync` regardless of disposition; expect `C502_V1_queued_A_exit_released_after_B_resumed_changes_nothing` red at the `PublishedEvents.ShouldNotContain(e => e.EventName == "SessionExited")` assertion.
- PC-11: break G-11 by flipping the agent found by `PersistentSessionId` at the start of the transaction without re-reading the pointer under the lock; expect `C502_V4_...` (`pointer-moved`) red at `agent.Status.ShouldBe(AgentStatus.Running)`.
- PC-12: break G-12 by removing the generation comparison from the Exited arm of `ReconcileSessionsAsync`; expect `SessionReconciliationServiceTests.Runner_Exited_snapshot_for_a_superseded_generation_does_not_close_the_row` red at `Status.ShouldBe(SessionStatus.Running)`.
- PC-13: break G-13 by closing a live row absent from the list without the refresh GET; expect `A_stale_list_absence_does_not_close_a_row_accepted_after_the_list` red at `Status.ShouldBe(SessionStatus.Starting)`.
- PC-14: break G-14 by skipping the locked re-read of generation and status before the commit in the re-adoption transaction; expect `A_failed_or_superseded_transition_never_counts_and_a_post_commit_publish_failure_never_refunds` (`superseded`) red at `CountFor(sessionId).ShouldBe(0)`.
- PC-15: break G-15 by removing the generation comparison in `ResumeInterruptedLaunchesAsync`; expect `Runner_Running_snapshot_for_a_superseded_generation_does_not_resume_an_interrupted_launch` red at `ownership.Resumes.ShouldBeEmpty()`.
- PC-16: break G-16 by capturing the generation after `_client.StartAsync` returns (so a Start that throws leaves it unset and cleanup falls to the unconditional route); expect `RunnerTerminalSessionGenerationTests.Cleanup_after_a_Start_that_threw_kills_conditionally_on_the_captured_generation` red at `client.KillGenerationCalls.ShouldBe([(S, A)])`.
- PC-17: break G-17 by making the runner's kill-generation handler ignore the expected value and kill the current object; expect `RunnerSessionGenerationTests.C502_V25_real_runner_exe_advertises_the_capability_echoes_and_refuses_a_mismatched_kill` red at the child-pid-alive assertion after the mismatched kill.
- PC-18: break G-18 by calling `KillAsync` when the conditional kill returns mismatch; expect `SessionRunnerGenerationWireTests.KillGeneration_posts_the_expected_generation_and_never_falls_back` (`mismatch-200`) red at `handler.Requests.ShouldNotContain(r => r.RequestUri!.AbsolutePath.EndsWith("/kill"))`.
- PC-19: break G-19 by defaulting a missing captured generation to the row's current `StartedAt` inside `HandleDeliveryFailureAsync`; expect `SessionMessageQueueDeliveryVerificationTests.A_deferred_re_check_of_a_row_sent_without_a_retained_generation_declines_the_recovery_kill` red at `adapter.KillGenerationCalls.ShouldBeEmpty()`.
- PC-20: break G-20 by skipping `GraceConfirmAsync` for `NoTranscriptRecord`; expect `A_late_UserPrompt_confirmation_prevents_the_generation_conditional_recovery_kill` red at `adapter.KillGenerationCalls.ShouldBeEmpty()`.
- PC-21: break G-21 by ignoring the DTO's generation in `WaitForExitAsync` and waiting for `Status == "Exited"` as today; expect `RunnerTerminalSessionGenerationTests.An_exit_waiter_finishes_as_superseded_when_GET_names_another_generation` red at `Exited.Result.ShouldNotBe(42)` (B's later exit code is borrowed).
- PC-22: break G-22 by removing the `sessionGenerationV1` check from `StartAsync` and `AttachHerdrAsync`; expect `SessionRunnerGenerationWireTests.A_runner_without_sessionGenerationV1_refuses_a_generation_bearing_launch_and_attach_before_any_POST` (`features-null`) red at `handler.Requests` paths `ShouldBe(["/capabilities"])`.
- PC-23: break G-23 by treating a null event generation as a match; expect `AgentSessionRuntimeTests.A_legacy_exit_event_without_a_generation_is_declined_and_reported_once` red at `Status.ShouldBe(SessionStatus.Running)`.
- PC-24: break G-24 by incrementing `SuccessfulReAdoptions` inside the refused branch of the reservation; expect `SessionReAdoptionStateTests.A_refused_reservation_does_not_increment` red at `SuccessfulReAdoptions.ShouldBe(cap)`.
- PC-25: break G-25 by incrementing before `SaveChangesAsync` commits the restoration; expect `A_failed_or_superseded_transition_never_counts_and_a_post_commit_publish_failure_never_refunds` (`save`) red at `CountFor(sessionId).ShouldBe(0)`.
- PC-26: break G-26 by escalating on the third successful restoration (when the count reaches the cap) instead of on the next mismatch; expect `Re_adoption_counts_committed_transitions_and_latches_after_the_cap` red at the round-3 `alerts.For(sessionId).Count(a => a.Severity == Critical).ShouldBe(0)` assertion.
- PC-27: break G-27 by leaving the probe and the Critical `AlertAndIncidentAsync` call in the latched path; expect the same method red at `Probed.Count.ShouldBe(4)` after round 104 (or the single-Critical-incident count).
- PC-28: break G-28 by clearing the session's count on a healthy sweep; expect `A_healthy_sweep_or_same_id_resume_does_not_reset_the_count` red at `CountFor(sessionId).ShouldBe(2)`.
- PC-29: break G-29 by recording the success incident through `AlertAndIncidentAsync` before the restoration save (the current shape); expect `A_failed_or_superseded_transition_never_counts_and_a_post_commit_publish_failure_never_refunds` (`save`) red at the `AgentIncidents` count for the session `ShouldBe(0)` (an incident committed by the side-effect save survives the failed restoration).
- PC-30: break G-30 by generating a new incident ID on every escalation attempt; expect `Cap_escalation_retries_only_unfinished_work_and_converges_to_one_incident` (`alert-throws`) red at the Critical incident count `ShouldBe(1)`.
- PC-31: break G-31 by releasing the lease before the restoration commits; expect `Two_reconcilers_racing_one_Failed_row_commit_exactly_one_transition` red at `CountFor(sessionId).ShouldBe(1)`.
- PC-32: break G-32 by deleting `if (session.StartedAt != generation) return;` from the `LaunchInteractiveAsync` catch; expect `SessionGenerationDeliveryOverlapTests.C502_V12_B_resumed_before_A_cleanup_gets_zero_kill_calls_and_boots` red at `FailureReason.ShouldBeNull()` for B.

### Out of scope

- Real provider sessions (Claude, Grok, Codex) and the real-CLI stub canaries: the fix is transport and
  identity, not provider behaviour; the fake adapter and cmd.exe children carry the identity the same way.
- Live Herdr: `FakeHerdrServer` exercises every sidecar and adoption branch; real pane lifecycle is
  covered by the existing Herdr live tests that remain untouched.
- The production runner on 17204 and any AppHost restart: every runner is in-proc or a random-port
  `LocalHttpRunner`; rollout timing stays with the caller (D-5).
- Grok rules generation, CARD-0478 execution and receipt identity, PID start times: D-1 states their
  meaning is unchanged; the existing custody and rules suites pin them.
- Durable exactly-once alert delivery across a server restart: D-7 accepts the in-memory reset.
- Queue-generation persistence for old ambiguous attempts (D-4 rejected it); V-35 pins the decline.
- Browser E2E and client changes: no frontend behaviour changes.

### Cost

Filters use `--property:OutputPath=bin-c502/` for V/R and `bin-pc/` for Mutation. Minutes are
estimated on this machine (no run was made during design; the Unit lane figure is the measured 70 s
from CARD-0475); the two process-spawning assemblies run one after the other.

| Item | Suite and filter | Minutes (estimated) |
|---|---|---|
| Setup/build | `Antiphon.Tests`, `Antiphon.SessionRunner.Tests`, `Antiphon.PtyHost.Tests` into `bin-c502/` | 9 |
| Unit lane | `Antiphon.Tests` `/*/*/*/*[Category=Unit]` (measured 70 s, plus the new `SessionReAdoptionStateTests` and `RunnerTerminalSessionGenerationTests`) | 2 |
| New server classes | `Antiphon.Tests` `/*/Antiphon.Tests.Application/(SessionGenerationExitTests*)\|(SessionGenerationDeliveryOverlapTests*)/*` and `/*/Antiphon.Tests.Agents/(SessionRunnerGenerationWireTests*)/*` | 5 |
| Extended server classes | `Antiphon.Tests` `/*/Antiphon.Tests.Application/(SessionReconciliationServiceTests*)\|(SessionRunnerEventPumpTests*)\|(AgentSessionRuntimeTests*)\|(AgentSessionLaunchQueueOwnershipTests*)\|(AgentControlServiceIntegrationTests*)\|(AgentSessionLaunchFailureTests*)\|(SessionMessageQueueDeliveryVerificationTests*)\|(SpecialistStartIntentTests*)\|(AgentSessionInterruptedLaunchResumeTests*)/*` and `/*/Antiphon.Tests.Agents/(SessionRunnerCapabilityGateTests*)/*` | 16 |
| Runner classes | `Antiphon.SessionRunner.Tests` `/*/*/(RunnerSessionGenerationTests*)\|(PtyHostAdoptionTests*)\|(SessionLivenessTests*)\|(HerdrAdoptionSweepTests*)\|(HerdrAttachTests*)\|(HerdrPaneSidecarTests*)/*` | 8 |
| Host classes | `Antiphon.PtyHost.Tests` `/*/*/(HostSessionPipeTests*)/*` | 1 |
| **Ordinary V/R floor (Code)** | build + Unit + the five class groups above, sequential | **41** |
| PC floor (Mutation) | 32 rows: 7 serial process-spawning rows at about 4 min each (mutate, incremental build, method-scoped red, restore, build, green) = 28; the remaining 25 in 8 batches by file at about 5 min each = 40; plus one `bin-pc/` build = 9 | 77 |
| **Total verification floor** | V/R floor + PC floor | **118** (estimated) |

Savings: method-scoped PC filters instead of class filters save about 2 min per row (roughly 60 min
across 32 rows); batching the 25 independent rows into 8 file-disjoint batches saves about 60 min
against serial execution; the class-group filters above replace a namespace run of
`Antiphon.Tests.Application` (measured 26 min for one service change in CARD-0239) so the V/R floor
stays under an hour. Sharding the PC plan across two detached worktrees off the task branch (allowed
above 15 to 20 rows) would bring the PC floor to about 45 min but is optional.

Handoff checks: bodies read; guards=32, mapped=32, missing=0, duplicate PC mappings=0; every PC names a
compiling mutation, an exact method filter and the decisive assertion; the cost table is numeric and
labelled estimated except the measured Unit lane.
