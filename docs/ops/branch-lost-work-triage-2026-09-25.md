# Possible lost-work branch triage — 2026-09-25

Source: `docs/ops/branch-cleanup-2026-09-25-possible-lost-work.csv` at `1d0461b2`. Card terminal reasons were read with `scripts/card.ps1 get`. Each comparison used `origin/master` and merge-base diff; master history was searched by card subject and rebased patch equivalence.

| Verdict | Branches | Result |
|---|---:|---|
| SUPERSEDED | 39 | Landed through a rebased/later card series on `origin/master`. |
| ABANDONED-OK | 7 | CARD-0575 explicitly says “Superseded, not shipped.” |
| LOST | 0 | None. |
| HUMAN | 0 | None. |

No follow-up cards are proposed because there are no LOST branches.

+## CARD-0118

Terminal reason: Fixed and merged to master (cec61de).

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-0100d53d` | SUPERSEDED | 1 | 1 / 332 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-0100d53d`

Commits:
- `b5642977820d4a30b6ad0e2043112f37e8703ba9` docs(plan): CARD-0118 Codex test residue investigation and cleanup design

Touched-file totals:  1 file changed, 332 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-0100d53d`.


+## CARD-0478

Terminal reason: Shipped: landed 90fd1c7c to master.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-0505ce7d` | SUPERSEDED | 5 | 34 / 3406 / 61 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-48888860` | SUPERSEDED | 33 | 109 / 23937 / 278 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-4adbee37` | SUPERSEDED | 30 | 108 / 23639 / 274 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-cd1d39f7` | SUPERSEDED | 10 | 90 / 12301 / 244 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-0505ce7d`

Commits:
- `07652c0e2d00579949d0f7499b4c82625921cb74` wip(CARD-0478): add tested native custody foundation
- `032dd2ce05c9fb726d75372b50321fb3581d99c8` feat: persist host and runner verification custody receipts
- `96f0c17c0d8466d2e1161fe504f1e7893168ea5f` fix: preserve legacy refusal behavior and fence replaced custody stores
- `ab72f4305c751f3df4a955aa05fd8240af7963f4` fix: include tracked descendants in explicit kill-all
- `08d2d111761e982766d2e529be2b983ac1a6b185` docs: record incomplete host and runner custody checkpoint

Touched-file totals:  34 files changed, 3406 insertions(+), 61 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-0505ce7d`.

### `feat/card-task-48888860`

Commits:
- `07652c0e2d00579949d0f7499b4c82625921cb74` wip(CARD-0478): add tested native custody foundation
- `032dd2ce05c9fb726d75372b50321fb3581d99c8` feat: persist host and runner verification custody receipts
- `96f0c17c0d8466d2e1161fe504f1e7893168ea5f` fix: preserve legacy refusal behavior and fence replaced custody stores
- `ab72f4305c751f3df4a955aa05fd8240af7963f4` fix: include tracked descendants in explicit kill-all
- `08d2d111761e982766d2e529be2b983ac1a6b185` docs: record incomplete host and runner custody checkpoint
- `045146dc0786cefbccc408261ca324c695cb7675` WIP CARD-0478 application source admission and custody cleanup; validation pending
- `fc2a58344917c72699690026a9daa6ee9af47555` Pin verification Git fixture checkout policy and cover receipt identity variants
- `32998e92b7dc2c75ed886a5591d191d86b78f3c6` Fence snapshot subdirectories and add real orphan-to-cleanup acceptance
- `848e3f16057c4cdce27f4c54d331429a754e1b22` Reorder active contracts to post-land verification; tighten final cleanup authority
- `244316125c888af785a33363696861e73dc4517a` Record application custody checkpoint, ordinary evidence and remaining acceptance
- `2632745d29906ce98fc67b8464860fbe129c28f3` feat(CARD-0478): typed execution detail, unresolved sourced release, named V/R tests
- `ffbb85675b18adbe97b6dc5fe8abe38e75503718` test(CARD-0478): import ProcessSpawnLimit in new ordinary classes
- `9f63e8c1bc6b24ae9e5d02d25120f42207c7cbd1` test(CARD-0478): fix G-189 local name clash
- `d3dff839aae511396c58e14122ce6695e0e821dc` test(CARD-0478): accept 422 refusals and no-retype after UserPrompt receipt
- `0cf4721e6b5a953d659c882953bbc687951e4b4e` test(CARD-0478): add remaining named G methods and V-4/V-9/V-15/V-16 crash matrix
- `8656b3c55331d4b97d4a512c679b73dc56f406e2` test(CARD-0478): fix V-9 land crash matrix to match swallowed reconcile faults
- `0f0fe9db901b7f23f1bc7869fecaf65f094e75df` test(CARD-0478): pin sourced admission authorization and capacity to the project bucket
- `f0693d47215dc29e12469f575e9796fc607dceb7` test(CARD-0478): close a prior attempt before reserving the next execution
- `c604692a748a8a58b72d63b20b0f4bbeb94ad442` test(CARD-0478): fence corrupt runner stores and record crash-matrix evidence
- `ffcbff7984acdf503105d3f87961ed04110224ea` test(CARD-0478): replace aliased cleanup/custody/V-9 oracles with independent fixtures
- `25fcb0088bead84def09c33a9528328ff0702456` test(CARD-0478): pin G-094 gitdir rewrite, G-113 no-force remove, G-223 complete first receipt
- `1ff834bb6d8ce3f9e2950a2cd4a595a9603256e3` test(CARD-0478): enqueue V-9b worker briefs with ExecutionTaskId and live session
- `951d862cf316c0e4734be272cf869c78100273cb` test(CARD-0478): repair remaining V-9 receipt oracles and aliased G methods
- `7dbc0f4e45607c7e52d8f3d9c19fe4ea5d6ed305` test(CARD-0478): confirm spilled V-9 receipts from typed pointer and idle CatchUp
- `e3db8fd324a305a2ee4fce276ff705f3e6a65e7b` test(CARD-0478): persist isolated UserPrompt when runner transcript is unavailable
- `c86c927aaa43db35dabcfeed192886549b44005a` test(CARD-0478): do not duplicate CatchUp UserPrompt on queued receipt recovery
- `0616e9ca80ffb9479c73b071a2dda9e503f058c7` test(CARD-0478): repair remaining delivery receipt oracles
- `252de2bf6885063934a4300de0db0adff3a660f4` test(CARD-0478): fix identity, CatchUp duplicate, and busy receipt-save oracles
- `59f915aac1f6121fe3ed83acd226495892ce920c` fix(CARD-0478): stamp completion notes and dispatch V-9b through the real launcher
- `2680b1c6a9de7e9a9eea20df1c6e052b86d0afb4` test(CARD-0478): stop completion scanner before G-150 receipt confirm
- `23bba3962fbdd07e13bea3d414679bf37de18a16` fix(CARD-0478): atomically stamp completion notes and repair partial enqueue
- `4b2041de05993d947d7b617a892cc7bdf73e73c2` test(CARD-0478): rename partial-enqueue locals that collided in one method
- `3b021db9ddaef6a6b2d98de63d387d335082f8cb` test(CARD-0478): add stamp-repair controls for atomicity, scanner, and duplicate-skip

Touched-file totals:  109 files changed, 23937 insertions(+), 278 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-48888860`.

### `feat/card-task-4adbee37`

Commits:
- `07652c0e2d00579949d0f7499b4c82625921cb74` wip(CARD-0478): add tested native custody foundation
- `032dd2ce05c9fb726d75372b50321fb3581d99c8` feat: persist host and runner verification custody receipts
- `96f0c17c0d8466d2e1161fe504f1e7893168ea5f` fix: preserve legacy refusal behavior and fence replaced custody stores
- `ab72f4305c751f3df4a955aa05fd8240af7963f4` fix: include tracked descendants in explicit kill-all
- `08d2d111761e982766d2e529be2b983ac1a6b185` docs: record incomplete host and runner custody checkpoint
- `045146dc0786cefbccc408261ca324c695cb7675` WIP CARD-0478 application source admission and custody cleanup; validation pending
- `fc2a58344917c72699690026a9daa6ee9af47555` Pin verification Git fixture checkout policy and cover receipt identity variants
- `32998e92b7dc2c75ed886a5591d191d86b78f3c6` Fence snapshot subdirectories and add real orphan-to-cleanup acceptance
- `848e3f16057c4cdce27f4c54d331429a754e1b22` Reorder active contracts to post-land verification; tighten final cleanup authority
- `244316125c888af785a33363696861e73dc4517a` Record application custody checkpoint, ordinary evidence and remaining acceptance
- `2632745d29906ce98fc67b8464860fbe129c28f3` feat(CARD-0478): typed execution detail, unresolved sourced release, named V/R tests
- `ffbb85675b18adbe97b6dc5fe8abe38e75503718` test(CARD-0478): import ProcessSpawnLimit in new ordinary classes
- `9f63e8c1bc6b24ae9e5d02d25120f42207c7cbd1` test(CARD-0478): fix G-189 local name clash
- `d3dff839aae511396c58e14122ce6695e0e821dc` test(CARD-0478): accept 422 refusals and no-retype after UserPrompt receipt
- `0cf4721e6b5a953d659c882953bbc687951e4b4e` test(CARD-0478): add remaining named G methods and V-4/V-9/V-15/V-16 crash matrix
- `8656b3c55331d4b97d4a512c679b73dc56f406e2` test(CARD-0478): fix V-9 land crash matrix to match swallowed reconcile faults
- `0f0fe9db901b7f23f1bc7869fecaf65f094e75df` test(CARD-0478): pin sourced admission authorization and capacity to the project bucket
- `f0693d47215dc29e12469f575e9796fc607dceb7` test(CARD-0478): close a prior attempt before reserving the next execution
- `c604692a748a8a58b72d63b20b0f4bbeb94ad442` test(CARD-0478): fence corrupt runner stores and record crash-matrix evidence
- `ffcbff7984acdf503105d3f87961ed04110224ea` test(CARD-0478): replace aliased cleanup/custody/V-9 oracles with independent fixtures
- `25fcb0088bead84def09c33a9528328ff0702456` test(CARD-0478): pin G-094 gitdir rewrite, G-113 no-force remove, G-223 complete first receipt
- `1ff834bb6d8ce3f9e2950a2cd4a595a9603256e3` test(CARD-0478): enqueue V-9b worker briefs with ExecutionTaskId and live session
- `951d862cf316c0e4734be272cf869c78100273cb` test(CARD-0478): repair remaining V-9 receipt oracles and aliased G methods
- `7dbc0f4e45607c7e52d8f3d9c19fe4ea5d6ed305` test(CARD-0478): confirm spilled V-9 receipts from typed pointer and idle CatchUp
- `e3db8fd324a305a2ee4fce276ff705f3e6a65e7b` test(CARD-0478): persist isolated UserPrompt when runner transcript is unavailable
- `c86c927aaa43db35dabcfeed192886549b44005a` test(CARD-0478): do not duplicate CatchUp UserPrompt on queued receipt recovery
- `0616e9ca80ffb9479c73b071a2dda9e503f058c7` test(CARD-0478): repair remaining delivery receipt oracles
- `252de2bf6885063934a4300de0db0adff3a660f4` test(CARD-0478): fix identity, CatchUp duplicate, and busy receipt-save oracles
- `59f915aac1f6121fe3ed83acd226495892ce920c` fix(CARD-0478): stamp completion notes and dispatch V-9b through the real launcher
- `2680b1c6a9de7e9a9eea20df1c6e052b86d0afb4` test(CARD-0478): stop completion scanner before G-150 receipt confirm

