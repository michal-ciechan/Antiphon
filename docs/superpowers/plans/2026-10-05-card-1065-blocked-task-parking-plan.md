# CARD-1065: publish work before parking Blocked tasks and release their seats

Date: 2026-10-05. Plan task: `a650f4b4-eee7-4224-9735-4b0d16c768f9`.
Inspected source: `0de12dac930ad52a235604c566ee643e71b21daa`.
Assigned branch: `feat/card-task-a650f4b4`; fast-forward-only from that source.
Status: Plan complete; **next: TestDesign**. Test design was not folded into this dispatch.
The checkpoint and PC inventory below is a concrete proposal for that stage to qualify,
not a claim that the proposed tests exist or that implementation is ready to run.

Amendment 2026-10-06 (Plan task `444809ee-a385-461d-ad83-ccfb749ec98a`, branch
`feat/card-task-444809ee`, inspected source `a1d014fc0ede59f21ddb573fb369e6abce85a700`,
equal to `origin/master` with S1-S4 landed): S5 Code task `d180d288` (branch
`feat/card-task-d180d288`, tip `14849e9bb5097c0e818a987fce091187ac775aca`, evidence
`docs/investigations/2026-10-06-card-1065-s5-code-blocker.md` on that branch) is blocked
because the landed S3/S4 contracts provide neither an authoritative runner
repository-identity read nor final runner verification for proofs produced on the local
lane. Prerequisite slices **S4b** (runner contract, runtime and wire) and **S4c** (server
capture and typed proof) are inserted additively: D-10-D-12, V-28-V-31, G-201-G-226,
PC-201-PC-226 and checkpoint rows CP-4b/CP-4c. S5 is re-scoped to depend on them and keeps
CP-5 (V-12-V-14). S1-S4 and their rows are not renumbered. Amendment status: plan and
verification design complete under the stated defaults; **next: land**, then Code S4b.

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

### Amendment ground truth (2026-10-06, source a1d014fc0)

Read, not inferred: `RunnerWorkspaceParkService.cs`; `RunnerWorkspaceService.MirrorAsync`,
`OwningRepositoryAsync`, `IsOwnedCommonDirectory`; the `SessionRunnerRuntime` constructor,
`ParkWorkspaceAsync`, `VerifyTerminalParkPublicationAsync` and `ReleaseTerminalSeatAsync`;
`PhoneHomeCommandDispatcher`, `PhoneHomeRuntimeAdapter` and runner `Program.cs` routes and
capability composition; `PhoneHomeContracts.cs`, `TerminalSeatRelease.cs`, `WorkspacePark.cs`,
`RepositoryCloneSource.cs`; server `TaskParkPublicationService.cs`, `LocalTaskParkPublisher.cs`,
`TaskParkPublicationDtos.cs`, `BlockedTaskParkingService.cs`, `RemoteWorkspaceService.MirrorAsync`,
`RemoteWorkspacePreparer`, `TerminalRunnerSeatReleaseService.TryReserveAsync`/`AdvanceAsync`,
`ISessionRunnerClient` and its four implementations; the S3/S4 evidence files; S5 branch
commits `9d72aea30`/`14849e9bb`; `tools/Antiphon.Checkpoints` `PlanTableImporter`/`AfterSelector`.

| S5 / plan assumption | Code at `a1d014fc0` | Consequence |
|---|---|---|
| The server can hand `TaskParkPublicationService.PrepareAsync` the bound runner's repository identity. | No wire operation returns it. `PhoneHomeWorkspaceMirrorResponse` carries only `Path`; `WorkspaceParkCommand` Prepare and Verify both require `Binding.RepositoryIdentity`, and the runner holds `park_repository_changed` when `RepositoryIdentity(common)` differs. `PrepareAsync` holds `park_repository_unknown` when the caller passes null. | D-10: a session-bound, generation-fenced, read-only identity operation (S4b) and a server capture step that persists the runner-observed intent before publication (S4c). |
| The desktop baseline endpoint fingerprint identifies the runner's publication endpoint. | `PrepareAsync` uses `baseline.Remote.EndpointFingerprint` (the desktop `TaskProgressGit.ReadEndpointAsync` digest) for remote episodes; the runner compares it with its own `remote get-url --push --all origin`. On the Linux runner the mirror push URL is the SSH deploy-key form while the desktop origin is the HTTPS clone source, so the runner would hold `park_endpoint_changed` in production. V-9 passed because both sides shared one local bare path. | D-10: the identity read also returns the runner-observed endpoint fingerprint; S4c persists it as the park intent for remote episodes after checking that it normalizes (`RepositoryCloneSource.TryNormalize`) to the same admitted repository as the desktop origin. |
| The final runner conditional command verifies clean HEAD for every workspace mode and both lanes (D-3). | `VerifyTerminalParkPublicationAsync` calls `RunnerWorkspaceParkService.VerifyAsync`, which requires a path under the runner worktree root named `task-<8hex>`, the owned task ref and `RemoteSha == SourceSha`. The runtime builds that verifier only when `PhoneHome:Enabled`; the desktop HTTP runner has none and returns `Unsupported` for any `Publication`. `TaskParkPublicationEvidence.ToRunnerReceipt()` writes `RemoteSha!`, which is null for `NoSourceChanges`. | D-11: typed source modes (`ParkVersion` 2, `workspaceParkSourceModesV1`), a session-checkout-bound local verifier on the non-phone-home runtime, and a mapping that never fabricates a remote SHA. |
| Remote Shared/ReadOnly proofs exist. | `PrepareAsync` holds `park_runner_changed` for a remote binding whose task is not Worktree; Shared and ReadOnly evidence is produced only by `LocalTaskParkPublisher` for sessions bound to the local runner. | The local-lane verifier covers Worktree (verify-only), Shared and ReadOnly local proofs; the remote lane keeps the existing strict mirror verification. |
| The local runner advertises conditional seat release. | Runner `Program.cs` advertises `terminalSeatReleaseV1` and `terminalSeatDeliveryEvidenceV1` unconditionally and `workspaceParkV1` only with a verifier; `GET /api/session-runners` on 2026-10-06 showed the deployed Windows lane advertising neither seat-release feature (older build). | Rollout step 1 already requires the upgraded desktop build; S4b adds the two new features to that gate. |
| The runtime knows each session's checkout. | `RunnerSession` has no cwd member; the launch `request.Cwd` is persisted in the transcript sidecar and the pty-host manifest (`manifest.Cwd ?? sidecar.Cwd` on adoption). | S4b retains the launch/adopted cwd on the session record (additive, internal) and binds both new operations to it. |
| S5 reserves with the persisted publication action identity. | `TerminalRunnerSeatReleaseService.TryReserveAsync` mints `Guid.NewGuid()`; the runtime refuses a release whose `Publication.Request.Binding.ActionId != request.ActionId`; `TaskParkPublicationService.Request` sets `ActionId = park.Id`. | Unchanged S5 scope: reserve with the park identity, as the S4 evidence already records. |
| Nonreport blocks can register and publish. | `BlockedTaskParkingService.RegisterAsync` stores a null `ReportDigest` for a null `Result`; `TaskParkPublicationService.LoadAsync` returns null when `ReportDigest is null`, so `PrepareAsync` holds `park_episode_changed`. | Unchanged S5 scope (blocker item 3): bind existing authorized transcript/checkpoint evidence as the episode digest without inventing a report or `CompletedAt`. |
| Checkpoint rows can be inserted before CP-5 without renumbering. | `AfterSelector` expands only `S<n>-S<m>` numeric ranges; `S4b` is a literal token selectable by `--after S4b` or `--rows CP-4b`. `PlanTableImporter` reads the first `### Checkpoints` heading, does not interpret row-id numbering, and derives `Expect` from `(Class*)` tokens. | CP-4b/CP-4c carry `After` = `S4b`/`S4c`; numeric ranges such as `--after S1-S5` do not select them, so Code runs them by name. |

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

Amendment 2026-10-06 (CARD-1112): S10 did not implement this projection. OpenTaskId stays limited to Dispatched or Working, so a live Blocked or Queued owner remains orphan=true, and the slot DTO still carries no park field. D-8 remains unimplemented. This docs pass does not file a separate card for it.

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

### D-10. Authoritative runner source-identity read, bound to the session's own checkout

Add `PhoneHomeOperation.WorkspaceRepositoryIdentity = 37` and the HTTP route
`POST /sessions/{id}/workspace-repository-identity`, gated by a new capability token
`workspaceRepositoryIdentityV1` beside `terminalSeatReleaseV1`. The typed request binds
session, path, expected runner store and accepted-start generation plus a version; the
result is `Read`, `Held` or `Unknown` with a bounded reason code and, only for `Read`, the
runner-computed `RepositoryIdentity` (SHA-256 of the canonical common directory, the same
function the park service already uses), the runner-observed push `EndpointFingerprint`
(SHA-256 of the single `remote get-url --push --all origin` value), `HeadSha`, the symbolic
`FullRef`, and the runner store/generation it answered for. The runtime serves it under the
session's launch gate: the session must exist and match the expected generation before and
after the Git reads; the path must equal that session's retained launch/adopted checkout;
the common directory must be runner-owned; only read-only Git runs. A missing session,
other path, foreign repository or generation drift is `Held`; an unreadable Git answer is
`Unknown` with no identity. Nothing is pushed, fetched or written, and no identity is ever
derived from the request.

The server consumes it in S4c: for a remote episode the park service calls the bound runner
through the routing/scoped clients, requires the answer to name the park's runner store and
generation, checks that the runner endpoint normalizes to the same admitted repository as the
desktop origin, and persists both runner-observed values as the existing Requested intent
before any Git or wire mutation. A local episode never calls the runner and keeps the
desktop common-directory identity. The runner still re-verifies the persisted identity and
endpoint on Prepare and Verify, so the capture is intent, never trust.

Rejected: an identity field on the mirror response alone (created before any session
generation exists and stale after re-mirroring); deriving the identity from the desktop
common directory or a guessed runner path; reusing the desktop endpoint fingerprint for a
runner whose push URL legitimately differs; a server-only trust path without runner
re-verification; a general "run Git on this path" operation.

### D-11. Typed source modes and a session-checkout-bound local verifier at release

Extend the park receipt additively with `WorkspaceParkSourceMode { Published, NoSourceChanges }`
(default `Published`, so every landed S2/S3 receipt keeps its meaning) and make `RemoteSha`
nullable only for `NoSourceChanges`. A release carrying a typed receipt uses `ParkVersion = 2`
and requires the new capability `workspaceParkSourceModesV1` on the runner; the existing
`ParkVersion != 1 -> Unsupported` refusals on old runners stay in place, and no client may
downgrade to version 1 or drop the publication to get through. A `Published` receipt with a
null remote SHA, or a `NoSourceChanges` receipt with one, is refused before any Git read:
a no-source-change proof is never rewritten as a fictitious publication.

Verification stays inside `ReleaseTerminalSeatAsync` under the input/generation gate, on
the live, exited and absent branches alike. On a phone-home runtime the existing strict
mirror verifier applies unchanged. On a runtime without phone-home repository policy
(the desktop lane) S4b constructs a session-checkout verifier that advertises
`workspaceParkSourceModesV1` only when it exists: the receipt path must equal the session's
retained checkout; `Published` requires a clean tracked tree, no sequencer, HEAD on the
receipt's full ref, HEAD = `SourceSha`, baseline ancestry, the captured endpoint and a fresh
exact remote ref equal to `SourceSha`, and never pushes; `NoSourceChanges` requires the same
clean source with HEAD = `SourceSha` = `BaselineSha` and reads no endpoint. Any mismatch is
`StaleObservation` with the seat, manifest and custody retained. Shared and ReadOnly proofs
are local-lane only, as the landed S4 policy already decides.

