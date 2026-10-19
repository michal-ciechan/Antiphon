# CARD-1362: bounded async runner reads in AgentSessionRuntime

Date: 2026-10-19. Stage: Plan. Target: **master only** (see "rc line" below).
Next: **test-design**. The brief does not fold TestDesign into this dispatch, so the
`### Checkpoints` proposal at the end is input to that stage, not a closed list.

Card: CARD-1362 (`d2db565d-d792-4b3b-935b-2463fc06cd3b`, Antiphon board), filed by
Investigate 7e43a724 (CARD-1359). Evidence:
[2026-10-19-card-1359-c540-child-launch-stall.md](../../investigations/2026-10-19-card-1359-c540-child-launch-stall.md)
on `feat/card-task-7e43a724` at `87cb3c14` (fetched, read; not merged).
Branch `feat/card-task-66beda91`, base `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`
(equal to `origin/master` at inspection). Owners read first:
[docs/session-runtime-invariants.md](../../session-runtime-invariants.md),
[docs/resilience.md](../../resilience.md), [docs/testing-and-build.md](../../testing-and-build.md),
[docs/logs.md](../../logs.md), [docs/project-context.md](../../project-context.md).

This Plan changes documentation only. No build, test, card write or dispatch ran.

## Outcome in one paragraph

`AgentSessionRuntime` turns every runner read into a thread-pool block: five
`_runnerClient.*Async(...).GetAwaiter().GetResult()` sites feed 68 production call sites in
16 files, including the 10 s watchdog, the queue service's delivery and
verification loops, and the remote-control, compaction, policy-refresh and recovery sweeps.
Only the List read carries its own deadline (3 s); the get, buffer and snapshot reads sit
under the admitted-read budget of up to 100 s. The plan makes the runtime's reads async
with one bounded deadline per read (`AgentSession:RunnerLiveReadTimeoutMs`, default 5000),
converts every production caller to `await` the new forms, deletes the sync wrappers, logs
per-request service time on the runner, and proves with a single-thread continuation pump
and a runner fake that blocks that a runner which never answers cannot keep an unrelated
server continuation from running. Behaviour is otherwise unchanged: a failed or timed-out
read keeps today's outcome class (List throws; `TryGet*` reads as "unavailable", never as
"gone"), and the in-process test adapters keep bypassing the runner.

## Ground truth

| Card assumption | Code at `6ec71d42` | Consequence for the plan |
|---|---|---|
| The five reads use `CancellationToken.None` with no deadline | True for the token. `SessionRunnerHttpClient.ListAsync` (`:557-569`) adds its own 3 s linked deadline (`SessionRunner:ListTimeoutSeconds`, owner cap `runner-list` 3 s). `GetAsync`, `GetBufferAsync`, `GetSnapshotAsync` (`:571-596`) run under `ResilienceBudget` with `RequestTimeoutSeconds` 100 s, 10 s attempts and up to 6 retries | The hazard is the blocked thread, not only the missing deadline: a List pins a thread for up to 3 s, a snapshot or buffer read for up to 100 s. Bound at the runtime (D-2), keep the client budgets for the other callers |
| Called by the watchdog every 10 s and about 20 queue-service sites | `WatchdogService.ScanAsync` (`:52,68`) under `WatchdogHostedService` (`PeriodicTimer`, `ScanIntervalMs` 10 000). `SessionMessageQueueService.cs` 32 sites (15 snapshot, 11 metadata, 3 `IsLiveOrUnknown`, one each of list, live-or-unknown list and buffer), `.Expectations.cs` 4 and `.DispatchBrief.cs` 1. Also `RemoteControlRecoveryService` 4 wrapper sites plus a direct `_runner.GetAsync(...).GetResult()` at `:767`, `BootReplyWatchdogService:424`, `ContextCompactionService:188`, `PolicyRefreshService:483,512`, `ApiErrorRecoveryService:284`, `AgentChannelService:176,202,203`, `SubscriptionUsageMonitorService:112`, `OrchestratorService:349,1084`, `ScheduleService:277,926`, `PoolDelegateRelease:75`, `AgentSessionService:2108,2223,2231`, `DiagnosticsEndpoints:27`, and the runtime's own `:1749,1860,2182,2198,2207` | 68 call sites in 16 production files, every one inside an `async` method or a private sync helper whose only callers are async. The conversion is mechanical but too large for one slice; S2 takes the queue service, S3 the rest |
| 8 cores, nothing raises the thread-pool minimum | No `SetMinThreads` under `server/` or `src/` | Not changed (D-1 rejected alternative). .NET's portable pool notifies blocking for `GetResult` waits and injects threads faster than one or two per second, but injection is bounded and `HttpClient` response continuations still queue behind pinned threads; the plan does not depend on the exact growth curve |
| The runner logs no per-request timing | `src/Antiphon.SessionRunner/appsettings.json` overrides `Microsoft.AspNetCore` to Warning; `Program.cs` maps no request logging, only `UseRunnerExceptionMapping` (`SessionReadLaunchRoutes.cs:27`). `Serilog.AspNetCore` 10 is referenced | Add a runner-owned timing middleware with an injected clock (D-7); a source context outside `Microsoft.AspNetCore` is not filtered |
| A slow runner is the slow party | Unknown (investigation verdict). On the runner, `GET /sessions` is an in-memory `List()` (`SessionRunnerRuntime.cs:974`); `GET /sessions/{id}` refreshes a herdr pane (`GetAsync`, one `pane.get`); `GET .../snapshot` for herdr does `ReadScreenAsync(...).GetResult()` (`:3819-3830`) | Runner-side sync-over-async on the herdr snapshot is out of this card's scope; the timing log (S4) is what will attribute the next stall. Noted for a follow-up card, not fixed here |
| `TryGetLiveSnapshot` false means the session is gone | No: false is "unavailable" (any exception, Debug log at `:1807`); the invariants say probes that act only on a confirmed live session skip an unconfirmed one, and destructive "not live" decisions read `ListLiveOrUnknownSessions`, whose List failure throws | A deadline must map onto the same two outcome classes (D-3). A timed-out List throws; a timed-out `TryGet*` returns null |
| Tests go through the runner | Mostly not: `_testAdapters` short-circuits every read; 36 test call sites in 9 files use the sync wrappers, 3 of them with a real `StaticSessionRunnerClient` | The sync wrappers can be deleted once those 36 sites await the new forms (S5); the adapter path is unchanged and keeps the existing classes green |
| `GET /api/runner-defaults` and `GET /api/session-runners` describe the lanes | Not reachable from this Linux mirror (`localhost:17202` refused). Not embedded here | The orchestrator reads both at dispatch. Every checkpoint below is portable: omit `-Runner` and `-Platform`; the server rows need the shared test Postgres (`Antiphon.Tests` Integration lane), the runner row is loopback in-process |

