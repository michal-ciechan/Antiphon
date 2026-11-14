# CARD-1916..1919 inherited-red investigation

Debug task 55b46f7e. Base/current master at start: ca8737e78170c9a441e216408da5fab636c34ca0. Linux x64; original time box 120 minutes, followed by an authorized 40-minute reopening. Work order: 1919, 1917, 1918, 1916. The original eight reds and timings are preserved in Debug 71aaddba's roster report; this task independently verifies relevant methods before repairs.

Verification scope: exact failing methods for diagnosis/bisection, then the three affected classes whole and the caller's seven-class guard union on the final tree. Builds and runs use build-slot.ps1, native default outputs and UseAppHost=false. Rows ran serially; every build/test execution held a slot for less than ten minutes. Admission is separate: the original-pass guard row was 680.792s wall including 135s queued, with 543s held. Per-run load, source, wall duration, slot output and TRX/phase evidence are retained under `.antiphon/evidence/debug-1916-1919/`. Unknown phases remain unknown. No new test timeouts are authorized.

**Complete: all eight inherited failures are repaired. Final reopened verification: 562 executed, 562 passed, zero failed or skipped.** CARD-1916/1917/1919 are test corrections; CARD-1918 is the only product change and requires ordinary Review. Merge must wait for that Review. The earlier two-red result and its costs below are historical evidence, superseded by the reopened verification.

## CARD-1919: stale selector expectation

Test wrong; selector closure correct. `f09330720303fce092d0803a69062bae1ce9c484` added C1514SamplerTickCpuTests; `673ebd43a434aeee5638f2c27b9e3f8beb378498` regenerated the derived index and added that class to the sampler dependencies. The why-missed expected SessionRunner filter was not updated. Preserve the new CPU witness and add it to the exact tuple expectation.

Independent baseline at 33e2322e3a1476d2bc48058999483a620c5df629: 24 executed, 16 passed, the same eight assertion failures, zero skipped. Wall 159.695s, slot wait 75s, held 83s; load 23.81/28.96/25.16 -> 17.63/24.31/24.01. Build: zero errors, 342.981s wall, 121s admission, 220s held; load 34.47/30.50/23.39 -> 24.41/29.16/25.21. TRX and all three complete SQL rosters retained in baseline-methods.

## CARD-1917: stale statement budgets after required safety reads

Test budgets wrong; product reads intended. In the detached scratch worktree, `git bisect run` (first-parent master history, queue-service path) identifies **4e4362beccf9b0be06c191990c4d323656809dad** as the first bad commit: fresh uncertain-attempt reconciliation adds one SELECT. At **5b80144d3cc7dcdcdb50c85ee03cd737e1508b41**, the exact 14-case method passes; at 4e4362bec it passes 11 and fails three, each by one query. The independent pre-change 282999c2c96f2118b142dd2635a5dbbbc12de730 run also passes 14/14.

**ecc9eb97130680c19e634f2a6ce236e61991cd3a** adds the second SELECT, preventing composer-less unresolved attempts from receiving another Enter or body. Current master counts are 72/67/67. Both reads implement the documented CARD-1892 input-uncertainty contract; neither is a repeated kind-only probe. Keep exact budgets, changed by exactly two with their provenance. No product, timeout or assertion-tolerance change for this card.

All three complete old/current SQL rosters, per-step TRX, timings and `git bisect log` are under the evidence root. `sql-diff.txt` retains the literal query differences (including unrelated current-master SQL text changes and compiler parameter renames). Existing-brief new probes are statements 37/38; missing-brief and watchdog new probes 35/36. One selects Sent/null-verdict charged non-maintenance rows with a Running non-Claude/non-Codex owner, ordered by Sequence; the other selects fresh Sent/null-verdict rows with LastDeliveryStartedAt above freshFloor, ordered by Sequence.

## CARD-1918: cancellation misclassified outside the Input boundary

Product wrong. The first-parent, AgentSessionService-path `git bisect run` identifies **5b80144d3cc7dcdcdb50c85ee03cd737e1508b41**: C1150_Running_cut_is_discovered_and_delivered_once executes once and fails its original OperationCanceledException assertion. That commit applies InputDeliveryUncertainException.Is to the entire recovered-launch try block, including SessionStarted publication. A synthetic publication cancellation is swallowed because the caller token is not canceled. This is not an Input attempt and must propagate without killing the attached process.

