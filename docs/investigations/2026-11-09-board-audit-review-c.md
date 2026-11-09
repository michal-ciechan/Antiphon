# Board audit: Review cards CARD-1713 through CARD-1823

Date: 2026-11-09. Base: `origin/master` `e06bcb3a0c17f4452db8b31ba92a22694ad3d5ab`.
Scope: every Antiphon card in column Review at `card.ps1 list -Board Antiphon -Status Review` whose number is in CARD-1713..CARD-1823. That list had 77 cards. `card.ps1 get` then showed 15 of them already Done (closed between the list and the get). Those 15 are not in the close list.
Every SHA below is a commit on `origin/master` (`git log origin/master`). Code checks are `git grep` / `git blame` / `git show` of the named file at that tip. Owed Mutation PCs and owed native Windows runs do not block DONE when the code acceptance is on master; they are named in the evidence.

Counts for the 77: DONE 41 (still Review, close these), PARTLY 13, OPEN 8, SUPERSEDED 0, UNSURE 0, already Done 15.

DONE still in Review: CARD-1713 CARD-1714 CARD-1719 CARD-1721 CARD-1722 CARD-1725 CARD-1736 CARD-1739 CARD-1740 CARD-1745 CARD-1747 CARD-1748 CARD-1749 CARD-1753 CARD-1755 CARD-1758 CARD-1759 CARD-1760 CARD-1762 CARD-1763 CARD-1764 CARD-1765 CARD-1766 CARD-1767 CARD-1769 CARD-1772 CARD-1780 CARD-1781 CARD-1782 CARD-1783 CARD-1785 CARD-1787 CARD-1788 CARD-1791 CARD-1794 CARD-1795 CARD-1796 CARD-1797 CARD-1798 CARD-1799 CARD-1803

## Closing reasons

Paste only for the DONE rows that are still Review.