## Decisions

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | Async-first reads on `AgentSessionRuntime` with the same semantics as today: `ListLiveSessionsAsync`, `ListLiveOrUnknownSessionsAsync`, `ListUnknownSessionsAsync`, `IsLiveOrUnknownAsync(Guid, string?, ct)`, `IsLiveOrUnknownAsync(AgentSession, ct)`, `TryGetLiveSnapshotAsync` (`Task<AgentSessionLiveSnapshot?>`), `TryGetLiveMetadataAsync` (`Task<AgentSessionLiveMetadata?>`), `GetBufferSnapshotAsync`, private `GetRunnerSequenceOrDefaultAsync`. The test-adapter branch stays first and synchronous inside each | A runner inventory cache refreshed by the event stream (changes liveness staleness, which CARD-0679 pinned for the phone-home directory only); raising `ThreadPool.SetMinThreads` (hides the block, keeps it) |
| D-2 | One deadline per runtime read, at the runtime, from `AgentSessionSettings.RunnerLiveReadTimeoutMs` (default 5000, floor 250 in code, `Math.Max` like the class's other bounds), via `CancellationTokenSource(delay, _timeProvider)` linked to the caller token. Snapshot and buffer share one deadline inside `TryGetLiveSnapshotAsync`. List keeps the client's 3 s cap as well (the shorter wins) | A new resilience profile cap on get/buffer/snapshot in `SessionRunnerHttpClient` (would shorten the diagnostics bundle, reconciliation and `RunnerTerminalSession` reads, which are not hot paths); 3 s (a herdr snapshot measured 870 ms in the failed run's healthy phase); 10 s (equals one attempt timeout, so the deadline never pre-empts a retry); a constructor parameter (ripples through 20 test constructions) |
| D-3 | Deadline outcomes keep today's classes. List family: the `OperationCanceledException` propagates, exactly as the 3 s list cap does now, so `WatchdogHostedService` logs "Watchdog scan failed" and the next tick retries; `IsLiveOrUnknownAsync` propagates too (a destructive caller must not read a timeout as "gone"). `TryGet*Async` return null and log at Debug with the existing message. Caller cancellation is distinguished from the deadline by `ct.IsCancellationRequested` and rethrown | Returning an empty live set on timeout (every session would read as dead: the exact failure CARD-0679 forbids); treating a timeout as live (an unconfirmed session must not receive watchdog answers) |
| D-4 | Callers convert in place. Private sync helpers become async with their callers: `HeldBackTypingBlocksTheComposer` (2 callers), `RemoteControlRecoveryService.TryPid` (3 callers, also converting its own direct `_runner.GetAsync(...).GetResult()`), `BootReplyWatchdogService.ComposerHead` (1), `PolicyRefreshService.IsTranscriptBound` (1). The diagnostics lambda becomes async. No fire-and-forget, no `Task.Run` | Wrapping the sync wrappers in `Task.Run` (moves the block, keeps a pinned thread) |
| D-5 | Delete the sync wrappers in S5 after the 36 test call sites await the new forms; a source pin test forbids `GetAwaiter().GetResult()`/`.Result` on `_runnerClient` or `_runner` read calls under `server/` | Keeping the wrappers as test seams (a new production caller re-opens the hazard with no compiler signal) |
| D-6 | The witness models the thread pool with a single-thread `SynchronizationContext` pump (new `SingleThreadContinuationPump` test helper, shape of `TurnEndGapDeliveryHarness.ContinuationQueue`), a `BlockingSessionRunnerClient` fake whose reads return `TaskCompletionSource` tasks the test releases, and a probe continuation posted to the pump as the stand-in for an unrelated server continuation such as the dispatcher tick. Pass: the probe completes before the runner is released. At base the pump thread is inside `GetResult()`, so the probe cannot run until release and a 2 s bounded wait fails the assertion. The deadline is proved on `FakeTimeProvider` | `ThreadPool.SetMaxThreads` in-test (process-wide, poisons the shared assembly); the real `AgentTaskDispatcher.TickAsync` as the probe (DB and dispatch graph for no extra proof: a free thread is a free thread); a wall-clock threshold (nondeterministic) |
| D-7 | Runner request timing is a runner-owned middleware (`RunnerRequestTiming`, `app.UseRunnerRequestTiming()` outermost, before `UseRunnerExceptionMapping`) with `TimeProvider.GetTimestamp`/`GetElapsedTime`, one line per request under `Antiphon.SessionRunner.RunnerRequestTiming`: method, route template, status, elapsed ms, session id when the route has one. Level: Debug below `SessionRunner:RequestLog:SlowMs` (500), Information at or above it, Warning at or above `StallMs` (5000) or on a 5xx or exception. `/events` and the phone-home socket log at Debug regardless (their elapsed is a stream lifetime). Fast requests therefore leave no line at the default level; slow ones always do | `UseSerilogRequestLogging` (clock not injectable; the level test would sleep); Information for every request (about 20 polls per second on a loaded desktop, roughly 300 MB a day against a 50 MB times 14 file backstop) |
| D-8 | Master only. No slice is urgent for the rc line: the mechanism is identified but not shown to be the 2026-10-19 trigger, two reruns did not reproduce it, and S1-S3/S5 touch 68 call sites in 16 files, which release-gates classifies as a refactor. If the stall recurs on the rc line before the next cut, S4 alone (runner timing, two files) is the cherry-pick candidate for diagnosis, not a fix | Landing S1 on the rc line as a "fix" (nothing on the line is proven broken by it) |
| D-9 | Slices are 45 to 60 minutes of Code each, one red-first witness and one narrow green per slice, no whole-lane run. S6 is optional and may be dropped by the orchestrator | A single Code dispatch (the conversion alone is two hours) |

## Contracts

### Runtime read API (S1)

```csharp
// AgentSessionSettings
public int RunnerLiveReadTimeoutMs { get; set; } = 5_000;

// AgentSessionRuntime (public unless noted; every method takes ct last)
Task<IReadOnlyList<Guid>> ListLiveSessionsAsync(CancellationToken ct);
Task<IReadOnlyList<Guid>> ListLiveOrUnknownSessionsAsync(CancellationToken ct);
Task<IReadOnlyList<Guid>> ListUnknownSessionsAsync(CancellationToken ct);
Task<bool> IsLiveOrUnknownAsync(Guid sessionId, string? runnerId, CancellationToken ct);
internal Task<bool> IsLiveOrUnknownAsync(AgentSession session, CancellationToken ct);
internal Task<IReadOnlySet<Guid>> ListLocalLiveSessionsAsync(CancellationToken ct);
Task<AgentSessionLiveSnapshot?> TryGetLiveSnapshotAsync(Guid sessionId, CancellationToken ct);
Task<AgentSessionLiveMetadata?> TryGetLiveMetadataAsync(Guid sessionId, CancellationToken ct);
Task<AgentSessionRuntimeBufferSnapshot> GetBufferSnapshotAsync(Guid sessionId, CancellationToken ct);
private Task<long> GetRunnerSequenceOrDefaultAsync(Guid sessionId, CancellationToken ct);
```

One private helper owns the deadline:

```csharp
// The deadline runs on the injected clock so W1b/W1c advance a FakeTimeProvider instead
// of sleeping; a linked source cannot take a TimeProvider, hence two disposables.
private (CancellationTokenSource Deadline, CancellationTokenSource Linked) LiveReadDeadline(CancellationToken ct)
{
    var deadline = new CancellationTokenSource(
        TimeSpan.FromMilliseconds(Math.Max(250, _settings.RunnerLiveReadTimeoutMs)), _timeProvider);
    return (deadline, CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token));
}
```

Rules the implementation keeps (R-n below pin them):

- The `_testAdapters` / `_testBuffers` branch is checked first and never touches the runner.
- List family: any exception from the runner, including the deadline, propagates; nothing
  substitutes an empty set.
- `TryGet*Async`: any exception, including the deadline, returns null after the existing
  Debug log; a caller-token cancellation rethrows.
- `TryGetLiveMetadataAsync` still returns null for a runner status other than Running or
  Starting.
- `ListLiveSessionsAsync` still concatenates `_directory?.LiveRemoteSessionIds()` (in-memory,
  no RPC) and the test adapter keys.
- `WaitForManualTurnQuietAsync` and `SendInputAsync` (`:1860`, `:2182-2207`) await the new
  forms with the caller's token; the 100 ms poll loop is unchanged.

### Runner request timing (S4)

```csharp
// src/Antiphon.SessionRunner/RunnerRequestLogSettings.cs, bound at SessionRunner:RequestLog
public sealed class RunnerRequestLogSettings { public int SlowMs { get; set; } = 500; public int StallMs { get; set; } = 5_000; }

// src/Antiphon.SessionRunner/RunnerRequestTiming.cs
public static IApplicationBuilder UseRunnerRequestTiming(this IApplicationBuilder app);
```

Line shape (message template, so the fields are structured):
`Runner request {Method} {Route} -> {StatusCode} in {ElapsedMs} ms session={SessionId}`.
`Route` is the endpoint's route pattern (`/sessions/{id:guid}/snapshot`), never the raw
path, so one grep groups a route. The middleware never throws: a timing failure is logged at
Debug and the request proceeds. `docs/logs.md` gains the grep recipe in the `session-runner`
row.

### Source pin (S2, S3, S5)

`tests/Antiphon.Tests/Application/RunnerReadSourcePinTests.cs` reads `server/**/*.cs` from
the repo root (the `RepoRoot` shape in `DelegationHarnessCensusTests`) and asserts, per
slice, that the named files contain no call to the retired sync names
(`TryGetLiveSnapshot(`, `TryGetLiveMetadata(`, `ListLiveSessions()`, `ListLiveOrUnknownSessions()`,
`ListUnknownSessions()`, `IsLiveOrUnknown(`, `GetBufferSnapshot(`, `ListLocalLiveSessions()`)
and no `.GetAwaiter().GetResult()` or `.Result` on a `_runnerClient.`/`_runner.` read
(`ListAsync`, `GetAsync`, `GetBufferAsync`, `GetSnapshotAsync`). Each slice's method lists its
own files, so S2's method is red at base and green after S2 while S3's stays red until S3.

## Slices

Each slice: red commit first (`test(CARD-1362): ... (Sn red)`), then the green commit, push
after each. Estimated Code time in brackets.

### S1. Bounded async reads in the runtime, watchdog on them [50 min]

Files: `server/Application/Settings/AgentSessionSettings.cs` (setting),
`server/Application/Services/AgentSessionRuntime.cs` (the API above; the existing sync
wrappers stay for this slice and delegate to the async forms with `GetResult()` so S2-S5
can convert callers incrementally; the runtime's own internal uses convert now),
`server/Application/Services/WatchdogService.cs` (`:52`, `:68` await).
Tests: `tests/Antiphon.Tests/TestHelpers/SingleThreadContinuationPump.cs`,
`tests/Antiphon.Tests/TestHelpers/BlockingSessionRunnerClient.cs`,
`tests/Antiphon.Tests/Application/AgentSessionRuntimeBoundedReadTests.cs`.

Red-first witness (behavioural, through the existing `WatchdogService.ScanAsync`):

- W1 `C1362_Blocked_runner_list_does_not_hold_the_pump_during_a_watchdog_scan`: build a
  runtime on `BlockingSessionRunnerClient` and a `WatchdogService` (shared test Postgres,
  `[Category("Integration")]`, `[NotInParallel("Watchdog")]`), start `ScanAsync` on the pump,
  post a probe continuation to the pump, assert the probe completes within 2 s while the
  runner is still unanswered, then release the runner with an empty list and assert the
  scan returns 0. The runtime takes a `FakeTimeProvider` that the test never advances, so
  the 5 s deadline cannot fire during the probe wait. At base the probe times out.
- W1b `C1362_List_deadline_throws_on_the_injected_clock`: `FakeTimeProvider`, runner never
  answers, advance 5 s, `ListLiveSessionsAsync` faults with `OperationCanceledException`,
  the caller token is not cancelled. Red at base by build (new API).
- W1c `C1362_Snapshot_deadline_reads_as_unavailable`: same clock, `TryGetLiveSnapshotAsync`
  returns null at 5 s, and a cancelled caller token rethrows instead. Red at base by build.

Narrow green: `/*/*/AgentSessionRuntimeBoundedReadTests/*` and `/*/*/WatchdogServiceTests/*`
(10 methods; adapter path unchanged).

### S2. Queue service delivery and verification sites [60 min]

Files: `server/Application/Services/SessionMessageQueueService.cs` (32 sites, lines listed
in Ground truth; `HeldBackTypingBlocksTheComposer` becomes `...Async`),
`server/Application/Services/SessionMessageQueueService.Expectations.cs` (4 sites),
`server/Application/Services/SessionMessageQueueService.DispatchBrief.cs` (1 site).
Pattern: `if (await _runtime.TryGetLiveSnapshotAsync(id, ct) is not { } snapshot) ...`;
the `ct` is the enclosing method's token in every site (all 37 are inside async methods
that carry one). If Code runs past the hour, the five list-family sites (`:904`, `:1748`,
`:2201`, `:5488`, `:5663`) and the buffer site (`:5716`) carry into S3 and the S2 pin
method excludes those names; say so in the report.
Tests: `RunnerReadSourcePinTests.S2_queue_service_awaits_runner_reads` (red at base).

Narrow green: `/*/*/SessionMessageQueueWedgedHeadTests/*` (34 methods, Slow) and
`/*/*/ExpectationSchedulingTests/*` (5). TestDesign may add a pump witness through
`TryPollLocalCommandAsync` if a harness makes it cheaper than 10 minutes; not required.

### S3. Remaining sweeps, endpoints and services [50 min]

Files (sites): `RemoteControlRecoveryService.cs` (`:474,625,631,767`; `TryPid` to
`TryPidAsync`, callers `:336,433,640`), `BootReplyWatchdogService.cs` (`:424`, `ComposerHead`
to `ComposerHeadAsync`, caller `:355`), `ContextCompactionService.cs` (`:188`),
`PolicyRefreshService.cs` (`:483-484,512`, `IsTranscriptBound` to `IsTranscriptBoundAsync`,
caller `:219`), `ApiErrorRecoveryService.cs` (`:284`), `AgentChannelService.cs`
(`:176,202,203`), `SubscriptionUsageMonitorService.cs` (`:112`), `OrchestratorService.cs`
(`:349,1084`), `ScheduleService.cs` (`:277,926`), `PoolDelegateRelease.cs` (`:75`),
`AgentSessionService.cs` (`:2108,2231`; the sync `GetBuffer` at `:2223` has no production
caller, only three test callers, and is deleted in S5 with the wrappers),
`server/Api/Endpoints/DiagnosticsEndpoints.cs` (`:27`, lambda becomes async).
Tests: `RunnerReadSourcePinTests.S3_sweeps_and_endpoints_await_runner_reads` (red at base).

Narrow green: `/*/*/BootReplyWatchdogTests/*` (12), `/*/*/RemoteControlRecoveryTests/*` (17),
`/*/*/PolicyRefreshServiceTests/*` (21 methods, 5 argument rows),
`/*/*/AgentChannelServiceIntegrationTests/*` (6), `/*/*/SubscriptionUsageMonitorTests/*` (14).
`ContextCompactionSweepTests` and `ScheduleSweepTests` are the fallback if a converted site
in those services turns out to be covered nowhere else; TestDesign decides from the diff.

### S4. Runner per-request timing [45 min]

Files: `src/Antiphon.SessionRunner/RunnerRequestLogSettings.cs`,
`src/Antiphon.SessionRunner/RunnerRequestTiming.cs`, `src/Antiphon.SessionRunner/Program.cs`
(`Configure<RunnerRequestLogSettings>`, `app.UseRunnerRequestTiming()` before
`UseRunnerExceptionMapping`), `src/Antiphon.SessionRunner/appsettings.json` (no change to
the `Microsoft.AspNetCore` override; the new source context is not under it),
`docs/logs.md` (`session-runner` row: the line shape and
`grep "Runner request" session-runner-*.log | awk '$NF+0 >= 1000'`-style recipe).
Tests: `tests/Antiphon.SessionRunner.Tests/RunnerRequestTimingTests.cs`: an in-process
`WebApplication` on `http://127.0.0.1:0` (the `BuildSlotTestHost` shape) with
`FakeTimeProvider` registered, a `ListLoggerProvider` capture, the middleware and three
routes: one that advances the clock 20 ms (Debug: no line at Information), one 600 ms
(Information line with route template and session id), one 6 s (Warning), plus a throwing
route (Warning with status 500). Red at base by build (new extension).

Narrow green: `/*/*/RunnerRequestTimingTests/*` (4 methods) and, because `Program.cs`
changed, `/*/*/BuildSlotEndpointTests/*` as the existing in-process route class.

### S5. Delete the sync wrappers, migrate tests, pin and document [55 min]

Files: `server/Application/Services/AgentSessionRuntime.cs` (delete `ListLiveSessions`,
`ListLocalLiveSessions`, `ListUnknownSessions`, `ListLiveOrUnknownSessions`, both
`IsLiveOrUnknown`, `TryGetLiveSnapshot`, `TryGetLiveMetadata`, `GetBufferSnapshot`,
`GetRunnerSequenceOrDefault`) and `AgentSessionService.GetBuffer` (its three callers at
`AgentSessionServiceIntegrationTests:126,201,202` await `GetBufferAsync`), the 36 runtime
test call sites in
`AgentSessionServiceIntegrationTests`, `PhoneHomeImmediateSendTests`,
`AgentSessionRuntimeTests`, `AgentChannelServiceIntegrationTests`,
`PhoneHomePendingInventoryTests`, `PhoneHomeStrandedQueueTests`, `AgentTaskPoolTests`,
`Agents/RealCliStubBServerHarness.cs`, `TestHelpers/QueuedReceiptAssertions.cs`, and the
`WaitUntilAsync(() => runtime.GetBufferSnapshot(...))` lambdas in `WatchdogServiceTests`
(become an async predicate or a small `WaitUntilAsync(Func<Task<bool>>)` overload).
Docs: `docs/session-runtime-invariants.md` (new bullet: runner reads from the runtime are
async and bounded by `AgentSession:RunnerLiveReadTimeoutMs`; a timeout is "unavailable" for
`TryGet*` and a thrown cancellation for the list family; never an empty live set),
`docs/resilience.md` ("Budget" section: the runtime's live-read deadline sits inside the
runner-read owner deadline and is not a resilience profile).
Tests: `RunnerReadSourcePinTests.S5_runtime_has_no_blocking_runner_reads` (red at base: the
five `GetResult()` sites exist).

Narrow green: `/*/*/RunnerReadSourcePinTests/*` (3 methods, all green now),
`/*/*/AgentSessionRuntimeTests/*` (23 methods, 8 argument rows, Slow) and
`/*/*/WatchdogServiceTests/*` (10).

### S6 (optional). Per-minute runner timing summary [45 min]

A `RunnerRequestTimingSummary` singleton (per route: count, max, mean, count at or above
`SlowMs`) flushed once a minute at Information by a small hosted service, so a quiet log is
positive evidence that the runner served fast rather than an absence. Witness: the summary
line after two recorded requests on the injected clock. The orchestrator may drop this
slice; S4 alone satisfies the card.

## Risks and how the slices hold them

- **Semantics drift on timeout.** The only new outcome is "deadline before answer". D-3
  maps it onto the two outcome classes callers already handle, and W1b/W1c pin both. A
  caller that today catches `TaskCanceledException` from the 3 s list cap keeps working
  because `OperationCanceledException` is its base.
- **A converted site that silently drops its `await`.** The source pin forbids the sync
  names, and every converted read is consumed as a value (`is not { }`, `&&`, `!`, `.Contains`),
  which does not compile on a `Task`; the one bare-statement shape CS4014 would only warn on
  does not occur. Review still reads the 12 `IsLiveOrUnknownAsync` sites, because a wrong
  token there (`CancellationToken.None` instead of the caller's) compiles.
- **Sync lambdas.** `OrchestratorService:1084` and `PoolDelegateRelease:75` are inside async
  methods (verified); `DiagnosticsEndpoints:27` is a sync minimal-API lambda and becomes
  `async (...) => ...`.
- **The watchdog `[NotInParallel("Watchdog")]` group.** W1 joins it so a blocked pump cannot
  overlap another watchdog test's shared state.
- **Runner log volume.** Fast requests are Debug and never written at the default level;
  the slow threshold is configurable. The 50 MB times 14 backstop is untouched.
- **Herdr snapshot on the runner is still sync-over-async** (`SessionRunnerRuntime.cs:3819`).
  Out of scope; file a card when S4's log shows it mattering.

## Plan verification proposal (superseded by the TestDesign section below)

V-1 a runner that never answers does not hold the pump while a watchdog scan awaits it
(W1). V-2 list and snapshot deadlines on the injected clock keep their outcome classes
(W1b, W1c). V-3 the runner writes one timing line per slow request with route template,
status, elapsed and session id, and none at the default level for a fast one. R-1 the
watchdog's existing rules still answer on the adapter path. R-2 the queue service's
delivery, late-confirm and expectation paths are unchanged. R-3 the converted sweeps keep
their decisions. R-4 the runtime's existing behaviour on the adapter and static-client
paths. R-5 no production file calls a retired sync name or blocks on a runner read.

### Proposed checkpoints (superseded; not the closed list)

Portable rows; the orchestrator reads `GET /api/runner-defaults` and
`GET /api/session-runners` at dispatch and names the lane in the brief. No `-Runner`, no
`-Platform`. `tests/Antiphon.Tests` rows need the shared test Postgres.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1362-s1/` | bounded-reads | `/*/*/AgentSessionRuntimeBoundedReadTests/*` | V-1, V-2 | 3 executed, 0 failed | 3 | 15 |
| CP-2 | S1 | CP-1 | watchdog-s1 | `/*/*/WatchdogServiceTests/*` | R-1 | 10 executed, 0 failed | 10 | 4 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1362-s2/` | queue-wedged | `/*/*/SessionMessageQueueWedgedHeadTests/*` | R-2 | all listed, 0 failed | 34 | 20 |
| CP-4 | S2 | CP-3 | expectations | `/*/*/ExpectationSchedulingTests/*` | R-2 | 5 executed, 0 failed | 5 | 3 |
| CP-5 | S2 | CP-3 | pin-s2 | `/*/*/RunnerReadSourcePinTests/S2_queue_service_awaits_runner_reads` | R-5 | 1 executed, 0 failed | 1 | 2 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1362-s3/` | sweeps | `/*/*/BootReplyWatchdogTests/*\|/*/*/RemoteControlRecoveryTests/*\|/*/*/AgentChannelServiceIntegrationTests/*\|/*/*/SubscriptionUsageMonitorTests/*` | R-3 | 49 executed, 0 failed | 49 | 18 |
| CP-7 | S3 | CP-6 | policy-refresh | `/*/*/PolicyRefreshServiceTests/*` | R-3 | all listed, 0 failed | 21 | 5 |
| CP-8 | S3 | CP-6 | pin-s3 | `/*/*/RunnerReadSourcePinTests/S3_sweeps_and_endpoints_await_runner_reads` | R-5 | 1 executed, 0 failed | 1 | 2 |
| CP-9 | S4 | `tests/Antiphon.SessionRunner.Tests -> bin-c1362-runner/` | runner-timing | `/*/*/RunnerRequestTimingTests/*` | V-3 | 4 executed, 0 failed | 4 | 6 |
| CP-10 | S4 | CP-9 | runner-routes | `/*/*/BuildSlotEndpointTests/*` | V-3 | 6 executed, 0 failed | 6 | 3 |
| CP-11 | S5 | `tests/Antiphon.Tests -> bin-c1362-s5/` | pin-all | `/*/*/RunnerReadSourcePinTests/*` | R-5 | 3 executed, 0 failed | 3 | 14 |
| CP-12 | S5 | CP-11 | runtime | `/*/*/AgentSessionRuntimeTests/*` | R-4 | all listed, 0 failed | 23 | 10 |
| CP-13 | S5 | CP-11 | watchdog-s5 | `/*/*/WatchdogServiceTests/*` | R-1 | 10 executed, 0 failed | 10 | 4 |

The combined-class filter on CP-6 uses the CARD-0403 syntax; TestDesign splits it if the
pinned TUnit version refuses four alternatives. Argument-expanded methods (CP-7, CP-12)
report more results than methods; TestDesign sets `Min` from the roster. The `Expect`
tokens are TestDesign's to confirm against the current roster.

### Proposed cost (superseded)

Ordinary floor: 106 minutes of checkpoint time across five Code dispatches (S1 19, S2 25,
S3 25, S4 9, S5 28), plus 45 to 60 minutes of authoring per slice. S6 adds one row of about
6 minutes if commissioned.

## rc line

No slice is proposed for `rc/2026-10-15` (D-8). The change is a refactor of a hazard that
is identified but not proven to be the 2026-10-19 trigger; it lands on master and reaches
the hosts with the next line. If an operator needs runner-side attribution on the current
line before then, S4 is self-contained (two runner files, one settings class, one doc row)
and is the only slice safe to cherry-pick.

## Session-wait checklist

Not applicable: no slice adds or changes a session that waits for input.


## Verification design

Stage: TestDesign (task 72243597, Frontier, 2026-10-19) at start ref
`dc223795763f34ee3f67d3b069ae6a060b615532`. Operator policy applied: per slice one red-first
witness plus one narrow green row, whole-class runs only where the slice migrates call sites
in that class, no Unit-lane rows. The table below is the closed list; the Plan's proposal
above is superseded. No source changed in this stage; the W1 proof below ran from
uncommitted test files that were deleted before the design commit.

### Inspection

- `AgentSessionRuntime.cs` `:59-100` (constructors: `TimeProvider` is injected, so a
  `FakeTimeProvider` reaches `LiveReadDeadline` without a new parameter), `:1668-1760`
  (`GetBufferSnapshot`, `ListLiveSessions`, `ListLocalLiveSessions`, `ListUnknownSessions`,
  `ListLiveOrUnknownSessions`, both `IsLiveOrUnknown`), `:1777-1838` (`TryGetLiveSnapshot`,
  `TryGetLiveMetadata`), `:1991-2005` (`GetRunnerSequenceOrDefault`). Five `GetResult()` sites
  on `_runnerClient` at `:1673`, `:1792`, `:1793`, `:1825`, `:1998`; the Plan's `:1749` etc.
  are the wrappers' internal callers. Boundary: the `_testAdapters`/`_testBuffers` branch
  precedes every runner call -> R-1, R-4. The test-adapter branch is the reason the existing
  classes stay green and is not safety-critical (a test seam).
- `WatchdogService.ScanAsync` `:47-160`: two runtime reads (`:52` list, `:68` snapshot), then
  `SendModalKeyAsync`/`SendInputAsync`; an empty live list returns 0 before the first
  `_db` query, so W1 needs a constructed, never-opened `AppDbContext` and no shared store
  (`SharedStoreWarmup` starts Postgres only for IL that references `TestDbFixture`) -> V-1.
- `SessionRunnerHttpClient.cs` `:557-596`: `ListAsync` links a 3 s `CancelAfter` and a
  `RunnerList` budget; `GetAsync`/`GetBufferAsync`/`GetSnapshotAsync` pass the caller's token
  into the resilience read. The real client honours its token; the fake below does not, so
  the runtime must await each read through `.WaitAsync(linked.Token)` for D-2 to hold against
  any client. **Contract refinement for S1:** every bounded read is
  `await _runnerClient.XAsync(..., linked.Token).WaitAsync(linked.Token)`; W1b/W1c and PC-4
  depend on it. Boundary: the client's own budgets -> out of scope (existing client tests).
- `ISessionRunnerClient` abstract members (from the compiling `StaticSessionRunnerClient`
  in `AgentSessionRuntimeTests.cs:809`): `StartAsync`, `ListAsync`, `GetAsync`,
  `GetBufferAsync`, `GetSnapshotAsync`, `GetTranscriptAsync`, `SendInputAsync`,
  `ClearLiveBufferAsync`, `ResizeAsync`, `KillAsync`, `StreamEventsAsync`; everything else has
  a default body. The fake implements exactly that set.
- `TurnEndGapDeliveryHarness.ContinuationQueue` `:685-731`: a `SynchronizationContext` over a
  channel drained by `Task.Run`. The new pump uses a dedicated thread instead, so the thread
  pool is not part of the witness at all (D-6).
- `WatchdogServiceTests.cs`: `[Category("Integration")] [NotInParallel("Watchdog")]`, 10
  methods, no argument rows; builds the runtime on the adapter path with `TimeProvider.System`
  and reads `runtime.GetBufferSnapshot(...)` inside `WaitUntilAsync(Func<bool>)` at `:415`,
  `:571` (S5 migrates these two lambdas) -> R-1.
- Rosters (counted from `[Test]` and `[Arguments]` rows at the start ref; an argument row is
  one executed result): `SessionMessageQueueWedgedHeadTests` 34 methods / 51 results (Slow,
  `NotInParallel("MessageQueue")`, drives `FlushSessionAsync` 31x and
  `FlushStrandedQueuesAsync` 9x, the loops that hold most of the 32 S2 sites);
  `ExpectationSchedulingTests` 5/5; `BootReplyWatchdogTests` 12/12 (`[NotInParallel]`);
  `RemoteControlRecoveryTests` 17/17; `PolicyRefreshServiceTests` 21/24;
  `AgentChannelServiceIntegrationTests` 6/6; `SubscriptionUsageMonitorTests` 14/14;
  `AgentSessionRuntimeTests` 23/29 (Slow, `NotInParallel("Pty")`, 3 methods on a real
  `StaticSessionRunnerClient`); `WatchdogServiceTests` 10/10; `BuildSlotEndpointTests` 6/6;
  `ContextCompactionSweepTests` 18/18; `ScheduleSweepTests` 18/18. No other class shares any
  of these names as a prefix, so the CARD-0403 `(Name*)` operands select one class each.
- Production call sites at the start ref: 62 wrapper calls in 16 files outside the runtime
  plus the direct `_runner.GetAsync(...).GetResult()` at `RemoteControlRecoveryService.cs:769`
  (the Plan's `:767`); the S2 list-family lines `:904`, `:1748`, `:2201`, `:5488`, `:5663`
  and the buffer site `:5716` are as stated. Test call sites: **81 in 13 files**, not 36 in 9:
  `PhoneHomeStrandedQueueTests` 33 (+1 comment), `PhoneHomePendingInventoryTests` 24,
  `AgentSessionRuntimeTests` 8, `AgentSessionServiceIntegrationTests` 3 (+3 `service.GetBuffer`),
  `WatchdogServiceTests` 2, `SessionMessageQueueDeliveredSpillRecoveryTests` 2,
  `AgentTaskPoolTests` 2, and one each in `QueuedReceiptAssertions`, `PhoneHomeOutageHarness`,
  `PhoneHomeSchedulePreviewTests`, `PhoneHomeImmediateSendTests`,
  `AgentChannelServiceIntegrationTests`, `RealCliStubBServerHarness`. S5's 55-minute estimate
  is low; the orchestrator should budget 75 or split the test migration from the deletion.
- S3 enclosing methods, all async: `ContextCompactionService.ProcessSessionAsync`,
  `ScheduleService.FireCoreAsync`/`BuildPreviewAsync`, `OrchestratorService.GetStateAsync`/
  `HasLiveRuntimeSessionAsync`, `ApiErrorRecoveryService.FireOneAsync`,
  `PoolDelegateRelease.IsSessionTerminalAsync`, `AgentSessionService.ResumeAsync`/
  `GetBufferAsync`, `SubscriptionUsageMonitorService.SweepAsync`,
  `AgentChannelService.LoadLiveTargetSessionAsync`/`FindMentionTargetsAsync`. The three
  services whose private sync helpers change signature (`TryPid`, `ComposerHead`,
  `IsTranscriptBound`) are the S3 green row; the one-line `await` conversions elsewhere are
  held by the compiler (a `Task` is not a `bool`/collection) and the S3 pin -> R-3.
- Runner: `Program.cs:142-151` builds the app and calls `app.UseRunnerExceptionMapping()`
  first; `appsettings.json` keeps `Microsoft.AspNetCore` at Warning under an Information
  default, so a `Antiphon.SessionRunner.RunnerRequestTiming` source logs at Information.
  `tests/Antiphon.SessionRunner.Tests` has `BuildSlotTestHost` (loopback Kestrel on
  `127.0.0.1:0`, port guard), `ListLoggerProvider`/`ListLogger<T>` (`[Level] message` sink,
  all levels enabled) and `Microsoft.Extensions.TimeProvider.Testing` 9.10.0. No test boots
  the real `Program`, so `BuildSlotEndpointTests` proves nothing about the S4 wiring; a
  source pin replaces that row -> V-3.
- `TestLaneCategoryGuardTests`: every class is `Unit` xor `Integration`. The new witness
  class is `Unit` (no store, no process); the pin class is `Unit`; the runner timing class
  follows `BuildSlotEndpointTests` (`Integration`, loopback Kestrel).

### Delivery inventory

No slice adds or changes an async delivery path. The converted reads are observations that
feed existing decisions (deliver, verify, answer, sweep); their producer, destination,
persistence and receipt are unchanged and already owned by the queue and watchdog tests.
What changes is the thread on which the observation waits and the deadline it carries.

- Substitute for the runner HTTP path: `BlockingSessionRunnerClient` (reads return
  `TaskCompletionSource` tasks; ignores the token; records the token it received). It proves
  the runtime gives up on the injected clock and keeps the outcome class; it cannot prove the
  real client's retry budget or the 3 s list cap, which existing `SessionRunnerHttpClient`
  tests own.
- Substitute for a saturated thread pool: `SingleThreadContinuationPump` (one dedicated
  thread, a `SynchronizationContext` whose `Post` queues to that thread). It proves a blocked
  read holds the only thread and an awaited read does not; it cannot prove the pool's
  injection curve, which D-1 declines to depend on.
- The adapter path (CP-2, CP-4, CP-6, CP-9) proves the converted callers still make the same
  decisions when the runtime answers in-process; it cannot prove behaviour under a slow
  runner, which W1b/W1c prove at the runtime boundary.

No transcript or UserPrompt evidence applies: no session receives input in this card.

### Proves it works now

- V-1: a runner that never answers the List does not hold the pump during a watchdog scan |
  `WatchdogService.ScanAsync` through `AgentSessionRuntime.ListLiveSessionsAsync` on a
  `FakeTimeProvider` that is never advanced | `AgentSessionRuntimeBoundedReadTests.
  C1362_Blocked_runner_list_does_not_hold_the_pump_during_a_watchdog_scan` | the probe
  continuation completes before the 2 s delay while `runner.ListCalls == 1` and the list is
  unreleased; after `ReleaseList([])` the scan returns 0. **Red at base by behaviour**
  (proof below).
- V-2a: the list deadline faults on the injected clock and never substitutes an empty set |
  runtime only | `C1362_List_deadline_throws_on_the_injected_clock` | `ListLiveSessionsAsync`
  is incomplete after `Advance(4999 ms)`, then `Should.ThrowAsync<OperationCanceledException>
  (() => task.WaitAsync(TimeSpan.FromSeconds(2)))` after `Advance(1 ms)`;
  `cts.IsCancellationRequested` false; `runner.LastListToken.IsCancellationRequested` true.
  Red at base by build (new API).
- V-2b: the snapshot deadline reads as unavailable and caller cancellation rethrows |
  runtime only | `C1362_Snapshot_deadline_reads_as_unavailable` | part one:
  `TryGetLiveSnapshotAsync(id, None)` returns null within `WaitAsync(2 s)` after
  `Advance(5000 ms)` and `runner.LastSnapshotToken.IsCancellationRequested` is true; part two
  (fresh fake and runtime): cancel the caller `cts` without advancing the clock,
  `Should.ThrowAsync<OperationCanceledException>(() => t2.WaitAsync(2 s))`. Red at base by
  build.
- V-3a..d: runner timing lines | in-process `WebApplication` on `127.0.0.1:0` with
  `FakeTimeProvider` registered, `ListLoggerProvider` capture, `UseRunnerRequestTiming()` then
  `UseRunnerExceptionMapping()`, four routes that advance the clock inside the handler |
  `RunnerRequestTimingTests`: `C1362_Fast_request_leaves_no_line_at_information` (20 ms: a
  `[Debug] Runner request GET /fast -> 200 in 20 ms` line exists and no `[Information]` or
  `[Warning]` line starting `Runner request`), `C1362_Slow_request_logs_information_with_route_
  template_and_session_id` (600 ms on `/sessions/{id:guid}/snapshot`: exactly one line
  `[Information] Runner request GET /sessions/{id:guid}/snapshot -> 200 in 600 ms session=<id>`
  with the route template, not the raw path), `C1362_Stalled_request_logs_warning` (6000 ms:
  `[Warning] ... in 6000 ms`), `C1362_Throwing_route_logs_warning_with_status_500` (handler
  throws `InvalidOperationException`, which the exception mapping does not map: the client
  sees 500 and the sink has `[Warning] Runner request GET /throws -> 500 in ...`). Red at base
  by build.
- V-3e: Program wiring | source pin | `C1362_Program_registers_timing_before_exception_mapping`
  reads `src/Antiphon.SessionRunner/Program.cs` from the repo root (`Antiphon.sln` walk) and
  asserts `app.UseRunnerRequestTiming();` occurs, `Configure<RunnerRequestLogSettings>` occurs,
  and the timing index is lower than the `app.UseRunnerExceptionMapping();` index. Red at base
  (names absent).

W1 witness as proved (Code copies it; the helpers' public shape is fixed here):

```csharp
// tests/Antiphon.Tests/TestHelpers/SingleThreadContinuationPump.cs
// SynchronizationContext + IAsyncDisposable over a BlockingCollection drained by one
// dedicated background thread that sets itself as the current context.
//   Post(d, state)                        -> queue (ObjectDisposedException after dispose)
//   Send(...)                             -> NotSupportedException
//   Task<T> InvokeAsync<T>(Func<Task<T>>) -> starts the function on the pump thread
//   Task PostProbeAsync()                 -> completes when the pump thread has run it
//   DisposeAsync                          -> CompleteAdding (thread exits when drained)
// tests/Antiphon.Tests/TestHelpers/BlockingSessionRunnerClient.cs : ISessionRunnerClient
//   ListAsync/GetAsync/GetBufferAsync/GetSnapshotAsync return TaskCompletionSource tasks
//   (RunContinuationsAsynchronously), ignore the token, record LastListToken /
//   LastSnapshotToken, count ListCalls; ReleaseList/ReleaseGet/ReleaseBuffer/
//   ReleaseSnapshot/FaultAll; GetTranscriptAsync answers empty; StreamEventsAsync yields
//   nothing; every other abstract member throws NotSupportedException.

[Category("Unit")]
public sealed class AgentSessionRuntimeBoundedReadTests
{
    [Test]
    public async Task C1362_Blocked_runner_list_does_not_hold_the_pump_during_a_watchdog_scan()
    {
        var runner = new BlockingSessionRunnerClient();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        var bus = new MockEventBus();
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-c1362-{Guid.NewGuid():N}");
        var runtime = new AgentSessionRuntime(
            runner, bus,
            Options.Create(new AgentSessionSettings { SessionLogPath = Path.Combine(tempRoot, "session-logs") }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            time, NullLogger<AgentSessionRuntime>.Instance);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=c1362_never_opened;Username=c1362;Password=c1362").Options);
        var service = new WatchdogService(
            db, runtime, new WatchdogMatcher(), new WatchdogCooldownStore(), bus,
            Options.Create(new WatchdogSettings()), time, NullLogger<WatchdogService>.Instance);

        await using var pump = new SingleThreadContinuationPump();
        var scan = pump.InvokeAsync(() => service.ScanAsync(CancellationToken.None));
        try
        {
            await WaitUntilAsync(() => Volatile.Read(ref runner.ListCalls) == 1);
            var probe = pump.PostProbeAsync();
            var first = await Task.WhenAny(probe, Task.Delay(TimeSpan.FromSeconds(2)));
            first.ShouldBe(probe, "an unrelated continuation must run on the pump while the runner list is still unanswered");
        }
        finally
        {
            runner.ReleaseList([]);
        }
        (await scan.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(0);
    }
}
```

W1 base proof (this stage, uncommitted files, isolated output `bin-c1362-w1/`, slot-gated
build then `--no-build` run of `/*/*/AgentSessionRuntimeBoundedReadTests/*` at
`dc2237957`): 

### Guards the regression

- R-1: the watchdog's rules still answer on the adapter path after its two reads convert
  (S1) and after its `WaitUntilAsync` lambdas become async predicates (S5) |
  `WatchdogServiceTests` (10): `C566_Unsafe_delete_refusal_requires_the_active_exact_menu`
  asserts `sent == 1` and `Inputs == ["2"]` per row; a scan that cannot see the adapter's
  screen answers 0.
- R-2: the queue service's delivery, wedged-head and expectation decisions are unchanged
  after 37 sites convert | `SessionMessageQueueWedgedHeadTests` (51 results) and
  `ExpectationSchedulingTests` (5); decisive: the `FlushSessionAsync` outcome per row (a
  flipped `is not { }` polarity delivers into a wedged head or holds a clear one).
- R-3: the three sweeps whose private helpers change signature keep their decisions |
  `BootReplyWatchdogTests` (12), `RemoteControlRecoveryTests` (17), `PolicyRefreshServiceTests`
  (24); decisive: the transcript-bound and composer-head branches that `TryPidAsync`,
  `ComposerHeadAsync`, `IsTranscriptBoundAsync` now feed.
- R-4: the runtime's own behaviour on the adapter and real-client paths after the wrappers are
  deleted | `AgentSessionRuntimeTests` (29 results), including the three
  `StaticSessionRunnerClient` methods that read a buffer through the real client type.
- R-5: no production file calls a retired sync name or blocks on a runner read |
  `RunnerReadSourcePinTests` (`[Category("Unit")]`, repo root by the `Antiphon.sln` walk,
  line-based, skipping lines whose trimmed start is `//` or `*`): `S2_queue_service_awaits_
  runner_reads` (the three `SessionMessageQueueService*.cs` files), `S3_sweeps_and_endpoints_
  await_runner_reads` (the 12 S3 files plus the `_runner.GetAsync(...).GetResult()` shape in
  `RemoteControlRecoveryService.cs`), `S5_runtime_has_no_blocking_runner_reads` (every
  `server/**/*.cs` outside `bin`/`obj`: no `\b(TryGetLiveSnapshot|TryGetLiveMetadata|
  IsLiveOrUnknown|GetBufferSnapshot)\(`, no `\b(ListLiveSessions|ListLiveOrUnknownSessions|
  ListUnknownSessions|ListLocalLiveSessions)\(\)`, and no line that names
  `_runnerClient.`/`_runner.` with `ListAsync|GetAsync|GetBufferAsync|GetSnapshotAsync` and
  `.GetAwaiter().GetResult()` or `.Result`). Each slice adds only its own method in its red
  commit, so master never carries a red pin between slices. Decisive assertion: the list of
  offending `path:line` strings is empty (the failure message prints them).

### Guard inventory

- G-1: D-3, list family: a deadline propagates as `OperationCanceledException`; nothing
  substitutes an empty live set (CARD-0679: an empty set reads every session as dead and
  feeds destructive decisions) | PC-1
- G-2: D-3, `TryGet*Async`: a deadline returns null ("unavailable"), never throws into the
  watchdog loop or the queue's delivery decision | PC-2
- G-3: D-3, `TryGet*Async`: a caller-token cancellation rethrows instead of reading as
  "unavailable" (a cancelled sweep must not record a decision) | PC-3
- G-4: D-2, the deadline exists, runs on the injected clock and does not depend on the client
  honouring its token (`WaitAsync(linked.Token)`) | PC-4
- G-5: D-1/D-4, the watchdog path awaits the read; no thread is pinned while the runner is
  silent (the card's hazard) | PC-5
- G-6: D-5, no production code blocks on a runner read or calls a retired sync name (the pin
  is the only compiler-independent signal once the wrappers are gone) | PC-6
- G-7: D-7, the timing middleware rethrows and records a 5xx at Warning; a swallowed exception
  would turn every runner failure into an empty 200 | PC-7
- Not safety-critical, no PC (ordinary rows cover them): the Debug/Information/Warning
  thresholds and the route-template field (diagnostic volume and grouping, V-3a..c); the
  Program.cs ordering pin (V-3e, diagnostic completeness); the test-adapter-first branch
  (test seam, R-1/R-4); the S2/S3 pin methods (same guard as G-6 at an earlier slice; PC-6
  exercises the mechanism once).

guards=7, mapped=7, missing=0, duplicate PC maps=0.

### Positive controls

Method-scoped, break/red/restore/green after land (Mutation); Code runs the V/R rows; Review
judges the design before land. Filters are `--treenode-filter "/*/*/<Class>/<Method>"`.

- PC-1: break G-1 by wrapping the runner read in `ListLocalLiveSessionsAsync` with
  `catch (OperationCanceledException) { return _testAdapters.Keys.ToHashSet(); }`; expect
  `AgentSessionRuntimeBoundedReadTests.C1362_List_deadline_throws_on_the_injected_clock` red
  at `Should.ThrowAsync<OperationCanceledException>` (the task completes with a list).
- PC-2: break G-2 by adding `catch (OperationCanceledException) { throw; }` ahead of the
  general catch in `TryGetLiveSnapshotAsync`; expect
  `C1362_Snapshot_deadline_reads_as_unavailable` red at `ShouldBeNull()` (part one throws).
- PC-3: break G-3 by removing the `when (ct.IsCancellationRequested)` rethrow in
  `TryGetLiveSnapshotAsync` (return null for every exception); expect
  `C1362_Snapshot_deadline_reads_as_unavailable` red at the part-two
  `Should.ThrowAsync<OperationCanceledException>` (returns null).
- PC-4: break G-4 by making `LiveReadDeadline` use `Timeout.InfiniteTimeSpan` (or dropping
  `.WaitAsync(linked.Token)` from the list read); expect
  `C1362_List_deadline_throws_on_the_injected_clock` red at the same `Should.ThrowAsync`
  (a `TimeoutException` from the 2 s `WaitAsync`, not a cancellation).
- PC-5: break G-5 by replacing `await _runtime.ListLiveSessionsAsync(ct)` in
  `WatchdogService.ScanAsync` with `_runtime.ListLiveSessionsAsync(ct).GetAwaiter().GetResult()`;
  expect `C1362_Blocked_runner_list_does_not_hold_the_pump_during_a_watchdog_scan` red at
  `first.ShouldBe(probe)` (this is the base shape; the proof above is PC-5's red).
- PC-6: break G-6 by adding to `AgentSessionRuntime` a private unused member
  `private long C1362Pc6() => _runnerClient.GetBufferAsync(Guid.Empty, CancellationToken.None).GetAwaiter().GetResult().LastSequence;`;
  expect `RunnerReadSourcePinTests.S5_runtime_has_no_blocking_runner_reads` red at the empty
  offenders assertion naming `AgentSessionRuntime.cs:<line>`.
- PC-7: break G-7 by catching the exception in `RunnerRequestTiming` without rethrowing
  (log nothing, return); expect
  `RunnerRequestTimingTests.C1362_Throwing_route_logs_warning_with_status_500` red at the
  status assertion (200, no `[Warning]` line).

### Out of scope

- The runner's own sync-over-async herdr snapshot (`SessionRunnerRuntime.cs:3819`): a
  follow-up card once S4's log shows it mattering.
- The thread pool's injection curve and `SetMinThreads`: D-1 rejected; the pump models the
  hazard without it.
- The HTTP client's retry budget and 3 s list cap under the new deadline ("shorter wins"):
  owned by the existing `SessionRunnerHttpClient` tests; the blocking fake bypasses them by
  design.
- `AgentChannelServiceIntegrationTests`, `SubscriptionUsageMonitorTests`,
  `ContextCompactionSweepTests`, `ScheduleSweepTests`, `OrchestratorServiceIntegrationTests`,
  `ApiErrorRecoveryServiceTests`, `BlockedTaskParkReleaseTests`: their S3 sites are one-line
  `await` conversions consumed as values, held by the compiler and the S3 pin; Review reads
  the token passed at each (the one shape that compiles wrong).
- `BuildSlotEndpointTests`: does not boot `Program.cs`; replaced by V-3e.
- The Unit lane, the Integration lane and any namespace-wide run (operator policy).
- S6 (optional per-minute summary): if commissioned, one row
  `/*/*/RunnerRequestTimingSummaryTests/*` on the CP-7 output, 1 method, 3 minutes.

### Checkpoints

Portable rows (no `-Runner`, no `-Platform`). `tests/Antiphon.Tests` rows CP-2, CP-4, CP-6
and CP-9 need the shared test Postgres; CP-1, CP-3, CP-5 and CP-8 do not start it. Combined
filters use the CARD-0403 form with the namespace segment; verify each class in the fresh TRX.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1362-s1/` | bounded-reads | `/*/*/AgentSessionRuntimeBoundedReadTests/*` | V-1, V-2a, V-2b | all 3 methods, 0 failed/skipped | 3 | 8 |
| CP-2 | S1 | CP-1 | watchdog-adapter | `/*/*/WatchdogServiceTests/*` | R-1 | all 10 methods, 0 failed/skipped | 10 | 4 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1362-s2/` | pin-s2 | `/*/*/RunnerReadSourcePinTests/S2_queue_service_awaits_runner_reads` | R-5 | 1 executed, 0 failed | 1 | 6 |
| CP-4 | S2 | CP-3 | queue-flush | `/*/Antiphon.Tests.Application/(SessionMessageQueueWedgedHeadTests*)\|(ExpectationSchedulingTests*)/*` | R-2 | all listed (34 + 5 methods, 56 results), 0 failed | 56 | 15 |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1362-s3/` | pin-s3 | `/*/*/RunnerReadSourcePinTests/S3_sweeps_and_endpoints_await_runner_reads` | R-5 | 1 executed, 0 failed | 1 | 6 |
| CP-6 | S3 | CP-5 | sweeps-helpers | `/*/Antiphon.Tests.Application/(BootReplyWatchdogTests*)\|(RemoteControlRecoveryTests*)\|(PolicyRefreshServiceTests*)/*` | R-3 | all listed (12 + 17 + 21 methods, 53 results), 0 failed | 53 | 10 |
| CP-7 | S4 | `tests/Antiphon.SessionRunner.Tests -> bin-c1362-s4/` | runner-timing | `/*/*/RunnerRequestTimingTests/*` | V-3a, V-3b, V-3c, V-3d, V-3e | all 5 methods, 0 failed/skipped | 5 | 5 |
| CP-8 | S5 | `tests/Antiphon.Tests -> bin-c1362-s5/` | pin-all | `/*/*/RunnerReadSourcePinTests/*` | R-5 | all 3 methods, 0 failed/skipped | 3 | 6 |
| CP-9 | S5 | CP-8 | runtime-and-watchdog | `/*/Antiphon.Tests.Application/(AgentSessionRuntimeTests*)\|(WatchdogServiceTests*)/*` | R-4, R-1 | all listed (23 + 10 methods, 39 results), 0 failed | 39 | 10 |

Union of `Covers`: V-1, V-2a, V-2b, V-3a..e, R-1..R-5, the whole ordinary scope. A red row
is fixed and rerun as the same row. The S2 carry-over clause in the Plan (list-family sites
moving to S3) changes CP-3's method body, not the table.

### Cost

- Ordinary V/R floor (Code) = sum of `EstimatedMinutes` = 8 + 4 + 6 + 15 + 6 + 10 + 5 + 6 +
  10 = **70 minutes** across five Code dispatches (S1 12, S2 21, S3 16, S4 5, S5 16),
  estimated; builds are 4 to 6 minutes each on the server2 mirror (measured 224 to 373 s for
  `tests/Antiphon.Tests` on 2026-10-19) and the single-method pin rows are seconds after
  their build.
- PC floor (Mutation) = two baseline builds (`tests/Antiphon.Tests` 6, `Antiphon.SessionRunner
  .Tests` 3) + 7 PCs x (incremental red build 2 + red run 1 + restore build 2 + green run 1) =
  9 + 42 = **51 minutes**, estimated, filters as listed under Positive controls.
- Total = 70 (V/R, includes each slice's setup build) + 51 (PC) = **121 minutes**, estimated.
  The W1 proof in this stage is additional and measured below.
- Savings against the Plan's 106-minute proposal: 36 minutes, from dropping
  `BuildSlotEndpointTests` (no Program boot), `AgentChannelServiceIntegrationTests` and
  `SubscriptionUsageMonitorTests` (compiler-held one-line sites), the second
  `WatchdogServiceTests` run as its own row (folded into CP-9's combined filter), and the
  separate `ExpectationSchedulingTests` row (folded into CP-4).

--- next stage ---
next: code
handoff: Implement CARD-1362 S1 from docs/superpowers/plans/2026-10-19-card-1362-bounded-async-runner-reads-plan.md: bounded async reads awaited through WaitAsync(linked.Token), watchdog on them, W1/W1b/W1c in AgentSessionRuntimeBoundedReadTests with the pump and blocking fake as specified; run CP-1 and CP-2 as the closed list.
artifact: docs/superpowers/plans/2026-10-19-card-1362-bounded-async-runner-reads-plan.md