Rejected: server-only attestation at send time (it leaves the final HEAD check outside the
runner gate that D-3 requires); a desktop root/repository configuration block for the HTTP
runner (unnecessary once the path is bound to the session's own checkout); loosening the
remote lane's lexical rule; emitting a `Published` receipt for ReadOnly; dropping
`Publication` when the runner lacks the capability.

### D-12. Prerequisite slices S4b and S4c; S5 depends on both and keeps CP-5

S4b owns the runner contract pieces: contracts, runtime, dispatcher/adapter/route mapping,
capability tokens, the session-checkout verifier, and the compile-level server client
plumbing for the identity operation and `ParkVersion` 2 (as S3 did). Its row CP-4b runs the
new runner class together with the landed `BlockedParkWireTests` methods (V-6-V-8) as
named regressions. S4c owns the server consumption: the capture step, the typed evidence
mapping and the client capability gate, with CP-4c running the new server class together
with the landed `TaskParkPublicationTests` methods (V-9-V-11). S5 is re-scoped to depend on
both, starts by cherry-picking the test-only S5 commit `9d72aea30` (V-12-V-14 red witnesses
and the `RunnerSeatReleaseFixture` `parking` flag) from `feat/card-task-d180d288`, composes
capture -> Prepare -> reserve with the park action identity -> `ParkVersion` 2 release, and
keeps its CP-5 row unchanged. Landed S1-S4 and their rows are not renumbered; the two new
rows carry the literal `After` tokens `S4b` and `S4c`.

Rejected: folding the runner contract work into S5 (its brief is CP-5 only and its matrix is
already the largest); one combined slice (two assemblies and two checkpoint builds exceed the
30-60 minute slice rule this plan sets); renumbering S5-S11 or CP-5-CP-13.

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
6. Amendment 2026-10-06: land S4b, then S4c, before S5 Code. S5 begins by
   cherry-picking `9d72aea30c1f0b2ac8ccab1af2076fd2eaff436e` (test-only) from
   `feat/card-task-d180d288`; its documentation commit `14849e9bb` is not cherry-picked.
   Serialize S4b/S4c with any CARD-0667 runner/client Code in flight.

Read-only inventory on 2026-10-05: authenticated GET `/api/runner-defaults` returned
revision 2 and no per-kind overrides; GET `/api/session-runners` returned eligible
Linux and Windows lanes plus an unavailable draining entry. Linux physical seats
were 9/10 at that instant; Windows catalogue used delegated-task accounting. These
non-atomic counts are not release evidence. No fleet hostname/path is a routing
constant in this plan. At dispatch re-read defaults/catalogue and current task/host
limits; omit `-Runner` unless deliberately pinning a host, omit `-Platform` unless
the lane requires an OS, and use `-Platform Any` to remove an inherited OS pin.

Read-only inventory on 2026-10-06 (this amendment): `/api/runner-defaults` revision 2,
operator-set global default runner, no per-kind overrides; `/api/session-runners` returned
a Windows desktop lane (delegated-task accounting, two seats, no seat-release feature
advertised), a draining Linux lane and an eligible Linux lane advertising
`terminalSeatReleaseV1`, `workspaceRepositoryV1` and `workspacePublishV1` but not yet
`workspaceParkV1`. These counts are not release evidence and no fleet location is a
constant here; re-read at dispatch.

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
| S4b | Runner contract pieces (amendment, D-10/D-11). Contracts `src/Antiphon.SessionRunner.Contracts/{PhoneHomeContracts,SessionRunnerContracts,TerminalSeatRelease,WorkspacePark}.cs`: operation 37, `WorkspaceRepositoryIdentityRequest`/`Result`, `WorkspaceParkSourceMode`, nullable `RemoteSha`, `ParkVersion` 2, features `workspaceRepositoryIdentityV1` and `workspaceParkSourceModesV1`. Runner `src/Antiphon.SessionRunner/{SessionRunnerRuntime,RunnerWorkspaceParkService,PhoneHomeCommandDispatcher,PhoneHomeRuntimeAdapter,Program}.cs`: retained session checkout, gated identity read, session-checkout verifier and mode-aware final verification on the live, exited and absent branches, capability advertisement. Server compile-level plumbing only: `ISessionRunnerClient.cs` and `{PhoneHomeRunnerClient,SessionRunnerHttpClient,RoutingSessionRunnerClient,RunnerScopedSessionRunnerClient}.cs` gain the identity call and the version-2 capability gate. No server caller, no migration, default-off. | New `tests/Antiphon.SessionRunner.Tests/WorkspaceSourceVerificationWireTests.cs`; V-28, V-29 plus landed V-6-V-8 as regressions; CP-4b. 44 author + 10 check = 54 min. |
| S4c | Server consumption (amendment, D-10/D-11). `server/Application/Services/TaskParkPublicationService.cs` adds `CaptureSourceIdentityAsync(parkId)` (remote: bound-runner read through `ISessionRunnerDirectory`, store/generation match, endpoint admission through `RepositoryCloneSource`, persisted as the Requested intent; local: desktop identity, zero runner calls) and makes `PrepareAsync` use the persisted runner endpoint for remote episodes. `server/Application/Dtos/TaskParkPublicationDtos.cs` maps evidence to the typed version-2 receipt without fabricating `RemoteSha`. `ISessionRunnerClient.cs` default plus the four clients refuse version 2 without `workspaceParkSourceModesV1`. No coordinator, reply or dispatcher change; default-off. | New `tests/Antiphon.Tests/Application/TaskParkRunnerIdentityTests.cs`; V-30, V-31 plus landed V-9-V-11 as regressions; CP-4c. 40 author + 10 check = 50 min. |
| S5 | Depends on landed S4b and S4c. `AgentTaskReplyService.cs`, `BlockedTaskParkingService.cs`, `TaskParkPublicationService.cs` (nonreport episode digest from existing authorized transcript/checkpoint evidence; no invented Result or CompletedAt), CARD-0667 `TerminalRunnerSeatRelease{Service,Policy}.cs`, `AgentTaskDispatcher.cs`, `PoolDelegateRelease.cs` and `AgentTaskService.cs`. Post-commit park registration; capture -> Prepare -> reserve with the persisted park action identity -> `ParkVersion` 2 release carrying the typed receipt; source checks preserved during ambiguous-release reconciliation; stopped retained identity; pool/retirement reservation. Start by cherry-picking test commit `9d72aea30` from `feat/card-task-d180d288`. | New `tests/Antiphon.Tests/Application/BlockedTaskParkReleaseTests.cs` (its three initial red witnesses exist on that commit); V-12-V-14, CP-5 unchanged. 44 + 8 = 52 min. |
| S6 | New `server/Application/Services/BlockedTaskSyncRecoveryService.cs`, `RemoteWorkspaceService.cs`, `BlockedTaskParkingService.cs`, `AgentTaskDispatcher.cs`, `server/Program.cs`. Persist/source-pin sync debt and bounded recovery; no evidence promotion. | New `tests/Antiphon.Tests/Application/BlockedTaskSyncRecoveryTests.cs`; B15-B16, CP-6. 40 + 8 = 48 min. |
| S7 | `AgentTaskReplyService.cs`, `AgentTaskService.cs`, `AgentTaskDispatcher.cs`, `DelegationReportFormatter.cs`, `RemoteWorkspaceService.cs`; consume CARD-0667's existing accepted-answer fields on `AgentTask.cs` and S1's episode fields. No later amendment of the landed S1 migration. Preserve agent, exact branch and input across cold dispatch. | New `tests/Antiphon.Tests/Application/BlockedTaskParkResumeTests.cs`; B17-B19, CP-7. 45 + 9 = 54 min. |
| S8 | Same reply/dispatch/formatter paths; `BlockedContextBuilder.cs`; adjust follow-up guidance without bypassing pinned/remote policy. Real queue continuation and prerequisite-confirmed caller reply; stale-turn isolation. | New `tests/Antiphon.Tests/Application/BlockedTaskParkDeliveryTests.cs`; B20-B22, CP-8. 44 + 9 = 53 min. |
| S9 | `BlockedTaskParkingService.cs`, `AgentTaskDispatcher.cs`, CARD-0667 coordinator, `server/Infrastructure/Agents/SessionRunner/RunnerSlotReconcileJob.cs`. Bounded legacy discovery through the same guards, no migration side effects. | New `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.cs`; B23-B24, CP-9. 42 + 8 = 50 min. |
| S10 | `RunnerSlotService.cs`, `AttentionService.cs`, `AttentionService.Leaks.cs`, DTOs `{AgentTaskDtos,RunnerSlotDtos}.cs`, `BlockedTaskParkingService.cs`, `server/Program.cs`; docs `{session-runtime-invariants,agent-card-lifecycle,orchestration-loop,ops-http,antiphon-api}.md`. Expose truthful state/occupancy and document rollout/refusals/Reply. Amendment 2026-10-06 (CARD-1112): the D-8 production projection was not implemented. Shipped S10 is the owner-doc and instruction text. A live Blocked or Queued owner remains orphan=true. CP-10 covers those doc pins only. | New `tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs`; B25-B26, CP-10. 43 + 8 = 51 min. |
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

### Proposed checkpoints (superseded by Verification design)

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
   Amendment 2026-10-06: both runners must also advertise `workspaceRepositoryIdentityV1`
   and `workspaceParkSourceModesV1` (S4b) before S5 activation; the Windows lane needs the
   upgraded HTTP runner build, which on 2026-10-06 advertised no seat-release feature.
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

Amendment handoff (2026-10-06): land this amendment, then dispatch Code for S4b with
`checkpoints: <this plan>@<landed sha> section "### Checkpoints"` row CP-4b, then S4c
(CP-4c), then re-dispatch S5 (CP-5) from the landed master with the cherry-pick named in
D-12. No human decision is required; D-10-D-12 are stated defaults the orchestrator may
override before S4b Code starts.


## Verification design

TestDesign task `1e11a5d2-20d8-4a9a-9a86-a44446ef4696`, 2026-10-05.
This appended section qualifies D-1 through D-9; it does not replace the fix design.
Its V/R, guard, PC, checkpoint and cost inventories supersede the provisional B/PC
and checkpoint proposal above. B01-B26 retain their method identities as V-1-V-26;
V-27 adds the missing cold Review integration. The earlier checkpoint heading was
renamed because PlanTableImporter reads the **first exact** Checkpoints heading.
There is now one executable 13-row manifest.

Source distinction: this branch remains a fast-forward descendant of
`837a7f3103a61a58c05a92ceab9cb28e8d8c8da0`. Read-only inspection of fetched master
`691606689` additionally includes CARD-0667 S2a (`b3742ccf3`, tests `b8867fcbd`).
No rebase, merge, cherry-pick, activation or deployment is part of TestDesign.

### Inspection

Paths below are relative to the repository; listed methods/helpers were read, not
inferred from names. New CARD-1065 test classes do not exist yet.

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| CARD-0667 master `TerminalSeatReleaseTests`: all S1a/S1b/S2a test bodies, `SeatWorld`, `SeatChild`, `TailWorld`; contracts `TerminalSeatRelease.cs`; runtime observation, conditional release, final fences, input and kill/forget bodies | Real fresh provider reads + fake-child custody, prompt floor, 120-second window and generation -> V-6-V-8, R-5. `SeatWorld` is private; do not pretend it is a reusable public helper. |
| `RunnerWorkspaceServiceTests`: eight Publish test bodies, `Scratch.Create/Service/Git/Dispose`; production `RunnerWorkspaceService.PublishAsync`, `OwningRepositoryAsync` | Existing publisher permits dirty equal HEAD and is not park authority; root, repository, ref, sequencer, ancestry -> V-3-V-5, R-6. Scratch uses a non-bare origin; new publication worlds use a bare origin. |
| `RunnerTaskSettlementTests`: `Runner_sync_block_commits_the_completion_obligation`, `AssertCompletionObligationAsync`, `Sync_uncertainty_blocks_and_reply_retries`, `Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements`, `Refused_sync_never_autosaves_or_releases_workspace`, `AssertRefusedAsync`; entire `RunnerSettlementWorld`; `RunnerSettlementSyncTests.SyncWorld` topology/service/owned Git/runner/cleanup bodies | Real Git, lease, report and isolated DB -> V-1/V-2/V-9-V-19/V-23/V-24, R-1. Existing notification assertions stop at AwaitingReceipt/queue; they cannot prove caller delivery. |
| `AgentTaskSettlementRaceTests`: two selected reply methods, `SeedBlockedTaskAsync`, `SeedAnswerTurnAsync`, shared task/marked turn seeders; `AgentTaskReplyService.AnswerAsync`, Continue classification/authority and settlement release decision | Same-session watermark, retained prior result and deduplication -> R-2, V-18/V-21. Seeded expected prompts do not prove input delivery; use only for prior history. |
| Entire `RemotePoolFollowUpAdmissionTests`, including `PoolPredecessor`, refusal and fresh-count helpers | Real admission before insert and independent remote/local restrictions -> R-3, V-22. |
| Entire `DelegationTestServices`, `TestDbFixture`, `QueuedReceiptAssertions`, `SessionQueueTranscriptPump`; `BridgeQueueHarness` options/Create/attach/OnSubmitted/Dispose; `PhoneHomeTestHost.StartAsync` and configuration/route composition | Nearest new server fixture, cloned migrated PostgreSQL, real queue, transcript ingestion, random loopback routes -> V-1/V-2/V-9-V-27. The historical Schema name means a cloned database. Pump uses the default store: do not use it unchanged for an isolated DB. |
| `DurableRunnerSpillReceiptTests.Dispatcher_pointer_survives_queue_binding_and_reaches_a_complete_UserPrompt`, `Queued_runner_spill_keeps_its_bytes_and_receives_a_complete_UserPrompt`, `SendNow_persists_the_spill_body_before_delivery_so_a_restart_can_read_it`; their real queue/receipt helpers | Pointer wire receipt versus retained full spill bytes -> V-20, V-27. A pointer receipt cannot prove the provider read the file. |
| `ReviewEvidenceResettlementTests`: Incident/Block/Answer/Rows/Replacement/Report helpers and `C1043_ConfirmedNoPushBinds`, `C1043_FinalReportWins`, `C1043_AppendPreservesHistory`, `C1043_HeaderSnapshotAndGetAgree` | Landed CARD-1043 automatic re-settlement and immutable predecessor/notification -> V-16/V-27, R-4. Those tests use a live old session, so a separate parked continuation capstone is necessary. |
| `RunnerSlotEndpointTests.Release_stops_the_desktop_row_and_records_the_reason`, PhoneHome host setup; PlanTableImporter.ExtractSection/SplitRow and testing/build coverage/checkpoint rules | Nearest endpoint/slot fixture -> V-25/V-26. Its peer-generated Exited DTO is a transport substitute, not process exit proof. |
| Owners: project-context, lifecycle, orchestration-loop stage/delegate rules, session-runtime-invariants delivery/source/occupancy rules, testing-and-build; CARD-0667 and CARD-1043 plans | Layer/receipt/admission and dormant activation boundaries -> all V/R. No card-state, deployment or Working-stop policy change. |
| Amendment 2026-10-06: `RunnerWorkspaceParkService` whole file; `SessionRunnerRuntime` constructor, `ParkWorkspaceAsync`, `VerifyTerminalParkPublicationAsync`, `ReleaseTerminalSeatAsync`; `RunnerWorkspaceService.MirrorAsync`, `OwningRepositoryAsync`, `IsOwnedCommonDirectory`; runner `Program.cs` capability and release routes; `PhoneHomeCommandDispatcher` park/release cases; `PhoneHomeContracts`, `TerminalSeatRelease`, `WorkspacePark`, `RepositoryCloneSource`; server `TaskParkPublicationService`, `LocalTaskParkPublisher`, `TaskParkPublicationDtos`, `BlockedTaskParkingService`, `RemoteWorkspaceService.MirrorAsync`, `TerminalRunnerSeatReleaseService.TryReserveAsync`/`AdvanceAsync`, the four runner clients; `BlockedParkWireTests` World/SeatWire, `TaskParkPublicationTests` PublicationWorld/ParkClient, `RunnerSeatReleaseFixture`; S5 commit `9d72aea30` | Identity producer gap, endpoint fingerprint mismatch, local-lane verifier gap and nullable remote SHA -> V-28-V-31, G-201-G-226. Session cwd lives in the sidecar/manifest, not `RunnerSession` -> S4b retains it. |

#### Landed API and dependency gate

The actual S1a/S1b/S2a API is `ITranscriptTailer.ObserveTerminalSeatAsync(ct)`,
`SessionRunnerRuntime.ObserveTerminalSeatAsync(sessionId, TerminalSeatObservationRequest, ct)`,
`AuthorizeTerminalSeatTokenAsync(sessionId, request, token, ct)` and internal
`ReleaseTerminalSeatAsync(sessionId, TerminalSeatReleaseRequest, timeout, ct)`.
The observation request binds ExpectedRunnerStoreId, ExpectedAcceptedStartedAt,
PromptBindingIdentity and PromptFloorRevision. Release adds ActionId and Token;
result binds SessionId/ActionId/AcceptedStartedAt and its outcome. ConfirmsExit is
not a source-publication receipt. S2a has per-instance final-check/signal hooks and
`BindChildForTest(child, tailer, generation)`, but no public conditional release route.
Its action cache is process-local, not the planned server durable ledger.

| Ordered dependency | What is available / missing | CARD-1065 slices allowed after it |
|---|---|---|
| CARD-0667 S1a -> S1b -> S2a, landed on inspected master | Fresh read, token qualification, internal conditional kill/verify/forget. S2a evidence reports 9 passing executions; this TestDesign did not rerun them. | Observation/fixture design only; no external signal caller. |
| CARD-0667 S2b -> S2c, **not landed** | Shared input/release gate with attempted-input fencing, then actual HTTP/phone-home mapper/capabilities/wrappers. | Required before CARD-1065 S3. Do not expose S2a directly. |
| CARD-0667 S3a -> S3d -> S3b -> S3c -> S3e -> S4a -> S4b, **not landed** | One durable RunnerSeatRelease ledger plus answer journal, exact receipt requeue, recovery, discovery, attention and dormant writer integration. | Required before CARD-1065 S1-S10 under the landed fix plan's ordering. In particular S1 schema builds on S3a, S5 on S3d/S4b, S7-S8 on S3b/S3c, S9-S10 on S3e/S4a/S4b. No duplicate ledger or invented current service signature. |
| CARD-1043 automatic binding/re-settlement, present in this branch | Real `ReviewEvidenceBindingService`, `ReviewEvidenceResettlementTests` and runner Review fixture. Independent recovery/delivery slices may continue separately. | S6 preserves evidence; S8 V-27 and S11 R-4 consume the automatic path at the integrated candidate SHA. No dependency on an unlanded recovery CLI. |
| CARD-1065 S1 -> S2 -> S3 -> S4 -> S4b -> S4c -> S5 -> S6 -> S7 -> S8 -> S9 -> S10 -> S11 | All new action switches stay false in default DI. | Serialize shared reply/dispatcher/runner files with CARD-0667 and CARD-1043 Code. CARD-0667 S4c must not activate Blocked release until this barrier exists. |

Code admission must use a checkout containing those predecessor commits. This branch
need not absorb them to publish a design. Resolve future coordinator calls from the
landed implementation at admission; the behavioral method identities below are fixed.
A missing planned predecessor is a scheduling dependency, not authorization to fake a
ledger. A changed/unreachable boundary returns to Plan before the dependent slice.

Amendment 2026-10-06, landed S1-S4 API at `a1d014fc0` (read, not inferred): runner
`RunnerWorkspaceParkService.PrepareAsync(WorkspaceParkRequest)` and `VerifyAsync(WorkspaceParkReceipt)`
with `ValidateLexicalTarget`, `InspectRepositoryAsync`, `InspectSourceAsync`, `ReadEndpointAsync`
and `ReadExactRefAsync`; `SessionRunnerRuntime.ParkWorkspaceAsync(WorkspaceParkCommand)` under
the launch gate with `ParkGenerationMatches`; `VerifyTerminalParkPublicationAsync` on the
live, exited and absent branches of `ReleaseTerminalSeatAsync`; `TerminalSeatReleaseRequest
(ActionId, Observation, Token, Publication, ParkVersion)`; `WorkspaceParkCommand.Supported`
requiring `workspaceParkV1` and `terminalSeatReleaseV1`. Server `ISessionRunnerClient.ParkWorkspaceAsync`
and `ReleaseTerminalSeatAsync` with HTTP, phone-home, routing and scoped implementations;
`TaskParkPublicationService.PrepareAsync(parkId, remoteRepositoryIdentity)`, `VerifyAsync(parkId)`,
internal `AcceptAsync`; `LocalTaskParkPublisher.InspectAsync(request, mode, repository, previous)`;
`TaskParkPublicationEvidence.From` and `ToRunnerReceipt`; `BlockedTaskParkingService.RegisterAsync`
and internal `PersistStateAsync`; `TerminalRunnerSeatReleaseService.RegisterAndReserveAsync`,
`TryReserveAsync` and `AdvanceAsync` (today sends `new(ActionId, observation, token)` with no
publication). S4b and S4c build on exactly these names; S5 composes them.

#### Missing setup assigned to bounded slices

- S1: implement `BlockedTaskParkFixture` in its already planned file, using
  DelegationTestServices and CreateIsolatedSchemaAsync; every context/ingest/queue/host
  must use that connection. Real migrations, separate committed contexts and EF
  transaction/command interceptors prove uniqueness, CAS and rollback. Test the unique
  index with direct duplicate inserts as well as concurrent service registration, so
  the service lock/reuse guard cannot mask an absent database constraint. Recreated DI
  retains the database, Git root, reports and runner artifacts. Do not seed successful
  release/publication receipts: obtain them through production operations.
- S2-S3: new test files own their private runner worlds, following the inspected
  SeatWorld and Scratch patterns. Use the existing fresh tailers and final barriers;
  new hooks in already named production files pause before/after a real I/O boundary,
  never return eligibility. Use isolated bare remotes, empty global/system Git config,
  no credentials, owned awaited Git children and the project ProcessSpawnLimit.
  Actual HTTP mapper and phone-home adapter must call the same runtime after S2c;
  all server routing/scoped clients are exercised through S5's fixture.
- S4-S6: compose the actual park publisher and coordinator with SyncWorld's topology,
  real repository lease and workspace reservations. Inject IO failure or EF save cuts,
  not a made-up Parked result. Record sequence-numbered boundary observations, checking
  a fresh DB context at the instant of wire entry. A second DB connection must obtain
  the task row lock while Git/idle wait is paused: no long transaction across either.
- S4b/S4c (amendment): the runner class owns a private world following the inspected
  `World`/`SeatWorld` pattern with one bare origin reachable through two different URL
  strings (plain path and `file://` form), a mirror under the runner root for the remote
  lane and a plain checkout used as the session's launch cwd for the local lane; both
  transports (HTTP mapper and phone-home dispatcher) run against the real runtime through
  `MapRunnerCapabilitiesRoute`/`MapTerminalSeatReleaseRoutes` and `PhoneHomeCommandDispatcher`.
  The server class reuses `BlockedTaskParkFixture` and the S4 `PublicationWorld` shape with
  a real in-process `RunnerWorkspaceParkService` behind the `ISessionRunnerDirectory` fake
  plus a recording client whose runner store/generation answers can be varied. No
  successful receipt is seeded; the runtime's own refusal counters prove zero force/kill.
- S7-S8: attach the real queue to the newly dispatcher-created session/agent via
  BridgeQueueHarness's AttachSessionId/AttachAgentId, PreserveDatabaseOnDispose and
  connection options. The fake adapter accepts actual LF/bracketed paste/separate CR;
  OnSubmitted writes native-format fixture bytes, then real normalizer and committed
  ingestion establish UserPrompt. Use a connection-aware ingestion closure in this
  card's fixture, not the default-store pump or the expected queue body as a receipt.
  Track every source event, queue ID, attempt floor, generation and delivered body.
  Use existing input-event/spill/API retrieval for long bodies; verify authorization
  and exact retained bytes at the destination path. Stop/dispose/await every pump.
- S9-S10: bounded sweep page size 2, pass budget 3, seven eligible/held rows plus one
  injected failing candidate; recreate service after every page. Map real attention,
  task-detail and slot endpoints into the loopback host with normal authorized caller
  context. Drop invalidations and re-read after restart. Never boot production Program
  against 17204; real default options are resolved through the test composition.

These seams fit the already named footprint. New test setup is delivered with its
slice, not in an all-tests-first mega-slice. Each test below returns one TUnit result;
its internal independently labeled scenarios are not extra Min executions.

### Delivery inventory

Join identities all the way through. P = task/attempt/block-event/park ID; S =
runner/store/session/accepted-start; U = accepted input event/answer round/target
attempt; N = notification/source event/destination/digest; Q = queue ID plus delivery
attempt floor/generation. No lookup may substitute just AgentId, short task ID or SHA.

| Producer -> recipient | Persistence and durable join | Recovery and observable receipt |
|---|---|---|
| Blocked settlement -> caller session | Report/artifact, source event and N commit before P's release can send; N -> Q pins caller and frozen body. | V-12 runs actual settlement -> notification worker -> real queue for busy and already eligible caller, all cuts H0-H6 below. Release/push failure cannot lose or duplicate it. One complete matching caller UserPrompt beyond Q's floor is required. |
| Park registration/sweep -> publication worker -> release coordinator -> runner | Requested P precedes push; immutable exact-ref receipt binds P+S+repository+endpoint+ref+HEAD; release action links existing CARD-0667 ledger before command. | V-1/V-3-V-8/V-11/V-12/V-23: restart before/after push, receipt save, reserve, send, exit and audit. Query exact remote after uncertain push. Lost release reply uses fresh authoritative S evidence, never blind repeat. Runner recipient proof is exit event/state, only expected manifest removal, preserved conversation/watermark, unchanged neighboring seat and capacity decrement. |
| Reply/Continue -> admission/dispatcher -> new session | U and exact release/P binding commit with target-attempt reservation; dispatcher creates new UUID on same task/agent; U -> Q -> new S. | V-17-V-21/V-26: every H cut for both eligible and busy target; old session cannot receive ambiguous-release input. Full inline UserPrompt or complete actual pointer UserPrompt plus full retrieved bytes/digest. Exactly one target attempt and one matching receipt after recovery. |
| New Review turn -> CARD-1043 re-settlement -> caller | New S/U report and final event -> append-only successor evidence -> new N/Q, preserving predecessor and old N. | V-27: sync-only phase has no new evidence; explicit Reply, full new-session receipt and final report produce a successor on the Code subject SHA. New caller UserPrompt contains the new evidence ID. Run both caller states and H cuts. |
| Sync worker -> task/attention HTTP reader | P + fixed published SHA + persisted retry/due/state; SourceReady commits before invalidation. | V-15/V-16/V-25: restart before/after sync, save, invalidation; authenticated fresh GET agrees with DB and original P. No provider turn or new approval/settlement. |
| Legacy sweep -> task/attention HTTP reader | Durable cursor + P uniqueness, same release ledger; no age-based shortcut. | V-23-V-25: fail callback, page-save and state-save; new claim/reply wins. Fresh GET reconstructs each linked hold/release after restart. |
| Park capture -> publication intent (amendment) | Runner-observed identity and endpoint persist in the Requested intent (P + S + runner store/generation) before any Git or wire mutation; a stale read is never reused across a generation. | V-30: a cut between the read and the intent save leaves no publication and re-reads on retry; mismatched runner store/generation or an unadmitted endpoint holds with zero publication. |

H0 = before producer commit (no accepted result; retry original request). H1 = after
commit before enqueue/admission wake. H2 = queue insert throws/rolls back. H3 = queue
commit succeeds but enqueue acknowledgment/wakeup is lost. H4 = delivery-attempt stamp
commits before input (restart must inspect uncertainty). H5 = actual submit and native
UserPrompt persist but queue verdict save fails. H6 = matching receipt recognized
before producer obligation/accepted-answer completion save. For cold input also cut
after U commits before target-attempt reservation, after reservation before launch,
after launch before Q, and after Q before dispatcher acknowledgment. Reuse the same
U/Q/attempt on recovery. Busy-at-enqueue and already-eligible-at-enqueue are separate
worlds, not “make idle later” substitutes for the latter.

For every handoff, test a successful control and its failure in an otherwise valid
world. Before restoring the fault assert persisted pending debt, zero premature
confirmation and retained full text; then restore/recreate/flush/reconcile to the
recipient receipt. Cross join H0-H6 with both recipient states in V-12/V-20/V-27;
join Reply-vs-release cuts to V-18 and target-attempt cuts to V-17/V-20. Do not multiply
irrelevant provider or Git variants into this transaction matrix: V-7/R-5 cover all
three native formats, V-3-V-5 cover Git combinations. All changed async paths end at
recipient evidence; no request, insert, event, Sent flag or ACK is a verdict.

Substitutes: controlled adapter/native fixture records prove queue, framing,
normalization and persisted recovery, not a paid provider's understanding. Spilled
input proves the complete pointer was submitted and its complete file/API bytes are
available; it does not prove file reading. A fake child with an observed Exited event
proves runtime custody bookkeeping, not OS process-tree kill. DI recreation/EF fault
cuts prove durable recovery, not arbitrary machine power loss. HTTP GET proves the
server projection, not browser rendering. Native provider/OS rollout acceptance
remains the separate isolated acceptance obligation in the fix plan, before enablement.

### Proves it works now

“Now” means required evidence on Code's committed candidate, not tests executed by
TestDesign. V-1-V-26 strengthen the corresponding B01-B26 above; the exact method
roster below is normative. Every labeled PC assertion must be in its named method
or a called helper with its literal label passed through; no constant assertion or
mocked eligibility is acceptable.

| ID | Layer / exact test (one result each) | Required ordinary behavior and boundary combinations |
|---|---|---|
| V-1 | `BlockedTaskParkStateTests.C1065_EpisodeIdentitySurvivesRestart` | Real PostgreSQL: concurrent same episode registrations and DI restart retain one ID; next attempt and next block event each create a different episode; no uncommitted publication/reserve escapes rollback. |
| V-2 | `BlockedTaskParkStateTests.C1065_ParkingDoesNotSettleOrDiscardHistory` | Real state/card lifecycle: Requested/Held/Published/ReleasePending/Parked preserve Blocked/open semantics, CompletedAt null for nonreport blocks, result/failure/report/transcript/workspace/agent/history. Card never moves to Review just for parking. |
| V-3 | `WorkspaceParkPublicationTests.C1065_CleanCommittedTipIsPublishedExactly` | Real bare Git: remote behind/equal/missing, clean tracked tree plus ignored generated files, ordinary FF push or equal-tip observation. Receipt binds every coordinate and fresh exact remote=HEAD, without desktop fetch. |
| V-4 | `WorkspaceParkPublicationTests.C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | Real Git refusals: staged, unstaged, untracked nonignored, dirty submodule; changed ignore policy requiring commit gate; detached/wrong branch, escaped/linked root, foreign common repository, all sequencer forms, invalid baseline/full ref, divergent/behind ancestry. Files/refs unchanged, no qualifying receipt. |
| V-5 | `WorkspaceParkPublicationTests.C1065_PushAckWithoutExactRemoteProofIsHeld` | IO/remote faults: nonzero or canceled push, successful-ACK-without-push, exact-ref unavailable/missing/moved, endpoint change, local HEAD/status drift after push. Clean read failure is Unknown; no receipt. Independent green equal/missing-ref cases prevent accidental blanket refusal. |
| V-6 | `BlockedParkWireTests.C1065_OldRunnerNeverReceivesFallbackKill` | Real serializing HTTP and phone-home adapters: each capability missing alone, unknown wire version and local unsupported peer hold with zero force release/generation kill. Full binding round-trips, existing operation numbers unchanged. |
| V-7 | `BlockedParkWireTests.C1065_ActivityOrReplacementInvalidatesParkRelease` | Real runtime/fresh native reads with published scratch source: Working/Unknown, wrong prompt floor, 0/119.999/120/120.001 seconds, unavailable gap, epoch/store/generation/session-object/binding/file/input/output changes one at a time. Both input routes race both winners; late source HEAD/dirty/ref/endpoint change before signal retains manifest. All-valid control releases once. |
| V-8 | `BlockedParkWireTests.C1065_ExitUnconfirmedRetainsSeatAndCustody` | Runtime custody: kill throw/cancel/non-exit/true-without-exit retain seat and manifest. Actual observed exit, already exited and authoritative absence release expected generation only. Lost reply, duplicate action and restart never kill a replacement or forget the generation watermark. |
| V-9 | `TaskParkPublicationTests.C1065_WorkspaceModesPreservePublicationAuthority` | Publication policy: local/remote owned Worktree use identical clean exact-ref proof; authorized already-published clean Shared works without push; Shared other writer/unauthorized ref/unpublished tip holds. ReadOnly clean known base is NoSourceChanges; discovered writes hold; SourceLanding excludes autosave/publish/release path. |
| V-10 | `TaskParkPublicationTests.C1065_CommitInstructionsAndRefusalsRespectOverrides` | Formatter and service: ordinary writable brief requires truthful WIP commit/push before blocked; NoCommit/ReadOnly/SourceLanding instructions and actual enforcement refuse autosave. No automatic Commit delegate; pending commit recovery and uncertain ownership hold. |
| V-11 | `TaskParkPublicationTests.C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | Real DB conditional advance: change each receipt field alone (park/task/attempt/block event/agent/S/repository/endpoint/ref/SHA/report digest/concurrency token). Wrong immutable receipt never reserves or overwrites source; task lock is not held across Git or idle wait. |
| V-12 | `BlockedTaskParkReleaseTests.C1065_ReportPublicationPrecedesPhysicalRelease` | Real report settlement commits report/checkpoint and completion obligation before publication, then reserve/wire. No source proof means zero release even via CARD-0667 direct Blocked caller. H0-H6 caller delivery plus push/reserve/send/exit/audit cuts recover same durable identities and whole caller prompt. |
| V-13 | `BlockedTaskParkReleaseTests.C1065_EachBlockCauseUsesCurrentIdleProof` | Cause matrix: normal question, bind refusal, unmarked/prerequisite wait, quota, create/routing/kind exhaustion, wall/cost, merge-back and land conflict. Valid current idle+publication can park; Working/unknown/sequencer/foreign writer holds. No-session case has zero release commands, no invented report or CompletedAt and zero provider launches. |
| V-14 | `BlockedTaskParkReleaseTests.C1065_ParkedAgentIsReservedButNotWarm` | Retained identity: stopped agent is not warm, not pooled/reused, not TTL-retired or deleted while Blocked reservation open. Standing, AlwaysOn, board and specialist owners each veto independently; continuation/cancel clears only its own reservation. Failed stop keeps owner. |
| V-15 | `BlockedTaskSyncRecoveryTests.C1065_LeaseBusyDoesNotRetainPublishedIdleSeat` | Hold actual desktop repository lease until runner_sync_lease_busy; independent clean publication and qualified runner still park once. Report/obligation and Pending sync SHA survive restart; failed independent publication still vetoes release. Existing 120-second budget remains unchanged. |
| V-16 | `BlockedTaskSyncRecoveryTests.C1065_SyncDebtRecoversWithoutMintingApproval` | Sync recovery: exact parked SHA only, even if branch later advances; unknown/changed endpoint/dirty desktop/sequencer/non-descendant hold without reset. One try per due item/pass, persisted 1/2/4/5-minute capped backoff, future due skipped and poison item cannot starve neighbor. Restart success updates SourceReady once but does not change verdict, evidence or notification counts. |
| V-17 | `BlockedTaskParkResumeTests.C1065_ReplyStartsOneAttemptFromPublishedSource` | Actual Answer -> normal admission -> dispatcher: same task and agent, one incremented attempt, fresh session UUID and credentials; preserve artifacts and clear prior sequence/nudge/check state. Reuse matching clean workspace or recreate missing mirror from exact pushed SHA. Cut every accepted-answer/attempt/launch/enqueue boundary. |
| V-18 | `BlockedTaskParkResumeTests.C1065_ReplyRacePersistsOneAnswerAndOneOwner` | Reply before release reserve cancels park and uses original live watermark; after reserve/before signal, after exit/before reply and lost release response preserve one durable answer/owner. No old-session delivery while outcome ambiguous; concurrent duplicates create one attempt and receipt. |
| V-19 | `BlockedTaskParkResumeTests.C1065_ResumePreservesSourceAndAdmissionRefusals` | Resume holds independently for dirty/advanced/divergent/missing source, changed ref/endpoint, stale round, active quota, full capacity, routing/kind/OS/host pin, scope/workspace ownership and commit recovery. No provider start in Answer HTTP or silent fallback; accepted text survives each refusal. |
| V-20 | `BlockedTaskParkDeliveryTests.C1065_FullAnswerRequiresNewSessionUserPrompt` | Actual queue receipt: full Unicode answer at 3999/4000/4001 characters and below/at/above configured byte spill ceiling, including last-byte canary; full context and authorized report/transcript/source references. Wrong session/generation/sequence/kind or matching prefix cannot confirm; H0-H6 both target states plus cold-attempt cuts recover one complete matching new-session prompt. |
| V-21 | `BlockedTaskParkDeliveryTests.C1065_ContinueAndPrerequisiteReplyUseGuardedPath` | Continue independently checks Blocked/question/standing authority, then same accepted-answer path. Explicit caller Reply after confirmed prerequisite publication works; arbitrary report prose/Done event wakes nothing; merge-helper parent completion launches no continuation; stale old-session TurnEnd cannot settle new attempt. |
| V-22 | `BlockedTaskParkDeliveryTests.C1065_PinnedFollowupCannotStealParkedIdentity` | Real new-work admission: parked local OnAgent refuses before task insert with Reply guidance; remote follow-up still refuses independently; same-task Reply uses retained reservation and normal admission. |
| V-23 | `BlockedTaskParkReclaimTests.C1065_LegacySweepRequiresFreshPublicationAndIdleWindow` | Legacy paged sweep creates current episodes/windows; ancient CompletedAt/silence is never qualification. Already-logically-parked live clean pushed/unpushed-committed owned Code work recovers via same path; dirty/Working/Unknown/missing source/report binding/ambiguous ownership hold. Cursor and episode survive restart; one reclaimed task cold-resumes its exact tip. |
| V-24 | `BlockedTaskParkReclaimTests.C1065_ClaimAndReplyInvalidateLegacyCandidate` | At list->reserve and reserve->send, change owner/session claim, task attempt/event/status, reply, cancel, source and generation separately. Old candidate cannot signal; cancellation takes only its own authorized path; no historical workspace deletion. |
| V-25 | `BlockedTaskParkProjectionTests.C1065_OccupancyTracksProcessesNotBlockedStatus` | Real slot/task/attention GET: Starting/Running/Stopping and stale/unknown catalogue count occupied through pending/held publication; only authoritative exit/absence frees seat. Blocked and Queued logical owners are not orphans. IDs/reasons survive rollback/missed invalidation/restart, no raw paths/secrets or report/answer payloads leak. Amendment 2026-10-06 (CARD-1112): that logical-owner sentence was not delivered. The shipped test pins the opposite: a live Blocked or Queued owner remains orphan=true. CP-10 is the doc-pin row, not a D-8 GET. |
| V-26 | `BlockedTaskParkProjectionTests.C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` | Default DI Enabled=false/ReclaimExisting=false; all four flag combinations separately gate new park and legacy discovery. Disable after accepted answer, sent release and pending sync: receipt reconciliation, answer delivery and sync debt still recover. No receipt/debt deletion. |
| V-27 | `BlockedTaskParkDeliveryTests.C1065_ParkedReviewReplyBindsFreshEvidence` | CARD-1043 integrated capstone: Review blocked by held real lease, published idle seat released, debt later SourceReady with original unbound outcome unchanged. Explicit Reply cold-starts same Review task/agent; whole new-session prompt then final Clean/Full report binds fresh Code-subject SHA, supersedes old evidence once and yields one whole caller receipt naming new ID. Missing/dirty/wrong-subject evidence refuses; old prompt/report never becomes approval. |
| V-28 | `WorkspaceSourceVerificationWireTests.C1065_RepositoryIdentityReadIsBoundToSessionCheckout` | Operation 37 over both HTTP and phone-home against the real runtime: the all-valid read returns the runner's common-directory identity, push-endpoint digest, HEAD and full ref for the session's own mirror; missing either capability, unknown version, absent session, other path (including a second owned mirror), foreign common directory, and store/generation mismatch before or after the read hold with no identity; Git failure is Unknown; the read runs under the launch gate (a paused Prepare blocks it) and issues only read-only Git. An old-runner surface answers unsupported with zero fallback. |
| V-29 | `WorkspaceSourceVerificationWireTests.C1065_LocalSourceModesVerifyFreshAtRelease` | Version-2 release on a non-phone-home runtime with a session-checkout verifier: Published (verify-only) and NoSourceChanges controls release once each on the live, exited and absent branches; dirty, sequencer, wrong ref, advanced HEAD, endpoint change, remote moved, baseline mismatch and a path that is not the session checkout refuse with retained seat, manifest and custody; mode/SHA inconsistency is refused before Git; no push command is ever issued; a runtime without the verifier advertises no `workspaceParkSourceModesV1` and answers Unsupported with zero force/generation kills. |
| V-30 | `TaskParkRunnerIdentityTests.C1065_RemoteIdentityCaptureBindsEpisodeBeforePublication` | Real isolated PostgreSQL and a real in-process runner park service: remote capture calls only the bound runner, persists the runner-observed identity and endpoint as the Requested intent before Git/wire, and Prepare then publishes against a runner whose push URL string differs from the desktop origin; owner/store/generation mismatch, unadmitted endpoint, Unknown read and a cut before the intent save hold with zero publication and no persisted identity; local capture makes zero runner calls. |
| V-31 | `TaskParkRunnerIdentityTests.C1065_LocalEvidenceMapsToTypedRunnerProofWithoutFiction` | Typed mapping and client gate: Published and NoSourceChanges evidence map to version-2 receipts with the exact mode and a null remote SHA only for NoSourceChanges; every client (default, HTTP, phone-home, routing, scoped) refuses version 2 without `workspaceParkSourceModesV1` as Unsupported with zero wire requests and never downgrades or drops the publication; the real HTTP route and phone-home dispatcher round-trip the mode and null remote SHA to the runtime unchanged. |

### Guards the regression

These existing tests remain under default-off composition. Do not rewrite their live
reply/warm/custody expectations just to make the enabled path green. Enabled behavior
is asserted separately by V-12-V-20 and V-27.

- R-1: Remote settlement safety | `RunnerTaskSettlementTests.Runner_sync_block_commits_the_completion_obligation`, `RunnerTaskSettlementTests.Sync_uncertainty_blocks_and_reply_retries`, `RunnerTaskSettlementTests.Refused_sync_never_autosaves_or_releases_workspace` | source event matches durable obligation; lease-busy Blocked can reply and later sync; dirty/divergent desktop retains bytes, no stopper call or workspace deletion. Three results, CP-11.
- R-2: Live reply watermark/deduplication | `AgentTaskSettlementRaceTests.an_answered_blocked_task_is_not_re_blocked_by_the_stale_boundary`, `AgentTaskSettlementRaceTests.the_answer_turn_settles_the_task_and_delivers_one_done_note` | stale boundary leaves Working and one Blocked event; later answer turn succeeds with one queued caller note. Two results, CP-11. These are queue-persistence regressions, not a delivery substitute.
- R-3: Independent remote pool admission | `RemotePoolFollowUpAdmissionTests.Remote_pool_follow_up_refuses_before_insert` | follow_up_remote_pool_unsupported with unchanged task/event/session counts and no event publication. One result, CP-11.
- R-4: CARD-1043 live continuation compatibility | `ReviewEvidenceResettlementTests.C1043_ConfirmedNoPushBinds`, `ReviewEvidenceResettlementTests.C1043_FinalReportWins`, `ReviewEvidenceResettlementTests.C1043_AppendPreservesHistory`, `ReviewEvidenceResettlementTests.C1043_HeaderSnapshotAndGetAgree` | confirmed equal/pushed source binds; final report governs; predecessor bytes/old snapshot remain immutable; current header/snapshot/GET name the successor or retain Decide on mismatch. Four results, CP-11. V-27 supplies the missing cold delivery integration.
- R-5: CARD-0667 runtime safety | all 24 methods/26 results in `TerminalSeatReleaseTests` after its S2c lands, CP-12/CP-13. Current master contains 17 methods/19 results; the seven S2b/S2c methods named in its plan must exist before these final rows. Exact class selects only this bounded protocol boundary. Assert fresh Working/Unknown refusals, current prompt, full 120 seconds, input ordering, generation/epoch/token/output fences, exit custody, wire mapping and explicit operator compatibility.
- R-6: Legacy publisher compatibility | `RunnerWorkspaceServiceTests.Publish_pushes_only_own_fast_forward_branch`, `RunnerWorkspaceServiceTests.Publish_equal_tip_is_not_pushed_and_reports_dirty_tree`, `RunnerWorkspaceServiceTests.Publish_refuses_an_active_sequencer_before_push` | original nonforced own-ref arguments, permissive equal dirty reporting and sequencer refusal remain unchanged; three results, CP-12/CP-13.


Amendment 2026-10-06: CP-4b re-runs V-6-V-8 and CP-4c re-runs V-9-V-11 as the named
regression controls for the changed runtime and service. They remain V controls with the
method identities above, so the R roster below is unchanged.
Exact regression method roster for lint and TRX comparison (37 methods, 39 results;
R-5 Fresh_tail_reads_each_provider contributes three results):

| ID | Exact test | Required result |
|---|---|---|
| R-1 | `RunnerTaskSettlementTests.Runner_sync_block_commits_the_completion_obligation` | Preserve the decisive R-1 assertions above. |
| R-1 | `RunnerTaskSettlementTests.Sync_uncertainty_blocks_and_reply_retries` | Preserve the decisive R-1 assertions above. |
| R-1 | `RunnerTaskSettlementTests.Refused_sync_never_autosaves_or_releases_workspace` | Preserve the decisive R-1 assertions above. |
| R-2 | `AgentTaskSettlementRaceTests.an_answered_blocked_task_is_not_re_blocked_by_the_stale_boundary` | Preserve the decisive R-2 assertions above. |
| R-2 | `AgentTaskSettlementRaceTests.the_answer_turn_settles_the_task_and_delivers_one_done_note` | Preserve the decisive R-2 assertions above. |
| R-3 | `RemotePoolFollowUpAdmissionTests.Remote_pool_follow_up_refuses_before_insert` | Preserve the decisive R-3 assertions above. |
| R-4 | `ReviewEvidenceResettlementTests.C1043_ConfirmedNoPushBinds` | Preserve the decisive R-4 assertions above. |
| R-4 | `ReviewEvidenceResettlementTests.C1043_FinalReportWins` | Preserve the decisive R-4 assertions above. |
| R-4 | `ReviewEvidenceResettlementTests.C1043_AppendPreservesHistory` | Preserve the decisive R-4 assertions above. |
| R-4 | `ReviewEvidenceResettlementTests.C1043_HeaderSnapshotAndGetAgree` | Preserve the decisive R-4 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Replacement_generation_is_never_released` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Token_for_another_session_is_refused` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Restart_invalidates_volatile_observation_tokens` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Fresh_tail_reads_each_provider` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Unknown_or_partial_tail_never_authorizes_release` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Binding_changes_during_read_refuse_qualification` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Old_turn_end_does_not_qualify_a_new_generation` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Working_remains_protected_after_arbitrary_silence` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Two_observations_require_the_full_safety_margin` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Activity_resets_the_qualification_window` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Unavailable_observation_discards_qualification` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Unknown_backend_custody_refuses_release` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Input_winning_the_gate_invalidates_release` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Conditional_input_invalidates_release` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Release_winning_the_gate_refuses_later_input` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Tail_growth_at_final_check_refuses_signal` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Output_growth_at_signal_boundary_refuses_release` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Kill_failure_retains_manifest_and_capacity` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Duplicate_action_is_idempotent` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Confirmed_exit_forgets_only_the_expected_generation` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Unsupported_capability_never_falls_back_to_force` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Http_and_phone_home_share_conditional_semantics` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Fresh_observation_does_not_publish_duplicate_entries` | Preserve the decisive R-5 assertions above. |
| R-5 | `TerminalSeatReleaseTests.Explicit_operator_release_keeps_its_contract` | Preserve the decisive R-5 assertions above. |
| R-6 | `RunnerWorkspaceServiceTests.Publish_pushes_only_own_fast_forward_branch` | Preserve the decisive R-6 assertions above. |
| R-6 | `RunnerWorkspaceServiceTests.Publish_equal_tip_is_not_pushed_and_reports_dirty_tree` | Preserve the decisive R-6 assertions above. |
| R-6 | `RunnerWorkspaceServiceTests.Publish_refuses_an_active_sequencer_before_push` | Preserve the decisive R-6 assertions above. |

### Guard inventory

Every row is independently breakable and maps to its own distinct PC. Component
comparisons are split even when the implementation puts them in one if-expression.
Inherited CARD-0667 guards exercised by the changed park path have local PCs below;
its broader unrelated server/operator controls remain owned by its own plan. No
safety-critical guard in this extension is intentionally untested. Assertions at a
pure production decision seam are required when a later guard would mask a bypass;
the same method also executes its real end-to-end refusal and an all-valid control.

| Guard | Plan reference + safety-critical guard / invariant | Positive control |
|---|---|---|
| G-1 | D-1: Database uniqueness for task/attempt/block event. | PC-1 |
| G-2 | D-1: Registration recovers the existing episode after restart. | PC-2 |
| G-3 | D-1: New attempt identity is not reused. | PC-3 |
| G-4 | D-1: New block-event identity is not reused. | PC-4 |
| G-5 | D-1: Park state never settles the task/card. | PC-5 |
| G-6 | D-1: Report, transcript, source and agent custody survive parking. | PC-6 |
| G-7 | D-1/D-4: Nonreport blocks do not acquire invented completion timestamps. | PC-7 |
| G-8 | D-2: Clean unpublished owned tip is pushed. | PC-8 |
| G-9 | D-2: Tracked/index dirt vetoes publication. | PC-9 |
| G-10 | D-2: Untracked nonignored files count as dirty. | PC-10 |
| G-11 | D-2: Submodule source dirt counts as dirty. | PC-11 |
| G-12 | D-2: Changed ignore policy requiring commit gate cannot be silently accepted. | PC-12 |
| G-13 | D-2: Requested path is lexically under authorized root. | PC-13 |
| G-14 | D-2: Resolved path is under the owned root. | PC-14 |
| G-15 | D-2: Common repository ownership is verified. | PC-15 |
| G-16 | D-2: Symbolic HEAD equals captured full ref. | PC-16 |
| G-17 | D-2: Only full owned refs and full OIDs are admitted. | PC-17 |
| G-18 | D-2: Every active sequencer vetoes. | PC-18 |
| G-19 | D-2: HEAD descends from captured dispatch baseline. | PC-19 |
| G-20 | D-2: Remote ancestry policy refuses divergent tip. | PC-20 |
| G-21 | D-2: Push itself remains nonforced under remote race. | PC-21 |
| G-22 | D-2: Publication reads fail closed. | PC-22 |
| G-23 | D-2: Push errors are not publication proof. | PC-23 |
| G-24 | D-2: Fresh post-push exact remote observation is mandatory. | PC-24 |
| G-25 | D-2: Equal/missing-baseline refs still require fresh proof. | PC-25 |
| G-26 | D-2: Endpoint fingerprint is preserved. | PC-26 |
| G-27 | D-2: Post-push local HEAD remains unchanged. | PC-27 |
| G-28 | D-2: Post-push source/index remains clean. | PC-28 |
| G-29 | D-3/D-9: workspaceParkV1 capability is required. | PC-29 |
| G-30 | D-3/D-9: Conditional release capability is independently required. | PC-30 |
| G-31 | D-3: Unsupported local/remote protocol has no force fallback. | PC-31 |
| G-32 | D-3: Wire keeps exact publication action/source binding. | PC-32 |
| G-33 | D-3: Runner store fence. | PC-33 |
| G-34 | D-3: Accepted-start generation fence. | PC-34 |
| G-35 | D-3: Token is tied to current session object. | PC-35 |
| G-36 | D-3: Token is tied to runtime epoch. | PC-36 |
| G-37 | D-3: Current prompt floor excludes old idle turns. | PC-37 |
| G-38 | D-3: Working independently vetoes. | PC-38 |
| G-39 | D-3: Unknown/incomplete fresh read independently vetoes. | PC-39 |
| G-40 | D-3: Two observations span full 120-second runner interval. | PC-40 |
| G-41 | D-3: Unavailable gap discards previous window. | PC-41 |
| G-42 | D-3: Binding identity invalidates a token. | PC-42 |
| G-43 | D-3: File revision invalidates a token. | PC-43 |
| G-44 | D-3: Transcript revision invalidates a token. | PC-44 |
| G-45 | D-3: Normal input shares release gate. | PC-45 |
| G-46 | D-3: Conditional input shares release gate. | PC-46 |
| G-47 | D-3: Attempted input invalidates release proof. | PC-47 |
| G-48 | D-3: Release-in-progress vetoes subsequent input. | PC-48 |
| G-49 | D-3: Unsubmitted composer and uncertain input veto custody. | PC-49 |
| G-50 | D-3: Unknown launch/adoption/external child custody vetoes. | PC-50 |
| G-51 | D-3: Final fresh native read precedes signal. | PC-51 |
| G-52 | D-3: Final session-object fence. | PC-52 |
| G-53 | D-3: Final accepted-generation fence. | PC-53 |
| G-54 | D-3: Final tailer ownership fence. | PC-54 |
| G-55 | D-3: Final input revision fence. | PC-55 |
| G-56 | D-3: Final output revision fence. | PC-56 |
| G-57 | D-3: Final source HEAD equality fence. | PC-57 |
| G-58 | D-3: Final source cleanliness fence. | PC-58 |
| G-59 | D-3: Final source ref fence. | PC-59 |
| G-60 | D-3: Final source endpoint fence. | PC-60 |
| G-61 | D-3: Publication reservation excludes competing input. | PC-61 |
| G-62 | D-3: Exit observation precedes forget/capacity release. | PC-62 |
| G-63 | D-3: Kill throw/cancellation remains unresolved. | PC-63 |
| G-64 | D-3: Replay action cannot kill twice. | PC-64 |
| G-65 | D-3: Cached success cannot release replacement. | PC-65 |
| G-66 | D-3: Confirmed eviction preserves generation history and conversation. | PC-66 |
| G-67 | D-3/D-8: Lost response is reconciled only from authoritative evidence. | PC-67 |
| G-68 | D-2: Shared publication has no automatic push authority. | PC-68 |
| G-69 | D-2: Shared authorized exact ref proof is required. | PC-69 |
| G-70 | D-2: Shared other-writer ownership vetoes. | PC-70 |
| G-71 | D-2: ReadOnly writes cannot become NoSourceChanges. | PC-71 |
| G-72 | D-2: ReadOnly known base must match. | PC-72 |
| G-73 | D-2/D-3: SourceLanding keeps its separate custody owner. | PC-73 |
| G-74 | D-2: NoCommit is enforced in service. | PC-74 |
| G-75 | D-2: Commit recovery obligations hold publication. | PC-75 |
| G-76 | D-2: Ordinary brief states WIP-before-block obligation. | PC-76 |
| G-77 | D-2: Brief exclusions preserve explicit custody overrides. | PC-77 |
| G-78 | D-1/D-2: Receipt park/action identity fence. | PC-78 |
| G-79 | D-1/D-2: Receipt task identity fence. | PC-79 |
| G-80 | D-1/D-2: Receipt attempt fence. | PC-80 |
| G-81 | D-1/D-2: Receipt block-event fence. | PC-81 |
| G-82 | D-1/D-3: Task concurrency token fence. | PC-82 |
| G-83 | D-1/D-3: Agent identity fence. | PC-83 |
| G-84 | D-1/D-3: Server receipt runner identity fence. | PC-84 |
| G-85 | D-1/D-3: Server receipt store identity fence. | PC-85 |
| G-86 | D-1/D-3: Server receipt session identity fence. | PC-86 |
| G-87 | D-1/D-3: Server receipt accepted generation fence. | PC-87 |
| G-88 | D-1/D-2: Server receipt repository identity fence. | PC-88 |
| G-89 | D-1/D-2: Server receipt endpoint identity fence. | PC-89 |
| G-90 | D-1/D-2: Server receipt full-ref fence. | PC-90 |
| G-91 | D-1/D-2: Server receipt source SHA fence. | PC-91 |
| G-92 | D-1/D-4: Report/checkpoint digest binds episode. | PC-92 |
| G-93 | D-1/D-3: Git/idle wait runs outside task transaction. | PC-93 |
| G-94 | D-1/D-4: Report and completion obligation commit before release. | PC-94 |
| G-95 | D-2/D-3/D-9: Every Blocked coordinator entry requires publication receipt. | PC-95 |
| G-96 | D-1/D-3: Release intent is durable before wire. | PC-96 |
| G-97 | D-1/D-4: Caller obligation recovers enqueue failure. | PC-97 |
| G-98 | D-1/D-4: Caller queue ACK is not a receipt. | PC-98 |
| G-99 | D-1/D-4: Caller receipt requires complete text. | PC-99 |
| G-100 | D-1/D-4: Caller receipt pins destination. | PC-100 |
| G-101 | D-1/D-4: Caller receipt requires UserPrompt kind. | PC-101 |
| G-102 | D-1/D-4: Caller receipt pins attempt floor. | PC-102 |
| G-103 | D-1/D-4: Caller receipt pins generation. | PC-103 |
| G-104 | D-1/D-4: Caller enqueue retry is deduplicated. | PC-104 |
| G-105 | D-1/D-4: Busy caller is not interrupted. | PC-105 |
| G-106 | D-4: All block writers enter same current eligibility path. | PC-106 |
| G-107 | D-4: Absent session does not create a release command. | PC-107 |
| G-108 | D-4: Ambiguous/foreign workspace owner cannot release. | PC-108 |
| G-109 | D-3/D-5: Parked agent is retained. | PC-109 |
| G-110 | D-3/D-5: Parked agent is never warm. | PC-110 |
| G-111 | D-3/D-5: Pool admission respects park reservation. | PC-111 |
| G-112 | D-3/D-5: Retirement respects park reservation. | PC-112 |
| G-113 | D-3: Standing nonpool owner vetoes. | PC-113 |
| G-114 | D-3: AlwaysOn independently vetoes. | PC-114 |
| G-115 | D-3: Board owner independently vetoes. | PC-115 |
| G-116 | D-3: Specialist owner independently vetoes. | PC-116 |
| G-117 | D-3/D-5: Reservation clearing is episode-specific. | PC-117 |
| G-118 | D-6: Desktop lease debt does not veto independently published idle release. | PC-118 |
| G-119 | D-6: Sync debt survives restart with exact parked source. | PC-119 |
| G-120 | D-6: Sync uses immutable parked SHA. | PC-120 |
| G-121 | D-6: Sync revalidates captured endpoint. | PC-121 |
| G-122 | D-6: Dirty desktop vetoes sync mutation. | PC-122 |
| G-123 | D-6: Desktop sequencer vetoes sync mutation. | PC-123 |
| G-124 | D-6: Non-descendant desktop history vetoes sync. | PC-124 |
| G-125 | D-6: Due/backoff persists and bounds each retry. | PC-125 |
| G-126 | D-6: One bad debt does not starve other due work. | PC-126 |
| G-127 | D-6: Sync success cannot promote task verdict. | PC-127 |
| G-128 | D-6: Sync success cannot mint approval or replay completion. | PC-128 |
| G-129 | D-5: Cold continuation retains task identity. | PC-129 |
| G-130 | D-5: Cold continuation retains agent identity. | PC-130 |
| G-131 | D-5: One accepted answer reserves one attempt. | PC-131 |
| G-132 | D-5: Cold continuation uses fresh session generation. | PC-132 |
| G-133 | D-5: Cold continuation rotates credentials. | PC-133 |
| G-134 | D-5: Old per-session watermarks cannot leak. | PC-134 |
| G-135 | D-5: Missing mirror recreates exact pushed SHA. | PC-135 |
| G-136 | D-5: Reply before reserve wins live path. | PC-136 |
| G-137 | D-5: Ambiguous release holds old-session input. | PC-137 |
| G-138 | D-5: Accepted answer is durable before resume wake. | PC-138 |
| G-139 | D-5: Stop/delete bypass needs exact confirmed release receipt. | PC-139 |
| G-140 | D-5: Resume revalidates exact retained source. | PC-140 |
| G-141 | D-5: Resume revalidates cleanliness. | PC-141 |
| G-142 | D-5: Resume preserves ref identity. | PC-142 |
| G-143 | D-5: Resume preserves endpoint identity. | PC-143 |
| G-144 | D-5: Stale question round cannot accept reply. | PC-144 |
| G-145 | D-5: Quota admission remains enforced. | PC-145 |
| G-146 | D-5: Host capacity remains enforced. | PC-146 |
| G-147 | D-5: Explicit host pin remains enforced. | PC-147 |
| G-148 | D-5: Explicit provider/kind remains enforced. | PC-148 |
| G-149 | D-5: Explicit platform remains enforced. | PC-149 |
| G-150 | D-5: Scope collision remains enforced. | PC-150 |
| G-151 | D-5: Workspace reservation remains enforced. | PC-151 |
| G-152 | D-5: Commit recovery remains enforced on dispatch. | PC-152 |
| G-153 | D-5: Answer HTTP does not start a process. | PC-153 |
| G-154 | D-5: Full answer survives formatting/spill. | PC-154 |
| G-155 | D-5: Receipt requires full matching body. | PC-155 |
| G-156 | D-5: Receipt pins destination session. | PC-156 |
| G-157 | D-5: Receipt pins generation. | PC-157 |
| G-158 | D-5: Receipt pins attempt floor. | PC-158 |
| G-159 | D-5: Receipt kind must be UserPrompt. | PC-159 |
| G-160 | D-5: Queue ACK/Sent cannot replace receipt. | PC-160 |
| G-161 | D-5: Enqueue and launch failure recovery reuses durable identity. | PC-161 |
| G-162 | D-5: Busy target receives no bytes until eligible. | PC-162 |
| G-163 | D-5: Continue requires Blocked status. | PC-163 |
| G-164 | D-5: Continue requires question classification. | PC-164 |
| G-165 | D-5: Continue requires standing authority. | PC-165 |
| G-166 | D-5: Prerequisite text/Done never automatically spends. | PC-166 |
| G-167 | D-5: Old session TurnEnd cannot settle resumed attempt. | PC-167 |
| G-168 | D-5: Merge-helper resolution stays completion. | PC-168 |
| G-169 | D-5: Local OnAgent cannot steal Blocked reservation. | PC-169 |
| G-170 | D-5: Remote pool follow-up restriction remains independent. | PC-170 |
| G-171 | D-7: Legacy window begins at discovery now. | PC-171 |
| G-172 | D-7: Legacy uses the same publication gate. | PC-172 |
| G-173 | D-7: Legacy report/transcript binding is required. | PC-173 |
| G-174 | D-7: Cursor advances durably without skipping held rows. | PC-174 |
| G-175 | D-7: Latest task status invalidates candidate. | PC-175 |
| G-176 | D-7: Latest attempt invalidates discovered candidate. | PC-176 |
| G-177 | D-7: Latest block event invalidates discovered candidate. | PC-177 |
| G-178 | D-7: Latest source invalidates discovered candidate. | PC-178 |
| G-179 | D-7: Latest generation invalidates discovered candidate. | PC-179 |
| G-180 | D-7: New session claim independently invalidates candidate. | PC-180 |
| G-181 | D-7: New agent claim independently invalidates candidate. | PC-181 |
| G-182 | D-8: Occupancy remains physical until confirmed exit. | PC-182 |
| G-183 | D-8: Stale/disconnected catalogue is unknown, never zero. | PC-183 |
| G-184 | D-8: Blocked/Queued retain logical slot ownership. | PC-184 |
| G-185 | D-8: GET rebuilds persisted attention after missed event. | PC-185 |
| G-186 | D-8: Projection/logs exclude payloads and raw endpoints. | PC-186 |
| G-187 | D-9: Enabled independently gates new publication/release. | PC-187 |
| G-188 | D-9: ReclaimExisting independently gates legacy discovery. | PC-188 |
| G-189 | D-9: Disabling cannot strand accepted answer recovery. | PC-189 |
| G-190 | D-9: Disabling cannot abandon release audit reconciliation. | PC-190 |
| G-191 | D-9: Disabling cannot abandon sync debt recovery. | PC-191 |
| G-192 | D-6/CARD-1043: Only fresh final report can authorize evidence replacement. | PC-192 |
| G-193 | D-5/CARD-1043: Cold re-settlement calls shared CARD-1043 replacement path. | PC-193 |
| G-194 | D-5/CARD-1043: Cold completion renders new evidence identity. | PC-194 |
| G-195 | D-1/D-2: Publication intent commits before Git mutation. | PC-195 |
| G-196 | D-2: Reclamation never automatically spends on Commit child. | PC-196 |
| G-197 | D-3: Release response applies only to expected server generation. | PC-197 |
| G-198 | D-3: Release response applies only to expected task attempt. | PC-198 |
| G-199 | D-3: Release response applies only to exact action identity. | PC-199 |
| G-200 | D-2: Repository lease serializes publication. | PC-200 |
| G-201 | D-10: Identity read requires `workspaceRepositoryIdentityV1`, `terminalSeatReleaseV1` and version 1. | PC-201 |
| G-202 | D-10: Identity read is generation-fenced after the Git read. | PC-202 |
| G-203 | D-10: Identity read path must equal the session's retained checkout. | PC-203 |
| G-204 | D-10: Identity read requires a runner-owned common directory. | PC-204 |
| G-205 | D-10: Identity read runs under the session launch gate. | PC-205 |
| G-206 | D-10: Unreadable Git never yields an identity. | PC-206 |
| G-207 | D-10: Endpoint fingerprint is runner-observed, never request-supplied. | PC-207 |
| G-208 | D-10: Absent session holds the identity read. | PC-208 |
| G-209 | D-11: Version-2 release requires `workspaceParkSourceModesV1`. | PC-209 |
| G-210 | D-11: Mode and remote SHA must agree before any Git read. | PC-210 |
| G-211 | D-11: Local verification path must equal the session's retained checkout. | PC-211 |
| G-212 | D-11: Local verification never pushes. | PC-212 |
| G-213 | D-11: NoSourceChanges requires HEAD equal to the baseline. | PC-213 |
| G-214 | D-11: Local Published requires a fresh exact remote ref equal to HEAD. | PC-214 |
| G-215 | D-11: Local modes keep the clean/sequencer/ref source inspection. | PC-215 |
| G-216 | D-11: Exited and absent confirmations verify local modes. | PC-216 |
| G-217 | D-11: `workspaceParkSourceModesV1` is advertised only with a verifier. | PC-217 |
| G-218 | D-10: Remote capture calls only the bound runner owner. | PC-218 |
| G-219 | D-10: Remote Prepare uses the runner-observed endpoint fingerprint. | PC-219 |
| G-220 | D-10: Runner endpoint must normalize to the admitted desktop repository. | PC-220 |
| G-221 | D-10: Unknown read persists no identity and publishes nothing. | PC-221 |
| G-222 | D-10: Captured identity must name the park's runner store and generation. | PC-222 |
| G-223 | D-10: Local capture makes zero runner calls. | PC-223 |
| G-224 | D-11: Typed mapping never fabricates a remote SHA. | PC-224 |
| G-225 | D-11: Clients refuse version 2 without capability and never downgrade. | PC-225 |
| G-226 | D-11: Both transports round-trip mode and null remote SHA exactly. | PC-226 |

### Positive controls

Mutation runs **break/red/restore/green after land**; Code runs ordinary V/R;
Review judges guard independence and executable reachability before land. Each row
below is a compiling production defect, applied singly to the named guard, with
all other facts valid. Do not mutate tests to manufacture red. Use the exact literal
method filter shown, never its whole class. A missing method, build/fixture failure,
timeout or zero executions is not red. After each mutation restore source and make
a fresh isolated build, then execute the same method green. Controls sharing a file
run sequentially; source remains frozen during each run. G-1's database-index mutant
uses a disposable CLI-generated migration and isolated cloned DB only; restore the
migration/model before the fresh green build.

Where multiple layered checks protect the same bad input, the named assertion is
made at the actual production decision as well as on final custody. For example,
bypass of final runtime generation is tested after server reservation so an earlier
server equality cannot mask it. Fault hooks pause/throw at I/O boundaries; they do
not fabricate production success. Labels G-n below must occur on those precise
assertions. All controls are executable once their owning slice and dependencies
land; absent future files are disclosed above, not counted as current test evidence.

| PC | Break guard by compiling defect | Exact detecting filter | Expected red assertion |
|---|---|---|---|
| PC-1 | G-1: remove the composite unique index from model and generate a disposable mutant migration. | `/*/*/BlockedTaskParkStateTests/C1065_EpisodeIdentitySurvivesRestart` | `G-1`: direct separate-context inserts of the same episode key cannot both commit. |
| PC-2 | G-2: replace the existing-key lookup with unconditional insert. | `/*/*/BlockedTaskParkStateTests/C1065_EpisodeIdentitySurvivesRestart` | `G-2`: second registration succeeds with the original park ID. |
| PC-3 | G-3: drop attempt from episode reuse predicate. | `/*/*/BlockedTaskParkStateTests/C1065_EpisodeIdentitySurvivesRestart` | `G-3`: same block event under next attempt has a different park ID. |
| PC-4 | G-4: drop block-event from episode reuse predicate. | `/*/*/BlockedTaskParkStateTests/C1065_EpisodeIdentitySurvivesRestart` | `G-4`: same attempt under next block event has a different park ID. |
| PC-5 | G-5: assign Succeeded when persisting Parked. | `/*/*/BlockedTaskParkStateTests/C1065_ParkingDoesNotSettleOrDiscardHistory` | `G-5`: task remains Blocked and card remains InProgress. |
| PC-6 | G-6: clear retained artifact/context fields on Parked. | `/*/*/BlockedTaskParkStateTests/C1065_ParkingDoesNotSettleOrDiscardHistory` | `G-6`: retained field snapshot equals pre-park snapshot. |
| PC-7 | G-7: assign current time to missing CompletedAt on discovery. | `/*/*/BlockedTaskParkStateTests/C1065_ParkingDoesNotSettleOrDiscardHistory` | `G-7`: routing/quota CompletedAt remains null. |
| PC-8 | G-8: skip push on behind or absent remote. | `/*/*/WorkspaceParkPublicationTests/C1065_CleanCommittedTipIsPublishedExactly` | `G-8`: bare remote exact owned ref equals committed HEAD. |
| PC-9 | G-9: ignore nonempty tracked/index status. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-9`: dirty decision refuses and receipt count is zero. |
| PC-10 | G-10: use --untracked-files=no in strict status. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-10`: untracked code yields no receipt and bytes remain. |
| PC-11 | G-11: pass --ignore-submodules=all to strict status. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-11`: dirty submodule yields no qualifying receipt. |
| PC-12 | G-12: bypass unresolved ignore-policy gate. | `/*/*/TaskParkPublicationTests/C1065_CommitInstructionsAndRefusalsRespectOverrides` | `G-12`: ignore-policy hold persists with zero release. |
| PC-13 | G-13: omit lexical root admission. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-13`: foreign-path decision refuses before Git. |
| PC-14 | G-14: omit resolved containment check. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-14`: in-root link to foreign tree receives no receipt. |
| PC-15 | G-15: skip OwningRepositoryAsync equivalent. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-15`: foreign common repository receives no receipt. |
| PC-16 | G-16: skip symbolic-ref comparison. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-16`: wrong/detached branch receives no receipt. |
| PC-17 | G-17: accept nonempty ref/OID instead of exact shape validation. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-17`: short OID or target ref is refused before push. |
| PC-18 | G-18: return false from active-sequencer predicate. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-18`: merge/rebase/cherry-pick/revert arm has zero pushes. |
| PC-19 | G-19: remove baseline ancestor predicate. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-19`: divergent baseline gets no publication receipt. |
| PC-20 | G-20: omit remote ancestry decision predicate. | `/*/*/WorkspaceParkPublicationTests/C1065_DirtyOrUnsafeSourceCannotPublishReceipt` | `G-20`: otherwise valid remote-divergent decision refuses before push. |
| PC-21 | G-21: add --force to park push arguments. | `/*/*/WorkspaceParkPublicationTests/C1065_PushAckWithoutExactRemoteProofIsHeld` | `G-21`: remote advanced after preflight stays unchanged after rejected push. |
| PC-22 | G-22: convert failed HEAD/status/ancestry inspection to clean-known. | `/*/*/WorkspaceParkPublicationTests/C1065_PushAckWithoutExactRemoteProofIsHeld` | `G-22`: read-error arm yields Unknown and no receipt. |
| PC-23 | G-23: continue success path after nonzero or canceled push. | `/*/*/WorkspaceParkPublicationTests/C1065_PushAckWithoutExactRemoteProofIsHeld` | `G-23`: typed push hold persists and no receipt exists. |
| PC-24 | G-24: reuse pre-push/tracking SHA instead of exact observation. | `/*/*/WorkspaceParkPublicationTests/C1065_PushAckWithoutExactRemoteProofIsHeld` | `G-24`: lying push ACK or moved ref leaves receipt absent. |
| PC-25 | G-25: return prepared receipt early for equal HEAD. | `/*/*/WorkspaceParkPublicationTests/C1065_PushAckWithoutExactRemoteProofIsHeld` | `G-25`: equal-at-baseline ref moved at final read is held. |
| PC-26 | G-26: pass null expected endpoint or skip fingerprint equality. | `/*/*/WorkspaceParkPublicationTests/C1065_PushAckWithoutExactRemoteProofIsHeld` | `G-26`: same ref/SHA on replacement endpoint is refused. |
| PC-27 | G-27: skip final local HEAD equality. | `/*/*/WorkspaceParkPublicationTests/C1065_PushAckWithoutExactRemoteProofIsHeld` | `G-27`: new local commit invalidates receipt. |
| PC-28 | G-28: skip final status read. | `/*/*/WorkspaceParkPublicationTests/C1065_PushAckWithoutExactRemoteProofIsHeld` | `G-28`: new dirt after push invalidates receipt. |
| PC-29 | G-29: omit only workspacePark capability check. | `/*/*/BlockedParkWireTests/C1065_OldRunnerNeverReceivesFallbackKill` | `G-29`: park command count is zero when only workspacePark is absent. |
| PC-30 | G-30: omit only conditional-release capability check. | `/*/*/BlockedParkWireTests/C1065_OldRunnerNeverReceivesFallbackKill` | `G-30`: release command count is zero when only release capability is absent. |
| PC-31 | G-31: call existing ReleaseSlotAsync on Unsupported. | `/*/*/BlockedParkWireTests/C1065_OldRunnerNeverReceivesFallbackKill` | `G-31`: force release and generation-kill call counts are zero. |
| PC-32 | G-32: serialize an empty publication receipt ID. | `/*/*/BlockedParkWireTests/C1065_OldRunnerNeverReceivesFallbackKill` | `G-32`: received binding equals original P/receipt tuple. |
| PC-33 | G-33: remove expected store comparison. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-33`: store-only replacement decision refuses and signal count is zero. |
| PC-34 | G-34: remove accepted-start equality. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-34`: generation-only replacement decision refuses and seat retained. |
| PC-35 | G-35: remove session-object identity from token authorization. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-35`: otherwise valid foreign-object proof is StaleObservation. |
| PC-36 | G-36: remove runtime epoch comparison. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-36`: otherwise valid old-epoch proof is StaleObservation. |
| PC-37 | G-37: ignore PromptFloorRevision. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-37`: old end cannot qualify even after 120 seconds. |
| PC-38 | G-38: remove Working verdict branch from production authorization. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-38`: otherwise matching Working proof returns Working and zero signals. |
| PC-39 | G-39: treat Unknown read as Idle in authorization. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-39`: partial/unavailable otherwise matching proof has zero signals. |
| PC-40 | G-40: change required interval to 119 seconds. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-40`: 119.999-second observation is unqualified. |
| PC-41 | G-41: retain first-observed time on read failure. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-41`: recovered observation at old t+120 starts at zero. |
| PC-42 | G-42: omit binding identity equality. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-42`: binding-only change gives StaleObservation. |
| PC-43 | G-43: omit file revision equality. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-43`: file-only change gives StaleObservation. |
| PC-44 | G-44: omit transcript revision equality. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-44`: transcript-only change gives StaleObservation. |
| PC-45 | G-45: remove launch-gate acquisition in normal input entry. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-45`: normal writer cannot enter while release holds gate. |
| PC-46 | G-46: remove launch-gate acquisition in conditional input entry. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-46`: conditional writer cannot enter while release holds gate. |
| PC-47 | G-47: advance input revision only after successful write. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-47`: failed/pending attempted input prevents signal. |
| PC-48 | G-48: omit ReleaseInProgress input decision. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-48`: both input routes make zero child writes after reserve wins. |
| PC-49 | G-49: ignore backend pending/composer/failed-input hold. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-49`: otherwise eligible composer/input uncertainty retains custody. |
| PC-50 | G-50: ignore unknown-backend custody hold. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-50`: each otherwise eligible unknown custody arm has zero signals. |
| PC-51 | G-51: reuse cached qualification transcript at final check. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-51`: unread native Working at final barrier yields zero signals. |
| PC-52 | G-52: remove final ReferenceEquals current session check. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-52`: object replaced at final barrier is retained without signal. |
| PC-53 | G-53: remove final accepted-start comparison. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-53`: generation changed after final read receives zero signals. |
| PC-54 | G-54: remove final tailer reference check. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-54`: tailer replaced after observation receives zero signals. |
| PC-55 | G-55: omit final input revision comparison. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-55`: input revision advanced at signal boundary yields zero signals. |
| PC-56 | G-56: omit last output sequence comparison. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-56`: output-only change at signal boundary retains seat. |
| PC-57 | G-57: omit park HEAD comparison just before signal. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-57`: changed HEAD at final barrier yields zero signals. |
| PC-58 | G-58: omit final park status comparison. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-58`: new dirty bytes at final barrier retain seat. |
| PC-59 | G-59: omit final symbolic ref equality. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-59`: same SHA on different branch cannot signal. |
| PC-60 | G-60: omit final endpoint binding comparison. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-60`: same ref/SHA on different endpoint cannot signal. |
| PC-61 | G-61: release operation reservation before Git receipt verification. | `/*/*/BlockedParkWireTests/C1065_ActivityOrReplacementInvalidatesParkRelease` | `G-61`: input cannot enter while publication-to-release reservation is held. |
| PC-62 | G-62: treat KillAsync true as confirmed exit without HasExited. | `/*/*/BlockedParkWireTests/C1065_ExitUnconfirmedRetainsSeatAndCustody` | `G-62`: true-without-exit keeps manifest and occupied count. |
| PC-63 | G-63: forget session in release exception handler. | `/*/*/BlockedParkWireTests/C1065_ExitUnconfirmedRetainsSeatAndCustody` | `G-63`: throw/cancel leaves live record and manifest. |
| PC-64 | G-64: evict unresolved action result before duplicate request. | `/*/*/BlockedParkWireTests/C1065_ExitUnconfirmedRetainsSeatAndCustody` | `G-64`: same action makes exactly one kill attempt. |
| PC-65 | G-65: return cached outcome before generation comparison. | `/*/*/BlockedParkWireTests/C1065_ExitUnconfirmedRetainsSeatAndCustody` | `G-65`: replacement is retained and result is GenerationMismatch. |
| PC-66 | G-66: delete accepted-generation watermark/transcript sidecar during forget. | `/*/*/BlockedParkWireTests/C1065_ExitUnconfirmedRetainsSeatAndCustody` | `G-66`: durable watermark and transcript remain after restart. |
| PC-67 | G-67: treat disconnected/incomplete catalogue as absence. | `/*/*/BlockedParkWireTests/C1065_ExitUnconfirmedRetainsSeatAndCustody` | `G-67`: lost reply with unknown catalogue retains debt and occupancy. |
| PC-68 | G-68: route Shared through ordinary worktree push. | `/*/*/TaskParkPublicationTests/C1065_WorkspaceModesPreservePublicationAuthority` | `G-68`: Shared push count is zero even with unpublished commits. |
| PC-69 | G-69: allow Shared clean HEAD without authorized remote receipt. | `/*/*/TaskParkPublicationTests/C1065_WorkspaceModesPreservePublicationAuthority` | `G-69`: unauthorized/unpublished Shared cannot park. |
| PC-70 | G-70: skip shared workspace other-writer predicate. | `/*/*/TaskParkPublicationTests/C1065_WorkspaceModesPreservePublicationAuthority` | `G-70`: otherwise clean published Shared with other writer is held. |
| PC-71 | G-71: return NoSourceChanges before strict dirty inspection. | `/*/*/TaskParkPublicationTests/C1065_WorkspaceModesPreservePublicationAuthority` | `G-71`: ReadOnly with writes yields no no-source-change receipt. |
| PC-72 | G-72: skip ReadOnly base equality. | `/*/*/TaskParkPublicationTests/C1065_WorkspaceModesPreservePublicationAuthority` | `G-72`: clean advanced ReadOnly HEAD is held. |
| PC-73 | G-73: remove SourceLanding exclusion. | `/*/*/TaskParkPublicationTests/C1065_WorkspaceModesPreservePublicationAuthority` | `G-73`: sourced task has zero park publish/release actions. |
| PC-74 | G-74: ignore no-commit flag in park admission. | `/*/*/TaskParkPublicationTests/C1065_CommitInstructionsAndRefusalsRespectOverrides` | `G-74`: NoCommit remains Held with no push/autosave. |
| PC-75 | G-75: ignore pending commit-recovery predicate. | `/*/*/TaskParkPublicationTests/C1065_CommitInstructionsAndRefusalsRespectOverrides` | `G-75`: unresolved recovery remains owned with no receipt. |
| PC-76 | G-76: omit the scoped blocked-publication instruction. | `/*/*/TaskParkPublicationTests/C1065_CommitInstructionsAndRefusalsRespectOverrides` | `G-76`: ordinary brief contains complete commit-before-block and push instructions. |
| PC-77 | G-77: emit ordinary WIP instruction for all workspace modes. | `/*/*/TaskParkPublicationTests/C1065_CommitInstructionsAndRefusalsRespectOverrides` | `G-77`: NoCommit/ReadOnly/SourceLanding briefs contain no conflicting instruction. |
| PC-78 | G-78: omit park/action ID comparison. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-78`: other park receipt reserves zero releases. |
| PC-79 | G-79: omit receipt TaskId comparison. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-79`: other task receipt reserves zero releases. |
| PC-80 | G-80: omit receipt attempt comparison. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-80`: prior-attempt receipt reserves zero releases. |
| PC-81 | G-81: omit source block-event equality. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-81`: same-attempt different-block receipt reserves zero releases. |
| PC-82 | G-82: omit task CAS/concurrency predicate. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-82`: changed token wins and stale advance affects zero rows. |
| PC-83 | G-83: omit retained AgentId equality. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-83`: reassigned-agent receipt cannot advance. |
| PC-84 | G-84: omit receipt runner equality. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-84`: runner-only change cannot reserve. |
| PC-85 | G-85: omit receipt store equality. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-85`: store-only change cannot reserve. |
| PC-86 | G-86: omit receipt session equality. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-86`: session-only change cannot reserve. |
| PC-87 | G-87: omit receipt accepted-start equality. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-87`: generation-only change cannot reserve. |
| PC-88 | G-88: omit repository/common-directory equality. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-88`: repository-only change cannot reserve. |
| PC-89 | G-89: omit server endpoint fingerprint equality. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-89`: endpoint-only change cannot reserve. |
| PC-90 | G-90: omit full-ref equality. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-90`: ref-only change cannot reserve. |
| PC-91 | G-91: omit source SHA equality. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-91`: SHA-only change cannot reserve. |
| PC-92 | G-92: omit retained report/checkpoint digest comparison. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-92`: changed report cannot authorize old episode release. |
| PC-93 | G-93: begin task transaction before awaited Git inspection. | `/*/*/TaskParkPublicationTests/C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | `G-93`: second connection obtains task row lock during paused Git. |
| PC-94 | G-94: invoke park reserve before settlement SaveChanges/commit. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-94`: wire-entry fresh context contains report and exact owed notification. |
| PC-95 | G-95: accept Blocked with only CARD-0667 idle proof. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-95`: direct coordinator and settlement path send zero commands without source proof. |
| PC-96 | G-96: send before committing release action. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-96`: wire-entry fresh context finds exact P/action row. |
| PC-97 | G-97: stamp notification handled before queue insert commits. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-97`: after enqueue rollback/restart one complete caller prompt arrives. |
| PC-98 | G-98: accept queue ACK as notification receipt. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-98`: ACK with no transcript leaves ConfirmedAt null. |
| PC-99 | G-99: use prefix matching for notification receipt. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-99`: prefix-only caller prompt leaves ConfirmedAt null. |
| PC-100 | G-100: remove notification recipient predicate. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-100`: whole note on other session leaves ConfirmedAt null. |
| PC-101 | G-101: accept QueuedUserPrompt as notification receipt. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-101`: matching non-UserPrompt leaves ConfirmedAt null. |
| PC-102 | G-102: omit notification receipt floor predicate. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-102`: old complete prompt leaves ConfirmedAt null. |
| PC-103 | G-103: omit notification receipt generation predicate. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-103`: wrong-generation whole prompt leaves ConfirmedAt null. |
| PC-104 | G-104: generate a new notification conversation key on retry. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-104`: lost acknowledgment recovers exactly one queue row and caller receipt. |
| PC-105 | G-105: use immediate mode for caller completion enqueue. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-105`: busy-before-enqueue caller has zero writes before TurnEnd. |
| PC-106 | G-106: skip discovery for non-report Blocked with null CompletedAt. | `/*/*/BlockedTaskParkReleaseTests/C1065_EachBlockCauseUsesCurrentIdleProof` | `G-106`: eligible quota/unmarked/wall arm acquires same guarded episode. |
| PC-107 | G-107: manufacture a release target for sessionless routing hold. | `/*/*/BlockedTaskParkReleaseTests/C1065_EachBlockCauseUsesCurrentIdleProof` | `G-107`: routing/create hold has zero release and provider launch calls. |
| PC-108 | G-108: ignore unknown or foreign workspace ownership. | `/*/*/BlockedTaskParkReleaseTests/C1065_EachBlockCauseUsesCurrentIdleProof` | `G-108`: otherwise qualified ownership-uncertain task remains held. |
| PC-109 | G-109: run generic ephemeral-agent deletion after release. | `/*/*/BlockedTaskParkReleaseTests/C1065_ParkedAgentIsReservedButNotWarm` | `G-109`: retained agent row still exists with original ID. |
| PC-110 | G-110: set PoolIdleSince on park success. | `/*/*/BlockedTaskParkReleaseTests/C1065_ParkedAgentIsReservedButNotWarm` | `G-110`: PoolIdleSince and PoolReservedForRootTaskId are null. |
| PC-111 | G-111: omit park reservation from reusable-agent selection. | `/*/*/BlockedTaskParkReleaseTests/C1065_ParkedAgentIsReservedButNotWarm` | `G-111`: unrelated task does not acquire parked AgentId. |
| PC-112 | G-112: omit park hold from pool retirement sweep. | `/*/*/BlockedTaskParkReleaseTests/C1065_ParkedAgentIsReservedButNotWarm` | `G-112`: retirement pass keeps parked identity and workspace. |
| PC-113 | G-113: omit standing-owner predicate. | `/*/*/BlockedTaskParkReleaseTests/C1065_ParkedAgentIsReservedButNotWarm` | `G-113`: standing-owner-only case has zero release commands. |
| PC-114 | G-114: omit AlwaysOn predicate. | `/*/*/BlockedTaskParkReleaseTests/C1065_ParkedAgentIsReservedButNotWarm` | `G-114`: AlwaysOn-only pool shape has zero release commands. |
| PC-115 | G-115: omit board ownership predicate. | `/*/*/BlockedTaskParkReleaseTests/C1065_ParkedAgentIsReservedButNotWarm` | `G-115`: board-owner-only case has zero release commands. |
| PC-116 | G-116: omit specialist role/owner predicate. | `/*/*/BlockedTaskParkReleaseTests/C1065_ParkedAgentIsReservedButNotWarm` | `G-116`: specialist-only case has zero release commands. |
| PC-117 | G-117: clear all agent park reservations during one resume/cancel. | `/*/*/BlockedTaskParkReleaseTests/C1065_ParkedAgentIsReservedButNotWarm` | `G-117`: unrelated/current replacement reservation remains intact. |
| PC-118 | G-118: require desktop SourceReady before reserve. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_LeaseBusyDoesNotRetainPublishedIdleSeat` | `G-118`: lease-busy task releases once while sync stays Pending. |
| PC-119 | G-119: keep sync due/SHA only in service memory. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_LeaseBusyDoesNotRetainPublishedIdleSeat` | `G-119`: new service reads same pending park/SHA and retries. |
| PC-120 | G-120: sync current branch tip rather than parked SHA. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_SyncDebtRecoversWithoutMintingApproval` | `G-120`: advanced branch is held without moving desktop to newer tip. |
| PC-121 | G-121: pass no expected fingerprint to sync exact-ref read. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_SyncDebtRecoversWithoutMintingApproval` | `G-121`: changed endpoint holds without fetch/move. |
| PC-122 | G-122: omit desktop cleanliness check. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_SyncDebtRecoversWithoutMintingApproval` | `G-122`: desktop dirty bytes and HEAD remain unchanged. |
| PC-123 | G-123: omit desktop sequencer check. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_SyncDebtRecoversWithoutMintingApproval` | `G-123`: active-sequencer desktop remains unchanged. |
| PC-124 | G-124: replace ff-only update with reset to parked SHA. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_SyncDebtRecoversWithoutMintingApproval` | `G-124`: divergent local commit remains HEAD. |
| PC-125 | G-125: set NextDueAt to now and retry loop immediately. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_SyncDebtRecoversWithoutMintingApproval` | `G-125`: one pass makes one call; restart respects 1/2/4/5 minute schedule. |
| PC-126 | G-126: return from sweep on first held/error item. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_SyncDebtRecoversWithoutMintingApproval` | `G-126`: second due item reaches SourceReady in same bounded pass. |
| PC-127 | G-127: assign Succeeded on SourceReady. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_SyncDebtRecoversWithoutMintingApproval` | `G-127`: status remains historical Blocked after successful sync. |
| PC-128 | G-128: invoke settlement/evidence recording on SourceReady. | `/*/*/BlockedTaskSyncRecoveryTests/C1065_SyncDebtRecoversWithoutMintingApproval` | `G-128`: StageOutcome/completion notification counts stay unchanged. |
| PC-129 | G-129: create a new task instead of requeueing current row. | `/*/*/BlockedTaskParkResumeTests/C1065_ReplyStartsOneAttemptFromPublishedSource` | `G-129`: task ID and task row count are unchanged. |
| PC-130 | G-130: clear AgentId as ordinary RequeueAsync does. | `/*/*/BlockedTaskParkResumeTests/C1065_ReplyStartsOneAttemptFromPublishedSource` | `G-130`: new session belongs to original AgentId. |
| PC-131 | G-131: increment Attempt again on recovery. | `/*/*/BlockedTaskParkResumeTests/C1065_ReplyStartsOneAttemptFromPublishedSource` | `G-131`: after each crash duplicate request yields one target attempt. |
| PC-132 | G-132: reuse old session UUID in launch request. | `/*/*/BlockedTaskParkResumeTests/C1065_ReplyStartsOneAttemptFromPublishedSource` | `G-132`: new session ID differs and old S never receives input. |
| PC-133 | G-133: reuse prior attempt credential binding. | `/*/*/BlockedTaskParkResumeTests/C1065_ReplyStartsOneAttemptFromPublishedSource` | `G-133`: accepted credential identity differs from previous attempt. |
| PC-134 | G-134: retain RepliedAtSequence/nudge/check fields during cold requeue. | `/*/*/BlockedTaskParkResumeTests/C1065_ReplyStartsOneAttemptFromPublishedSource` | `G-134`: new attempt per-session state is reset before launch. |
| PC-135 | G-135: use master as mirror start ref. | `/*/*/BlockedTaskParkResumeTests/C1065_ReplyStartsOneAttemptFromPublishedSource` | `G-135`: new mirror HEAD equals parked SHA and full ref. |
| PC-136 | G-136: ignore accepted reply when reserving candidate. | `/*/*/BlockedTaskParkResumeTests/C1065_ReplyRacePersistsOneAnswerAndOneOwner` | `G-136`: pre-reserve reply prevents release and keeps original attempt/session. |
| PC-137 | G-137: enqueue to historical session during ReleasePending. | `/*/*/BlockedTaskParkResumeTests/C1065_ReplyRacePersistsOneAnswerAndOneOwner` | `G-137`: old-session write and queue counts for accepted answer are zero. |
| PC-138 | G-138: persist answer only after dispatch enqueue. | `/*/*/BlockedTaskParkResumeTests/C1065_ReplyRacePersistsOneAnswerAndOneOwner` | `G-138`: post-accept restart retains full answer and produces one receipt. |
| PC-139 | G-139: admit cold continuation with absent or mismatched receipt. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-139`: no cold launch and no agent deletion for invalid release receipt. |
| PC-140 | G-140: skip retained HEAD equality. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-140`: advanced/divergent source refuses launch without reset. |
| PC-141 | G-141: skip retained status read. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-141`: dirty source refuses launch with bytes retained. |
| PC-142 | G-142: skip resumed full-ref equality. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-142`: same SHA under changed ref refuses launch. |
| PC-143 | G-143: skip resumed endpoint equality. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-143`: same SHA under changed endpoint refuses launch. |
| PC-144 | G-144: omit requested-round equality. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-144`: stale round changes no accepted inputs/attempts. |
| PC-145 | G-145: ignore active quota hold for receipt-bound resume. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-145`: active quota yields zero launches and retains accepted text. |
| PC-146 | G-146: skip capacity reservation for cold resume. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-146`: full host launches zero sessions and accepted reply remains queued. |
| PC-147 | G-147: allow fallback runner on pinned-host refusal. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-147`: no alternate-host launch occurs. |
| PC-148 | G-148: substitute available kind after required-kind refusal. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-148`: no alternate-kind launch occurs. |
| PC-149 | G-149: ignore requested platform during cold dispatch. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-149`: wrong-OS host receives zero launches. |
| PC-150 | G-150: omit scope admission for cold resume. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-150`: colliding task keeps reply pending and launches zero sessions. |
| PC-151 | G-151: omit workspace-use admission for cold resume. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-151`: foreign workspace claim keeps reply pending with zero launches. |
| PC-152 | G-152: ignore pending commit obligation in cold admission. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-152`: commit-recovery hold retains reply and launches zero sessions. |
| PC-153 | G-153: call launch directly after accepting answer. | `/*/*/BlockedTaskParkResumeTests/C1065_ResumePreservesSourceAndAdmissionRefusals` | `G-153`: before dispatcher tick launch count is zero. |
| PC-154 | G-154: truncate answer to prior-report excerpt limit. | `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt` | `G-154`: retrieved/inline answer equals complete Unicode input including tail canary. |
| PC-155 | G-155: replace whole-body comparison with prefix comparison. | `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt` | `G-155`: prefix-only UserPrompt leaves answer pending. |
| PC-156 | G-156: query receipt without target-session predicate. | `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt` | `G-156`: complete old-session UserPrompt leaves answer pending. |
| PC-157 | G-157: omit receipt generation predicate. | `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt` | `G-157`: complete wrong-generation prompt leaves answer pending. |
| PC-158 | G-158: omit receipt sequence floor predicate. | `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt` | `G-158`: complete earlier-sequence prompt leaves answer pending. |
| PC-159 | G-159: accept QueuedUserPrompt/assistant kind. | `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt` | `G-159`: matching non-UserPrompt row leaves answer pending. |
| PC-160 | G-160: clear accepted input after enqueue or Sent alone. | `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt` | `G-160`: no-transcript arm retains accepted text and completion is unconfirmed. |
| PC-161 | G-161: generate new input/conversation key on retry. | `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt` | `G-161`: each crash recovers one Q/attempt and one complete prompt. |
| PC-162 | G-162: change cold queue send mode to immediate. | `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt` | `G-162`: busy-before-enqueue target has zero writes until TurnEnd. |
| PC-163 | G-163: omit Continue Blocked check. | `/*/*/BlockedTaskParkDeliveryTests/C1065_ContinueAndPrerequisiteReplyUseGuardedPath` | `G-163`: otherwise authorized non-Blocked request is refused. |
| PC-164 | G-164: omit question-kind predicate. | `/*/*/BlockedTaskParkDeliveryTests/C1065_ContinueAndPrerequisiteReplyUseGuardedPath` | `G-164`: non-question Blocked request is refused. |
| PC-165 | G-165: omit standing-authority predicate. | `/*/*/BlockedTaskParkDeliveryTests/C1065_ContinueAndPrerequisiteReplyUseGuardedPath` | `G-165`: question without authority is refused. |
| PC-166 | G-166: enqueue continuation from sweep when report mentions completed prerequisite. | `/*/*/BlockedTaskParkDeliveryTests/C1065_ContinueAndPrerequisiteReplyUseGuardedPath` | `G-166`: Done/prose-only change yields zero new attempts/launches. |
| PC-167 | G-167: drop current-session/watermark check on settlement lookup. | `/*/*/BlockedTaskParkDeliveryTests/C1065_ContinueAndPrerequisiteReplyUseGuardedPath` | `G-167`: old turn leaves new attempt Working with no completion event. |
| PC-168 | G-168: route merge-helper parent resolution through resume admission. | `/*/*/BlockedTaskParkDeliveryTests/C1065_ContinueAndPrerequisiteReplyUseGuardedPath` | `G-168`: resolved parent succeeds with zero cold continuation launches. |
| PC-169 | G-169: omit blocked-agent follow-up admission check. | `/*/*/BlockedTaskParkDeliveryTests/C1065_PinnedFollowupCannotStealParkedIdentity` | `G-169`: new task insert count is zero and refusal gives Reply guidance. |
| PC-170 | G-170: skip remote-pool restriction for parked predecessor. | `/*/*/BlockedTaskParkDeliveryTests/C1065_PinnedFollowupCannotStealParkedIdentity` | `G-170`: otherwise eligible remote follow-up returns follow_up_remote_pool_unsupported. |
| PC-171 | G-171: initialize first observation from old CompletedAt. | `/*/*/BlockedTaskParkReclaimTests/C1065_LegacySweepRequiresFreshPublicationAndIdleWindow` | `G-171`: ancient task remains unqualified at new t+119.999. |
| PC-172 | G-172: allow legacy release from MirrorPushed flag alone. | `/*/*/BlockedTaskParkReclaimTests/C1065_LegacySweepRequiresFreshPublicationAndIdleWindow` | `G-172`: dirty/unknown/unpublished legacy arm sends zero commands. |
| PC-173 | G-173: accept missing handoff/report binding. | `/*/*/BlockedTaskParkReclaimTests/C1065_LegacySweepRequiresFreshPublicationAndIdleWindow` | `G-173`: missing-binding task retains seat with typed hold. |
| PC-174 | G-174: advance cursor only in memory and terminate after first page. | `/*/*/BlockedTaskParkReclaimTests/C1065_LegacySweepRequiresFreshPublicationAndIdleWindow` | `G-174`: restart completes fair seven-row traversal without duplicate episodes. |
| PC-175 | G-175: omit Blocked-status recheck before reserve/send. | `/*/*/BlockedTaskParkReclaimTests/C1065_ClaimAndReplyInvalidateLegacyCandidate` | `G-175`: reply/cancel status change prevents stale release. |
| PC-176 | G-176: omit task attempt recheck at reserve/send. | `/*/*/BlockedTaskParkReclaimTests/C1065_ClaimAndReplyInvalidateLegacyCandidate` | `G-176`: next attempt at either barrier prevents old release. |
| PC-177 | G-177: omit block event recheck at reserve/send. | `/*/*/BlockedTaskParkReclaimTests/C1065_ClaimAndReplyInvalidateLegacyCandidate` | `G-177`: same-attempt newer block at either barrier prevents old release. |
| PC-178 | G-178: omit source receipt freshness recheck at reserve/send. | `/*/*/BlockedTaskParkReclaimTests/C1065_ClaimAndReplyInvalidateLegacyCandidate` | `G-178`: changed source at either barrier prevents old release. |
| PC-179 | G-179: omit generation recheck at reserve/send. | `/*/*/BlockedTaskParkReclaimTests/C1065_ClaimAndReplyInvalidateLegacyCandidate` | `G-179`: replacement generation at either barrier prevents old release. |
| PC-180 | G-180: omit session-owner query at final reservation. | `/*/*/BlockedTaskParkReclaimTests/C1065_ClaimAndReplyInvalidateLegacyCandidate` | `G-180`: same-attempt new session claim prevents release. |
| PC-181 | G-181: omit agent-owner query at final reservation. | `/*/*/BlockedTaskParkReclaimTests/C1065_ClaimAndReplyInvalidateLegacyCandidate` | `G-181`: same-agent different-session claim prevents release. |
| PC-182 | G-182: subtract Published/Parked task state from live capacity. | `/*/*/BlockedTaskParkProjectionTests/C1065_OccupancyTracksProcessesNotBlockedStatus` | `G-182`: Starting/Running/Stopping pending/held seats count occupied. |
| PC-183 | G-183: return empty authoritative inventory when runner disconnected. | `/*/*/BlockedTaskParkProjectionTests/C1065_OccupancyTracksProcessesNotBlockedStatus` | `G-183`: unknown inventory does not report released capacity. |
| PC-184 | G-184: filter owner tasks to Dispatched/Working only. | `/*/*/BlockedTaskParkProjectionTests/C1065_OccupancyTracksProcessesNotBlockedStatus` | `G-184`: bound Blocked/Queued slot is not orphan. |
| PC-185 | G-185: serve park projection only from invalidation memory. | `/*/*/BlockedTaskParkProjectionTests/C1065_OccupancyTracksProcessesNotBlockedStatus` | `G-185`: fresh authorized GET after restart contains same park ID/reason. |
| PC-186 | G-186: include raw Git error/report/answer in projection detail. | `/*/*/BlockedTaskParkProjectionTests/C1065_OccupancyTracksProcessesNotBlockedStatus` | `G-186`: synthetic secret/path/body canaries absent from GET and structured logs. |
| PC-187 | G-187: omit Enabled check. | `/*/*/BlockedTaskParkProjectionTests/C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` | `G-187`: Enabled=false produces zero new park Git/wire actions. |
| PC-188 | G-188: ignore ReclaimExisting when Enabled. | `/*/*/BlockedTaskParkProjectionTests/C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` | `G-188`: Enabled=true/ReclaimExisting=false produces zero legacy episodes. |
| PC-189 | G-189: return early from accepted-answer recovery when disabled. | `/*/*/BlockedTaskParkProjectionTests/C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` | `G-189`: disabled recovery still yields one new-session receipt. |
| PC-190 | G-190: return early from pending-release reconciliation when disabled. | `/*/*/BlockedTaskParkProjectionTests/C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` | `G-190`: disabled lost-reply recovery confirms exact generation exit. |
| PC-191 | G-191: return early from sync debt sweep when disabled. | `/*/*/BlockedTaskParkProjectionTests/C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` | `G-191`: disabled due sync debt reaches SourceReady. |
| PC-192 | G-192: feed parked stored report to successful resettlement. | `/*/*/BlockedTaskParkDeliveryTests/C1065_ParkedReviewReplyBindsFreshEvidence` | `G-192`: old report cannot mint successor before new complete answer turn. |
| PC-193 | G-193: restore skip-recording-if-any-outcome on cold success. | `/*/*/BlockedTaskParkDeliveryTests/C1065_ParkedReviewReplyBindsFreshEvidence` | `G-193`: fresh final report creates exactly one correctly bound successor. |
| PC-194 | G-194: reuse old notification snapshot after successor commit. | `/*/*/BlockedTaskParkDeliveryTests/C1065_ParkedReviewReplyBindsFreshEvidence` | `G-194`: complete caller UserPrompt names new evidence ID and exact subject SHA. |
| PC-195 | G-195: push before saving Requested park action. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-195`: at push entry fresh DB contains exact Requested action. |
| PC-196 | G-196: create a Commit child when park sees dirty source. | `/*/*/TaskParkPublicationTests/C1065_CommitInstructionsAndRefusalsRespectOverrides` | `G-196`: dirty hold creates zero delegated tasks and provider launches. |
| PC-197 | G-197: omit server accepted-generation check when applying confirmed result. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-197`: replacement server session stays Running with unchanged termination fields. |
| PC-198 | G-198: omit task attempt check when applying confirmed result. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-198`: new task attempt and owner remain unchanged after stale response. |
| PC-199 | G-199: omit response ActionId equality before audit. | `/*/*/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease` | `G-199`: wrong-action result leaves pending release and no Parked transition. |
| PC-200 | G-200: omit park publication repository lease acquisition. | `/*/*/WorkspaceParkPublicationTests/C1065_CleanCommittedTipIsPublishedExactly` | `G-200`: second publisher cannot enter Git mutation during first reserved operation. |
| PC-201 | G-201: skip the capability/version check in the identity route and dispatcher. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_RepositoryIdentityReadIsBoundToSessionCheckout` | `G-201`: missing capability or version 2 yields `identity_unsupported` with zero runtime calls. |
| PC-202 | G-202: omit the post-read generation/object recheck. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_RepositoryIdentityReadIsBoundToSessionCheckout` | `G-202`: generation replaced during the paused Git read yields `identity_generation_changed` and no identity. |
| PC-203 | G-203: accept any path under the runner root. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_RepositoryIdentityReadIsBoundToSessionCheckout` | `G-203`: a second owned mirror path yields `identity_path_unowned`. |
| PC-204 | G-204: skip the owned-common-directory check. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_RepositoryIdentityReadIsBoundToSessionCheckout` | `G-204`: checkout of a foreign repository yields `identity_repository_unowned`. |
| PC-205 | G-205: run the identity read outside the launch gate. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_RepositoryIdentityReadIsBoundToSessionCheckout` | `G-205`: read completes only after the paused Prepare releases the gate. |
| PC-206 | G-206: compute the identity from the request path when Git fails. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_RepositoryIdentityReadIsBoundToSessionCheckout` | `G-206`: failing Git child yields Unknown with null identity. |
| PC-207 | G-207: echo a request-supplied fingerprint instead of reading origin. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_RepositoryIdentityReadIsBoundToSessionCheckout` | `G-207`: result fingerprint equals the digest of the runner's actual push URL. |
| PC-208 | G-208: answer from the durable manifest when the session is untracked. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_RepositoryIdentityReadIsBoundToSessionCheckout` | `G-208`: untracked session yields `identity_session_unknown`. |
| PC-209 | G-209: accept version 2 without `workspaceParkSourceModesV1`. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_LocalSourceModesVerifyFreshAtRelease` | `G-209`: version-2 release on the unadvertised runtime is Unsupported with zero force/generation kills. |
| PC-210 | G-210: omit the mode/remote-SHA consistency fence. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_LocalSourceModesVerifyFreshAtRelease` | `G-210`: NoSourceChanges with a remote SHA is StaleObservation before any Git command. |
| PC-211 | G-211: verify the server-supplied path instead of the session checkout. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_LocalSourceModesVerifyFreshAtRelease` | `G-211`: receipt for another clean checkout is StaleObservation with retained seat. |
| PC-212 | G-212: push when the exact remote ref is behind HEAD. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_LocalSourceModesVerifyFreshAtRelease` | `G-212`: behind remote is StaleObservation and the command log has zero push. |
| PC-213 | G-213: omit HEAD equal to baseline for NoSourceChanges. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_LocalSourceModesVerifyFreshAtRelease` | `G-213`: clean advanced ReadOnly HEAD is StaleObservation. |
| PC-214 | G-214: skip the fresh exact-ref read for local Published. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_LocalSourceModesVerifyFreshAtRelease` | `G-214`: remote moved after the receipt is StaleObservation. |
| PC-215 | G-215: skip source inspection for local modes. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_LocalSourceModesVerifyFreshAtRelease` | `G-215`: dirty checkout is StaleObservation with manifest retained. |
| PC-216 | G-216: skip local verification on the exited branch. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_LocalSourceModesVerifyFreshAtRelease` | `G-216`: exited child with advanced HEAD is StaleObservation and metadata is retained. |
| PC-217 | G-217: advertise `workspaceParkSourceModesV1` unconditionally. | `/*/*/WorkspaceSourceVerificationWireTests/C1065_LocalSourceModesVerifyFreshAtRelease` | `G-217`: runtime without a verifier does not list the feature. |
| PC-218 | G-218: omit the owner/store/cwd equality before the identity read. | `/*/*/TaskParkRunnerIdentityTests/C1065_RemoteIdentityCaptureBindsEpisodeBeforePublication` | `G-218`: changed owner holds `park_runner_changed` with zero runner calls. |
| PC-219 | G-219: keep the desktop baseline fingerprint for remote Prepare. | `/*/*/TaskParkRunnerIdentityTests/C1065_RemoteIdentityCaptureBindsEpisodeBeforePublication` | `G-219`: runner with a different push URL string publishes with a Published receipt. |
| PC-220 | G-220: omit endpoint normalization equality. | `/*/*/TaskParkRunnerIdentityTests/C1065_RemoteIdentityCaptureBindsEpisodeBeforePublication` | `G-220`: runner endpoint naming another repository holds `park_endpoint_unadmitted`. |
| PC-221 | G-221: persist a partial identity on an Unknown read. | `/*/*/TaskParkRunnerIdentityTests/C1065_RemoteIdentityCaptureBindsEpisodeBeforePublication` | `G-221`: Unknown read leaves `RepositoryIdentity` null and no receipt. |
| PC-222 | G-222: omit the store/generation comparison on the read result. | `/*/*/TaskParkRunnerIdentityTests/C1065_RemoteIdentityCaptureBindsEpisodeBeforePublication` | `G-222`: answer for another generation holds with no persisted identity. |
| PC-223 | G-223: call the runner for a local binding. | `/*/*/TaskParkRunnerIdentityTests/C1065_RemoteIdentityCaptureBindsEpisodeBeforePublication` | `G-223`: local capture records zero runner calls. |
| PC-224 | G-224: fill the remote SHA with the source SHA for NoSourceChanges. | `/*/*/TaskParkRunnerIdentityTests/C1065_LocalEvidenceMapsToTypedRunnerProofWithoutFiction` | `G-224`: mapped NoSourceChanges receipt has null remote SHA. |
| PC-225 | G-225: downgrade to version 1 without publication when the capability is missing. | `/*/*/TaskParkRunnerIdentityTests/C1065_LocalEvidenceMapsToTypedRunnerProofWithoutFiction` | `G-225`: each client returns Unsupported with zero wire requests. |
| PC-226 | G-226: drop the mode from the HTTP wire DTO. | `/*/*/TaskParkRunnerIdentityTests/C1065_LocalEvidenceMapsToTypedRunnerProofWithoutFiction` | `G-226`: runtime-received receipt equals the sent mode and null remote SHA. |

### Out of scope

- Paid/native provider qualification, OS process-tree termination and browser rendering
  are excluded from ordinary rows because the design uses declared deterministic
  substitutes. The original isolated acceptance/activation gate remains required;
  neither simulated UserPrompt nor fake child exit authorizes production enablement.
- No release of Working sessions, inferred prerequisite scheduler, SourceLanding
  autosave, arbitrary rowless ownership recovery, force fallback, branch reset or
  workspace deletion. CARD-0079 and explicit operator cancellation keep their owners.
- No whole Unit/namespace/assembly run: the closed affected filters below include
  CARD-0667 runtime, CARD-1043 automatic binding, legacy publisher and live Reply
  regressions. Unrelated evidence-recovery CLI, provider UI and landing mutation
  suites remain under their own plans. No changed path is excluded for test cost.
- Full Cartesian multiplication of cause x provider x workspace x crash x time is
  unnecessary: each independent decision has an all-valid control and single-factor
  negative, with mandatory interacting pairs/triples in V-7 (input/source/generation),
  V-12/V-20/V-27 (recipient state x every handoff cut), V-15 (busy lease x independent
  publication failure), V-18 (Reply timing x release certainty), V-23 (legacy x age x
  dirty/Working) and V-26 (all four switches x preaccepted work). No age or default
  switch substitutes for source, custody or receipt proof.

### Checkpoints

Exactly one isolated build and one filter per row. S1-S10, S4b and S4c consume their own row once;
S11 runs CP-11/12 on Linux and CP-13 on Windows at the identical committed candidate
SHA. Each new method returns one result, including internal scenario loops. Final
server roster is 23 new + 10 existing = 33; final runner roster is 8 new + 26
CARD-0667 + 3 publisher = 37 (amended 2026-10-06). New tests may not add parameter expansion without
updating this manifest. CP-12/13 deliberately require all S2b/S2c results. Confirm
actual TRX names equal the selected roster, not merely at least Min.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1065-cp1/` | park-state | `/*/*/BlockedTaskParkStateTests/C1065_*` | V-1,V-2 | all 2 listed, 0 failed/skipped | 2 | 7 |
| CP-2 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1065-cp2/` | publication | `/*/*/WorkspaceParkPublicationTests/C1065_*` | V-3-V-5 | all 3 listed, 0 failed/skipped | 3 | 8 |
| CP-3 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1065-cp3/` | park-wire | `/*/*/BlockedParkWireTests/C1065_*` | V-6-V-8 | all 3 listed, 0 failed/skipped | 3 | 9 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1065-cp4/` | source-policy | `/*/*/TaskParkPublicationTests/C1065_*` | V-9-V-11 | all 3 listed, 0 failed/skipped | 3 | 8 |
| CP-4b | S4b | `tests/Antiphon.SessionRunner.Tests -> bin-c1065-cp4b/` | source-wire | `/*/*/(WorkspaceSourceVerificationWireTests*)\|(BlockedParkWireTests*)/C1065_*` | V-28,V-29,V-6-V-8 | all 5 listed, 0 failed/skipped | 5 | 10 |
| CP-4c | S4c | `tests/Antiphon.Tests -> bin-c1065-cp4c/` | source-identity | `/*/*/(TaskParkRunnerIdentityTests*)\|(TaskParkPublicationTests*)/C1065_*` | V-30,V-31,V-9-V-11 | all 5 listed, 0 failed/skipped | 5 | 10 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1065-cp5/` | park-release | `/*/*/BlockedTaskParkReleaseTests/C1065_*` | V-12-V-14 | all 3 listed, 0 failed/skipped | 3 | 9 |
| CP-6 | S6 | `tests/Antiphon.Tests -> bin-c1065-cp6/` | sync-debt | `/*/*/BlockedTaskSyncRecoveryTests/C1065_*` | V-15,V-16 | all 2 listed, 0 failed/skipped | 2 | 9 |
| CP-7 | S7 | `tests/Antiphon.Tests -> bin-c1065-cp7/` | resume | `/*/*/BlockedTaskParkResumeTests/C1065_*` | V-17-V-19 | all 3 listed, 0 failed/skipped | 3 | 10 |
| CP-8 | S8 | `tests/Antiphon.Tests -> bin-c1065-cp8/` | delivery | `/*/*/BlockedTaskParkDeliveryTests/C1065_*` | V-20-V-22,V-27 | all 4 listed, 0 failed/skipped | 4 | 12 |
| CP-9 | S9 | `tests/Antiphon.Tests -> bin-c1065-cp9/` | reclaim | `/*/*/BlockedTaskParkReclaimTests/C1065_*` | V-23,V-24 | all 2 listed, 0 failed/skipped | 2 | 9 |
| CP-10 | S10 | `tests/Antiphon.Tests -> bin-c1065-cp10/` | projection | `/*/*/BlockedTaskParkProjectionTests/C1065_*` | V-25,V-26 doc pins only. Amendment 2026-10-06 (CARD-1112): this row does not prove D-8. The shipped occupancy pin says a live Blocked or Queued owner remains orphan=true. | all 2 listed, 0 failed/skipped | 2 | 9 |
| CP-11 | S11 | `tests/Antiphon.Tests -> bin-c1065-cp11/` | final-server | `/*/*/(BlockedTaskPark*)\|(BlockedTaskSyncRecoveryTests*)\|(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(RunnerTaskSettlementTests*)\|(AgentTaskSettlementRaceTests*)\|(RemotePoolFollowUpAdmissionTests*)\|(ReviewEvidenceResettlementTests*)/(C1065_*)\|(Runner_sync_block_commits_the_completion_obligation*)\|(Sync_uncertainty_blocks_and_reply_retries*)\|(Refused_sync_never_autosaves_or_releases_workspace*)\|(an_answered_blocked_task_is_not_re_blocked_by_the_stale_boundary*)\|(the_answer_turn_settles_the_task_and_delivers_one_done_note*)\|(Remote_pool_follow_up_refuses_before_insert*)\|(C1043_ConfirmedNoPushBinds*)\|(C1043_FinalReportWins*)\|(C1043_AppendPreservesHistory*)\|(C1043_HeaderSnapshotAndGetAgree*)` | V-1,V-2,V-9-V-27,V-30,V-31,R-1-R-4 | all 33 listed, 0 failed/skipped | 33 | 18 |
| CP-12 | S11 | `tests/Antiphon.SessionRunner.Tests -> bin-c1065-cp12/` | final-runner | `/*/*/(WorkspaceParkPublicationTests*)\|(BlockedParkWireTests*)\|(WorkspaceSourceVerificationWireTests*)\|(TerminalSeatReleaseTests*)\|(RunnerWorkspaceServiceTests*)/(C1065_*)\|(Replacement_generation_is_never_released*)\|(Token_for_another_session_is_refused*)\|(Restart_invalidates_volatile_observation_tokens*)\|(Fresh_tail_reads_each_provider*)\|(Unknown_or_partial_tail_never_authorizes_release*)\|(Binding_changes_during_read_refuse_qualification*)\|(Old_turn_end_does_not_qualify_a_new_generation*)\|(Working_remains_protected_after_arbitrary_silence*)\|(Two_observations_require_the_full_safety_margin*)\|(Activity_resets_the_qualification_window*)\|(Unavailable_observation_discards_qualification*)\|(Unknown_backend_custody_refuses_release*)\|(Input_winning_the_gate_invalidates_release*)\|(Conditional_input_invalidates_release*)\|(Release_winning_the_gate_refuses_later_input*)\|(Tail_growth_at_final_check_refuses_signal*)\|(Output_growth_at_signal_boundary_refuses_release*)\|(Kill_failure_retains_manifest_and_capacity*)\|(Duplicate_action_is_idempotent*)\|(Confirmed_exit_forgets_only_the_expected_generation*)\|(Unsupported_capability_never_falls_back_to_force*)\|(Http_and_phone_home_share_conditional_semantics*)\|(Fresh_observation_does_not_publish_duplicate_entries*)\|(Explicit_operator_release_keeps_its_contract*)\|(Publish_pushes_only_own_fast_forward_branch*)\|(Publish_equal_tip_is_not_pushed_and_reports_dirty_tree*)\|(Publish_refuses_an_active_sequencer_before_push*)` | V-3-V-8,V-28,V-29,R-5,R-6 | all 37 listed, 0 failed/skipped | 37 | 10 |
| CP-13 | S11 | `tests/Antiphon.SessionRunner.Tests -> bin-c1065-cp13/` | windows-runner | `/*/*/(WorkspaceParkPublicationTests*)\|(BlockedParkWireTests*)\|(WorkspaceSourceVerificationWireTests*)\|(TerminalSeatReleaseTests*)\|(RunnerWorkspaceServiceTests*)/(C1065_*)\|(Replacement_generation_is_never_released*)\|(Token_for_another_session_is_refused*)\|(Restart_invalidates_volatile_observation_tokens*)\|(Fresh_tail_reads_each_provider*)\|(Unknown_or_partial_tail_never_authorizes_release*)\|(Binding_changes_during_read_refuse_qualification*)\|(Old_turn_end_does_not_qualify_a_new_generation*)\|(Working_remains_protected_after_arbitrary_silence*)\|(Two_observations_require_the_full_safety_margin*)\|(Activity_resets_the_qualification_window*)\|(Unavailable_observation_discards_qualification*)\|(Unknown_backend_custody_refuses_release*)\|(Input_winning_the_gate_invalidates_release*)\|(Conditional_input_invalidates_release*)\|(Release_winning_the_gate_refuses_later_input*)\|(Tail_growth_at_final_check_refuses_signal*)\|(Output_growth_at_signal_boundary_refuses_release*)\|(Kill_failure_retains_manifest_and_capacity*)\|(Duplicate_action_is_idempotent*)\|(Confirmed_exit_forgets_only_the_expected_generation*)\|(Unsupported_capability_never_falls_back_to_force*)\|(Http_and_phone_home_share_conditional_semantics*)\|(Fresh_observation_does_not_publish_duplicate_entries*)\|(Explicit_operator_release_keeps_its_contract*)\|(Publish_pushes_only_own_fast_forward_branch*)\|(Publish_equal_tip_is_not_pushed_and_reports_dirty_tree*)\|(Publish_refuses_an_active_sequencer_before_push*)` | V-3-V-8,V-28,V-29,R-5,R-6 | all 37 listed, 0 failed/skipped | 37 | 18 |

Bootstrap the checkpoint tool through the build-slot gate, then let its row runner
take the build/test slots. For example, for the S1 committed slice:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1065-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1065-driver/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1065-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md --after S1 --expected-source-sha "$(git rev-parse HEAD)"
```

The command resolves the just-committed implementation SHA. For S11 use
`--rows CP-11,CP-12` on Linux and `--rows CP-13` on Windows; do not run all S11
rows on one host. Continue
`wait` while exit=75 and await completion before editing/settling. Exit 4 is not run.
Preserve each tool-produced CHECKPOINT line with executed/passed/failed/skipped counts
and source/build provenance. No unlisted build/test loop or whole-Unit run. Report
any necessary failure-driven rerun with its reason and source commit. Code/Review run
check-evidence-diff.ps1 over the full task range. Cleanup only producer-owned
bin-c1065 outputs through checkpoint cleanup; evidence stays ignored.

Amendment rows: after the S4b commit run `--after S4b` (or `--rows CP-4b`); after the S4c
commit run `--after S4c` (or `--rows CP-4c`). `S4b` and `S4c` are literal After tokens that a
numeric range such as `--after S1-S5` does not select. CP-11/CP-12/CP-13 now include the
two new classes; their estimates are unchanged.

### Cost

Estimated, not measured runtime: ordinary Code V/R floor = **136 minutes**,
the sum of CP-1 through CP-13 (7 + 8 + 9 + 8 + 9 + 9 + 10 + 12 + 9 + 9 + 18 + 10 + 18).
This includes **39 minutes** isolated row builds/setup (3 per row) and
**97 minutes** filtered V/R. Separate checkpoint-driver bootstrap/coverage lint
allowance is **5 minutes**; do not double-count row builds.

Slices remain 30-60 minutes: S1 39 author + 7 check = 46; S2 41+8=49;
S3 43+9=52; S4 42+8=50; S5 43+9=52; S6 39+9=48; S7 44+10=54;
S8 44+12=56; S9 41+9=50; S10 42+9=51; S11 10 evidence + 46 check=56.
Total author/evidence = **428 minutes**; Code floor with bootstrap =
**569 minutes**. If an implementation slice exceeds this bound, split that
slice's commissioning before starting its next boundary; do not silently enlarge its
checkpoint selection or build all fixtures first.

Separate SourceLanding Mutation floor: **200 method-scoped cycles**.
Each literal filter is in its PC row: 60 runner controls (V-3-V-8) at
6 minutes each = **360 minutes**; 140 server controls at 8 minutes each =
**1120 minutes**. Runner cycle allowance = 1 edit/break + 2 red build/run +
1 restoration + 2 green build/run. Server = 1 edit/break + 3 red build/run +
1 restoration + 3 green build/run. Total PC red/restore/green floor =
**1480 minutes**, plus **30 minutes** source binding/restoration audit/reporting =
**1510 minutes Mutation**. No cross-PC build reuse assumed; fresh restored green
is mandatory. Combined Code+Mutation floor = **2079 minutes**, excluding
predecessor implementation, queue delay, repairs and later rollout acceptance.

Narrow grouping replaces 27 separate new-method ordinary builds with 10 slice
builds: at 3 minutes each that saves **51 estimated build minutes** in the
preparatory V/R pass. Final regression/Windows builds and all PCs are retained.
Compared with the provisional 114-minute ordinary plan, the qualified manifest adds
**22 minutes** for its missing regressions, extra capstone and guard scenarios.
No whole-Unit time saving is claimed because this change never required that run.

Design audit: bodies read as listed; **guards=200, mapped=200, missing=0,
duplicate PC maps=0**. Every PC has a compiling mutation, one literal method filter
and a decisive assertion; all are executable after their stated owning-slice setup.
Static coverage findings for absent future tests are not a green claim. Code must
rerun coverage on the integrated implementation and reconcile missing/unmapped
obligations before ordinary Review; SourceLanding Mutation alone proves reachability.

#### TestDesign validation and handoff

The checkpoint tool was built through `scripts/build-slot.ps1` at design commit
`58f88fe49`: lease granted, 6.81 seconds reported by MSBuild, zero errors and one
CS8602 warning in unchanged TaskOwnerGuard.cs. This documentation task ran **zero
V/R tests and zero mutation cycles**. The additional build was solely to run the
required plan importer/coverage lint; it is not a CP execution or implementation
build. Tool source did not change in this task.

Manifest import succeeds with exactly 13 rows. CP-11 and CP-13 each receive the
importer's advisory about their derived 54-minute timeout; their 18-minute estimates
and bounded 31/35-result filters stay within the 56-minute S11 slice. No timeout was
raised. The separate plan reader parses **64 method obligations** (27 new, 37
regression methods) and **200 assertion-label obligations**, with no malformed or
unmapped plan entries. The structural audit finds 200 guards, 200 distinct mapped
PCs, zero missing/duplicate mappings, 13 rows and a 136-minute ordinary sum.

Full `coverage --plan` currently exits **2**, `INPUT_INVALID: unresolved selected
class`, before source/assertion analysis: its first selected class,
BlockedTaskParkStateTests, is future S1 work. The diagnostic path
tests/Shared/TestClassificationMetadata.cs is the tool's last enumerated project
source, not a defect found in that file. This is a known missing implementation,
not clean coverage. The remaining nine new classes and unlanded CARD-0667 methods
also cannot be certified yet. Do not add placeholder tests or alter the reader to
hide this result. Code repeats the literal command after the implementation exists:

```sh
dotnet tools/Antiphon.Checkpoints/bin-c1065-driver/Antiphon.Checkpoints.dll coverage --plan docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md --format text
```

Keep generated import/lint receipts under ignored evidence storage. Next is Code,
commissioned in the dependency order above; S2b through S4b of CARD-0667 must land
before this card's dependent production slices. No human policy decision remains.

#### Amendment validation and cost (2026-10-06, task 444809ee)

Rows CP-4b and CP-4c add **20 estimated minutes** (10 + 10) to the ordinary Code floor and
CP-11/CP-12/CP-13 each grow by two executions at an unchanged estimate, so the ordinary
Code floor becomes **156 minutes** (136 + 20) with isolated row builds now **45 minutes**
(15 rows at 3) and **111 minutes** filtered V/R. Slices: S4b 44 author + 10 check = 54;
S4c 40 + 10 = 50; total author/evidence **532 minutes**; Code floor with bootstrap
**693 minutes**. Mutation adds 17 runner controls (PC-201-PC-217) at 6 minutes =
**102 minutes** and 9 server controls (PC-218-PC-226) at 8 minutes = **72 minutes**:
**226 method-scoped cycles**, PC red/restore/green floor **1654 minutes**, Mutation
**1684 minutes** with the 30-minute audit, combined Code+Mutation floor **2377 minutes**.
Design audit after the amendment: guards=226, mapped=226, missing=0, duplicate PC maps=0;
V-28-V-31 add four one-result methods (server roster 23 new + 10 existing = 33; runner
roster 8 new + 26 + 3 = 37). All new controls are executable once S4b/S4c land; their
absent test files are disclosed, not counted as current evidence.

Importer validation (tool-only, no tests): the checkpoint tool was built through
`scripts/build-slot.ps1` (lease granted, waited 0s, MSBuild 5.57s, 0 errors, the one
pre-existing CS8602 warning in `TaskOwnerGuard.cs`) to the isolated output
`bin-c1065-plan-driver/`, which was removed afterwards. `import --plan <this plan>` exited 0
with `imported 15 rows`; CP-4b and CP-4c import with `after: [S4b]`/`[S4c]`, builds
`bin-c1065-cp4b`/`bin-c1065-cp4c`, expect tokens `WorkspaceSourceVerificationWireTests` +
`BlockedParkWireTests` and `TaskParkRunnerIdentityTests` + `TaskParkPublicationTests`,
`minExecuted: 5`; CP-11 imports with `minExecuted: 33` and CP-12/CP-13 with `37`. The only
warnings are the pre-existing CP-11/CP-13 54-minute derived-timeout advisories at their
unchanged 18-minute estimates. The read-only `coverage --plan` lint exited 2 with
`unresolved selected class`, exactly as the S3/S4 evidence recorded for absent future
classes; it launched no build or test driver. This amendment ran zero V/R tests and zero
mutation cycles. Checkpoint tool source did not change.