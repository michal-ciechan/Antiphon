# CARD-1605: report storage budgets under contention

Plan date: 2026-10-29. Source inspected:
`f8bddb6efda7b0e8f50f3b45744087e52188bc66`. Task: `d8d36ebe`.
Scope: read-only investigation and this plan; no product changes or test execution.
This is a completed implementation plan with a proposed ordinary verification
roster. TestDesign remains a separate stage: it must independently review the
plan, freeze the witnesses and bind the provisional performance budgets before
Code. The caller requests that independent review use a different company's model.

## Problem and evidence

The production two-second deadline starts before repository discovery. Queueing
for Git, starting Git, resolving a durable root and proving ignore custody spend
the same allowance needed to write and verify the report. A healthy write can
therefore be refused before it begins. Preserve the two-second write-stall
contract; separate preparation from report-file I/O instead of enlarging it.

The CARD-1605 description names Windows qualification task `28bbb58e`, group
`all-group-78` (468 cases), with failures at 4.89 s and 5.55 s. Those are test
durations, not StoreAsync p95 measurements or proof of which preparation phase
was slow. No desktop files were accessed and that completed run was not resumed.

The cited Git path `.antiphon/task-4f836ed8.md` does not exist at refreshed branch
tip `914db0a3567ba3a4f16837052eff4698df8586df`. Its full stored task report was
read instead with `scripts/delegate.ps1 -Status 4f836ed8`. It documents the test
fixture's 30-second override, unchanged production two seconds, and the loaded
class timings reproduced in Performance budget below. Preserve this distinction:
the fixture repair did not repair production storage.

## Ground truth

Line references are at the inspected source SHA; paths are repository relative.

| Card assumption | What the code does | Design consequence |
|---|---|---|
| Two seconds bounds a report write stall. | `server/Infrastructure/Files/AgentReportStore.cs:23-39` creates and starts one linked timer before `ResolveRootAsync`; the same token reaches custody, existing-content read, write, rename and readback at lines 47-69. | Give preparation its own bounded token; start the unchanged two-second report I/O token only after custody succeeds. |
| Git already has a 15-second bound. | `server/Application/Services/GitWorkspaceService.cs:1160-1177` acquires the gate and synchronously starts the child before creating its timeout. `GitProcessGate.cs:15-29` defaults to width eight and waits only on its caller token. `GitSettings.cs:6-7` defaults to eight/15. | Merely passing the caller token to preparation could wait indefinitely. Keep a separate finite store-local preparation deadline; do not change the global Git gate or timeout. |
| Cancellation drops the file. | `AgentReportStore.cs:74-82` maps non-caller cancellation to `io-budget-expired` and removes an owned temporary file. A readback failure after rename can leave canonical bytes on disk but returns no pointer. | Require no successful pointer after deadline and no temporary residue; do not claim every failure means no canonical file exists. |
| A returned path is durable exact content. | `AgentReportStore.cs:41-69,94-109,112-180` uses full task GUID plus raw UTF-8 SHA-256, durable-root and ignore/tracking checks, sibling WriteThrough temporary write, rename and exact readback. | Preserve these checks, order, path identity, and atomic publication. No retry or root-custody cache. |
| Caller text is lost. | `AgentTaskReplyService.cs:3660-3702` falls back to legacy spill; small reports may have no spill, and task Result remains. `CardDoneArtifactPreservation.cs:41-54` refuses `artifact_unpreserved`; `AgentTaskLandNotificationService.cs:394-414` refuses `completion_report_unavailable`. | Fix canonical file preservation without changing caller settlement, cleanup admission or delivery semantics. |
| Existing tests protect the production deadline. | `tests/Antiphon.Tests/TestHelpers/ReportWorkspace.cs:44-53` supplies 30 seconds. `AgentReportStoreTests.cs:178-211` has a zero-budget row, not a slow-Git-versus-slow-write discriminator. | New witnesses must explicitly use production defaults; existing custody tests retain their purpose. |
| The 2.5-second write-stall contract can be relaxed. | `docs/superpowers/plans/2026-10-13-card-1032-s8-code-evidence.md:123-146` records the forced write stall returning `io-budget-expired`; CARD-1605 explicitly requires retaining it. | A two-second write deadline and an actual 2.5-second stall witness are acceptance requirements. |
| A fixed host is needed. | Required GETs `/api/runner-defaults` and `/api/session-runners` succeeded on 2026-10-29; defaults revision 2, no kind overrides, eligible Linux and Windows lanes. | Re-read placement at dispatch. Checkpoints name lanes; omit `-Runner`, and omit `-Platform` unless an OS witness requires it. |

