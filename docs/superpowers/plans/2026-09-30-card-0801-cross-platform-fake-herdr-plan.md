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

Static recount at the TestDesign source SHA used each actual class declaration (not its filename)
and all `[Test]`/`Arguments` attributes, including attributes on the method's own line. Server
method counts in table order are 10, 9, 3, 11, 8, 13, 7, 8. Runner method/result counts below were
independently reproduced. The linked `Antiphon.TestSupport.TestClassificationGuardTests` adds one
result to CP-1/CP-2, outside the 497. The two new focused classes add 24, also outside that census.

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

**CARD-0690 / CARD-0821 hang boundary.** The incidental Unit classes include
`GrokRulesFileLaunchTests`, `GrokRulesStoreFailureTests` (all six argument cases start the fake,
even the PtyHost arms), and `CodexLaunchRefusalTests`. CP-6 executes the first two whole classes
plus `GrokRulesRunnerRefusalTests` (38 results). CP-7 executes all Codex refusal cases plus
`GrokRulesAdoptionTests` and `RunnerStartupReadinessTests` (9 results), with real teardown and a
fresh TRX. CP-7 retains CARD-0821's named oversized Codex regression. CARD-0856's historical
“12 methods in two classes” is not a complete current-source census. Completion of these rows
proves that the selected fake consumers no longer hang; it does not establish that unrelated
runner Unit failures or the whole Unit lane are fixed. The broad
CP-14 is **Antiphon.Tests**, not SessionRunner.Tests. Whole-lane qualification remains with
CARD-0690/CARD-0821 and broader lane/performance work with CARD-0856.

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

Peer verification must compare the public client's `InstanceId` with the independently held
child `Process.Id` and `StartTime`, and differ from the parent's identity. Reconnect to that same
child must remain equal; replace it with a second child at the **same endpoint** and it must
change. Capturing just a PID is insufficient because PIDs are reused. The listening child must
create/bind its own listener (credentials belong to the process that listened). Missing/invisible
PID, nonpositive PID, short native result, failed native call or unreadable start time gives null;
the existing real disposal backend must then return `GuardUnavailable` with zero `pane.close`.
Do not replace it with a test backend returning a fabricated `HerdrServerInfo`. A narrow injected
native-read failure can drive that negative branch; the successful identity tests always use
real OS credentials. Treat cross-PID-namespace identity as unavailable if the peer is not
inspectable, rather than guessing from socket-file ownership.

### D2. Per-instance endpoint configuration, with explicit fixture wiring

Add optional `HerdrSettings.SocketPath` as an instance-scoped low-level endpoint. Precedence:
internal `socketOverride` (existing seam) -> configured nonblank `SocketPath` -> explicit
`Session` -> `HERDR_SOCKET_PATH` -> `HERDR_SESSION` -> platform default. With SocketPath unset,
Windows keeps its existing behavior. Unix defaults follow the versioned upstream resolver above;
missing `.config` directories must not cause a relative path by accident. Reject a relative
configured Unix endpoint with a clear unavailable/configuration error; do not mkdir from clients.
On Unix, the reserved literal session name `default` resolves to `herdr/herdr.sock`, not
`herdr/sessions/default/herdr.sock`, matching upstream `normalize_name`/`active_api_socket_path`.
Blank or relative XDG/HOME inputs cannot produce an implicit relative connection: use the next
valid absolute base or fail explicitly. Windows keeps its current session/default interpretation
and ApplicationData precedence; do not silently apply upstream's Unix normalization there.

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
through arbitrary directories or sweep unverified socket paths.

**Crash recovery is explicit, not a promise from `finally`.** SIGKILL/host termination closes
handles but can leave a pathname socket. The endpoint lease stores a small 0600 ownership marker
inside its 0700 directory: schema, fixture owner, random lease ID, exact path, owner PID/start
identity, host/PID-namespace identity, and bound socket identity. On the next fixture allocation,
reclaim at most 16 marked
leases from the dedicated `/tmp/ah-<32 hex>` namespace whose same-user owner is positively dead.
Revalidate marker, directory and socket identity without following symlinks, under an exclusive
lease-file lock, immediately before unlink. A live owner, unreadable identity, foreign/unmarked
directory, changed socket, or unexpected extra file is preserved. Remove only the recorded socket,
marker and empty directory; never recursive-delete. No age-only cleanup or operator session path.
The parent of an owned child cleans its exact lease after confirmed child exit. After a machine
crash, reclamation is deferred until the next fixture run; unknown residue is reported, not erased.
Windows kernel pipe names disappear after the last handle closes; its crash test proves a new
listener can bind the same name. No production endpoint cleanup is added.

Windows keeps `%APPDATA%\\herdr\\sessions\\antiphon-herdr-test-<guid>\\herdr.sock` as the existing
logical pipe name, with byte mode and max instances 4; it is not a filesystem Unix endpoint.
Serialize listener ownership on the explicit endpoint lease so two fakes cannot claim one lease,
while allowing one fake's multiple pipe instances. For the native occupied-name error test use
an owned `NamedPipeServerStream` with max instances 1; default multi-instance pipes alone do not
provide collision refusal. Test both the lease guard and an actual OS bind failure. On Unix,
reserve directories using exclusive creation (not check-then-CreateDirectory), record ownership
before binding, and inspect real modes/path bytes in the assertions. Long TMPDIR or checkout
paths never participate in endpoint construction. A replacement socket must survive old disposal.

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

