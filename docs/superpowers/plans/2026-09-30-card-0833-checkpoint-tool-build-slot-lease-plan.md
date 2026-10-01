# CARD-0833: restore checkpoint executor build-slot leases

Plan task `45703b71-bd8a-4236-a145-0dcdc81b965b`, 2026-09-30. Inspected source:
`7f28d2bb247ba1c0f785336f5decd4a16b5577c1`. Linux is the acceptance platform.
TestDesign task `da7e1d0b-f9f0-40b1-9715-6cc6e5ef38b8`, 2026-09-30, audited
`5cd8d0b9da70a011d55a50ae8c0e7e0ebf8db190` and prerequisite source
`1dcc03c0a596dbd1fa857ee93293c6dfbf05828a`. This document is the only edited file.
No build, test execution, lease acquisition, deployment, task-token change, or
card write was performed. Counts below are static source census, not run evidence.

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
| Launch refusal propagation | The scheduler's build and row paths, `Program.Row`, and `BaselineComparer.RunLeased` currently check only `ExitCodes.SlotTimeout`. | Returning another nonzero lease result alone is insufficient: all four launch boundaries must honor it. The baseline path was missing from the original Plan. |

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

**Dispatch hold:** do not launch CARD-0833 Code until the caller has a confirmed
publication outcome for the complete CARD-0804/0805 owner `3895bec1`, including
its required qualification, and has reserved the checkpoint-tool edit lane.
`git ls-remote` during TestDesign still showed master at `5cd8d0b9` and that
owner branch at `1dcc03c0`; this is source evidence, not a landing receipt.
Then serialize **0804/0805 land -> 0833 Code/Review/land -> 0835 Code/Review/land
-> 0850**. No concurrent 0833/0835 checkpoint-tool edits. If order changes,
consume the earlier confirmed landing and commit a fresh census/footprint
amendment before the next Code run. Do not dispatch either overlapping writer
on the assumption that the other is merely pushed. CARD-0853 owns the separate
restart-time missing-token investigation; it is not a prerequisite here.

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
| S2: refusal and evidence | Implement D-2/D-3; honor any nonzero slot exit at build, scheduled row, direct row, and baseline driver boundaries; preserve diagnostics through reports. | A 400 causes one POST, no grace sleep, no driver, exit 2, and a durable reason/body. Transport fallback remains explicit. |
| S3: regressions and owner documentation | Complete the named tests, process-spawn registration, and update the two build-slot/checkpoint-tool sections of the owner doc. | Closed manifest passes on Linux; receipts prove grants; Windows scope is reported separately. |

Allowed production files, all relative to `tools/Antiphon.Checkpoints/`:

- `Slots/LeaseHolder.cs`, `Slots/BuildSlotClient.cs`, new `Slots/SlotDiagnostic.cs`.
- `CheckpointApp.cs`, `Program.cs`.
- `Baseline/BaselineComparer.cs` (TestDesign amendment: honor refusal in `RunLeased`; retain a bounded diagnostic in `ToolRuns`; no worktree cleanup-policy changes).
- `Execution/RunScheduler.cs`, `Execution/RowRunner.cs`.
- `State/RunState.cs`.
- `Report/CheckpointLine.cs`, `Report/ReportModel.cs`, `Report/ReportMerger.cs`,
  `Report/ReportWriter.cs` (slot evidence rendering only).

Allowed test files:

- New `tests/Antiphon.Tests/Checkpoints/CheckpointSlotContractTests.cs`.
- New `tests/Antiphon.Tests/Checkpoints/CheckpointSlotExecutorTests.cs`.
- New `tests/Antiphon.Tests/Checkpoints/BuildSlotBrokerFixture.cs`.
- Existing `tests/Antiphon.Tests/Checkpoints/BuildSlotClientTests.cs` (change the
  shared-lease expectation to refusal, preserve both landed null-renewal methods, add limiter; projected post-prerequisite roster is 12).
- Existing `tests/Antiphon.Tests/Checkpoints/CheckpointTestSupport.cs` (only make synthetic GET listings structurally valid; preserve prerequisite temp ownership).
- Existing `tests/Antiphon.Tests/Checkpoints/RunSchedulerTests.cs`: its synthetic `SlotPids` fixture must now supply the static UTC wire start that the strict client requires; its regression assertions and test roster remain unchanged.
- `scripts/lib/checkpoint-usage.ps1` is a necessary additive census exception: `CheckpointTempUsageTests.namespace_census_matches_compiled_checkpoint_cases` requires its selected count to move from 272 to 290 when the 18 planned cases are added. Only that number changes; no usage protocol or runner behavior changes.
- Existing `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs` (register spawning classes).

Allowed docs: this plan and `docs/testing-and-build.md`. No project/package change
is expected: `Antiphon.Tests.csproj` already references both the checkpoint tool
and SessionRunner. Use the prerequisite's temp-scope helpers without editing
their cleanup implementation. Existing regression tests may be read, not silently
expanded or rewritten to accept a regression; any additional file/test need must
be recorded in a committed footprint/manifest amendment before execution.

Out of scope: other `scripts/`, `server/`, `src/`, broker settings/contracts, deployment,
ownership guard semantics, secret custody, temp reclamation, source certification,
and CARD-0850 output-accounting behavior. Do not edit generated `docs/cards/`.

## Verification design

### Inspection and refusal propagation

Bodies inspected: `LeaseHolder`, `BuildSlotClient`, `CheckpointApp`, `Program.Row`,
`RunScheduler`, `RowRunner`, `BaselineComparer`, `RunState`, `ExitCodes`, and the
four report producers; real `BuildSlotBroker`/`BuildSlotRoutes`; existing slot,
scheduler, row, app, line, merger, baseline and process-limiter tests and their
shared helpers. Prerequisite temp scope, executor acknowledgement, usage roster,
native fixtures and changed test declarations were also inspected. The proposed
new fixture/tests do **not** exist yet; this audit freezes their construction and
assertions, not a claim that an implemented fixture passed.

