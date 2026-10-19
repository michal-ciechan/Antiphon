# CARD-1349: bound sibling inspection and keep dispatch moving

Plan date: 2026-10-19. Target: **master only**; the release candidate is closed.
Stage: Plan complete; **TestDesign is separate and next**. The checkpoint manifest
below is the proposed ordinary selection, not a claim that verification ran or that
the full Verification design/guard/positive-control inventory already exists.

## Outcome and scope

A large same-card branch history must not run an unbounded Git walk on the serial
dispatch tick. Inspect a bounded inventory, use batched ref observations before
expensive containment work, and perform the legacy sibling-warning/landing guard
outside the tick. Preserve automatic base selection, explicit-base precedence,
pending-land holds and conservative treatment of incomplete evidence. A completed
runner mirror must immediately stop presenting an in-flight preparation hold.
Inspection refusals must be visible in logs and the existing attention feed.

This is a server scheduling and diagnostic change. It does not introduce parallel
task launch, alter host/role quotas, prune branches, restart services, enable parking,
change landing authority, or dispatch another delegate. No product code is changed
by this Plan task.

## Evidence and ground truth

Inspected source baseline: `f99f8bffb5887d43fafae61ae979ae9e11a36f80`.
Card read with `scripts/card.ps1 get CARD-1349 -Board Antiphon`; revision count was
zero. Fetched `feat/card-task-3f531f98` and read the complete investigation at
`f17d89500ffe687eab93dfe4e94c0eabf1fb4cd6`:
`docs/investigations/2026-10-19-server2-remoteprep-stall.md`. That file is on the
evidence branch, not assumed present on master. The investigation's measurements
are historical observations, not timings newly reproduced by this Plan task.

| Card premise | What this source actually does | Design consequence |
|---|---|---|
| A 64-branch card stalls dispatch for 13–18 minutes. | `AgentTaskDispatcher.TickAsync` awaits `EvaluateCardSiblingBaseAsync` before claim. It enumerates Succeeded and Blocked Worktree siblings, performs per-branch Git, then compares every ordered pair of distinct warning tips. `C540_ProbeCensus` explicitly asserts the quadratic comparison count. | Bound both the linear inspection and warning reduction; changing only `cherry` is insufficient. Replace the old performance assertion deliberately. |
| A five-second inspection setting should stop this. | `AgentTaskWorktreeBaseResolver.Inspection` has a monotonic 5-second budget, 16-candidate cap and 128-command cap. The earlier dispatcher guard uses `DelegationWorktreeService` and `LandingGit.DefaultBudget` (five minutes per command), outside that budget. | Share bounded primitives, not the resolver's source-selection policy. The new guard must never fall back to the unbudgeted helper path. |
| Skip ancestors cheaply. | The resolver already batches `for-each-ref` tips and `--merged`, but applies its candidate cap before those calls. The guard probes individual branches, containment and descriptions repeatedly. | Bound raw inventory separately and eliminate proven target ancestors before the expensive-candidate cap. Do this in both readers so the second reader does not reject 64 already-contained branches. |
| Skip documentation-only branches. | No trusted cheap ref flag proves this. Plan/TestDesign work can be required by the next stage. D-2 explicitly excludes instructions, policy, test selections, schemas and executable examples from narrative-only treatment. | Do not silently discard unlanded Markdown or Docs/Plan-role branches. D-3 records the safe narrowing of this optimization. |
| The mirror or its Hangfire job is stuck. | `RemoteWorkspacePreparer` owns off-tick work in its own service scope. It saves a path/binding and returns; launch is on a later tick. The investigation measured 14–26 seconds from hold to binding for the three examples. Preparation is not a Hangfire job. | Fix the saved outcome's presentation and the dispatch blockage, not runner transport or job retries. |
| The stale mirror hold lasted 28 minutes. | The investigation's explicit timestamps for task `046b1e67` show binding at 10:18:37Z and dispatch at 10:52:11Z, about 34 minutes. Other elapsed figures describe different observation points. | Use the timestamped interval when describing the incident; acceptance uses ordering/barriers, not that historical duration. |
| Saved binding implies the current task is ready. | `CaptureMirrorBindingAsync` intentionally retains late binding evidence even if the attempt/runner no longer matches. Its `true` result is binding capture, not proof that the current queued row was updated. Legacy path capture also permits a canceled attempt's resource record. | Readiness requires current Queued status, attempt, runner and nonempty matching remote path; never infer it from capture success alone. |
| `inspection_timeout` is not logged. | `BlockAsync` stages Status, FailureReason and a Blocked event, then saves. It has no log. Some callers are inside an outer claim transaction. | Log the stable reason after the relevant commit, not merely after `SaveChangesAsync` inside an uncommitted transaction. |
| Inspection stalls have no attention. | A committed inspection block already enters `BuildBlockedAsync`, but looks like a human question with Reply/Escalate. Queued attention requires a Held event and uses 300/900-second warning/error defaults. The synchronous walk writes no hold before it starts. | Add an early inspection hold; improve existing rows and actions instead of adding a new alert sink or board state. |

