# Merge batch 241: report (task 8f001b6d)

Subject 1 (CARD-1940 residual diagnostics) landed on master. master moved d100f8fa2c679a5c74b6b235087308776a8f5d50 -> 8ca2ca57fca282be239a6d4326499b8106eba514 as a fast-forward; ls-remote confirms master = feat/card-task-8f001b6d = 8ca2ca57f.
Subject 2 (CARD-1901 round 2) is HELD: land-graph -Check refused a new property on the combined tree. Nothing of subject 2 was pushed.

## Subject 1: CARD-1940 residual (Debug 9248daa5), LANDED

The branch tip had moved to 95a55eed13308806b4b19aad2440d12babd5cb51, one commit later than the brief's 37a2da267. That commit is docs only ("accept negative result and hand diagnostics to merge", investigation note +17/-5). I used the tip.

Diff check: only tests/Antiphon.Tests/Application/VerificationRoundDeliveryTests.cs (+2: one C1940_NOTE Console.WriteLine for row "regenerated", which reads the note object already loaded) and tests/Antiphon.Tests/TestHelpers/C544DeliveryRig.cs (+21: an ILoggerProvider that prints C1940_STORE for the AgentReportStore category), plus docs/investigations/2026-11-15-card-1940-residual.md. No production file, no assertion change, no timeout or wait change. CARD-1940 stays OPEN.

| original | master |
|---|---|
| 4a1029688e7f32678dc14fb90828aa9bd5f42d04 | ad5f600e2ef1549872fa1d0c1ba099fe06b31309 |
| 89b66cb02243efb4444fea344b1fa9be45c8aa33 | e77d93c7e8fa6a3c5e8701c8b67b3711c7963fd3 |
| 37a2da267546f82621f130c5fa12c38e4ac32053 | bf8ce6ea61109ee6be052342a84a39c3bbd5083e |
| 95a55eed13308806b4b19aad2440d12babd5cb51 | 8ca2ca57fca282be239a6d4326499b8106eba514 |

All four were clean `cherry-pick -x` picks; nothing was skipped. The regen printed "unchanged", so there is no regen commit. Pins are unchanged and computed equals stored: policyHash b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d, PinnedInputDigest 26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e, LegacyExclusionDigest 6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a (95 records). land-graph -Check approved with digest 0091BC0F4C3F0814B5349FB50429E3B725FFCBB8942A2ADC73F099B5CCD724F8, count 80. The release graph on 8ca2ca57f gave classes=2130 paths=4489; the artifact was deleted.

Verification on the subject-1 tree (Antiphon.Tests incremental build, UseAppHost=false, 0 errors; load 2.6-18):
- VerificationRoundDeliveryTests (C544), run from /work/worktrees: 21/21, 3m11s. C1940_STORE and C1940_NOTE lines printed, e.g. `C1940_NOTE state=Confirmed ... rendering=True`.
- VerificationRoundDeliveryTests, run from the /tmp/merge241-c544 checkout: 21/21, 3m36s. I removed that checkout by its exact path.
- DerivedImpactIndexTests alone: 27/27, 2m38s.
- ImpactedClassesSelectorTests alone: 50/50, 4m09s.
- 11-class guard union: 115 total, 114 passed, 1 skipped (C1210_Windows_linux_only_outcomes_are_exact, the expected Windows skip), 0 failed, 3m27s. C1210_Roster_closure_matches_source_gates passed. C1729 reported VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3.

## Subject 2: CARD-1901 round 2 retirement audit (chain c6da537d tip 7ac976b59), HELD

### Why it is held
On the combined tree, land-graph -Check refused:
`definition file sets a property the audit does not allow; -Update cannot approve it: tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj _PtyHostNativeArtifactsArgs`