Touched-file totals:  108 files changed, 23639 insertions(+), 274 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-4adbee37`.

### `feat/card-task-cd1d39f7`

Commits:
- `07652c0e2d00579949d0f7499b4c82625921cb74` wip(CARD-0478): add tested native custody foundation
- `032dd2ce05c9fb726d75372b50321fb3581d99c8` feat: persist host and runner verification custody receipts
- `96f0c17c0d8466d2e1161fe504f1e7893168ea5f` fix: preserve legacy refusal behavior and fence replaced custody stores
- `ab72f4305c751f3df4a955aa05fd8240af7963f4` fix: include tracked descendants in explicit kill-all
- `08d2d111761e982766d2e529be2b983ac1a6b185` docs: record incomplete host and runner custody checkpoint
- `045146dc0786cefbccc408261ca324c695cb7675` WIP CARD-0478 application source admission and custody cleanup; validation pending
- `fc2a58344917c72699690026a9daa6ee9af47555` Pin verification Git fixture checkout policy and cover receipt identity variants
- `32998e92b7dc2c75ed886a5591d191d86b78f3c6` Fence snapshot subdirectories and add real orphan-to-cleanup acceptance
- `848e3f16057c4cdce27f4c54d331429a754e1b22` Reorder active contracts to post-land verification; tighten final cleanup authority
- `244316125c888af785a33363696861e73dc4517a` Record application custody checkpoint, ordinary evidence and remaining acceptance

Touched-file totals:  90 files changed, 12301 insertions(+), 244 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-cd1d39f7`.


+## CARD-0497

Terminal reason: Shipped: landed b2661115 to master.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-050cb713` | SUPERSEDED | 7 | 38 / 2670 / 31 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-050cb713`

Commits:
- `f9976481494687a8818d54734f96120a71e217a1` fix(session-runner): CARD-0497 launch Codex via node.exe and refuse oversize command lines
- `ea3f6c94da122b7a994b947a76400373f9ee3990` test(session-runner): CARD-0497 assert developer_instructions at the last argv slot
- `12a952eed1c0045aec9d5be72775d0b19a1887e3` test: CARD-0497 skip catalog-overflow compositions in argv matrix
- `11eadbac90ed48df3417df64481100009b08c1f3` test(agents): CARD-0497 wait for stub receipt before turn-complete in Codex canaries
- `9839ceea66b7025b4473885823f456ce297468fd` test(agents): CARD-0497 V-3 sentinel gate, actual-arg near-cap fit, PC-10 registration counter
- `3bfc7ae0fcf9c328d81de975b35aa1bc29509206` docs: CARD-0497 restate PC-10 registration counter and PC-20 double-prefix mutation
- `21e9abed31b7debfe7ab92c465e40ce4bb83f3bb` fix(session-runner): CARD-0497 keep PATH custom exe and resolve relative codex.js against cwd

Touched-file totals:  38 files changed, 2670 insertions(+), 31 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-050cb713`.


+## CARD-0408

Terminal reason: Landed on master at 3a09c6e3.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-0998ad05` | SUPERSEDED | 10 | 105 / 12175 / 464 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-0998ad05`

Commits:
- `bf5a4219a9450597506de7cf9938e6346731f89e` docs(CARD-0408): plan opt-in card-file publishing and private notes
- `3843a0bb5608141857fbbb24b586cb02015899b4` docs(CARD-0408): specify privacy verification and flag seven plan contract gaps
- `3efe2c69ab74248d9f35eb61849ff862b79d07e4` docs(CARD-0408): resolve all seven TestDesign contract gaps
- `7821f65a158465e4edebdaf65008348259616cf4` feat(CARD-0408): harden card-file writer and add private-by-default schema (S1)
- `1fa299aea2a7dd8e5bb932440be0e00a54726eec` feat(CARD-0408): add explicit private notes and publication policy APIs (S2)
- `a66dfc486b61cc12c347d5cc0bc810620a1945e3` feat(CARD-0408): reconcile revoked exports and guard target ownership (S3-S4)
- `46989073ad9cd72739d4d616b8be82a65eac75bd` feat(CARD-0408): add file-only private notes and truthful CLI publication status (S5)
- `5e0aa6fe5208cfcd7c71380cb1c637f02f0395ef` feat(CARD-0408): add explicit private-note and publication controls (S6)
- `9c10989fbd131d030861402758daa4d57ac8bb5e` test(CARD-0408): add managed-file symlink fixture requiring Windows privilege
- `67f056a82087cca8a32cb6d42c3e4e8370c5cfb2` docs(CARD-0408): document privacy contract and verification gaps (S7)

Touched-file totals:  105 files changed, 12175 insertions(+), 464 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-0998ad05`.


+## CARD-0527

Terminal reason: Landed on master at 6ab13481.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-0d48a707` | SUPERSEDED | 21 | 63 / 20310 / 45 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-151b4e13` | SUPERSEDED | 36 | 74 / 22197 / 45 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-2853f966` | SUPERSEDED | 21 | 63 / 20310 / 45 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-360e223e` | SUPERSEDED | 18 | 59 / 19718 / 44 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-6d2a2060` | SUPERSEDED | 8 | 56 / 19381 / 105 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-b23741b2` | SUPERSEDED | 29 | 64 / 20931 / 45 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-b23d0fe2` | SUPERSEDED | 9 | 42 / 8819 / 21 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-ca4eef9c` | SUPERSEDED | 33 | 68 / 21621 / 45 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-0d48a707`

Commits:
- `fb887c2a831abcd85dddcdadb52e4be2993d7318` CARD-0527 S1: persist commit-on-settle policy (no settle behaviour yet)
- `a52b8397c15b5cac78c7d57b77a5bb98fb815c78` CARD-0527 S2: GatedCommitService ignore gate, never pushes
- `0987b4bda70a544925fc4bd09b062af3e98f30e0` CARD-0527 S3-S6: settle hook, Commit child, worktree gate, docs
- `6745a133445d9c2585b07a74cf40bd830b390028` CARD-0527 fix cap-vs-refuse header and C527 test setup
- `1a3848956fc3d8898fc31b372cc4f5221972650f` CARD-0527 seed a landing row for the SourceLanding ineligible case
- `2193e8fad782a0578d84f43e28ff9d9da31be4d2` CARD-0527 ignore .antiphon dirt at settle so landed stays landed
- `7510f4c47455bd551599dd4a73e2eb6d9da02ea5` CARD-0527 refuse rename and unavailable git inspections; verification pending
- `72e925abc699dbd6924ae96f4e65333602f6d571` CARD-0527 persist completion outbox and exact recovery identity; add child chain cases, verification pending
- `3780f871a06e7065820a28adadef9a8352ee33d0` CARD-0527 fail closed on child audit inspection errors; fix JSON import, verification pending
- `5e2a87a4eabed1f37f76085905f935e84ab6455d` CARD-0527 add upstream baseline migration and complete chain fixture; ordinary verification pending
- `ab26987503045312915a02c64d209054ccead938` CARD-0527 scope immutable outbox to commit outcomes and record repair verification design
- `0eeb0b2c7b0e96e98063e768cb705c390eef5af7` CARD-0527 fix staged rename commits and use execution identity in child delivery test; Unit 2479/2484, four inherited reds
- `71164e035f811e09bbff00c0dd6375cf8f9904f5` CARD-0527 distinguish unborn history and persist LF briefs; gate 31 passed, reply 67/68, child LF regressions pending
- `5b9eb97398ddca9f86af3a5d4e940101cb7b5f3a` CARD-0527 complete policy matrix and preserve settlement notification identity; gate endpoint 46 and reply 68 passed before this slice
- `3201b7e11330d02c88f705af08c501ee185f48af` CARD-0527 isolate child dispatch fixtures and normalize finding contract; reply 69 and outbox 258 pass, Unit and endpoint correction pending
- `7d0134491d0ac1fc934d5bb7cd1f6068f9e38811` CARD-0527 make isolated child repo seeding static; R17 missing session field reproduced on pre-Code base
- `e0496abbb7604083e940a93c11ec772fbfe6359e` CARD-0527 recapture R17 fixture from real server; inherited missing session null confirmed, capture 1 passed
- `4d2a95508024253dddd647b6f9c8f71f63341da5` CARD-0527 ordinary verification complete: all 72 V/R pass; 3049/3056 latest cases pass, 6 inherited failures and 1 skip; PC1-45 pending
- `1d9d04bbadfbbc948e8edb10c0f7db3e2ae90328` CARD-0527 preserve completed commits through inspection faults and policy flips; verification pending
- `edcc056b309d10c1f0420656ec60114cbd6b4b4c` CARD-0527 use repository NotFoundException constructor; initial build caught mismatch
- `29708a88ff35a5f12b943459a7b5f02f3069501c` CARD-0527 F8/F9 ordinary verification complete: 3062 pass, six unchanged inherited failures, one skip

Touched-file totals:  63 files changed, 20310 insertions(+), 45 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-0d48a707`.

### `feat/card-task-151b4e13`

