# CARD-1523 / CARD-1524: bounded phone-home consumption and accurate backlog diagnostics

Date: 2026-10-22 UTC. Stage: Plan. Verification remains a separate TestDesign stage.
Source inspected: `b8f0ce088b1ab6d7b8aebfb16f85e95b81d3a904`.
Both Antiphon cards were read in full with `scripts/card.ps1 get ... -Board Antiphon`.

## Outcome and evidence boundary

Keep a finite consumer stall from disconnecting a healthy phone-home transport.
Use negotiated event credits, returned only after observation completes, and a
small, bounded pool of consumers that preserves order within each session.
Keep the existing event-count and payload-size limits. Do not discard, merge or
rewrite output, transcript, exit, transcript-fault or transcript-bound events to
relieve pressure; the hard sustained-overload boundary is explicit in D-6.
Make the next overload identify the active observer and event mix. Separately,
rename cumulative received counters and render end diagnostics from one snapshot.

This document is a design, not a performance qualification or an executed red
witness. The exact historical slow observer is **unknown**. The source establishes
the serial bottleneck and its awaited work; the retained incident logs do not
distinguish SignalR, activity persistence, transcript ingestion, scheduling or GC.
The controlled witnesses below isolate those dependencies without recreating CPU
saturation on a product host. Do not claim that SignalR caused the three incidents.

The full Debug report was read from published commit
`1723b8bebffe6a8e7e9a7534b55882ce001cce0b`, branch
`feat/card-task-e10e4bf9`, at
`docs/investigations/2026-10-23-server2-runner-disconnect-flaps-debug.md`.
It was not yet present in `origin/master` at the initial fetch. Its Docs owner
lands that report separately; do not copy or cherry-pick it into this task.
The report dates its incident times in UTC on October 22 despite its October 23
filename. Its three desktop overflows reached the 8,192-event limit; reported
pending payload was approximately 1–1.5 MB, well below the 16 MiB size limit.
Desktop load overlaps those events but is not a measured per-observer cause.

## Ground truth

References name methods at the inspected source; later line numbers may move.