Use an optional instance `TimeProvider` (System by default) in the actual readiness deadline;
the existing test project already references `Microsoft.Extensions.TimeProvider.Testing`.
Tests hold the real listener-start gate, call the tokenless API, advance the injected clock to
4.999 seconds (still pending) then 5 seconds (faulted), and assert the actual returned task's
outcome. Do not implement a second deadline in a test wrapper. Expose the actual accept-loop
completion for fault tests so they can assert all waiters are completed when the loop terminates.
Acquire startup waiters before `Start()` as well as after terminal failure, so a premature
success signal cannot be hidden by replacing the promise afterward.
An independent two-second observation bound ends in a named Shouldly assertion; neither an
uncaught WaitAsync timeout nor a test/row watchdog is acceptable red evidence. Release gates,
cancel requests and join owned tasks in finally even when an assertion fails.

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

In particular, `CodexWindowsLaunchPolicy.Apply` returns the request unchanged on Unix, and the
Grok argv policy is host-aware. CP-6/CP-7 must retain each test's Windows refusal assertions and
exercise the existing Unix contract with owned native fixtures. For an allowed Unix argv case,
assert actual runtime launch/registration and exact script/argv at the native/fake boundary,
then stop its owned child; do not replace it with a pure `Apply(request)==request` test. For a
refused Unix case, assert the applicable real typed refusal and zero side effects. Keep result
counts and argument matrices, but use names that describe both OS expectations where needed.
Production-policy changes, fake preflight refusals, injected successful ready states or seeded
confirmation receipts are not fixture portability fixes. Report a product gap if these real
paths cannot satisfy the existing contract; do not mask it to obtain 497 green results.

## Slices and exact expected footprint

Only this plan file changes in this TestDesign task. The following is the prospective Code footprint;
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

### Collision audit (TestDesign source and live board, 2026-09-30)

Reserve the exact S1-S4 paths above, including both project files and both living docs; no
production runtime, Grok adapter, quota classifier, dispatcher, checkpoint-tool or deployment
file is authorized by this plan. Shared assembly builds still consume host build slots even
when edited files do not overlap. Recheck task/source SHA and file scope before Code dispatch;
the statuses below are observations, not ongoing concurrency permission.

| Work | Exact collision / coordination |
|---|---|
| CARD-0849, Review `6f85f65f` observed Dispatched | Its footprint includes `docker-compose.server2-runner.yml`, `docker-compose.server2-runner.temp.yml`, `scripts/c590-remote.sh`, deploy/verification scripts, `Infrastructure/DockerStackContractTests.cs`, `Scripts/RemoteScriptContractTests.cs`, `docs/docker-stack.md`, `docs/bootstrap.md`. No direct S1-S4 edit collision. CP-14 may execute its Unit tests; staging depends on usable NuGet apphost packs. No rollout/recreation from this task. |
| CARD-0719, Review `1c31680d` observed Dispatched | Quota normalizer/tailer/contracts, server recovery/classifier and `docs/ops-http.md`/session owners are outside S1-S4. Same runner assembly and server Unit acceptance create build/evidence overlap. If its final diff changes either test project file, serialize those staging edits. |
| CARD-0505, card in Review | Dispatcher settings/concurrency surfaces are outside Herdr transport. CP-14 may execute those Unit tests. Do not edit dispatcher defaults or bypass a same-stage admission refusal. |
| CARD-0778, queued follow-on scope | **Correction to the brief:** source at published TestDesign SHA `40e77e2212a3d936f303073321dce11b75a1f0f2` has S1 capture fixtures in `Antiphon.Tests`, S2 Grok adapter work, and no `Antiphon.SessionRunner.Tests` edits. Its CP-1 classes are `GrokStartupReadinessTests`, `RunnerGrokAdapterReadyTests`, `GrokStartupCaptureStoreTests`, `RunnerGrokAdapterTrustPromptTests`, `RunnerGrokAdapterSignInPromptTests`, `GrokAdapterTests`, `RunnerGrokAdapterTurnCompleteTests`, `RunnerCodexAdapterReadyTests`; CP-2 classes are `GrokStartupReadyOrderingTests`, `GrokRulesReadyOrderingTests`, `GrokRulesQueueBarrierTests`. **Intersection with the 24/497 runner roster is empty.** CP-14 overlaps its existing Unit classes and any new ones after landing; shared FakeGrok staging/runtime is a behavioral dependency. If the caller has a newer S1 plan touching runner tests, compare that exact SHA before dispatch rather than invent overlapping classes. |
| CARD-0804 / CARD-0805 | Checkpoint fixture/tool cleanup and landing-conflict work touches `tests/Antiphon.Tests/Checkpoints/` and `tools/Antiphon.Checkpoints/`, outside S1-S4. CP-14 and the checkpoint driver depend on its integrated result; preserve both owners' changes at land. This card changes no checkpoint parser/cleanup code. |
| CARD-0856 | Owns wider Unit/Integration scheduling, suite wall-clock, incidental interface fakes and prebuilt Herdr lane work. This card only makes the named closure execute and dispose correctly. |