## Decisions

- **D-1: separate preparation and report-file I/O.** Preparation covers root
  resolution, encoding/path construction, ignore/tracked checks and ancestor
  validation. The two-second I/O phase begins immediately before the existing-file
  content check, and covers that check, write/flush/dispose, prepublish hooks,
  rename and final exact readback. It is one shared deadline, never reset between
  those operations. This also bounds the existing-file fast path. Reject starting
  it only inside `WriteAsync`, which would leave verification unbounded.
- **D-2: bound preparation independently.** Propose a store-local internal
  `PreparationBudget`, default 30 seconds, linked to the caller token. Dispose it
  at the phase boundary; create the I/O token from the caller, not the expired
  preparation token. Thirty seconds is a provisional engineering ceiling, not a
  measured p95: it allows queue/start overhead around the existing 15-second Git
  command allowance. Measurement below must validate or narrow it before Code
  acceptance. Reject caller-only preparation (unbounded gate wait), a global Git
  timeout change (unrelated consumers), and resetting the preparation deadline
  per command (unbounded aggregate queueing).
- **D-3: preserve refusal semantics.** Keep `DefaultIoBudget == 2 seconds` and
  `io-budget-expired` for the report I/O phase. Use a distinct
  `preparation-budget-expired` for the store's preparation deadline. Caller
  cancellation takes precedence and propagates. Only the owning budget's actual
  cancellation may produce its budget reason; an unrelated thrown cancellation
  must not masquerade as timeout. No interface or persistent schema change.
- **D-4: preserve custody.** Do not skip ignore/tracking proof, weaken root or
  reparse checks, change exact byte identity, accept a fake path, or publish after
  a canceled token. Check cancellation at phase transitions, before the existing
  valid-file return, before rename and before final success. Retain owned-temp
  cleanup. Synchronous filesystem calls and Process.Start remain cooperatively
  bounded, not forcibly preemptible; do not implement detached abandoned I/O.
- **D-5: no retry or new attention workflow.** A retry multiplies Git load and
  may repeat a post-rename outcome; it does not repair the wrongly scoped timer.
  Existing warning/fallback/refusal paths remain. An attention item alone would
  surface the loss without fixing it. Do not add background workers or sessions.
- **D-6: measure distinct phases.** Extend the store's existing structured
  diagnostics with total and preparation/I/O elapsed times and fixed phase/outcome
  names, including early return and expiry. Record no report bytes, exception
  messages or secret-bearing paths. Keep detailed gate/process timing in the
  bounded verification harness unless an existing instrumentation seam suffices.
  Separate temporary cleanup time from the active-budget spans, and distinguish
  normal completion, verified reuse, preparation expiry, I/O expiry and caller
  cancellation. A phase not entered is absent, not a measured zero.
- **D-7: target both maintained lines.** The production fix must reach the active
  rc and master through separate authorized ordinary review/landing flows.
  Master publication alone is not activation. This Plan pushes only its assigned
  branch and authorizes no deployment, tag, promotion or release-gate shortcut.

These are proposed implementation decisions under the card's scope, with D-2
explicitly provisional pending measured preparation timings and independent
review. They are not evidence that the implementation or acceptance has run.

## Smallest implementation

Change `server/Infrastructure/Files/AgentReportStore.cs` only for production:
split token lifetimes, keep the current I/O default, distinguish timeout reasons,
and add bounded phase diagnostics. Reuse the injected `GitWorkspaceService` and
its existing `GitProcessGate` constructor seam for real contention witnesses.
No production changes to Git scheduling, caller services, Program registration,
report policy, retention, cleanup execution, or session state are intended.

Use two sequential linked cancellation sources. After each awaited preparation
operation and immediately before leaving preparation, check its token so a Git
wrapper's fallback result cannot turn an expired preparation into success or the
wrong root refusal. At the boundary dispose preparation, start the I/O budget
from the original caller token, and check that token before further I/O. A
separate `existing-read` phase/hook makes the fast path observable without
performing any publication. Every hook receives the token for its own phase.
Temporary cleanup remains in the owning `finally`. A readback timeout after
rename returns no pointer, although the exact canonical file can remain; a later
ordinary caller may validate that file. Do not delete it as timeout recovery.

