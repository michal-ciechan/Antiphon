Landed merge batch 86 on master at 991e805e939b7cd95985a46feac8e378811a6496; all 14 picks match their source patches, the solution builds, and all 322 required tests pass.

Base: 0dbd8295a034f8d88f5b98d1e29f14ac82c1890a. Verification source and published master: 991e805e939b7cd95985a46feac8e378811a6496. Assigned branch: feat/card-task-53da6f14. The three inputs were selected with git cherry in the commissioned order; all 14 '+' commits were deliverables, including the complete CARD-1713 stack and its docs/superpowers files. No report-only source commits needed picking. Every cherry-pick used -x and was immediately pushed to the task branch. There were no conflicts, repairs, substantive findings, or incomplete required checks.

Publication: plain fast-forward push to master, confirmed by git ls-remote. One initial publication attempt; target-move loop count 0, push retry count 0. Immediately preceding fetch still showed the expected base. git merge-tree --write-tree origin/master HEAD passed and produced tree 1821fa930c68d45d919d7a6959752cae7098dbff. No rebase, reset, force push, rc write, deployment or land-graph update occurred. The caller's standing authority covered the test-only changes and the accepted Interim review scope for the runner product change; no further approval was required.

Changes: independent CARD-1719 G05/G07 ghost-seat witnesses; synchronized CARD-1709 shutdown witness; CARD-1713 absolute 48-hour lease cap and bounded zero/default normalization in BuildSlotBroker.cs and BuildSlotSettings.cs, with runner tests and owner/plan documentation. The merged deliverable changes 12 files, 685 insertions and 9 deletions. The task-branch-only report added after publication does not change the verified product source or master.

| Land SHA | Source SHA | Stable patch ID (equal at source and land) |
|---|---|---|
| f9b44e666dea1d9c10b4338ab2889a37b39bfebe | 86b94af3a53a2897b8d060bf1c8a5fd1a59caac1 | 990c92037a21dd8d8d334bf35b5ab7b54a25bc21 |
| c3bf6061b29337cc1a0951a066f505afc64e6ddb | 9813bdb2c3da9fa45c26cc2bf719040c9872221c | 7d6e37a03264f51ce2856b21f8e3c8bc79cc7706 |
| c2ecc92eb3b6b2084db08558d0f7e9d431f5ef4b | ba1b7371a30c1d532aa0b67430d8eff0b3036e06 | 8d961c0afaf5a138dd968d82de12ac15dcc3399e |
| 6598cf8fecdd975e973cb6ed71b13764d653f3bc | 295936b9e981bf8a81fdf820c96fa02b881ec569 | 0bbe5d17fe5d4076f45e23afcd64467977047b54 |
| 1b9c9e22c9f85126c2e2b702109ba0c882321fb5 | dbf75c9d6e2eb99af3ddea43b0adc85aab05345a | 2f1ab7e202de21e3ea7fca49ab54ae89f0340ee2 |
| 4fbb745df4d0e82eea918f1fffabc6a430b22707 | cc166db9d3ca78af6606abf2939b8010122aa843 | 3f8d423afe22747734d54fdbbd980167ec039fa3 |
| 959349dba9a0e082d0b53702edaa970dfb8a3520 | d15317800d856c43b252905e141088cf007afe60 | 93647b6e055fa9a42802532a51affee1f3ea7998 |
| e5ecdb2ec8f3d2af3d5e9b60cda82ebb1392877f | a507a6f3e8e8c5537343c6bc6d539571e4eee454 | 267430582ae68d885e75b8f4c2e0964425c90ae8 |
| 62eaeee16b16dcc3be4843404c00bcf13cc0a3bb | 38e7740b44378375474b6268a5c07291016bdcfa | dffba11ceae01154dea59023e8f2bf2b34da17e7 |
| a4e3845e0025b79950b38db994e8f591cf90b132 | f9d1f0eb345d3b10e88e868e7eefc5ea68ab51ef | c1015c333529d4175b70302de1cc1242c4b878ab |
| 141730f82947578c31b0cc64283bb69359ba587a | 22206223a614905aa5123ae673b2b83cceae3942 | c1ed1eaf52ad4955050861eb44e01f6b4e68f3a2 |
| 3b6de4ea546b45eb8deb895cea7d3543ae4b7a0f | 3bd481a04b35212b0e096a9bf72d2f7b3b1e6959 | a95768dd01c967ef80be61a76a7a24d1c8b11e9b |
| ee09ffd85c3387c30424f76b9da25386c3f53583 | 253a86bc397300936eee02c8b2ba1586d3cffd46 | e0fe141ce5f83afef094e2b61b08a41f2cd5ed1d |
| 991e805e939b7cd95985a46feac8e378811a6496 | b4ed0f582468e5db0999c2215eece32868fffeed | db75c229fdf41a66d3878acb2fad3df7a615700b |

