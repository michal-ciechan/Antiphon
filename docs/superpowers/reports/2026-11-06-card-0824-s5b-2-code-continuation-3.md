# CARD-0824 S5b-2 continuation 3

Work remains incomplete. C1's original red is closed; U2 positive remote grants pass. C4's last tested recovery is red; its subsequent slot-preparation repair compiled but reached no test slot. Feature admission stays OFF. Original Code/landing owner: `543d8c5b-8a8a-4796-bb6a-f9d28447cbb8`. Task `7d6aabc8`, assigned branch `feat/card-task-7d6aabc8`, worktree `/work/worktrees/task-7d6aabc8`, full task base `82efe8fb94630937152a292308b922c39beb7d89`.

Plan: `docs/superpowers/plans/2026-11-05-card-0824-s5b-ref-only-plan.md`. Verification design: `docs/superpowers/testdesign/2026-11-05-card-0824-s5b-2-testdesign.md`. Prior outcomes and owed variants remain preserved in `docs/superpowers/reports/2026-11-06-card-0824-s5b-2-code-continuation-2.md`.

## Initial verification and discovery repair

Native incremental baseline compile at the task base passed: 0 errors, 1756 warnings, compiler 184.88s; slot=granted waited=0s. This brief-required unlisted build uses native default output, `UseAppHost=false`, `TEMP=/tmp`; it also bootstraps the checkpoint tool. No baseline test credit.

Exact CP-3 first ran at base `82efe8fb94630937152a292308b922c39beb7d89`, containing the inherited retained-agent fix `ae201bde0a3c6ccdc3d670d2ffe2f35ce401367e`. Both initial attempts failed **before discovery of any method**, with no TRX and no passing or behavioral-red credit. Last behavioral C1 red remains `7e88a6e3af000edc620db4454103912be7441efc` until an actual later assertion result is recorded.

The initial hypothesis of test-concurrency configuration was disproved by the second attempt with `TUNIT_MAX_PARALLEL_TESTS=1`. Read-only IL inspection of the locally pinned TUnit 1.44 engine shows `ShouldScanAssembly` refusing `Assembly.Location.Contains("ref")`. Its collector creates a semaphore with `min(assemblyCount, 2*ProcessorCount)` and does not handle zero admitted assemblies. Direct reflection returned `False` for the real cold-output Antiphon.Tests assembly and `True` for the native-output assembly. Thus the frozen `bin-c0824-ref2-*` names are the decisive inherited path fault. The test design now uses `bin-c0824-s5b2-*`; exact filters, minima, assertions, row caps and build caps are retained. The ignored manifest pins slotWait=12m, build=20m and CP-3=6m. No framework/package change or timeout widening.

Run `20261106-052819-4f17`: build admission 15s, compiler 321.80s; row admission 45s, execution 7.937s. Run `20261106-053751-5230`: build admission 0s; row admission 60s, execution 14.332s. Both producing builds verified clean committed source; neither failed enclosing report is a green certificate. Their task-owned cold binary outputs were removed by literal absolute path after the executors joined; evidence remains ignored under `.antiphon/checkpoints/`.

The following essential lines are retained without edits:

### 20261106-052819-4f17

```text
source: 82efe8fb94630937152a292308b922c39beb7d89 state=clean buildSource=verified
CHECKPOINT CP-3 commit=82efe8fb94630937152a292308b922c39beb7d89 build=n/a filter=/*/Antiphon.Tests.Application/(C0824RefOnlyContinuationTests*)|(C0824RefOnlyConsumerTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=granted waited=45s dirty=0 source=82efe8fb94630937152a292308b922c39beb7d89 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=45s executed=7.937s wall=52.975s
BUDGET wall=475.867s execution=415.870s admissionOnly=59.998s
verdict: RED exit=2
```

### 20261106-053751-5230

```text
source: 82efe8fb94630937152a292308b922c39beb7d89 state=clean buildSource=verified
CHECKPOINT CP-3 commit=82efe8fb94630937152a292308b922c39beb7d89 build=n/a filter=/*/Antiphon.Tests.Application/(C0824RefOnlyContinuationTests*)|(C0824RefOnlyConsumerTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=granted waited=60s dirty=0 source=82efe8fb94630937152a292308b922c39beb7d89 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=60s executed=14.332s wall=74.555s
BUDGET wall=559.981s execution=499.944s admissionOnly=60.036s
verdict: RED exit=2
```

