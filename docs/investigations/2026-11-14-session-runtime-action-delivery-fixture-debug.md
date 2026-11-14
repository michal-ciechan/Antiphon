# Session runtime action delivery fixtures: first-bad proof

Task: 1bf97852. Assigned base/current origin/master at investigation start:
`d5c798092590ad377361292e2785a56b3b93fe16`.

The existing committed tests are the red-first witnesses. No new test or production
change is needed to reproduce the failure. A detached scratch worktree ran
`git bisect start f22a2c1476da7c85a5b54cb04215b451ea73c0c9 6f5ce63cb`,
then `git bisect run pwsh -NoProfile -File /tmp/task-1bf97852-evidence/bisect.ps1`.
Every step built Antiphon.Tests once in its native output with UseAppHost=false,
then ran only the three requested methods through build-slot.ps1. Each row expanded
to 11 cases; there were no skips, build errors, or timing failures.

| Source | Passed | Failed |
|---|---:|---:|
| f22a2c1476da7c85a5b54cb04215b451ea73c0c9, bad endpoint verified | 3 | 8 |
| d7cdd7bf76069a786ee7eaaee868e3a0f5e1bcb7 | 3 | 8 |
| 47d86c514a1bdad73dbf84aeaeb3bd460e7aa7ac | 11 | 0 |
| 1529560f9944898f9a9594a8ffee4ae918501508 | 11 | 0 |
| 4e4362beccf9b0be06c191990c4d323656809dad | 3 | 8 |
| 38d898eb009ee8ccd282f4a33cc0941059eb094a, direct parent | 11 | 0 |

First bad: **4e4362beccf9b0be06c191990c4d323656809dad**, "reconcile fresh
uncertain attempts before later input". This predates the round-2 composer-less
hold. CARD-1892 and its implementation plan intentionally preserve uncertain
attempts and reconcile them before later input. The product's hold is required.

BridgeQueueHarness.SeedChannelCorrelationAsync documents an already DELIVERED
message, but seeds Sent/one attempt/null verdict. The new recovery correctly reads
that as unresolved input. The mixed-source test also seeds an already delivered
machine note with a null verdict, inadvertently adding a fresh recovery operation
before the pending late-confirm source it intends to exercise. Correct those
preconditions with Delivered verdicts; retain all delivery, recipient, ordering,
debt and late-confirm assertions. Leave genuinely unknown attempts verdict-less.

## Why missed

Merge 213's combined-tree queue/recovery verification should have included SessionRuntimeActionRecoveryTests and SessionRuntimeActionRoutingTests for the fresh-attempt reconciliation change.
Merges 212-214 ran selected classes, leaving this recovery/routing interaction outside their selections.
The CARD-1886 S2 Review ran on old base 6f5ce63cb, where these cases were green, so it could not detect the subsequently introduced interaction.

## Evidence and cost

Raw build logs, test logs, TRX memberships/durations, load samples, the bisect
driver and git bisect log are retained under `/work/evidence/task-1bf97852/`.
Test rows took about 82-104 seconds, separately from build-slot admission and
compilation. Host load exceeded 30 repeatedly (peaking near 70); no timeout was
classified as a defect. Fixture phase attribution is unknown; TRX test durations
and gate/build elapsed times are retained. No full assembly or namespace ran.

## Repair and completed verification

Red evidence was committed and pushed first as
`52e1708e2d343fd9a92c1efd786fa7a07969be60`. Test-only repair:
`cb212a5801328038581a6d98b40556f1a72ba06d`. It adds Delivered/DeliveredAt to
the already-delivered channel fixture and Delivered to the mixed-source test's
already-delivered machine note. No assertion, timeout, or production code changed.

Master advanced during the bisect to
`f805ae444dcfdec07fd1ed18727817b16f2e5bc0`. To honor both current-master
verification and the assigned branch's no-rebase/no-target-merge contract, the
scratch worktree checked out that master and applied the committed task delta
with git apply --index. Its git write-tree exactly matched git merge-tree's
combined tree: `636b8442299dfd79e94fe51e31b5764029448a97`. The working files
matched the index before each row and after all verification. No source changed
while a build or test ran. A final fetch still observed the same master and the
same conflict-free combined tree. The subsequent task report amendment is prose.

All rows ran serially via scripts/build-slot.ps1, with native default output,
UseAppHost=false on build and no-build run, and TUnit through dotnet run.

| Whole-class selection | Project | Passed | Failed | Skipped | TRX seconds |
|---|---|---:|---:|---:|---:|
| SessionRuntimeActionRecoveryTests | Antiphon.Tests | 24 | 0 | 0 | 116.18 |
| SessionRuntimeActionRoutingTests | Antiphon.Tests | 35 | 0 | 0 | 222.14 |
| SessionMessageQueueInterruptedAttemptTests | Antiphon.Tests | 24 | 0 | 0 | 79.40 |
| Requested seven-class guard union | Antiphon.Tests | 89 | 0 | 0 | 246.81 |
| C1722GitTimeoutBudgetTests | Antiphon.SessionRunner.Tests | 98 | 0 | 0 | 0.66 |
| Total | | 270 | 0 | 0 | |

The exact guard union was TestClassificationPolicyTests*,
TestClassificationGuardTests*, SlowTestRegistryMergeTests*, SlowTestTripwireTests*,
NativeLinuxPolicyWitnessTests*, DerivedImpactIndexTests*, and
C1729LayeringGuardTests*. C1722GitTimeoutBudgetTests is the other test class
changed by the breaking commit. The additional interrupted-attempt class is
justified by the behavioral decision: all composer-less no-Enter/no-retype and
resolved-attention cases remain green. Original-roster inspection independently
confirmed all 11 variants of the three originally selected methods passed,
including all eight previously red cases.

The DerivedImpactIndex driver was built first (7.23 seconds), then regeneration
ran twice through the gate with -NoBuild: both printed
`derived-impact-index unchanged`. No generated-index commit was needed. The final
Antiphon.Tests build took 429.49 seconds and the runner test build 26.76 seconds,
excluding admission; both succeeded. Every test row remained below ten minutes.
No full suite, namespace lane, timeout widening, or repeat of a green test row ran.

One local orchestration wrapper stalled before launching a child or any tests.
Its owned PID/cwd/command and lack of children were checked, it was terminated and
joined, and direct gated commands completed the required selections above. That
attempt supplied no test receipt and is not credited. Its file and empty log are
retained as historical evidence; use the direct commands, not that wrapper.

For reruns, build tests/Antiphon.Tests and tests/Antiphon.SessionRunner.Tests
through build-slot.ps1 with -p:UseAppHost=false. Run each table row through the
same gate using `dotnet run --project tests/<Project> --no-build
-p:UseAppHost=false -- --treenode-filter '/*/*/<Class>/*'`. The guard row uses
the seven named class operands joined with TUnit's class-level OR syntax and
a trailing wildcard on each operand. The exact executed commands, TRX files,
original-roster.json, and verification-summary.txt are in the external evidence
root. No production host or service was restarted.

Next stage: merge directly; the repair is test-only.