The live scoped listing returned no active CARD-0778 task at inspection; its latest successful
TestDesign artifact was read from `origin/feat/card-task-307e1a7f`, without integrating that
branch. No rebase/amend/reset of this assigned branch; landing owns target integration.

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

New `FakeHerdrServerListenerTests` has **12 unparameterized results**, all OSes:
`C801_StartupFailureFaultsAllWaiters`, `C801_AcceptFailureFaultsLaterWaiters`,
`C801_UnawaitedFailureIsObserved`, `C801_DefaultReadinessDeadline`,
`C801_CallerCancellationDoesNotStopListener`, `C801_DisposeBeforeReadySettlesWait`,
`C801_ParallelEndpointsAreIsolated`, `C801_EndpointLengthAndPermissions`,
`C801_BindCollisionDoesNotDeleteOwner`, `C801_DisposeAllowsLeaseRebind`,
`C801_CrashedOwnerLeaseReclaimed`, `C801_LiveOrForeignLeasePreserved`.
Use a real occupied Unix endpoint for the collision case, not permission denial under root.
Windows exercises the corresponding exclusive named-pipe collision; permissions assertions
branch by transport while every test body still executes. Default-deadline uses an injected
listener-start gate and the actual deadline's clock, not a five-second arbitrary scheduling
sleep. Keep in-process lifecycle tests Unit; mark process-spawning cases Integration and with
the assembly's ProcessSpawnLimit. The crash case launches an owned listener child, proves its
native ping, terminates only that child, observes exit, invokes the same lease recovery used by
ordinary allocation and asserts real path removal/rebind. The preservation case holds a live
owner and seeds foreign/unmarked/symlink/changed-file cases in its own scratch area, then checks
those physical entries and live ping survive. Do not use ambient `/tmp` residue as test data.
The crash child must create its lease through the compiled fixture helper (load the test assembly
in the owned PowerShell process if needed); fabricating a dead-owner marker in the parent does
not prove crash cleanup. Parent-held restart leases remain parent-owned and are a separate case.

New `HerdrTransportTests` has **12 unparameterized results**, all OSes:
`C801_NativePing`, `C801_RequestAndSubscriptionCoexist`, `C801_ConnectCancellation`,
`C801_ConnectDeadline`, `C801_MissingEndpoint`, `C801_MalformedResponse`,
`C801_PathOverridePrecedence`, `C801_NamedAndDefaultResolution`,
`C801_ConnectedPeerIdentity`, `C801_DifferentPeerIdentityAfterRestart`,
`C801_UnavailableIdentityRefusesDisposal`, `C801_RepeatedConnectDisposeReleasesHandles`.
For different-peer identity, use distinct owned child processes, not two listeners in the same
process (the same process must have the same identity). Reuse the test assembly as an owned
child mode or a PowerShell child that binds a stream; no real Herdr/provider or new daemon.
Prefer the PowerShell child embedded in `tests/Shared/HerdrTestProcess.cs` for a fixed footprint:
the generated TUnit entry point is not an assumed child-mode dispatcher. Use `pwsh` on Unix,
`pwsh.exe` on Windows, arguments through ArgumentList, with redirected stdout handshake and
owned-process exit cleanup. A test listener must not supply its own PID in the pong.
Resolver tests should supply an explicit environment snapshot to the pure resolver helper rather
than mutate the test host's environment. Protocol tests must assert actual requests/outcomes.

`C801_NativePing` uses the public `IOptions<HerdrSettings>` construction and `SocketPath`, not
the internal override or an injected stream. Also assert the disabled default refuses before
opening a connection. `C801_NamedAndDefaultResolution` supplies Linux/macOS/Windows snapshots,
including long and non-ASCII configuration roots, empty/missing environment values and explicit
`Session=default`; preserve the Windows baseline and pin any intentional Unix normalization to
the upstream resolver. Negative endpoint tests own short missing paths; they must not inspect
or accidentally contact an operator session. `C801_UnawaitedFailureIsObserved` checks a real
loop-completion observation/disposal and preserved `ListenerFault`, not a forced GC race.
Use a narrow pending-native-connect seam for cancellation/deadline error mapping when the OS
would refuse a missing Unix socket immediately; successful native transport remains unmocked.
`C801_RepeatedConnectDisposeReleasesHandles` samples owned stream/socket disposal, joins listener
handlers, and uses bounded native FD/handle counts or a child process for isolation; a fake
counter incremented by the test is not resource-release evidence.

### Positive controls (later Mutation, method-scoped)

Every identifier below is a required assertion message in the named test. The two-second
observation budget is independent of production five-second readiness and the row watchdog.
Capture the actual operation's completion/error, then use Shouldly assertions; a thrown harness
TimeoutException is not a kill. Default-deadline mutation advances the real deadline's fake
clock, so it also fails within two wall seconds. A child start may use a separate bounded setup
budget, but the mutated operation must reach its named assertion within two seconds after setup.

