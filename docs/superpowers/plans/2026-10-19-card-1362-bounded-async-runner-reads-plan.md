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

## Verification design (proposal for TestDesign)

V-1 a runner that never answers does not hold the pump while a watchdog scan awaits it
(W1). V-2 list and snapshot deadlines on the injected clock keep their outcome classes
(W1b, W1c). V-3 the runner writes one timing line per slow request with route template,
status, elapsed and session id, and none at the default level for a fast one. R-1 the
watchdog's existing rules still answer on the adapter path. R-2 the queue service's
delivery, late-confirm and expectation paths are unchanged. R-3 the converted sweeps keep
their decisions. R-4 the runtime's existing behaviour on the adapter and static-client
paths. R-5 no production file calls a retired sync name or blocks on a runner read.

### Checkpoints

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

### Cost

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

--- next stage ---
next: test-design
handoff: Design the verification for CARD-1362 from docs/superpowers/plans/2026-10-19-card-1362-bounded-async-runner-reads-plan.md: confirm W1/W1b/W1c on the single-thread pump and FakeTimeProvider, the per-slice source pins, the runner timing test host, and close the CP-1..CP-13 table with real roster counts and the CARD-0403 filter syntax.
artifact: docs/superpowers/plans/2026-10-19-card-1362-bounded-async-runner-reads-plan.md