Minimal repair: this recovered-launch catch accepts only the typed InputDeliveryUncertainException. GrokRulesRefreshService.InitializeAsync already emits that type for unresolved rules Input; queue flushes already retain their own ambiguous writes. Keep ct.ThrowIfCancellationRequested and every existing cut, pending-brief, no-kill, single-UserPrompt and repeated-discovery assertion. No timeout or cancellation assertion is changed. Product change requires ordinary Review.

## CARD-1916: distinct stale-fixture and contract issues

Test wrong for the changed Input contract. `git bisect run` identifies **5b80144d3cc7dcdcdb50c85ee03cd737e1508b41** as first bad for the transport arm: 3/4 passed, that arm failed. The transport/refusal fixture throws before RecordingInterruptRunner records any input. HttpRequestException cannot prove that boundary on a real transport; CARD-1892 intentionally retains ambiguous writes. Change this retry arm to a definitive runner problem (409, fixture_input_refused), name it flush-definitive-refusal-then-retry, and preserve its original raw Pending, attempt-count, no-input and subsequent single-body retry assertions. This is test repair, not a product rollback or a Delivered marker on an unconfirmed row.

Original-pass state before the caller decision (historical): the two aged Grok no-prompt cases still had their original raw Pending/second-attempt assertions. The four-case method passes at 4e4362beccf9b0be06c191990c4d323656809dad; `git bisect run` identifies **ecc9eb97130680c19e634f2a6ce236e61991cd3a** as first bad, with 2/4 passed and the two no-prompt cases failed. The brief explicitly forbids weakening raw Pending assertions, while docs/session-runtime-invariants.md's Input uncertainty section explicitly requires holding Grok Sent/null-verdict attempts without Enter/retype. A clarification is pending; no dependent change is authorized by silence. These are real unconfirmed attempts, not Merge 218's already-confirmed Sent fixtures, so seeding Delivered would conceal the obligation.

## Why the checks were missed

- CARD-1919: sampler Review **2cef094f-670c-454f-aeaa-8047c6cd752a**, source 1a79c5119bce4c36e07e81bb43f5030ef072a4bf, ran named SessionRunner classes and an eleven-class Antiphon.Tests guard filter (116 passed). Its stored report lists the filter, which omits ImpactedClassesSelectorTests. The subsequent a9f8cd21 repair reports 23 named SessionRunner checks plus 80 guards, also omitting it. The new class/index dependency was covered; the exact selector output consumer was not.
- CARD-1917/1918/1916: CARD-1892 Reviews **d552541f-0d4b-42be-9386-7c2f8f495a38** (round 1) and **caeec914-1116-45ef-b19e-5e087159296f** (round 2), and the round-3 plan, use CP-2's eight-class queue/rules/launch union and CP-3's guards. They omit DelegationDispatchRecoveryBoundaryTests and AgentTaskMidTurnRefineTests. Their findings concerned other real defects; do not relabel those historical Found outcomes as Clean. Merge 218's b445b5254e438c1e4313a51846ac5f1d0bc6b083 repair touches SessionRuntimeActionRecoveryTests and BridgeQueueHarness, not these two omitted classes. Stored review reports and stage-outcome metadata were retrieved read-only for this audit.

## Original-pass verification and disposition (historical; superseded)

Final code/index source: **2c3b91248b657889fc7dd3f8f48c0d312e75dfe6**. Both final rows began and ended at this SHA with clean tracked source/index, and both reused the final native build, UseAppHost=false. Antiphon.Tests.dll SHA-256: `c11efe41204d6c99bfb47e36c2366787edec3dfdf7095fb059122cdecaff2697`. These are direct-run records, not an asserted checkpoint or Review source-clean receipt.

**561 executed / 559 passed / 2 failed / 0 skipped.** All three affected classes ran whole. The adjacent nine-case interrupted-launch class protects the changed recovered-launch catch. Packing related classes into one integration host shares Postgres bootstrap; the selector and exact requested seven-class guard union ran in a second serial host.

