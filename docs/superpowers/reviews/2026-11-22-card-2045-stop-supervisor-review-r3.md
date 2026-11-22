# CARD-2045 stop-supervisor review, round 3

**Clean.** Both round-2 blockers are closed. Land through original Code owner `2e578038-735f-4c72-8746-5d69938200d1` using the reviewed repair from `5dd16507-1e2a-4c32-bde0-c3b9689ce04a`.

The round covers `623a0dc573796c7b7625de484d1257515069ee4c..5eff132ea736727873ac284de7ce5aa19ebac36f`. Only the stop entry point and its harness changed. The checkout default runs only when parameter binding reports omission. A supplied blank exits 6 before path resolution or helper import.

The committed red harness at `d6ce9a400b89411430a181f546a33c8be046060f` passed ten cases and failed precisely the four blank and whitespace invocations, with dry-run exit 2 and Linux confirmation exit 5. Smoke at the reviewed SHA passed 53/53. Its 14 refusal rows captured the child exit and each stream separately. Each observed exit 6, empty stdout, the case-specific reason on stderr, no stale-helper marker, no log creation and the original lock hash. The two earlier red witnesses reproduced their six and one expected failures.

All six comparisons of an ordinary checkout, a linked worktree and a task copy with explicit checkout matched the prior revision byte for byte on stdout and stderr, with exits 2 and 5. Both files were ASCII and parsed cleanly on PowerShell 7.5.4. The test uses separate asynchronous stream reads and the exit code from the child process. The changed syntax passed inspection for Windows PowerShell 5.1. Native 5.1 execution, real Windows taskkill and the positive controls remain pending.

The Code transcript recorded the red, Smoke and two scratch mutant runs outside the build-slot gate and before the repair commit. Its first red attempt also included two fixture expectation failures corrected before the red commit. The scratch output-channel change failed 14/14 assertions and suppressing the helper checks failed the four missing-helper rows. Review used the committed red sources and a fresh leased checkpoint for ordinary evidence. The stop repair has no committed checkpoint table. The review manifest kept Smoke and the ten guard classes from the preceding review.

The impact selector chose zero additional rows, including zero downgrade tests, over both the two-commit review range and the six-commit candidate. Evidence-diff found zero entries and zero violations over the candidate. Fetched master `d4b20888a8e3c147fac0ae650d9773c4390bb6c0` merged cleanly in merge-tree, producing `64131eb1bd05fa6338417cc1fef5f13f79c3f252`.

Checkpoint `20261122-225704-03c3` validated all five receipts for the reviewed SHA. The guard TRX contains 85 distinct passing executions across all ten requested classes, with zero failed or skipped. Review built the checkpoint CLI once under its own build-slot lease. An initial attempt, `20261122-225424-3852`, was stopped after the executor could not find the wrapper's log-search command. The complete run used `/bin/grep` and a 55-minute wall limit including cleanup.

The complete checkpoint took 24m37s, including 6m15s waiting exclusively for admission and 6m06s cleaning the isolated output. The test host reported 2m18s.

<details><summary>Validated checkpoint receipts</summary>

```text
CHECKPOINT CP-1 commit=5eff132ea736727873ac284de7ce5aa19ebac36f build=n/a filter=sh /work/review-evidence/task-3116882e/red.sh executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=270s dirty=0 source=5eff132ea736727873ac284de7ce5aa19ebac36f sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=5eff132ea736727873ac284de7ce5aa19ebac36f build=n/a filter=pwsh -NoProfile -NonInteractive -File scripts/test-qualification-stop.ps1 -Case Smoke executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=60s dirty=0 source=5eff132ea736727873ac284de7ce5aa19ebac36f sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=5eff132ea736727873ac284de7ce5aa19ebac36f build=n/a filter=sh /work/review-evidence/task-3116882e/parity.sh executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=15s dirty=0 source=5eff132ea736727873ac284de7ce5aa19ebac36f sourceState=clean buildSource=notApplicable
CHECKPOINT CP-4 commit=5eff132ea736727873ac284de7ce5aa19ebac36f build=n/a filter=scripts/antiphon evidence-diff --base ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=5eff132ea736727873ac284de7ce5aa19ebac36f sourceState=clean buildSource=notApplicable
CHECKPOINT CP-5 commit=5eff132ea736727873ac284de7ce5aa19ebac36f build=ok filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(TestFixtureCleanupGuardTests*)/* executed=85 passed=85 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261122-225704-03c3/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=5eff132ea736727873ac284de7ce5aa19ebac36f sourceState=clean buildSource=verified
```

</details>
