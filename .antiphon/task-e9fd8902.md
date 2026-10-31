# CARD-1609 continuation 11

Assigned baseline 8e1dea3e6931ef96b98c88ceb8e9a3b8bdda4f53; branch feat/card-task-e9fd8902. S5 remains incomplete. Production deletes remain disabled; first production action is preview-only.

## Long-Apply admission red

New assertions were committed and pushed before repair at 445a22abe88002218c8c9f870b39621fdac563ff. Existing immutable-receipt expiry, future skew and unsupported schema/policy controls remain unchanged. The added real host Apply advances wall clock during the actual audit; it reaches the intended assertion red because expiry is checked after the scan. No fixture/build error or delete occurred.

The unchanged product at the assigned baseline plus the new witness executed 1/0/1/0; method46.909s, compiler210.06s, scheduler build274.273s, row58.213s. Granted slots: build0s wait,row30s. Run wall401.038s, charged371.030s. Terminal wait exit1, clean source and producing build verified.

```text
CHECKPOINT ADMIT-RED commit=445a22abe88002218c8c9f870b39621fdac563ff build=ok filter=/*/Antiphon.Tests.Scripts/VolumeRetirementReceiptTests/Freshness_and_policy_version_are_rechecked executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-e9fd8902/.antiphon/checkpoints/20261031-054440-4134/rows/ADMIT-RED/run.trx slot=granted waited=30s dirty=0 source=445a22abe88002218c8c9f870b39621fdac563ff sourceState=clean buildSource=verified
```

Expected0, actual2, diagnosis RecycleGitAuditUnknown reason=preview-required. The test does not claim long authenticated-origin renewal: that remains separate bridge/coordinator work. The second added control (expired before any expensive audit) was not reached in this red run.

Tool bootstrap was leased,5 warnings/0 errors,8.85s compiler/10s held. Gated plan import succeeded16 rows,45s admission/5s held, with the expected CP-3 importer-default and CP-11 duplicate cross-host-build warnings. Imported defaults are not execution budgets. Focused red uses4m row/12m charged/57m wall bounds, fresh bin-c1609-11-admit-red, UseAppHost=false,TEMP=/tmp. Four-minute bound is provisional collection, not measured p95. Full phases live at /tmp/c1609-11-admit-red-phases; unrecorded internal phases remain unknown.

## Outstanding ordered scope

Complete long-scan/manual/v2 acceptance (including publication renewal and single-owner exchange), then fresh full CP-15, then the whole closed Linux roster and portable Linux roster/docs checks. Windows CP-11/12/13 and production preview/audit remain owed. Do not add focused passing counts to historical red to claim a fresh row.

## Apply repair and acceptance execution (owner guard refused qualification)

Repair a8c5683698db4cf734fb1ded4967a4c4ffef5cc0 admits exact immutable Ready/authority bytes before the expensive Apply audit, under the existing owning shell/FD8 lifecycle. Consumption still requires matching policy/daemon/incarnations and complete inventory, and per-delete publication/live checks are unchanged. Cached authority is reset at entry, bound to BASHPID and Preview SHA-256, and cannot cross Resume/invocations. UTC elapsed time must agree with monotonic time within60s. An append-only admission record is not Ready and cannot delete. No delete path was added.

The fake long-scan clock now advances wall and monotonic time together. Added controls prove an already expired Preview refuses before the first audit despite caller-supplied cache variables, and an unmatched wall-clock jump refuses without consumption/effects. No prior assertion or timeout was removed/relaxed.

Acceptance run20261031-055353-0c21 executed4/4/0/0, with clean source/build provenance. J4=96.135s,J10=270.073s,J8=476.848s,W1=35.102s. Compiler/build phases are retained; scheduler build292.021s, row883.394s, host testsWall878.264s; no slot wait. Total1206.226s charged/wall. The run is NOT GREEN: owner reads timed out from06:08:29 through06:13:40, latched owner-unverified, terminal exit7. Source validation correctly refused report_failed (exit2). Do not relabel this report or claim qualified acceptance from its passing TRX.