Commits:
- `fb887c2a831abcd85dddcdadb52e4be2993d7318` CARD-0527 S1: persist commit-on-settle policy (no settle behaviour yet)
- `a52b8397c15b5cac78c7d57b77a5bb98fb815c78` CARD-0527 S2: GatedCommitService ignore gate, never pushes
- `0987b4bda70a544925fc4bd09b062af3e98f30e0` CARD-0527 S3-S6: settle hook, Commit child, worktree gate, docs
- `6745a133445d9c2585b07a74cf40bd830b390028` CARD-0527 fix cap-vs-refuse header and C527 test setup
- `1a3848956fc3d8898fc31b372cc4f5221972650f` CARD-0527 seed a landing row for the SourceLanding ineligible case
- `2193e8fad782a0578d84f43e28ff9d9da31be4d2` CARD-0527 ignore .antiphon dirt at settle so landed stays landed
- `7510f4c47455bd551599dd4a73e2eb6d9da02ea5` CARD-0527 refuse rename and unavailable git inspections; verification pending
- `72e925abc699dbd6924ae96f4e65333602f6d571` CARD-0527 persist completion outbox and exact recovery identity; add child chain cases, verification pending
- `3780f871a06e7065820a28adadef9a8352ee33d0` CARD-0527 fail closed on child audit inspection errors; fix JSON import, verification pending
- `5e2a87a4eabed1f37f76085905f935e84ab6455d` CARD-0527 add upstream baseline migration and complete chain fixture; ordinary verification pending
- `ab26987503045312915a02c64d209054ccead938` CARD-0527 scope immutable outbox to commit outcomes and record repair verification design
- `0eeb0b2c7b0e96e98063e768cb705c390eef5af7` CARD-0527 fix staged rename commits and use execution identity in child delivery test; Unit 2479/2484, four inherited reds
- `71164e035f811e09bbff00c0dd6375cf8f9904f5` CARD-0527 distinguish unborn history and persist LF briefs; gate 31 passed, reply 67/68, child LF regressions pending
- `5b9eb97398ddca9f86af3a5d4e940101cb7b5f3a` CARD-0527 complete policy matrix and preserve settlement notification identity; gate endpoint 46 and reply 68 passed before this slice
- `3201b7e11330d02c88f705af08c501ee185f48af` CARD-0527 isolate child dispatch fixtures and normalize finding contract; reply 69 and outbox 258 pass, Unit and endpoint correction pending
- `7d0134491d0ac1fc934d5bb7cd1f6068f9e38811` CARD-0527 make isolated child repo seeding static; R17 missing session field reproduced on pre-Code base
- `e0496abbb7604083e940a93c11ec772fbfe6359e` CARD-0527 recapture R17 fixture from real server; inherited missing session null confirmed, capture 1 passed
- `4d2a95508024253dddd647b6f9c8f71f63341da5` CARD-0527 ordinary verification complete: all 72 V/R pass; 3049/3056 latest cases pass, 6 inherited failures and 1 skip; PC1-45 pending
- `1d9d04bbadfbbc948e8edb10c0f7db3e2ae90328` CARD-0527 preserve completed commits through inspection faults and policy flips; verification pending
- `edcc056b309d10c1f0420656ec60114cbd6b4b4c` CARD-0527 use repository NotFoundException constructor; initial build caught mismatch
- `29708a88ff35a5f12b943459a7b5f02f3069501c` CARD-0527 F8/F9 ordinary verification complete: 3062 pass, six unchanged inherited failures, one skip
- `bd2ca07e73d6db556a050c76bd514eeda86c88c3` fix(CARD-0527): keep committed settlement pending across retry inspection failures; verification pending
- `3b2646d2be1242307c9bcfc51d1c9a2a8679515a` fix(CARD-0527): reject absent and malformed endpoint paths before mutation; verification pending
- `e8f05ccdf7bdd5391852ac00aae37a3e6d7fd17f` fix(CARD-0527): honor ignore negations in gate and child audit; verification pending
- `a7e945cc2aeba343e6a2d8e3f154a6b492ed36d4` docs(CARD-0527): define F10-F12 ordinary regressions and pending mutation controls
- `b92e111a406f85232aa6603ff61684d9bfa80378` test(CARD-0527): use fresh retry contexts after failed save; 78 reply cases passed, two fixture failures pending recheck
- `2ea8da34a45c161dab13211ec7c4d07e4d5c372b` fix(CARD-0527): preserve non-Git settlement while keeping failed inspections pending; three delivery regressions under repair
- `1ee247281fc4584240f7fec78c6b81e25920cabd` docs(CARD-0527): record explicit non-Git regression coverage and PC-61 through PC-63
- `59e5499b721be381fb6b49d3f2fe8e0e87b7293d` docs(CARD-0527): ordinary V-1..67 and R-1..17 passed; six inherited failures unchanged; 63 PCs pending
- `936636066914bbf48f8a26d5d50cde54d6b234bf` Merge commit '59e5499b721be381fb6b49d3f2fe8e0e87b7293d' into feat/card-task-ca4eef9c
- `63b0926f5b710fecad6305636dbb2225257bd4f3` fix(CARD-0527): persist settlement recovery before Git; ordinary verification pending
- `5cec88266911fd83068e5966d0b7a09ae6cdccc2` fix(CARD-0527): enforce literal commit filenames and receipt footprint; verification pending
- `9f2cd5f1680b43aadd777781efa673ba28d0bb64` docs(CARD-0527): record 91 passing V/R IDs, six inherited failures and pending PCs
- `29321fd38877e49bbbb186dc95a212268655703c` fix(CARD-0527): recover durable attempts across refs and reflogs; verification pending
- `053e1f85c9032a84839640c36c1e37e55f7daf92` fix(CARD-0527): manifest actual staged selection after reverts; ordinary verification pending
- `1ef62967820880e0e68e3e77519f5b699f91c91f` docs(CARD-0527): F15/F16 ordinary complete, 96 V/R pass; six inherited reds, 80 PCs pending

Touched-file totals:  74 files changed, 22197 insertions(+), 45 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-151b4e13`.

### `feat/card-task-2853f966`

Commits:
- `fb887c2a831abcd85dddcdadb52e4be2993d7318` CARD-0527 S1: persist commit-on-settle policy (no settle behaviour yet)
- `a52b8397c15b5cac78c7d57b77a5bb98fb815c78` CARD-0527 S2: GatedCommitService ignore gate, never pushes
- `0987b4bda70a544925fc4bd09b062af3e98f30e0` CARD-0527 S3-S6: settle hook, Commit child, worktree gate, docs
- `6745a133445d9c2585b07a74cf40bd830b390028` CARD-0527 fix cap-vs-refuse header and C527 test setup
- `1a3848956fc3d8898fc31b372cc4f5221972650f` CARD-0527 seed a landing row for the SourceLanding ineligible case
- `2193e8fad782a0578d84f43e28ff9d9da31be4d2` CARD-0527 ignore .antiphon dirt at settle so landed stays landed
- `7510f4c47455bd551599dd4a73e2eb6d9da02ea5` CARD-0527 refuse rename and unavailable git inspections; verification pending
- `72e925abc699dbd6924ae96f4e65333602f6d571` CARD-0527 persist completion outbox and exact recovery identity; add child chain cases, verification pending
- `3780f871a06e7065820a28adadef9a8352ee33d0` CARD-0527 fail closed on child audit inspection errors; fix JSON import, verification pending
- `5e2a87a4eabed1f37f76085905f935e84ab6455d` CARD-0527 add upstream baseline migration and complete chain fixture; ordinary verification pending
- `ab26987503045312915a02c64d209054ccead938` CARD-0527 scope immutable outbox to commit outcomes and record repair verification design
- `0eeb0b2c7b0e96e98063e768cb705c390eef5af7` CARD-0527 fix staged rename commits and use execution identity in child delivery test; Unit 2479/2484, four inherited reds
- `71164e035f811e09bbff00c0dd6375cf8f9904f5` CARD-0527 distinguish unborn history and persist LF briefs; gate 31 passed, reply 67/68, child LF regressions pending
- `5b9eb97398ddca9f86af3a5d4e940101cb7b5f3a` CARD-0527 complete policy matrix and preserve settlement notification identity; gate endpoint 46 and reply 68 passed before this slice
- `3201b7e11330d02c88f705af08c501ee185f48af` CARD-0527 isolate child dispatch fixtures and normalize finding contract; reply 69 and outbox 258 pass, Unit and endpoint correction pending
- `7d0134491d0ac1fc934d5bb7cd1f6068f9e38811` CARD-0527 make isolated child repo seeding static; R17 missing session field reproduced on pre-Code base
- `e0496abbb7604083e940a93c11ec772fbfe6359e` CARD-0527 recapture R17 fixture from real server; inherited missing session null confirmed, capture 1 passed
- `4d2a95508024253dddd647b6f9c8f71f63341da5` CARD-0527 ordinary verification complete: all 72 V/R pass; 3049/3056 latest cases pass, 6 inherited failures and 1 skip; PC1-45 pending
- `1d9d04bbadfbbc948e8edb10c0f7db3e2ae90328` CARD-0527 preserve completed commits through inspection faults and policy flips; verification pending
- `edcc056b309d10c1f0420656ec60114cbd6b4b4c` CARD-0527 use repository NotFoundException constructor; initial build caught mismatch
- `29708a88ff35a5f12b943459a7b5f02f3069501c` CARD-0527 F8/F9 ordinary verification complete: 3062 pass, six unchanged inherited failures, one skip

Touched-file totals:  63 files changed, 20310 insertions(+), 45 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-2853f966`.

### `feat/card-task-360e223e`

