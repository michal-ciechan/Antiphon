# CARD-1609 continuation 11

Outcome: partial continuation; long Apply, exact-object producer and public Close/Resume passed at7650549b0162404a71ddc8ffece57d9d78c7f2b7. Final coordinator/fixture repairs are pushed at56340382cf49e573328fb5b4b9d045cfc3c9f26e, but full acceptance is still RED at its last qualified run. The 150-minute budget prevents another fresh acceptance build, full CP-15 or whole-roster qualification. S5 is incomplete; next: code. Production deletes remain disabled.

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

## Final diagnosis, repair, and qualification boundary

The gated smoke probe passed with an ordinary local SSH child. The extra gated one-method diagnostic reused the902a4446569992a4e31e9141aa6694753779863a assembly while consuming current bf6064a78c2994b17b10eaa0a19e69cfc9958048 scripts; it executed1/0/1/0,78.152s method,79.114s test host,slot wait15s/held81s. It is diagnostic only, not a fresh qualified build. Its failure-line=0 established a remote exit rather than a caught client exception. This unlisted diagnostic was necessary to locate the introduced transport error; no final-row credit is claimed.

The fixture had injected setup before every EXIT trap, including helper-local traps. On the second audit it reset phase state and recursively aliased the audit wrapper.4a382faf66fadd5a957ac34a02c5d2baf40da979 changes that to one exact global host-entry injection, with an exact-one-marker guard. This does not alter the product or remove any protective assertion.

Fresh ENTRY-FIX20261031-075303-659f at4a382faf66fadd5a957ac34a02c5d2baf40da979 ended RED1,2/0/2/0, source clean/build verified. W1=114.127s, J10about259s. Build304.176s,compiler184.10s,row384.958s,tests374.036s,total808.221s wall/793.232s charged; build slot15s,row0. No deadline fired. The14m charged/16m wall task-allocation cap was not a raised row deadline; row remained10m.

```text
CHECKPOINT ENTRY-FIX commit=4a382faf66fadd5a957ac34a02c5d2baf40da979 build=ok filter=/*/Antiphon.Tests.Scripts/(VolumeRetirementReceiptTests*)|(VolumeRetirementWrapperTests*)/(Manual_attestation_is_exact_and_cannot_waive_findings*)|(Retire_temp_uses_current_proof_protocol*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-e9fd8902/.antiphon/checkpoints/20261031-075303-659f/rows/ENTRY-FIX/run.trx slot=granted waited=0s dirty=0 source=4a382faf66fadd5a957ac34a02c5d2baf40da979 sourceState=clean buildSource=verified
```

The W1 host trace now proves an initial v2 proof, complete real audit, exact-object renewed origin proof, matching controller confirmation, immutable Ready, transport-exit=0/failure-line=0 and zero deletes. The public bridge still falsely refused. A separate gated scalar-return probe at4a382faf66fadd5a957ac34a02c5d2baf40da979 reproduced assertion red: output-count=2, types System.Threading.Tasks.VoidTaskResult and System.Int32. PowerShell emitted the async completion object into the exit-code return.56340382cf49e573328fb5b4b9d045cfc3c9f26e casts the coordinator and bounded-process stdin completion results to void. The identical gated probe then passed: output-count=1, System.Int32,0. Both probes waited30s; red held3s and green1s. This is a narrow red/green repair witness, NOT a green full W1/J10 selection. No fresh build followed this final source repair; acceptance must be rerun first. The withdrawn-origin and subsequent wrapper Apply controls in the new driver remain unqualified because W1 stopped at its first assertion.

No test method was removed/skipped, assertion relaxed, floor lowered, production policy deadline changed or deletion path added without its retained refusal assertions. The original c1609-wrapper-cases.mjs is byte-identical to baseline. No credential entered a helper. All fake SSH/Docker/Git operations used local inherited owned children. No desktop files, live d47848cc qualification, production volumes, tags, deploy, rc rebase or activation were touched. Standing authority covered these repairs; no new approval was requested.

## Final closed-list status (this continuation)

| Row | Fresh whole-row result | Required floor | Row cap for next run |
|---|---|---:|---:|
| CP-3 |not run; owed|25|28m|
| CP-4 |not run; owed|12|5m|
| CP-5 |not run; owed|30|2m|
| CP-6 |not run; owed|132|6m|
| CP-7 |not run; owed|52|4m|
| CP-8 |not run; owed|10|17m|
| CP-9 |not run; full22 owed|22|30m|
| CP-10 |not run; real-Docker cleanup owed|3|8m|
| CP-15 |not run; fresh full16 first after acceptance|16|6m|
| CP-16 |not run; owed|23|6m|
| CP-11 |native Windows owed|12|10m|
| CP-12 |native Windows owed; fresh Linux C1210 also owed|3|2m|
| CP-13 |native Windows owed; fresh Linux docs also owed|16|2m|

CP-1/2/14 are historical intentional-red plan rows, not fresh final green rows. Do not add focused passes to prior partial/historical rows. CP-15 was not attempted because acceptance never reached a fresh green boundary and the bounded task budget was consumed by red-first repairs and verified builds. No full-row p95 is established; method/build samples above are not p95. Inherited S5 gaps remain: H6 pipe-holding descendant cleanup after the root exits; planned recursive state/dind persistence and observation-error closure; lock/reservation/dependency/historical-container matrices; strict envelope completeness. Current J8 matrix green does not close those separate gaps. Final Opus Review is premature.