```text
CHECKPOINT ACCEPT-A commit=a8c5683698db4cf734fb1ded4967a4c4ffef5cc0 build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementReceiptTests*)|(VolumeRetirementWrapperTests*)/(Freshness_and_policy_version_are_rechecked*)|(Manual_attestation_is_exact_and_cannot_waive_findings*)|(Journal_intents_and_partial_resume_are_exact*)|(Retire_temp_uses_current_proof_protocol*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-e9fd8902/.antiphon/checkpoints/20261031-055353-0c21/rows/ACCEPT-A/run.trx slot=granted waited=0s dirty=0 source=a8c5683698db4cf734fb1ded4967a4c4ffef5cc0 sourceState=clean buildSource=verified
```

Fresh direct bounded health/owner GETs recovered200 after terminal completion: same task/session, Dispatched/Running. No owner identity/token/environment was removed, no bypass or receipt rewrite was used. A preliminary mistyped expected SHA was refused before any build/test, zero credit; corrected invocation produced this run. OS inventory/lint passed at this repair for the three changed source paths, with unchanged Linux test gates and no OS selector/startup change.

## Pre-test V2 fixture correction

29da3530ee9fbda5575fd6f9850e29d3e3fb5da6 adds real wrapper/bridge/real-Git ancestry and renewal acceptance plus the no-intervening-effects single-inventory witness. The old c1609-wrapper-cases.mjs remains byte-identical; the new driver is c1609-wrapper-v2-cases.mjs. No earlier vector was removed. Only a disposable host fixture copy tightens final publication age900s to5s; a6s real wait after the real audit forces renewal. Production policy is unchanged.

Run20261031-061732-64ec was stopped through the supported tool before tests after inspection found copied C994 cleanup fields would overwrite the wrapper's real operation/DryRun. Stop joined and removed the owned tool copy; terminal wait returned6 because no terminal report was published. No test started and no red/green credit is claimed. No matching executor/build process remains. Its partial fresh output is retained until final literal cleanup. The next fixture excludes those fixed fields, preserving actual bridge exports. This is an execution-efficiency deviation, not a passing/failed assertion receipt.

## Single-inventory red and wrapper fixture preflight failure

Run20261031-061853-3410 at e70b39e1ada13d31fa104b958a137dd6a744b43e reached terminal exit1:2 executed/0 passed/2 failed/0 skipped, clean source/build verified. J4 is intended assertion red: expected one content helper after Ready, observed two, with no intervening container removal. W1 is NOT a valid renewal red: its fixture supplied an explicit project id without the fake API's corresponding detail route, so it stopped at RecycleProjectUnresolved. No product attribution/acceptance credit for that failure.

```text
CHECKPOINT V2-RED commit=e70b39e1ada13d31fa104b958a137dd6a744b43e build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementReceiptTests*)|(VolumeRetirementWrapperTests*)/(Freshness_and_policy_version_are_rechecked*)|(Retire_temp_uses_current_proof_protocol*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-e9fd8902/.antiphon/checkpoints/20261031-061853-3410/rows/V2-RED/run.trx slot=granted waited=0s dirty=0 source=e70b39e1ada13d31fa104b958a137dd6a744b43e sourceState=clean buildSource=verified
```

Build368.509s,row153.603s,test147.474s,total552.925s; slots granted with0s wait. J4=76.832s,W1=70.418s (fixture failure). No deadline fired. The fixture now uses the existing default project resolution and registers only wrapper-created exact ignored c727 evidence roots for review-file downloads; protected host records remain inside the owned fixture. This expands no production trust boundary.

The J4 repair keeps the first full audit under FD8, and keeps the second audit/comparison for main or actual temp-container effects. With no intervening container effects only that redundant second scan is omitted; all live guards, incarnations, full preview comparison, helper cleanup, publication freshness and exact deletion checks remain. Existing test identities/counts and all refusal vectors remain. New J10 real-clock-seam assertions require manual Preview admission before a long scan, preserving the grant bytes and measuring freshness from completion; these precede that repair.

