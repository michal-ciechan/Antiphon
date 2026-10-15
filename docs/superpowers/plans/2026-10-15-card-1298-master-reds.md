# CARD-1298: repair two inherited census failures

Task/landing owner: fbba8d73-6262-4af2-8609-94122a5fb155.
Base: e03508908b510dc43b60b3a3aa2a54f0d008e1c5.

Operator scope: minimal exact selections, no whole Unit lane (CARD-1287).
Checkpoint comparison-filter and B-2 caller-authorization items are untouched.
No server or runner restart. No production change and no guard relaxation.

S1: reproduce both existing assertions independently on the committed base.
S2: use AddGitWorkspaceService in the reply fixture; encode FreshProbe as a
single-line source string, following the ReceiptProbe fixture in the same file.
The runtime probe remains a public sealed class with the same TUnit test.
Keep both census implementations unchanged.

V-1: RuleC fails naming DelegateBranchDeliveryTests.cs at base, passes after repair.
V-2: lane census fails naming CheckpointTrackedBaselineTests.cs::FreshProbe at
base, passes after repair.
R-1: full census and helper classes retain all registration rules.
R-2: full DelegateBranchDeliveryTests retains the real delivery/recovery behavior.
R-3: full CheckpointTrackedBaselineTests retains fresh-build execution and custody.
R-4: PlanCoverageBindingTests, the direct source reader of the lane guard, stays green.
Manual: inspect the patch for unchanged guards, assertions and deadlines; verify
source-clean receipts/TRX rosters; check evidence history; check merge-tree.
No new test methods: existing red assertions are the regression tests.
PC-1: pending Mutation, remove the added helper call; exact RuleC method must fail.
PC-2: pending Mutation, restore the multiline FreshProbe source literal; exact
lane census method must fail. No deliberate mutants run by Code.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1298-red/` | census-red | `/*/*/DelegationHarnessCensusTests/RuleC_reply_harnesses_register_git_via_helper_or_BridgeQueueHarness` | V-1 red | exactly 1 failed at base | 1 | 3 | true |
| CP-2 | S1 | CP-1 | lane-red | `/*/*/TestLaneCategoryGuardTests/every_test_class_is_tagged_unit_xor_integration` | V-2 red | exactly 1 failed at base | 1 | 1 | true |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1298-green/` | census-green | `/*/*/DelegationHarnessCensusTests/*` | V-1, R-1 | exactly 7 passed | 7 | 3 | true |
| CP-4 | S2 | CP-3 | lane-green | `/*/*/TestLaneCategoryGuardTests/*` | V-2 | exactly 1 passed | 1 | 1 | true |
| CP-5 | S2 | CP-3 | helper | `/*/*/DelegationTestServicesTests/*` | R-1 | exactly 5 passed | 5 | 1 | true |
| CP-6 | S2 | CP-3 | delivery | `/*/*/DelegateBranchDeliveryTests/*` | R-2 | exactly 3 passed | 3 | 3 | true |
| CP-7 | S2 | CP-3 | tracked-baseline | `/*/*/CheckpointTrackedBaselineTests/*` | R-3 | exactly 13 passed | 13 | 5 | true |
| CP-8 | S2 | CP-3 | guard-reader | `/*/*/PlanCoverageBindingTests/*` | R-4 | exactly 6 passed | 6 | 1 | true |

### Cost

18 estimated checkpoint minutes plus authoring, under the 30-minute brief budget.
One isolated test build per source state is necessary to prove red then green;
all subsequent rows reuse that state's build. One separate slot-gated checkpoint
tool bootstrap build is required because this clean worktree has no tool binary.
No broad assembly runs, repetitions, or unrelated classes.

### Base evidence and execution mode

CP-1 and CP-2 each executed exactly one test at the full base SHA above, each
failed with the expected offender, zero skips, slot=granted waited=0s,
dirty=0 sourceState=clean buildSource=verified. Thus these are reproduced
inherited failures, not inferred from another task's report.

`run --plan` refused before launching a driver with `CHECKPOINT owner
owner-unverified` (exit 7): this task environment has no ANTIPHON_TASK_TOKEN.
Use the documented foreground checkpoint `row` verb for each exact table row;
retain slot admission, source checking and build stamps, and await every row.
This execution-mode deviation avoids launching an unowned detached executor.
It does not change task identity, fabricate a token, or alter the checkpoint tool.
Bootstrap build passed, 2 existing compiler warnings, slot granted/waited=0s,
held=7s. Raw generated evidence remains ignored under `.antiphon/checkpoints/`.