## RED witnesses and proposed verification

All new methods below belong to
`tests/Antiphon.Tests/Application/AgentReportStoreBudgetTests.cs` unless specified.
Construct the actual store with its production defaults and an injected real
`GitWorkspaceService`/test-owned gate. Do not use `ReportWorkspace.Store()` for
deadline witnesses: its 30-second override would mask the bug. The helper may
reuse its owned path/task/expected-byte conventions.

| ID / proposed exact method | Cases | Setup and independently observable outcome | Why a stub or incorrect fix fails |
|---|---:|---|---|
| V-1 `Gate_wait_longer_than_write_budget_still_preserves_report` | 1 | Initialize scratch first; acquire the only lease of a private `GitProcessGate(1)`, start real StoreAsync, observe `Waiting == 1`, then hold for 2.5 s. Release in `finally`, await the store, require the full GUID/raw-byte hash path, exact UTF-8 bytes, no temporary residue, and zero remaining gate waiters/holders. | Current code returns `io-budget-expired` before release. The assertion requires actual readable bytes, so a manufactured success/path or skipped Git cannot pass. Require gate-entry observation and no early store completion. |
| V-2 `Slow_git_response_does_not_spend_write_budget` | 1 | Linux-only scratch executable configured through `GitSettings.ExecutableName` delays the first root query for 2.5 s, records entry, then `exec`s the resolved real Git with its original argument vector. Later queries pass through. Require actual wrapper entry, real custody checks and the same byte/path outcome as V-1. | Current store cancels the child before the root answer. A root hook alone would not prove propagation through Git. This simulates slow Git startup/first response, not a blocked kernel `Process.Start`; that limitation stays explicit. |
| V-3 `Write_stall_keeps_two_second_refusal` | 1 | With production `DefaultIoBudget`, enter `BeforeIoAsync("write")`, delay 2.5 s on its token, and record entry/completion/cancellation. Require `Path == null`, reason `io-budget-expired`, cancellation before that delay completes, absent new canonical file and no temporary residue. | Guards CARD-1032: raising the I/O budget, starting it after the hook, or ignoring the token makes the delay complete and publishes. It passes on the old implementation and must continue passing; it is not the new-regression RED. |
| V-4 `Io_deadline_is_shared_through_verification` | 2 | Arguments `publish` and `readback`. Capture the real write-phase token; consume part of its allowance, then hold the named later phase past the remaining allowance. Require the same token, cancellation at the original deadline and `io-budget-expired`, with no returned pointer. Publish case leaves no new canonical file; readback case may leave exact bytes after rename. | A fresh two seconds per phase, budget disposal before verification, or success after late readback cannot pass. Combine token identity with cancellation and file outcomes, never identity alone. |
| V-5 `Preparation_timeout_releases_gate_without_writing` | 1 | Private gate held; use a short internal preparation allowance only in this test. Require observed queue entry, `preparation-budget-expired`, zero write-phase entries, no published/temp file, and no queued waiter after completion. Release the test lease and prove a subsequent gate acquisition works. | Moving the timer without an independent queue deadline would hang; returning a generic failure misses the exact reason/phase and waiter assertions. |
| V-6 `Caller_cancellation_propagates_in_each_budget_phase` | 4 | Arguments `queued`, `write`, `publish`, `readback`; cancel the real caller after an entry handshake. Require OperationCanceledException, no result pointer, and owned-temp cleanup. For queued, observe removal from Waiting; after rename, do not require file absence. | Swallowing cancellation as either budget reason or returning success fails. Every case proves its target phase was reached. |
| V-7 `Canceled_fast_path_or_late_readback_never_returns_success` | 2 | Arguments `existing-read`, `readback`. Preseed exact empty bytes for the fast path so a zero-byte read cannot be the only cancellation guard; at the named hook cancel the caller and let the hook return normally. Require cancellation, no success and intact canonical bytes. | A hook that throws on behalf of production would mask a missing final token check. This fixture deliberately does not throw. |
| V-8 `Artifact_preservation_observes_store_budget_outcome` | 2 | Arguments `queued`, `write-stall`. Use concrete `CardDoneArtifactPreservation` with the real store and owned roots. Reuse V-1/V-3 handshakes. Queued success records a readable exact external pointer; write stall returns `artifact_unpreserved` and sets no pointer. | No mock store result or self-comparison; consumer verdict is checked against independent on-disk bytes and the task's ResultFilePath. No real cleanup runs. |
| V-9 `Budget_diagnostics_identify_phase_and_outcome` | 4 | Arguments `stored`, `reused`, `preparation-expired`, `io-expired`. Capture structured logger fields and independently time the invocation; require the expected entered phases/outcome, nonnegative measured durations, and total consistent with phase/cleanup spans within measured overhead. Inject a sentinel report body and prove it never appears in diagnostics. | Logging a constant duration/outcome, omitting early reuse, or reporting preparation timeout as I/O timeout fails. Timing assertions use tolerance, not equality with two seconds. |
| R-1 existing `AgentReportStoreTests` | 28 | Whole existing class: exact content/full IDs, all 11 root cases, five tracking/ignore cases, two ineffective-ignore cases, atomic publication/reuse/concurrency, path limit and seven failure/cancellation cases. | Preserves custody gates that a tempting fast path or cache could bypass. Do not loosen their assertions or remove the zero-budget witness. |
| R-2 existing `OutputDistillationDeliveryTests.Storage_failures_leave_a_recoverable_settled_report` and `Host_cancellation_during_storage_can_retry_settlement_once` | 8 | Seven storage failure vectors plus the caller-cancellation settlement retry. | Pins Result retention, fallback delivery and one logical queued note without adding an automatic store retry. |
| R-3 existing `VerificationRoundDeliveryTests.C544_ReportRegeneratedFromSnapshot` | 1 | Real saved completion snapshot, remove its owned report, mutate current task.Result, restart fixture and deliver. | Verifies snapshot bytes are regenerated and delivered once, not current mutable task bytes. |
| V-10 new `AgentReportStoreLoadTests.Store_samples_record_normal_and_contended_phases` | 1 | The bounded 80-store sampling design below, exact-byte/readback assertions for every sample, zero unexpected refusals, and nonzero observed gate contention in the loaded group. | A timing-only benchmark could pass while losing files. This test validates every path/content and refuses an unobserved load condition; 80 internal samples still count as one TUnit result. |