Source anchors: `server/Application/Services/AgentTaskDispatcher.cs`
(`TickAsync`, `EvaluateCardSiblingBaseAsync`, `DispatchOneAsync`, `BlockAsync`),
`AgentTaskWorktreeBaseResolver.cs`, `DelegationWorktreeService.cs`,
`RemoteWorkspacePreparer.cs`, `RemoteWorkspaceService.cs`, `DispatchHoldDetails.cs`,
`AgentTaskPipelineStatusService.cs`, `AttentionService.cs`,
`server/Infrastructure/Git/LandingGit.cs`, and
`server/Application/Settings/GitSettings.cs`.

Read the relevant owner sections before implementation: `docs/project-context.md`,
`docs/orchestration-loop.md` (same-card base contract and D-2),
`docs/testing-and-build.md` (checkpoint manifest, slot gate and asynchronous delivery),
`docs/agent-card-lifecycle.md`, and `docs/session-runtime-invariants.md` (CARD-1083).

## Decisions

**D-1 — Two bounded readers, one set of inspection primitives.** Extract the
resolver's monotonic budget/command gate and frozen-ref inventory into concrete
application services (`WorktreeBaseInspection.cs` and `CardSiblingInventory.cs`,
proposed names). The legacy guard and authoritative resolver retain their different
eligibility rules: Blocked siblings may warn/hold, but are not automatic sources.
Each operation gets one total deadline covering its DB inventory, Git gate waits,
ref probes, containment, descriptions and reduction. Reuse the existing configured
5 seconds, 16 expensive candidates and 128 Git commands; do not reset the deadline
per phase or per sibling. Caller shutdown cancellation propagates; local deadline
exhaustion returns typed incomplete evidence. Reject raising LandingGit's timeout,
timeouts around only one command, and catch-and-proceed on unknown evidence.

**D-2 — Cheap bounded inventory before expensive work.** Add
`Git:WorktreeBaseMaxInventoryRows = 256`, positive and at least
`WorktreeBaseMaxCandidates`, validated alongside the existing Git settings in
`server/Program.cs`. Read at most cap+1 deterministic rows to detect truncation,
with a separate bounded pending-land existence/representative query over the full
eligible set. Inventory overflow is `candidate_limit`, never absence. Keep separate
counts for raw rows, distinct branch refs, target-contained branches, expensive
candidates, inspected candidates, omitted candidates and Git commands.

Resolve the actual target once, batch exact local branch tips and merged refs,
validate returned ref names/object IDs, and keep all subsequent observations bound
to full IDs. A branch may move between the two batch calls: use the merged result
only if its returned object ID agrees with the first frozen observation; otherwise
retain it for bounded inspection. Missing/invalid output is unknown, never proof of
containment. Exact-match the requested ref set because `for-each-ref` patterns can
also match longer names. Skip genuinely absent local branches. Deduplicate repeated
refs/tips while retaining every task/request identity needed for a hold. Only then
apply the expensive-candidate cap. No fetch, repository mutation lease, or network
operation is needed for this read-only inventory.

**D-3 — Documentation is not proof of irrelevance.** Skip already-contained docs
using the same ancestry evidence as any other branch. Keep unlanded documentation
eligible for source selection and pending-land checks. Omit optional subject/count
enrichment when the remaining budget is small; a warning can name the frozen SHA
and say detail is unavailable. Reject blanket `*.md`, `docs/**`, role-based or
commit-subject filtering. It would discard this very plan or an executable test
selection. The card's docs-only fast path is therefore narrowed to proven contained
branches and optional display enrichment; no new semantic document classifier is
part of this fix. This is a safety decision supported by existing policy, not an
unanswered request for permission.

