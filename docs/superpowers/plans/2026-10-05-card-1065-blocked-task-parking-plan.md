# CARD-1065: publish work before parking Blocked tasks and release their seats

Date: 2026-10-05. Plan task: `a650f4b4-eee7-4224-9735-4b0d16c768f9`.
Inspected source: `0de12dac930ad52a235604c566ee643e71b21daa`.
Assigned branch: `feat/card-task-a650f4b4`; fast-forward-only from that source.
Status: Plan complete; **next: TestDesign**. Test design was not folded into this dispatch.
The checkpoint and PC inventory below is a concrete proposal for that stage to qualify,
not a claim that the proposed tests exist or that implementation is ready to run.

## Outcome and scope

A successfully parked ordinary delegated task remains Blocked and answerable, retains
its report, transcript, branch, workspace and agent identity, and owns no live runner
process. Its complete source state is recoverable from an exact pushed commit before
release begins. A reply creates one admitted continuation on that source. Physical
capacity is freed only after the runner confirms exit/removal of the expected generation.

A park that cannot prove publication or idleness remains visibly pending/refused. It
must not masquerade as a released seat. This is the card's explicit refusal exception,
not permission to discard dirty code or stop a Working session to meet a capacity target.
CARD-0079 remains the sole special automatic stop of a Working session.

This plan extends the conditional release and continuation work in
[CARD-0667](2026-10-01-card-0667-terminal-task-seat-release-plan.md). It does not build a
second kill protocol. It adds the missing source-publication requirement, a durable
park episode, agent retention, sync-debt separation, and migration of existing Blocked
tasks through the same guarded path. Related evidence repair remains owned by
[CARD-1043](2026-10-05-card-1043-unbound-review-evidence-plan.md).

No production sessions, cards, runner budgets, deployment settings or source files
were changed during this Plan. Historical seat counts and task examples come from the
card; they were not reproduced by stopping anything.

## Ground truth

| Card assumption / requested behavior | Code at the inspected source | Design consequence |
|---|---|---|
| Only Succeeded releases a delegate. | `AgentTaskReplyService.cs:1106` includes Succeeded and Failed, plus no-progress recovery. `ReleaseDelegateAsync` can warm a Shared pool agent or stop/delete a Worktree pool agent. | Preserve CARD-0691's Failed behavior; adding Blocked to this Boolean is unsafe and insufficient. |
| Blocked is a terminal status. | `AgentTaskService.IsSettled` at line 3651 excludes Blocked. Cards, workspace ownership and pool sweeps regard Blocked as open. | Keep global task/card semantics. Park state and process custody are separate from task verdict. |
| Orphan classification should already free the seats. | `RunnerSlotService.OccupiesCapacity` counts Starting/Running/Stopping runner processes. `LoadDesktopAsync:414` recognizes only Dispatched/Working task ownership. A Blocked process can be `orphan=true`; the existing reconcile job generally repairs audit receipts, not automatic discovery/release. | Correct logical ownership separately. An orphan flag is never release authority or a reason to subtract capacity. |
| CARD-0667 does not cover Blocked. | Its current committed plan explicitly covers report-completed Blocked, conditional release, retained workspaces and cold answer continuation. This checkout has S1a/S1b fresh transcript observation/qualification, but `SessionRunnerRuntime:964` says the internal boundary is read-only with no release caller. No `RunnerSeatRelease` coordinator/entity exists yet. | The bug remains real, but the neighboring plan already owns substantial implementation. Consume its completed protocol/coordinator; do not treat a planned API as shipped or duplicate it. |
| Every Blocked transition follows a final report. | `SettleAsync` records report-completed Blocked; bind-refusal recovery, `BlockUnmarkedWaitingAsync`, quota blocking, routing/create holds, wall reroutes, dispatch holds and merge/land conflicts are distinct writers. Some have no session or no `CompletedAt`. | Discover state, classify the block cause and require current idle/publication evidence. Do not use `CompletedAt != null` as the only generic park eligibility rule. |
| A successful publisher proves all code was pushed. | `RunnerWorkspaceService.PublishAsync:275` observes `dirty`, can push committed HEAD while dirty, and returns without pushing when relation is equal. It neither commits dirty files nor supplies the required final exact-ref/clean receipt. | Introduce a stronger, capability-gated publication operation; leave legacy `workspacePublishV1` semantics intact. |
| Existing commit-on-settle can save any Blocked mirror. | `CommitOnSettleEligibility` admits Succeeded Shared tasks only; `GatedCommitService` is server-side and never pushes. Remote runner has no reference to that server service. | Require author WIP commit before a blocked report; refuse dirty parking. Reuse existing Git/publication guards, not an imagined remote autosave primitive. |
| `runner_sync_lease_busy` proves work was not pushed. | `RemoteWorkspaceService` couples exact-ref observation/fetch and desktop fast-forward to the common repository lease. It retries in 10-second slices within a 120-second default budget. Publication and safe mirror state can exist while desktop synchronization cannot obtain that lease. | Obtain independent runner-side clean/pushed evidence, release the idle seat, and retain durable desktop sync debt. Do not lengthen the global lease wait. |
| Reply restarts a stopped delegate. | `AnswerAsync:375-446` requires an old session ID, changes Blocked to Working, stamps a transcript sequence watermark, and enqueues to that session. It has no cold-start branch. | Use CARD-0667's durable accepted-answer/requeue path; a 200 plus an undeliverable old-session message is not success. |
| Continue is a general unblock. | `ContinueWithAuthorityAsync` admits only a question block with stored standing authority, then calls Answer. Cost, routing and merge blocks do not qualify. | Preserve these guards; only change the delivery destination for a parked eligible question. |
| The conditional standing resume API solves cold task continuation. | `AgentControlService:560` refuses selected-history resume while Dispatched/Working/Blocked execution assignments exist. Native provider resume is not a universal contract. `RequeueAsync:3086` retains Result/FailureReason, but normally stops the session, deletes ephemeral agents and resets attempt fields. | Add a receipt-bound continuation reason to task admission. Keep the original task and agent, launch a fresh session, and carry persisted context explicitly. Do not repurpose standing/compaction recovery. |
| A prerequisite landing automatically resumes a generic waiting task. | No general prerequisite relationship/wakeup exists on AgentTask. The concrete merge-helper path can resolve its conflicted parent to Succeeded; it does not resume arbitrary waiting work. | Existing orchestrator observes confirmed prerequisite publication and sends Reply/Continue. Preserve the merge-helper behavior; add no prose-parsing dependency scheduler. |
| `-OnAgent` should work once the process is gone. | CARD-0537 refuses `follow_up_agent_blocked`; a dead-session message currently recommends cancel/re-send. CARD-1037 separately refuses extant remote pool follow-ups with `follow_up_remote_pool_unsupported`. | Preserve reservation by the Blocked task even after parking. Reply that task; do not let a new follow-up steal the retained identity. Keep remote follow-up restrictions. |
| Pool and agent deletion are harmless after release. | Generic release deletes the pool Agent after confirmed exit, while pool janitors use PoolIdleSince. Standing/AlwaysOn/board/specialist owners are protected. | A parked identity must be retained but never advertised as a warm reusable process. Protect it from generic retirement/reuse while its task is open. |