Commit/push S1 tests before the production fix. CP-1 must compile and execute
both intended methods, with two path/preservation assertion failures attributable
to `io-budget-expired`; fixture failure, child launch failure, build failure,
zero tests, or an unentered slow path is not RED evidence. V-3 is the unchanged
positive contract. After S2, execute the same two methods inside CP-2 and require
green. If the baseline does not fail as specified, investigate before changing
production. Do not make a red test green by changing its I/O allowance.

TestDesign must bind controls for timer-start placement, missing preparation
bound, raised I/O bound, per-phase timer reset, swallowed caller cancellation,
and skipped final token check. These are pending post-land Mutation obligations,
not permission for this Plan to mutate or execute code. Controls must be scoped
to one exact method per cycle and assert the intended failure.

## Slices and files

| Slice | Files | Outcome and verification |
|---|---|---|
| S1: discriminating tests first | New `tests/Antiphon.Tests/Application/AgentReportStoreBudgetTests.cs` with V-1/V-2/V-3; new `tests/Antiphon.Tests/TestHelpers/ReportBudgetWorkspace.cs` for shared seed, owned per-case clones, direct store construction and the delayed Linux Git wrapper. | No production edits. Commit/push, then CP-1 intentional RED. Keep its failure receipts at the tested SHA. V-3 participates in S2's full green roster. |
| S2: split deadlines and complete witnesses | `server/Infrastructure/Files/AgentReportStore.cs`; extend the S1 test class/helper for V-4..V-9; new `tests/Antiphon.Tests/Application/AgentReportStoreLoadTests.cs` for V-10. Update `ReportWorkspace.cs` comments only if needed to distinguish its custody-test override. Update test roster/classification inputs only through their existing census workflow if new classes require entries. | Commit/push before tests. CP-2..CP-5 require green; exact method/case roster is 46 + 8 + 1 + 1 = 56 executed results. Keep application callers and global Git behavior unchanged. Record observed timing against that source. |

The helper uses the assembly-local `ProcessSpawnLimit`, because the delayed
executable starts a shell as well as Git; `GitProcessSpawnLimit` is insufficient
for that class. Load tests cap in-method concurrency explicitly as described
below. No separate test project is justified for this small server/Git boundary;
CARD-1546 owns broader Git extraction. No session is added or changed to wait
for input, so the CARD-1083 conditional-release checklist is not triggered.

