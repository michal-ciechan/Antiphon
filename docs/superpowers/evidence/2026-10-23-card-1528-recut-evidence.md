# CARD-1528 master re-cut equivalence evidence

Re-cut completed without conflicts. Ten reviewed commits were cherry-picked in original order and each was pushed separately. No build, test, evaluator Update/Check, mutation, desktop operation, restart or live Windows qualification operation ran.

Task: 1e78def7-fa73-4e97-867a-5de638d5a02d. Branch: feat/card-task-1e78def7. Worktree: /work/worktrees/task-1e78def7.
Original Code/landing owner: 2d02464e-b07c-4d9a-902b-76080288eff3. This re-cut is an adoption source; it does not replace the original landing owner.
Task base: 71e578108d6a9604f6f8d7d9f1aae5e45dbd0130. Original plan/source base: c332327a2ba462a4842a31499711a8632e984b19.
Original reviewed tip: 1a30f3bebf5e08055644f375157b8935ba49027e. Re-cut source tip (before this report-only commit): 9854a2e1e716a2334725c9647710a8502fe2ec7b.
Fetched master used for proof: 617e1d5db0e8f00b8ddb7480b7c4c4ab9248a6de. Master advanced after the assigned task base; no merge/rebase/reset was performed.
Plan: docs/superpowers/plans/2026-10-23-card-1528-land-audit-filesystem-metadata-plan.md.

## Commit and patch identity

Selection: git log --reverse --no-merges c332327a2..1a30f3beb. All ten stable patch IDs match, including the plan amendment commit; no exception was needed. No conflict resolution changed a patch. The plan was already present on master with the original contents, so its verification amendment applied cleanly and retains the reviewed branch blob.

| Reviewed commit | Re-cut commit | Stable patch ID (both) | Result |
|---|---|---|---|
| 1a5c1e04e93aebc6227c283e2fdc3be2a268a24e | 8121562d715e00215b8e4c7c26cd09272ab52d07 | 324a25ac75632a9c924254c16d74439bf5bcdaaa | identical |
| bc61e27410a9fd52add47df0a7d7c17acf31dc76 | 95f6ae082b963cd0004534d63871a51a09f4f721 | 9285a7035e95e67714a44d7be61ea4d85a3fa4f4 | identical |
| 2f142aa9c55d947f00bde6989df149663de728df | 45097d31871111eab4fdba02392c373e42518061 | af0807a83f8f75bc8249d126fd0f0daefcf82cc0 | identical |
| 2a90839c7738f1d0616a97f388e14453f074a11e | 9d28ae5ee52d4192c92d0544843c709c7f1f1abc | 622333176042b49d91f708c3509cd4d0d7ff0803 | identical |
| 4df958549ef4440e1b65f5d7b4e57cfafcc85c92 | 297f74ac77cb651221b3a595b33dcb68ee6fb689 | 29c889d2576b3c2056ccd6955ee1f3373d136e56 | identical |
| 2e9c5028238d255c7d2524a7484ff762c91d682a | c6b1cb72f603bfd847bf94560140749db0cd22af | 2fa6888fe2d1e44b14f9d278b15a72784c6a35ac | identical |
| 230300764d43a28c9d35e1dbea0df0e3b2985338 | 730dbf8f43038d08f9b8ff16f4e7ab1857145679 | 9c08ef8bdc3ef0e993c11b719dab4022b452f2e6 | identical |
| edc256ec97e2223c92fac2c18faf81dd0545c23e | c811b150af8fbd5e5f736a01ad3500b8d9981e57 | 77df2e1c7db78768fefbe0b036d66439a339197a | identical |
| b2493845680f43bd93d82fa0602c41c5f8be5262 | 11274f9896c202ac199550f1e92fd88e695a163b | 87b38acd31b13c56eb1897fe2115038c0fd9e467 | identical |
| 1a30f3bebf5e08055644f375157b8935ba49027e | 9854a2e1e716a2334725c9647710a8502fe2ec7b | 60cd7d08ac989b13e2c37c8e71d785621510b79f | identical |

## Every touched-file blob

Fourteen paths were touched by the ten reviewed commits. Thirteen final blobs are identical, including every executable/test/fixture file and the plan. The sole nonidentical path preserves a separately proved master-only documentation delta.