The Blocked writer inventory to cover is `AgentTaskReplyService` (normal settlement,
bind refusal, quota, merge-back conflict, unmarked wait), `AgentTaskService`
(create/routing exhaustion, wall block, cost ceiling), `AgentTaskDispatcher`
(routing/kind blocking), and `AgentTaskLandService` (rebase conflict). A durable
state sweep covers writers without adding a stop call to each. CARD-0412 retained
capacity waits remain Working and are outside this feature.

Owners consulted: `docs/project-context.md`, `docs/ops-http.md`,
`docs/session-runtime-invariants.md`, `docs/agent-card-lifecycle.md`,
`docs/orchestration-loop.md`, `docs/testing-and-build.md`, and the two related plans.
The host lifetime/input sections of `docs/adr/0002-modern-conpty-backend.md` and
the resume source-of-truth sections of `docs/agent-kinds.md` were also checked;
`ProviderContractCatalog` still records Codex native resume as Unknown/unprobed.
These anchors describe the inspected SHA, not deployed binary identity.

## Decisions

### D-1. One durable park episode per blocked attempt

Add `AgentTaskPark` keyed by task ID, attempt and originating Blocked/settlement
event ID, with a uniqueness constraint. Capture Agent ID, historical session ID,
runner/store/accepted-start generation, task concurrency token, report reference
and digest, task branch/full ref, repository/endpoint identity, source SHA, verified
remote SHA, publication receipt identity, release ledger ID, reason and timestamps.
Keep bounded codes instead of raw Git stderr or credential-bearing remote URLs.

States: `Requested -> Published -> ReleasePending -> Parked`; `Requested` or
`Published` may be `Held` with a typed retry/refusal reason; an accepted answer
transitions through `ResumePending -> Resumed`. Keep desktop synchronization as an
independent `NotRequired/Pending/Ready/Held` fact. Persist intents before Git/wire
mutation and compare task/attempt/generation under the task mutation transaction
before each advance. Work across a process restart must resume the same identity.

The task becomes logically Blocked as today so the caller can answer immediately.
It is called **parked** only after confirmed release. Do not add a task/card status,
put Blocked in `IsSettled`, remove workspaces, clear report history, or manufacture
CompletedAt for routing/quota blocks. A Blocked row with no live generation incurs
no seat-release debt but can still have publication/sync debt.

Rejected: in-memory flags, log-only release debt, one mutable receipt shared across
attempts, and treating a task's status as proof that a process exited.

### D-2. Require clean source and an exact remote publication receipt

The author must commit all source changes, including untracked non-ignored code,
with a truthful WIP message before emitting `blocked`, then push the task branch.
Amend `DelegationReportFormatter.BuildBrief` for ordinary writable tasks to say this
explicitly. Respect `-NoCommit`, ReadOnly and SourceLanding exclusions in that text.
Generated ignored logs/TRX/receipts remain ignored; clean means clean source/index
and no untracked non-ignored files, not an empty build-output directory.

Parking does **not** auto-commit unknown changes. Dirty status, changed ignore
rules requiring the commit gate, sequencer/conflict state, unknown ownership,
unresolved commit-recovery or a no-commit instruction produce a visible hold.
The caller can Reply to arrange publication while the current session is still
live. This is the card's permitted “refuse or flag a park that cannot publish.”
Do not launch an automatic paid Commit child just to reclaim a seat.

For a clean ordinary task-owned Worktree, reuse ordinary non-forced push of the
owned branch when HEAD is committed but not yet published. Add a new
`workspaceParkV1` capability and typed prepare/verify contract beside existing
workspace operations. On the runner, reuse root/realpath/repository/branch,
sequencer and ancestry validation from `RunnerWorkspaceService`; do not change
the permissive legacy publisher in place. Use the owned Git-child execution and
repository locking discipline. Validate the captured endpoint identity and exact
`refs/heads/...`; run a fresh exact-ref observation after push even if HEAD was
already equal, or the branch was missing at the baseline. A push exit code alone
is insufficient. No fetch/reset of the canonical desktop is needed for this proof.

The resulting immutable receipt binds park/action ID, task/attempt, generation,
repository identity, endpoint fingerprint, full ref, baseline ancestry, clean
status and HEAD=remote SHA. A final fresh HEAD/status check must still match at
release; changes invalidate the receipt. Read/status/network failure is Unknown.
Push is fast-forward only; no force, rebase, reset, merge, target publication or
workspace cleanup. Endpoint authentication failure is a typed hold.

For local owned Worktrees, perform the same proof with existing server Git and
repository-lease seams. Shared work may park only when its complete checkout is
clean, its current HEAD is already verified on an explicitly authorized remote
ref, and no other task/writer owns that session/workspace. Do not automatically
push a shared target branch. An unpublished/dirty/shared-other-owner case stays
held, with migration to an owned Worktree as the recovery advice. ReadOnly with
no mutations records `NoSourceChanges` against its known base; discovered writes
hold instead of being silently omitted. SourceLanding keeps its own restoration
and custody protocol and is never committed/pushed by this path.

Rejected: `git add -A` on an uncertain checkout, report claims as publication proof,
`MirrorPushed=true` as clean proof, local tracking refs as remote proof, or silently
switching branches to make parking succeed.

