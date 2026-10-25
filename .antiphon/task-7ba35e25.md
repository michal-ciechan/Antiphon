# RC integration preparation — task 7ba35e25

Prepared and pushed the assigned integration branch; no rc ref, tag, release script, running qualification or host was changed. The single gated solution build passed with 0 errors; no tests were run.

## Identity

- Task: 7ba35e25-b40f-4fc5-8621-4c64e759fab2; role Merge, assigned branch `feat/card-task-7ba35e25`.
- Base / protected qualification candidate: `a97194ee7a4adf215454fd82c453ba46362596ee` (`origin/rc/2026-10-24`).
- Fetched master selection boundary: `c5ab17338632422905b4c3029b79abe6c0dd1100`.
- Integration source built: `e21a58b6accbc3e952a23340146c23b8fdeef43a`. The final report commit adds only this Markdown file; the final pushed branch SHA is in the caller-facing report and remote branch.
- Absolute report path: `/work/worktrees/task-7ba35e25/.antiphon/task-7ba35e25.md`.

## Selection and prerequisites

Applied 255 individual `git cherry-pick -x` commits in master landing order and pushed after every commit. 252 executable/test repair commits plus three prerequisite documentation commits; no feature commit was imported. The three documentation commits preserve the document history needed by the later mixed land-graph audit repair. All other documentation-only or investigation commits were omitted.

The rc base is an ancestor of the fetched master boundary. Every post-cut commit touching executable, build, configuration or test inputs through that boundary is included. All such changes were inspected as bug fixes or test/fixture repairs to existing rc code. The final source comparison against c5ab17338632422905b4c3029b79abe6c0dd1100 found only 24 differing Markdown paths and zero executable/build/configuration/test differences. This gives the complete source prerequisite chain without importing any master-only feature. No selected pick is blocked by a missing feature.

## Required task coverage

Group 2 is covered by the qualification/notice/park-root and startup-route regression repairs in the ordered list (including `0f9a22fd70d2db679dae5d265f44e3ca8a38a2a7`, `ef4a038a8b50098aeec80023f2c6a43339c87f71`, `dbccdfa2e9a51558e3ddac222dcac132494fccd8`, `b85366ecf09a2e35bb6611c662a93cfb0f914b86` and `ceb6b8da3b0c11d062594d84c7b98751a681e99c`). Additional rc fixes cover fake-provider staging, logger lifetime, land graph audit and expired runner lease recovery.

Counts below include documentary source commits when present; only source commits appearing in the ordered pick table were imported. Source task branches were matched to landed master subjects; all matched exactly, including branches rebased by landing.

| Run group | Source task | Landed commits identified | Selected |
|---|---|---:|---:|
| 11 | `2c9106ba` | 4 | 4 |
| 16 | `6c76c765` | 4 | 4 |
| 19 | `867c8362` | 3 | 3 |
| 20 | `fd07317d` | 1 | 1 |
| 21 | `d2a0a930` | 1 | 1 |
| 24 | `4cd09059` | 118 | 115 |
| 25 | `2de763a6` | 2 | 1 |
| 26 | `409d401c` | 4 | 3 |
| 27 | `df70be52` | 8 | 8 |
| 27 | `ed7af4f3` | 4 | 3 |
| 28 | `6d1c77a9` | 4 | 3 |
| 28 | `40bb57ba` | 6 | 5 |
| 29 | `62a77bb0` | 6 | 5 |
| 30 | `b76d2a8c` | 2 | 2 |
| 30 | `b8c3a4cd` | 4 | 4 |
| 31 | `d55dd41d` | 3 | 2 |
| 31 | `74315dd5` | 3 | 2 |
| 32 | `cea2c546` | 7 | 6 |
| 33a | `c3c1d420` | 4 | 4 |
| 33b | `f237ea10` | 4 | 3 |
| 34a | `32a1e275` | 7 | 6 |
| 34b | `8a0ab444` | 3 | 2 |
| 35a | `7cd0d43e` | 7 | 5 |
| 35b | `991f01d8` | 6 | 5 |
| 37 | `22bd72e0` | 4 | 3 |
| 38 | `db4f734b` | 12 | 11 |
| 45 | `8c208f93` | 11 | 10 |
| 65 | `9d2537e0` | 10 | 9 |

## Optional pick disposition at the final fetch

| Pick | Source branch tip | Disposition |
|---|---|---|
| Group 36 / afef8c76 | `9182f666e698b4ed7fd891008ccd89b87e65d6e1` | Not landed on master; omitted. Includes conditional-input spill transport repair; commission a later reviewed rc port after landing. |
| CARD-1575 / 8ac0836f | Landed `c5ab17338632422905b4c3029b79abe6c0dd1100` | Included as pick 255 after the final fetch. Original task tip `26143a06b4fab5d88b5b9a429f3b3e607f8a0c04` was rebased by landing. |
| CARD-1577 / d4452f64 | `09466d5cccc4c1b812ab9288b502a9b89208da81` | Not landed on master; omitted. Script-cost recalibration remains on its task branch. |

## Ordered picks (master SHA → picked SHA)