The new property comes from branch commit 43f1aac1585bbaf8f6ca67670a7555e945342b7e "fix(test-build): confine checkpoint native host intermediates". It adds a `_PtyHostNativeArtifactsArgs` PropertyGroup property that forwards UseArtifactsOutput/ArtifactsPath to the CARD-1664 child PtyHost build. Master has no such property. Per the brief, a property refusal STOPs the subject; I did not run the refresh. Precedent: memory CARD-1790. Suggested repair, as a separate Code/Debug task on the branch: inline the conditional arguments into the Exec (for example, two Exec elements with Conditions on `'$(UseArtifactsOutput)'`) instead of adding a new property name, then re-run -Check and refresh only if the remaining refusal is a manifest mismatch.

### Second defect found (fixed on the combined tree only, not landed)
Master 50d2dfdd124ebc340cad4d5c58acd3ef8a07d7b4 ("roster the two CARD-1609 selector-fixture native-Linux gates") and the branch's roster repair both add NativeSourceOnly entries for VolumeRetirementContentTests:
- Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent
- Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof

The two additions are at different lines, so git auto-merges both copies. C1210_Roster_closure_matches_source_gates then fails with `c1210-duplicate-roster` (expected 239 distinct, was 237). The fix is to keep master's pair and drop the branch's pair at the top of NativeSourceOnly. Master's Results=8 matches the eight [Arguments] cases; the branch's pair has Results=1. With that fix, the guard union passed 114 + 1 expected skip. The next Merge must repeat this resolution.

### Diff check (done before picking)
Production files: scripts/c1008-content-audit.sh, scripts/c1008-host-proof.ps1, scripts/c1008-retirement.sh, scripts/c1609-host-proof.ps1, scripts/c590-real.ps1 (the C1609 retirement coordinator control-request renewal), scripts/c590-remote.sh (the c1008 audit body: v2 salvage refs, the publication-pending exit 6, and c1609_effect_publication_fresh before Ready and before every temp `docker volume rm`), scripts/fixtures/c1609-wrapper-v2-cases.mjs, scripts/test-live-lane-run.ps1 (pin only) and docs/docker-stack.md. scripts/retire-runner-volumes.ps1 and scripts/deploy-server2.ps1 are untouched. The only file outside the area is the test csproj above.

Test diffs are method splits: C1364 post-removal into resume/main/temp, RSC receipt into drift/recreated, C1105 census into _binding/_landing, Legacy rolling into four per-mode methods, and the wrapper renewal into its own method. The removed v2 "a 404/DNS/timeout still admits" and "apply reuses the same proof" assertions are the intended behavior change: an offline v2 scan alone now exits 6/Unknown and needs live completion. RetiredTempContainerScriptTests replaces an unused clockStepMs knob with the supported C727_TEST_POLL_MS=25. No timeout is widened.

### Combined-tree picks (local only; tip 68e8ad6f8b21ea10ba9a8c6285cec609485b7f22, never pushed)
There were 83 commits from `git cherry origin/master origin/feat/card-task-c6da537d`, all "+" (none were already on master). None were .antiphon-report-only.
- 71 picked cleanly.
- 1 needed a policy union: 9917511daa. I kept master's C0824RefOnlyNativeTests, PtyDeliveryCeilingsTests and HostTopConsumerTests rows and added the branch's method-level VolumeRetirementPublicationTests and VolumeRetirementWrapperTests rows. All 197 branch rows and all master rows are present.
- 3 took ours for TestTiming.cs, the live-lane digest or the impact index: 2e2bd948a2, 0c18a10593, a4d488d194. Master already lists each of the three new host-load labels exactly once.
- 8 became empty after resolution and were skipped: the repin-only commits 3dd96dd75d, 4d49dd0cc8, 5e64da428d, 6a14d77f9c, 8572fc4195, 270a728b2b, 62b0cada70 and the index-only fb3951c9a6.
- Every non-conflicted file is byte-identical to `git merge-tree origin/master <tip>`.

Combined-tree re-pins (own commit 3a76c630b):
- policyHash b6101387... -> a9c07d3cc95a1a2ba29d53680a66a6fd6a420598851f5c9719306227ceabf1c5. This equals the Review's scratch union hash.
- PinnedInputDigest 26d90df0... -> 8399dbfc36a28effaf5ceabbb0b54513ca22617fc626b78fe3c68cd3fc76af7c. Neither 26d90df0 nor 710bd747 survives.
- LegacyExclusionDigest recomputed UNCHANGED: 6d6ff646..., 95 records. The tool reproduced master's pin on master's policy. The new branch rows are all "Linux-only by construction", so they are not legacy rows.