Verification used native default outputs, TEMP=/tmp, UseAppHost=false on build and --no-build runs, separate host leases, serialized solution compilation (-m:1), and bounded foreground processes, all fully joined. No source edits occurred during a build or test run. There was one final-tree solution build and one execution of each test selection; no green selection was rerun.

Solution build: exit 0, 0 errors, 1,886 warnings, MSBuild elapsed 5m57.57s, slot wait 60s, held 359s. No NETSDK1047 occurred. The base graph evaluator was absent on the first check, so that initial check exited 1 with 'built evaluator missing'. One necessary additional helper-only build supplied it: 0 errors, 0 warnings, elapsed 6.56s, slot wait 271s, held 7s. The subsequent full base graph check passed. This extra build was necessary because the mandatory base audit cannot run without its evaluator; it was not a second solution build. Initial and successful receipts are preserved.

| Selection | Passed/total | Failed/skipped | Slot wait s | Slot held s | Wall s |
|---|---:|---:|---:|---:|---:|
| contract | 16/16 | 0/0 | 15 | 14 | 31.086 |
| policy | 47/47 | 0/0 | 0 | 94 | 95.698 |
| card1719 | 56/56 | 0/0 | 15 | 88 | 103.649 |
| channel1709 | 162/162 | 0/0 | 30 | 312 | 343.554 |
| runner1713 | 41/41 | 0/0 | 285 | 24 | 310.441 |

contract: MechanicalPcContractTests 11/11; TaskPlatformGuidanceTests 5/5.

policy: SlowTestTripwireTests 2/2; TestClassificationGuardTests 1/1; SlowTestRegistryMergeTests 5/5; TestClassificationPolicyTests 39/39.

card1719: RunnerSlotEndpointTests 10/10; GhostSeatReleaseTests 24/24; TaskSupervisionVisibilityTests 22/22.

channel1709: ChannelBridgeTests 41/41; ChannelInboundRecoveryTests 48/48; ChannelInputWithheldIncidentTests 31/31; TaskSupervisionReceiptTests 42/42.

runner1713: BuildSlotBrokerTests 17/17; BuildSlotMetricsTests 5/5; BuildSlotSettingsTests 2/2; BuildSlotMaxHoldTests 6/6; TestClassificationGuardTests 1/1; BuildSlotEndpointTests 7/7; BuildSlotEndToEndTests 3/3.

Evidence history check, before publication:

```text
EVIDENCE range base=0dbd8295a034f8d88f5b98d1e29f14ac82c1890a head=991e805e939b7cd95985a46feac8e378811a6496
EVIDENCE result commits=14 entries=0 violations=0 base=0dbd8295a034f8d88f5b98d1e29f14ac82c1890a head=991e805e939b7cd95985a46feac8e378811a6496
```

Both full land-graph -Check invocations approved their precheck, evaluation passes, and canary. Base and final each reported digest D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03, count 69. There was no inherited mismatch to waive. No selected build-definition record, target pin, or LandVerificationBuildGraph record changed.

Protected input values were independently recomputed at base and final and compared byte for byte:

- Live-lane -PrintInputDigest: f5151a2a95ed14a8de0a50f9a475d0d45853949ce61c1ed83304c04178bfd73f.
- Get-NightlyPolicyHash: 0686a97cccbec3d421b2298b47ff64aaa63d923d654252e2ea44bd3d1532487b.

Per-test durations and outcomes remain in TRX and HTML; optional phase JSONL, invocation manifests, source identity and assembly SHA-256 provenance are retained in /work/evidence/task-53da6f14. The test phase records cover available fixture/database activity; absent external-call phases are unknown and unattributed time remains remainder, with no measured-p95 claim. Queue wait, slot hold, run wall and test durations are distinct. No run exceeded 30 minutes. The explicitly excluded load-sensitive ExecutionLedger classes were not run.

Built artifacts before cleanup:

- tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll: SHA-256 a765ce081c608fb530d00758bd6241121d2d55c4fd2069ec71f2b199f189e917
- tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Server.dll: SHA-256 4b05b11f062d6d2824cf8c28c145a41b9d293120074aed4597de2f4a99f9639e
- tests/Antiphon.SessionRunner.Tests/bin/Debug/net9.0/Antiphon.SessionRunner.Tests.dll: SHA-256 34839a71b9adaee15d127586706fbdf7e5ed2219a7ec837e8c3603f0deb215e3
- tests/Antiphon.SessionRunner.Tests/bin/Debug/net9.0/Antiphon.SessionRunner.dll: SHA-256 f76d23211f7c7021a4392c0691185e1eb6e80d85c1f42e540958f3c9101f8ece

Reproduction uses a fresh checkout at 991e805e939b7cd95985a46feac8e378811a6496. Build through the gate, then execute each selection once through a fresh lease:

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge86-solution -SlotWaitMinutes 15 -- dotnet build Antiphon.sln -p:UseAppHost=false -m:1 -nodeReuse:false
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label <selection> -SlotWaitMinutes 15 -- dotnet run --project tests/<project> --no-build -p:UseAppHost=false -- --treenode-filter '<filter>' --timeout 12m --report-trx --report-trx-filename run.trx --results-directory <new-results-path>
```

- contract, project Antiphon.Tests: `/*/*/(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)/*`
- policy, project Antiphon.Tests: `/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)/*`
- card1719, project Antiphon.Tests: `/*/*/(GhostSeatReleaseTests*)|(TaskSupervisionVisibilityTests*)|(RunnerSlotEndpointTests*)/*`
- channel1709, project Antiphon.Tests: `/*/*/(ChannelBridge*)|(ChannelInboundRecoveryTests*)|(ChannelInputWithheldIncidentTests*)|(TaskSupervisionReceiptTests*)/*`
- runner1713, project Antiphon.SessionRunner.Tests: `/*/*/(BuildSlot*)|(TestClassificationGuard*)/*`

Graph reproduction additionally needs a gated native-output build of tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj, then scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll. It must never use -Update for this task. Full literal argv and 25/45/30-minute outer watchdogs remain in the retained drivers and invocation manifests; these include separate 15-minute admission bounds, while test execution uses the explicit 12m limit.

Cleanup completed after all children and leases exited. All 104 inventoried bin/obj/output directories were checked to contain no tracked files and removed using manually specified literal absolute paths, with no rm -rf, variable/glob deletion or volume operation. No scratch worktree was created. Local test-result and scratch evidence directories were removed after copying their complete contents to /work/evidence/task-53da6f14; only this report remains in the task worktree. Post-cleanup output census is empty and tracked source is clean.

Raw receipts and reproduction scripts: /work/evidence/task-53da6f14. Report: /work/worktrees/task-53da6f14/.antiphon/task-53da6f14.md.

--- next stage ---
next: done
handoff: Batch 86 published to master and required final-tree checks complete; no action required for this merge. Product activation remains a separate caller-owned operation.
artifact: /work/worktrees/task-53da6f14/.antiphon/task-53da6f14.md
