# Master port of rc top-ups 38 and 39

Branch `feat/card-task-7f6736bc`. Base and `origin/master` stayed `98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`. No rebase. `origin/rc/2026-10-27` is `cde8def4163829aa5b867df6ec625433be2522cb`.

## Outcome

Cherry-picked the 100 content commits in `8edda999623a473be2d1970727c172bfb5088d59..cde8def4163829aa5b867df6ec625433be2522cb` that `git cherry -v origin/master origin/rc/2026-10-27` marks `+`. Every pick applied. There were no conflicts. The tip before this report is the land-graph approval `f81b12b969de3839f972e1d735969cef7d6244ee`. `git merge-tree --write-tree origin/master HEAD` at that commit exited 0 and wrote `f89844008c26a146cca92f861cb3b590e3de5311`, equal to `HEAD^{tree}`.

## Skipped

Already on master (`git cherry` `-`):

- `f9ae0ff18bd5c5cf7c34aae0de5fd035fbdbd6cc` calibrate Windows land canary copy budget
- `d36465d6901235cd12db9de3aea49c2450636357` witness repeated immutable project reads
- `a3e69a9c265b0e605a191c429e529cd08513e298` read immutable project blobs once
- `f1cced78eb80ce004b9dff98063b8b296d73550f` witness repeated invalid shared project input reads
- `4eeca781cd4fb07ad359094cc2597ce991903222` reuse invalid immutable XML results
- `ade54222543381e2e1b31ad79d8ae33a4035f70b` skip C478_V14 job-object breakaway on Linux

Report-only (only `.antiphon/task-00ae50cf.md`):

- `59deddd30a4173562b0e0ba476fdc497ea9de1a4` record Windows scope A skip witnesses
- `97756db4ee6a0fc769c218c24ac2b35a9354e2ca` drop the scope A report-only cherry-pick

Land-graph regeneration on rc, not replayed (regenerated once on this tree instead):

- `952c22e93d9caa7441c995296c7590f045c621ce` regenerate the land-graph approval for rc top-up 38. Its message records old digest `20DE49F6` count 66 and new digest `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99` count 68.

Kept as content, as instructed: `130ea525aa1f25230123ee3584ea58dbf0604b95` and `cde8def4163829aa5b867df6ec625433be2522cb`.

The range has no commit whose subject or body names Windows skips B. The Windows-skip content in the range is the four scope A commits below.

## Conflicts

None. Expected paths (`.antiphon/task-af2b78c8.md`, `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`, `scripts/fixtures/c1008-fake-docker.sh`, `CapacityRecoveryService.cs`, `CapacityLockOrderTests.cs`, `tests/test-execution-policy.json`, `scripts/test-rc-platform.ps1`, both test csproj files) did not conflict. Git auto-merged, without a conflict marker:

- `docs/testing-and-build.md` in `78f8dde03` (`35313d64d`)
- `docs/orchestration-loop.md` and `docs/testing-and-build.md` in `3d81f4a9a` (`46b8a062a`)
- `docs/orchestration-loop.md` and `docs/testing-and-build.md` in `8da9b8c18` (`b87b705f2`)

`40ba9807a` (`2ab686616`) reintroduced `_PtyHostNativeProps`. `cde8def41` (`a1784a82c`) removed it and left the allow-listed `_PtyHostNativeDir` with inline Restore/Build Properties. The tip contains no `_PtyHostNativeProps`.

## Policy hash

`Read-NightlyExecutionPolicy` accepted the tree. Hash `4bf17609dbf771a840e6af9f64b343ecd86c0439f8f4ca6dc3f40351c5cec148`, schema 4. That is the hash the slice B policy commit already wrote. No separate hash commit.

## Land graph

`-Check` on the picked tree failed only because the recorded digest did not match.

- Old: `20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6` count 66, pins blob `749d65387b4e9ae7f84fba8c7b6f42658b430dc7`, SDK `10.0.403`
- Live, and the one `-Update`: `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99` count 68, pins blob `d7513d002e3b9cd3ed95faf82ee26d037c9604a1`, SDK `10.0.403`

