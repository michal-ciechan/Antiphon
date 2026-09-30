# CARD-0833: restore checkpoint executor build-slot leases

Plan task `45703b71-bd8a-4236-a145-0dcdc81b965b`, 2026-09-30. Inspected source:
`7f28d2bb247ba1c0f785336f5decd4a16b5577c1`. Linux is the acceptance platform.
This commit is documentation only. No build, test, lease acquisition, deployment,
task-token change, or card write was performed for this Plan.

## Outcome and verified cause

The executor creates a holder process for each driver but sends a null
`processStartUtc`. The shared Linux broker cannot inspect processes in the
runner's PID namespace, so it rejects that request. The tool treats this
definitive rejection as temporary unavailability, waits 60 seconds, then starts
the driver without a lease. Repair the holder identity and reject answered
protocol failures before launching any build or test.

Read CARD-0833 in full with `pwsh -NoProfile -File scripts/card.ps1 get CARD-0833`
(revision 2). The card records review `fa627fa3`'s HTTP 400 finding, 36 host-wide
unleased report lines, and successful leases from both PowerShell wrappers in
the same sessions. Those counts are reported historical evidence, not a census
repeated by this Plan.

| Boundary | Independently verified source evidence | Implication |
|---|---|---|
| Executor composition | `CheckpointApp.cs:103-105` constructs `BuildSlotClient` with `log: Note` and `new ProcessLeaseHolderSource()`. `Program.cs:267-275` uses the same holder source for `row`. | Fix the real composition, including direct rows. Supplying a fake `IBuildSlotClient` in a test would bypass the defect. |
| Holder identity | `Slots/LeaseHolder.cs:30` returns `ProcessStartUtc => null`. `BuildSlotClient.AcquireAsync` selects `holder?.ProcessStartUtc ?? _processStartUtc`; neither production caller supplies the fallback. | The POST includes `processStartUtc: null`, not the real child start time. |
| Broker validation | `src/Antiphon.SessionRunner/BuildSlotBroker.cs:48-72` validates positive PID/nonblank label, then resolves `request.ProcessStartUtc?.ToUniversalTime() ?? _liveness.TryGetStartTimeUtc(request.Pid)`. Null yields `BuildSlotOutcome.Invalid`. `BuildSlotRoutes.cs:49` maps it to HTTP 400 Problem Details. | Start time is conditionally required: a broker-visible PID can supply it indirectly; a foreign-container PID cannot. |
| Linux topology | `docker-compose.server2-runner.yml:71,145-160` points clients at `http://build-slots:8080/build-slots`; this separate broker uses `HolderLiveness=renew`, grace 90 seconds, renewal every 20 seconds. | PID lookup in the broker container is not a dependable lookup of a holder in the runner container. An accidental numeric PID collision can also supply an unrelated start time; send the actual identity explicitly. |
| Probe versus acquisition | `ProbeAsync` GET accepts HTTP 200 immediately. The acquire loop silently retries an unrecognized POST response every five seconds until its 60-second grace expires. | The card's suspected GET probe is not where an initial GET 200 loses the lease. It is the subsequent POST 400 path. Both paths need diagnostics. |
| Why scripts work | `scripts/lib/build-slot.ps1:76-83` reads the selected `-HolderPid` process's UTC start time and includes it in the POST. Lines 108-120 start renewal only when the grant supplies a positive interval. | There is no alternative request flag for renew mode. Both modes use the same request. Copy the wire contract, not the script's broad unexpected-response fallback. |
| Renewal contract | `BuildSlotBroker.GrantOf` supplies `renewEverySeconds` only in renew mode. `BuildSlotClient.RenewUntilReleasedAsync` already posts `/build-slots/{leaseId}/renew` and disposal stops renewal before DELETE. | Preserve renewal and per-driver holders. Do not substitute the executor's single PID for every driver. |
| Launch refusal propagation | The scheduler's build and row paths, and `Program.Row`, currently check only `ExitCodes.SlotTimeout`. | Returning another nonzero lease result alone is insufficient: all three launch boundaries must honor it. |

