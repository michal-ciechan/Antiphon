# CARD-1483 S4 Code authoring and evidence record

Original Code/landing owner: 6e80fc7e-008b-4c02-af99-8b6ff3ef8dc1.
Branch: feat/card-task-6e80fc7e. Worktree: /work/worktrees/task-6e80fc7e.
Clean fetched master/task base: eb6101b1a0eb8b7542cd631f8064c6ae0671733c.
No carried commits, rebase, reset, merge or branch switch.

Implemented D-6 recording of MSBuild evaluation filesystem probes, checking lexical
and real paths including absent files/directories. The independent second evaluation
uses a disposable repository copy: Markdown bytes replaced, root README removed,
docs/c1483-canary/canary.md added. Properties, item FullPaths and imports are compared
across project/global-property closures with repository relocation normalized.
Canary exceptions refuse. Copy writes through escaping links refuse.
Pins are unchanged and remain a prerequisite, never a sufficient approval.
Script wiring and the evaluation record remain S5. Restart: none.

Plan: docs/superpowers/plans/2026-10-23-card-1483-s4-amendment.md, following the
original 2026-10-22 plan and S2/S3 amendments. CP-4 covers the full affected
LandGraphEvaluationTests class, 30 executions. Unit and whole-suite verification
are deferred by the brief (CARD-1466), never claimed passed. No ordinary R-n rows.
V-14/V-15/V-16 and CARD-1498 are deferred to S5. CARD-1504 evaluation-time
Conditions are covered here; target-body cases retain S3 coverage.

## Red evidence

Committed tests: 21ac2df284f9ae6192973c14c4f90c76acca4970. Production is unchanged
from the clean master base. Fresh TRX roster independently inspected: every expected
method present, 30 executed, 21 passed, nine failed, none skipped. Seven failures are
actual exit-0 false approvals. Two nested Condition fixtures initially returned exit 2
because their quote delimiters were invalid; these are fixture errors, not defect proof.

CHECKPOINT CP-4 commit=21ac2df284f9ae6192973c14c4f90c76acca4970 build=ok filter=/*/*/LandGraphEvaluationTests/(C1483_*) executed=30 passed=21 failed=9 skipped=0 trx=/work/worktrees/task-6e80fc7e/.antiphon/checkpoints/20261023-001841-8549/rows/CP-4/run.trx slot=granted waited=90s dirty=0 source=21ac2df284f9ae6192973c14c4f90c76acca4970 sourceState=clean buildSource=verified

Build slot granted, waited 0s; row slot granted, waited 90s. Build phase 348.4s,
tests wall 80.4s, total run 9m14s. The red receipt is intentionally non-green.
V-1, V-2, V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10, V-13 and inherited
V-1a/V-1b/V-3a passed. V-11 all five cases showed false approval. V-12 direct
README/docs reads showed false approval; its Condition variants needed fixture repair.

Fixture repair committed separately before production changes. A slot-gated manual
run of those exact corrected Condition XML shapes against the retained, provenance-bound
S3 tool returned approved/exit 0 for docs/flag.md and %52EADME.md, expected refusal/1.
Slot 7aa366b7-efb1-4d5b-88d1-0840786b7f1a, waited 0s, held 3s. No mutant was applied.
This unlisted manual run establishes valid red witnesses for the two corrected cases
without another whole-class rebuild. The final CP-4 executes their actual tests.

Unlisted build: checkpoint-tool bootstrap through build-slot.ps1, slot
4b9edf37-68ce-42a0-adf1-3bd85de9a357, waited 0s, exit 0. Required to run CP-4.
The red CP-4 is the plan-required unlisted pre-fix run. No ungated build/test driver.

## Final qualification contract

This record is committed before the final checkpoint. Final results belong to the
fresh generated .antiphon/checkpoints/<run>/report.json and run.trx for the committed
HEAD passed as --expected-source-sha; the final caller report supplies that SHA,
unaltered CHECKPOINT line, receipt validation and merge-tree result. This authoring
record does not pre-claim the pending final run. No assertions/timeouts were weakened.

The audit script -Update regenerated the unchanged manifest digest
637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E, count 58.
The tool writes no manifest; no generated manifest was hand-edited.

RC dependency order is S1 plus repairs, then S2, then S3, then S4. S4 edits the S2
solution evaluator and uses the S3 pin gate; it is not a standalone pick onto an rc
without that chain. The requested final bundle-C artifact and the S3 Review's
.antiphon/task-4e2b240e.md were not present in the fetched refs at authoring time.
No rc mutation or activation is performed here; exact rc pick qualification remains
with the caller's reviewed bundle recipe.

