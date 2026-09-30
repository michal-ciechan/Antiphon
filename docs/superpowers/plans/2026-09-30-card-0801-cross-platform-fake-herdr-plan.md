# CARD-0801: cross-platform FakeHerdrServer and real Herdr transport

Date: 2026-09-30. Stage: TestDesign. Plan baseline: `5f5baeb702b9e4db378866d80e862fca27b62b6e`.
TestDesign source: `4c6d95f380ce20e845147d5f2996721eb9280017`. Next: Code.
This document freezes the ordinary verification manifest and assertion-based mutation controls.

Live card title verified with `scripts/card.ps1 get CARD-0801 -Board Antiphon`:
**Make FakeHerdrServer cross-platform (unix socket on Linux) so Herdr integration tests run on Linux**.
TestDesign independently recounted source attributes, including same-line attributes and partial
classes: **69 methods / 110 results in eight server classes; 335 methods / 497 results in 24 runner
classes**. No builds, tests, or runtime portability claims are part of this static validation.

## Decision and evidence

The card's **OPERATOR DECISION 2026-09-30** requires execution on Linux and Windows. It supersedes
`origin/feat/card-task-b3864165:docs/superpowers/plans/2026-09-30-card-0801-linux-fake-herdr-listener-skip-plan.md`.
That document remains history; do not implement its attribute, platform refusal, or 110-test skip.
This plan was produced by source inspection and a PowerShell attribute census only: no builds,
test hosts, live Herdr sessions, or production configuration changes were run.

Read owners: `docs/project-context.md`, `docs/herdr-sessions.md`, `docs/testing-and-build.md`,
`docs/session-runtime-invariants.md`, and the stage/landing and HTTP instructions in
`docs/orchestration-loop.md` and `docs/ops-http.md`. Read CARD-0801 before the old plan.

The failure evidence is unchanged: the first `AgentAttachHerdrTests` case blocked in `StartFake`
on server2 on 2026-09-25; `WaitUntilListeningAsync().GetAwaiter().GetResult()` never got a signal.
In task a7ce7cf4, an unfiltered Linux run reached 5,395 passes and 170 failures after 3,670 seconds,
then repeated unobserved pipe exceptions and never wrote a TRX. The old plan's constructor probe
refines the explanation: when `ApplicationData` is empty, the fake passes the relative path
`herdr/sessions/antiphon-herdr-test-<guid>/herdr.sock` to `NamedPipeServerStream`, which throws
`PlatformNotSupportedException`. A rooted path may be accepted by .NET on a different Linux
host. Either way, the current accept loop does not complete the listening promise on a terminal
failure. The later guarded full run took 3 h 00 m 09 s: 12,029 passed, 546 failed, 222 skipped.
Those are historical measurements, not this plan's validation results.

**A fake-only change cannot meet acceptance.** `src/Antiphon.SessionRunner/HerdrClient.cs`:

- `ConnectPipeAsync` explicitly refuses non-Windows and returns `NamedPipeClientStream`.
- `SendCoreAsync` accesses `SafePipeHandle` and calls `GetNamedPipeServerProcessId` from kernel32.
- Both normal RPC and subscription paths call that connector.
- The internal constructor has a **path** override, not a connection factory; most tests use
  `HerdrSettings.Session` instead. There is no current in-memory or Unix transport injection.
- `HerdrDisposalBackend.InspectAsync` refuses a null `HerdrServerInfo.InstanceId`. Merely hiding
  the kernel32 call on Unix would break the disposal tests and the disposal safety contract.

