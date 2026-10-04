# CARD-0996: deterministic phone-home connection-end tests

Date: 2026-10-04. Stage: Plan; next stage: TestDesign (separate, not folded into this dispatch).
Inspected source: `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`.
Card: CARD-0996 on Antiphon, read in full through `scripts/card.ps1`.

## Outcome and scope

Repair the tests, preserving the endpoint's existing distinction between an observed
WebSocket exception and request cancellation. An abort is already logged deterministically
as an ended Warning; the presence of exception-derived fields is what varies. A longer
wait cannot add properties to that immutable log entry.

Keep the original transport-code test name but drive a controlled receive exception.
Add an independently controlled cancellation test. Retain real Kestrel peer-abort coverage
and assert connection correlation there regardless of which valid transport path wins.
No production C# changes are planned. This dispatch writes only this plan.

Implementation footprint:

- `tests/Antiphon.Tests/Agents/PhoneHomeConnectionTests.cs`.
- `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs`.
- `tests/Antiphon.Tests/TestHelpers/PhoneHomeReceiveControl.cs` (new, test-only).
- `docs/ops-http.md` (one clarification of conditional transport fields).

## Ground truth

Line references describe the inspected commit, not a promise about later line numbers.

| Card assumption / possible interpretation | Code and evidence | Consequence |
|---|---|---|
| Peer `Socket.Abort()` necessarily produces `WebSocketException`. | `SessionRunnerEndpoints.cs:257-308` awaits the receive task with the request token. Cancellation selects `AbortReason`; a WebSocket exception selects `transport_abort` and populates `transportFault`. `PhoneHomeLiveConnection.cs:416-424` also permits ordinary return when cancellation is observed at the loop boundary. | OS scheduling cannot select a particular exception branch for a real peer abort. |
| Cancellation leaves the connection end unlogged. | `SessionRunnerEndpoints.cs:310-329` disconnects and calls `LogEnded` after either branch. `AbortReason`, at `:383`, returns `transport_abort` unless the host is stopping. | The card's missing fields are not a missing ended event or an incorrect disconnect classification. |
| `SocketError = null` is the formatter's no-inner-socket result. | `PhoneHomeTransportFault.cs:12-15` returns a named WebSocket error and a socket enum name or literal `none`. The endpoint calls it only when `transportFault` is present. `LogEnded`, at `:346-377`, omits both keys otherwise. | Absence of keys means no captured WebSocket exception; `none` means an observed WebSocket exception without an inner socket exception. Preserve that distinction. |
| The test already fails when `WsError` or connection ID is missing. | `PhoneHomeConnectionTests.cs:623-626` uses `?.ToString().ShouldBe(...)` for both ended `ConnectionId` and `WsError`; the null-conditional bypasses the assertion. `SocketError` is assigned to a local and asserted separately, exposing the first failure. | Fix both null-conditional assertion chains, not only `WsError`. |
| `WaitLiveAsync` proves the receive operation has started and logging has completed. | `PhoneHomeTestHost.cs:164-177` polls `Directory.SnapshotLive`. Acceptance publishes the live connection before endpoint logging and `ReceiveLoopAsync`. `WaitForLogsAsync` waits for an entry, then samples again after 50 ms. | Synchronize controlled injection on receive entry, and inspect final log snapshots after endpoint completion. Neither connection visibility nor a delay is a completion witness. |
| All aborts having transport codes is the established contract. | CARD-0716's historical D-6 wording and V-2 assumed peer abort produces an exception. Current production explicitly makes fields conditional. CARD-0996 authorizes either test separation or a new all-aborts logging policy. | Choose test separation; document the current conditional meaning instead of fabricating evidence. |
| The symptom is only isolated-method instability. | CARD-0996 cites CARD-0953 Final Review task `e4b3d539`: baseline `bf58e534`, 1/3 normal and 2/3 serialized class runs failed; candidate `1f6e2e40`, 1/3 normal and 3/3 serialized failed. Isolated method runs passed. Current source has 27 non-parameterized `[Test]` methods in this class. | Repeat the complete class on Linux in both scheduling modes; isolated-method green is insufficient. These are reported historical measurements, not runs performed by this Plan dispatch. |

## Decisions

### D-1. Test repair; retain production logging semantics

A real peer abort must yield one correlated `transport_abort` Warning and the matching
directory reason. Transport fields are mandatory when the receive operation throws a
WebSocket exception, and absent when cancellation supplies no such exception. Host stop
continues to mean `request_aborted` and close 1001 `server_stopping`.

Rejected: fill every canceled abort with `WsError=none`/`SocketError=none`. That changes an
operator-visible schema to satisfy a scheduling-sensitive test and blurs the distinction
between an absent exception and an exception without an inner socket error. Rejected:
change the production request token or extract a production logging abstraction solely
for this test. No product requirement or observed product failure justifies either.

### D-2. Control the receive boundary through the test host

Add an optional, per-host `PhoneHomeReceiveControl` argument at the end of
`PhoneHomeTestHost.StartAsync`. In the existing middleware, after `UseWebSockets` and before
the endpoint, apply it only to the configured connect request. Default null preserves the
current path for every other consumer.

The control wraps `IHttpWebSocketFeature`: delegate the real upgrade to Kestrel, then
return a WebSocket decorator to the endpoint. Keep the real client peer connected. The
decorator controls the pending receive consumed by `PhoneHomeFraming.ReadFrameAsync`,
while forwarding state, send, close, abort and disposal to the accepted socket. Do not
start a concurrent underlying receive. Forward close operations to the real socket so
existing disposal can finish its handshake with the peer.

Expose per-instance asynchronous signals for receive entry and endpoint completion.
Use `TaskCompletionSource` with `RunContinuationsAsynchronously`. For the fault cases,
release that receive with the selected `WebSocketException` while the request token and
host-stopping token are still uncanceled. For cancellation, link a test-owned CTS to the
original request token and cancel it only after receive entry; the pending controlled
receive must actually observe that token and throw `OperationCanceledException`. Keep
`ApplicationStopping` false. Record the observed cancellation/exception in the control as
a branch witness; never synthesize production log entries in the fixture.

