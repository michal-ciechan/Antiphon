Landed S4a on master at fed5899951daa3c66be3dee31355e6261286dc01; replay, V-1 and layering inventory are complete. One retained setup timeout is a caller-dispositioned inherited-load warning.

# CARD-0824 S4a master landing (task fee547cb)

Base master: `3737148ee1e43e3695174b7a8e9befeb6d69fb41`. Reviewed subject: `cc591873b3eefc7c72046f102e0663530af01446`. Assigned branch: `feat/card-task-fee547cb`.

## Replay proof

47 linear cherry-picks with `-x`, in source order; all 47 stable patch IDs match. No merge commits and no replay conflict. Skipped only `.antiphon/task-5ec136af.md` report commit `f3a982888fd23a681e7eff08041216e9266c4b20`. Deliverable plans/investigations retained. Source branch untouched. A transient optional-index refresh lock refused one pick before it changed anything; the lock disappeared without deletion and the same pick then succeeded.

| Source SHA | New SHA | Stable patch ID (equal at source/new) | Conflict |
|---|---|---|---|
| 2cfaa79a9f689434c10eccda4a9c93961fb5808d | 2693525f7845b99970a29a5c2ba28c312472ab98 | 3519ed308b1816314289d933dfb0664e2560aeab | no |
| 1b92a522bc87fec585d280ab11b38b35023bd552 | c9f387e55bc8833b87e3ee33c18511e47a03dbcf | 53ac91fd513790825100d49974a5493b1a076ea0 | no |
| 771f57b03cc8a7cbdd09e84dd200959fc40b770d | f7d6440ce2b76e4bf748d5778b698a75a5dd6dfc | b35556656207075e3bee3de5106fbadf3d5f556b | no |
| eb30f38f8b082b1c4f0f9bcb4814097023dacfae | 341197a4b21a581f10ab784779f9e25bb877da2a | c07bca8bf4c94a9885f39a0f1e7e2d82a4d90a53 | no |
| 3f3055368bdb8b9cdec52f738b0d72ac18b93ca4 | f7d360dd2c81246d75648d13eb53e46e1e5a0408 | c652a59fdaf031072f8bb739b802bbbcd1ce62e5 | no |
| 04b33eaef6ba7211c06a6c97993f37c443d29206 | eaba0ff452393342c5c3e0bf8bdbc32810fef713 | d68ae8e7f1bc0b4fdf7e71c88569368d63247e37 | no |
| 4da21b87fd42e9dde21c12365619e1449f57b128 | aecdae2b69df7f613ca13dcf74a9ac71dec0c58b | 35cdd7d6c9443c1b8292eec8404dadc7f0bb1a97 | no |
| 3f8b0f4d3a3d4dcc77645a79309e6b2aef1ae717 | d03db8ba0267b92954fd5fbc430dfdd434c36979 | b4b0ed02256eece81f1dff57752f16de3df9fee6 | no |
| bf126465bfff6ab6e58b3139106311824a406c75 | 04753d553ee8c19779fbe2c5d6b959103a29070e | e8f390f7b9e0acc74cddc1ca78c9441e8e186d37 | no |
| c8035d4089e5d129c410f480dc19224bdc157009 | ddd75783cbac8662db493fd017c33b3e28972d44 | 3ad0896ea023ee1fbdd134984caac0b837a540e5 | no |
| 7b7c8d7a7146e76866442744d1e3f32a3ad8ed6f | a22a723523a395d897ede75446865bd39e4faa29 | f1a4eed3a722179ce7b9270c7d96b39836512e45 | no |
| e3e64893ef3a52f26f4b23bc0e28f04bee1d523c | 60cb6015bf2e6069d63ebe9eb13ee84fa986acc5 | 827fbab75ef2fddcc1465a71b49c83b646441a59 | no |
| 21149fbb9ee9f9f78bded33ef5a1ca206b684a9e | e5dc038522ab617558347e4fcf4e3b5250e243b0 | bd79966cb19cbf8e831364d7f0f347487850471c | no |
| f7dad7e5af2f835801be9fe70b83788af8089904 | 45b68c7ad92610421305d574ffb84113f69f964a | b99ed980e7b202aee88d1cba6f3bc7c0d05ca332 | no |
| 6c4b2817259b3822ca4ac0a428fb3f6e61289a88 | 3d74420517f3107bac199d0322319ec8bd48c1df | 25a29afa4565039f09d9da0089e25717bed0a6e3 | no |
| b13afc2f44449aa8a48a8e9bd1ce3d3ee6371be3 | dc746ae47560ecf4ce04b25f64bf4f724a0b32f4 | 86abde32388fa892307121422e024e82812f5b87 | no |
| 044f7c535758bf72f1abb121cf033f039a919fce | 2bbe1c3b689a90bf51d7daaf76782007139918f1 | 6ccdb0569ea27aa57c5b324f7f007a7aac97c022 | no |
| a0e29dd04f8983c8c0143fe8fc1b5b6678f00af9 | 987aa33dad1caf0f36c174936fb0196798783b3d | 7a104f8582ccf46bd38313488ccfdc4f3220e01a | no |
| 883fb45ce12ac04391d4dc490e20a150f3ab2a22 | abfa8533044a88f030dcc991f4645310ed65d2cc | 1794830b6a90b2962cd8e91bac4e408873c8fc21 | no |
| 4a8abfe1b03c1a91fcae000a0d58baa72884e5fe | 27a88f14aedad82c830bdac103bb926fca7c44a3 | c9a3a038916c1f1bec37062bdd865beacda6dd3e | no |
| 488f37182d3e7ea48950077c65ece04994f664d4 | 0b45fe14176fc306d4d1ce8667b89816d2ed122f | 7acbcbd9496a6ce33f73886884da8938ce88bad4 | no |
| 52c3f17515baa5be2adbfc6bc4651da1c13a3391 | 33642b314859e04198fdaab67cd60e8ee994a99f | ac7ac05d583ad4baf89009d53eb1b88a82a46ab4 | no |
| e1861f7f5775e8c06d7033023970d426fe3ae4ba | d9dfe336d9b4a490589b3b27d61b0387fda66d90 | 347b434da2a81c78fa8e9d7eb29d5844b4c0573c | no |
| f54ff3176c9668a77d0ea9294728780c76e03e60 | 521b467e0aa4bdf7bf131614bc04a6fd627fe2a6 | b41fc8f2f1bd291dee74efe0aae7569b7e5fe6f1 | no |
| 31434ba607356fc19ba395290f999305b77bbad3 | 4fd7c0e46c7a0b6f2a07d9c3c29429a99028d50e | 14239ba847fa807ce9d05ba2ddf60da2f79e9337 | no |
| aba1f2cfd868d1630d02cf1b8adb7281f08e85b8 | 2cbd0c8f12d30076e9103dbded8289a631c47058 | 82b03ceca31778a9a23b066836a00bef8144759a | no |
| 3f168ae2f59d87f72c36585e165bba1d0dffecf5 | 629b1e924f25ecad13c36a53ec8ac3fd4a7c5deb | 2be2ef9a251a891528811252dc11a8b72727f884 | no |
| 2c27d7b7b885d9a9bb22577b9fe602c95419f489 | 02b4b74e521ba081daaecd7803c9fdcd0cfae0db | 3a7010340e356951202409fdb644f2c73006329c | no |
| e80e98dc94a8e61c8f63d4f108da008494ae288c | 164c16e9614a83676c36f4ed980e3d6f56188472 | aadfa6b62b398b7d91f3a4b6f8510df50061c07c | no |
| 901b3c3b4e3c6e070f54aea1ff3da53d49348299 | d928586b451faa62ec05db79d5379616267f723d | c8f4175d2c4917eda0b4c7f3b5fb126b407b14fb | no |
| adf87bdc6f67dfddded17c41c7050f5b69ce40ef | b4a9d2ca120db4ebb51d09a85e6bc55561c8b799 | 812ba5ed6bf83ae9ff08e8d405555ef4e329ffa6 | no |
| 839d798cdd752eb090b1836a67b33742d8e37f1e | 0a867479ae7d4be99a016d309603af17e07f15ad | 66d6fac100393c405a63b98a585577c6a9d07557 | no |
| b9b81c91e5ea0fc4aadd1c444e9a2af9372579ee | 0997739521b6e5e7049fec53e84f7ba2b29559ae | 5f858f310933a13a1d87f4549ad1970b337a5716 | no |
| 29bcfa41be6dd2210e43ae3fb8b2be432cb9d978 | 7ad1ee9aec23f4d0d877bb73bcd1a03230bba2e9 | b90ed2e1a7ca8ffcfdd1f416e6007a80b7d5a574 | no |
| 558c0accb973d16ad65cc511d323bec36d908fbb | 64692e567fc42568c6de07ccd78b5cd2b8e82c2f | ab488ea035d49d9335ff996a4d42ce29b3311217 | no |
| 82fc3d99106d68b4b290836d15315c2755e77a10 | f7e87f1723b682bb3f61c3708dbfe432d360b362 | dd7609365291df8d293433df95e1022c41e0a81a | no |
| 1f4b4cd768ee73b6be0f8bcbac782cbc4436f3bc | 3f07a8d3756e5265efb352983c2614b154580af0 | d8a24da8f64f32c7bb7ba789cf8ef59665b38b21 | no |
| 32eb3a841569a7fe277df5f63793c43be9842767 | 512f6a6dca77dd59fff8b9a8af0292fd0269f94e | 0b86f84fba5cf204112cadd099f5fc73b605cb2e | no |
| 2859f486459ae5e29ce6fc272cd46d1818b78d5e | f2428131d06e7de8be184f35728c9caf1a03d0e7 | aa2768b261762fb77595ef3d5ee7ae4f1be48188 | no |
| 68272124b3b8fb378f77d4fd2693d7ed654da0c2 | 59e12a7126c37521f432ab742f6ca82b503b9216 | 5ef089c118148de82ae650d38a9cb0dfb0f19923 | no |
| 8c519a7598c169fdc063bfcddb655644db90381c | 7606921cfda68a9d86869bf18672288af74d4f9e | f7cdebf63c301d95491a9ae50792e89e52e2af23 | no |
| 22bb9eb8dbd290c4bf90f0599ea2a6bda156569c | 450d9b6fd5f4035ce23d88285fe5703c2433e43f | 833e6f5bd400fa17edb418968667496c23b00f0b | no |
| a4aff79c0bcbf6fa39592033be9abb2ca7536748 | 2f6c0a1c848736080c99a7885f42eae827196954 | 8c5245119efb33ff9c4672a0637f2a223f61ad44 | no |
| f28571c8e216a6cff7e8c2789308636ae3423f29 | a4c51d80db01c078b0c754aa4eb1ca87ccc5b9be | 933ce59b81d7b5e812850051055d22b763bd5b47 | no |
| 0ac54954def9d4ee1753a489ed6e42c9369c929c | 9c91a904802267f70f2ca2a0e227015e3dae38f9 | 416b4ce85fb875d2463f84483741565469b66a34 | no |
| 285ea7b90458143c4c214f9b91104732c0af6c84 | 46af879d9d9242664b60bc1aa8a9a1be90938b1b | 2d56867633b65147d452f04a39289f5bd8e52a50 | no |
| cc591873b3eefc7c72046f102e0663530af01446 | 4daabe5ed5f162141b6d6f54c0c252151b17310d | a98334796ee5ec748ab4931e9ff6a46fc8ddc1bf | no |