## C1 red discharged

Exact CP-3 at 0aa0f938fb84e9ea33fcaa2e310c5e4dd721b987 passed 7/7, zero failures/skips. All seven intended methods were inspected in fresh TRX. C1 reached and passed runner-cwd=bound, exact parked source/full-ref prompt, busy deferral, real modeled-recipient UserPrompt count, retained binding/mode and absent desktop worktree assertions. This verifies inherited fix ae201bde0a3c6ccdc3d670d2ffe2f35ce401367e without weakening assertions. Last behavioral red was 7e88a6e3af000edc620db4454103912be7441efc; this is the code-equivalent tested successor. C1 remote-missing/recreation and full Q cuts remain owed; C4 and U2 still have the previously reported incomplete variants.

Receipt validation passed for exact SHA and CP-3. Build slot=granted waited=90s, compiler191.26s, executed307.45s/wall420.12s; row slot=granted waited=195s, execution94.944s. Terminal wall802.670s includes admission-only284.927s and execution517.743s. The executor and wait completed; tool-owned cold output was deleted. Source state clean, producing build verified. No repeat after green is needed.

### 20261106-055034-db81

```text
source: 0aa0f938fb84e9ea33fcaa2e310c5e4dd721b987 state=clean buildSource=verified
CHECKPOINT CP-3 commit=0aa0f938fb84e9ea33fcaa2e310c5e4dd721b987 build=ok filter=/*/Antiphon.Tests.Application/(C0824RefOnlyContinuationTests*)|(C0824RefOnlyConsumerTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-7d6aabc8/.antiphon/checkpoints/20261106-055034-db81/rows/CP-3/run.trx slot=granted waited=195s dirty=0 source=0aa0f938fb84e9ea33fcaa2e310c5e4dd721b987 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=195s executed=94.944s wall=290.118s
PHASES CP-3 slotWait=285s build=284.4156032s startup=23.8735992s testsWall=64.5757191s teardown=4.217924s hostWall=92.667267s
BUDGET wall=802.670s execution=517.743s admissionOnly=284.927s
verdict: GREEN exit=0
```
# Continuation 3 detailed handoff

C1's inherited behavioral red is closed. Positive remote decision-file grants now pass real phone-home/runtime checks. RefOnly redirect custody has a new store/receipt fence, but its recovery witness is still red. The frozen design and Final qualification remain incomplete; feature admission stays OFF. Next is Code. No landing, desktop work, restart or deployment occurred.

Task: `7d6aabc8-2de8-4c44-b9d9-6e54edfd6a95`; original Code task/landing owner: `543d8c5b-8a8a-4796-bb6a-f9d28447cbb8`. Worktree `/work/worktrees/task-7d6aabc8`, branch `feat/card-task-7d6aabc8`, base `82efe8fb94630937152a292308b922c39beb7d89`.

Plan: `docs/superpowers/plans/2026-11-05-card-0824-s5b-ref-only-plan.md`; verification design: `docs/superpowers/testdesign/2026-11-05-card-0824-s5b-2-testdesign.md`; stored report: `docs/superpowers/reports/2026-11-06-card-0824-s5b-2-code-continuation-3.md`; unbound Mutation inventory: `docs/superpowers/mutations/2026-11-05-card-0824-s5b-2-unbound.md`. The continuation-2 report preserves earlier costs, failures, exact ordinary method receipts and all 48 original affected classes. No earlier red report is relabelled green.

## Implementation and defect-sensitive witnesses

Read-only `DecisionPathInspect` phone-home operation 49 and `DecisionPathInspectionV1` capability connect the actual client, dispatcher, runtime adapter and session runtime. Older/unsupported runtimes return no proof. Runtime observations bind session id, runner store, accepted start generation, exact actual CheckoutPath and every requested relative path before and after inspection, under the launch gate. They read no file contents, refuse traversal/rooted/control/ambiguous path components and refuse links/reparse points throughout existing paths and root ancestors. This is a point-in-time observation, not a custody receipt. Windows execution is owed.