| Boundary | Current hole | Required result and detector |
|---|---|---|
| Probe -> acquire | `SlotSession` has only Mode/CPU; unknown modes fall into POST. | Carry refusal exit/reason/diagnostic; a refused probe creates no holder or POST. V-3. |
| Holder -> POST | Actual child exists, but its start is null. | Capture once, serialize actual child UTC identity; capture failure disposes and returns exit 2. V-1/V-11/V-18. |
| Lease -> build | `BuildOneAsync` admits exit 2 through the CPU-4 fallback. | Test nonzero before driver/CPU selection; store admission exit/reason/wait on build progress. V-13. |
| Failed build -> dependent rows | `BuildFailed` produces an empty generic placeholder; `BuildReport` fabricates slot=skipped and n/a counts. | Preserve build admission facts on every dependent row, zero executed tests, exit 2 (or 4 for slot timeout), slot=refused/timeout and original wait. Ordinary compiler failure remains build-failed. V-13. |
| Lease -> scheduled row | `RunRowAsync` checks only exit 4; refused commands can execute. | Refusal result before `RowRunner`, same reason/wait in progress/result/line. V-14, including a TUnit row after a successful build as an internal case. |
| Lease -> direct row | `Program.RunAsync` does not forward runtime into Row; Row checks only exit 4. | Forward HTTP/driver/output seams, use the real holder/client; return 2 with a refusal CHECKPOINT line and no build/test. V-17. |
| Lease -> baseline driver | `BaselineComparer.RunLeased` also checks only exit 4. | Return the nonzero admission result before any driver; diagnostic in ToolRuns, no claim of baseline execution/classification. V-14 internal baseline case; R-1 adds its five existing regressions. |
| Result -> persistent report | `ToReportRow` and `ReportMerger.Clone` currently have no slot fields. | Typed Slot/SlotReason/WaitedSeconds and admission exit survive state, JSON, Markdown/line and merge. V-13. |
| Report -> caller exit | `ExitCodes.FromRowStates` already gives 2 precedence over 1. | Refused rows reach that aggregation; final state and returned exit are 2, not executor-crashed 6 or successful 0. V-13/V-14/V-17; existing seven exit-code cases. |

Do not rely on a log message to block launch, or parse a formatted line to recover
structured admission state. A probe-only refusal must also emit selected-row
receipts through the normal scheduler/report path. For direct Row the public
receipt is the emitted CHECKPOINT/EXIT CODE output; it has no existing full-run
report.json contract. Capture that output through an optional runtime TextWriter
(default Console.Out), not a process-global Console.SetOut change. The ordinary
executor's full diagnostic remains in its owned executor.log.

### Delivery inventory

There is no new agent/session queue, caller prompt or transcript delivery here.
The asynchronous paths being changed have these local receipts:

| Producer -> destination | Identity and persistence | Recovery/observable receipt |
|---|---|---|
| Holder/client -> broker routes | PID + captured UTC start, label; broker lease ID after grant | Actual serialized HTTP request and real route response; retry reuses identity, replacement changes it; V-1/V-11. A GET 200 is not a lease. |
| Admission -> driver scheduler | Run ID + build ID/CP ID; build/row progress and refusal result | Await executor completion; driver call roster plus final state/report prove refusal or grant, including dependent rows; V-11/V-13/V-14/V-17. |
| Client Note -> executor writer -> caller report | Run ID + label + operation; bounded executor.log, report.json, report.md and CP line | Await the existing writer flush/disposal, reopen files, deserialize, then merge through real ReportMerger and render again; V-8/V-9/V-13. No new crash-replay promise is introduced. |
| Lease owner -> renewal/release | Lease ID; renew/DELETE request roster and holder handle | Cancellation/drain completes before DELETE; broker live set and exact child exit observed before fixture cleanup; V-8/V-12/V-15/V-18. |

### Production-path fixture and failure detection

Replace the proposed hand-written broker response adapter with an in-process
ASP.NET **TestServer mapping the real BuildSlotRoutes.MapBuildSlotRoutes** and
real BuildSlotBroker. Microsoft.AspNetCore.Mvc.Testing already supplies the
TestHost dependency; no new package/project reference is needed. Follow the
service setup in `tests/Antiphon.SessionRunner.Tests/BuildSlotTestHost.cs`, but
use UseTestServer/CreateHandler, no Kestrel port and no real runner Program.
Register explicit fixture-owned options, memory, clock and liveness services;
ignore ambient runner settings. Use renew liveness, budget 2, CPU 6, memory floor
0. The foreign-PID liveness probe always returns null. Broker time must not age
past grace while a driver is deliberately gated.

Qualification sends a positive PID/nonblank label with null start through the
real routes: assert HTTP 400, type/title build_slot_invalid and the exact detail.
The same PID/label plus a valid UTC start must yield a real grant; DELETE it and
assert zero occupancy before acceptance. These internal assertions add no TUnit
executions. No fixture can return a constant granted/refused SlotLease. Only
malformed/transport/503 contract cases use scripted HTTP responses, explicitly
separate from the decisive real-route tests.

Add optional runtime SlotHandler, SlotClock and SlotDelay inputs to the normal
BuildSlotClient construction; `Runtime.Slots` stays **unset**. Preserve the real
ProcessLeaseHolderSource and Note callback in executor and Row. A delegating
request recorder captures the actual outbound body. For V-13/V-14/V-17 only,
a fault adapter removes processStartUtc from a copy **after recording the valid
original body**, then sends it to the real route. Thus the 400 is produced by
real broker validation without breaking holder creation as test setup. No mutation
changes this adapter. The fake driver is a harmless spy writing tiny TRX files;
it must record and return even if accidentally called on refusal (do not throw
in setup and mask the intended no-driver assertion).

V-11 uses one successful build and two rows that can overlap. At each driver
entry record live lease/holder/label, parsed UTC start versus Process.StartTime,
and build -maxcpucount:6. UTC comparison uses an independently retained Process handle, tolerance at most
1 ms. To test the cached getter itself without replacing the executor source,
V-11 also owns a real ProcessLeaseHolder.Start subcase: read its property twice
separated by 10 ms, assert exact string equality and the independent start time,
and dispose in finally. Merely checking the request retry copy would miss a
getter that returns UtcNow, since AcquireAsync reads it once. Record both rows'
active grants and the sibling surviving the first release. Controller gates
release independently of whether a second driver/renewal ever arrives: always
complete or cancel/await every task in finally, then assert the completed driver
and grant roster. A missing second start must produce a roster assertion, never
an indefinite wait for that start.

Acquisition time is virtual and advances by the requested delay; sticky terminal
responses allow the old 60-second loop to finish and fail `delay-count == 0`.
Renewal uses real cancellable delay, not that instantly completed acquisition
delay. In V-12 the driver finishes on an independent observation window spanning
two configured renewal intervals, whether a renew occurs or not; after normal
completion assert the recorded renew count/order. No `WaitAsync` timeout is a
PC red. V-8 can hold a renew response through cancellation, release it via an
independent controller, and assert its completion precedes DELETE. Cleanup tests
snapshot broker occupancy/child liveness **before** fallback fixture disposal.
The fixture finally cleans only its own captured identities, even under a mutant.

V-18's optional start-time reader is inside LeaseHolder, defaults to the actual
process read, and can be forwarded by the real holder source. Its throwing test
callback retains an independent Process handle for the newly started child.
Assert admission exit 2, zero broker requests and child exit when acquisition
returns; then finally reap that exact owned child if the assertion failed.
This seam cannot replace holder creation or prefill a timestamp in V-11.

