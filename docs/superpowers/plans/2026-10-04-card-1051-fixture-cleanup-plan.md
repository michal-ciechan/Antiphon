# CARD-1051: remove read-only Git objects during evidence fixture cleanup

Plan with folded TestDesign, 2026-10-04. Complexity: **easy**. Inspected source:
`19cd9131393fa104ad2f39879b20018a08400ef8`. Code budget: **40 minutes**, plus
observed build-slot waits. Next stage: **Code**; no unresolved design decision.

Reuse `StartRefGit.DeleteDirectory(Root)` in `EvidenceGitFixture.DisposeAsync`,
retaining the existing assertion that the owned root was removed. This repairs
Windows teardown without changing evidence-policy behavior or the shared helper.

## Ground truth

| Card assumption | What the inspected code does | Plan consequence |
|---|---|---|
| Two workflow tests fail during Windows cleanup. | The card reports failures at master `53e165503` in `Workflow_calls_real_guard_and_propagates_failure` and `Workflow_uses_data_arguments_and_no_write_permissions`. Both use `EvidencePolicyWorkflowTests.AstAsync`, which disposes an `EvidenceGitFixture`. | Require both methods to execute on Windows. Planning has not independently rerun the historical failures. |
| The fixture deletes read-only objects directly. | `tests/Antiphon.Tests/Scripts/EvidenceGitFixture.cs:DisposeAsync` calls recursive `Directory.Delete` without changing attributes, then asserts root absence. `RemoveDirectoryRecursive` in the card is the runtime stack frame, not a repository helper to edit. | Replace this one deletion call, retaining the postcondition and completed `ValueTask`. |
| CARD-0694 supplied a reusable fix. | The closed card records that `RaceWorld.DisposeAsync` reuses `tests/Antiphon.Tests/TestHelpers/StartRefGit.cs:DeleteDirectory`; the current code confirms it. That helper sets every owned file's attributes to `Normal`, recursively deletes, and catches cleanup exceptions. | Add the TestHelpers import and reuse it; preserve the fixture assertion so an unsuccessful best-effort delete still fails the test. |
| The helper already has regression coverage. | `WorktreeRetirementRaceTests.C694_DeleteDirectory_RemovesReadOnlyFile` creates a nested read-only file, calls the helper and asserts root absence. It does not call `EvidenceGitFixture.DisposeAsync`. | Keep the helper test and add one fixture-level regression that detects this call-site regression. |
| All fixture repositories are self-contained. | Default creation initializes a fresh repository. Anchored creation uses `git clone --shared --no-checkout`; its alternates file refers to the source object store. Disposal is also used after failed creation. | Pass only `Root` to the helper. Do not resolve or traverse Git alternates. Retain missing-root tolerance and exercise an anchored consumer. |
| Linux success proves this fix. | The card reports both affected workflow methods passing on Linux; CARD-0694 explicitly records that read-only files can still be deleted there. | Windows is the decisive acceptance and mutation lane. A Linux pass cannot stand in for it. |

## Decisions

- **D-1: Reuse the existing helper without modifying it.** Replace the guarded
  direct delete with `StartRefGit.DeleteDirectory(Root)`; its missing-directory
  check makes the outer check unnecessary. Reject a copied attribute-clearing
  loop, a new abstraction, retries, or broad fixture cleanup refactoring: the
  existing helper already implements the needed operation.
- **D-2: Keep cleanup failure visible.** Retain the existing root-absence
  assertion and its message immediately after the helper call. Reject removing
  the assertion or catching its failure: the helper is best-effort, whereas
  this fixture promises to remove its owned root. Preserve setup-failure
  disposal and the current synchronous `ValueTask` implementation.
- **D-3: Add one behavior regression through the actual fixture.** Force a
  real locally written Git object to be read-only, explicitly dispose the
  fixture, and assert successful disposal and root absence. Reject only
  testing the helper again, inspecting source text, or relying on incidental
  Git attributes: those do not deterministically guard this call site.