| Card assumption or question | What the code does | Design consequence |
|---|---|---|
| Which consumer releases the desktop backlog? | `PhoneHomeRecoveryPump.PumpEventsAsync` has one `await foreach` per connection. It calls `ReleaseEvent` in `finally`, after the awaited runtime operation. `RunOneAsync` starts it only after successful catch-up. | Fix this owner, retain catch-up-before-live ordering, and count work being observed as pending. |
| Could the ordinary runner pump be competing for those events? | `SessionRunnerEventPump` uses `RoutingSessionRunnerClient.StreamEventsAsync`, which explicitly delegates to `_directory.Local`. `PhoneHomeRunnerClient.StreamEventsAsync` is another iterator over the remote channel, but it is not the registered hosted consumer. | The incident's hosted owner is the recovery pump. Account for the other iterator when changing the queued-item type; do not accidentally wire a competing remote consumer. |
| Output observation is inexpensive forwarding. | `AgentSessionRuntime.ObserveOutputAsync` updates sequences/first-delta state, synchronously scans mentions, awaits each `IEventBus.PublishToGroupAsync`, then awaits `RecordActivityAsync`. | Measure these phases separately. A blocked output observer currently blocks every session on that connection. |
| Every output event reads its owner and writes activity. | CARD-0679 already caches owner bindings per connection and throttles activity writes with `ActivityWriteMinIntervalMs`. Transcript ingestion also already separates committed rows/actions from deferred effects under CARD-1259. | Preserve these mechanisms; do not implement another cache, throttle or transcript outbox. |
| A heavy lock is proven to be the cause. | The transport uses short accounting/rate/inventory critical sections. Mention processing has per-session locks; persistence has session write leases. The pump also awaits external work. No incident timing identifies one lock or await as the culprit. | Record active phase, elapsed time and queue age. Do not widen lock scopes around awaits or claim a particular lock caused the incident. |
| The server channel is bounded by its channel type. | `PhoneHomeLiveConnection` uses an unbounded channel with explicit count/size reservation before enqueue; limits include the in-progress observer. The receive loop throws `phone_home_event_overflow` before admission beyond a limit. | Maintain explicit global reservation across every new queue and worker. Moving a frame between queues must not release its reservation. |
| Blocking the receive loop is harmless backpressure. | Heartbeats, RPC results/errors and events share `ReceiveLoopCoreAsync`. | Never await consumer capacity in that loop. Apply credits to the runner's event sender before its socket write gate. |
| Reconnect replays an unlimited event backlog. | Runner `SessionRunnerEventHub.SubscribeBounded` limits its subscriber queue. `EventLoopAsync` sends without pacing beyond the socket writer. Desktop catch-up performs List and sequential transcript reads before starting live consumption; those responses are RPCs, not a replay of the live event hub. Live events accumulate during catch-up. | There is a bounded runner queue and a potentially rapid socket burst, not a proven unlimited replay. Credits must work during catch-up while leaving List/Transcript RPCs usable. |
| “Live buffer” is the retained queue. | `_liveBufferEvents`/`_liveBufferBytes` only increment. `ReleaseEvent` decrements separate `_pending*` fields. `SessionRunnerEndpoints.LogEnded` labels the cumulative count “live buffer”. | Rename to received totals; preserve the public pending gauges. Use 64-bit cumulative counters. |
| The 50%/90% warnings use the cumulative counter. | `WarnHighWater` already divides pending count/size by their limits and fires once per threshold per connection. | Preserve this meaning and cadence. Test a drained queue with large lifetime totals so it cannot emit a false high-water warning. |
| `pendingEventBytes` measures UTF-8 bytes. | Desktop currently uses `GetRawText().Length` (UTF-16 code units). Runner hub uses UTF-8 JSON bytes plus the event-name length. | Define desktop/window payload bytes explicitly as UTF-8 payload JSON bytes, calculated once and released with the admitted item. Keep runner hub accounting independently bounded; do not equate its different overhead measure with the window. |
| An end line is one observation. | `LogEnded` reads mutable counters separately for interpolated text and structured properties. A concurrent drain can change them between reads. | Capture once, format both representations from that immutable record, and distinguish overflow-trigger state from later end state. |
| Existing hardening should be replaced. | CARD-0996 has real Kestrel fixtures plus deterministic receive/endpoint-completion control and conditional transport-error fields. CARD-0729 scopes status to the requested runner and returns 404 for unknown IDs. | Extend those seams and preserve those contracts; no second connection-end logger, status route or abort test framework. |

## Decisions

### D-1. Preserve event contents and existing hard limits

The admitted event remains the original event, with its original session ID,
sequence, UUID, generation fields and text. Preserve per-session FIFO across
output, transcript and lifecycle events. Output mention parsing must see the
original ordered chunks. Keep `MaxPendingEvents=8192` and
`MaxPendingEventBytes=16777216` as the default desktop bounds; keep independently
configured runner subscriber limits. This plan does not change fleet settings.

Rejected: dropping “low-value” output. Output participates in mention parsing and
terminal sequence reconstruction. Rejected: concatenating output under its last
sequence; a client's buffer snapshot can cut through that batch and duplicate or
omit a prefix (`client/src/features/board/SessionTerminal.tsx`). Rejected: raising
the count cap alone; historical mean ingress does not establish a safe stall or
drain bound. No lease, heartbeat or tunnel timeout changes address this overflow.

### D-2. Negotiate a count-and-byte event window on registration

Add the capability `phoneHomeEventWindowV1` and an optional registration-response
`eventWindow` with version, maximum pending events and maximum payload UTF-8 bytes.
Store the negotiated window on the registration ticket and resulting connection;
the response and accepted connection must use the same limits. Append an
`EventWindow` frame kind without renumbering existing kinds. Keep protocol version
1: this is an additive, explicitly negotiated capability, not an implicit change
to how all peers send events.

Only a supporting runner and supporting server activate it. An old runner gets
no window and keeps the current overflow/reconnect behavior. A new runner talking
to an old server sees the absent field and uses that same legacy behavior. Never
enable credits merely because a frame with a new numeric kind arrives. Validate
positive bounds and the supported version; malformed negotiated data is a typed
protocol failure, not a zero-credit wait forever. Expose negotiated mode in
connection diagnostics so mixed deployment cannot be mistaken for activation.
Advertise support through `PhoneHomeRuntimeAdapter`'s capability list; the test
command surface must obtain capabilities from that real adapter rather than
hard-code a feature that the shipping service does not offer.

