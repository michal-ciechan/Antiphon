Landed S5b-2, both migrations, and the status-update skill on master 1163840885e23f8267d80db709a83d1b091c21ec; the authorized master-only landing succeeded with the documented timeout warning and eight known inherited failures.

Task 2b1406ee. Source branch feat/card-task-2b1406ee. Assigned base df1fc145181a3bedf444f9113f822f30d3b136f6. Publication is confirmed by git ls-remote origin refs/heads/master. No rc ref was pushed, no desktop action ran, no restart/deployment occurred, feature admission remains OFF (Enabled=false, empty RunnerIds, Roles=none), and no producer was added. The caller's standing grant already authorizes master-only Interim scope; no further approval was requested.

## Publication and replay

One landing attempt, zero target-movement/re-pick loops, zero failed-push retries. Re-fetched master immediately before the fast-forward push; it remained the assigned base. git merge-tree --write-tree origin/master HEAD exited 0 with tree 4daa66ca48c5dc6b64f6bab0bdb6c2ad40fb2337. Master push receipt:

```text
To github.com:michal-ciechan/Antiphon.git
   df1fc1451..116384088  HEAD -> master
1163840885e23f8267d80db709a83d1b091c21ec	refs/heads/master
```

67 non-report-only '+' commits were cherry-picked -x in source order: 63 from origin/feat/card-task-204a327a (reviewed tip de1963dd09e5c12951aa1a178cb833c85e69bda4), then four from origin/feat/card-task-23abede2 (tip a281184224de09cb0e0cd848eb73197b1b5346d9). No merge commits, rebase, branch switch, reset, force push, or land-graph -Update. The replay stayed local until the final master push; this report-only successor is published afterward on the assigned task branch.

Review basis: Grok round-1 Interim report on origin/feat/card-task-fd0ebdc3 at docs/investigations/2026-11-06-review-card-0824-s5b-2-r1.md; no blockers, operator accepts Interim for this master-only land. This does not discharge the Full/Final obligations below.

Skipped report-only commits:
- fad9b6fc902c6a39fd51843fe10b357fc5880a66 CARD-0824 S5b-2: avoid unverified build-warning attribution in partial report
- 59e02aaaf7cf7434fb1f21dd20a5853639a61a0f docs(card-0824): qualify retained-agent C1 repair through accepted prompt
- 40221e6ccc6dbb75770a686d08b5e6860b50beac docs(card-0824): close C4 recovery and retain full affected-class outcomes
- 756eb436ce93b0ee3316e64d3b9cf98ebac4c06a WIP(card-0824): publish replay evidence and unresolved schema decision
- de1963dd09e5c12951aa1a178cb833c85e69bda4 WIP(card-0824): publish migration repair and incomplete verification evidence

One report conflict occurred at source 71ce1f3cae36cdf51c6c30fc2b3e325021cfbbd4 because the omitted report-only 59e02aaaf7cf7434fb1f21dd20a5853639a61a0f had supplied adjacent historical text. Resolved docs/superpowers/reports/2026-11-06-card-0824-s5b-2-code-continuation-3.md to that source commit's exact reviewed report blob. No product conflict or implementation repair. This preserves the historical outcomes/costs; it is not a fresh source-clean or Full Review assertion.