| Class | Passed | Failed | Skipped |
|---|---:|---:|---:|
| DelegationDispatchRecoveryBoundaryTests | 382 | 0 | 0 |
| AgentTaskMidTurnRefineTests | 36 | 2 | 0 |
| AgentSessionInterruptedLaunchResumeTests | 9 | 0 | 0 |
| C1729LayeringGuardTests | 10 | 0 | 0 |
| SlowTestTripwireTests | 2 | 0 | 0 |
| NativeLinuxPolicyWitnessTests | 3 | 0 | 0 |
| TestClassificationGuardTests | 1 | 0 | 0 |
| DerivedImpactIndexTests | 27 | 0 | 0 |
| SlowTestRegistryMergeTests | 5 | 0 | 0 |
| ImpactedClassesSelectorTests | 42 | 0 | 0 |
| TestClassificationPolicyTests | 42 | 0 | 0 |

The only final failures are `C1169_AgedScreenOnlyRefinementFollowsInterruptedSentRecovery(no-prompt-enter-only)` (raw Status expected Pending, actual Sent) and `(no-prompt-retype)` (DeliveryAttempts expected 2, actual 1). Both are inherited at the measured first-bad hold commit. The final four-case definitive-refusal method and the original Running-cut cancellation method pass; all 14 exact statement budgets pass.

**Original decision request, resolved by the reopening:** permit the two obsolete aged-Grok expectations to adopt the intentional Sent/null-verdict hold, keeping refinementDelivered pending, one charged attempt and no automatic Enter/retype until complete UserPrompt; or explicitly require a product contract change for refinement rows. The brief forbids weakening the raw Pending assertion while the runtime owner explicitly forbids the automatic Enter/retype these tests expect. The clarification asked during the task remains unanswered. Existing raw Pending/attempt assertions are preserved. No Delivered marker was added to unconfirmed rows. No timeout or cancellation assertion was loosened.

The original recommendation was to retain CARD-1892’s hold and correct these two tests after resolving that explicit brief constraint. Verification remains incomplete until then; do not merge this branch as green. Product repair still owes ordinary Review. Repairs proceeded under the caller’s standing Debug authority; no restart, deployment, live run, or production volume was touched.

## Index and pinned inputs

Built Antiphon.DerivedImpactIndex first, separately gated. A single leased generation helper then invoked the canonical regeneration script twice with -NoBuild: first printed `derived-impact-index changed`; second printed `derived-impact-index unchanged`. No builds occurred inside that helper. The only generated source delta adds AgentTaskMidTurnRefineTests to RunnerProblemException.cs consumers and was committed separately.

Protected nightly inputs are byte-unchanged from the task base. policyHash stays `355a567e792404412f25cfbfd138a117b42def354739e26b037889dd03d72233`; live-lane PinnedInputDigest stays `0854031a9cc816e959f182bac02aa28e5be420abd2fd1b43113d7bcee1f669bd`. No repin.

## Original-pass evidence and phase limits

Evidence root: `/work/worktrees/task-55b46f7e/.antiphon/evidence/debug-1916-1919/`. Exact commands, per-test TRX duration/membership, full SQL rosters, all four bisect logs, per-run load/source/duration JSON, native console output and final phase journals remain ignored there. The detached scratch worktree was source-clean, its bisect state reset, then removed via its exact literal absolute path after all owned commands finished. No scratch commit, target merge, branch switch, reset/rebase of a pushed branch, or force-push occurred.

Historical phase journals are unavailable: the initial recorder setup supplied its root without required identity metadata. This was corrected for both final rows. Final journals have 429 and 132 complete cases respectively, health failure=null and droppedPhaseCount=0. Integration recorded phase union is 60.888s (db-create union 25.449s, db-cleanup 24.670s, fixture-setup 10.774s; unions overlap). Selector/guards recorded phase union is 2.070s (process-spawn union 0.110s, process-wait 2.070s). The remaining slot-held wall is uninstrumented/remainder, not assigned to startup, teardown, CPU or individual external calls. Per-query/HTTP timing is not separately extracted. No unavailable phase is reported as zero.

TRX SHA-256: integration `5108b172c4e9614cf34b4048e28e6f2ab5668f61bd661707ad1271b14f955e7e`; selector/guards `bf4187c36d9065ca035ed94cf615f92ea6f0623bf636d346c740d92e6aef69b0`.

## Original-pass run costs