Commits:
- `fb887c2a831abcd85dddcdadb52e4be2993d7318` CARD-0527 S1: persist commit-on-settle policy (no settle behaviour yet)
- `a52b8397c15b5cac78c7d57b77a5bb98fb815c78` CARD-0527 S2: GatedCommitService ignore gate, never pushes
- `0987b4bda70a544925fc4bd09b062af3e98f30e0` CARD-0527 S3-S6: settle hook, Commit child, worktree gate, docs
- `6745a133445d9c2585b07a74cf40bd830b390028` CARD-0527 fix cap-vs-refuse header and C527 test setup
- `1a3848956fc3d8898fc31b372cc4f5221972650f` CARD-0527 seed a landing row for the SourceLanding ineligible case
- `2193e8fad782a0578d84f43e28ff9d9da31be4d2` CARD-0527 ignore .antiphon dirt at settle so landed stays landed
- `7510f4c47455bd551599dd4a73e2eb6d9da02ea5` CARD-0527 refuse rename and unavailable git inspections; verification pending
- `72e925abc699dbd6924ae96f4e65333602f6d571` CARD-0527 persist completion outbox and exact recovery identity; add child chain cases, verification pending
- `3780f871a06e7065820a28adadef9a8352ee33d0` CARD-0527 fail closed on child audit inspection errors; fix JSON import, verification pending
- `5e2a87a4eabed1f37f76085905f935e84ab6455d` CARD-0527 add upstream baseline migration and complete chain fixture; ordinary verification pending
- `ab26987503045312915a02c64d209054ccead938` CARD-0527 scope immutable outbox to commit outcomes and record repair verification design
- `0eeb0b2c7b0e96e98063e768cb705c390eef5af7` CARD-0527 fix staged rename commits and use execution identity in child delivery test; Unit 2479/2484, four inherited reds
- `71164e035f811e09bbff00c0dd6375cf8f9904f5` CARD-0527 distinguish unborn history and persist LF briefs; gate 31 passed, reply 67/68, child LF regressions pending
- `5b9eb97398ddca9f86af3a5d4e940101cb7b5f3a` CARD-0527 complete policy matrix and preserve settlement notification identity; gate endpoint 46 and reply 68 passed before this slice
- `3201b7e11330d02c88f705af08c501ee185f48af` CARD-0527 isolate child dispatch fixtures and normalize finding contract; reply 69 and outbox 258 pass, Unit and endpoint correction pending
- `7d0134491d0ac1fc934d5bb7cd1f6068f9e38811` CARD-0527 make isolated child repo seeding static; R17 missing session field reproduced on pre-Code base
- `e0496abbb7604083e940a93c11ec772fbfe6359e` CARD-0527 recapture R17 fixture from real server; inherited missing session null confirmed, capture 1 passed
- `4d2a95508024253dddd647b6f9c8f71f63341da5` CARD-0527 ordinary verification complete: all 72 V/R pass; 3049/3056 latest cases pass, 6 inherited failures and 1 skip; PC1-45 pending

Touched-file totals:  59 files changed, 19718 insertions(+), 44 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-360e223e`.

### `feat/card-task-6d2a2060`

Commits:
- `fb887c2a831abcd85dddcdadb52e4be2993d7318` CARD-0527 S1: persist commit-on-settle policy (no settle behaviour yet)
- `a52b8397c15b5cac78c7d57b77a5bb98fb815c78` CARD-0527 S2: GatedCommitService ignore gate, never pushes
- `0987b4bda70a544925fc4bd09b062af3e98f30e0` CARD-0527 S3-S6: settle hook, Commit child, worktree gate, docs
- `6745a133445d9c2585b07a74cf40bd830b390028` CARD-0527 fix cap-vs-refuse header and C527 test setup
- `1a3848956fc3d8898fc31b372cc4f5221972650f` CARD-0527 seed a landing row for the SourceLanding ineligible case
- `2193e8fad782a0578d84f43e28ff9d9da31be4d2` CARD-0527 ignore .antiphon dirt at settle so landed stays landed
- `83f4051f76f2ed362af85fb4064988bffc2f4307` fix(CARD-0527): repair F1-F7 review defects; ordinary verification pending
- `a4b35bf3aef469318e2d8b77c04c17a964a76ec0` test(CARD-0527): accept staged git-mv index on F1 and LF-normalized child briefs

Touched-file totals:  56 files changed, 19381 insertions(+), 105 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-6d2a2060`.

### `feat/card-task-b23741b2`

Commits:
- `fb887c2a831abcd85dddcdadb52e4be2993d7318` CARD-0527 S1: persist commit-on-settle policy (no settle behaviour yet)
- `a52b8397c15b5cac78c7d57b77a5bb98fb815c78` CARD-0527 S2: GatedCommitService ignore gate, never pushes
- `0987b4bda70a544925fc4bd09b062af3e98f30e0` CARD-0527 S3-S6: settle hook, Commit child, worktree gate, docs
- `6745a133445d9c2585b07a74cf40bd830b390028` CARD-0527 fix cap-vs-refuse header and C527 test setup
- `1a3848956fc3d8898fc31b372cc4f5221972650f` CARD-0527 seed a landing row for the SourceLanding ineligible case
- `2193e8fad782a0578d84f43e28ff9d9da31be4d2` CARD-0527 ignore .antiphon dirt at settle so landed stays landed
- `7510f4c47455bd551599dd4a73e2eb6d9da02ea5` CARD-0527 refuse rename and unavailable git inspections; verification pending
- `72e925abc699dbd6924ae96f4e65333602f6d571` CARD-0527 persist completion outbox and exact recovery identity; add child chain cases, verification pending
- `3780f871a06e7065820a28adadef9a8352ee33d0` CARD-0527 fail closed on child audit inspection errors; fix JSON import, verification pending
- `5e2a87a4eabed1f37f76085905f935e84ab6455d` CARD-0527 add upstream baseline migration and complete chain fixture; ordinary verification pending
- `ab26987503045312915a02c64d209054ccead938` CARD-0527 scope immutable outbox to commit outcomes and record repair verification design
- `0eeb0b2c7b0e96e98063e768cb705c390eef5af7` CARD-0527 fix staged rename commits and use execution identity in child delivery test; Unit 2479/2484, four inherited reds
- `71164e035f811e09bbff00c0dd6375cf8f9904f5` CARD-0527 distinguish unborn history and persist LF briefs; gate 31 passed, reply 67/68, child LF regressions pending
- `5b9eb97398ddca9f86af3a5d4e940101cb7b5f3a` CARD-0527 complete policy matrix and preserve settlement notification identity; gate endpoint 46 and reply 68 passed before this slice
- `3201b7e11330d02c88f705af08c501ee185f48af` CARD-0527 isolate child dispatch fixtures and normalize finding contract; reply 69 and outbox 258 pass, Unit and endpoint correction pending
- `7d0134491d0ac1fc934d5bb7cd1f6068f9e38811` CARD-0527 make isolated child repo seeding static; R17 missing session field reproduced on pre-Code base
- `e0496abbb7604083e940a93c11ec772fbfe6359e` CARD-0527 recapture R17 fixture from real server; inherited missing session null confirmed, capture 1 passed
- `4d2a95508024253dddd647b6f9c8f71f63341da5` CARD-0527 ordinary verification complete: all 72 V/R pass; 3049/3056 latest cases pass, 6 inherited failures and 1 skip; PC1-45 pending
- `1d9d04bbadfbbc948e8edb10c0f7db3e2ae90328` CARD-0527 preserve completed commits through inspection faults and policy flips; verification pending
- `edcc056b309d10c1f0420656ec60114cbd6b4b4c` CARD-0527 use repository NotFoundException constructor; initial build caught mismatch
- `29708a88ff35a5f12b943459a7b5f02f3069501c` CARD-0527 F8/F9 ordinary verification complete: 3062 pass, six unchanged inherited failures, one skip
- `bd2ca07e73d6db556a050c76bd514eeda86c88c3` fix(CARD-0527): keep committed settlement pending across retry inspection failures; verification pending
- `3b2646d2be1242307c9bcfc51d1c9a2a8679515a` fix(CARD-0527): reject absent and malformed endpoint paths before mutation; verification pending
- `e8f05ccdf7bdd5391852ac00aae37a3e6d7fd17f` fix(CARD-0527): honor ignore negations in gate and child audit; verification pending
- `a7e945cc2aeba343e6a2d8e3f154a6b492ed36d4` docs(CARD-0527): define F10-F12 ordinary regressions and pending mutation controls
- `b92e111a406f85232aa6603ff61684d9bfa80378` test(CARD-0527): use fresh retry contexts after failed save; 78 reply cases passed, two fixture failures pending recheck
- `2ea8da34a45c161dab13211ec7c4d07e4d5c372b` fix(CARD-0527): preserve non-Git settlement while keeping failed inspections pending; three delivery regressions under repair
- `1ee247281fc4584240f7fec78c6b81e25920cabd` docs(CARD-0527): record explicit non-Git regression coverage and PC-61 through PC-63
- `59e5499b721be381fb6b49d3f2fe8e0e87b7293d` docs(CARD-0527): ordinary V-1..67 and R-1..17 passed; six inherited failures unchanged; 63 PCs pending

Touched-file totals:  64 files changed, 20931 insertions(+), 45 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-b23741b2`.

### `feat/card-task-b23d0fe2`

Commits:
- `8f7a41f51e685a28be7263ef6544862b3c51e0bd` docs(CARD-0527): test-design commit-on-settle: 55 V, 17 R, 31 guards each with a positive control
- `009db40bc7ed1f629896a7d7415383ff922982ea` Merge remote-tracking branch 'origin/feat/card-task-2139f2f3' into feat/card-task-b23d0fe2
- `3c5c5b72f6740c78779d35c8e6119d697da11ea0` CARD-0527 S2 checkpoint: gated local commit primitive and 16 git cases; verification pending
- `5e4ead377f43d7b2b65d720525af492c13bf52e6` CARD-0527 S2: 15 git cases pass; add held-lease success coverage for next build
- `794114b6ed4a43bf96783463fe411a0b7da5eb38` CARD-0527 S1 checkpoint: policy storage and API/script/UI surfaces; migration and verification pending
- `7de58da25402e005f5ac3882bdc1a1916aac6336` CARD-0527 S1: CLI-generated additive migration and policy/script/brief checks; verification pending
- `f2007eccd4f89768ea0fc7127301a767c8fe9c3b` CARD-0527 fix legacy insert fixture: 60/61 checks and 6 client tests passed; missing Attempt was test setup
- `4ca4162a0abef3c9329d00ab631860f2a2a6d9e0` CARD-0527 S3-S5 checkpoint: settle hook, routed Commit child, endpoint audit and gated worktree sweep; tests pending
- `b05c5f0a46af2c279f12f56d398fdf20bcc14b25` CARD-0527 add settle receipt/recovery/audit and worktree refusal tests; ordinary verification pending

Touched-file totals:  42 files changed, 8819 insertions(+), 21 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-b23d0fe2`.

### `feat/card-task-ca4eef9c`