Patch proof: 66/67 whole-commit stable patch IDs match. The sole report-conflict exception is land 47d244b44694604a8c25bf3661326a24b88c277b: source patch 9854ec5b356e0a5cfd49b465d3d4a08a0b01afe1 versus land patch 3f48c3bde1d61da259938e929c8c8c8a3179d2af. Excluding docs/superpowers/reports/**, both are 838f861a2cb6529312318eb0077ded1654ccabe2. All non-report changes are patch-identical.

## Final-tree verification

All rows tested the exact landed SHA 1163840885e23f8267d80db709a83d1b091c21ec, tracked clean before and after, with one native-default-output Debug solution build, UseAppHost=false, TEMP=/tmp. Build command: timeout 35m pwsh -NoProfile -File scripts/build-slot.ps1 -Label 2b1406ee-final-solution -- dotnet build Antiphon.sln -p:UseAppHost=false -maxcpucount:1 --nologo -v:minimal. Result 0 errors, 1961 warnings, compiler wall 638.34s; slot held 640s, waited 0s. No NETSDK1047 and no solution rebuild. Extra prerequisite build: native LandGraphAudit tool at the base, needed for the required base graph check; 0 errors/0 warnings, 28.14s, granted slot waited 0s/held 29s. Neither is an unleased driver.

One host lease per direct test run, one row per invocation (below the three-row ceiling), serial. All passing rows ran once. Test driver properties match the build; --no-build, --timeout 8m, outer 22m bound including a 12-minute admission budget. No waits/assertions/retries were changed. These are direct gated Merge final-tree results, not fabricated checkpoint-tool receipts. The first table's exact CP filters were retained. CP-4's fresh TRX roster is exactly its seven named methods.

Test assembly SHA-256: 32d9d952eee403e7c6c9e274a7f6d8d08d05f772a293ae89bc52c7ace0a2a523. Total 297 executions: 286 passed, 11 failed, zero skips. The 11 failures are precisely the three permitted FULL-REDIRECT timeouts and eight named inherited landing failures. The batch is not reported as globally green.

| Row | Executed | Passed | Failed | Skipped | End-to-end wall s |
|---|---:|---:|---:|---:|---:|
| CP-1 | 10 | 10 | 0 | 0 | 98.304 |
| CP-2 | 8 | 8 | 0 | 0 | 75.688 |
| CP-3 | 7 | 7 | 0 | 0 | 100.554 |
| CP-4 | 7 | 7 | 0 | 0 | 80.76 |
| CP-5 | 47 | 47 | 0 | 0 | 153.975 |
| CP-6 | 27 | 27 | 0 | 0 | 65.728 |
| GUIDANCE-STATUS | 6 | 6 | 0 | 0 | 10.944 |
| FULL-REDIRECT | 91 | 88 | 3 | 0 | 87.531 |
| INHERITED-LAND | 94 | 86 | 8 | 0 | 131.121 |

CP-1..CP-6: 106/106 passed. GUIDANCE-STATUS: TaskPlatformGuidanceTests 5/5 and new StatusSnapshotScriptTests 1/1. CP-5 is the complete TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire* sweep, 47/47. CP-6 includes NativeLinuxPolicyWitnessTests*, MechanicalPcContractTests*, layering rows and all four migration results. Policy sweep plus native witnesses are completely green. Mechanical contract passed 11/11. Layering: VIOLATIONS 0 LISTED 170 STALE 0 AMBIGUOUS 3; retained '# slice S5b-2' allowlist debt RefOnlyCommittedSource.cs -> GitWorkspaceService.

LOUD WARNING (CARD-1722 family): FULL-REDIRECT was run once on the landed tree and is NOT GREEN: 88/91 passed, 3 TimeoutExceptions, zero skips. No non-timeout failure.
- RemoteWorkspacePreparerTests.Cancel_of_a_queued_runner_task_returns_while_its_mirror_is_pending_and_a_late_success_is_still_recorded: Expected 1 WorkspaceMirror request(s); saw 0, WaitForRequestsAsync before cancellation (line 276).
- C1076_pushes_on_one_repository_run_one_at_a_time_and_the_waiter_names_the_pusher: operation timed out at line 732.
- C672_prepared_task_launches_while_a_land_holds_the_lease(prepared): operation timed out at line 571.
Continued under the brief's explicit timeout-only exception. These neighboring timeouts are not independently proved to fail at base by this pass; that rerun was not required by the exception. The Review's historical result was 82 passed/9 timeouts; the author's two historical results were 90 passed/1 timeout. No historical run was relabeled green and no green row was repeated.

INHERITED-LAND: 86/94 passed, exactly eight known inherited failures, no additional failure:
- Freshness: C498_NonReviewedRoleLandsWithDefaultTarget(Plan,equal), (Plan,behind), (TestDesign,equal), (TestDesign,behind), and C688_BehindLocalBranchLandsFromRemoteWithoutFastForward.
- Persistence: C498_MonitorPassDuringObservedInspection(0), (300), (900).
C498 expects cleanup Complete but observes Refused; C688 does not observe the expected source-ref deletion. These match the brief's known fake-git ls-files -z fixture gap on master df1fc145181a3bedf444f9113f822f30d3b136f6, owned by Debug 1307a410. Per brief, only the two final-tree classes were run here; no fixture repair or redundant base run.

## EF deployment note

Linear sequence: 20261105121558_Card0824RefOnlyDesktopCleanup -> 20261106005946_AddRefOnlySettlementDebtWorkspaceSnapshot -> 20261106032116_AddRefOnlyParkWorkspaceSnapshot.

Both original IDs are retained. First adds AgentTaskSyncDebts.WorkspaceSnapshotJson nullable text; second adds AgentTaskParks.WorkspaceSnapshotJson nullable text. Neither adds NOT NULL/default/index/rename/type change. Four fresh migration results passed: chain plus HasPendingModelChanges=false; additive upgrade/empty downgrade preserves legacy rows; debt and park recorded-snapshot downgrades each raise SQLSTATE 23514 card0824_refonly_snapshot_downgrade_blocked and retain exact bytes/applied migration. Down takes ACCESS EXCLUSIVE before checking/dropping; recorded snapshots must be drained before downgrade. No production migration was applied by this task. Previous binary was not launched and large-table lock p95 is unmeasured, as in Review.

## Land graph and input identities

Base and final land-graph -Check both exit 0/approved, digest D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03, count 69, both evaluation passes. No inherited mismatch warning was needed. No selected build-definition record or target pin changed. -Update never ran.

Unlike a policy-registry edit, these selected source commits do not modify tests/test-execution-policy.json or any of the five protected census inputs. Recomputed base and final values are therefore identical, not artificially refreshed:
- policyHash stored and recomputed at base/final: 2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72.
- -PrintInputDigest at base/final and PinnedInputDigest: 417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939.
Exact diff of protected inputs is empty. The status branch also makes no policy edit, so no paired pin refresh was necessary.

check-evidence-diff over base..landed source: commits=67 entries=0 violations=0, exit 0. git diff --check passed. Evidence audit is also run over the report-only successor before settlement.

## Evidence, costs and cleanup

Absolute evidence root: /work/worktrees/task-2b1406ee/.antiphon/merge-2b1406ee. rows.json stores exact selections; each row directory stores console.log, run.trx, roster.json with per-case durations, receipt.json with clean source and assembly fingerprint, and HTML report. all-receipts.json joins rows. timing/ retains opt-in phase journals. Uninstrumented phases remain unknown, not zero. solution-build.log, base/final-land-graph.log, base/final input digest/policy hash, evidence-diff.log, final-merge-tree.txt, master-push.log and patch-proof.json/md retain provenance. Initial audit attempt lacked its tool binary and was not credited; the tool was then built and base -Check passed. Initial policy hash helper import lacked nightly-common and was not credited; the corrected canonical recomputation is the stored result above.

All owned foreground build/test/audit/push processes joined. Native bin/obj and native TestResults outputs removed using exact literal absolute paths; final inventory empty. No scratch worktree was created by replay; graph tool-owned canaries were cleaned by its script. Ignored TRX/logs/receipts and phase evidence remain under their original evidence root; no generated payload directory is committed. This single narrative Markdown report is committed individually after master publication.

Existing status branch evidence remains historical: author reported 99 affected passes, a whole-Unit timeout and two formatter-path guards reproduced at base. This Merge did not rerun whole Unit or those formatter guards; it ran the explicitly commissioned new script test, platform guidance and common policy/contract/architecture selections. No claim of Full Final qualification.

## Owed after landing

- Frozen V-1..V-8 and all unexecuted R variants remain owed; authored CP greens are method coverage, not every frozen variant. Includes ancestry/CAS/tuple and real phone-home controls; debt crash/corruption cuts; artifact/path/type/budget/unavailable-source variants; actual session/control/advisory/ZombieCensus callers; all Q handoffs and actual-provider UserPrompt acceptance.
- Unit shards UNIT-AD, UNIT-IL, UNIT-MP, UNIT-QT, UNIT-UZ and native skips from the prior UNIT-EH row remain owed. The status branch's whole Unit qualification also remains incomplete.
- Native Windows N1, C0824RefOnlyBoundaryNativeTests.Windows_case_alias_cannot_own_the_same_ref_twice, and native Windows path-containment/factory witnesses remain owed.
- S5b-2 PC-1..PC-161, all variants and method-scoped SourceLanding Mutation remain owed. Status skill PC-1..PC-3 in docs/superpowers/mutations/2026-11-06-status-update-candidates.md remain pending Mutation.
- Manual actual-provider UserPrompt acceptance remains owed; Queue/Sent/ack is not acceptance.
- FULL-REDIRECT timeout family remains open (CARD-1722); inherited eight-test fixture repair remains Debug 1307a410. No repair was folded into this land.
- Activation/deployment is separate caller work; this master land neither advances rc nor activates server/runner binaries.

## Per-pick patch map

| Land SHA | Source SHA | Source patch-id | Land patch-id |
|---|---|---|---|
| e10e7961f6e1bbdd1885b3e769f28626fb33c9ec | 10d6d2fd1db1f90578b7b6c0cdeda883992b50ba | b46dcd83d0c119da71402669436fd75abc6e14e0 | b46dcd83d0c119da71402669436fd75abc6e14e0 |
| 61b87f1a0339b0969f242e87eced3c964b934779 | 11b6a05600505a1071a048035c0b439a84845dbe | 84741afd7bcf73334d10ceee74455f0c16608a4d | 84741afd7bcf73334d10ceee74455f0c16608a4d |
| 4c2c74fc9440fdf1745c1b0ea5822f5f09cacd38 | c5281f726eeda3812cadb2808d8933127fd091f3 | 975df5f00cfc0cc250f8b6c32d96623d1ded638a | 975df5f00cfc0cc250f8b6c32d96623d1ded638a |
| 4bbb90de948200cf13bd70407e392d98cab76cc5 | 6a69b69406bd4a239f8a4fd5220c968bde05d14b | 9fa77e6fe92c5fc2c7316b53281404255e25cbd8 | 9fa77e6fe92c5fc2c7316b53281404255e25cbd8 |
| 6bea9d68017135edf436889a108043de8d1f18e3 | 4372252f8435d38a69ab85308c4968b4a9b4622e | a2e9b2823fb14e820afdbcc178b009f763f27e13 | a2e9b2823fb14e820afdbcc178b009f763f27e13 |
| c8e61a69d18e876d84794e34e594e1d9a437965d | 6d2d36f94199eb6f9b08cea677329fed729f0323 | 95c0f4bc07a772e2a4e46dc7f3e7da944defe59e | 95c0f4bc07a772e2a4e46dc7f3e7da944defe59e |
| e682935b06b1d81521da00da499fca8570f24337 | ac64f34cafc50f953d2bd72c2666d6ef61f3b258 | c0dd6b1212689c1a3424105fa599390faa11a8ce | c0dd6b1212689c1a3424105fa599390faa11a8ce |
| 62fa62de79ceb34476614471a92919013048807e | 0c56ac27d60d8400ddd3a44257426b93e3645051 | bdb21a8ec39e4e17295035ca31bf3c872be49495 | bdb21a8ec39e4e17295035ca31bf3c872be49495 |
| f4ad9151902dffa30afb66244b7737d78b2b1600 | 35359e0774809bd74052d7a3d4e87963e4a809ee | c785c5fd22b562de78c101e8d9d4959273897bc6 | c785c5fd22b562de78c101e8d9d4959273897bc6 |
| 96e093814de2333bec596125009e56f9cf73e495 | 674e3ba21f664df5e18fe73fd09a6a0a63b0a258 | e50ca90eb4e516b0b4bb260cbbe9da3103e3e234 | e50ca90eb4e516b0b4bb260cbbe9da3103e3e234 |
| 81f0e98283abdbd2461863da815b6dc8b731c49e | 1ddfae98e73b3dd360dfa7e54196079fe8703255 | e81450c4cd258d497591fd2e4abc084989d008ac | e81450c4cd258d497591fd2e4abc084989d008ac |
| 901e1897221e230d24158d28875f38561f1a6290 | bcebe1e948c73fffac91641f39645b04ae0132b4 | a73946d0c935b5680efa0b05e32946c35ad95f8e | a73946d0c935b5680efa0b05e32946c35ad95f8e |
| 44036bcabf1c3a66c02ce4e5793fbf352b728d44 | 74d08b72b2ec30864a01a9032a6797e390aef6a0 | b1f1a19313f0ef580ec1862deaacd737517000b3 | b1f1a19313f0ef580ec1862deaacd737517000b3 |
| 4e26c3d7fca3db47f66c71d646888570f3e6d59f | 5b4df3bbbaeb9d2c95bf7532facbdf367779fcff | 44f9db2c55a04c02cc9db9cb43c224487a6b989a | 44f9db2c55a04c02cc9db9cb43c224487a6b989a |
| 7983c3c08ccab5b027de1fa9546820b891cbeac5 | c10a4cc0555b94d80933138dfc54333ef3f3bb22 | 71cabb0af91f50e3358bb8dff02cf7499ec826ac | 71cabb0af91f50e3358bb8dff02cf7499ec826ac |
| 7164613a515b37fb25adbd7650adda6c10469de3 | 0df380e1feaa06312a35af56a298b8d19fc9bfd7 | 40e8e2f10c8630c94c66c80f7c32f5cb87551084 | 40e8e2f10c8630c94c66c80f7c32f5cb87551084 |
| f08d62e2e1e57801448d1c83d324d2775fd30b80 | 5c0beb135e8fb71cfd29693b315ef4d34c114479 | bdd040ceb9f4a243c5075c7c56033c1265e5bd13 | bdd040ceb9f4a243c5075c7c56033c1265e5bd13 |
| 917e7113b572e354b60485d7c3be6478cb1038e0 | 1f1ea55ee152b6ef38c3b0216a403a1a4e302616 | eb3469adee517eab547f60c6c112a62360b2e4fb | eb3469adee517eab547f60c6c112a62360b2e4fb |
| c2138b7a4e2aa5055b2fa855ee5c4afe04f1dcb2 | c398b49fae8fa6f185d645ec8beff10f9227a535 | cd3343072bc10c2e819dfed2be568de34803814c | cd3343072bc10c2e819dfed2be568de34803814c |
| d9529ad119bd4613b0ea482d58ab0e6132fb943e | 212f8e3f7bc0c4bbce07c6afdd4d230c5f06683f | 08765d800677cab3c0766393b60204582f2e475e | 08765d800677cab3c0766393b60204582f2e475e |
| 793f33cded5c8b7fa16d69cdc34eabf9feec645f | c19276d259017b23f0eb30e70a6f817c034d0c4c | d4ca7041e843774e9bb55f72f2c583c135aefc08 | d4ca7041e843774e9bb55f72f2c583c135aefc08 |
| 930d31976c7f4037d91773db02431b80d77a8ccb | 60f56b152453b2435f359099f1945c0906f55ee3 | 79fd034fbed981904b119003c2417b2d8cd39a1f | 79fd034fbed981904b119003c2417b2d8cd39a1f |
| 58d72b237f041a5ae8a2af0f866abf7e1253edc2 | 5567f897ba5546c92942eb58fca02e9b4d50bf0f | a69934e8d38ec8c67fcd7bc85dce6888cd692d3c | a69934e8d38ec8c67fcd7bc85dce6888cd692d3c |
| 9300c54493f394ec263811b976463b5b9fe54572 | 20fe28d0cda60200d65a1bbd0365c1d759bcf8fc | f3a82a190c812223b22c5793325c55e3b6b637e1 | f3a82a190c812223b22c5793325c55e3b6b637e1 |
| cee6bac5a2712195ba21f345c6bfc71bd2eb4374 | 7a3d3e03f30a71e86ebca1dce51c7439c782b479 | 18e079a11524032932a6635ee247722e6525945a | 18e079a11524032932a6635ee247722e6525945a |
| 8fd7f1f0f08de17b61435a90744310ab1fbdc691 | c79677f0756edc2d49912aac57b7446b0f295f8a | d1f4be97b60b618704c0102823181cdebdffa6c4 | d1f4be97b60b618704c0102823181cdebdffa6c4 |
| 32fa98a46e3c613d600fcb2cb92b6faa0700445b | 231563b90cd6f5df2e414e2888101101beede641 | 971bf782f1a1cd05cb890199f779a52d456fd799 | 971bf782f1a1cd05cb890199f779a52d456fd799 |
| 8101de48e67b0c0cef426e2b1430fbbc35bd4b29 | fdc19442b1d01271fdea6f3e8fae0d9463794a92 | 60a09f3ea6e272ece4a9027eb064e4f6e7b1ce81 | 60a09f3ea6e272ece4a9027eb064e4f6e7b1ce81 |
| 4ed37dbd1288b28dd97e8cbc0cc7d8b542b8ad9f | 34277c5e3ace8ed39838cd489048a0af5cbe2119 | 24aed43c1f5447d580ef766b06fc42a1ba7ae0f8 | 24aed43c1f5447d580ef766b06fc42a1ba7ae0f8 |
| 51941f9830ebf5206b7ee771d2a3c39a554d7045 | fd17ba6762df10eda8550e85d6474f48c20c3de3 | a91b89468fbb41838ac5a4056f2ca75e49f8bf55 | a91b89468fbb41838ac5a4056f2ca75e49f8bf55 |
| f194ff221a7645bbcaecf9d44b9e65d5595dbbbf | 2d9071b8eb62d4c52d4c8faecae77055818ae4e6 | d687a14436e87f63632568f8e8b459988d8e5a63 | d687a14436e87f63632568f8e8b459988d8e5a63 |
| 8e576d3d9201533b77311c56db566817029d8938 | f0133c93da23549d9839378627e9aa49370192f4 | f7fd2e035ff4d6a56e036e0a6dda82562df2e4fa | f7fd2e035ff4d6a56e036e0a6dda82562df2e4fa |
| 009d1b6002137adc6d58d69770aea6f04d0fccc9 | feb13a3565034872d799ad1d383321e993baffae | 4de52a15fea5476c409286ab380bd32428077c2b | 4de52a15fea5476c409286ab380bd32428077c2b |
| 4731fb89c35bb61b1e688010341eca57fae1b8ab | ccf989dc48de7d8bd02cb97b5925dbabe044ad2d | f2c80f74fa00be545c14db83f6ec8b97b74d4682 | f2c80f74fa00be545c14db83f6ec8b97b74d4682 |
| 97f38f9b754649b7f4bcfe259a9262186b68cd4d | a51bcad8051f795d65a858b25a89fa7f7d2b5966 | c11bce88dcff2e21152e860839ab1381fdacd3f7 | c11bce88dcff2e21152e860839ab1381fdacd3f7 |
| 413be4e85494b9182d9049d9bfe0ce771c21fd1b | fad9ae14c73624db906b9bccfb25391551604264 | 92bcb255318dc4cf2588cb40309e2c1995c1ccfc | 92bcb255318dc4cf2588cb40309e2c1995c1ccfc |
| dc8cc04ec048ebd275ece7d02556f2d4eed13ec8 | df041542e3f4dca37ffd68dd88a829cabae159c8 | 0de02717e6fb83880f59862216e384e8f60b0013 | 0de02717e6fb83880f59862216e384e8f60b0013 |
| d50eb50f60be286841307d58694e8311921bbb8b | 6b236e9c4257d601fe692b9d174275d9e4811019 | e8706ad4c2fb676f0df81f38e7803f976beff786 | e8706ad4c2fb676f0df81f38e7803f976beff786 |
| ce881d97a94d44435729635ba2b5596b0e8d3761 | 9440a3e1538a9319632955139fbcea6d12fa8831 | e080bd2945e50f3a5b7201dbbe055bdfb99d972d | e080bd2945e50f3a5b7201dbbe055bdfb99d972d |
| 03b7e75ceb7cd622224d857c2876e58525b4a512 | c06ae8df7f1eaac22fb1b11517d3acdc4c9e034b | eed7b41e094885c8baaf14eab8b9bc5e744795dc | eed7b41e094885c8baaf14eab8b9bc5e744795dc |
| 1a5a2887f0ea85a166c64ce1c5a50d43d420fbc6 | 683ee8f883e2074e5a37a18a16e11e2f9be04f19 | 47c222b34c4568c2f8c6c091b4d182c8c1366a44 | 47c222b34c4568c2f8c6c091b4d182c8c1366a44 |
| 0c6cf472eb8dec3923d81f516c63d0417f259dcb | 0b4fbcef77535e88344385704e9445ed38e289e4 | 103324c87ca1cde68bc40841206954c91ea7d426 | 103324c87ca1cde68bc40841206954c91ea7d426 |
| 43d819da7189349c548b07ebd2677e6be086d95b | 1e7a9802bcf839509def5c293a0bd3df1e2c2a2d | 9b386b57d495d6912d601a5ce277c2002d85c886 | 9b386b57d495d6912d601a5ce277c2002d85c886 |
| e7d64b925b6778b9b79178a6d1614493c1627c3e | badfd865b28b2de5ddf8cea4d3911e3a3c8f2359 | 97f3294c6a603cdfb4fb6b6599df42266d9a63a1 | 97f3294c6a603cdfb4fb6b6599df42266d9a63a1 |
| 34776270d26c9c49ce9be253bd0a06f65d670173 | 3c2b231ef517dd2f0d8f8d6fc54404c221506f29 | c1c3867a7fa2cb5562103aa6479b8c1ea171d5e2 | c1c3867a7fa2cb5562103aa6479b8c1ea171d5e2 |
| 816dacfcaffee21df8b391d5cb147e69a6b222f5 | e6e2765dc55bd280f78704e432a28e420879b474 | f282c9221bfc1706382500c4039ca9b3df13562d | f282c9221bfc1706382500c4039ca9b3df13562d |
| f1286569dbdba4daf3c029c3fcc730b04a8c09b1 | 6b0fd07b46ec7cc0992e345439d9c55221c2f23f | 80cc671ccbe5e8166bb62075cab842b969031922 | 80cc671ccbe5e8166bb62075cab842b969031922 |
| f7653021799f27fe10d996ea5d4c855c89ee75e4 | d87f7636aa4f712c6ae50fef9e9f07cf91a27929 | 3d3d35b7fc2ae590e27cbf0e80ae8ea31c755461 | 3d3d35b7fc2ae590e27cbf0e80ae8ea31c755461 |
| 4d9edf16102f8ad4718750630ca5489ecfa048c1 | e021a476d4cd68dfa41c9493f8a8ca5705090d99 | 6eff318b3c9fa8dbacd091e5f9fe9e7ae879345e | 6eff318b3c9fa8dbacd091e5f9fe9e7ae879345e |
| 97d6b283fa9d63d3f8e4812d29d332cc0f3ab1ff | da221a199b79a6158a9189c2dcfd8ff5fdccc4ef | 1d3954f64b2fa275437e8a731a04a79833b5e9d9 | 1d3954f64b2fa275437e8a731a04a79833b5e9d9 |
| b55d45d27fe065a3d1bbc6962d59914516751ac3 | 679cdd51c626c00cfaf5ae8532dc99df618fc485 | 8d73484fb8f6d489a31ac73082df7d1e6dcaede1 | 8d73484fb8f6d489a31ac73082df7d1e6dcaede1 |
| 7587d523c3ecfa4cfdc38ecc638715a038465d75 | 91bff40932ee6071ace5a5d844fc57d7b89be2fe | 018805e7f7ad4902bc8efb81eabc7f01e7adb3a8 | 018805e7f7ad4902bc8efb81eabc7f01e7adb3a8 |
| 5ccb5fc07190acea94c712af8616b430d8837a5c | fb1881f37eead3f425260cc3fabdb6f80cd96e5d | 5b6293e2299bab10c039b83264387b650e6cba52 | 5b6293e2299bab10c039b83264387b650e6cba52 |
| 9c0e66fe63c998cf095a5950b009b676520243b7 | 603ffb881f2285f47d8bc2b27360c1f27fdbff26 | e0c52dc93e1948cad3a1167a6b08d7938bb07353 | e0c52dc93e1948cad3a1167a6b08d7938bb07353 |
| 383021ce12e602f89b3a51a66d9fe531d59361cb | c005933768e843ae913f448b3e86ac9b5e560117 | ebac464e017213e71fd7c455cf7e104a45b0eb5f | ebac464e017213e71fd7c455cf7e104a45b0eb5f |
| 47d244b44694604a8c25bf3661326a24b88c277b | 71ce1f3cae36cdf51c6c30fc2b3e325021cfbbd4 | 9854ec5b356e0a5cfd49b465d3d4a08a0b01afe1 | 3f48c3bde1d61da259938e929c8c8c8a3179d2af |
| 0a6ae2a8ddb1a4d111f0f3343dbfa40189a54dbc | f2d80c568696720a84ead2e19c40fa96d9f768fb | 5f20b7aa8d0b8def6ae18f0b243ff1f1d4a268a2 | 5f20b7aa8d0b8def6ae18f0b243ff1f1d4a268a2 |
| 7a4cde66dbff05f02f095dd3a595b181e9bd1db6 | 1fc2e80119f0241ccb4aee6e59b7895ecb8b5bfb | 3c3228bd9fd6a51ae877965207a0d102ed21188a | 3c3228bd9fd6a51ae877965207a0d102ed21188a |
| 3d5297c7695650c942c3c25559fa8778602b7c81 | 04ed6fcf00d3b6a9af5cb40590924fd9207bc3ec | 5b01eb435c545a765442b9de83b4fa35114c742d | 5b01eb435c545a765442b9de83b4fa35114c742d |
| 51b958fd6658b7cbf4dc5241ccb2fcb40be7ff80 | 85c25efd7975ad0dcbae5a9d45d91947501621b3 | a458c8a65cdfd6d5603923d2c76ff0b80db009e5 | a458c8a65cdfd6d5603923d2c76ff0b80db009e5 |
| e0d68bd69a73ef767e7ec276eb4a8b5d2151c211 | 4b64c474032f51449f243e1f5d0c631e2738520d | 0d9262c7984c06e9668b2cc492b0f59ac03754b8 | 0d9262c7984c06e9668b2cc492b0f59ac03754b8 |
| a48fdac63241b2ef8a477fa6c5b27e739c37b50b | 7728713c65af0876d836269de483a71d68a43c17 | 33e275731cfeed2e78160fc87e429b36762397b3 | 33e275731cfeed2e78160fc87e429b36762397b3 |
| ccf639380f28f4b34a4d7b9464551b47f27c4254 | 92862113530a81f80b8ae73fc68aea379327d674 | a460b5093848f3ac0faed6c329862ed6ee8bd41e | a460b5093848f3ac0faed6c329862ed6ee8bd41e |
| ea001d3ccca74ba7aaa18512edd90e31c6fb97f8 | 7000aa695dd81424ca8fa80c8d699ab5227f6a9f | 97d9c0b7d7291da64ba1c0082c3f8e01f2b87752 | 97d9c0b7d7291da64ba1c0082c3f8e01f2b87752 |
| fc0a9ad3b7a01d7a5c81c20b24796bbae4800ab3 | 8c3e7b629382adfb1a33a68c41ed795ef426954d | 2bd5683938d9c934187b743e7aca7def7b3b31bf | 2bd5683938d9c934187b743e7aca7def7b3b31bf |
| 6ff45c347d01c50395db7a360876d56b8f9812e2 | e237b4dd71ba19fa5d64b6361e5b4fa517c0bee9 | e783fe1d67d90b9f97aaa9690286299084179671 | e783fe1d67d90b9f97aaa9690286299084179671 |
| 1163840885e23f8267d80db709a83d1b091c21ec | a281184224de09cb0e0cd848eb73197b1b5346d9 | 32b6c4a931a8be47a130a9357a1f019186e1bfe6 | 32b6c4a931a8be47a130a9357a1f019186e1bfe6 |


## Exact row selections and raw lease lines

### CP-1

Filter: `/*/Antiphon.Tests.Application/(C0824RefOnlySyncTests*)|(C0824RefOnlyDebtTests*)/*`

```text
BUILD SLOT waiting label=2b1406ee-CP-1 position=1 occupied=4/4 elapsed=0m
BUILD SLOT granted lease=7b66eebd-c350-4c16-950a-c81a25821f21 waited=15s maxcpucount=6
BUILD SLOT released lease=7b66eebd-c350-4c16-950a-c81a25821f21 held=82s
{
  "id": "CP-1",
  "filter": "/*/Antiphon.Tests.Application/(C0824RefOnlySyncTests*)|(C0824RefOnlyDebtTests*)/*",
  "min": 10,
  "source": "1163840885e23f8267d80db709a83d1b091c21ec",
  "sourceState": "clean",
  "assemblySha256": "32d9d952eee403e7c6c9e274a7f6d8d08d05f772a293ae89bc52c7ace0a2a523",
  "started": "2026-11-06T15:14:11.239Z",
  "finished": "2026-11-06T15:15:49.543Z",
  "wallSeconds": 98.304,
  "exit": 0,
  "signal": null,
  "sourceUnchanged": true,
  "counters": "<Counters total=\"10\" executed=\"10\" passed=\"10\" failed=\"0\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"0\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />"
}
```

### CP-2

Filter: `/*/Antiphon.Tests.Application/(C0824RefOnlyReadTests*)|(C0824RefOnlyDeliverableTests*)|(C0824RefOnlyReceiptTests*)/*`

```text
BUILD SLOT granted lease=b4816a34-a77b-4b1f-bc74-6c073d76f05f waited=0s maxcpucount=6
BUILD SLOT released lease=b4816a34-a77b-4b1f-bc74-6c073d76f05f held=74s
{
  "id": "CP-2",
  "filter": "/*/Antiphon.Tests.Application/(C0824RefOnlyReadTests*)|(C0824RefOnlyDeliverableTests*)|(C0824RefOnlyReceiptTests*)/*",
  "min": 8,
  "source": "1163840885e23f8267d80db709a83d1b091c21ec",
  "sourceState": "clean",
  "assemblySha256": "32d9d952eee403e7c6c9e274a7f6d8d08d05f772a293ae89bc52c7ace0a2a523",
  "started": "2026-11-06T15:16:15.385Z",
  "finished": "2026-11-06T15:17:31.073Z",
  "wallSeconds": 75.688,
  "exit": 0,
  "signal": null,
  "sourceUnchanged": true,
  "counters": "<Counters total=\"8\" executed=\"8\" passed=\"8\" failed=\"0\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"0\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />"
}
```

### CP-3

Filter: `/*/Antiphon.Tests.Application/(C0824RefOnlyContinuationTests*)|(C0824RefOnlyConsumerTests*)/*`

```text
BUILD SLOT granted lease=179316ed-9b56-400c-9cf2-80b397204b83 waited=0s maxcpucount=6
BUILD SLOT released lease=179316ed-9b56-400c-9cf2-80b397204b83 held=100s
{
  "id": "CP-3",
  "filter": "/*/Antiphon.Tests.Application/(C0824RefOnlyContinuationTests*)|(C0824RefOnlyConsumerTests*)/*",
  "min": 7,
  "source": "1163840885e23f8267d80db709a83d1b091c21ec",
  "sourceState": "clean",
  "assemblySha256": "32d9d952eee403e7c6c9e274a7f6d8d08d05f772a293ae89bc52c7ace0a2a523",
  "started": "2026-11-06T15:17:52.248Z",
  "finished": "2026-11-06T15:19:32.802Z",
  "wallSeconds": 100.554,
  "exit": 0,
  "signal": null,
  "sourceUnchanged": true,
  "counters": "<Counters total=\"7\" executed=\"7\" passed=\"7\" failed=\"0\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"0\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />"
}
```

### CP-4

Filter: `/*/Antiphon.Tests.Application/(C0824RefOnlyBoundaryTests*)|(C0824RefOnlyCleanupTests*)|(RunnerSettlementSyncTests*)/(Legacy_release_keeps_the_classified_revision*)|(Local_merge_and_checkout_typed_removal_refuse_ref_only_owner*)|(Incomplete_preparation_refuses_initial_and_resumed_land*)|(All_desktop_consumers_skip_ref_only_without_deleting_refs*)|(Pushed_tip_fast_forwards_exact_owned_checkout*)|(Endpoint_change_is_not_followed*)|(C1082_SettledDebtSyncOnlyFastForwardsToTheRecordedSource*)`

```text
BUILD SLOT granted lease=c69be2de-ac8d-4696-82a0-b734b62ddc4b waited=0s maxcpucount=6
BUILD SLOT released lease=c69be2de-ac8d-4696-82a0-b734b62ddc4b held=79s
{
  "id": "CP-4",
  "filter": "/*/Antiphon.Tests.Application/(C0824RefOnlyBoundaryTests*)|(C0824RefOnlyCleanupTests*)|(RunnerSettlementSyncTests*)/(Legacy_release_keeps_the_classified_revision*)|(Local_merge_and_checkout_typed_removal_refuse_ref_only_owner*)|(Incomplete_preparation_refuses_initial_and_resumed_land*)|(All_desktop_consumers_skip_ref_only_without_deleting_refs*)|(Pushed_tip_fast_forwards_exact_owned_checkout*)|(Endpoint_change_is_not_followed*)|(C1082_SettledDebtSyncOnlyFastForwardsToTheRecordedSource*)",
  "min": 7,
  "source": "1163840885e23f8267d80db709a83d1b091c21ec",
  "sourceState": "clean",
  "assemblySha256": "32d9d952eee403e7c6c9e274a7f6d8d08d05f772a293ae89bc52c7ace0a2a523",
  "started": "2026-11-06T15:20:15.724Z",
  "finished": "2026-11-06T15:21:36.484Z",
  "wallSeconds": 80.76,
  "exit": 0,
  "signal": null,
  "sourceUnchanged": true,
  "counters": "<Counters total=\"7\" executed=\"7\" passed=\"7\" failed=\"0\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"0\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />"
}
```

### CP-5

Filter: `/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)/*`

```text
BUILD SLOT granted lease=3f8589c6-7714-4676-9d8d-ee9cf0aed877 waited=0s maxcpucount=6
BUILD SLOT released lease=3f8589c6-7714-4676-9d8d-ee9cf0aed877 held=153s
{
  "id": "CP-5",
  "filter": "/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)/*",
  "min": 47,
  "source": "1163840885e23f8267d80db709a83d1b091c21ec",
  "sourceState": "clean",
  "assemblySha256": "32d9d952eee403e7c6c9e274a7f6d8d08d05f772a293ae89bc52c7ace0a2a523",
  "started": "2026-11-06T15:21:49.167Z",
  "finished": "2026-11-06T15:24:23.142Z",
  "wallSeconds": 153.975,
  "exit": 0,
  "signal": null,
  "sourceUnchanged": true,
  "counters": "<Counters total=\"47\" executed=\"47\" passed=\"47\" failed=\"0\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"0\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />"
}
```

### CP-6

Filter: `/*/*/(C0824RefOnlySnapshotMigrationTests*)|(C1729LayeringGuardTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)/*`

```text
BUILD SLOT granted lease=f6401462-d864-462c-9c31-d5720871e383 waited=0s maxcpucount=6
BUILD SLOT released lease=f6401462-d864-462c-9c31-d5720871e383 held=64s
{
  "id": "CP-6",
  "filter": "/*/*/(C0824RefOnlySnapshotMigrationTests*)|(C1729LayeringGuardTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)/*",
  "min": 27,
  "source": "1163840885e23f8267d80db709a83d1b091c21ec",
  "sourceState": "clean",
  "assemblySha256": "32d9d952eee403e7c6c9e274a7f6d8d08d05f772a293ae89bc52c7ace0a2a523",
  "started": "2026-11-06T15:24:46.058Z",
  "finished": "2026-11-06T15:25:51.786Z",
  "wallSeconds": 65.728,
  "exit": 0,
  "signal": null,
  "sourceUnchanged": true,
  "counters": "<Counters total=\"27\" executed=\"27\" passed=\"27\" failed=\"0\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"0\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />"
}
```

### GUIDANCE-STATUS

Filter: `/*/*/(TaskPlatformGuidanceTests*)|(StatusSnapshotScriptTests*)/*`

```text
BUILD SLOT granted lease=33de8127-c72e-46cb-8272-4ea79e796fbe waited=0s maxcpucount=6
BUILD SLOT released lease=33de8127-c72e-46cb-8272-4ea79e796fbe held=10s
{
  "id": "GUIDANCE-STATUS",
  "filter": "/*/*/(TaskPlatformGuidanceTests*)|(StatusSnapshotScriptTests*)/*",
  "min": 6,
  "source": "1163840885e23f8267d80db709a83d1b091c21ec",
  "sourceState": "clean",
  "assemblySha256": "32d9d952eee403e7c6c9e274a7f6d8d08d05f772a293ae89bc52c7ace0a2a523",
  "started": "2026-11-06T15:26:27.344Z",
  "finished": "2026-11-06T15:26:38.288Z",
  "wallSeconds": 10.944,
  "exit": 0,
  "signal": null,
  "sourceUnchanged": true,
  "counters": "<Counters total=\"6\" executed=\"6\" passed=\"6\" failed=\"0\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"0\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />"
}
```

### FULL-REDIRECT

Filter: `/*/*/(PhoneHomeRollingRunnerTests*)|(RemoteWorkspacePreparerTests*)|(C0824WorkspaceSlotServerTests*)|(PhoneHomeMirrorJoinTests*)/*`

```text
BUILD SLOT granted lease=5a921e22-b09c-4ca3-8d0e-17b298a89ca6 waited=0s maxcpucount=6
BUILD SLOT released lease=5a921e22-b09c-4ca3-8d0e-17b298a89ca6 held=85s
{
  "id": "FULL-REDIRECT",
  "filter": "/*/*/(PhoneHomeRollingRunnerTests*)|(RemoteWorkspacePreparerTests*)|(C0824WorkspaceSlotServerTests*)|(PhoneHomeMirrorJoinTests*)/*",
  "min": 91,
  "source": "1163840885e23f8267d80db709a83d1b091c21ec",
  "sourceState": "clean",
  "assemblySha256": "32d9d952eee403e7c6c9e274a7f6d8d08d05f772a293ae89bc52c7ace0a2a523",
  "started": "2026-11-06T15:27:12.967Z",
  "finished": "2026-11-06T15:28:40.498Z",
  "wallSeconds": 87.531,
  "exit": 2,
  "signal": null,
  "sourceUnchanged": true,
  "counters": "<Counters total=\"91\" executed=\"91\" passed=\"88\" failed=\"3\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"0\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />"
}
```

### INHERITED-LAND

Filter: `/*/*/(AgentTaskLandSourceFreshnessTests*)|(AgentTaskLandSourcePersistenceTests*)/*`

```text
BUILD SLOT granted lease=2bfda61d-19d8-408a-b2e7-6fa6fe245252 waited=0s maxcpucount=6
BUILD SLOT released lease=2bfda61d-19d8-408a-b2e7-6fa6fe245252 held=130s
{
  "id": "INHERITED-LAND",
  "filter": "/*/*/(AgentTaskLandSourceFreshnessTests*)|(AgentTaskLandSourcePersistenceTests*)/*",
  "min": 8,
  "source": "1163840885e23f8267d80db709a83d1b091c21ec",
  "sourceState": "clean",
  "assemblySha256": "32d9d952eee403e7c6c9e274a7f6d8d08d05f772a293ae89bc52c7ace0a2a523",
  "started": "2026-11-06T15:28:50.390Z",
  "finished": "2026-11-06T15:31:01.511Z",
  "wallSeconds": 131.121,
  "exit": 2,
  "signal": null,
  "sourceUnchanged": true,
  "counters": "<Counters total=\"94\" executed=\"94\" passed=\"86\" failed=\"8\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"0\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />"
}
```

--- next stage ---
next: done
handoff: Master publication confirmed; caller retains the owed qualification/Mutation/manual/activation work and timeout warning.
artifact: /work/worktrees/task-2b1406ee/.antiphon/task-2b1406ee.md