The canonical 400 body reconstructed from `BuildSlotBroker` and
`BuildSlotRoutes.Problem` is below; `<pid>` denotes the submitted integer. This
is a source-derived body, not a newly captured production POST response:

```json
{"type":"build_slot_invalid","title":"build_slot_invalid","status":400,"detail":"holder pid <pid> is not running and no processStartUtc was given"}
```

A read-only GET through this session's configured build-slot endpoint returned
HTTP 200, `enabled=true`, `budget=4`, `maxCpuCount=6`, `occupied=0`, zero leases
and zero waiters. No production POST was needed to establish the validation
failure. The effective endpoint comes from `ANTIPHON_BUILD_SLOTS_URL`; preserve
that precedence over the Windows `:17204` and Linux loopback `:8080` defaults.

Owners: [testing and build operations](../../testing-and-build.md), especially
Build slots and Checkpoint runner tool; [CARD-0589 D-3/D-6](2026-09-25-card-0589-build-fanout-cap-plan.md).
The 0589 plan predates shared-container renew mode; current broker code and
contracts establish that extension. No broker or PowerShell change is needed.

## Decisions

### D-1. Capture the holder's actual UTC start time once

`ProcessLeaseHolder.Start` must capture the process it just started:
`process.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)`.
Store that string on the holder and return it on every acquisition retry. The
PID and time must describe the same child, including a replacement holder after
a duplicate grant. If identity capture fails, dispose that newly created child
and return a recorded nonzero admission failure; never invent a timestamp or
silently send null. Preserve existing cancellation/disposal custody.

Do not reuse CARD-0804/0805's `ProcessIdentity.StartUtcTicks` as a wire timestamp:
its Linux implementation stores `/proc/<pid>/stat` clock ticks since boot, despite
the member's name. Those are an opaque process-generation token, not UTC ticks.
Do not change that lifecycle contract in this card.

Keep one holder per in-flight driver. The broker's idempotence key is
`(pid, processStartUtc)`. Do not switch to a shared parent identity or disable
renewal to make acquisition appear successful. Keep null/missing
`renewEverySeconds` as no renewal; positive numeric values start renewal.

### D-2. Separate refusal from unavailability

Use existing exit **2** (`ExitCodes.Invalid`) for broker/protocol admission
failure, with `slot=refused` and a stable reason. Keep exit **4** for a busy or
memory-floor wait timeout. A refused lease has `MaxCpuCount=0`; callers must
check its nonzero exit before applying their fallback CPU count.

| Observation | Decision |
|---|---|
| GET 200 with valid enabled listing | Acquire a separate lease per driver. Parse JSON structurally. |
| GET 200 with `enabled=false`, or POST 200 with `unlimited=true` | Explicit unlimited mode with the supplied positive CPU limit. No renew/release without a lease. |
| POST 200 with a valid grant | Admit only after registering a distinct held lease. Apply its CPU limit. |
| GET 400/401/403 or another definitive unexpected 4xx; POST 400 including `build_slot_invalid`, or unexpected 4xx | Immediate refusal, exit 2. One attempt, no grace delay, no driver. GET 404 has the compatibility exception below; POST 404 after a successful probe is a refusal. |
| GET/POST 200 malformed JSON, invalid shape/CPU limit, or POST missing lease without unlimited | Immediate protocol refusal, exit 2. A 200 status alone is not a grant. |
| Duplicate lease still unresolved after the existing bounded holder replacements | Refuse the second driver; retain the first driver's valid lease. Never release someone else's held grant or run the second driver unleased. |
| Recognized 409 `build_slot_busy` / `build_slot_memory_floor` | Preserve visible polling and retry hints; expire at the slot-wait deadline with exit 4 and no driver. |
| HTTP 5xx | Retry within the existing 60-second transient grace, recording the last answer; if unresolved, exit 2 with the answered status/body. Never turn an answered error into a successful unleased lease. |
| GET transport failure/timeout throughout grace | Preserve CARD-0589's unleased fallback at CPU 4, carrying `runner_unreachable` and the last exception. |
| POST transport failure after an enabled GET | Preserve bounded transport retry and the same explicit fallback, carrying the POST exception and last answered observation. A remembered definitive refusal must never be erased by a later transport error. |
| Initial GET 404 | Preserve the tool's existing `unavailable` compatibility mode with CPU 4 and reason `broker_not_found`; no per-driver 60-second wait. |
| Caller cancellation | Propagate cancellation and dispose owned holders/leases; no fallback. |