Use the prerequisite's owned temp base and real executor-ack contract. Do not
bypass acknowledgement or allocate unowned roots. All new executor methods are
Integration with ParallelLimiter<ProcessSpawnLimit>. Contract methods spawn no
children and are Unit; fake QueueHolders are appropriate only there. Add the
limiter to existing BuildSlotClientTests, which already starts a real holder.
Update its scripted GET listings (including PidIdempotentSlotHandler) to valid
broker listing shapes/positive CPU values without relaxing the new parser.
Do not mutate/unset ambient credentials: fake owner lookup and handler explicitly
supply synthetic bindings, with the missing-token case confined to V-16.

### New ordinary roster

All names below are single, non-parameterized `[Test]` methods. Internal variants
are not extra executions. The TestDesign keeps 18 new methods (10 contract + 8 executor); expanded
internal scenarios below do not add methods or executions. Assertions are against production outcomes, not constants returned
by a fixture pretending to be the client.

| ID | Class and method | Assertions that must detect a defect |
|---|---|---|
| V-1 | `CheckpointSlotContractTests.holder_identity_is_preserved_in_acquire_and_retry` | Valid PID/start pair is identical on busy retry; a replacement carries its own pair; strict broker accepts it. |
| V-2 | `CheckpointSlotContractTests.definitive_acquire_400_refuses_without_delay` | Per internal 400/401/403/404/unrecognized-409 case: exit 2/refused, exactly one POST, zero delays, exact invalid detail preserved; holder disposed. The real-route 400 remains the decisive case. |
| V-3 | `CheckpointSlotContractTests.probe_400_refuses_without_delay` | Rejected session and AcquireAsync result both carry exit 2/CPU 0; no holder or POST, zero delays, GET status/body retained. Internal 400/401/403/unexpected 4xx cases use the same refusal predicate. |
| V-4 | `CheckpointSlotContractTests.malformed_probe_success_is_refused` | Malformed and shape-invalid GET 200 cannot become enabled/unleased. |
| V-5 | `CheckpointSlotContractTests.malformed_grant_is_refused` | Missing lease, malformed JSON and invalid CPU grant cannot admit work; release a newly identified grant if later validation fails, without releasing another driver's grant. |
| V-6 | `CheckpointSlotContractTests.transport_fallback_carries_last_failure_after_bounded_grace` | GET and POST transport-only variants use bounded virtual grace, CPU 4, explicit fallback reason and last observation; cancellation does not become fallback. |
| V-7 | `CheckpointSlotContractTests.answered_server_error_never_becomes_successful_unleased` | GET and POST 503 variants exhaust bounded retry then exit 2 with status/body; an answered 503 followed only by transport failures still refuses and retains that observation. |
| V-8 | `CheckpointSlotContractTests.renew_and_release_diagnostics_keep_status_and_body` | Renewal and release failures have operation/status/detail; renewal cancellation drains before DELETE. |
| V-9 | `CheckpointSlotContractTests.diagnostics_are_bounded_and_escape_body_controls` | Overlong/multiline response yields a bounded single-line excerpt and truncation marker; synthetic reflected token is redacted; two interleaved acquisition labels retain their own diagnostics, never a shared LastError. |
| V-10 | `CheckpointSlotContractTests.compatible_modes_preserve_limits_and_reasons` | Initial 404 reason/no POST; disabled listing and explicit unlimited grant honor CPU limit without a held lease. |
| V-11 | `CheckpointSlotExecutorTests.executor_reachable_broker_grants_build_and_rows` | Normal composition and real holders: build and two rows granted; CPU 6 applied to build; distinct simultaneous leases; fresh report lines/JSON agree; zero held leases/holders on completion. Also compare held start value across reads/retries. |
| V-12 | `CheckpointSlotExecutorTests.executor_renews_until_driver_finishes_then_releases` | Driver exits independently after two renewal intervals; completed request roster contains matching renew before release, none after disposal, and holder exit. Missing renewal fails the count assertion after normal completion. |
| V-13 | `CheckpointSlotExecutorTests.executor_rejection_launches_no_build_or_rows_and_reports_reason` | Reachable broker rejects acquisition: no driver calls, exit 2, dependent rows preserve reason/wait, executor.log contains body/status; reason survives report JSON and ReportMerger. An internal scripted GET-400 case proves probe refusal also reaches dependent-row receipts. |
| V-14 | `CheckpointSlotExecutorTests.executor_row_rejection_does_not_run_command` | Command-only and built-TUnit manifests: row acquisition 400 yields zero row-driver calls, exit 2 and reason. Internal BaselineComparer case uses the same real client/holder and route rejection; no baseline git/build/test driver calls, refusal retained in ToolRuns. |
| V-15 | `CheckpointSlotExecutorTests.executor_cancellation_releases_lease_and_holder` | Cancellation while a fake driver owns a grant drains it and the holder; no leaked active lease. |
| V-16 | `CheckpointSlotExecutorTests.executor_missing_owner_token_stops_before_slot_probe` | Synthetic bound owner without token yields exit 7/owner-unverified, no host.txt/git.txt capture, zero slot requests and zero drivers. Ambient credentials are not used or unset. |
| V-17 | `CheckpointSlotExecutorTests.row_entrypoint_rejection_invokes_no_driver` | `Program.RunAsync` row path uses real slot client/holder; 400 returns exit 2, no build/test call, receipt includes reason. |
| V-18 | `CheckpointSlotExecutorTests.failed_start_time_capture_disposes_new_holder` | A narrow injected start-time reader throws after the real holder starts; capture its identity in the fixture, assert exit 2/slot refusal (not executor-crashed), assert child exit before failed acquisition returns, and assert no broker request. Fixture finally retains cleanup of that exact owned child. |

### Static census and landing reconciliation

Census method: load the installed Roslyn parser into PowerShell (ParseText only,
no compilation), inspect tracked C# Test method syntax nodes, count each Arguments
attribute as one case, otherwise one per method. Strings/comments containing fake
Test attributes do not count. Unit includes class/method Category(Unit), linked
`tests/Shared/TestClassificationGuardTests.cs`, and the 18 statically enumerated
`DispatchHoldLedgerTests.HoldSentences` cases (one MethodDataSource method).
No other Unit matrix/repeat/data-source expansion was found; CancellationToken
parameters do not multiply cases. Exclude helper/child-host assemblies. Count
methods and cases separately, preserving full namespace/class/method/argument
identities, not just aggregate totals. Code can repeat this read-only source
census before any build; do not run --list-tests or test discovery for this stage.

