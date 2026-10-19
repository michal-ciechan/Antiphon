# CARD-1349: dispatch sibling walk verification

TestDesign, 2026-10-19. **Master only. No product implementation or verification execution in this stage.**

This is the append-only verification companion to
`docs/superpowers/plans/2026-10-19-card-1349-dispatch-sibling-walk-plan.md`, read in full
from `origin/feat/card-task-8ddcaafd` at
`4925542e09af4cb47827736d774488c3e09a7e3d`. It leaves the fix design, D-1–D-8 and S1–S4
unchanged. This document's roster and checkpoint manifest finalize and supersede
the parent's **provisional verification selections**, not its implementation design.
The parent is not present at this checkout's base; do not assume it has landed or
discard it as documentation-only. Fetch its branch and use the exact object above.

Evidence read in full: `docs/investigations/2026-10-19-server2-remoteprep-stall.md`
from `origin/feat/card-task-3f531f98` at
`f17d89500ffe687eab93dfe4e94c0eabf1fb4cd6`. Card read through
`pwsh -NoProfile -File scripts/card.ps1 get CARD-1349 -Board Antiphon` (three revisions).
Source/fixture inspection baseline: `d5dec9555bd726ad11bb30073c1733d6b3e7bb0e`.
Historical incident timings are not new measurements. No unresolved product choice
or unverifiable seam was found. The seams below use production entry points and
existing I/O boundaries; Code must implement their test bindings before claiming green.

## Verification design

### Inspection

Paths below are repository-relative; names in backticks are bodies actually read,
not coverage inferred from a filename.

- `server/Application/Services/AgentTaskDispatcher.cs`: `TickAsync` sibling/admission call sites, `EvaluateCardSiblingBaseAsync`, claim/commit boundaries, `BlockAsync`, prepared-mirror predicates; `AgentTaskWorktreeBaseResolver.cs`: inventory, pending checks, `Inspection` budget/gate/ref/containment methods | D-1–D-5 -> V-1–V-8, V-16–V-20, R-1–R-8, R-16–R-20.
- `RemoteWorkspacePreparer.cs`: binding and legacy success, exception/backoff path; `RemoteWorkspaceService.cs`: preparation/capture and matching; `AgentTaskPipelineStatusService.cs`: queue-reason precedence; `DispatchHoldDetails.cs`: remote-prep prefix/classification | D-6 -> V-9–V-11, R-9–R-11. Capture's boolean currently proves retention, not current readiness.
- `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.cs`: V19/V20/V21/V22/V25/V30, C788 target/explicit helpers, C499 repair hold, C508 repair source, deleted-branch case, `CreateProvider`, seed/lease helpers; `.SiblingWarnings.cs`: graph cases, rollback/loser, moving/missing-tip fakes, census and `SiblingWorld`; `.CardBranchNames.cs`: C1213 SourceLanding and snapshot transport/directory | nearest fixture for new `DispatchSiblingInspectionTests.cs`; R-5, R-16–R-18, R-20.
- `AgentTaskWorktreeBaseResolverTests.cs`: V03/V04/V07/V08 bodies (identity, merges, dirty checkout, missing local refs) | R-16; no claim that a mock Git graph proves real ancestry.
- `DispatcherRemotePrepStarvationTests.cs`: complete body, `Configure`, `AdapterSlot`, seed and push fake; `RemoteWorkspacePreparerTests.cs`: capture/race/lost-reply cases, C672 prepared admission and recipient test, `BriefEnqueueFault`, `ConfigureDelivery`, `Rig` lifecycle | nearest fixtures for new `DispatcherSiblingInspectionSchedulingTests.cs` and `RemoteWorkspaceReadinessTests.cs`; V-6–V-11, V-18.
- `DispatchBaseNotificationTests.cs`: capture identity, projection custody/integrity/replay, provider and seeds; `.Coverage.cs`: commit/projection atomic fault bodies; `.SiblingWarnings.cs`: atomic projection and full-prompt/attempt-floor bodies | V-14/V-15. These existing tests mostly stop at obligations or seed receipt rows; they are not producer-to-recipient proof.
- `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs`: all ten C540 producer/crash methods, `CrashAsync`, plus two mismatch methods | cut-point reference for V-14. Native E2E is excluded below; do not describe these as executed or substitute the portable fixture for native acceptance.
- `DispatchHeldAttentionTests.cs`: full class, age/state/action assertions and isolated reader/seed helpers; `TestHelpers/RunnerAlarmFakes.cs`: `ListLogger<T>`; `MockEventBus.cs`: publication/throw-once bodies | nearest fixtures for new `DispatchBaseInspectionDiagnosticsTests.cs`; V-12/V-13. The existing logger captures formatted text only: the new local logger must also retain structured state pairs.
- `TestHelpers/QueuedReceiptAssertions.cs`: complete body; `BridgeQueueHarness.cs`: `CreateAsync`, real queue/runtime/launch registrations, options, transcript helper and disposal; `DelegationTestServices.cs`: worktree graph registration; `ScratchGitRepo.cs`: complete body | V-6/V-14/V-18 transport recipe. Runtime callback writes submitted bytes, never a fabricated receipt copied from a queue row.
- `AgentSessionService.cs`: `ResumeInterruptedLaunchAsync` and missing-running-brief recovery; `DelegationDispatchRecoveryBoundaryTests.RunningRecovery.cs`: first two cut witnesses, `WirePrompt`, `OpenRecoveryAsync`, `RecoveryWorld.ResumeAsync`/`DiscoverAsync`; `.AbsentLaunch.cs` refused-launch fixture and `.AbsentLaunchRepair.cs` caller-note recovery body | V-21 recovery binding. Preserve database on provider disposal; supply accepted generation/live inventory only for a genuinely launched fixture session. Never fabricate a live process.
- Owners: `docs/project-context.md`; `docs/testing-and-build.md` checkpoint schema, slots, exact filters, mutation and async-delivery sections; `docs/orchestration-loop.md` same-card source contract and D-2; `docs/agent-card-lifecycle.md` state rules; `docs/session-runtime-invariants.md` delivery and dormant parking | all rows. No instruction/policy/test-selection content qualifies for D-2 narrative exemption.

**Common recipes.** B = isolated PostgreSQL schema, production dispatcher/resolver,
concrete worktree graph, scripted `ILandingGit` recording argv, linked tokens and
named barriers; `FakeTimeProvider` only on the inspection/coordinator clock. G = B
with `ScratchGitRepo` and real Git, plus a recording decorator for fault/call cuts.
Q = `BridgeQueueHarness` with real `AgentSessionLaunchQueue`,
`SessionMessageQueueService`, notification service and runtime; system/advancing
clock, fake protocol adapter registered as the launch factory. M = phone-home
`RemoteWorkspacePreparerTests.Rig` pattern, private schema, current/late mirror
responses and fresh independent readers. L = B/M plus a structured logger and
transaction interceptor. All real-process fixtures retain
`ParallelLimiter<ProcessSpawnLimit>`; no production runner or credentials.

Use `ValidateScopes=true` and register/start/stop the **production** inspection
worker in the new harness. Existing one-tick fixture drivers become explicit
enqueue tick -> await owned worker at a named barrier -> consumption tick. Do not
poll arbitrary numbers of ticks or replace the worker with inline execution.
Legacy assertions counting all Held events must count the semantic land/lease hold
separately from the newly required inspection-pending event. Keep all source,
session, notification, dirty-file and frozen-SHA assertions.

`C540_ProbeCensus(20)` deliberately loses its quadratic equality. Replace it with
bounded command/candidate counts and explicit incomplete/omitted evidence; 0/1
retain their source/warning assertions. Move `MovingSiblingGit` and
`UnresolvableSiblingTipGit` hooks onto the new batch calls so they actually fire.
`C540_ObservationsRemainPinned` checks full frozen object IDs, not the old literal
`cherry master <tip>` shape. Never turn off a fault hook to make a test green.

### Delivery inventory

| Path / durable identity | Producer -> destination | Persistence boundary and recovery | Observable receipt / tests |
|---|---|---|---|
| A: inspection work, task ID + attempt + full base-intent fingerprint; observation also carries sibling/request identities | serial tick -> bounded production coordinator -> own-scope worker -> normal dispatcher | Queued/Held commits before advisory enqueue. No durable authority in memory. Reject/full enqueue leaves Queued; restart before enqueue, during work or after result loses only observation. Next tick re-arms. Worker completion does not claim or launch. Claim re-reads metadata and leased ref IDs. | V-7/V-8 prove matching consumption and stale rejection. V-6 continues through the ordinary launch/brief queues to the later task's matching complete UserPrompt **while sibling Git is still blocked**. Queue admission or Held is not receipt. |
| B: delegation brief, task ID + attempt + claimed session/generation + ExecutionTaskId/queue ID | admitted dispatcher -> launch queue and durable session-message queue -> recipient session | Failure before claim commit rolls back; replay starts from queued task. A live interrupted launch recovers its missing brief through its existing owner. A certified absent unattempted launch follows existing Blocked custody policy, preserving input and owing a caller note. After insert/failed acknowledgement or lost wakeup, reconstruct the queue over the same schema. | V-6 eligible/busy/inserted/lost-wakeup; V-18 C672 receipt test; V-21 pre-insert and after-claim cuts. Successful/recovered-live launch: one full task-marker/body receipt on claimed session, zero before idle. Absent launch: zero worker receipt, preserved input and full caller hold-note UserPrompt; never mislabeled task delivery. |
| C: sibling warning, successful dispatch event + warning key -> intent ID + preallocated notification ID -> SourceLandNotificationId + destination session/generation + digest | dispatcher consuming inspection result -> `DispatchBaseWarningIntentService` -> notification reconciliation -> real session queue -> frozen parent | Claim atomically captures immutable reduced tips/body/route. Recovery scans original intents after claim/projection cuts; never recomputes moved refs. Keyed enqueue survives pre-enqueue failure, insert-before-link crash, and lost wakeup. Late receipt resolves interrupted delivery without retyping. | V-14 is a new portable **producer-to-parent** test in `DispatchBaseNotificationTests`, all ten cuts below. V-15 is only the independent negative receipt oracle. Require full original body in a parent UserPrompt beyond the attempt floor and matching note/queue identity; no confirmation from Sent, events, ACKs or inserted rows. |
| D: mirror readiness, task + attempt + runner + preparation ID/binding ID (legacy: originating task/attempt/runner/path) | phone-home binding, recovery or legacy success -> task/path + Held event -> post-commit `IEventBus` hint -> pipeline/attention reader; later ordinary dispatcher -> B | Retain late bindings independently of current eligibility. Interrupt after binding retention, after current path, before ready-event commit and after commit before publication. Recovery inserts at most one ready event; durable GET projects current path even when the hint is lost. Notification failure cannot increment prep failures. | V-9/V-10/V-11 independent-context pipeline/attention DTO reads before another tick; callback reader observes committed state before `AgentTaskChanged`. V-18/V-21 then prove later session input through Q. DTO receipt proves server read-model correctness, not a browser repaint. |