### D-3. Reuse CARD-0667's idle-release protocol, with a mandatory publication gate

Complete/consume CARD-0667 through its dormant S4b coordinator, answer recovery and
wire path before this feature can mutate sessions. Its S1a/S1b observation APIs
alone are not authority to signal. Reuse fresh complete native transcript reads,
current prompt floor, two stable observations at least 120 seconds apart, input
ordering, runner/store/generation checks, last pre-signal reinspection, and
kill/exit/forget confirmation. Fresh Working or Unknown always vetoes, regardless
of age or task verdict. Pending/attempted/uncertain input and unknown child custody
also veto. No unconditional ReleaseSlot/KillGeneration fallback on an old runner.

Extend that coordinator's **Blocked** eligibility: a matching CARD-1065 publication
or valid no-source-change receipt is required before reserve/send. Extend its final
runner conditional command with the park publication binding; validate the same
clean HEAD and generation immediately before signal. Serialize input reservation,
publication and release so a Reply cannot race a Git snapshot; never hold a database
transaction across Git, network or the idle interval. A brief runner-side operation
reservation refuses competing input with a typed retriable result and invalidates
qualification if input already won. Persist accepted answers before retrying.

After successful release set the old session Stopped only for its exact generation;
keep the agent as a stopped identity, PoolIdleSince/PoolReservedForRootTaskId null.
The active Blocked park reservation prevents pool reuse, janitor deletion and other
task assignment. Clear/advance that reservation atomically on continuation or
explicit cancellation. A failed/lost stop retains the owner and occupancy until
fresh authoritative reconciliation confirms absence/exit. No archive/delete of
native conversation files is needed to release a seat.

Standing/AlwaysOn/board/specialist sessions retain independent ownership; parking a
task must not stop a standing worker or promise that its independently owned seat
is free. Surface `standing_owner` as the exception. Ordinary Shared pooled tasks
that qualify for parking cold-release rather than enter the warm pool. Never move
a blocked task into a TTL-kill path to evade these guards.

CARD-0667 deliberately leaves known local Blocked sessions on the old live path.
This extension admits an eligible local task only through the same fully fenced
protocol and publication checks. The local transport must support those capabilities;
the old unqualified local stopper is not a fallback.

Rejected: widening `shouldRelease`, counting warm pooling as freed capacity,
automatic operator-token orphan sweeps, and killing by silence.

### D-4. Cover every block cause without treating every hold as completed work

Use post-commit fast registration for report settlement and a bounded persisted
cursor sweep over current Blocked tasks for recovery/other writers. No provider
turn is generated by the sweep. Questions, prerequisite waits and unmarked waits
can qualify after a genuine current idle boundary and publication proof. A quota
block can qualify only if the same fresh idle evidence exists; retain quota holds
and explicit redispatch policy. Routing/create holds without sessions are already
seat-free. Land/merge conflict with a live Git sequencer or another writer remains
held until that owner resolves it. Missing final text is not invented.

Store the actual block event and retained transcript coordinates even for a
non-report block. Capture a durable checkpoint handoff from existing authorized
report/transcript storage before release. A disappeared server row is not proof
that an unknown runner seat belongs to this task: leave rowless discovery to
CARD-0667, and refuse automatic release when ambiguous task/workspace ownership
prevents proving whether unpushed work exists.

### D-5. Preserve task and agent identity; Reply queues one new admitted session

Use CARD-0667's accepted-answer journal and task-row serialization. Keep old episode
coordinates immutable. A reply before release reservation cancels the park intent
and uses the existing live path/watermark. During ambiguous release, persist the
answer and show ResumePending; do not send to the old session or launch twice.
After confirmed release, requeue the **same task** with the **same Agent ID** and
a fresh session UUID, increment attempt exactly once, rotate credentials, clear
old session sequence/nudge/check fields and retain Result/FailureReason/history.
The special continuation reason skips stopping/deleting the retained agent only
with the exact release receipt. Ordinary retry/cancel semantics stay separate.

Dispatch through normal quota, routing, host budget, pin, scope, workspace and
commit-recovery admission. Preserve explicit runner/platform/provider constraints;
no silent host/provider fallback. Revalidate the parked full ref and SHA. Reuse the
retained clean checkout only if it still matches; if the mirror is absent, recreate
through the owned workspace preparation path from that exact pushed SHA. Diverged,
advanced or dirty source is a visible refusal, never reset or substitution with
master. A caller may commission an explicit source recovery separately.

The new brief contains the full accepted answer once, original goal, stored report
excerpt, stable authorized full-report/transcript links and parked source SHA.
Large answers use existing input-file spill/API retrieval; do not truncate them
to the previous-report excerpt limit. Retain reports before stopping the runner.
Only a matching complete UserPrompt on the **new** session confirms delivery.
An old session's TurnEnd cannot settle the new attempt. A queue/enqueue crash is
recovered by the same input identity, not another attempt.

Continue still requires question classification plus standing authority and enters
this same path. For a prerequisite landing, the orchestrator sends that explicit
Reply/Continue only after its existing structured publication confirmation. There
is no autonomous dependency inference from report text or a card moving to Done.
The merge-helper parent-completion flow remains completion, not a paid continuation.

Retained blocked identities still refuse new `-OnAgent` work under CARD-0537. Update
dead-session guidance to Reply for a confirmed parked task rather than cancel it.
Do not remove CARD-1037's separate remote follow-up refusal or allow a fresh
follow-up to bypass the parked task's reservation.

Rejected: starting a process in Answer HTTP, guaranteed native conversation resume,
spawning a different task, clearing agent identity as ordinary retry does, or
automatically spending quota on a guessed prerequisite.

### D-6. Separate runner-sync debt from process lifetime

For a completed report blocked by desktop synchronization, first persist the report,
completion obligation and sync reason as today. If runner-side publication and
idle proof pass, release through D-3 even while the desktop repository lease is
busy. Store Pending sync debt keyed to park/attempt and the exact published SHA.
If the independent publication observation also fails, hold; do not guess that a
busy lease is the only problem.