## Prior integration resolutions retained

No new hunks required resolution. The current master plus reviewed subject merge-tree equals all 27 replayed deliverable paths except the intentional CP-3 minimum correction.

- DesktopLegacyReleasePolicyService: S4a StopReason switch/tick-budget check precedes S5b-1 before-action boundary, fresh task reload and RefOnly hold. The inherited S5b-1 revision-token advisory is unchanged.
- GuardedWorktreeRemoval: the reviewed A+B live-authorization/content-custody seam is retained with master RefOnly owner fences; resumed set-aside reads gate then custody/DeleteApproved; content binding precedes final gate/move; unregister checks at child start; post-unregister gate precedes delete and custody; branch-delete child start re-reads authorization.
- RemoteWorkspaceService: live switch read follows runner description and precedes BeforeRemoveMirrorCommand and send; transport re-reads after the send semaphore. TaskWorktreeRetirementService retains RefuseDesktopReleaseAsync beside its gated mirror signature.
- The S5b-1 no-checkout branch includes the initial StopLegacyContinuationAsync guard and live mirror-send callback, plus both already-off and late-switch witnesses.

All reviewed S4a fences remain: DeleteSettled, ContentSeal set-aside receipts, fd-relative no-follow Linux deletion, IgnoredSecretFloor, RunnerWorkspaceRetention, reflog salvage refs with 64-ref cap, direct/dangling-symbolic collision refusal, and retention pruning only during executing ticks. OFF means no automatic filing/release/delete/set-aside/salvage/prune, on both Checkout and no-checkout paths.