### Checkpoints

Proposed closed ordinary scope for TestDesign to freeze. All rows use the **Linux
integration lane**; the explicit Linux requirement comes from V-2's controlled
executable, not a fleet host name. Other cases are portable. Native Windows
release qualification is not performed or credited by these rows. The current
Plan runs no checkpoint. CP-1 is baseline RED only; final green acceptance is
CP-2..CP-5, including V-1/V-2 again. Each row binds one isolated build (or its
explicit same-slice reuse) and one exact filter. EstimatedMinutes are provisional
bounded scheduling estimates, not measured costs.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1605-red/` | linux-budget-red | `/*/Antiphon.Tests.Application/AgentReportStoreBudgetTests/(Gate_wait_longer_than_write_budget_still_preserves_report*)\|(Slow_git_response_does_not_spend_write_budget*)` | V-1,V-2 baseline | both listed methods; 2 intended assertion failures, retained as RED | 2 | 4 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1605-green/` | linux-budget-custody | `/*/Antiphon.Tests.Application/(AgentReportStoreTests*)\|(AgentReportStoreBudgetTests*)/*` | V-1..V-9,R-1 | all listed, 0 failed/skipped; 46 cases | 46 | 4 | true |
| CP-3 | S2 | CP-2 | linux-report-callers | `/*/Antiphon.Tests.Application/OutputDistillationDeliveryTests/(Storage_failures_leave_a_recoverable_settled_report*)\|(Host_cancellation_during_storage_can_retry_settlement_once*)` | R-2 | all listed, 0 failed/skipped; 8 cases | 8 | 2 | true |
| CP-4 | S2 | CP-2 | linux-snapshot-report | `/*/Antiphon.Tests.Application/VerificationRoundDeliveryTests/C544_ReportRegeneratedFromSnapshot` | R-3 | all listed, 0 failed/skipped; 1 case | 1 | 2 | true |
| CP-5 | S2 | CP-2 | linux-report-load | `/*/Antiphon.Tests.Application/AgentReportStoreLoadTests/Store_samples_record_normal_and_contended_phases` | V-10 | 1 executed, 0 failed/skipped; 80 byte-verified internal samples | 1 | 2 | true |

Suffix audit at the inspected SHA found only `AgentReportStoreTests` for that
class prefix; neither new class exists. Both CP-3 method prefixes and CP-4's
exact method have exactly one declaration. The new class/method names are
reserved proposals, not discovered tests. TestDesign and Code must repeat the
suffix audit after adding them, and validate the full fresh TRX roster; the Min
floor alone cannot prove selection. Every OR operand above is parenthesized and
ends in `*`; table pipes are escaped. Do not use OR between complete tree paths.

Use the checkpoint tool's `run --plan <this-plan> --after S1` for CP-1 and
`--after S2` for the final group, with exact committed source binding; import a
manifest when pinning measured timeouts (`run <manifest> --after S2`). These
selection forms are present in `tools/Antiphon.Checkpoints/Program.cs:635-681`.
Bootstrap any `dotnet run` through `scripts/build-slot.ps1`; the checkpoint row
driver acquires its own slot. Await every foreground command and keep waiting
when the tool returns 75. Record intentional CP-1 exit 1 as RED, never green.
Slot timeout 4 means not run; no unleased retry. Keep output paths with forward
slashes, reuse only a verified same-source build, and remove only task-owned
alternate outputs after all owned runs end. No full-suite rerun is justified by
this focused timer change. Ordinary release qualification remains separate.

## Performance budget

Apply [testing/build performance standards](../../testing-and-build.md#performance-standards)
and [R1-R8](../../test-performance-standards.md). This documentation stage runs no
build or test: source/API inspection is sufficient to establish the timer scope.

Historical report `4f836ed8`, source
`914db0a3567ba3a4f16837052eff4698df8586df`, Linux runner: existing class 28/28
alone in 5.072 s; five runs with eight CPU workers on 24 cores in 7.213, 5.733,
4.930, 4.952, 5.347 s. Small-sample nearest-rank p95 is 7.213 s for the **whole
class**, not StoreAsync or a particular test. The two Windows failure durations
are likewise not a loaded StoreAsync p95. No current production p95 is available.

CARD-1605 owns the bounded collection of normal and loaded per-store and per-phase
durations. Do not relabel estimates as measurements, use the fixture's 30-second
override as production evidence, or widen the protected two-second I/O deadline
to accommodate load.

### Collection, setup and attribution

V-10 performs one warmup outside the sample, then 20 stores per payload/load
bucket: 4 KiB and 64 KiB of valid UTF-8, normal sequential and loaded concurrency
16 against a private width-eight Git gate. That is 80 stores, unique full task
IDs, using one initialized owned repository and its unchanged ignore policy.
Loaded work is real repository discovery/custody and WriteThrough report writes,
not fleet-wide CPU burners or access to production reports. Start each loaded
batch together, record observed queue depth/peak and ambient host CPU/load/memory;
require contention to have actually occurred. Keep setup and warmup outside
StoreAsync timing. Every sample must return independently verified exact bytes.
Report n, p50, nearest-rank p95, max and refusals for total, preparation and I/O
in each bucket. With n=20, p95 is the 19th sorted observation: this remains a
limited controlled sample, not an estimate of production tail probability.

New tests initialize and seed a Git template once per test host, clone/copy it
into isolated owned case directories, and share it across the budget/load classes
when in one host. They require no container, database, migration, web host or real
runner. Existing CP-2 custody cases retain their small owned Git fixtures; no
unrelated fixture rewrite. CP-3/CP-4 use `TestDbFixture`'s lazy per-assembly
container/migrated template and per-case database clones. Their harness builds a
service provider, not a real AppHost; the explicit CP-4 restart is the behavior
being tested and must be timed separately. Do not start containers/migrate per
case or introduce per-case expensive seeds in the new helper.

Use the existing opt-in `TestTiming` recorder for fixture setup, clone, body,
filesystem, process spawn/wait and teardown; retain `ANTIPHON_TEST_PHASE_*` run,
group, attempt and suite identity. Retain detailed per-store phase samples under
ignored `.antiphon/checkpoints/<run-id>/` alongside TRX and source/build receipts.
Join records by actual run/test identity, source SHA, Linux lane, host, category,
payload size and concurrency; retain exact planned/executed membership. Record
Git gate admission separately from child execution where observed. Uninstrumented
CLI/filesystem subspans are unknown and remainder is explicit; do not label them
zero. Store elapsed time is wall time, not the sum of overlapping loaded calls.
Production diagnostics provide comparable preparation/I/O/outcome fields after
activation, without raw content. Windows observations must remain a separate
platform/load bucket; no extrapolated Windows p95 is claimed.

### Provisional durations and timeout policy

| Work | Expected duration before measurement | Initial bound / reason |
|---|---|---|
| V-1/V-2 and V-3 real-time witnesses | 2.5 s imposed preparation wait or about 2 s I/O expiry, plus fixture/Git overhead; provisional at most 6 s per method | 12 s per test initially; target about 2x observed comparable p95 after the first real samples. A scheduler overrun is diagnostic, never permission to widen the protected I/O bound. |
| Other new budget methods | 0.1-3 s per case, provisional | 10 s per case for phase-entry and teardown, to be narrowed from observations. Local hooks/leases must be released and all launched work joined in `finally`. |
| CP-1/CP-2 isolated builds | 1-3 min each, unmeasured here; desired ceiling about 6 min pending p95 | The scheduler has a 15 min build floor; retain it only as the explicit tooling exception below, with the independent run cap still active. No repeated builds for CP-3..CP-5. |
| CP-2 test execution | 20-40 s including new real-time witnesses; existing 28-case class historical p95 7.213 s | 90 s provisional, then about 2x measured execution p95 rounded to supported units. Existing class history cannot price the new witnesses. |
| CP-3/CP-4 integration rows | Each 30-60 s including shared startup/cloning; unknown current p95 | 2 min execution each initially. Phase collection must show container/readiness, template/migration, clone, provider setup, body and teardown separately. |
| CP-5 sample method | 30-60 s for 80 small report stores, provisional | 2 min execution initially, then about 2x measured p95. One scheduled sampling invocation; no repeated battery solely to improve the percentile. |
| Build-slot admission | Load-dependent, separate from execution | Explicit 5 min acquisition allowance per build/row. Exit 4 is not run; report admission p95 separately from execution. |
| Complete planned work | Table floor 14 min, including two isolated builds and baseline RED; final S2 group floor 10 min | Provisional 20 min charged execution and a 25 min hard wall for S2; S1 8/13 min. Collection is bounded, not an open-ended stress run. |

All new duration numbers above are provisional CARD-1605 estimates, with the
named first-row/CP-5 collection obligation. Existing imported defaults
`max(15 min, 3 * EstimatedMinutes)` are not approved performance budgets.
TestDesign must import/pin the explicit finite row/admission/total limits through
supported manifest fields before Code; do not add unsupported timeout columns
to the table. `CheckpointSpec.TimeoutMinutes` pins each row;
`Build.SlotWaitMinutes` pins admission; CLI `--total-timeout` and
`--hard-wall-timeout` pin the charged execution and independent wall caps.
`RunScheduler.BuildDeadlineMinutes` at lines 370-379 retains a 15-minute build
floor even when every row timeout is shorter. Its `--row-timeout` override changes
both builds and rows, so it cannot independently grant six minutes to a build
and two to a test row. Record this CARD-1356 tooling limitation as a provisional
CARD-1605 build-only exception, not a measured need or a reason to widen test
timeouts; retain the tighter per-test, per-row and whole-run limits. Do not add a
checkpoint-tool change to this card to hide that mismatch. If the runner exposes only
whole-minute row bounds, round up approximately 2x the measured p95 and record
that granularity. Keep historical failure/run costs; do not overwrite them.

After the first bounded sample, set test and row ceilings near 2x comparable
p95; a single new test observation is explicitly provisional, not measured p95.
Use the permitted at-most-three normal and two loaded proof repetitions only
when needed to resolve a demonstrated uncertainty, never a whole-suite repeat.
The production preparation ceiling D-2 needs a recorded p95/reference and
independent acceptance: prefer roughly 2x loaded preparation p95 with documented
allowance for the existing Git command bound. Retaining 30 s requires a stated
CARD-1605 exception if measurements do not justify it. Never alter the mandated
two-second I/O ceiling by applying the test-timeout formula to it.

The app/category is server + Git operations, Integration (R-3 also Slow).
`Antiphon.Tests` directly references `Antiphon.Server`; the store is compiled in
that server project, which references messaging, Pty, runner contracts, resilience
and observability. Those projects are dependencies, not changed consumers of
this implementation. The selected downstream behaviors are canonical storage,
settlement fallback, artifact preservation, and snapshot regeneration. No client,
messaging transport or native Pty behavior changes. Broader app partitioning and
scoped rc credit remain CARD-1545/CARD-1546 responsibilities. No planned row or run
exceeds 30 min; if observed or expected to do so, stop expanding scope and have
the caller commission Debug with exact source/selection/host/timings rather than
retrying the broad run.

## Risks and acceptance limits

- Preparation may now consume up to its own ceiling before the two-second I/O
  window. Thirty plus two seconds is a cooperative active-budget bound, not a
  hard wall guarantee for synchronous Process.Start, File.Move, directory access,
  stream disposal or temporary deletion. Test hooks prove cooperative stalls;
  hard cancellation of kernel filesystem calls is outside this fix.
- Root discovery/ignore checks contain filesystem I/O too. They remain bounded
  by preparation, not the two-second report-write window. This is an intentional
  contract clarification; retain all checks and independently review it.
- Existing-file verification intentionally consumes the I/O budget. A very large
  report or genuinely slow filesystem may still refuse, including after rename.
  That is the preserved contract, not justification for retries or wider limits.
- Git's own timeout may still return an unavailable root/custody result before
  the preparation timer expires. It is distinct from preparation expiry; preserve
  the existing conservative result and avoid broad Git error-handling changes.
- Linux controlled contention proves the scoped regression, not the original
  Windows host's p95. Subsequent rc qualification/activation must record that
  platform's behavior under the existing release policy; this task neither reads
  desktop evidence nor restarts an ended qualification.

Accept the implementation only with the intended CP-1 RED, CP-2..CP-5 green
counts/rosters and source-bound receipts, retained two-second/2.5-second refusal,
bounded preparation cancellation, measured per-store load evidence, and unchanged
caller custody/fallback outcomes. Independent ordinary Code Review still follows
implementation. Land to rc and master using their authorized pipelines; require
the active product host's version to match the intended rc source before claiming
activation. This plan itself changes no product behavior.
