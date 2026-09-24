# CARD-0679 — phone-home launch loss on server2 (2026-09-24)

Investigate stage, task 492a3ddb. Read-only: nothing was changed in code, config or the database.

## Verdict

**Confirmed. The launch frames were not lost.** In both of the sessions the card calls "lost" (e3c147bb, e34a1fcd),
the Launch **was acknowledged by the runner**. The desktop then failed the session because it lost the
phone-home WebSocket during the ready-wait (`WaitForReadyAsync` → `GetBufferAsync`). Its clean-up kill went to
the dead connection object and was refused. So the runner kept the processes running while the desktop
recorded them as Failed (split-brain), and the task was failed about 5 minutes later.

Four confirmed defects combine to produce this:

1. **D1: a reconnect cancels every in-flight request.** On disconnect, `PhoneHomeLiveConnection.DisposeAsync`
   calls `TrySetCanceled()` on every waiter. The caller then sees a bare `TaskCanceledException` ("A task was
   canceled.") that carries no transport meaning. This is the source of the cancellations. They do **not** come
   from a launch-timeout token, and they do **not** come from a server restart.
2. **D2: remote adapters stay pinned to one connection object for the life of the session.**
   `AgentProtocolAdapterFactory.Create(kind, runnerId)` calls `_directory.Resolve(runnerId)` once, and the
   returned `PhoneHomeRunnerClient` wraps that single `PhoneHomeLiveConnection`. After a reconnect, every call
   through that adapter hits the dead object, including the clean-up `KillGenerationAsync`. The runner therefore
   keeps the session and its capacity slot: the orphan.
3. **D3: a launch failure is terminal, whatever its cause.** `AgentSessionService.LaunchInteractiveAsync` marks
   every exception Failed and rethrows. It has no transport classification and no re-queue, and the launch
   queue calls it once with `CancellationToken.None`.
4. **D4: remote sessions are invisible to the stranded-queue watchdog.**
   `AgentSessionRuntime.ListLiveSessions()` lists only the *local* runner. A boot brief deferred by a phone-home
   drop is therefore never retried. This is the 837ff8d2 / bbc4e2e6 failure.

**Why the socket closes:** the phone-home connection churned 59 times between 16:00 and 20:11 (+01:00), with
lifetimes of 29 s to 7 min. No process restarted. In two of the three losses, the evidence shows the
**desktop** started the close. The only desktop-initiated close path that logs nothing is the endpoint's
`EventOverflow` / `MessageTooLarge` catch. EventOverflow is therefore the leading explanation, but it is
**inferred by elimination, not observed**, because that path logs nothing. See §Q1 and §Open.

## Timeline (desktop log `C:\src\Antiphon\server\logs\antiphon-20260924.log`, +01:00; runner and DB are UTC)

| Time (+01:00) | Event | Evidence |
|---|---|---|
| 17:50:13 | Last desktop server start before the incidents | log line 34575 `Application started` |
| 15:55:05 (14:55:05Z) | Last start of the server2 runner container; RestartCount 0 | `docker inspect antiphon-runner-session-runner-1` |
| 19:10:45.411 | Phone-home connection `0HNOQBFJPV8UU` connects | line ~37824 |
| 19:12:39.551 | Task 8c094bd8 dispatched → session **e3c147bb** | line 37909 |
| 19:13:02.299 | `Session reconciliation scan failed`: `TaskCanceledException` at `PhoneHomeLiveConnection.cs:129` (`await waiter.Task`) in `ListAsync` | line ~37922 |
| 19:13:02.324 | Clean-up kill refused: `InvalidOperationException: Phone-home connection is not dispatch-eligible` at `:89` in `KillGenerationAsync` | line ~37927 |
| 19:13:02.328 | **e3c147bb fails**: `TaskCanceledException` at `:129` in **`GetBufferAsync`** ← `RunnerTerminalSession.WaitForQuietAfterVisibleAsync` ← `RunnerClaudeAdapter.WaitForReadyAsync` ← `AgentSessionService.cs:459`, i.e. after `adapter.StartAsync` returned | line 37931 |
| 19:13:06.666 | Connect endpoint `0HNOQBFJPV8UU` returns, **4.3 s after its waiters were cancelled** | line 37942 |
| 19:13:07.233 / 18:13:06Z | The runner registers again | desktop line 37944; runner log `[18:13:06] Sending HTTP request POST …/register` |
| 19:18:07.556 / .721 | Another reconciliation `ListAsync` cancelled, then connection `0HNOQBFJPV8VA` ends (lifetime 5 m 00 s) | lines ~38160–38164 |
| 19:19:03.153 | Task 93442387 dispatched → session **e34a1fcd** | line 38237 |
| 19:19:11.124 | Clean-up kill refused: "not dispatch-eligible" | line ~38240 |
| 19:19:11.132 | **e34a1fcd fails**: `WebSocketException … invalid state ('CloseSent')` from `WriteFrameAsync` at `:105` in **`GetBufferAsync`** ← `WaitForReadyAsync` ← `:459` (after the ack) | line 38246 |
| 19:19:11.135 | Connect endpoint `0HNOQBFJPV90F` returns (lifetime 60 s) | line 38245 |
| 19:25:56.538 | Task 2e8b3a67 dispatched → session **479d4d8a** | line 38672 |
| 19:25:57.780 | Connect endpoint `0HNOQBFJPV928` returns (lifetime 37 s) | line 38675 |
| 19:25:57.805 | **479d4d8a fails**: `TaskCanceledException` at `:129` in **`StartAsync`** (the Launch request itself, before any reply), then the kill is refused | lines 38682 ff. |
| 19:30:36.202 | Task 837ff8d2 dispatched → session **bbc4e2e6** | line 38929 |
| 19:30:49.634 | Connect endpoint returns (lifetime 2 m 50 s) | line 38954 |
| 19:30:49.662 | Boot flush: `Deferring delivery to session bbc4e2e6: herdr unreachable (no attempt charged)` | line 38955 |
| 19:30:49.667 (18:30:49.667Z) | `InteractiveLaunchCompletedAt` stamped; the brief remains `Pending`, 0 attempts | DB `AgentSessions` / `SessionQueuedMessages` |
| 18:31:48Z, 18:36:48Z | Runner: bbc4e2e6 "running WITHOUT a transcript" (Claude writes none until a prompt arrives) | runner log |
| 19:18:26, 19:23:33, 19:30:22 | The dead-session reconciler fails 8c094bd8, 93442387 and 2e8b3a67 | lines 38183, 38543, 38919 |
| 19:41:13 | The delivery watchdog fails 837ff8d2: "the brief is still queued Pending and was never attempted" | line 39479 |
| 19:42:35 / 19:42:36 | Operator force-releases the server2 slots for e3c147bb and e34a1fcd (the orphans) | lines 39568, 39573 |

DB rows, read with `psql`:

| Session | Status | Failure reason | Queued brief |
|---|---|---|---|
| e3c147bb | Failed (5) | "A task was canceled." | Pending, Delegation origin, 0 attempts |
| e34a1fcd | Failed (5) | "…('CloseSent')…" | Pending, Delegation origin, 0 attempts |
| 479d4d8a | Failed (5) | "A task was canceled." | Pending, Delegation origin, 0 attempts |
| bbc4e2e6 | Status 4 (killed by the delivery watchdog) | — | Pending, Delegation origin, 0 attempts |

The line numbers in the stack traces match the current source: `PhoneHomeLiveConnection.cs` was last changed
at ea6b603f (2026-09-23). Line 89 is the dispatch-eligible throw, line 105 is `WriteFrameAsync`, and line 129
is `await waiter.Task`.

## Q3 — Where does the cancellation come from?

**It comes from the disconnect clean-up, not from a token.**

- `AgentSessionLaunchQueue.cs:144` passes `CancellationToken.None` into `LaunchInteractiveAsync`, so the launch
  has no timeout token.
- The per-request timeout (`RequestTimeoutFor`: 2 min for Launch, 60 s otherwise) throws
  `PhoneHomeTransportException(RequestTimeout)` from inside the `WhenAny` branch. It cannot raise a
  `TaskCanceledException` at line 129.
- Line 129 (`await waiter.Task`) throws `TaskCanceledException` only if the waiter itself was cancelled. The
  only code that does that is `PhoneHomeLiveConnection.DisposeAsync` (`foreach waiter → TrySetCanceled()`). It
  is called from the connect endpoint's `finally` (`SessionRunnerEndpoints.cs:121-124`, after
  `Disconnect`), and from `AcceptConnect` when a newer socket replaces the old one.
