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

## Verification design

TestDesign, 2026-10-20. This section appends verification obligations; D-1 through
D-4 and S1/S2 above remain the fix design. Fetched `origin/master` again:
`706d99b8a6eb624ddb1bda535b015205b2c0d50e`. The assigned start,
`530e547f2d4404c747b070148536a6c314e423f3`, differs only by this plan.
Coordinates below therefore include CARD-1356 and CARD-1370. No builds, tests,
or mutations were executed by TestDesign.

### Inspection

- `CheckpointSourceExecutionTests.cs`: bodies of
  `command_only_selection_beside_built_row_receipt_passes_both_validators`,
  `host_written_entries_under_the_row_output_are_adopted_and_cleaned_on_green`,
  and their runtime/report helpers | nearest executor fixture -> V-1/R-1;
  the command-first, single-SHA case does not cover the two-commit defect.
- `CheckpointTestSupport.cs`: `FakeDriver`, `WriteResults`, `MarkRun`,
  `AfterFileClock`; `CheckpointFixtureProcess.cs`: `Git`/bounded process runner;
  `CheckpointTestScope.cs`: `CheckpointTestBase` and teardown |
  fixture ownership, fake-driver calls, valid TRX and Git observations -> V-1/V-2.
- `CheckpointStatePublicationTests.cs`: executor/readback/wait portion of the
  publication fixture, including `ExecutorOwnershipStore.Write` with a dead
  identity; `WaitCommandTests.cs`: completed/foreign receipt and finished-executor
  bodies | use the dead-identity seam after joining execution -> V-1/V-2.
  Synthetic death does not qualify real process death or crash recovery.
- `CheckpointApp.cs`: `CreateRun`, `ExecuteCoreAsync` selection, normal inventory,
  cleanup/final publication and `BuildReport`; `RunScheduler.cs`: selection and
  `BuildOneAsync`; `OutputCleanup.cs`: discovery and created-path cleanup |
  production inventory and custody boundary -> V-1/R-1, G-1 through G-4.
- `SourceSnapshot.cs`: capture; `SourceRunGuard.cs`: observation and build binding;
  `BuildStep.cs`: property/layout arguments; `ReportWriter.cs`:
  `RequiredReceiptRefusal`; `ReportValidator.cs`: source/row qualification;
  `WaitCommand.cs`: terminal receipt readback | V-1/V-2/R-2, G-5 through G-7.
- `PlanTableImporter.cs`: section/header/filter/build-reuse parsing;
  `docs/testing-and-build.md`: checkpoint schema, slots, classification and
  mutation cycle; `docs/mechanical-pc-contract.md`: binding requirements |
  manifest/cost below. Publication retry, deadlines and process transport are
  inspected prerequisites, excluded from new acceptance claims.

All code paths above are relative to `tools/Antiphon.Checkpoints/` or
`tests/Antiphon.Tests/Checkpoints/`. New tests belong only in the planned
`CheckpointSelectedOutputTests.cs`. Classify it `[Category("Integration")]` and
`[ParallelLimiter<ProcessSpawnLimit>]`, deriving from `CheckpointTestBase`.
Do not inherit `Slow` merely because the much larger neighboring class has it;
if actual timing warrants `Slow`, register the new fully qualified class in
`tests/Antiphon.Tests/slow-tests-allowlist.txt` in the same slice.

### Delivery inventory

No new or changed asynchronous notification, queue, recipient session, or
recovery path: D-1 changes the contents of an existing local receipt. Real-queue
busy/already-eligible recipients, enqueue/crash handoffs and complete UserPrompt
transcripts are therefore not applicable; this plan claims no session delivery.

The relevant existing evidence path is
`CreateRun -> ExecuteAsync/RunScheduler -> report.json + report.md + state.json
-> WaitCommand.WaitAsync`, joined by the run directory/RunId, observed commit,
fingerprint and selected build/output identities. The observable result is the
actual wait exit and named receipt diagnostics, plus qualified selected-row
evidence. A successful row, written report, or terminal flag alone is insufficient.
Recovery by rereading a terminal run is exercised; crash/publication recovery
itself is unchanged and excluded.

