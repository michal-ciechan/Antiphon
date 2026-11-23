# CARD-1514 Final Review of the readiness path identity re-cut

**Return to Code.** All 17 ordinary rows ran at `bb5bdb87149047270e6303593825bac7ed7b1e18`. Fifteen passed. CP-7 and CP-9 each had one process-cleanup failure. The exact-SHA receipt validator returned exit 2, `report_failed`. Both methods passed in isolation on the same candidate and on the original master base. Attribution remains unresolved.

| Where | Failure in the combined run | Why it blocks | Fix to verify |
|---|---|---|---|
| CP-7, `HerdrAlwaysOnChannelParityTests.Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(Grok, False)` | `TestOwnedPtyHost.WaitNativeAsync` reported `process observation Unknown: IOException` during disposal. The row had 491 passes, one failure and nine skips. | The fixture could not confirm the retained process generation had exited. The teardown exception fails the required row. | Diagnose the failing observation during exit. Preserve process-identity checks and the refusal on unresolved ownership. Verify disposal in the original row. |
| CP-9, `LinuxOwnedGitProcessTests.Termination_kills_reparented_member_before_rescue(False, True)` | With the root already exited, `TerminateAndJoinAsync` returned `Joined == false` against its six-second budget. The row had 419 passes, one failure and six skips. | The test stopped at line 115 before proving empty membership, death of the known descendant and survival of unrelated processes. | Capture the join result and membership state under row load, resolve the cause, and verify the before-rescue assertions at the existing deadline. |

## Ordinary evidence

Run `20261123-171911-7679` took 52m38s. Its 17 distinct TRXs contain 1,638 distinct result identities. Of the 1,614 cases executed, 1,612 passed and two failed. The other 24 skipped. All three builds and every row received a slot. The report records a clean tree, verified build provenance, zero unlisted runs and under 0.01 s of admission-only time. The two failures prevent receipt qualification. The focused candidate diagnostic `20261123-175537-70a3` passed all eight parameter cases and validated at the reviewed SHA. The original base `194233ccbc1e28f742fed6ef63ba7485c2b15f48` and the commissioning master `4e9b7efdbea4af35340bfcc55ca7bc74db1c3286` each passed the same eight cases in method-scoped replays.

| CP | Passed | Failed | Skipped | CP | Passed | Failed | Skipped |
|---|---:|---:|---:|---|---:|---:|---:|
| 1 | 1 | 0 | 0 | 10 | 47 | 0 | 0 |
| 2 | 204 | 0 | 2 | 11 | 1 | 0 | 0 |
| 3 | 3 | 0 | 0 | 12 | 10 | 0 | 0 |
| 4 | 142 | 0 | 0 | 13 | 10 | 0 | 0 |
| 5 | 9 | 0 | 1 | 14 | 7 | 0 | 0 |
| 6 | 1 | 0 | 0 | 15 | 87 | 0 | 0 |
| 7 | 491 | 1 | 9 | 16 | 2 | 0 | 0 |
| 8 | 145 | 0 | 6 | 17 | 33 | 0 | 0 |
| 9 | 419 | 1 | 6 | | | | |

Every filter and executed/skip count matches Code’s 17 stored CP lines and the final plan. All 206 CP-2 identities and outcomes matched the retained readiness, collation, project setup, timing and Grok inventories, including every parameter row. CP-1 ran first. CP-8 ran exclusively and passed its historical-baseline witness. The timing lifecycle row verifies seven staged probe cases in each mode. All 24 skipped cases independently match the roster’s exact methods and multiplicities. Code’s full receipt summed to **1,614 executed, 1,613 passed**, one failed and 24 skipped. Its task summary undercounted executions and passes by 40.

Historical evidence includes the original Linux failure at `ecc9bfaa5` with actual readiness `Missing`, bounded-candidate failures at 1,003 candidates before the 1/2-row repair, and the U+20000 mismatch before the scalar-model fix. The two hold-writer failures and the three subsequent green trio TRXs matched their recorded SHA-256 hashes. Both services use the same SQL prefilter followed by the exact in-process identity. The tests check positive and negative persisted IDs, including POSIX twins, and assert the C database locale before exercising its fold. No production queue, receipt, recovery or input-wait code changed. Native Windows execution and all SourceLanding positive controls remain pending.

The range selector matched four kinds, zero migration/downgrade rows, and a separate native Windows class. Its 14 capped classes for `AgentService.cs` remain visible as selection debt. Evidence-diff found zero violations across 24 commits and one report entry. The 52m38s ordinary run exceeds the 30-minute performance threshold. CP-12 and CP-13 took 14m19s and 13m22s. Preserve these timings for the CARD-1514 performance Debug.

## Re-cut and merge

Twenty `-x` commits correspond to the 21-commit reviewed chain. The omitted `ff4a61f55` index was regenerated in `f42132a0b`. Thirteen of the 15 original files have identical final blobs, including all three production files. Fourteen have identical added and removed lines. The remaining differences are as follows.

- The roster contributes the same patch as before. Across tips, its four new hunks are a volume-retirement Linux entry, three reason constants with their comments, five Windows entries and 29 additional Windows entries. All 49 Windows identities are unique. All entries from both inputs are present.
- The only difference between the two change sets’ index edits is `LandReceiptScanHarness.cs`. Its classes/owed counts increase by one in both patches. The old counts move from 582/557 to 583/558. The re-cut counts move from 585/560 to 586/561. The full zero-context tip-to-tip patch contains the four roster hunks and all 121 generated-index hunks. [Retained patch](/work/review-evidence/8f467d8d/old-tip-to-recut.patch)
- The re-cut adds a 33-line checkpoint plan and a 98-line Code report. The report is the entire delta from Code’s tested `d382cedd5` to the reviewed tip.

Regeneration reproduced the committed index on the candidate and on both merge trees. Master `4e9b7efdb` merged to `4dc0ae9fb812dd648f51bc232ae28bc5ef3ba7ed`. Master `39ceb38c20e44e5db80cea274a86239eae0282c6` merged to `1b09a37a497fdbe0e30e5df513d0625180855f10`, also without conflicts. Neither merge needs an index edit. At land, verify the actual rebased tree with `pwsh -NoProfile -File scripts/build-slot.ps1 -Label land-index -- pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1`.

The excluded `RcInputOwnersMapTests.RealOwnerMapLiteralPathsExist` failed 1/1 on commissioning master with `missing-literal-path: scripts/qualification <= scripts/qualification/**`, matching Code’s candidate failure. Current master includes fix `391bda2d3`. A fresh run at `39ceb38c2` passed 9/9, the owner-map witness and all four variants of each failing impact method. [Current-master TRX](/work/review-evidence/8f467d8d/latest-master.trx)

The separately commissioned fourth pass supplements substantive round 3. The evidence subject is Code `3ad7f31d-02bd-49d8-8def-d07920b645e4`. The original landing owner is `84561585-b264-4731-afe4-2d7dfdb1fadd`. The first checkpoint launcher interpreted bare `90` as seconds and was stopped before any tests executed. The replacement used `90m` and fresh output names. Supplemental runs were limited to attribution and the moving master target. The full unedited CP lines, provenance, TRXs, case audit and validator output are retained. [Ordinary report](/work/review-evidence/8f467d8d/20261123-171911-7679/report.md) · [Validator output](/work/review-evidence/8f467d8d/ordinary-validation.log)