- **Server restart: ruled out.** No `Application started` / `shutting down` between 17:50:13 and 20:11. The
  runner container has been up since 14:55:05Z with RestartCount 0, and the runner log has no startup lines.
- **Load-induced slowness:** it is not the direct cause of any failure. The desktop was under load, though:
  delegation sweeps hit their 60 s budget at 18:49, 19:06, 19:10 and 19:27, a DbCommand took 1,219 ms at
  18:54:13, and card-file git timed out at 19:18:06. That load is the likely reason events back up (§Q1).

## Q1 — Why was the socket closing?

Facts:

- **The churn is chronic, not an incident.** Registrations per hour on 2026-09-24: 3, 6, 1, 5, 9, 47, 14, 23, 3,
  3, 23, 7, 14, 14. On 2026-09-23 between 00:00 and 08:00 there were about 230 per hour, one every ~15 s.
  Connection lifetimes from 18:48 to 19:32: 42 s, 29 s, 3 m 48, 4 m 21, 7 m 37, 1 m 52, 3 m 15, 2 m 21,
  5 m 00, 60 s, 5 m 36, 32 s, 37 s, 49 s, 29 s, 39 s, 2 m 50, 1 m 46. The run of sub-minute lifetimes between
  19:24 and 19:28 coincides with three server2 launches, each followed by its own catch-up.