Approval commit `f81b12b969de3839f972e1d735969cef7d6244ee`. The following `-Check` printed `approved` and exited 0. Allow-list was not widened.

## Tests

Slot builds, `UseAppHost=false`, `OutputPath=bin-port38/`, then `dotnet exec` of that DLL. Verified source `f81b12b969de3839f972e1d735969cef7d6244ee`.

| Project | Result |
|---|---|
| Antiphon.Tests | 0 errors, 1387 warnings, slot waited 0s, held 160s |
| Antiphon.PtyHost.Tests | 0 errors, 1 warning, slot waited 0s, held 13s (compile proof) |
| Antiphon.SessionRunner.Tests | 0 errors, 45 warnings, slot waited 0s, held 22s |

Antiphon.Tests filter `/*/*/(RcFullWatchdogTests*)|(ReleaseGatePolicyTests*)|(RcRosterRepairTests*)|(WindowsUnitPlatformAdmissionTests*)|(C1514LandingSplitBudgetTests*)|(ReleaseGatePlatformTests*)/*`: total 27, passed 26, failed 0, skipped 1. Duration 4m 10s.

| Class | Passed | Failed | Skipped |
|---|---:|---:|---:|
| RcFullWatchdogTests | 4 | 0 | 0 |
| ReleaseGatePolicyTests | 6 | 0 | 0 |
| RcRosterRepairTests | 3 | 0 | 0 |
| WindowsUnitPlatformAdmissionTests | 4 | 0 | 1 |
| C1514LandingSplitBudgetTests | 5 | 0 | 0 |
| ReleaseGatePlatformTests | 4 | 0 | 0 |

The skip is `C1210_Windows_linux_only_outcomes_are_exact`: CARD-1210 child skip-roster audit requires Windows. Linux cases in that class passed. `C1323_PlatformPolicy` passed. Census methods `C1404_Group10MeasuredBudget`, `C1577_Group71MeasuredBudget`, `C1584_E2EExpandedBudget`, `C1410_MeasuredCostGroups`, and `G2865_Group28Group65MeasuredBudget` passed.

`RunnerCustodyReleaseTests`: 7 passed, 0 failed, 0 skipped.

`CapacityRecoveryService` and `CapacityLockOrderTests` were not touched, so those classes were not run.

## Picked commits

`rc` SHA, then this branch's SHA, then subject.