`AgentTaskDecisionQuestionService` preserves task locking and locks current session custody while requesting that bounded observation. It requires the exact echoed tuple/path set and unchanged store before applying the existing policy to verified remote paths. Unsupported, unavailable, unreadable and unknown observations hold with the existing named reason. Checkout keeps its existing policy path. The two-second observation budget is new bounded I/O, not a widened test timeout.

U2's actual service -> PhoneHomeRunnerClient -> command dispatcher -> runtime witness grants a safe runner file; independently refuses wrong cwd/store/generation/session, traversal and absolute identity/repository paths; corrupts each echoed tuple/path dimension; refuses an actual symlink, restores the original file and grants again; verifies bytes survive and no input is sent. The actual AgentSessionService and AgentControlService RefOnly caller witnesses are still owed; the frozen method's name does not discharge them.

`AgentTaskDispatcher.ReleaseSlotBeforeRebindAsync` now requires one completed task/attempt/runner/path/full-ref mirror binding with the current store and positive slot generation for RefOnly. It captures the client before checking store, validates outcome plus exact reply slot/generation, and rechecks current store and unchanged queued/sessionless task tuple after I/O. Wrong or unknown custody retains the old runner. The real C4 witness uses old and target runner slot pools, actual transport, independent published source S, retained desktop binding/creation base, no identity worktree, and full source ref/blob/HEAD/index/worktree inventory assertions. Its six authored cases are store, generation, unknown, unreadable reply, reply generation and valid. Later cases are NOT reached when the first recovery assertion fails. Unreadable reply is not real network loss. Queued/deferred and true network-loss variants remain owed.

Correction after the first managed red: slot mirrors intentionally return `Binding=null`, leaving the pre-request mirror preparation with an empty binding/path while the actual task records the ledger slot/path/generation. Commit `74603cf4c480a084acdae624b31396f62af7c48b` accepts exactly one matching preparation (task/attempt/runner/nonempty preparation id/full ref/creation baseline/store), either its completed binding/path or its slot-protocol empty binding/path. The actual slot/generation receipt and post-I/O task/store checks remain required. The stricter initial completed-binding-only predicate caused the new recovery red; its wrong-store refusal alone was insufficient mutation sensitivity. The repair's exact checkpoint result is appended below; no unverified repair is green.

C4 shares one schema/host/recipient pair per method; variants lease distinct tasks/refs and perform real target releases. Fixture Git children have a 30-second bound plus 5-second stream drain. No assertion or timeout was relaxed. Canonical origin-tracking setup occurs before inventory freezing, preserving the complete foreign-ref equality assertion.

## Commit and native driver accounting

All commits were pushed to the assigned branch. Baseline native compile and C1 history are retained in the stored report above.

| Slice / full SHA | Actual outcome |
|---|---|
| `0aa0f938fb84e9ea33fcaa2e310c5e4dd721b987` | Correct pinned TUnit discovery paths and document its case-sensitive `ref` substring exclusion. Initial two exact CP-3 attempts had zero discovery/no TRX, no behavioral-red credit. Exact CP-3 then 7/7, C1 closure. |
| `ca5a04685588b096a0248ca243ba6bc67e5578b9` | Publish C1 clean/verified receipt and retained historical costs. |
| `4d59e25f46e2551e57228420e3a639356fe1ca5c` | Add real C4/U2 witnesses and remote inspection protocol. Native build failed one fixture constructor error; 301.91s compiler, slot=granted waited=0s held=303s. No behavioral red. |
| `67b0992b732fc9c8757f36f9594a6d0e8bfb5323` | Correct constructor and bound fixture Git. Native build 0 errors/1758 warnings, compiler625.60s, slot=granted waited=0s held=626s. Two-method TDD run: 0 pass/2 fail/0 skip, host43.924s, slot=granted waited=45s held=50s. U2 is assertion-red at `grant-root=runner`; C4 is setup-red at unqualified WaitLiveAsync with two runners, no behavioral credit. |
| `d7b77bfdb75d9151c9b223cddd109258538c0d02` | Implement remote grants and qualify fixture live lookup. Native build 0 errors/1757 warnings, compiler537.83s, slot=granted waited=496s held=540s. Two-method rerun: 1 pass/1 fail/0 skip, host35.123s, slot=granted waited=210s held=40s. U2 passed14.384s; C4 reached real old ledger then failed `redirect-refused:store`6.575s: task incorrectly moved to target. |
| `5eb826a9581e84e339adf2cf264b11592699a919` | Implement custody fence, conservative unreadable-JSON hold, shared C4 setup, pending controls inventory. |
| `1cbfbf8fba4a81b030f0ed9a9470283932fe8234` | Seed legitimate scoped tracking ref before complete inventory snapshot. Managed run below: CP-3 6 pass/1 fail/0 skip. C1 and U2 green; C4 original wrong-store refusal passes, restored recovery fails `remote-redirect:store`. |
| `617b04604f0e62db355f6399cacc957682fa49d9` | Preserve C4 recovery status/release/request/event diagnostics in the existing assertion. No oracle weakened; rerun result appended below. |
| `74603cf4c480a084acdae624b31396f62af7c48b` | Correct slot-versus-card preparation distinction in the store fence; exact CP-3 rerun commissioned with expected committed SHA. |