- **Until 15:54 the ends were logged, and since then they are silent.** Between 00:53 and 15:54,
  `ExceptionMiddleware` logged 95 connect-endpoint exceptions, mostly `WebSocketException: The remote party
  closed the WebSocket connection without completing the close handshake` (a transport abort). The 59
  reconnects after 16:00 logged **no** exception at all.
- **The runner logs nothing when a connection ends normally.** `PhoneHomeConnectionService.RunConnectionAsync`
  returns when any one of the receive, heartbeat or event loops completes, including a faulted send. It then
  reconnects without a log line. A warning is logged only when an exception escapes, which here means only the
  runner-side overflow. The runner log for 17:00–19:30Z contains **zero** "Phone-home connection ended"
  warnings, so the runner-side event-hub overflow is ruled out.
- **Two of the three losses were closed by the desktop.**
  - e34a1fcd failed because the socket was `CloseSent`. On `ManagedWebSocket`, `CloseSent` means *this side* sent
    the Close while the socket was Open. A close started by the runner would leave the desktop in
    `CloseReceived`, and `DisposeAsync` skips `CloseAsync` unless the state is `Open`.
  - e3c147bb: the waiters were cancelled at 19:13:02.3 and the endpoint returned at 06.666. The only code
    between the two is `_socket.CloseAsync(...)` in `DisposeAsync`, which runs only if the socket was still
    `Open`. So the desktop's receive loop ended while the socket was Open, and 4.3 s is the time the runner
    took to answer the Close.