Reconcile via a dedicated bounded sweep using existing workspace reservations and
sync Git guards, one try per due item/pass, persisted backoff of 1, 2, 4, then at
most 5 minutes. Do not sleep/retry in the task-settlement transaction or raise
RunnerSyncBudgetSeconds. A lease busy result stays pending; changed source,
endpoint, dirty desktop, active sequencer or non-descendant source becomes a
specific hold. Import only the parked immutable tip; never pick up a newer branch
tip without a new episode. Recreate service/process tests must prove debt survival.

Successful synchronization marks SourceReady and updates the existing task/attention
projection. It does **not** turn the stored report into new Clean evidence, claim a
fresh Review, replay all settlement side effects or synthesize Succeeded. The task
can remain historically Blocked/Decide without a seat. An explicit Reply obtains
a new successful report through D-5; CARD-1043 owns superseding/rebinding that Review
evidence. This fixes cause 1's seat starvation without weakening approval rules.

Rejected: marking success before confirmed sync, reparsing stored review prose as
approval, minting duplicate StageOutcomes/notifications, or keeping the model alive
solely to retry a desktop Git operation.

### D-7. Reclaim legacy parked tasks through exactly the same guarded episode

Do not seed a migration that kills all historical Blocked rows. The enabled sweep
pages them, creates the same durable episode under the task lock, captures current
report/transcript/source coordinates and starts a **new** idle qualification window.
An old CompletedAt or thirty hours of silence does not backdate safety evidence.
Clean pushed tasks can release; clean committed unpushed owned Worktrees can use
D-2's ordinary push. Dirty tasks remain owned with actionable publication refusal.
Working, unknown generation, missing report/transcript binding and uncertain
background execution remain held. Recovery never uses age to stop them.

A Reply, cancellation, new task claim, changed source or replacement generation
at any discovery/reserve/send boundary invalidates the old candidate. Do not delete
the five historical Code workspaces from the card. After release, prove one can
resume from its actual pushed tip with the retained task/agent and context.

### D-8. Truthful occupancy and durable visibility

Keep `OccupiesCapacity` based on runner physical status. Keep host/pipeline budget
policy unchanged; their counters describe different things. Include Blocked and
Queued owners in logical owner resolution where bound to that session; add park
state/reason/release ID to the slot/task detail instead of pretending owned seats
are orphans. Still count a live/unknown pending park as occupied. Fresh absent/Exited
evidence frees it; disconnected catalogue data stays stale/unknown, never zero.

Use CARD-0667's release attention identity joined to the park ID and task/card for
Requested/Held/Parked/ResumePending and sync debt. Show bounded actionable codes
such as `park_dirty`, `park_publish_unconfirmed`, `park_working`, `park_generation_changed`,
`park_shared_owner`, `park_unsupported`, `park_sync_pending`. Persist state before
invalidation; a fresh authenticated GET must reconstruct it after missed events
or server restart. Do not add an alert sink or unsolicited user messaging channel.

### D-9. Ship dormant; recover already accepted work when disabled

Add typed `BlockedTaskParkingOptions.Enabled=false` and
`ReclaimExisting=false`, bound in `server/Program.cs`. New park publication/release
requires Enabled; legacy discovery additionally requires ReclaimExisting. Both
use D-2/D-3, not a faster cleanup path. Disabling prevents new actions; it does not
erase receipts, abandon accepted answers, suppress audit reconciliation, or strand
confirmed-release resumes. The runner refuses unsupported protocol operations.

CARD-0667's proposed default-on S4c must not enable its weaker Blocked-after-report
release ahead of this publication gate. Serialize that activation with this work:
its terminal Succeeded/Failed/Canceled behavior can be commissioned independently,
but Blocked physical release must require D-2 when activated. No rollout from this
plan task is authorized or performed. These are settled implementation defaults,
not unresolved operator choices; TestDesign is the next stage.

## Dependency and implementation order

1. Refresh source and live collision inventory before Code. CARD-0667 and CARD-1043
   share reply/dispatcher/task/runner code; do not admit overlapping Code in parallel.
2. Land/consume CARD-0667's fully fenced runner wire and dormant server coordinator,
   answer persistence/delivery, discovery and attention (through S4b). At this Plan
   baseline only its first two read-only runner slices are present. The caller may
   commission those remaining slices first; CARD-1065 TestDesign can proceed now.
3. Implement CARD-1065's additive publication/park barrier and integrations below.
   Do not fork a second `RunnerSeatRelease` ledger. Read the actually landed method
   names before finalizing TestDesign; preserve the required boundaries below.
4. Consume CARD-1043 before qualifying the Review-block/reply/resettle capstone.
   No CARD-1065 edit to landing approval or generic evidence rebinding is implied.
5. Keep new actions default-off through ordinary Code, Review, publication and
   post-land verification. Activate as described below, then reclaim legacy debt.

Read-only inventory on 2026-10-05: authenticated GET `/api/runner-defaults` returned
revision 2 and no per-kind overrides; GET `/api/session-runners` returned eligible
Linux and Windows lanes plus an unavailable draining entry. Linux physical seats
were 9/10 at that instant; Windows catalogue used delegated-task accounting. These
non-atomic counts are not release evidence. No fleet hostname/path is a routing
constant in this plan. At dispatch re-read defaults/catalogue and current task/host
limits; omit `-Runner` unless deliberately pinning a host, omit `-Platform` unless
the lane requires an OS, and use `-Platform Any` to remove an inherited OS pin.

## Implementation slices

Each slice is 30-60 minutes including its named ordinary checkpoint allowance;
estimates exclude host queue delay and failure diagnosis. Commit/push each complete
slice. If a boundary cannot fit, return a truthful checkpoint and subdivide it in
the plan before expanding the scope. No all-tests-first schema/fixture mega-slice.
All new production entry points remain dormant until S10; S10 still ships off.
Names marked new are proposed; CARD-0667 names refer to its required landed work.