Linux x64, Debian 12, TUnit 1.44.0 / .NET runtime 9.0.23. Load is host 1/5/15-minute load at invocation and exit. Wall includes admission and wrapper overhead; held is the broker’s integer-second execution window. n=1 for each row; these are observations, not p95 or a new timeout policy. Earlier reds and their costs are retained.

| Run | Source | Executed/passed/failed/skipped | Wall s | Slot wait s | Held s | Load start -> end |
|---|---|---|---:|---:|---:|---|
| baseline-build | 33e2322e3a1476d2bc48058999483a620c5df629 | build/tool exit 0 | 342.981 | 121 | 220 | 34.47/30.50/23.39 -> 24.41/29.16/25.21 |
| baseline-methods | 33e2322e3a1476d2bc48058999483a620c5df629 | 24/16/8/0 | 159.695 | 75 | 83 | 23.81/28.96/25.16 -> 17.63/24.31/24.01 |
| bisect-aged-4e4362becc-build | 4e4362beccf9b0be06c191990c4d323656809dad | build/tool exit 0 | 289.106 | 60 | 228 | 14.24/20.34/25.58 -> 23.89/22.48/24.95 |
| bisect-aged-4e4362becc | 4e4362beccf9b0be06c191990c4d323656809dad | 4/4/0/0 | 89.196 | 0 | 87 | 23.89/22.48/24.95 -> 26.05/23.25/24.99 |
| bisect-aged-ecc9eb9713-build | ecc9eb97130680c19e634f2a6ce236e61991cd3a | build/tool exit 0 | 519.826 | 270 | 248 | 26.05/23.25/24.99 -> 33.94/33.33/29.24 |
| bisect-aged-ecc9eb9713 | ecc9eb97130680c19e634f2a6ce236e61991cd3a | 4/2/2/0 | 125.987 | 75 | 50 | 33.94/33.33/29.24 -> 26.60/28.91/28.03 |
| bisect-budgets-282999c2c9-build | 282999c2c96f2118b142dd2635a5dbbbc12de730 | build/tool exit 0 | 328.948 | 15 | 312 | 16.84/23.73/23.83 -> 40.56/31.17/26.78 |
| bisect-budgets-282999c2c9 | 282999c2c96f2118b142dd2635a5dbbbc12de730 | 14/14/0/0 | 259.955 | 195 | 64 | 40.56/31.17/26.78 -> 21.80/28.86/27.07 |
| bisect-budgets-4e4362becc-build | 4e4362beccf9b0be06c191990c4d323656809dad | build/tool exit 0 | 369.271 | 75 | 293 | 19.15/27.78/26.76 -> 35.05/37.86/31.81 |
| bisect-budgets-4e4362becc | 4e4362beccf9b0be06c191990c4d323656809dad | 14/11/3/0 | 145.563 | 30 | 114 | 35.05/37.86/31.81 -> 20.08/30.62/29.97 |
| bisect-budgets-5b80144d3c-build | 5b80144d3cc7dcdcdb50c85ee03cd737e1508b41 | build/tool exit 0 | 299.951 | 75 | 224 | 19.59/30.34/29.88 -> 26.52/28.52/29.08 |
| bisect-budgets-5b80144d3c | 5b80144d3cc7dcdcdb50c85ee03cd737e1508b41 | 14/14/0/0 | 233.794 | 165 | 68 | 26.52/28.52/29.08 -> 21.90/28.39/29.19 |
| bisect-cut-5b80144d3c-build | 5b80144d3cc7dcdcdb50c85ee03cd737e1508b41 | build/tool exit 0 | 164.982 | 120 | 44 | 19.45/27.65/28.94 -> 33.23/29.27/29.29 |
| bisect-cut-5b80144d3c | 5b80144d3cc7dcdcdb50c85ee03cd737e1508b41 | 1/0/1/0 | 209.992 | 166 | 42 | 33.23/29.27/29.29 -> 12.84/20.52/25.75 |
| bisect-good-282999c2c9-build | 282999c2c96f2118b142dd2635a5dbbbc12de730 | build/tool exit 0 | 292.468 | 60 | 230 | 44.70/33.74/29.74 -> 29.59/32.55/30.38 |
| bisect-good-282999c2c9 | 282999c2c96f2118b142dd2635a5dbbbc12de730 | 9/9/0/0 | 86.153 | 30 | 54 | 29.59/32.55/30.38 -> 23.07/30.08/29.73 |
| bisect-refine-5b80144d3c-build | 5b80144d3cc7dcdcdb50c85ee03cd737e1508b41 | build/tool exit 0 | 309.616 | 15 | 292 | 25.40/30.33/29.81 -> 44.48/38.84/33.57 |
| bisect-refine-5b80144d3c | 5b80144d3cc7dcdcdb50c85ee03cd737e1508b41 | 4/3/1/0 | 100.517 | 45 | 54 | 44.48/38.84/33.57 -> 23.65/32.61/31.84 |
| final-build | 2c3b91248b657889fc7dd3f8f48c0d312e75dfe6 | build/tool exit 0 | 364.735 | 120 | 244 | 22.43/28.20/30.39 -> 26.98/26.37/28.43 |
| final-integration | 2c3b91248b657889fc7dd3f8f48c0d312e75dfe6 | 429/427/2/0 | 207.249 | 60 | 146 | 24.46/25.84/28.24 -> 27.43/27.70/28.44 |
| final-selector-guards | 2c3b91248b657889fc7dd3f8f48c0d312e75dfe6 | 132/132/0/0 | 680.792 | 135 | 543 | 27.40/27.67/28.41 -> 33.98/27.31/28.89 |
| index-driver-build | 8ffed72a49ee2b583a74c24b837cfab0de71e3e2 | build/tool exit 0 | 268.920 | 256 | 11 | 22.59/31.95/31.64 -> 32.00/31.98/31.73 |
| index-generation | 8ffed72a49ee2b583a74c24b837cfab0de71e3e2 | build/tool exit 0 | 153.881 | 75 | 77 | 42.09/34.30/32.50 -> 26.82/29.36/30.80 |