**D-4 — Preserve safety before warning cosmetics.** A current pending integration
outranks candidate/command/time limits, including explicit Target/FreshWorktree and
BaseTask modes. Preserve the existing explicit StartRef exception when bounded
evidence proves it contains the reviewed source. Unknown evidence leaves that hold
in place. Preserve NeedsResolution as a warning, not a pending integration hold.
Preserve merge-range/patch-equivalence rules, dirty-checkout and same-repository
validation, adopted-worktree precedence, repair and SourceLanding handling.

Warning collapse is optional. Stop pairwise ancestry work when its shared budget
would expire; emit individual bounded warnings plus an explicit omitted-count
summary instead of dropping warnings or guessing edges. Check every pending request
before collapse. A complete containment/source-selection verdict and incomplete
warning enrichment are different outcomes. The old `C540_ProbeCensus(20)` requirement
for 380 ordered comparisons is intentionally replaced with a bounded census; the
small identical/chain/fork semantics remain regression obligations.

**D-5 — Move the legacy guard off the tick; keep one launch owner.** Use an owned,
bounded in-process coordinator, following the scoped-worker pattern of
`RemoteWorkspacePreparer`/`AgentTaskCheckHostedService`. Proposed names:
`CardSiblingInspectionQueue` and `CardSiblingInspectionHostedService`. Use
nonblocking enqueue, at most two active inspections and 32 total queued/running/
completed entries per server process. These are local work limits, not fleet
placement settings. Git still takes the registered `GitProcessGate`. No singleton
captures a scoped DbContext or tracked task.

The tick persists a stable `Held: same-card base inspection is pending` detail,
enqueues the task identity and continues immediately. A full coordinator leaves the
task queued with a stable capacity detail; it never awaits capacity. The worker
loads its own scope and returns an immutable bounded observation. It does not
create a worktree, launch a session, change the task status or send a parent note.
The normal dispatcher alone consumes the result and applies admission/claim rules.

Key results by task ID, Attempt and the base-intent fingerprint (card, canonical
repository, runner, requested mode/ref/task, merge target and effective configured fallback).
Capture sibling task/request metadata used by the guard. Before applying a result,
re-read that metadata under the normal claim flow; a new/changed pending request or
changed intent invalidates it and schedules a fresh inspection. Never reuse a
result across cancel/retry, runner reassignment affecting task intent, or adoption
of an already-created worktree. Advisory warnings remain observations of frozen
tips, as the existing warning contract requires; they confer no source authority.

Under the repository lease, re-resolve the target/requested ref and any pending-land
ref whose containment allowed Proceed in one bounded identity-fence phase. Compare
with the result's frozen IDs; movement, missing refs or unknown evidence discards
that authority and requeues inspection. Do not repeat the history walk in this
phase. Ordinary warning-only branch tips deliberately remain historical observations.
This fence is necessary for explicit StartRef/FreshWorktree paths too, since they
can bypass automatic source selection. Include its time/commands in the claim's
shared inspection budget; a timeout cannot become permission to launch.

The authoritative `AgentTaskWorktreeBaseResolver.ResolveAsync` still rechecks source
eligibility under the repository lease immediately before creation, using its
bounded inspection. It is not replaced with cached background source-selection
authority. This plan removes the unbounded legacy walk from the tick; it does not
promise a hard deadline for the whole tick, which also owns bounded source checks,
worktree creation and unrelated sweeps. Moving those owners is outside CARD-1349.

Take/remove completed results when consumed. Evict superseded/nonqueued identities
and expire unused results after 30 seconds; ready tasks then re-arm. Do not let
early held tasks monopolize the queue: preserve queue order and offer unattempted
tasks a turn before re-enqueuing a consumed hold. A result that still holds is
rechecked on a later tick. Restart loses observations, not durable authority: queued
tasks re-arm from their Held state. Shutdown cancels and awaits owned workers; no
unobserved fire-and-forget operation or silent swallowed exception. A local
inspection error becomes typed incomplete evidence; stopping writes no false block.

Reject parallelizing `TickAsync`, running `DispatchOneAsync` from the worker, or
letting mirror completion launch directly. Each would duplicate the admission,
capacity, lease and transactional-claim owner.

**D-6 — Refresh readiness at capture, with a durable read fallback.** After a
binding or legacy path is recorded for the current queued attempt, replace the
current in-flight Held reason with the stable sentence
`Held: prepared workspace is ready; awaiting dispatch admission.` Use the existing
Held event stream; do not delete historical events. Serialize the task update and
conditional event insertion with the task row, recheck runner/attempt/path/status,
and insert at most once for that ready transition. A newer capacity/lease/land hold
wins; readiness must not overwrite it.