## V-1

Docs-only correction `c4e182ec60fd829a1fc536e9fec036a0c95e8d8d`: CP-3 Min 100 -> 61 in `docs/superpowers/plans/2026-11-06-card-0824-s4a-integration-b1-checkpoints.md`. Exact selection fresh verification pending below; historical red remains in prior reviews.

## Scope and identities

No migration or model snapshot changed; no csproj/build definition or target pin changed; no execution-policy change. No test file moved; discovery parity from relocation is inapplicable. None of the 40 Phase 0 R100 file moves overlaps an S4a touched path; master moves are retained.

Baseline graph -Check approved, digest D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03, count 69. Base nightly policyHash 2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72; base live input digest 417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939. Final comparisons pending below.

Authority: operator authorized master-only land and accepted Interim scope; no further approval requested. The task bundle requires pushing each commit to the assigned task branch, so that higher-priority contract supersedes the brief request to keep it local. No published commit was rewritten. No rc push, host setting change, restart or activation.

## Layering inventory correction

Preflight exact repository inventory method executed 1/1 at c4e182ec60fd829a1fc536e9fec036a0c95e8d8d, printing `VIOLATIONS 3 LISTED 166 STALE 0 AMBIGUOUS 3`. Its non-strict test pass was not credited as a clean inventory. Three real dependencies already present in the reviewed S4a stack are new to master’s inventory: policy -> Git salvage helper (pruning), mirror cleanup -> runner transport refusal exception (off-switch refusal), and guarded removal -> Application retirement service (system-caller identity). Under the brief’s express existing-debt exception, commit `fed5899951daa3c66be3dee31355e6261286dc01` lists exactly these under `# slice S4a`. Guard engine/map/strict flag unchanged; no behavioral repair. Raw preflight retained in layering-preflight/, layering-preflight.log and layering-preflight-summary.json. Full nine-method final inventory row is recorded below.