| Slice | Files and boundary | Tests / budget |
|---|---|---|
| S1 | New `server/Domain/Entities/AgentTaskPark.cs`, `server/Application/Services/BlockedTaskParkingService.cs`, `server/Application/Settings/BlockedTaskParkingOptions.cs`; `server/Infrastructure/Data/AppDbContext.cs`, `server/Program.cs`; CLI-generated `server/Migrations/<timestamp>_AddAgentTaskParks.cs`, designer and model snapshot. Durable state/uniqueness and DI, no release caller. | New `tests/Antiphon.Tests/Application/BlockedTaskParkStateTests.cs` and `tests/Antiphon.Tests/TestHelpers/BlockedTaskParkFixture.cs`; B01-B02, CP-1. 40 author + 6 check = 46 min. |
| S2 | New `src/Antiphon.SessionRunner.Contracts/WorkspacePark.cs`, new `src/Antiphon.SessionRunner/RunnerWorkspaceParkService.cs`; reuse internal guards in `RunnerWorkspaceService.cs`. Strict inspection/push/fresh exact-ref/receipt; no wire caller. | New `tests/Antiphon.SessionRunner.Tests/WorkspaceParkPublicationTests.cs`; B03-B05, CP-2. 42 + 7 = 49 min. |
| S3 | `src/Antiphon.SessionRunner/{PhoneHomeCommandDispatcher,PhoneHomeRuntimeAdapter,Program,SessionRunnerRuntime}.cs`, contracts `{PhoneHomeContracts,SessionRunnerContracts,TerminalSeatRelease}.cs`; server `Application/Interfaces/ISessionRunnerClient.cs`, `Infrastructure/Agents/SessionRunner/{PhoneHomeRunnerClient,SessionRunnerHttpClient,RoutingSessionRunnerClient,RunnerScopedSessionRunnerClient}.cs`. Append capability/operation, publication binding and final release check under existing input/generation gate. | New `tests/Antiphon.SessionRunner.Tests/BlockedParkWireTests.cs`; B06-B08, CP-3. 45 + 7 = 52 min. |
| S4 | `BlockedTaskParkingService.cs`, new `server/Application/Services/TaskParkPublicationService.cs`, `PhoneHomeRunnerMirrorPublisher.cs` only for shared transport plumbing; local Git/lease adapter, workspace-mode/no-commit policy. `DelegationReportFormatter.cs` adds scoped WIP-before-block instruction. | New `tests/Antiphon.Tests/Application/TaskParkPublicationTests.cs`; B09-B11, CP-4. 43 + 7 = 50 min. |
| S5 | `AgentTaskReplyService.cs`, `BlockedTaskParkingService.cs`, CARD-0667 `TerminalRunnerSeatRelease{Service,Policy}.cs`, `AgentTaskDispatcher.cs`, `PoolDelegateRelease.cs` and `AgentTaskService.cs`. Post-commit park registration; source-before-release gate; stopped retained identity; pool/retirement reservation. | New `tests/Antiphon.Tests/Application/BlockedTaskParkReleaseTests.cs`; B12-B14, CP-5. 44 + 8 = 52 min. |
| S6 | New `server/Application/Services/BlockedTaskSyncRecoveryService.cs`, `RemoteWorkspaceService.cs`, `BlockedTaskParkingService.cs`, `AgentTaskDispatcher.cs`, `server/Program.cs`. Persist/source-pin sync debt and bounded recovery; no evidence promotion. | New `tests/Antiphon.Tests/Application/BlockedTaskSyncRecoveryTests.cs`; B15-B16, CP-6. 40 + 8 = 48 min. |
| S7 | `AgentTaskReplyService.cs`, `AgentTaskService.cs`, `AgentTaskDispatcher.cs`, `DelegationReportFormatter.cs`, `RemoteWorkspaceService.cs`; consume CARD-0667's existing accepted-answer fields on `AgentTask.cs` and S1's episode fields. No later amendment of the landed S1 migration. Preserve agent, exact branch and input across cold dispatch. | New `tests/Antiphon.Tests/Application/BlockedTaskParkResumeTests.cs`; B17-B19, CP-7. 45 + 9 = 54 min. |
| S8 | Same reply/dispatch/formatter paths; `BlockedContextBuilder.cs`; adjust follow-up guidance without bypassing pinned/remote policy. Real queue continuation and prerequisite-confirmed caller reply; stale-turn isolation. | New `tests/Antiphon.Tests/Application/BlockedTaskParkDeliveryTests.cs`; B20-B22, CP-8. 44 + 9 = 53 min. |
| S9 | `BlockedTaskParkingService.cs`, `AgentTaskDispatcher.cs`, CARD-0667 coordinator, `server/Infrastructure/Agents/SessionRunner/RunnerSlotReconcileJob.cs`. Bounded legacy discovery through the same guards, no migration side effects. | New `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.cs`; B23-B24, CP-9. 42 + 8 = 50 min. |
| S10 | `RunnerSlotService.cs`, `AttentionService.cs`, `AttentionService.Leaks.cs`, DTOs `{AgentTaskDtos,RunnerSlotDtos}.cs`, `BlockedTaskParkingService.cs`, `server/Program.cs`; docs `{session-runtime-invariants,agent-card-lifecycle,orchestration-loop,ops-http,antiphon-api}.md`. Expose truthful state/occupancy and document rollout/refusals/Reply. | New `tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs`; B25-B26, CP-10. 43 + 8 = 51 min. |
| S11 | No new behavior. Complete exact-source Linux/Windows qualification and resolve any defect in its owning slice, with a new committed run only when changed. | CP-11/CP-12/CP-13: 14 + 7 + 16 check; 10 evidence = 47 min. |

All new server fixtures use `DelegationTestServices` for the existing graph and real
migrated isolated PostgreSQL. Sharing a test helper does not authorize changes to
unrelated test infrastructure, build drivers or bundle files. No `antiphon.areas.json`
expansion is planned. Regenerate additive schema against the admitted latest source;
never hand-edit a migration or retrofit an already landed migration.

## Verification requirements for TestDesign

TestDesign must turn this proposal into the required `## Verification design`,
resolve fixture methods against the landed CARD-0667 API, expand any independent
guard variants, and run the plan coverage lint before returning Code. Proposed
class/method names below are one result each (internal scenario loops are not
additional tests). More than one independently removable guard needs separate PC
variants at that stage; the table's one PC per behavior is the minimum, not a cap.

Use real scratch Git repositories and a bare remote for publication/source tests;
real serializers, router/client paths, queue services and PostgreSQL for lifecycle
tests; CARD-0667's real tailers and controlled session child for release. Fake clocks
and named barriers cover races and 120-second windows. No live provider, production
runner, user home, external broker or arbitrary sleep-based idle proof. Git children
are awaited and owned; any new process-spawning fixture uses its assembly limiter.
Kill counters alone are insufficient without custody/manifest and queue assertions.