| # | Master SHA | Picked SHA | Change |
|---:|---|---|---|
| 1 | `f4e8da80bfc224ff39414e34f3117ed72473f505` | `c594a39460e0c4525ac2ba3defd1456479416784` | test: witness Linux fake-provider staging collisions |
| 2 | `7bf0096334c745b1fb111b43eaa5374208663c48` | `584b4e18912bdaca653aee70990709e74246038d` | test: match relative MSBuild collision diagnostics |
| 3 | `9722842b85b59753eb09a9cd24865615dc50f1c6` | `db407cfeee2f460b8a87ac4b539176c0efea2a28` | test: preserve the single-provider E2E witness name |
| 4 | `e9f0259e5fd07dbe9f50365f07c382839af6fdbd` | `fef142530c5dbc232b9a99bc19137eac3245d15f` | fix: keep Unix fake apphosts out of staging directory paths |
| 5 | `e15dd13b734dfffc64284693e7501e035c1dda16` | `97b9738821dbfc791e6491c71f25d019c4ff3037` | test: expose phone-home startup route binding failure |
| 6 | `0e3e8f22ae65a04e07926038664b7e0e3685a778` | `d0e3673619818deea160924505444df851762ca9` | fix: register live timing in the phone-home test host |
| 7 | `bf40c087e895552a314d55e345cb8e1635affa2e` | `10a71824a528aa7b99e13c2eb4fb12e3b9a197a8` | test(observability): expose startup sink disposal order and scope allocations |
| 8 | `80763f048f32b9f7639b31e85f0f2a4250bb9005` | `3c7bb39e1c97171dfbecf32c25a0eb28520f5d7c` | test(observability): make disposal race deterministic and reject five-pair inference |
| 9 | `d685550ca417d867c43d523aef3a5a5bd529eb2d` | `793a1ce9cd24fcbeed41353a281a2813b7d3043b` | fix(observability): own startup logger through host disposal and stabilize paired bound |
| 10 | `0f9a22fd70d2db679dae5d265f44e3ca8a38a2a7` | `c9332453c18abcb13e36e5a4e9fd1eaf641c98b8` | test: witness Windows notice clipping and park root admission |
| 11 | `ef4a038a8b50098aeec80023f2c6a43339c87f71` | `622bd079f502efb82849a88819abe39c8c4b70fd` | fix: normalize park roots and preserve guarded effort and notice fixtures |
| 12 | `dbccdfa2e9a51558e3ddac222dcac132494fccd8` | `5d1ad8a2a30c51d972c8b5cc3d55c91bed07e0cd` | test: correct Shouldly overload in notice regression |
| 13 | `b85366ecf09a2e35bb6611c662a93cfb0f914b86` | `ad26c5501fa94a532f673887c055ab63f6d6ba93` | test: witness qualification route and composer regressions |
| 14 | `ceb6b8da3b0c11d062594d84c7b98751a681e99c` | `1afd23792184d832f00ec2eb297a763ec9ec1cc3` | fix: isolate startup diagnostics binding and refresh guarded Claude fixtures |
| 15 | `dcdfd0c6cacd870acf864e6f3b366af5abe23c08` | `993508e82ad161cac6dfb33fd3be4a4622298223` | test: supply guarded Claude observations in post-land delivery fixtures |
| 16 | `a9275a54390c255a3b6922830ba249d540736146` | `588774af7290e86c4eea14b291b0a91dea3b1ae7` | test: drain receipt routing before pruning completion evidence |
| 17 | `bf1ddea8e6ba85e209e5328ac39a24b226f48608` | `0865bdfda8f19b8a1bcdf1cebb9019029c43bba5` | test: supply guarded Claude observations for message delivery fixtures |
| 18 | `e4b23223b0e3ee8991448c3171830b23e8df32f4` | `fdca43c0743d0225f471d1caa0e30d7a3cd6574d` | test: witness guarded Claude recovery intent regression |
| 19 | `dcf5c3bbee11b1e510e58b27e7890cec95f19213` | `f26fe14d6bfc2aa6abfaf72ea54fb8f6a727ecd7` | test: correct recovery witness assertion overload |
| 20 | `48f5f1b1a89de07063db8fd956acec40a4bcf326` | `0ffde2c002415a1858ecfda64b4021353806dd62` | fix: keep clear-composer recovery on ordinary guarded input |
| 21 | `e48f12db25cb8a958b7e82db53c6de0e3b53b3a8` | `f79e32e53b63e48d3ea006ad0b9610479cffff0c` | fix(hostcleanup): keep diagnostic namespaces fixed in source |
| 22 | `764bf8288775f6145c73a109148866fa596bc5c2` | `bd273960c32b75b9154800ecbf2ed79177da6497` | docs: record inherited land audit target blockers |
| 23 | `a71ed30b421a8e93e52411cce6a54c606e888657` | `17bb9090ddfb78bd4e99e1d4b4f928a36ed0eef7` | docs: explain no-pin fake-provider replacement constraint |
| 24 | `1c8986785864905e3e23a74c84f860f4e8a41fdc` | `ebc5e72b07100e60130e789516d73c087ab5c3bc` | docs: retain caller-supplied layout verification provenance |
| 25 | `4297b38c227fb9ee2d9b112c6bdc6c6cf58eb490` | `d06a80fc2122f3e5d6a4a9f2675bd4f210fb4a27` | test: guard exact SDK copy-item removal policy |
| 26 | `a756d600aa8e25987a4e97bd10c1802ee7c1ddc6` | `ebe5a53294d69ac315a198e00469d42b0644235b` | fix: admit exact pinned fake-provider SDK item removals |
| 27 | `5f7d5376f1cdbb06fa19f53686b126621031c839` | `2040dc86740be4e97dd3d1772463dce53ac247a0` | fix: regenerate evaluated land graph records through audit |
| 28 | `a196b9fe772edf94b1514bb97bd79f493dbda052` | `cd6a3f65e4849e3202b813c0e12f185e358090fe` | fix: preserve source-switch cleanup and fixture verification artifacts |
| 29 | `6ae5f13ef16d97f601444b608ebab54ea256d820` | `f0755cc9612f5da7c1452c6573b990650504705a` | test: reproduce phone-home eligibility latch after lease expiry |
| 30 | `1992ba8326971a39fada2c44fbcd5e09b01d2417` | `41fcafe60378c1eff5fe26cafee0659710021fad` | test: isolate lease recovery witnesses from periodic refresh |
| 31 | `9e38c1c87a2783181e04d5522196b75dafb696a9` | `e0e80fe5077643b80b6ec6eb4b3995d96604b568` | fix: validate catch-up before restoring an expired runner lease |
| 32 | `4e31ba5bd646b8bbac273cac15c396ad3a43b529` | `6e5195f548343ed719b4268a7eaad58cdd5c14dd` | test: qualify guarded manual input and isolate retirement refusal |
| 33 | `11ea992b97c93c38bb72859b6b4153009a2da2a4` | `ccad7ba39931d21e72bebe456de2274cbeeea35e` | test: use the interactive API for manual-turn inventory coverage |
| 34 | `c9dc34ed2f038cfff72106c3b17a04cdbff5bf3c` | `139683488d6e4fb0c8dab178f8a72b233e27a8b8` | test: supply warm Claude launch and dialog state in commit child chain |
| 35 | `81e16ecc064929587f14bc8eaf25da4a4a78cd8f` | `3760cb860173925cea1f2c5c6ac1a33ce9c2d160` | test: reproduce quoted adopted-checkout registration assertion failure |
| 36 | `4c5747c667a81ca7b010b90b17f42c0e3da3d946` | `a21300f6a26db570d511152fb79496d104645412` | test: exercise adopted-checkout porcelain record delimiters |
| 37 | `3d2ffccc4498838b71d65ae9dfabe128a81c2975` | `3e1115607099bf50b676e9962f4f401ecf211e9c` | test: compare adopted-checkout registrations using filesystem paths |
| 38 | `4218a53b3a8ad7b6c91e80ad458452192fa809db` | `103ea16ccb5c6d8f537f1433f46a04a0483bafda` | test: centralize observed Claude delivery harness creation |
| 39 | `8b28337f6f5b1e13917788a320560044939b04b7` | `c0fc75fede95f7f484023f20b59e9e13ce7ef759` | test: observe guarded Claude input in AgentTaskWorktreeLockOutcomeTests |
| 40 | `ea5c4d1c9d046bd77fe451289dc6e62c23584cc5` | `c05d82ef8eff85898c3dfb02fa23d01e6e0bfa0a` | test: observe guarded Claude input in AgentTaskReplyOverlayTests |
| 41 | `bbf7631690d9fed090abcf6f928de5c5ebc864b8` | `5a034ebe6d4975ede7e6cca27ae650b41e519ba1` | test: observe guarded Claude input in AgentTaskLandNotificationRecoveryTests |
| 42 | `a567d10ae9fdb4a8abf8a6ee5d9963c67b74b89b` | `0227c9d6afa49cca1d7ac404089ffa2caf039491` | test: observe guarded Claude input in AgentTaskLandReceiptTests |
| 43 | `fbdb079e0b98c8c5ab0a83931a3c95737c08f0f2` | `0ebf84ca28f3e395d4d0b07642b4244111d9f060` | test: observe guarded Claude input in AgentTaskLandQueuedReceiptTests |
| 44 | `bd71d361356209061bcdceab3d04f163c6a24a96` | `f8cf11c3541d9535b1165a265eaf800e127eb8ec` | test: observe guarded Claude input in ApiErrorRecoveryServiceTests |
| 45 | `045a0ff45d74ce2329113e6985f54d80aeaaf3e6` | `36bbae2b92198c0a208028f77210f1dcc48259f4` | test: observe guarded Claude input in CapacityRecoveryQueueTests |
| 46 | `355a2a73fb9f0a0bdd8ee0ec3dafde8139ba05b0` | `7040e4a881cd8812939832f684e34245d601bc83` | test: observe guarded Claude input in ChannelBatchingTests |
| 47 | `c40b6f85db6c5c6612437f6653918689b3656518` | `f97e5304fff14ff06578d2e0297c92ce9cd592fd` | test: observe guarded Claude input in ChannelBridgeTests |
| 48 | `0ec7a75da0604d04250bc821f2255003e3de1b4a` | `a4523405ce474484dc671a18fe8672c4f0cf3519` | test: observe guarded Claude input in ChannelPromptCorrelationTests |
| 49 | `47eac38d03b17be914a9b7ee1f74a1f2dcb4c90c` | `dd557df74d061f694294deeb655757f73d866b3f` | test: observe guarded Claude input in ChannelReplyDurabilityTests |
| 50 | `f3535f599218518787b49d2b3f800fcbeb01f2f0` | `8d904c14767725cd7950fa72702131097c05d443` | test: observe guarded Claude input in ChannelReplyLateConfirmDispatchTests |
| 51 | `7d8b672d2ec91e0f78403c3a4bbeb686647a3fb3` | `d08ab5e6ff58a88e82a511652d3b4a61ff69dc56` | test: observe guarded Claude input in CompactionRecoveryTests |
| 52 | `053b0422b4512fe442d89de12a7b197286dd80d6` | `f3dfb1a264f98377138213c0ff7fdb4d6baf7cbc` | test: observe guarded Claude input in DeliveryUnverifiedDiagnosticTests |
| 53 | `29bfa40ca6fb1039b885982fc6671afb542767bd` | `38b35a39e45df1259b0d58c1a0f5398b4f15d1f8` | test: observe guarded Claude input in PolledCompletionNoteShrinkTests |
| 54 | `37a4fa0bcf4b7a334e2288ca828529836646c74d` | `0af6e1a9e3f63378bed55fbb046722b6b7b5b51a` | test: observe guarded Claude input in PreDispatchFailureDeliveryTests |
| 55 | `bfaca575fda9c15b765eae69273766592a391810` | `55c2ffd5c5bb9248f0628e6a6be811bbeea56144` | test: observe guarded Claude input in SessionFinishedDuplicateTests |
| 56 | `a50f48fbb10ca123b31b5baff1d2b84489698afa` | `72ccf7d94487e056eab7dd1e5147e680d64f64db` | test: observe guarded Claude input in SessionGenerationDeliveryOverlapTests |
| 57 | `86c9081011414b89f66c04b52425bb2a36f5c5d7` | `2388f24902584ba7682d12fc434bbe340128d5e8` | test: observe guarded Claude input in SessionMessageQueueInterruptedAttemptTests |
| 58 | `87c3eac0b1857f1e840d681ce594bff912aeda3e` | `0e7317291d26552f7e57a72ba67d1edca060ec91` | test: observe guarded Claude input in SessionMessageQueueSpillTests |
| 59 | `1ec13944d01c04c49dc498e0539f0f4781a66031` | `2f920497bb769cc8c67e41ee351c3d85357d4e6c` | test: observe guarded Claude input in SessionMessageQueueSupervisionTests |
| 60 | `51507e5132f77d6cf69457d354152da584b49ca5` | `cba657e6c3bd1cd42325083dd31ffb76aaf457fc` | test: observe guarded Claude input in SessionMessageQueueWedgedHeadTests |
| 61 | `f170bd5df81515e0a2cd7552f3291d439dd82237` | `80c6facec4f5e68986d83f497d5b2042d11e9399` | test: observe guarded Claude input in SessionRuntimeActionDeferralTests |
| 62 | `f09f005a15501b625f3d319be0a2da8e81120532` | `87f73d00dd3fdb80f1781d86fa774af26cbc7c8b` | test: observe guarded Claude input in SessionRuntimeActionRecoveryTests |
| 63 | `8b7452bfd8c322e9b965ae33982cfe94f30c88de` | `982cb82a7abbd96cb09e7b581e3cac39bc7e7c2a` | test: observe guarded Claude input in SessionRuntimeActionRoutingTests |
| 64 | `70a2aa2ea75c140fa705be54d48acbe0c9b23917` | `b69037421ea012016b5f963c9802d1b5d9d87446` | test: observe guarded Claude input in SessionRuntimeActionTemporaryBlockTests |
| 65 | `7c287f6c2c35f33750ebb52e6665dc78c18d317f` | `543f9a8b7314c20aef41f35ff214832ae36b05b1` | test: observe guarded Claude input in SessionRunnerEventPumpTests |
| 66 | `66be87311369f90555f9bfe7c8a76724b7ec2a13` | `a6ce1f7355d8850d3e15fdb27dda2a81f7f44b18` | test: observe guarded Claude input in TranscriptBindingIncidentTests |
| 67 | `9a41efd2f83392f600a4ff3a053a3e75d1677d6f` | `cfd472b42294894708b9c9f0f1a83b86dd786b24` | test: observe guarded Claude input in AgentTaskAnswerTests |
| 68 | `d75fee3563bd5ab988d8274cf3b04e3302050724` | `6bb634deeb6c1b1a8582f6f82b5cabe46c9aef97` | test: observe guarded Claude input in CompletionNoteDurableSweepTests |
| 69 | `662faf0c5d8fb1887d6769c7d58244a17697fb44` | `b9c8f33c0b8f97f7060f093f14e7046ab84404f2` | test: observe guarded Claude input in DispatchBaseNotificationTests |
| 70 | `661e84e33c653aab352131e6b8fcdc32f4600c05` | `138866c274598460ae2b3aa00ca1f42d64c103d8` | test: observe guarded Claude input in DispatcherSiblingInspectionSchedulingTests |
| 71 | `661eaeeb77b77a8ec7955bcf212593bf92465c52` | `783e00d476c59576e8b3af864e9ad9d95abb17c9` | test: observe guarded Claude input in DurableRunnerSpillReceiptTests |
| 72 | `4138c81bec5e29fad29fd0427b514e7374da986c` | `a4a56511735d0dc3923203f0c496a8ef6ac1bfa5` | test: observe guarded Claude input in LandEvidenceWarningDeliveryTests |
| 73 | `f463a21c01b3d0ad4cc23023ecdd855557a37c20` | `eda5dd6afb966108b75c9982d1ac257bba1bcfa8` | test: observe guarded Claude input in LegacyCheckNotePublicationTests |
| 74 | `b6eb484ba919d6b673fb8b1b64873f5f6a29e030` | `236c640e14118bb4ec08f49b84ffaa7105f48ff4` | test: observe guarded Claude input in PendingSettlementDeliveryTests |
| 75 | `1be575f0e919798c4ba890c0e487e0b26163639c` | `c35d0658aa77df7f8e754daa1c4b2a956fa746e3` | test: observe guarded Claude input in PhoneHomeQueuedTurnTests |
| 76 | `629e98636b5aa7a40f5449918fd4a9e30ed8a200` | `f06eeccce46f05a5f12c0f91a0841d2418824bf7` | test: observe guarded Claude input in PhoneHomeStrandedQueueTests |
| 77 | `4c0e5ac4805b411b29249213b848b6517f0cbb0b` | `2a84c6e66ba7b4a3d0d71f1caa428a029963b24f` | test: observe guarded Claude input in ReadyGateFailureNoticeDeliveryTests |
| 78 | `eb6a6eeba90a324f167e601488444c1b6ef77240` | `6da34d343420651813c63f1d908ecd449669f4de` | test: observe guarded Claude input in RemoteCompletionSpillTests |
| 79 | `ec1ce803084b0671f6c86400e7f11b2e0a3a9d44` | `dc14f0c108dcc84c3d979b45a72277bdceb5e958` | test: observe guarded Claude input in RolePolicyToolBundleDispatchTests |
| 80 | `c1701a743b953158b366ef751e0c69267671d31a` | `792cfa7d777a3dee4f581ab53e59584c3f2cd523` | test: observe guarded Claude input in SessionMessageQueueBootWedgeTests |
| 81 | `799efdcf4e10b66df146f07556e0f4a2ad2b0a01` | `567611d39c73772d5d6ce4a2da5b8640ec8192d0` | test: observe guarded Claude input in SessionMessageQueuePhoneHomeDropTests |
| 82 | `c692f5de3e58deae557a70b2bc8ccbc91c9bf01a` | `49a9345f726ec0cfde9831f63352cc9f9a02278b` | test: observe guarded Claude input in SessionQueueReceiptPlumbingTests |
| 83 | `5b5fd56511e40cfb988ea15bdb589ecf984a402f` | `0658e367c04d9a228ca5d4cea45988ebd96205aa` | test: observe guarded Claude input in SessionRuntimeActionTests |
| 84 | `9b40b7248b0c93911b879a6636296b0e11e05067` | `cb12701e937bdee55133b618d1649eb0d0f53d96` | test: observe guarded Claude input in SpecialistExecutionIdentityTests |
| 85 | `6608af2995c9febd3ea8bca9f40385ea9534a407` | `ac7f04cb68f12530ecc9dc9efce4cdf5b005ecd2` | test: observe guarded Claude input in SpecialistInputTransportTests |
| 86 | `4307f8d15fcf201f3fb70dd1ae19497cbcaad24f` | `ef9fb3b76d175b267826ff816099bec45c1a82a2` | test: observe guarded Claude input in SpecialistQualificationTests |
| 87 | `a71e1b18b3e5d89aad1a05c1dc26119d39d6dd49` | `b6a88c5890f1556fe7e49186691ad739a1cb551a` | test: observe guarded Claude input in StandingBootQueueWitnessTests |
| 88 | `4b20bb5b172cb95748de70f9df1ec6420b50b9e1` | `389da9cdd543f09a2a554c10744c25230e3984b0` | test: observe guarded Claude input in CheckNoteDeliveryHandoffTests |
| 89 | `c8ff96bbfd4ce8b76fd44974627734739a78a02c` | `537f145bff3be2d7573d3d5463e103ff992e0b9f` | test: observe guarded Claude input in ContextCompactionSweepTests |
| 90 | `df30656f5832bba94e34c2e63465f4bdd4aa7073` | `57578e545c82f616a1633a8cff1d72d457af6ce6` | test: observe guarded Claude input in OutputDistillationDeadlineTests |
| 91 | `7ac3071646c2301ae75cfc2c201669040afd3f56` | `ecf4835b6459bd0bbbbb18ef4430f3970692860c` | test: share guarded Claude setup for newly launched fake adapters |
| 92 | `fda48f6a29b29215c3082adf5b93e75afc79dad7` | `86dd177e5478a8a53c027fe7af116aa1079a88b6` | test: bind standalone Claude delivery adapters in SessionMessageQueueServiceTests |
| 93 | `748cd9b105bacb8d05541ae62d8d09e141706b2c` | `c601d2e901ed0f481360600b8ff844d8cde60dff` | test: bind standalone Claude delivery adapters in ChannelBridgeTests |
| 94 | `bf108bd3d17f9f77d385768aee9da73902815d94` | `f2f9964a507eaff9f469fb06ef6d4af1752628b5` | test: bind standalone Claude delivery adapters in ContextCompactionSweepTests |
| 95 | `178d9bdd5fa54b392679ac6e47c409859e179845` | `aa29511b0ce7824fdd7e82593a0169fe3aae366f` | test: bind standalone Claude delivery adapters in OutputDistillationProducerTests |
| 96 | `80bcad910c3706982e2877072ea46d0d3128fe8c` | `8e0c4e235f849758735322c63efb35c2afd08594` | test: bind standalone Claude delivery adapters in OutputDistillationApplyRaceTests |
| 97 | `1f05500d3beb387e2cbd81ac5150b1c77e7d7ecc` | `353bcb4977947c4cfa4b21885ecef921b5244131` | test: bind standalone Claude delivery adapters in OutputDistillationDeliveryTests |
| 98 | `bbe0d19f7acd038d5b6207a5187e1c9886a85911` | `829c92826ff7f2ed0cef660d707a30ca1e8a566b` | test: bind standalone Claude delivery adapters in ReceiptFailureDeliveryTests |
| 99 | `2d9b2809c483491b61160b2cdb3b348b5c3cbca7` | `fe40bad619ef7bc53210490851bafd3739571622` | test: bind standalone Claude delivery adapters in CheckNoteDeliveryHandoffTests |
| 100 | `0f25fd776577620479188fb9c36df199105f7b31` | `269f97f509dc0595f93b8a4564a14476ab132344` | test: bind standalone Claude delivery adapters in SessionGenerationDeliveryOverlapTests |
| 101 | `ec7bc20d00e9c5dca28521c7285fec7f74b74052` | `fc903be5f4b3b0db32961347fd1d0bacc849bd64` | test: bind standalone Claude delivery adapters in PolicyRefreshServiceTests |
| 102 | `99f73d3d00bdf0198002aa9269e7dd8bc2348ba4` | `4e163a726545792820fc9cb23885245c04e27c4c` | test: bind standalone Claude delivery adapters in AgentSystemPromptLaunchTests |
| 103 | `1bbbf9a84d926e022743e075516ac8ac4fab1d3c` | `38fe412ec83a1c56a0ce9b371f5d7a05fa0dc822` | test: support generation-bound synchronous terminal attachment |
| 104 | `c49e9370523b3b9ad29789c39f23fa18bbe7ee78` | `cc51e095033d9dead7998ec3e436244f393f0ea7` | test: supply current Claude observations in AgentTaskReplyIntegrationTests |
| 105 | `2ad08d4bebc4e7d1089eea29e0297cc4b08e0e35` | `bf79fc6922f83b8cb29eadc9bf52296f82cbc427` | test: supply current Claude observations in AgentSessionInterruptedLaunchResumeTests |
| 106 | `c60878ec924170c00595ec1e2667bc1543676415` | `3d63ec1053e42541148e82a07045300700b9ea1f` | test: supply current Claude observations in AgentSessionLaunchFailureTests |
| 107 | `ccd7ea31f55c9694f64f51e2a5563975e8860efa` | `5102619772a9be85b3e3b3551299b87f5e139c9e` | test: supply current Claude observations in RemoteControlRecoveryHarness |
| 108 | `d1e0e5a326e6b4a59972bd365d9e369d751b60cd` | `9d2a4b2b067cb09067f5a51e140df18828065c47` | test: supply current Claude observations in DeliveredSpillFixture |
| 109 | `868aaef76771ba89dc5c048381870464918ddc25` | `72982d6e415d5b788a4c716bb439f2abc27ea8fd` | test: supply current Claude observations in TaskInputSpillFixture |
| 110 | `ebddfb0f52f9406299975622e0f3413a28f80964` | `9b5161e6b422d7299003b4d679db89630dd03dcf` | test: supply current Claude observations in LandOutcomeDeliveryHarness |
| 111 | `cd51addb16410127aa1bfb39a66dcfe3a5448272` | `77d96b1bcca6b7498a2bb1c8791048802dacf5a0` | test: supply current Claude observations in LandRefusalDeliveryHarness |
| 112 | `702e07e140096f2f8e6214b2181ee39f702a1097` | `f0fe722c255a2ffd8551a374204cb754885ee1cf` | test: supply current Claude observations in LandReceiptScanHarness |
| 113 | `c51ab8d438f390a2257a3671da7c73afa944d596` | `aa6ea39341ba2bc3f5e259a989c03666021ff64b` | test: supply current Claude observations in PhoneHomeHostedRunner |
| 114 | `a32a61fc971a5294eb6c0f22797e8c12066652df` | `4d810924dbdb72a5c49c3863fce7da82437955ed` | test: supply current Claude observations in PhoneHomeOutageHarness |
| 115 | `930d286242221eeac836d7432c6d9cd27018cf37` | `aac5d35f89196016cac6e5b5a625ddbc541ef268` | test: supply current Claude observations in PostLandMutationDeliveryWorker |
| 116 | `83b39a8554d56dc81a15dad509c6d73cd0d74335` | `aa0776df1dba3750b7ad9ca4d8d613f00670381d` | test: supply current Claude observations in LandQueueRaceWorker |
| 117 | `7c637ac86e4d10fffb56eb1774c8f4a84e812873` | `b6da8b168b199c76753d0029a0db1c99dbde8e34` | test: supply current Claude observations in QueuedReceiptAssertions |
| 118 | `29f1ed1bdfc3a8f306d805cacf3b09924d271737` | `7e315c194e5a25a0194eeaa905b824693ab9bda2` | test: preserve non-fake interrupted-launch adapter support |
| 119 | `7a75a57444ae0634b48d8cbea575cf55b4c0e0ac` | `54863f648d4a92127beb20f7cb6eb4787c1558c7` | test: preserve launch prompt hooks on guarded fake writes |
| 120 | `7dbbb1f08ac101322d4e3a8bee9c196705cd8bc0` | `9f70c2180fe8b58432081e36227d6172f7fe83d2` | test: complete guarded Claude fixture wiring in AgentSessionLaunchFailureTests |
| 121 | `1a557c70bdc7d76680e0e2e49dcb21bb9f4cf6fd` | `c91d1b499e70030707dc50491d1f2f75ebfbe6e1` | test: complete guarded Claude fixture wiring in AgentSessionInterruptedLaunchResumeTests |
| 122 | `596a6480ddaff656c94bb866898e0a530e2ea0fa` | `454503f6137bd2fa648d39449afba69a5cdd3692` | test: complete guarded Claude fixture wiring in CheckNoteDeliveryHandoffTests |
| 123 | `c5e334f548e38b1b3d3fedce00d058c0e6a94146` | `039b8c4a8d05f51923a924c15f7e5159a7f5ba7e` | test: complete guarded Claude fixture wiring in RemoteControlRecoveryTests |
| 124 | `1c11c0d3924374e222a0c7554db300a69af662c2` | `7a1d202c125e43176ba8072d128ae50475291bfc` | test: complete guarded Claude fixture wiring in CheckCompactionCrashWorker |
| 125 | `aaaa9a3ef2feb905229228583de5d8ddbae4fddc` | `1b5076746828339e195f7029c92dca9c92355807` | test: supply guarded Claude observations in AgentTaskReplyIntegrationTests |
| 126 | `2430c97baf1d267e1cead18c55515bebd6e45d82` | `f36467c0e0eec3fb583dc1edd0dd2d44bb02ddec` | test: supply guarded Claude observations in AgentTaskLandPublicationTests |
| 127 | `d2c2b307580f6517c32846d1f926c3a643c06628` | `ad9c925d59a9e5e9a8db32bbc62f162a23be45ff` | test: supply guarded Claude observations in CodexStartupDeliveryTests |
| 128 | `00a1e4079dad4c7dead47d7938155d6a25e99ce5` | `2a7ae838bcacf9aa2b10c78209fe37d6b0cf1755` | test: supply guarded Claude observations in CodexCliObservationTests |
| 129 | `3b3325e39d25cd632c96fa144db3ba293fde6ca9` | `7f39607ef6283d14cb78284f9bf946787f8ae4fa` | test: supply guarded Claude observations in DefaultRunnerRerouteTests |
| 130 | `a65fe0c49b68591a2ee003e98654973d43fc24da` | `1a63ec39251da8d1230a2403659e0cf2c5efea46` | test: supply guarded Claude observations in PhoneHomeImmediateSendTests |
| 131 | `b4275f41ccba0578c2cdd95d424d78cd64ce9069` | `a2022bb9efa12c372fdb5a15a4e91223226fb577` | test: supply guarded Claude observations in SubscriptionUsageMonitorTests |
| 132 | `648ed7184f5822df52697bca321a8e7f68a0ffc5` | `3c04dcf2d7f7dbde1666b1c95210eb6cb46d2164` | test: supply guarded Claude observations in RoutingExhaustedPlacementTests |
| 133 | `261230d09117174fc571eab3dd56e85d4959d6cf` | `0a0d81e656bc10047d7d7e602f558c1687c468ca` | test: supply guarded Claude observations in RunnerAlarmDeliveryTests |
| 134 | `c2e1970b4b4dd4257368eb51b3d0d3004471b7c1` | `7f5a0fef5f3ed958cbda1bc4591e0863ebbf9eda` | test: supply guarded Claude observations in DispatcherRemotePrepStarvationTests |
| 135 | `e07be469e8156f6e764aa223f4e44919978946e2` | `f11140c04371a38d7fe9b023645b0386431f16a3` | test: supply guarded Claude observations in RepairSourceSettlementTests |
| 136 | `f1450dffb85b966147b70ac07b68e34c160de81d` | `54a2cd9ff8a7f71b7b507e3d0f024a0f1409849a` | test: supply guarded Claude observations in RemoteWorkspacePreparerTests |
| 137 | `46386f1bcd3e13955432378135f1dbeade9a1f4a` | `402cc538f999ba508bbdb34ad43240f6bc70992d` | test: supply guarded Claude observations in ProviderQuotaRefusalAcceptanceTests |
| 138 | `ef9c1e3ae97369b85f4e80378f6051ab634096b4` | `daa8a6db14b4abf1562a9a743fa132cb98432ddd` | test: supply guarded Claude observations in CapacityRecoveryAcceptanceTests |
| 139 | `f272b086469d3550d06af3c744afa519a22776e2` | `7dea1ab672845555d469f11a4504f9a18b0972de` | test: supply guarded Claude observations in SpecialistToolPolicyLaunchTests |
| 140 | `1cc4a4ec4e7716f809ef9194273baf9df2e244c3` | `7143b36907a8fff269b3c7970dea9dbda7b983d1` | test: supply guarded Claude observations in BootLivenessProbeScopeTests |
| 141 | `3fe5a54f331e72708d4008a58e40e45aef14ea25` | `47f1631acbd800d8451a9979289e9a7aa831816c` | test: supply guarded Claude observations in AgentSessionServiceIntegrationTests |
| 142 | `287b50a4c2715989b1fb59bc4520ff278d18aa8b` | `92229033b506087d9ed663629ad47ef3e79475b8` | test: supply guarded Claude observations in AgentSessionLaunchQueueOwnershipTests |
| 143 | `7842d496ed66a01a139676703f8fc908a0e8f43d` | `b00a36d83e2a49234170c791e2a51a340858dd30` | test: supply guarded Claude observations in WorktreeLandingCleanupRetryTests |
| 144 | `43c1207dbcc7637914e541225e63b07f39b5dcd2` | `efca1afb48051453a5446a6cbee814edfa920c8f` | test: supply guarded Claude observations in RunnerAlarmNotifierTests |
| 145 | `cbca1858596961e27b05e199a0ea1917d02bc551` | `07950747a6ac87e94639454805c3e57eff5f9af6` | test: supply guarded Claude observations in ChannelFollowUpAttachmentTests |
| 146 | `1e33e4caacae20b7dfbfb759801bfc3fb8f0d99e` | `7340a7184f75b8bb402786071de8f78501edcee7` | test: supply guarded Claude observations in CheckCompactionCrashTests |
| 147 | `f20424ea8c1cda1730db2b35bad51bdb2b47667a` | `eb46ef8caeae6400682df24969456862e30d2c4a` | test: supply guarded Claude observations in CheckCompactionReceiptTests |
| 148 | `4019b8af509c6d0facd08bdd1f87f346447207d9` | `e62139d960b7426296702c761c6cda8587c04dcf` | test: bind Claude caller observations in Codex startup recovery worker |
| 149 | `d0a2be32c32d3a55a57048382de9b819f953f784` | `7b0a0a2ad6c8fba18a1781d38e2fd2d1e3aee77f` | test: refresh bound Claude observation when SessionMessageQueueWedgedHeadTests accepts a new generation |
| 150 | `925aeb4417052f3a3cbd4b6706ce5bad4ebb0697` | `82762788e57fb48165a25f198eb25eb0b4d5a2d0` | test: refresh bound Claude observation when SessionRuntimeActionRoutingTests accepts a new generation |
| 151 | `6cee9870342e256fcb5fa43c169b327f02c84130` | `95d835da6ca858b4d74fa472d3773f6e47f01899` | test: keep guarded input separate from legacy prompt call instrumentation |
| 152 | `e39ecf3ad271d22fff661c02c223d55c8138a806` | `6f2176864abf28186709f0133e8722b75b97fdc5` | test: asynchronously dispose specialist identity dispatcher fixtures |
| 153 | `90d56983a8ed3777843fbe52a4ed51e66015750b` | `bff858eaf517eb0ea4f5f435eb79b9191236fc95` | test: render current Claude composers in wedged-head recovery fixtures |
| 154 | `6a804adba99a85bcd4c2e707a9f4e49f1cf9a092` | `56a45c6ee9a75d599f2d7262f04926c01c4f6bb4` | test: register Claude launch fixtures before dialog admission |
| 155 | `a9c4fbdda0b740adf975f8e573d4c2394dc2b5f7` | `778eef5010fc6f5bc7daeecfe089a9e427d277e5` | test: scope automatic Claude runtime wiring to card launches |
| 156 | `a80223f48040f1e139e8e9d3601f9736182ccdac` | `55602a78a5cd04355711a4f0ae4787c2838a8cdf` | test: retire overdue watchdog fan-out fixture after its assertions |
| 157 | `ef11b610c59db83b0e1b3ba4ad9cb9528ff9cae7` | `0d5045ef69d36d97c37a2e2b34a8ae2d7bbf5399` | test: use protected private output in land verifier fixture |
| 158 | `5d81e0e41ab7018027eb1ba5ea0df4a5fc92142a` | `e3cc36ca062be0f2cb11a3329815d62ec5ede2a6` | test: reproduce Windows land audit diagnostic path mismatch |
| 159 | `0a86570b63d243eceec13db84584eebee948bad0` | `64c008d7322ef7327ccc5f45532bd336f6c91dd4` | test: compare land graph diagnostic paths across platforms |
| 160 | `1ff01861fe5fcb0afdf6ae1cadb62e0639719c79` | `0ea0779b447b577f3f6b3281ce33b4377121ca98` | test: isolate output distillation delivery stores and model transport sizes |
| 161 | `c2730a654657801ee5ec54469df9a0f1ff7272ac` | `d18a2ef804833703aa60df9616e6a742aed2a4e9` | test: publish pending settlement busy transitions through runtime ingestion |
| 162 | `f8c8e77e5de1bf07c637db15ff9e5d226b1df915` | `5274d1aa93ba55ee113c3e99d78fe4b72a006942` | test: reproduce backend restart input on the native Linux shell |
| 163 | `02062c80799c5979d8d89ea92c27f39fa467341f` | `7b9afd2dd1add938e8a0c10ce8aa5c9b31722038` | test: import transcript event DTO for runtime receipt transitions |
| 164 | `37bbb32ba03010c6180d4be82ea8587a6e318190` | `b1ddd82e2d020f04ef4fb250f38466de31a86c20` | test: persist raw runner session and register its database across backend restart |
| 165 | `7efaac040c1a755f5dcbd779a7eaf82e10ef0372` | `20d5495926c76c57514a74562e9fbddc52e51e84` | test: expose missing distillation admission before waiting for specialist work |
| 166 | `7934665f144feae56f3432c54b7d8c4a40fcca79` | `6c963d22de4878bfbdd8e640f59ae443ec96e703` | test: retain source eligibility and settlement path in admission diagnostics |
| 167 | `2cc2617ead0e0a71543b11f27a9dddea076a7beb` | `12126561642d009c0ce731e2554c527671fd8a7f` | test: model standalone report directories without a repository binding |
| 168 | `563d191e4f8c2023aa645d628ef7f94d041262eb` | `d15180b69698cc2b85fd6c588194cf845fe89035` | test: bind replacement routing recipient before positive receipt |
| 169 | `1bf027f049b8bca8bea1830d12e81fa349d38046` | `83151942b3c0d33ac11873295654da34bee20ced` | test: wire committed transcript state in channel redelivery witness |
| 170 | `dbcfcf40f82e9952f7fcc967216b62e68cf281b0` | `8ec483c02f0d3a7d7595999018d113fbabd41538` | test: assert liveness refusals for created and terminal Codex sessions |
| 171 | `97440b158ba03f046900b23874c8a9bdfe70cd36` | `5a4c3540a1445d2a73a183c8b7dcf626e34a0eca` | test: provide current Claude observation to retention delivery fixtures |
| 172 | `bd9ad34f8e387d161e10ea1344296c766be0321f` | `1606dc3b28a141224185981e898f19d6f73078a7` | test: bind check-sweep composer fixtures to guarded Claude sessions |
| 173 | `40b1dbf0a1c1f2ac26f5d4595b2ae09daf8c8953` | `9050aad704a02456e4b120dafc43163e361bcd97` | test: execute durable late-confirm action after channel transcript replay |
| 174 | `053cb1831794ecb4affa5fe6b1ea6ea95c50595a` | `dcb74a2eba619b13b37b0fed212a0005e225d5a9` | test: reproduce Windows separator mismatch in settlement registration assertion |
| 175 | `15edfdfbd9a84b024846e67c74987f3d072415cf` | `a76c799b8ce45e6fe9264cfe0ddfd3ce3251f792` | test: compare settlement registration paths using Git separators |
| 176 | `7b220614231b217fa37a7e87f19bd864d700000c` | `171f6c5ceb8a120c2bd93cfe19fc3c7c3b2e2f43` | test: publish compaction caller turn end to loaded session state |
| 177 | `99bd60f861eab6ffdcb98d5a4a006719ff4d97bc` | `27aecd6886aeb2fbc1726f9137ff44f1fc2b2203` | test: launch label follow flows with supported Herdr Grok fixture |
| 178 | `e050c706feea4fe3d3dfe26d8c8f87bab9a9456e` | `3bf96c9377c2f51a0f837f0d0a73a26c7c4fdc84` | test: publish land refusal caller transitions to session state |
| 179 | `a8df8962f651e2a529a45d2d19885b1680d087d9` | `1f567f3138973e8fca4eb8c7ee84111b9396120f` | test: inject receipt queue crash after durable transaction commit |
| 180 | `d601bf00bbafb17414d13b77fc5ba34e5d33fdc7` | `65b323d86ca56414fbc0da0fd3fc415ef204ea1c` | test: bind review loop fake to the persisted Claude generation |
| 181 | `06fe13ab3eea16a4f3a3defdbca8156399802009` | `531dcb89a663207071ba449b0f93db055f960944` | test: provide guarded Claude composer in unified outbound transport |
| 182 | `3e474a6b346776b5a499b39ab50ad6ca267a540c` | `1565464f1538d2fcd468fdeb6256fe263fc4135e` | test: drain durable review dispatch actions in offline loop fixture |
| 183 | `828f17ac2fc3d69707b56331c74ec81006428872` | `ba5d30ae466d65d423ec34ca886df6880210abef` | test: reconcile outbound busy-turn completion with the working-state store |
| 184 | `385fdae499d34be40cc1f0df8c473a9f9027fb09` | `1673cf66f14518ebe1e66f3ab0c77b23901cad01` | test: read parked-answer spill from persisted recipient cwd |
| 185 | `c2e3ea4eac47d13f90609855e527d3022033517d` | `d01981ae98a427cd0a7e0ae255704ccfb39b2011` | test: diagnose parked-answer spill location across restart |
| 186 | `2b3573d128add6407b8b6fd6bdb999b2c572659c` | `47487f38dd84941e99853887ddbe757fcb98d59d` | test: preserve fake runner spill storage across server restart |
| 187 | `5815d4bfc327dad34be46fe604bc44aa6d16c49c` | `e26a0d6e2cdab01897fc78217c64bb2db360fbe5` | test: bind parked-answer recipients to current Claude observation |
| 188 | `7d9e54d3142297896243970af9d3d8175af61b2c` | `0c181e59da9de41d9426860e3956b2e742115b9d` | test: support parked-answer recovery recipients and ordered probe cleanup |
| 189 | `828566675051cbe91e0c6b08d77171e8091c47b2` | `e170ed0267cc0e2d75ceadade0fa1d0ccb3ecebe` | test: model guarded Claude terminal over phone-home outage peer |
| 190 | `44db1470b8d023a0c0bf0f0c96e87737c5c2fd3d` | `a01e8ff777641e612f11efc6b2bb4fab78428c69` | test: opt recovered delivery scenarios into guarded phone-home peer |
| 191 | `b5786424503f9d4da91a0c2a23d07022cfb26aef` | `038bc25450cfb7349d3f3ad6aecdd392e2a596de` | test: drain deferred mid-turn refinement effects before assertions |
| 192 | `4a5b56c6565328c8b54cfa150141b996e170484e` | `04dae38234fa7fad8aa0b645033baeaf86aa43bc` | test: execute deferred queue recovery after receipt transcript pull |
| 193 | `37a930f62eb35f291898d73734c5b219ccd8fc36` | `8b88b6669cf988b06b9125ff014a46114d4d6ef8` | test: bind seat recipients to Claude dialog generation and use owned sync branch |
| 194 | `3c3c94aee74840d1a9d74c1f690e0c5d1e9ff3b1` | `2ddb71cc46f33f3ea915dc53b865d1351752c692` | test: supply supported Claude dialog before remote-control work delivery |
| 195 | `d9081c87110bbc04253799f42525e357c006d582` | `16e5e926e595fe65224236c55f5b5e3982c7e558` | test: rendezvous with owned Herdr launch before injecting terminal state |
| 196 | `c6dbe8eb298adb19c6c71472d9fca309bf218626` | `d6d02e74ec7a7db22edcf57ff1937fee58fc99ad` | test: expose launched remote-control adapter to the runtime modal gate |
| 197 | `e106dd21bf9bfde81f562ad25657fefab898a2e7` | `f177dd4414ad214a3e7cf5a5d6cbafce2befcf94` | test: model expectation delivery generation and viewport transitions |
| 198 | `1444d1775393f9e8ff24e92408489cb15421c18b` | `693b163fe2909939128a615aaf1ce09df044998e` | test: check predecessor landing pins before terminal retention |
| 199 | `db0cd3fbe6cf49bec22439f7ba57768869261b26` | `0b9ab0b1f724ac5cb126870ee365fe088fc93c7a` | test: isolate legacy landing writer migration from current EF schema |
| 200 | `5d05f4bf78ceecc19763b142a0e0179d8d18c917` | `6d0d9da49489ce37a7cda8cb9b0e403a28c22baf` | test: register transcript state services in standing boot watchdog fixture |
| 201 | `de6c8438b368306e96435cebd899494caf128eb4` | `d04713bf20c892cefe63ecef80c16e4f411caec8` | test: keep transcript index migration witnesses on their historical schema |
| 202 | `42f540c7471dde570b481d9aea28fa084c05381b` | `5fb41d730d8065cd6b7953e1762597de1b0b88f3` | test: await sibling inspection before continuation delivery dispatch |
| 203 | `67860d9e35ccaac745f1be37923420c0d2214f56` | `af0829f430e250537270a8cfa22e3f0806bc1a3e` | test: bind rolling runner branches and model guarded Claude input |
| 204 | `7801af7f87f917158ff0d9dbd7677ae58be2e2bb` | `6ad12c8e9e31d03e488cc535e190db8fabfc1211` | test: make mutation slot fixtures explicitly shared |
| 205 | `6da34ab276b35e34bad708ab27e3531870749390` | `d0d99433f500076e656e24f68692feb545f04380` | test: await sibling inspection before continuity dispatch assertions |
| 206 | `0535488450bd326cb9544e199d667b0470c69e0a` | `695bfd0252230795ecb0b2dacfb6b93eb4292c0a` | test: align blocked-seat review pin with current bundle wording |
| 207 | `bb8215a3d235bf06cff319c5730f347164ea2e9f` | `548aa277d26a6be705201771d6a4946229a90026` | test: isolate check floor provisioning and assert the standing contract |
| 208 | `8000bfd6687d94b796ed9c5bbbebe03554fdcbff` | `64252719466f432ef3d6615806eb3d4cb28a867d` | test: distinguish land observation cleanup from target advancement |
| 209 | `93eeb7c420d18d6806eb1f437a0a3a1e357b1208` | `814ed6394ff364055cc9b18f6db1d21be28933c9` | test: preserve source approval ref invariants across retention cleanup |
| 210 | `4f555fddee6705a6115d78f1a712ad882abf40b0` | `b8bb23b2cc9e2b10f495052de667937320f98a5c` | test: await bundle harness disposal and align stage wording |
| 211 | `87a5846fffb31f329262d9e85013721e23148949` | `888fdf486ce78fd1fa36918f5bfd7dfb56f1b5f8` | test: bind standing queue race adapter to dialog generation |
| 212 | `ff0992619073339339b4d079e3a1b769712edf1d` | `d543ae29b16c9ef46afc16aec415daa0021a4b43` | test: seed launch effort for reusable continuation pool seat |
| 213 | `0e9270368a3ed0f9ab05b41034aa92858421a9f0` | `cbec63001922990c94c79eb9e9e3a5e9edff5af5` | test: use a resolvable policy refresh fixture executable on Linux |
| 214 | `ab1108767e337b0e4955bb967133e0de098c8d48` | `c10c53d173e7554578537af27549d4bb1f5c8237` | test: account for task attempt binding in receipt transcript ingestion |
| 215 | `aacd36af65c7bf2ee9dd1aa78c2e73422aec75d4` | `dab1ca7e476d59e55f0e4aa79c5cbfcd1c5c3594` | test: supply guarded Claude observation for aged land notes |
| 216 | `9b8e18e164694fce50eab8139cba2d9c41bf6b4c` | `4e03fc25b9139818cde5dfdd0f1203af50009693` | test: bind scheduled delivery fake to Claude dialog generation |
| 217 | `b4d54cdae24bf8ffb8400dcd38fd0bc261ee1451` | `dd5a33b7612c17d5f86f98f0afad2c22f149b4ac` | test: scope alarm refresh fault to the queued-message projection |
| 218 | `335631a4ba7f1ba719b3a9504d3107659d53797c` | `ac874d37da385f5a379778cf1a4dfe3fe402f39e` | test: distinguish operator and provider input in C514 recovery |
| 219 | `a84c7c2874fc629f570f4053df1f685916e7ce32` | `484ca38299577bb754320b641af7ae2c402ff1cf` | test: paint supported Claude composer in opted-in native history fixtures |
| 220 | `58801a729bc77512591633c104e189c81b78287c` | `cbd6a0111b9357a8e2100806a5ed41a11f4249d1` | test: publish C514 busy and idle transitions through the shared state helper |
| 221 | `73d735c571a72c0e49fd0b9a33adaad59b6641b0` | `ca14c3099c129adacf039b64bde6bba19d144998` | test: assert native launch completion before waiting on recipient proof |
| 222 | `084e1f1e045079b652fe42252e3ad761be142cd1` | `8ae01114a442be29da1a16dbc9895a572cf6025b` | test: redraw opted-in fake composer so cleared input leaves the screen |
| 223 | `55f71420976a6b8a77306d44c5967cfcc611455c` | `41ea8729d249adc3d73961b09a921a8411cb918c` | test: witness local queued UI spill rejection through SendNow |
| 224 | `4e445098ee2f4c5231634370ee9dde0f12bc3fd1` | `cc1a2a843fe0f986547c07600e153c0a129b131d` | fix: retain source bytes for generated queue spill pointers |
| 225 | `4aa30e85fa5a6c90912031ba75fd649b8f348b32` | `f48b9dfe79fda9684153f3d11d4002e2e49c254e` | test: seed delivery database fixtures through the migrated model |
| 226 | `08cff5720410f7596130f5fae6f6e0e7cf0a74c4` | `6d31baa7e70f374deb936483f5af7b116356757e` | test: use protected spill names in completed-card removal guards |
| 227 | `b3364fd2d64d616416c5ba5d89c68e2f6ab0bba7` | `2f656d483b01295ec010eae87aa44c704da881fd` | test: align landing removal wrappers with protected-name matrix |
| 228 | `50592a506b05bdceeea3e5a77682c2cb77c725a2` | `059b2d4fee41b999f77df9e948de2687b1535473` | test: exercise protected settings at both retirement inspections |
| 229 | `d8fa2c3f6ab79c8275525636daf44f5602e33920` | `a19ffc35ea91b6ba648a754c24cd5a1e3ece082d` | test: keep nested protected-report cleanup witness current |
| 230 | `f8976b137077f26be481bc8af2275e64a045a3d3` | `4c843762bb84ed9ff13b47bba0ceafca06fb23e3` | test: assert refused raw removal preserves git registrations |
| 231 | `546c9ae42da28ae6fb721c505d37b23d74475a77` | `e52fc251b4434a5c1a7639f7ea593cadc64fcd4e` | test: reproduce Windows separator aliases in virtual cleanup IO |
| 232 | `9b5c0e0de0f58bef6fbe980223ded0b5d6e76c62` | `69a3fe81b2292f37a55a17665ea0bc0c67e16b47` | test: use the pinned Npgsql explicit connection clone API |
| 233 | `1a8d23cf92ed982e20e98757e918da08953a2c2a` | `0ca8105de8bda4051677deeda9797c89d49411b0` | test: preserve native path identity in virtual host cleanup fixtures |
| 234 | `0bc69609aa399ee0bbb7539623efe35837e1636a` | `24bd100c67e8928814aa713cf423a9ddd010b77a` | test: drain deferred compaction recovery effects before asserting |
| 235 | `82863301ad8a86274361c2075d8b872cecbdc2f6` | `41c21e54d4a29cfe633c0e72ba51881bc580fe1d` | test: bind concurrency dispatch recipients to current Claude observations |
| 236 | `daf5094f5e81ce996b3e229612000d2b4ff3a947` | `2f058bc49b8c4b74c14063de5d8c5b67f52c889e` | test: recover outage mentions through a generation-bound Claude terminal |
| 237 | `c20adb972fe22a320e0ef6bd2a2b79b28a164579` | `4f77e2eaa129040873bcb4110e60f5a782b8bc16` | test: model guarded multi-runner recovery and join reconnect teardown |
| 238 | `2360034755b37d7915d1fad71401d8758f76fd0c` | `54fe695e11073fe47935fb7d02feef797d3e6dd4` | test: render current Codex composer and reconcile dispatch fixture activity |
| 239 | `1357a1f27c581c83496c7e093f23b4d98b981797` | `575e3a671f14d49f7d4aa661a29321da0bc06b44` | test: reconcile multi-runner idle transition before queue flush |
| 240 | `bd5100f1a99a1a380f44bbd3e9f6ccbfd017ae93` | `cf188f83b52f508d6ea12a8a81faffdf5de7bd18` | test: publish Codex idle transitions and reuse remote dispatch driver |
| 241 | `4653256fe3611ea6b57542b14ed2ec86f437b791` | `4ffc4582af110701895051de5ef41effc0a13753` | test: isolate Codex delivery recipient store identity |
| 242 | `8610b255590281fdedb85d4d88827a1f1dd91145` | `171ff5d5b5ba3859a246835c94cccd124957eb0a` | test: pin unwritten Codex pointer recovery after a spill refusal |
| 243 | `5064a3124eebb6752ca6a62824c4acd8d9210910` | `07eff2111c4c153b9de5b710bd52a30fcd2deca0` | fix: refund a Codex pointer attempt rejected before its first input |
| 244 | `f962d589e36a5e90d68e51570c4431c26a435ca2` | `3f00a8457fa8c73df29eca0aa13b57935247d2ad` | test: bind replacement specialist terminal before checking stale policy |
| 245 | `19ac17c3c71497cf455225a978c769296735143a` | `f96f9e186b8b3bb7e9053b41c1ea6558220f4857` | test: publish interrupted-attempt idle transition to the state store |
| 246 | `d5cd630d557d1c6970b2750970a11fe304145571` | `93492710c37d08956ebbc101c3620fcf94533a24` | test: reuse supported modern specialist transport and projected turns |
| 247 | `a4b947ea006255a0cd6de0e5dfc0c2afe9f9e1a9` | `b2bd69bc756272e4b5dcc4e4a904eec95828e1b2` | test: exercise capacity rerouting separately from subscription quota refusal |
| 248 | `b1e82c827669d3470c6cf92033cea8da78b94c6c` | `07159e89b4ec482b91449d2977812d3886a553a9` | test: reach repository lease hold cut through the outer land gate |
| 249 | `42c716d6818f0f39506e676eefd8f394b2d162ac` | `d9a6e75ee0cdca9c92546f27000862501f3c4adc` | test: model guarded phone-home input and observer replacement lifecycle |
| 250 | `ada6ffcc6902b0576cfd2cb8a5b1f85d74cc93f2` | `43770bdb4674da54753160efc006f21b814e3c9b` | test: preserve genuine land execution lease for verification window owner |
| 251 | `e0dfe9f13b429af302a76adae9cd6c2a72407f60` | `efaf894f79faf4ea6398ba857241a8f4eb1b6cab` | test: await failed inventory refresh processing before advancing fake time |
| 252 | `b55e1408ab8332f17ffb748ffef3066fe15db097` | `41835e690be8cb23d9483bccf9cf26172dc0935a` | test: bind refresh completion logs and expose real competing land hold witness |
| 253 | `0299582c13a9585b37e972955a13bc4871a3068e` | `ff5264a1ea29119a202ac56406c914f5c82b6828` | test: retain refusal for unrelated holds during landing verification |
| 254 | `2ec2ecd544d9affdfe99faf745256eff0e919638` | `60302d18c98ed35a3d5b470e25f9fd9e39802b72` | fix: preserve active landing verification after a competing execution hold |
| 255 | `c5ab17338632422905b4c3029b79abe6c0dd1100` | `e21a58b6accbc3e952a23340146c23b8fdeef43a` | test: assert Herdr Claude unknown-custody refusal |