V-14 case roster: `eligible`, `busy`, `claim-committed`, `projection-before-commit`,
`before-enqueue`, `enqueue-fails`, `queue-inserted-before-link`, `lost-wakeup`,
`prompt-before-verdict`, `receipt-before-save`. Each begins at a real dispatcher
claim with two divergent Blocked warning siblings (and a duplicate tip), not seeded
notification rows. Freeze intent IDs/body/digest/route, change refs and current task
route after the cut, recreate scoped services, invoke the real recovery scan and
queue. The busy case must have Pending/zero attempts/zero matching prompts before
idle. All cases end with one complete prompt **per original warning**, identical
queue IDs after replay, and no prompt on a replacement parent. Do not use
`QueuedReceiptAssertions`' global one-prompt assertion for this multi-note case;
use its submission callback and complete-match logic per notification identity.

V-21 cuts are `claim-rollback`, `before-brief-insert`, `after-claim-before-launch`.
Inject only at the owned task's persistence/launch I/O boundary; assert the cut fired,
no matching receipt exists yet and the exact durable recovery obligation remains.
For `before-brief-insert`, use the inspected `RecoveryWorld` pattern to drive
`AgentSessionService.ResumeInterruptedLaunchAsync`; discovery is
`SessionReconciliationService.ScanAsync` followed by the real launch queue's owned
drain. Supply the original accepted session generation and runner inventory.
For `after-claim-before-launch`, there is no process to attach: use the inspected
absent-launch fixture's certified-empty evidence and grace advancement, then the
existing dispatcher/watchdog recovery owner. Require Blocked custody, unchanged
task attempt/session/input, zero automatic starts and zero worker receipt. Seed a
real parent route and require its complete hold-note UserPrompt through Q (as in
the inspected caller-note body). This is recovery to a decision state, not
successful brief delivery; later deliberate Retry remains existing policy.
For `claim-rollback`, the task remains Queued and the normal dispatcher retries
the claim to a complete brief UserPrompt. Each cut ends at its actual recipient
evidence; no new automatic relaunch or fake live runner is permitted.
If the existing owner cannot recover a cut, report the concrete boundary to Plan;
do not silently reduce the case to successful enqueue or add an unrelated recovery
mechanism. This is an executable acceptance requirement, not permission to skip it.

**Substitutes and limits.** Fake Git proves bounded calls/cancellation/order, not Git
ancestry; G supplies that oracle. Fake adapter submission proves production queue
delivery through the adapter contract; it does not prove a native PTY/provider or
remote transport accepts bytes. No synthetic UserPrompt before actual `OnSubmitted`.
`QueuedReceiptAssertions` intentionally marks the fake session live: assert the
claimed session/runner/path/generation first; that helper cannot prove launch
readiness itself. M's recording launch sink proves admission only; Q supplies
recipient proof. `MockEventBus` proves post-commit publication, not SignalR/browser
delivery; independent read-model reads prove durable fallback. No new user-facing
delivery path relies on the hint. Real queue busy/eligible and all handoff cuts are
mandatory; a design stopping at a request, event or queued row is rejected.

### Proves it works now

These are **planned witnesses**, not passing results. N = new; E = existing
regression; C = changed fixture/assertion. Each row supplies recipe, variant/case,
oracle, reachability and classification. Exact filters/argument floors are below.
New methods live in `tests/Antiphon.Tests/Application/` in the named class file,
except V-14/V-21 added to the nearest existing partial/new scheduling file as named.

| ID / behavior | Layer; exact test | Recipe / variants (TUnit executions) | Oracle and reachability | Classification |
|---|---|---|---|---|
| V-1 bounded inspection | service/integration; `DispatchSiblingInspectionTests.C1349_InspectionLimits` | B; 8: DB deadline, Git-gate deadline, in-command deadline, cumulative phases, command cap, expensive-candidate cap, inventory cap, caller cancellation | Drive both legacy guard and authoritative resolver internally per case. Phase hook must fire; assert typed reason, cap+1 DB read ceiling, <=128 commands, no post-cancel calls, no false complete result; caller cancel propagates without persisted Blocked. Cumulative phases each fit separately but exceed the same 5s total. | N; old unbounded guard red |
| V-2 batched frozen inventory | service/integration; `DispatchSiblingInspectionTests.C1349_ContainedInventory` | B/G; 7: 64 ancestors, duplicate refs/tips, moved merged SHA, prefix-neighbor ref, invalid OID, absent local ref, batch error | Both readers: 64 ancestors do not trip 16-candidate cap; zero per-ancestor cherry/log; at most two branch-list batches plus fixed common/target probes. Distinct task/request identities survive dedupe. Moved/invalid/error stays unknown or inspectable, never silently contained. Exact requested refs only. | N |
| V-3 meaningful docs | integration; `DispatchSiblingInspectionTests.C1349_DocumentationSourceIsPreserved` | G; 2: unlanded plan, pending plan | Only commit changes a plan Markdown file; eligible source SHA selected, or current pending request holds with null worktree/session. Bound calls independently so source retention alone cannot hide the original walk. | N |
| V-4 bounded reduction | service/integration; `DispatchSiblingInspectionTests.C1349_ReductionRemainsBounded` | B/G; 3: identical/chain, 64 divergent, enrichment deadline | Proven small representatives/covered counts survive; large/incomplete set has bounded work and explicit omitted count, no invented ancestry edge. Optional enrichment exhaustion cannot erase an already valid pending hold or complete source verdict. | N/C census |
| V-5 pending precedence | integration; `DispatchSiblingInspectionTests.C1349_PendingLandSurvivesLimits` | B/G; 8: pending beyond raw cap, candidate cap, command cap, deadline, Git error, dedup alias, unrelated StartRef, unresolved StartRef | Full eligible-set pending query is reached before truncation; internal Auto/Task/Target matrix stays Queued with no worktree/session; explicit StartRef only proceeds with proved reviewed-source containment. Request beyond row 256 and lower-ranked alias must still hold. | N plus E R-5 |
| V-6 later task receives brief | integration/Q; `DispatcherSiblingInspectionSchedulingTests.C1349_LaterTaskReceivesBriefBeforeInspectionCompletes` | B+Q; 4: eligible, busy, queue-inserted, lost-wakeup | Block first task's sibling Git without advancing clock; tick completes and later recipient has whole task-marker/body UserPrompt before releasing Git. Busy has zero prompts until idle; real queue recovery reuses the original row exactly once. | N; baseline serial tick red |
| V-7 stale observation rejected | integration; `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch` | B; 15: task ID, attempt, status, card, repository, runner, mode, requested ref, base-task ID, merge target, effective fallback, new request, request state, reviewed source, adopted checkout | Finish an observation, change exactly one dimension, consume via normal claim. Assert obsolete result rejected before worktree/session creation (or no second worktree for adoption); replacement is inspected. For metadata mutations verify query actually observed the changed row. | N; independently mutable guards |
| V-8 owned queue lifecycle | integration; `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers` | B; 10: dedup, active limit, total capacity, consume, expiry, fairness, restart before enqueue, restart after result, error, shutdown | At most 2 active and 32 total; enqueue never awaits space. Repeated ticks one operation; consume removes; 30s expiry re-arms; previously unattempted task enters before held task requeue. Destroy/recreate coordinator over same DB then consume and launch. Stop cancels and joins owned workers; unrelated Working session/seat unchanged. | N |
| V-9 immediate readiness | integration; `RemoteWorkspaceReadinessTests.C1349_CaptureRefreshesReadyHold` | M; 3: binding, legacy path, recovered binding | Before another tick, independent readers see one ready Held transition and awaitingDispatch; task remains Queued, no launch. Event observer reads committed ready state. Retain historical prep event. | N; stale projection baseline red |
| V-10 late/racing capture | integration; `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold` | M; 10: canceled, Working, retry, reassigned, empty path, mismatched path, capacity hold, lease hold, land hold, stale dispatcher writer | Barrier at capture/task lock; move one field only. Late binding retained but no false ready event/path authority. Stronger current hold survives; a cached prep writer cannot append stale text after ready commit. | N |
| V-11 crash/lost hint | integration; `RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification` | M+L; 6: binding-only commit, path-only commit, ready rollback, publish throws, concurrent capture, repeated recovery | Independent pipeline and attention reads truthful; recovery at most one ready event; rollback publishes nothing; publication failure leaves prep failures/backoff unchanged. Retained binding with no task path must be recovered before declaring ready, not guessed from capture's boolean. | N |
| V-12 committed safe log | integration; `DispatchBaseInspectionDiagnosticsTests.C1349_BlockedLogMatchesCommittedReason` | L; 5: standalone commit, outer commit, outer rollback, repeated read, sensitive fields | Exercise all four reason codes internally, plus real noninspection BlockAsync caller. One Information record after committed transition, zero on rollback/re-read; task/card/attempt/reason/counts match independent DB. No goal, subject, stderr or free-form FailureReason sentinel in state/message/exception. | N |
| V-13 useful attention | integration; `DispatchBaseInspectionDiagnosticsTests.C1349_InspectionAttentionExplainsRecovery` | L; 7: 299s, 301s, 901s, prelaunch block, ordinary question words, session-bound block, retried/terminal | Correct absent/Warning/Error, stable condition key, bounded counts; prelaunch inspection block offers OpenDrawer/Cancel, no Reply/Escalate; ordinary question/session case unchanged; resolved item absent. All four strict reason prefixes exercised inside prelaunch case. | N |
| V-14 warning recipient/recovery | integration/Q; `DispatchBaseNotificationTests.C1349_InspectionWarningsReachCaller` | G+Q; ten exact cases in Delivery inventory | Real dispatcher -> immutable intent -> projection -> keyed real queue -> full parent UserPrompt; every cut fires and frozen identity/body survives restart and ref/route movement. | N portable delivery; existing projection tests retained |
| V-15 receipt negative controls | integration; `DispatchBaseNotificationTests.C540_WarningRequiresCompletePrompt`, `C540_WarningRequiresAttemptFloor` | existing isolated DB receipt setup; 1 + 2 | Truncated body, old sequence or old timestamp does not confirm; full prompt at sequence 12 confirms. These deliberately seed rows and classify matching only, not transport. | E |
| V-16 real Git semantics | integration/G; exact R-16 roster below | existing argument matrices | Source/containment/checkout/identity assertions retained. Updated batch fault hook fires. No remote fetch, branch deletion or workspace mutation by inspection. | E/C |
| V-17 authoritative source recheck | integration/G; exact R-17 roster | existing preview, incomplete and pending methods | Under-lease selection respects current source, preview changes, Review refusal and fresh-Code target fallback; worktree HEAD equals actual selected SHA. | E/C |
| V-18 special source/mirror admission | integration/G+M+Q; exact R-18 roster | repair, adoption, SourceLanding, seven prepared admission arms, four C672 recipient arms | Preserve owner/source SHA, occupied checkout and snapshot identity; repair/snapshot/Interim still require their lease. Prepared ordinary mirror receipt uses its runner path through Q. | E/C |
| V-19 leased reference fence | integration; `DispatcherSiblingInspectionSchedulingTests.C1349_LeasedReferenceFence` | G; 7: target moves, requested ref moves, pending ref moves, ref missing, probe error, fence deadline, unchanged | Move refs after background Proceed and before lease; Fresh/StartRef cases prevent automatic resolver from hiding fence defects. Obsolete authority requeues without create/launch, unchanged IDs proceed. Deadline is shared, no new history walk in fence. | N |
| V-20 configuration and wiring | service/integration; `DispatchSiblingInspectionTests.C1349_InspectionConfiguration` | production options/DI; 4: zero inventory, inventory below candidates, valid boundary, scoped worker | Production validator rejects first two; cap==candidates accepted; production hosted worker resolves separate scopes and never retains scoped DbContext after disposal. Not a test-owned duplicate validation predicate. | N |
| V-21 brief handoff recovery | integration/Q; `DispatcherSiblingInspectionSchedulingTests.C1349_BriefHandoffRecovers` | B+Q; 3 cuts in Delivery inventory | Rollback/recovered-live cuts produce one full brief receipt; certified absent launch preserves original custody with zero automatic starts and one full parent hold-note receipt. Use the distinct recovery/recipient oracle above. | N |