- `df8bb765bc6a65ba07c6c22892bf05d2d11df8da` `59221b4dca0d6fe625b748c94f73cf3d98d075f3` docs: record the CARD-1613 pinned Linux skip reflection plan
- `7eec288adcb6c4075e326852f1fc4baeeb7c81b9` `c77deb93d3800a9d112c843babcf8bc3a4fa6886` test: add the pinned NativeLinuxOnly witness before annotation
- `bd59c1b6c5f2dc160bb513ad3ae55fae6b3c14f7` `e1b5adb27e6b1ed149d63269a7ed0eddaaa8e473` test: pin NativeLinuxOnly on the 45 Linux-only rows
- `78f8dde03cbb69f05d01666f03eff9cade0447de` `35313d64d4c167b96598dd8a1101e19e64904cf4` test: retire the Linux-only source lexer
- `a3fa5eb51d563e4f85ea28398bf9f5da06f880d9` `4bcdb2f31e108ca46605e4549bef4e7972e37213` test: import ProcessSpawnLimit in the matcher parity parent
- `9e91659d35339dc5d586bf28c8e2baa578d87870` `dd5bbd217d41e8e3c0699616fa8f661065c83dfb` test: require inherited-hide and inherited-builder witnesses
- `a854616a0188b6470b13b55c5ae454729f877e93` `0dd4680f0de9b4a4a0a19bc62e2432fdcb0d9d94` fix: keep inherited tests by MethodInfo and walk base members
- `354cdf1a2c60c7fb0859d4aa0b321bf86ac44271` `d3092f03db1bc43c0258165c60f25c446be80a4c` test: require the Windows probe skip reasons
- `197982adb2fced25cc4cdaaedad67e055b0432ce` `1f06925afbaa4296c328f819ba221ceeb11f716b` test: require frozen rows to keep their platform and marker
- `925352ae49c5c050a69122195142344f35d8c8ae` `cdf8c145f13f9675603e40f5f567f54208fce510` fix: witness every frozen row, even if its reason or platform changes
- `b8f7fbeb54d025d1901e9142faaa3a3d6aa4444f` `8d5f326baa507f5181dc29b3daee18fed4b103b8` fix: compare Windows probe skip reasons
- `8c60ee2595d57545b98cb683d34304130568b53e` `fa748a3a6c20417e30f9a4980be378fec4a992db` fix: keep frozen-row stubs out of the lane class scan
- `9401cd607cd60dfd0a2331833a0dac8c19e66ca0` `fa1650c94109756cfd8bb8f03382f0296023b335` test: repair native Windows probe and checkpoint stamp checks
- `4b51f26ef2b6ccd1a7ee6b9369976f2adc9a0430` `cdc95e7f2e17583f1d91a97ed08f43b1290e50eb` fix: pin the native Linux fixture target and admit its text fixtures
- `f72ae2c5e9efb099f79d6ea85156c98a7db96f6d` `99239d4042accafd012487d5f4844d0db3d01e7e` test: expect unwitnessed suites and duplicate policy keys to fail
- `a381bcb628af047b18ae389905e68517143b521b` `1a6a9a9e6cdac45d514e437702d524843b0f9116` fix: refuse unwitnessed Linux-only rows and duplicate policy keys
- `65c5b16f707222c2456fe81886d6037c23fb0a8e` `1175d2e08f46542d4770147045773a918583bb0c` test: record native Windows CARD-1613 red witnesses
- `6970cb6bf6f149c3cd50ac90df3fc6da855f6dd0` `fd2edc0aebcbddb39c8fac7892760bfc205b23d4` perf: avoid link-target handles for ordinary audit paths
- `a4a6bb39c4222f274fbd9263570d9461807d3089` `e5e8b1e7f53399f93075e1fc9ccd95500e038f86` perf: batch independent Windows canary copies around link barriers
- `63820871ffcf114ed90285f9f7d59972c6a92500` `b717034df0b18eba10a53e847073041ef00200e7` test: preserve C1483 duplicate-copy red witness
- `2418ef6b969033b55554b6f2915dd99a68d30dfb` `f7b9cd794e0893977d5d7269227ffaeb48b27243` fix: deduplicate Windows canary documentation paths
- `b02284854a29ed74278d73ab3b546b37650885dd` `a7a14ca4a009f4898834f8823d4d74dc7e935fa5` fix: extract the canary entry set and the copy-race seam
- `815bfced80f97e42c97d4fed765a51ce3826ac31` `09fe9ae44231d8df7493e621004b09f598192d86` test: witness canary link guards and the ordinal entry set
- `93a859d24092e6a27d50d7ce6454cd84ba42ecc6` `a4324c191a20937c1f73d8a4589385c79cc7b28a` docs(card-1614): import the reviewed Linux custody repair plan
- `019d8002df6d32669f6f13a3f858ab835163fa68` `dfef0ee1c3a10ac980b9756914cad121e14ed1d8` test(card-1614): witness missing owned pty-host layout
- `96d56898a28056e49f751099f7b693a73795b3c4` `148960e175d9360996d84010de086fc724a6e500` test(card-1614): witness identity, namespace, and admission
- `5ceb61ecf53b3d6389721131993981beacb86e5e` `846ab147b0c061404ba58bf6d4e92004b3d4d36c` fix(card-1614): keep sudo inside the cgroup and split helper locals
- `5ee70db1c2c2d6f999566f351bcc252288d443cc` `6898ab6f72eb2e4100c1f2176b08f49d249d097d` fix(card-1614): isolate custody trees and keep Linux unadvertised
- `3d81f4a9aaa5d6bc9ac0a4c6dd9d32ec3ccd62c8` `46b8a062a3915195e4eae70bb8354c1c4a0f3feb` fix(card-1614): stage the Linux payload and record cleanup ownership
- `fabfbd7b42ed10a37b32f187e523d719e97abecb` `7e0ed16348e83c4f879798b11b87c7c48beaac5b` test(card-1614): accept a probed Linux ledger on Linux
- `b2b7ac23cdeacd0c73624511bb0295ef863b8324` `235badca904ca2ad3c854a9940bf23f4130ef014` test(card-1614): witness a read-only namespace marker aborting boot
- `69fce59bb96c8b8119b8b4f875df5b8cb9ee8763` `4d7a0933ae47bc2d4bb17fcbec6ab89dbafac6dc` fix(card-1614): keep namespace boot failure from stopping the runner
- `49dcbc7583f9c26ae23f925c1407b736e7e81903` `35d4b961b1648e916550cdd7f0947401ae7047fd` test(card-1614): witness the custody reader ignoring the namespace
- `33e4e6b0a2a14089cb73053dfbc0e5b04980f42f` `9fa472e3db0fcef55b75586a418035dfb1b0b530` fix(card-1614): read custody trees through the namespace
- `30a41b6f7bfba98eb28138efb47ea93925190e56` `5e0f3faf4544d8981e6bea7a618e189a5589aa26` test(card-1614): witness an unfrozen kill when freeze fails
- `71803cd171732e031a0f0b0dc59161f1bcba4ec2` `c9142959b7ae88e1669cef805b4b2a8be3978c4f` fix(card-1614): refuse custody kill when the freezer is missing or fails
- `7666f49abe296e58b415b9b15b70e5fbf3d84170` `751de8f569af851f330df7216aad928e289f3481` fix(card-1614): drop the namespace boot fail_step stub
- `833ee0afa5a77c5d0d91bc5d05452892932ad289` `3a62c37ab4cc69562ee77ec4cf4236873bbf13b6` test(card-1614): witness population and drain by content, not size
- `ea8e9b02abb50805c38df78749a47daf2d20e517` `2ca4d816281674d290b0c77e56b0d1fff9ef5e7e` fix(card-1614): probe two namespaces on a private cgroup root
- `af5c0ed3337629ab123a627311f19cffe1b559a8` `7c8490624b823458fb9f7c241a6a0b0c675c035e` test(card-1614): witness the entrypoint library guard
- `a5e07a7785c6c8717b68f75e4c094c196065edfe` `9f8ebacc6095b4eee5ee573c40b01c70e3cb4ae3` test(card-1614): witness disallowed land-audit properties in owned apphost targets
- `724d5a4e0b46030443fb7365fac0336622c97e7f` `d8b6cd0ae8e14ad901827f2a603b41b6657840cc` fix(card-1614): inline the owned apphost MSBuild properties
- `cf382c0347ab1abc9622650319e1a67bb518310b` `63f9a0d06698fa64d7a346a7a78dcee8ea04fee3` fix(card-1614): pin the rewritten OS-conditioned apphost targets
- `db414e7dc72035cfa41e86e22cb150583adbb866` `c2897b15c2d911040f861e0554f57dd8b8c87fab` docs: carry the reviewed CARD-1514 rc speed-up plan and timing census
- `dd45dc721b3832bc074bbdd6c262598d5930432e` `4dda5a3769f97ffa7b33e0916f9fbac1949c2525` test: fail rc timing contracts until instrumentation persists
- `791c4cf1b6433644c86213a31748e10444bd5814` `48f31c81e0a1b96b44aa6ca3d599cee0308e7b4f` feat: persist rc timing observations without changing verdicts
- `79fc1f8784fc547a40c7db007eb8bc71cf76a6f8` `b5eedfd25d98d59964c1d5b9a84ba0a0c6790fe3` test: fail rc timing contracts until production paths persist
- `07e321e5091ec159bff556368a76dc12f4bd23c7` `a9a9be27dc63e3eb2cbb7743d07494ee95acd560` feat: persist rc timing observations without changing verdicts
- `9b0012dfa5341427fd2d34f40dda5791a38a282a` `f91195e363a9a3a9bac69fd95deb21755517e814` test: declare pending Windows OS witnesses for rc timing
- `803b7d772d60a0079452b11ef76aa75594b9413d` `671d74225e20a2f62c593d80160cfd1828f522df` test: pin Windows descendant image after process exit
- `ac0d38f889607d09f87be9cc200cfaad58e70314` `834bb75f3f7542edb8f91db368afdbae8bd0bf2d` fix: retain native descendant image before Windows exit
- `3f95b703a12aef33033c4362d2efbb96211a21ee` `a400d92d0ecc9da36e5f947705b39d26197e4142` test: fail when each summary shard gets its own re-read budget
- `38b2618ca5d7709ca5e22e98d60b8769cfc727be` `7f242badeeceb1f4f8399c4f5d1f6aec68621efc` fix: share one summary re-read budget across shards
- `208db0c8068dffd804de4ff4674e5c06e7e3f4a9` `7346365be2843af11318ce48383796d1099a037e` feat: drop unwired A4 and bind discovery and setup call sites
- `4840f1f361f09589d912fa16c99e84499b80ab16` `7fa3e42ff15b7991d21a88b0b545436f0b932948` docs: defer A4 and correct the S1 round-3 claims
- `71e853e23531d16ac93fa85febc49f5725e96873` `64a73c2fd6792bb21ce1112e5240254f5c85554c` fix: call the Windows process owner from a live witness
- `a21108959506006da8d19e1449be4b1a38228b6c` `95fd99a2df2d0cb7d17cdb051beb4f60de24054f` test: fail the counts witness when late or orphan evidence is lost
- `e6384b1169f746e0e0a79132a1ca2a92121647d4` `9fb3d0f0c8dcd3d86f8dab7ec51ec3e43d9d96bc` test: fail when planning, supplemental discovery, and owner exit are unwitnessed
- `273962fa19dec665cf71794a7a0e2d36d8a55acf` `dd9b5852c8878dc06b72d24b2f790e9932052c42` fix: witness supplemental discovery, planning phases, and owner exit
- `665dfb9f4ee5c975b874751e120545e04550e844` `d6e1bf2ed5c69b3bc5950a6ecb7435237c3d82e0` test: fail when setup phase stamps lose UTC
- `d1dbd3a986b73cf90c34aae0684b76c7eb83e84a` `dfdaf8473a293b85ebaddfb6a88c6341cbb34553` test: witness the rc planning success path and owner-exit AST
- `7a534464167fe6a6b54fd4ecbe3a06f8bb2e2392` `c07c8aecc1e35883667e29dc690bd969a9405ce6` fix: keep setup phase stamps in UTC when rows are rewritten
- `5ddf05697f0d10b6b486044576ad7d21998bb723` `5469810ff6e668e5120cf53c6e4be77bada95fa0` docs: anchor the S1 OS witness inventory at the slice base
- `e2e1db608f035a4bed482b91115d45938753bee0` `d57a2e296f3179724d91fe1bfcb38cdb33c21453` test: run scope A link fixtures natively on Windows
- `c15632f788f43333b1ab52a5bbcc078ebeb63f26` `7d2dc4e4a77c6b4fc6890b254957450d54e19dc0` test: qualify the case-sensitivity probe timeout exception
- `4a95a44969aaa00aa2f932be5a95cfc98e54da79` `b79fb5894bb9fe80174b1a5e6436bf3eaf1225b6` test: exercise unreadable runner registration with a Windows ACL
- `ddf22d0c37ece4230590e44363242d6f61463f58` `54dd6b0c482926e15de92109a6f6e5935929f1a0` test: explicitly restore the registration access-control section
- `ed17a208bbcffd023ab0e0a593a289bfba9d4eb8` `d407688527df111f635a4eae8a5373ce80aa2559` fix: close the C1105 resume extraction over called functions
- `62b7c59c6dc8ef65dcf9193f8aec5c922fe0cf95` `b2ba9e4c399c7b6ecb95c0e8513b0ec25231bb93` fix: qualify the preview helper PATH with chroot
- `130ea525aa1f25230123ee3584ea58dbf0604b95` `871de51154e9056d872ab39d0559e3bc19b0c89e` fix: pin the combined Linux-only witness after rc top-up 38
- `69cf8e177060b4eaf2f8f60008d9893504a499e8` `4c4da1353b8877c24dc83948f3eab45c4a68aa19` test: fail when the rc watchdog admission proof is absent
- `66d3c24db4f0861ed898476de3ccf99003bee954` `34f81022019a97f6c4284ecd33e4d3bfab043037` fix: pair launch watchdogs with admitted cost and record hang progress
- `faa9552e651feda9ec45bf6740a334db85a5f348` `658d88725c1c55be2532b9636d63203d5e85683b` test: fail when an unmeasured plan exceeds the suite watchdog
- `65d984f981b933cde0ee1ac0c43c4650866d6355` `c729537f7e876fddc2d136a775db019d6afbd8aa` test: fail when a measured error factor is below one
- `f9c7a145877240300e87e30fed49e9092a4cdaf3` `803c2237d50890f25c3cf6a81db4710b86acead4` test: fail when an unparseable wall is dropped from calibration
- `eaf5f5dc18d9473b806e26a978a725d9d6d0f101` `8f282a4501e43f18e0d086e21dd3930dd5e22011` fix: refuse unsafe watchdog admission and censor bad walls
- `477158f612762d2257d21f6f39251990ac86bb19` `18a6dc45e09b58f724b759de890886634cda1c3a` fix: witness launch admission and let the timeout disposition own coverage
- `8b14fdae05aadf56999897681b74c8502e41d188` `5eec47de8b03825fe8600da1a2cd9b070dbea720` test: fail when the planner admits a pty-host plan over its watchdog
- `7c41daced36b208da9e84484f671c1407061043d` `357281425a5d921671d5662e9d973583f96fe53a` test: witness the measured 600 s floor and an empty calibration
- `3cd138b9083d504a21d5ac2f2015045fea348e36` `bad2550000a12ca285c2d6441fce5d8a085721a7` test: fail when an invalid planned cost stays uncensored
- `041565b228e9f821bc914dd5a9b4d820892e89cf` `c232c137abc97013577ac14da746aaa6869250b7` fix: refuse an over-cap plan at planning time and censor an invalid planned cost
- `026cff12219dd91f3dadfc48e36a7c18d853b899` `7b2d736318adfe2499f4024c4333023ccd27f9a0` fix: compile the refused-launch witness message
- `40ba9807a88c092219375f775f654ec4d179f12b` `2ab6866161a1b112c25b4ef50a4f628e6cc9e6e6` fix(card-1614): stage the Linux payload and record cleanup ownership
- `d81d6d5574b89bd7c535a7d0102c7ee611cb64aa` `0f1eb1a5c728faf63544ac9f13552922671f7153` test(card-1614): witness Linux seal, recovery, and Windows linger
- `8da9b8c189f6b114994dd3a2150ab630f10fa1ab` `b87b705f272aec5ad397f44a2307e1e8177fe030` fix(card-1614): withhold Linux auto-seal and recover dead hosts
- `4a47f1834ca9b3f70759d370cdc5860f986ba3b2` `c31ae78f7bedf7ee21aa321d0b05ca05cff147ea` test(card-1614): witness that a prior seal does not recover on an ordinary read
- `6f2f3e6514a5437f27c11a153816c3a400dffd3e` `bbb9ad6d7c6a816ef4ee9304db4ef6ca65190c48` test(card-1614): witness that a released launch failure exits within its TTL
- `56fd46d31b137793617b35e8264ed761c0339fcf` `7398675da568e5a65e46d8e16752a2cf4cf2eae0` fix(card-1614): expose the containment test seam across the pty assembly
- `d43ea79ecc6dcbdde835cda472ff06a10aa78ff8` `69491cf174fafe9c66811728a4805d25723cbef4` test(card-1614): record the launch-failure namespace before the first write
- `13873a9e9f9dafc63a2bbc2c4a127989ba15139f` `dd1f703848f45741d00e5d6c468b41c0cebde1f4` test(card-1614): pin root-exit seals and real native release faults
- `d492bc606cf08d4b07559e0d5d9ff5c4e718b9cf` `c7cf924e23eeb45730df80c05f05e8d367e14fb3` test(card-1614): bound host teardown so a pty read cannot hide the witness
- `b7a0885667ca17c9d9423c44c5a480dbaf3951b2` `3e86bfd06a79638ef188f3346b92d0ad0618ea69` test(card-1614): hold the inbox child until the exit handler is subscribed
- `81844eb26aacb8d96ffd3bd1b7c251c9ddef0cdd` `6f20dbbcd9d83a001ce7563654ccfea3491976b8` fix(card-1614): recover only on an explicit seal and arm linger after release
- `8b13b9ffe070d7427f3fb61121b1c1a1a751d9e5` `fba1afba33b3c2fe3068a9311ee099385aaa5073` test: capture C680 native dialog setup diagnostics
- `f8656f74ac860c0d27c420d642f6623caad71a12` `e36d0fab350461dbe26801fd9cc93ffd65791939` test: use runner transcript DTO in C680 diagnostics
- `3451c52b499abb7f9b6a99f9fd66a4d58b3b515a` `558020e683aef3841a79f99a0585fa73d9def78c` test: add opt-in C680 submission and transcript timing controls
- `9648346a148b911bf7e5e6f6b30bb6e71b0fb6a0` `1dd74e9646118104435beb0c6a28d852adbc4970` docs: diagnose C680 timeout as unsubmitted question prompt
- `8fcb44c3fb4bf6550c3ee806c30a722a364cb32f` `e903c3137f84f8735ee52547fa1024d7c4818a2d` test: confirm native C680 probe submission before dialog checks
- `47e554581f0031913f863801c840f0a3fa4d2e0d` `43410ef695cd1dd918d894ffb65c58d565d0e161` docs: record native C680 repair qualification
- `cde8def4163829aa5b867df6ec625433be2522cb` `a1784a82cda7ed7d9db3dcf4cfc321a2d7c25507` fix: restore rc inline pty-host apphost properties