Binding retention remains valid for late/canceled/reassigned attempts. Preserve
that evidence even if the current task cannot become ready. Put the transition in
a shared readiness helper used by binding capture/recovery and legacy path success,
not only the happy-path log in `RemoteWorkspacePreparer`. Respect an existing
transaction rather than nesting transactions. Where binding retention commits
before task readiness, recovery and the read fallback below must cover that gap.
Guard stale dispatcher writes too: a cached in-flight hold must not be appended
after current readiness was committed.

Publish `AgentTaskChanged` through `IEventBus` after commit. Publication failure must
not be caught as a mirror failure or increment RemotePrepFailures. The pipeline
projection must derive awaiting-dispatch from a current recorded remote path when
its only stored reason is stale in-flight remote preparation, even after a crash
between capture and event insertion. It must preserve stronger live admission
reasons. Apply the same current-state interpretation to attention so an old prep
hold cannot continue claiming the mirror is running. No new queueReason wire value
is needed: use the existing awaiting-dispatch value. The next ordinary tick launches
through all existing gates; event notification is an invalidation hint, not launch
or delivery evidence.

**D-7 — Log committed blocks and reuse attention.** Emit one structured Information
record for a committed dispatcher Blocked transition with task/card IDs, attempt,
stable reason code and bounded diagnostic counts when available. Do not log goals,
raw Git output, commit subjects or unrestricted FailureReason text. Audit every
`BlockAsync` call: callers inside the claim transaction emit after its commit;
standalone callers emit after their save. Rollback and repeated reads emit no
successful-block record. Log the sibling inspection start/finish at Debug with
operation identity, counts, elapsed time and a fixed command-kind label; log a
budget/inventory refusal at Warning once per operation, not once per sibling.

Queued inspection holds feed existing `AttentionKind.DispatchHeld` and its existing
300/900-second defaults and stable task condition key. Committed prelaunch
`inspection_timeout`, `candidate_limit`, `git_command_limit` and
`git_inspection_error` blocks retain the existing attention kind but get a specific
base-inspection headline, reason/count evidence and OpenDrawer/Cancel actions.
Recovery text offers retry after resolving the cause, or cancel/recreate with an
explicit source/fresh target when appropriate; it does not offer Reply to a nonexistent
session or suggest that a larger model fixes Git. Use a strict reason prefix and
prelaunch state, so an ordinary human question containing these words is unchanged.
No enum renumbering, new alert sink, external notification or automatic card move.

**D-8 — Portable verification, separate TestDesign.** The Plan brief does not fold
TestDesign. It must append the required `## Verification design` structure and
finalize the proposed method rosters, guards, PCs and delivery inventory below.
There is no outstanding product-choice question; next is `test-design`, not Code.
Read `GET /api/runner-defaults` and `GET /api/session-runners` again before each
dispatch. At Plan inspection, revision 2 preferred an eligible Linux runner; an
eligible Windows runner also existed. This observation is not a host pin. All
proposed checkpoints use the portable .NET/PostgreSQL/fake-Git lane on the current
eligible default runner; omit `-Runner` and `-Platform`. Do not embed a fleet address.

## Independently landable slices

Each slice is a separate ordinary Code/Review/land unit if commissioned separately.
S1 is a safe bounded mitigation; S2 is required to finish removal of the legacy walk
from the serial tick. S3 can proceed independently. S4 builds on the inspection
outcomes and early holds. Do not close CARD-1349 after S1 alone.