| Selection | TestDesign base 5cd8d0b9 methods / cases | Prerequisite branch 1dcc03c0 methods / cases | Required additive landing + CARD-0833 cases |
|---|---:|---:|---:|
| BuildSlotClientTests | 11 / 11 | 11 / 11 | 12 (the two branches add differently named null-renewal regressions) |
| RunSchedulerTests | 14 / 14 | 7 / 7 | 14 |
| RowRunnerTests | 13 / 13 | 13 / 13 | 13 |
| ExitCodeTests | 1 / 7 | 1 / 7 | 7 |
| BaselineComparerTests | 5 / 5 | 5 / 5 | 5 |
| CheckpointAppTests | 3 / 3 | 3 / 3 | 3 |
| CheckpointLineTests | 3 / 3 | 3 / 3 | 3 |
| ReportWriterTests | 6 / 6 | 5 / 5 | 6 |
| ReportMergerTests | 1 / 1 | 1 / 1 | 1 |
| ProcessSpawnLimitTests | 3 / 3 | 3 / 3 | 3 |
| Whole Checkpoints namespace (informational, not a new run) | 155 / 161 | 250 / 259 | 293 cases / 284 methods after F1 (+3 methods/cases) |
| Whole Unit selection | 2459 / 3592 | historical execution totals are not used | 3680 cases / 2544 methods after F1 (+1 Unit method/case) |

**Code source re-census at assigned HEAD `6bf159cf6` (before execution):**
the landed tree contains 263 Checkpoints methods / 272 expanded cases and
2533 Unit methods / 3669 expanded cases. The Roslyn syntax census expands
`Arguments` and the 18 `DispatchHoldLedgerTests.HoldSentences` cases; no test
host was launched. `Get-NamespaceCensus` independently records 272 selected,
with 11 declared Windows skips (10 Linux recovery cases plus
`DetachedLauncherTests.executor_survives_its_starter`) and 18 Linux skips
(14 Windows recovery cases plus four Windows timeout cases). The named R-1,
R-2, R-3 rosters remain 51, 13, and 3 cases. Compared with the frozen
additive projection, the Checkpoints namespace is +2 cases; the current
`TimeoutTests.total_deadline_skips_a_queued_build_without_starting_it` and
`DetachedLauncherTests.executor_survives_its_starter` are present. The Unit
selection is +4 cases net, with four landed
`PostLandRetrospectiveContractTests` methods among the intervening changes.
After the 18 planned CARD-0833 methods, the frozen target is 281 Checkpoints
methods / 290 cases and 2543 Unit methods / 3679 cases. Linux's Unit outcome
partition remains 33 named skips, hence 3646 executed/passed. Re-run this
source census and reconcile exact names after S3 before launching checkpoints.

R-1 is the first five classes: **45 methods / 51 cases** after additive landing.
ExitCodeTests is its one seven-case method. R-2 is the next four classes:
**13 methods / 13 cases**. R-3 is ProcessSpawnLimitTests: **3 / 3**. Including
V-1..V-18 and the three F1 additions, CP-1..CP-5 select **82 methods / 88 cases**, all passed, zero skips.
This explicitly supersedes the original 79-case plan: +5 baseline regressions,
+1 prerequisite null-renewal regression. Each row's exact class members must
match the source census, not merely meet MinExecuted.

The prerequisite branch and current master are different source populations.
Do not replace master's extra seven scheduler, one report-writer, one timeout
and one wait-command case with its older versions. Preserve both
`pid_liveness_grant_with_null_renewal_is_granted_without_renewing` and
`null_renewal_interval_is_granted_without_renewal` unless a reviewed, committed
manifest amendment explicitly consolidates them. The prerequisite adds 105 new
methods/108 cases in new classes and that one null test; 69/72 of the new-class
methods/cases are Unit. Thus the additive namespace is **261 methods / 270
cases before 0833**, and Unit is **2529 methods / 3665 cases**. The brief's
`261 cases` is not reproducible as expanded cases: 261 is the projected method
count. At the inspected prerequisite tip the count is 250/259, not its stale
plan's 256 cases; its four-case completed_wait method adds three expansions,
and ExitCodeTests adds six. No future landing is claimed from these projections.

CARD-0804/0805 CP-9/CP-10 belong to that owner's namespace-usage protocol, not
this six-row manifest. Their filter remains `/*/Antiphon.Tests.Checkpoints/*/*`.
Both passes must use the **same committed SHA and exact census-derived roster**,
including parameter IDs, and match that roster to complete TRX results. For the
inspected branch alone the static Linux expectation is 259 selected / 241
executed / 18 OS skips; after the additive landing it is 270 / 252 / 18; with
0833's portable 18 it is 288 / 270 / 18. Do not reuse 256, 261 or a historical
floor in those usage receipts. The 18 exclusions are 14 Windows recovery methods
and four TimeoutTests Windows methods. Confirm any host-capability skips by exact
name/reason instead of claiming those as executed. CARD-0835 plans five
CheckpointSourceStateTests and six CheckpointSourceExecutionTests in this same
namespace: another **11** cases, giving 299 selected if nothing else changes.
Re-census its actual landing; do not run this namespace now or alter its usage
scripts in this card. This document gives the caller the reconciliation obligation.

R-4 (CP-6) is exact census equality for the full Unit lane. Current base has 3592
selected cases, 33 declared Linux skips and 3559 expected executed cases on a
fully capable Linux test host. Add prerequisite Unit delta 73 and 0833 delta 10:
**3675 selected = 3642 executed/passed + 33 skipped**, zero failed. The 33 are:
GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls
(12 Arguments); DirectoryBrowseServiceTests' five RequireWindowsDrives callers;
PtyDeliveryCeilingsTests' three RequireRedistributable callers;
SessionDeliveryProfileTests' two RequireLocalModernConPty callers; the four
TimeoutTests windows_* methods; LandingRemovalPolicyControlTests.C665_LockedFileMidDeleteResumesOnLaterPass
and .C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded; and one each at
AgentRegistrySettingsTests.The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine,
AgentExecutableResolverTests.Resolves_sibling_flavor_when_configured_one_is_gone,
AgentPinPathTests.V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash,
ClaudeRemoteControlLaunchArgsTests.Off_settings_path_round_trips_through_LaunchArgvGuard,
and DelegationReportFormatterTests.reported_repository_paths_normalize_relative_and_absolute_windows_forms.
The three redistributable skips require the Linux host without shipped ConPTY;
if capability changes, amend the selected outcome partition before execution.
Symlink/permission failures or any additional skip are reported and triaged,
not silently subtracted. Windows uses its own census-derived outcome partition;
Linux's 33 skips are not portable Windows expectations.

**Freeze gate before Code's first checkpoint:** read the confirmed prerequisite
landing SHA, preserve the union above, statically census the actual committed
S1-S3 tree and commit its exact per-row methods/cases/skip partition in this
section. The table below is frozen to the explicitly calculated additive tree,
not permission to run stale numbers after an intervening commit. If the landed
roster differs, enumerate the named delta and update Expect/Min together before
execution. No open-ended >= rule or historic count can satisfy this gate. No
extra build/discovery run is needed. The normal CP TRX must then prove exact set
and multiplicity equality, including no unintended class-prefix matches.
The prerequisite C804_ROSTER_FILE hook may supplement that comparison during
these runs; it is not execution evidence by itself.

