# CARD-1365: async herdr snapshot route with a bounded pane.read deadline

Date: 2026-10-19. Stage: Plan. Target: **master only** (see "rc line" below).
Next: **test-design**. The brief does not fold TestDesign into this dispatch, so the
`### Checkpoints` proposal at the end is input to that stage, not a closed list.

Card: CARD-1365 (`c4558b97-bad1-4440-898a-1e6eb2886b3e`, Antiphon board), filed as an
out-of-scope finding of the CARD-1362 plan
([2026-10-19-card-1362-bounded-async-runner-reads-plan.md](2026-10-19-card-1362-bounded-async-runner-reads-plan.md)
on `feat/card-task-66beda91` at `dc223795`, fetched and read; not merged). This plan keeps that
plan's deadline and configuration conventions: one `...TimeoutMs` setting, a floor applied with
`Math.Max`, a `CancellationTokenSource(delay, clock)` linked to the caller token, caller
cancellation distinguished from the deadline, and the deadline's outcome mapped onto an outcome
class the callers already handle.
Branch `feat/card-task-c9eb0227`, base `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`
(equal to `origin/master` at inspection). Owners read first:
[docs/herdr-sessions.md](../../herdr-sessions.md),
[docs/session-runtime-invariants.md](../../session-runtime-invariants.md),
[docs/testing-and-build.md](../../testing-and-build.md),
[docs/resilience.md](../../resilience.md).

This Plan changes documentation only. No build, test, card write or dispatch ran.

## Outcome in one paragraph

The session runner serves `GET /sessions/{id}/snapshot` for a herdr session by calling
`ReadScreenAsync(CancellationToken.None).GetAwaiter().GetResult()` inside
`RunnerSession.GetSnapshot()`; the phone-home `Snapshot` operation goes through the same
synchronous method. Both lanes pin a thread-pool thread for the whole `pane.read`, and nothing
bounds that read once herdr has accepted the connection. The plan makes the snapshot read async
on every hop (session, runtime, HTTP route, phone-home surface, adapter, dispatcher), bounds the
herdr `pane.read` with `SessionRunner:Herdr:PaneReadTimeoutMs` (default 3000 ms, floor 250) on
the runtime's injected clock, answers a deadline as the existing "herdr unreachable" class on
both lanes (503 `herdr_unreachable`, which the server already treats as a deferral, never as an
empty screen), deletes the synchronous forms, and proves with one single-thread continuation
pump witness that a stalled screen read leaves the thread free to answer a `List` request and
that the read ends at the deadline with its token cancelled. The pty-host path keeps its
in-memory read and its semantics; the herdr content-delta counter fold is unchanged.

## Ground truth