## Reopened decision, repair and final verification

The caller explicitly chose option A: CARD-1892 round 2/3 and CARD-1905 supersede the older raw-Pending requirement for unconfirmed Grok Input. The row remains Sent with a null verdict (logical refinementDelivered=pending), without automatic resend, Enter or retyping; only a complete transcript UserPrompt proves delivery. This reopening required no new permission and changed tests only.

Commit 2cdfc81b68fb26c70efd17940acc47d62895f174 restores the original C0491 flush-transport-failure-then-retry arm as a separate ambiguous-transport witness while retaining the definitive-refusal retry arm. After the ambiguous failure and another boundary, the row is Sent/null with one charged attempt and zero further raw inputs; a later complete UserPrompt yields LateConfirmed and refinementDelivered=delivered without another send. The fake throws before recording Input; real transport ambiguity cannot establish that boundary, which is why the test does not authorize a retry.

Both aged C1169 no-prompt variants now assert Sent/null, exactly one charged attempt, and exactly the initial encoded body plus CR throughout aging, the boundary, and later confirmation. Screen-visible and screen-empty variants remain distinct. Both then ingest a complete UserPrompt and require LateConfirmed/delivered with no further Input. No cases were deleted and no unconfirmed fixture was seeded Delivered. The commit message cites 5b80144d3cc7dcdcdb50c85ee03cd737e1508b41 (ambiguous Input retention) and ecc9eb97130680c19e634f2a6ce236e61991cd3a (composer-less hold) as the semantic changes.

Pending remains asserted for definitive refusal, non-Grok kind-claude refusal, unsuccessful conditional interrupts, and the aged head-only prompt's proven truncation. All these cases pass in the full class. No timeouts, cancellation assertions, policy inputs or nightly pins changed.

Final source for build, integration, index and guards: **2cdfc81b68fb26c70efd17940acc47d62895f174**, tracked tree clean before/after each row. Both test rows reuse Antiphon.Tests.dll SHA-256 **1a411537b8333dac9232579869d172e54d40579aa5190026f2457929ed4f03a7**; UseAppHost=false, native outputs. Their distinct native TRX files are retained alongside normalized run.trx copies under the ignored evidence root. These are direct-run records, not an ordinary Review approval or checkpoint source-clean receipt.

| Selection | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| DelegationDispatchRecoveryBoundaryTests, whole class | 382 | 382 | 0 | 0 |
| AgentTaskMidTurnRefineTests, whole class | 39 | 39 | 0 | 0 |
| AgentSessionInterruptedLaunchResumeTests, whole class | 9 | 9 | 0 | 0 |
| ImpactedClassesSelectorTests, whole class | 42 | 42 | 0 | 0 |
| Requested seven-class guard union | 90 | 90 | 0 | 0 |
| Total | 562 | 562 | 0 | 0 |