| Slice | Files and concrete change | Named tests and completion condition |
|---|---|---|
| S1 — Bound and batch both sibling readers | Add `server/Application/Services/WorktreeBaseInspection.cs`, `CardSiblingInventory.cs`, and `CardSiblingInspectionService.cs` for the extracted legacy guard. Change `AgentTaskWorktreeBaseResolver.cs`, `AgentTaskDispatcher.cs`, `GitSettings.cs`, and validation/DI in `server/Program.cs`. Keep `DelegationWorktreeService.cs` helpers for other callers; remove this guard's unbudgeted calls. | New `tests/Antiphon.Tests/Application/DispatchSiblingInspectionTests.cs`; update the census and command-shape assertions in `AgentTaskDispatchBaseGuardTests.SiblingWarnings.cs`. Preserve state/outcome assertions. Bounded fake-Git behavior, ancestor batching, docs/pending-land safety and real Git graph regression selections pass. |
| S2 — Schedule legacy guard outside tick | Add `server/Application/Services/CardSiblingInspectionQueue.cs` and `server/Infrastructure/Orchestration/CardSiblingInspectionHostedService.cs`; integrate enqueue/consume/metadata recheck in `AgentTaskDispatcher.cs`; stable holds in `DispatchHoldDetails.cs`; production DI in `server/Program.cs` and fixture DI/lifecycle in `tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs`. Update affected guard fixture drivers to await owned inspection then run the consumption tick explicitly. | New `DispatcherSiblingInspectionSchedulingTests.cs`, using the isolated DB and real queue seams from `DispatcherRemotePrepStarvationTests.cs` and `BridgeQueueHarness`. Eligible later work reaches its recipient while fake sibling Git is still blocked; no duplicate launch, stale result, stranded worker or permission bypass. |
| S3 — Mirror readiness is immediately truthful | Add shared `RemoteWorkspaceReadinessService.cs` if needed; change `RemoteWorkspaceService.cs`, `RemoteWorkspacePreparer.cs`, `DispatchHoldDetails.cs`, `AgentTaskDispatcher.cs` hold writer, `AgentTaskPipelineStatusService.cs`, and stale-prep interpretation in `AttentionService.cs`. Register the helper in production and test graph. | New `RemoteWorkspaceReadinessTests.cs`, based on `RemoteWorkspacePreparerTests.Rig`/phone-home fixtures. Binding and legacy paths refresh before another tick; late results retain bindings without false readiness; failure/event/crash races are covered. Existing prepared-mirror admission/receipt tests remain selected. |
| S4 — Committed block log and useful attention | Change `AgentTaskDispatcher.cs` post-commit logging paths, `AttentionService.cs` prelaunch reason presentation and early inspection hold interpretation; document final behavior/configuration in `docs/orchestration-loop.md` and `docs/ops-http.md`. | New `DispatchBaseInspectionDiagnosticsTests.cs` based on `DispatchHeldAttentionTests` and base-guard fixtures. Commit/rollback log witnesses and attention lifecycle/actions pass. No client contract or new enum is necessary. |

New test names below are planned, not existing coverage. Production/test helpers
must remain concrete services or actual I/O seams. Do not introduce a mock-only
dispatcher implementation to make the performance witness pass.

## Proposed verification scope for TestDesign

### Red-first witnesses and narrow green selections

Use `ILandingGit` fakes that record argv, honor linked cancellation and can block at
named phases. Use `FakeTimeProvider` for deadlines and barriers for scheduling;
never wait minutes or assert a fragile wall-clock speedup. For a newly extracted
seam, begin with a compiling test against the existing dispatcher/resolver entry
point, commit/push its failing assertion, then extract/fix. A missing type or fixture
error is not the red witness. New safety guards additionally need method-scoped PCs
in TestDesign; red-first development runs do not replace post-land Mutation.