A `finally` around the middleware's awaited next delegate completes the request-finished
signal. Tests await it before the final count/property/error assertions. Distinguish a
faulted request from mere completion. Dispose all CTS registrations and socket resources;
await owned tasks on failure as well as success. Use existing two-second receive/log
bounds and a completion bound derived from the existing three-second close handshake,
not a larger timeout on the old assertion. Do not broaden `WaitForLogsAsync` globally.

Rejected: sleep until the race happens to select an exception, swallow missing fields,
mask Kestrel's token with `CancellationToken.None`, or invoke private `LogEnded` by
reflection. None proves the endpoint's two real classification paths.

### D-3. Keep real transport coverage and strict assertions

Parameterize `Accept_and_end_lines_carry_the_connection_id_and_transport_codes` with two
controlled exceptions: `ConnectionClosedPrematurely` without an inner exception, and the
same WebSocket error with `IOException` wrapping `SocketException(ConnectionReset)`.
Require exact `SocketError` values `none` and `ConnectionReset`, respectively. This retains
coverage of the recursive exception walker without asserting an OS-specific kernel result.

Capture property values into locals, assert non-null/nonblank, then compare. Both accept
and ended connection IDs must be present and equal; require matching runner ID and epoch,
one accepted Information entry, one ended Warning, `transport_abort`, and matching status.
Assert the host was not stopping and the fault was injected after receive entry.

Add `Request_cancellation_logs_transport_abort_without_transport_codes`. Cancel after
receive entry while the host and peer are alive, await endpoint completion, then assert
one correlated ended Warning and directory `transport_abort`, absent `WsError` and
`SocketError` keys (use `Properties.ContainsKey`, not only the null-returning indexer),
and no exception-middleware Error. Its receive-cancellation witness must be set.

Retain `Peer_abort_is_a_warning_with_transport_abort_not_a_middleware_error` as an
uncontrolled, real Kestrel abort. Strengthen it with non-null connection correlation.
Either both exception fields are absent, or both are present and valid; partial fields
fail. Exact `ConnectionClosedPrematurely` and socket codes belong to the controlled test.
Do not turn the controlled fault test into this permissive either-path assertion.

### D-4. Linux proof, resolved placement, bounded repetitions

Read `GET /api/runner-defaults` and `GET /api/session-runners` again at execution dispatch.
The 2026-10-04 Plan read returned defaults revision 2, no per-kind overrides, an eligible
Linux global default and an eligible Windows runner. These are observations, not a host
pin. Omit `-Runner`; require `-Platform Linux` for the flake verification because the
reported symptom is Linux-specific. TestDesign itself needs no platform restriction.
Use the inherited API/build-slot resolution; do not embed a fleet address or checkout.

The fixed proof is one initial full-class run, ten full-class normal repetitions, and ten
full-class serialized repetitions. The exception to the ordinary 3-normal/2-loaded ceiling
is the demonstrated Review flake cited in Ground truth, exact filter
`/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/*`. Normal and serialized both failed
historically; neither is a substitute for the other. Revised ordinary budget is 24 minutes
plus slot waiting. Serialized means `TUNIT_MAX_PARALLEL_TESTS=1`; it is not a claim of loaded
host qualification. Record actual ambient load. Do not create artificial fleet load.

Every repetition is independently reported. No `[Retry]`, `--known-flaky`, retry-on-success
shell loop, timeout widening, or failure erasure. One-host repetition recreates each test's
Kestrel fixture; it does not prove fresh test-process behavior or eliminate every possible
flake. Stop after the prescribed proof, and report all failures.

## Implementation slices

| Slice | Files and concrete work | Tests / completion |
|---|---|---|
| S1: deterministic test inputs and assertions | Add `PhoneHomeReceiveControl.cs`; opt it into `PhoneHomeTestHost.cs`; update `PhoneHomeConnectionTests.cs` as D-2/D-3. Keep production files untouched. Implement the fixture and its consuming tests together so a helper-only commit is not mistaken for a completed fix. | Two argument cases of the original transport-code method, one new cancellation method, strengthened real peer-abort method. Remaining 25 existing methods keep their behavior. Commit and push before verification. |
| S2: document the observed logging contract | Add a short sentence beside the phone-home status/logging description in `docs/ops-http.md`: every end has a reason and correlation; exception fields appear only for a captured WebSocket exception; socket `none` describes that exception's missing inner socket error. Explain the correction from historical CARD-0716 prose in this plan, without rewriting historical artifacts. | Review documentation against `SessionRunnerEndpoints.LogEnded` and `PhoneHomeTransportFault.Describe`; commit and push. Execute CP-1 through CP-3 against the same frozen S1-S2 commit. |

If implementation discovers that this fixture cannot reach both endpoint branches without
production changes, return that concrete finding to Plan. Do not quietly switch to D-1's
rejected all-aborts logging policy.

## Provisional verification design (superseded by TestDesign below)

Historical proposal only. The appended **Verification design** is the executable authority,
including its smaller budget, roster and sole `### Checkpoints` table. Its repeat scope also
supersedes D-4's proposed repetition counts; D-1 through D-3 and the implementation slices
remain the fix design.

This is the proposed executable scope for the separate TestDesign stage to audit and
freeze. TestDesign must confirm the fixture lifetime/cancellation mechanics, exact roster,
red-capable guard inventory and repeat receipt validation before handing off to Code.
No tests or builds were run by this Plan dispatch; the code diagnosis is source-backed.