Commits:
- `fb887c2a831abcd85dddcdadb52e4be2993d7318` CARD-0527 S1: persist commit-on-settle policy (no settle behaviour yet)
- `a52b8397c15b5cac78c7d57b77a5bb98fb815c78` CARD-0527 S2: GatedCommitService ignore gate, never pushes
- `0987b4bda70a544925fc4bd09b062af3e98f30e0` CARD-0527 S3-S6: settle hook, Commit child, worktree gate, docs
- `6745a133445d9c2585b07a74cf40bd830b390028` CARD-0527 fix cap-vs-refuse header and C527 test setup
- `1a3848956fc3d8898fc31b372cc4f5221972650f` CARD-0527 seed a landing row for the SourceLanding ineligible case
- `2193e8fad782a0578d84f43e28ff9d9da31be4d2` CARD-0527 ignore .antiphon dirt at settle so landed stays landed
- `7510f4c47455bd551599dd4a73e2eb6d9da02ea5` CARD-0527 refuse rename and unavailable git inspections; verification pending
- `72e925abc699dbd6924ae96f4e65333602f6d571` CARD-0527 persist completion outbox and exact recovery identity; add child chain cases, verification pending
- `3780f871a06e7065820a28adadef9a8352ee33d0` CARD-0527 fail closed on child audit inspection errors; fix JSON import, verification pending
- `5e2a87a4eabed1f37f76085905f935e84ab6455d` CARD-0527 add upstream baseline migration and complete chain fixture; ordinary verification pending
- `ab26987503045312915a02c64d209054ccead938` CARD-0527 scope immutable outbox to commit outcomes and record repair verification design
- `0eeb0b2c7b0e96e98063e768cb705c390eef5af7` CARD-0527 fix staged rename commits and use execution identity in child delivery test; Unit 2479/2484, four inherited reds
- `71164e035f811e09bbff00c0dd6375cf8f9904f5` CARD-0527 distinguish unborn history and persist LF briefs; gate 31 passed, reply 67/68, child LF regressions pending
- `5b9eb97398ddca9f86af3a5d4e940101cb7b5f3a` CARD-0527 complete policy matrix and preserve settlement notification identity; gate endpoint 46 and reply 68 passed before this slice
- `3201b7e11330d02c88f705af08c501ee185f48af` CARD-0527 isolate child dispatch fixtures and normalize finding contract; reply 69 and outbox 258 pass, Unit and endpoint correction pending
- `7d0134491d0ac1fc934d5bb7cd1f6068f9e38811` CARD-0527 make isolated child repo seeding static; R17 missing session field reproduced on pre-Code base
- `e0496abbb7604083e940a93c11ec772fbfe6359e` CARD-0527 recapture R17 fixture from real server; inherited missing session null confirmed, capture 1 passed
- `4d2a95508024253dddd647b6f9c8f71f63341da5` CARD-0527 ordinary verification complete: all 72 V/R pass; 3049/3056 latest cases pass, 6 inherited failures and 1 skip; PC1-45 pending
- `1d9d04bbadfbbc948e8edb10c0f7db3e2ae90328` CARD-0527 preserve completed commits through inspection faults and policy flips; verification pending
- `edcc056b309d10c1f0420656ec60114cbd6b4b4c` CARD-0527 use repository NotFoundException constructor; initial build caught mismatch
- `29708a88ff35a5f12b943459a7b5f02f3069501c` CARD-0527 F8/F9 ordinary verification complete: 3062 pass, six unchanged inherited failures, one skip
- `bd2ca07e73d6db556a050c76bd514eeda86c88c3` fix(CARD-0527): keep committed settlement pending across retry inspection failures; verification pending
- `3b2646d2be1242307c9bcfc51d1c9a2a8679515a` fix(CARD-0527): reject absent and malformed endpoint paths before mutation; verification pending
- `e8f05ccdf7bdd5391852ac00aae37a3e6d7fd17f` fix(CARD-0527): honor ignore negations in gate and child audit; verification pending
- `a7e945cc2aeba343e6a2d8e3f154a6b492ed36d4` docs(CARD-0527): define F10-F12 ordinary regressions and pending mutation controls
- `b92e111a406f85232aa6603ff61684d9bfa80378` test(CARD-0527): use fresh retry contexts after failed save; 78 reply cases passed, two fixture failures pending recheck
- `2ea8da34a45c161dab13211ec7c4d07e4d5c372b` fix(CARD-0527): preserve non-Git settlement while keeping failed inspections pending; three delivery regressions under repair
- `1ee247281fc4584240f7fec78c6b81e25920cabd` docs(CARD-0527): record explicit non-Git regression coverage and PC-61 through PC-63
- `59e5499b721be381fb6b49d3f2fe8e0e87b7293d` docs(CARD-0527): ordinary V-1..67 and R-1..17 passed; six inherited failures unchanged; 63 PCs pending
- `936636066914bbf48f8a26d5d50cde54d6b234bf` Merge commit '59e5499b721be381fb6b49d3f2fe8e0e87b7293d' into feat/card-task-ca4eef9c
- `63b0926f5b710fecad6305636dbb2225257bd4f3` fix(CARD-0527): persist settlement recovery before Git; ordinary verification pending
- `5cec88266911fd83068e5966d0b7a09ae6cdccc2` fix(CARD-0527): enforce literal commit filenames and receipt footprint; verification pending
- `9f2cd5f1680b43aadd777781efa673ba28d0bb64` docs(CARD-0527): record 91 passing V/R IDs, six inherited failures and pending PCs

Touched-file totals:  68 files changed, 21621 insertions(+), 45 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-ca4eef9c`.


+## CARD-0575

Terminal reason: Superseded, not shipped; v0 host-exec-wrapper pivoted away.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-0d678e51` | ABANDONED-OK | 3 | 1 / 851 /  | Explicit terminalReason supersedes this discarded v0 design/evidence approach. |
| `feat/card-task-318daeb7` | ABANDONED-OK | 3 | 1 / 851 /  | Explicit terminalReason supersedes this discarded v0 design/evidence approach. |
| `feat/card-task-4fe68cac` | ABANDONED-OK | 1 | 33 / 1523 /  | Explicit terminalReason supersedes this discarded v0 design/evidence approach. |
| `feat/card-task-5e3ba4d4` | ABANDONED-OK | 5 | 1 / 1138 /  | Explicit terminalReason supersedes this discarded v0 design/evidence approach. |
| `feat/card-task-6b037196` | ABANDONED-OK | 5 | 1 / 1138 /  | Explicit terminalReason supersedes this discarded v0 design/evidence approach. |
| `feat/card-task-ba318e38` | ABANDONED-OK | 1 | 1 / 239 /  | Explicit terminalReason supersedes this discarded v0 design/evidence approach. |
| `feat/card-task-f7cf0fe7` | ABANDONED-OK | 6 | 1 / 1501 /  | Explicit terminalReason supersedes this discarded v0 design/evidence approach. |

### `feat/card-task-0d678e51`

Commits:
- `6f16fb61280b7e94b224a399943c295ac5f0b0bb` docs(CARD-0575): plan local Docker Grok worker; test design pending
- `c391749673d7b08bc37d5a5c7d52760543707e77` docs(card-0575): append verification design; return refinement durability seam to Plan
- `17c414fc84f2cac8511124d4aece748f7dc09477` docs(card-0575): specify durable refinement replay and immutable spills

Touched-file totals:  1 file changed, 851 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-0d678e51`.

### `feat/card-task-318daeb7`

Commits:
- `6f16fb61280b7e94b224a399943c295ac5f0b0bb` docs(CARD-0575): plan local Docker Grok worker; test design pending
- `c391749673d7b08bc37d5a5c7d52760543707e77` docs(card-0575): append verification design; return refinement durability seam to Plan
- `17c414fc84f2cac8511124d4aece748f7dc09477` docs(card-0575): specify durable refinement replay and immutable spills

Touched-file totals:  1 file changed, 851 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-318daeb7`.

### `feat/card-task-4fe68cac`

Commits:
- `56d5ad47d715ba6e5bfa305163b931362202d6d6` docs(CARD-0575): measure Docker Grok worker phone-home, auth, and host-socket

Touched-file totals:  33 files changed, 1523 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-4fe68cac`.

### `feat/card-task-5e3ba4d4`

Commits:
- `6f16fb61280b7e94b224a399943c295ac5f0b0bb` docs(CARD-0575): plan local Docker Grok worker; test design pending
- `c391749673d7b08bc37d5a5c7d52760543707e77` docs(card-0575): append verification design; return refinement durability seam to Plan
- `17c414fc84f2cac8511124d4aece748f7dc09477` docs(card-0575): specify durable refinement replay and immutable spills
- `3cfe11ae2c20943360e02f89b690c34ca11ef472` docs(card-0575): reject VD-1 Code handoff pending receipt evidence
- `e25550e78a2ed812341c0efc7099fddb02144b18` docs(card-0575): specify durable transcript generation provenance

Touched-file totals:  1 file changed, 1138 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-5e3ba4d4`.

### `feat/card-task-6b037196`

Commits:
- `6f16fb61280b7e94b224a399943c295ac5f0b0bb` docs(CARD-0575): plan local Docker Grok worker; test design pending
- `c391749673d7b08bc37d5a5c7d52760543707e77` docs(card-0575): append verification design; return refinement durability seam to Plan
- `17c414fc84f2cac8511124d4aece748f7dc09477` docs(card-0575): specify durable refinement replay and immutable spills
- `3cfe11ae2c20943360e02f89b690c34ca11ef472` docs(card-0575): reject VD-1 Code handoff pending receipt evidence
- `e25550e78a2ed812341c0efc7099fddb02144b18` docs(card-0575): specify durable transcript generation provenance

Touched-file totals:  1 file changed, 1138 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-6b037196`.

### `feat/card-task-ba318e38`

Commits:
- `6f16fb61280b7e94b224a399943c295ac5f0b0bb` docs(CARD-0575): plan local Docker Grok worker; test design pending

Touched-file totals:  1 file changed, 239 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-ba318e38`.

### `feat/card-task-f7cf0fe7`

Commits:
- `6f16fb61280b7e94b224a399943c295ac5f0b0bb` docs(CARD-0575): plan local Docker Grok worker; test design pending
- `c391749673d7b08bc37d5a5c7d52760543707e77` docs(card-0575): append verification design; return refinement durability seam to Plan
- `17c414fc84f2cac8511124d4aece748f7dc09477` docs(card-0575): specify durable refinement replay and immutable spills
- `3cfe11ae2c20943360e02f89b690c34ca11ef472` docs(card-0575): reject VD-1 Code handoff pending receipt evidence
- `e25550e78a2ed812341c0efc7099fddb02144b18` docs(card-0575): specify durable transcript generation provenance
- `7e07c7c302f863e340923c586bd65cfc7b64dd44` docs(card-0575): audit 265 provenance controls and reconcile verification floors