Substitutes: `FakeDriver` supplies compiler success and a minimal passing TRX;
it does not prove MSBuild or a nested real TUnit host. `FixedSlotClient("off")`
is confined to the scratch fixture and does not prove broker admission; outer
checkpoint builds/tests still use the host slot gate. The dead executor identity
substitutes only for liveness, not receipt generation or validation. Real scratch
Git, filesystem output/stamps, executor publication and the wait reader remain
in the test path. No hand-authored report inventory or session receipt substitute
is permitted.

### Proves it works now

- **V-1:** a later selection ignores retained outputs from the earlier commit
  while retaining all its own output provenance | portable executor integration |
  `CheckpointSelectedOutputTests.selected_run_excludes_earlier_commit_unused_outputs`
  via CP-2 (CP-1 is its pre-fix witness) | both argument results green.
- **V-2:** selected retained outputs still require a present stamp with the
  matching commit and fingerprint | portable executor-to-wait integration |
  `CheckpointSelectedOutputTests.selected_retained_output_stamp_mismatch_is_still_refused`
  via CP-3 | healthy control returns 0; each independent defect returns 2 with
  the exact selected stamp path and existing refusal prefix.

**Recipe W / case W-retain and W-clean.** Implement V-1 as the one
`[Arguments(true)]`/`[Arguments(false)]` method already named by the plan.
In a `TempDir()` scratch repository, commit the project fixture, `.gitignore`
and selection marker at A using bounded `CheckpointFixtureProcess.Git`.
Ignore all `bin-*/`, fixture results and scope-marker paths before capture;
set scratch-local Git author configuration. Keep both build definitions throughout.
Run only A, retain it, and save A's stamp and sentinel bytes. Then commit only the
selection marker at B and run only B, passing `ExpectedSourceSha` from actual
`rev-parse HEAD`. Use default `SourceSnapshot` capture for both runs; assert clean,
known A/B observations with different commits/fingerprints.

Make B produce two matching owned directories, its entry project output and a
dependency fixture output with the same B build ID. In the fake build callback,
create/seal the latter through `SafeWrite.CreateFreshDirectory`,
`CheckpointBuildBinding.Expected(...).Write(...)`, and the existing owned-write
helpers; leave the primary stamp to the scheduler. Record both expected paths
independently of the production discovery list. This is an output-discovery
fixture, not a claim about real compiler layout. Write the sentinel during A's
build and seal it with the output; use `AfterFileClock` where needed for the
driver-observed exit. The normal cleanup case must remove both B directories.

**Oracle/reachability:** await both `ExecuteAsync` calls. Before each wait,
write the fixture dead identity using the inspected
`ExecutorOwnershipStore.Write(run, current with { Pid = int.MaxValue,
StartUtcTicks = 1 })` seam; never alter report/state source facts or
`OutputPaths`. Call the real `WaitCommand.WaitAsync` with expected B SHA,
five-second max wait and a bounded cancellation token. No tool shadow copy is
needed for these in-process runs. Treat 75, 6, fixture exceptions and zero
selection as invalid evidence.

Capture the executor result, terminal state, wait result/text and report before
asserting. Order decisive assertions as follows: W-retain first asserts
`waitExit.ShouldBe(0, "selected-retained-wait")`; W-clean first asserts
`executeExit.ShouldBe(0, "selected-cleanup-exit")`. Then require all three
executor/state/wait exits to be zero, `phase=done`, qualified B row evidence,
and an empty cleanup refusal list. Assert the exact unordered two-path B set
(`selected-output-set`), B-only `OutputNames`, A's `unused` diagnostic, and no
A build/row driver calls. Finally assert A's directory still exists
(`earlier-output-present`), and byte equality for its stamp
(`earlier-stamp-unchanged`) and sentinel. For W-clean assert both B directories
absent and `CleanedOutputs=true`; for W-retain require their valid B stamps.
Log the nested report and wait text into test output for the outer TRX.