## Qualified intended renewal/manual reds and single-inventory green

Run20261031-063113-491c at88ab37c77aed90118a98862d3e5835b167af0735 reached terminal exit1 with3/1/2/0, clean source/build verified. J4 passed the complete expanded admission/clock matrix and the one-audit assertion. W1 now reaches the intended real wrapper/private-A->B publication gate and refuses proof-stale after the six-second scan/stricter five-second fixture age. J10 reaches intended long manual Preview admission red: authority expired during the actual scan, producing ReceiptUnavailable. Both intended red assertions executed without fixture/build failures and zero target effects.

```text
CHECKPOINT V2-MANUAL-RED commit=88ab37c77aed90118a98862d3e5835b167af0735 build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementReceiptTests*)|(VolumeRetirementWrapperTests*)/(Freshness_and_policy_version_are_rechecked*)|(Retire_temp_uses_current_proof_protocol*)|(Manual_attestation_is_exact_and_cannot_waive_findings*) executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-e9fd8902/.antiphon/checkpoints/20261031-063113-491c/rows/V2-MANUAL-RED/run.trx slot=granted waited=15s dirty=0 source=88ab37c77aed90118a98862d3e5835b167af0735 sourceState=clean buildSource=verified
```

Scheduler build260.400s,row222.004s,tests215.362s,total552.525s charged/567.530s wall. Build wait0,row15s. Method samples J4about106s,J10=43.121s,W1=65.880s; use TRX for exact values. No deadline fired. Current full-row p95 remains unknown. No source definition or policy deadline changed. The next H1 expansion checks exact required commit/tree/blob/original-tag semantics before the renewal implementation, preserving all v1 assertions and sharing one tiny real-Git seed.

## Required-object producer red

At0c22785c48aeb37a5b84e2cd80d4026296fcd466, OBJECTS-RED reached the intended unpublished-commit assertion: the producer ignored the requested object and emitted an authorizing proof, exit0 where refusal was required. The advanced-origin ancestor control passed first. Later tree/blob/tag/wrong-type/missing controls were not reached in this red. Feature detection kept new parameters off the unchanged producer, so no unknown-argument/missing-method/build failure earned red credit.

```text
CHECKPOINT OBJECTS-RED commit=0c22785c48aeb37a5b84e2cd80d4026296fcd466 build=ok filter=/*/Antiphon.Tests.Scripts/VolumeRetirementHostProofTests/Host_proves_complete_origin_ancestry executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-e9fd8902/.antiphon/checkpoints/20261031-064338-e98e/rows/OBJECTS-RED/run.trx slot=granted waited=0s dirty=0 source=0c22785c48aeb37a5b84e2cd80d4026296fcd466 sourceState=clean buildSource=verified
```

Terminal exit1, clean source/build verified. Method3.623s,row8.884s,build388.114s,total456.702s charged/456.703s wall; no slot wait or fired deadline. H1 retains its original v1 assertions. All required-object cases share one tiny origin/work seed; individual fresh proof scratch/output isolates the producer subject.

## Coordinated renewal candidate

The candidate moves private-origin inventory into the same FD8-owning host process as Preview/Apply. The credential-capable client answers nonce/phase/digest-bound proof requests over that process's stdin/stdout, fetches only approved origins in origin-only scratch, and proves every audited object by exact type and the preserved commit/tag/tree/blob rules. Complete per-repo required-object coverage is checked. Objects travel by protected files and stdin, never giant argv. Unchanged advertisements reuse only that phase's origin-only bundle; changed advertisements fetch a new graph and reprove the same objects. Network/graph work shares600s per phase; each confirmation is bounded10s. Unknown/EOF/malformed responses refuse. Live guards are read after network work, and controller confirmation precedes Ready and each exact target intent. No credential or ambient host Git config enters a helper.