For the current epoch, the server advertises cumulative **released** event count
and payload-byte count in window updates. The runner tracks cumulative reserved
sent count/bytes. It may reserve the next event only when both
`sent + next - released <= negotiated maximum` inequalities hold. Totals are
64-bit with checked bounds. Duplicate updates are harmless; stale-epoch updates
cannot release current credit. Reject impossible acknowledgements beyond sent
totals and inconsistent nonmonotonic updates. This is memory flow control, not a
claim that a user prompt was delivered or that a transcript row committed.

Return credit exactly once on observation completion or the existing per-event
failure path. Credit notification is nonblocking: one connection-owned writer
coalesces cumulative updates in a single pending slot and uses the existing send
gate. No task or queued acknowledgement per event. A signal arriving during a
send must cause another send of the latest totals; losing that wakeup could
otherwise leave an idle window permanently closed. Connection cancellation owns
and joins this writer; no update may go to a replacement socket.
Apply the same ownership rule to the runner's receive, heartbeat and event
loops: cancellation must unblock a credit waiter and join the owned loops,
without disposing a socket underneath a still-writing child.

At the runner, wait for event credit **before** acquiring
`PhoneHomeConnectionWriter`'s gate. Keep the current event reserved in the bounded
hub until its send finishes, as today. Heartbeats and RPC replies remain on their
existing paths and do not spend event credit. An event larger than the negotiated
byte window must fail explicitly with size diagnostics; it cannot wait for credit
that will never exist. The desktop still enforces its hard cap against legacy,
buggy or noncompliant senders.

Rejected: `BoundedChannelFullMode.Wait` inside the desktop receive loop, which
would also hold heartbeats and replies. Rejected: unbounded buffering, disk
spooling or a second transport; they add storage/recovery protocols that this
finite-stall repair does not require.

### D-3. Four consumers per connection, one active observer per session

Replace the single observation task with a connection-owned scheduler and four
fixed workers. Add typed `PhoneHomeRunner:EventConsumerConcurrency`, default 4,
validated in the range 1–16. Value 1 is the serial fallback for diagnosis. It does
not disable negotiated flow control. The setting is per connection, not per
session or per event; do not size it from a fleet hostname or seat count.

The dispatcher parses once and puts the admitted item on that session's FIFO.
A session can be queued as ready once and owned by at most one worker. A worker
observes one event, then returns a still-nonempty session to the tail of the ready
queue. This permits other sessions to progress without reordering a session's
output, transcript or exit. There is no task per session, no semaphore waiter per
event and no global priority that lets an exit pass its preceding transcript.
Start only the fixed workers off the supervision/receive task so synchronous
mention work cannot occupy the task that supervises or receives the connection.

All queues and active items share the existing connection reservation. With
`P` admitted events there can be at most `P` nonempty session queues and ready
keys, and at most four active observers. Remove empty queue metadata. A transfer
from ingress to a worker changes no pending count or bytes. Release in exactly
one `finally`; invalid/unrecognized and foreign-owner frames retain their current
failure/ignore semantics and are also released exactly once. Each observation
gets its own service scope; no DbContext is used concurrently. Retain the current
owner/store checks, cached binding policy, exit generation checks, replay guards,
inventory updates and per-event failure isolation.

Catch-up still commits before live observation starts. The pump owns dispatcher,
workers and their cancellation. Pump restart, supersession and shutdown cancel
and join those children before replacing that connection's observer group. Do
not create a replacement group while an old group can still observe the same
session. Report an uncooperative observer; do not detach it, grant synthetic
credit or kill an agent to release it. Keep a single tracked teardown task in
the runner cycle; later cycles check its completion without awaiting it inline
in `RunCycleAsync`. Supervision continues for other runners. Host stop joins all
tracked groups. Unread items can release their memory reservation during final
closed-connection cleanup, but must not send credit or pretend to be observed.

This removes cross-session head-of-line blocking when a worker is stalled. It
cannot make progress if all workers or the entire process are suspended. It also
does not allow a same-session transcript to overtake that session's output.
The flow window supplies bounded protection in those cases. Four workers are a
design bound, not a claim of fourfold production throughput.

### D-4. Measure the observer and the producer mix without payload logging

