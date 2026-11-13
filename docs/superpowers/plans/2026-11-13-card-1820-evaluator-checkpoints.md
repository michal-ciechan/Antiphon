# CARD-1820 S1R3E source-bound MSBuild evaluation

First unfinished prerequisite: S1R3 explicitly leaves MSBuild evaluation unavailable.
This subtask completes its evaluator boundary before S1R4/S1R5/S1R6 composition.
Upstream: [selector plan](2026-11-12-card-1820-selector-v2-trustworthy-for-skipping-plan.md).
The additive `release-inputs` command in the existing staged LandGraphAudit driver
uses real SDK MSBuild without restore/build/target execution. It exports surviving
Compile, ProjectReference, Content/None/resources, AdditionalFiles/analyzers,
imports and explicit target properties. Every tracked byte is bound to the exact
Git commit; untracked evaluated inputs and source drift refuse. SDK import bytes
are independently hashed. No generated-code/runtime completeness is inferred.
It returns incomplete/Full and is not wired to ordinary selection or qualification.
No fine-edge subtraction or class cap. Project-level output avoids serializing
S1R3's 8.2M path/class pairs (CARD-1877 A-2).

## Verification design

V-3E: ReleaseImpactEvaluationTests exercises the real staged driver in four methods:
EvaluatedItemsRespectConditionsUpdateAndRemove; ReceiptBindsContentSdkAndTargetProperties;
DirtyOrMismatchedSourceRefuses; UntrackedEvaluatedInputRefusesAndMissingImportFails.
The red adapter calls the existing real evaluator and exports an explicitly unbound
empty inventory. Its assertion failures precede the source-binding/export implementation.
V-3: the original four project-closure tests remain required adjacent regressions.
R-3: committed ordinary index equals its builder. Other upstream V-1/V-2/V-4..V-17,
R-1/R-2/R-4/R-5 stay inherited/later scope and are not credited here.
CARD-1866 A-1/A-3 remain S1R6 envelope witness/cleanup work; evaluator content binding
addresses only evaluator inputs, not the P2 envelope authenticity contract.
CARD-1868 delegate/precision and CARD-1877 reflection/old-new union remain before S1R6.
Native Windows proof W1 remains orchestrator-owned. Restart: none.
No PC IDs declared: controls/discovery remain pending for SourceLanding Mutation;
deliberate execution is waived by the operator, not satisfied by ordinary green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-106 | S1R3E | `tests/Antiphon.Tests -> bin-c1820-eval-red3/` | evaluator | `/*/*/ReleaseImpactEvaluationTests/*` | V-3E | four named methods, zero failed/skipped | 4 | 3 | true |
| CP-4 | S1R3E | CP-106 | project | `/*/*/ReleaseImpactProjectTests/*` | V-3 | four methods, zero failed/skipped | 4 | 1 | true |
| CP-101 | S1R3E | CP-106 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | policy/mechanical/platform/layering | all named classes, zero failed/skipped | 78 | 4 | true |
| CP-102 | S1R3E | CP-106 | index | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | R-3 | named method, zero failed/skipped | 1 | 2 | true |
| CP-103 | S1R3E | CP-106 | project-shape | `/*/*/LandGraphAuditProjectInstanceTests/*` | impacted land-graph-audit-project | one method, zero failed/skipped | 1 | 1 | true |
| CP-104 | S1R3E | CP-106 | spawn-limiter | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | all cases, zero failed/skipped | 57 | 1 | true |
| CP-105 | S1R3E | CP-106 | default-audit | `/*/*/LandGraphEvaluationTests/(C1483_S1_HarmlessFixtureApproves*)\|(C1483_S1_EvaluatedSkippableItemRefuses*)` | default command remains unchanged | two methods, zero failed/skipped | 2 | 1 | true |

## Performance budget

13 minutes provisional selection plus one isolated cold build; 20-minute existing
row/build bound (`--row-timeout 20m`) is retained. Slot admission bound 45m, separately recorded. Shared
staged driver build, isolated tiny fixture repositories per method; no server/DB.
The tests log tool wall time and TRX durations; unavailable nested IO is remainder.
Red runs CP-106 only, green all rows once with a fresh output path. No repetition
required after green; at most 3 normal + 2 loaded per unchanged selection.
Declared preparation: gated native checkpoint-tool bootstrap and DerivedImpactIndex
driver build before owner-script -NoBuild ordinary-index regeneration. Each driver
gets a slot. These are outside checkpoint rows and reported explicitly.
Manual: full base..HEAD evidence-diff, fresh merge-tree, OS witness declaration/lint,
full changed-glob impacted-table census and byte identity of ordinary selector code.
No migrations or OS-selected production behavior changes; native evaluation/platform
compatibility is still pending W1, never credited by Linux.