### Guards the regression

- R-1–R-14 and R-19–R-21: corresponding V method is also the regression test; the decisive assertions and exact variants above are required, not examples. Method discovery, a count-only assertion or a timeout alone is insufficient.
- R-5 additionally: `AgentTaskDispatchBaseGuardTests.T0442_V20_pending_land_precedes_source_selection_in_every_mode` (6) retains null worktree/branch/session; `T0442_V30_incomplete_dispatch_inspection_blocks_previewed_source_and_keeps_land_hold` (5) retains reason/source preview and pending hold. Update semantic Held count only as described above.
- R-15: both exact C540 receipt methods from V-15 retain `ConfirmedAt == null` for negative evidence and `ConfirmingPromptSequence == 12` for full current evidence.
- R-16: `AgentTaskWorktreeBaseResolverTests.T0442_V03_card_guid_and_git_common_directory_define_identity` (5), `T0442_V04_containment_and_completion_evidence_are_distinct` (7), `T0442_V07_dirty_or_in_progress_checkout_is_excluded` (5), `T0442_V08_local_commit_availability_is_explicit` (5). `AgentTaskDispatchBaseGuardTests.C540_IdenticalTipsCollapse` (1), `C540_AncestorChainCollapses` (1), `C540_DivergentTipsRemainVisible` (1), `C540_ForkKeepsBothTips` (2), `C540_PatchEquivalentSiblingsStillWarn` (1), `C540_RedundantSiblingStillHolds` (2), `C753_NeedsResolutionSiblingWarnsInsteadOfHolding` (1), `C753_ExplicitStartRefContainingReviewedSourceBypassesSiblingHold` (1), `C540_ObservationsRemainPinned` (1), `C559_UnresolvableTipStillWarns` (1), `C540_ProbeCensus` (3). Preserve representative identities, unknown warnings and dirty-file sentinels while replacing obsolete command shapes.
- R-17: `AgentTaskDispatchBaseGuardTests.T0442_V19_dispatch_rechecks_the_create_preview` (3), `T0442_V21_completed_land_releases_hold_and_rechecks_remaining_tips` (4), `T0442_V22_launch_blocks_when_preview_inputs_worsen` (4), `C788_ReviewWithIncompleteInspectionBlocks` (4), `C788_FreshCodeWithTargetPreviewKeepsSafeBase` (4), `C788_ExplicitBaseOverridesIncompleteAutoGuard` (2). Explicit-base allowance applies to incomplete optional Auto discovery only; it never bypasses unknown pending integration or a mandatory leased identity fence.
- R-18: `AgentTaskDispatchBaseGuardTests.T0442_V25_dispatch_adopts_owned_checkout_before_reselecting_invalid_source` (1), `C499_V06_ARepairIsHeldWhileItsOwnerIsLanding` (1), `C508_RepairRecordsOwnerAndSkipsSiblings` (1), `C1213_SourceLandingKeepsLegacyIdentity` (1); `RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease` (7), `C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease` (4), `C1032_CaptureLateReassignment` (1), `C1032_CaptureAttemptAndCancellation` (1), `C1032_CaptureLostReplyRecovery` (1). SourceLanding's C1213 method proves workspace creation/validation, not a live Mutation launch; the C672 snapshot arm pins dispatcher lease admission.
- R-14 additionally: `AgentTaskDispatchBaseGuardTests.C540_ClaimRollbackHasNoCollapsedIntent` (4), `C540_ClaimLoserOwesNoIntent` (1); `DispatchBaseNotificationTests.C540_CollapsedProjectionIsAtomic` (1), `C508_IntentProjectionCustody` (4), `C508_IntentProjectionLostAcknowledgement` (1). No rolled-back/losing claim owes a note; original payload/route/age and one projection survive replay.

### Guard inventory

Every independently bypassable guard below has one distinct PC. Existing unchanged
transport internals beyond these selected boundaries are excluded below; no listed
safety guard is exempt or assigned a shared PC. Conjunction fields are split even
when one method exercises them. A successful-path test is not its positive control.

