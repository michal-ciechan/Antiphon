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

## Verification design

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

### Cost

Ordinary Code checkpoint floor: 24 minutes (8 + 8 + 8), plus up to 3 minutes for the tool
bootstrap, approximately 35 minutes authoring, and observed slot waits. Code dispatch
estimate: 62 minutes plus slot waits. Ordinary Review reruns the approved scope once;
no additional repetition battery after its prescribed rows are green.

Provisional Mutation floor: five controls x three method-scoped build/run phases x two
minutes = 30 minutes, plus discovery/reporting and slot waits. Combined execution floor:
54 minutes ordinary + Mutation, excluding the tool bootstrap and authoring. TestDesign
must retain both component floors and revise estimates if its frozen design changes them.

### Checkpoints

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
