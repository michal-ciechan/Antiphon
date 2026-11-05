Batch 72's candidate is verified and pushed; master publication is blocked because master advanced after the task branch was published.

Tested candidate: 4ef4cfd2c435aca17cbb0658dca95a27415a8bb3 on feat/card-task-727a57dd.
Original base: 28cdefab326782f2c91ca361768a7252dc875ec7.
Final fetched master: 3ce7f87ef06315ace62572d7e51b2de0347dbef9. No new master SHA was published by this task.

Required caller action: commission a fresh Merge checkout based on current master and replay the eight source commits below. The assigned branch contract forbids rebasing, resetting, amending, or merging master into this already-pushed branch. All eight picks were pushed individually. No master push was attempted because the fast-forward ancestry precondition is false; the failed-push retry loop was not entered. No force push was used.

Master added only 3ce7f87ef06315ace62572d7e51b2de0347dbef9, which changes tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs (47 insertions, 1 deletion). git merge-tree --write-tree origin/master 4ef4cfd2c435aca17cbb0658dca95a27415a8bb3 exited 0, tree b019795619aa91b3e31fa22c42c357f48fcb8f31. There were no cherry-pick conflicts or conflict resolutions. The combined tree with this later master commit was not built or tested: the current-master landing gate remains incomplete.

Standing authority was used for CARD-1709's test-only land and CARD-1719's operator-accepted Interim master-only scope; no approval was requested. CARD-1716 has Clean Full Review 606b7d8d; CARD-1719 has Review bbd446f6 at Interim scope. CARD-1721 advisories remain unchanged. No source repairs were added by this Merge.

Each row is a cherry-pick -x. The candidate SHA is not a master land SHA. Stable patch-ids match the source for every row.

| Candidate commit | Source commit | Stable patch-id |
|---|---|---|
| a983d96b9dbcdaca3e003eb9d443505d0386e4e9 | 8f869e5600d9aa937b2831e8c71b124f0cbacbeb | 23f49d4061180fb3f8a17491fe82bc56d3175005 |
| 01aa10939250f930c17406e8e497c1d63e7c8a00 | 2baea8e38761ddfe88b926c55abf6dc67cd40d20 | 7381539fa489e08134450e1bc77df1717c2a3ad3 |
| 3ddef044c127f96ce5500ef64d081df5f84a47f3 | 9f291e9263418f243a05c487479ff1f98f142730 | 959409f4e3927705d0162aede813013237b70417 |
| af929bf2a293594fe9524b75c4a1dea8238b81dd | 19a56f66cd22588a0e2b99f8ea654e8b915f42bd | 9a5b64bad25663543aa0687b67ff1811b2198a6b |
| 1b44c85685fe39c5bec4517dae04458b021415a1 | 97011c4258942ea64a1c3368aa3fb3df5ddce8f5 | 5c3d05b7994ddfa738e4252ddd17deb9ee3f85e5 |
| e851e7309da8fd2346fcfb69686a34191889a2f7 | 9d7f67fe3d47ae4b71569e0e7f286822e787d939 | ff7ff63d6a501079ee5a0936d1819a0805616dad |
| 368af62dc1c1fe5360259cb3e5f7dcda2ffc2477 | c249c4ef70ef5e8e36d8347aabaa20e671af434a | 1cd2c35e2653355ee0e15ab0b7e87df9bb90b790 |
| 4ef4cfd2c435aca17cbb0658dca95a27415a8bb3 | e76378e45adf6d8f8dcb92af1adf17b345ad4f84 | bcba5b22381776e57f14e865a8cbe6996f922dbf |

The first two rows are CARD-1709; the next three CARD-1716; the final three CARD-1719. git cherry origin/master <source-branch> showed 9f291e9263418f243a05c487479ff1f98f142730 as '+'. It adds the C1716 witness file; 19a56f66cd22588a0e2b99f8ea654e8b915f42bd only adds its missing import. Both were therefore picked, correcting the brief's description of that history. Report-only 419d8feb90ef21e10b8e713fc483a12a48579b66 and 05da67f7853cad8c5c3726a29173976e1b5017a8 were skipped.