## Skipped documentation-only commits

All 26 rows below were omitted because they add or amend narrative/investigation evidence rather than an executable prerequisite. Mixed commits retain their original documentation changes. The three explicitly retained document-history prerequisites are in the ordered list.

- `9ff5679940701b5e0ff34183ce04f918e456d648` — docs: retain guarded Claude sweep results and incomplete verification handoff
- `03f9c664c73a27fbfd800dc1e4322b1ab4b724d8` — docs: record reduced Claude fixture verification and land handoff
- `ca250de0b48ca222cacdbaa3a3793ac0993c521f` — docs: attribute Claude fixture sweep failures at base
- `e970c2c9ff2f723dd1cccf130e61901eeca5f200` — docs: record group 26 fixture repairs and native rerun handoff
- `c2e35da7c1ae24792b1f115c7f47996f7463f625` — docs: record group 25 residue witness and green Linux verification
- `ec5123c39db2a03e4b51a2abaebc74db17c32d9b` — docs: report group 27 land fixture fixes and native follow-up
- `e0893861ee15129ed280f800c2b3d915f25e5a8f` — docs: record group 28 fixture repairs and native rerun handoff
- `0e64a6aba1b4e28052faebfac88f01232b4cb66b` — docs: record group 29 debug fixes and six-class verification
- `be55a999b1a8de651eaf85e642257eef356287cc` — docs: record group 31b fixture repairs and verification
- `5b6a1f9cb14d79b7169a19ba1a6fc2ebb5270d82` — docs: record group 28b diagnosis and native rerun evidence
- `a28f1faa9dfcdee08b28f3d60c6fe530592194c4` — docs: record group 31a fixture diagnosis and Linux verification
- `dc1a2184526085b8f4305878a3804f9cec41daad` — docs: record group 32 fixture repairs and scoped Linux verification
- `a7895d00b29dab8833559c4cd46552c7375a67d2` — docs: record group 33b fixture repairs and scoped verification
- `1c937a4273c166d7c099cd5e3277f62087c1d8e0` — docs: record group 34a fixture repairs and Linux verification
- `263aa2c6cf7193ee87ce6cdfeadad4632e73cb83` — docs: record group 35b Linux fixes and native rerun obligations
- `256213bf8bca96c9b6adf344741bc8f864ca491d` — docs: record group 37 fixture repairs and scoped Linux evidence
- `1603a6f18c5614e07c04e9a0b7841ec8c4798582` — docs: retain group 35a base failures and native coverage limits
- `acb6f2e60fca4806b7c3c2150c2abdeaee931269` — docs: report group 35a fixture repairs and remaining Herdr contract failures
- `9376a1daacff7b26d2c2788466efca8d9dac0b11` — docs: record group 34b spill defect and red-green verification
- `a640c8614c6358839c125326049c9f08e174bc1a` — docs(CARD-1574): investigate orchestrator fresh-restart gap
- `fda635c6641fccc84c97dd299aeae205f14eec88` — docs(CARD-1574): plan resume receipt readiness and verification
- `785d6a636ecddd685dea59b613260c2edfc00581` — docs: document rc qualification run-group repair loop
- `d1c95c65210e30ce1657f0ffc68e2132173b6f6d` — docs: investigate Herdr custody failures and record spill defect
- `0d51344da28bcb5dea4f37a86c7434bc4c606da4` — docs: record group 65 fixture fixes and Linux verification
- `f0c1c658a708b0f0afc932021033288ba13d9eef` — docs: report group 45 fixture repairs and Codex pre-input refund verification
- `a0b51f75fc93b768c6b3bfce883e3378660c53c9` — docs: report group 38 repairs and real-lease landing red-green evidence