Keep a fixed-size, per-connection diagnostic accumulator. Record admitted and
released count/bytes by the known event kinds (unknown is one bucket), oldest
pending age, and each active worker's session ID, event kind, current phase and
elapsed time. A fixed top-16 session sample attributes producers; report its
overflow/other bucket, never retain an unbounded historical session map. Keep
queued payloads out of all logs and metric labels.

Measure `parse`, `owner`, `output.mention`, `output.publish`, `output.activity`,
`transcript.persist`, `exit.observe` and transcript incident handling. Use the
injected monotonic clock. Reuse the runtime's phase-observation pattern; a small
optional typed diagnostic callback on the output observation overload can expose
its inner phases to the pump without an infrastructure dependency in Application.
Existing callers use the unchanged overload. Do not add another runtime wrapper
or log every output chunk.

Include the bounded snapshot in the existing high-water/end records. Emit at
most one slow-observer warning per connection per ten seconds when an active
phase exceeds one second, including a still-running phase, not only phases that
eventually finish. Use the existing pump supervision cadence to inspect it.
These are diagnostic thresholds, not timeouts that cancel business operations.
Record runner credit-wait state and current bounded subscriber pressure as
metadata, so the next report can distinguish desktop consumption from a sender
burst. Existing ten-second release-rate evidence remains available.

The controlled tests must identify their deliberately held dependency. On later
ordinary activation, compare ingress/release rates, oldest age and phase maxima
through a normal workload and a naturally occurring land interval. Do not run a
production load generator. If healthy-observer drain cannot exceed ingress, this
plan has not established sustained throughput: use the measured phase for a
separate targeted repair, not another buffer increase or timeout change.

### D-5. CARD-1524 distinguishes gauges, totals and trigger snapshots

Rename internal cumulative members to `ReceivedEvents` and
`ReceivedPayloadBytes` (`long`). They count current-epoch event frames admitted
to this connection, including later ignored/failed observations, and never
decrement. They do not include rejected-over-limit or stale-epoch frames. Rename
the end-log field `LiveBufferEvents` to `ReceivedEvents` and label it “received
total”; add the cumulative byte field with explicit units. Update the three
`PhoneHomeStrandedQueueTests` sites that use the old name as an arrival witness.

Public `pendingEvents`/`pendingEventBytes` remain **unreleased** backlog gauges,
including queued and active items. Correct the latter to UTF-8 payload JSON bytes
with a shared measurement helper for window sender/receiver, calculated before
admission and carried on the queued item. Do not reserialize independently in
`ReleaseEvent`. This fixes non-ASCII under-accounting; it is not an allocation/RSS
metric. Runner hub overhead accounting remains separately documented.
Use a lease-bearing queued item for frame, admitted size and enqueue timestamp.
Adapt `PhoneHomeRunnerClient.StreamEventsAsync` to unwrap/release that item in its
iterator `finally` (including enumeration cancellation); the hosted recovery pump
remains the production owner. Neither observer may return the same credit twice.

Take one short-lock accounting snapshot for the paired pending/received values.
No I/O, JSON parsing, event observation or log sink call happens under that lock.
Capture other end fields once into locals and render both text and structured
properties from the same immutable end record. Preserve CARD-0996's conditional
`WsError`/`SocketError`, reason, correlation and Warning behavior. This guarantees
consistent representations of an observation, not an atomic snapshot of every
subsystem in the process.

At a hard-limit refusal retain a separate immutable trigger snapshot (including
incoming event size and limiting dimension) before throwing. The end record can
then distinguish `overflowAt` from `pendingAtEnd`, since draining may continue.
High water still means max(pending count/count cap, pending UTF-8 bytes/byte cap)
and still warns once at 50% and once at 90% per connection. Totals never select a
threshold. Preserve public status field names and document the corrected byte
unit; avoid a second “buffer” gauge with a competing meaning.

### D-6. Finite-stall guarantee and sustained-overload boundary

The acceptance envelope is explicit: every event fits the negotiated byte window;
server-held events stay within that window; additional events fit the runner's
existing subscriber bounds; the held observer is eventually released. Under
these conditions no overflow close, eligibility flap or event omission is
allowed, and the original events must drain in session order. This covers a
finite stall even while a rapid sender would formerly exceed the desktop cap.