| Guard | Plan reference + safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-1/S1: DB inventory participates in total deadline | PC-1 |
| G-2 | D-1/S1: Git gate wait participates in total deadline | PC-2 |
| G-3 | D-1/S1: In-flight Git receives the deadline token | PC-3 |
| G-4 | D-1/S1: One monotonic budget spans all phases | PC-4 |
| G-5 | D-1/S1: Command cap enforced before executing another command | PC-5 |
| G-6 | D-2/S1: Expensive-candidate cap enforced | PC-6 |
| G-7 | D-2/S1: Raw inventory bounded and truncation is unknown | PC-7 |
| G-8 | D-1/D-5: Caller shutdown is cancellation, not local failure | PC-8 |
| G-9 | D-2/S1: Pending lookup spans rows outside bounded inventory | PC-9 |
| G-10 | D-2/S1: Ancestor pruning precedes expensive cap in both readers | PC-10 |
| G-11 | D-2/S1: Dedupe retains pending task/request identity | PC-11 |
| G-12 | D-2/S1: Batch output exact-matches requested refs | PC-12 |
| G-13 | D-2/S1: Invalid object IDs are unknown | PC-13 |
| G-14 | D-2/S1: Merged evidence binds same frozen object ID | PC-14 |
| G-15 | D-2/S1: Git error differs from proved absent local branch | PC-15 |
| G-16 | D-3/S1: Unlanded documentation remains a source | PC-16 |
| G-17 | D-3/S1: Pending documentation remains an integration hold | PC-17 |
| G-18 | D-4/S1: Pending integration precedes discovery limits in every mode | PC-18 |
| G-19 | D-4/S1: Explicit StartRef exception requires positive reviewed-source containment | PC-19 |
| G-20 | D-4/S1: Unknown reviewed-source containment cannot bypass hold | PC-20 |
| G-21 | D-4/S1: Reviewed source, not rewritten sibling tip, defines exception | PC-21 |
| G-22 | D-4/S1: NeedsResolution is warning-only | PC-22 |
| G-23 | D-4/S1: Reduction shares inspection budget | PC-23 |
| G-24 | D-4/S1: Unproved reduction edges cannot erase warnings | PC-24 |
| G-25 | D-4/S1: Capped warning output reports omissions | PC-25 |
| G-26 | D-4/S1: Patch-equivalence cannot prove merge-range containment | PC-26 |
| G-27 | D-4/S1: Source identity requires same Git common directory | PC-27 |
| G-28 | D-4/S1: Source identity requires same card GUID | PC-28 |
| G-29 | D-4/S1: Dirty checkout is not eligible source | PC-29 |
| G-30 | D-4/S1: In-progress sequencer is not eligible source | PC-30 |
| G-31 | D-4/S1: Active writer prevents source inheritance | PC-31 |
| G-32 | D-4/S1: Missing local source cannot silently become explicit source | PC-32 |
| G-33 | D-5/S2: Tick never waits for inspection result | PC-33 |
| G-34 | D-5/S2: At most two active inspections | PC-34 |
| G-35 | D-5/S2: 32 total entries including completed results | PC-35 |
| G-36 | D-5/S2: Repeated enqueue deduplicates operation identity | PC-36 |
| G-37 | D-5/S2: Result use compares task ID | PC-37 |
| G-38 | D-5/S2: Result use compares attempt | PC-38 |
| G-39 | D-5/S2: Result use compares Queued status | PC-39 |
| G-40 | D-5/S2: Result use compares card | PC-40 |
| G-41 | D-5/S2: Result use compares canonical repository | PC-41 |
| G-42 | D-5/S2: Result use compares runner | PC-42 |
| G-43 | D-5/S2: Result use compares requested mode | PC-43 |
| G-44 | D-5/S2: Result use compares requested ref | PC-44 |
| G-45 | D-5/S2: Result use compares base-task ID | PC-45 |
| G-46 | D-5/S2: Result use compares merge target | PC-46 |
| G-47 | D-5/S2: Result use compares effective fallback | PC-47 |
| G-48 | D-5/S2: New current request invalidates observation | PC-48 |
| G-49 | D-5/S2: Changed request state invalidates observation | PC-49 |
| G-50 | D-5/S2: Changed reviewed source invalidates observation | PC-50 |
| G-51 | D-5/S2: Adopted worktree invalidates cached preparation authority | PC-51 |
| G-52 | D-5/S2: Target identity fenced under lease | PC-52 |
| G-53 | D-5/S2: Requested ref identity fenced under lease | PC-53 |
| G-54 | D-5/S2: Pending-land exemption ref identity fenced under lease | PC-54 |
| G-55 | D-5/S2: Missing fence ref cannot authorize launch | PC-55 |
| G-56 | D-5/S2: Fence error cannot authorize launch | PC-56 |
| G-57 | D-5/S2: Fence uses claim's shared time/command budget | PC-57 |
| G-58 | D-5/S2: Authoritative resolver still reruns under lease | PC-58 |
| G-59 | D-4/D-5: Incomplete Review inspection never uses fresh-Code fallback | PC-59 |
| G-60 | D-5/S2: Worker cannot claim, create or launch | PC-60 |
| G-61 | D-5/S2: Consumed result removed | PC-61 |
| G-62 | D-5/S2: Unused results expire after 30 seconds | PC-62 |
| G-63 | D-5/S2: Held tasks cannot monopolize requeue order | PC-63 |
| G-64 | D-5/S2: Restart re-arms durable Queued/Held task | PC-64 |
| G-65 | D-5/S2: Local worker error becomes typed incomplete evidence | PC-65 |
| G-66 | D-5/S2: Shutdown joins owned workers | PC-66 |
| G-67 | D-5/S2: Inspection cancellation has no session-stop authority | PC-67 |
| G-68 | D-5/S2: Worker owns a separate disposable scope | PC-68 |
| G-69 | D-2/S1: Inventory setting positive | PC-69 |
| G-70 | D-2/S1: Inventory setting at least candidate cap | PC-70 |
| G-71 | D-6/S3: Current mirror readiness requires Queued status | PC-71 |
| G-72 | D-6/S3: Current mirror readiness requires matching attempt | PC-72 |
| G-73 | D-6/S3: Current mirror readiness requires matching runner | PC-73 |
| G-74 | D-6/S3: Current mirror readiness requires nonempty path | PC-74 |
| G-75 | D-6/S3: Current mirror readiness requires matching captured path | PC-75 |
| G-76 | D-6/S3: Late binding retention independent of current eligibility | PC-76 |
| G-77 | D-6/S3: Readiness does not overwrite stronger capacity hold | PC-77 |
| G-78 | D-6/S3: Readiness does not overwrite stronger lease hold | PC-78 |
| G-79 | D-6/S3: Readiness does not overwrite stronger land hold | PC-79 |
| G-80 | D-6/S3: Ready transition deduplicates under task lock | PC-80 |
| G-81 | D-6/S3: Cached dispatcher cannot append obsolete prep hold | PC-81 |
| G-82 | D-6/S3: Ready state/event obey existing transaction | PC-82 |
| G-83 | D-6/S3: AgentTaskChanged publishes after actual commit | PC-83 |
| G-84 | D-6/S3: Hint publication failure is not mirror failure | PC-84 |
| G-85 | D-6/S3: Pipeline read repairs stale prep interpretation | PC-85 |
| G-86 | D-6/S3: Attention read repairs stale prep interpretation | PC-86 |
| G-87 | D-6/S3: Binding/task readiness commit gap is recoverable | PC-87 |
| G-88 | D-6/S3: Prepared mirror cannot bypass special-source lease | PC-88 |
| G-89 | D-4/S2: Repair uses owner authority, not sibling selection | PC-89 |
| G-90 | D-4/S2: Adoption precedes new source resolution | PC-90 |
| G-91 | D-7/S4: Blocked log emitted only after committed transition | PC-91 |
| G-92 | D-7/S4: Committed block logged exactly once | PC-92 |
| G-93 | D-7/S4: Logs exclude unrestricted sensitive content | PC-93 |
| G-94 | D-7/S4: Inspection attention uses strict reason prefix | PC-94 |
| G-95 | D-7/S4: Inspection attention requires prelaunch state | PC-95 |
| G-96 | D-7/S4: Queued attention respects age thresholds | PC-96 |
| G-97 | D-7/S4: Attention clears after retry/terminal resolution | PC-97 |
| G-98 | D-4/D-5: Warning obligation atomic with successful claim | PC-98 |
| G-99 | D-5: Losing claim creates no warning obligation | PC-99 |
| G-100 | D-4/D-5: Warning payload/route frozen at claim | PC-100 |
| G-101 | D-4/D-5: Projection marker cannot precede atomic event/note commit | PC-101 |
| G-102 | D-5: Warning pre-enqueue failure leaves recoverable debt | PC-102 |
| G-103 | D-5: Queue identity retained across insert/link interruption | PC-103 |
| G-104 | D-5: Lost warning wakeup recovers via real queue | PC-104 |
| G-105 | D-5: Complete body required for warning receipt | PC-105 |
| G-106 | D-5: Receipt must be above attempt sequence floor | PC-106 |
| G-107 | D-5: Receipt without sequence baseline needs attempt timestamp floor | PC-107 |
| G-108 | D-5: Existing complete warning prompt prevents retyping | PC-108 |
| G-109 | D-5: Busy recipient waits until idle | PC-109 |
| G-110 | D-5: Pre-insert brief failure remains recoverable | PC-110 |
| G-111 | D-5: Certified absent launch preserves original custody without automatic replacement | PC-111 |
| G-112 | D-5/S2: Full inspection coordinator never makes serial tick wait for capacity | PC-112 |
| G-113 | D-5/S2: Shutdown cancels owned inspection operations before joining | PC-113 |
| G-114 | D-2/S1: Inventory query reads at most cap+1 rows, independently of overflow classification | PC-114 |

### Positive controls

All PCs below are mandatory post-land SourceLanding Mutation work. Code runs V/R;
ordinary Review judges this design and ordinary evidence before land. **No PC has
been executed by TestDesign.** Run every PC, with baseline -> compiling break ->
expected assertion red -> exact restoration -> green, on its bound method only.
Never count compilation/fixture errors, zero tests, a timeout without the specified
oracle, or a missing mutation hook as red. A barrier witness must also record that
the barrier was entered before advancing its clock or releasing its fixture escape.

Each row names an intended compiling production defect and exact method/variant.
New-file symbols are proposed by the parent plan, not falsely bound to code that
already exists. Code records actual path, member, original/replacement span and
source SHA for each payload in its handoff; payloads not mechanically bound then
remain **binding required after Code**, never implicitly executable/PC-clean.
Mutation binds against the landed snapshot and refuses mismatched spans. If a
planned defect is redundant/unreachable, preserve that result and return for an
explicit repair/design amendment; do not widen a filter or claim a kill.

Use `/*/*/ClassName/ExactMethod*` for these parameterized methods (the suffix selects
argument results, not other test families); inspect the precise intended argument
row in fresh TRX. Single-method nonparameterized filters may omit the suffix. The
ordinary CP filter is not permission to run that whole group for a PC. Use the
external copied checkpoint driver, separate baseline/red/green output paths, the
host slot gate and externally retained per-PC receipts; refresh restored source
mtime or force a verified rebuild. No commit/push from a SourceLanding snapshot.