| PC | Deliberate defect (one variant at a time) | Method and required failing assertion |
|---|---|---|
| PC-1 | Signal readiness before bind, or swallow the bind exception as successful startup | `C801_StartupFailureFaultsAllWaiters`: `C801_START_ORIGINAL_CAUSE` requires each waiter's captured error and ListenerFault to be the injected original SocketException; real occupied-socket case also runs. An injected bind failure alone is green error-path coverage, not a mutation. |
| PC-2 | Remove `FaultListening` propagation; separately allow `ResetListening` to erase terminal accept failure | `C801_StartupFailureFaultsAllWaiters` / `C801_AcceptFailureFaultsLaterWaiters`: after actual loop completion, `C801_ALL_WAITERS_SETTLED` asserts old and subsequent wait tasks are completed, then `C801_START_ORIGINAL_CAUSE` checks the error. No blocking await of an unsettled wait. |
| PC-3 | Remove tokenless default readiness deadline | `C801_DefaultReadinessDeadline`: after advancing through the five-second boundary, `C801_DEFAULT_DEADLINE_SETTLED` requires completion and `C801_DEFAULT_DEADLINE_ERROR` requires the fake's timeout with endpoint context. Release the bind gate in finally; no watchdog red. |
| PC-4 | Restore non-Windows refusal; separately route Unix to a named pipe | `C801_NativePing`: `C801_NATIVE_PING_NO_ERROR` asserts captured client error is null, then pong version/protocol and the listener's real ping request are checked. Uses configured public client, never injected transport success. |
| PC-5 | Return own PID, constant or null; separately drop start ticks / cache identity across reconnect | `C801_ConnectedPeerIdentity` / `C801_DifferentPeerIdentityAfterRestart`: `C801_PEER_IS_CHILD`, `C801_SAME_PEER_STABLE`, `C801_REPLACED_PEER_DIFFERS` compare independently observed child identities, same endpoint. Null and own-PID variants fail the first comparison. |
| PC-6 | Omit unlink; separately unlink on failed bind or remove a replacement path | `C801_DisposeAllowsLeaseRebind`: `C801_OWNED_SOCKET_REMOVED`; `C801_BindCollisionDoesNotDeleteOwner`: `C801_OWNER_ENDPOINT_PRESERVED` checks path identity and real ping, and `C801_REPLACEMENT_PRESERVED` checks a replacement entry survives old disposal. |
| PC-7 | Await the subscription handler inside accept | `C801_RequestAndSubscriptionCoexist`: await actual subscription registration, issue second RPC with 250 ms cancellation, capture error; `C801_RPC_WHILE_SUBSCRIBED` requires success and `C801_EVENT_STILL_DELIVERED` checks the subsequent event. Cancel subscription in finally before joining. |
| PC-8 | Return fabricated identity on native lookup failure, or remove the null-identity disposal guard | `C801_UnavailableIdentityRefusesDisposal`: `C801_IDENTITY_UNAVAILABLE_REFUSES` requires GuardUnavailable from the real backend; `C801_UNVERIFIED_PANE_NOT_CLOSED` requires zero close requests. Only native-read failure is injected. |
| PC-9 | Change directory/socket modes to 0777 or derive endpoint from long TMPDIR | `C801_EndpointLengthAndPermissions`: `C801_PRIVATE_DIRECTORY`, `C801_PRIVATE_SOCKET`, `C801_SHORT_NATIVE_ENDPOINT` inspect actual mode and UTF-8 bytes. Run the long-TMPDIR arm in an owned child with only its environment changed. |
| PC-10 | Remove dead-owner reclamation; separately treat live/foreign/unmarked lease as reclaimable | `C801_CrashedOwnerLeaseReclaimed`: `C801_CRASH_RESIDUE_REMOVED`; `C801_LiveOrForeignLeasePreserved`: `C801_FOREIGN_LEASE_PRESERVED` asserts physical sentinel/path contents and live owner ping. Windows asserts native name rebind after child exit. |
| PC-11 | Bypass Enabled, ignore configured SocketPath, or choose Unix transport on Windows | Existing `Optional_backend_defaults_off_and_never_silently_falls_back` and `C801_NativePing`/`C801_PathOverridePrecedence`: `C801_DISABLED_NO_CONNECTION`, `C801_CONFIGURED_ENDPOINT`, `C801_NATIVE_PING_NO_ERROR`. Windows branch defect must be killed on Windows. |

Mutation reports each exact changed line, method filter, red assertion and restored-source green.
Build failure, zero-test discovery, a skip, or an unrelated fixture failure is not a killed control.
The ordinary 110-result run is the positive execution control; it cannot be replaced by discovery.
Use `/*/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests/<exact method>` or
`/*/Antiphon.SessionRunner.Tests/HerdrTransportTests/<exact method>` for new controls, and
`/*/Antiphon.SessionRunner.Tests/HerdrClientTests/Optional_backend_defaults_off_and_never_silently_falls_back`
for the existing disabled-default control. Report the chosen exact method, not a namespace run.
This TestDesign establishes falsifiable oracles; Mutation must still demonstrate the actual red
and restored green. CARD-0846/CARD-0719's timeout-only and injected-model lessons remain review
gates. No reimplementation of the connector, readiness algorithm or disposal verdict in tests.