All PCs remain pending in docs/superpowers/mutations/2026-10-23-card-1483-s4-candidates.md,
including every inherited S1, S2 and S3 variant. Ordinary green is not Mutation proof.

## First implementation qualification and repair

CHECKPOINT CP-4 commit=7cd0ff56f846d79a7f8c5837cfc6c3592bf577b8 build=ok filter=/*/*/LandGraphEvaluationTests/(C1483_*) executed=30 passed=27 failed=3 skipped=0 trx=/work/worktrees/task-6e80fc7e/.antiphon/checkpoints/20261023-003253-9879/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=7cd0ff56f846d79a7f8c5837cfc6c3592bf577b8 sourceState=clean buildSource=verified

Fresh TRX: 30 executions, 27 passed, three failed, none skipped. All nine V-11/V-12
negative variants passed, including their safe-file/literal-HasTrailingSlash controls.
Introduced failures were V-1a duplicate diagnostics, V-5 the inherited safe JSON glob,
and V-13 ancestor .globalconfig candidate paths differing for /work versus /tmp.
Build 353.5s, tests 99.1s, wall 8m00s; build and row slots granted, waited 0s.
These reds are retained, not attributed to the base and not relabelled green.

The repair preserves exact assertions and timeouts. Existing denial diagnostics take
precedence; glob traversal is delegated only with verified FileMatcher provenance to
D-4, with explicit Condition frames excluded; V-11 combines a JSON glob with each
unsafe explicit Exists call as a negative for that allowance. The canary copy now
shares the source parent's ancestors. No snapshot category is ignored. Full CP-4
reruns on the repaired committed report/source tip. No repeat-proof run is requested.

Focused diagnosis after the first CP-4 repair used additional slot-gated tool-only
builds and tiny fixture invocations to avoid spending another full test-project build
on a known compatibility failure. At cb02772ebe909b322c9dbc48ef96771e4ecd50b8,
the real repository approved; the JSON fixture still refused because early glob
checks enter EngineFileUtilities before FileMatcher. At the next diagnostic commit,
refusal named EngineFileUtilities plus LazyItemEvaluator.IncludeOperation. The final
exception requires the actual MSBuild assembly, that IncludeOperation frame and its
matcher/EngineFileUtilities frame, and no Condition function frame. Missing provenance
still refuses. The full CP-4 is rerun after this report and source are committed.
Generated diagnostic logs are retained under .antiphon/c1483-s4-diagnostics/.

The next diagnostic smoke caught a second, distinct call path: recursive FileMatcher
workers live in the SDK Microsoft.Build.Framework assembly and lose IncludeOperation
on their worker stack. Early checks require IncludeOperation plus EngineFileUtilities;
recursive callbacks require the exact SDK FileMatcher type/assembly identity. Explicit
Condition frames forbid the exception, and FileOrDirectoryExists never uses it. The
safe JSON plus unsafe explicit probe fixture is the negative for this compatibility
allowance. No assertion, timeout or pin was changed to obtain approval.

## Final-start infrastructure refusal (retained)

The 50f6feec40e18b9c4547892b72fd876c4685466b focused compatibility smoke passed:
JSON-only glob approved (0); the same fixture with an explicit docs/new Exists probe
refused (1). Slot a01b2a17-18fe-4d4f-9bff-0dddd4fdfdb1, waited 0s, held 3s.
The six post-failure tool-only diagnostic builds and six small diagnostic invocations
were all slot-gated; they identified ancestor discovery, early include traversal and
SDK Framework worker callbacks. They supplement, never replace, final CP-4.

The subsequent final CP-4 start did not create a run directory or launch a driver:
`CHECKPOINT owner owner-unverified`, exit 7. A direct task API read returned HTTP 502.
This is missing execution, not a passed or failed test row. Preserve its log at
.antiphon/c1483-s4-diagnostics/c1483-s4-final.log. Retry only with normal owner validation;
no owner/slot bypass, widened deadline or changed test selection is authorized.
The source range evidence checker passed (9 commits, four report entries, zero violations),
and merge-tree against the fetched master was conflict-free at this point. The final
caller report must name the eventual tested tip and qualified receipt, or leave CP-4
incomplete and route back to Code if infrastructure prevents it.