**Classification:** CP-1 is an intentional regression witness: W-retain must
fail `selected-retained-wait` with actual 2 and
`receipt-source-mismatching: <A stamp>`; W-clean must fail
`selected-cleanup-exit` with actual 2 and `still-present`. Both selected fake
rows must have passed; prerequisite failures do not establish the regression.
CP-2 is the same two-case acceptance run after D-1.

**Recipe S / cases S-healthy, S-commit, S-fingerprint, S-missing.** Implement
V-2 as one TUnit method with these internal scenarios, not four counted results.
Produce one healthy retained selected-output report through the executor, use
the same real-source/dead-identity/finite-wait recipe, and prove wait returns 0.
Save the selected stamp bytes. In order, change only its commit to a different
valid fixture Git SHA; restore; change only its fingerprint to a different
valid 64-hex value; restore; remove only the stamp; restore in `finally`.
Do not edit either report or terminal state. Recheck healthy wait after each
restoration. The mismatch cases require
`receipt-source-mismatching: <selected stamp>`; missing requires
`receipt-missing: <selected stamp>`. Label the decisive `waitExit.ShouldBe(2)`
assertions `selected-commit-refused`, `selected-fingerprint-refused` and
`selected-missing-refused`. Tampering happens after executor completion, so
build admission and row binding checks cannot mask the retained-output reader.
Classification: preservation, already supported before the fix.

### Guards the regression

- **R-1:** the old all-manifest inventory must not return, lose a selected
  directory, restamp A, or reclaim A | V-1's exact method, both arguments |
  wait/cleanup zero, exact B path set, A directory and original bytes retained.
- **R-2:** correcting attribution must not weaken selected stamp validation |
  V-2's exact method | all three independent bad-stamp cases return 2 at their
  labeled assertions after a healthy zero-exit control.

### Guard inventory

These are the safety-critical guards at the changed inventory boundary and the
explicit D-2 preservation obligations. Independently bypassable predicates are
separate even when one method covers them.

- G-1: D-1, only selected non-command build IDs contribute receipt outputs
  (and hence the remaining-output check) | PC-1.
- G-2: D-1, every matching selected output remains inventoried; selection is not
  permission to truncate discovery | PC-2.
- G-3: D-2, earlier unselected output stamps remain byte-identical | PC-3.
- G-4: D-2, cleanup never gains authority over earlier unselected outputs | PC-4.
- G-5: D-2, a missing selected retained stamp refuses | PC-5.
- G-6: D-2, a selected retained stamp with the wrong commit refuses | PC-6.
- G-7: D-2, a selected retained stamp with the wrong fingerprint refuses | PC-7.

Scheduler launch selection, source capture, receipt digest/shape, cleanup
containment/ledger internals, executor death and publication guards are unchanged
prerequisites, not requalified by this repair. Driver-call and diagnostic checks
establish fixture reachability; they do not expand this card into a scheduler or
cleanup safety audit. No in-scope guard is omitted or declared untested.

### Positive controls

All recipes below are **binding required after Code**, classification
`higher-tier-required` (`binding-missing`/`patch-unresolved`), not mechanical
execution packs. Bind unique literal payloads, allowed file sets, preimage and
mutant hashes, exact assertion locations and expanded rosters at confirmed
landed L under `docs/mechanical-pc-contract.md`. Code runs V/R; Review judges
these obligations before land; Mutation runs every PC after land. Do not claim
PC execution at TestDesign or ordinary Code.

Each PC has one variant. PC-1 through PC-4 select only
`/*/*/CheckpointSelectedOutputTests/selected_run_excludes_earlier_commit_unused_outputs*`
with Min=2; PC-5 through PC-7 select only
`/*/*/CheckpointSelectedOutputTests/selected_retained_output_stamp_mismatch_is_still_refused*`
with Min=1.

