# CARD-0679: phone-home launch loss is a reconnect split-brain

Date: 2026-09-24. Stage: Plan, with the verification design folded into this dispatch.
Task: `d5219854`. Source inspected: `0595e8e93b45299fdbac16865f18ef5b9e0e7011`.
Investigation: [2026-09-24-card-0679-phone-home-launch-loss.md](../../investigations/2026-09-24-card-0679-phone-home-launch-loss.md).
Next stage: **Code**. Six bounded Code rounds in the order below; each round runs its own two
checkpoint rows and stays under the operator's ~3 minute verification cap.

## Outcome and scope

A phone-home WebSocket drop must never turn an acknowledged launch into a Failed desktop row
with a live orphan on the runner, and a delegation brief queued on a phone-home session must be
retried by the same watchdog that retries it for a local session. Every reconnect must leave a
log line that names the side that closed, the reason and the pending-event counts, on both the
desktop and the runner, so the ~230 reconnects an hour seen on 2026-09-23 can be attributed
rather than inferred.

In scope: the desktop phone-home transport (`PhoneHomeLiveConnection`, `PhoneHomeRunnerDirectory`,
`PhoneHomeRecoveryPump`, `SessionRunnerEndpoints` connect route, `AgentProtocolAdapterFactory`),
the interactive launch path (`AgentSessionService.LaunchInteractiveAsync` /
`LaunchInteractiveProcessAsync`), `AgentSessionRuntime.ListLiveSessions`, the CARD-0653 slot
release intents, and the runner's `PhoneHomeConnectionService` / `PhoneHomeCommandDispatcher`
logging and launch admission.

Out of scope: the runner answering 404 for sessions it reported live (§Open of the investigation,
separate card), the local runner's launch path, Herdr sessions, the transcript format, any new
board state or card transition, and a live server2 canary (Review may commission one; the rounds
here are desktop and runner unit/integration tests only).

Deployment notes the Code rounds must carry:

- Rounds R2 and R5 change the **runner** binary. They reach server2 only through the runner image
  redeploy; the desktop rounds land independently and degrade gracefully against an older runner
  (see D-9). server2 tasks run on git 2.47.3 after the pending redeploy; nothing in this plan
  depends on a git feature, but the redeploy is the moment the runner-side rounds go live.
- The operator caps verification at ~3 minutes per round. Every round below is two checkpoint
  rows of ~1.5 minutes each; no round runs a namespace or the full assembly.

## Ground truth

| Card / investigation assumption | What the inspected code does | Design consequence |
|---|---|---|
| The Launch frames for e3c147bb and e34a1fcd were lost. | `PhoneHomeRunnerClient.StartAsync` returned the echoed generation; both failures are in `WaitForReadyAsync` -> `RunnerTerminalSession.WaitForQuietAfterVisibleAsync` -> `GetBufferAsync` (`AgentSessionService.cs:459`, after `adapter.StartAsync`). | Post-ack losses need re-attach, not re-launch (D-8). |
| The runner's docker log has no launch entry, so the frame never arrived. | `PhoneHomeCommandDispatcher.LaunchAsync` logs nothing; `SessionRunnerRuntime.StartAsync` logs only when it tails a transcript. | Add a runner-side launch/kill log line (D-4). |
| The cancellation comes from a launch timeout or a server restart. | `AgentSessionLaunchQueue.cs:144` passes `CancellationToken.None`; `RequestTimeoutFor` throws a typed `RequestTimeout`; the only `TrySetCanceled` is `PhoneHomeLiveConnection.DisposeAsync`, called from the connect route's `finally` and from `AcceptConnect`. | Replace the cancellation with a typed `ConnectionClosed` transport failure (D-5). |
| A remote adapter follows the runner across reconnects. | `AgentProtocolAdapterFactory.Create(kind, runnerId)` calls `_directory.Resolve(runnerId)` once; `PhoneHomeRunnerDirectory.Resolve` returns `new PhoneHomeRunnerClient(live)` bound to one `PhoneHomeLiveConnection`; `RunnerTerminalSession` holds that client for its life. `RoutingSessionRunnerClient.Route` resolves per call, but only by session binding. | Give remote adapters a runner-scoped client that resolves on every call (D-6). |
| The clean-up kill reaches the runner. | `KillAndDisposeAsync` -> `RunnerTerminalSession.KillGenerationAsync` -> the pinned client -> `RequestAsync` throws `InvalidOperationException("... not dispatch-eligible")` at the gate (`PhoneHomeLiveConnection.cs:86-89`); the catch logs and swallows. | D-6 fixes the live-B case; D-7 defers the kill when no eligible connection exists. |
| A launch failure is classified before it is made terminal. | `LaunchInteractiveAsync` marks every exception Failed with `FailureReason = ex.Message`; the only special cases are `ResumeTargetMissingException` and `AgentLaunchBlockedException`. `RestartFailurePolicy.Classify` knows nothing about `PhoneHomeTransportException`. | Add a transport-loss classification and a bounded retry loop inside the launch (D-8). |
| The stranded-queue watchdog serves every idle session with a pending brief. | `FlushStrandedQueuesAsync` (`SessionMessageQueueService.cs:1295`) filters candidates by `_runtime.ListLiveSessions()`, which is `RoutingSessionRunnerClient.ListAsync` = `_directory.Local.ListAsync`. 19 other callers use the same method (see D-10). | Add a cached remote inventory to `ListLiveSessions` (D-10). |
| The event pump keeps running for the life of a connection. | `RunCycleAsync` starts `_ = PumpEventsAsync(live, ct)` once and returns early while `_recovered == live`; an exception thrown by `ObserveOutputAsync` / `ObserveTranscriptAsync` ends the pump; nothing restarts it and nothing releases later events. | Per-event fault isolation and pump supervision (D-2). |
| The pump's cost per event is small. | Per event: `OwnerMatchesAsync` opens a DI scope and runs a `GetBindingAsync` query; `ObserveOutputAsync` then calls `RecordActivityAsync`, which opens another scope, runs two queries and a `SaveChangesAsync`. Three round trips per output chunk. | Owner cache per connection, coalesced activity writes, larger event cap (D-3). |
| The disconnect reason is recorded somewhere. | `PhoneHomeRunnerDirectory.Disconnect(connection, reason)` ignores `reason`; the connect route's `finally` calls `Disconnect(connection, "closed")` after the overflow catch; `Status()` reports `DisconnectReason: "unavailable"` whenever the runner is not available. | Record and report the first reason per connection with counts (D-1). |
| The runner logs when its connection ends. | `PhoneHomeConnectionService.RunConnectionAsync` returns after `Task.WhenAny(receive, heartbeat, events)`; the outer loop logs only when an exception escapes, which is only the runner-side overflow. | Log the ending loop, its fault and counts (D-4). |
| Resolve refuses a closed socket (card ask 2). | `Resolve` and `SnapshotLive` check only `DispatchEligible` and the lease; `DeclaredCapacity` and `Status` check `SocketOpen`. | `Resolve` requires `SocketOpen` (D-6). |
| A duplicate Launch is refused by the runner in a typed way. | `SessionRunnerRuntime.StartCoreAsync` throws `InvalidOperationException("Session '...' is already running.")`; over phone-home that becomes a `runner_internal_error` frame. The custody path already returns the existing session when the binding is equal. | Idempotent same-generation Launch, typed 409 otherwise (D-9). |
| The CARD-0340 attach path covers phone-home. | `ResumeInterruptedLaunchAsync` exists and attaches through `IAttachableProtocolAdapter` (`RunnerClaudeAdapter`, `RunnerRawAdapter`, ...), but `SessionReconciliationService` drives `ResumeInterruptedLaunchesAsync` only for the local owner (`owner is null`). | Reuse the attach primitive inside the launch loop (D-8); do not widen the restart reconciler. |
| A pending slot-release intent kills the seat when the runner returns. | `RunnerSlotService.ReconcilePendingReleasesAsync` audits only: "It never kills". | Add a generation-conditional kill arm to the same intent kind and job (D-7). |
| The delegation watchdogs give a launch time to recover. | `DeadSessionFailGraceMinutes` = 3 after a session reads Failed/Stopped; the delivery watchdog fails a never-attempted brief at about 10 minutes from dispatch. A Starting row is not dead. | Retry budget must finish well inside 10 minutes and keep the row Starting (D-8). |