- **D-4: Qualify on Windows using runtime placement.** Planning read both
  `GET /api/runner-defaults` and `GET /api/session-runners`: revision 2 selects
  a Linux default and the inventory includes an available, dispatch-eligible
  Windows runner. Code, ordinary Review and PC-1 require `-Platform Windows`;
  omit `-Runner` and re-read both routes at dispatch. No fleet address or host
  is part of this plan. Non-OS work omits platform; `-Platform Any` explicitly
  removes an inherited pin. Do not reinterpret a Linux receipt as Windows.
- **D-5: One changed behavior, one positive control.** Run the four bounded
  rows below, with one isolated test build and reuse for subsequent rows.
  Reject whole-Unit, namespace, full-assembly and full RaceWorld runs. The
  shared helper is unchanged, and representative fixture consumers cover
  default and anchored creation. No additional PCs for unchanged policy rules.

## Implementation slices

### S1: fixture cleanup and focused regression

| File | Change | Tests |
|---|---|---|
| `tests/Antiphon.Tests/Scripts/EvidenceGitFixture.cs` | Import `Antiphon.Tests.TestHelpers`; call the existing cleanup helper and retain the root-absence assertion. | V-1, R-1, R-3; PC-1. |
| `tests/Antiphon.Tests/Scripts/EvidenceGitFixtureTests.cs` (new) | Add the single test `C1051_DisposeAsync_RemovesReadOnlyGitObject` described below. Use `Category("Integration")` and the assembly-local `ParallelLimiter<ProcessSpawnLimit>` because real Git children run. | CP-1. |

Read-only reference paths: `tests/Antiphon.Tests/TestHelpers/StartRefGit.cs`,
`tests/Antiphon.Tests/Application/WorktreeRetirementRaceTests.cs`,
`tests/Antiphon.Tests/Infrastructure/EvidencePolicyWorkflowTests.cs`, and
`tests/Antiphon.Tests/Scripts/EvidenceSupplementalDeletionGuardTests.cs`.
No workflow, policy script, production source or owner-document change is needed.

Commit and push S1 before its ordinary verification. Keep source frozen while
the rows run. A repair is a new commit followed by the affected row rerun;
report its reason and counts. Do not weaken assertions or widen timeouts to pass.

## Verification design

### Behavior and regression coverage

| ID | Exact coverage | Required outcome |
|---|---|---|
| V-1 | `EvidenceGitFixtureTests.C1051_DisposeAsync_RemovesReadOnlyGitObject` | Dispose a fixture containing a deliberately read-only local Git object without exception; its entire root is absent afterward. |
| R-1 | `EvidencePolicyWorkflowTests.Workflow_selects_every_required_event`, `EvidencePolicyWorkflowTests.Workflow_pins_full_history_and_event_head`, `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure`, `EvidencePolicyWorkflowTests.Workflow_uses_data_arguments_and_no_write_permissions` | All four existing workflow checks pass, including both reported teardown failures. Preserve their current assertions. |
| R-2 | `WorktreeRetirementRaceTests.C694_DeleteDirectory_RemovesReadOnlyFile` | The existing shared-helper regression passes; no other RaceWorld methods are selected. |
| R-3 | `EvidenceSupplementalDeletionGuardTests.Verifies_later_cleanup_without_original_anchor_entries` | The existing anchored and unanchored loop completes, retaining inventory/read-only/deletion/recoverability assertions and successful disposal in both branches. This is one TUnit execution. |

Implement V-1 as follows:

1. Create the real fixture with `CreateAsync()` and keep its root for assertions.
   Within a `try/finally`, write a unique blob through `PutAsync`; derive its
   locally owned loose-object path from the returned object ID under
   `Repo/.git/objects/<first-two>/<remaining-id>`. Assert that this file exists.
   Do not choose an object borrowed through alternates.
2. Add `FileAttributes.ReadOnly` to that file's existing attributes and assert
   that the bit is set immediately before disposal. This makes the defect
   deterministic regardless of the Git installation's defaults.