- **How the desktop's receive loop can end while the socket is Open, with nothing logged:**
  - The `EventOverflow` / `MessageTooLarge` catch at `SessionRunnerEndpoints.cs:116-120` calls
    `directory.Disconnect(connection, ex.Code)`. `Disconnect` discards the reason.
  - Any other exception reaches `ExceptionMiddleware`, which logs it; none was logged after 15:54.
  - A `null` frame means a Close was received, which leaves the state `CloseReceived`, not `Open`.
  - Cancellation of `RequestAborted` comes with an aborted socket, which is not `Open` either.
  - `MessageTooLarge` is unlikely because the runner enforces the same 16 MiB limit before sending
    (`PhoneHomeFraming.WriteFrameAsync`) and logs a warning when a reply cannot be sent. No such warning
    exists.
  - **This leaves `EventOverflow`:** the desktop holds more than 1024 pending events or more than 16 MiB of
    them (`PhoneHomeLiveConnection.cs:170-174`, defaults in `PhoneHomeContracts.cs:16-17`).
- **Why events would back up.** Pending events are released only by `PhoneHomeRecoveryPump.PumpEventsAsync`.
  It is serial, and for each event it opens a DI scope and runs a DB query (`OwnerMatchesAsync` →
  `GetBindingAsync`), then publishes to SignalR.
  - It does not start until catch-up has completed. Catch-up runs `List`, plus `GetTranscript` and a persist
    for every owned session on every new connection. Output keeps arriving and is counted during catch-up,
    so a busy runner plus a slow desktop gives a self-sustaining loop: reconnect, slow catch-up, backlog,
    overflow, reconnect.
  - `PumpEventsAsync` is also fire-and-forget (`_ = PumpEventsAsync(live, ct)`, `PhoneHomeRecoveryPump.cs:99`).
    Its per-event `try/finally` releases the event, but the exception still ends the pump for that
    connection, and `RunCycleAsync` never restarts it (`_recovered == live`). From then on nothing releases
    events, so overflow is certain after 1024 more events. This is a confirmed code-level defect; whether it
    fired on 2026-09-24 is not confirmed.
- **Other causes ruled out.**
  - Keepalive: the runner heartbeats every 15 s and the lease is 90 s. The desktop never closes a socket over
    lease expiry; `SnapshotLive` only clears `DispatchEligible`.
  - Proxy idle timeout: an idle cut would appear as the logged "closed without completing the close
    handshake" abort, and the socket is never idle for 15 s.
  - A competing registration: each old endpoint returned *before* the next `register`, so no
    `AcceptConnect` replacement happened.

## Q2 — Why does a transport failure fail the task instead of re-queuing it?

- `AgentSessionService.LaunchInteractiveProcessAsync` (`:433-551`) catches every exception, calls
  `KillAndDisposeAsync`, and rethrows.
- `LaunchInteractiveAsync` (`:341-420`) then sets `Status=Failed`, `FailureReason=ex.Message` and
  `TerminationSource=SystemRequest` for *any* exception. The only special cases are `ResumeTargetMissing` and
  `AgentLaunchBlocked`. Nothing distinguishes "transport lost before or after the runner acknowledged".
- `AgentSessionLaunchQueue.LaunchInteractiveSessionAsync` (`:139-146`) runs it once and logs
  `Queued interactive session launch failed`. It has no retry. The dead-session reconciler then fails the task
  ("Session died before the task settled").
- **The launches the card calls lost had in fact been acknowledged.** In two of the three failures
  (e3c147bb, e34a1fcd) the Launch completed: the failure is in `WaitForReadyAsync`, reached only after
  `adapter.StartAsync` returned the runner's echoed launch generation (`PhoneHomeRunnerClient.StartAsync`). A
  re-queue of the *launch* would not have been correct for these two, because the runner already held the
  session. What was needed was to keep, or re-attach to, the runner's live session on the new connection.
  - Only 479d4d8a failed before the ack (the `StartAsync` waiter was cancelled). Whether the runner acted on
    that frame is unknown; see §Open.
  - The card's statement that "the runner's docker log has no entry for either session, so the Launch frame
    never reached it" rests on a false premise: the runner does not log launches at all. It logs a session
    only once it tails a transcript, and a Claude session with no delivered prompt writes no transcript.

