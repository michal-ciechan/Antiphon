# rc top-up 38 blocked (task b2ae1ae9)

origin/rc/2026-10-27 stayed ade54222543381e2e1b31ad79d8ae33a4035f70b.
Product tip on feat/card-task-b2ae1ae9 is ad08468151d0ae97a25bad4230f7cd06199951f9.
Land-graph -Check was not a digest mismatch, so -Update was not run and rc was not pushed.
Tests were not started. No per-class counts.

## Approval

Recorded manifest on this tree (rc base; the series-1 regeneration commit was not carried):

- digest 20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6
- count 66
- evaluation SDK line 10.0.403
- pins blob 749d65387b4e9ae7f84fba8c7b6f42658b430dc7 (stale relative to the committed pins file; the pin commit 3cbb70648 moved pins to 8d956c09c and that line was only rewritten by the skipped regeneration)

Command, under a build slot (lease 02b2ef41-591b-4909-9c25-42dd90a22968, waited 0s, maxcpucount 6, held 6s):

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label land-graph-check -- pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check`

Exit 1. stdout/stderr:

```
definition file sets a property the audit does not allow; -Update cannot approve it: tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj _PtyHostNativeProps
audit refused: land graph pre-check refused
```

No `digest=` line. `Assert-AllowedProperties` exits during the definition walk, before the pre-check prints a digest (`scripts/audit-land-verification-precheck.ps1`). `_PtyHostNativeDir` is allow-listed. `_PtyHostNativeProps` is not. The same property is also set in `tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj`. The walk sorts full `mode blob path` rows, so the SessionRunner blob (8b23fd3f) is reported before the PtyHost blob (cf16c162). Both targets match slice A commit e7cb2c750; the auto-merge did not rewrite them.

Series 1's own approval was E117DDECAC45785183AEF8BBE6818DB4DB85A6EC89B9EAD065C1047BF0EDD49B count 68 (commit 2e18b8af, not carried). Slice A changes two definition blobs that were already in that graph, so E117DDEC cannot describe this tree:

- tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj: 0f6058ac blob b4ed9068c94a6c7cb229418edf2394a89935010e -> HEAD 8b23fd3f206354413319a52bddcd48fd1b3345f7
- tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj: 0f6058ac blob b14c4bfe64d67e1f8f80c6465f924f2075d987ec -> HEAD cf16c16230b0e4fc20944d436d4438f0d654dfbd

Slice A adds no definition path. A graph that accepted the property would keep count 68 and change the digest. This tree was not accepted, and no replacement digest was printed.

## Conflicts

Content conflict, resolved in 578c5e995d5c0095e585137e54628c4ca3a272cd (from e7cb2c750):

- tests/test-execution-policy.json: only `policyHash`. CARD-1613 hash bf703bb14839c0e93c6f3516b2d5dd69288eea73063c36e02396af9fbcde61f7 versus slice A hash 4bf8e2d990a29851a0c116be9cbbbfd7350ec02a94c49636b213f682d1275305. Both bodies were kept (CARD-1613 NativeLinuxPolicy.Probe child row, and the three CARD-1614 exclusion rows for HostCustodyTests, LinuxRunnerCustodyLifecycleTests, CustodyNamespaceBootstrapTests). Recomputed with Get-NightlyPolicyHash after dot-sourcing nightly-common.ps1 and nightly-policy.ps1: 1e5507472b335df2d7c5609063a7ce6f1ebae2ec08f59cb0b9ff1c467150f47e. Read-NightlyExecutionPolicy accepted it.

Auto-merges that kept both sides (incoming added lines present; CARD-1613 pins still present):

- tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj (CARD-1613 NativeLinux embeds + slice A CopyOwnedPtyHostAppHost)
- tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj (same embeds + slice A rewrite of CopyOwnedPtyHostAppHostForHerdr)
- docs/testing-and-build.md
- scripts/test-rc-platform.ps1
- tests/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests.cs (several picks)
- tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs (CARD-1613 NativeLinuxOnly markers + Windows skips A junction/symlink arms)

No conflict in CheckpointOwnedOutputTests, CheckpointScriptReaderDeadlineTests, CompletedCardWorkspaceRemovalTests, or RunnerWorkspaceBindingTests. Skips A edited CompletedCardWorkspaceInspectionRepairTests, not CompletedCardWorkspaceRemovalTests.

## Skipped regeneration

- 2e18b8af559414bda1a494ced63b69c7c93ad99f fix: regenerate the land-graph approval after the fixture pin

No other commit in these series has a subject that regenerates the land-graph approval.

## Skipped report-only and review-only commits

CARD-1613: 1147f708910b07658336dcf6cce64e2a6d6659db, 2b764cecb1ea12a2c10251522f8635f904a9457e, b11c0ff5977412ecb6b4ac4665c9078e21b30164, d84020507f42cf1ad8a1dfedcc1822967d4982a0, 0f6058ac9a39080531afe01f5230e0f56e1d2fba.

CARD-1614 slice A: 23dba6dd2970201b99bcc25aa7b68e8c35667c59, b79f91944ddeb78fcbbafa4430f895098f7ab7be, 9d106004d7ff999302024d865f8c679931d63ae1, bec2818735a720e450ecd5e5afe3604f9e6fdde0, 7a2cd795590c1836ca213c3894cf17fab780e7d6. 2615c8986d75d5b91a5516c5cf7365f9d8270c7c was picked; it also changes tests.

S1: 9e485d2d1bf398025e473842bf63ec4d4f88785d, d2ec4e6fbf19f28576ffd6b3aba12e7c44eb4272, 6394a4e53e36fabe95878b4c9e4b4e739d5a5784, d12ccc80c3d75b9bc5d235a3dc44dcc9f12faf9d, 43a6441a9d635155d5f4092bd95d100ec833800e, 9f9a5aaa73c8dc80b57420ff6b129d33684a6088, db282ed7a7827feb38af67887a45f3399d999a1c, cbdf5f76bf4b14845f1e15fd8961065ace0c14c6.

Windows skips A: a25bc3ba33fca4d0c07fe2a0c9623db865e846c4, eb77637fc2d1a5f8b49fec982845a48ca0fa055b.

Windows skips B: 4515e5ea51a7cf0b54b46a4d893be6f1b576fcaa.

Not picked, per the brief: speed-ups S3, CARD-1614 slice B, the C1105 fixture fix.

## Picked commits (new SHA from old SHA)

Base ade54222543381e2e1b31ad79d8ae33a4035f70b.

CARD-1613:

- e3bd11388e1ce9b41531e3265c050dc7f8eea047 from 1e6e306ac6699fb8df47960d4a289e0ac158fe37 plan
- 416318bcab63fd12965c52936a48b7019750b396 from 5f018bc7bc731dc1c07fe2be19e0b8b6b8172f86
- eef480d3eb01fcab700f39b7bc3fe401ac0530fa from 4809ad45191363868f5038ee8cb125e39b5d1ace
- eab5a75ed7806f32c29741a0177c9c88aeee9758 from d62cb17d5a32dfc712ad1e911f94cd47a38ddf38
- bb073feca896d43b18bac7a25b302444307d238e from aa72491b72b1af57141e7c37ac50a384485755a0
- 308cdcc20abf58a7868bbccb23266cb82f8bd6a7 from eaedaa4ce9edef7678ce08a880ef54869361539f
- d624751ab28842185b0497925d36768fc68bc1c2 from 9da2c500b7bf4434b5f62e2bc9097a05b9a61a03
- e153342f114835844865beefb309725078687efe from f3cc905667a4eb7b954ed44462f8f3751df7422b
- c0543576ca32432847e83a829ccbf3ae391fc4a0 from a44ffa6b8b649118dfa76977782404adc8accdf5
- 27b11d74a6c801820152b7117668479d06b92309 from 0e310a8ad77f8bb5d1fd0b21d030cd8640b9194c
- 4308f21dadf2c674a9c2db9b1d06f7a31cf2249c from 47e476c86568130923f8cad5953a9a4fa8f2b580
- bfab8fa9ed7010f056377ed645a9f88eb3a37865 from fa8452462e3f64134ea21cbd2984ea6b4027d645
- dd20460eb0e9d840df4d6acecbf662c702e4342b from 1ab9fa2fadb6211e4a155032942bb927ba0e9ffb
- 878f2c8c0c5e9672213ef4fb5bcff421417bd696 from 3cbb70648151c4f13014c0968015c5ca89418f23
- ff2826006857e89d9bfcbf87be79aef7fa202700 from a96dc222be6257177901ef405794f437287fbb86
- a9fff5328bb31a312b63c8dacc26352aadb6bd37 from 2d0273e3fcf8750ce454dd93c980fb5dece3c59e

Land-graph audit repairs:

- 8419d6fc10ac02a05438f22ce36f25cb9e431110 from 677ef8c7ba066fb112ed565c3e6afb3878360e6c
- 9aadcf71887714f6873bb49896c85d8ee3757a83 from 41387492a50792324a13a1a2d036efe4c193315e
- 19f12ee58fe95be01b8245087270b98bf3aa18df from f389a3d61e57d11905b39de6d0cdf4faf46d3150
- 0fbd82c708b518888e32e041c5cc801bf56ef9f9 from ba8869e0f0d1abb2252a5c788ac6beefea9d2c97
- e2e17878bf13c598b1c5b9e267f97154ec03cee8 from 3c487de7f6c4a59d7685c1d5f9645f6625e3f6b7
- d3a445ffaec92e47eb4a4677f7677e8da0077782 from 0a72a2667bc89e0390a021d62e7912281b05de40
- efe26178698a89ebb0fe25cb924c3f99a4cb03b9 from c39286a1b49d39db90e9c6bc5b4b2700c5066056

CARD-1614 slice A:

- fbe362131d7401b09d71a61fbef0e654d9d55309 from dcb2d70e2fc613ffbdef617fa16a385235548806 plan
- f8723404bcfeeff1ad0046a0115f542b67293014 from be1d5df93a0bd68fca484ccd9d54cb332e05fcd5
- f18bffbc1fb30b9b46ae55c047758426dbc994f5 from aa13042c760bdf5bd01f1beb4edead31aa50676d
- 34254997035f404ea757ae730adfbf650e37337a from 238601a0c7b4f07e47576d2b6958517051235d4c
- 95dd3d028575cba5e3bf9b1162328e80673a68ee from a26c1b21a9c55e86e24b2c961395f443f977d17d
- 578c5e995d5c0095e585137e54628c4ca3a272cd from e7cb2c7500e1130cfc53e6b7ea4ac737e946808e (policy hash resolved)
- d247c29a5d26e1d60dd4302189e10e557f3b41a1 from 99a17f1050866393cf6c9c8dbc3b865c850ad039
- 04d223aa34f37e62f78474685dcf63fd2e8acdcd from 778786e75eaafc4a9cf4251854a94a67a30e3527
- 328af714ec49e028f80791ed361c3cc23dcad6c9 from a1a4a7960e2fc8fa8c8580757cd3937da1a4fa10
- 035adf96b80418300a531be64bcc27a64a257f03 from 357a990b12c17f1ee737aefd589528687561053b
- 5edb9c3df53ee60339f9223dcea2b4b3511b5526 from 025f3ba733236035338bc09d6b7be7d3e8f33543
- 3c3ef456b79b9c20bf1551f0cc4f98313d7e01f1 from 38e97ecb3157e6194d84857caf71bf30a961f5a6
- 4cf6ca0cf8f4d6795e5da28c2c385b63acae365d from 208c8ca516806c102bdfefa62a4d8c553beeea41
- 41c760950ab70a4a53e5708063bc817169aae1c6 from 05c541de416405fb4640b6d5b74b4f5678bfaa18
- 330bfc07a87f84e6519c5136c00b238b4425fd7c from d024eabbfa40d72dae9314b133f7b169134b9ee1
- 58ed5eec2488f4f3059cf8ca4667847711164afe from 02c37328af231f223a7043ee84fd59e29ee09b28
- 6f8e3ccfb974b890022ae24528742b43f6a53ad6 from 2615c8986d75d5b91a5516c5cf7365f9d8270c7c

Speed-ups S1:

- 3d9e86b369542e1939c5f741408bf70570c6d13d from c4001e86cc41c72d88d6f578758cf842f3ebd07e plan
- d1542eefc91dcfa2218aa0071e93836e064d88a9 from 7e9db99dd3748356b5bc5b47b7e3e7f3ce0aa2d3
- 35cf4f5f92e387b8f63d2ba97da0bab12be5983e from 440b282af70d31b9d08163ec76d9e2bd6fb0b1b1
- cdebb2b9484e6cf07b309ba720f944604cea3b3f from 77c53cb04787b3e94f618954294895485b94fde0
- 6584d7633a9e837b9b65f14a896ad828073ff12c from eef03be7d50151a2ef14aa10b1514af5fc2cbd53
- a6929b040cb750441be3d6df80e95a7a55c50244 from 5bda525ca411fe4f0c108dface94f3a16c361fae
- a669ae10d19ef24c1c9563499779848c5ded04ec from 63f0b85299d918f6527b98774fb10e233a5bad44
- e15e56467f676fe73f1b6c66eda0c5a000e30753 from 3e8c8ca1899649e8054e00147412d83866cb3f8f
- 7136967ea9fff9481b08ff7eba32e22d821da0a4 from 4a1b675a3d8b2322208e2fcf31934b8b2ac6ae3b
- a0ee7b3eec798607e12b98564cb53bf921189881 from 6ae4c942608ff7fe1a392ff33dbbd39fd9093207
- 947ab2c28e49d4f3b4e98294282557968dc21091 from faea17c7aed5ef4596c5d5e58fab1101a062dc1c
- b0cb98cf139b1cfa5a2a60fba35d4cded8982ca9 from b2eb632dec21d4886ba24b7ff1c9e382f8eaf897
- d5e45ed852d621f5d7028266a6718006f4374c10 from ac4bebc112272f04300c344615b2cedc05af9344
- 719a93df584df2ceb33c77401dfb020e0ce8aae5 from e885398117c035156d33d5011584538c7adb1ac5
- d46e1812a80d96ffbcc65b36c5763c1547bbcb96 from 6867b995c203fa72a3da4e08dd64176e2f44d5c0
- 05211442a1f43322a822efb15898e87df3711c94 from ffa12a9c177c97d53e8d1b672d5d66ac93e98574
- 4cad14121fe2f194c7d8673694270d517fb2dc58 from e253106eb5336801a822cce41c5f6b3f3f7a28ce
- 313ce175d7d21c533a18541afb7612291e09fc64 from 27462c3272c18006ccbe82e6ba17add0457d0d9a
- 759b48277fdba73b3698f838395fb095e52bd7ce from f7b9d231ec1dfccd19f08bb3eac31d80e8b35ebc
- d5d3081150f7efd15166ffb224f42982412f4909 from 5599dedda8d468df473f530c5938a749c3078e59

Windows skips A:

- 9006f923046e4ce891bf3561a31059e5cd400e24 from 87727bd2167b71620e9bf937c457efb06307d3b4
- 07a263d4e749c552bb1b2418f39fec67ce27e3e2 from 745e4b6c1ef2abae9a9f49cc2ed0ca7892ce3cd9

Windows skips B:

- 48e5b9216fd86f111d9d378ef60d6d3ed60fe669 from 0a2b5bf404493281cae0eefd3f6b7c4279198e8f
- ad08468151d0ae97a25bad4230f7cd06199951f9 from 94b93f85cfe6e52fd75e141ed11b4ec91b3903b7