- **PC-1 / all-builds:** break G-1 in `CheckpointApp.ExecuteCoreAsync` by removing
  only the new selected-build predicate from the normal `OutputPaths`
  enumeration. Both W cases go red at the CP-1 assertions above (2 executed,
  2 failed, 0 passed/skipped). A's genuinely older stamp and still-existing
  directory defeat accidental equivalence; B's rows remain green.
- **PC-2 / truncated-discovery:** break G-2 in that enumeration by inserting
  `.Take(1)` after its distinct path projection. Both W cases go red at
  `selected-output-set` (one B path instead of two; 2 failed). One valid B path
  remains, so receipt shape and retained-stamp validation permit the test to
  reach this assertion; cleanup still uses the complete created-path inventory.
- **PC-3 / restamp-unused:** break G-3 by inserting, after normal inventory
  construction in `ExecuteCoreAsync`, a loop over existing outputs of unused
  manifest builds. Deserialize each existing `CheckpointBuildBinding`, replace
  only `Commit`/`Fingerprint` with `source.Start` values and write it back.
  This deliberate raw-file overwrite models the forbidden restamping workaround.
  Both W cases go red at `earlier-stamp-unchanged` (2 failed). Keep selected
  inventory and cleanup unchanged so B completes and A remains present.
  Bind only this one production-file insertion; no test edit or writer bypass
  elsewhere is part of the control.
- **PC-4 / broaden-cleanup-authority:** break G-4 by replacing only
  `createdPaths: state.CreatedOutputPaths` in the normal `CleanOwnedOutputs`
  call with discovery of all manifest build IDs. W-clean goes red at
  `earlier-output-present` (false instead of true); W-retain stays green:
  2 executed, 1 failed, 1 passed, 0 skipped. A and B run in the same fixture
  process, so A's valid creation records and sealed sentinel still exist;
  the unchanged ledger cannot mask this expanded run-level authority.
- **PC-5 / skip-missing:** break G-5 in
  `ReportWriter.RequiredReceiptRefusal`'s output loop by changing only the
  missing-stamp branch from `return "receipt-missing: " + path` to `continue`.
  Recipe S goes red at `selected-missing-refused` (0 instead of 2;
  1 executed/failed, 0 passed/skipped). Earlier wrong-commit/fingerprint cases
  still refuse; valid report/state bindings and absent tool copy permit zero.
- **PC-6 / ignore-commit:** break G-6 in the same output loop by removing only
  `binding.Commit != report.Commit` from the disjunction. Recipe S goes red at
  `selected-commit-refused` (0 instead of 2; 1 failed). The stamp is present,
  parseable and fingerprint-correct; tampering after execution bypasses no
  unrelated guard and does not alter the receipt digest.
- **PC-7 / ignore-fingerprint:** break G-7 there by removing only
  `binding.Fingerprint != report.Source.Start.Fingerprint`. Recipe S goes red
  at `selected-fingerprint-refused` (0 instead of 2; 1 failed). The commit-only
  case still refuses and is restored before this case, whose commit is correct.

For every variant run fresh baseline -> compiling defect/intended red ->
exact restoration -> fresh green, method-scoped in all phases. Baseline and
restored green require respectively 2/2 or 1/1 passing results with no skips.
All controls share files or methods; run separately, with no combined mutation.
Use the external copied checkpoint driver and distinct phase output/evidence
paths prescribed by the testing owner. Build/fixture errors, extra failures,
zero results and surviving mutants are not intended red. Preserve evidence and
restoration outside the SourceLanding snapshot; never commit mutations.

Design audit: **guards=7, mapped=7, missing=0, duplicate PC maps=0**.
All 7 PCs/7 variants are mandatory runs for post-land Mutation; execution is
pending (0/7 run at TestDesign). An execution-complete handoff must account for
all 7 red/restore/green cycles; the present handoff is to Code.

