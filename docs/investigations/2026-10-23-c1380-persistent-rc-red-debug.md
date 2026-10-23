# CARD-1571: persistent C1380 rc red, Debug ae4df578

Diagnosed the shared C1380 red, repaired two Windows auditor path defects, and verified the rc-specific correction. Master approval remains incomplete because the real author-time audit reached its existing 120-second deadline.

## Root cause and attribution

Native Windows reproduced `C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff` at rc **12a0b107f4c896979dda94b4afb628842f601883** and master **a2aedb57fc98422a1c1723f4e1f0d4cef40d70f3**: 1 executed, 1 failed on each, expected Verifier.Calls=0, actual=1. Primary cause: platform-independent drift in the generated audit metadata after CARD-1514 added the timing-probe project and changed test project definitions. Both manifests still declare 58 definitions; both current committed graphs contain 59. Production correctly requires a build when the count/fingerprint differs.

The original CARD-1537 committed-blob/mode fixture repair is present on both branches. Historical stopped-qualification and a37ceaf1 results remain valid historical reds, with their original costs and incomplete coverage. CARD-1537 was updated to link this distinct follow-up; new **CARD-1571**, id e754793f-6a52-47e9-822b-fdcaa4e791e7, records the metadata drift and Windows auditor blockers. No ordering/timing or Windows line-ending explanation is needed for the current C1380 graph mismatch.

| Graph | Checked-in digest / count | Current digest / count |
|---|---|---|
| rc | 637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E / 58 | D9C9124D076729FCBD8C0E4DEB4AC5A054C87C64D2345470A544048860C239F0 / 59 |
| master | 84E2796292E985EEECCB2381FFBE133A81376B81F7A042F4D7BAEEE9A2C517D3 / 58 | 60C8870DF97C1FA3231BA95C442C2CFD7B3E870A379D8A1501F7D4A6C1C6491A / 59 |

## Pushed repairs

Assigned branch **feat/card-task-ae4df578**, based on pinned master. Its initial unpublished rc base was moved to master with an empty rebase; no task commit was rewritten. Each code slice was committed and pushed before verification.

- **e794522d75bf6dc40f5c2adeab1b0761db0234c7**: `tools/Antiphon.LandGraphAudit/TargetPins.cs` normalizes the pins-file lookup with Path.GetFullPath. On Windows the previous mixed-separator Path.Combine result did not match the normalized tracked-file inventory. The exact-pin test first failed with the real tracked pins file reported untracked and its target pin missing. It now passes its existing positive and negative controls.
- **7c42927d09044f1ce1039e023089ae0e78104976**: `tools/Antiphon.LandGraphAudit/EvaluationSnapshot.cs` removes Path.GetPathRoot(root).Length when normalizing a relocated rootless repository path. Windows MSBuildProjectDirectoryNoRoot omits the drive root; trimming a leading separator did not normalize it. After the first repair, the exact-pin test exposed this independent false differential refusal. An untouched detached master tool build reproduced the same refusal on the owned harmless fixture; the repaired tool approved that unchanged fixture. The existing exact-pin and harmless-fixture TUnit methods then passed at the final code tip.

Landing policy, allowlists, assertions, test sources, and timeouts are unchanged. No new test was needed: existing committed tests supplied the red witnesses. No Review, Unit lane, whole class/suite, qualification, rc push, tags, release scripts, restart or deployment was run.

## Rc application

The first repair cherry-picks cleanly onto rc 12a0b107f4c896979dda94b4afb628842f601883. The rootless snapshot class is absent on rc, so 7c42927d09044f1ce1039e023089ae0e78104976 is master-only.

In the isolated rc worktree, its existing legacy `scripts/audit-land-verification-graph.ps1 -Update` completed successfully and generated the rc digest/count above. The combined delta (pins-path fix plus rc manifest refresh) compiled successfully and was verified natively. Exact reviewable patch: **C:\Antiphon\verification\task-ae4df578\rc-backport.patch**, SHA-256 **6336D96A0016C63069DE62B1EB794F8CA5299F5A231C81226332FB2D1E32D397**. Every adapted build/test launch records that same source-delta digest against rc HEAD. This is a scratch validation delta, not an rc commit or push.

Operator rc action: pick e794522d75bf6dc40f5c2adeab1b0761db0234c7 and regenerate the rc manifest through that branch's auditor, or apply the supplied combined patch at the pinned rc base. The patch already includes the code pick. A later rc tip needs its own audit refresh.

## Verification, retained reds and costs

All runs were serial, foreground and joined. Every driver used the host build-slot gate and the native owner, with unique logs/TRX. The exact filters, source commits/deltas, hard deadlines, native exit/cleanup facts and commands are in `<row>-launch.json`, `<row>-native.json` and `<row>.log` beneath **C:\Antiphon\verification\task-ae4df578**. TRX owns test outcomes; these diagnostic runs are not checkpoint-tool receipts or Clean/Full gate credit. Source stayed frozen throughout each run.