| IDs / behavior | Planned exact method (class below) | Red witness before fix; decisive green outcome |
|---|---|---|
| V-1 / R-1, bounded work | `DispatchSiblingInspectionTests.C1349_InspectionLimits` (4 arguments: deadline, command cap, candidate cap, raw inventory cap) | Old guard starts extra fake calls/never observes the five-second token. Green records the typed limit, bounded counts, no calls after cancellation, and no false complete verdict. Deadline arm covers gate wait and description/reduction phases within the same operation. |
| V-2 / R-2, cheap contained history | `DispatchSiblingInspectionTests.C1349_ContainedInventory` (3: 64 ancestors, duplicate refs/tips, moved ref between batch reads) | Old guard makes per-branch probes; resolver caps before excluding ancestors. Green avoids per-ancestor cherry/description and accepts a complete all-contained inventory; moved/invalid evidence stays inspectable/unknown. Assert total command ceilings and selected base. |
| V-3 / R-3, docs are meaningful | `DispatchSiblingInspectionTests.C1349_DocumentationSourceIsPreserved` (2: unlanded plan, pending plan) | Start with the latency assertion showing the unbounded legacy probe on a plan branch. Green still selects the eligible plan SHA, or holds pending land, while respecting the deadline. A blanket docs skip must fail the source/hold assertion. |
| V-4 / R-4, warning reduction | `DispatchSiblingInspectionTests.C1349_ReductionRemainsBounded` (2: identical/chain set, 64 divergent rows) | Old pairwise comparison exceeds the declared budget. Green preserves proven representative semantics for the small set and explicit incomplete/omitted evidence for the capped set. No vanished pending request. |
| V-5 / R-5, pending precedence | Existing `AgentTaskDispatchBaseGuardTests.T0442_V20_pending_land_precedes_source_selection_in_every_mode` (6 arguments) and `T0442_V30_incomplete_dispatch_inspection_blocks_previewed_source_and_keeps_land_hold` (5) | Existing regression guards, not newly claimed reds. Hold still wins; Previewed/Review/task-source refusal still blocks before worktree/session creation. Retain CARD-0788's fresh-Code safe-target behavior. |
| V-6 / R-6, serial tick fairness | `DispatcherSiblingInspectionSchedulingTests.C1349_LaterTaskReceivesBriefBeforeInspectionCompletes` (4: eligible, busy recipient, queue inserted, lost wakeup) | Keep fake Git blocked without advancing time; old tick cannot deliver later work. Green observes the later task's complete matching UserPrompt via the real queue before releasing Git. Busy arm queues then delivers once after idle. |
| V-7 / R-7, observation custody | `DispatcherSiblingInspectionSchedulingTests.C1349_StaleInspectionCannotDispatch` (5: retry, cancel, intent change, new pending land, worktree adopted) | New async boundary needs TestDesign PC witnesses in addition to tests-first baseline. Green rejects stale identities, retains pending hold, and never creates a second worktree/session. A compiling removal of each identity guard must fail its corresponding assertion. |
| V-8 / R-8, worker lifecycle | `DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers` (4: repeated ticks, saturated queue, restart, shutdown) | Old tick waits on fake Git. Green deduplicates, enforces 2 active/32 total, returns without waiting for a queue slot, gives later tasks a turn, re-arms after restart and joins canceled workers. |
| V-9 / R-9, ready before next tick | `RemoteWorkspaceReadinessTests.C1349_CaptureRefreshesReadyHold` (2: binding, legacy path) | Save success and do not run another tick: old latest hold/pipeline still says preparation. Green reads durable ready hold and awaiting-dispatch projection, zero session starts and unchanged gate admission. |
| V-10 / R-10, late result/race safety | `RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold` (5: canceled, reassigned, retried, newer capacity hold, stale dispatcher writer) | Current implementation leaves the old prep text; new behavior's identity fences need separate PCs. Green preserves binding evidence but creates no false ready state/event and never overwrites the stronger hold. |
| V-11 / R-11, recovery after capture | `RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification` (3: crash after binding, failure before ready event commit, event publication throws) | Old projection trusts stale Held text. Green GET derives current readiness; recovery inserts at most one ready event; notification failure never adds a remote-prep failure/backoff. |
| V-12 / R-12, committed diagnostics | `DispatchBaseInspectionDiagnosticsTests.C1349_BlockedLogMatchesCommittedReason` (3: inspection_timeout, candidate_limit, outer transaction rollback) | Old BlockAsync emits no committed-reason log. Green emits one structured code/count record on commit and none on rollback; no raw stderr/goal leaks. |
| V-13 / R-13, attention lifecycle | `DispatchBaseInspectionDiagnosticsTests.C1349_InspectionAttentionExplainsRecovery` (4: pending younger/older threshold, blocked without session, retried/terminal) | Old walk has no early Held event; blocked item says human answer. Green shows the reason/counts at the right time with stable identity and useful actions, then removes it after retry/terminal transition. Ordinary question behavior remains unchanged. |

Fixtures already inspected: `AgentTaskDispatchBaseGuardTests.cs` and its
`SiblingWarnings` partial (real temporary repositories and lease hooks),
`DispatcherRemotePrepStarvationTests.cs` (real queue and transcript assertions),
`RemoteWorkspacePreparerTests.cs` (phone-home, fake clock, capture races),
`DispatchHeldAttentionTests.cs` (isolated DB and age thresholds),
`DelegationTestServices.cs`, and the resolver's private `Inspection` seam.
TestDesign must read remaining helper bodies it binds, especially
`QueuedReceiptAssertions`, readiness recovery callers and log-capture fixtures.

### Delivery and recovery obligations to expand