### Out of scope

- Historical receipt repair, schema changes, CARD-1356 deadlines, CARD-1370
  publication/crash recovery: D-2/D-3 leave their implementations unchanged.
- Real queue/session delivery, live runners, deployment and RC activation:
  no corresponding path changes; master-only.
- Real MSBuild layout/confinement, OS process-death qualification, broad cleanup
  or scheduler batteries: substitutes and unchanged boundaries are declared
  above. No whole-Unit, class, namespace or assembly sweep is authorized.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1297-red/` | selected-output-witness | `/*/*/CheckpointSelectedOutputTests/selected_run_excludes_earlier_commit_unused_outputs*` | V-1, R-1 (pre-fix) | exactly 2 executed, 2 intended failures, 0 passed/skipped; W-retain/W-clean assertions above | 2 | 7 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1297-green/` | selected-output-fixed | `/*/*/CheckpointSelectedOutputTests/selected_run_excludes_earlier_commit_unused_outputs*` | V-1, R-1 | exactly 2 executed, 2 passed, 0 failed/skipped | 2 | 7 |
| CP-3 | S2 | CP-2 | selected-stamp-preserved | `/*/*/CheckpointSelectedOutputTests/selected_retained_output_stamp_mismatch_is_still_refused*` | V-2, R-2 | exactly 1 executed, 1 passed, 0 failed/skipped | 1 | 1 |

This is the closed ordinary list: two builds, three exact method-filter
invocations, five TUnit results across the two commits (two expected red, then
three green). CP-3 reuses CP-2's S2 build; use `--serial` to keep method runs
sequential. The importer does not turn CP-1's prose into a green result: retain
its actual exit 1 and both failing TRX assertions, not a fabricated success.

Commit/push S1 first. Bootstrap the checkpoint tool through
`scripts/build-slot.ps1` into `bin-c1297-tool-s1/`, then use
`run --plan docs/superpowers/plans/2026-10-20-card-1297-selected-build-output-provenance-plan.md
--after S1 --serial --expected-source-sha <S1-full-sha>`. After committed/pushed
S2, use a fresh bootstrap output `bin-c1297-tool-s2/` and the same command with
`--after S2` and S2's SHA. Both bootstrap builds are declared setup, not extra
test selections; use the tool's `--no-build` invocation for subsequent waits.
Await each run until not 75; do not keep the bootstrap slot leased across
executor builds. Preserve actual CHECKPOINT lines, counts, failure details,
source/slot provenance and ignored artifacts. Run the evidence-diff guard over
the recorded Code base through its final pushed SHA. Remove only owned alternate
outputs through guarded cleanup after retaining evidence.

### Cost

All numbers are estimates, not measured test results.

- Ordinary V/R floor (Code) = **7 + 7 + 1 = 15 minutes**, exactly CP-1/CP-2/CP-3
  and their filters above. This includes two six-minute isolated builds and
  three one-minute method runs. Tool bootstrap/setup adds **2 minutes**;
  ordinary execution budget = **17 minutes**, plus **20-30 minutes authoring**.
- PC floor (Mutation): each of PC-1 through PC-7 uses its exact filter above.
  Budget **7 baseline + 7 red + 1 restoration/inspection + 7 restored-green =
  22 minutes per PC**, including the fresh build in every seven-minute phase.
  All seven cost **154 minutes**, plus **2 minutes driver/evidence setup =
  156 minutes**. Binding/discovery/reporting adds an estimated **15 minutes**
  separately; no batching savings are assumed.
- Total execution/setup floor = **17 + 156 = 173 minutes** across Code and
  Mutation. Including authoring and binding/reporting gives **208-218 minutes**.
  Reusing CP-2 for CP-3 saves **one six-minute build** versus rebuilding each
  ordinary row (21 -> 15 minutes ordinary V/R). The three stamp defects share
  one preservation method/run; they remain three independently mapped PCs.
  TestDesign itself adds no build/test execution cost.