## Decisions

Numbered D-1 through D-12. Each is an implementation decision within the brief, written under
stated defaults; the Code rounds do not need a further approval to proceed.

### D-1. Every desktop connection end is logged once, with a classified reason and counts

`PhoneHomeLiveConnection` gains `StartedAtUtc`, `LastDisconnectReason`, and read-only counters
that already exist as fields (`PendingEvents`, `PendingEventBytes` (new accessor),
`LiveBufferEvents`, `LiveBufferBytes`, `InFlight`). The connect route in
`SessionRunnerEndpoints` becomes:

- log `Information` on accept: runner id, epoch, capacity, platform, build version;
- run the receive loop inside one `try` whose catches classify the end:
  `event_overflow` / `message_too_large` (the existing `PhoneHomeTransportException` codes),
  `close_received` (a null frame), `request_aborted` (`OperationCanceledException` with
  `RequestAborted` set), `transport_abort` (`WebSocketException`), `receive_fault:<TypeName>`
  (anything else);
- call `directory.Disconnect(connection, reason)` **once** from the `finally` with that reason
  (the current catch-then-finally pair calls it twice and the second call would win if the
  directory ever recorded it);
- log one `Warning` line: runner id, epoch, reason, lifetime, socket state at close,
  `PendingEvents`, `PendingEventBytes`, `LiveBufferEvents`, `InFlight`, and the number of waiters
  the dispose is about to fail;
- rethrow only `receive_fault`; a `transport_abort` is a Warning with a reason, not an
  `ExceptionMiddleware` Error (those 95 pre-15:54 log entries become one line each with counts).

`PhoneHomeRunnerDirectory.Disconnect(connection, reason)` records `(reason, at, epoch)` as
`LastDisconnect`, first writer wins per connection, and increments a process-lifetime
`Reconnects` counter on each `AcceptConnect`. `Status()` returns the recorded reason in
`DisconnectReason` instead of the constant `"unavailable"`, and `PhoneHomeRunnerStatusDto` gains
`PendingEvents`, `PendingEventBytes`, `LastDisconnectAtUtc`, `Reconnects`, `LastCatchUpMs`. The
`/api/session-runners/{runnerId}/status` row in `docs/ops-http.md` lists the new fields.

`ReceiveLoopAsync` logs a `Warning` high-water line once per connection when pending events first
pass 50% and again at 90% of `MaxPendingEvents` (or the byte cap), naming the counts and the
pump's events-per-second over the last 10 s. That is the "observable before it closes" evidence
the brief asks for; the overflow throw itself stays.

Rejected: a new alert kind (the brief's observability question is answered by log lines and the
status DTO, and an alert per reconnect at 230/hour would be noise); a DB row per reconnect (same
reason; the log is the record and the counter is the aggregate).

### D-2. The event pump is fault-isolated per event and supervised per connection

`PhoneHomeRecoveryPump.PumpEventsAsync` wraps the per-event body in `try/catch (Exception ex)
when (ex is not OperationCanceledException)`: log a `Warning` with the event name, session id,
epoch and a per-connection `EventFailures` count, then continue; the `finally` still calls
`ReleaseEvent(size)`. `RunCycleAsync` stores the pump task in `_pump` and, on the `_recovered ==
live` path, checks `_pump.IsCompleted`: a completed pump while `live.SocketOpen` is logged as
`Error` ("pump ended while the connection is live") and restarted. The pump also ends cleanly when
`live.Events` completes (the connection was disposed), which is not an error.

Rejected: restarting the whole recovery cycle (catch-up would re-run List and transcripts, adding
load to the same reconnect storm); a bounded retry of the failing event (a poisoned event would
block every later one; the event is already persisted by the runner's transcript, and catch-up on
the next connection re-reads it).

### D-3. The pump's database work is cached and coalesced, and the event cap is raised

Three independent reductions, each measured by the D-1 counters after landing:

1. **Owner cache per connection.** `PhoneHomeRecoveryPump` keeps, per live connection, a
   `ConcurrentDictionary<Guid, (bool Owned, DateTimeOffset At)>`. A positive match is cached for
   the connection's lifetime (a session's `RunnerId`/`RunnerStoreId` binding is persisted before
   Launch and never changes; `PhoneHomeSessionRoutingTests.Restart_and_pin_change_keep_persisted_owner`).
   A negative match is cached for `PhoneHomeRunner:OwnerCacheNegativeSeconds` (default 5) so a row
   committed a moment after its first event is still picked up. Catch-up seeds the cache from the
   List it already runs. `PhoneHomeRunnerDirectory` exposes an internal `BindingLookups` counter
   for the test.