## Static checks and remaining commands

Definition files did not change; -Update never ran. The REAL land graph -Check passed at4a382faf66fadd5a957ac34a02c5d2baf40da979 using the leased evaluator from the7650549b0162404a71ddc8ffece57d9d78c7f2b7 producing build. Digest20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6,count66,phase=pass2,approved; slot waited15s/held39s. The final void casts do not change definitions. Module load and PowerShell/Bash/Node syntax passed after the closing-parenthesis fix; transport scripts remain ASCII. OS witness inventory passed at4a382faf66fadd5a957ac34a02c5d2baf40da979:10 source paths, test-only OS candidates, no production selector/startup changes. Final report-only-tip checks are recorded below.

Fetched origin/master and merge-tree --write-tree at final code56340382cf49e573328fb5b4b9d045cfc3c9f26e passed without conflicts, tree bbc6983bd24b8e57e403e6b70ddb3ca734279e2d. No target merge was performed. This does not establish landability while acceptance remains red.

Next owner: bootstrap a NEW output, import the plan, set UseAppHost=false,TEMP=/tmp and fresh phase variables, use the corrected caps in the table, then run the exact acceptance selection first (J4/J10/J8/W1/H1 as appropriate to the final repairs). Run/wait until terminal; validate the structured report. Only after acceptance green: fresh full CP-15, then the entire closed Linux list CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-15,CP-16 serial, then Linux portable C1210/docs. Use a fresh output every run and preserve owner/source checks. No bypass/retry unleased after exit4.

Native Windows commands remain owed, only on its separately assigned native host:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1609-windows-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1609-windows-tool/ --property:UseAppHost=false
dotnet tools/Antiphon.Checkpoints/bin-c1609-windows-tool/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --out .antiphon/c1609-windows.yaml
# Pin UseAppHost=false, CP-11=10m, CP-12/13=2m; fresh output and phase variables.
dotnet tools/Antiphon.Checkpoints/bin-c1609-windows-tool/Antiphon.Checkpoints.dll run .antiphon/c1609-windows.yaml --rows CP-11,CP-12,CP-13 --serial --expected-source-sha <full-source-sha> --max-wait 30s
dotnet tools/Antiphon.Checkpoints/bin-c1609-windows-tool/Antiphon.Checkpoints.dll wait --run <id> --max-wait 30s
```

Production preview/audit remains OWED after S5 and independent final Opus review, reviewed rc port after topup38/before CARD-1615, and installed tool/image UID/protocol/registry/endpoint verification. First production action is PREVIEW ONLY:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <reviewed-rc-tool-sha> -Phase retire-temp -DryRun -ProjectId <real-project-id>
```

Inspect complete findings/proof/refusal denominators. No production Apply or Close is authorized by this report. Production deletes remain DISABLED.

## Pushed slices

```text
445a22abe88002218c8c9f870b39621fdac563ff test: require Apply admission before long content audits
f4118d17d2cb32b86839473f59340e8336efd6e3 docs: retain intended long Apply admission red and measured costs
a8c5683698db4cf734fb1ded4967a4c4ffef5cc0 fix: bind Ready admission before the long Apply audit
10d7c11538dfe339eb0a8c91f77614cd10e524a1 docs: preserve four acceptance passes and latched owner guard failure
29da3530ee9fbda5575fd6f9850e29d3e3fb5da6 test: require locked v2 wrapper proof renewal and one audit without container effects
e70b39e1ada13d31fa104b958a137dd6a744b43e test: preserve real cleanup context in the wrapper transport fixture
88ab37c77aed90118a98862d3e5835b167af0735 fix: avoid a duplicate audit without container effects and strengthen long manual Preview acceptance
0c22785c48aeb37a5b84e2cd80d4026296fcd466 test: require exact audited object coverage from renewed origin proofs
63173fe1a658b2d3ec34671a3e4e25e3f9b1d084 docs: retain required-object producer assertion red
7650549b0162404a71ddc8ffece57d9d78c7f2b7 feat: renew exact publication proofs through the locked retirement coordinator
902a4446569992a4e31e9141aa6694753779863a fix: close the coordinator proof-export expression and retain mixed acceptance receipts
bf6064a78c2994b17b10eaa0a19e69cfc9958048 fix: retain numeric coordinator failure location without child output
4a382faf66fadd5a957ac34a02c5d2baf40da979 test: inject fixture state once at the host entry rather than helper traps
56340382cf49e573328fb5b4b9d045cfc3c9f26e fix: suppress async completion objects from retirement exit-code returns
```

The final evidence-only commit follows these code commits. All checkpoint executors and direct probes were awaited to terminal. Generated JSON/TRX/logs remain ignored; essential unchanged CHECKPOINT lines are retained here. Cleanup removes only the eleven literal alternate build output paths recorded in .antiphon/c1609-11-cleanup.txt after the last run; no checkpoint evidence, fixture volume or state directory is deleted.

--- next stage ---
next: code
handoff: rerun repaired acceptance with fresh source/build provenance; then full CP-15 and whole closed Linux roster; preserve all S5/Windows/production owed work above
artifact: .antiphon/task-e9fd8902.md

Final static/cleanup: module load, diff whitespace and OS witness inventory passed at final code56340382cf49e573328fb5b4b9d045cfc3c9f26e. All eleven literal alternate outputs above were removed after every owned run joined. Checkpoint evidence and retained fixture state were preserved.