- CARD-1713: landed on master 959349dba9a0e082d0b53702edaa970dfb8a3520. Live holder reaped at 48h (`BuildSlotMaxHoldTests.V1_Default_keeps_live_holder_until_48_hours_then_reaps_once`); `LeaseMaxHoldHours` is in `docs/testing-and-build.md`.
- CARD-1714: landed on master d95d3f2cdce02fd95bf2145ecee979769cfc4c3c. InspectAsync treats stderr as status_untrusted and refuses `worktree_path_too_long` before the move. Native CP-25 still owed.
- CARD-1719: landed on master d10e1c51a72852aa51895109b66e567f81346dca. `GhostSeatRelease` releases a clean Failed seat; witnesses in `GhostSeatReleaseTests`.
- CARD-1721: landed on master 91a649074e888596eaf18c3f312bd016ea948abc and cff07f97eb570e84b67f1e4fbb271124f40b40bd. Activity fence, sweep cap, attention text, and `OwnsAutomaticPath` witness. Mutation PCs still owed.
- CARD-1722: landed on master e7c13ee9016625bba00a9b6d92262bba2ee9377d and 84a480040994a596992d38028b260671b2b4f95f. Debounce and receipt budget are test-controlled; the source receipt is written before build readiness.
- CARD-1725: landed on master 191b1c1f2e89a836a8e631cf5d6b2ef046e22878. ASCII scan no longer dispatches a pipeline. Witness `de5af0b70f5782553334262c7050e87fb76ae8a5`.
- CARD-1736: landed on master d4bdc5455bd2a647f42cf83f10c950d47f22457f. Composer stall cases advance on the registered clock.
- CARD-1739: landed on master 5d5973be2cd867bf592cd0363240e728f64d3b28. Healthy checkpoint receipt fixtures carry all four count tokens.
- CARD-1740: landed on master a6a5cce614781da3feeb78477e102c6613da79bd. Release solution builds FakeBrowser. Witness `6f4da766c9f06a65817a3a30b2346ec21c61efe3`.
- CARD-1745: landed on master 2b0468947bea8f8cfea45314fa231f54e09e62e4 and b4c226c2e41262ef73f3597413382b5cbc12f1bc. Linux SHA restart gate probes before any docker stop. This card is the script, not the cutover.
- CARD-1747: landed on master e51d4375a4bc57297bc052ef3734d0d3ee824347. Checkpoint-tool classes use `CheckpointToolSpawnLimit` width 2. Red witness `292f49dfa8797a868e73d7e27c5e06b9d59c1ce3`. The three-run flake note is not in the commit.
- CARD-1748: landed on master 8b7092b6247d8b81cfd1a01d8847f755a9276c46. A killed Unit row prints journal counts and the diagnostic stays out of receipt JSON. Witness `be4c317a38568a37b15d41ae985fee6f8054ef1d`.
- CARD-1749: landed on master c4592abe2f2a0329bf44d3438e68916e7450d5f6. Sibling inspection fallback reads the GitSettings default. Witness `7b6de6f191bf5d20ffb01d9ea2a9b5f940820a2d`.
- CARD-1753: landed on master 7ddd79885b87470da1ad2a971c01a7605160bc78. `scripts/land-graph-land-refresh.ps1` refreshes batch-owned drift and never asks. Rule text `03b2b8593e38a39fde154e1650372ac44988c456`.
- CARD-1755: landed on master 7d4e8e80056bc4087a1ff86cfed6960d58961db8. Receipt hash and git blob share one read; a same-stamp rewrite is refused (`9af0dbc8cbd22f7f4bcbd752d3d5c4a7b39813f3`).
- CARD-1758: landed on master as its four slices: 933b5c016046fbcccbad04ed54da6b8b793b5a14 (S1), c46677af596b0d2b7bc7cf8ddba185e94938ffcd (S2 source-generated `RunnerReadJson`), 7dc25223bd992505390495a5b9c03c0cd396a612 (S3 304), 87000d366861f8c5281e83ed92b57a66d9fd8860 (S4 since-cursor). Confirmed at `SessionReadLaunchRoutes` and `PhoneHomeCommandDispatcher`.
- CARD-1759: landed on master 4d91773c122186930a3da485dd2a3221a1e960cd. Each land-note lookup is compiled once. Witness `6db9130dfd4ced7a9751fe673628e4f5138bb651`.
- CARD-1760: landed on master f9181e7c91a72cfdf802cdfd7799ff70114a2350. `LastBefore` binary-searches. Witness `6ac68b684edeadb9ed747c152527ef2f5ed5e01d`.
- CARD-1762: landed on master c46677af596b0d2b7bc7cf8ddba185e94938ffcd. Hot buffer, snapshot, and transcript reads use `RunnerReadJsonContext`. Witness `RunnerReadJsonTests`.
- CARD-1763: landed on master 7dc25223bd992505390495a5b9c03c0cd396a612. Unchanged pty buffer and snapshot reads answer 304. Witness `16b895201e7d2013018cf48acc07dfdcf3d434e3`.
- CARD-1764: landed on master 87000d366861f8c5281e83ed92b57a66d9fd8860. Transcript since-cursor applies only when gap-free. Witness `cca29660c2acd18f45729704ad4df049e41d5556`.
- CARD-1765: landed on master cc3a2b2fe1e83dcf5843342d0875c6094267d453. S1 test design CP-7/CP-8 `timeoutMinutes` is 20.
- CARD-1766: landed on master abad143eff62d2f61dc811441df586a1a66e71e5. Non-pty sequence polls stay on the buffer route, including the watchdog tail choice.
- CARD-1767: landed on master 80cb252b9ea548e47e2fdaa28cd775f76ed60261. `OverlapAsync` skips the owner read when the key has no ref. Witness `09a32184bb6fab5d122ce05644086451a0af174c`.
- CARD-1769: landed on master 593ba300b711fca9708d967ecac5b0dc3eb66325. Unchanged AwaitingReceipt notes park and Canceled leaves the page. Witness `b202661472e86b8ad38486e3c0a58b71e260ed80`.
- CARD-1772: landed on master 83ce597ee6864a336679adc0553c0a2a4220f3f8. stop/wait refuse a blank report.json identity before the terminal gate creates a file.
- CARD-1780: landed on master f5cc4facfacdc4e4a72a6d098d225305624dac12. Out-of-solution references drop Configuration. Witness `C1780_RealRepositoryFlagsReferencesOutsideTheSolution`.
- CARD-1781: landed on master d12d98040901e8268462993b5d1d63affdd7b423. Why-missed write-up is in `docs/orchestration-loop.md` and the rc skill. Witness `WhyMissedWriteUpContractTests`.
- CARD-1782: landed on master ac1a77971d48d180ae4125479b62839abb6db915 and a1de447cd9e338cacd42f810d5bf35099c3d220b. `tests/impacted-classes.json` has the downgrade selector; `stage-review.md` says `+impacted rows`.
- CARD-1783: landed on master 8b5d102645c101dc1eb2e9b619569e708ea9622d. Merge and rc cut run the fixed high-radius subset. Witness `9f3b9c1cb9ec1b270aae708fe88d3ec9ec12acde`.
- CARD-1785: landed on master b5452ca07fc10fb4a69659dace560c5b421903f7. Additive-migration rule and downgrade selector are in the owner docs. Token repair is CARD-1789, already Done.
- CARD-1787: landed on master cef76fe409b9526eba0b8f0b5cf9def42510089c. Land git identity file is allow-listed and outranking git env is scrubbed. Witness `1313960d2d`.
- CARD-1788: landed on master d95e5f0e16a2c8fb5ba5a7940720f8f43e1cee1b. Host HEAD is compared before up and `/work` chown is scoped. Witness `d2c1778b7e`.
- CARD-1791: landed on master 1da13dae0cc3a3eea89aaee4d6d2d7a0b480f8a9. Lease backend is `(pid, backend_start)` with monotonic expiry. Witness `f95b1de84e`.
- CARD-1794: landed on master aa600ddecea670605da8f721f6778819e89f6a61. Gone mirrors leave the removal budget, execute route exists, disk floor alarms. Commit body maps items 1-5; settling is explained as the out-of-repo 24h override.
- CARD-1795: landed on master 52596e38452269a18d0277887c2edeb190921aae. E2E fixture keeps Windows-path settings inside its workspace. Witness `9f009f299f`.
- CARD-1796: landed on master 11f381a74d8fb6df3e8ea7b655f071e80ed1288c. One retention tick at a time; execute runs in the background. Witness `1a1b84ac5e`.
- CARD-1797: landed on master a28982a5d09aaf7ed661a1c5a73c68131352a59f. E2E boots seed Claude trust into the fixture config. Witness `5aee4e8e81`.
- CARD-1798: landed on master 060e6e71ba6c224097397e06572293ec6cb0f027. A refused cursor keeps the full answer; ended sessions are forgotten; held bytes are capped. Witness `a01f4901bc`.
- CARD-1799: landed on master 3b0ae86a0650a643bc8dd0cb8916f776493b34e5. Over-cap Telegram files are named in the note and the sender is told once. Deploy recorded in `a0e14b3e1e5c83425ac070fac5d9e953b8d1be74` (am-service on server2 from `653f1145`, verdict ok).
- CARD-1803: landed on master ceec59968e4cebe4aa0da1eb0c148fa8869be3f7. A blocked seat past the hour bound is paged. Witness `2d8d85d7e9`. Detection only.