The guard union is unchanged: TestClassificationPolicyTests 42, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, DerivedImpactIndexTests 27, C1729LayeringGuardTests 10. All 14 exact statement budgets pass. The original running-cut OperationCanceledException assertion passes unchanged. Adjacent real-failure witnesses (Ready_false_fails_kills_the_adapter_and_the_agent and Sign_in_block_persists_LaunchBlock) also pass; ordinary Review still owns assessment of the narrowed catch's full real-failure behavior.

Rebuilt the syntax-only index driver first with the gate's explicit maxcpucount, then ran the canonical generator twice with -NoBuild in the same lease (one build total). Both passes printed derived-impact-index unchanged. No new index commit is needed; the earlier separately committed refusal-dependency update remains. The non-dotnet wrapper's generic BUILD SLOT warning is retained; the helper explicitly passes -maxcpucount from ANTIPHON_BUILD_SLOT_MAXCPUCOUNT to its single build.

| Run | Source | Executed/passed/failed/skipped | Wall s | Slot wait s | Held s | Load start -> end |
|---|---|---|---:|---:|---:|---|
| final-r2-build | 2cdfc81b68fb26c70efd17940acc47d62895f174 | build/tool exit 0 | 304.846 | 45 | 258 | 24.03/26.75/27.85 -> 28.53/32.37/30.40 |
| final-r2-integration | 2cdfc81b68fb26c70efd17940acc47d62895f174 | 430/430/0/0 | 382.011 | 240 | 141 | 25.13/31.52/30.14 -> 16.24/24.13/27.68 |
| final-r2-index | 2cdfc81b68fb26c70efd17940acc47d62895f174 | build/tool exit 0 | 201.969 | 120 | 80 | 14.84/23.57/27.46 -> 20.98/21.04/25.59 |
| final-r2-selector-guards | 2cdfc81b68fb26c70efd17940acc47d62895f174 | 132/132/0/0 | 521.901 | 60 | 460 | 20.10/20.85/25.50 -> 10.44/13.48/19.60 |

final-r2-integration: 430 complete case journals; known phase union 46.021s; phase unions {"db-create":18.892,"db-cleanup":21.859,"fixture-setup":5.275}; health failure=null, droppedPhaseCount=0.

final-r2-selector-guards: 132 complete case journals; known phase union 1.198s; phase unions {"process-spawn":0.11,"process-wait":1.198}; health failure=null, droppedPhaseCount=0.

Phase unions overlap; the rest of slot-held wall is uninstrumented/remainder, not attributed to an unavailable phase. n=1 per row; no p95 or timeout conclusion is inferred. Historical measurements and failed outcomes above remain preserved. Reopened TRX SHA-256: integration 10041d4a1efe0bdf3464f3e0d2fda56583835b56c2be23873f9e0d896e299e87; selector/guards 0ac858e6e909ab71ffd62276761deb7926ccdab4a48b26b1e2bb43aee4c4e2c5.

### Ordinary Review focus; Merge waits

The branch has one product change, cc7e81afc442e1de02e0769bc807b875bcd04e07 in AgentSessionService.cs: the recovered-launch handler catches only typed InputDeliveryUncertainException after Running and retains ct.ThrowIfCancellationRequested. Review must try to break the original running-cut cancellation witness and verify that a real failure is not swallowed and follows the existing failure/cleanup path. Verify that actual typed ambiguous Input still retains the running process.

The 72/67/67 budgets intentionally include two safety reads, introduced by 4e4362beccf9b0be06c191990c4d323656809dad and ecc9eb97130680c19e634f2a6ce236e61991cd3a. Review must verify these are exactly the queries logged, not unrelated growth. Below are unedited statements 37/38 from the final existing-brief SQL roster (72 statements); missing-brief and watchdog have these probes at 35/36 (67 each). The two probes are byte-identical across all three final rosters: SHA-256 7793e76b7b923cf77433d14d188464cd861037d2b99d308319d247d0726afc97 and d48e9d0e515676fb427ee58ddbbf9e04958d5d29cf89cce9eefa100d881136ff, respectively. Full rosters and their old-endpoint counterparts remain under the evidence root; no parameter values or secrets are emitted.