| PC | Break guard by compiling defect | Exact method / reached variant; decisive red assertion |
|---|---|---|
| PC-1 | Break G-1: pass caller token instead of linked deadline token to inventory query. | `DispatchSiblingInspectionTests.C1349_InspectionLimits`; DB deadline: **inspection_timeout and query cancellation observed**. |
| PC-2 | Break G-2: pass CancellationToken.None to GitProcessGate.EnterAsync. | `DispatchSiblingInspectionTests.C1349_InspectionLimits`; Git-gate deadline: **canceled gate waiter count is 1 before fixture releases the gate**. |
| PC-3 | Break G-3: pass caller token to ILandingGit instead of linked budget token. | `DispatchSiblingInspectionTests.C1349_InspectionLimits`; in-command deadline: **fake Git observed cancellation and no extra command**. |
| PC-4 | Break G-4: reset the operation start timestamp at each phase. | `DispatchSiblingInspectionTests.C1349_InspectionLimits`; cumulative phases: **inspection_timeout after the cumulative five-second boundary**. |
| PC-5 | Break G-5: replace command-limit predicate with false. | `DispatchSiblingInspectionTests.C1349_InspectionLimits`; command cap: **commands <= configured cap**. |
| PC-6 | Break G-6: skip the post-prune candidate-limit branch. | `DispatchSiblingInspectionTests.C1349_InspectionLimits`; expensive-candidate cap: **candidate_limit and zero inspection of candidate cap+1**. |
| PC-7 | Break G-7: return complete for a cap+1 inventory instead of candidate_limit. | `DispatchSiblingInspectionTests.C1349_InspectionLimits`; inventory cap: **candidate_limit, omitted > 0 and not complete**. |
| PC-8 | Break G-8: translate caller OperationCanceledException to inspection_timeout. | `DispatchSiblingInspectionTests.C1349_InspectionLimits`; caller cancellation: **caller cancellation propagated and no persisted Blocked event**. |
| PC-9 | Break G-9: restrict pending lookup to sampled row IDs. | `DispatchSiblingInspectionTests.C1349_PendingLandSurvivesLimits`; pending beyond raw cap: **pending task beyond row 256 still holds with null session**. |
| PC-10 | Break G-10: apply expensive cap before merged-ref elimination. | `DispatchSiblingInspectionTests.C1349_ContainedInventory`; 64 ancestors: **complete target selection for 64 ancestors**. |
| PC-11 | Break G-11: keep only newest alias metadata before pending lookup. | `DispatchSiblingInspectionTests.C1349_PendingLandSurvivesLimits`; dedup alias: **older alias pending request still holds**. |
| PC-12 | Break G-12: accept StartsWith requested ref instead of set membership. | `DispatchSiblingInspectionTests.C1349_ContainedInventory`; prefix-neighbor ref: **unrequested prefix neighbor never supplies selected/contained tip**. |
| PC-13 | Break G-13: store malformed batch object ID as valid. | `DispatchSiblingInspectionTests.C1349_ContainedInventory`; invalid OID: **no complete containment/source from invalid OID**. |
| PC-14 | Break G-14: use merged ref-name membership without comparing SHA. | `DispatchSiblingInspectionTests.C1349_ContainedInventory`; moved merged SHA: **moved branch retained for inspection, not discarded as contained**. |
| PC-15 | Break G-15: convert failed for-each-ref result to empty successful inventory. | `DispatchSiblingInspectionTests.C1349_ContainedInventory`; batch error: **git_inspection_error rather than complete empty selection**. |
| PC-16 | Break G-16: exclude role Plan or docs-only candidate before selection. | `DispatchSiblingInspectionTests.C1349_DocumentationSourceIsPreserved`; unlanded plan: **selected source SHA equals unlanded plan SHA**. |
| PC-17 | Break G-17: skip Plan-role pending sibling. | `DispatchSiblingInspectionTests.C1349_DocumentationSourceIsPreserved`; pending plan: **Queued and null worktree/session**. |
| PC-18 | Break G-18: return explicit Target before pending lookup. | `DispatchSiblingInspectionTests.C1349_PendingLandSurvivesLimits`; candidate cap/deadline/command cap: **all internal mode arms stay held, no session**. |
| PC-19 | Break G-19: bypass pending whenever WorktreeBaseRequestedRef is nonnull. | `DispatchSiblingInspectionTests.C1349_PendingLandSurvivesLimits`; unrelated StartRef: **unrelated requested commit leaves pending hold**. |
| PC-20 | Break G-20: treat failed requested-ref probe as contains-reviewed-source. | `DispatchSiblingInspectionTests.C1349_PendingLandSurvivesLimits`; unresolved StartRef: **unresolved requested ref leaves pending hold**. |
| PC-21 | Break G-21: compare requested commit with current tip instead of ExpectedSourceSha. | `AgentTaskDispatchBaseGuardTests.C753_ExplicitStartRefContainingReviewedSourceBypassesSiblingHold`; existing: **Dispatched with no sibling hold despite later conflict-resolution commit**. |
| PC-22 | Break G-22: include NeedsResolution in active pending predicate. | `AgentTaskDispatchBaseGuardTests.C753_NeedsResolutionSiblingWarnsInsteadOfHolding`; existing: **Dispatched plus matching warning intent**. |
| PC-23 | Break G-23: skip deadline/command gate in pairwise reduction. | `DispatchSiblingInspectionTests.C1349_ReductionRemainsBounded`; enrichment deadline: **bounded command count and explicit incomplete enrichment**. |
| PC-24 | Break G-24: treat unknown ancestry as true. | `DispatchSiblingInspectionTests.C1349_ReductionRemainsBounded`; 64 divergent: **every retained divergent tip visible or explicitly counted omitted**. |
| PC-25 | Break G-25: omit the bounded omitted-count summary. | `DispatchSiblingInspectionTests.C1349_ReductionRemainsBounded`; 64 divergent: **summary omitted count equals unrepresented candidates**. |
| PC-26 | Break G-26: remove merge-range refusal before cherry. | `AgentTaskWorktreeBaseResolverTests.T0442_V04_containment_and_completion_evidence_are_distinct`; merge_range: **merge branch remains unknown with warning**. |
| PC-27 | Break G-27: accept equal origin URLs as same repository. | `AgentTaskWorktreeBaseResolverTests.T0442_V03_card_guid_and_git_common_directory_define_identity`; same_origin_clone: **foreign clone is not selected as source**. |
| PC-28 | Break G-28: query by card identifier instead of GUID. | `AgentTaskWorktreeBaseResolverTests.T0442_V03_card_guid_and_git_common_directory_define_identity`; same_identifier_other_board: **other-board source is not selected**. |
| PC-29 | Break G-29: return true from dirty-status eligibility check. | `AgentTaskWorktreeBaseResolverTests.T0442_V07_dirty_or_in_progress_checkout_is_excluded`; tracked_unstaged: **explicit source refused requested_source_invalid**. |
| PC-30 | Break G-30: skip sequencer state eligibility check. | `AgentTaskWorktreeBaseResolverTests.T0442_V07_dirty_or_in_progress_checkout_is_excluded`; merge_in_progress: **explicit source refused and MERGE_HEAD unchanged**. |
| PC-31 | Break G-31: omit active-writer exclusion. | `AgentTaskDispatchBaseGuardTests.T0442_V22_launch_blocks_when_preview_inputs_worsen`; task_active: **Blocked, requested_source_invalid, null new worktree**. |
| PC-32 | Break G-32: substitute remote-tracking tip for absent local ref. | `AgentTaskWorktreeBaseResolverTests.T0442_V08_local_commit_availability_is_explicit`; remote_only: **explicit source Unknown/requested_source_invalid**. |
| PC-33 | Break G-33: await owned inspection completion in TickAsync before continuing. | `DispatcherSiblingInspectionSchedulingTests.C1349_LaterTaskReceivesBriefBeforeInspectionCompletes`; eligible: **later recipient complete UserPrompt exists while Git barrier remains closed**. |
| PC-34 | Break G-34: change active inspection limit from 2 to 3. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; active limit: **maximum concurrent entered workers <= 2**. |
| PC-35 | Break G-35: count only running entries for capacity. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; total capacity: **total outstanding <= 32 and overflow tick returned before release**. |
| PC-36 | Break G-36: remove existing-key rejection. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; dedup: **one worker invocation for repeated same-identity ticks**. |
| PC-37 | Break G-37: remove only the task ID comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; task ID: **obsolete task ID result rejected before create/launch**. |
| PC-38 | Break G-38: remove only the attempt comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; attempt: **obsolete attempt result rejected before create/launch**. |
| PC-39 | Break G-39: remove only the Queued status comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; status: **obsolete status result rejected before create/launch**. |
| PC-40 | Break G-40: remove only the card comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; card: **obsolete card result rejected before create/launch**. |
| PC-41 | Break G-41: remove only the canonical repository comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; repository: **obsolete repository result rejected before create/launch**. |
| PC-42 | Break G-42: remove only the runner comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; runner: **obsolete runner result rejected before create/launch**. |
| PC-43 | Break G-43: remove only the requested mode comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; mode: **obsolete mode result rejected before create/launch**. |
| PC-44 | Break G-44: remove only the requested ref comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; requested ref: **obsolete requested ref result rejected before create/launch**. |
| PC-45 | Break G-45: remove only the base-task ID comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; base-task ID: **obsolete base-task ID result rejected before create/launch**. |
| PC-46 | Break G-46: remove only the merge target comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; merge target: **obsolete merge target result rejected before create/launch**. |
| PC-47 | Break G-47: remove only the effective fallback comparison from result acceptance. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; effective fallback: **obsolete effective fallback result rejected before create/launch**. |
| PC-48 | Break G-48: skip current-request ID re-read comparison. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; new request: **new pending request holds, no worktree/session**. |
| PC-49 | Break G-49: omit IsPending/state from captured metadata comparison. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; request state: **changed pending state invalidates cached Proceed**. |
| PC-50 | Break G-50: omit ExpectedSourceSha/original-source metadata comparison. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; reviewed source: **new reviewed-source request cannot use old containment**. |
| PC-51 | Break G-51: accept cached result despite now-recorded owned checkout. | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch`; adopted checkout: **no second checkout/session and adopted HEAD retained**. |
| PC-52 | Break G-52: skip target frozen-SHA comparison. | `DispatcherSiblingInspectionSchedulingTests.C1349_LeasedReferenceFence`; target moves: **requeued, no worktree/session from old target authority**. |
| PC-53 | Break G-53: skip requested-ref SHA comparison. | `DispatcherSiblingInspectionSchedulingTests.C1349_LeasedReferenceFence`; requested ref moves: **StartRef task requeued before worktree creation**. |
| PC-54 | Break G-54: skip pending ref SHA comparison. | `DispatcherSiblingInspectionSchedulingTests.C1349_LeasedReferenceFence`; pending ref moves: **containment exemption invalidated, no launch**. |
| PC-55 | Break G-55: accept null current commit in fence. | `DispatcherSiblingInspectionSchedulingTests.C1349_LeasedReferenceFence`; ref missing: **missing ref requeues with null session**. |
| PC-56 | Break G-56: catch Git fence exception and return Proceed. | `DispatcherSiblingInspectionSchedulingTests.C1349_LeasedReferenceFence`; probe error: **error requeues with null session**. |
| PC-57 | Break G-57: give fence a fresh independent timeout. | `DispatcherSiblingInspectionSchedulingTests.C1349_LeasedReferenceFence`; fence deadline: **shared-budget exhaustion prevents create/launch**. |
| PC-58 | Break G-58: reuse preview/base result instead of ResolveAsync under lease. | `AgentTaskDispatchBaseGuardTests.T0442_V19_dispatch_rechecks_the_create_preview`; new_descendant: **worktree HEAD/source task equals current descendant**. |
| PC-59 | Break G-59: remove Review exclusion from target fallback. | `AgentTaskDispatchBaseGuardTests.C788_ReviewWithIncompleteInspectionBlocks`; deadline: **Blocked with null worktree/session**. |
| PC-60 | Break G-60: in the worker's own scope, save the task's Status as Dispatched before publishing the result. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; restart after result: **before consumption tick: Queued, no worktree/session/launch**. |
| PC-61 | Break G-61: leave completed result in store after Take. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; consume: **same completed operation cannot be consumed twice**. |
| PC-62 | Break G-62: disable result expiry predicate. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; expiry: **after 30s new operation is inspected before admission**. |
| PC-63 | Break G-63: prepend consumed-held task ahead of unattempted IDs. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; fairness: **first newly freed worker belongs to unattempted task**. |
| PC-64 | Break G-64: skip enqueue when latest Held already says inspection pending. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; restart before enqueue: **new provider runs inspection and consumes result**. |
| PC-65 | Break G-65: catch worker exception and publish complete Proceed. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; error: **git_inspection_error, no unsafe launch**. |
| PC-66 | Break G-66: return from StopAsync without awaiting workers. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; shutdown: **all entered workers exited before StopAsync completes**. |
| PC-67 | Break G-67: invoke injected delegate session stopper for unrelated Working task on stop. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; shutdown: **unrelated Working session/seat unchanged, stopper calls 0**. |
| PC-68 | Break G-68: reuse tick scope for queued work. | `DispatchSiblingInspectionTests.C1349_InspectionConfiguration`; scoped worker: **worker DbContext is distinct and valid after producer scope disposed**. |
| PC-69 | Break G-69: remove positive-value validator. | `DispatchSiblingInspectionTests.C1349_InspectionConfiguration`; zero inventory: **production options validation rejects zero**. |
| PC-70 | Break G-70: remove cross-setting validator. | `DispatchSiblingInspectionTests.C1349_InspectionConfiguration`; inventory below candidates: **production options validation rejects inventory<candidates**. |
| PC-71 | Break G-71: omit Queued predicate in readiness update. | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold`; canceled: **binding may remain retained but no false ready event/current readiness**. |
| PC-72 | Break G-72: omit attempt equality in readiness helper. | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold`; retry: **binding may remain retained but no false ready event/current readiness**. |
| PC-73 | Break G-73: omit runner equality in readiness helper. | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold`; reassigned: **binding may remain retained but no false ready event/current readiness**. |
| PC-74 | Break G-74: omit nonempty-path check. | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold`; empty path: **binding may remain retained but no false ready event/current readiness**. |
| PC-75 | Break G-75: omit equality to current recorded path. | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold`; mismatched path: **binding may remain retained but no false ready event/current readiness**. |
| PC-76 | Break G-76: return before retaining binding when task is canceled. | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold`; canceled: **late binding row still exists with original identity**. |
| PC-77 | Break G-77: allow readiness transition over current capacity hold. | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold`; capacity hold: **latest hold and projected reason remain the stronger admission reason**. |
| PC-78 | Break G-78: allow readiness transition over current lease hold. | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold`; lease hold: **latest hold and projected reason remain the stronger admission reason**. |
| PC-79 | Break G-79: allow readiness transition over current land hold. | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold`; land hold: **latest hold and projected reason remain the stronger admission reason**. |
| PC-80 | Break G-80: remove locked recheck of existing ready transition. | `RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification`; concurrent capture: **concurrent identical captures leave exactly one ready Held event**. |
| PC-81 | Break G-81: omit current readiness recheck in TraceHeld path. | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold`; stale dispatcher writer: **latest Held never returns to in-flight prep after ready commit**. |
| PC-82 | Break G-82: save ready event in independent committed context inside outer transaction. | `RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification`; ready rollback: **outer rollback leaves no ready event and no publication**. |
| PC-83 | Break G-83: publish AgentTaskChanged before committing readiness transaction. | `RemoteWorkspaceReadinessTests.C1349_CaptureRefreshesReadyHold`; binding: **event-time independent reader already sees ready event/path**. |
| PC-84 | Break G-84: let IEventBus exception enter prep failure/backoff catch. | `RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification`; publish throws: **RemotePrepFailures and DispatchNotBeforeAt unchanged**. |
| PC-85 | Break G-85: trust old prep Held even with current matching remote path. | `RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification`; path-only commit: **queueReason is awaitingDispatch before recovery tick**. |
| PC-86 | Break G-86: keep in-flight preparation headline for current ready path. | `RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification`; path-only commit: **attention does not claim mirror preparation is running**. |
| PC-87 | Break G-87: return early on retained BindingId before restoring current task path. | `RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification`; binding-only commit: **recovery restores matching path and one ready event**. |
| PC-88 | Break G-88: remove SourceLanding exclusion from LaunchesPreparedMirror. | `RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease`; snapshot: **snapshot stays Queued and sink empty while lease held**. |
| PC-89 | Break G-89: remove RepairSourceTaskId exclusion from sibling inspection admission. | `AgentTaskDispatchBaseGuardTests.C508_RepairRecordsOwnerAndSkipsSiblings`; existing: **Repair source SHA equals owner; no sibling DispatchBase intent**. |
| PC-90 | Break G-90: resolve source before trying owned checkout adoption. | `AgentTaskDispatchBaseGuardTests.T0442_V25_dispatch_adopts_owned_checkout_before_reselecting_invalid_source`; existing: **existing checkout/task HEAD unchanged and dispatched**. |
| PC-91 | Break G-91: log successful block immediately after SaveChanges in outer transaction. | `DispatchBaseInspectionDiagnosticsTests.C1349_BlockedLogMatchesCommittedReason`; outer rollback: **zero successful-block Information records after rollback**. |
| PC-92 | Break G-92: remove post-commit block-log call. | `DispatchBaseInspectionDiagnosticsTests.C1349_BlockedLogMatchesCommittedReason`; standalone commit/repeated read: **exactly one matching structured Information record**. |
| PC-93 | Break G-93: add FailureReason as structured diagnostic property. | `DispatchBaseInspectionDiagnosticsTests.C1349_BlockedLogMatchesCommittedReason`; sensitive fields: **sentinel absent from all structured fields/message/exception**. |
| PC-94 | Break G-94: classify by Contains(inspection_timeout) instead of strict prefix. | `DispatchBaseInspectionDiagnosticsTests.C1349_InspectionAttentionExplainsRecovery`; ordinary question words: **ordinary question retains original Reply behavior**. |
| PC-95 | Break G-95: omit prelaunch/session condition from classifier. | `DispatchBaseInspectionDiagnosticsTests.C1349_InspectionAttentionExplainsRecovery`; session-bound block: **session-bound question retains original actions**. |
| PC-96 | Break G-96: emit DispatchHeld before warning threshold. | `DispatchBaseInspectionDiagnosticsTests.C1349_InspectionAttentionExplainsRecovery`; 299s: **no DispatchHeld at 299s**. |
| PC-97 | Break G-97: reuse prior inspection condition without current status/stint check. | `DispatchBaseInspectionDiagnosticsTests.C1349_InspectionAttentionExplainsRecovery`; retried/terminal: **old inspection item absent**. |
| PC-98 | Break G-98: persist warning intent outside claim transaction. | `AgentTaskDispatchBaseGuardTests.C540_ClaimRollbackHasNoCollapsedIntent`; afterSave: **rolled-back claim has zero intent, session and dispatch event**. |
| PC-99 | Break G-99: bypass concurrency-token refusal. | `AgentTaskDispatchBaseGuardTests.C540_ClaimLoserOwesNoIntent`; existing: **null worktree/session and zero warning intents**. |
| PC-100 | Break G-100: recompose warning from current task route/refs during materialization. | `DispatchBaseNotificationTests.C1349_InspectionWarningsReachCaller`; claim-committed: **original body and parent receive complete prompts; replacement parent receives none**. |
| PC-101 | Break G-101: persist MaterializedAt separately before projection commit. | `DispatchBaseNotificationTests.C540_CollapsedProjectionIsAtomic`; existing: **fault leaves marker null and zero event/note; replay produces one pair**. |
| PC-102 | Break G-102: mark notification confirmed/NotRequired on enqueue IOException. | `DispatchBaseNotificationTests.C1349_InspectionWarningsReachCaller`; enqueue-fails: **retry reaches original parent's full UserPrompt**. |
| PC-103 | Break G-103: omit SourceLandNotificationId on enqueue. | `DispatchBaseNotificationTests.C1349_InspectionWarningsReachCaller`; queue-inserted-before-link: **same notification reuses one queue ID and one complete prompt**. |
| PC-104 | Break G-104: suppress recovered notification's pending flush request. | `DispatchBaseNotificationTests.C1349_InspectionWarningsReachCaller`; lost-wakeup: **full parent UserPrompt appears after reconstructed service pumps**. |
| PC-105 | Break G-105: replace IsCompleteIn with prefix IsConfirmedBy. | `DispatchBaseNotificationTests.C540_WarningRequiresCompletePrompt`; existing: **ConfirmedAt remains null for truncated prompt**. |
| PC-106 | Break G-106: ignore LastDeliveryBaselineSequence. | `DispatchBaseNotificationTests.C540_WarningRequiresAttemptFloor`; sequenceFloor=true: **ConfirmedAt remains null for sequence 9 below floor 10**. |
| PC-107 | Break G-107: ignore LastDeliveryStartedAt timestamp floor. | `DispatchBaseNotificationTests.C540_WarningRequiresAttemptFloor`; sequenceFloor=false: **ConfirmedAt remains null for five-minute-old prompt**. |
| PC-108 | Break G-108: skip late transcript reconciliation and retry typing. | `DispatchBaseNotificationTests.C1349_InspectionWarningsReachCaller`; prompt-before-verdict: **one matching UserPrompt per notification and attempts unchanged**. |
| PC-109 | Break G-109: use MessageSendMode.Immediate for delegation brief. | `DispatcherSiblingInspectionSchedulingTests.C1349_LaterTaskReceivesBriefBeforeInspectionCompletes`; busy: **zero complete prompts and Pending while busy, then exactly one after idle**. |
| PC-110 | Break G-110: disable missing-brief EnsureLaunchBrief recovery call. | `DispatcherSiblingInspectionSchedulingTests.C1349_BriefHandoffRecovers`; before-brief-insert: **one matching complete UserPrompt after ResumeInterruptedLaunchAsync**. |
| PC-111 | Break G-111: change certified-absent transition from Blocked to Queued so the next tick automatically relaunches. | `DispatcherSiblingInspectionSchedulingTests.C1349_BriefHandoffRecovers`; after-claim-before-launch: **original task Blocked, unchanged attempt/session, zero automatic starts, full parent hold-note receipt**. |
| PC-112 | Break G-112: replace full-queue TryWrite/refusal with synchronous waiting on WriteAsync(item).AsTask().GetAwaiter().GetResult(). | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; total capacity: **overflow tick completed before any worker/capacity barrier release**. |
| PC-113 | Break G-113: omit owned worker token cancellation during StopAsync, retaining the join. | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`; shutdown: **entered workers observed cancellation before fixture emergency release; no worker remains after StopAsync**. |
| PC-114 | Break G-114: remove Take(cap+1) from inventory query while retaining overflow refusal. | `DispatchSiblingInspectionTests.C1349_InspectionLimits`; inventory cap: **DbCommandInterceptor records bounded SQL/result read <= cap+1, not merely final candidate_limit**. |