| Path | Reviewed blob | Re-cut blob | Result |
|---|---|---|---|
| docs/orchestration-loop.md | bb5933a068efcf77ea3b72272fa36fb7a773a781 | 7568be5802c61525165bb74c3300bca58e4186f0 | master-newer delta (proved below) |
| docs/superpowers/mutations/2026-10-23-card-1528-candidates.md | bb3d58662dda247b4cbe448ae175de835c416de2 | bb3d58662dda247b4cbe448ae175de835c416de2 | identical |
| docs/superpowers/plans/2026-10-23-card-1528-land-audit-filesystem-metadata-plan.md | 0e52865f6347d5af16a6a838e16c9be2fe3f911b | 0e52865f6347d5af16a6a838e16c9be2fe3f911b | identical |
| docs/superpowers/reports/2026-10-23-card-1528-code.md | a42c98ffc46475b14b85650cf9ec453ba01eb940 | a42c98ffc46475b14b85650cf9ec453ba01eb940 | identical |
| scripts/audit-land-verification-graph.ps1 | be390dd89e0c72fff29d05a77238feee6d5cc2fc | be390dd89e0c72fff29d05a77238feee6d5cc2fc | identical |
| scripts/test-land-graph-c1528-fixtures.ps1 | 0225542a3a616c4e10c4f2a93f42fbf10234ce93 | 0225542a3a616c4e10c4f2a93f42fbf10234ce93 | identical |
| tests/Antiphon.Tests/Application/Fixtures/land-graph-c1528.json | c5df4d187231313a7299e153b5245c8ea5a539c1 | c5df4d187231313a7299e153b5245c8ea5a539c1 | identical |
| tests/Antiphon.Tests/Application/LandGraphEvaluationFilesystemTests.cs | 9e95bfbac5422006d5a01e8250399b97a75ed523 | 9e95bfbac5422006d5a01e8250399b97a75ed523 | identical |
| tests/Antiphon.Tests/Application/LandGraphEvaluationProbeTests.cs | eeb1d4a6c82774a3d834372505fa66b4022b4c93 | eeb1d4a6c82774a3d834372505fa66b4022b4c93 | identical |
| tests/Antiphon.Tests/Application/LandGraphEvaluationScriptTests.cs | c14e18cec07621303aded6a2bd018311f6230acc | c14e18cec07621303aded6a2bd018311f6230acc | identical |
| tools/Antiphon.LandGraphAudit/BuildInputPolicy.cs | ee35a7566e55c882648459168a611c952a4fa078 | ee35a7566e55c882648459168a611c952a4fa078 | identical |
| tools/Antiphon.LandGraphAudit/EvaluationExpressionPolicy.cs | a973598966642ff162e443cec3894b4f13c8fe78 | a973598966642ff162e443cec3894b4f13c8fe78 | identical |
| tools/Antiphon.LandGraphAudit/SolutionAudit.cs | d130ac4cfa81160eec6037dfef8b89841bbea632 | d130ac4cfa81160eec6037dfef8b89841bbea632 | identical |
| tools/Antiphon.LandGraphAudit/TargetPins.cs | 63cd5aea14b53e0af1d3048f0cc999458163471c | 63cd5aea14b53e0af1d3048f0cc999458163471c | identical |

docs/orchestration-loop.md differs only by inherited master commit e17c636c1168c131b7ac4e99ccffc02653ab0a13 (CARD-1526): native rc startup probe/recovery wording and the OS-branch witness checklist. Its reviewed-to-re-cut diff is exactly the original-base-to-task-base diff after removing only the Git index line; no hunks or content were ignored. The CARD-1528 owner amendment retains its identical per-commit patch ID.

## Complete reviewed-to-re-cut delta

The required git diff 1a30f3beb HEAD -- . ':!docs/superpowers/plans/2026-10-23-card-1528-land-audit-filesystem-metadata-plan.md' contains 53 inherited master paths before this evidence file is added. For all paths except the owner document, ls-tree proves reviewed blob/mode = original-base blob/mode and re-cut blob/mode = assigned-task-base blob/mode, including absent entries. The owner document has the exact inherited-diff proof above. This report is the sole subsequent new path.

