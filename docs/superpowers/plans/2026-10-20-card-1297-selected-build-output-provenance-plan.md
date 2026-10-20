# CARD-1297: scope checkpoint output provenance to selected builds

Plan, 2026-10-20. Target: **master only**. Next stage: **TestDesign**.
The brief does not fold TestDesign into Plan; the executable verification manifest
must be added by that stage before Code starts.

## Current-master finding

Fetched `origin/master` and inspected
`706d99b8a6eb624ddb1bda535b015205b2c0d50e`, also this task's clean starting HEAD.
**The defect remains.** This is source-path verification, not a new executed
reproduction; no builds or tests were run during Plan.

CARD-1370's state-publication repair and subsequent terminal-settlement fixes are
present, including `3a7ed451668051b3627b8802ed9005e7bf3db327` and
`368262d6c8e995246d2ebea899cf4c3f5f0e0e17`. CARD-1356's build-deadline change is present at
`24e071faa135e28176bb28a38e82dee1743811d8`. Neither changes the defective output
inventory: `CheckpointApp.ExecuteCoreAsync` still discovers outputs for every
manifest build, while `ReportWriter.RequiredReceiptRefusal` requires every listed
retained output's stamp to match the current run.

The card's historical incident is independently recorded in
[CARD-1296's evidence](../evidence/2026-10-15-card-1296-d2-doc-guard.md):
the selected CP-3 passed 405/405 at
`41f4942073f41295e38a4e52866e41d5751581c1`, but `wait` rejected an unused output
from `c78599926845853177365a75670d60850421b064`. Those are historical results,
not verification of this plan or a future repair.

## Ground truth

Coordinates below refer to the inspected master SHA.

| Card assumption | What current code does |
|---|---|
| A selected run executes only its requested build groups. | `tools/Antiphon.Checkpoints/CheckpointApp.cs:143` selects request rows; `Execution/RunScheduler.cs:66-69` derives needed build IDs from non-command rows and marks other builds `unused`. |
| The unused earlier output enters the second report. | `CheckpointApp.cs:408-411` enumerates `MatchingIsolatedDirectories` for **all** `manifest.Builds`. `Cleanup/OutputCleanup.cs:116` discovers by isolated directory name, independently of whether that build ran. |
| The report already knows which builds were unused. | `CheckpointApp.cs:824-826` excludes `unused` builds from `OutputNames`, but applies no equivalent selection to `OutputPaths`. `ReportModel.Builds` retains all build progress, including `unused`. |
| `wait` wrongly treats the old output as current provenance. | `Report/ReportWriter.cs:116-124` checks every `OutputPaths` stamp when exit is zero and outputs are retained. It compares commit and fingerprint. `Commands/WaitCommand.cs:79-86` calls it after terminal state and executor death and returns exit 2 on refusal. |
| Passing selected-row validation should imply `wait` succeeds. | These are different checks. `Report/ReportValidator.cs:25-60` qualifies selected rows and their source evidence; the retained-output check belongs to `RequiredReceiptRefusal`. Historical validator success therefore does not disprove the wait defect. |
| Deleting unused output would solve the problem. | `Cleanup/OutputCleanup.cs:35-110` deletes only current-process recorded paths under its existing custody checks. An earlier run's output is not this run's deletion authority. Also, `CheckpointApp.cs:472-487` uses the same overbroad output list for its remaining-output check when `KeepOutputs` is false. |
| Existing tests cover this sequence. | `CheckpointSourceExecutionTests.command_only_selection_beside_built_row_receipt_passes_both_validators` runs its command-only selection **before** any built output exists, at one synthetic SHA. It does not exercise retained output across two commits followed by a different selection. |

## Decisions

- **D-1 — fix inventory construction.** In
  `tools/Antiphon.Checkpoints/CheckpointApp.cs`, restrict the build enumeration
  that populates `model.OutputPaths` to build IDs referenced by the already
  selected non-command rows. Use the scheduler's same ordinal ID semantics.
  Keep discovery of every matching directory for those IDs, including referenced
  project outputs. Preserve the all-build diagnostic list and existing
  `OutputNames` behavior. This corrects the producer's false attribution and also
  prevents unused outputs from poisoning the remaining-output check.
  Rejected: filtering by stamp SHA (would hide a genuinely wrong selected stamp),
  taking only `CreatedOutputPaths` (could conceal unexpected selected-output
  residue), and removing all output validation in the reader.
- **D-2 — preserve selected-output refusals and custody.** Leave
  `ReportWriter.RequiredReceiptRefusal`, receipt schemas, source qualification,
  build admission, and cleanup authority unchanged. A selected retained output
  with a missing stamp, wrong commit, or wrong fingerprint still refuses.
  Unselected earlier output remains on disk with identical contents; it is
  neither restamped nor deleted. Rejected: deleting the earlier build, treating
  every old stamp as acceptable, and changing `CleanedOutputs` to bypass checks.
- **D-3 — bounded scope and compatibility.** Produce corrected new receipts;
  do not rewrite historical receipts or add schema migration/legacy repair.
  Preserve CARD-1370 publication/settlement and CARD-1356 deadline logic.
  This card changes no agent session or session input-wait policy. No deployment,
  RC publication, or product-host restart is part of this master-only repair.
- **D-4 — placement.** Read `GET /api/runner-defaults` and
  `GET /api/session-runners` during Plan: defaults revision 2 and eligible Linux
  and Windows capacity were advertised. Re-read at dispatch. The proposed
  checkpoint lane is **portable checkpoint integration**, using scratch Git and
  existing fake build/test drivers; it needs no specific OS or host.
  Omit `-Runner` and `-Platform`. Do not encode the observed fleet location.

These are implementation decisions, with no unresolved operator choice or
assumption requiring a `decide` handoff.

## Slices

| Slice | Files and change | Required evidence |
|---|---|---|
| S1 — witness and guard | Add `tests/Antiphon.Tests/Checkpoints/CheckpointSelectedOutputTests.cs`, deriving from `CheckpointTestBase`, with the existing Integration/process-spawn limiter conventions. Reuse `CheckpointFixtureProcess.Git`, `FakeDriver`, `CheckpointFixtures.WriteResults`, and owned receipt helpers. Add the two methods described below. Do not change production code. | Commit and push S1, then one exact-method expected-red witness invocation, two parameterized results. A build, fixture, ownership, or zero-selection failure is not the expected red. |
| S2 — selected output inventory | Change only the normal output-discovery enumeration in `tools/Antiphon.Checkpoints/CheckpointApp.cs` per D-1. No changes to `ReportWriter.cs`, scheduler, state store, cleanup implementation, or schema are expected. | Commit and push S2 before a fresh isolated green build. Run the witness once and the selected-stamp guard once, reusing that same committed build for the second method. Report counts and source SHAs separately from S1. |

## TestDesign handoff

Keep the ordinary scope to these two behaviors. Add the required
`## Verification design`, exact `### Checkpoints` table, counts, costs and
method-scoped positive-control obligations here, then hand off to Code.
No whole-Unit, namespace, assembly, or broad class sweep is authorized.

**Witness method:**
`CheckpointSelectedOutputTests.selected_run_excludes_earlier_commit_unused_outputs`.
Use `[Arguments(true)]` and `[Arguments(false)]` for the second run's
`KeepOutputs`, yielding two TUnit results. In an owned scratch Git repository,
commit a small fixture at A and run only group A with retained outputs. Commit a
selection-only fixture change at B, retaining both manifest build definitions,
then run only group B with a distinct fresh isolated output. Obtain actual
commit/fingerprint observations from the fixture Git repository rather than
assigning invented SHAs. Generated outputs and evidence must be ignored there.
The production `CreateRun`/`ExecuteAsync` path must create the report; do not
handcraft the very `OutputPaths` list under test.

The fake driver writes minimal valid TRX for each selected row. Assert run B
launches no build or row from group A, records A as unused, records only B's
output paths/names, and preserves A's stamp and sentinel bytes. For retained
outputs, require B's executor, terminal state and actual `WaitCommand` to return
zero at expected SHA B with verified row provenance. For normal cleanup, require
zero with B removed and A still present. The current implementation should fail
the retained case with `receipt-source-mismatching` naming A, and the cleanup
case with `still-present`; order assertions to expose those behavioral verdicts
before secondary inventory assertions.

Use existing fixture executor-ownership seams to represent a dead fixture
executor before waiting, with a finite wait bound. In-process `ExecuteAsync`
otherwise records the still-live test process. Do not kill the test host, use a
production session runner, or treat exit 75/6 as the defect. Own and await any
fixture child. The two commits belong only to the scratch fixture, never to
extra branches in the assigned worktree.

**Preservation method:**
`CheckpointSelectedOutputTests.selected_retained_output_stamp_mismatch_is_still_refused`.
Produce a healthy selected-output report through the executor, establish the
zero-exit control, then independently replace that selected stamp's commit,
replace its fingerprint, and remove the stamp. Each negative must make the
actual wait path return 2 with its existing named missing/mismatch refusal
identifying the selected output. Restore fixture bytes between cases or use
independent fixtures. Keep this as one TUnit method with internal scenarios,
not three invented executed counts. This is existing behavior to preserve;
do not manufacture a pre-fix regression for it. TestDesign should bind a
post-land method-scoped control that bypasses the selected stamp check.

Proposed checkpoint allocation for TestDesign:

| Run | After | Lane | Exact-method selection | Output and expected count |
|---|---|---|---|---|
| Expected red | S1 | Portable checkpoint integration | `/*/*/CheckpointSelectedOutputTests/selected_run_excludes_earlier_commit_unused_outputs*` | Fresh `bin-c1297-red/`; 2 executed, 2 intended assertion failures. |
| Narrow green | S2 | Portable checkpoint integration | Same witness method, written out in the final manifest. | Fresh `bin-c1297-green/`; 2 passed, 0 failed/skipped. |
| Preservation green | S2 | Portable checkpoint integration | `/*/*/CheckpointSelectedOutputTests/selected_retained_output_stamp_mismatch_is_still_refused*` | Reuse S2 green build; 1 passed, 0 failed/skipped. |

Trailing wildcards accommodate TUnit argument-expanded names; qualify actual
roster/counts. Use the checkpoint tool once per committed slice group and await
every run until its exit is not 75. Gate its bootstrap through
`scripts/build-slot.ps1`; checkpoint drivers take their own slots. Keep distinct
red/green outputs, preserve ignored receipts, and remove task-owned alternate
outputs through the existing guarded cleanup. Never silently widen a filter or
run unleased after a slot timeout. Code/Review run the evidence-diff guard.

Estimate for TestDesign to refine: 5-10 minutes design; Code 20-30 minutes
authoring plus about 12-20 minutes for the two isolated build groups and three
method-filter invocations. Plan spent no build/test time. The guard's internal
negative scenarios are not additional build runs.