The Unit row overlaps Unit members of the named rows. Do not add a whole
Checkpoints sweep: known CARD-0818/0828 concurrency/owner-watch hazards are not
expanded into extra named runs. Their presence in the required Unit lane remains
visible; inherited failures require triage and cannot silently change selection.

### Guard inventory and positive controls, pending post-land Mutation

The original **17 PC groups / 26 variants** are audited individually in rows
1-26 below; the Original column preserves that crosswalk. They were insufficient:
V-8 had no detecting control (old PC-11 incorrectly named it for report merging),
renewal could fail only by timeout, and baseline admission plus independent
cleanup/serialization predicates were omitted. Rows 27-48 add the missing cuts
without adding TUnit methods. Final inventory: **48 independently bypassable
guards, 48 distinct PCs, missing=0, duplicate guard-to-PC mappings=0**. More than
one guard may use the same method, but each mutation/run is separate.

Each row G-n maps only to PC-n. The Guard/defect column names the production
invariant and compiling change; the Assertion column is the exact assertion
label Code must use in that method. Do not mutate fixture responses, assertions
or setup. Keep other guards valid so the target cannot be masked by another
refusal. Normal method completion with the named assertion failure is required;
a timeout, leaked-fixture teardown exception, compilation failure, unrelated
exception or zero-test selection is not a red control.

| G / PC | Original variant | Production guard / compiling defect | Single detecting method | Named assertion after bounded completion |
|---|---|---|---|---|
| 1 | PC-1 | Real holder identity: restore ProcessStartUtc => null. | V-11 | actual-driver-roster: one build and two rows executed with grants; run-exit=0. |
| 2 | PC-2A | Wire identity: omit serialized processStartUtc. | V-1 | outbound-start-equals-holder: every actual request contains the expected timestamp. |
| 3 | PC-2B | Replacement identity: keep the old PID/start after opening replacement. | V-1 | replacement-pair: second holder's pair is used, old holder disposed once. |
| 4 | PC-3A | Definitive acquire refusal: return successful unleased on POST 400. | V-2 | acquire-refused-exit: exit 2, state refused, CPU 0. |
| 5 | PC-3B | Immediate refusal: retain the former retry/grace before returning 400 refusal. | V-2 | acquire-delay-count: zero delays and one POST (virtual clock lets the mutant return). |
| 6 | PC-4 | Definitive probe refusal: turn GET 400 into unavailable/unleased. | V-3 | probe-refused-exit: rejected session with exit 2/status/detail. |
| 7 | PC-5A | Valid probe shape: accept malformed/shape-invalid GET 200. | V-4 | invalid-probe-refused: every malformed/shape/CPU case refuses. |
| 8 | PC-5B | Grant identity required: accept POST 200 with no lease and no unlimited flag. | V-5 | missing-lease-refused: exit 2, no admitted work. |
| 9 | PC-6 | Answered server error: exhausted 503 becomes successful unleased. | V-7 | answered-error-refused: both GET and POST end exit 2 with 503 detail. |
| 10 | PC-7A | Explicit fallback evidence: discard transport fallback reason. | V-6 | fallback-reason: runner_unreachable and last exception retained. |
| 11 | PC-7B | Transport grace: fall back on first exception. | V-6 | transport-grace: virtual elapsed equals configured grace and expected retry count. |
| 12 | PC-8 | Renewable grants: disable renewal for positive interval. | V-12 | renew-before-release: completed request roster has matching renew before DELETE; independent driver window ends normally. |
| 13 | PC-9A | Release custody: omit DELETE. | V-15 | released-lease: DELETE count one and broker live leases zero, before fallback cleanup. |
| 14 | PC-9B | Holder custody: omit owned-holder disposal. | V-15 | owned-holder-exited: captured child is gone after normal disposal, before fallback cleanup. |
| 15 | PC-10A | Build admission: restore timeout-only check in BuildOneAsync. | V-13 | build-driver-count: zero builds and rows despite spy being safe to call. |
| 16 | PC-10B | Scheduled admission: restore timeout-only check in RunRowAsync. | V-14 | refused-row-driver-count: zero command/TUnit row calls. |
| 17 | PC-10C | Direct admission: restore timeout-only check in Program.Row. | V-17 | direct-driver-count: zero build/test calls and emitted exit 2. |
| 18 | PC-11A | Durable error body: omit broker body from acquisition Note. | V-13 | durable-acquire-detail: reopened executor.log contains operation=acquire, status=400, reason and exact broker detail. |
| 19 | PC-11B | Merge preservation: omit SlotReason in ReportMerger.Clone. | V-13 | merged-slot-reason: deserialized merged row retains build_slot_invalid. V-8 is not a report test. |
| 20 | PC-12A (bounding) | Bounded diagnostics: remove 2048-character excerpt cap. | V-9 | excerpt-bound: capped body plus fixed marker, with truncation reported. |
| 21 | PC-12B | Secret reflection: omit synthetic-token redaction. | V-9 | token-absent: reflected sentinel absent from diagnostic/log/receipt. |
| 22 | PC-13 | Owner admission: force entryAdmitted=true after the initial owner check, retaining later checks. | V-16 | owner-before-evidence: host.txt/git.txt do not exist. This named file-roster assertion detects the bypass even if later checks/canceled tokens still prevent HTTP and drivers. |
| 23 | PC-14 | Null renewal compatibility: restore TryGetInt32 on JSON null. | BuildSlotClientTests.pid_liveness_grant_with_null_renewal_is_granted_without_renewing | null-renewal-granted: capture exception/result, then assert the outcome is exception-free, granted and exit 0; either raw exception or a translated refusal fails this assertion. |
| 24 | PC-15 | Duplicate ownership: exhausted duplicate grants return successful unleased. | BuildSlotClientTests.a_shared_pid_lease_is_not_claimed_by_the_second_driver | duplicate-refused: second exit 2/CPU 0, first lease remains live until its own release. |
| 25 | PC-16 (disabled GET) | Explicit disabled broker: treat enabled=false listing as enabled. | V-10 | disabled-no-post: supplied CPU honored and zero acquire/renew/delete calls. |
| 26 | PC-17 | Failed capture cleanup: omit child disposal when start-time reader throws. | V-18 | failed-capture-child-exited: exact child handle observes exit before failed acquisition returns. |
| 27 | added | Renewal diagnostics: discard answered renew status/body. | V-8 | renew-diagnostic: operation/label/status/detail of the scripted renewal error present. |
| 28 | added | Release diagnostics: discard answered DELETE status/body. | V-8 | release-diagnostic: operation/label/status/detail of the scripted release error present. |
| 29 | added | Drain before release: issue DELETE before cancellation/await of renewal. | V-8 | renewal-drained-before-delete: completion event precedes DELETE; no later renew. |
| 30 | added | Cancellation: return a successful fallback lease from the caller-cancellation catch. | V-6 | cancellation-propagated: cancellation captured, no successful lease/fallback or extra requests. |
| 31 | added | Invalid-grant custody: omit release of a newly acquired lease whose later validation fails. | V-5 | invalid-grant-released: that unique lease released once, pre-existing sibling remains live. |
| 32 | added | Dependent receipt: turn build admission refusal into generic build-failed placeholder. | V-13 | dependent-admission: every dependent row keeps exit/state/reason/wait and zero execution in reopened state and report. |
| 33 | added | Report mapping: omit SlotReason when mapping result -> ReportRow. | V-13 | persisted-slot-reason: original report.json retains reason before merge. |
| 34 | added | Stable captured identity: return current UTC time on each holder read. | V-11 | cached-holder-start: repeat reads/outbound retries equal captured Process.StartTime, within 1 ms. |
| 35 | added | Grant CPU: replace admitted MaxCpuCount with fallback 4. | V-11 | granted-build-cpu: actual build argv contains -maxcpucount:6. |
| 36 | added | Per-driver identity: serialize the executor parent PID/start for every holder. | V-11 | simultaneous-holder-roster: two distinct actual child identities/grants, plus sibling survives first release. Controller finishes without waiting forever for a missing row. |
| 37 | added | Baseline admission: restore timeout-only check in BaselineComparer.RunLeased. | V-14 | baseline-driver-count: zero baseline driver calls; ToolRuns retains refusal. |
| 38 | added | JSON syntax: catch malformed grant JSON and treat it as admitted. | V-5 | malformed-grant-refused: exit 2/CPU 0, no admitted lease. |
| 39 | added | Grant CPU validity: accept zero/negative/wrong-kind CPU as fallback 4. | V-5 | invalid-cpu-refused: each invalid CPU grant refuses, cleanup still observed. |
| 40 | PC-12A split | Single-line diagnostics: omit escaping of body control characters. | V-9 | diagnostic-one-line: raw CR/LF/NUL absent; escaped content retained within cap. |
| 41 | PC-16 split | Explicit unlimited grant: treat POST unlimited=true as a missing-lease error. | V-10 | unlimited-grant-limit: exit 0/supplied CPU, no renewal/release. |
| 42 | added | Probe refusal propagation: let AcquireAsync process a refused SlotSession as enabled. | V-3 | refused-session-no-acquire: no holder/POST, lease exit 2/CPU 0. |
| 43 | added | Sticky answered error: discard remembered 503 when a later request has a transport exception. | V-7 | answered-then-unanswered-refused: grace ends exit 2 with preserved 503 observation. |
| 44 | added | Capture failure admission: map start-reader exception to successful unleased. | V-18 | failed-capture-admission: exit 2/refused/CPU 0, zero broker calls. |
| 45 | added | New process limiter: remove CheckpointSlotExecutorTests' limiter attribute. | ProcessSpawnLimitTests.Process_spawning_classes_carry_the_limiter | CheckpointSlotExecutorTests must carry the limiter (existing reflection assertion). |
| 46 | added | Existing holder-test limiter: remove BuildSlotClientTests' limiter attribute. | ProcessSpawnLimitTests.Process_spawning_classes_carry_the_limiter | BuildSlotClientTests must carry the limiter (existing reflection assertion). |
| 47 | added | 404 compatibility evidence: discard broker_not_found on initial GET 404. | V-10 | initial-404-reason: unavailable/CPU 4, broker_not_found, no acquire/delay. |
| 48 | added | Acquisition-local evidence: replace immutable/local observations with a shared last-error value. | V-9 | diagnostics-stay-with-label: interleaved acquisitions retain their distinct status/detail/label. |

