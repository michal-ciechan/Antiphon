# CARD-0448 continuation 3

**Incomplete acceptance; do not land or deploy.** This pass finishes the preparation-boundary
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

## Verification record

Final execution counts and artifacts will be recorded here before handoff. No pending run is
treated as passing evidence.

- Initial `bin-c448-next` attempt failed at build copying because inherited test PID 57164
  still owned those DLLs. Zero execution credit. Its executable and command line were checked;
  it was the prior continuation's protocol regression, not a live server or runner.
- Pre-fix `bin-c448-c3` build: 0 errors, 133 warnings. It was built from `0798caf8` before the
  production changes. The RED invocation uses `--no-build` deliberately to execute that image.
- Separate fixed `bin-c448-c3-green` build: 0 errors; one new nullable warning was corrected
  before the final rebuild.

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