Design audit: **guards=114, mapped=114, missing=0, duplicate PC maps=0**.
Execution audit at TestDesign: 0/114 run; all 114 must run after land. This is
not a mutation-clean claim and does not defer ordinary V/R to Mutation.

### Out of scope

- Native PTY/provider and browser/SignalR acceptance. No input protocol or client implementation changes; Q and durable read-model substitutes have the explicit limits above. The ten native C540 E2E methods were read for cut semantics, not silently commissioned as a second suite.
- Real fleet scheduling, throughput benchmark, daemon restarts, deployment/activation, branch pruning, release-candidate publication, production sessions and parking. No test may stop a real Working session. Master publication is not activation.
- Full assemblies, namespace/category sweeps, unrelated landing/verification custody, report delivery, alternate provider behavior and every historical receipt guard. Only the changed paths and selected unchanged safety boundaries are within this manifest. SourceLanding snapshot lifecycle is not requalified by C1213 workspace creation.
- Migration/client tests: no schema or wire enum change is planned. If Code introduces one, return for a scoped design amendment and its owner checks; do not expand this closed list silently.

### Checkpoints

**Combined S1–S4 ordinary scope.** Commit all four slices before this selection;
all rows have the same `After` so one isolated build can be reused with no source
edits. Each row is one exact filter against that build; CP-1 is the isolated build,
all later rows explicitly reuse it. If slices are commissioned/landed separately,
the caller must commission a corresponding manifest amendment (not run these
all-slice rows on missing code). The union is the entire ordinary Code scope.
All rows are portable Antiphon.Tests; no suite or namespace filter is authorized.
The literal OR separators are escaped for Markdown as required by the importer.
Every `Min` is a TUnit execution count; internal matrices do not inflate it.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S4 | `tests/Antiphon.Tests -> bin-c1349-all/` | v1-inspectionlimits | `/*/*/DispatchSiblingInspectionTests/C1349_InspectionLimits*` | V-1, R-1 | all listed, 0 failed/skipped | 8 | 7 |
| CP-2 | S1-S4 | CP-1 | v2-containedinventory | `/*/*/DispatchSiblingInspectionTests/C1349_ContainedInventory*` | V-2, R-2 | all listed, 0 failed/skipped | 7 | 2 |
| CP-3 | S1-S4 | CP-1 | v3-documentationsourceispreserved | `/*/*/DispatchSiblingInspectionTests/C1349_DocumentationSourceIsPreserved*` | V-3, R-3 | all listed, 0 failed/skipped | 2 | 1 |
| CP-4 | S1-S4 | CP-1 | v4-reductionremainsbounded | `/*/*/DispatchSiblingInspectionTests/C1349_ReductionRemainsBounded*` | V-4, R-4 | all listed, 0 failed/skipped | 3 | 1 |
| CP-5 | S1-S4 | CP-1 | v5-pendinglandsurviveslimits | `/*/*/DispatchSiblingInspectionTests/C1349_PendingLandSurvivesLimits*` | V-5, R-5 | all listed, 0 failed/skipped | 8 | 2 |
| CP-6 | S1-S4 | CP-1 | v20-inspectionconfiguration | `/*/*/DispatchSiblingInspectionTests/C1349_InspectionConfiguration*` | V-20, R-20 | all listed, 0 failed/skipped | 4 | 1 |
| CP-7 | S1-S4 | CP-1 | v6-latertaskreceivesbriefbeforeinspectioncompletes | `/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_LaterTaskReceivesBriefBeforeInspectionCompletes*` | V-6, R-6 | all listed, 0 failed/skipped | 4 | 3 |
| CP-8 | S1-S4 | CP-1 | v7-staleinspectioncannotdispatch | `/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_StaleInspectionCannotDispatch*` | V-7, R-7 | all listed, 0 failed/skipped | 15 | 2 |
| CP-9 | S1-S4 | CP-1 | v8-inspectionqueuerecovers | `/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_InspectionQueueRecovers*` | V-8, R-8 | all listed, 0 failed/skipped | 10 | 2 |
| CP-10 | S1-S4 | CP-1 | v19-leasedreferencefence | `/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_LeasedReferenceFence*` | V-19, R-19 | all listed, 0 failed/skipped | 7 | 2 |
| CP-11 | S1-S4 | CP-1 | v21-briefhandoffrecovers | `/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_BriefHandoffRecovers*` | V-21, R-21 | all listed, 0 failed/skipped | 3 | 3 |
| CP-12 | S1-S4 | CP-1 | v9-capturerefreshesreadyhold | `/*/*/RemoteWorkspaceReadinessTests/C1349_CaptureRefreshesReadyHold*` | V-9, R-9 | all listed, 0 failed/skipped | 3 | 2 |
| CP-13 | S1-S4 | CP-1 | v10-latecapturecannotreplacecurrenthold | `/*/*/RemoteWorkspaceReadinessTests/C1349_LateCaptureCannotReplaceCurrentHold*` | V-10, R-10 | all listed, 0 failed/skipped | 10 | 2 |
| CP-14 | S1-S4 | CP-1 | v11-readyprojectionsurviveslostnotification | `/*/*/RemoteWorkspaceReadinessTests/C1349_ReadyProjectionSurvivesLostNotification*` | V-11, R-11 | all listed, 0 failed/skipped | 6 | 2 |
| CP-15 | S1-S4 | CP-1 | v12-blockedlogmatchescommittedreason | `/*/*/DispatchBaseInspectionDiagnosticsTests/C1349_BlockedLogMatchesCommittedReason*` | V-12, R-12 | all listed, 0 failed/skipped | 5 | 2 |
| CP-16 | S1-S4 | CP-1 | v13-inspectionattentionexplainsrecovery | `/*/*/DispatchBaseInspectionDiagnosticsTests/C1349_InspectionAttentionExplainsRecovery*` | V-13, R-13 | all listed, 0 failed/skipped | 7 | 1 |
| CP-17 | S1-S4 | CP-1 | v14-inspectionwarningsreachcaller | `/*/*/DispatchBaseNotificationTests/C1349_InspectionWarningsReachCaller*` | V-14, R-14 | all listed, 0 failed/skipped | 10 | 5 |
| CP-18 | S1-S4 | CP-1 | negative-receipts | `/*/*/DispatchBaseNotificationTests/(C540_WarningRequiresCompletePrompt*)\|(C540_WarningRequiresAttemptFloor*)` | V-15, R-15 | all listed, 0 failed/skipped | 3 | 1 |
| CP-19 | S1-S4 | CP-1 | git-identity-containment | `/*/*/AgentTaskWorktreeBaseResolverTests/(T0442_V03_card_guid_and_git_common_directory_define_identity*)\|(T0442_V04_containment_and_completion_evidence_are_distinct*)` | V-16, R-16 | all listed, 0 failed/skipped | 12 | 3 |
| CP-20 | S1-S4 | CP-1 | git-checkout-local-refs | `/*/*/AgentTaskWorktreeBaseResolverTests/(T0442_V07_dirty_or_in_progress_checkout_is_excluded*)\|(T0442_V08_local_commit_availability_is_explicit*)` | V-16, R-16 | all listed, 0 failed/skipped | 10 | 3 |
| CP-21 | S1-S4 | CP-1 | small-warning-graphs | `/*/*/AgentTaskDispatchBaseGuardTests/(C540_IdenticalTipsCollapse*)\|(C540_AncestorChainCollapses*)\|(C540_DivergentTipsRemainVisible*)\|(C540_ForkKeepsBothTips*)\|(C540_PatchEquivalentSiblingsStillWarn*)` | V-16, R-16 | all listed, 0 failed/skipped | 6 | 2 |
| CP-22 | S1-S4 | CP-1 | pending-reviewed-source | `/*/*/AgentTaskDispatchBaseGuardTests/(C540_RedundantSiblingStillHolds*)\|(C753_NeedsResolutionSiblingWarnsInsteadOfHolding*)\|(C753_ExplicitStartRefContainingReviewedSourceBypassesSiblingHold*)` | V-16, R-16 | all listed, 0 failed/skipped | 4 | 1 |
| CP-23 | S1-S4 | CP-1 | frozen-tips-census | `/*/*/AgentTaskDispatchBaseGuardTests/(C540_ObservationsRemainPinned*)\|(C559_UnresolvableTipStillWarns*)\|(C540_ProbeCensus*)` | V-2, V-4, V-16, R-2, R-4, R-16 | all listed, 0 failed/skipped | 5 | 2 |
| CP-24 | S1-S4 | CP-1 | pending-and-incomplete | `/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V20_pending_land_precedes_source_selection_in_every_mode*)\|(T0442_V30_incomplete_dispatch_inspection_blocks_previewed_source_and_keeps_land_hold*)` | V-5, R-5 | all listed, 0 failed/skipped | 11 | 3 |
| CP-25 | S1-S4 | CP-1 | preview-recheck | `/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V19_dispatch_rechecks_the_create_preview*)\|(T0442_V21_completed_land_releases_hold_and_rechecks_remaining_tips*)\|(T0442_V22_launch_blocks_when_preview_inputs_worsen*)` | V-17, R-17 | all listed, 0 failed/skipped | 11 | 3 |
| CP-26 | S1-S4 | CP-1 | incomplete-base-modes | `/*/*/AgentTaskDispatchBaseGuardTests/(C788_ReviewWithIncompleteInspectionBlocks*)\|(C788_FreshCodeWithTargetPreviewKeepsSafeBase*)\|(C788_ExplicitBaseOverridesIncompleteAutoGuard*)` | V-17, R-17 | all listed, 0 failed/skipped | 10 | 2 |
| CP-27 | S1-S4 | CP-1 | special-sources | `/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V25_dispatch_adopts_owned_checkout_before_reselecting_invalid_source*)\|(C499_V06_ARepairIsHeldWhileItsOwnerIsLanding*)\|(C508_RepairRecordsOwnerAndSkipsSiblings*)\|(C1213_SourceLandingKeepsLegacyIdentity*)` | V-18, R-18 | all listed, 0 failed/skipped | 4 | 3 |
| CP-28 | S1-S4 | CP-1 | mirror-admission | `/*/*/RemoteWorkspacePreparerTests/C672_prepared_task_launches_while_a_land_holds_the_lease*` | V-18, R-18 | all listed, 0 failed/skipped | 7 | 2 |
| CP-29 | S1-S4 | CP-1 | mirror-recipient | `/*/*/RemoteWorkspacePreparerTests/C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease*` | V-18, R-18 | all listed, 0 failed/skipped | 4 | 3 |
| CP-30 | S1-S4 | CP-1 | late-mirror-regressions | `/*/*/RemoteWorkspacePreparerTests/(C1032_CaptureLateReassignment*)\|(C1032_CaptureAttemptAndCancellation*)\|(C1032_CaptureLostReplyRecovery*)` | V-10, V-11, V-18, R-10, R-11, R-18 | all listed, 0 failed/skipped | 3 | 3 |
| CP-31 | S1-S4 | CP-1 | claim-atomicity | `/*/*/AgentTaskDispatchBaseGuardTests/(C540_ClaimRollbackHasNoCollapsedIntent*)\|(C540_ClaimLoserOwesNoIntent*)` | V-14, R-14 | all listed, 0 failed/skipped | 5 | 2 |
| CP-32 | S1-S4 | CP-1 | projection-custody | `/*/*/DispatchBaseNotificationTests/(C540_CollapsedProjectionIsAtomic*)\|(C508_IntentProjectionCustody*)\|(C508_IntentProjectionLostAcknowledgement*)` | V-14, R-14 | all listed, 0 failed/skipped | 6 | 2 |