| Card assumption | Code at `6ec71d42` | Consequence for the plan |
|---|---|---|
| The herdr snapshot route blocks on `ReadScreenAsync(...).GetResult()` | True. `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:3830-3831` inside `RunnerSession.GetSnapshot()` (`:3819-3868`), reached from `SessionRunnerRuntime.GetSnapshot(Guid)` (`:1045`). HTTP: `Program.cs:292-294` is a synchronous lambda with no token. Phone-home: `IPhoneHomeRuntimeSurface.GetSnapshot` (`PhoneHomeCommandDispatcher.cs:33`) is synchronous, called at `:338`; `PhoneHomeRuntimeAdapter.cs:104` forwards it to the runtime | Two lanes pin a thread for one `pane.read`. The fix is on `RunnerSession` and every hop above it must become `async`, or the pinned thread only moves (D-1, D-4) |
| It is the same hazard class as CARD-1362 | True in kind, smaller in count: one site, two lanes. Neither the runner nor the server raises the thread-pool minimum. Kestrel request handling and the phone-home receive pump are both thread-pool continuations | One slice, not five. The witness models the pool the same way CARD-1362 D-6 does (D-5) |
| The read needs a deadline | There is none. `HerdrClient.SendCoreAsync` (`HerdrClient.cs:87-104`) passes its token into `HerdrTransport.ConnectAsync`, `WriteRequestAsync` and `ReadResponseAsync` (`reader.ReadLineAsync(ct)`, `:510-526`); only the connect is bounded (`HerdrSettings.ConnectTimeoutMs`, 5 s, `HerdrSettings.cs:22`). A herdr that accepts and never answers holds the read for ever | A linked deadline token aborts an in-flight `pane.read` at the pipe read. No client change is needed; the bound belongs at the read site (D-2) |
| A failed `pane.read` is already handled | Partly. `HerdrBackendUnavailableException` propagates: HTTP answers 503 `herdr_unreachable` through `HerdrUnreachableFilter` (`Program.cs:156-170`); phone-home has no specific catch and answers `PhoneHomeErrorFrames.Internal` through the generic catch (`PhoneHomeCommandDispatcher.cs:557-562`). Any other exception is logged at Debug (`:3855`) and the route answers 200 with the mirror, which for a herdr session is never fed (`HandleOutput` `:4117` is the pty-host client's only writer), so the screen text is empty | A deadline must not fall into the 200-empty branch: the server would read an empty screen as a real snapshot. It maps onto the unreachable class on both lanes (D-3), which also removes the lane disparity for this one exception |
| The server copes with a 503 on snapshot | Yes. `SessionRunnerHttpClient.GetSnapshotAsync` (`server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:581-596`) runs under `runner.get-snapshot`; `EnsureRunnerSuccessAsync` (`:1218-1231`) maps 503 `herdr_unreachable` to `ServiceUnavailableException(Unreachable)`; `SessionMessageQueueService.IsHerdrUnreachable` (`:3565-3566`) defers on it; `AgentSessionRuntime.TryGetLiveSnapshot` returns null for any exception. Phone-home: `PhoneHomeRunnerClient.ThrowIfError` (`:576-591`) maps a 503 frame to the same `ServiceUnavailableException(code)`, any other status to `ConflictException`. 503 is retryable in the resilience pipeline (`docs/resilience.md:49`: 408/429/502/503/504) | A deadline costs the server at most (retries + 1) x the runner deadline per read, bounded, instead of an unbounded block; once CARD-1362 lands its 5 s runtime deadline wins. The phone-home catch in D-3 makes the deferral class reachable on that lane too |
| A red-first witness can prove the thread is not held | `PhoneHomeCommandDispatcher.DispatchAsync` (`:243`) exists at base with an async signature and routes `Snapshot` through the adapter to the runtime with no `ConfigureAwait(false)` anywhere on the path. A `RunnerSession` can be constructed and tracked with a fake `ISessionChild` (`BindChildForTest` `:3755`, `Track` `:1699`; the `SeatWorld.Bind` shape in `tests/Antiphon.SessionRunner.Tests/TerminalSeatReleaseTests.cs:1555-1567`). The runtime takes a `TimeProvider` (`:180`, `_labelClock` `:197`). `CancellationTokenSource(TimeSpan, TimeProvider)` is already used in the runner (`CodexCliVersionProbe.cs:110`) | The witness is red by behaviour at base through `DispatchAsync` on a single-thread pump and proves the deadline on `FakeTimeProvider` without touching any new symbol (D-5) |
| The synchronous forms have few callers | 12 fakes of `IPhoneHomeRuntimeSurface` each carry a one-line `GetSnapshot` (7 in `tests/Antiphon.SessionRunner.Tests`, 5 in `tests/Antiphon.Tests`); 19 runner-test call sites in 4 files and 4 `Antiphon.Tests` sites in 4 files, all inside async methods or async-capable lambdas (listed in S1). `Antiphon.Tests` references the runner project (`Antiphon.Tests.csproj:68`) and `InternalsVisibleTo` covers both test projects | Full conversion is 35 one-line test edits and fits the budget; the slice must build `Antiphon.Tests` as well (D-4, CP-3) |
| Only the snapshot route reads the pane | No. `GET /sessions/{id}` (`SessionReadLaunchRoutes.cs:11-12`) calls `GetAsync`, whose `RefreshStatusAsync` (`HerdrPaneChild.cs:1084-1095`) does `pane.get` plus `pane.read` with the caller's `RequestAborted` token: already async, not bounded by a runner-side deadline. Adoption's "`pane.read` answers" check is a one-off at startup | Out of this card's scope; the card names the snapshot route. Recorded in D-7 for a follow-up if CARD-1362 S4's timing log shows it |
| `GET /api/runner-defaults` and `GET /api/session-runners` describe the lanes | Read at planning time: global runner `server2`; runners `desktop` (windows, delegated-task capacity 2), `server2` (linux, draining, not accepting new work), `server2-temp` (linux, accepting). Not embedded here. The witness is in-process with no process or socket; `HerdrRunnerSessionTests` uses `FakeHerdrTransport`, which picks a named pipe on Windows and a Unix socket on Linux, and `HerdrTestProcess.ShellPath` for its one pty-host launch on both | Every checkpoint is portable: omit `-Runner` and `-Platform`; the orchestrator names the lane at dispatch. CP-3 needs the shared test Postgres |

## Decisions

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | Async on every hop, same semantics as today. `RunnerSession.GetSnapshotAsync(TimeProvider clock, TimeSpan paneReadTimeout, CancellationToken ct)` replaces `GetSnapshot()`; `SessionRunnerRuntime.GetSnapshotAsync(Guid, CancellationToken)` replaces `GetSnapshot(Guid)`; the HTTP route lambda becomes `async` with the request token; `IPhoneHomeRuntimeSurface.GetSnapshotAsync(Guid, CancellationToken)` replaces `GetSnapshot(Guid)`; the adapter forwards; the dispatcher awaits. The pty-host branch has no await and completes synchronously inside the async method, so its callers see the same in-memory read. The pending-adoption refusal, the content-delta fold under `_gate` and the mirror fallback for non-deadline failures are unchanged | A default interface method `GetSnapshotAsync` forwarding to a kept synchronous `GetSnapshot` (leaves a synchronous read on the live runtime that must refuse herdr sessions: a loud trap at best, the hazard at worst); `Task.Run` around the synchronous read (moves the pinned thread, keeps it) |
| D-2 | One deadline per herdr snapshot read, at the read site, from `HerdrSettings.PaneReadTimeoutMs` (default 3000, floor 250 via `Math.Max`, bound at `SessionRunner:Herdr:PaneReadTimeoutMs`), applied as `new CancellationTokenSource(timeout, clock)` linked to the caller token, with the runtime's injected `TimeProvider` (`_labelClock`). The runtime resolves the `TimeSpan` once in its constructor from `_herdrClient?.Settings` and falls back to the class default when no herdr client is registered (a herdr session cannot exist without one; test fixtures that bind a fake child get the default). 3000 sits inside the server's CARD-1362 `RunnerLiveReadTimeoutMs` (5000) so the runner answers before the server gives up, and above the 870 ms healthy herdr snapshot CARD-1359 measured | 5000 (equal to the server's cap: the runner would never answer first); 1000 (no margin over the measured healthy read); a setting on `SessionRunnerSettings` (herdr knobs live under `SessionRunner:Herdr`, next to `ConnectTimeoutMs` and `LaunchDetectTimeoutMs`); the deadline inside `HerdrPaneChild.ReadScreenAsync` (bounds one implementation, not the read; a fake-child witness could not prove it); a constructor parameter on `RunnerSession` (eight construction sites, four of them static adoption helpers) |
| D-3 | The deadline is the "herdr unreachable" class on both lanes. `RunnerSession` catches `OperationCanceledException` when the deadline fired and the caller token did not, and throws `HerdrBackendUnavailableException("Herdr pane.read for session <id> exceeded <n> ms.")`; a cancelled caller token rethrows unchanged. HTTP keeps answering 503 `herdr_unreachable` through the existing filter. The dispatcher gains one catch, `HerdrBackendUnavailableException` to `Error(request, HerdrProblemTypes.Unreachable, ex.Message, 503)`, placed before the generic catch, so the phone-home lane reaches the server's deferral class (`ThrowIfError` maps a 503 frame to `ServiceUnavailableException(code)`) instead of an "Internal" frame that the server reads as a conflict. Non-deadline failures keep today's Debug log and mirror fallback | The mirror fallback for a deadline (a herdr mirror is never fed: the server would read an empty screen as a snapshot, the "missing snapshot is not an empty composer" failure the invariants forbid); a new problem type such as `herdr_slow` (the server's `EnsureRunnerSuccessAsync` only defers on a bare 503 or `herdr_unreachable`; anything else surfaces as a transport failure the queue does not defer on); 504 (retryable and unrecognised) |
| D-4 | Delete the synchronous forms in the same slice and convert every caller: 12 surface fakes, 19 runner-test sites, 4 `Antiphon.Tests` sites. Each is a one-line edit inside an async method or an async-capable lambda. The slice therefore builds both test projects | Two Code dispatches (`Antiphon.Tests` would not compile between them and the branch would be unlandable mid-way; the budget is for one); keeping the synchronous forms as test seams (a new production caller reopens the hazard with no compiler signal; CARD-1362 D-5 makes the same call for the server) |
| D-5 | The witness models the thread pool with a single-thread `SynchronizationContext` pump (`SingleThreadContinuationPump`, a dedicated background thread, shape of `TurnEndGapDeliveryHarness.ContinuationQueue`, in the runner test project so CARD-1362 S1 can link it with a `<Compile Include>` the way `FakeHerdrServer.cs` is linked today). It drives the real `PhoneHomeCommandDispatcher` over `PhoneHomeRuntimeAdapter` over a real `SessionRunnerRuntime` on a `FakeTimeProvider`, with one tracked `RunnerSession` bound to a `StallingChild` whose `ReadScreenAsync` returns `tcs.Task.WaitAsync(ct)` and records its token. Request A is a `Snapshot` frame for that session posted to the pump; request B is a `List` frame posted next (the inventory read the server's watchdog depends on). Pass: B answers while A is still pending, then advancing the clock past the deadline ends A as a 503 `herdr_unreachable` error frame with the child's token cancelled and the test's token not cancelled. At base the pump thread is inside `GetResult()`, so B cannot run and a 2 s bounded wait fails the assertion: red by behaviour, with no new symbol referenced | A Kestrel loopback host with two concurrent HTTP requests (not red at base: Kestrel serves the second request on another pool thread; only pool exhaustion would show it, and `ThreadPool.SetMaxThreads` is process-wide); calling `GetSnapshotAsync` directly (red only by build); a `Task.Delay` child (wall-clock and nondeterministic); a second pty-shaped session as request B (needs a tracked childless session the runtime's disposal has never been exercised with; `List` is in-memory and the better stand-in) |
| D-6 | Narrow green is `/*/*/HerdrRunnerSessionTests/*` (7 methods): the real `HerdrPaneChild`, `HerdrClient` and `FakeHerdrServer` `pane.read` path now served through `GetSnapshotAsync`, the content-delta fold cases, and the pty-host untouched case. `Antiphon.Tests` gets one build with `/*/*/PhoneHomeSpillTransportTests/*` (3 methods, Integration) as the smallest class that constructs a converted surface fake | `/*/*/PhoneHomeCommandDispatcherTests/*` (43 methods, none exercises the `Snapshot` operation, so it proves nothing about the change at ten times the cost; TestDesign may add one exact method for the new catch if it writes one) |
| D-7 | Scope ends at the snapshot read. `GET /sessions/{id}` (`RefreshStatusAsync`) and adoption's `pane.read` keep the caller's token and no runner-side deadline; a follow-up card is filed only if CARD-1362 S4's per-request timing shows them stalling. Master only: the hazard is identified, not observed on the rc line, and the same reasoning as CARD-1362 D-8 applies | Bounding `RefreshStatusAsync` here (two more routes and the adoption path for a hazard the card does not name) |
| D-8 | One slice, 30 to 45 minutes of Code: about 10 for the core (session, runtime, settings, route, surface, adapter, dispatcher), 10 to 15 for the 35 one-line conversions, 15 for the pump and the witness. Red commit (`test(CARD-1365): ... (S1 red)`) first, then the green commit; push after each | Splitting the conversions into their own slice (see D-4) |

## Contracts

### Settings

```csharp
// src/Antiphon.SessionRunner/HerdrSettings.cs, bound at SessionRunner:Herdr
/// <summary>
/// CARD-1365: bound on one pane.read served by GET /sessions/{id}/snapshot and the phone-home
/// Snapshot operation. A read that exceeds it answers 503 herdr_unreachable; the mirror is never
/// substituted. Floor 250 ms. Keep it inside the server's AgentSession:RunnerLiveReadTimeoutMs.
/// </summary>
public int PaneReadTimeoutMs { get; set; } = 3_000;
```

### Runtime and session

```csharp
// SessionRunnerRuntime
private readonly TimeSpan _herdrPaneReadTimeout;   // ctor: Math.Max(250, _herdrClient?.Settings.PaneReadTimeoutMs ?? new HerdrSettings().PaneReadTimeoutMs)
public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
    GetSession(sessionId).GetSnapshotAsync(_labelClock, _herdrPaneReadTimeout, ct);

// SessionRunnerRuntime.RunnerSession (replaces GetSnapshot())
public async Task<RunnerSnapshotDto> GetSnapshotAsync(TimeProvider clock, TimeSpan paneReadTimeout, CancellationToken ct)
{
    if (_pendingReason is not null)
        throw new HerdrBackendUnavailableException("Herdr is unreachable; session is pending adoption.");

    if (_herdrChild is { } herdr)
    {
        using var deadline = new CancellationTokenSource(paneReadTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            var screen = await herdr.ReadScreenAsync(linked.Token);
            if (screen is not null)
            {
                lock (_gate)
                {
                    // CARD-0164 fold, unchanged.
                    _lastSequence = Math.Max(_lastSequence, Math.Max(screen.Revision, screen.ContentSequence));
                    return new RunnerSnapshotDto(_sessionId, screen.Text, screen.Text, _lastSequence, _startedAt, _acceptedStartedAt);
                }
            }
        }
        catch (HerdrBackendUnavailableException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new HerdrBackendUnavailableException(
                $"Herdr pane.read for session {_sessionId:D} exceeded {paneReadTimeout.TotalMilliseconds:N0} ms.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Herdr pane.read failed for snapshot of {SessionId}", _sessionId);
        }
    }

    lock (_gate)
        return new RunnerSnapshotDto(_sessionId, _liveBuffer.ToString(), _screen?.GetScreenText() ?? "", _lastSequence, _startedAt, _acceptedStartedAt);
}
```

Rules the implementation keeps (R-n pin them):

- The pending-adoption refusal comes first and is unchanged.
- The pty-host branch never awaits; a pty-host snapshot is the same in-memory read as today.
- The content-delta fold and the DTO shape are unchanged; `TaskCanceledException` is an
  `OperationCanceledException`, so the two `when` filters cover it.
- The deadline message carries the session id and the bound, never screen text.
- A non-deadline, non-unavailable failure keeps the Debug log and the mirror fallback.

### Routes and the phone-home surface

```csharp
// Program.cs
app.MapGet("/sessions/{id:guid}/snapshot", async (Guid id, SessionRunnerRuntime runtime, CancellationToken cancellationToken) =>
    Results.Ok(await runtime.GetSnapshotAsync(id, cancellationToken)))
    .AddEndpointFilter(HerdrUnreachableFilter);

// PhoneHomeCommandDispatcher.cs
public interface IPhoneHomeRuntimeSurface { /* ... */ Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct); /* ... */ }
PhoneHomeOperation.Snapshot => Result(request, await _runtime.GetSnapshotAsync(ReadSessionId(request), ct)),
// new catch, before the generic one:
catch (HerdrBackendUnavailableException ex) { return Error(request, HerdrProblemTypes.Unreachable, ex.Message, 503); }

// PhoneHomeRuntimeAdapter.cs
public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => _runtime.GetSnapshotAsync(sessionId, ct);
```

### Witness (test contract)

`tests/Antiphon.SessionRunner.Tests/HerdrSnapshotBoundedReadTests.cs`, one method,
`C1365_Stalled_pane_read_frees_the_pump_for_a_list_and_answers_herdr_unreachable_at_the_deadline`,
written only against symbols that exist at base:

1. `SessionRunnerSettings { SessionLogPath = <temp root> }`, `FakeTimeProvider clock`,
   `new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance, timeProvider: clock)`.
2. `var session = new SessionRunnerRuntime.RunnerSession(id, settings, new SessionRunnerEventHub(), NullLogger.Instance)`;
   `session.BindChildForTest(child)` with `StallingChild : ISessionChild` (shape of `SeatChild` in
   `TerminalSeatReleaseTests`; `ReadScreenAsync(ct)` stores `ct` in `ReadToken`, increments `Reads`,
   returns `_read.Task.WaitAsync(ct)`; `Release()` completes `_read` with a `ChildScreen`);
   `runtime.Track(session)`.
3. `var dispatcher = new PhoneHomeCommandDispatcher(new PhoneHomeRuntimeAdapter(runtime, RunnerBuildIdentity.Resolve()), new PhoneHomeSettings { Enabled = true, AllowedCwd = <root>, CapacityStatePath = <root>/capacity })`;
   frames built as in `PhoneHomeCommandDispatcherTests` (`PhoneHomeFrameKind.Request`,
   `Operation: Snapshot` with `{ sessionId }` payload serialised with `PhoneHomeFraming.Json`;
   `Operation: List` with no payload).
4. `await using var pump = new SingleThreadContinuationPump();`
   `var slow = pump.InvokeAsync(() => dispatcher.DispatchAsync(snapshotFrame, cts.Token));`
   `var probe = pump.InvokeAsync(() => dispatcher.DispatchAsync(listFrame, cts.Token));`
5. **Red assertion.** `var list = await probe.WaitAsync(TimeSpan.FromSeconds(2));` then
   `list.Kind.ShouldBe(PhoneHomeFrameKind.Result)`, `child.Reads.ShouldBe(1)`,
   `slow.IsCompleted.ShouldBeFalse()`. At base the pump thread is blocked in `GetResult()`, the
   probe never runs, `WaitAsync` throws `TimeoutException`: red. After the fix the snapshot yields at
   its first incomplete await and the probe answers at once.
6. `child.ReadToken.CanBeCanceled.ShouldBeTrue()` (a bound was attached; `CancellationToken.None` cannot be cancelled).
7. `clock.Advance(TimeSpan.FromSeconds(10));` (past any sane default; the exact default is not a
   base symbol, see TestDesign note) then `var reply = await slow.WaitAsync(TimeSpan.FromSeconds(2));`
   `reply.Kind.ShouldBe(PhoneHomeFrameKind.Error)`, `reply.ErrorCode.ShouldBe(HerdrProblemTypes.Unreachable)`,
   `reply.StatusCode.ShouldBe(503)`, `child.ReadToken.IsCancellationRequested.ShouldBeTrue()`,
   `cts.IsCancellationRequested.ShouldBeFalse()`.
8. `finally { child.Release(); }` so a base run's blocked thread returns and the pump and runtime dispose.

The 2 s waits are failure bounds, never sleeps: on the green path both complete immediately, and
the deadline is driven by `clock.Advance`, whose timer callback cancels the deadline source on
the test thread and posts the continuation to the pump.

`tests/Antiphon.SessionRunner.Tests/SingleThreadContinuationPump.cs`: a `SynchronizationContext`
with an unbounded channel drained by one dedicated background `Thread` that sets itself as the
current context around each callback; `InvokeAsync<T>(Func<Task<T>>)` posts the call and returns
its task; `DisposeAsync` completes the channel and waits at most 1 s for the thread (a thread
still blocked, the red case before `Release`, is abandoned and named in the failure message).

## Slices

### S1. Bounded async snapshot on every hop [30 to 45 min]

Red commit `test(CARD-1365): pin that a stalled pane.read frees the thread and ends at the deadline (S1 red)`:
`tests/Antiphon.SessionRunner.Tests/SingleThreadContinuationPump.cs`,
`tests/Antiphon.SessionRunner.Tests/HerdrSnapshotBoundedReadTests.cs`. CP-1 runs red here
(1 executed, 1 failed: the probe wait times out).

Green commit `feat(CARD-1365): serve the herdr snapshot asynchronously under a pane.read deadline (S1)`:

Production (6 files):

- `src/Antiphon.SessionRunner/HerdrSettings.cs`: `PaneReadTimeoutMs`.
- `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`: `_herdrPaneReadTimeout` (constructor
  `:174-203`), `GetSnapshotAsync(Guid, CancellationToken)` replacing `GetSnapshot(Guid)` (`:1045`),
  `RunnerSession.GetSnapshotAsync(TimeProvider, TimeSpan, CancellationToken)` replacing
  `RunnerSession.GetSnapshot()` (`:3819-3868`).
- `src/Antiphon.SessionRunner/Program.cs:292-294`: async route with the request token.
- `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`: interface member (`:33`), the
  `Snapshot` arm (`:338`), the new catch before `:557`.
- `src/Antiphon.SessionRunner/PhoneHomeRuntimeAdapter.cs:104`: `GetSnapshotAsync` forward.
- `src/Antiphon.SessionRunner/appsettings.json`: no change (the default applies; the key is documented).

Surface fakes (12 one-line members, `GetSnapshot(Guid)` to `GetSnapshotAsync(Guid, CancellationToken)`
returning `Task.FromResult(...)` or throwing as before):
`tests/Antiphon.SessionRunner.Tests/RunnerCapacityCountTests.cs:199`,
`RunnerMultilinePromptDeliveryTests.cs:115`, `PhoneHomeConnectionServiceTests.cs:900`,
`TerminalSeatReleaseTests.cs:746` (forwards to the adapter), `PhoneHomeCommandDispatcherTests.cs:1362`,
`RunnerWorkspaceBindingTests.cs:756`, `CodexProviderAuthRoutingTests.cs:118`;
`tests/Antiphon.Tests/Application/PhoneHomeTaskDispatchProjectionTests.cs:682`,
`PhoneHomeRollingRunnerTests.cs:124`, `AgentTaskInputFallbackTests.cs:527`,
`PhoneHomeSpillTransportTests.cs:181`, `tests/Antiphon.Tests/Agents/PhoneHomeConnectionTests.cs:941`.

Runner-test call sites (19, `runtime.GetSnapshot(x)` to `await runtime.GetSnapshotAsync(x, CancellationToken.None)`,
all already inside async methods):
`HerdrRunnerSessionTests.cs:36,44,74,78,108,116,122,190,191`;
`SessionBufferBoundsTests.cs:61,176,195`; `PtyHostAdoptionTests.cs:59,307,313`;
`RemoteControlConditionalInputTests.cs:26,54,75,127`.

`Antiphon.Tests` call sites (4): `Agents/HerdrRealCliCanarySupport.cs:150` (async loop),
`TestHelpers/HerdrLabelFollowHttpFixture.cs:127` (route lambda becomes
`(Guid id, CancellationToken ct) => Runtime.GetSnapshotAsync(id, ct)`),
`TestHelpers/DirectSessionRunnerClient.cs:353` (`GetSnapshotAsync` becomes `async`, keeps
`ct.ThrowIfCancellationRequested()`), `Application/CodexCliDeliveryWorld.cs:312` (same).

Docs: `docs/herdr-sessions.md` (settings table row `PaneReadTimeoutMs | 3 000 | bound on one
snapshot pane.read; a deadline answers 503 herdr_unreachable`, and one sentence in the
content-delta paragraph: the snapshot route serves `pane.read` asynchronously under that bound
and never substitutes the mirror for a late read); `docs/session-runtime-invariants.md` (one
bullet at the end of the main list, before the CARD-0254 preserved-source marker: the runner's
herdr snapshot read is async on both lanes and bounded by `SessionRunner:Herdr:PaneReadTimeoutMs`;
a deadline is the unreachable class, 503 `herdr_unreachable`, which the server defers on, never an
empty screen; pinned by `HerdrSnapshotBoundedReadTests` and `HerdrRunnerSessionTests`).

Narrow green: CP-1 rerun (reruns=1), CP-2, CP-3.

## Risks and how the slice holds them

- **A deadline read as "gone".** D-3 maps it onto the class the server already defers on
  (`IsHerdrUnreachable`) and `TryGetLiveSnapshot` reads as unavailable; the witness pins the
  code and status on the phone-home lane, the existing `HerdrUnreachableFilter` pins HTTP.
- **Server retry amplification.** 503 is retryable, so a stuck herdr costs the server up to
  (retries + 1) x 3 s per snapshot read until CARD-1362's 5 s runtime deadline lands; bounded
  either way, and strictly less than today's unbounded block. No change to the resilience
  profile.
- **A converted site that drops its `await`.** Every converted read is consumed as a value
  (`.LastSequence`, `.RawOutput`, a return), which does not compile on a `Task`; the surface
  fakes change signature, so a missed one fails the build.
- **Pump dispose hanging on a red run.** `Release()` in `finally` returns the blocked thread;
  `DisposeAsync` is bounded at 1 s regardless.
- **Overlap with CARD-1362.** Different sides (server runtime versus runner session); both
  touch `Program.cs` in different regions (S4 timing middleware versus the snapshot route) and
  both add a pump helper, which CARD-1362 S1 can link from the runner test project instead of
  writing its own. Land order does not matter.
- **`FakeTimeProvider` and the deadline source.** `CancellationTokenSource(TimeSpan, TimeProvider)`
  schedules on the fake clock; `Advance` fires it synchronously, the continuation is posted to the
  pump, the test awaits with a bound. No wall-clock dependency.

## Verification design (proposal for TestDesign)

V-1 a stalled herdr `pane.read` does not hold the serving thread: a `List` request posted
behind it on the pump answers while the read is pending (witness step 5). V-2 the read ends at
the deadline on the injected clock as 503 `herdr_unreachable` with the child's token cancelled
and the caller token not cancelled (steps 6 and 7). R-1 the herdr snapshot path keeps its
semantics through the real client: content-delta fold, sticky revision, interleaving with
`GetAsync`, pty-host untouched (`HerdrRunnerSessionTests`). R-2 the converted phone-home
surface fakes and `Antiphon.Tests` callers compile and a converted fake still serves its lane
(`PhoneHomeSpillTransportTests`).

TestDesign notes: the exact default (3000) and floor (250) are not base symbols, so the witness
advances 10 s; a second method pinning the default through
`new HerdrClient(new HerdrSettings { Enabled = true, PaneReadTimeoutMs = 3000 })` and a 2 999 ms
then 1 ms advance is cheap, red by build, and TestDesign's call. A caller-cancellation case
(`cts.Cancel()` before advancing; `slow` faults with `OperationCanceledException`, the reply is
no error frame) is the same cost. Neither is required by the brief's one-witness rule.

### Checkpoints

Portable rows; the orchestrator reads `GET /api/runner-defaults` and `GET /api/session-runners`
at dispatch and names the lane in the brief. No `-Runner`, no `-Platform`. CP-3 needs the
shared test Postgres (`Antiphon.Tests` Integration lane).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1365-runner/` | witness | `/*/*/HerdrSnapshotBoundedReadTests/*` | V-1, V-2 | 1 executed, 0 failed | 1 | 7 |
| CP-2 | S1 | CP-1 | herdr-snapshot | `/*/*/HerdrRunnerSessionTests/*` | R-1 | 7 executed, 0 failed | 7 | 4 |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1365-server/` | surface-fakes | `/*/*/PhoneHomeSpillTransportTests/*` | R-2 | 3 executed, 0 failed | 3 | 18 |

CP-1 runs once at the red commit (expected 1 failed) and again at the green commit; the report
carries both lines with `reruns=1`. `Expect` tokens are TestDesign's to confirm against the
current roster.

### Cost

Ordinary floor: 29 minutes of checkpoint time plus the one red CP-1 run (about 3 minutes
without a rebuild), in one Code dispatch of 30 to 45 minutes of authoring.

## rc line

No slice is proposed for the current rc line (D-7). The hazard is identified, not observed on
the line; the change lands on master and reaches the hosts with the next line.

## Session-wait checklist

Not applicable: no slice adds or changes a session that waits for input.

--- next stage ---
next: test-design
handoff: Design the verification for CARD-1365 from docs/superpowers/plans/2026-10-19-card-1365-async-herdr-snapshot-route-plan.md: confirm the single-thread pump witness through PhoneHomeCommandDispatcher (red by behaviour at base, deadline on FakeTimeProvider), decide the optional default-pin and caller-cancellation methods, and close CP-1..CP-3 with real roster counts.
artifact: docs/superpowers/plans/2026-10-19-card-1365-async-herdr-snapshot-route-plan.md