The 18 changed paths contain the CARD-1709 shutdown/retry/order witnesses, CARD-1716's bounded wake-worker queue waits, five-second retry, heartbeat route and stall incident, and CARD-1719's clean-equal Failed seat release plus witnesses and supporting documentation. All changes are exact source patches.

All following build/test runs verified 4ef4cfd2c435aca17cbb0658dca95a27415a8bb3, with native default outputs, UseAppHost=false and TEMP=/tmp. Each driver had its own build-slot lease. The named test selections were the brief's final-tree scope; the whole suite and checkpoint tool were not run.

| Run | Passed | Failed | Skipped | Runtime | Slot wait / held |
|---|---:|---:|---:|---|---|
| CARD-1709 G1, all 13 classes | 369 | 0 | 0 | 11m08s | 601s / 676s |
| Wake A: ChannelBridge*, ChannelInbound*, AgentTaskLandNotificationRecoveryTests* | 141 | 0 | 0 | 3m54s | 391s / 238s |
| Wake B: SessionMessageQueue*, TaskSupervisionReceiptTests* | 332 | 0 | 13 | 6m26s | 375s / 389s |
| CARD-1719 seats plus guards | 139 | 0 | 0 | 4m59s | 135s / 305s |
| Three requested distillation methods alone, every argument case | 12 | 0 | 0 | 1m20s | 195s / 86s |

G1's TRX contains all 13 requested classes and 369 tests. Wake A contains all four C1716 witness cases, all Passed, and 49 land-notification recovery tests. Wake B's skips are the eight SessionMessageQueuePtyIntegrationTests and five SessionMessageQueueGrokPtyIntegrationTests cases gated to Windows. The wake groups passed 473 in total; adding the 47 guards gives 520, which is Review's 467 plus the 49 land-notification tests and four new C1709 witnesses.

The 139 seats/guards results split into 92 seat/pool tests (AgentTaskPool 48, BlockedTaskParkRelease 3, GhostSeatRelease 9, RunnerSlotEndpoint 10, TaskSupervisionVisibility 22) and 47 guards (TestClassificationPolicy 39, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2).

G1 had no OutputDistillationDeliveryTests failures. The requested isolated control nevertheless reran Composed_summary_and_metadata_obey_utf8_spill_guard, Unusable_file_uses_api_recovery, and Legacy_minimum_and_unicode_boundaries: all 12 argument cases passed on the same build.

Build history is retained: the first parallel Antiphon.sln build failed after 13m23s with 3 errors (NETSDK1047 for the nested native PtyHost assets and MSB3073), 1845 warnings; lease 420d2620-ab11-40dd-84ed-99a916c66bae, wait 15s, held 804s. Its log shows overlapping RID and RID-less restores of the shared PtyHost assets. This failure was not reproduced at base, and no product-regression attribution is claimed. A fresh gated solution build with -m:1 passed in 1m07s, 0 errors, 7 warnings, zero NETSDK1047 occurrences; lease 3d97e93a-6094-4e00-9b44-9e0e51cf5380, wait 331s, held 67s. This additional build was justified by the failed required build; no source/project change or timeout relaxation was made. A local wrapper was also stopped before any broker or test child launched, then replaced by the direct gated driver; no test run was interrupted.

Reproduce the successful compile with:
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge72-solution -- dotnet build Antiphon.sln -p:UseAppHost=false -m:1 --nologo

Test commands use the same properties and --no-build, through build-slot.ps1, with --treenode-filter, --report-trx, --report-trx-filename run.trx and an owned --results-directory. The ordinary run timeout was 30m; the isolated 12-case control used 10m. Filters:

G1: /*/*/(TaskSupervisionReceiptTests*)|(PostLandMutationDeliveryTests*)|(CompletionNoteWorkQueueTests*)|(CompletionNoteDurableSweepTests*)|(OutputDistillationDeliveryTests*)|(OutputDistillationProducerTests*)|(OutputDistillationCleanupTests*)|(RunnerAlarmDeliveryTests*)|(StaleWorkingTranscriptWatchdogTests*)|(DataRetentionServiceTests*)|(CheckCompactionCrashTests*)|(CheckCompactionWorkerLifetimeTests*)|(CheckCompactionFixtureTests*)/*
Wake A: /*/*/(ChannelBridge*)|(ChannelInbound*)|(AgentTaskLandNotificationRecoveryTests*)/*
Wake B: /*/*/(SessionMessageQueue*)|(TaskSupervisionReceiptTests*)/*
Seats/guards: /*/*/(GhostSeatReleaseTests*)|(TaskSupervisionVisibilityTests*)|(RunnerSlotEndpointTests*)|(AgentTaskPoolTests*)|(BlockedTaskParkReleaseTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)/*
Distillation alone: /*/*/OutputDistillationDeliveryTests*/(Composed_summary_and_metadata_obey_utf8_spill_guard*)|(Unusable_file_uses_api_recovery*)|(Legacy_minimum_and_unicode_boundaries*)

Input digests were measured before picking at the original base, then again at the candidate:

| Input | Base value | Candidate value |
|---|---|---|
| -PrintInputDigest | 836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7 | 836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7 |
| Get-NightlyPolicyHash | 5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4 | 5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4 |

Definitive land-graph -Check on both the original base and candidate exited 0 and approved digest 76C9D73ED09D1BEC69469863D61924D309974EE6B78FEB3CAC602D4512E53779, count 69. -Update was never run. Both checks used the evaluator built by this task at tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll; its source is identical between base and candidate. The base check used a temporary detached checkout, subsequently removed. Earlier approvals from an older cached evaluator were superseded by these current-source checks. Reproduce with scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll after building.

Binary SHA-256 provenance:
- Antiphon.Tests.dll: a25d54fb1391af4c95ab6fef4ffd60c977ab34fa4ab93841b1333cdf6afca967
- test output Antiphon.Server.dll: 160cd4df83dabfb33e4235fc78368137a5b017fb35cf23074303b66a4f6385ce
- current LandGraphAudit.dll: 1780b4d1e6518540df5b37dc9f621266331a32d0cb639847573b8e53b5a738e7

check-evidence-diff over original base..candidate exited 0: 8 commits, 0 entries, 0 violations. All stable patch-ids matched. Source was tracked-clean throughout verification. Owned bin/obj/test-result outputs were deleted after the counts were recorded; logs remain ignored under .antiphon/merge72. No delegate/test/build child remains running.

The next rc cut still needs all eight source commits in the table, after a fresh master landing. CARD-1716's product fix 97011c4258942ea64a1c3368aa3fb3df5ddce8f5 is the Telegram wake-worker port, with its two witness commits. CARD-1719's product fix e76378e45adf6d8f8dcb92af1adf17b345ad4f84 needs its two witness/compile commits; its Interim master-only acceptance does not claim RC qualification or activation. CARD-1721 advisories remain for the caller. This task made no rc push, desktop run, runner restart, or AppHost restart; live qualification b19a9880 was untouched.

--- next stage ---
next: blocked
handoff: Commission a fresh Merge checkout from current master (last fetched 3ce7f87ef06315ace62572d7e51b2de0347dbef9); replay the eight exact source commits above, complete the current-master graph/compile gate, and fast-forward publish. Candidate 4ef4cfd2c435aca17cbb0658dca95a27415a8bb3 has all requested targeted checks passing. Its published task branch cannot be rebased or merged with master under this commission.
artifact: .antiphon/task-727a57dd.md