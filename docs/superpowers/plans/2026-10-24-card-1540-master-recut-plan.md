# CARD-1540 master re-cut verification

Code task ae7dd371-1ed4-4edd-8285-ff1cdd04bdc7, original Code/landing owner
6c903205-b7d5-4bfe-8cf1-e9486eacb490. Assigned task base:
dfebf941d7d343009bb819cea4d65bb3dca8fbcf. Re-cut the nine commits ending at
805509b611850708c3900c9f5c77764c29298e65 with cherry-pick -x, retaining master's
OS-function expectations and its 59-definition manifest. The only conflicts are
explanatory comments in LandGraphEvaluationPinsTests.cs; master wins, and the
additive changed/missing-pin assertions remain. Regenerate evaluation.txt through
audit-land-verification-graph.ps1 -Update; never copy or edit a digest.

The original [implementation and verification design](2026-10-23-card-1540-land-audit-environment-timestamp-plan.md)
owns D-1..D-7, V-1..V-8 and R-1..R-6. This re-cut's explicit narrow Final brief
replaces its eight checkpoint rows with the one row below. No Unit lane or full
assembly is requested. No source changes during the checkpoint run.

LandGraphEvaluationPinsTests.cs declares the LandGraphEvaluationTests partial
class, not a separate runnable class. The first OR operand runs every method in
all its partial files, including all pins tests. The second operand runs exactly
the prior manifest registry case and C1380_DocsOnlyRebasedLandRecordsNonCodeSkip.
Every operand ends with a wildcard. Inspect fresh TRX for each intended method
and argument, nonzero execution, no unexpected methods, and all V/R outcomes.

Manual acceptance: retain master's manifest bytes; run real audit -Update and
-Check; validate clean source/build provenance at committed expected HEAD; run
check-evidence-diff.ps1 over the full task base..HEAD; fetch master and run
merge-tree --write-tree without merging. Restart: none. No rc changes, tags or
release scripts. No new defect witnesses are authored: preserve the predecessor's
ten measured base approvals and its excluded PATH infrastructure failure as
historical evidence, not new execution.

## Performance budget

One shared Antiphon.Tests build, then one exact test selection. The previous
63-result selection at 805509b611850708c3900c9f5c77764c29298e65 took 14m33s,
including a 519s build, on Linux with zero slot wait. This superset adds existing
evaluation methods and one DB-backed landing acceptance case. Estimated total:
22 minutes, provisional rather than measured p95 (CARD-1540); collect one final
TRX sample. Keep a finite 30-minute charged execution budget; admission is bounded
separately by the checkpoint tool. No test timeout or assertion is widened.

Category: git operations/server Integration; Antiphon.Tests stages the referenced
LandGraphAudit tool. Production runtime is unchanged. Evaluation fixtures own tiny
Git repositories because repository evaluation is the test subject. The C1380
landing fixture uses the assembly's shared PostgreSQL setup once per run, with
existing isolated schemas. No browser/provider/AppHost setup. Record per-test
durations, checkpoint build/slot/startup/body/teardown phases and available external
process spans in .antiphon/c1540-recut and .antiphon/checkpoints. Internal filesystem,
DB setup breakdowns or external-call attribution not emitted by these existing
fixtures remain unknown; unattributed duration is remainder, never zero.

Necessary prerequisite builds, declared separately from the single test build:
the final LandGraphAudit Release tool for the required author-time -Update, and
the checkpoint launcher if absent. Both use build-slot.ps1 and unique bin-c1540-*
output directories, then are removed after verification. Neither selects tests.
Portable execution uses GET /api/runner-defaults and /api/session-runners without
runner/platform pins. No full-assembly selection or unbounded class is run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | Recut | `tests/Antiphon.Tests -> bin-c1540-recut/` | master-recut | `/*/*/LandGraphEvaluationTests/*\|/*/*/LandNonCodeSkipTests/(C1385_RegistryMatchesNarrowGraph*\|C1380_DocsOnlyRebasedLandRecordsNonCodeSkip*)` | V-1..V-8 R-1..R-6 full evaluation and pins, C1380 acceptance | all passed, 0 failed/skipped | 80 | 22 |

Run the checkpoint tool with --plan <this file> --after Recut
--expected-source-sha <committed HEAD> --total-timeout 30m. Wait to terminal exit,
then inspect fresh TRX and validate receipt source/build provenance.

## Pending Mutation and native work

All controls remain pending for method-scoped SourceLanding Mutation:
PC-1 admit every plain name (gate/include/path); PC-2 omit timestamp detection
(modified); PC-3 omit metadata function arguments (item-function); PC-4 drop knob
pin predicate (unpinned); PC-5 case-sensitive names (syntax matrix/lowercase knob);
PC-6 raw-only and unescaped-only variants (escaped property/metadata matrix).
Mutation owns deliberate mutants, red/restore/green and missing-control discovery.
No PC is discharged here. CP-W remains informational Windows work owed by the
original plan, not passed and not blocking this portable re-cut. No ordinary V/R
ID is deferred from this targeted Final scope.
