# CARD-0448 continuation 3

**The preparation/retry fixes pass 52/52 affected checks; full acceptance is incomplete. Do not land or deploy.** This pass finishes the preparation-boundary
work left in flight at checkpoint `563e4b18`. The complete V/R/C/F/PC ledger remains owned by
[the continuation matrix](2026-09-08-card-0448-code-continuation-matrix.md) and
[the accepted plan](2026-09-08-card-0448-land-publication-safety-plan.md).

Worktree: `C:\Antiphon\worktrees\card-task-c86499fb`.
Branch: `feat/card-task-c86499fb`.

## Changes

- `0798caf8` checkpoints the inherited, uncommitted four-case explicit Verified retry test.
- `2ef363c0` fixes two preparation defects and adds a target-advance-intent retention companion.
  Source/target/target-checkout/filter changes at Verified can open a fresh operation only after
  an explicit request and a fresh source inspection under the repository lease. Replacement
  uses the existing atomic active-operation transaction and keeps old operations and pins.
  Operations at or after TargetAdvanceStarted retain their unresolved evidence.
- The selected verification filter is reread at source fences before publication. An older
  verifier pass cannot satisfy a newly selected filter.
- `LandingGitResult.RebaseHeadSha` captures HEAD at successful rebase command completion.
  Publication preparation and local child merge compare their subsequent inspection against
  that result. Publication also rereads task coordinates before recording Prepared. These
  close the already-written after-rebase tests for another writer's commit and task metadata.
  This is an explicit command-completion fence, not a filesystem transaction against writers
  that ignore the repository lease.
- The orchestration owner documents the new retry and preparation rules.
- `111be4f1` keeps the new unavailable-rebase-result refusal on the existing
  inspection-required recovery path, with a same-coordinate explicit-retry companion.

## Verification record

All counts below come from completed, nonzero TRX files with the expected executed methods.

- Initial `bin-c448-next` attempt failed at build copying because inherited test PID 57164
  still owned those DLLs. Zero execution credit. Its executable and command line were checked;
  it was the prior continuation's protocol regression, not a live server or runner.
- Pre-fix `bin-c448-c3` build: 0 errors, 133 warnings. It was built from `0798caf8` before the
  production changes. The RED invocation uses `--no-build` deliberately to execute that image.
- Separate fixed `bin-c448-c3-green` build: 0 errors; one new nullable warning was corrected
  before the final rebuild. The final rebuild has 0 errors and 133 warning lines, none in
  the changed source/test files.
- `continuation3-preparation-red.trx`: 14 executed, 4 passed, 10 expected assertion failures,
  0 skipped (21m17s). The failures are the four Verified retry variants, local child adoption,
  and after-rebase source advance plus all four task-metadata variants. Same-SHA branch switch,
  staged, dirty and untracked companions already passed. Every failure is a Shouldly assertion;
  the old binary's source excerpts can refer to moved current source lines, so method names,
  assertion messages, command evidence and the recorded DLL hash identify this baseline.
- PC27 explicit-replacement subvariant: unmodified baseline 1/1 passed; widening replacement
  to TargetAdvanceStarted produced 1/1 intended assertion RED. Source bytes were restored
  exactly, then rebuilt for the three affected classes. The restored control passed in the
  final 52/52 run. Its RED message is `a request cannot discard unresolved target-advance evidence`.
- The prior session's [reconciliation](2026-09-08-card-0448-continuation2-reconciliation.md),
  committed separately at `111da96a`, records its deliberate cancellation of the superseded
  inherited run during the overlap. There is no completed
  `continuation2-protocol-regression-01.trx` or verdict; the invocation has no execution credit.
  Its concurrent commit changed documentation only. This pass's production source remained
  unchanged, and its mutation control was restored byte-for-byte before the final regression.

| Completed run | Executed | Passed | Failed | Skipped | Result |
|---|---:|---:|---:|---:|---|
| `continuation3-preparation-red.trx` | 14 | 4 | 10 | 0 | Intended pre-fix assertion RED; 21m17s |
| `continuation3-pc27-before.trx` | 1 | 1 | 0 | 0 | Unmodified control baseline |
| `continuation3-pc27-red.trx` | 1 | 0 | 1 | 0 | Intended mutation assertion RED |
| `continuation3-restored-regression.trx` | 52 | 52 | 0 | 0 | Rebuilt restored GREEN; tests 24m20s, build plus run 25m18s |

The restored run contains exactly `AgentTaskLandPreparationIdentityTests` (16/16),
`AgentTaskLocalMergeSafetyTests` (9/9), and `LandingGitTests` (27/27). It includes positive
local merge, no-change cleanup and Unicode-path cases alongside the refusal cases. No client,
Pty, live-stack or full-assembly run was required by this pass's changes or claimed here.

### Exact preparation variants

Filters below use `/*/*/AgentTaskLandPreparationIdentityTests/<method>`; the parameter column
identifies the executed data row. E/P/F/S means executed/passed/failed/skipped. These are
specific variants, not completion claims for entire V or PC families.