## Table

| card | title | column | verdict | evidence | remaining |
|---|---|---|---|---|---|
| CARD-1713 | Build slot lease kept for a live holder has no absolute upper bound | Review | DONE | 959349dba9a0e082d0b53702edaa970dfb8a3520; `BuildSlotMaxHoldTests.V1`; docs `LeaseMaxHoldHours` default 48 | absent/zero select the 48h default, not "no bound" |
| CARD-1714 | Guarded removal re-check is blind to unreadable dirs and cannot delete long paths | Review | DONE | d95d3f2cdce02fd95bf2145ecee979769cfc4c3c; red witness e2bc1c2dfe | native Windows CP-25 still owed |
| CARD-1715 | status_untrusted is opaque and invisible; two land sites mislabel it | Review | PARTLY | item 2 landed a77bae94d1ff23728270a44181faa6752484617b at `AgentTaskLandSourceResolver.cs:369` and `:511` | stderr bucket, fleet attention, non-English pin, path-length pin |
| CARD-1717 | S4a audit B D-1..D-5 | Review | OPEN | red witness 987aa33dad1caf0f36c174936fb0196798783b3d; blocked review doc d03db8ba02 | D-1..D-5 |
| CARD-1718 | S4a audit A A-D1..A-D5 | Review | PARTLY | A-D3 aa729ee2cec4b6b256b15012a7bcaecad8826f00, cfcfa33cd4, 0963befb7a; secret floor in `WorktreeCleanupSettings` | A-D1 S4a proof, A-D2 ignorecase, A-D4 ls-files stderr, A-D5 ADS |
| CARD-1719 | Failed CompletedWithoutProgress ghost seats | Review | DONE | d10e1c51a72852aa51895109b66e567f81346dca; `GhostSeatRelease.cs` | Mutation PCs named on CARD-1721 |
| CARD-1721 | CARD-1719 advisories A-1..A-4 | Review | DONE | 91a649074e888596eaf18c3f312bd016ea948abc, cff07f97eb570e84b67f1e4fbb271124f40b40bd; `GhostSeatReleaseTests` `OwnsAutomaticPath` | PC-1..PC-3 |
| CARD-1722 | rc b19a9880 g71, g105 flakes and scheduler race | Review | DONE | e7c13ee9016625bba00a9b6d92262bba2ee9377d; 84a480040994a596992d38028b260671b2b4f95f | g116 and g120 were named analysis-pending, not these three items |
| CARD-1723 | CARD-1591 S1 advisories A-1..A-3 | Review | OPEN | pin test 6a8738b4fe4314f6662cb5558b92af5ec82cfda2 only | A-1..A-3 |
| CARD-1725 | C1233_FileCallers exits 137 | Review | DONE | 191b1c1f2e89a836a8e631cf5d6b2ef046e22878 | none on the ASCII-dispatch fix |
| CARD-1726 | Release-candidate red 2026-11-09 BUILD FAILED | Review | OPEN | candidate 7bee43614495 is on master; no repair commit | build failure itself |
| CARD-1728 | Debug Unit shard completion and stop receipt | Review | PARTLY | doc c7216a06f5 names the serial `ProcessSpawnLimit` chain and no TRX | qualified Unit receipt |
| CARD-1732 | rc coverage A-1..A-7 | Review | OPEN | red test 673cba20f26c3868c57a65696fca1bda637aaca2; repair 7ed514f4 is not an ancestor | card text says do not close; A-1 repair not on master; A-2..A-7 open |
| CARD-1733 | ReleaseTagAllocator reservation matrices exhaust 300s | Review | OPEN | no commit names the card | allocator 300s failures |
| CARD-1736 | C1259 event pump load-sensitive | Review | DONE | d4bdc5455bd2a647f42cf83f10c950d47f22457f | none |
| CARD-1737 | S4a masked preservation fences | Review | PARTLY | witnesses through 891c54fd75, which still says pending obligations | independent preservation evidence and mutation |
| CARD-1739 | healthy receipt counts omit tokens | Review | DONE | 5d5973be2cd867bf592cd0363240e728f64d3b28 | none |
| CARD-1740 | Release solution does not build FakeBrowser | Review | DONE | a6a5cce614781da3feeb78477e102c6613da79bd; witness 6f4da766c9f06a65817a3a30b2346ec21c61efe3 | follow-ups are CARD-1780 |
| CARD-1744 | Cut the production control plane to server2 | Review | OPEN | plan only, b1e129b448 | operator step: cutover not evidenced |
| CARD-1745 | Linux server restart SHA gate | Review | DONE | 2b0468947bea8f8cfea45314fa231f54e09e62e4, b4c226c2e41262ef73f3597413382b5cbc12f1bc | using the gate at cutover is CARD-1744 |
| CARD-1746 | Place the broker and enable ChannelBridge | Review | PARTLY | decision 462e3e3b4568a4076d14c3eb9571fe94abb298ea keeps the server2 broker | enable ChannelBridge at cutover, after the current member stops |
| CARD-1747 | Checkpoint-tool tests own spawn limiter | Review | DONE | e51d4375a4bc57297bc052ef3734d0d3ee824347 | three consecutive flake runs not in the commit |
| CARD-1748 | Killed Unit row prints journal counts | Review | DONE | 8b7092b6247d8b81cfd1a01d8847f755a9276c46, 84298c40f3, d0a7add596 | none |
| CARD-1749 | Sibling inspection fallback deadline | Review | DONE | c4592abe2f2a0329bf44d3438e68916e7450d5f6 | none |
| CARD-1753 | Land-graph refresh never asks | Review | DONE | 7ddd79885b87470da1ad2a971c01a7605160bc78; `scripts/land-graph-land-refresh.ps1` | none |
| CARD-1755 | Retirement content binding CPU | Review | DONE | 7d4e8e80056bc4087a1ff86cfed6960d58961db8; witness 9af0dbc8cbd22f7f4bcbd752d3d5c4a7b39813f3 | none |
| CARD-1757 | HostStatsSampler whole-system snapshot | Review | PARTLY | code 4e1582286efd09cb39e82feb08689dcf3c73e24a samples one process | re-trace under 1% of runner CPU not evidenced |
| CARD-1758 | Runner read polling CPU | Review | DONE | slices 933b5c0160, c46677af59, 7dc25223bd, 87000d3668 | none; follow-ups are their own cards |
| CARD-1759 | Land-notification reconcile CPU | Review | DONE | 4d91773c122186930a3da485dd2a3221a1e960cd | the 1,550-note rescan is CARD-1769 |
| CARD-1760 | LastBefore linear scan | Review | DONE | f9181e7c91a72cfdf802cdfd7799ff70114a2350 | none |
| CARD-1762 | Source-generated JSON for hot reads | Review | DONE | c46677af596b0d2b7bc7cf8ddba185e94938ffcd; `RunnerReadJson.PhoneBuffer` / `HttpBuffer` used by the runner routes | CARD-1771 follow-up already Done |
| CARD-1763 | pty If-None-Match 304 | Review | DONE | 7dc25223bd992505390495a5b9c03c0cd396a612 | none |
| CARD-1764 | gap-safe transcript since-cursor | Review | DONE | 87000d366861f8c5281e83ed92b57a66d9fd8860 | follow-up is CARD-1798 |
| CARD-1765 | Load telemetry S1 CP-7/CP-8 budgets | Review | DONE | cc3a2b2fe1e83dcf5843342d0875c6094267d453; testdesign `timeoutMinutes: 20` | none |
| CARD-1766 | herdr sequence readers use the costly GET | Review | DONE | abad143eff62d2f61dc811441df586a1a66e71e5 | none |
| CARD-1767 | Skip dead owner read in OverlapAsync | Review | DONE | 80cb252b9ea548e47e2fdaa28cd775f76ed60261 | none |
| CARD-1768 | Hangfire lock advisories A-1..A-7 | Review | PARTLY | A-1..A-3 d116a618f1ddc048e5b8501b659cd535fd96deef; A-4/A-5/A-7 text is in `docs/docker-stack.md` around the 45s bound | A-6 `C459_ScheduledWorkerExecutesBothLanes` flake |
| CARD-1769 | Park AwaitingReceipt rescans | Review | DONE | 593ba300b711fca9708d967ecac5b0dc3eb66325 | follow-ups are CARD-1773 |
| CARD-1770 | CARD-1731 evidence-path advisories | Review | PARTLY | A-1 fbf9418c1f347e3fc8a4256c73f669d9a0b4212e; witness cad49b3048 | A-2 Windows shapes, A-3 Windows sample, A-4 mutation note |
| CARD-1771 | CARD-1762 follow-ups | Done | DONE | d861149d0738bef6019e1a437efc69d245512ab9; witness ff5490965251b029d3b2538221b38329bdd0cf45 | already closed |
| CARD-1772 | blank report.json identity changes inventory | Review | DONE | 83ce597ee6864a336679adc0553c0a2a4220f3f8 | none |
| CARD-1773 | CARD-1769 park-map and clock advisories | Review | OPEN | card-1773 commits on master are rc newline and canceled-row test retargets, not A-1..A-7 | A-1..A-7 |
| CARD-1774 | tick retry, own backend, doc bounds | Review | PARTLY | 0774dfa3dd8f9e5392981418bed21de0e18937e9 and 663faf96fa; doc bound in `docs/docker-stack.md` | A-4 real-silence witness, A-6 poll, A-7 move the unit text pins |
| CARD-1775 | Production server compose (CARD-1744 S1) | Review | PARTLY | compose 9ff8993a66d9f0193552c3b8f7fcb1dbefe8993f; witness 4cf4f980f9f7ac8cef915fd94d17f1cc58913c90 | operator step: production project not started |
| CARD-1776 | Linux land credential (CARD-1744 S2) | Review | PARTLY | identity 4128a884a9ae5713a1708ae559b8f5ed5e9ec71e | operator step: cutover path rewrite and token mount not evidenced |
| CARD-1777 | require ANTIPHON_API (CARD-1744 S3) | Review | PARTLY | f7d9af47d2de8a4fc54aa60ee1f8faa815033de8 | operator step: desktop token copy after cutover not evidenced |
| CARD-1778 | Re-home always-on agents (CARD-1744 S4) | Review | PARTLY | sidecar trust de3c59991297c9d49d18bba0b7ad6d25ea270156; plan 31cbd977a1 | operator step: seven agents not shown re-homed |
| CARD-1780 | Audit config propagation and missing solution projects | Review | DONE | f5cc4facfacdc4e4a72a6d098d225305624dac12; `C1780_RealRepositoryFlagsReferencesOutsideTheSolution` | none; commit says consumers of config-specific output are solution members |
| CARD-1781 | why-missed write-up for each new rc failure | Review | DONE | d12d98040901e8268462993b5d1d63affdd7b423; `WhyMissedWriteUpContractTests` | none |
| CARD-1782 | Impacted-classes selector | Review | DONE | ac1a77971d48d180ae4125479b62839abb6db915, a1de447cd9e338cacd42f810d5bf35099c3d220b | none |
| CARD-1783 | Merge and rc-cut high-radius subset | Review | DONE | 8b5d102645c101dc1eb2e9b619569e708ea9622d | none |
| CARD-1785 | Additive-migration rule and downgrade selector in briefs | Review | DONE | b5452ca07fc10fb4a69659dace560c5b421903f7 | review token fixes are CARD-1789, already Done |
| CARD-1787 | Allow-list land git identity keys | Review | DONE | cef76fe409b9526eba0b8f0b5cf9def42510089c | none |
| CARD-1788 | Host HEAD compare and cheaper /work chown | Review | DONE | d95e5f0e16a2c8fb5ba5a7940720f8f43e1cee1b | A-9 stays on CARD-1778 |
| CARD-1789 | sort-after-master clause and selector tokens | Done | DONE | c0121bfd0ef883f16ecd7ceb456073721e954792 | already closed |
| CARD-1791 | Hangfire lease backend identity | Review | DONE | 1da13dae0cc3a3eea89aaee4d6d2d7a0b480f8a9 | none |
| CARD-1794 | Retention frees 0 bytes | Review | DONE | aa600ddecea670605da8f721f6778819e89f6a61 | settling cause recorded as the 24h override, not a separate state machine |
| CARD-1795 | E2E backslash check-interpreter directory | Review | DONE | 52596e38452269a18d0277887c2edeb190921aae | none |
| CARD-1796 | Retention execute route background | Review | DONE | 11f381a74d8fb6df3e8ea7b655f071e80ed1288c | host MinTerminalHours override is an operator action, not this card |
| CARD-1797 | E2E boots append trust to real ~/.claude.json | Review | DONE | a28982a5d09aaf7ed661a1c5a73c68131352a59f | none |
| CARD-1798 | since-cursor reuse, forget, cap | Review | DONE | 060e6e71ba6c224097397e06572293ec6cb0f027 | none |
| CARD-1799 | Telegram over-cap note and auto-reply | Review | DONE | 3b0ae86a0650a643bc8dd0cb8916f776493b34e5; deploy a0e14b3e1e5c83425ac070fac5d9e953b8d1be74 | slice 2 is CARD-1800 |
| CARD-1800 | Telegram out-of-band up to 20 MB | Review | OPEN | no commit | plan and transfer not started |
| CARD-1803 | Blocked seat past N hours escalates | Review | DONE | ceec59968e4cebe4aa0da1eb0c148fa8869be3f7 | detection only; the lease-busy note in the card is a separate idea |
| CARD-1804 | Retention cross-process lock | Done | DONE | 187ed25891cfc2f6da744268ddefcc2f36af7f37 | already closed; Mutation PCs named on the card |
| CARD-1805 | Settlement lease-busy follow-ups | Done | DONE | 39cfde8d031d495dee3ec2a26c14bdac74f0a07e | already closed |
| CARD-1807 | Spawn-limit census from attributes | Done | DONE | 7cbc4c9561a02cf88be942aa1cd422a40fc82c95 | already closed |
| CARD-1808 | EphemeralHttpListener fresh listener on retry | Done | DONE | 9c8cd8b0f5dafef44064a4df7b4411bf18e13996 | already closed |
| CARD-1809 | Review checklist R1-R5 | Done | DONE | 5176b816bbd6e49dc3291a1c220572ebf0fc6a74 | already closed |
| CARD-1810 | Census detects a class that loses marker and limiter | Done | DONE | 7f0a8bb1f8; fix of the assignment walk is CARD-1816 97d4c6708652617f113d043ad92738795b0ffe5b | already closed |
| CARD-1811 | CapabilityScriptTests DPAPI fails on Linux | Done | DONE | 7bee43614495293afbae1b0f4add019d11dacb9c | already closed |
| CARD-1812 | dirty pooled lock, aborted execute, lost lock | Done | DONE | d3ad4fad6f997e71eb10445b9d56999eebc9d612 | already closed; Mutation PCs still named |
| CARD-1813 | pwsh cache advisories | Done | DONE | 93acc0b5421a164bbd0ac4f556ac4dc3b5b2e368, 78e6f36e50, f1ce1944ec | already closed; Windows run and Mutation PCs owed |
| CARD-1814 | DPAPI Linux skips and rc rows | Done | DONE | 2d7c01352687913b1bc7d2a37b03882279f13d8c | already closed |
| CARD-1815 | tick-lock 57P01 and foreign holder | Done | DONE | 324f66da355530a43b644b37a86c3c7719e0e6f1 | already closed |
| CARD-1816 | RepoRoot walk misses assignments | Done | DONE | 97d4c6708652617f113d043ad92738795b0ffe5b | already closed |
| CARD-1817 | Runner repository gc owner and invalid GIT_CONFIG_COUNT | Done | DONE | eb1bff5cded6ed08e6d54b94c1f58b6d2d713962 | already closed |

## Not done, noted

No fix was made. This audit does not close cards.