The unlisted native builds and two precise two-method runs were necessary red-first diagnosis/fix verification before the committed managed CP-3 group. They ran under individual host build-slot leases, native default output, matching UseAppHost=false and TEMP=/tmp. A timeout wrapper around no-build test execution prints the gate's non-dotnet maxCPU warning; it does no nested build. No unleased driver. Red evidence: `.antiphon/7d6aabc8/red/red.trx`, `.antiphon/7d6aabc8/red2/red2.trx`; compiler/console logs remain alongside them. No deliberate mutants were run.

## Managed run 20261106-070112-eabf

Expected and actual tested source `1cbfbf8fba4a81b030f0ed9a9470283932fe8234`; source clean and build provenance verified in run state. Isolated build passed0 errors/1788 warnings, compiler610.07s; slot=granted waited=120s, executed677.0980595s, wall829.850428s. CP-3 slot=granted waited=15s, executed77.1223588s, wall92.1733442s; host75.6947538s, startup15.6573004s, testsWall55.4869701s, teardown4.5504994s. All seven fresh TRX methods inspected. Exact outcome6/7, one C4 failure19.751s at restored `remote-redirect:store`; C1 and U2 passed. Full consumer row was stopped/joined while acquiring a slot, before a test driver: NOT RUN, no counts/credit. The stopped enclosing run has no final green receipt/report. Stop verified executor identity dead and removed its tool copy before any source edit.

The raw CHECKPOINT line below is copied without edits from the persisted row state; it is not a fabricated whole-run certificate:

```text
CHECKPOINT CP-3 commit=1cbfbf8fba4a81b030f0ed9a9470283932fe8234 build=ok filter=/*/Antiphon.Tests.Application/(C0824RefOnlyContinuationTests*)|(C0824RefOnlyConsumerTests*)/* executed=7 passed=6 failed=1 skipped=0 trx=/work/worktrees/task-7d6aabc8/.antiphon/checkpoints/20261106-070112-eabf/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=1cbfbf8fba4a81b030f0ed9a9470283932fe8234 sourceState=clean buildSource=verified
```

FULL-CONSUMER is an additional bounded Final affected-class selection, not a frozen checkpoint or full assembly. It names ten original consumer classes plus AgentTaskDecisionQuestionTests/InternalDecisionPolicyTests, at least279 executions, provisional8-minute execution estimate/unchanged15-minute row cap. Build20+CP-3 six+consumer15 gave41-minute charged/77-minute hard wall including three12-minute admissions. No broad/full-assembly run occurred. No unchanged green row was repeated; CP-3 changed through authored witnesses and diagnosis. No proven Review flake or extra repetition budget was invoked.

## Every ordinary ID and exact remaining acceptance