| Master-newer path | Proof |
|---|---|
| .antiphon/task-15cca245.md | reviewed = original base; re-cut = task base |
| .antiphon/task-75c32a51.md | reviewed = original base; re-cut = task base |
| .antiphon/task-ddd3fc81.md | reviewed = original base; re-cut = task base |
| .antiphon/task-f93ec86b.md | reviewed = original base; re-cut = task base |
| docs/antiphon-api.md | reviewed = original base; re-cut = task base |
| docs/apphost-runbook.md | reviewed = original base; re-cut = task base |
| docs/bootstrap.md | reviewed = original base; re-cut = task base |
| docs/investigations/2026-10-23-card-1514-s3-code-evidence.md | reviewed = original base; re-cut = task base |
| docs/investigations/2026-10-23-card-1529-warm-handoff-compact-debug.md | reviewed = original base; re-cut = task base |
| docs/investigations/2026-10-23-checkpoint-stamp-64mib-debug.md | reviewed = original base; re-cut = task base |
| docs/release-gates.md | reviewed = original base; re-cut = task base |
| docs/superpowers/evidence/2026-10-23-card-0151-s1-code.md | reviewed = original base; re-cut = task base |
| docs/superpowers/evidence/2026-10-23-card-0151-s1-fixture-repair.md | reviewed = original base; re-cut = task base |
| docs/superpowers/mutations/2026-10-23-card-1486-s5-candidates.md | reviewed = original base; re-cut = task base |
| docs/superpowers/mutations/2026-10-23-card-1514-s3-candidate.md | reviewed = original base; re-cut = task base |
| docs/superpowers/mutations/2026-10-23-card-1526-os-witnesses.md | reviewed = original base; re-cut = task base |
| docs/superpowers/plans/2026-10-23-card-0931-or-filter-validation-plan.md | reviewed = original base; re-cut = task base |
| docs/superpowers/plans/2026-10-23-card-1514-s3-code-verification.md | reviewed = original base; re-cut = task base |
| docs/superpowers/plans/2026-10-23-card-1518-permission-wait-classification-plan.md | reviewed = original base; re-cut = task base |
| docs/superpowers/plans/2026-10-23-card-1526-os-gated-code-real-type-witness-plan.md | reviewed = original base; re-cut = task base |
| docs/superpowers/plans/2026-10-23-card-1526-repair2-evidence.md | reviewed = original base; re-cut = task base |
| docs/superpowers/plans/2026-10-23-card-1531-checkpoint-admission-budget-plan.md | reviewed = original base; re-cut = task base |
| docs/superpowers/plans/2026-10-23-card-1532-streaming-source-stamps-plan.md | reviewed = original base; re-cut = task base |
| docs/testing-and-build.md | reviewed = original base; re-cut = task base |
| scripts/check-os-witnesses.ps1 | reviewed = original base; re-cut = task base |
| scripts/fixtures/nightly/c1514-checkpoint-split-listing.txt | reviewed = original base; re-cut = task base |
| scripts/lib/nightly-class-costs.json | reviewed = original base; re-cut = task base |
| scripts/test-nightly-tests.ps1 | reviewed = original base; re-cut = task base |
| scripts/test-os-witnesses.ps1 | reviewed = original base; re-cut = task base |
| server/Application/Dtos/BoardDtos.cs | reviewed = original base; re-cut = task base |
| server/Application/Services/AgentTaskReplyService.cs | reviewed = original base; re-cut = task base |
| server/Application/Services/BoardService.cs | reviewed = original base; re-cut = task base |
| server/Application/Services/CardRevisionLog.cs | reviewed = original base; re-cut = task base |
| server/Application/Services/CardService.cs | reviewed = original base; re-cut = task base |
| server/Application/Services/DelegationReportFormatter.cs | reviewed = original base; re-cut = task base |
| server/Bundles/stage-code.md | reviewed = original base; re-cut = task base |
| server/Bundles/stage-review.md | reviewed = original base; re-cut = task base |
| server/Domain/Entities/Card.cs | reviewed = original base; re-cut = task base |
| server/Domain/Entities/CardRevision.cs | reviewed = original base; re-cut = task base |
| server/Infrastructure/Data/AppDbContext.cs | reviewed = original base; re-cut = task base |
| server/Migrations/20261023030321_AddCardEstimateDays.Designer.cs | reviewed = original base; re-cut = task base |
| server/Migrations/20261023030321_AddCardEstimateDays.cs | reviewed = original base; re-cut = task base |
| server/Migrations/AppDbContextModelSnapshot.cs | reviewed = original base; re-cut = task base |
| tests/Antiphon.Tests/Application/AgentTaskLandCheckpointMatrixTests.cs | reviewed = original base; re-cut = task base |
| tests/Antiphon.Tests/Application/AgentTaskLandCheckpointWorkerTests.cs | reviewed = original base; re-cut = task base |
| tests/Antiphon.Tests/Application/BlockedReplyReportRetentionTests.cs | reviewed = original base; re-cut = task base |
| tests/Antiphon.Tests/Application/CardCorrectionIntegrationTests.cs | reviewed = original base; re-cut = task base |
| tests/Antiphon.Tests/Application/CardEstimateTests.cs | reviewed = original base; re-cut = task base |
| tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs | reviewed = original base; re-cut = task base |
| tests/Antiphon.Tests/ProcessSpawnLimitTests.cs | reviewed = original base; re-cut = task base |
| tests/Antiphon.Tests/Scripts/C1514LandingSplitBudgetTests.cs | reviewed = original base; re-cut = task base |
| tests/Antiphon.Tests/slow-tests-allowlist.txt | reviewed = original base; re-cut = task base |

