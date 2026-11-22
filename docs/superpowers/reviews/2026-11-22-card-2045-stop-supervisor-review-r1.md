# CARD-2045 stop-supervisor review, round 1

**Return to Code.** The task-directory copy works with the intended checkout. The new loader also accepts a directory containing stale helpers as its code source.

## Blocker

- **Where.** `scripts/qualification/stop-supervisor.ps1`, lines 31–34, in Code commit `8429705386dfb247b50422cdb542997e0ea86891`.
- **Failure.** `Resolve-Path -LiteralPath` establishes that the supplied path exists. The next statement imports the helper without establishing that the path is a checkout. In an empty fixture where Git returned 128 for the worktree root, PowerShell attempted the helper lookup under that directory. The missing file ended the diagnostic.
- **Why.** A task or export directory retaining the checkout-relative helper could supply the stop implementation, even for a dry run. The new test covers a stale copy beside the entry point when the operator supplies the correct checkout. It omits the wrong-root refusal.
- **Fix.** Verify the resolved filesystem directory is the checkout's top level and the expected helper belongs to that checkout before importing it. Support linked worktrees. Add a negative case using genuine copied helpers in a directory with no checkout metadata, and assert refusal before import.

## Verification

Code owner `2e578038-735f-4c72-8746-5d69938200d1`. Reviewed range `ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91..8429705386dfb247b50422cdb542997e0ea86891`. Code had no checkpoint table for this stop-script repair.

| Selection | Independent result |
|---|---|
| `C1507_TaskDirCopyLoadsCheckoutLibrary` | At `081828ed40f3880fabee4425864c6829756c4bd9`, 0 passed and 1 failed on the unsupported parameter. At the reviewed tip, 1 passed. |
| `-Case Smoke` | 39 assertions passed across all 13 cases on Linux. |
| Checkout entry with the root omitted | Both stdout comparisons to the pre-fix entry were byte-identical. Dry-run returned 2. Linux `-Confirm` returned 5. The native lock kept its bytes and the run created no supervisor log. |
| PowerShell compatibility | Both edited files were ASCII and parsed cleanly on PowerShell 7. Inspection found no new language feature beyond 5.1. The existing native Windows PowerShell 5.1 and taskkill witnesses remain pending. |
| Final guard filter | 85 passed across all 10 named classes, 0 failed, 0 skipped. |
| Impact and history | 0 impacted rows, 0 downgrade tests selected. Gated evidence-diff inspected 2 commits and found 0 violations. |

The Code transcript shows its narrow tests ran before the corresponding commits and its evidence-diff launcher invoked MSBuild outside the slot gate. Review verified the committed scripts and reran evidence-diff under a lease.

The first combined run, `20261122-210342-f1db`, took 11m04s including a 421s cold build. The pre-fix test produced the expected failure, then the Review wrapper returned 127 because its shell lacked `rg`. All other rows passed. Review retained the initial receipt and used the existing guard build for the replacement receipt.

The rc-qualification skill's stop example can add `-CheckoutRoot <checkout>` in a documentation follow-up. Positive controls remain pending.

The checkpoint validator accepted script rows CP-1–3 in run `20261122-211136-cd1f` and guard row CP-4 in `CP-4-20261122-212219-e9d0` for the reviewed SHA. The guard TRX contained 85 distinct passing execution identities. The fetched master at `7488dc689f2949402ac85695e65ef64c9e0ee625` produced merge tree `186551c914e5ec1a346cf29c1bf73aa8d4a0b900` without conflicts.

<details>
<summary>Validated checkpoint lines</summary>

```text
CHECKPOINT CP-1 commit=8429705386dfb247b50422cdb542997e0ea86891 build=n/a filter=sh /work/review-evidence/task-f6b6a174/prefix-red.sh executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=75s dirty=0 source=8429705386dfb247b50422cdb542997e0ea86891 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=8429705386dfb247b50422cdb542997e0ea86891 build=n/a filter=pwsh -NoProfile -NonInteractive -File scripts/test-qualification-stop.ps1 -Case C1507_TaskDirCopyLoadsCheckoutLibrary executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=135s dirty=0 source=8429705386dfb247b50422cdb542997e0ea86891 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=8429705386dfb247b50422cdb542997e0ea86891 build=n/a filter=pwsh -NoProfile -NonInteractive -File scripts/test-qualification-stop.ps1 -Case Smoke executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=465s dirty=0 source=8429705386dfb247b50422cdb542997e0ea86891 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-4 commit=8429705386dfb247b50422cdb542997e0ea86891 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(TestFixtureCleanupGuardTests*)/* executed=85 passed=85 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/review-c2045-guard/CP-4-20261122-212219-e9d0/run.trx slot=granted waited=405s dirty=0 source=8429705386dfb247b50422cdb542997e0ea86891 sourceState=clean buildSource=verified
```

</details>