Historical CP-1:10/10 at `07cb3da4330caa3a16bcb36ff278db7fa9fb551a`, waited240s/executed51.723s. CP-2:8/8 at `3fb9637177807c0e6e9da14d678f2284488093d2`, waited105s/executed75.214s. CP-4:7/7 same SHA, waited90s/executed64.001s. CP-5:47/47 at `7e88a6e3af000edc620db4454103912be7441efc`, waited195s/executed129.009s. All zero skips. They are retained historical observations, not fresh current-source full-class verification, and were not repeated.

| ID | Actual outcome and remaining frozen variants |
|---|---|
| V-1 S1..S7 | Historical CP-1 method greens. Owed: ancestry/CAS I/O/post-read faults; complete independent initial/under-lease tuple/pin corruption; mirror negatives and real phone-home P queue; actual lease/reservation/child lifetime variants. |
| V-2 D1..D3 | Historical CP-1 exact debt recovery/captured mode-binding/claim-save comparisons pass. Every unexercised frozen variant remains owed; no Mutation credit. |
| V-3 R1..R3 | Historical CP-2 honest Check/read counts, Pending unpinned/unavailable counts, report retention and repair hints pass. Full affected classes still owed. |
| V-4 B1..B4 | Historical CP-2 source-pinned bytes/diff/budget, admission and real streaming controls pass. Remaining frozen artifact variants not explicitly reached remain owed. |
| V-5 C1..C4 | C1 original red closed with unchanged cwd/parked S/ref/busy deferral/accepted UserPrompt/no-desktop assertions. C1 missing mirror/recreation and full Q cuts owed. C2 historical publication/recovery and independent mode/binding/revision/digest/store/generation corruption/restoration pass; actual P queue, intent/receipt/claimed/synced/saved cuts and independent attempt/source/save variants owed. C3 historical live idle/busy, restart/gate-off pass; released-seat/Q cuts/bad receipts owed. C4 new wrong-store refusal passes but restore/restart target move is RED; remaining generation/unknown/unreadable/result-generation/valid cases unreached, plus actual deferred/queued release, network loss and enqueue/save/preparation cuts owed. Independent early/locked refusal controls are incomplete. |
| V-6 U1..U3 | U1 historical explicit custody/frozen S/child Worktree and Worker/Shared/ReadOnly/local-tuple refusals pass; actual dispatched/follow-up and complete binding variants owed. U2 real remote grant/containment/echo controls pass; actual session/control caller tests still owed. U3 historical Home mode and distinct/same-ref journal admission pass; advisory owner, same-slot/generation terminal ambiguity, RefOnly Census and inherited-follow-up display owed. |
| V-7 A1..A3 | Historical CP-4 portable advisories/cleanup pass. Native N1/A-7 owed. |
| V-8 W1 | Historical CP-2 graph recreation/report-source-destination-receipt cuts through accepted modeled input pass. Remaining full Q cuts/independent negatives and actual provider manual acceptance owed. |
| R-1 | Historical CP-4 exact Checkout fast-forward at S passes; full RunnerSettlementSyncTests owed. |
| R-2 | Historical CP-4 endpoint refusal before fetch/merge passes; full class owed. |
| R-3 | Historical CP-4 Checkout debt follows recorded S passes; full class owed. |
| R-4 | Historical CP-2 paired legacy bundle behavior passes; full DeliverableBundleServiceTests owed. |
| R-5 | Historical CP-4 persistent report preservation/TEMP-contained negative destination passes; full cleanup class owed. |
| R-6 | Production defaults remain OFF/empty; no new mode producer or gate selector. Existing recorded-mode handlers remain active; complete U1/R-6 variants owed. |
| R-7 | Historical classification47/47 retained. test-execution-policy.json unchanged: no nightly policyHash/live census pin refresh required. |

**Whole Unit lane NOT RUN/owed**, explicit CARD-1720 host restriction. Native N1/Windows path-containment qualification and real-provider/manual acceptance NOT RUN/owed. No Linux skip/static lint is native credit. No rollout activation or 20-task/24h acceptance claimed. All remaining IDs above are deferred ordinary Final obligations, never passed by implication.