The original PC-12A/PC-16 each hid two independently bypassable guards; rows
40/41 split them. Diagnostic failures and refusal propagation no longer borrow
an unrelated detecting method. PC-23 updates the existing method's exception
capture/assertion only; it does not add another test. V-1 uses known, non-null
fake holder pairs and a bounded duplicate-response sequence followed by a valid
replacement grant. V-2 uses a sticky 400 response and virtual time, so a retrying
mutant reaches the intended counter assertion. V-13 includes nonzero prior busy
wait before rejection as well as immediate rejection, so dropping wait evidence
can actually fail. V-5 includes a distinct pre-existing lease so cleanup cannot
pass by releasing every ID. V-9's concurrent scripted requests are released in
opposite order to detect a shared observation field. Supply its redaction token
through an optional client diagnostic input sourced from Runtime.EnvironmentLookup
in composition (production default reads the existing environment lookup); never
set or read a real task token in the test. Redact, escape, then bound the excerpt,
so control-character expansion cannot evade the cap. The duplicate-lease method
includes both no-holder and exhausted-replacement cases, asserting the bounded
replacement count and preserving the first driver's live grant.

Every PC is **method-scoped**, including baseline, red and restored green.
For a V-n substitute its Class.method from the 18-row roster into the exact
single argument `--treenode-filter '/*/Antiphon.Tests.Checkpoints/Class*/method'`.
For PCs 23/24 use the full methods printed above with the same namespace; for
45/46 use `/*/Antiphon.Tests/ProcessSpawnLimitTests*/Process_spawning_classes_carry_the_limiter`.
All 48 controls select exactly **one** non-parameterized TUnit result per phase,
zero skips and no other method. Do not use the whole class, namespace, a CP row,
or an OR of full filter paths for a PC. New tests use internal loops, never
Arguments, so this count stays one. Store actual assertion messages and TRX
names/counters. Each variant needs a compiling production defect, intended
assertion red, exact restoration, fresh build and restored green; refresh source
timestamps so a mutant DLL cannot be reused. Ordinary Code/Review precedes
land; all 48 PCs plus discovery stay pending on the original card's post-land
verification companion at the immutable SourceLanding commit.

### Scope limits

The real broker routes and foreign-PID validation are reused, not changed.
Their general FIFO, memory-floor, TTL and liveness policies remain owned by
CARD-0589 and its broker tests. Existing busy/memory-floor/exit-4 client behavior
is retained by R-1; Code must not redesign those guards in this repair. If the
implementation changes an independently bypassable policy outside the 48 cuts,
amend the named control and cost before handoff instead of claiming it covered.
Renewal-loss handling for an already running driver, owner/token policy,
checkpoint temp recovery, CARD-0835 source authority, and CARD-0850 cleanup
accounting remain out of scope. No new live queue or deployment test is needed.

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

Linux ordinary floor: **35 minutes**, plus **2 minutes** leased tool bootstrap,
authoring estimate **180 minutes**, and observed slot waits: Code estimate
**217 minutes** before queueing. Windows estimates total **47 minutes** plus
bootstrap if separately commissioned. Linux is required; Windows smoke/regression
qualification is a separate reported result and is not claimed from Linux or
from the existing null-response unit test alone. The new tests should be portable.