2. **Coalesced activity writes.** `AgentSessionRuntime.RecordActivityAsync` writes `LastSeenAt`
   and `RunAttempt.LastEventAt` at most once per session per
   `AgentSession:ActivityWriteMinIntervalMs` (default 1000, `0` disables the throttle), keyed by
   session id, on the runtime's `TimeProvider`. This applies to local sessions too; it is a pure
   reduction of identical writes, and every stall/idle rule in the codebase measures in seconds
   or minutes.
3. **Larger event cap.** `PhoneHomeProtocol.DefaultMaxPendingEvents` becomes 8192. The byte cap
   (16 MiB) is the real memory bound and stays. Both sides read the same constant, so the runner's
   bounded subscription rises with it; `PhoneHomeLimits.Validate` is unchanged.

Rejected: starting the pump before catch-up completes (would break the catch-up-before-live
ordering that `Catchup_commits_before_live_release` pins, and transcript events must not land
before the transcript they belong to); batching SignalR publishes (they are in-memory and cheap;
the cost is the DB round trips); a separate persistence queue with its own worker (a second buffer
with its own overflow semantics, when the existing channel already is the buffer).

### D-4. The runner logs its connection ends, launches and kills

`PhoneHomeConnectionService.RunConnectionAsync` logs `Information` on connect (epoch) and, after
`WhenAny`, one `Information` line naming which loop completed (`receive` / `heartbeat` / `events`),
its exception type and message when faulted, `overflow`, the subscriber's `PendingEvents`,
`InFlight`, and the connection lifetime. The outer `ExecuteAsync` keeps its Warning for escaped
exceptions. `PhoneHomeCommandDispatcher` logs `Information` for each `Launch` (session id, exe
basename, `AcceptedStartedAt`, outcome or refusal code) and each `KillGeneration` / `ReleaseSlot`
(session id, expected generation, outcome). No prompt text, path or credential is logged.

Rejected: logging at Debug (the whole point is that the default runner log answers §Open without a
redeploy to raise levels).

### D-5. A disconnect fails in-flight requests with a typed transport error

New constant `PhoneHomeProblemTypes.ConnectionClosed = "phone_home_connection_closed"`.
`PhoneHomeLiveConnection.DisposeAsync(string reason)` (the parameterless form delegates with
`"dispose"`) fails each waiter with
`PhoneHomeTransportException(ConnectionClosed, $"Phone-home connection to {RunnerId} (epoch {Epoch}) closed while {operation} was in flight: {reason}")`.
The waiter map therefore also keeps the operation per request id. `RequestAsync` throws the same
typed error when the socket is not `Open` before sending, and wraps `WebSocketException` /
`ObjectDisposedException` from `WriteFrameAsync` in it (the e34a1fcd `CloseSent` shape). The
caller's own token still surfaces as `OperationCanceledException`.

Consumers that change behaviour without edits: `PhoneHomeRecoveryPump.CatchUpAsync` (its
`catch when not OperationCanceledException` now catches the typed failure and schedules the retry
instead of faulting the cycle), `PhoneHomeRunnerDirectory.GetInventoryAsync` (returns
`Unavailable(reason)` instead of propagating a cancellation into the reconciliation scan). Explicit
edits: `SessionMessageQueueService.IsHerdrUnreachable` adds
`PhoneHomeTransportException { Code: ConnectionClosed }`; `RestartFailurePolicy.Classify` adds
`PhoneHomeTransportException` and `ServiceUnavailableException { Code: phone_home_unavailable }`
to the `Infrastructure` arm (a lost socket is pacing evidence, never permission to replace a
conversation). `RequestTimeout` is deliberately **not** added to the unreachable predicate: a
timeout means the runner may have acted.

Rejected: `TrySetException(new OperationCanceledException(...))` subclasses (every `catch
(OperationCanceledException)` in the codebase would keep treating a transport loss as a caller
cancellation).

### D-6. Remote adapters route every call to the current live connection; Resolve requires an open socket

New `RunnerScopedSessionRunnerClient(ISessionRunnerDirectory directory, string runnerId)` in
`server/Infrastructure/Agents/SessionRunner/`: implements `ISessionRunnerClient` and
`IVerificationWorkspaceTransport` by calling `_directory.Resolve(runnerId)` on each call, exactly
as `RoutingSessionRunnerClient` does by binding. `AgentProtocolAdapterFactory.Create(kind,
runnerId)` hands adapters that client for a non-local runner id instead of the result of one
`Resolve`. Nothing else in the adapters changes: `RunnerTerminalSession` still holds one client;
the client is what became dynamic. `RemoteSpillCourier` stays attached through `Resolve` as today.

`PhoneHomeRunnerDirectory.Resolve` additionally throws `ServiceUnavailableException(Unavailable)`
when `!live.SocketOpen` (card ask 2). `SnapshotLive` is unchanged apart from D-1's recording; the
consumers that need "usable" already check `DispatchEligible` and now `SocketOpen` through
`Resolve`.

Rejected: routing by session binding (`RoutingSessionRunnerClient`) for adapters (a DB read per
call on the hot terminal path, and `StartAsync` is called before any binding-based route has a
session to look up); recreating the adapter on reconnect (the adapter's state, `StartedAt`,
generation and `Exited` task, must survive the reconnect; only the transport should change).

### D-7. A failed remote launch whose kill cannot be sent records a generation-conditional kill intent

`RunnerSlotService` gains a public `RecordDeferredKillAsync(db, runnerId, sessionId,
acceptedGeneration, reason)` that writes an `AgentIncident` of kind `RunnerSlotReleaseIntent` with
`FailureReason = "pending:kill-generation:<runnerId>:<generationTicks>"`. `ReconcilePendingReleasesAsync`
parses that marker and, when `Resolve(runnerId)` succeeds, calls `KillGenerationAsync(sessionId,
generation)`: `Killed` or `AlreadyExited` finishes the intent with an audit message naming the
outcome; `GenerationMismatch` marks it `failed:` (a replacement session with the same id is not
ours to kill); a transport/unavailable failure leaves it pending, as today. The existing
audit-only arm is unchanged.

