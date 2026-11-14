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

## Verification pending

After the fixture repair: whole Recovery and Routing classes, the breaking
commit's C1722GitTimeoutBudgetTests, and the brief's exact guard union. Also run
SessionMessageQueueInterruptedAttemptTests to preserve the composer-less no-input
hold and resolved-attention regressions. This additional class is justified by
the behavioral decision above. All rows run serially through the host slot gate.