Each PC uses exactly `/*/*/<Class>/<Method>` for its green -> compiling mutation ->
intended named assertion red -> restored fresh-build green cycle. Run PCs after
ordinary Review and land in the commissioned SourceLanding Mutation task, retaining
receipts externally. Do not mutate a whole class/suite or combine same-file controls.
Zero tests, fixture/build errors and a timeout do not prove the intended red.

| Behavior / PC | Proposed exact Class.Method (test path is its class file above) | Witness and compiling defect |
|---|---|---|
| B01 / PC-01 | `BlockedTaskParkStateTests.C1065_EpisodeIdentitySurvivesRestart` | Two concurrent registrations/restart produce one durable same-attempt episode, next attempt differs. Remove the identity uniqueness/conditional reuse decision; duplicate identity assertion fails. |
| B02 / PC-02 | `BlockedTaskParkStateTests.C1065_ParkingDoesNotSettleOrDiscardHistory` | Blocked remains open; original report/transcript/agent/workspace retained and card does not advance. Make Parked set Succeeded; status/retention witness fails. |
| B03 / PC-03 | `WorkspaceParkPublicationTests.C1065_CleanCommittedTipIsPublishedExactly` | Bare remote holds exact owned full ref/SHA; equal and missing ref cases work; final receipt matches clean HEAD. Remove the push when remote is behind; fresh remote equality fails. |
| B04 / PC-04 | `WorkspaceParkPublicationTests.C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | Tracked/untracked source, sequencer, wrong branch/root/endpoint and non-descendant cases retain files and emit no qualifying receipt. Bypass dirty refusal; dirty receipt witness fails. TestDesign splits the other guards into variants. |
| B05 / PC-05 | `WorkspaceParkPublicationTests.C1065_PushAckWithoutExactRemoteProofIsHeld` | Failed/lying push, unavailable exact ref, changed remote, changed local HEAD/status after push leave hold and no release receipt. Replace post-push exact comparison with exit-code success; remote-proof witness fails. |
| B06 / PC-06 | `BlockedParkWireTests.C1065_OldRunnerNeverReceivesFallbackKill` | Capability absent returns unsupported with zero publication/release/force-kill commands; full wire preserves action/source/generation. Bypass capability admission; command-count witness fails. |
| B07 / PC-07 | `BlockedParkWireTests.C1065_ActivityOrReplacementInvalidatesParkRelease` | Fresh Working/new input/output/source/generation at final gate causes no signal or manifest removal; unchanged qualified control releases once. Omit final park source equality; changed-HEAD witness fails. Existing CARD-0667 controls still protect Working/input/generation. |
| B08 / PC-08 | `BlockedParkWireTests.C1065_ExitUnconfirmedRetainsSeatAndCustody` | Kill throws/non-exit/lost response retains manifest and capacity; authoritative confirmed exit later frees exactly once. Forget before exit confirmation; custody witness fails. |
| B09 / PC-09 | `TaskParkPublicationTests.C1065_WorkspaceModesPreservePublicationAuthority` | Local/remote owned Worktree require clean exact publication; Shared cannot push target/other writer; ReadOnly and SourceLanding never autosave. Admit Shared's unauthorized push; zero-push witness fails. |
| B10 / PC-10 | `TaskParkPublicationTests.C1065_CommitInstructionsAndRefusalsRespectOverrides` | Ordinary blocked brief requires WIP/push; no-commit/ReadOnly/SourceLanding exclusions persist; dirty/no-commit remains visibly held. Remove scoped park instruction; ordinary brief witness fails. TestDesign adds no-commit enforcement variant. |
| B11 / PC-11 | `TaskParkPublicationTests.C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | Change attempt/generation/ref/source after prepared receipt; conditional commit/release is refused without Git overwrite. Omit attempt check; old-receipt witness fails. |
| B12 / PC-12 | `BlockedTaskParkReleaseTests.C1065_ReportPublicationPrecedesPhysicalRelease` | Real blocked settlement commits report/obligation, publication, then release intent/wire in order; crash at each boundary recovers same debt. Bypass receipt-required gate; release-before-publication witness fails. |
| B13 / PC-13 | `BlockedTaskParkReleaseTests.C1065_EachBlockCauseUsesCurrentIdleProof` | Question/unmarked/quota/route/no-session/land-conflict inventory: eligible idle published work parks; Working/quota unknown/sequencer retain ownership; no provider launch. Treat quota Blocked as unconditional idle; Working/no-signal witness fails. |
| B14 / PC-14 | `BlockedTaskParkReleaseTests.C1065_ParkedAgentIsReservedButNotWarm` | Same Agent survives stop and unrelated pool/cancel/retirement passes; no warm reuse; independent standing owners protected. Clear reservation on Parked; unrelated-reuse witness fails. |
| B15 / PC-15 | `BlockedTaskSyncRecoveryTests.C1065_LeaseBusyDoesNotRetainPublishedIdleSeat` | Hold real desktop repository lease; runner clean exact ref still parks/releases with Blocked report and pending sync debt. Make release require synchronized desktop; seat-release witness fails. |
| B16 / PC-16 | `BlockedTaskSyncRecoveryTests.C1065_SyncDebtRecoversWithoutMintingApproval` | Release lease/restart; due sweep syncs exact parked SHA once, preserves verdict/evidence; changed remote/dirty desktop hold; persisted backoff bounds calls. Promote stored report to Succeeded; verdict/no-new-approval witness fails. |
| B17 / PC-17 | `BlockedTaskParkResumeTests.C1065_ReplyStartsOneAttemptFromPublishedSource` | Real Answer/admission/dispatcher yields same task/agent, new session, exact SHA; absent mirror rebuilt; report and old transcript retained. Clear AgentId as ordinary retry; identity witness fails. |
| B18 / PC-18 | `BlockedTaskParkResumeTests.C1065_ReplyRacePersistsOneAnswerAndOneOwner` | Before reservation/after send/lost reply/restart barriers preserve full answer once and no uncertain old-session delivery or duplicate attempt. Enqueue to old session while release ambiguous; destination witness fails. |
| B19 / PC-19 | `BlockedTaskParkResumeTests.C1065_ResumePreservesSourceAndAdmissionRefusals` | Changed ref/dirty tree, stale round, quota, commit-recovery, runner unavailable and capacity waits preserve text/receipt without unauthorized launch. Bypass exact-source recheck; changed-source no-launch witness fails. |
| B20 / PC-20 | `BlockedTaskParkDeliveryTests.C1065_FullAnswerRequiresNewSessionUserPrompt` | Long spilled answer plus context reaches new session; prefix-only/wrong-session prompt cannot mark Sent; accepted answer survives transaction/enqueue/receipt crashes. Use enqueue ACK as delivery; pending-until-whole-prompt witness fails. |
| B21 / PC-21 | `BlockedTaskParkDeliveryTests.C1065_ContinueAndPrerequisiteReplyUseGuardedPath` | Question+authority Continue and explicit caller reply after confirmed prerequisite publication queue one continuation; wrong block kind/no authority refuse; arbitrary Done/report text wakes none; stale old TurnEnd cannot settle it. Bypass Continue classifier; typed-refusal witness fails. |
| B22 / PC-22 | `BlockedTaskParkDeliveryTests.C1065_PinnedFollowupCannotStealParkedIdentity` | New local OnAgent blocked with useful Reply guidance; remote pool refusal preserved; Reply same task succeeds when admitted. Skip Blocked-agent pin check; no-created-task witness fails. |
| B23 / PC-23 | `BlockedTaskParkReclaimTests.C1065_LegacySweepRequiresFreshPublicationAndIdleWindow` | Legacy clean/pushed Code task parks after new qualification; dirty, Working, missing/ambiguous ownership and no-publication cases retain seats; cursor/restart neither skips nor duplicates. Use old CompletedAt as qualified age; zero-early-release witness fails. |
| B24 / PC-24 | `BlockedTaskParkReclaimTests.C1065_ClaimAndReplyInvalidateLegacyCandidate` | New owner/attempt/Reply between list and reserve or reserve and send vetoes old release; cancellation uses its own authority. Omit final owner check; preserved-owner witness fails. |
| B25 / PC-25 | `BlockedTaskParkProjectionTests.C1065_OccupancyTracksProcessesNotBlockedStatus` | Actual slots/catalogue/task projections count pending/live/unknown and omit only confirmed exit; Blocked logical owner is not a generic orphan; attention GET survives lost invalidation/restart. Subtract Published from occupied; physical-count witness fails. |
| B26 / PC-26 | `BlockedTaskParkProjectionTests.C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` | Real default DI does no new publish/release/legacy discovery; each switch separately gated; disabling after accepted reply still recovers receipt/answer delivery. Gate answer recovery behind Enabled; resume-pending recovery witness fails. |