Unleased execution remains an explicit compatibility outcome, not proof of a
budgeted run. CARD-0833's ordinary acceptance against the reachable enabled
Linux broker requires **every selected build and row to say `slot=granted`**.
An unlimited, unavailable, unleased, or refused result does not qualify that
acceptance, even if tests otherwise pass.

### D-3. Preserve the last observation in durable evidence

Add a small slot diagnostic value with operation (`probe`, `acquire`, `renew`,
`release`), status when answered, stable reason, bounded response-body excerpt,
exception type/message when unanswered, and elapsed seconds. Keep these values
local to each acquisition; concurrent rows must not overwrite a shared
`LastError`. A probe diagnostic can be copied onto its immutable session result.

Use the existing `Note` callback into `executor.log`. Log the initial probe
decision, changed failure observations, and the final refusal/fallback. An
acquisition line includes its build/row label. For the observed defect the log
must contain `operation=acquire status=400 reason=build_slot_invalid` and the
broker's detail. Renewal/release failures should also retain their answered
status/body. Preserve existing renewal-loss behavior; changing active-driver
policy after renewal loss is outside this repair.

Bound each body excerpt to 2,048 characters, mark truncation, and escape control
characters into one log line. Exclude headers and request bodies; redact the
task token if reflected in text, and omit URL userinfo/query credentials. Do not
log the owner token or persist it in reports. Logging must use the existing
executor writer and disposal contract, not a new competing file writer.

Carry stable `SlotReason` through session/lease, row request/result, progress,
report JSON, and merged reports. Append `slot-reason=<code>` to checkpoint lines
only when there is a reason; keep existing successful line shape. Retain the
full bounded diagnostic in `executor.log`, not as an unescaped body in the
receipt. A build refused before execution must produce dependent-row receipts
with the same refusal reason and wait, zero executed tests, and exit 2; it must
not vanish into a generic `build-failed` line. A refused direct command row must
likewise report no execution. Preserve source identity fields added by CARD-0835
if sequencing changes and they are present at implementation time.

### D-4. The restart-time missing token is a separate investigation

Recommend a separate launch-binding/restart card; do not file it in this task.
CARD-0833's evidence attributes missing task token and absent branch/merge-target
binding to review `6fea402b`, dispatched around 12:57Z during AppHost restart.
Its current task status can be read, but does not establish the historical child
environment or prove restart caused the omission.

The tool's behavior is independently explained by `TaskOwnerGuard`: a bound task
with missing API/token/valid ID immediately becomes `owner-unverified` before
slot probing (`CheckpointApp.ExecuteCoreAsync`). With no expected or environment
task ID, `Bound` is false and the owner read is skipped. Thus unsetting
`ANTIPHON_TASK_ID` can bypass a fresh run's guard; it neither repairs credentials
nor validates ownership. Do not prescribe or perform that workaround, weaken
the guard, or move token transport into the slot request. A saved request with
`OwnerTaskId` remains bound even if its executor environment omits the ID.

The separate investigation should correlate dispatch/restart timing, task and
session binding, launch environment **presence booleans only**, and branch
provenance. It must distinguish an omitted token from a present but invalid
token or an unavailable owner API. No token value needs to be collected.

## Sequencing and overlap

These are observations at planning time, not landing receipts. Recheck the named
owners before Code; a board column or a pushed branch alone is not publication.