## Plan and generated records

Plan diff origin/master..re-cut: 50	5	docs/superpowers/plans/2026-10-23-card-1528-land-audit-filesystem-metadata-plan.md. It is exactly the original Code verification amendment (+50/-5); reverse diff HEAD..origin/master removes that amendment. Normalized patch text is identical to original plan-base..reviewed. The plan blob equals the reviewed branch blob.
All three generated graph records were compared as Git blobs, never regenerated or edited:

| Record | Blob | Result |
|---|---|---|
| server/Application/Services/LandVerificationBuildGraph.manifest.txt | 6f3e5a8a08c4088f7e7bb46f01a13a9268a84de1 | identical across master, reviewed, re-cut |
| server/Application/Services/LandVerificationBuildGraph.evaluation.txt | f6edc97afec0065fac6203131abcdf3bbc728a18 | identical across master, reviewed, re-cut |
| server/Application/Services/LandVerificationBuildGraph.pins.txt | ed6f685830ef82613daf6a8503b08159c84ec5de | identical across master, reviewed, re-cut |

Manifest contents:
```text
84E2796292E985EEECCB2381FFBE133A81376B81F7A042F4D7BAEEE9A2C517D3
58
```
Evaluation record contents:
```text
84E2796292E985EEECCB2381FFBE133A81376B81F7A042F4D7BAEEE9A2C517D3
10.0.402
ed6f685830ef82613daf6a8503b08159c84ec5de
```

## Read-only gates and verification boundary

git merge-tree --write-tree 617e1d5db0e8f00b8ddb7480b7c4c4ab9248a6de 9854a2e1e716a2334725c9647710a8502fe2ec7b exited 0, tree c497138df566aeea999fd19e7df05b3a754eb159. No conflicts.
The full task-base..source-tip evidence guard exited 0:
```text
EVIDENCE range base=71e578108d6a9604f6f8d7d9f1aae5e45dbd0130 head=9854a2e1e716a2334725c9647710a8502fe2ec7b
EVIDENCE result commits=10 entries=0 violations=0 base=71e578108d6a9604f6f8d7d9f1aae5e45dbd0130 head=9854a2e1e716a2334725c9647710a8502fe2ec7b
```
After this report-only commit, the guard must also run over the full task-base..final-pushed-tip range; that exact SHA/result belongs in the final task report. Fresh fetch/merge-tree and remote-tip confirmation likewise follow publication.

No builds or tests were run, as explicitly commissioned. slot=n/a; waited=0s (no slot requested). No repeat-proof runs. No CHECKPOINT lines or fresh receipts/TRX were created; none are claimed. No alternate output directories were created.
The brief reports historical Final Review 7a42a520 as CLEAN at original tip 1a30f3bebf5e08055644f375157b8935ba49027e, receipt 20261023-070333-3ba1, CP-1..CP-6 = 9/2/9/2/3/1 (26/26). This is attributed commissioning evidence, not a fresh receipt validation or a source-clean assertion for this new SHA.

| Ordinary IDs | Re-cut outcome | Required confirmation |
|---|---|---|
| V-1, V-2, V-3, V-4 | Not run by re-cut task | CP-1, 9 results |
| V-5, V-6 | Not run by re-cut task | CP-2, 2 results |
| R-1, R-2 | Not run by re-cut task | CP-3, 9 results |
| R-3 | Not run by re-cut task | CP-4, 2 results |
| V-7, R-4 | Not run by re-cut task | CP-5, 3 results; CP-6, 1 result |

Confirmation Review owns CP-1..CP-6 against the final pushed SHA through the checkpoint tool; inspect fresh TRX and nonzero exact counts and retain unedited CHECKPOINT lines/provenance. Required execution remains pending for that SHA. No whole assembly/Unit run is commissioned by this Git-only brief.
PC-1 (five Exists variants), PC-2 (custom/default metadata), PC-3 (false-absent), PC-4a (nested), PC-4b (unknown-type and unknown-member separately), PC-4c (chain), PC-4d (instance), PC-5 (present/projected/metadata x absent/prior x separate manifest/evaluation publication), and PC-6 (removed-docs plus dynamic/escaped removal controls) all remain pending for method-scoped SourceLanding Mutation. Existing S5 controls remain pending independently. Candidate pack: docs/superpowers/mutations/2026-10-23-card-1528-candidates.md.
GET /api/runner-defaults and GET /api/session-runners were read successfully. Effective runner selection remains unpinned; no dispatch or host change occurred. Restart: none.