The regen went in as its own commit (60533e8bc). The second run printed "unchanged". The diff was Antiphon.sln 323->324, DelegateScriptRunner.cs 128/103 -> 129/104, and a VolumeRetirementPublicationTests edge.

### Combined-tree verification
All rows ran serially, each under 10 minutes, at load 1-28. The sln build used -m:1 and UseAppHost=false: 4m53s, 0 errors. The roster fix needed an Antiphon.Tests rebuild: 3m25s, 0 errors.
- VolumeRetirementApplyGateTests: 13/13, 5m34s.
- VolumeRetirementWrapperTests/Retire_temp_real_wrapper_renews_each_effect: 1/1, 4m00s. The other 6 wrapper methods were not run (the brief said "at least" this one).
- VolumeRetirementPublicationTests: all 33 methods in 9 method-filter rows, 33/33, 0 failed. That includes the 8 corpus methods that Debug c6da537d left unrun.
  - P1 (4) 2m50s
  - P2 (3) 4m20s
  - P3 (3) 3m51s
  - P4 (2) 3m32s
  - P5 (5) 2m22s
  - P6 (5) 2m52s
  - P7 corpus 1901/1902 + second halves (4) 3m07s
  - P8 corpus 1903/1904 + second halves (4) 3m49s
  - P9 (3) 46s
- RemoteScriptContractTests: all 229 test methods in 24 serial partitions, 515 tests, 515 passed, 0 failed, 0 skipped, about 72 minutes. A coverage script checked that every method falls in exactly one partition with no prefix collisions. Partitions R01-R24 with test counts:
  - R01 16, R02 8, R03 8, R04 78, R05 27, R06 8
  - R07 1 (5m31s), R08 2, R09 1 (4m50s), R10 23, R11 146, R12 36
  - R13 1, R14 1, R15 1, R16 1 (5m29s)
  - R17 8, R18 11, R19 16
  - R20 4, R21 5, R22 5 (census partitions)
  - R23 49, R24 59
- VerificationRoundDeliveryTests: 21/21 from the worktree (3m00s) and 21/21 from /tmp/merge241-c544 (3m32s).
- ImpactedClassesSelectorTests: 50/50.
- DerivedImpactIndexTests: 27/27.
- 11-class guard union, first run: 1 failure, the C1210 roster duplicate described above. After the fix: 114 passed + 1 expected skip; C1729 reported VIOLATIONS 0.
- land-graph -Check: REFUSED (property). This is the blocker.