Touched-file totals:  1 file changed, 1501 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-f7cf0fe7`.


+## CARD-0585

Terminal reason: Shipped; landed 8c2be396.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-0eb35cd2` | SUPERSEDED | 1 | 1 / 149 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-0eb35cd2`

Commits:
- `5378b6340711fe7b7658d67ba0e245ae4a05ec6e` docs(CARD-0585): investigate batched edit/test Code-stage cost

Touched-file totals:  1 file changed, 149 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-0eb35cd2`.


+## CARD-0146

Terminal reason: S1 shipped and live (dca5b06e); remaining work split out.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-157b7f62` | SUPERSEDED | 1 | 12 / 106 / 4 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-157b7f62`

Commits:
- `ab19c051eb7c99b3458d662e9e564d9a5c652449` feat(roles): CARD-0146 S1 Investigate and TestDesign vocabulary

Touched-file totals:  12 files changed, 106 insertions(+), 4 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-157b7f62`.


+## CARD-0210

Terminal reason: Fixed and merged to master (d217306).

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-17c504bb` | SUPERSEDED | 1 | 1 / 268 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-17c504bb`

Commits:
- `8552dcbb80e8056b134690d1431f9f3d61def132` docs(plan): CARD-0210 root cause and fix design for per-task and per-agent boards

Touched-file totals:  1 file changed, 268 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-17c504bb`.


+## CARD-0005

Terminal reason: Allocator/archive implementation complete; docs close-out.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-1857c5d9` | SUPERSEDED | 1 | 3 / 362 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-1857c5d9`

Commits:
- `a0cd6ed9e59cd8cf80596c037788ac002acb8cd6` docs(specs): CARD-0005 identifier plan + CARD-0019 amendment 1

Touched-file totals:  3 files changed, 362 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-1857c5d9`.


+## CARD-0415

Terminal reason: Landed at d4f64f19.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-1f1c67b6` | SUPERSEDED | 10 | 14 / 6070 / 27 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-1f1c67b6`

Commits:
- `57dc47a91180b3db83d9b951491ae26ca75a960a` test(CARD-0415): checkpoint identity reproduction and scope corrections; verification pending
- `e5b4b6750ca9216a3373fbaf06de75c276987a59` fix(CARD-0415): snapshot specialist kind and tier; baseline identity regression reproduced
- `d7df09b5d0179ec3c1e9fed818c84d8277246898` feat(CARD-0415): checkpoint specialist model and generation snapshots; migration and controls pending
- `9e5ed3e3581562580aabacea9b5de04775cab681` test(CARD-0415): migrate identity snapshots and cover dispatch drift; verification pending
- `ee18a9579028e633cebccea257ae7b907b651ca0` test(CARD-0415): fix tuple assertion compilation and add public pin control
- `ff997d539604e7c99fce0fae48b7b604064b3547` test(CARD-0415): identity matrix passes 10 cases; add scoped mutation entrypoints
- `2b9abf16651eb312f06977f61704982cd4cc4e0b` docs(CARD-0415): record verified identity slice; fallback remains pending
- `753af1c0217d5caf51d52cbcd3c4f3908a7d0a95` docs(CARD-0415): checkpoint identity control evidence; S2-S6 remain unimplemented
- `dd9f800ffa23e15c348560733ca187f4f70b7d69` docs(CARD-0415): record 90 passing regressions and remaining V/R/PC work
- `23502557ca3c797768c8e12f251667e4b4c44c8e` docs(CARD-0415): record policy-blocked build-output cleanup residue

Touched-file totals:  14 files changed, 6070 insertions(+), 27 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-1f1c67b6`.


+## CARD-0208

Terminal reason: Fixed and live (1d304b7f).

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-40597978` | SUPERSEDED | 1 | 1 / 373 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-40597978`

Commits:
- `a567c18e9d1830136d04a5e77aa45b6735f4bc18` CARD-0208: plan — refusal-clock flake is a fixed 1.1s scheduling allowance under thread-pool starvation

Touched-file totals:  1 file changed, 373 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-40597978`.


+## CARD-0499

Terminal reason: Fixed and landed.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-5a8dada2` | SUPERSEDED | 13 | 50 / 11110 / 71 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-ea257e64` | SUPERSEDED | 14 | 51 / 11121 / 76 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-5a8dada2`

Commits:
- `0b158af2f84320830b60215c33aacf19d0b415ce` feat(delegation): CARD-0499 attribute completion to a registered repair source
- `00864e14e570c63e12ecba9cb7cfa17da0a99665` fix(schema): CARD-0499 generate EF migration so the snapshot matches the model
- `c2c16ec334281248fa54c4b852d5c0c739fc2424` test(CARD-0499): mark Slow dispatch/settlement classes and fix Unit policy cases
- `37b9d2b6797221ed1aa800d07a1bf8df593b10d0` fix(delegation): CARD-0499 nested-lease pin, refusal order and fixture git
- `f20e4f0ff3bb2a00f3e5c68adea5ca279fd3738b` test(CARD-0499): remaining V/R settlement, git, land, script and delivery cases
- `738335ea4a968d22760eef6bd6b2f9f85b24febd` test(CARD-0499): register workspace git through the worktree graph helper
- `0e62a84c498e8019088511a67dc8a0fabd207a07` test(CARD-0499): inline repair briefs and detach owner tree for identity refusals
- `6f1a53d2b87ae6b70d4f6bf11efc8d2835fbef11` fix(delegation): CARD-0499 warn foreign claims and clone the pushed branch
- `afe8ee5d1055a7f263b5efa618ea91723d42bab2` test(CARD-0499): stop fabricating UserPrompt receipt evidence
- `ef5c10b7b60fc6fdc8e67d71b1563b1fafba6113` test(CARD-0499): mark dispatched recipient Running before receipt flush
- `0a85aa0fe1e62c34dea99ad1fda97ffc68b2b9bd` test(CARD-0499): deliver repair briefs inline so UserPrompt is the queued body
- `b335e05921a4325f17f7f4c67092092becb29bfe` test(CARD-0499): raise the single-write ceiling so V-39 types the full brief
- `b0cbcaeaf329ee4ed2ee6eb428d3cbaa5029c5c8` test(CARD-0499): compare receipt bodies after line-ending normalize

Touched-file totals:  50 files changed, 11110 insertions(+), 71 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-5a8dada2`.

### `feat/card-task-ea257e64`

Commits:
- `0b158af2f84320830b60215c33aacf19d0b415ce` feat(delegation): CARD-0499 attribute completion to a registered repair source
- `00864e14e570c63e12ecba9cb7cfa17da0a99665` fix(schema): CARD-0499 generate EF migration so the snapshot matches the model
- `c2c16ec334281248fa54c4b852d5c0c739fc2424` test(CARD-0499): mark Slow dispatch/settlement classes and fix Unit policy cases
- `37b9d2b6797221ed1aa800d07a1bf8df593b10d0` fix(delegation): CARD-0499 nested-lease pin, refusal order and fixture git
- `f20e4f0ff3bb2a00f3e5c68adea5ca279fd3738b` test(CARD-0499): remaining V/R settlement, git, land, script and delivery cases
- `738335ea4a968d22760eef6bd6b2f9f85b24febd` test(CARD-0499): register workspace git through the worktree graph helper
- `0e62a84c498e8019088511a67dc8a0fabd207a07` test(CARD-0499): inline repair briefs and detach owner tree for identity refusals
- `6f1a53d2b87ae6b70d4f6bf11efc8d2835fbef11` fix(delegation): CARD-0499 warn foreign claims and clone the pushed branch
- `afe8ee5d1055a7f263b5efa618ea91723d42bab2` test(CARD-0499): stop fabricating UserPrompt receipt evidence
- `ef5c10b7b60fc6fdc8e67d71b1563b1fafba6113` test(CARD-0499): mark dispatched recipient Running before receipt flush
- `0a85aa0fe1e62c34dea99ad1fda97ffc68b2b9bd` test(CARD-0499): deliver repair briefs inline so UserPrompt is the queued body
- `b335e05921a4325f17f7f4c67092092becb29bfe` test(CARD-0499): raise the single-write ceiling so V-39 types the full brief
- `b0cbcaeaf329ee4ed2ee6eb428d3cbaa5029c5c8` test(CARD-0499): compare receipt bodies after line-ending normalize
- `8e8f3e6b2195cb72e341b381105768d4deca97c1` test(CARD-0499): make V-06 discriminate the repair-owner landing hold

Touched-file totals:  51 files changed, 11121 insertions(+), 76 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-ea257e64`.


+## CARD-0476

Terminal reason: Shipped: landed 0b0f62e0 to master.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-6630ad4e` | SUPERSEDED | 6 | 15 / 2734 / 220 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-6630ad4e`

Commits:
- `e1f7f566a015d7f20d5bcf1f74ed9ecadfcee5ae` feat(tests): CARD-0476 lazy DB lifecycle and restart preflight cache
- `0c9eca710caf74b98a3b6aa81e5807b86d44fa16` fix(tests): CARD-0476 stabilize lifecycle waiters and resolver PATH rows
- `b36227bf38e3fb863305e7b25eb253fdb2f78cfb` fix(tests): CARD-0476 probe recursion and worker failure evidence
- `513265929f35d10713d66cab116d9ee28a50bafc` fix(tests): CARD-0476 hung-child observation bound covers kill overhead
- `afdabd320f17e6c9f0eacecc03456f57a8b34a65` docs(test): CARD-0476 record lazy DB startup and preflight cache
- `fc404dc760c27518f38728782971e0763c0b68fe` fix(tests): CARD-0476 kill drop-path guards, enforce probe depth, pin pty env