Run through the checkpoint tool using this document, one committed group, and the
full implementation SHA:

```text
run --plan docs/superpowers/plans/2026-10-19-card-1349-dispatch-sibling-walk-test-design.md --after S1-S4 --expected-source-sha <full-code-sha>
```

Use the documented tool `run`/`wait` lifecycle until exit is not 75. If bootstrapping
is needed, build the checkpoint tool once through `scripts/build-slot.ps1` into an
owned alternate output and invoke its built driver without an outer slot; rows
acquire their own slots. Report that infrastructure-only exception and its time.
Exit 4 is not-run/blocked, never an unleased retry. Foreground-own every driver and
wait for completion. Keep code frozen throughout; record every CP's executed roster,
counts, failures, unedited CHECKPOINT line and validated source/build receipt.
Code/Review also run `scripts/check-evidence-diff.ps1` over task-base..pushed-tip.
Delete only the producer-owned alternate outputs after recording provenance.

Tests-first reds use the exact relevant method at a committed tests-only slice;
commit/push before the long run and report separately. For extracted/new types, a
pre-fix witness must compile against the old dispatcher/resolver entry point: no
missing type counts as red. For new asynchronous guards with no old equivalent,
the post-land PC is mandatory. Ordinary qualification does not claim these PCs ran.
A failing row is fixed and repeated as that row with costs retained; no unrelated
rerun after a clean result.