There is no promise to retain infinite output during a permanent stall. Sustained
overload that also fills the runner subscriber takes the existing bounded hub
overflow, close and reconnect/catch-up path. Preserve its explicit evidence,
typed in-flight request failure and recovery barrier; never acknowledge receipt
as successful prompt delivery. Replayed durable transcript UUIDs persist once.
Transient output/incident events beyond both capacities are not made durable by
this design; the overload must be visible, never silently called lossless or
ready. Persisted runner transcripts remain the recovery authority. Do not claim
all transient exit/fault events can be reconstructed from a transcript.

The plan changes transport waiting for credit, not an agent session waiting for
user input. It adds no session-release timer, park action or kill. CARD-1083
continues to apply: with parking disabled, an input-waiting session keeps its
seat and has no automatic release deadline.

### D-7. Compatibility and activation

Use existing registration, ticket, status and connection-end machinery. Deploy
the reviewed server and runner changes through their existing owners; capability
negotiation permits either binary order. New-to-new enables the repair, and both
mixed-version directions retain their documented legacy bounded behavior.
Publication to master alone is not activation; product activation uses the
reviewed rc process and verifies loaded SHAs plus negotiated event-window mode.
No restart or deployment is part of this Plan task.

Read `GET /api/runner-defaults` and `GET /api/session-runners` again at dispatch.
At 2026-10-22 23:42 UTC the defaults response was revision 2 and an eligible Linux
lane was available; Windows was also available. These are observations, not a
placement pin. All checkpoints below are Linux-compatible managed loopback/DB
tests; omit `-Runner` and `-Platform` on an unconstrained dispatch. Do not embed a
fleet location. There is no native Windows, real provider or PTY acceptance row.

## Implementation slices

Each slice is committed and pushed on its assigned task branch. S1 must precede
production changes, preserving the original red receipts. Do not merge the
target, amend pushed commits or edit source while a checkpoint is running.

| Slice | Files and concrete change | Tests / evidence |
|---|---|---|
| S1 — red witnesses and owned fixtures | Add `tests/Antiphon.Tests/Application/PhoneHomeBackpressureTests.cs` and `TestHelpers/PhoneHomeHostedRunner.cs`; extend `PhoneHomeTestHost` only through opt-in service/receive controls. Add an `IEventBus` decorator with entered/release signals and a test-owned slow activity/persistence seam. Reuse real `PhoneHomeConnectionService`, event hub, dispatcher, Kestrel connect endpoint, `PhoneHomeRecoveryPump`, runtime and isolated DB. No product repair here. | CP-1–CP-5. Test names below compile against the base: inspect optional new JSON/log fields as data instead of referencing not-yet-added production types. A missing contract field is an assertion failure, never a compiler red. |
| S2 — accounting and attribution | `server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs`, `PhoneHomeRunnerClient.cs`, new adjacent `PhoneHomeEventDiagnostics.cs`, `server/Api/Endpoints/SessionRunnerEndpoints.cs`, `server/Application/Services/AgentSessionRuntime.cs`, `server/Application/Dtos/SessionObservationPhase.cs` (new diagnostic callback data), `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs` (shared payload measure), three old counter-reference sites in `tests/Antiphon.Tests/Application/PhoneHomeStrandedQueueTests.cs`. Snapshot counters, correct units, rename totals and instrument phases. | CARD-1524/attribution tests, exercised with the final green group. No change to abort reason policy. |
| S3 — negotiated producer backpressure | `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`, `SessionRunnerContracts.cs`, `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs`, `PhoneHomeLiveConnection.cs`, `src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs`, `PhoneHomeRuntimeAdapter.cs`, new adjacent `PhoneHomeEventWindow.cs`. Advertise support in the phone-home registration capabilities, bind it to tickets and implement bounded cumulative credit handling. Reuse the writer and subscriber lease. | Finite-stall and window/compatibility tests. The producer under test is the actual service, not a test peer implementing the proposed algorithm. |
| S4 — fair bounded observation | `server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs`, new adjacent `PhoneHomeEventScheduler.cs`, `server/Application/Settings/PhoneHomeRunnerSettings.cs` (including its `PhoneHomeRunnerSettingsRules`). Four fixed workers, per-session FIFO, aggregate accounting, join on stop/replacement. Existing `PhoneHomeRunnerSettingsValidator` delegates to those rules; no new validator or registration is needed. | Cross-session progress, same-session order, ownership/generation, failure and cancellation assertions. |
| S5 — contract documentation | `docs/ops-http.md` phone-home section and `docs/session-runtime-invariants.md` phone-home ingestion paragraph. Explain negotiated versus legacy mode, finite capacity, byte units, received totals, snapshot timing and sustained recovery. | Static review against D-1–D-7; no prose-mirroring test. Run the complete narrow green group at the committed S2–S5 tip. |

