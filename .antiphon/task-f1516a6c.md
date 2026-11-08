# Merge batch 126

Master fast-forwarded from `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e` to `c460b0d0a4de591b8e662f8dbc57eeac3f26d249`. First push accepted. No subject excluded. No migration. rc was not pushed. Loop count 1.

## Checks

- Evidence `3364197bf1..c460b0d0a4`: commits=27 entries=0 violations=0. Mirror was not shallow (`blob:none` partial clone; evidence exit was 0, not 2).
- Stable patch-ids matched for all 26 cherry-picks. Auto-merges (GitSettings.cs, docs/docker-stack.md, docs/testing-and-build.md, tools/Antiphon.Checkpoints/Program.cs) did not change patch-ids.
- Antiphon.sln build: 0 errors, 2076 warnings, 6m 23s. Slot `batch126-sln` waited 135s, maxcpucount=6, lease 8aac6303-d626-47b0-a9d2-4222eb89ada8. UseAppHost=false, native output, TEMP=/tmp.
- Antiphon.Tests union: 361 passed, 0 failed, 0 skipped, 6m 48s. C1729 stdout: `VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3`.
- FakeBrowserSolutionReleaseTests is in Antiphon.MarkdownPdf.Tests, so it could not share that process: 1 passed, 0 failed, 0 skipped, 14.6s.
- Policy recomputed on the combined tree and matched the CARD-1775 pins. No second subject edited `tests/test-execution-policy.json` or `scripts/test-live-lane-run.ps1`, so there was no extra recompute commit.
  - policyHash `c6da42db3549444e55fcf35ead72f674861dd24f4809bc924780bebcbd07f39d` -> `50c33c11acb18e79a69b58e846112c99d428e8f34da99aa5df8b2da9d7e271e0`
  - PinnedInputDigest `2e519236f898920407b63636797731c00aa73e22f01727d2f92872126cbc56f3` -> `8510514a080bb26c46ca73363488f42d24eb53924d3ffcb21412c6f94f2bd995`
- Land-graph refresh commit `c460b0d0a4de591b8e662f8dbc57eeac3f26d249` touches only the evaluation and manifest (pins blob unchanged). Post-refresh -Check exit 0.
  - digest `E0565240FF824353AA5AA6B90D579DE5C7AEE799C6A73D5990F946C1D780BC2D` count 71 -> `3607946C34AFF302EC65D57B41F93E400DBB0C7C1F51CE30D2202224A593432E` count 71
  - SDK 10.0.403
  - pins blob `7a2586e58bd4768648c6f670f6f2c4432f68936a` unchanged
  - changed records: `Antiphon.sln`, `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj`
- `-Card` was omitted (multi-subject batch). The script still printed `card-note: recorded` because an empty card skips the post and returns success.

## Commits