### Per-commit table (subject 2, combined tree; local only, NOT on master)
| # | original | combined-tree pick (local only, NOT landed) | resolution |
|---|---|---|---|
| 1 | 054df8b34cb4c0cef6ba298c0d4e6f74bb22ab50 | 8602c1bb70abceb3b24a3163e70d17cb69aaa649 | clean |
| 2 | 9917511daa12dce2bbc9031341a6468ea9ebdf05 | 31c17289caae1c54330e31387396e326c39f9c25 | resolved-union |
| 3 | 2e2bd948a2ca6ab6277b0c9b68c8e8398d3eeda9 | 1734c8d90292ae339da02eb39bcf50f711935c1b | resolved-ours |
| 4 | bcaa09021963dccfabfbc11c397b9c07ec59b408 | 14ccb863e9c117bb7a99471f0a5f636bcbbd83bf | clean |
| 5 | 8c135c593813d804a2cb1e61fca80e4ab94bc595 | 0cd6fa64c24397b5883e54ddd0cd5ce3fe01766b | clean |
| 6 | 664b302f40aa7e832e32ff763b0a2ecde4625248 | e6189708477f8bbdb8aa13b98f2467a12c6dec5a | clean |
| 7 | 04f91efc2fd735752bdce016926a91bbe7f69ff5 | 703943c58458af70aea87a6eb886e9d32a4d0980 | clean |
| 8 | bced86fa233cc1e77d52d7f01427d33d8c12b48e | 29a698f82d602c9954be4d401db8ee06a40f188e | clean |
| 9 | dc463eb6a8a420607318ae610a09673d53f16acd | 153d1eb34b955ad3189f3ec69074d9173b04fd9a | clean |
| 10 | 2e48fb9877ee3cd2840e422ed680faa1a5e3dfef | f635794cadb929e4e7eee6ecf7a12b5fc23364e0 | clean |
| 11 | 1691093006696db2a5e755de51647783543e2574 | 74bed154d763f5a9965865afb4dd0d8dd7f67587 | clean |
| 12 | e8f5213368b2e3e0811525c08a04f0b4f1a2953b | ae0b3c05556903aae53cb94f70bb41ec42482d42 | clean |
| 13 | 55e6e4605ad7c7b4b7b190d03802a15af12977f3 | 5d683a4a742afc5ac58116b541ac070bfeca6b92 | clean |
| 14 | 400080b3a1309574f1b2d49d8fbe55863779ff84 | 148f246bd753e257ed22f832816c681f914febb9 | clean |
| 15 | fa0aaa08930028369fe8581cd9e26910b7b8ddc4 | ce89e0f9960f5bcec3900c7ca12962d4e2d784a4 | clean |
| 16 | d34763abab2e842427bfa0db80a4654c0b26064d | b532fd2a88051d64918039f22f52585c1da689c3 | clean |
| 17 | 355d5b0b5610dee640ee4816f3fffe45dc2c518f | ae462515ccd5b6fc0eea49889bc5e04e1f1d7a01 | clean |
| 18 | 0e5fba4de749801c11c7429b9ebed3dbfc15a455 | 487bd7e4fa266ef5a38759b7cc62a49117231a1d | clean |
| 19 | f34e6ddd09a86eea455cd80553ea055ae10474ee | 313d97dc618323d31c2921f401b14f46f52a5590 | clean |
| 20 | 1f717f442c39366bb3266b268f840dc51d13d6b3 | 456989dbb35340891321a4a63815a9e84ef8913d | clean |
| 21 | 40eb81095c0be98165bd1927b9053322cd44d738 | 0abda9698db5e8f2dcf826e0136c107cd1e92265 | clean |
| 22 | 137fde4adc58f98b4d59b5d945b3380e9b1a5749 | af039a29bf9aa467c5332f6b4608691b03a3ea3a | clean |
| 23 | 05e34c2396dd969085de00951b537d45bef96a0b | 3584258255b4d0079b1ba7967ba82100e94188e4 | clean |
| 24 | 3dd96dd75df0b59b1d248628497c6c6596634985 | - | empty-after-resolve |
| 25 | e00873d9c4a26b6314a49e19ca808c9a33d9996b | 42378e174337bb185fcd10b32f066795b7373b33 | clean |
| 26 | e5cd8a803b22525b16c31afd3d3d8c5528a48977 | 7eaa90607bf9539ec5b2c747e6d4773b98acc3b4 | clean |
| 27 | 4d49dd0cc89ed5827448ffc639571c0f401072a8 | - | empty-after-resolve |
| 28 | ca09e5018d4042a7e42995fb815f976c0e2f0620 | 2c90536e4854d772108f6f1e53d68da433f933d1 | clean |
| 29 | 5e64da428de8f4cb5f1c365279bde658cae94810 | - | empty-after-resolve |
| 30 | 46a118b6e51560959ec51564f708b70844da1c53 | 7647138b88e02362dff1354efffb6af0a7843fa4 | clean |
| 31 | 453017dbd5c81214d4eec3484fd451c62db89307 | 780c12e0195aa0ef31c4861270919f554863532d | clean |
| 32 | 213b4bff3bfd7bdfc604bb539d4ab4524f023050 | 02d6a7e337e5713bad85eb4a294dcd2635fd140d | clean |
| 33 | 5d74e29a05a53f5d565c3270d90b1e1673718533 | 5c6c6bc5770d6ff6731886b62bdb6e5e9aeb0a98 | clean |
| 34 | 9c4a8102c45d3c033df0bdce7cf7e37ed55c3adc | 912cd3598bd83044caf74220b8ca0431e9127bf0 | clean |
| 35 | 15ccf47dcd26acbf4fcd354db2624cae660caca5 | 8996bb0777584a27857e9c979b7fbe9e1892ac49 | clean |
| 36 | 0c18a10593b7eda3f24db43d2f48c5e712b1272b | ae7f35d2b40c8838061a666521f85a74302b0bdc | resolved-ours |
| 37 | cb635dbdca92194b42546c2f8bc8d9806d7dbbb6 | c86b450f586ba3396b1a3d8551080be8af63c047 | clean |
| 38 | d2fb783aa0dcd577f38f24137bb9602796310ee3 | a51a1875c02daba1ef413e7fab80ed4f272f25f3 | clean |
| 39 | d431ecdfe1d953131c88d0915fe04f4f47e7de38 | 5c97a138ffd573eb49f9f87e9f1cfab9a2df500e | clean |
| 40 | 6a14d77f9cf917e892a84bd5089d527a2406b68e | - | empty-after-resolve |
| 41 | 2745b5b09087dc9a77f9057cff51e9b9c743469b | 3e3953499e1b83c6595d805a9e7397c2e1ec885c | clean |
| 42 | bf13300a1f3276f443c6991db152ae647f2bf60c | 8dd4cdcbf7735797776291fe038e37a7500bb2bb | clean |
| 43 | 12bb6fd5c16f3d067c5b5c7d05811a223a9c633d | 89fca4f5b55276c32ba7165b2331036617f84ce5 | clean |
| 44 | cead11e754c1586a83a1ec614bdc21e7ea6f7723 | 30667579edbbebd028ccb02ced32078723ed437c | clean |
| 45 | e946718512752e7915689894e1fa894f3479ff5e | 78fa7b9e78beb4ab37d0da1a661e59e729658895 | clean |
| 46 | a397298190a99881d98dfb9abf593ab9d9fc75ca | 7de61ddcf970a8fb16a11a94de9ca85519e46d82 | clean |
| 47 | fb3951c9a6a82aa11eb6b721075496268375c244 | - | empty-after-resolve |
| 48 | 4da5f97076b6cb48268afad270d77e41ba2741ab | c9957ba9a985081c4ba9b000908e3e4826a957da | clean |
| 49 | 8572fc419515f26561e98f0917a4b1dfb473ccb8 | - | empty-after-resolve |
| 50 | 1ce2c38c3c3a26a6275de29a8daa6af1f33667dc | 3ae87ff5b9ff2f4b178d89adbfb4fa8cac9f2ab6 | clean |
| 51 | 789fecc6029fd493ca941cf34902b20f544cdf4f | d844554d383e9a2c365aa88bbebc2262458a58a0 | clean |
| 52 | 83840716c5cbd06e9857f53f70772ecb503ae82f | adae6b0c508225c4db17d8fa58b6394e68fb5cdb | clean |
| 53 | 6f2fdfcc9010ba903ec7405213e33c9960cad35d | aae1d04559ed052747671f3ce851121dbea50c96 | clean |
| 54 | d15478c568d5bdb42b22c9c413e2567264f6696d | 7c9a5296b2d664f8107f544b66c78e55e2ae3205 | clean |
| 55 | 7d87426eebb3a6d34121c53f5004fb62bada0346 | 7879212f3021856c7cf66b9fb517e4bff0c74fbc | clean |
| 56 | 024f239fabf3ba9a5955607f4326ea72dc32c202 | 293f3ad2f3af6117139e2c85dc7f873375833816 | clean |
| 57 | a4d488d1949bc629103809b59334c84ed90a5c51 | ade0dbca9611959dd4abdd82b3c6251ae0ced40e | resolved-ours |
| 58 | f721ba501f41ba461aa145e5dfc318d3ba18c9a7 | 8724b3d161eeba9c6a91655c683d79d8087fbf1f | clean |
| 59 | 4a8109dbfd5d29627a87e97adada8df73beec11a | e59a6590221d412080d7e0265a64a9d4b1678d12 | clean |
| 60 | 02889d811d5a0e0523d4c563ed539d25ac1abae8 | 0563f2ba16309bd936205806406793216a576c35 | clean |
| 61 | 8dc381da535bb4f26242eae846bb8ca8f9da0a14 | 61fa896a84a48dfc22caf27856871e57c9caa4c4 | clean |
| 62 | 7c785e0d988467ed1a75af7015ff2070a502d98e | 1731de7a777050d0694b335ad81e0bc1f4145ac2 | clean |
| 63 | 7df685eddfd848ce9f2627d1b17460e79fb67217 | f85a21cff7145a489e439dfab3113a30b2a7ac6d | clean |
| 64 | f399989c97a198a22cdf52196958056b22915b51 | 472c7251d6570f44fd6a9915538a272aa5e4eaba | clean |
| 65 | 1ec95b93f15c8d612e2c1d8afd22c04746a397e0 | 24eb430a67d24a13599ff5fee7962493c523aab6 | clean |
| 66 | 43f1aac1585bbaf8f6ca67670a7555e945342b7e | 4ac522b1e9810f5c249b140cdd6c57ce7292fdc0 | clean |
| 67 | 161c70303c563d45f128171f2270f2222b36a663 | 7a453f4b5019de7d9c76bdaf1b8a826c2c5ee065 | clean |
| 68 | ec8448c2965ab998223963203e215c009a4c0a14 | 39f9d9644f4b35ccd1ff31874dd89b11d60a0eee | clean |
| 69 | 270a728b2bfb929a1534cf47c133c23459061293 | - | empty-after-resolve |
| 70 | 5e7b5d6b3bad77ca49ccbe3d79aa15aaf1f04e2b | 8f67eadea06f5df4e81e581092bfb641afb71869 | clean |
| 71 | 62b0cada7048427598ff2969651f565d8748d4c2 | - | empty-after-resolve |
| 72 | 5dcc9f27dae665162797a186f3503ac33285e2ed | eda258b45c7d9176edaa2344c7688997d14cb331 | clean |
| 73 | a0b3e1e0f9b11e24a4e3b271a36c04dbef6de5dd | 697016f91b15b1ec46c547fb4fa2181af4586a1c | clean |
| 74 | e791df1b7970038b167488f187c77fde01a7a004 | 104f02b7c915ec4687d1387401fd026440135a4b | clean |
| 75 | ee7ccd1b1c28bd5c8c48b83a475c764438a1f00d | 7a35d3297348eba739211daa7e2e5c9120dfc044 | clean |
| 76 | f300cf5ace78bc0515e15d2ab0e2cad52120132f | 1df519c528d02aa5b0bfab8d9301215a8152578c | clean |
| 77 | 3ea46361a9f3ac6b4493a6753b9ab84c902fdcaf | 7244f3c6c064b5edadf2dca2b3bddf1e6149cdab | clean |
| 78 | c7b7ae3a991652a4c831f6a49b7975157da5e61a | 2de5824fef66bbd14398cf968b19e19b73db6e01 | clean |
| 79 | 7061c76ec9dde3c4fe1286f463ce76d8743ddee0 | 64e7227ad05622a6998bc8903cb83a30fea4f34f | clean |
| 80 | c4eeff2e35e310698e2fd85e83271f49f5881895 | e52fb885dfd057d400f9bb4113a9b0990296c560 | clean |
| 81 | 2c4a664fb3750ef000e7e1b7c13a154aae13773f | ed9e1af7c01902e53195d4c4c2ea67dabd5ce3d8 | clean |
| 82 | fef9562dc53e4fc477ecb24b4b1d136fad683452 | 42f9bac179d470dd2945939bc17d800832b83505 | clean |
| 83 | 7ac976b596b4c8251bf352f291f0f040a6c9547e | ccc9b152ecb3655d8caad8d61b3458233e04385f | clean |