| ID | Class / methods | Required evidence |
|---|---|---|
| V-1 | `PhoneHomeConnectionTests.Accept_and_end_lines_carry_the_connection_id_and_transport_codes` (two argument results) | Observe receive entry before injection. Exact `WsError=ConnectionClosedPrematurely`; exact `SocketError=none` or `ConnectionReset`; nonblank equal IDs; one accept and one end; matching epoch/runner/status; no middleware Error after endpoint completion. Missing fields must fail even when other fields remain correct. |
| V-2 | `PhoneHomeConnectionTests.Request_cancellation_logs_transport_abort_without_transport_codes` (one new result) | Observe receive entry, cancel the request token, witness cancellation with the host still running, await endpoint completion. Require `transport_abort`, nonblank equal IDs, exactly one end, matching status, neither transport key present and no middleware Error. |
| R-1 | `PhoneHomeConnectionTests.Peer_abort_is_a_warning_with_transport_abort_not_a_middleware_error` | Real peer abort remains covered without receive injection. Require correlation and classification; a complete valid code pair or no pair is acceptable here only. |
| R-2 | Existing whole `PhoneHomeConnectionTests`, including `Host_stop_sends_a_going_away_close_before_the_socket_dies`, `Overflow_disconnect_logs_reason_epoch_and_pending_counts`, `Overflow_close_finishes_the_handshake`, `Receive_fault_close_finishes_the_handshake`, and both typed-disconnect tests | Exercise default fixture path, shutdown distinction, overflow/receive-fault cleanup, and waiter failures. Existing 27 methods become 28 methods / 29 results: parameterizing one adds one result and the cancellation method adds one. Retain all other cases. |
| R-3 | Same complete class, repeated in both scheduling modes | Every ordinal has all 29 results and zero failed/skipped; initial 29 plus 290 normal plus 290 serialized = 609 planned results. Counts are proposed source-derived counts, not measured receipts. |

### Guard sensitivity for TestDesign

Carry these candidates into method-scoped post-land Mutation, not ordinary Code test runs:

| PC | Compiling defect to inject | Exact detecting method and expected failure |
|---|---|---|
| PC-1 | Remove ended `ConnectionId` structured-state entry. | `Accept_and_end_lines_carry_the_connection_id_and_transport_codes*`: non-null/correlation assertion, both arguments. |
| PC-2 | Remove only ended `WsError` entry, retaining `SocketError`. | Same exact method prefix: non-null WebSocket-code assertion, both arguments; this specifically detects the old null-conditional blind spot. |
| PC-3 | Remove only ended `SocketError` entry. | Same exact method prefix: non-null/exact socket-code assertion, both arguments. |
| PC-4 | Change `AbortReason` to return `request_aborted` for the non-stopping host. | `Request_cancellation_logs_transport_abort_without_transport_codes`: exact reason assertion. |
| PC-5 | Populate transport keys with `none` when no WebSocket exception was captured. | Same cancellation method: key-absence assertion. |

All filters are `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/<method>`; the wildcard
above selects only the two arguments of that exact method. A build failure, missing test,
or a fixture timeout is not an intended red. Each control needs clean baseline, intended
assertion failure and restored green at the landed source. TestDesign must pin the concrete
edit and assertion for every control; ordinary Review checks the pending inventory.

### Execution and evidence

Run the checkpoint tool once for S1-S2 with this plan and exact committed source SHA.
Its optional Markdown `Repeat` column is supported by
`tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs`; `Min` remains the per-repetition
floor, which the runner multiplies by `Repeat`. CP-2/CP-3 need distinct isolated outputs.
No global `--repeat` override: it would also repeat CP-1.

This card's proposed ordinary scope is the entire affected integration class, rather than
the repository's default whole Unit lane: production is unchanged, the new control is
opt-in and only these tests use it, and this same class exercises the default fixture.
TestDesign must ratify this explicit bounded scope or add justified named rows before Code;
Code must not silently run a full assembly. No client, DB, native pty, or external-provider
work is implicated by this fixture. The test host uses random loopback ports and no live runner.

Bootstrap the checkpoint tool through the build-slot gate into an isolated `bin-c996-tool/`
output; then invoke the built tool DLL to avoid holding an outer slot while checkpoint
children acquire their own slots. Example from the checkout root (PowerShell):

```powershell
$plan = 'docs/superpowers/plans/2026-10-04-card-0996-phone-home-connection-end-flake-plan.md'
$sha = git rev-parse HEAD
Remove-Item Env:TUNIT_MAX_PARALLEL_TESTS -ErrorAction SilentlyContinue
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c996-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c996-tool/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c996-tool/net9.0/Antiphon.Checkpoints.dll run --plan $plan --after S1-S2 --expected-source-sha $sha --max-wait 50s
```

The bootstrap is the sole separately listed tool build (allow three minutes beyond the row
floor); verify the tool target framework before resolving its DLL path. Await every run;
if exit 75, call the built tool's `wait --run <returned-run-id> --max-wait 50s` until terminal.
Keep source frozen while any child is running. Each row takes the host build-slot gate;
exit 4 is not run/blocked, never permission to run unleased. Use the runner's Linux
`UseAppHost=false` handling. Retain generated receipts/TRX/logs in ignored checkpoint paths.
Validate source receipts separately for CP-1 with expected repeat 1 and CP-2/CP-3 with
expected repeat 10; preserve unedited CHECKPOINT lines and every ordinal's counts.

A red row stays red. If a different existing method fails, reproduce only that method at
the recorded base in a separate checkout before calling it inherited. Record the diagnostic
run and reason. Do not broaden, retry the full class until green, or claim the fix qualified
with unexplained red. Clean task-owned alternate outputs using the checkpoint tool and
verified bootstrap output inventory; do not remove daemon bins or force-add evidence.
Code/Review run the full-task-range `scripts/check-evidence-diff.ps1` check.

#### Provisional cost (superseded)

Ordinary Code checkpoint floor: 24 minutes (8 + 8 + 8), plus up to 3 minutes for the tool
bootstrap, approximately 35 minutes authoring, and observed slot waits. Code dispatch
estimate: 62 minutes plus slot waits. Ordinary Review reruns the approved scope once;
no additional repetition battery after its prescribed rows are green.

Provisional Mutation floor: five controls x three method-scoped build/run phases x two
minutes = 30 minutes, plus discovery/reporting and slot waits. Combined execution floor:
54 minutes ordinary + Mutation, excluding the tool bootstrap and authoring. TestDesign
must retain both component floors and revise estimates if its frozen design changes them.

#### Provisional checkpoints (superseded; not an executable manifest)