| Adjacent card | Evidence and exact overlapping area | Required order |
|---|---|---|
| CARD-0804/0805, owner `3895bec1` | Both cards are Review. Owner report is Succeeded at `1dcc03c0a596dbd1fa857ee93293c6dfbf05828a`, publication unconfirmed, Windows CP-7/11/12 pending in that report. Its branch changes `CheckpointApp.cs`, `Program.cs`, `Slots/BuildSlotClient.cs`, `Report/ReportWriter.cs`; shared checkpoint fixtures, `BuildSlotClientTests`, app/row/scheduler/report-adjacent tests; `ProcessSpawnLimitTests.cs`; and `docs/testing-and-build.md`. It introduces owned temp scopes and lifecycle cleanup. | **Must land before CARD-0833 Code.** Consume its completed lifecycle work and test fixture API; do not recreate or cherry-pick a partial lifecycle implementation. |
| CARD-0835 | The landed plan/TestDesign artifact is present at this baseline. Card currently says Review; the brief says Code is held behind 0804/0805. Its S2 edits `CheckpointApp.cs`, `Program.cs`, `Execution/RowRunner.cs`, `Execution/RunScheduler.cs`, `State/RunState.cs`, `Report/CheckpointLine.cs`, `ReportModel.cs`, `ReportWriter.cs`, `ReportMerger.cs`, and related fixtures/docs. | **Recommend 0804/0805 -> 0833 -> 0835.** 0835 need not land first. Hold its Code while 0833 owns these files. If its Code has already started, serialize through the caller and consume its landed receipt changes before starting 0833; do not run both in parallel. |
| CARD-0850 | Backlog. Edits `CheckpointApp.cs`, `Report/ReportWriter.cs`, report tests and likely output/report model fields for `--keep-outputs` and selected build counts. | Not a prerequisite. Run after 0833, preferably after 0835, with an explicit handoff. Preserve its ownership of output cleanup truth; do not fold that feature into this card. |
| CARD-0823 | Backlog text describes null **response** `renewEverySeconds` causing `TryGetInt32` to throw after grant. `9c019896816392a29114d865c7e6df9d5dfc65de`, already in this baseline, adds the Number-kind check and `pid_liveness_grant_with_null_renewal_is_granted_without_renewing`. Overlap: `Slots/BuildSlotClient.cs`, its tests, and possibly `CheckpointApp.cs` crash diagnostics. | The missing **request** `processStartUtc` is a different mismatch. The null-reader fix is already present, so no additional 0823 landing is required for Linux Code. Preserve it when consuming 0804/0805; serialize any remaining 0823 lease-exception/inner-error work. Do not claim the entire card resolved from this inspection. |

Cut the eventual Code worktree from the confirmed prerequisite result. This Plan
branch stays fast-forward-only from its assigned base; no rebase/reset of a
pushed task branch is part of this plan.

## Slices and exact footprint

S1-S3 are one ordinary verification group (`all`). Commit and push each slice,
then run the single closed checkpoint manifest after all three commits exist.
Tests are authored with the slice they guard; positive-control mutations execute
in the post-land Mutation stage, not against this active Plan branch.

| Slice | Work | Decisive result |
|---|---|---|
| S1: valid holder identity | Capture/cache the real child start time; clean up failed identity capture. Keep distinct holders and renewal/null compatibility. Add the executor HTTP seam and strict broker fixture. | The real executor holder passes the foreign-PID broker validation and each running driver owns a distinct grant. |
| S2: refusal and evidence | Implement D-2/D-3; honor any nonzero slot exit at build, scheduled row, and direct row boundaries; preserve diagnostics through reports. | A 400 causes one POST, no grace sleep, no driver, exit 2, and a durable reason/body. Transport fallback remains explicit. |
| S3: regressions and owner documentation | Complete the named tests, process-spawn registration, and update the two build-slot/checkpoint-tool sections of the owner doc. | Closed manifest passes on Linux; receipts prove grants; Windows scope is reported separately. |

Allowed production files, all relative to `tools/Antiphon.Checkpoints/`:

- `Slots/LeaseHolder.cs`, `Slots/BuildSlotClient.cs`, new `Slots/SlotDiagnostic.cs`.
- `CheckpointApp.cs`, `Program.cs`.
- `Execution/RunScheduler.cs`, `Execution/RowRunner.cs`.
- `State/RunState.cs`.
- `Report/CheckpointLine.cs`, `Report/ReportModel.cs`, `Report/ReportMerger.cs`,
  `Report/ReportWriter.cs` (slot evidence rendering only).

Allowed test files:

- New `tests/Antiphon.Tests/Checkpoints/CheckpointSlotContractTests.cs`.
- New `tests/Antiphon.Tests/Checkpoints/CheckpointSlotExecutorTests.cs`.
- New `tests/Antiphon.Tests/Checkpoints/BuildSlotBrokerFixture.cs`.
- Existing `tests/Antiphon.Tests/Checkpoints/BuildSlotClientTests.cs` (change the
  shared-lease expectation to refusal, retain existing 11-case roster, add limiter).
- Existing `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs` (register spawning classes).

Allowed docs: this plan and `docs/testing-and-build.md`. No project/package change
is expected: `Antiphon.Tests.csproj` already references both the checkpoint tool
and SessionRunner. Use the prerequisite's temp-scope helpers without editing
their cleanup implementation. Existing regression tests may be read, not silently
expanded or rewritten to accept a regression; any additional file/test need must
be recorded in a committed footprint/manifest amendment before execution.

Out of scope: `scripts/`, `server/`, `src/`, broker settings/contracts, deployment,
ownership guard semantics, secret custody, temp reclamation, source certification,
and CARD-0850 output-accounting behavior. Do not edit generated `docs/cards/`.

## Verification design

### Production-path fixture and failure detection

Use a test-owned in-process `HttpMessageHandler` adapter over the **real**
`BuildSlotBroker` with fake liveness/memory/time dependencies. Deserialize the
actual JSON request as `BuildSlotRequest`, call `TryAcquire`, and return the
same status/Problem Details/grant shape as `BuildSlotRoutes`. Its liveness probe
returns null for every requested PID to model the separate Linux broker
container; renew-mode liveness is governed by renew calls. Configure budget 2,
CPU 6, no memory floor, and short renewal interval for the renewal test. Test
setup must first prove that a null-start request returns the exact 400 and that
the same valid PID/label plus UTC start is admitted, then reset fixture state.
This qualification is an internal assertion, not an extra TUnit execution.

Add optional `SlotHandler`, `SlotClock`, and `SlotDelay` runtime seams and route
them into the normal client construction. Leave `Runtime.Slots` unset in executor
acceptance tests. Keep the production `ProcessLeaseHolderSource`, log callback,
and acquisition/renew/release code. Reuse that construction for `Program.Row` and
forward the existing runtime driver seam there so its refusal can be observed
without starting a real build. Do not inject a pre-granted slot or a holder with
a prefilled timestamp into the decisive executor test.

Run `CheckpointApp.ExecuteAsync` over minimal request/resolved-manifest files in
the prerequisite's owned temp scope. Use a fake driver that writes tiny valid
TRX files and records the broker's live grant at each build/test start. Gate two
rows to overlap; assert each has a different holder/grant and release of one
does not release its sibling. Assert the reported PID belongs to the actual
holder and its parsed UTC time matches that process's start (allow only the
small platform conversion tolerance), then assert holders are gone after owned
disposal. Never boot real server Program or contact production services.

Use fake acquisition time so restoring the old 60-second loop fails an outcome
assertion quickly, not a wall-clock timeout. Renewal uses a bounded event gate
on its first observed POST; do not give its background loop an instantly
completed fake delay. Every gate is released/canceled and awaited in `finally`.
New spawning tests are Integration and carry the assembly-local
`ParallelLimiter<ProcessSpawnLimit>`; nonspawning contract tests are Unit.

### New ordinary roster

All names below are single, non-parameterized `[Test]` methods. Internal variants
are not extra executions. TestDesign must retain these totals or amend the table
explicitly. Assertions are against production outcomes, not constants returned
by a fixture pretending to be the client.