## Verification outline for the separate TestDesign stage

TestDesign owns the final guard-to-test/positive-control mapping, deterministic
fixture latches, importer validation and any necessary correction to this closed
manifest. It must add the executable `## Verification design` before handing to
Code. No tests or builds have been run by this Plan dispatch. Do not treat the
following expected reds as measured results.

### Hosted red witness and recipients

`Finite_stall_preserves_connection_order_and_delivery` uses the real runner
service connected to `PhoneHomeTestHost` on an ephemeral loopback address. Use a
separate test DB schema and the real runtime observation path. The runner runtime
event hub is the event producer; a test command runtime supplies deterministic
List/transcript answers without starting an agent process. Register/start the
actual recovery pump as a hosted service, not a manual `ReleaseEvent` drain.
Reuse CARD-0996's endpoint-completion witness when checking the original close.

Set desktop count capacity to 4 and runner subscriber capacity to 8, with ample
byte capacity for the first scenario. Complete catch-up and witness eligibility.
Hold the first session's `AgentTextDelta` publication at the I/O seam, with an
explicit `Entered` latch. Publish four same-session events and witness desktop
pending=4 (including the held observer); then publish four more through the
runner hub. The runner must not overflow its own eight-item capacity. In current
code the fifth desktop event throws `phone_home_event_overflow`, the endpoint
ends, eligibility withdraws and an in-flight List fails. The red assertion is
that this finite burst **keeps the connection open and eligible**; retain the
completed endpoint reason as corroborating evidence of why it failed.
For that in-flight RPC oracle, hold only the post-catch-up List response at a
test latch after witnessing receipt; an immediately answered List cannot prove
the disconnect failure. On the green path release this response while output
observation and event credit remain held. Do not hold the initial catch-up List.

With the repair, the second group waits at the real sender's credit gate, the
desktop remains at its cap, and a heartbeat plus a List RPC complete while the
observer remains held. Release the observer; all eight events must drain, with
zero pending count/bytes on both sides and no residual credit waiter. Include
ordered output, a split mention token, a UserPrompt with a nonce/UUID, TurnEnd and
a generation-bearing exit in the event roster (the first four can be output).
Assert exact original output sequence/text at the event-bus recipient, exactly
one stored nonce/UUID, one routed mention at the test recipient, and the matching
exit disposition. Publication invocation is not an actual browser receipt;
stored UserPrompt is the delivery evidence, not an RPC acknowledgement.

Run a second bounded scenario inside that method for byte credit with non-ASCII
JSON, chosen so count fits and UTF-8 bytes bind. Do not generate megabytes or
8,193 real events merely to test the default constant. Record the known payload
sizes and the byte-accounting identity. No timer guessing, arbitrary sleeps,
retry attributes or live CPU saturation is an acceptable substitute for latches.
Failure cleanup releases test holds, cancels and awaits every owned service,
worker, socket and request; teardown must not strand the deliberately held task.

### Remaining narrow methods

All five new red/green methods live in `PhoneHomeBackpressureTests` and have one
TUnit result each; internal scenario rows do not multiply the result count.

