Batch 73 landed all eight reviewed source picks on master; the shortened current-master verification passed.

New master SHA: d10e1c51a72852aa51895109b66e567f81346dca.
Replay base: 3ce7f87ef06315ace62572d7e51b2de0347dbef9.
Loop count: 1 initial replay; 0 master-movement replays. Publication succeeded on the first push attempt after the final fetch confirmed master had not advanced. No retry backoffs were needed.

All rows are linear cherry-pick -x commits, published individually on feat/card-task-c9ebcc1d and then fast-forwarded to master. All eight stable patch-ids match their source commits exactly; no conflicts, conflict resolutions or source repairs occurred.

| Land SHA | Source SHA | Stable patch-id |
|---|---|---|
| af2b4993a3a37b49f0a6cbae77890ca84589c8a5 | 8f869e5600d9aa937b2831e8c71b124f0cbacbeb | 23f49d4061180fb3f8a17491fe82bc56d3175005 |
| 1a25ca83fcd688c4dddeeb3c6e0187705767e3b2 | 2baea8e38761ddfe88b926c55abf6dc67cd40d20 | 7381539fa489e08134450e1bc77df1717c2a3ad3 |
| 2c866f44a2f37d1d2baddc9078ceae606882c384 | 9f291e9263418f243a05c487479ff1f98f142730 | 959409f4e3927705d0162aede813013237b70417 |
| ba499f6ca6d784e837d2e1e34039d6d14fc72d4e | 19a56f66cd22588a0e2b99f8ea654e8b915f42bd | 9a5b64bad25663543aa0687b67ff1811b2198a6b |
| 95e3e5e9cc5f0c2e91eae36ecd53b988edebdded | 97011c4258942ea64a1c3368aa3fb3df5ddce8f5 | 5c3d05b7994ddfa738e4252ddd17deb9ee3f85e5 |
| 7df541c289d389b027c273da03fadcad38b54ab0 | 9d7f67fe3d47ae4b71569e0e7f286822e787d939 | ff7ff63d6a501079ee5a0936d1819a0805616dad |
| 5fa6eb655ec8ef115d2c488b1a37207698f4c03a | c249c4ef70ef5e8e36d8347aabaa20e671af434a | 1cd2c35e2653355ee0e15ab0b7e87df9bb90b790 |
| d10e1c51a72852aa51895109b66e567f81346dca | e76378e45adf6d8f8dcb92af1adf17b345ad4f84 | bcba5b22381776e57f14e865a8cbe6996f922dbf |

Rows 1–2 are CARD-1709 witnesses, rows 3–5 CARD-1716, and rows 6–8 CARD-1719. The eighth pick missing from the abbreviated brief was 9f291e9263418f243a05c487479ff1f98f142730, the original C1716 witness; its import repair follows it. The report-only source commits were excluded as in batch 72. The replay changes 18 paths: channel recovery and queue handling, ghost-seat release, witnesses and their supporting documentation.

Fresh verification on d10e1c51a72852aa51895109b66e567f81346dca:

| Check | Outcome |
|---|---|
| Stable patch-id proof | 8/8 source matches |
| check-evidence-diff, base..candidate | 8 commits; 0 entries; 0 violations |
| Serialized Antiphon.sln build | 0 errors; 1,845 warnings; 6m13.59s; no NETSDK1047 |
| Six requested test classes | 101 passed; 0 failed; 0 skipped; 2m09.349s |
| Input digest and nightly policy hash | Final equals base for both |
| Base land-graph -Check | Approved, recorded digest and count 69 |
| Candidate land-graph -Check | Approved, identical recorded digest and count 69 |
| git merge-tree --write-tree origin/master HEAD | Exit 0, tree b019795619aa91b3e31fa22c42c357f48fcb8f31 |

The test roster was confirmed from TRX: TestClassificationPolicyTests 39, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, GhostSeatReleaseTests 9, ChannelInboundRecoveryTests 45. All four C1716 cases passed (held lock at sweep/enqueue, ordering/retry, stalled-worker incident). The full named recovery class shared one process with guards and seats to avoid another build-slot queue and repeated database setup.