`AgentSessionService.KillAndDisposeAsync` gains a remote arm: when the session has a `RunnerId`,
the kill threw a transport-class exception (D-5's `ConnectionClosed`, `ServiceUnavailableException`
with `phone_home_unavailable`, or the eligibility `InvalidOperationException`), and a generation is
known, it records the deferred kill instead of only logging. `PhoneHomeRecoveryPump.RunCycleAsync`
enqueues `RunnerSlotReconcileJob` through an optional `IBackgroundJobClient` right after
`MarkRecovered`, so the kill lands seconds after the reconnect rather than at the next 2-minute cron.

Rejected: an automatic orphan sweep (`ReleaseOrphansAsync`) on reconnect (it kills by desktop-state
inference; a generation-conditional kill of a session we ourselves failed is a decision already
taken, an orphan sweep is not); a new incident kind (the CARD-0653 kind, job, cron and endpoint
already carry intents through restarts).

### D-8. A transport loss during a remote launch re-attaches after the ack and re-queues before it, bounded

Inside `LaunchInteractiveProcessAsync`, for a session with a `RunnerId` only, the segment from
`adapter.StartAsync` through `WaitForReadyOrThrowAsync` runs in a loop bounded by
`PhoneHomeRunner:LaunchTransportRetries` (default 2 retries after the first attempt) and
`PhoneHomeRunner:LaunchReattachWaitSeconds` (default 90, the lease):

1. A transport-class exception (D-5 `ConnectionClosed`, `ServiceUnavailableException`
   `phone_home_unavailable`, the eligibility `InvalidOperationException`) before `StartAsync`
   returned is a **pre-ack** loss; after it returned it is a **post-ack** loss. Any other exception
   takes today's path.
2. On either loss: `await adapter.DisposeAsync()` (never a kill), log a Warning naming the phase,
   attempt number and runner, add an `AgentTaskEvent` Warning to the Dispatched task when one
   exists, then wait for `_directory.DeclaredCapacity(runnerId) is not null` polling every 500 ms
   up to `LaunchReattachWaitSeconds` on the service's `TimeProvider`. The session row stays
   `Starting` throughout, so the dead-session reconciler does not act.
3. Pre-ack: create a fresh adapter and call `StartAsync` again with the same spec and
   `AcceptedStartedAt` (the fence; D-9 makes the runner idempotent for it).
   Post-ack: create a fresh adapter and call `IAttachableProtocolAdapter.AttachAsync(sessionId)`,
   then continue at `WaitForReadyOrThrowAsync`. An adapter kind without attach support takes the
   exhausted path immediately.
4. The re-attach is permitted only while nothing has been typed: the loop covers exactly the
   Start/ready segment, which precedes every typing step. A loss later in the tail (notes,
   remote-control commands, boot flush) is not retried here; it takes the existing kill path, which
   D-6/D-7 now make effective.
5. Exhausted retries or an eligibility wait that times out throw
   `RemoteLaunchTransportLostException(phase, attempts, waited)`; `LaunchInteractiveAsync` marks
   the session Failed with a `FailureReason` naming the runner, phase, attempt count and wait
   (never "A task was canceled."), sets `RestartFailureKind.Infrastructure`, and the post-ack case
   records the D-7 deferred kill because the runner does hold the session.

Worst case with defaults: initial attempt + 2 retries × (90 s wait + a ready wait), which sits
inside the 10-minute delivery watchdog and keeps the 3-minute dead-session grace irrelevant
(the row is never Failed while retrying). The dispatcher's pool delegates pass no notes, no
remote-control name and no initial prompt, so the retried tail types nothing twice.

`AgentSessionService` takes an optional `ISessionRunnerDirectory? directory` constructor parameter
for the eligibility probe; when null (local-only worlds), the loop degrades to today's behaviour.