Series on this branch:

- CARD-1613: `59221b4dca0d6fe625b748c94f73cf3d98d075f3` through `1175d2e08f46542d4770147045773a918583bb0c` (17)
- Land-graph audit Windows repairs: `fd2edc0aebcbddb39c8fac7892760bfc205b23d4` through `09fe9ae44231d8df7493e621004b09f598192d86` (6)
- CARD-1614 slice A: `a4324c191a20937c1f73d8a4589385c79cc7b28a` through `63f9a0d06698fa64d7a346a7a78dcee8ea04fee3` (20). Slice A ends with inline `_PtyHostNativeDir` only.
- Speed-ups S1: `c2897b15c2d911040f861e0554f57dd8b8c87fab` through `5469810ff6e668e5120cf53c6e4be77bada95fa0` (20)
- Windows scope A: `d57a2e296f3179724d91fe1bfcb38cdb33c21453` through `54dd6b0c482926e15de92109a6f6e5935929f1a0` (4)
- C1105: `d407688527df111f635a4eae8a5373ce80aa2559` and `b2ba9e4c399c7b6ecb95c0e8513b0ec25231bb93`
- Combined Linux-only witness pin: `871de51154e9056d872ab39d0559e3bc19b0c89e` from `130ea525aa1f25230123ee3584ea58dbf0604b95`
- Speed-ups S3: `4c4da1353b8877c24dc83948f3eab45c4a68aa19` through `7b2d736318adfe2499f4024c4333023ccd27f9a0` (12)
- CARD-1614 slice B round 2: `2ab6866161a1b112c25b4ef50a4f628e6cc9e6e6` through `6f20dbbcd9d83a001ce7563654ccfea3491976b8` (11). The first of these restores `_PtyHostNativeProps`; the property-correction commit removes it.
- C680: `fba1afba33b3c2fe3068a9311ee099385aaa5073` through `43410ef695cd1dd918d894ffb65c58d565d0e161` (6)
- Property correction: `a1784a82cda7ed7d9db3dcf4cfc321a2d7c25507` from `cde8def4163829aa5b867df6ec625433be2522cb`
- Land-graph approval: `f81b12b969de3839f972e1d735969cef7d6244ee`