## Split-brain (caller refinement: e3c147bb and e34a1fcd Running on server2 at 18:42Z)

This follows directly from D2. The adapter used for the launch was built by
`_adapterFactory.Create(session.AgentKind, session.RunnerId)` (`AgentSessionService.cs:442`), which binds
`new PhoneHomeRunnerClient(live)` to the connection that existed at that moment
(`PhoneHomeRunnerDirectory.Resolve`, `:61-71`).

When that connection dropped:

1. The endpoint's `finally` ran `Disconnect`, which set `DispatchEligible=false`, then `DisposeAsync`, which
   cancelled the waiters.
2. `KillAndDisposeAsync` then called `KillGenerationAsync` through the **same pinned client**.
3. `RequestAsync` rejected it at the eligibility gate (`PhoneHomeLiveConnection.cs:86-89`: "not
   dispatch-eligible"). `KillAndDisposeAsync` logs this and swallows it by design.

The runner never received a kill. The runner-side sessions stayed Running in their capacity slots until the
operator released them at 19:42:35 and 19:42:36. By contrast, `RoutingSessionRunnerClient` resolves on every
call (`RoutingSessionRunnerClient.cs:71-84`), so the delivery path does reach the new connection.

## 837ff8d2 / bbc4e2e6 — boot prompt never delivered

- The launch completed across the reconnect: `InteractiveLaunchCompletedAt` is 18:30:49.667Z and no launch
  error was logged.
- The boot flush inside launch (`AgentSessionService.cs:524`) ran at the moment the connection ended
  (endpoint returned at 19:30:49.634; flush at .662).
  - It sent input through `RoutingSessionRunnerClient`, which calls `Resolve`. There was no live eligible
    connection, so `ServiceUnavailableException(phone_home_unavailable)` was thrown.
  - `IsHerdrUnreachable` (`SessionMessageQueueService.cs:2834-2838`) mapped that to `BackendUnreachable`, and
    the refund path (`:2110-2131`) reset the brief to Pending with no attempt charged.
- Nothing retried it:
  - The boot flush runs once.
  - A session that has never had a prompt produces no turn end.
  - `FlushStrandedQueuesAsync` (`:1235-1327`) would cover a Delegation-origin brief after
    `StrandedAgeSeconds`=60. But it only visits candidates in `_runtime.ListLiveSessions()`, and that method
    (`AgentSessionRuntime.cs:1191-1199`) calls `_runnerClient.ListAsync`. `RoutingSessionRunnerClient.ListAsync`
    is `_directory.Local.ListAsync` (`RoutingSessionRunnerClient.cs:20-21`), the local runner only. **A
    phone-home session is never a stranded-queue candidate** (D4).
- The delivery watchdog failed the task after 10 minutes.
- Aside: at 19:41:10 the watchdog's stop got `Session 'bbc4e2e6…' was not found` from the runner, although
  the runner had reported the session running at 18:36:48Z. Similar 404s for other sessions appear at 19:32
  and 19:35. This was not pursued; see §Open.

## Confirmed defects, with red tests that reproduce them today

Each test is specified so that it fails on the current code. These are reproduction specifications for Plan
and TestDesign; they were not written or run in this stage.

| # | Defect | Red test (fails today) | Where |
|---|---|---|---|
| D1 | A disconnect surfaces in-flight requests as a bare `TaskCanceledException` | Start `RequestAsync(List)` on a `PhoneHomeLiveConnection` over a fake socket that never replies, then call `DisposeAsync`. Assert the throw is a typed transport failure that carries the connection epoch and a reason such as "connection closed". Today it is `TaskCanceledException`. | `tests/Antiphon.Tests` phone-home unit tests |
| D2 | A remote adapter is pinned to one connection | Register, connect A, mark it recovered, then `AgentProtocolAdapterFactory.Create(ClaudeCode, "server2")`. Then register and connect B (A disposed) and mark B recovered. Assert `adapter.KillGenerationAsync` reaches B's socket. Today it throws "not dispatch-eligible" from A. | directory + adapter factory |
| D2′ | A failed launch orphans the runner session | Run `LaunchInteractiveAsync` with a fake phone-home runner that acks Launch, then drop the connection during the ready-wait and reconnect. Assert the runner receives `KillGeneration` for that generation, or the session is re-attached. Today the runner session is never killed. | AgentSessionService launch tests |
| D3 | A transport loss during launch fails the task terminally | The same harness as D2′. Assert the session is not Failed with `FailureReason="A task was canceled."` and that the task is not failed by the dead-session reconciler: the launch is either re-attached (after the ack) or re-queued (before the ack), bounded to a few attempts. Today the session is Failed. | AgentSessionService / AgentSessionLaunchQueue |
| D4 | The stranded-queue watchdog never serves phone-home sessions | Seed a Running session with `RunnerId=server2`, a live recovered phone-home connection whose `List` returns that session as Running, and a Delegation-origin Pending brief older than 60 s. Run `FlushStrandedQueuesAsync`. Assert one delivery attempt. Today the session is not in `ListLiveSessions()`, so there are 0 attempts. | SessionMessageQueueService |
| D5 | The event pump dies on the first failing event | Run `PumpEventsAsync` with a runtime whose `ObserveOutputAsync` throws on event 1, then feed events 2–N. Assert that events 2–N are observed and `PendingEvents` returns to 0. Today the pump ends and `PendingEvents` stays at N−1. | PhoneHomeRecoveryPump tests |
| D6 | The disconnect reason is discarded, and the runner's normal end is silent | Drive the endpoint's receive loop to `EventOverflow`. Assert a log entry with the runner id, epoch and reason `event_overflow`. Today `Disconnect(connection, reason)` ignores `reason` and nothing is logged. On the runner side: a connection whose heartbeat send faults must log which loop ended and why. | endpoint + `PhoneHomeConnectionService` |

## Open (what would resolve it)

- **Which desktop close path fires, and at what rate.** EventOverflow is inferred by elimination. It is
  resolved by the D6 logging: a reason on `Disconnect`, plus the `PendingEvents` and `LiveBufferEvents`
  counts at close. One evening of runtime would then show whether every silent reconnect is `event_overflow`,
  and whether its cause is pump throughput or a dead pump (D5).
- **Whether the runner acted on 479d4d8a's Launch.** The desktop never got a reply. The caller's slot listing
  at 18:42Z did not include it, which suggests the runner either never read the frame or cancelled the launch
  together with the connection. `DispatchAndReplyAsync` passes the connection token into
  `_dispatcher.DispatchAsync`, so a Launch in flight when the connection ended is cancelled on the runner. A
  runner-side launch log line would settle this.
- **The runner answering 404 for sessions it reported live** (bbc4e2e6 at 19:41, a3ae4ce3 at 19:32, 08cbe378
  at 19:35). Not investigated. This may be a separate card.

## Not done, noted (one-line fix directions for Plan; no design here)

- D1: fail in-flight waiters with a typed `PhoneHomeTransportException(ConnectionClosed)` rather than cancelling them.
- D2: have remote adapters resolve the current live connection on each call (as `RoutingSessionRunnerClient` does), not at `Create`.
- D2′/D3: on a transport loss after the ack, re-attach to the runner session on the next recovered connection (the CARD-0340 attach path); on a loss before the ack, re-queue with a bounded attempt count, fencing with the launch generation so a duplicate Launch is refused.
- D4: make `ListLiveSessions` include live phone-home sessions (the runner inventory from `GetInventoryAsync`).
- D5: catch per-event failures inside `PumpEventsAsync` and supervise the pump task.
- D6: log each connect and disconnect with runner id, epoch, reason and pending-event counts on the desktop, and the ending loop and exception on the runner.
- Card ask (2): `Resolve` / `SnapshotLive` should also require `SocketOpen`. Today only `DeclaredCapacity` and `Status` check it.