Touched-file totals:  15 files changed, 2734 insertions(+), 220 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-6630ad4e`.


+## CARD-0443

Terminal reason: Landed on master at e4191c39.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-6cb9c0d9` | SUPERSEDED | 18 | 48 / 10644 / 80 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-6cb9c0d9`

Commits:
- `17a614b3dc6dde9f0ae4da832d7ee80ad95c0571` wip(CARD-0443): checkpoint cleanup journal and persistence tests; migration and verification pending
- `5729573a738ff880a7be388f1ecbf8a85c4a0116` feat(CARD-0443): generate cleanup attempt migration; checkpoint build passed, persistence runs pending
- `98469cfd09cfaa27b15c152c5bab5f9a49f34b49` wip(CARD-0443): add bounded Windows observation producers; qualification and tests pending
- `f74264354880492da96a3ce5e8e5392b23f57a18` wip(CARD-0443): checkpoint guarded two-slot retry and observation unit tests; ordinary verification pending
- `552cfbaa4ab95f0934f40f76f2e150aab290ea4d` wip(CARD-0443): wire current-request capture into terminal outcome and lifecycle observations; integration tests pending
- `724196eaa77c5fe5cea6eb1beb06bcf8ec1060c7` fix(CARD-0443): use expression-tree-compatible probe assertion; rebuild pending
- `412f6fdb177d3d95d009d647d9bcd26084027fca` test(CARD-0443): add real receipt and Git retry regression cases; ordinary runs pending
- `fe5484a5950411207e865f599ba0bb54bb6c76b6` CARD-0443 retain partial owner evidence and refuse unprepared Handle setup; integration failure under investigation
- `cdc5dcabd5181c6a59b25ea80b21274609f67dff` CARD-0443 add owned native holder and complete-body delivery cases; focused journal tests 18 passed, new cases pending
- `1c8a51e3f95daa2c7636c883041e439727e2af3f` CARD-0443 use dependency-free strict graph doubles after missing-Moq build failure
- `53ef596f516818c57cf0fda59b841bf77d9974b7` CARD-0443 connect holder pipe before enabling flush; native fixture startup failed 2 cases
- `666c4dab0dbfc0bb77d3619344a842b16e7c92d4` CARD-0443 hold directory list access for native sharing qualification; real file error32 passed, directory fixture remained nonblocking
- `9d8b85ad687e1ce15ecb387345570304c03bb6f6` CARD-0443 correct delivery setup: admission creates no approval note; prior run stopped after two fixture failures
- `c80a8b8ab6a589490dfa1a1ee9a84617a50531f2` test(CARD-0443): select modern delivery profile; verification pending
- `8d7d0481df62a02b6f53c151cdda1be6e99b46c5` docs(CARD-0443): record incomplete checkpoint; native 2 and delivery 4 passed, Unit has 4 baseline failures
- `903f2ffeea760edd96b05a5855ce934660657c81` docs(CARD-0443): correct R1 and PC17 class after zero-test selection; R2 six passed, reland baseline check pending
- `c7c12610e910e855a7b0ca95e2a6ebaa73fea18a` docs(CARD-0443): record ordinary outcomes and 103 missing targets; 5 observed failures match base, Code remains incomplete
- `6b2189863aa140d09c17f43f4286a0bb5e3a3b5b` docs(CARD-0443): preserve final incomplete handoff and report policy-rejected output cleanup

Touched-file totals:  48 files changed, 10644 insertions(+), 80 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-6cb9c0d9`.


+## CARD-0467

Terminal reason: Shipped held/aged land visibility and end-to-end delivery.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-7e73872f` | SUPERSEDED | 7 | 9 / 184 / 4 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-7e73872f`

Commits:
- `987be533f422bde88c62c9bd177012a439cf564b` test(land): retain unreceived attention across restart and repeated refusal
- `0dd21853630b591bf252f06c1dffda70eeb3f29d` Merge branch 'feat/card-task-7e73872f' into HEAD
- `d8e89f0a5734f83804ea73bdb95b7f5dc0e4cb05` fix(land): show one escalating hold and complete upgrade handoff checks
- `ac977a159f0a1cee2649432b0590e10260f05cba` test(land): prove cleanup I/O failure and later receipt end to end
- `8116a9a772adf8e050f989fadd790233f84ec2de` fix(land): refresh unresolved drawer receipts without task events
- `eaf7a53c5e2b82dc105db4d09646d1167416971b` test(land): advance query refresh assertions with the test clock
- `d0a26a56890fa65ede0708592906abf74da3e5af` test(land): inspect nullable interruption reason and persisted refusal

Touched-file totals:  9 files changed, 184 insertions(+), 4 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-7e73872f`.


+## CARD-0134

Terminal reason: Fixed and merged to master (7caca1d).

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-a1d37ec8` | SUPERSEDED | 1 | 1 / 213 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-a1d37ec8`

Commits:
- `7a2f11d28bc5fd1510744d0886286aea2a9acc51` docs(plan): CARD-0134 board E2E session Select locator fix

Touched-file totals:  1 file changed, 213 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-a1d37ec8`.


+## CARD-0461

Terminal reason: Landed: e4aa5924.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-a5e37919` | SUPERSEDED | 13 | 45 / 3467 / 44 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-a5e37919`

Commits:
- `d314a7578b5194811222211108a31818f033c9d8` feat: add fail-closed Herdr pane disposal inspection increment
- `a28cd260323bc5d0b84842ece913af0c80999fc9` fix: align disposal refusal codes and compile HTTP verification
- `d3da3d769891bcb38a1908872a35bf0677567058` test: cover disposal preview bounds and document backend prerequisite
- `293bae34f8e47feb9af161d8710df2e7ef15d62c` test: fix preview capacity verification
- `7be553e6c14efdd2655c54b99756d0817e812b49` docs: record CARD-0461 partial implementation and guarded backend blocker
- `bd0b0e2c0c346fd0d913153fc9381009584eeb61` Implement best-effort guarded Herdr pane disposal and verification
- `893f7159a3f4f802e939c6000bbf7296653872a8` Strengthen disposal boundary and acquisition race coverage
- `19dcc7727d390823b2001b989c56dc4965810292` Validate server disposal requests and pin acquisition process probes
- `80db355bb905678b18bb8cba633cfd2bf0625183` Verify named-pipe server process identity in Herdr client regression
- `6d659918fa2ed2bc300fbd807e745244bce389be` fix: complete Stop when adoption wins the pane lease
- `9df59b8f88a8aa09a73f28752b20ddd84e73ee97` fix: publish adopted herdr child atomically before Stop proceeds
- `45d9b7c060639a07655a1a93dfcb52dd9bc2a56d` test(CARD-0461): shorten deterministic pending-adoption dial
- `69de4489051a4c4b06af551bb8d1a903116ba5a9` test(CARD-0461): remove inherited five-second pending dials

Touched-file totals:  45 files changed, 3467 insertions(+), 44 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-a5e37919`.


+## CARD-0544

Terminal reason: Landed on master at f091e84d.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-aaab8f81` | SUPERSEDED | 20 | 75 / 15479 / 148 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |
| `feat/card-task-d2117ffc` | SUPERSEDED | 22 | 76 / 15679 / 142 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-aaab8f81`

Commits:
- `690605db555631b9c26c170bac960402ea3f7f0d` wip(CARD-0544): S1-S3 dormant verification profile, admission, latch and land guard
- `4604605ecbdfc62b8e0a7243925b43b1a5d0fee4` wip(CARD-0544): D-9 Completion obligation on the CARD-0481 outbox; profile in briefs
- `a50030a6b3c1d93eccbf6c32067cd9329463406f` feat(CARD-0544): CLI round/policy arguments and read-only task verification profile
- `703c647d0faf2b05875fc57b3700af7fa7f20756` test(CARD-0544): policy, instruction, brief and parser C544 tests; bundle round contracts
- `0bdb6ba3b079eafd8515541c05ab4945643a7f0f` test(CARD-0544): card policy, CLI round/policy and readiness reader C544 tests
- `106f76fa82024111edf844bcfc8659e25a5fa9b1` test(CARD-0544): VerificationRoundDispatchTests (9) through the production dispatcher
- `c3bda7a64d1b17a062df568f94d61046cd14e8ce` test(CARD-0544): VerificationRoundSettlementTests (10) through real reply settlement
- `d76470caf56b067fc56332cec3d93c448ad716da` test(CARD-0544): InterimVerificationLandGuardTests (13) on the controlled landing harness
- `ea51aa4eedf48f73baecf7a3431b7ad98bbade98` test(CARD-0544): InterimVerificationLandGitTests (3) through actual Git; world attach mode
- `4db3bf8631fa0725df8072e8b42eff825a3c9b7a` test(CARD-0544): WIP delivery rig + completion receipt/recovery tests (unverified)
- `6aebef5995e99e91f4142913bfc066e5107805da` test(CARD-0544): delivery rig, completion receipt/recovery and brief handoff tests passing
- `775a6ff72b19706239e3c9d0ff09db3cf1056729` test(CARD-0544): land refusal receipt and recovery delivery tests passing
- `16fe89bb7dbbfc737411e50f749f20eb7e42001b` test(CARD-0544): WIP remaining delivery rendering/receipt methods (unverified)
- `5d97ae6aa422106d5c254257c611c2539f8355e0` test(CARD-0544): WIP whole-wire and report regeneration rigs (unverified)
- `42d419293b9383f9418f09a4dc25518e538a2142` test(CARD-0544): VerificationRoundDeliveryTests complete (19 methods, each passing)
- `3946a7a83ed428d613a9b8d913e70d84affbcb2f` test(CARD-0544): DataRetentionServiceTests.C544_CompletionObligationRetention passing
- `4445ec50fe7dffbdffbe316d3e837dd5f0205c8f` fix(CARD-0544): nightly health daily validity, scheduled identity and job adapter (D-6/D-7)
- `9d0d6276d867cf7646f1572e637c9e24b6893661` test(CARD-0544): NightlyVerificationContractTests + Test-C544_* health cases; fix sub-second due instant
- `b1b857a30a17dba2f5f3ebce11c4a648602f3eac` docs(CARD-0544): verification rounds, completion obligation, nightly readiness and API fields
- `4b90d575aa5d8a5ab40c3a7f6b88497b0588ef99` test(CARD-0544): register VerificationRoundDeliveryTests in the Slow allowlist

Touched-file totals:  75 files changed, 15479 insertions(+), 148 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-aaab8f81`.

### `feat/card-task-d2117ffc`