The initial serialized solution build at c4e182ec60fd829a1fc536e9fec036a0c95e8d8d passed with 0 errors/1927 warnings (275.93 s compiler, lease 277 s after 75 s admission). A second serialized native-output build is specifically required after the allowlist correction to bind the final tree; not an unexplained repeat. Extra prerequisite: baseline graph evaluator native Release bootstrap, 18.42 s compiler, 0 errors/warnings, lease 19 s after 15 s admission. Additional exact-method inventory preflight was required to identify allowable rows before ordinary final-tree selections.

## Final-tree build and guard

Final source `fed5899951daa3c66be3dee31355e6261286dc01`: serialized Antiphon.sln build passed, 0 errors/1927 warnings, native outputs, UseAppHost=false, BuildInParallel=false, maxcpucount=1. Compiler 192.68s, lease 194s, no admission wait; log final-inventory-build.log. Native test/server DLL SHA-256 identities are recorded in built-assembly-sha256.txt; source is unchanged/clean for final rows.

All nine layering methods passed, no skips, inventory `VIOLATIONS 0 LISTED 169 STALE 0 AMBIGUOUS 3`. The three nonviolating ambiguous syntax references are inherited; the engine/map/mode are unchanged. Raw TRX row LAYERING records census/read/analysis/total 0.034/0.119/8.870/9.023s and host duration 13.330s.

