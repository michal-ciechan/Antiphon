# CARD-0502: generation-safe exits and bounded re-adoption accounting

Status: planned; separate TestDesign required before Code.

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