Every full affected class is still owed here: core25 and park9, consumer10 and redirect4 listed individually in continuation-2's inventory, plus PhoneHomeConnectionTests, AgentTaskDecisionQuestionTests and InternalDecisionPolicyTests. Added runtime protocol closure also owes full PhoneHomeCommandDispatcherTests, PhoneHomeConnectionServiceTests, RunnerCapabilitiesTests and RunnerPlatformContractTests in Antiphon.SessionRunner.Tests. The FULL-CONSUMER attempt executed no method. Exact filters/inventory are preserved in `.antiphon/7d6aabc8/full-selections.json` and `full-*.filter`. These are bounded named classes; unbounded whole Unit is excluded by the brief. No other full assembly was run.

Exact owed full-class list (no execution credit from inventory):

- core: RunnerSettlementSyncTests, RunnerCompletionProgressTests, TaskCompletionProgressPolicyTests, DelegationWorktreeTests, C0824DesktopLegacyReleasePolicyTests, C0824RefOnlyCleanupTests, C0824RefOnlyBoundaryTests, C0824RefOnlyIdentityTests, SettlementSyncRecoveryTests, SettlementSyncDebtSchemaTests, RunnerTaskSettlementTests, DelegateCheckProbeTests, DeliverableBundleServiceTests, AgentTaskDetailBlockedContextTests, DelegateBindRefusalRecoveryTests, VerificationRoundDeliveryTests, VerificationRoundSettlementTests, PendingSettlementDeliveryTests, AgentTaskLandNotificationPersistenceTests, AgentTaskLandNotificationRecoveryTests, AgentTaskLandReceiptTests, AgentTaskLandQueuedReceiptTests, AgentTaskLandReceiptWatermarkTests, AgentTaskLandReceiptWatermarkSafetyTests, ReceiptScanDestinationIntegrationTests.
- consumer: AgentTaskServiceIntegrationTests, AgentTaskDecisionQuestionIntegrationTests, AgentSessionServiceIntegrationTests, AgentControlServiceIntegrationTests, HomeTaskServiceIntegrationTests, ZombieCensusServiceTests, AdvisoryChangePolicyTests, WorktreeRetirementRaceTests, TerminalRunnerSeatReleaseTests, WorkspaceReservationLivenessTests, AgentTaskDecisionQuestionTests, InternalDecisionPolicyTests.
- park: TaskParkPublicationTests, BlockedTaskSyncRecoveryTests, BlockedTaskParkStateTests, BlockedTaskParkReleaseTests, BlockedTaskParkResumeTests, BlockedTaskParkDeliveryTests, BlockedTaskParkReplyAdmissionTests, BlockedTaskParkReclaimTests, BlockedTaskParkProjectionTests.
- redirect/protocol: PhoneHomeRollingRunnerTests, RemoteWorkspacePreparerTests, C0824WorkspaceSlotServerTests, PhoneHomeMirrorJoinTests, PhoneHomeConnectionTests; runtime project PhoneHomeCommandDispatcherTests, PhoneHomeConnectionServiceTests, RunnerCapabilitiesTests, RunnerPlatformContractTests.

Diagnostic run `20261106-071944-f210` at617b was stopped/joined before build admission; last logged slot=not-acquired waited=75s. No compiling build or test driver ran; no counts or green credit. Earlier a mistyped expected SHA was correctly refused preflight with `source_mismatch or source_dirty`, before any run/driver. The corrected expected SHA was used for this stopped run. No source mutation happened until executor-dead cleanup returned.

Repair run `20261106-072258-e8ba` at74603 used the unchanged20-minute build/six-minute CP-3 caps, with26-minute charged/50-minute hard-wall bound. Slot=granted waited=75s. It was stopped/joined before a producing build completed or tests began, to correct another independently identified fixture issue: shared recipient counters were cumulative across variants. `f28e7de1d49f37f9e3daeb2d1928594cabab1deb` captures each pre-action request count and requires no additional target preparation/wrong-store release. This preserves the zero-effect oracle after legitimate requests from preceding variants, not a relaxed assertion. The stopped state records wall100.9921767s/execution25.9768821s/admissionOnly75.0152932s; no passing credit. Run `20261106-072557-e813` expects committed f28e7de1d49f37f9e3daeb2d1928594cabab1deb with the same bounds; actual result appended at settlement.