| Method | Parameter | Pre-fix E/P/F/S | Restored E/P/F/S |
|---|---|---|---|
| `C448_V10_AfterRebaseCannotAdoptAnotherWritersPreparation` | `advance` | 1/0/1/0 | 1/1/0/0 |
| same method | `metadata` | 1/0/1/0 | 1/1/0/0 |
| same method | `metadata-path` | 1/0/1/0 | 1/1/0/0 |
| same method | `metadata-target` | 1/0/1/0 | 1/1/0/0 |
| same method | `metadata-repository` | 1/0/1/0 | 1/1/0/0 |
| same method | `same-sha-switch` | 1/1/0/0 | 1/1/0/0 |
| same method | `staged` | 1/1/0/0 | 1/1/0/0 |
| same method | `dirty` | 1/1/0/0 | 1/1/0/0 |
| same method | `untracked` | 1/1/0/0 | 1/1/0/0 |
| `C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation` | `source` | 1/0/1/0 | 1/1/0/0 |
| same method | `target` | 1/0/1/0 | 1/1/0/0 |
| same method | `target-checkout` | 1/0/1/0 | 1/1/0/0 |
| same method | `verification-filter` | 1/0/1/0 | 1/1/0/0 |
| `C448_V25_LocalMergeCannotAdoptACommitAfterItsRebase` | none | 1/0/1/0 | 1/1/0/0 |
| `C448_V15_ExplicitRepostCannotReplaceTargetAdvanceIntent` | none | New companion: separate baseline GREEN and PC27 RED above | 1/1/0/0 |
| `C448_V17_MissingRebaseResultRequiresFreshExplicitInspection` | none | Not in pre-fix binary; no RED credit | 1/1/0/0 |

The failing V10 rows assert that Prepared was never committed after another writer's change;
the V15 rows assert a fresh operation and verification; V25 asserts local merge refusal and
retained source. These assertions failed on the pre-fix executable and passed after the fix.

## Files and evidence

- `server/Application/Services/AgentTaskLandingProtocol.cs`: preparation fences, verification
  filter recheck, narrowly permitted fresh Verified operation, and inspection-required recovery.
- `server/Infrastructure/Git/LandingGit.cs` and `server/Application/Dtos/LandingDtos.cs`: the
  command-completion HEAD observation in the Git result.
- `server/Application/Services/DelegationWorktreeService.cs`: local merge consumes and checks
  that observed HEAD.
- `tests/Antiphon.Tests/Application/AgentTaskLandPreparationIdentityTests.cs` and
  `tests/Antiphon.Tests/TestHelpers/LandingGitFixture.cs`: regression cases and recorded HEAD evidence.
- `docs/orchestration-loop.md`: retry and rebase-boundary contract.

Evidence archive: `C:\src\Antiphon\.antiphon\task-3996eca6-evidence`.
It contains the four TRX files under `trx`, logs, before/after repository and committed-row
JSONL evidence, `continuation3-results.json` with every executed method, and
`continuation3-controls/manifest.json` with the mutation patch, commands, counts and SHA-256s.
The source SHA is identical before and after the mutation:
`dfbf205b6d2df41c162a92031cef876066bb790e753c0e5a6f137ddceb98094a`.
The final executed server DLL SHA-256 is
`a9609cf8802a46839558931e2298ec5815d5b266cb7f7b475f247ce42c336bdd`.
The archive also preserves the prior session's ignored preparation files under
`prior-prepared-do-not-apply-blindly`; their presence does not mean they were installed or tested.

Rerun from this branch, using a fresh result name:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c448-review/ -- --treenode-filter '/*/*/(AgentTaskLandPreparationIdentityTests*)|(AgentTaskLocalMergeSafetyTests*)|(LandingGitTests*)/*' --report-trx --report-trx-filename c448-preparation-review.trx
```

The two output directory names created by this continuation are archived and removed using
validated absolute paths and literal deletion. Inherited `bin-c448` and `bin-c448-next` outputs
are retained. The `bin-c448-review` example above creates new outputs if rerun.

## Remaining acceptance

The previous continuation report already records the 25 legacy failures' individual triage,
the 68/68 corrected legacy scope, ownership-based creation recovery, child journaling, event
consistency, and the wildcard cleanup-script fix. Those are inherited evidence, not reruns
or full-matrix completion claims from this pass.

The expanded control manifest contains only the corrected stdout-descendant PC45 control
as a completed baseline/RED/restored-GREEN triple. Its earlier stdout mutant survived and
was explicitly rejected. The separate earlier manifest contains seven completed, limited
controls (standing admission, publication count, terminal refusal atomicity, publication
monotonicity, target checkout, creation ignored content, and the wildcard cleanup script).
These are individual subvariants; they do not complete the 50 PC families. The many entries
in `.antiphon/continuation2-control-cases.json` are planned mutations, not executed evidence.

Still required: the remaining named V/R/C/F variants; independent PC-family subvariants;
the baseline incident reproduction; complete caller/barrier evidence; the per-variant ledger
refresh; and the final unmutated combined regression across the required classes. A historical
aggregate pass or a passing subset cannot satisfy these requirements. Continue Code, then
request the safety review; no landing authorization is implied by this checkpoint.

The reconciliation identifies concrete pending work: 24 prepared dispatcher admission cases;
the second OS recovery worker for C24; expanded boundary/change variants; and installed
`BeforeRebaseIntent`/`BeforePushIntent` selectors whose fixture callbacks are not implemented.
The two alternative preparation/retry patch scripts are obsolete and must not overwrite this
implementation. Adapt the pending policy/admission installers rather than running them blindly.
Continue with a single production-code and mutation-test owner, or isolated checkouts.
