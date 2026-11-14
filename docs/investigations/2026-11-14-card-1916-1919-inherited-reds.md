# CARD-1916..1919 inherited-red investigation

Debug task 55b46f7e. Base/current master at start: ca8737e78170c9a441e216408da5fab636c34ca0. Linux x64; time box 120 minutes. Work order: 1919, 1917, 1918, 1916. The original eight reds and timings are preserved in Debug 71aaddba's roster report; this task independently verifies relevant methods before repairs.

Verification scope: exact failing methods for diagnosis/bisection, then the three affected classes whole and the caller's seven-class guard union on the final tree. Builds and runs use build-slot.ps1, native default outputs and UseAppHost=false. Rows run serially, bounded below ten minutes. Per-run load, source, wall duration, slot output and TRX/phase evidence are retained under `.antiphon/evidence/debug-1916-1919/`. Unknown phases remain unknown. No new test timeouts are authorized.

Investigation in progress; this document will record first-bad commits, SQL differences, product/test attribution, omitted review selections, and final results. No green verdict yet.

## CARD-1919: stale selector expectation

Test wrong; selector closure correct. `f09330720303fce092d0803a69062bae1ce9c484` added C1514SamplerTickCpuTests; `673ebd43a434aeee5638f2c27b9e3f8beb378498` regenerated the derived index and added that class to the sampler dependencies. The why-missed expected SessionRunner filter was not updated. Preserve the new CPU witness and add it to the exact tuple expectation.

Independent baseline at 33e2322e3a1476d2bc48058999483a620c5df629: 24 executed, 16 passed, the same eight assertion failures, zero skipped. Wall 159.695s, slot wait 75s, held 83s; load 23.81/28.96/25.16 -> 17.63/24.31/24.01. Build: zero errors, 342.981s wall, 121s admission, 220s held; load 34.47/30.50/23.39 -> 24.41/29.16/25.21. TRX and all three complete SQL rosters retained in baseline-methods.

## CARD-1917: stale statement budgets after required safety reads

Test budgets wrong; product reads intended. In the detached scratch worktree, `git bisect run` (first-parent master history, queue-service path) identifies **4e4362beccf9b0be06c191990c4d323656809dad** as the first bad commit: fresh uncertain-attempt reconciliation adds one SELECT. At **5b80144d3cc7dcdcdb50c85ee03cd737e1508b41**, the exact 14-case method passes; at 4e4362bec it passes 11 and fails three, each by one query. The independent pre-change 282999c2c96f2118b142dd2635a5dbbbc12de730 run also passes 14/14.

**ecc9eb97130680c19e634f2a6ce236e61991cd3a** adds the second SELECT, preventing composer-less unresolved attempts from receiving another Enter or body. Current master counts are 72/67/67. Both reads implement the documented CARD-1892 input-uncertainty contract; neither is a repeated kind-only probe. Keep exact budgets, changed by exactly two with their provenance. No product, timeout or assertion-tolerance change for this card.

All three complete old/current SQL rosters, per-step TRX, timings and `git bisect log` are under the evidence root. `sql-diff.txt` retains the literal query differences (including unrelated current-master SQL text changes and compiler parameter renames). Existing-brief new probes are statements 37/38; watchdog new probes 35/36. One selects Sent/null-verdict charged non-maintenance rows with a Running non-Claude/non-Codex owner, ordered by Sequence; the other selects fresh Sent/null-verdict rows with LastDeliveryStartedAt above freshFloor, ordered by Sequence.

## Why the checks were missed

## CARD-1918: cancellation misclassified outside the Input boundary

Product wrong. The first-parent, AgentSessionService-path `git bisect run` identifies **5b80144d3cc7dcdcdb50c85ee03cd737e1508b41**: C1150_Running_cut_is_discovered_and_delivered_once executes once and fails its original OperationCanceledException assertion. That commit applies InputDeliveryUncertainException.Is to the entire recovered-launch try block, including SessionStarted publication. A synthetic publication cancellation is swallowed because the caller token is not canceled. This is not an Input attempt and must propagate without killing the attached process.

Minimal repair: this recovered-launch catch accepts only the typed InputDeliveryUncertainException. GrokRulesRefreshService.InitializeAsync already emits that type for unresolved rules Input; queue flushes already retain their own ambiguous writes. Keep ct.ThrowIfCancellationRequested and every existing cut, pending-brief, no-kill, single-UserPrompt and repeated-discovery assertion. No timeout or cancellation assertion is changed. Product change requires ordinary Review.

- CARD-1919: sampler Review **2cef094f-670c-454f-aeaa-8047c6cd752a**, source 1a79c5119bce4c36e07e81bb43f5030ef072a4bf, ran named SessionRunner classes and an eleven-class Antiphon.Tests guard filter (116 passed). Its stored report lists the filter, which omits ImpactedClassesSelectorTests. The subsequent a9f8cd21 repair reports 23 named SessionRunner checks plus 80 guards, also omitting it. The new class/index dependency was covered; the exact selector output consumer was not.
- CARD-1917/1918/1916: CARD-1892 Reviews **d552541f-0d4b-42be-9386-7c2f8f495a38** (round 1) and **caeec914-1116-45ef-b19e-5e087159296f** (round 2), and the round-3 plan, use CP-2's eight-class queue/rules/launch union and CP-3's guards. They omit DelegationDispatchRecoveryBoundaryTests and AgentTaskMidTurnRefineTests. Their findings concerned other real defects; do not relabel those historical Found outcomes as Clean. Merge 218's b445b5254e438c1e4313a51846ac5f1d0bc6b083 repair touches SessionRuntimeActionRecoveryTests and BridgeQueueHarness, not these two omitted classes. Stored review reports and stage-outcome metadata were retrieved read-only for this audit.