Commits:
- `bdb677f556b48b74b9c8a5096ade74636943a1c4` wip(CARD-0544): S1-S3 dormant verification profile, admission, latch and land guard
- `fe2a28ea68e290030b9f5c10ceb91136202affe9` wip(CARD-0544): D-9 Completion obligation on the CARD-0481 outbox; profile in briefs
- `1a3aff23021aadb952ce8c8f1488601c66b087f0` feat(CARD-0544): CLI round/policy arguments and read-only task verification profile
- `c9fcc091d20ac4706cff340f40b46cb00337bfb1` test(CARD-0544): policy, instruction, brief and parser C544 tests; bundle round contracts
- `c71ad38f52c0f0adb856a9e9132173dc16a3d2c7` test(CARD-0544): card policy, CLI round/policy and readiness reader C544 tests
- `38ce4672478d4aaecd731f62f0fd34907a6edf01` test(CARD-0544): VerificationRoundDispatchTests (9) through the production dispatcher
- `8b26b9e3fc9e0a0e5c81c34fd8705389d03d7c60` test(CARD-0544): VerificationRoundSettlementTests (10) through real reply settlement
- `d14e24a208605c05ecda5cc33f0e4681b9bc26f6` test(CARD-0544): InterimVerificationLandGuardTests (13) on the controlled landing harness
- `594ca4055a06424c50bb7204b9b8c691aea9378c` test(CARD-0544): InterimVerificationLandGitTests (3) through actual Git; world attach mode
- `a8e8917145bb9f484c31e9f9019aaf8cf2b876a9` test(CARD-0544): WIP delivery rig + completion receipt/recovery tests (unverified)
- `d8202aa2dbcd287a3e36d60f367faa6b1b5385cd` test(CARD-0544): delivery rig, completion receipt/recovery and brief handoff tests passing
- `097d34809a4e3e2f319bd36048bff89ce02314ec` test(CARD-0544): land refusal receipt and recovery delivery tests passing
- `5267f1a0097632d09f4b9f78568e4dc5c39ecd00` test(CARD-0544): WIP remaining delivery rendering/receipt methods (unverified)
- `7b974d46736cf2ff379771bc56c4087553e29413` test(CARD-0544): WIP whole-wire and report regeneration rigs (unverified)
- `4320707c028a331e6050275b89e6da859b0a3fa1` test(CARD-0544): VerificationRoundDeliveryTests complete (19 methods, each passing)
- `f6a3a4eedfbc0b8e5f5ff79eb1480b5d609e1845` test(CARD-0544): DataRetentionServiceTests.C544_CompletionObligationRetention passing
- `b1bcae69a02ca1f6eda2786e0005b5b4b3cea354` fix(CARD-0544): nightly health daily validity, scheduled identity and job adapter (D-6/D-7)
- `8e54526b6b283f49b4c42b825cb748399ee0059e` test(CARD-0544): NightlyVerificationContractTests + Test-C544_* health cases; fix sub-second due instant
- `3b87a82a815f3575311167269bc54b482460aba7` docs(CARD-0544): verification rounds, completion obligation, nightly readiness and API fields
- `640431864dd2841d4c1c2ddca76be8cae47bc933` test(CARD-0544): register VerificationRoundDeliveryTests in the Slow allowlist
- `c3dcdb306986759ad586f5e1f06bc4822445fa86` fix(CARD-0544): unify D-9 Completion onto CARD-0527 TaskCompletion; restate PC-69; ordinary verification pending
- `ba8e0717f57f457846f6ca3fa1b62d2376b6d4ee` docs(CARD-0544): record unified TaskCompletion ordinary verification; 3 inherited Unit reds, PC-69/121/122 pending

Touched-file totals:  76 files changed, 15679 insertions(+), 142 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-d2117ffc`.


+## CARD-0034

Terminal reason: Fixed and merged to master (c3cabd1).

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-b4e6383d` | SUPERSEDED | 1 | 2 / 389 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-b4e6383d`

Commits:
- `016fda9f554a08ceedfab5140112393b1cfdd370` docs(plan): CARD-0034 investigate+design - the select-to-delegate gesture already works (feature 008); the gaps are discovery and agent-keyed deliverables

Touched-file totals:  2 files changed, 389 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-b4e6383d`.


+## CARD-0057

Terminal reason: Shipped in full, all six slices.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-bf31273c` | SUPERSEDED | 1 | 35 / 7931 / 54 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-bf31273c`

Commits:
- `dc2a7fd600ed03e208a5a5c039f7e3dfc3a6c680` feat(schedules): CARD-0057 S1-S3 scheduled prompt delivery

Touched-file totals:  35 files changed, 7931 insertions(+), 54 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-bf31273c`.


+## CARD-0395

Terminal reason: Grok rules-file transport landed on master (5d96d8cb).

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-c7ff2da0-verification` | SUPERSEDED | 25 | 111 / 11982 / 253 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-c7ff2da0-verification`

Commits:
- `34bd00782b1e9a22f9142bccb61b02675bcf5358` docs: plan durable Grok rules transport for CARD-0395
- `57db87f555173a13c9d1623bd28836479573e5a6` docs(test): specify CARD-0395 Grok rules transport verification
- `a06242c0ca2113e85dba72112157fa8e44c96ae8` feat(grok): add typed rules transport and atomic runner store (CARD-0395 S1)
- `402d1481047298e37bd4d21d2ec9fd79b6eb590f` fix(grok): validate configured transport limits and effective argv budget (CARD-0395)
- `d60449348b4e5d208846ebd44807089b1bb22c2a` feat(grok): persist rules acknowledgement barriers and refresh triggers (CARD-0395)
- `8c0ec49b11f484411e2c8fd84aed87f18b4b4c37` test(grok): restore file-backed dispatch expectations and document open acceptance (CARD-0395)
- `8856bd65ea396bf7ff02d7e1ee1e355a50ed9451` test(grok): pin pre-effect refusal controls and record incomplete acceptance (CARD-0395)
- `318b09661d4951e500db2882c5e97b851c1619e2` fix(grok): recover failed startup and persist receipt before spawn (CARD-0395)
- `b47dfd8ad626abc4ff95462938ff5eb49fd00a61` test(grok): verify launch barriers and independent transport backstops (CARD-0395)
- `76dccb842b7ca44b4b66bc7c86b49e7c9765e44c` docs(grok): record resumed boundary control results (CARD-0395)
- `b9b6883275bfb928f8d1c199239da36444fbc914` fix(grok): validate retained receipts and cover lifecycle failure matrices (CARD-0395)
- `63cf2906b911247edc5a7d48cebb9980fbb4babb` fix(grok): retain failed startup ownership until cleanup is confirmed
- `83a36ff8ca87fd970d2108b76a1b4e6a5564deaf` test(grok): cover explicit migration and native compaction queue ordering
- `c791d990f15a02afacf51d4a2d8034565de1bacf` test(grok): pin card boot and deferred settlement rules barriers
- `599583c0f8e5b190c4e37bc2fcc48089ab05cd29` test(grok): calibrate native 1.0.13 file reads through isolated Responses stub
- `79edc300476470408d27003a6138967e023c1520` task dfc6f878: CARD-0395: build Grok rules file transport
- `349650c33004f029ab2fc9185730d2388af00174` docs: reconcile CARD-0395 continuation acceptance ledger
- `0cbf5aa4b2bb5b8016e835164623778f14468421` test: add real Grok rules dispatch and native continuation evidence
- `88efe8b50c08772e6bbf651842507958a290bdbc` test: prove missing and incomplete Grok read controls red then green
- `fad239ebb623aed65cdda3ced31abc762b79ecfd` test(grok): retain failed native live and compaction acceptance evidence
- `e3207c9c11bacea1e62d0f8561deedd5971dfbd6` Verify native live rules compliance and calibrate automatic compaction
- `da1678b77189e14fd162db13a2d3b873a6213d38` Capture genuine inline endurance and retain file-arm attempts
- `9153a75ef583ecbec77fbbbdf2960fab56d58407` Verify rules HTTP recovery and fail unsafe dispatch before queuing
- `db0b1131f903fbc646da176291551a89e4e27dc4` Complete rules acceptance and recover confirmed input behind barriers
- `d0720f9e156a75117e697549f7a0bdc30e3bf7a0` Record CARD-0416 as operator endurance follow-up

Touched-file totals:  111 files changed, 11982 insertions(+), 253 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-c7ff2da0-verification`.


+## CARD-0206

Terminal reason: Fixed and merged to master (0d126bc).

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-d9b02a4c` | SUPERSEDED | 1 | 1 / 365 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-d9b02a4c`

Commits:
- `4dab0fb33d34762c9ce49b3371d7799962e0c9cf` docs(plan): CARD-0206 SessionRunner.Tests pty-host leak - root cause + fix plan

Touched-file totals:  1 file changed, 365 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-d9b02a4c`.


+## CARD-0398

Terminal reason: Shipped and landed: origin/master 86902399.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-f1f3b5a8` | SUPERSEDED | 1 | 1 / 191 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-f1f3b5a8`

Commits:
- `9043b818f254df97d4d2c1b1a96fc0f3c568f097` plan(orchestration): CARD-0398 Codex remote capability without widening AllowedRoots

Touched-file totals:  1 file changed, 191 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-f1f3b5a8`.


+## CARD-0472

Terminal reason: Fixture fix landed; no CARD-0467 deadlock found.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-f450ecba` | SUPERSEDED | 1 | 1 / 63 / 9 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-f450ecba`

Commits:
- `f20ff0430b942659601cbe1a2921d85baa72d5ac` test: confirm overlay recovery with emitted prompt evidence

Touched-file totals:  1 file changed, 63 insertions(+), 9 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-f450ecba`.


+## CARD-0163

Terminal reason: Fixed and merged to master (c8ab493).

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-fa71cb77` | SUPERSEDED | 1 | 1 / 525 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-fa71cb77`

Commits:
- `8c9c43ce99d9ebe723b9a852474e44033b2a764f` docs(herdr): CARD-0163 S4b plan - metadata status push + herdr badges, report_agent rejected

Touched-file totals:  1 file changed, 525 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-fa71cb77`.


+## CARD-0032

Terminal reason: Fixed and merged across all five slices.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-fb15a6fe` | SUPERSEDED | 1 | 1 / 486 /  | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-fb15a6fe`

Commits:
- `dce786f70dbd2335e8bb65a74f720ac38c7aabf5` docs(plan): CARD-0032 guided project setup to first dispatched task

Touched-file totals:  1 file changed, 486 insertions(+). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-fb15a6fe`.


+## CARD-0305

Terminal reason: Shipped in one pass, e6d879af.

| Branch | Verdict | Unique commits | Files / +ins / -del | Reason |
|---|---|---:|---:|---|
| `feat/card-task-fcf948b8` | SUPERSEDED | 1 | 22 / 2859 / 58 | Terminal reason records a landed/merged implementation; master contains the subsequent rebased/card-series implementation. |

### `feat/card-task-fcf948b8`

Commits:
- `4370b0075ed1397a1360e8eed1a572f83bc20529` wip(checkpoint): CARD-0305 task fcf948b8 - stalled, 22 files, not verified

Touched-file totals:  22 files changed, 2859 insertions(+), 58 deletions(-). File-level inventory: `timeout 30 git diff --numstat origin/master...origin/feat/card-task-fcf948b8`.


<!-- APPEND -->