### Cost

All numbers below are **estimated**, not measured in TestDesign. Build-slot wait,
restore/download variance and any reruns are reported separately, never erased.

- Ordinary V/R floor (Code): **75 minutes**, exactly the sum of CP-1–CP-32
  EstimatedMinutes; **213 TUnit executions**, one isolated product build.
  CP-1 includes 5 minutes build/setup + 2 minutes its exact filter. The other
  **68 minutes** are the explicitly named CP-2–CP-32 filters above. Infrastructure
  tool bootstrap allowance is **3 minutes**, outside the product-row floor.
- Every PC has the exact method filter from its table row: transform
  `Class.Method` to `/*/*/Class/Method*`, never a whole class. **PC-1–PC-114: 8 minutes
  each** = baseline isolated build+method 2.5 + mutation/apply 0.25 + red isolated
  build+method 2.5 + restore/mtime check 0.25 + restored isolated build+method 2.5.
  Thus the separate Mutation floor is **114 × 8 = 912 minutes**. All PCs run; none
  are sampled. This includes per-phase build and restoration cost. Long real-Git
  argument methods may exceed the estimate; report actual elapsed times.
- Combined setup + ordinary + post-land PC floor = **3 + 75 + 912 = 990
  minutes** (16.50 hours), excluding authoring, ordinary Review, landing,
  slot waits and failures. Mutation is a separately commissioned companion after
  ordinary Code/Review/land, not part of the Code dispatch time budget.
- Build reuse saves **31 × 5 = 155 estimated minutes** versus rebuilding the
  same graph for every ordinary filter. Narrow filters exclude a full assembly;
  no numerical suite saving is claimed without a current measured baseline.
  PC batching savings are **0 minutes**: these controls mostly share dispatcher,
  inspection, readiness or notification files/methods and cannot be safely assumed
  independent before Code binds their spans. Any legal later batching requires
  different files AND methods, distinct per-PC evidence, and a revised measured
  cost; it cannot remove a control.
- The parent proposed 33 minutes/52 executions before binding recovery and fences.
  This final ordinary estimate adds 42 minutes and 161 executions for the
  named boundary matrices and exact historical regressions. Its 5–10 minute
  allowance was provisional, not a ceiling. The PC cost is large because a new
  cached launch-admission result has independently bypassable identity/authority
  checks; it is not hidden inside the ordinary floor.

Before Code handoff: required sections present; all V/R IDs covered by the table;
guards=114, mapped=114, missing=0, duplicate PC maps=0; numeric costs complete.
PC execution remains 0/114 at this design-only stage, with all 114 required after land.
No product code, suite run, deployment or implementation dispatch was performed.

TestDesign static checks passed: nine required sections, 32 valid nine-column CP
rows, complete V/R-1–V/R-21 union, 114 distinct guard/PC mappings and arithmetic
above. A source-declaration audit found all 39 selected existing test methods and
their 101 argument-expanded cases; the remaining 112 executions are planned new
cases, not existing coverage. These checks do not substitute for Code's executed
TRX roster, build provenance or Mutation's red/restore/green results.