## Conflict and verification results

- Cherry-pick conflicts: none. Manual resolutions, source repairs, branch switches, rebases, resets and force pushes: none. Pushed history was never rewritten.
- The first merge preview after the final fetch conflicted in `HerdrAlwaysOnChannelParityTests.StandingRecovery.cs`: master had just landed optional CARD-1575 after the initial selection snapshot. Adding that requested landed commit as pick 255 resolved the preview conflict without a manual edit. Final preview is clean.
- All 255 original-versus-picked binary diffs are byte-for-byte identical; no cherry-pick patch was rewritten.
- One solution build at e21a58b6accbc3e952a23340146c23b8fdeef43a: exit 0, 0 errors, 1252 warnings; compiler elapsed 00:02:55.17. Slot granted after 195 seconds, maxcpucount=6, released after 176 seconds. The warnings include a transient MSB3026 file-copy retry that succeeded; no manual retry or second build was run.
- Build command: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-integration-7ba35e25 -- dotnet build Antiphon.sln --property:OutputPath=bin-7ba35e25-rc/ --nologo`.
- Build log (ignored, not committed): `/work/worktrees/task-7ba35e25/.antiphon/task-7ba35e25-build.log`.
- Tests executed by this task: 0. No sweep, native probe or qualification was run; the next full qualification remains the gate.
- Removed all 49 producer-owned bin-7ba35e25-rc output directories after build exit; zero remain. Tracked source stayed clean throughout the build and cleanup.
- Final source merge preview after git fetch origin master: clean against c5ab17338632422905b4c3029b79abe6c0dd1100, resulting preview tree 204628023e23d3d7ebf7cfd2d534d2fd9a5879c5. No merge was performed.
- Evidence history audit of base..built source passed: commits=255, entries=0, violations=0. The final report-only tip is audited again after its push; the caller-facing report carries that outcome.
- Protected remote rc readback: a97194ee7a4adf215454fd82c453ba46362596ee refs/heads/rc/2026-10-24 (unchanged).

## Publication commands for the orchestrator after Review

DO NOT execute while the existing qualification of `a97194ee7a4adf215454fd82c453ba46362596ee` is still active. It must settle first. Obtain a clean exact-tip Review and the fresh native Windows candidate probe required by `docs/release-gates.md` → Startup-path rc picks. Startup-route binding and host logger lifetime changed in this candidate. This Linux build does not satisfy that native gate. If the reviewed branch or old rc tip changed, stop and commission a new integration/review; do not rebase or force-push this one.

The following PowerShell commands fast-forward only the rc branch; they create no tag and run no release script. Run in the approved owning repository after the publication gates above. Verify `$candidate` equals the full source SHA named by Review before the push.

```powershell
$ErrorActionPreference = 'Stop'
git fetch origin refs/heads/feat/card-task-7ba35e25:refs/remotes/origin/feat/card-task-7ba35e25 refs/heads/rc/2026-10-24:refs/remotes/origin/rc/2026-10-24
if ($LASTEXITCODE -ne 0) { throw 'fetch failed' }
$candidate = (git rev-parse refs/remotes/origin/feat/card-task-7ba35e25).Trim()
if ($LASTEXITCODE -ne 0) { throw 'candidate resolution failed' }
$old = 'a97194ee7a4adf215454fd82c453ba46362596ee'
$remoteRc = @(git ls-remote origin refs/heads/rc/2026-10-24)
if ($LASTEXITCODE -ne 0 -or $remoteRc.Count -ne 1 -or ($remoteRc[0] -split '\s+')[0] -ne $old) { throw 'rc changed; stop for reintegration' }
git merge-base --is-ancestor $old $candidate
if ($LASTEXITCODE -ne 0) { throw 'candidate does not fast-forward protected rc' }
git push origin "${candidate}:refs/heads/rc/2026-10-24"
if ($LASTEXITCODE -ne 0) { throw 'rc push failed; read remote before any retry' }
$published = @(git ls-remote origin refs/heads/rc/2026-10-24)
if ($LASTEXITCODE -ne 0 -or $published.Count -ne 1 -or ($published[0] -split '\s+')[0] -ne $candidate) { throw 'rc publication not confirmed' }
```

After publication, repeat the native probe and fresh-process gated `dotnet restore Antiphon.AppHost` rehearsal under the owner policy, then run a new pinned full Windows qualification. Keep publication, qualification and activation separate. No activation is authorized by this preparation report.

--- next stage ---
next: review
handoff: Review the exact pushed feat/card-task-7ba35e25 tip, ordered pick coverage and startup/native publication gates; do not publish while the existing qualification is active.
artifact: .antiphon/task-7ba35e25.md