Initial inventory, immutable object-roster copies and exchange/control/admission records live under the protected host operation and phase nonce. Host graph files are hash-compared on both sides and flushed before authority advances. Preview manual authority is admitted before the long phase and rechecked byte-for-byte against the same fingerprints at completion; wall/monotonic disagreement still refuses. Fresh invocations and Resume receive no cached authority. Main v1 and all existing exact-name/refusal/delete/partial-resume gates remain. The file-backed SSH adapter now forwards the same interactive protocol without a nested lock. No production rollout or tool activation is performed.

Bash, Node and PowerShell parsing, ASCII transport checks and git diff --check passed before this candidate commit. New code is not yet qualified; intended reds above are preserved. Definition files are unchanged; -Update has not run.

## Renewal candidate verification and syntax correction

Run20261031-071038-170d at7650549b0162404a71ddc8ffece57d9d78c7f2b7 ended RED1,5/3/2/0, clean source/build verified. H1 exact-object matrix passed35.762s; J4 expanded long Apply passed104.532s; J8 complete public Close/Resume passed442.043s. W1 and J10 both failed loading c590-real.ps1 because its new proof-export subexpression lacked a closing parenthesis. This is my introduced syntax defect, not intended assertion red. The earlier statement that PowerShell parsing passed was incorrect: a chained shell command masked its status. The fix is one closing parenthesis; a separate terminating module-load command now passes. No test deadline/assertion was weakened.

```text
CHECKPOINT RENEW-GREEN commit=7650549b0162404a71ddc8ffece57d9d78c7f2b7 build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementReceiptTests*)|(VolumeRetirementWrapperTests*)|(VolumeRetirementHostProofTests*)/(Freshness_and_policy_version_are_rechecked*)|(Manual_attestation_is_exact_and_cannot_waive_findings*)|(Journal_intents_and_partial_resume_are_exact*)|(Retire_temp_uses_current_proof_protocol*)|(Host_proves_complete_origin_ancestry*) executed=5 passed=3 failed=2 skipped=0 trx=/work/worktrees/task-e9fd8902/.antiphon/checkpoints/20261031-071038-170d/rows/RENEW-GREEN/run.trx slot=granted waited=0s dirty=0 source=7650549b0162404a71ddc8ffece57d9d78c7f2b7 sourceState=clean buildSource=verified
```

Build326.386s,row791.522s,tests781.582s,total1177.061s; build/row slot wait0. No deadline fired. Only the affected W1/J10 methods are rerun after this fix; the three passing methods retain this precise source provenance. The complete acceptance selection is not green from this mixed run.

## Coordinator transport failure remains red

RENEW-FIX20261031-073058-fa78 at902a4446569992a4e31e9141aa6694753779863a is terminal RED1,2/0/2/0, clean source/build verified. W1 reached a valid initial proof exchange under FD8 and then transport refused before Ready/effects (103.012s method). J10 passed its long manual phase and existing direct matrices but public authenticated DryRun also refused RemoteTransportFailed (about180s method). This is an introduced coordinator regression, not inherited red. No fixture assertion or deadline was relaxed. The following diagnostic adds only a numeric source line to transport metadata; no child stderr, paths or secrets are retained.

```text
CHECKPOINT RENEW-FIX commit=902a4446569992a4e31e9141aa6694753779863a build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementReceiptTests*)|(VolumeRetirementWrapperTests*)/(Manual_attestation_is_exact_and_cannot_waive_findings*)|(Retire_temp_uses_current_proof_protocol*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-e9fd8902/.antiphon/checkpoints/20261031-073058-fa78/rows/RENEW-FIX/run.trx slot=granted waited=0s dirty=0 source=902a4446569992a4e31e9141aa6694753779863a sourceState=clean buildSource=verified
```

Build624.365s,compiler494.39s,row295.673s,tests283.747s,total1023.852s. No slot wait or deadline fired. Host compiler contention was observed; p95 remains unknown. Full CP-15/roster are still unrun, ordered behind acceptance.