3. Call the real `DisposeAsync` inside `Should.NotThrowAsync`, with the stable
   failure label `c1051-dispose-read-only`. Assert root absence afterward with
   label `c1051-owned-root-removed`. Do not clear attributes before this call.
   Call disposal a second time to retain missing-root tolerance; the root must
   stay absent. This is still one test execution.
4. In `finally`, clean up only this fixture root with the existing helper if
   residue remains. Do not dispose again there or mask the decisive failure.
   This is test-owned cleanup after the assertion, never the operation tested.

Keep D-2's postcondition as a source-review requirement. R-3 is a consumer
smoke check, not proof of arbitrary symlink/reparse-point cleanup safety.
Git alternates are text references, not child directories; do not add traversal
of their targets. No new link handling or global cleanup policy is in scope.

### Positive control

Guard inventory: **G-1**, fixture disposal removes owned read-only objects.
Guards=1, mapped=1, missing=0, duplicate maps=0. PC-1 targets the changed
fixture call site; the unchanged shared helper needs no second mutation.

| PC | Compiling mutation | Exact detecting filter | Expected red |
|---|---|---|---|
| PC-1 | In `EvidenceGitFixture.DisposeAsync`, replace only `StartRefGit.DeleteDirectory(Root)` with the original guarded recursive `Directory.Delete`. Leave the postcondition and test unchanged. | `/*/Antiphon.Tests.Scripts/EvidenceGitFixtureTests/C1051_DisposeAsync_RemovesReadOnlyGitObject` | On Windows, `c1051-dispose-read-only` fails because disposal throws on the read-only object; one executed failed test. |

After Code, independent ordinary Review and confirmed land, commission a fresh
SourceLanding Mutation task on Windows. Run this exact method green, introduce
the compiling fixture defect, rebuild and run it red, restore exact bytes,
rebuild and run it green. Each phase must execute one result with no skip;
setup/build/fixture-creation errors or zero tests do not count as red. Linux
survival is not a meaningful control for Windows read-only deletion semantics.

This is compiled C#: each changed phase needs a fresh alternate-output build.
Use `scripts/build-slot.ps1` around each build and no-build TUnit run; use
`dotnet run --project tests/Antiphon.Tests`, never `dotnet test`. Keep the exact
filter above for every phase and retain fresh TRX plus the actual assertion.
SourceLanding runs local inherited children only, commits nothing and keeps
all mutation evidence/restoration records in its assigned external evidence
root. Remove owned alternate outputs after all children exit.

### Execution and evidence

Run Code and Review on Windows. Prerequisites are the pinned SDK, Git, PowerShell
7, and the historical anchor object named by `EvidenceGitFixture.Anchor` in the
source repository. A missing prerequisite is not a green or a skip allowance.
The selected methods do not need a database, production runner session or live
provider. No new timeouts, retry policy or broad test selection are authorized.

One leased checkpoint-tool bootstrap is explicitly authorized outside the four
test rows. From the candidate checkout root:

```powershell
if (-not $IsWindows) { throw 'CARD-1051 acceptance requires Windows' }
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1051-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1051-tool/ --nologo
$plan = 'docs/superpowers/plans/2026-10-04-card-1051-fixture-cleanup-plan.md'
$tool = 'tools/Antiphon.Checkpoints/bin-c1051-tool/net9.0/Antiphon.Checkpoints.dll'
$sha = (git rev-parse HEAD).Trim()
dotnet $tool coverage --plan $plan
dotnet $tool run --plan $plan --after S1 --expected-source-sha $sha --max-wait 50s
```

The built-DLL invocation is the no-implicit-build equivalent of the prescribed
checkpoint-tool `dotnet run ... -- run --plan` entry point. Rows take their own
slots; never wrap the tool run or `run-checkpoint.ps1` in another slot. Continue
`dotnet $tool wait --run <returned-id> --max-wait 50s` after every exit 75 until
terminal. Own and await all runs before settlement. A slot timeout (exit 4)
means not run; never retry unleased or use `-NoSlot`.