The real Herdr does use filesystem Unix domain sockets on Linux/macOS, and named pipes on Windows.
This is confirmed against [Herdr v0.8.2 IPC source](https://github.com/herdrdev/herdr/blob/v0.8.2/src/ipc.rs)
(`connect_local_stream`, `bind_local_listener`) and the [socket API documentation](https://herdr.dev/docs/socket-api/#socket-transport).
The versioned [session resolver](https://github.com/herdrdev/herdr/blob/v0.8.2/src/session.rs)
and [config resolver](https://github.com/herdrdev/herdr/blob/v0.8.2/src/config/io.rs) define named
sessions under `herdr/sessions/<name>/herdr.sock`; Unix config is `$XDG_CONFIG_HOME/herdr`, else
`$HOME/.config/herdr`, else the temp directory plus `herdr`. macOS follows this Unix rule, not
.NET's Application Support folder. Preserve Antiphon's existing Windows resolution in this card.
Protocol 20 and the production launch requirement for a PowerShell pane stay unchanged.

`card.ps1 search Herdr -All` returned 75 cards across all boards. The broader timing/boundary
investigation is **CARD-0856**, board `8988ca03-7414-47ad-b0b6-51556c701703`, card
`7e4bfaee-1970-40b0-b7df-7b5636c4bf3f` (inspect with `scripts/card.ps1 get CARD-0856`). It records
270 minutes on Windows, about 180 on Linux, Herdr only 3% of serial test time, and a 6-7 minute
Linux Unit lane. Link that card for lane separation, incidental interface fakes, prebuilt fake
executables, clone-lock profiling, and whole-suite speed work; do not file another timing card.
Its suggestion to retain a Windows-only production client is superseded here by CARD-0801's
operator decision and the concrete disposal/HTTP consumer requirements.

## Source census and coverage boundary

Counts below are **expanded results**, not method counts or RPC counts. Census rule: group all
partial declarations, one result per unparameterized `[Test]`, otherwise one per `Arguments`
attribute, including combined `[Test, Arguments(...), Arguments(...)]`. There are no test-data
source or matrix attributes in this closure. Code must check the fresh TRX against each row;
source counts are expectations, not execution evidence. Internal loops do not multiply results.

### Antiphon.Tests: eight classes, 69 methods, 110 results

All files are under `tests/Antiphon.Tests/`. Counts apply on both Linux and Windows; zero skips.

| Class | Files | Results | Role of fake |
|---|---|---:|---|
| `Application.AgentAttachHerdrTests` | `Application/AgentAttachHerdrTests.cs` | 16 | Herdr attach/seat ownership is the subject; synchronous `StartFake` wait |
| `Application.HerdrAlwaysOnChannelParityTests` | `Application/HerdrAlwaysOnChannelParityTests.cs`, `.StandingRecovery.cs`, `.SupervisionHold.cs` | 24 | Herdr lifecycle/channel parity; 8 PtyHost argument results and 1 banner result do not need the fake |
| `Application.HerdrLabelFollowWireTests` | `Application/HerdrLabelFollowWireTests.cs` | 4 | Label HTTP contract via `HerdrLabelFollowHttpFixture` |
| `Application.HerdrLabelFollowFlowTests` | `Application/HerdrLabelFollowFlowTests.cs` | 19 | Label persistence/lifecycle flow via the same fixture |
| `Application.HerdrPaneDisposalEndpointTests` | `Application/HerdrPaneDisposalEndpointTests.cs` | 14 | Server endpoint behavior; fake is the remote end of the HTTP fixture |
| `Application.HerdrPaneDisposalApplicationTests` | `Application/HerdrPaneDisposalApplicationTests.cs` | 14 | Application guard/receipt behavior via that fixture |
| `Agents.HerdrPaneDisposalHttpWireTests` | `Agents/HerdrPaneDisposalHttpWireTests.cs`, `Agents/HerdrPaneDisposalExecutionWireTests.cs` | 9 | Server/runner wire behavior via that fixture |
| `Scripts.HerdrPaneScriptTests` | `Scripts/HerdrPaneScriptTests.cs` | 10 | Operator script/HTTP contract; fake is incidental |

Application subtotal 91, Agents 9, Scripts 10. `HerdrDisposalHttpFixture` delegates to the linked
`HerdrPaneDisposalFixture`; the two DB-only label classes use `FakeSessionRunnerClient` and are
outside this closure. Keep all 24 parity results, including the nine not dependent on Herdr.

### Antiphon.SessionRunner.Tests: 24 classes, 335 methods, 497 results

The old estimate of roughly 170 was **170 methods in 16 direct-consumer classes**, which expand
to 195 results here. Following `HerdrPaneDisposalFixture` and `HerdrLabelFollowFixture` adds eight
classes and 302 results. All 497 results are in this card's acceptance, including mixed classes.
The actual subjects are production client/runtime services; the fake is their wire peer. No
existing suite is a dedicated fake-listener lifecycle test; S2 supplies that missing coverage.

| Class (file is `<Class>.cs` unless noted) | Methods | Results | Subject / incidental |
|---|---:|---:|---|
| `HerdrClientTests` | 16 | 16 | Transport/protocol subject; 4 use the stateful fake, 9 use local raw pipe helpers, 3 are missing/disabled/serialization cases |
| `HerdrAttachTests` | 23 | 23 | Herdr attach subject |
| `HerdrLaunchShapeTests` | 37 | 37 | Herdr launch and script shape subject |
| `HerdrAdoptionSweepTests` | 21 | 22 | Herdr adoption subject; one method has two Arguments |
| `HerdrNamedTabPlacementTests` | 23 | 23 | Placement subject; 3 methods use fake, other rows are pure planner checks |
| `HerdrPaneChildKillTests` | 6 | 6 | Herdr kill/detach subject |
| `HerdrRunnerSessionTests` | 7 | 7 | Herdr runtime subject plus one native PtyHost regression |
| `HerdrEventPumpTests` | 5 | 5 | Herdr event handling subject |
| `HerdrStatusPushTests` | 4 | 4 | Herdr status metadata subject, mixed pure classification |
| `HerdrPlacementCheckRouteTests` | 5 | 5 | Placement HTTP boundary subject |
| `GrokRulesFileLaunchTests` | 6 | 21 | Incidental wire peer for rules persistence/refusal; 12 Arguments in one method, 5 in another |
| `GrokRulesRunnerRefusalTests` | 11 | 11 | Incidental wire peer, mixed PtyHost/error-mapping checks |
| `GrokRulesAdoptionTests` | 1 | 4 | Incidental peer in Herdr/PtyHost x corrupt/valid receipt matrix |
| `GrokRulesStoreFailureTests` | 1 | 6 | Incidental peer for store failure variants |
| `CodexLaunchRefusalTests` | 3 | 3 | Incidental peer for unsupported/unsafe launch checks |
| `RunnerStartupReadinessTests` | 1 | 2 | Incidental peer in a real isolated runner's startup barrier |
| `HerdrClientSurfaceTests` | 3 | 3 | Typed getter protocol, through label fixture |
| `HerdrLabelFollowSchedulingTests` | 14 | 25 | Label lifecycle subject, through label fixture |
| `HerdrLabelObservationTests` | 24 | 71 | Label binding/observation subject, through label fixture |
| `HerdrLabelSnapshotTests` | 13 | 36 | Label snapshot subject, through label fixture |
| `HerdrPaneDisposalServiceTests` (also `HerdrPaneDisposalGuardTests.cs`) | 56 | 92 | Disposal service/guard subject, through disposal fixture |
| `HerdrPaneDisposalConcurrencyTests` | 20 | 27 | Disposal concurrency subject, through disposal fixture |
| `HerdrPaneDisposalIdentityTests` | 22 | 35 | Mixed identity unit checks and disposal fixture |
| `HerdrPaneDisposalStopRegressionTests` | 13 | 13 | Disposal/ordinary Stop regression, through disposal fixture |

Reproduce the closure with `rg -l 'FakeHerdrServer|HerdrPaneDisposalFixture|HerdrLabelFollowFixture'
tests/Antiphon.SessionRunner.Tests --glob '*.cs'`, then distinguish construction/fixture use from
comments and state-type references and follow partial classes. `HerdrPaneDisposalGuardedLiveTests`
mentions a fixture helper but its listener is installed Herdr, not the fake. The opt-in live,
headed classes are outside acceptance; do not start an operator Herdr or real provider.

## Design

### D1. A real platform connector, with an owned Stream and backend identity

**Production behavior and gate.** This enables real Linux/macOS connections where Antiphon
currently throws before connecting; it is not merely a fake implementation change. The real
Herdr already uses pathname Unix sockets. Preserve `HerdrSettings.Enabled=false` by default,
`SessionBackend.PtyHost=0`, and the per-agent opt-in. This checkout explicitly sets Enabled=true,
so Unix users who already selected Herdr will now reach their daemon: document that activation
risk and the unchanged PowerShell-pane launch requirement. Select transport strictly by host OS:
Windows always retains named pipes even when `SocketPath` is set; no Unix fallback after Windows
connection failure. Leave deployed settings unchanged. CP-1/CP-2 and the separate Windows
verification must prove this branch and the disabled default through the public client.

Add a small internal `HerdrConnection`/`HerdrTransport` implementation in the runner. Connection
ownership includes the stream and optional peer process identity; request parsing accepts
`Stream`. Normal calls and event subscription both use `ConnectAsync(endpoint, timeout, ct)`.

- Windows: retain `NamedPipeClientStream`, async byte transport, the current timeout and
  `GetNamedPipeServerProcessId` plus process start ticks. No Windows transport or wire changes.
- Linux/macOS: `Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)`,
  `UnixDomainSocketEndPoint`, cancellation-aware `ConnectAsync`, then an owning `NetworkStream`.
  Use a pathname socket, not Linux abstract namespace or .NET's implicit named-pipe emulation.
- Resolve the endpoint once per connection and use it in diagnostics. Dispose partially opened
  sockets/streams on every failure. Preserve caller cancellation; turn connector timeout and
  IO/socket failure into `HerdrBackendUnavailableException` with the original cause. Keep
  protocol mismatch, malformed JSON and Herdr API exceptions distinct. No retry or PTY fallback.
- Guard native calls by actual transport/OS. Linux peer PID comes from connected-socket
  `SO_PEERCRED`; macOS uses `SOL_LOCAL/LOCAL_PEERPID`. Resolve process start time and retain the
  existing `pid:startTicks` instance format. An unavailable PID/start observation stays null,
  so disposal still refuses. Never use the client's own PID, socket pathname, a fake JSON PID,
  or a constant as daemon identity. Windows must continue to pass its exact current identity test.

The Linux interface is documented in [unix(7)](https://man7.org/linux/man-pages/man7/unix.7.html);
Apple defines the peer PID option and 104-byte `sun_path` in [XNU's socket header](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/un.h).
Isolate this native interop in `HerdrPeerIdentity.cs`, retain safe handle ownership during the
call, and verify returned lengths/errors. No change to disposal authorization, process census,
backend identity comparison, transcript confirmation, or session exit decisions is authorized.

### D2. Per-instance endpoint configuration, with explicit fixture wiring

Add optional `HerdrSettings.SocketPath` as an instance-scoped low-level endpoint. Precedence:
internal `socketOverride` (existing seam) -> configured nonblank `SocketPath` -> explicit
`Session` -> `HERDR_SOCKET_PATH` -> `HERDR_SESSION` -> platform default. With SocketPath unset,
Windows keeps its existing behavior. Unix defaults follow the versioned upstream resolver above;
missing `.config` directories must not cause a relative path by accident. Reject a relative
configured Unix endpoint with a clear unavailable/configuration error; do not mkdir from clients.

`FakeHerdrServer.EndpointPath` is the native endpoint; keep `Session` and the Windows `PipeName`
compatibility property. Each fixture sets `SocketPath = fake.EndpointPath` next to `Session`.
The out-of-process readiness test passes `--SessionRunner:Herdr:SocketPath` through ArgumentList.
The fake and client then exercise the **same production connector**, not a test-only bypass.

The brief's preferred no-call-site-edit outcome is not possible at this baseline: existing
consumers only inject a session, which resolves under the real user's configuration directory;
that path can exceed Unix limits and cannot safely be made a short test root without global
environment mutation or test-prefix special cases in production. Make the small explicit
fixture edits. Do not change HOME/XDG/HERDR variables process-wide, build a static endpoint
registry, reinterpret a session name as an absolute path, or weaken production resolver semantics.

### D3. A test listener abstraction preserving one RPC per stream and live subscriptions

Add `FakeHerdrTransport.cs` in the runner tests and link it into Antiphon.Tests alongside the fake.
It owns a native listener and returns accepted **Streams**. Its explicit operations are bind/start,
accept, and async disposal; it also exposes a fault-injection factory to the listener tests.
Keep the JSON state machine, replay buffer, method gates and scripted launch semantics in
`FakeHerdrServer`; they do not depend on the OS and must not be rewritten for transport work.

Windows still creates the named-pipe instances with the current name, byte mode and instance
limit. Unix binds/listens once, accepts separate sockets, and retains the listener while accepted
streams are served. Subscription and gated-request streams stay owned by the fake's handler list
and cannot block the next accept. Normal responses still close their stream. Client disconnects
are per-connection failures; bind/listen/accept failures are terminal listener failures, not an
IOException retry loop. Preserve the current post-request listening barrier on Windows; on Unix
it means the listener is bound and the next accept can service a connection. Do not publish a
successful readiness signal before bind/listen succeeds.

Use one unique lease per test: Windows's existing GUID session name; Unix a cryptographically
random private directory `/tmp/ah-<32 hex>/s` (42 ASCII bytes including the path separators).
Use the short `/tmp` spelling on macOS too, not its expanded `/private/tmp` or a long TMPDIR.
Check UTF-8 byte length, not C# character count: less than 104 bytes including room for NUL is
portable and satisfies Linux's 108-byte limit. The short root is an implementation choice for
owned test sockets, not production Herdr discovery. An unavailable/unwritable `/tmp` is a loud
fixture error; it is never a reason to connect to a real session.

Create the directory exclusively with user-only access (0700) before binding; make the socket
0600 before signaling readiness. Refuse an existing directory/symlink or occupied socket instead
of deleting it. Default construction owns the lease; tests intentionally restarting a fake
share an explicit `FakeHerdrEndpoint` lease while replacing the server, preserving the endpoint
seen by the existing client. The lease remains until the last server has stopped and the test
has disposed it. A second simultaneous listener on the lease must fail without unlinking the
first. No deterministic session-to-/tmp global map.

Disposal cancels accept and handlers, closes owned streams/listeners, awaits observed tasks, then
unlinks only the socket this listener bound and removes its own now-empty private directory.
Failure before successful bind must not delete somebody else's path. Repeated disposal is safe;
startup failure, cancellation and restart leave no owned handles/socket behind. Do not recurse
through arbitrary directories or sweep stale sockets from other runs.

### D4. Listener startup always settles; fail fast even for tokenless callers

A startup/terminal-accept failure records the original exception as `ListenerFault`, faults the
current `_listening` promise under `_listenGate`, and terminates the loop once. Observe both
loop and promise faults even if the caller never awaits readiness, without hiding them from
callers. Do not leave a previously successful promise in place after terminal failure: subsequent
readiness calls must see the fault. `ResetListening` cannot erase a recorded fault.

`WaitUntilListeningAsync` also has a **5-second default readiness deadline**, even without a
caller token. Its timeout names the endpoint and fake and is a failure, never a skip; normal
constructor/bind failures surface their original cause immediately. Cancellation cancels that
caller's wait, not the listener. Dispose-before-ready completes pending readiness with disposal/
cancellation rather than leaving it pending. A bounded guard is warranted because the current
synchronous `StartFake()` caller supplies no cancellation token. Avoid catching the terminal
listener error as a transient per-client IOException or restarting a failed listener in a loop.

Tests use an independent 2-second bound for injected immediate faults and a 7-second outer bound
for the 5-second default deadline. This lets Mutation distinguish original-cause propagation,
the fallback timeout, and the original never-completing behavior.

### D5. Preserve execution by fixing fixture prerequisites, not skipping mixed classes

All 110 + 497 existing results must execute on Linux and Windows. There are independent Windows
assumptions exposed by the transport fix:

- Parity resolves `fakeclaude.exe`/`fakegrok.exe` and skips when missing. Use the existing
  `TestAppHostPath` resolver and stage real Unix apphosts for these producers under their owned
  subdirectories. Keep `UseAppHost=false` on the outer Linux build; extend the existing inner
  restore/build staging pattern used for PtyHost so the `fakeclaude` file/directory collision
  does not return. A missing required fake is a setup failure, not an accepted skip.
- `HerdrAdoptionSweepTests`, `HerdrRunnerSessionTests`, `GrokRulesAdoptionTests`, and
  `RunnerStartupReadinessTests` use real `cmd.exe` children. Provide a small shared test-process
  helper retaining the Windows commands and using an owned `/bin/sh` child on Unix; native PTY
  echo tests still send input and prove observed output. Retain process limits and finally-based
  teardown. Fake foreground names used only as protocol data remain fixture data.
- The readiness test must launch the built runner DLL via `dotnet` when the Unix apphost is not
  staged, on its isolated random port. Do not use 17204 or the production runner. Its milestone
  decoder is a PowerShell script; keep its actual start-order assertions on both OSes.
- Existing Grok/Codex tests sometimes assert a Windows argv/path policy. Keep Windows assertions;
  on Unix assert the existing Unix policy with portable fixture executables/paths. Do not force
  Windows production policy on Linux, early-return a body, catch-and-pass, or remove Arguments.
  If a production defect beyond this transport/identity scope is exposed, report the exact test
  and stop that row for scope triage; do not lower its count or quietly turn it into a skip.

These changes are acceptance prerequisites, not CARD-0856's proposed wholesale replacement of
incidental wire fakes. No production Herdr launch-script/shell policy change is part of this card.

## Slices and exact expected footprint

Only this plan file changes in the Plan task. The following is the prospective Code footprint;
new files are named explicitly. Every slice is committed and pushed before its checkpoint group.

**S1 — production transport, endpoint and identity.**

- `src/Antiphon.SessionRunner/HerdrClient.cs`
- `src/Antiphon.SessionRunner/HerdrSettings.cs`
- `src/Antiphon.SessionRunner/HerdrTransport.cs` (new; owned connection type in this file)
- `src/Antiphon.SessionRunner/HerdrPeerIdentity.cs` (new)

**S2 — shared listener and focused tests.**

- `tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs`
- `tests/Antiphon.SessionRunner.Tests/FakeHerdrTransport.cs` (new, endpoint lease in same file)
- `tests/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests.cs` (new)
- `tests/Antiphon.SessionRunner.Tests/HerdrTransportTests.cs` (new)
- `tests/Antiphon.SessionRunner.Tests/HerdrClientTests.cs` (port raw helper and explicit endpoint)
- `tests/Shared/HerdrTestProcess.cs` (new; owned peer/process helper used by transport tests)
- `tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj` (link helper)
- `tests/Antiphon.Tests/Antiphon.Tests.csproj` (link shared listener source)

Commit S1 then S2, run the S1-S2 checkpoint group once. All raw helper cases in HerdrClientTests
move to the platform listener; keep their 16 results and assertions. No Windows-only attribute.

**S3 — endpoint injection and portable consumers.** Under `tests/Antiphon.SessionRunner.Tests/`:

`HerdrAttachTests.cs`, `HerdrLaunchShapeTests.cs`, `HerdrAdoptionSweepTests.cs`,
`HerdrNamedTabPlacementTests.cs`, `HerdrPaneChildKillTests.cs`, `HerdrRunnerSessionTests.cs`,
`HerdrEventPumpTests.cs`, `HerdrStatusPushTests.cs`, `HerdrPlacementCheckRouteTests.cs`,
`GrokRulesFileLaunchTests.cs`, `GrokRulesRunnerRefusalTests.cs`, `GrokRulesAdoptionTests.cs`,
`GrokRulesStoreFailureTests.cs`, `CodexLaunchRefusalTests.cs`, `RunnerStartupReadinessTests.cs`,
`HerdrPaneDisposalFixture.cs`, `HerdrLabelFollowFixture.cs`, and
`Antiphon.SessionRunner.Tests.csproj` (stage the required Unix PtyHost). Also
`Fixtures/RunnerRestart/RestartFixture.cs`: select `pwsh` on Unix only in
`DecodeCapturedMilestones`, whose current call inherits the `pwsh.exe` default; CP-7 exercises
the actual decoder. Keep other restart-fixture defaults and production restart scripts unchanged.

Under `tests/Antiphon.Tests/`: `Application/AgentAttachHerdrTests.cs`,
`Application/HerdrAlwaysOnChannelParityTests.cs`,
`Application/HerdrAlwaysOnChannelParityTests.StandingRecovery.cs`,
`TestHelpers/HerdrLabelFollowHttpFixture.cs`, and `Antiphon.Tests.csproj` (fake apphost staging).
Extend S2's `tests/Shared/HerdrTestProcess.cs` and link it into Antiphon.Tests. The SupervisionHold partial
and all fixture-only label/disposal test bodies need no endpoint edits. `LocalHttpRunner.cs`,
`DirectSessionRunnerClient.cs`, the production runtime and the test fake CLI implementations are
not expected to change. Any expansion requires an explicit finding and revised checkpoint scope.

**S4 — owner documentation and complete acceptance.** Update `docs/herdr-sessions.md` with native
transports, endpoint override, Unix resolution, peer identity and the continuing PowerShell
launch limitation; update `docs/testing-and-build.md` to replace the obsolete “leave attach out
of Linux filters” instruction with this exact execution closure and fail-fast contract. Commit
S3 then S4, run all S1-S4 rows once. The frozen `tests/linux-test-roster.json`, nightly schedules,
skip plan, production deployment config and board exports are not edited. Direct class filters
execute the excluded roster classes without changing lane admission; broader lane upkeep belongs
to CARD-0856/CARD-0590.

Collision notes: the brief reports no other work touching Herdr fakes. CARD-0849 is limited to
`deploy/compose/c590-remote.sh` and its tests, with no source overlap. The expanded plan also
reserves the production Herdr files, both test project files and the two living docs: recheck
those at dispatch because staging/docs are wider collision surfaces. No rebase of the assigned
Plan branch is allowed; push only its fast-forward descendants. The landing service owns target
integration.

## Verification design

| ID | Required observation |
|---|---|
| V-1 | Real client ping/request/subscription over the actual OS transport, protocol and caller-cancel behavior unchanged |
| V-2 | Tokenless readiness faults promptly on bind/accept failure; loop observed; default deadline and disposal settle waits |
| V-3 | Unique short endpoints, restrictive Unix permissions, collision refusal, parallel fake isolation, owned cleanup/rebind |
| V-4 | Peer PID/start identity from the connected server; failed identity lookup keeps disposal unavailable |
| V-5 | All eight Antiphon.Tests classes: exactly 110 executed, 0 skipped, per-class counts above |
| V-6 | All 24 runner classes: exactly 497 executed, 0 skipped, per-class counts above |
| R-1 | Existing 16 client tests, including raw protocol errors and subscription replay, remain green on Windows and Linux |
| R-2 | Assembly-wide Unit-category smoke completes with fresh TRX; classification metadata remains valid |
| R-3 | Real runner startup barrier and native PtyHost mixed rows retain physical process/output assertions on both OSes |

New `FakeHerdrServerListenerTests` has **10 unparameterized results**, all OSes:
`C801_StartupFailureFaultsAllWaiters`, `C801_AcceptFailureFaultsLaterWaiters`,
`C801_UnawaitedFailureIsObserved`, `C801_DefaultReadinessDeadline`,
`C801_CallerCancellationDoesNotStopListener`, `C801_DisposeBeforeReadySettlesWait`,
`C801_ParallelEndpointsAreIsolated`, `C801_EndpointLengthAndPermissions`,
`C801_BindCollisionDoesNotDeleteOwner`, `C801_DisposeAllowsLeaseRebind`.
Use a real occupied Unix endpoint for the collision case, not permission denial under root.
Windows exercises the corresponding exclusive named-pipe collision; permissions assertions
branch by transport while every test body still executes. Default-deadline uses an injected
listener-start gate, not a five-second arbitrary scheduling sleep. Keep fake lifecycle tests Unit;
mark any process-spawning transport case with the assembly's ProcessSpawnLimit.

New `HerdrTransportTests` has **12 unparameterized results**, all OSes:
`C801_NativePing`, `C801_RequestAndSubscriptionCoexist`, `C801_ConnectCancellation`,
`C801_ConnectDeadline`, `C801_MissingEndpoint`, `C801_MalformedResponse`,
`C801_PathOverridePrecedence`, `C801_NamedAndDefaultResolution`,
`C801_ConnectedPeerIdentity`, `C801_DifferentPeerIdentityAfterRestart`,
`C801_UnavailableIdentityRefusesDisposal`, `C801_RepeatedConnectDisposeReleasesHandles`.
For different-peer identity, use distinct owned child processes, not two listeners in the same
process (the same process must have the same identity). Reuse the test assembly as an owned
child mode or a PowerShell child that binds a stream; no real Herdr/provider or new daemon.
Resolver tests should supply an explicit environment snapshot to the pure resolver helper rather
than mutate the test host's environment. Protocol tests must assert actual requests/outcomes.

### Positive controls (later Mutation, method-scoped)

| PC | Deliberate defect | Required red evidence |
|---|---|---|
| PC-1 | Break Unix bind with an occupied endpoint or injected SocketException | `C801_StartupFailureFaultsAllWaiters` observes the original cause in <2 s; collision test proves owner still answers. This is a normal green error-path test before mutation. |
| PC-2 | Remove terminal `FaultListening` propagation | Same method fails its original-cause assertion within its independent 2 s bound; fallback 5 s timeout is not an acceptable substitute |
| PC-3 | Restore the old never-settled promise and remove the default deadline | `C801_DefaultReadinessDeadline` fails by its independent 7 s bound; test/row watchdog reaps only its owned host, no endless run |
| PC-4 | Restore the client's non-Windows refusal, or route Unix to NamedPipeClientStream | `C801_NativePing` fails on Linux; CP-10 through CP-13 must later show all 110 executed on restored source |
| PC-5 | Return the client PID/constant/null instead of peer identity | Distinct-child identity/restart test fails; disposal tests fail for null. Never weaken the disposal guard to obtain green |
| PC-6 | Remove socket cleanup, or unlink before a failed bind owns the path | Cleanup/rebind or collision-owner test fails; no assertion limited to a fake's own boolean |
| PC-7 | Block accept on a live subscription | Request/subscription coexistence fails within its RPC deadline |

Mutation reports each exact changed line, method filter, red assertion and restored-source green.
Build failure, zero-test discovery, a skip, or an unrelated fixture failure is not a killed control.
The ordinary 110-result run is the positive execution control; it cannot be replaced by discovery.

### Execution and cheaper acceptance

Use the checkpoint tool, not ad hoc dotnet test. One run per committed slice group:

```powershell
# Linux Code task, after the respective commits:
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0801-cross-platform-fake-herdr-plan.md --rows CP-1
# Then S3 + S4 committed:
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0801-cross-platform-fake-herdr-plan.md --rows CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-13,CP-14
```

Call `wait <run-id> --max-wait 50s` until exit is not 75; preserve the per-row CHECKPOINT report,
expanded class counts, failures and TRX. Tool bootstrap build, if needed, runs behind
`scripts/build-slot.ps1`; row drivers own their slots and must not be nested inside that wrapper.
A slot timeout (4) is not a license to build unleased. Use one isolated build per build cell;
subsequent rows reuse it only inside the same After group. Rows are serial to keep native-process
and DB evidence bounded and avoid cross-assembly resource contention. No unlisted broad runs.

Commission **a separate task with `-Platform Windows`**, pinned to the final Linux-tested Code
SHA, for `--rows CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-13,CP-14`.
It uses the same manifest and fresh outputs; CP-2 rechecks the S1-S2 tests against the final SHA.
No runner selection can silently substitute Linux for this Windows proof. macOS implements the
same Unix transport with its own identity branch; when a macOS host is available, CP-2 is the
bounded native qualification (39 executed). Linux is not evidence that the macOS native call ran.
The required merge evidence here is Linux plus Windows; record macOS qualification as pending.

Acceptance is the complete **named fake-consumer closure**, plus an assembly-wide Unit-category
smoke. CP-14 is expected to take 8 minutes on Linux, with a 24-minute execution deadline under
the checkpoint tool's 3x estimate (slot waiting is separate). This is a filtered Unit smoke, not
an unfiltered whole-assembly completion claim. It supplements the exact consumer rows, which
exercise the formerly hanging listeners and wait for clean teardown. CARD-0856's measured Unit
reference is 6-7 minutes; the 8-minute figure is an estimate and must be replaced by actual timing.

No 190-minute unfiltered Linux Antiphon.Tests acceptance row is commissioned. Such a run would
still add order/interference coverage with unrelated Integration classes, broader process-leak
and global serialization evidence, and proof that the entire assembly writes a final TRX. It
would also re-spend hours on known unrelated Linux failures. The explicit targeted invariant is
that every discovered fake consumer can start, execute and dispose without a listener hang;
the source closure bounds it. Whole-assembly assurance remains the nightly and CARD-0856 lane work.

Each targeted row requires its exact per-class count, **0 skipped and 0 failed**, on both OSes.
A smaller count is not accepted merely because the manifest Min floor passed. CP-14 is the only
broad row: record any known inherited Unit failures by exact name; an unexplained/new failure is
red. Do not run the old fake-dependent baseline blindly with `--baseline`: it can reproduce the
original hang. A baseline needed for an unrelated failure must first be bounded/method-scoped
and reported as an additional diagnostic run with a reason.

### Cost

Estimated Linux checkpoint floor: **84 minutes**; Windows: **98 minutes**, excluding slot waits.
Final Windows-only task is 88 minutes because it does not repeat the intermediate CP-1.
Final Linux acceptance after the initial slice is 76 minutes. These conservative budgets include
native process fixtures and database startup; they are estimates, not claimed measurements.
Authoring S1-S4: 4-6 hours, reflecting the newly identified identity, transitive fixture and
native-harness work; TestDesign should assess splitting Code at S2/S4 without changing coverage.
Review is separate; Mutation is method-scoped and budgeted separately (roughly 30-60 minutes).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c801-core/` | initial-transport | `/*/*/(FakeHerdrServerListenerTests*)\|(HerdrTransportTests*)\|(HerdrClientTests*)\|(TestClassificationGuardTests*)/*` | V-1, V-2, V-3, V-4, R-1 | Linux/Windows: 39 executed (10+12+16+1), 0 failed/skipped | 39 | 8 | 10 | true |
| CP-2 | S1-S4 | `tests/Antiphon.SessionRunner.Tests -> bin-c801-runner/` | final-transport | `/*/*/(FakeHerdrServerListenerTests*)\|(HerdrTransportTests*)\|(HerdrClientTests*)\|(TestClassificationGuardTests*)/*` | V-1, V-2, V-3, V-4, V-6, R-1 | Linux/Windows: 39 executed (10+12+16+1), 0 failed/skipped | 39 | 8 | 10 | true |
| CP-3 | S1-S4 | CP-2 | runner-lifecycle | `/*/Antiphon.SessionRunner.Tests/(HerdrAttachTests*)\|(HerdrLaunchShapeTests*)\|(HerdrAdoptionSweepTests*)/*` | V-6 | Linux/Windows: 82 executed (23+37+22), 0 failed/skipped | 82 | 8 | 10 | true |
| CP-4 | S1-S4 | CP-2 | runner-placement-kill | `/*/Antiphon.SessionRunner.Tests/(HerdrNamedTabPlacementTests*)\|(HerdrPaneChildKillTests*)\|(HerdrRunnerSessionTests*)/*` | V-6, R-3 | Linux/Windows: 36 executed (23+6+7), 0 failed/skipped | 36 | 5 | 6 | true |
| CP-5 | S1-S4 | CP-2 | runner-events-routes | `/*/Antiphon.SessionRunner.Tests/(HerdrEventPumpTests*)\|(HerdrStatusPushTests*)\|(HerdrPlacementCheckRouteTests*)/*` | V-6 | Linux/Windows: 14 executed (5+4+5), 0 failed/skipped | 14 | 3 | 4 | true |
| CP-6 | S1-S4 | CP-2 | incidental-rules | `/*/Antiphon.SessionRunner.Tests/(GrokRulesFileLaunchTests*)\|(GrokRulesRunnerRefusalTests*)\|(GrokRulesStoreFailureTests*)/*` | V-6 | Linux/Windows: 38 executed (21+11+6), 0 failed/skipped | 38 | 4 | 5 | true |
| CP-7 | S1-S4 | CP-2 | incidental-native | `/*/Antiphon.SessionRunner.Tests/(GrokRulesAdoptionTests*)\|(CodexLaunchRefusalTests*)\|(RunnerStartupReadinessTests*)/*` | V-6, R-3 | Linux/Windows: 9 executed (4+3+2), 0 failed/skipped | 9 | 6 | 7 | true |
| CP-8 | S1-S4 | CP-2 | runner-labels | `/*/Antiphon.SessionRunner.Tests/(HerdrClientSurfaceTests*)\|(HerdrLabelFollowSchedulingTests*)\|(HerdrLabelObservationTests*)\|(HerdrLabelSnapshotTests*)/*` | V-6 | Linux/Windows: 135 executed (3+25+71+36), 0 failed/skipped | 135 | 5 | 6 | true |
| CP-9 | S1-S4 | CP-2 | runner-disposal | `/*/Antiphon.SessionRunner.Tests/(HerdrPaneDisposalServiceTests*)\|(HerdrPaneDisposalConcurrencyTests*)\|(HerdrPaneDisposalIdentityTests*)\|(HerdrPaneDisposalStopRegressionTests*)/*` | V-4, V-6 | Linux/Windows: 167 executed (92+27+35+13), 0 failed/skipped | 167 | 6 | 7 | true |
| CP-10 | S1-S4 | `tests/Antiphon.Tests -> bin-c801-server/` | server-attach-parity | `/*/Antiphon.Tests.Application/(AgentAttachHerdrTests*)\|(HerdrAlwaysOnChannelParityTests*)/*` | V-5, R-3 | Linux/Windows: 40 executed (16+24), 0 failed/skipped | 40 | 12 | 14 | true |
| CP-11 | S1-S4 | CP-10 | server-label-disposal | `/*/Antiphon.Tests.Application/(HerdrLabelFollowWireTests*)\|(HerdrLabelFollowFlowTests*)\|(HerdrPaneDisposalEndpointTests*)\|(HerdrPaneDisposalApplicationTests*)/*` | V-5 | Linux/Windows: 51 executed (4+19+14+14), 0 failed/skipped | 51 | 5 | 6 | true |
| CP-12 | S1-S4 | CP-10 | server-wire | `/*/Antiphon.Tests.Agents/HerdrPaneDisposalHttpWireTests/*` | V-5 | Linux/Windows: 9 executed, 0 failed/skipped | 9 | 2 | 3 | true |
| CP-13 | S1-S4 | CP-10 | server-script | `/*/Antiphon.Tests.Scripts/HerdrPaneScriptTests/*` | V-5 | Linux/Windows: 10 executed, 0 failed/skipped | 10 | 4 | 5 | true |
| CP-14 | S1-S4 | CP-10 | assembly-unit-smoke | `/*/*/*/*[Category=Unit]` | R-2 | Both OSes: >=3000 executed, fresh TRX, no new failures; known inherited failures individually reported | 3000 | 8 | 5 | true |

## Operator sanction and handoff

No further operator decision is needed to publish this plan or implement the required Linux and
Windows transport/coverage fix: the dated card decision supplies that direction. The production
client change is a necessary dependency, now explicit, not an optional fake-only shortcut.
Commission the separate Windows task and allow the stated budgets. No live Herdr session,
production runner restart, deployment, destructive cleanup, or paid provider run is part of this
acceptance. macOS native execution needs a host before it can be claimed, but that does not block
the specified Linux/Windows acceptance. Any proposed reduction of the 110/497 execution roster,
relaxation of disposal identity, or change to production shell/lifecycle semantics goes back to
the operator; it is not pre-approved by this plan.

Publish this Plan through `scripts/delegate.ps1 -Land 9ec344d4` after its pushed commit has settled,
then dispatch TestDesign on this artifact (not the historical skip plan). The source branch is
fast-forward-only; do not rebase/amend/reset its pushed commits. If the land front door refuses a
still-working Plan task, the caller repeats it after settlement rather than manually merging.