| Native TUnit launch | Executed | Passed | Failed | Not executed |
|---|---:|---:|---:|---:|
| master-final-green | 0 | 0 | 0 | 0 |
| master-harmless-final-green | 1 | 1 | 0 | 0 |
| master-pins-final-green | 1 | 1 | 0 | 0 |
| master-pins-green | 1 | 0 | 1 | 0 |
| master-pins-red | 1 | 0 | 1 | 0 |
| master-red | 1 | 0 | 1 | 0 |
| rc-pins-green | 1 | 1 | 0 | 0 |
| rc-red | 1 | 0 | 1 | 0 |
| rc-registry-green | 1 | 1 | 0 | 0 |
| rc-seed-green | 2 | 2 | 0 | 0 |
| rc-target-green | 1 | 1 | 0 | 0 |

The four red results are preserved evidence: rc C1380 baseline, master C1380 baseline, master exact-pin baseline, and the intermediate exact-pin run after the first repair that exposed the rootless-path defect. `master-final-green` is the failed combined-filter selection: zero tests, native exit 8, no credit. It was corrected with separate exact-method launches. No passing result was obtained by loosening an assertion or retrying a demonstrated failure unchanged.

Final master methods: `LandGraphEvaluationTests.C1483_S3_PinnedTargetMatchesOnlyItsHash` and `C1483_S1_HarmlessFixtureApproves`, at code tip 7c42927d09044f1ce1039e023089ae0e78104976. Rc methods: the original C1380 target-contained case, `C1385_RegistryMatchesNarrowGraph`, `C1537_SeedPreservesCommittedGraphWithCrLfCheckout` (two arguments), and `LandGraphEvaluationTests.C1483_S3_PinnedTargetMatchesOnlyItsHash`. The exact method filter is `/*/*/<Class>/<Method>`, one invocation per method; no discovery-only or zero-count result is execution evidence.

Standalone tool probes are separate from TUnit counts: untouched master harmless fixture refused with MSBuildProjectDirectoryNoRoot changed; repaired tool approved the same fixture. See baseline-harmless-red.log and master-harmless-green.log. Fixture provenance is create-harmless.ps1; no production repository was used as the fixture.

| Build | Errors | Warnings | Compiler elapsed |
|---|---:|---:|---|
| baseline-audit-tool-build | 0 | 0 | 00:00:05.72 |
| master-build | 0 | 1179 | 00:11:26.87 |
| master-final-build | 0 | 1179 | 00:10:17.84 |
| master-pins-build | 0 | 1179 | 00:11:03.51 |
| master-rootless-tool-build | 0 | 0 | 00:00:07.48 |
| rc-adapted-build | 0 | 1146 | 00:09:23.63 |
| rc-build | 0 | 1146 | 00:08:06.84 |

The five full Antiphon.Tests builds cover rc baseline, master baseline, first repair, final master code and adapted rc. Two small audit-tool builds establish untouched-base attribution and bootstrap the repaired author-time audit. Existing informational-version Git stamping changes generated assembly metadata between committed slices, so each parent rebuild recompiles the test graph. Build, slot and native wall costs remain separate in the retained receipts. Desktop SDK: 10.0.300; target net9.0 (audit tool net10.0). No build errors occurred; existing analyzer/compiler warnings remain recorded above.

## Incomplete master approval

The first normal master -Update refused the Windows pins path. With both repairs, precheck succeeded at 60C8870D.../59 and the full evaluator reached its existing **120-second** solution deadline (tool exit 2, wrapper exit 1). The partial canary shows copying was still incomplete at exit. This is incomplete author-time verification, not an approved graph. The wrapper left master's manifest/evaluation records unchanged. No manual digest, forged record, SDK substitution, or timeout increase was used.

Master's retained evaluation record names SDK 10.0.402; this desktop resolves 10.0.300. Complete the real audit on a suitable approved host/SDK, commit its generated records, then run the exact C1380 and registry checks against that committed source. Master C1380 was not re-run after the code-only fixes because its required approval metadata remains unchanged and unresolved. No master or release gate is green on the strength of this report.

## Custody and cleanup

All completed native owners report ChildrenExited=true, OutputDrained=true, CleanupComplete=true, RemainingProcessCount=0. Their shared native lock is released. No file-in-use failure occurred, so CARD-1539 lock-holder capture was not triggered.

Cleanup could not complete: automatic approval review rejected recursive deletion of the uniquely identified audit canary and a task-owned build output as **blocked by policy**, with no further reason. No cleanup bypass was attempted. Retained roots are the assigned worktree, scratch rc and baseline worktrees, and canary **C:\Antiphon\worktrees\antiphon-landgraph-canary-6b088cc82c234d13a20d7936892e0651**. The canary contains this audit's unique temporary-manifest nonce and exact digest/count; see canary-custody-proof.json. Producer output inventories are rc-output-inventory.json, master-output-inventory.json, rc-adapted-output-inventory.json and baseline-output-inventory.json. They identify 60 assigned-worktree, 30 rc and one baseline alternate bin directory retained for operator cleanup. Ordinary obj caches also remain. The rc validation patch is preserved externally; tracked scratch changes are restored after verification.

--- next stage ---
next: decide
handoff: Choose a suitable approved host/SDK for the complete master author-time audit, or commission the Windows full-repository canary/deadline performance work. Keep master approval incomplete. The two path fixes are pushed and narrowly verified; rc has a compiled, natively verified adaptation for operator picking. Cleanup needs an allowed operator path because automatic approval review rejected deletion. No Review or release action was commissioned.
artifact: C:\Antiphon\verification\task-ae4df578\report.md

[antiphon-report:ae4df578 done]