| ID | Class and method | Assertions that must detect a defect |
|---|---|---|
| V-1 | `CheckpointSlotContractTests.holder_identity_is_preserved_in_acquire_and_retry` | Valid PID/start pair is identical on busy retry; a replacement carries its own pair; strict broker accepts it. |
| V-2 | `CheckpointSlotContractTests.definitive_acquire_400_refuses_without_delay` | Exit 2/refused, exactly one POST, zero delays, exact invalid detail preserved; holder disposed. |
| V-3 | `CheckpointSlotContractTests.probe_400_refuses_without_delay` | Rejected session, no acquire POST, zero delays, GET status/body retained. |
| V-4 | `CheckpointSlotContractTests.malformed_probe_success_is_refused` | Malformed and shape-invalid GET 200 cannot become enabled/unleased. |
| V-5 | `CheckpointSlotContractTests.malformed_grant_is_refused` | Missing lease, malformed JSON and invalid CPU grant cannot admit work; release a newly identified grant if later validation fails, without releasing another driver's grant. |
| V-6 | `CheckpointSlotContractTests.transport_fallback_carries_last_failure_after_bounded_grace` | GET and POST transport-only variants use bounded virtual grace, CPU 4, explicit fallback reason and last observation; cancellation does not become fallback. |
| V-7 | `CheckpointSlotContractTests.answered_server_error_never_becomes_successful_unleased` | GET and POST 503 variants exhaust bounded retry then exit 2 with status/body. |
| V-8 | `CheckpointSlotContractTests.renew_and_release_diagnostics_keep_status_and_body` | Renewal and release failures have operation/status/detail; renewal cancellation drains before DELETE. |
| V-9 | `CheckpointSlotContractTests.diagnostics_are_bounded_and_escape_body_controls` | Overlong/multiline response yields a bounded single-line excerpt and truncation marker; synthetic reflected token is redacted. |
| V-10 | `CheckpointSlotContractTests.compatible_modes_preserve_limits_and_reasons` | Initial 404 reason/no POST; disabled listing and explicit unlimited grant honor CPU limit without a held lease. |
| V-11 | `CheckpointSlotExecutorTests.executor_reachable_broker_grants_build_and_rows` | Normal composition and real holders: build and two rows granted; CPU 6 applied to build; distinct simultaneous leases; fresh report lines/JSON agree; zero held leases/holders on completion. Also compare held start value across reads/retries. |
| V-12 | `CheckpointSlotExecutorTests.executor_renews_until_driver_finishes_then_releases` | Keep a fake driver running through a real renewal tick; observe matching renew before release and no renewal after disposal; holder exits. |
| V-13 | `CheckpointSlotExecutorTests.executor_rejection_launches_no_build_or_rows_and_reports_reason` | Reachable broker rejects acquisition: no driver calls, exit 2, dependent rows preserve reason/wait, executor.log contains body/status; reason survives report JSON and ReportMerger. |
| V-14 | `CheckpointSlotExecutorTests.executor_row_rejection_does_not_run_command` | Command-only manifest, acquisition 400: zero commands, exit 2 and reason in receipt. |
| V-15 | `CheckpointSlotExecutorTests.executor_cancellation_releases_lease_and_holder` | Cancellation while a fake driver owns a grant drains it and the holder; no leaked active lease. |
| V-16 | `CheckpointSlotExecutorTests.executor_missing_owner_token_stops_before_slot_probe` | Synthetic bound owner without token yields exit 7/owner-unverified, zero slot requests and zero drivers. Ambient credentials are not used or unset. |
| V-17 | `CheckpointSlotExecutorTests.row_entrypoint_rejection_invokes_no_driver` | `Program.RunAsync` row path uses real slot client/holder; 400 returns exit 2, no build/test call, receipt includes reason. |
| V-18 | `CheckpointSlotExecutorTests.failed_start_time_capture_disposes_new_holder` | A narrow injected start-time reader throws after the real holder starts; capture its identity in the fixture, assert it exits before the failed creation returns, and assert no broker request. Fixture finally retains cleanup of that exact owned child. |

R-1: `BuildSlotClientTests` **11**, `RunSchedulerTests` **14**,
`RowRunnerTests` **13**, `ExitCodeTests` **7 expanded Arguments cases** = **45**.
R-2: `CheckpointAppTests` **3**, `CheckpointLineTests` **3**, `ReportWriterTests`
**6**, `ReportMergerTests` **1** = **13**. R-3: `ProcessSpawnLimitTests` **3**.
These are source-derived baseline counts, not executed evidence. R-4 is the
required Unit lane with a conservative floor of 3,500 executions; the 0804/0805
owner reported 3,567 executed and 33 declared skips on its branch. Record the
actual fresh TRX total and every skip instead of treating that historic total as
an exact census of a later merged tree.