Preserve complete coverage output with dispositions and the unedited CHECKPOINT
lines in the stored report. Report each CP's SHA, actual execution/passed/failed/
skipped counts and lane. Require all named methods, nonzero counts, `dirty=0`,
clean stable source and verified build provenance. Validate the generated
`report.json` with `scripts/validate-checkpoint-receipt.ps1 -Evidence <report.json>
-ExpectedSourceSha <sha> -Rows CP-1,CP-2,CP-3,CP-4`. Review repeats this ordinary
selection at its exact reviewed SHA and reports `reviewedSourceClean` truthfully.
Run `scripts/check-evidence-diff.ps1 -BaseRef <task-base> -HeadRef <pushed-sha>`
over the complete Code/Review range. Generated evidence stays ignored.

If a selected existing method fails, confirm that exact method at the recorded
base before calling it inherited; never rerun the whole assembly. Such diagnostic
builds/runs are reported separately with their reason. Remove only task-owned
`bin-c1051/` and `bin-c1051-tool/` outputs across projects after all children exit.

### Cost

Ordinary checkpoint floor: **14 minutes** (8 + 1 + 1 + 4), one test-project
build with three reuse rows. Allow **15 minutes** for implementation,
**3 minutes** for leased tool bootstrap/coverage and **8 minutes** for evidence,
review of the diff and cleanup: **40 minutes Code**, plus observed slot waits.
Every row is a single method or a four-method class, estimated at eight minutes
or less; no row hides a Unit/namespace run. Review uses the same ordinary scope.
Mutation is separately budgeted at **15 minutes** for one method's three
compiled phases, evidence and restoration, plus observed slot waits. Estimates
are not measured results. No repeat-proof run is required after green.

### Checkpoints

Closed ordinary selection: **Windows filesystem lane** for all rows. CP-1,
CP-3 and CP-4 are focused Integration methods; CP-2 is the four-method workflow
Unit class. All rows share the committed S1 source, execute serially and total
**7 TUnit results** with zero failed/skipped. Group names carry the OS lane;
the importer has no platform column, so the Windows dispatch/preflight is required.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1051/` | windows-fixture-cleanup | `/*/Antiphon.Tests.Scripts/EvidenceGitFixtureTests/C1051_DisposeAsync_RemovesReadOnlyGitObject` | V-1 | exactly 1 executed/passed, 0 failed/skipped | 1 | 8 | true |
| CP-2 | S1 | CP-1 | windows-workflow-unit | `/*/Antiphon.Tests.Infrastructure/EvidencePolicyWorkflowTests/*` | R-1 | exactly 4 executed/passed, 0 failed/skipped | 4 | 1 | true |
| CP-3 | S1 | CP-1 | windows-existing-helper | `/*/Antiphon.Tests.Application/WorktreeRetirementRaceTests/C694_DeleteDirectory_RemovesReadOnlyFile` | R-2 | exactly 1 executed/passed, 0 failed/skipped | 1 | 1 | true |
| CP-4 | S1 | CP-1 | windows-anchored-consumer | `/*/Antiphon.Tests.Scripts/EvidenceSupplementalDeletionGuardTests/Verifies_later_cleanup_without_original_anchor_entries` | R-3 | exactly 1 executed/passed, 0 failed/skipped | 1 | 4 | true |

## Publication and handoff

This Plan task commits and pushes only `feat/card-task-5ba28b37`. The caller
lands the plan immediately after this task settles successfully:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land 5ba28b37-c203-4f1f-9b8c-400b05c5f653 -ExpectedSourceSha <pushed-plan-sha>
```

Ordinary land admission requires a succeeded task (`AgentTaskLandService`),
so publication cannot be requested by this still-running Plan task. Do not push
master directly or rewrite a pushed task commit. Confirm the landing receipt,
then dispatch Code on Windows with this artifact's `### Checkpoints` pinned to
the full plan commit SHA. Folded verification is complete; no separate
TestDesign dispatch or operator decision is needed.
