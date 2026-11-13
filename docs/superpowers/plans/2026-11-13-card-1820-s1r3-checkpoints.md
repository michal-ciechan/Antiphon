# CARD-1820 S1R3 conservative project closure

S1R3 is first in dependency order after landed S1R1/S1R2. Upstream plan:
[trustworthy skipping](2026-11-12-card-1820-selector-v2-trustworthy-for-skipping-plan.md).
S1R6 depends on S1R3-S1R5; CARD-1866 A-1/A-3 drift witness/fixture cleanup stay there.
CARD-1868 precision/delegate work belongs to S1R6/V1, not this project-wide conservative boundary.
Native Windows proof remains orchestrator-owned. Restart: none.

The module will expand known project inputs through reverse references without subtracting
the landed relation. Conditions union all literal candidates; imports and compiled/generated/
resource inputs add positives. Dynamic expressions, missing inputs and reflection targets
retain unknowns. XML source inventory is not evaluated MSBuild evidence: keep
project-evaluation-unavailable and Full. A separately commissioned source-bound evaluator
is required before S1R6; this is the upstream slice's permitted incomplete-support boundary.
No release CLI composition or activation. Ordinary selector/builder semantics stay unchanged.

V-3 uses two actual Roslyn-compiled fixture assemblies and reads their metadata reference,
then calls the real graph module. The initial adapter calls the landed relation unchanged;
commit and execute its assertion reds before replacing that adapter. Shared compilation is
once per class/host, in memory; no subprocess, database, server or disk fixture setup.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-4 | S1R3 | `tests/Antiphon.Tests -> bin-c1820-r3-red/` | linux-project | `/*/*/ReleaseImpactProjectTests/*` | V-3 | four named methods, no failed/skipped | 4 | 12 | true |
| CP-101 | S1R3 | CP-4 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | Final guards and impacted test-lane-category | all named classes, no failed/skipped | 78 | 4 | true |
| CP-102 | S1R3 | CP-4 | ordinary-index | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | R-3 index drift | named method, no failed/skipped | 1 | 2 | true |
| CP-103 | S1R3 | CP-4 | relation-regression | `/*/*/ReleaseImpactRelationTests/*` | V-2 adjacent module | all four, no failed/skipped | 4 | 1 | true |

## Performance budget

19 minutes provisional, one cold build shared across green rows. Red selects CP-4 only;
green uses a fresh output root, identical filter/assertions, and all rows. The upstream
20-minute explicit build/row bound is retained, not widened; finite slot admission is 45m.
Red/green are separate committed groups. No repetitions after green. New in-memory fixture
cost is provisional (first observation retained under CARD-1820/CARD-1404 collection).
Record phase journal, TRX durations, slot wait and execution separately; unknown attribution
is remainder. Over 30 minutes needs caller-commissioned Debug; no delegate fanout here.

Checkpoint tool native bootstrap and native DerivedImpactIndex driver build are declared
preparation outside rows, each gated; driver precedes owner-script -NoBuild index regeneration.
CARD-1868 driver precondition is explicit even though no driver-executing regression is selected.
Generated evidence remains ignored. Inspect each intended class/method in fresh TRX;
strict expected SHA and validate receipts. Evidence-diff covers the full task base..HEAD;
fresh merge-tree and OS declaration lint complete manual checks. No migrations match.
No PC IDs declared: SourceLanding Mutation owns controls/discovery under the operator waiver.
V-1, V-4..V-17 and R-1/R-2/R-4/R-5 stay inherited or later-slice scope, never credited here.
