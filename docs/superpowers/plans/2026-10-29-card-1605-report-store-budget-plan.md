# CARD-1605: report storage budgets under contention

Plan date: 2026-10-29. Source inspected:
`f8bddb6efda7b0e8f50f3b45744087e52188bc66`. Task: `d8d36ebe`.
Scope: read-only investigation and this plan; no product changes or test execution.
This first planning slice records the verified cause and design decisions; the
next slice completes the witness roster, checkpoints and performance collection.

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
to accommodate load. The checkpoint/per-phase budgets and collection roster are
completed in the next planning slice before handoff.

## Remaining planning work

Bind RED-first tests to concrete methods and independent oracles, audit matching
class/method suffixes, list isolated checkpoint builds and lanes, and specify
shared fixture lifetime, bounded load samples and approximately 2x measured-p95
test timeouts. Verification remains a separate TestDesign stage; the caller also
requests independent plan review by a model from a different company.