| ID / method | Decisive oracle and original-code red |
|---|---|
| V-1 `Finite_stall_preserves_connection_order_and_delivery` | Hosted witness above; current code closes with desktop event overflow. Green proves the actual sender waits, RPC/heartbeat responsiveness and eventual recipients, including catch-up while event credit is exhausted. |
| V-2 `Slow_session_does_not_block_another_session` | Hold session A's first output. With fewer events than the desktop window, session B's transcript commits and matching exit is observed before releasing A; A's later transcript/exit remain behind A's output. Baseline cannot commit B until A releases. Use a named observation barrier and bounded assertion wait after established fixture readiness. Also prove the worker ceiling, FIFO and no concurrent observers for one session; a foreign owner/stale epoch cannot reach runtime. |
| V-3 `End_snapshot_distinguishes_pending_from_received` | Enqueue/release repeatedly past the lifetime count cap with at most one pending event; totals rise, pending returns to zero, no false high-water warning. Fill to exact 50%/90%, once each; close while draining. Text and structured count/byte/total fields match the same snapshot and original `live buffer` key/label is absent. Baseline fails the renamed accurate diagnostic contract. Include UTF-8 size, nonnegative accounting and overflow-trigger versus end-state assertions. |
| V-4 `Slow_observer_is_attributed_without_payload` | Hold output publication, then separately a test activity/persistence dependency; injected clock advances beyond one second and ten-second throttling boundaries. Existing high-water/slow records must identify the held phase, session and event kind, with fixed-size mix/age evidence and no nonce/text/token. Baseline has no such phase attribution. Prove a still-held phase is reported and warnings are bounded. |
| V-5 `Window_limits_epoch_and_legacy_peers` | Exercise registration/ticket binding and actual runner service: new/new windows, absent capability and absent response field, stale/duplicate/invalid cumulative updates, byte-only exhaustion, one event larger than the window, release during credit-update send, cancellation and reconnect resetting credit. Baseline lacks negotiation. Each branch has a substantive state/send-count or typed refusal assertion. TestDesign must map added guard branches to method-scoped positive controls; a baseline missing-field failure alone is insufficient guard coverage. |
| R-1 `Sustained_overload_recovers_durable_transcript_once` | On the repaired real sender, hold observation, fill both explicitly small budgets and exceed the runner budget by one. Assert runner `loop=events overflow=true`, bounded reservations and explicit end; fail pending RPCs with existing semantics. Reconnect, supply retained transcript from the test producer's durable transcript store, and assert the UUID once and eligibility only after catch-up. This is regression/limit evidence, not a claim of lossless transient-output recovery. |
| R-2 existing CARD-0729/CARD-0996 methods | The exact methods in CP-12–CP-14 retain unknown-runner isolation, exception-derived transport fields and cancellation-without-exception semantics while `LogEnded` changes. |
| R-3 existing `Catchup_commits_before_live_release` | CP-15 preserves the established catch-up barrier while the scheduler changes. |

The five tests must compile and reach the stated assertions before production
changes. Build/setup errors, missing tests, a runner-side overflow in V-1, or
teardown timeouts are not valid red witnesses. V-2's deadline is a business
progress assertion after both sessions and the hold are witnessed, not a fixture
startup timeout. Retain the actual failure assertion and counts per method.
R-1 must run on the repaired path; preserve old tests of legacy hard overflow.

### Checkpoints

This is the closed proposed ordinary execution list for TestDesign to finalize.
All rows use the Linux managed loopback lane; DB rows use isolated schemas. Each
filter names exactly one method (CP-13's trailing wildcard selects its two
argument cases). One red build and one green build are reused explicitly. Run
each row once; no full assembly, broad class, repeated endurance or native lane.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1523-red/` | linux-hosted-red | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Finite_stall_preserves_connection_order_and_delivery` | V-1 | Build succeeds; 1 expected product-assertion failure with desktop overflow evidence | 1 | 8 | true |
| CP-2 | S1 | CP-1 | linux-hosted-red | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Slow_session_does_not_block_another_session` | V-2 | 1 expected cross-session progress failure | 1 | 1 | true |
| CP-3 | S1 | CP-1 | linux-hosted-red | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/End_snapshot_distinguishes_pending_from_received` | V-3 | 1 expected diagnostic contract failure | 1 | 1 | true |
| CP-4 | S1 | CP-1 | linux-hosted-red | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Slow_observer_is_attributed_without_payload` | V-4 | 1 expected missing-attribution failure | 1 | 1 | true |
| CP-5 | S1 | CP-1 | linux-hosted-red | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Window_limits_epoch_and_legacy_peers` | V-5 | 1 expected negotiation assertion failure | 1 | 1 | true |
| CP-6 | S2-S5 | `tests/Antiphon.Tests -> bin-c1523-green/` | linux-hosted-green | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Finite_stall_preserves_connection_order_and_delivery` | V-1 | 1 passed; 0 failed/skipped | 1 | 8 | true |
| CP-7 | S2-S5 | CP-6 | linux-hosted-green | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Slow_session_does_not_block_another_session` | V-2 | 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-8 | S2-S5 | CP-6 | linux-hosted-green | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/End_snapshot_distinguishes_pending_from_received` | V-3 | 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-9 | S2-S5 | CP-6 | linux-hosted-green | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Slow_observer_is_attributed_without_payload` | V-4 | 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-10 | S2-S5 | CP-6 | linux-hosted-green | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Window_limits_epoch_and_legacy_peers` | V-5 | 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-11 | S2-S5 | CP-6 | linux-hosted-recovery | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/Sustained_overload_recovers_durable_transcript_once` | R-1 | 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-12 | S2-S5 | CP-6 | linux-hosted-status | `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/Unknown_runner_status_is_404_and_carries_no_live_runner_identity` | R-2 | 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-13 | S2-S5 | CP-6 | linux-hosted-end-log | `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/Accept_and_end_lines_carry_the_connection_id_and_transport_codes*` | R-2 | 2 argument cases passed; 0 failed/skipped | 2 | 1 | true |
| CP-14 | S2-S5 | CP-6 | linux-hosted-end-log | `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/Request_cancellation_logs_transport_abort_without_transport_codes` | R-2 | 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-15 | S2-S5 | CP-6 | linux-hosted-catchup | `/*/Antiphon.Tests.Application/PhoneHomeEventPumpTests/Catchup_commits_before_live_release` | R-3 | 1 passed; 0 failed/skipped | 1 | 1 | true |