Existing narrow regressions are R01 `RunnerTaskSettlementTests` methods
`Runner_sync_block_commits_the_completion_obligation`,
`Sync_uncertainty_blocks_and_reply_retries`, and
`Refused_sync_never_autosaves_or_releases_workspace`; R02
`AgentTaskSettlementRaceTests.an_answered_blocked_task_is_not_re_blocked_by_the_stale_boundary`
and `the_answer_turn_settles_the_task_and_delivers_one_done_note`; R03
`RemotePoolFollowUpAdmissionTests.Remote_pool_follow_up_refuses_before_insert`.
TestDesign must state mode-specific legacy expectations when enabled; do not delete
a regression simply because the new cold path differs from the disabled live path.
CARD-0667 final protocol/input/Working controls remain prerequisites, and CARD-1043
must cover the successful Review continuation at the integrated candidate SHA.

### Asynchronous delivery inventory

| Producer -> recipient | Durable identity/persistence | Recovery and required receipt |
|---|---|---|
| Blocked settlement -> caller session | Existing settlement/completion obligation + source event; park references that identity. | Busy and idle caller, failure before/after enqueue and restart; complete matching caller UserPrompt, no duplicated blocked note. Extend B12/B20 fixtures; release fault cannot lose the report. |
| Park publication -> release coordinator -> runner | Park action/attempt + exact publication receipt + CARD-0667 release action/generation. | Persist before wire; lost reply -> fresh authoritative reconcile; no blind resend to changed generation, no stop without source proof. B07/B08/B11/B12. |
| Accepted Reply/Continue -> admitted new session | Existing durable accepted-answer ID/round/target attempt, input event and queue conversation key. | Transaction/enqueue/delivery cuts, long input, Busy/newly eligible caller and exact new-session UserPrompt. B17-B21. |
| Sync debt worker -> task/attention readers | Park ID + immutable source + persisted due/attempt/outcome. | Fresh DB/API reconstruction after restart/lost invalidation; no duplicate evidence/settlement. B16/B25. |
| Legacy sweep -> task/attention readers | Same park/release identity as fast registration, persisted cursor. | Freshly claimed owner wins; bounded fair paging; actual authorized GET shows debt/result. B23-B25. |

### Checkpoints