Every row is the Linux integration lane; `Serial=true` isolates the row from other builds
and rows, while `Environment` controls TUnit scheduling inside that row. Clear any inherited
`TUNIT_MAX_PARALLEL_TESTS` before launching the tool; CP-1/CP-2 use its ordinary default and
CP-3 alone pins it to 1.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment | Repeat |
|---|---|---|---|---|---|---|---:|---:|---|---|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c996-linux-once/` | linux-integration-once | `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/*` | V-1, V-2, R-1, R-2 | All 29 results; 0 failed/skipped | 29 | 8 | true | n/a | 1 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c996-linux-repeat/` | linux-integration-repeat-normal | `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/*` | V-1, V-2, R-1, R-2, R-3 | 10 ordinals x 29 = 290 results; 0 failed/skipped in every ordinal | 29 | 8 | true | n/a | 10 |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c996-linux-serial/` | linux-integration-repeat-serialized | `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/*` | V-1, V-2, R-1, R-2, R-3 | 10 ordinals x 29 = 290 results; 0 failed/skipped in every ordinal | 29 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` | 10 |

## TestDesign handoff

Confirm the test-host seam reaches the production endpoint without changing production
code; freeze the 29-result roster, the paired strict/absent-field assertions, five PC
edits and their costs, and the bounded integration scope. Validate the checkpoint table,
including per-row repeats and mixed-repeat receipt selection, against current tooling.
Then hand off to Code. No caller product decision is outstanding.

## Verification design

Frozen by TestDesign on 2026-10-04 at source
`4329cd3cc5d6eea75150917383c8798f9bb8521e`, task `5320c5ab`.
This appended section replaces the provisional verification scope above, including D-4's
609-result proposal. It does not change the test-only fix or production logging contract.
Code runs the one active checkpoint table below on Linux. No tests, builds, mutations or
runtime qualification were performed by TestDesign; counts and times here are planned.

### Inspection

Bodies were read before naming the following cases; these are source observations.

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| Entire `tests/Antiphon.Tests/Agents/PhoneHomeConnectionTests.cs`, all 27 tests, `WaitForLogsAsync`, `AssertHandshakeClosedAsync`, `IdentityIsNull`, `DummySpec`, `FakeRuntime` | Existing class has 27 non-parameterized results; V-1 adds one argument result, V-2 adds one method. The 50 ms log resample and peer-abort test's 200 ms delay are not completion evidence. R-1/R-2/R-3 retain the real transport and default-host boundaries. |
| Entire `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs`: `StartAsync`, `ConnectPeerAsync`, `WaitLiveAsync`, disposal, `CapturingLoggerProvider`, `CapturedLog`, `PhoneHomeScriptedPeer`, local-client substitutes | Nearest fixture for new `PhoneHomeReceiveControl.cs`. Use real upgrade/peer close handshake and per-host control; no production runner or database. Publication of a live connection precedes receive entry. Logger's indexer returns null for both missing and null-valued properties; V-2 must inspect keys. V-1/V-2/R-1. |
| `RollingRunnerSettings.Pair`/`Entry` | Configured-runner, unknown-ID, drain/retirement and same-secret boundaries stay in R-2; no new fixture behavior outside the selected connect path. |
| `SessionRunnerEndpoints` connect endpoint, `LogEnded`, `AbortReason`; `PhoneHomeLiveConnection.ReceiveLoopAsync`, receive core, `DisposeAsync`; entire `PhoneHomeFraming` and `PhoneHomeTransportFault`; entire `ExceptionMiddleware` | Fault versus cancellation versus normal close, nested socket error, lifetime stop, close completion and middleware exception handling -> V-1/V-2/R-1/R-2. No change to these production bodies. |
| `TestDbFixture.InitializeAsync`/teardown; assembly hooks in `ProductionRunnerGuard` and `PtyBackendEnvGuard`; `Antiphon.Tests.csproj` | Preserve eager production isolation and lazy DB warmup. This selection never calls the shared store or boots real Program. Provider/native process tests are excluded. |
| Entire `PlanTableImporter`, `RepeatEvidenceValidator`, shared `RepeatEvidenceAttribute`, `CheckpointRepeatFixture`; checkpoint tool `Validate` and target framework | First exact `### Checkpoints` wins; only the appended table has that heading. `Repeat` is supported; Min is per ordinal; CP-1/2 share Repeat=1 output, CP-3 needs Repeat=2 output. Receipt validation must select mixed-repeat rows separately. R-3. |
| Owners: project conventions; testing/build checkpoint, repeat, custody and mutation sections; session-runtime phone-home invariants; ops HTTP status description; orchestration stage/receipt contract | No broadened production policy, live endpoint action, queue delivery claim, or whole-Unit lane. |

Missing setup to implement in S1: `PhoneHomeReceiveControl.cs` does not exist at this SHA;
there is no controlled-receive overload, request-finished witness, endpoint-fault witness or
cancellation test yet. Code must deliver these together with their consuming assertions.
There is no unverified production seam that requires another Plan stage.

Freeze the fixture contract as follows:

1. Add the optional control last in `StartAsync`. Match only the selected runner's connect
   path and WebSocket request. Registration, status and every null-control host retain their
   existing execution path. All control state is instance-owned; no environment/static
   switch, logger fabrication or shared CTS. Use `RunContinuationsAsynchronously` signals.
2. After `UseWebSockets`, install an outer completion observer **before**
   `UseMiddleware<ExceptionMiddleware>()`. Keep feature replacement in the existing inner
   middleware before the endpoint. The inner await records and rethrows any endpoint
   exception; the outer await records any escaping pipeline exception and signals completion
   in `finally`. This is essential: completing only the current inner `finally` lets tests
   sample before `ExceptionMiddleware` writes its Error. An outer completion signal alone
   also cannot prove success, because that middleware handles exceptions. Await completion
   and assert both fault witnesses null before asserting the final log snapshot.
3. Wrap `IHttpWebSocketFeature` and delegate the actual upgrade. Override the decorator's
   `ReceiveAsync(ArraySegment<byte>, CancellationToken)` and funnel the Memory overload to
   the same control if implemented. `ReadFrameAsync` passes a byte buffer; there must be no
   overload that silently goes to the native receive. Do not run a second underlying receive.
   Forward state, close status/description, sends, close, abort and disposal to the real
   socket. In particular, forward `CloseAsync` to the underlying socket so it can read the
   real peer's acknowledgement after the controlled receive has finished.
4. Record receive entry with the token passed by the endpoint. Injection is legal only after
   that signal. For V-1 require original request token, effective token and host-stopping
   token uncanceled, and peer/socket open at injection; record the exact thrown exception
   instance at the receive boundary. For V-2 replace `HttpContext.RequestAborted` with a CTS
   linked to its original token and the test-owned CTS before endpoint parameter binding.
   Only cancel the test CTS after receive entry. The pending receive must await with that
   effective token and actually catch/rethrow its `OperationCanceledException`; record that
   observation there, not in the method that requests cancellation. Assert the original
   token and ApplicationStopping were still uncanceled at injection. Do not manufacture
   `OperationCanceledException` with an unrelated token or merely return a Close frame.
5. Provide observation-only use for R-1: wrap completion/fault observation but leave the
   accepted socket and request token unchanged. This replaces its 200 ms delay with the
   same final observation boundary while preserving a real `peer.Socket.Abort()`.
6. Bound receive entry/log observation at two seconds and completed pipeline at
   `PhoneHomeProtocol.CloseHandshakeSeconds + 2` seconds (currently five). This bounds a new
   completion witness; it does not enlarge the old log timeout. Restore request features
   and original token in `finally`, dispose linked CTS/registrations only after the request
   finishes, and release/cancel then await any owned pending receive during failure cleanup.
   Dispose peer before host, then control; no fire-and-forget cleanup. Keep
   `WaitForLogsAsync` unchanged for unrelated methods.

Boundary combinations are deliberate: fault x no inner socket and fault x
`IOException(SocketException(ConnectionReset))` are V-1's two arguments; request cancellation
with no exception and a running host is V-2; uncontrolled peer abort can reach either valid
end shape in R-1; host stop is the existing R-2 shutdown test. Default/no control, handshake,
request waiter, ticket and configured-runner boundaries run in R-2. Simultaneous forced
fault plus forced cancellation has no deterministic winning-branch contract and is excluded;
R-1 retains that real transport race without demanding an exception code. No extra Cartesian
matrix of socket enum values or host-stop-plus-injected-fault is needed for this repair.

### Delivery inventory

New/changed **product asynchronous delivery paths: zero**. There is no new session input,
queue producer, persistence transaction, recovery worker, notification or recipient. This
card does not claim prompt delivery; a queue row, event, Sent/ack flag, log entry, request or
scripted peer's Input frame cannot supply a matching complete UserPrompt transcript.
Busy/already-eligible recipients and crash/enqueue-failure recovery at durable handoffs are
therefore excluded: no such handoff is changed here. If Code discovers one is necessary,
return to Plan and add real-queue producer-to-recipient tests with complete UserPrompt
receipts before qualification; do not substitute these transport tests.

The test-only asynchronous handoffs are nevertheless explicit:

| Producer -> destination | Identity / persistence / recovery | Observable receipt and limit |
|---|---|---|
| V-1 controller -> real endpoint's pending receive | Per-host control + Kestrel ConnectionId + runner ID/epoch; memory only, no durable obligation. Failure cleanup cancels/releases and awaits this request. | Receive-boundary exception witness, endpoint/pipeline completion, final correlated logs and directory status. Proves exception classification, not native Linux choice of exception. |
| V-2 test CTS -> linked request token -> pending receive -> endpoint | Same connection identity; original/effective token observations retained only for this test. No persisted state or crash replay. | Actual receive cancellation witness, host still running, completed pipeline, absent keys and matching end/status. Proves the cancellation branch, not a native socket-abort timing distribution. |
| R-1 real client abort -> Kestrel receive/request cancellation -> endpoint | Same connection identity; no injected receive or token. Cleanup awaits observed pipeline completion. | Correlated end/status and final absence of middleware Error. Proves real peer abort is handled; either full valid code pair or no pair is acceptable. |

`PhoneHomeScriptedPeer`, in-memory directory and captured logger are substitutes for a
runner, persistent fleet state and production log storage. They cannot prove downstream
session execution, replay recovery, log shipping or user prompt receipt. None is asserted.
There are no safety-critical delivery/recovery guards to mutate in this scope.

### Proves it works now

- V-1: deterministic captured transport exception | real Kestrel/endpoint integration |
  `PhoneHomeConnectionTests.Accept_and_end_lines_carry_the_connection_id_and_transport_codes`
  with `[Arguments(false)]` and `[Arguments(true)]` (`withInnerSocket`) | construct
  `new WebSocketException(WebSocketError.ConnectionClosedPrematurely)` or
  `new WebSocketException(WebSocketError.ConnectionClosedPrematurely,
  new IOException("controlled receive", new SocketException((int)SocketError.ConnectionReset)))`.
  Await receive entry, inject, await completed pipeline and assert fault witnesses null.
  Assert the observed receive exception is the injected instance; host not stopping and no
  cancellation at injection. Final snapshot: exactly one accepted Information and one ended
  Warning in `PhoneHomeLiveConnection` category; both runner IDs and epochs match host/live;
  both ConnectionId locals are nonblank and equal; reason and directory DisconnectReason
  equal `transport_abort`; directory unavailable. `wsError` local is nonblank and exactly
  `ConnectionClosedPrematurely`; `socketError` local is nonblank and exactly `none` for false
  or `ConnectionReset` for true. No null-conditional assertion call. No middleware Error.
- V-2: request cancellation is classified without fabricating exception fields | same
  integration | `PhoneHomeConnectionTests.Request_cancellation_logs_transport_abort_without_transport_codes`
  | after receive entry cancel only the linked test input; verify actual receive cancellation,
  running host and live peer at injection. After completed pipeline require one accepted
  Information and one ended Warning, the same strict runner/epoch/nonblank ID correlation,
  `transport_abort` in ended log and directory, directory unavailable, null endpoint/pipeline
  fault witnesses and no middleware Error. Assert separately
  `ended.Properties.ContainsKey("WsError").ShouldBeFalse()` and
  `ended.Properties.ContainsKey("SocketError").ShouldBeFalse()`; present-null fails too.

For final snapshots select accepted/ended records by category and message kind, then assert
level/count/properties; do not filter away a wrong level, wrong runner, missing ID or missing
Reason before counting. Use snapshot locals for all property assertions, including both
ConnectionIds; expressions such as `entry["WsError"]?.ToString().ShouldBe(...)` are prohibited.
Record witness state before cleanup changes socket state or cancels lifetime tokens.

### Guards the regression

- R-1: retain real peer-abort coverage |
  `Peer_abort_is_a_warning_with_transport_abort_not_a_middleware_error` uses observation-only
  control and real `peer.Socket.Abort()`. After pipeline completion, assert one accepted
  Information/ended Warning, strict nonblank ID/runner/epoch correlation, non-null lifetime,
  `transport_abort` log and directory, unavailable directory and no middleware Error.
  `ContainsKey("WsError")` must equal `ContainsKey("SocketError")`. If present, both values
  must be nonblank, WsError a defined WebSocketError name, and SocketError either `none`
  or a defined SocketError name (reject numeric spellings). A partial pair always fails.
  The permissive pair alternative must never be copied into V-1 or V-2.
- R-2: optional helper changes do not break existing connections | every unchanged method
  in the roster below runs with default control. In particular host stop asserts close 1001
  `server_stopping` and directory `request_aborted`; overflow and receive-fault tests assert
  Closed before the handshake timeout; the two typed-disconnect methods distinguish
  `phone_home_connection_closed_in_flight` from `phone_home_connection_closed_before_send`.
  Keep their assertions, bounds and test inputs unchanged.
- R-3: class-level Linux scheduling sensitivity | CP-1 initial normal, CP-2 one further
  normal class run, CP-3 two serialized ordinals. Total four class executions / 116 results;
  **three repeat rounds after the initial run**, not ten per mode. Every execution must
  contain this exact roster, zero failed and zero skipped; no retry/relabeling of failures.
  CP-3 serializes within TUnit, not just between checkpoint rows. Record ambient load without
  manufacturing load. This bounded evidence plus deterministic branch witnesses qualifies
  this repair; it does not prove a statistical flake-rate bound or fresh-process endurance.

Frozen ordinary roster: namespace `Antiphon.Tests.Agents`, class `PhoneHomeConnectionTests`.
Numbers below are TUnit results per class execution, not assertions or durations.

| Method | Results | Coverage |
|---|---:|---|
| `Authentication_is_required_at_both_endpoints` | 1 | R-2 |
| `Tickets_are_bound_expiring_and_single_use` | 1 | R-2 |
| `Recovery_barrier_withholds_dispatch` | 1 | R-2 |
| `Disconnect_and_lease_expiry_refuse_new_work` | 1 | R-2 |
| `Live_boot_and_store_identity_cannot_be_replaced` | 1 | R-2 |
| `Old_epoch_reply_cannot_complete_current_request` | 1 | R-2 |
| `Unanswered_request_times_out_instead_of_waiting_forever` | 1 | R-2 |
| `Unanswered_mutation_is_not_replayed` | 1 | R-2 |
| `Register_connect_and_correlate_out_of_order_results` | 1 | R-2 |
| `Held_launch_does_not_block_heartbeat_or_reads` | 1 | R-2 |
| `Supported_operations_preserve_contracts` | 1 | R-2 |
| `Default_buffers_and_rules_fit_full_envelopes` | 1 | R-2 |
| `Fragmented_message_limit_is_enforced_on_both_peers` | 1 | R-2 |
| `Request_limit_refuses_the_thirty_third_request` | 1 | R-2 |
| `Overflow_disconnect_logs_reason_epoch_and_pending_counts` | 1 | R-2 |
| `Peer_abort_is_a_warning_with_transport_abort_not_a_middleware_error` | 1 | R-1 |
| `Pending_event_high_water_is_warned_before_overflow` | 1 | R-2 |
| `Disconnect_fails_in_flight_requests_with_a_typed_connection_closed_error` | 1 | R-2 |
| `Send_on_a_closed_connection_is_the_same_typed_error` | 1 | R-2 |
| `Host_stop_sends_a_going_away_close_before_the_socket_dies` | 1 | R-2 |
| `Accept_and_end_lines_carry_the_connection_id_and_transport_codes` | 2 (false, true) | V-1 |
| `Request_cancellation_logs_transport_abort_without_transport_codes` | 1 | V-2 |
| `Overflow_close_finishes_the_handshake` | 1 | R-2 |
| `Receive_fault_close_finishes_the_handshake` | 1 | R-2 |
| `Unknown_runner_status_is_404_and_carries_no_live_runner_identity` | 1 | R-2 |
| `Equal_secrets_do_not_let_a_server2_temp_ticket_connect_as_server2` | 1 | R-2 |
| `Draining_changes_neither_dispatch_eligibility_nor_capacity` | 1 | R-2 |
| `A_retired_runner_id_cannot_register_until_its_drain_is_cleared` | 1 | R-2 |

28 methods / 29 results: 25 unchanged methods, one strengthened real-abort method,
one parameterized method, one new cancellation method. Compare actual TRX TestDefinitions
and argument/native identities, not only display names or aggregate Min. The importer derives
an Expect token for the class; its prose Expect cell does **not** enforce this full roster.
Code/Review must report the explicit per-method multiplicities above from fresh receipts.

### Guard inventory

No production authorization, input-delivery or recovery guard changes. The in-scope
safety-critical evidence assertions are the six decision-bearing logging guards below:
operators must not be given false connection attribution, captured-exception evidence or
host-shutdown classification. The provisional five controls are retained in purpose; PC-5
is split into PC-5/PC-6 because the two absent-key assertions can be bypassed independently.
No unchanged ticket/dispatch/lease guard is claimed newly qualified by these PCs.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-3: an ended ConnectionId cannot be missing while accept/end correlation passes | PC-1 |
| G-2 | D-3: captured WebSocket exception cannot omit its WsError field | PC-2 |
| G-3 | D-3: captured WebSocket exception cannot omit its SocketError field, including the exact `none`/`ConnectionReset` cases | PC-3 |
| G-4 | D-1/D-3: non-stopping request cancellation must remain `transport_abort`, not host `request_aborted` | PC-4 |
| G-5 | D-1/D-3: cancellation with no captured exception must not supply a WsError key | PC-5 |
| G-6 | D-1/D-3: cancellation with no captured exception must not supply a SocketError key | PC-6 |

The receive-entry/token/exception witnesses, completed-pipeline barrier, log counts/levels,
runner/epoch equality and no-middleware-Error assertions are mandatory fixture validity and
regression checks in V-1/V-2/R-1; they are not new product safety/admission guards. Their
absence rejects Code at Review even if the six PCs are pending. Existing safety guards in
R-2 are unchanged collateral coverage, not a claim of a new full security mutation campaign.

### Positive controls

Only post-land SourceLanding Mutation runs these. Ordinary Code runs V/R; ordinary Review
judges fixture/assertion quality, this inventory and clean ordinary evidence before land.
All six defects are compiling, local edits to
`server/Api/Endpoints/SessionRunnerEndpoints.cs`; no helper/test assertion is weakened.
Apply each independently; they share a file and must not be batched.

- PC-1: break G-1 by deleting only `new("ConnectionId", connectionId),` from `LogEnded`'s
  structured state. Expect exact method
  `Accept_and_end_lines_carry_the_connection_id_and_transport_codes` red in both arguments
  at `endedConnectionId.ShouldNotBeNullOrWhiteSpace()`. Accepted ID remains present.
- PC-2: break G-2 by deleting only `state.Add(new("WsError", wsError));`. Expect exact method
  `Accept_and_end_lines_carry_the_connection_id_and_transport_codes` red in both arguments
  at `wsError.ShouldNotBeNullOrWhiteSpace()`. SocketError remains present. This is the old
  null-conditional blind spot's positive control.
- PC-3: break G-3 by deleting only `state.Add(new("SocketError", socketError));`. Expect exact
  method `Accept_and_end_lines_carry_the_connection_id_and_transport_codes` red in both
  arguments at `socketError.ShouldNotBeNullOrWhiteSpace()`. WsError remains present.
- PC-4: break G-4 by changing only `AbortReason`'s false arm from `"transport_abort"` to
  `"request_aborted"`. Expect exact method
  `Request_cancellation_logs_transport_abort_without_transport_codes` red at
  `ended["Reason"].ShouldBe("transport_abort")`, with receive-cancellation witness true
  and ApplicationStopping false. Assert the ended reason before directory reason.
- PC-5: break G-5 by appending `if (wsError is null) state.Add(new("WsError", "none"));`
  after the existing transport-state conditional and before OriginalFormat in `LogEnded`.
  Expect exact method `Request_cancellation_logs_transport_abort_without_transport_codes`
  red at `ended.Properties.ContainsKey("WsError").ShouldBeFalse()`; SocketError stays absent.
- PC-6: break G-6 by appending `if (wsError is null) state.Add(new("SocketError", "none"));`
  at that same location instead. Expect exact method
  `Request_cancellation_logs_transport_abort_without_transport_codes` red at
  `ended.Properties.ContainsKey("SocketError").ShouldBeFalse()`; WsError stays absent.

PC-1/2/3 use only
`/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/Accept_and_end_lines_carry_the_connection_id_and_transport_codes*`
with Min=2; the trailing wildcard expands this method's two arguments, not another method.
PC-4/5/6 use only
`/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/Request_cancellation_logs_transport_abort_without_transport_codes`
with Min=1. Repeat=1 for every PC phase. No class-filter mutation run.

Each PC has a method-scoped clean baseline, break/red, exact restore/green cycle: three
separate isolated builds/results directories, each build through the inherited slot gate.
Use the unchanged local `scripts/run-checkpoint.ps1` copied with its `lib/build-slot.ps1`
into the assigned external evidence root, following `docs/testing-and-build.md` SourceLanding
instructions. Red requires build exit 0 and test exit 1 at the stated assertion, with the
full intended argument roster. Zero executions, missing TRX, build failure, fixture timeout,
missing receive witness or setup failure is not red. Preserve all phase receipts and refresh
restored source timestamps before rebuilding. Do not commit/push snapshot mutations/evidence;
restore every byte and verify restoration at the landed SHA. Repairs return to Code.

### Out of scope

- Whole Unit/assembly, other PhoneHomeTestHost consumers, DB, client, provider, PTY and Windows
  lanes: no production behavior changes; the added helper is explicitly opt-in and this class
  already exercises default fixture behavior. If default middleware ordering/behavior changes
  beyond the conditional observer, re-scope before running, not by silently broadening Code.
- Real session queue/crash/recovery delivery: no changed path; substitutes and recipient limits
  are declared in Delivery inventory. Existing `Unanswered_mutation_is_not_replayed` is a
  transport replay assertion, never a UserPrompt-delivery receipt.
- Ten normal plus ten serialized executions, artificial host load, fresh-process endurance
  and statistical absence-of-flakes claims. Additional endurance, if commissioned after a
  new unexplained failure, belongs to the named follow-up slice **C996-ENDURANCE**; none is
  required for this deterministic test repair and no such run is preauthorized here.
- Native kernel-specific SocketError expectation for a real abort and all-aborts field
  fabrication: rejected by D-1/D-3. Static review of the ops-http clarification suffices;
  there is no new docs test or unrelated timeout/retry change.

Execution rules for the closed table: resolve eligible Linux placement at Code dispatch
using current runner defaults/session-runners (omit a named runner pin). Use the repository
API/build-slot resolution, no embedded host. Clear inherited `TUNIT_MAX_PARALLEL_TESTS`
before launch; CP-3 alone sets it to 1. `Serial=true` prevents other checkpoint activity in
this run and does not replace the TUnit setting. No `[Retry]`, `--known-flaky`, retry loop
or global `--repeat`. A red stays red. Reproduce only a failing existing method at the base
in a separate checkout before claiming inherited red; record that diagnostic and its cost.

Commit/push S1 and S2 before long runs, then freeze one exact source SHA for CP-1 through
CP-3. The sole separate bootstrap is the checkpoint tool (net9.0, inspected):

```powershell
$c996Plan = 'docs/superpowers/plans/2026-10-04-card-0996-phone-home-connection-end-flake-plan.md'
$c996Sha = git rev-parse HEAD
Remove-Item Env:TUNIT_MAX_PARALLEL_TESTS -ErrorAction SilentlyContinue
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c996-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c996-tool/ --nologo
if ($LASTEXITCODE -ne 0) { throw 'C996 checkpoint bootstrap failed' }
$c996Tool = 'tools/Antiphon.Checkpoints/bin-c996-tool/net9.0/Antiphon.Checkpoints.dll'
dotnet $c996Tool import --plan $c996Plan --out .antiphon/c996-checkpoints.yaml
if ($LASTEXITCODE -ne 0) { throw 'C996 checkpoint manifest refused' }
dotnet $c996Tool coverage --plan $c996Plan
# Inspect/adjudicate coverage findings before starting ordinary checkpoints.
dotnet $c996Tool run --plan $c996Plan --after S1-S2 --expected-source-sha $c996Sha --max-wait 50s
```

Import writes an ignored manifest; coverage is read-only. Neither launches a test/build
row after the single bootstrap. For coverage adjudicate historical/provisional prose references explicitly;
only this appended scope governs ordinary execution. Await each run and, on exit 75, use
`wait --run <returned-run-id> --max-wait 50s` until terminal; do not settle while it runs.
Every child driver owns its slot; do not wrap the tool run in a second slot. Slot timeout
exit 4 is not-run/blocked, never authority to bypass the gate. Linux rows keep the checkpoint
runner's `UseAppHost=false` default.

On the returned `report.json`, validate `--rows CP-1,CP-2 --expected-repeat 1` and separately
`--rows CP-3 --expected-repeat 2`, both with `--expected-source-sha $c996Sha`. Preserve unedited
CHECKPOINT lines, per-ordinal native case identities, actual roster/counts, load and source
provenance. CP-3 must contain two complete 29-result ordinals (0 and 1), not 58 copies of a
subset. CP-1/2 are distinct host invocations; CP-3's ordinals share one host and ordinary
fixture hooks. Keep generated evidence ignored. Clean only manifest-owned outputs with the
tool, and the inventoried `bin-c996-tool/` bootstrap outputs. Code/Review run
`scripts/check-evidence-diff.ps1` over the entire task range through the exact pushed SHA.

### Checkpoints

This is the sole active table. The class is the narrowest selection that reproduces the
reported failure context. Rows cover the entire ordinary V/R scope; no whole-Unit row.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment | Repeat |
|---|---|---|---|---|---|---|---:|---:|---|---|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c996-linux-once/` | linux-integration-once | `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/*` | V-1, V-2, R-1, R-2 | Exact 29-result roster above; 0 failed/skipped | 29 | 8 | true | n/a | 1 |
| CP-2 | S1-S2 | CP-1 | linux-integration-repeat-normal | `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/*` | V-1, V-2, R-1, R-2, R-3 | Exact same 29-result roster in a second normal host; 0 failed/skipped | 29 | 1 | true | n/a | 1 |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c996-linux-serial/` | linux-integration-repeat-serialized | `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/*` | V-1, V-2, R-1, R-2, R-3 | 2 ordinals x exact 29-result roster = 58; 0 failed/skipped each | 29 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` | 2 |

### Cost

All times are estimates, not measured outcomes; slot waits are additional and must be
reported. Keep the prior conservative eight-minute isolated test-build allowance; do not
infer wall-clock savings just from fewer test results.

- Ordinary V/R floor (Code): **17 minutes = 8 + 1 + 8**, including the two test-project
  builds. Each row uses the exact class filter above. Bootstrap/setup adds **3 minutes**;
  ordinary verification total **20 minutes**. With **35 minutes** for S1/S2 authoring and
  documentation, Code is **55 minutes** plus observed slot waits, within the 30-60 minute
  ordinary delivery budget. Separate ordinary Review rechecks the same closed scope;
  reserve another **20 verification minutes**, with no additional repeat battery.
- Mutation floor, named post-land follow-up slice **C996-PC**: PC-1/2/3 each select the exact
  transport-code method (two arguments); PC-4/5/6 each select the exact cancellation method
  (one result). Each PC costs **2 minutes baseline build/run + 2 red build/run + 2 restore
  build/run = 6 minutes**. Six controls cost **36 minutes**; allow **4 minutes** for custody,
  restoration and reporting = **40 minutes**. No whole-class run or PC repeats are included.
  This mandatory post-land companion is separate from Code's ordinary 55-minute slice;
  it must not be squeezed into Code or dropped to claim the whole lifecycle fits one slot.
- Combined requested execution floor: **17 ordinary + 36 Mutation = 53 minutes**. Including
  Code bootstrap/setup and Mutation overhead: **60 minutes**. Including implementation:
  **95 minutes** across Code and C996-PC. With separate Review's ordinary verification,
  reserve **115 minutes** across the lifecycle, plus Review analysis and observed waits.
- Savings versus the proposal: **609 -> 116 results**, 493 fewer (**80.95%**); **three -> two
  test-project builds**; ordinary floor **24 -> 17 minutes**, saving **7 minutes (29.17%)**.
  Explicitly splitting the absent-key control adds **6 Mutation minutes** (30 -> 36).
  Net ordinary-plus-PC floor **54 -> 53 minutes**, saving **1 minute** while independently
  covering both forbidden keys. Endurance beyond this budget is C996-ENDURANCE only after
  a separately commissioned need; it is not hidden ordinary verification.

Handoff audit: bodies read as listed; guards=6, mapped=6, missing=0, duplicate PC maps=0.
All six PCs name existing compiling mutation sites, exact detecting methods and decisive
assertions to implement; Code must retain those assertions. No PCs have been executed.
The test-only seam is verifiable, setup gaps are assigned to S1, numeric costs and the
closed Linux roster are frozen. Next: Code, then independent Review, land, C996-PC.

TestDesign checks passed: `git diff --check`; a Node source/table audit confirmed the fix
design prefix is unchanged, exactly one active checkpoint heading, schema/row widths,
reused build and repeat compatibility, all 28 roster names against the 27 existing method
bodies plus the planned cancellation method, 29/116 result arithmetic, 17-minute ordinary
floor and the six distinct guard/control mappings. This was a static audit against the
inspected importer rules, not execution of the .NET importer or a passing test receipt.