The final mechanical merge comparison differs only for the intentionally omitted prior .antiphon report, V-1 minimum correction, and three-row layering inventory correction. All production/test code equals the mechanical composition. Nightly policyHash and live input digest compare byte-identically to base.

## Commissioned verification roster

One native final-tree solution build is reused by serial --no-build rows, same UseAppHost=false property; every driver has its own build-slot lease, TEMP/TMP=/tmp. Groups contain at most three rows. Group 4 (layering) ran first to settle the permitted inventory correction; groups 1, 2 and 3 follow. CP-3 also supplies the overlapping full CP-91 policy and CP-92 mechanical classes once; no duplicate green sweep is owed.

| Row | Group | Project | Exact filter | Minimum executed | Invariant |
|---|---|---|---|---:|---|
| S4A | 1 | tests/Antiphon.Tests | `/*/*/(C0824DesktopLegacyRelease*)\|(C1708ReflogSalvageTests*)/*` | 202 | policy, switch including both RefOnly witnesses, native/settings, fifteen salvage cases |
| REMOVAL | 1 | tests/Antiphon.Tests | `/*/*/(TaskWorktreeRetirementTests*)\|(WorktreeResidueSweepTests*)\|(WorktreeGuardedCleanupTests*)\|(SettledWorktreeRemovalTests*)\|(LandingGitTests*)\|(LandingRemovalPolicyControlTests*)\|(WorktreeCleanupJournalTests*)\|(WorktreeCleanupPresentationTests*)\|(WorktreeCleanupSettingsValidatorTests*)\|(CardDoneWorktreeCleanupTests*)\|(CardWorktreeCleanupRecoveryTests*)\|(CompletedCardWorktreeRemovalTests*)\|(AgentTaskLandPublicationTests*)\|(WorktreeNoFollowDeleteTests*)/*` | 583 | all named GuardedWorktreeRemoval consumers, no-follow and content-custody fences |
| RETENTION | 1 | tests/Antiphon.Tests | `/*/*/(C1677RetentionServerTests*)\|(C1677RetentionSettingsTests*)\|(PhoneHomeConnectionTests*)\|(C0824WorkspaceSlotServerTests*)/*` | 138 | RunnerWorkspaceRetention through actual server transport, settings, workspace slots |
| REFONLY | 2 | tests/Antiphon.Tests | `/*/*/(C0824RefOnlyIdentityTests*)\|(C0824RefOnlyGateTests*)\|(C0824RefOnlyLandingTests*)\|(C0824RefOnlyCleanupTests*)/*` | 20 | S5b-1 identities/gates, producer recipient transcript, cleanup and switch-off no-checkout witness |
| CP3 | 2 | tests/Antiphon.Tests | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)/*` | 61 | exact B-1 plan CP-3, classification CP-91 and mechanical CP-92 |
| GUIDANCE | 2 | tests/Antiphon.Tests | `/*/*/(TaskPlatformGuidanceTests*)/*` | 1 | task platform mechanical instruction guidance |
| RUNNERRETENTION | 3 | tests/Antiphon.SessionRunner.Tests | `/*/*/(C1677RetentionInventoryTests*)\|(C1677RetentionDispatchTests*)\|(C1677RetentionEngineTests*)\|(C1677RetentionNativeProcessTests*)/*` | 1 | actual RunnerWorkspaceRetention engine, dispatch, inventory and native cases |
| LEGACYBASE | 3 | tests/Antiphon.Tests | `/*/*/DelegationWorktreeTests*/(C508_ReuseKeepsRecordedBase*)\|(C508_BaseResolutionRequiresOwnedLease*)` | 2 | S5b-1 unchanged Checkout base/admission CP-5 |
| LAYERING | 4 | tests/Antiphon.Tests | `/*/*/(C1729LayeringGuardTests*)/*` | 9 | CP-1 nine layering guard methods; inventory must have zero unlisted violations |

Invocation shape: `TEMP=/tmp TMP=/tmp timeout --foreground 45m pwsh -NoProfile -File scripts/build-slot.ps1 -Label fee547cb-<ROW> -SlotWaitMinutes 20 -- dotnet run --project <PROJECT> --no-build -p:UseAppHost=false -- --treenode-filter <FILTER> --report-trx --report-trx-filename run.trx --results-directory /work/test-evidence/task-fee547cb/rows/<ROW>`. Each rows/<ROW>/invocation.json stores literal argv, source, start/end and exit; driver.log and run.trx are the raw receipts. Direct Merge verification was commissioned by the brief; these are not checkpoint-tool certificates or Full Review claims.

## Retention initial failure (retained)

At final source fed5899951daa3c66be3dee31355e6261286dc01, RETENTION executed 138 cases: 137 passed, 1 failed, no skips. Failure: C0824WorkspaceSlotServerTests.C0824_ReleaseRoundTripRealRunner(direct), `TimeoutException: scratch git exceeded its operation deadline`, in C0824SlotTransportWorld.BuildRepository line 158 (scratch clone) before the exercised release round trip. The fixture budget is one second, unchanged; no timeout/retry/assertion edit is made. The class, fixture and scripts/lib/CheckpointProcessRunner.cs are unchanged from base (timeout-input-blobs.txt). Byte equality alone is not a reproduced inherited-red verdict. An exact-base checkout at /work/worktrees/task-fee547cb-baseline, detached at 3737148ee1e43e3695174b7a8e9befeb6d69fb41, is built only to run the failed method. Initial RETENTION driver/TRX remains unedited in rows/RETENTION. Base/candidate diagnostic results and final gate disposition follow below.

Exact-method diagnostic comparison: base 3737148ee1e43e3695174b7a8e9befeb6d69fb41 passed 2/2 (29.382s host, 34s lease); unchanged candidate fed5899951daa3c66be3dee31355e6261286dc01 passed 2/2 (28.283s host, 31s lease), no skips. Both direct and reply-lost arguments are included because the exact method is parameterized. All execution identities are fresh. The initial timeout is not reproduced, and no assertion reproducer establishes inherited versus introduced attribution. This is not a persistent candidate-only product failure, but it also does not make the original commissioned RETENTION row green. The brief singles out only the inode-reuse case for a flake exception, says slot tests must pass, and requires all rows green; Publication was initially held for caller disposition of this fixture deadline; the subsequent explicit PUSH decision below releases that hold. No timeout widening, automatic retry, or production/test repair. The paired methods are additional, specifically justified failure-diagnostic runs, not reruns of green rows.

The base-only build passed with 0 errors/1729 warnings, 475.62s compiler/476s lease. It ran only to verify the failure at the exact base; same UseAppHost=false, native default outputs, through its own build slot. Source was exact and clean before/after its method execution. Raw logs: base-failure-build.log, base-failed-method.log, candidate-failed-method.log; raw TRX directories base-failed-method/ and candidate-failed-method/; comparison timeout-comparison.json.

## Final ordinary outcomes

| Row | Passed | Failed | Skipped | Admission s | Lease held s | Driver wall s |
|---|---:|---:|---:|---:|---:|---:|
| S4A | 202 | 0 | 8 | 0 | 111 | 112.510 |
| REMOVAL | 583 | 0 | 7 | 420 | 198 | 618.523 |
| RETENTION | 137 | 1 | 0 | 375 | 87 | 462.951 |
| REFONLY | 20 | 0 | 0 | 0 | 114 | 115.145 |
| CP3 | 61 | 0 | 0 | 0 | 128 | 128.577 |
| GUIDANCE | 5 | 0 | 0 | 0 | 8 | 8.687 |
| RUNNERRETENTION | 109 | 0 | 0 | 0 | 25 | 27.101 |
| LEGACYBASE | 2 | 0 | 0 | 0 | 37 | 38.447 |
| LAYERING | 9 | 0 | 0 | 0 | 16 | 16.397 |

Ordinary totals: 1,129 executed, 1,128 passed, one failed, 15 native skips (1,144 selected). All nine commissioned rows ran at the final committed source, with fresh unique execution identities and all minimum counts met. The original ordinary run remains RED, solely RETENTION; paired base/candidate exact-method passes are separately recorded, not substituted into the initial whole-row result. The caller has classified this as inherited load and explicitly authorized publication with a loud warning. No green row was repeated. The inode witness passed first time (0.026s); no inode retry was needed.

CP-3 complete roster, exactly 61/61: TestClassificationPolicyTests 39, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11. This closes V-1 with the unchanged exact filter. The historical two NativeLinux policy failures are absent on the combined current tree. C1708 salvage is 15/15, switch class 30/30 including the RefOnly late-switch witness; RefOnly 20/20 includes the initially-off no-checkout witness and real caller delivery/recovery checks. Runner retention 109/109; platform guidance 5/5; legacy-base methods 2/2.

Performance: ordinary row drivers total 1,528.338s wall, including 795s admission (52%) and 724s lease-held time; these are distinct measures, not compiler CPU or a sum of nested test durations. The complete commissioned verification including builds and exact-failure diagnosis exceeds 30 minutes; caller follow-up belongs with existing Debug 5ae98d31. Preserve the phase logs and actual loaded-host admission; no measured p95 is invented. Re-estimate SourceLanding groups before admission, retaining one cold packed build per group and method-scoped PC cycles. External-call span details not captured by the ordinary receipts remain unknown.

## Final graph, readiness and custody

Final land-graph -Check APPROVED, exit 0, digest D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03, count 69, lease 29s/no admission wait. Both base/final are approved; no inherited graph warning, no -Update, no build-definition or target-pin delta. Both evaluator canary directories were removed by the evaluator.

Immediately before publication, fetched master was still `3737148ee1e43e3695174b7a8e9befeb6d69fb41`; merge-tree against final source exited 0 with tree `352b3118e9a7447a0e931f9b0a416f43738e2f2b`. Plain fast-forward `git push origin fed5899951daa3c66be3dee31355e6261286dc01:refs/heads/master` succeeded, and `git ls-remote origin refs/heads/master` confirmed new master `fed5899951daa3c66be3dee31355e6261286dc01`. Replay rounds: one initial replay, zero master-movement replay loops, one successful master push, zero retries. Publication contains the verified source only; neither the earlier blocked-report commit nor this final report amendment was pushed to master. The report amendment is committed and pushed to the assigned task branch after publication. No published commit was rewritten. No build or green selection was repeated because master and verification source were unchanged.

Migrations/model snapshot tree is identical, `d39a86d422eb6aa3b4ede38003f2e5299577f37e` at both base/final. Evidence audit over source base..HEAD: 49 commits, zero evidence entries, zero violations. Git diff --check passes. No test file relocated; parity obligations from file moves are inapplicable.

All foreground drivers were joined. Removed 104 task-owned native bin/obj roots using individually literal absolute paths, no glob/force removal, with tracked-path/link/creation-time checks and no active output users in the primary cleanup audit. The exact-base detached worktree was clean at the expected SHA and removed with `git worktree remove /work/worktrees/task-fee547cb-baseline` (no force). The given worktree remains clean; only the supplied ignored inbox remained before writing this report. No foreign tree, volume or shared cache was deleted. Raw evidence is retained externally at `/work/test-evidence/task-fee547cb`; cleanup-preflight.json, cleanup-final.json and cleanup-additional-roots.json record ownership/deletion. No report-only amendment changes verification source.

## Caller decision and publication

LOUD WARNING: the original RETENTION row includes C0824WorkspaceSlotServerTests.C0824_ReleaseRoundTripRealRunner(direct), a one-second scratch-Git SETUP TIMEOUT. The exact parameterized method subsequently passed 2/2 at both base and unchanged candidate. Original 137-pass/1-fail row evidence is retained; no timeout/assertion was loosened and no green row rerun. No persistent candidate-only product failure was demonstrated.

Caller input .antiphon/inbox/90a107af-a586-4e4c-9e3e-deefb0addbab.md explicitly decides PUSH under the operator's 2026-11-06 land instruction. It classifies this as the inherited-load fixture family seen on Windows rc and this host, treats it as a loud warning rather than a hold, and identifies the pending measured-budget repair origin/feat/card-task-ab37b2cf tip 4eb141231. That classification comes from the caller's wider evidence; this task's isolated base run passed rather than independently reproducing a red. The initial failed run and its costs are unchanged. Publication follows this explicit disposition; the original hold is resolved, not silently relabeled green.

All authorized work is complete. No additional approval was sought after the PUSH decision. Master is published but no host was activated; no rc branch push, restart, deployment or host configuration change occurred.

## Three S4a layering debts inventoried

Commit fed5899951daa3c66be3dee31355e6261286dc01 adds exactly these existing reviewed-stack dependencies to tests/Antiphon.Tests/Layering/allowlist.txt under '# slice S4a':

| Referencing file | Reference | Referenced file | Debt retained |
|---|---|---|---|
| server/Application/Services/DesktopLegacyReleasePolicyService.cs | WorktreeReflogSalvage | server/Infrastructure/Git/WorktreeReflogSalvage.cs | Application release policy calls the Git-owned salvage pruning helper. |
| server/Application/Services/RemoteWorkspaceService.cs | PhoneHomeRequestRefusedException | server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs | Application mirror cleanup handles the runner transport's typed live-switch refusal. |
| server/Infrastructure/Git/GuardedWorktreeRemoval.cs | TaskWorktreeRetirementService | server/Application/Services/TaskWorktreeRetirementService.cs | Git guarded removal reads the Application retirement service's system-caller identity. |

Exact allowlist rows:

```text
server/Application/Services/DesktopLegacyReleasePolicyService.cs -> WorktreeReflogSalvage @ server/Infrastructure/Git/WorktreeReflogSalvage.cs
server/Application/Services/RemoteWorkspaceService.cs -> PhoneHomeRequestRefusedException @ server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs
server/Infrastructure/Git/GuardedWorktreeRemoval.cs -> TaskWorktreeRetirementService @ server/Application/Services/TaskWorktreeRetirementService.cs
```

The guard engine, map and strict:false setting are untouched. Final inventory: VIOLATIONS 0 LISTED 169 STALE 0 AMBIGUOUS 3; nine methods passed.

Still owed: the 15 Windows-native cases skipped on Linux (including C0824DesktopLegacyReleaseNativeTests and filesystem-sharing/junction cases), the whole Unit lane, SourceLanding Mutation for every S4a/CARD-1717/CARD-1708/new-witness control (one cold packed build per group, method scoped, re-estimate because admission dominates), and the next rc cut. S4a is ON by default in code and was not activated in any host. The next rc cut will carry the landed stack ON by default.

--- next stage ---
next: done
owed: Windows-native cases; whole Unit lane; SourceLanding Mutation for S4a/CARD-1717/CARD-1708/new-witness controls; next rc cut.
handoff: Master fed5899951daa3c66be3dee31355e6261286dc01 is published. Retain the caller-dispositioned inherited-load timeout warning and pending measured-budget repair; schedule SourceLanding sweeps with one cold packed build per group and revised admission budgets, then the next rc cut. No host activation occurred.
artifact: /work/worktrees/task-fee547cb/.antiphon/task-fee547cb.md