**PC-1..PC-159 and every variant pending SourceLanding Mutation**, including new store/client-capture/echo controls and any missing-control discovery. This Code task does no deliberate mutant red/restore/green cycle. A zero-PC interpretation would still require Review; this plan has159. Review is not commissioned because ordinary implementation/verification remains incomplete. reviewedSourceClean remains `?`, Review-owned. D-2 advisories are not silently relabelled; the Final gate stays incomplete.

## Platform, publication and operational limits

GET /api/runner-defaults and GET /api/session-runners were read at start, saved ignored under `.antiphon/7d6aabc8/`; no fleet host or OS pin was embedded. OS inventory lint passed against the full base-to-tested-source range with real changed-file/candidate inventory; it does not establish Windows runtime behavior. No rc/bootstrap/startup/Program/DI/OS-factory selection change was introduced. New RPC runs on demand using existing runtime/session custody. Eventual activation requires server and runner binaries; **restart: server/runner**, none performed.

Fetch origin/master succeeded; at master `3737148ee1e43e3695174b7a8e9befeb6d69fb41`, merge-tree returned exit1/tree `3596294f83441a92ffbce8ae82e768642025c7a0`: add/add conflict in testdesign and content conflict in C0824SlotTransportWorld.cs. No merge or branch alteration. Landing caller must handle integration; branch is not reported landable. Final-range evidence diff, final source/remote SHA, cleanup and diagnostic rerun outcome are appended at settlement.

## Settlement at the continuation budget

Run `20261106-072557-e813` expected and built `f28e7de1d49f37f9e3daeb2d1928594cabab1deb`. The isolated build passed0 errors/1788 warnings, compiler639.86s, slot=granted waited=330s, executed736.9332943s/wall1097.9435754s. Build source is clean/verified with identical committed SHA and fingerprint before/after. CP-3 remained at admission (last logged waited=90s, queuePosition=2); it acquired no test slot and executed no tests. Stopped/joined07:46 UTC to preserve the150-minute task budget. Executor-dead cleanup returned before any report/source edit. The stale row state `running`/waited0 is not test execution; executor log proves admission only. No final enclosing green receipt/report exists. Latest correction74603 plus fixture counter correctionf28 are **compile-verified, behaviorally UNVERIFIED**. Last actual CP-3 stays6 pass/1 fail/0 skip at1cb, C4 `remote-redirect:store`; later C4 variants unreached. First next-Code action is exact CP-3 at f28 or code-equivalent successor, with unchanged cwd/UserPrompt, custody, source-inventory and positive grant assertions.

Final merge-tree at f28/master3737148ee1e43e3695174b7a8e9befeb6d69fb41 returned exit1/tree `7e418a4499edefc624c02430b8e969b9d37e2bd9`, same two conflicts above. No conflict resolution by merge. Full task-range evidence audit at f28:10 commits,0 entries,0 violations. Final publication audit is rerun over the eventual report-only successor. OS inventory passed at74603; final inventory is pinned again to the report successor with the same portable behavior and no new OS-selected startup branch. This remains syntax/inventory credit only. Native checkpoint output naming also changes from bin-c0824-ref2-native to bin-c0824-s5b2-native to avoid the same proven discovery exclusion; native N1 is still NOT RUN.

No additional green repetition, full assembly, whole Unit, native/manual qualification or Mutation was run. All55 named full affected classes and every deferred V/R variant above remain owed. Feature stays OFF. No human decision is required to continue Code. After implementation and ordinary verification, request Claude Review full round1; caller then lands original Code543d8c5b-8a8a-4796-bb6a-f9d28447cbb8 and commissions SourceLanding Mutation. No Review approval, land or activation claim.

Cleanup completed after every owned executor/wait joined: exact literal absolute paths only. A fresh inventory finds no remaining bin/obj/bin-c0824 build directories under server/src/tests/tools. Ignored receipts/TRX/logs remain in their original evidence roots. `git diff --check` passed. The report-only publication successor changes no tested code. The final response and ignored `.antiphon/task-7d6aabc8.md` identify its full pushed SHA; all source and report slices use the assigned branch.
