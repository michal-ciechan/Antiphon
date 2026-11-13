# CARD-1887 S3 execution selection

Original Code owner: ce95788a. Base: 1fde603cce208ff8a9147715101b3976c737edd0.
Implements S3 (seed and pins), because the CARD-1886 combined plan assigns this
query-only rewrite to CARD-1887. S1, S2, S4 and S5 remain separately commissioned.

The parent [plan](2026-11-13-card-1886-postgres-jit-and-hot-queries-plan.md)
owns the design, V-3 and R-1, performance budget, measurements and deferred
post-qualification desktop acceptance. Its fractional CP-5/CP-8 minute estimates
are rejected by the current importer even when selecting CP-3 only. This artifact
provides exactly the commissioned S3 and final-guard selection, with integer costs;
no other slice's tests or estimates are changed here.

V-3: independent pin-status/reason enumeration, one-command budgets, frozen old/new
SQL row differential (all seed fields), exact-count transcript cases and scale
EXPLAIN regression. R-1: named guard union including DerivedImpactIndexTests,
actual impacted-class census, evidence-diff, OS witness inventory and merge-tree.
The semantic differential canonicalizes unordered result sets by complete JSON row;
seed returns a dictionary and pins a set, so neither API promises row ordering.
Transcript LastSequence and all projected boundaries remain part of that comparison.

## Performance budget

Use the parent's bounded provisional commissioning: 10m per row, 20m charged run,
10m admission. Build through the host slot; rows serial. Scale data is built once
in the scale test's disposable clone; no per-case container or migrations.
Record timing JSONL and fresh TRX under ignored evidence; all unavailable phases
remain unknown. Read load before each EXPLAIN; no desktop work or activation.
Use 120s provisional scale body estimate (not a relaxed timeout) until measured.

Pending SourceLanding Mutation: PC-3-parent, PC-3-queue, PC-3-status bind
SessionStatePinQueryTests.Pin_membership_preserves_all_reasons;
PC-3-correlation binds SessionStatePinQueryTests.Scale_plans_enumerate_open_tasks_once.
Mutation owns deliberate red/restore/green and additional missing controls.
No migration or OS branch change is planned. Startup composition is unchanged;
server restart activates the reviewed SQL later. Restart: server; do not restart now.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-3 | S3 | tests/Antiphon.Tests -> bin-c1887-s3c/ | pin-query | `/*/*/(SessionStatePinQueryTests*)\|(SessionStateProjectionTests*)\|(SessionStateWarmupTests*)\|(SessionStateCacheReadTests*)/*` | V-3 | all listed, 0 failed | 4 | 4 |
| CP-10 | all | tests/Antiphon.Tests -> bin-c1887-all/ | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)\|(ReleaseImpactEnvelopeTests*)/*` | R-1 | all listed, 0 failed | 11 | 2 |

--- next stage ---
next: code
handoff: Execute S3 and the final guard union, then ordinary Review. Keep all PCs pending for SourceLanding Mutation and defer desktop activation/measurement until live qualification ends.
artifact: docs/superpowers/plans/2026-11-13-card-1887-pin-query-code-plan.md

First execution 20261113-230500-2e05: 31 passed, one setup timeout in the
monolithic 3,118,500-row insert; not a protected-behavior red. Preserve that
receipt and output. Insert batches now retain the same total cardinality and
60s call limit. The scale class runs globally serial to isolate measurement
from the adjacent tests in this row. A fresh output name retains the failed build.

Red proof at 6201701c3736e28249bfb534553852d3e08c2319, run
20261113-231908-dfd6: 31 passed, scale assertion failed on 64 task-index loops
for seed-64 (512 loops for fleet pins). Both full old/current plans were saved.
Scale setup took 299s for transcripts plus unmeasured wide-EF task setup; total
scale test ~444s. Final setup copies one EF-populated task prototype with set SQL
and uses ordered synthetic entry IDs to avoid random primary-key insert churn.
It retains 27,695 tasks, 3,118,500 transcripts, all 512 sessions and every assertion;
the exact total count is now additionally asserted. No timeout was widened.

Final diff census: the sole matching hand-authored impacted-classes.json glob
row is test-lane-category, covered by CP-10. CP-10 additionally includes
ReleaseImpactEnvelopeTests because the mandated derived-index regeneration changes
its input. The reverse graph also lists broad runtime integration families and
Windows-only GrokDelegateEndToEndTests; those are outside the commissioned closed
S3 plus hand-glob scope, are not run/credited here, and remain visible in
.antiphon/evidence/card-1887/impact-final.json for Review. No OS-selected
production behavior changed; no S3-specific native Windows witness is required.