The nine required columns below use the repository importer format. Group prefixes
name lanes: CP-1 through CP-12 run in the **Linux** lane; CP-13 is the **Windows**
lane at the identical final source SHA. No host is pinned. New methods are named
exactly by the behavior table; each class is this card's small test boundary.
Final class-prefix selections must prove no unintended classes were discovered.
TestDesign must finalize roster/counts and add the integrated CARD-0667/CARD-1043
regression selections before Code; no whole Unit/assembly selection is authorized.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1065-s1/` | linux-park-state | `/*/*/BlockedTaskParkStateTests/*` | B01-B02 | all 2 listed, 0 failed/skipped | 2 | 6 |
| CP-2 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1065-s2/` | linux-publication | `/*/*/WorkspaceParkPublicationTests/*` | B03-B05 | all 3 listed, 0 failed/skipped | 3 | 7 |
| CP-3 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1065-s3/` | linux-park-wire | `/*/*/BlockedParkWireTests/*` | B06-B08 | all 3 listed, 0 failed/skipped | 3 | 7 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1065-s4/` | linux-source-policy | `/*/*/TaskParkPublicationTests/*` | B09-B11 | all 3 listed, 0 failed/skipped | 3 | 7 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1065-s5/` | linux-park-release | `/*/*/BlockedTaskParkReleaseTests/*` | B12-B14 | all 3 listed, 0 failed/skipped | 3 | 8 |
| CP-6 | S6 | `tests/Antiphon.Tests -> bin-c1065-s6/` | linux-sync-debt | `/*/*/BlockedTaskSyncRecoveryTests/*` | B15-B16 | all 2 listed, 0 failed/skipped | 2 | 8 |
| CP-7 | S7 | `tests/Antiphon.Tests -> bin-c1065-s7/` | linux-resume | `/*/*/BlockedTaskParkResumeTests/*` | B17-B19 | all 3 listed, 0 failed/skipped | 3 | 9 |
| CP-8 | S8 | `tests/Antiphon.Tests -> bin-c1065-s8/` | linux-delivery | `/*/*/BlockedTaskParkDeliveryTests/*` | B20-B22 | all 3 listed, 0 failed/skipped | 3 | 9 |
| CP-9 | S9 | `tests/Antiphon.Tests -> bin-c1065-s9/` | linux-reclaim | `/*/*/BlockedTaskParkReclaimTests/*` | B23-B24 | all 2 listed, 0 failed/skipped | 2 | 8 |
| CP-10 | S10 | `tests/Antiphon.Tests -> bin-c1065-s10/` | linux-projections | `/*/*/BlockedTaskParkProjectionTests/*` | B25-B26 | all 2 listed, 0 failed/skipped | 2 | 8 |
| CP-11 | S11 | `tests/Antiphon.Tests -> bin-c1065-final-linux/` | linux-final-server | `/*/*/(BlockedTaskPark*)\|(BlockedTaskSyncRecoveryTests*)\|(TaskParkPublicationTests*)/C1065_*` | B01-B02, B09-B26 | all 20 listed, 0 failed/skipped | 20 | 14 |
| CP-12 | S11 | `tests/Antiphon.SessionRunner.Tests -> bin-c1065-final-linux-runner/` | linux-final-runner | `/*/*/(WorkspaceParkPublicationTests*)\|(BlockedParkWireTests*)/C1065_*` | B03-B08 | all 6 listed, 0 failed/skipped | 6 | 7 |
| CP-13 | S11 | `tests/Antiphon.SessionRunner.Tests -> bin-c1065-final-windows/` | windows-final-runner | `/*/*/(WorkspaceParkPublicationTests*)\|(BlockedParkWireTests*)/C1065_*` | B03-B08 | all 6 listed, 0 failed/skipped | 6 | 16 |

Execution contract: use the checkpoint tool `run --plan <this path> --after <slice>
--expected-source-sha <committed SHA>` through the documented build-slot gate for
its driver; await run/wait until exit is not 75. Final rows run on their stated
lanes, not together on whichever host happens to execute the command. Validate
receipts and actual selected names/counts; report each CP-n, source/build binding,
failures and skipped cases. Commit before runs and freeze source while a run is
active. Clean owned `bin-c1065-*/` outputs using checkpoint cleanup. Exit 4 is a
slot timeout, never authorization to run unleased. Run full task-range
`scripts/check-evidence-diff.ps1` in Code/Review; generated evidence stays ignored.

### Cost

Proposed ordinary checkpoint floor: **114 minutes** (sum of EstimatedMinutes).
Authoring/evidence estimate: **438 minutes**, total Code floor **552 minutes** before
host queue delay or repairs; dispatch separately by the 11 bounded slices above.
PC floor: **156 minutes** at 6 minutes per complete 26 method-scoped cycles, plus
30 minutes discovery/restoration/reporting = **186 minutes** minimum Mutation.
Combined provisional floor: **738 minutes**, excluding CARD-0667 prerequisites and
CARD-1043's independent implementation. TestDesign must increase the PC floor for
every added guard variant and finalize narrow regression row costs/counts. These
are planning estimates, not measured execution evidence or permission to omit work.

## Rollout and activation

1. Ship compatible runner protocol first with no server caller enabled. Verify loaded
   runner capability/build identity directly. Absence of either conditional release
   or workspacePark capability must hold, never downgrade. Windows and Linux protocol
   qualification is required before including their respective runners.
2. Publish additive server schema and dormant code through ordinary Review and land.
   Coordinate CARD-0667 activation so Blocked cannot bypass publication. Update the
   canonical main checkout before any restart, use the owning restart/rolling runbook,
   and verify `/api/version` SHA/features plus runner build/capability. Health alone
   proves neither activation nor source identity. No worktree restart override.
3. In an isolated acceptance stack exercise a blocked WIP Code task, real bare-remote
   publication, conditional idle release, capacity returning, a new waiting task
   admitted, and a reply completing from the parked SHA. Exercise a desktop lease
   hold with a completed Review report; seat releases, sync debt later resolves,
   explicit reply plus CARD-1043 evidence binding remains valid. No live provider
   starts are implied by the ordinary unit/integration checkpoints.
4. After qualified publication, explicitly enable new parking while legacy reclaim
   remains false. Observe fresh episodes, source receipts, actual runner seat counts,
   new admissions and whole reply receipts. No false released counts, lost answers,
   changed agent IDs, source drift or Working stops are acceptable.
5. Enable bounded legacy reclaim only after the new path passes; inventory current
   Blocked work and classify every outcome. Dirty/unknown/Working items remain visible
   holds; caller repairs publication explicitly. Verify one reclaimed historical task
   can Reply on its pushed branch. Never use operator force-release as an acceptance
   substitute or delete retained workspaces to force capacity down.
6. Roll back new action switches on any unsafe/refused anomaly; preserve recovery of
   already accepted answers, release receipts and sync debt. Disabling does not
   recreate old processes or erase history. Keep additive schema until all debts are
   reconciled. File any new defect through the normal board process.

## Handoff

TestDesign must finalize the executable verification section, producer-to-recipient
crash cases, one PC per independent guard variant, exact count/roster and cost,
closed checkpoints including R01-R03 and integrated 0667/1043 cases, and capability
wire compatibility. Refresh the partially landed CARD-0667 API and preserve its
Working/input/custody controls. No operator decision is needed to write that design;
Code admission and activation remain ordered by the explicit dependencies above.