* Inspection handoff: producer is the queued-task tick; durable identity is task +
  attempt + intent; persisted state remains Queued plus Held. The bounded memory
  queue carries advisory work. Restart re-arms it. Receipt is dispatcher consumption
  under fresh metadata/claim, followed by the ordinary launch queue when admitted.
  Persisted Held text alone proves neither completed inspection nor delivery.
* Existing sibling warnings: continue using `DispatchBaseWarningIntentService` and
  `DispatchBaseNotificationPayload` inside the successful dispatch transaction.
  Keep rollback/claim-loser behavior and frozen SHA evidence. TestDesign must select
  producer-to-parent-recipient tests from `DispatchBaseNotificationTests` including
  busy/eligible recipients and recovery cuts. A warning row is not a UserPrompt.
* Mirror readiness: producer is current-attempt capture (binding, recovered binding
  or legacy path); destination is durable task/event projection and UI invalidation.
  GET is the durable receipt if invalidation is lost. Its later session launch uses
  the existing real queue and complete UserPrompt receipt. The readiness hint is
  never proof of launch, and must not create a new direct-input path.

No session is created to wait for inspection or a base choice. These are prelaunch
Queued/Blocked states, with no seat to release. Existing sessions waiting for input
retain their ownership: **parking disabled means no automatic release deadline**
(CARD-1083). Inspection timeout cancels only this operation's owned Git work; it is
not session-stop authority. No Working session is stopped by this design.

### Checkpoints

Proposed closed ordinary list; TestDesign must append any missing exact warning
delivery/regression selections and its PC map before Code, and recompute the cost.
Lane for **every row**: portable .NET/PostgreSQL with fake Git or owned temporary
Git repos, on the current eligible default runner. No native desktop dependency,
production runner, live agent credential or full suite is required. Rows marked S2
run after S1 as well; S4 includes the preceding slices if commissioned together.
`Min` counts parameterized TUnit results, not internal assertions.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1349-s1/` | inspection-limits | `/*/*/DispatchSiblingInspectionTests/C1349_InspectionLimits` | V-1, R-1 | all 4 arguments, 0 failed/skipped | 4 | 5 |
| CP-2 | S1 | CP-1 | contained-inventory | `/*/*/DispatchSiblingInspectionTests/C1349_ContainedInventory` | V-2, R-2 | all 3 arguments, 0 failed/skipped | 3 | 1 |
| CP-3 | S1 | CP-1 | documentation-source | `/*/*/DispatchSiblingInspectionTests/C1349_DocumentationSourceIsPreserved` | V-3, R-3 | both arguments, 0 failed/skipped | 2 | 1 |
| CP-4 | S1 | CP-1 | bounded-reduction | `/*/*/DispatchSiblingInspectionTests/C1349_ReductionRemainsBounded` | V-4, R-4 | both arguments, 0 failed/skipped | 2 | 1 |
| CP-5 | S1 | CP-1 | pending-land-modes | `/*/*/AgentTaskDispatchBaseGuardTests/T0442_V20_pending_land_precedes_source_selection_in_every_mode` | V-5, R-5 | all 6 arguments, 0 failed/skipped | 6 | 2 |
| CP-6 | S1 | CP-1 | incomplete-selection | `/*/*/AgentTaskDispatchBaseGuardTests/T0442_V30_incomplete_dispatch_inspection_blocks_previewed_source_and_keeps_land_hold` | V-5, R-5 | all 5 arguments, 0 failed/skipped | 5 | 2 |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-c1349-s2/` | inspection-fairness | `/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_LaterTaskReceivesBriefBeforeInspectionCompletes` | V-6, R-6 | all 4 arguments, 0 failed/skipped | 4 | 6 |
| CP-8 | S2 | CP-7 | stale-inspection | `/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_StaleInspectionCannotDispatch` | V-7, R-7 | all 5 arguments, 0 failed/skipped | 5 | 1 |
| CP-9 | S2 | CP-7 | inspection-lifecycle | `/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_InspectionQueueRecovers` | V-8, R-8 | all 4 arguments, 0 failed/skipped | 4 | 1 |
| CP-10 | S3 | `tests/Antiphon.Tests -> bin-c1349-s3/` | mirror-ready | `/*/*/RemoteWorkspaceReadinessTests/C1349_CaptureRefreshesReadyHold` | V-9, R-9 | both arguments, 0 failed/skipped | 2 | 5 |
| CP-11 | S3 | CP-10 | mirror-ready-races | `/*/*/RemoteWorkspaceReadinessTests/C1349_LateCaptureCannotReplaceCurrentHold` | V-10, R-10 | all 5 arguments, 0 failed/skipped | 5 | 1 |
| CP-12 | S3 | CP-10 | mirror-ready-recovery | `/*/*/RemoteWorkspaceReadinessTests/C1349_ReadyProjectionSurvivesLostNotification` | V-11, R-11 | all 3 arguments, 0 failed/skipped | 3 | 1 |
| CP-13 | S4 | `tests/Antiphon.Tests -> bin-c1349-s4/` | block-log | `/*/*/DispatchBaseInspectionDiagnosticsTests/C1349_BlockedLogMatchesCommittedReason` | V-12, R-12 | all 3 arguments, 0 failed/skipped | 3 | 5 |
| CP-14 | S4 | CP-13 | inspection-attention | `/*/*/DispatchBaseInspectionDiagnosticsTests/C1349_InspectionAttentionExplainsRecovery` | V-13, R-13 | all 4 arguments, 0 failed/skipped | 4 | 1 |