Do not sweep the entire Checkpoints namespace: it includes unrelated known
CARD-0818/0828 concurrency/owner-watch hazards. The named executor tests cover
ownership composition with bounded gates; TaskOwnerGuard itself is unchanged.
The Unit row remains required by the owner recipe. A known unrelated failure is
reported and triaged, not silently filtered out. Reconcile roster drift from
prerequisite landings before the first run, preserving a closed manifest.

### Positive controls, pending post-land Mutation

Each control changes a production decision and must compile, execute the named
method with a nonzero count, fail its intended assertion, then pass after fresh
restoration/build. Timeouts, fixture failures and zero-test runs are not red
proof. Ordinary Code/Review precedes land; controls belong in the original
card's post-land verification companion and immutable SourceLanding snapshot.

| PC | Compiling defect variant | Exact detecting method(s) |
|---|---|---|
| PC-1 | Restore `ProcessStartUtc => null`. | V-11: strict broker returns 400 and grant/driver assertions fail. |
| PC-2 | Serialize no start time, or reuse the old holder pair after replacement (two independent variants). | V-1. |
| PC-3 | Turn POST 400 into successful unleased fallback; separately retain the 60-second retry before refusal (two variants). | V-2 (exit or delay count), V-13 (no driver). |
| PC-4 | Treat GET 400 as unavailable/unleased. | V-3. |
| PC-5 | Accept invalid GET 200; separately accept grant without lease (two variants). | V-4; V-5. |
| PC-6 | Treat exhausted answered 503 as successful unleased. | V-7. |
| PC-7 | Drop fallback reason; separately skip transport grace (two variants). | V-6. |
| PC-8 | Disable renew for a positive interval. | V-12. |
| PC-9 | Omit DELETE, or omit owned-holder disposal (two independent variants). | V-15, with fixture finally cleanup retained. |
| PC-10 | Restore launch guards that check only slot timeout (build, scheduled row, direct row as three variants). | V-13; V-14; V-17 respectively. |
| PC-11 | Discard broker body in log; separately drop slot reason when mapping/merging reports (two variants). | V-13, V-8. |
| PC-12 | Remove diagnostic bounding/escaping; separately remove reflected-token redaction (two variants). | V-9. |
| PC-13 | Ignore the owner admission result at executor entry. | V-16. |
| PC-14 | Restore `TryGetInt32` on JSON null. | Existing `BuildSlotClientTests.pid_liveness_grant_with_null_renewal_is_granted_without_renewing`. |
| PC-15 | Return successful unleased on exhausted duplicate grant. | Updated existing `BuildSlotClientTests.a_shared_pid_lease_is_not_claimed_by_the_second_driver`. |
| PC-16 | Treat explicit disabled/unlimited mode as enabled without a grant. | V-10. |
| PC-17 | Omit child disposal when start-time capture throws. | V-18; the fixture's independent finally cleanup still runs. |

There are **17 controls / 26 defect variants**. V-18's reader seam belongs inside
`Slots/LeaseHolder.cs`; its production default reads the actual process start
time. No controls have been executed in this Plan.

### Execution procedure and cost