Mutation estimate: 48 variants at 6 minutes per method-scoped restored cycle =
288 minutes, plus 20 minutes discovery/report/restoration = **308 minutes**.
Combined Linux authoring + bootstrap + ordinary + Mutation estimate is **525
minutes**. The added baseline regression row cost is one minute; splitting the
22 missing control variants adds 132 Mutation minutes. Build reuse still saves
five ordinary rebuilds; no unmeasured runtime saving is claimed. These are planning allowances, not measured durations.

### Checkpoints

One isolated build plus one exact filter per row; `CP-1` reuse means the same
build at the same committed `all` source, without another build. Filters use
trailing `*` on every class operand; table `\|` becomes literal `|` at the CLI.
The whole filter is one quoted argv value. For CP-3, the exact invocation operand
is `--treenode-filter '/*/Antiphon.Tests.Checkpoints/(BuildSlotClientTests*)|(RunSchedulerTests*)|(RowRunnerTests*)|(ExitCodeTests*)|(BaselineComparerTests*)/*'`.
Parenthesize each OR operand in the class segment, not the whole path; never
split full paths at pipes. Single-method controls use the formula above with
no method wildcard, because each listed method is non-parameterized.
The F1 source delta freezes **88 named executions**, plus **3680
selected Unit cases** (3647 executions and 33 declared Linux skips). Unit overlaps
named Unit members. Named rows require zero failures and zero skips. Reconcile
the actual prerequisite landing before running; this is a closed selection, not
an open-ended floor. Min is only the tool's mechanical lower-bound check; exact
roster/count/outcome equality in Expect is an additional acceptance requirement.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c833/` | slot-contract | `/*/Antiphon.Tests.Checkpoints/CheckpointSlotContractTests*/*` | V-1-V-10, F1 client wait | exactly 11 executed/passed, 0 failed/skipped | 11 | 12 | 18 | true |
| CP-2 | all | CP-1 | slot-executor | `/*/Antiphon.Tests.Checkpoints/CheckpointSlotExecutorTests*/*` | V-11-V-18, F1 executor and fallback wait | exactly 10 executed/passed, 0 failed/skipped | 10 | 4 | 5 | true |
| CP-3 | all | CP-1 | slot-regression | `/*/Antiphon.Tests.Checkpoints/(BuildSlotClientTests*)\|(RunSchedulerTests*)\|(RowRunnerTests*)\|(ExitCodeTests*)\|(BaselineComparerTests*)/*` | R-1 | exactly 51 executed/passed, 0 failed/skipped | 51 | 4 | 5 | true |
| CP-4 | all | CP-1 | receipt-regression | `/*/Antiphon.Tests.Checkpoints/(CheckpointAppTests*)\|(CheckpointLineTests*)\|(ReportWriterTests*)\|(ReportMergerTests*)/*` | R-2 | exactly 13 executed/passed, 0 failed/skipped | 13 | 2 | 3 | true |
| CP-5 | all | CP-1 | process-limit | `/*/Antiphon.Tests/ProcessSpawnLimitTests*/*` | R-3 | exactly 3 executed/passed, 0 failed/skipped | 3 | 1 | 1 | true |
| CP-6 | all | CP-1 | unit-lane | `/*/*/*/*[Category=Unit]` | R-4 | exact census: 3680 selected; Linux 3647 executed/passed, 33 named skips, 0 failed | 3647 | 12 | 15 | true |

F1 adds `CheckpointSlotContractTests.busy_then_refused_acquire_carries_exact_wait`
to CP-1 and `CheckpointSlotExecutorTests.executor_busy_then_rejection_retains_exact_wait_in_state_report_and_merge`
plus `report_fallback_line_retains_nonzero_wait_when_result_has_no_line` to
CP-2. A virtual 7-second busy interval precedes the refusal. The executor
case checks state, report JSON, both dependent Markdown lines, and merged
rows; the fallback case checks the separate `BuildReport` line formatter.
Historical counts and results below remain measurements of the earlier SHA.

## Handoff and completion

TestDesign is complete: 18 proposed methods, 48 distinct mapped guard/control
variants, real holder/HTTP-route composition, four refusal launch gates and
source-derived checkpoint counts are specified. No test or PC has executed.
Next is Code, held until confirmed CARD-0804/0805 publication/qualification and
the caller's exclusive checkpoint-tool lane. Code consumes that landed tree,
commits the exact census reconciliation before checkpoint execution, and lands
before CARD-0835 starts its overlapping edits; CARD-0850 follows. CARD-0853
remains the separate missing-token investigation.

Code completion requires Linux ordinary results at a pushed SHA, granted build
and row leases against the enabled host broker, no leaked fixture leases/holders,
durable rejection/fallback evidence, and the owner-doc update. Separate Review
then assesses the committed implementation; post-land Mutation retains the listed
controls. No runner redeploy or AppHost restart is required to activate a rebuilt
checkout-local tool. Existing detached shadow copies continue their old binary;
never claim the fix applied to an already running executor.

## Code result and red/green evidence (2026-10-01)

Implementation and ordinary Linux verification source SHA:
`f1778b580e83c6e35b3f6f9bb40a07b2ae09cc09`. The committed Roslyn source
census is 281 Checkpoints methods / 290 expanded cases and 2543 Unit methods /
3679 expanded cases. Each ordinary row was run directly through
`scripts/run-checkpoint.ps1`, which obtained its own host build-slot grant;
CP-1 built `tests/Antiphon.Tests` into `bin-c833/`, and CP-2..CP-6 reused it.
The filters and class rosters were the exact six-row manifest above.

| Row | Executed | Passed | Failed | Skipped | Slot | Waited |
|---|---:|---:|---:|---:|---|---:|
| CP-1 | 10 | 10 | 0 | 0 | granted | 15 s |
| CP-2 | 8 | 8 | 0 | 0 | granted | 75 s |
| CP-3 | 51 | 51 | 0 | 0 | granted | 60 s |
| CP-4 | 13 | 13 | 0 | 0 | granted | 150 s |
| CP-5 | 3 | 3 | 0 | 0 | granted | 45 s |
| CP-6 | 3646 | 3646 | 0 | 33 | granted | 75 s |

CP-6's first exact run selected the same 3679 cases and returned 3645 pass,
one failure, 33 skips: the unrelated
`ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` observed
12.5 s virtual time against its `<12 s` assertion. The required once-only
exact-row rerun passed 3646 / 3646 with 33 named Linux skips. Earlier build
compilation errors and the original renewal fixture failure were corrected in
new fast-forward commits; CP-3's initial 50/51 failure repeated before its
synthetic GET listing was made structurally valid. The new V-7 probe-wait
assertion also went red twice (expected 15 s, observed 0 s), then passed after
the `SlotSession` diagnostic elapsed time was carried into the refusal lease.

For independent red proof, a separate local clone of the same branch was used;
its mutant commits were **not pushed** and do not belong to the deliverable.
Mutant `00028de0b11beafab45d59585249a766f1121ded` omitted the wire start,
admitted answered errors and malformed bodies, discarded fallback reason and
renewal, changed 404 compatibility, and removed diagnostic bounding. Against
that compiled source, the exact V-1..V-10 class row executed 10/10 and failed
10/10 at named assertions. Exact method rows V-11, V-12, V-13, V-14, and V-17
each executed once and failed at, respectively, simultaneous occupancy, renew
count, build-driver count, refusal exit, and direct driver count. A discarded
combined method-OR filter returned zero tests and is not red evidence. After
reverting mutant A in the local clone, mutant
`f55747e6cc6c500ecfe443daaaf9a67e03440b63` omitted DELETE, treated a
bound owner with missing token as unbound, and omitted failed-holder child
reaping. Exact V-15, V-16, and V-18 method rows each executed once and failed
at broker occupancy, owner admission, and child-exit assertions. Thus all 18
new methods have observed red and green outcomes; the 48 planned PC variants
remain pending for method-scoped SourceLanding Mutation as specified.

The production edit footprint overlaps CARD-0835 in `CheckpointApp.cs`,
`Program.cs`, `RunScheduler.cs`, `RowRunner.cs`, `RunState.cs`,
`CheckpointLine.cs`, `ReportModel.cs`, and `ReportMerger.cs`; its source-identity
semantics were not changed. It overlaps CARD-0850 in `CheckpointApp.cs` only;
output cleanup behavior was not changed. CARD-0823's null renewal compatibility
remains covered by both existing methods. CARD-0853's owner guard was not edited
in the deliverable. The only script edit is the plan-recorded namespace census
change from 272 to 290 in `scripts/lib/checkpoint-usage.ps1`; no checkpoint
script driver was changed. Windows qualification is pending in a separate task
against the implementation SHA above.

### Final Code requalification after custody assertion review

The last source commit is `03e7ae692ccb0fb908398fde449d4089c50088cc`.
It strengthens V-11's wire PID/start equality and sibling-survival checks,
V-12's normal holder-exit check, and V-15's canceled holder-exit check; the
production implementation remains at `f1778b580e83c6e35b3f6f9bb40a07b2ae09cc09`.
The six closed rows were run again directly through `scripts/run-checkpoint.ps1`
at that clean source commit, with one isolated build and five reuse runs. All
six obtained host build-slot grants. Fresh TRX files are under
`.antiphon/card0833-final2/` in this worktree.

| Row | Executed | Passed | Failed | Skipped | Slot | Waited |
|---|---:|---:|---:|---:|---|---:|
| CP-1 | 10 | 10 | 0 | 0 | granted | 0 s |
| CP-2 | 8 | 8 | 0 | 0 | granted | 45 s |
| CP-3 | 51 | 51 | 0 | 0 | granted | 15 s |
| CP-4 | 13 | 13 | 0 | 0 | granted | 15 s |
| CP-5 | 3 | 3 | 0 | 0 | granted | 15 s |
| CP-6 | 3646 | 3646 | 0 | 33 | granted | 15 s |

There were no failures in this final six-row run. The 48 method-scoped PC
variants remain pending Mutation; these green rows and the earlier red proofs
do not discharge them. Windows qualification should use source commit
`03e7ae692ccb0fb908398fde449d4089c50088cc` (the production code is
unchanged from the implementation SHA above).

### Final Review repair (2026-10-01)

V-6 and V-7 now include internal busy-before-transport, busy-before-503,
continued transport and busy-reset sequences. A busy 409 resets acquisition's
unanswered grace; the first subsequent transport failure or 5xx starts its own
bounded grace. V-11 parses the wire start with its UTC offset intact and checks
both offset and UTC instant against the child process. V-17 captures direct-row
output through `Runtime.Output`, avoiding process-wide console redirection.
V-8 now waits for the scripted renewal observation and diagnostic instead of
sleeping for a fixed 1.2 seconds; the second full Unit run exposed that timing
dependency while CP-1 and the first Unit run passed V-8.
These internal cases leave the checkpoint census unchanged: CP-1 10, CP-2 8,
CP-3 51, CP-4 13, CP-5 3, and CP-6 3679 selected (3646 executed and 33
declared Linux skips). The six rows must be rerun at this repair's committed SHA;
the earlier results above apply to the earlier SHAs only. Windows qualification
and all method-scoped SourceLanding mutation controls remain pending.

#### Repair verification result

All six rows below used the exact closed filters above and one `bin-c833/`
build at committed source `3f72bf5c48d38a391b8620f5af6bf29c24b7aa14`.
Each direct `scripts/run-checkpoint.ps1` call obtained a host grant with CPU 6;
CP-2 through CP-6 reused the CP-1 build. TRX files are under
`.antiphon/card0833-repair-final/` in the runner mirror.

| Row | Executed | Passed | Failed | Skipped | Result |
|---|---:|---:|---:|---:|---|
| CP-1 | 10 | 10 | 0 | 0 | green |
| CP-2 | 8 | 8 | 0 | 0 | green |
| CP-3 | 51 | 51 | 0 | 0 | green |
| CP-4 | 13 | 13 | 0 | 0 | green |
| CP-5 | 3 | 3 | 0 | 0 | green |
| CP-6 first | 3646 | 3645 | 1 | 33 | `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` |
| CP-6 exact rerun | 3646 | 3645 | 1 | 33 | same unrelated virtual-time assertion |

V-11 separately passed 1/1 at that same SHA with `TZ=UTC`,
`TZ=America/New_York`, and `TZ=Pacific/Kiritimati`. In a separate detached
scratch worktree, each exact method run selected one test and zero skips:
V-6 and V-7 failed with the pre-fix `BuildSlotClient` and passed restored;
V-11 failed with `DateTime.SpecifyKind(started, Utc)` under New York time and
passed restored; V-17 failed when `Program.Row` ignored `Runtime.Output` and
passed restored; revised V-8 failed when renew diagnostics were dropped and
passed restored. Each mutant and restoration was committed in scratch before
its leased build/run. The scratch source and index were clean after each phase,
and the final scratch source had no diff against the repair source commit.

The unrelated CP-6 timing assertion remains red after the required exact-row
rerun, so this repair does not claim a green Unit lane. V-13 still lacks its
planned nonzero prior-busy-wait case and uses `WaitedSeconds >= 0`, which does
not test retained wait evidence. Baseline fetch aborts on a failed fetch as it
did at the pre-repair parent; the direct Row receipt reports the reason but
does not print the broker status/detail, consistent with the plan's direct Row
receipt scope. Windows holder start-time kind, process launch/exit custody,
and output behavior require the separate Windows qualification task. All 48
method-scoped SourceLanding mutation controls remain pending.