## Confirmed rc source ordering

The rc sequence uses ORIGINAL reviewed commits in S4 -> S5 -> CARD-1528 order, never any of this re-cut branch or its task-base master changes. Apply source/test paths only and drop plan Markdown (and other documentation paths). Documentation-only commits are omitted. S5 final boundary contains generated master records: do not copy those onto rc; regenerate and qualify using the resulting rc tree under its separate commissioning. The list below records original identities, including omitted documentation/boundary entries so no prerequisite is silently lost. This task does not apply any rc pick or claim current rc activation.

| Stage | Original reviewed commit, in order | Source-only handling |
|---|---|---|
| S4 | 3549efe64f483a0df0e56407527f44f2c2608d99 | retain source/test paths; drop documentation and plan paths |
| S4 | c94661b7a349576e3f3ecf935ae4c57b88f3a63b | retain source/test paths; drop documentation and plan paths |
| S4 | 7a2d8e516aead366cae53809a89e8081374bdd40 | retain source/test paths; drop documentation and plan paths |
| S4 | 4b1554c9c4f8cc0c9e1443994003460d24f046fa | retain source/test paths; drop documentation and plan paths |
| S4 | f249d595af8cea8cf9b318bea9853d14ab7646ab | retain source/test paths; drop documentation and plan paths |
| S4 | 204e3dcff82a9080f9fcd668a152ebcb22292423 | retain source/test paths; drop documentation and plan paths |
| S4 | b36c58d9810757724cc9c74b108d9b3d6e159f65 | retain source/test paths; drop documentation and plan paths |
| S4 | e78b13a3393bce71790c3b79a0526e45fa58c875 | retain source/test paths; drop documentation and plan paths |
| S4 | fd08ab50057b179d5d98eecc8f533e8ed474dfed | retain source/test paths; drop documentation and plan paths |
| S5 | 22c8d94857cbfae3cdac923f47b7b55b78a8a604 | retain source/test paths; drop documentation and plan paths |
| S5 | 70cb6c020d0ee265b899086d4a00fe3b4dbc6f77 | retain source/test paths; drop documentation and plan paths |
| S5 | 7a0d9ab57a9a26e4bc64fdfd349ed256ca028124 | retain source/test paths; drop documentation and plan paths |
| S5 | c60d10d14dbc4490dd289ae9c3923633da25ab22 | retain source/test paths; drop documentation and plan paths |
| S5 | efec8530442bc540f8b09fb6e9f8d9a9c287dfa6 | retain source/test paths; drop documentation and plan paths |
| S5 | bd215d7b4f559db69fbf4d7363417fce0f2c2467 | boundary only: regenerate rc records; never copy master records |
| CARD-1528 | 1a5c1e04e93aebc6227c283e2fdc3be2a268a24e | retain source/test paths; drop documentation and plan paths |
| CARD-1528 | bc61e27410a9fd52add47df0a7d7c17acf31dc76 | retain source/test paths; drop documentation and plan paths |
| CARD-1528 | 2f142aa9c55d947f00bde6989df149663de728df | retain source/test paths; drop documentation and plan paths |
| CARD-1528 | 2a90839c7738f1d0616a97f388e14453f074a11e | retain source/test paths; drop documentation and plan paths |
| CARD-1528 | 4df958549ef4440e1b65f5d7b4e57cfafcc85c92 | omit: documentation only |
| CARD-1528 | 2e9c5028238d255c7d2524a7484ff762c91d682a | retain source/test paths; drop documentation and plan paths |
| CARD-1528 | 230300764d43a28c9d35e1dbea0df0e3b2985338 | retain source/test paths; drop documentation and plan paths |
| CARD-1528 | edc256ec97e2223c92fac2c18faf81dd0545c23e | retain source/test paths; drop documentation and plan paths |
| CARD-1528 | b2493845680f43bd93d82fa0602c41c5f8be5262 | omit: documentation only |
| CARD-1528 | 1a30f3bebf5e08055644f375157b8935ba49027e | retain source/test paths; drop documentation and plan paths |

--- next stage ---
next: review
handoff: Light confirmation Review of the final pushed re-cut tip: verify equivalence evidence, run CP-1..CP-6 (26 expected), validate fresh receipts/TRX and source provenance, and retain all Mutation controls pending. Preserve the original Code landing owner and original S4 -> S5 -> CARD-1528 rc source sequence.
artifact: docs/superpowers/evidence/2026-10-23-card-1528-recut-evidence.md