Rejected: retrying from `AgentSessionLaunchQueue` by re-calling `LaunchInteractiveAsync` (its catch
marks Failed before the queue sees the exception, and the queue does not know whether the ack
happened); keeping the pinned adapter and only swapping the socket (the adapter is not the problem
after D-6, but its `Exited` watcher and terminal state were bound to the dead request; a fresh
adapter attached to the runner's session is the CARD-0340 shape that already has tests); widening
`SessionReconciliationService.ResumeInterruptedLaunchesAsync` to remote owners (that path is for a
desktop restart and fails non-delegate rows loudly; the in-launch loop knows the phase).

### D-9. The runner answers a same-generation duplicate Launch with the existing session, and refuses a different generation with a typed 409

`PhoneHomeCommandDispatcher.LaunchAsync`, inside `MutateAsync`: if the runtime holds a live
session with the request's id and `SessionGeneration.Equal(existing.AcceptedStartedAt,
request.AcceptedStartedAt)`, return `Result(request, existing.ToDto())` and log the launch as
`duplicate-ack`; if the generation differs, throw
`PhoneHomeAdmissionException(PhoneHomeProblemTypes.SessionAlreadyRunning, ..., 409)` (new
constant `"phone_home_session_already_running"`). The runtime's `InvalidOperationException` path
remains for the local HTTP surface.

Against an older runner, the pre-ack retry's duplicate Launch still receives
`runner_internal_error`; the desktop treats any non-transport error on a retry as a real failure
(kill path, which D-6/D-7 make effective). No string matching on the detail text.

Rejected: a desktop-side `GetAsync` before every retried Launch to decide between attach and
launch (a second request on a fresh connection that can itself drop; the runner is the authority
on what it holds, and the idempotent ack makes the decision unnecessary).

### D-10. `ListLiveSessions` includes the recovered runner's cached inventory

`PhoneHomeLiveConnection` gains `KnownLiveSessions` (a concurrent set of session ids with runner
status Running/Starting): `PhoneHomeRecoveryPump.CatchUpAsync` replaces it from the List it runs;
`PhoneHomeRunnerClient.StartAsync` adds on a successful ack; `PumpEventsAsync` removes on
`SessionExited`; `KillGenerationAsync` / `KillAsync` / `ReleaseSlotAsync` remove on a killed
result. The pump refreshes it with a List every `PhoneHomeRunner:InventoryRefreshSeconds` (default
30) while `_recovered == live` and records the List latency as `LastCatchUpMs` for D-1.

`ISessionRunnerDirectory` gains `IReadOnlyCollection<Guid> LiveRemoteSessionIds()` with a default
implementation returning an empty set. `PhoneHomeRunnerDirectory` returns the set when
`SnapshotLive()` is dispatch-eligible with an open socket, else empty. `AgentSessionRuntime` takes
an optional `ISessionRunnerDirectory? directory` and `ListLiveSessions()` unions
`directory.LiveRemoteSessionIds()`. No RPC is added to the synchronous method.

Callers whose behaviour changes (all in the "treat as live" direction, the same as a local
session): `SessionMessageQueueService` (`FlushStrandedQueuesAsync`, `EnqueueAsync` deliver-if-idle,
`SendNowAsync`, the local-command poll, the Now-mode gate), `AgentChannelService.LoadLiveTargetSessionAsync`,
`WatchdogService.ScanAsync` (its `TryGetLiveSnapshot` already routes remote snapshots),
`SubscriptionUsageMonitorService`, `ContextCompactionService`, `ApiErrorRecoveryService`,
`PolicyRefreshService` (`notLive` becomes false for a live phone-home session, so no relaunch),
`ScheduleService`, `OrchestratorService`, `AgentSessionService:1396`, and the runtime's own
manual-turn wait. Code greps `ListLiveSessions(` and confirms none of them kills or relaunches on
"now live"; the plan's reading found none.

Rejected: an async `ListLiveSessionsAsync` used only by the stranded sweep (leaves the other 19
callers treating a live phone-home session as dead, including deliver-if-idle, which is the very
path that would have typed bbc4e2e6's brief); a List RPC inside the synchronous method (a 60 s
request timeout on a hot path, and the method is called from sync-over-async sites).

### D-11. Tests reproduce the incident shapes against the real transport

The red tests are the investigation's table, written against `PhoneHomeTestHost` +
`PhoneHomeScriptedPeer` (real Kestrel WebSocket, real directory, real endpoint) and, for the launch
loop, `BridgeQueueHarness` + the real `AgentProtocolAdapterFactory` bound to the host's directory,
the pattern `PhoneHomeEventPumpTests` already uses with a shared isolated schema. `AgentKind.Raw`
is the launch kind under test: `RunnerRawAdapter` implements `IAttachableProtocolAdapter` and its
ready wait accepts `ReadyGrace` with no visible output, so the scripted peer's default replies are
enough and no Claude readiness probe is simulated. A dropped connection is `peer.Socket.Abort()`
followed by a fresh `ConnectPeerAsync`. `PhoneHomeTestHost` gains a capturing `ILoggerProvider`
(`host.Logs`) so D-1 assertions read structured log entries, not console text.

### D-12. Docs

`docs/session-runtime-invariants.md` gains two bullets under the phone-home group: "a reconnect
fails in-flight requests with `phone_home_connection_closed`, never a cancellation, and a remote
adapter routes every call to the current connection"; "a transport loss during a remote launch
re-attaches after the ack and re-queues before it, bounded by `LaunchTransportRetries`; the
runner session is killed on reconnect through a generation-conditional intent when the launch is
finally failed". `docs/ops-http.md` updates the status row (D-1 fields) and the slots row (the
kill-generation intent arm). `docs/logs.md` gains one line naming the desktop and runner
disconnect lines to grep for. `AGENTS.md` is unchanged.

## Implementation rounds

Each round is one Code dispatch, one commit per slice, red tests committed before the production
change, then the two checkpoint rows. Order is fixed: R1 and R2 are the observability the brief
wants first; R3 removes the churn source the investigation names; R4 to R6 fix the launch loss.

| Round | Slice | Files | Tests (new methods) |
|---|---|---|---|
| R1 | S1: D-1 desktop disconnect reason, counts, status DTO, high-water warning | `server/Api/Endpoints/SessionRunnerEndpoints.cs`, `server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs`, `PhoneHomeRunnerDirectory.cs`, `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs` (`PhoneHomeRunnerStatusDto`), `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs` (capturing logger), `docs/ops-http.md` | `PhoneHomeConnectionTests`: V-1, V-2, V-3 |
| R2 | S2: D-4 runner connection-end/launch/kill logging; D-9 idempotent Launch + `SessionAlreadyRunning` | `src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs`, `PhoneHomeCommandDispatcher.cs`, `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs` (constant) | `PhoneHomeConnectionServiceTests`: V-4, V-5; `PhoneHomeCommandDispatcherTests`: V-6, V-7 |
| R3 | S3: D-2 pump supervision; D-3 owner cache, coalesced activity, event cap | `server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs`, `PhoneHomeRunnerDirectory.cs` (`BindingLookups`), `server/Application/Services/AgentSessionRuntime.cs` (`RecordActivityAsync`), `server/Application/Settings/AgentSessionSettings.cs`, `PhoneHomeRunnerSettings.cs`, `PhoneHomeContracts.cs` (`DefaultMaxPendingEvents`) | `PhoneHomeEventPumpTests`: V-8, V-9, V-10; `AgentSessionRuntimeActivityTests` (new): V-11 |
| R4 | S4: D-5 typed `ConnectionClosed`; D-6 `RunnerScopedSessionRunnerClient` + `Resolve` SocketOpen; D-7 deferred kill intent | `PhoneHomeLiveConnection.cs`, `PhoneHomeContracts.cs`, `RunnerScopedSessionRunnerClient.cs` (new), `server/Infrastructure/Agents/Pty/AgentProtocolAdapterFactory.cs`, `PhoneHomeRunnerDirectory.cs`, `server/Application/Services/RunnerSlotService.cs`, `AgentSessionService.cs` (`KillAndDisposeAsync`), `SessionMessageQueueService.cs` (`IsHerdrUnreachable`), `RestartFailurePolicy.cs`, `PhoneHomeRecoveryPump.cs` (job enqueue) | `PhoneHomeConnectionTests`: V-12, V-13; `PhoneHomeDirectoryTests`: V-14, V-15; `PhoneHomeDeferredKillTests` (new): V-16, V-17 |
| R5 | S5: D-8 launch transport loop | `AgentSessionService.cs` (`LaunchInteractiveProcessAsync`, `LaunchInteractiveAsync`, ctor), `PhoneHomeRunnerSettings.cs` (two settings), `server/Application/Exceptions/RemoteLaunchTransportLostException.cs` (new) | `PhoneHomeLaunchTransportTests` (new): V-18, V-19, V-20, V-21 |
| R6 | S6: D-10 inventory + `ListLiveSessions`; D-12 docs | `PhoneHomeLiveConnection.cs`, `PhoneHomeRecoveryPump.cs`, `PhoneHomeRunnerClient.cs`, `server/Application/Interfaces/ISessionRunnerDirectory.cs`, `PhoneHomeRunnerDirectory.cs`, `AgentSessionRuntime.cs`, `tests/Antiphon.Tests/TestHelpers/SingleRunnerDirectory.cs` (default member, no change needed unless it overrides), `docs/session-runtime-invariants.md`, `docs/logs.md`, `docs/ops-http.md` | `PhoneHomeStrandedQueueTests` (new): V-22, V-23, V-24 |

Slice commits: `S1`, `S2`, ... as in the table; red-test commits are `S1-tests`, etc. Every commit
message states the real checkpoint outcome.

## Verification design

### Harness and red-first discipline

- `PhoneHomeTestHost` (real Kestrel loopback, real `PhoneHomeRunnerDirectory`, real connect route)
  and `PhoneHomeScriptedPeer` are the transport harness. A drop is `peer.Socket.Abort()`; a
  reconnect is a new `ConnectPeerAsync()` followed by `host.Directory.MarkRecovered(live)` (or the
  real pump's `RunCycleAsync` where the test is about the pump).
- Launch-path tests use `BridgeQueueHarness.CreateAsync` with `ConfigureServices` registering the
  real `AgentProtocolAdapterFactory` constructed with `directory: host.Directory`, plus
  `host.Directory` as `ISessionRunnerDirectory`, on the same isolated schema as the host
  (`TestDbFixture.CreateIsolatedSchemaAsync`), as `PhoneHomeEventPumpTests` does. Sessions are
  seeded with `RunnerId = host.AllowedRunnerId`, `RunnerStoreId = host.StoreId`, `RunnerCwd = "/work"`,
  `AgentKind.Raw`, `Status = Starting`, and a Dispatched `AgentTask` pointing at them.
- Runner-side tests use the existing `PhoneHomeTestWebSocket`, `SessionRunnerEventHub` and
  `RecordingRuntime` in `tests/Antiphon.SessionRunner.Tests`, plus a recording `ILogger`.
- Red first: each round commits compiling tests that fail on the current code at the assertion
  the roster names (never a build or fixture failure), runs the red row, then implements and runs
  the green row. A test that is already green before the production change is demonstrated red by
  temporarily removing the guarded line, recorded as an unlisted control run with its reason, and
  the mutation restored before the green row.
- No wall-clock waits above 3 s in tests: eligibility waits and throttles run on
  `FakeTimeProvider` (`LaunchReattachWaitSeconds`, `ActivityWriteMinIntervalMs`); socket drops are
  observed by polling `live.SocketOpen` with a 2 s deadline as the existing tests do.
- Nothing here contacts server2, 17202-17205, Docker or a provider.

### Coverage roster and decisive assertions

One execution per method. The red mechanism names the assertion that fails on the current code.

| ID | Class.Method | Assertion / red mechanism |
|---|---|---|
| V-1 | PhoneHomeConnectionTests.Overflow_disconnect_logs_reason_epoch_and_pending_counts | Limits `MaxPendingEvents: 2`; emit 4 events before recovery; `host.Logs` has one Warning with runner id, epoch, `phone_home_event_overflow`, `PendingEvents=2`; `Status().DisconnectReason == "phone_home_event_overflow"`. Today: no such log entry, `DisconnectReason == "unavailable"`. |
| V-2 | PhoneHomeConnectionTests.Peer_abort_is_a_warning_with_transport_abort_not_a_middleware_error | `peer.Socket.Abort()`; exactly one Warning with reason `transport_abort` and lifetime; zero Error entries from `ExceptionMiddleware`. Today: an Error from the middleware and no reason. |
| V-3 | PhoneHomeConnectionTests.Pending_event_high_water_is_warned_before_overflow | Limits `MaxPendingEvents: 10`, no pump; emit 6 events; a Warning naming `50%` and the counts exists while `live.SocketOpen` is still true; emit to 9: a `90%` Warning; overflow only after the 11th. Today: silence until the socket closes. |
| V-4 | PhoneHomeConnectionServiceTests.Connection_end_logs_the_loop_that_ended_and_its_fault | `PhoneHomeTestWebSocket` fails the next heartbeat send; `RunConnectionAsync` returns; the recording logger has one Information entry naming `heartbeat`, the exception type, `overflow=false`, pending and in-flight counts, and the lifetime. Today: no entry. |
| V-5 | PhoneHomeConnectionServiceTests.Event_hub_overflow_end_names_the_events_loop_and_overflow_true | Bounded subscription overflow; the connection-end line says `overflow=true` and names `events`; the existing `PhoneHomeTransportException(EventOverflow)` is still thrown. Today: only the outer Warning without the loop. |
| V-6 | PhoneHomeCommandDispatcherTests.Duplicate_launch_with_the_same_generation_returns_the_existing_session | Dispatch Launch twice with equal `AcceptedStartedAt`; second reply is a Result whose `SessionId`/`AcceptedStartedAt` equal the first; the runtime holds one session; log has `duplicate-ack`. Today: an Error frame `runner_internal_error` ("already running"). |
| V-7 | PhoneHomeCommandDispatcherTests.Duplicate_launch_with_another_generation_is_a_typed_409 | Second Launch with a later generation; Error frame `StatusCode 409`, `ErrorCode == "phone_home_session_already_running"`; no second process. Today: `runner_internal_error`. |
| V-8 | PhoneHomeEventPumpTests.Pump_survives_a_failing_event_and_releases_the_rest | Runtime whose `ObserveOutputAsync` throws on the first event; feed 5 output events; all 4 later events are observed and `live.PendingEvents == 0`; `EventFailures == 1`. Today: pump ends, `PendingEvents == 4`. |
| V-9 | PhoneHomeEventPumpTests.Recovery_cycle_restarts_a_pump_that_ended_while_the_socket_is_open | Complete the pump task artificially (test seam `EndPumpForTest`) then `RunCycleAsync`; a new pump observes the next event; an Error entry "pump ended while the connection is live". Today: `RunCycleAsync` returns false and the event is never observed. |
| V-10 | PhoneHomeEventPumpTests.Owner_lookups_are_cached_per_connection | 50 events for one owned session and 5 for one foreign session; `host.Directory.BindingLookups` rises by at most 2 (one per session); after a reconnect the first event looks up again. Today: 55 lookups. |
| V-11 | AgentSessionRuntimeActivityTests.Output_bursts_write_LastSeenAt_at_most_once_per_interval | `FakeTimeProvider`, interval 1000 ms; 20 output events at the same instant then one after 1001 ms; `LastSeenAt` written twice (SaveChanges interceptor count == 2) and equals the last write's time. Today: 21 writes. |
| V-12 | PhoneHomeConnectionTests.Disconnect_fails_in_flight_requests_with_a_typed_connection_closed_error | `autoReply: false` peer; start `GetHealthAsync`; `peer.Socket.Abort()`; awaiting throws `PhoneHomeTransportException` with `Code == "phone_home_connection_closed"` and a message containing the runner id, the epoch and `Health`. Today: `TaskCanceledException`. |
| V-13 | PhoneHomeConnectionTests.Send_on_a_closed_connection_is_the_same_typed_error | Abort the peer, wait for `!live.SocketOpen`, then `RequestAsync(List)`; typed `ConnectionClosed`, not `WebSocketException`. Today: `WebSocketException` from `WriteFrameAsync`. |
| V-14 | PhoneHomeDirectoryTests.Remote_adapter_reaches_the_replacement_connection_after_a_reconnect | Connect A, mark recovered, `factory.Create(Raw, host.AllowedRunnerId)`, `AttachAsync(sessionId)` (peer Get returns Running); abort A; connect B, mark recovered; `adapter.KillGenerationAsync(gen, grace)`; `peerB.RequestCount(KillGeneration) == 1`. Today: `InvalidOperationException` "not dispatch-eligible" from A's connection. |
| V-15 | PhoneHomeDirectoryTests.Resolve_refuses_a_recovered_connection_whose_socket_is_closed | Mark recovered, abort the peer, wait for `!SocketOpen` while the lease is fresh; `Resolve` throws `ServiceUnavailableException(phone_home_unavailable)`. Today: returns a client on the dead object. |
| V-16 | PhoneHomeDeferredKillTests.Kill_with_no_eligible_connection_records_a_generation_kill_intent | `KillAndDisposeAsync` path driven through a failing Raw launch with the peer aborted before the kill; one `RunnerSlotReleaseIntent` with `FailureReason == "pending:kill-generation:grok-linux:<ticks>"`. Today: a Warning log and no row. |
| V-17 | PhoneHomeDeferredKillTests.Reconcile_finishes_the_intent_with_a_generation_conditional_kill_on_reconnect | Seed the intent; connect + recover a peer; `ReconcilePendingReleasesAsync`; `peer.RequestCount(KillGeneration) == 1` with the seeded generation; intent `FailureReason` reconciled; a peer replying `GenerationMismatch` marks it `failed:` and kills nothing. Today: the job audits only and never sends a kill. |
| V-18 | PhoneHomeLaunchTransportTests.Loss_after_the_launch_ack_reattaches_and_ends_Running | Peer A `SilentFor(Buffer)`; `LaunchInteractiveAsync`; on the first Buffer request abort A; connect B (Sessions contains the launched id, Running); advance the fake clock past the poll; the session ends `Running`, `peerB.RequestCount(Get) >= 1`, `peerA.Launches.Count == 1`, `peerB.Launches.Count == 0`, no `KillGeneration` on either peer, one task-event Warning naming `post-ack` attempt 1. Today: `Status == Failed`, `FailureReason == "A task was canceled."`. |
| V-19 | PhoneHomeLaunchTransportTests.Loss_before_the_ack_requeues_the_launch_within_the_bound | Peer A `SilentFor(Launch)`; abort A on the Launch request; connect B; B receives exactly one Launch with the same `AcceptedStartedAt`; the session ends `Running`. Today: Failed, "A task was canceled." |
| V-20 | PhoneHomeLaunchTransportTests.Exhausted_retries_fail_with_a_transport_reason_and_a_deferred_kill | `LaunchTransportRetries: 1`, no reconnect; advance the clock past `LaunchReattachWaitSeconds`; `Status == Failed`, `FailureReason` contains the runner id, `post-ack`, `1 retry` and the seconds waited, `RestartFailureKind == Infrastructure`; one kill-generation intent exists; the row was `Starting` at every poll before the final failure. Today: Failed immediately with "A task was canceled." and no intent. |
| V-21 | PhoneHomeLaunchTransportTests.Local_runner_launch_failures_are_unchanged | `RunnerId = null` with the harness's throwing local client; the exception type and `FailureReason` equal today's; no eligibility wait occurs (fake clock never advanced, completes immediately). Guard, expected green before and after. |
| V-22 | PhoneHomeStrandedQueueTests.Stranded_delegation_brief_on_a_phone_home_session_is_delivered | Running session bound to the runner; recovered peer whose List names it Running; a Delegation-origin Pending brief older than `StrandedAgeSeconds`; `FlushStrandedQueuesAsync` returns 1 and `peer.Inputs.Count == 1`. Today: returns 0, no Input. |
| V-23 | PhoneHomeStrandedQueueTests.ListLiveSessions_follows_the_runner_inventory_without_an_rpc | After catch-up the id is listed; emit `SessionExited` for it: removed within 2 s; abort the peer: empty; `peer.RequestCount(List)` unchanged across the three `ListLiveSessions()` calls. Today: the id is never listed. |
| V-24 | PhoneHomeStrandedQueueTests.Launch_ack_adds_the_session_before_the_next_refresh | `client.StartAsync` through the recovered connection; `ListLiveSessions()` contains the id immediately, before any refresh List. Today: not listed. |

Regression classes run in the green rows: `PhoneHomeConnectionTests` (all, including
`Unanswered_request_times_out_instead_of_waiting_forever` and `Recovery_barrier_withholds_dispatch`),
`PhoneHomeEventPumpTests` (all, especially `Catchup_commits_before_live_release` and
`Live_buffer_overflow_withholds_ready_and_recovers`, whose overflow limits are set explicitly and
so are unaffected by the D-3 default), `PhoneHomeDirectoryTests`, `AgentProtocolAdapterFactoryTests`
(the factory still returns the same adapter types), `AgentSessionInterruptedLaunchResumeTests` (the
attach primitive), `RunnerSlotRulesTests` (intent parsing), `PhoneHomeConnectionServiceTests` and
`PhoneHomeCommandDispatcherTests` on the runner side. `AgentSessionLaunchFailureTests` is `Slow` and
`NotInParallel`; it is not in a row, and R5 states that in its report.

### Execution and evidence

Run each row with `scripts/run-checkpoint.ps1` and the exact filter, `-MinExecuted` and
comma-separated `-Expect` class names; results roots `.antiphon/c679/CP-n-<sha>`, fresh per run.
Build outputs are `bin-c679-rN/` (forward slash), one per round, removed after the round's awaited
runs and never by age or pattern. Red rows return exit 1 with the named methods in `FAILED` lines;
that exit is the evidence, and the report lists the expected failing methods. Green rows require
zero failed and zero skipped among the new methods and every listed regression class executed.
Report every row as one `CHECKPOINT` line with counts, commit and TRX path, plus `reruns=k`.
Source is frozen during a run. A failure not explained by the round is re-run alone at the base
commit and reported as inherited or owned; no timeout is widened and no assertion loosened.

### Cost

Six Code rounds, each about 60 to 80 minutes of authoring plus ~3 minutes of verification. The
ordinary checkpoint floor is the sum of `EstimatedMinutes` below: **18 minutes**. Suggested
`-ExpectAbout` per round: R1 70, R2 70, R3 80, R4 85, R5 90, R6 75 minutes. No live server2 canary
or broad suite is in this profile; the runner-side rounds go live with the pending server2 redeploy
and the D-1/D-4 log lines are the acceptance evidence for the churn question over the following
evening (a follow-up read, not a Code checkpoint).

### Checkpoints

The closed list for Code. `Sn-tests` means the round's compiling red tests are committed before
its production change; `Sn` means the production change is committed. Filters use the CARD-0403
combined-class syntax. Red rows require the named assertion failures, not any failure.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-c679-r1/` | desktop-observability-red | `/*/*/PhoneHomeConnectionTests*/*` | V-1, V-2, V-3 | 3 new + 14 existing executed; V-1, V-2, V-3 fail at their log/status assertions | 17 | 1.5 |
| CP-2 | S1 | CP-1 | desktop-observability-green | `/*/*/PhoneHomeConnectionTests*/*` | V-1, V-2, V-3; connection regressions | all listed, 0 failed/skipped | 17 | 1.5 |
| CP-3 | S2-tests | `tests/Antiphon.SessionRunner.Tests -> bin-c679-r2/` | runner-observability-red | `/*/*/(PhoneHomeConnectionServiceTests*)|(PhoneHomeCommandDispatcherTests*)/*` | V-4, V-5, V-6, V-7 | 4 new + 28 existing executed; V-4..V-7 fail at their log/frame assertions | 32 | 1.5 |
| CP-4 | S2 | CP-3 | runner-observability-green | `/*/*/(PhoneHomeConnectionServiceTests*)|(PhoneHomeCommandDispatcherTests*)/*` | V-4..V-7; runner connection regressions | all listed, 0 failed/skipped | 32 | 1.5 |
| CP-5 | S3-tests | `tests/Antiphon.Tests -> bin-c679-r3/` | pump-red | `/*/*/(PhoneHomeEventPumpTests*)|(AgentSessionRuntimeActivityTests*)/*` | V-8, V-9, V-10, V-11 | 4 new + 5 existing executed; V-8..V-11 fail at their count assertions | 9 | 1.5 |
| CP-6 | S3 | CP-5 | pump-green | `/*/*/(PhoneHomeEventPumpTests*)|(AgentSessionRuntimeActivityTests*)/*` | V-8..V-11; pump ordering regressions | all listed, 0 failed/skipped | 9 | 1.5 |
| CP-7 | S4-tests | `tests/Antiphon.Tests -> bin-c679-r4/` | routing-red | `/*/*/(PhoneHomeConnectionTests*)|(PhoneHomeDirectoryTests*)|(PhoneHomeDeferredKillTests*)/*` | V-12..V-17 | 6 new + 22 existing (17 connection after S1, 5 directory) executed; V-12..V-17 fail at their typed-error / request-count / intent assertions | 28 | 1.5 |
| CP-8 | S4 | CP-7 | routing-green | `/*/*/(PhoneHomeConnectionTests*)|(PhoneHomeDirectoryTests*)|(PhoneHomeDeferredKillTests*)|(AgentProtocolAdapterFactoryTests*)|(RunnerSlotRulesTests*)/*` | V-12..V-17; factory and intent regressions | all listed classes, 0 failed; 6 new methods, 0 skipped | 36 | 1.5 |
| CP-9 | S5-tests | `tests/Antiphon.Tests -> bin-c679-r5/` | launch-red | `/*/*/PhoneHomeLaunchTransportTests*/*` | V-18, V-19, V-20, V-21 | 4 executed; V-18, V-19, V-20 fail at `Status`/`FailureReason`; V-21 passes | 4 | 1.5 |
| CP-10 | S5 | CP-9 | launch-green | `/*/*/(PhoneHomeLaunchTransportTests*)|(AgentSessionInterruptedLaunchResumeTests*)/*` | V-18..V-21; attach-path regression | all listed classes, 0 failed; 4 new methods, 0 skipped | 12 | 1.5 |
| CP-11 | S6-tests | `tests/Antiphon.Tests -> bin-c679-r6/` | inventory-red | `/*/*/PhoneHomeStrandedQueueTests*/*` | V-22, V-23, V-24 | 3 executed; all three fail at their listed/delivered assertions | 3 | 1.5 |
| CP-12 | S6 | CP-11 | inventory-green | `/*/*/(PhoneHomeStrandedQueueTests*)|(PhoneHomeEventPumpTests*)/*` | V-22..V-24; pump regressions after the inventory hooks | all listed classes, 0 failed; 3 new methods, 0 skipped | 11 | 1.5 |

`Min` counts executed TUnit results. Existing-class counts are from the classes as they stand at
`0595e8e9`: `PhoneHomeConnectionTests` 14, `PhoneHomeEventPumpTests` 5, `PhoneHomeDirectoryTests` 5,
`PhoneHomeConnectionServiceTests` 8, `PhoneHomeCommandDispatcherTests` 20,
`AgentProtocolAdapterFactoryTests` 6, `RunnerSlotRulesTests` 2,
`AgentSessionInterruptedLaunchResumeTests` 7 methods (8 results with its two-argument case). Later
rows include the methods earlier rounds added to a shared class. Code reads the fresh TRX rather
than these numbers if a class has grown by then. Cost floor = 12 × 1.5 = 18 minutes.