Code runs the finalized table through `tools/Antiphon.Checkpoints`, one committed
slice group per run, with `--plan`, `--after` and `--expected-source-sha`. If the tool
needs bootstrapping, build it once through `scripts/build-slot.ps1` into an owned
alternate output, then invoke that built tool's `run`/`wait` verbs without an outer
slot lease; checkpoint rows acquire their own slots. The bootstrap build is an
explicit infrastructure exception to the product-row list and must be reported
with its SHA and cost. Do not hold an outer build slot while the executor waits for
row slots. Use the documented run/wait lifecycle until the result is not 75.
A slot timeout is not-run/blocked, never an unleased
retry. Commit/push before running; keep source frozen during each run and retain
the unedited CHECKPOINT lines, tested SHA, counts and validated receipts.

The red-first diagnostic executions use the exact witness filters above at the
tests-only commits and are reported separately from green qualification. No broad
suite, category lane or repeated clean run is authorized by this plan. Existing
regression tests can be green at baseline; do not manufacture a red by weakening
their assertions. New guard PCs belong to the post-land SourceLanding task, never
to this Plan task. Generated TRX/JSON/logs stay ignored. Clean only owned alternate
outputs using the testing guide's custody rules.

### Cost and handoff acceptance

Estimated, not measured: proposed ordinary floor **33 minutes** (sum of CP rows;
52 TUnit results). Four isolated builds are included. TestDesign should budget
another 5–10 minutes for exact existing warning-delivery and special-mode regression
rows it adds, then publish the final numeric ordinary floor and a separate per-PC
red/restore/green floor. No PC execution estimate is claimed before that inventory.

Authoring estimate: S1 60–90 minutes, S2 90–120, S3 45–60, S4 30–45; total
225–315 minutes plus ordinary verification, approximately 30–45 minutes of tests-first
red setup/execution, and observed build-slot wait. TestDesign itself: 30–45 minutes.
These are dispatch planning estimates, not permission to run unlisted tests.
The optimization removes up to quadratic warning comparisons, caps expensive
inspection at the existing five-second envelope, and permits other eligible work
to proceed while a sibling inspection is still blocked. It does not claim a new
fleet throughput benchmark or a hard full-tick latency bound.

TestDesign must finish the complete Inspection/Delivery/V/R/Guard/PC/Out-of-scope/
Checkpoints/Cost structure; give a distinct compiling positive control for each
independently bypassable budget, identity, pending-land, publication-order and
readiness guard; bind exact real-Git regressions for explicit reviewed-source
containment, missing refs, patch-equivalent/merge ranges, current-source preview
changes, fresh-Code fallback and adopted/repair/SourceLanding paths. In particular,
reconcile the old one-tick fixture assumptions and obsolete quadratic census without
dropping source/notification assertions. Include complete real-queue recipient
evidence for changed async paths. If that requires changing a design decision,
return `next: plan` with the concrete unverifiable boundary.

Acceptance after Code/Review: every finalized ordinary row has nonzero expected
counts and validated clean-source provenance; no missed pending integration or
silently discarded unlanded plan; a later recipient progresses before the sibling
Git barrier releases; successful mirror capture reads ready without another tick;
committed inspection blocks are logged and presented accurately. Land each reviewed
implementation through the normal master-only operation. Activation is separately
commissioned from the canonical checkout; a master land is not a restart. Record
the post-land Mutation companion and do not call pending PCs clean.