```sql
-- statement 37
SELECT s."Id", s."AgentSessionId", s."Body", s."CanceledAt", s."CapacityRecoveryActionKey", s."CapacityWaitId", s."CapacityWaitVersion", s."ChannelOutboundDeliveryId", s."ChannelReplyDiscoveryClosedAt", s."ChannelReplySettledAt", s."ContentDigest", s."ConversationKey", s."CreatedAt", s."DeferredFromRunAttemptId", s."DeliveryAttempts", s."DeliveryVerdict", s."DeliveryVerdictAt", s."DirectReplyConversationId", s."DirectReplyHandle", s."DirectReplyPendingThroughSequence", s."DirectReplyPromptSequence", s."DirectReplyProvider", s."DirectReplyTailClosedAt", s."DirectReplyThroughSequence", s."ExecutionDeadlineAt", s."ExecutionTaskId", s."FirstWithheldAt", s."HoldUntil", s."LastDeliveryBaselineSequence", s."LastDeliveryGeneration", s."LastDeliveryStartedAt", s."LastWithheldAt", s."LastWithholdReason", s."MaintenanceAcceptedStartedAt", s."MaintenanceEvidence", s."MaintenanceKind", s."MaintenanceResult", s."MaintenanceResultAt", s."MaintenanceSlotActive", s."NoteHeader", s."Origin", s."PinRefreshKey", s."PinRequestedHash", s."PinRequestedLocationGeneration", s."PinRequestedRevision", s."RemoteSpillBody", s."RemoteSpillRelativePath", s."ReviewReplyConsumedAt", s."ReviewThreadId", s."RulesAcknowledgedAt", s."RulesBoundarySequence", s."RulesChainId", s."RulesCoveredByMessageId", s."RulesDeadlineAt", s."RulesFailure", s."RulesFollowOnCount", s."RulesPromptSequence", s."RulesReceiptJson", s."RulesRefreshKey", s."RulesTurnEndSequence", s."RuntimeActionKey", s."SentAt", s."Sequence", s."SourceChannelInboundId", s."SourceLandNotificationId", s."SourceScheduleId", s."SourceTaskId", s."SourceTaskSupervisionNoticeId", s."SpecialistInputPolicyJson", s."Status", s."SubmissionStartedAt", s."WithholdCount"
FROM "SessionQueuedMessages" AS s
WHERE s."Status" = 1 AND s."DeliveryVerdict" IS NULL AND s."DeliveryAttempts" > 0 AND s."MaintenanceKind" = 0 AND EXISTS (
    SELECT 1
    FROM "AgentSessions" AS a
    WHERE a."Id" = s."AgentSessionId" AND a."Status" = 2 AND a."AgentKind" <> 1 AND a."AgentKind" <> 2) AND s."AgentSessionId" = @__sessionId_0
ORDER BY s."Sequence"

-- statement 38
SELECT s."Id", s."AgentSessionId", s."Body", s."CanceledAt", s."CapacityRecoveryActionKey", s."CapacityWaitId", s."CapacityWaitVersion", s."ChannelOutboundDeliveryId", s."ChannelReplyDiscoveryClosedAt", s."ChannelReplySettledAt", s."ContentDigest", s."ConversationKey", s."CreatedAt", s."DeferredFromRunAttemptId", s."DeliveryAttempts", s."DeliveryVerdict", s."DeliveryVerdictAt", s."DirectReplyConversationId", s."DirectReplyHandle", s."DirectReplyPendingThroughSequence", s."DirectReplyPromptSequence", s."DirectReplyProvider", s."DirectReplyTailClosedAt", s."DirectReplyThroughSequence", s."ExecutionDeadlineAt", s."ExecutionTaskId", s."FirstWithheldAt", s."HoldUntil", s."LastDeliveryBaselineSequence", s."LastDeliveryGeneration", s."LastDeliveryStartedAt", s."LastWithheldAt", s."LastWithholdReason", s."MaintenanceAcceptedStartedAt", s."MaintenanceEvidence", s."MaintenanceKind", s."MaintenanceResult", s."MaintenanceResultAt", s."MaintenanceSlotActive", s."NoteHeader", s."Origin", s."PinRefreshKey", s."PinRequestedHash", s."PinRequestedLocationGeneration", s."PinRequestedRevision", s."RemoteSpillBody", s."RemoteSpillRelativePath", s."ReviewReplyConsumedAt", s."ReviewThreadId", s."RulesAcknowledgedAt", s."RulesBoundarySequence", s."RulesChainId", s."RulesCoveredByMessageId", s."RulesDeadlineAt", s."RulesFailure", s."RulesFollowOnCount", s."RulesPromptSequence", s."RulesReceiptJson", s."RulesRefreshKey", s."RulesTurnEndSequence", s."RuntimeActionKey", s."SentAt", s."Sequence", s."SourceChannelInboundId", s."SourceLandNotificationId", s."SourceScheduleId", s."SourceTaskId", s."SourceTaskSupervisionNoticeId", s."SpecialistInputPolicyJson", s."Status", s."SubmissionStartedAt", s."WithholdCount"
FROM "SessionQueuedMessages" AS s
WHERE s."AgentSessionId" = @__8__locals1_sessionId_0 AND s."Status" = 1 AND s."DeliveryVerdict" IS NULL AND s."LastDeliveryStartedAt" > @__freshFloor_1
ORDER BY s."Sequence"
```