Each build/test driver used its own granted build-slot lease and native default outputs, with UseAppHost=false and TEMP=/tmp. Build lease 2160df0e-a2f8-483a-8e35-155b42a9d5e5 waited 285s and held 375s. Test lease 7b9d0539-221e-4544-8307-455286cc7f63 waited 75s and held 133s. The build used a 45m outer budget; the test run used an explicit 20m execution timeout. These generous bounds follow this Merge brief. Per-test durations and optional fixture phase journals were retained; unavailable phase attribution remains unknown.

Reproduction:

TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge73-solution -- dotnet build Antiphon.sln -p:UseAppHost=false -m:1 --nologo

TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge73-guards-witness-seats -- dotnet run --project tests/Antiphon.Tests -p:UseAppHost=false --no-build -- --treenode-filter '/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(ChannelInboundRecoveryTests*)|(GhostSeatReleaseTests*)/*' --timeout 20m --report-trx --report-trx-filename run.trx --results-directory <owned-results-directory>

pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll

Both graph checks used this checkout's freshly built evaluator; its source and Core dependency are unchanged between base and final. The base check ran against an isolated detached checkout, subsequently removed. The manifest committed on this base records 76C9D73ED09D1BEC69469863D61924D309974EE6B78FEB3CAC602D4512E53779, count 69; both real evaluations approved exactly that digest. No -Update ran.

Base and final -PrintInputDigest: 836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7.
Base and final Get-NightlyPolicyHash: 5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4.

Binary SHA-256 provenance:
01ce27fb4a85fc58cca14e5000be8d5ff9af7016330696398edb3095270beb68  tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll
62379e7125f07f643df99036512ad523cbb23ba799fb51c79b8047e0fe65ded1  tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Server.dll
c28b6253dd965db7aa598f8ae51c3f3d86bc08d08005df034ee276971dba5766  tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll

Batch 72 evidence carries forward by identical source patch-ids: CARD-1709 369 passed; wake-worker groups 473 passed with 13 Windows-only skips; seats/guards 139 passed; isolated distillation 12 passed. These are inherited receipts from 727a57dd, not tests rerun by this task. No whole-suite or checkpoint rerun was commissioned. CARD-1716 retains Full Review 606b7d8d; CARD-1719 retains its operator-accepted Interim master-only scope, without asserting RC qualification. CARD-1721 advisories remain unchanged. Standing authority covered the requested merge/land, so no further approval was requested.

RC handoff: the next rc cut must take all eight source-equivalent land commits in the table, subject to prerequisite checking. In particular CARD-1716's Telegram wake-worker fix is 95e3e5e9cc5f0c2e91eae36ecd53b988edebdded (source 97011c4258942ea64a1c3368aa3fb3df5ddce8f5) with 2c866f44a2f37d1d2baddc9078ceae606882c384 and ba499f6ca6d784e837d2e1e34039d6d14fc72d4e. CARD-1719's ghost-seat fix is d10e1c51a72852aa51895109b66e567f81346dca (source e76378e45adf6d8f8dcb92af1adf17b345ad4f84) with 7df541c289d389b027c273da03fadcad38b54ab0 and 5fa6eb655ec8ef115d2c488b1a37207698f4c03a. This task did not push rc, restart any service or run anything on the desktop; live Windows qualification b19a9880 was untouched. Master publication is not activation.

All foreground build/test/audit/push runs completed. The 104 owned bin/obj roots and the owned test-result directory were deleted using literal absolute paths after recording counts and provenance. The detached base checkout was removed. Console logs, per-test summaries, hashes and timing evidence remain ignored under /work/worktrees/task-c9ebcc1d/.antiphon/merge73. Tracked source was clean throughout verification and after cleanup. This report is published separately on the assigned task branch; master retains the verified product tip above.

--- next stage ---
next: done
handoff: Master landed; carry the listed CARD-1716 and CARD-1719 chains into the next reviewed rc cut and retain CARD-1721 advisories.
artifact: /work/worktrees/task-c9ebcc1d/.antiphon/task-c9ebcc1d.md