Expected Code totals: five deliberate red results, then eleven green results in
ten green rows. Estimated ordinary verification floor: 12 minutes red + 17
minutes green = 29 minutes, plus about three minutes checkpoint-tool bootstrap
and actual slot waits. Review uses the ten green rows at the final source SHA;
it does not rerun the historical red build. These are estimates, not receipts.
Implementation is larger than one small Code slice: budget roughly 60–90 minutes
for fixtures/accounting and 90–150 for the protocol/scheduler, plus verification.
Split execution into sequential commissioned slices if the dispatch budget is
shorter; never omit the red or green group to meet a time limit.

### Execution and evidence contract

Use the checkpoint tool's `run --plan <this path> --after S1` and, at the final
production tip, `run --plan <this path> --after S2-S5`, each with
`--expected-source-sha <full committed SHA> --max-wait 50s`. If bootstrapping it,
build `tools/Antiphon.Checkpoints` through `scripts/build-slot.ps1` into a unique
`bin-c1523-tool/` path, then invoke that DLL; do not launch an ungated implicit
build. Import/coverage are static checks. TestDesign must verify the importer and
runner keep all five deliberate red rows even after an earlier expected test
failure; the red run's nonzero exit remains truthful evidence, not a green
certificate. On exit 75, use `wait --run <id> --max-wait 50s` until terminal and
join all work before reporting. No `dotnet test`, bypassed slot or second driver.

Preserve unedited CHECKPOINT lines, full source SHA, exact roster/counts, native
TRX and source/build provenance. Validate the green receipt for CP-6–CP-15; do
not ask the green validator to certify deliberate red. A slot timeout is not
run/blocked, never permission for an unleased retry. Diagnose only an observed
failure at the base before classifying it as inherited. Generated receipts/logs
stay ignored. Remove only owned alternate output directories when finished.
Code and Review run `scripts/check-evidence-diff.ps1` over base..HEAD.

TestDesign must give each new admission, ordering, accounting, credit and
compatibility guard a method-scoped positive-control target. SourceLanding
Mutation remains a separate commissioned post-land stage with external evidence
custody, not an excuse to mutate or push from a review snapshot. No positive
controls or importer execution were performed in this Plan task.

## Plan handoff

The incident premise is confirmed. The misleading-total premise is also
confirmed, with the correction that current high-water warnings already use
pending gauges. No operational tuning is required to finish this plan. Remaining
work is deterministic TestDesign of the selected bounded protocol and consumer
design, then Code, independent Review and owner-controlled landing/activation.
The historical slow dependency remains unproven and must be reported that way.

--- next stage ---
next: test-design
handoff: Finalize hosted red witnesses, credit/FIFO guard controls and the closed Linux checkpoint manifest for CARD-1523/1524; preserve both existing caps and original events. Add Verification design before Code. Verify real runner sender and hosted pump, mixed versions, sustained overload and diagnostic snapshot semantics.
artifact: docs/superpowers/plans/2026-10-22-card-1523-1524-phone-home-backpressure-plan.md