Final changed paths: server/Application/Services/AgentSessionService.cs; tests/Antiphon.Tests/Application/AgentTaskMidTurnRefineTests.cs; tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.cs; tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs; tests/derived-impact-index.json; this investigation report. Product correction is limited to the first path; other executable changes are tests/index. No merge, restart, activation, live session or volume operation occurred.

## Pushed slices

- 33e2322e3a1476d2bc48058999483a620c5df629 docs(debug): record inherited-red baseline and diagnostic scope
- c600bdc5bb9618716ddb898df0875973d7ba1c1c test(card-1919): include sampler CPU witness in selector expectation
- bbb4df095f3d427c4c3500df91796106bf295523 test(card-1917): account for proven input-uncertainty safety reads
- cc7e81afc442e1de02e0769bc807b875bcd04e07 fix(card-1918): preserve recovered-launch publication cancellation
- 8ffed72a49ee2b583a74c24b837cfab0de71e3e2 test(card-1916): model definitive refusal without weakening Pending assertions
- 2c3b91248b657889fc7dd3f8f48c0d312e75dfe6 chore(debug): regenerate derived index for refusal fixture dependency

- 5e7de331f9ca902f9a9738efac9badeae371ba8d docs(debug): original blocked report publication
- 2cdfc81b68fb26c70efd17940acc47d62895f174 test(CARD-1916): assert ambiguous Grok hold and late confirmation

The final narrative-only publication commit follows these tested slices.

## Exact replay

Run from the task checkout. These commands reproduce the repaired final selection; no failures are expected. Each build/run takes its own slot. Build the syntax-only driver before the guard reader. Use a fresh results root; no generator rebuild or assembly-wide selection is needed.

```powershell
$reviewRoot = Join-Path ([IO.Path]::GetTempPath()) ('debug55b-' + [Guid]::NewGuid().ToString('N'))
pwsh -NoProfile -File scripts/build-slot.ps1 -Label review-index-driver -- dotnet build scripts/regenerate-derived-impact-index -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label review-source -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label review-integration -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/(DelegationDispatchRecoveryBoundaryTests*)|(AgentTaskMidTurnRefineTests*)|(AgentSessionInterruptedLaunchResumeTests*)/*' --report-trx --results-directory (Join-Path $reviewRoot 'integration')
pwsh -NoProfile -File scripts/build-slot.ps1 -Label review-guards -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/(ImpactedClassesSelectorTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(C1729LayeringGuardTests*)/*' --report-trx --results-directory (Join-Path $reviewRoot 'guards')
```

Final evidence-diff and merge-tree results over the published report tip are recorded in the task completion report. The original-pass target was ca8737e78170c9a441e216408da5fab636c34ca0; the reopening fetch checks the then-current target separately.

--- next stage ---
next: review
handoff: Ordinary Review of the single CARD-1918 product change must preserve running-cut cancellation and must not swallow a real failure; verify the two logged safety queries justify exact 72/67/67 budgets and the Grok Sent/null/no-resend/late-UserPrompt contract. Merge must wait for Review approval.
artifact: docs/investigations/2026-11-14-card-1916-1919-inherited-reds.md