The tool under repair must be freshly built before using it to verify itself.
Declare one supporting bootstrap build outside the checkpoint table, under the
working PowerShell slot wrapper, after S1-S3 are committed/pushed:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-0833-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c833-tool/ --property:UseAppHost=false --nologo
dotnet tools/Antiphon.Checkpoints/bin-c833-tool/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-09-30-card-0833-checkpoint-tool-build-slot-lease-plan.md --after all
dotnet tools/Antiphon.Checkpoints/bin-c833-tool/net9.0/Antiphon.Checkpoints.dll wait --run <run-id> --max-wait 570s
```

Confirm the project's resolved target/output layout before launch; the inspected
project targets net9.0. This is the explicit-build equivalent of the prescribed
`dotnet run --project tools/Antiphon.Checkpoints -- run --plan ...`; it prevents
an implicit unleased launcher build. **Do not wrap the self-leasing checkpoint
run in another build-slot wrapper.** Report bootstrap lease/exit separately.
Do not select `--slots off`, unset task identity, or accept the old unleased
binary to get through its own gate. If bootstrap cannot obtain a grant, report
the limitation rather than bypassing the slot budget.

Use one run for the committed `all` group; keep calling `wait` after exit 75
until terminal. CP-2 onward reuse CP-1's single isolated test-project output;
that project already builds the tool as a reference. No `dotnet test`, full
assembly run, additional discovery build, or ad hoc broad rerun is planned.
Fixes use a new commit and rerun the affected group with fresh results; enumerate
all reruns and reasons. Report unedited CP lines, expected versus actual counts,
failures/skips, build and row slot state, log/report paths and the tested SHA.

Linux ordinary floor: **34 minutes**, plus **2 minutes** leased tool bootstrap,
authoring estimate **180 minutes**, and observed slot waits: Code estimate
**216 minutes** before queueing. Windows estimates total **46 minutes** plus
bootstrap if separately commissioned. Linux is required; Windows smoke/regression
qualification is a separate reported result and is not claimed from Linux or
from the existing null-response unit test alone. The new tests should be portable.

Mutation estimate: 26 variants at 6 minutes per method-scoped restored cycle =
156 minutes, plus 20 minutes discovery/report/restoration = **176 minutes**.
Combined Linux authoring + bootstrap + ordinary + Mutation estimate is **392
minutes**. These are planning allowances, not measured durations.

### Checkpoints

One isolated build plus one exact filter per row; `CP-1` reuse means the same
build at the same committed `all` source, without another build. Filters use
trailing `*` on class operands; table `\|` becomes literal `|` at the CLI.
There are **79 named executions**, plus the Unit lane (which overlaps Unit
members of the named roster). Named rows require zero failures and zero skips.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c833/` | slot-contract | `/*/*/CheckpointSlotContractTests*/*` | V-1-V-10 | exactly 10 executed/passed, 0 failed/skipped | 10 | 12 | 18 | true |
| CP-2 | all | CP-1 | slot-executor | `/*/*/CheckpointSlotExecutorTests*/*` | V-11-V-18 | exactly 8 executed/passed, 0 failed/skipped | 8 | 4 | 5 | true |
| CP-3 | all | CP-1 | slot-regression | `/*/*/(BuildSlotClientTests*)\|(RunSchedulerTests*)\|(RowRunnerTests*)\|(ExitCodeTests*)/*` | R-1 | exactly 45 executed/passed, 0 failed/skipped | 45 | 3 | 4 | true |
| CP-4 | all | CP-1 | receipt-regression | `/*/*/(CheckpointAppTests*)\|(CheckpointLineTests*)\|(ReportWriterTests*)\|(ReportMergerTests*)/*` | R-2 | exactly 13 executed/passed, 0 failed/skipped | 13 | 2 | 3 | true |
| CP-5 | all | CP-1 | process-limit | `/*/*/ProcessSpawnLimitTests*/*` | R-3 | exactly 3 executed/passed, 0 failed/skipped | 3 | 1 | 1 | true |
| CP-6 | all | CP-1 | unit-lane | `/*/*/*/*[Category=Unit]` | R-4 | all selected Unit cases; >= 3500 executed, 0 failed; enumerate every skip and actual total | 3500 | 12 | 15 | true |

## Handoff and completion

Next is TestDesign: audit the refusal propagation and fixture cuts, reconcile
the 18 new methods/26 PC variants and prerequisite roster drift, then hand off
Code with the closed manifest. Code waits for confirmed CARD-0804/0805 land and
the caller's checkpoint-tool edit lane. The missing-token issue is a separate
recommended investigation, not a dependency for producing this plan.

Code completion requires Linux ordinary results at a pushed SHA, granted build
and row leases against the enabled host broker, no leaked fixture leases/holders,
durable rejection/fallback evidence, and the owner-doc update. Separate Review
then assesses the committed implementation; post-land Mutation retains the listed
controls. No runner redeploy or AppHost restart is required to activate a rebuilt
checkout-local tool. Existing detached shadow copies continue their old binary;
never claim the fix applied to an already running executor.