### Execution and cheaper acceptance

Use the checkpoint tool, not ad hoc dotnet test. The literal-filter manifest below is extracted
to an ignored YAML file (JSON syntax), using the tool's supported positional manifest argument.
This is the one documented input-format deviation from the owner's `run --plan` recipe: the
baseline table parser cannot store raw OR pipes. It does not change builds, counts or scheduling.
No checkpoint tooling edit is authorized. Save this extraction as an ignored helper or run it
in PowerShell; it only parses/writes data and does not build:

```powershell
$c801Plan = 'docs/superpowers/plans/2026-09-30-card-0801-cross-platform-fake-herdr-plan.md'
$c801Text = Get-Content -Raw $c801Plan
$c801Match = [regex]::Match($c801Text, '(?s)<!-- C801-MANIFEST -->\s*```json\s*(.*?)\s*```')
if (-not $c801Match.Success) { throw 'C801 manifest is missing' }
$c801Manifest = $c801Match.Groups[1].Value | ConvertFrom-Json
if ($c801Manifest.checkpoints.Count -ne 14) { throw 'C801 must have 14 rows' }
foreach ($c801Row in $c801Manifest.checkpoints) {
    if ($IsWindows) {
        $c801Row.estimatedMinutes = $c801Row.estimatedMinutesWindows
        if ($c801Row.PSObject.Properties['minExecutedWindows']) {
            $c801Row.minExecuted = $c801Row.minExecutedWindows
        }
    }
}
# ManifestLoader does not apply the table importer's Windows estimate override itself.
$c801Manifest | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 .antiphon/c801-checkpoints.yaml

# One permitted bootstrap build per host, if a current tool output is unavailable.
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c801-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c801-tool/ --property:UseAppHost=false -nodeReuse:false --nologo
# Run from this output with --no-build so launcher compilation cannot bypass the build slot.
dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c801-tool/ --property:UseAppHost=false -- run .antiphon/c801-checkpoints.yaml --rows CP-1 --max-wait 50s
# After S3 + S4 have also been committed:
dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c801-tool/ --property:UseAppHost=false -- run .antiphon/c801-checkpoints.yaml --rows CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-13,CP-14 --max-wait 50s
```

Call `wait <run-id> --max-wait 50s` until exit is not 75; preserve the per-row CHECKPOINT report,
expanded class counts, failures and TRX. Tool bootstrap build, if needed, runs behind
`scripts/build-slot.ps1`; row drivers own their slots and must not be nested inside that wrapper.
A slot timeout (4) is not a license to build unleased. Use one isolated build per build cell;
subsequent rows reuse it only inside the same After group. Rows are serial to keep native-process
and DB evidence bounded and avoid cross-assembly resource contention. No unlisted broad runs.

### Separate platform verification

| Commission | Platform / SHA | Exact selection and receipt | Estimate |
|---|---|---|---:|
| W-1 (separate task, not a fifteenth checkpoint) | `-Platform Windows`, pinned to final Linux-tested Code SHA | Extract the same manifest with Windows estimates; run `--rows CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-13,CP-14`. Fresh named-pipe, peer PID/start-time, listener/crash, native process, HTTP/script and Unit TRX. CP-2..CP-13 total 632 results (497 existing runner + 24 new + 1 classification + 110 server), zero failed/skipped. CP-14 has its separate Unit floor. | 88 min |

CP-1 is the intermediate Linux slice check; W-1 repeats those assertions through CP-2 at the
final SHA, not another intermediate build. A Windows Code task that implements S1-S2 itself
also runs CP-1 (10 minutes). No runner selection can substitute Linux for W-1. macOS implements
the Unix transport with its own identity branch; when a host is available, CP-2 is the bounded
native qualification (41 executed). Linux is not evidence that the macOS native call ran.
Required merge evidence is Linux plus Windows; record macOS native qualification as pending.

### Acceptance boundary

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
native-harness work; split Code at S2/S4 as described in the handoff without changing coverage.
Review is separate; Mutation is method-scoped and budgeted separately (roughly 30-60 minutes).

### Checkpoints

The 14 rows below are the closed execution list. Exact literal filters and roster tokens are
in the embedded manifest immediately below the summary; each OR operand ends in `*`.
The current `PlanTableImporter.SplitRow` requires Markdown-escaped pipes and does not honor
backtick spans. To obey this brief's literal-pipe requirement without modifying checkpoint
tooling (CARD-0804/0805 scope), **use the supported positional YAML-manifest input**, extracted
from this plan by the recipe above; do not run this summary through `--plan`. JSON is the flow
syntax subset of YAML accepted by `ManifestLoader`. Only this plan is tracked.