| Land | Source | Patch-id | Subject |
|---|---|---|---|
| 841606834e214cc2fe948becdbcba6ba1a27b808 | b80894e7f9c4f9a001f2708a5b16f12e86a3e5eb | a12fbb32b59aa9be181518bba2e85f592f48bb2f | CARD-1776 witness push credential |
| dff78584d636fa49eca37652f2c3f14a48002a92 | 0f76e7b0a9379345684dbb4f5452073e53eb82f4 | 3f951ff5de621cd3c69d4b5e750e301f93856a14 | CARD-1776 mount land credential |
| d6c63c922ee34106083f7fc7d7a0cd9954865b4a | 989f150f462ec3798e634ae7c1a92265deb08e51 | f4a4405237a92312c5c5c24b102b923653f3ae2d | CARD-1776 witness rebase without identity |
| 4128a884a9ae5713a1708ae559b8f5ed5e9ec71e | cef2481a855a317cfcb720a91e1f9a81513ffd19 | afb925ba4a6c6151f1e7869b1d0d41f9cdf4acc3 | CARD-1776 read-only git identity |
| 4cf4f980f9f7ac8cef915fd94d17f1cc58913c90 | 3e37355511ab5964dccd332f0d4aa72b168b4d48 | 86146561b1a35dafd22fa8eb2f4c2db7a0403513 | CARD-1775 witness production compose |
| 9ff8993a66d9f0193552c3b8f7fcb1dbefe8993f | 8f254394951ef042afaf3682f97b27787ddc9824 | d88319a5a0d499bc7d46c95935dfee307ad52dd4 | CARD-1775 production compose and deploy |
| 986297da8546857622f62a647c9284ccf10299ee | 5f1bba8b3db4097851770a345d7caeb8431fcb2d | 19a2e3f40a0f685b66fa2137d5d307256f839b8d | CARD-1775 Windows-lane exclusions |
| 14c6025027998f806632581b8836e7e06e3c7e10 | 7febe19dddfac44fcf2d0864895c378da8deee36 | ec1a758c11c747c369ce2e160e89e00b45868fd8 | CARD-1775 specialist dirs and drain witness |
| 2d6d3b645e2941edee5941fe18b05658104619f4 | 37244c6a08159f6a662c042d7d9f44175c5eaf38 | b90ce073cb9d9337d25f11dd75dbb137c2f86668 | CARD-1775 share specialist dirs and drain |
| c44005aebaabbd36cc384093eeeec647a977ce97 | 22bc24a761c3f12a0777034721d3c8e6000a7ad7 | 852ba78141994d7cdbdf427d96d2bb4b9e32fb01 | CARD-1740 expect Release FakeBrowser |
| a6a5cce614781da3feeb78477e102c6613da79bd | 7b2e21923729008fc022a5c798509d004ef8892b | bf475db56acc21f7c0f95e7a9bd3a8c2f5027711 | CARD-1740 Release solution configuration |
| 6f4da766c9f06a65817a3a30b2346ec21c61efe3 | 8fb5c75a053e0136ffd410ffc4fc24d7feae2ef4 | 98060f2e594225fc15662d8631bba523b916cb36 | CARD-1740 witness Release build |
| c47e20e5cbd1bb3abf9b2dc47066f10c249b712b | a9938194e7cf3afd1911b3ff669a4b10c084ab1a | 6985c90e5d64c284432e47c0ac066d353ff4205a | CARD-1740 outer slot |
| 9a9d2e460727b71770db80e450512a8fb8d7e851 | 7bf75f77e8fef37fc277378e91b548d58b16f8aa | f170cb806fffd8f0bc4be77edd5fccf425a927bd | CARD-1740 measured cost |
| be4c317a38568a37b15d41ae985fee6f8054ef1d | f99dd0048e4f8851c96ee0774ad218e4e668c391 | 7482112fc31dc780d1992f3c137c005bad903fe4 | CARD-1748 killed-row witness |
| 8b7092b6247d8b81cfd1a01d8847f755a9276c46 | 226e5786787d9bab66b09433f7766317c0182791 | 53bf04c16fc0dee6070938bd0983b29da3460c70 | CARD-1748 journal counts |
| 84298c40f371de20d0e9b022cd609b296055d0b8 | 60a2afd4ac811cfd747d66acfd6a89fa7e83b4a8 | badfd385e621fc116096fbd19914949e976f8a06 | CARD-1748 keep diagnostic out of receipt JSON |
| ccc472160adba05a809ddc9b38e0d66b34f5ce6e | 8d85c2f7546e6b4d90853d123cc35909a4a19e64 | 172c32ed13e683b77c6261d732bee267b5a7bcfe | CARD-1748 inherited phase-root witness |
| d0a7add596a9bc202f3ffb1909b91ff607e923fe | 434a2ae7bec1dc1e821fd71aa2b67956e5653fa1 | 9cf24ad7b3743dfe33f3105df682fd8fda27ab04 | CARD-1748 drop inherited phase variables |
| 85273023fea142c6c142fe2aea09ae14404a184c | 6e0408cf9bf39683eb61457ec11e8da03f6e227d | 657e8ae1a5f46a4b9575deacbacd3a5d78500063 | CARD-1748 echo inherited-root line |
| fcc985f44ae2e407accd3703ca39f3d1e558eff5 | 0ee25dc1c12ce941cb9494f69450c1bc46165ae1 | 31ac21700e6448ee3f05786fef918429d3e5ec78 | CARD-1785 additive-migration witness |
| b5452ca07fc10fb4a69659dace560c5b421903f7 | 98f63fd040fe3c5c47b667db7da9ad9bafb43739 | cbd26c4367b6ab6009b6d578cc72a9893fd8088d | CARD-1785 additive rule and downgrade selector |
| d12d98040901e8268462993b5d1d63affdd7b423 | a2b343e988ee1d56c94dd31d6e6f0c5d0995ca5c | 01b55da1c1318994d8da64f52c6f1cb708c83b36 | CARD-1781 why-missed rc note |
| 7ccfef80ee9af103add2921488ed3fbdfcccd560 | 8fb0f6c013389d999e8bac8379ba7a0c1a324e3f | 40cb700d3bc6e8b6c2f5d0e86d39ae4f9c6553b0 | why DispatchBaseNotificationTests missed the rc cut |
| 7e0cd04017d0f95804d7330e4ee2f0f045e25f5b | 5bc3c725dbea5a5f9663b2f3ebf599fc2d0e6cf0 | f71a93af919320b7d153a66c991b2650fc8f64e7 | why land-notification persistence was missed |
| d068e1b1902ab4aab5916ad771eeab5779de09a4 | 738fea8b5dd42c6bd83d6d864cf564a93d19e948 | 6b770cd473ebc9a1a567c1f84a61a987ac1c01cf | why SessionRuntimeActionRoutingTests missed the rc cut |
| c460b0d0a4de591b8e662f8dbc57eeac3f26d249 | (this batch) | | land-graph refresh |

## Owed

- SourceLanding mutation positive controls for the landed cards.
- The Unit lane and nightly were not run.
- Windows still owes the three CARD-1775 methods excluded as Linux-only: `WhatIf_prints_the_plan_and_calls_no_docker`, `Execute_refusals_stop_before_docker`, `Execute_drains_large_compose_output_without_echoing_a_secret`.
- This publication is master-only. `rc/2026-11-11` was not moved.
