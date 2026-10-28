# Task 0c5e753f: measured group 28 and group 65 class costs

Measured serial costs from run 8f055f87 split group 28 and group 65 so each chunk price covers its measured need and stays under the 3600 s watchdog. The antiphon watchdog stays 3600000 ms.

Rows store the raw journal serial test-seconds, basis measured. Doubling WorktreeGuardedCleanup would be 3186 s, past the 2400 s lone-class launch ceiling, and would method-split with no per-method times. The largest raw class is 1593 s. Same about-1x storage as CARD-1404. One Windows run. Serial seconds overstate wall time where git work runs in parallel.

Commits on feat/card-task-0c5e753f, base f84228f285dd3e4ad7b5dc8260cbc9f819256f50:

- 7db0687cb3b3aa5712102077210142f5fd5af3c7 red G2865 test
- 9a16e2cbcc33d2330d8f214d7e03ee58d7cb78bd 12 measured rows and the c1410 pin

Files: scripts/lib/nightly-class-costs.json, scripts/test-nightly-tests.ps1, tests/Antiphon.Tests/Scripts/C1514LandingSplitBudgetTests.cs.

## Before

Same 17 plus 28 synthetic classes at model prices. Group 28 was 1 chunk, 17 classes, 900 s, measured need 3831 s. Group 65 was 2 chunks: 27 classes at 890 s (need 4060 s) and HostCleanupPolicy alone at 11 s (need 0). The live Windows plan was one 900 s chunk of 28; this set spills one class because the model sum exceeds 900 s.

## After

Resolve-NightlySuiteChunks with rc/windows exclusions and journal node counts: 5 and 5 chunks, 10 launches, watchdog 3600000.

Group 28 prices/needs: 1110/1110, 907/907, 879/877, 887/825, 563/112.

Group 65 prices/needs: 1593/1593, 942/942, 891/857, 899.5/668, 391.5/0.

The four singleton prices are PostLandMutationCustody 1110, BlockedTaskParkReclaim 907, WorktreeGuardedCleanup 1593, and CompletedCardWorktreeRemoval 942.

Locked c1410 listing (19012 nodes): launches 107 to 114, suite seconds 52631.653116531146 to 59699.10480689747 (16.58 h). Max launch stayed 2088 s. DelegateBranchDelivery, RepairSourceDispatch, and CompletedCardWorktreeRemoval are priced at the journal rate times the shorter listing count (3/7, 39/41, 36/37).

## Rows

nodes/seconds:

- BlockedTaskParkDeliveryTests 6/575
- BlockedTaskParkReclaimTests 38/907
- DelegateBranchDeliveryTests 7/334
- PostLandMutationCustodyTests 56/1110
- RepairSourceDispatchTests 41/302
- RepairSourceSettlementTests 36/491
- SessionRuntimeActionRoutingTests 35/112
- CompletedCardWorktreeRemovalTests 37/942
- LandingAdmissionControlTests 42/96
- LandingRemovalControlTests 34/761
- SettledWorktreeRemovalTests 78/668
- WorktreeGuardedCleanupTests 49/1593

LandingAdmission's 42 is the c1410 listing and this checkout; the report printed 96 s and no test count. Sources cite run 8f055f87, journals cases-239920-ac1b3db4d0fa429d9c2c95ac1ab391a1.jsonl (group 28) and cases-269272-6d44809b271d4f07935b60d58442bb4f.jsonl (group 65), candidate rc-2026-10-25-105792f7d22f, investigation 3d4bd26f60b2c9ad41ba610caaabd2ef8ee1d0ea.

## Left unmeasured

- Group 28 early bucket, 295 tests / 166 s combined (HerdrLabelFollow, AgentCreateRace, OutputDistillationGate, AgentTaskServiceIntegration, TaskInputAttentionCost). Below the combined model. No per-class split.
- Group 65 LandRecoveryCheckout + LandingGit + LandRecoveryIgnoredNested, 107 tests / 643 s. No per-class split.
- LandingSourceFreshness + LandingSourceBoundaryControl + RepositoryChildRecovery, 66 tests / 271 s. No per-class split.
- GitServiceTests 13 s, under the 18 s model.
- ChannelBridgeTests 27 of 40 and 66 s, killed mid-class.
- WorktreeManagerGitIntegrationTests 16 of about 29 and 56 s at the kill.
- Remaining rest bucket 34 s, no per-class split.
- Four unstarted group 28 classes (ChannelInboundRecovery, CodexStartupDelivery, LandEvidenceWarningDelivery, ApiErrorRecoveryService) have only a combined ~192 s from failed-fast run a779085c.

## Tests

One slot build of tests/Antiphon.Tests (UseAppHost=false, OutputPath=bin-g2865/, lease 833b3156, 0 errors), then --no-build, 10 passed / 0 failed in 1m 13s (lease 84171d52): C1514 C1584, C1577, C1404, C1410, G2865; RcRosterRepair Mirror, Explicit, Costs; TestPhaseSupervisor C1514T_PlannedManifestAndEnvironment and C1514T_ActualAndFailurePaths. Script G2865 reprint: 1 passed, 0 failed. NightlyDiscoveryParserTests and NightlyVerificationContractTests do not load the cost file. bin-g2865 removed.

## Land

origin/rc/2026-10-27 is still f84228f285dd3e4ad7b5dc8260cbc9f819256f50, so this branch fast-forwards rc. merge-tree against origin/master 0794458dbb5c381116eb00ae6e894315ffc030d5 exited 1. Content conflicts in scripts/lib/nightly-class-costs.json and scripts/test-nightly-tests.ps1 (master's CARD-1577 and E2E cost pins edit both files; these 12 classes are absent on master). Add/add conflicts on .antiphon/task-af2b78c8.md and docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md, which this task left untouched. Left unresolved.

--- next stage ---
next: review
handoff: review the 12 measured cost rows and the G2865 chunk proof
artifact: scripts/lib/nightly-class-costs.json