<!-- CARD-0801 split decision: Linux skips only the named CARD-0863/0864/0865/0866
     follow-on results in CP-6/9/10/11. Windows runs every result and retains its full floor. -->
| CP | After | Build | Group | Exact filter | Covers | Expected executed | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c801-core/` | initial-transport | manifest CP-1.filter | V-1, V-2, V-3, V-4, R-1 | Linux/Windows: 41 executed (12+12+16+1), 0 failed/skipped | 41 | 8 | 10 | true |
| CP-2 | S1-S4 | `tests/Antiphon.SessionRunner.Tests -> bin-c801-runner/` | final-transport | manifest CP-2.filter | V-1, V-2, V-3, V-4, V-6, R-1 | Linux/Windows: 41 executed (12+12+16+1), 0 failed/skipped | 41 | 8 | 10 | true |
| CP-3 | S1-S4 | CP-2 | runner-lifecycle | manifest CP-3.filter | V-6 | Linux/Windows: 82 executed (23+37+22), 0 failed/skipped | 82 | 8 | 10 | true |
| CP-4 | S1-S4 | CP-2 | runner-placement-kill | manifest CP-4.filter | V-6, R-3 | Linux/Windows: 36 executed (23+6+7), 0 failed/skipped | 36 | 5 | 6 | true |
| CP-5 | S1-S4 | CP-2 | runner-events-routes | manifest CP-5.filter | V-6 | Linux/Windows: 14 executed (5+4+5), 0 failed/skipped | 14 | 3 | 4 | true |
| CP-6 | S1-S4 | CP-2 | incidental-rules | manifest CP-6.filter | V-6 | Linux: 30 executed, 8 skipped (CARD-0863); Windows: 38 executed, 0 skipped; 0 failed | 30 | 4 | 5 | true |
| CP-7 | S1-S4 | CP-2 | incidental-native | manifest CP-7.filter | V-6, R-3 | Linux/Windows: 9 executed (4+3+2), 0 failed/skipped | 9 | 6 | 7 | true |
| CP-8 | S1-S4 | CP-2 | runner-labels | manifest CP-8.filter | V-6 | Linux/Windows: 135 executed (3+25+71+36), 0 failed/skipped | 135 | 5 | 6 | true |
| CP-9 | S1-S4 | CP-2 | runner-disposal | manifest CP-9.filter | V-4, V-6 | Linux: 144 executed, 23 skipped (CARD-0864); Windows: 167 executed, 0 skipped; 0 failed | 144 | 6 | 7 | true |
| CP-10 | S1-S4 | `tests/Antiphon.Tests -> bin-c801-server/` | server-attach-parity | manifest CP-10.filter | V-5, R-3 | Linux: 27 executed, 13 skipped (CARD-0865); Windows: 40 executed, 0 skipped; 0 failed | 27 | 12 | 14 | true |
| CP-11 | S1-S4 | CP-10 | server-label-disposal | manifest CP-11.filter | V-5 | Linux: 49 executed, 2 skipped (CARD-0866); Windows: 51 executed, 0 skipped; 0 failed | 49 | 5 | 6 | true |
| CP-12 | S1-S4 | CP-10 | server-wire | manifest CP-12.filter | V-5 | Linux/Windows: 9 executed, 0 failed/skipped | 9 | 2 | 3 | true |
| CP-13 | S1-S4 | CP-10 | server-script | manifest CP-13.filter | V-5 | Linux/Windows: 10 executed, 0 failed/skipped | 10 | 4 | 5 | true |
| CP-14 | S1-S4 | CP-10 | assembly-unit-smoke | manifest CP-14.filter | R-2 | Both OSes: >=3000 executed, fresh TRX, no new failures; known inherited failures individually reported | 3000 | 8 | 5 | true |

<!-- C801-MANIFEST -->
```json
{
  "schemaVersion": 1,
  "plan": "docs/superpowers/plans/2026-09-30-card-0801-cross-platform-fake-herdr-plan.md",
  "resultsRoot": ".antiphon/checkpoints",
  "parallel": {
    "maxRows": 1
  },
  "timeouts": {
    "rowMinutes": 15,
    "totalMinutes": 206
  },
  "builds": [
    {
      "id": "bin-c801-core",
      "project": "tests/Antiphon.SessionRunner.Tests",
      "outputPath": "bin-c801-core/"
    },
    {
      "id": "bin-c801-runner",
      "project": "tests/Antiphon.SessionRunner.Tests",
      "outputPath": "bin-c801-runner/"
    },
    {
      "id": "bin-c801-server",
      "project": "tests/Antiphon.Tests",
      "outputPath": "bin-c801-server/"
    }
  ],
  "checkpoints": [
    {
      "id": "CP-1",
      "after": ["S1","S2"],
      "build": "bin-c801-core",
      "group": "initial-transport",
      "filter": "/*/*/(FakeHerdrServerListenerTests*)|(HerdrTransportTests*)|(HerdrClientTests*)|(TestClassificationGuardTests*)/*",
      "expect": ["FakeHerdrServerListenerTests","HerdrTransportTests","HerdrClientTests","TestClassificationGuardTests"],
      "expectText": "Linux/Windows: 41 executed (12+12+16+1), 0 failed/skipped",
      "minExecuted": 41,
      "estimatedMinutes": 8,
      "estimatedMinutesWindows": 10,
      "serial": true
    },
    {
      "id": "CP-2",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-runner",
      "group": "final-transport",
      "filter": "/*/*/(FakeHerdrServerListenerTests*)|(HerdrTransportTests*)|(HerdrClientTests*)|(TestClassificationGuardTests*)/*",
      "expect": ["FakeHerdrServerListenerTests","HerdrTransportTests","HerdrClientTests","TestClassificationGuardTests"],
      "expectText": "Linux/Windows: 41 executed (12+12+16+1), 0 failed/skipped",
      "minExecuted": 41,
      "estimatedMinutes": 8,
      "estimatedMinutesWindows": 10,
      "serial": true
    },
    {
      "id": "CP-3",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-runner",
      "group": "runner-lifecycle",
      "filter": "/*/Antiphon.SessionRunner.Tests/(HerdrAttachTests*)|(HerdrLaunchShapeTests*)|(HerdrAdoptionSweepTests*)/*",
      "expect": ["HerdrAttachTests","HerdrLaunchShapeTests","HerdrAdoptionSweepTests"],
      "expectText": "Linux/Windows: 82 executed (23+37+22), 0 failed/skipped",
      "minExecuted": 82,
      "estimatedMinutes": 8,
      "estimatedMinutesWindows": 10,
      "serial": true
    },
    {
      "id": "CP-4",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-runner",
      "group": "runner-placement-kill",
      "filter": "/*/Antiphon.SessionRunner.Tests/(HerdrNamedTabPlacementTests*)|(HerdrPaneChildKillTests*)|(HerdrRunnerSessionTests*)/*",
      "expect": ["HerdrNamedTabPlacementTests","HerdrPaneChildKillTests","HerdrRunnerSessionTests"],
      "expectText": "Linux/Windows: 36 executed (23+6+7), 0 failed/skipped",
      "minExecuted": 36,
      "estimatedMinutes": 5,
      "estimatedMinutesWindows": 6,
      "serial": true
    },
    {
      "id": "CP-5",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-runner",
      "group": "runner-events-routes",
      "filter": "/*/Antiphon.SessionRunner.Tests/(HerdrEventPumpTests*)|(HerdrStatusPushTests*)|(HerdrPlacementCheckRouteTests*)/*",
      "expect": ["HerdrEventPumpTests","HerdrStatusPushTests","HerdrPlacementCheckRouteTests"],
      "expectText": "Linux/Windows: 14 executed (5+4+5), 0 failed/skipped",
      "minExecuted": 14,
      "estimatedMinutes": 3,
      "estimatedMinutesWindows": 4,
      "serial": true
    },
    {
      "id": "CP-6",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-runner",
      "group": "incidental-rules",
      "filter": "/*/Antiphon.SessionRunner.Tests/(GrokRulesFileLaunchTests*)|(GrokRulesRunnerRefusalTests*)|(GrokRulesStoreFailureTests*)/*",
      "expect": ["GrokRulesFileLaunchTests","GrokRulesRunnerRefusalTests","GrokRulesStoreFailureTests"],
      "expectText": "Linux: 30 executed, 8 skipped (CARD-0863); Windows: 38 executed, 0 skipped; 0 failed",
      "minExecuted": 30,
      "minExecutedWindows": 38,
      "estimatedMinutes": 4,
      "estimatedMinutesWindows": 5,
      "serial": true
    },
    {
      "id": "CP-7",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-runner",
      "group": "incidental-native",
      "filter": "/*/Antiphon.SessionRunner.Tests/(GrokRulesAdoptionTests*)|(CodexLaunchRefusalTests*)|(RunnerStartupReadinessTests*)/*",
      "expect": ["GrokRulesAdoptionTests","CodexLaunchRefusalTests","RunnerStartupReadinessTests"],
      "expectText": "Linux/Windows: 9 executed (4+3+2), 0 failed/skipped",
      "minExecuted": 9,
      "estimatedMinutes": 6,
      "estimatedMinutesWindows": 7,
      "serial": true
    },
    {
      "id": "CP-8",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-runner",
      "group": "runner-labels",
      "filter": "/*/Antiphon.SessionRunner.Tests/(HerdrClientSurfaceTests*)|(HerdrLabelFollowSchedulingTests*)|(HerdrLabelObservationTests*)|(HerdrLabelSnapshotTests*)/*",
      "expect": ["HerdrClientSurfaceTests","HerdrLabelFollowSchedulingTests","HerdrLabelObservationTests","HerdrLabelSnapshotTests"],
      "expectText": "Linux/Windows: 135 executed (3+25+71+36), 0 failed/skipped",
      "minExecuted": 135,
      "estimatedMinutes": 5,
      "estimatedMinutesWindows": 6,
      "serial": true
    },
    {
      "id": "CP-9",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-runner",
      "group": "runner-disposal",
      "filter": "/*/Antiphon.SessionRunner.Tests/(HerdrPaneDisposalServiceTests*)|(HerdrPaneDisposalConcurrencyTests*)|(HerdrPaneDisposalIdentityTests*)|(HerdrPaneDisposalStopRegressionTests*)/*",
      "expect": ["HerdrPaneDisposalServiceTests","HerdrPaneDisposalConcurrencyTests","HerdrPaneDisposalIdentityTests","HerdrPaneDisposalStopRegressionTests"],
      "expectText": "Linux: 144 executed, 23 skipped (CARD-0864); Windows: 167 executed, 0 skipped; 0 failed",
      "minExecuted": 144,
      "minExecutedWindows": 167,
      "estimatedMinutes": 6,
      "estimatedMinutesWindows": 7,
      "serial": true
    },
    {
      "id": "CP-10",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-server",
      "group": "server-attach-parity",
      "filter": "/*/Antiphon.Tests.Application/(AgentAttachHerdrTests*)|(HerdrAlwaysOnChannelParityTests*)/*",
      "expect": ["AgentAttachHerdrTests","HerdrAlwaysOnChannelParityTests"],
      "expectText": "Linux: 27 executed, 13 skipped (CARD-0865); Windows: 40 executed, 0 skipped; 0 failed",
      "minExecuted": 27,
      "minExecutedWindows": 40,
      "estimatedMinutes": 12,
      "estimatedMinutesWindows": 14,
      "serial": true
    },
    {
      "id": "CP-11",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-server",
      "group": "server-label-disposal",
      "filter": "/*/Antiphon.Tests.Application/(HerdrLabelFollowWireTests*)|(HerdrLabelFollowFlowTests*)|(HerdrPaneDisposalEndpointTests*)|(HerdrPaneDisposalApplicationTests*)/*",
      "expect": ["HerdrLabelFollowWireTests","HerdrLabelFollowFlowTests","HerdrPaneDisposalEndpointTests","HerdrPaneDisposalApplicationTests"],
      "expectText": "Linux: 49 executed, 2 skipped (CARD-0866); Windows: 51 executed, 0 skipped; 0 failed",
      "minExecuted": 49,
      "minExecutedWindows": 51,
      "estimatedMinutes": 5,
      "estimatedMinutesWindows": 6,
      "serial": true
    },
    {
      "id": "CP-12",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-server",
      "group": "server-wire",
      "filter": "/*/Antiphon.Tests.Agents/HerdrPaneDisposalHttpWireTests/*",
      "expect": ["HerdrPaneDisposalHttpWireTests"],
      "expectText": "Linux/Windows: 9 executed, 0 failed/skipped",
      "minExecuted": 9,
      "estimatedMinutes": 2,
      "estimatedMinutesWindows": 3,
      "serial": true
    },
    {
      "id": "CP-13",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-server",
      "group": "server-script",
      "filter": "/*/Antiphon.Tests.Scripts/HerdrPaneScriptTests/*",
      "expect": ["HerdrPaneScriptTests"],
      "expectText": "Linux/Windows: 10 executed, 0 failed/skipped",
      "minExecuted": 10,
      "estimatedMinutes": 4,
      "estimatedMinutesWindows": 5,
      "serial": true
    },
    {
      "id": "CP-14",
      "after": ["S1","S2","S3","S4"],
      "build": "bin-c801-server",
      "group": "assembly-unit-smoke",
      "filter": "/*/*/*/*[Category=Unit]",
      "expect": [],
      "expectText": "Both OSes: >=3000 executed, fresh TRX, no new failures; known inherited failures individually reported",
      "minExecuted": 3000,
      "estimatedMinutes": 8,
      "estimatedMinutesWindows": 5,
      "serial": true
    }
  ]
}
```
<!-- /C801-MANIFEST -->

### TestDesign validation receipt

Static checks completed at the stated source baseline: all eight server and 24 runner class
counts match the source census; all 14 manifest IDs are unique; all three isolated build paths
exist as project directories and satisfy the output-name rule; build reuse stays within its
slice group; every OR operand has its own parentheses and trailing wildcard; no filter contains
a Markdown escape. CP-2..CP-13 cover 35 distinct classes and sum to 632 planned results, including
the 24 new focused cases and one linked classification guard. The PowerShell extraction recipe
was executed as a data-only check and reproduced the embedded manifest exactly on Linux.
Time sums are 84 minutes Linux / 98 Windows including CP-1, and 76 / 88 for final CP-2..CP-14.
`git diff --check` passed. No build, TUnit discovery/execution, native listener, mutation or Windows
run was performed in TestDesign; those remain Code/Mutation/Windows verification obligations.

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

Publish this TestDesign task through the normal caller-owned landing front door after settlement,
then dispatch Code pinned to this plan's full published SHA. A practical split is S1-S2 plus CP-1,
then S3-S4 plus CP-2..CP-14, with a final separate W-1 task at the identical Code SHA. The second
slice must preserve all 607 existing results; no skip stage is commissioned. The source branch
is fast-forward-only; do not rebase/amend/reset pushed commits. The caller resolves land conflicts
through the landing service, not manual merges in this worktree.
