# CARD-0459: typed cleanup for settled worktrees and publication residue

Plan task: `b7d6c5c5` (2026-09-19). Source baseline:
`95bf7f420578388d71d51d8202aecc031ed40e41`, including the
[investigation](../../investigations/2026-09-19-card-0459-worktrees-never-get-cleaned-up.md).
The requested investigation branch was already checked out elsewhere; this task uses
that exact commit on `feat/card-task-b7d6c5c5`.

Status: implementation plan complete; **TestDesign is a separate next stage**.
The PC and guard inventory below is input to that stage, not executed verification.
This dispatch changes documentation only and performs no live cleanup or configuration change.

## Outcome and scope

Make the existing daily residue job useful through two explicitly different authorities:

1. Retire an ordinary, genuinely finished task worktree whose caller has released its
   workspace, whose committed work is already contained in the configured remote target,
   and whose current contents pass the existing guarded remover.
2. Retry cleanup of an existing, confirmed publication using its original landing
   operation and the existing receipt-backed cleanup protocol.

Neither path publishes work, invents review approval, discards unique commits, commits
files, deletes ignored outputs in advance, stops processes, or force-removes directories.
CARD-0452 continues to own ignored-content policy. With today's policy a single ignored
file still refuses removal, including all 43 historical ignored-content refusals until
their contents or the shared policy legitimately change.

The 446 never-landed directories are a **candidate population**, not an approved delete
list. The investigation did not establish how many have unique commits, unresolved
handoffs, or dirty contents. Success means deleting the proven-safe subset and accounting
for every remaining candidate with a reason; it does not mean making the census zero.

## Ground truth

Counts below are the investigation's 2026-09-19 observations, not a new live measurement.
Source anchors were checked at the baseline above.

| Card assumption / tempting inference | What the code and evidence actually show | Consequence |
|---|---|---|
| Land never cleans a tree | `AgentTaskLandingProtocol.CleanupAsync` issues typed `Publication` removal. 64 operations completed directory, registration and branch cleanup. | Preserve that path; add discovery/retry, not another land implementation. |
| Code trees need another delete trigger | All 43 refused landing rows say `ignored_content_preserved`; zero Code-role Complete rows. 42 of those refused rows still had directories. | Retrying must preserve the guard and reconcile the one already-absent case from its receipt. |
| Every remaining tree is landing residue | Of 494 `card-task-*` directories, 446 had no active landing row; 130 of those were Code and 169 Review. | Query landing history as well as active pointer before declaring a tree never-landed. |
| Terminal means disposable | `AgentTask` has terminal status, `NextStage`, report/deliverable pointers, base/repair links, and request IDs, but no workspace-release or handoff-consumed receipt. | Add explicit, durable workspace disposition. Do not infer it from age, role, card status, or report prose. |
| Turning Execute on fixes the sweep | `WorktreeResidueSweepService.ClassifyOne` ends in Unknown / Evidence required. `TryRemoveEligibleAsync` uses the untyped remover. | Replace the authority gap end to end before activation. |
| Janitor is a second working safety net | `WorktreeJanitorHostedService` calls `PruneStaleAsync`; that calls an overload which always refuses `typed_removal_authority_required`. | Retire the duplicate scheduled pruning path, keep the untyped overload fail-closed. |
| A filename or latest matching task identifies the owner | `MatchTask` combines path/branch hits and may pick the latest terminal task; scan also infers names for orphan directories. | Discovery hints cannot mint authority. Require one full task identity and exact canonical coordinates. |
| Ignored output is just dirt the scanner can ignore | `InspectResidueAsync` treats `!!` as untracked. `GuardedWorktreeRemoval` independently checks `IgnoredPaths.Length != 0` twice. | Separate inventory facts from policy. Do not let a scanner-specific ignored rule permanently block policy-approved retries. |
| Local ancestor means published | Current scanner uses local `merge-base`; unknown Git reads are not proof. `LandingGit.ObserveAsync` already checks the configured remote destination and containment. | Never-landed retirement requires fresh remote containment; no patch-equivalence or local-only substitute. |
| A typed enum alone is authority | `GuardedWorktreeRemoval.AuthorityAsync` recognizes Publication/LocalMerge; Verification has its own route. `WorktreeRemovalEvidence` rereads committed DB state in a new scope. | Add a distinct retirement receipt and evidence read; do not fabricate a landing or borrow LocalMerge. |
| Existing cleanup journal can represent any delete | `WorktreeCleanupAttempt` has required FKs to `AgentTaskLandRequest` and `AgentTaskLanding`. | Leave publication diagnostics intact; add a small retirement-specific intent/result ledger. |
| A DB recheck alone closes the race | Dispatcher takes a common-directory lease for writers, but ReadOnly dispatch skips it; requeue changes attempt/status and sessions can start independently. | Add a workspace-use reservation shared by retirement and every managed reuse/launch path. |
| The backlog is all ordinary task trees | Census also found unregistered directories, nested baselines, other managed names, and external evidence/verification trees. | Inventory them, retain them. SourceLanding/Mutation cleanup remains exclusively `CleanupVerification`. |

Relevant owners: [orchestration](../../orchestration-loop.md),
[lifecycle](../../agent-card-lifecycle.md), [HTTP operations](../../ops-http.md),
[session invariants](../../session-runtime-invariants.md),
[tests/builds](../../testing-and-build.md), [project conventions](../../project-context.md).

## Decisions

All D-n entries are implementation choices within this brief. No ignored-file policy
decision is delegated to this card; no unanswered decision blocks TestDesign.

- **D-1 — One scheduler.** Keep Hangfire `antiphon:worktree-residue`, daily 10:00
  Europe/London. Remove the `WorktreeJanitorHostedService` registration and retire its
  automatic `PruneStaleAsync` loop. Keep old removal interfaces conservative for other
  callers. Rejected: another Windmill job, Scheduled Task, or two independent schedulers;
  the server already owns the DB, lease, policy and job registration.
- **D-2 — Two request kinds.** Add `SettledTask` removal purpose backed by a retirement
  receipt. Publication retries remain `Publication`, pinned to a confirmed operation.
  Rejected: fake `AlreadyPresent` landings, LocalMerge without a merge, or terminal-status
  flags passed to an untyped delete; they misrepresent the authority and audit trail.
- **D-3 — Full identity and positive preservation proof.** Scope execution to ordinary
  registered leaf `card-task-<8hex>` trees under the configured managed root, with exactly
  one full task owner. Bind repository/common/admin directory, branch, source SHA and
  remote target identity. Require the source commit to be an ancestor of a fresh remote
  target observation. Rejected: short-ID matching alone, remote task-branch existence,
  patch equivalence, or comparing only file trees. Divergent/rebased-only work stays for
  explicit landing/triage through existing workflows.
- **D-4 — Confirm that the workspace is finished.** A caller records `NoFurtherWorkspaceUse`
  for an exact terminal attempt after reviewing its report and pending review/handoff.
  This is workspace release, not permission to discard data. It cannot waive any Git,
  live-owner, content, recovery or identity guard. Historical rows have no implicit
  release. A file-backed batch can record separately reviewed releases by full task ID;
  there is no "release all terminal tasks" mode. Rejected: `NextStage=None`, a Done card,
  a successful Review, or two hours elapsed as sufficient proof on their own.
- **D-5 — No hidden handoff inference.** Outstanding Code/Review/Plan/TestDesign/Land/Decide
  handoffs require caller disposition that names how they were consumed, superseded or
  canceled. Preserve `NextStage` and the original report; attach the disposition alongside
  them. Any open known consumer, merge/commit-recovery obligation or land request blocks
  removal even with a release. Legacy missing stage/report evidence requires explicit
  reviewed disposition, never a guessed default. This makes the 446-tree backlog
  actionable without pretending the database already contains a handoff-completion graph.
- **D-6 — Preserve CARD-0452 exactly.** Both paths reach the same ignored-content policy
  and its final content checks. Do not add an allowlist, force bit, build-folder deletion,
  or private copy of the policy in the sweep. Inventory may display ignored-file counts;
  only the remover decides whether they are protected. Rejected: silently cleaning
  `bin`, `obj`, `node_modules`, `.antiphon` or any pattern before asking the guard.
- **D-7 — Durable intent, bounded retry.** Journal each retirement command intent before
  Git, with a separate attempt identity and independent component outcomes. One ordinary
  worktree-remove command per retirement attempt. Scheduled retries use a persisted
  not-before time of 24 hours after a terminal refusal, independent of server uptime.
  Do not reset a spent command slot on restart. Publication keeps CARD-0443's existing
  bounded diagnostic/retry behavior within each land request.
- **D-8 — Reserve workspace use.** A committed retirement claim excludes new use of that
  exact canonical path/subtree and branch while cleanup is pending or incomplete.
  Before a claim, retry/requeue can atomically invalidate a release. After a deletion
  intent, reuse requires a fresh task/worktree; never relaunch into partially removed
  coordinates. A refused check before any deletion intent releases the execution claim,
  so the caller can revoke the release and reuse the untouched workspace. Rejected:
  a final SELECT without cooperating admission, process killing,
  and holding an EF transaction across slow Git/network commands.
- **D-9 — Preserve publication semantics.** Scheduled cleanup requests explicitly say
  cleanup-only and carry `RequiredLandingOperationId`. Revalidate this both before queueing
  and before protocol/source-resolution execution. A missing/replaced/unconfirmed receipt
  refuses; it cannot fall through to fetch-source/rebase/verify/push. Publication evidence,
  request history and recovery pins retain their existing meanings.
- **D-10 — Durable, bounded reporting.** Store sweep-run summary and per-candidate outcomes
  outside disposable worktrees; queueing is not removal. Store operation/request IDs,
  reasons and component facts, without file contents or raw Git stderr. Scheduled landing
  cleanup uses `ReplyTo=None`/NotRequired, with queryable task events and run results;
  it does not revive an old caller session. Explicit caller Land retains its existing
  notification/receipt contract.
- **D-11 — Staged activation that ends in execution.** Ship with `Execute=false`, retain
  `MinSettledMinutes=120`, add `MaxActionsPerRun=25` and durable fair selection by oldest
  last-evaluation time then ID. Preview and qualify, then the commissioned deployment
  activates `Execute=true`. A complete delivery includes an executing scheduled run;
  leaving the feature permanently report-only is not acceptance. Rollback flips Execute
  off and unregisters the recurring job if needed; it does not erase receipts.
- **D-12 — Deliberate exclusions.** Unregistered leftovers, branch-only discoveries without
  an existing receipt, nested baselines, main/external/evidence trees, Mutation roles,
  SourceLanding bindings and repair-source tasks are inventory-only. Failed unpublished
  landing operations also remain on their existing recovery path. No speculative cleanup
  of sidecars or remote branches is included.

## Proposed records and interfaces

Names below are planned additions, not existing APIs.

`TaskWorktreeRetirement` binds a GUID, schema version, full task ID, task attempt,
terminal status/completion time, report digest (or explicit missing-report disposition),
task revision at release, caller identity/reason/time, handoff disposition, and exact
source/target coordinates. Include source SHA, target ref, remote fingerprint and observed
target SHA; include immutable report/deliverable preservation references. Fields record
release, claim, command-start and component completion separately. A unique active
retirement per task attempt prevents concurrent claims. Release is revocable before the
claim; an interrupted claimed operation remains fenced until reconciliation.

`TaskWorktreeRetirementAttempt` binds retirement ID and sweep-run ID, attempt number,
timestamps, persisted not-before time, command intent ID, command result and directory /
registration / local-branch outcomes. Missing components become success only when
reconciled against this operation's earlier deletion intent and exact identity.
Snapshot the release revision separately from later task concurrency-token changes:
the sweep's own claim/result writes must not invalidate its authority, while attempt,
report, coordinates, handoff or consumer changes must. Do not compare an old release
token to a task token that the cleanup itself just rotated.

`WorktreeResidueRun` and its bounded result rows persist candidate classifications,
retirement IDs or landing operation/request IDs, failures and deferred work. Paginate
the inventory; do not embed hundreds of paths into one log line. Retention cannot prune
tasks, receipts or referenced report evidence needed by incomplete cleanup. Completed
records follow the existing configurable task-retention window, with dependency order.

Extend `WorktreeRemovalRequest` with an optional retirement ID and
`IWorktreeRemovalEvidence` with a fail-closed `ReadRetirementAsync`. Its Infrastructure
implementation opens a fresh scope/AsNoTracking view. Keep the authority predicate in a
concrete policy and external DB/Git/reservation access behind I/O seams; no generic service
interface or second filesystem deleter is needed.

For publication retries add nullable `RequiredLandingOperationId` and an explicit
cleanup-only kind/origin to `AgentTaskLandRequest`; existing rows keep existing behavior.
`AgentTaskLandService.RequestCleanupRetryAsync(taskId, operationId, sweepRunId, ct)` reuses
the land queue, request locking, cleanup journal, terminal event persistence and recovery.
The cleanup-only discriminator must be checked before `LandSourceResolver` and again in
`AgentTaskLandingProtocol`. The discriminator is not public approval to publish.

Expose a scoped preview/status surface and exact-task release:

- `POST /api/agent-tasks/worktree-residue/preview` with project/board scope: inventory only.
- `GET /api/agent-tasks/worktree-residue/runs/{runId}`: durable run/candidate outcomes.
- `POST /api/agent-tasks/{id}/worktree-retirement` with expected task revision, full source
  SHA, report digest, `NoFurtherWorkspaceUse`, disposition reason and optional consumed
  handoff task/evidence IDs: records release after validation; does not delete inline.
- `DELETE /api/agent-tasks/{id}/worktree-retirement/{retirementId}`: revoke only before claim.

The revoke operation is also available after a pre-command refusal has released its
claim; it is unavailable once any deletion intent exists.

Follow existing caller authorization and project/root restrictions; a child task token
cannot release arbitrary other tasks. A dedicated ASCII `scripts/worktree-residue.ps1`
provides preview, status, release/revoke and file-backed exact-ID batches. Reject stale
revisions; report per-item refusals. Never trust paths, common directory, remote fingerprint
or terminal status supplied by the client. Register static routes before `/{id}` routes.

## Selection and execution

### Discovery and preview

Combine managed registrations, exact task rows and **all confirmed incomplete landing
operations**, not just directories returned by the scanner. The latter is necessary for
the refused landing whose directory already disappeared. Load landing/request history,
repair/base/follow-up/parent links, workspace consumers, outstanding commit recovery and
retirement evidence. An ambiguous match yields `identity_ambiguous` and remains retained.

Preview is observational: no release/claim, land enqueue, command intent, ref mutation or
filesystem deletion. It can persist an inventory report. Local ancestry is advisory in
preview; mark remote containment as requiring execution-time refresh rather than claiming
it has been proved. Display lane and independent blockers, including `release_required`,
`handoff_pending`, `live_owner`, `publication_unconfirmed`, `unique_commits`,
`ignored_content_preserved`, `identity_unknown` and `outside_scope`.

### Settled-task lane

1. Require Succeeded/Failed/Canceled, completed time older than the existing floor,
   ordinary Worktree mode, an exact recorded release, no landing history requiring
   resolution, and preserved report/deliverable evidence. Status and age only admit
   consideration; neither supplies removal authority.
2. Acquire the genuine common-directory mutation lease. Under the shared workspace-use
   reservation, reread task/release/consumers and atomically claim retirement. Commit the
   claim before any mutation. Lock order is repository lease, workspace reservation,
   task/retirement rows; never acquire the repository lease while holding those DB rows.
3. Check all live or queued task consumers, including ReadOnly, child/follow-up/repair/base
   users; session CWDs and pooled/standing agent workspaces; pending launch reservations;
   pending merge/commit recovery and landing requests. Compare canonical path containment
   and branch/common-directory identity, not string prefixes. Include descendants and
   nested registrations. Unknown session/backend ownership or repository child journals
   hold; neither a terminal task nor a StopReturned log proves process exit. Cleanup
   never stops an agent. Existing managed runtime evidence is used; missing evidence
   becomes a retained reason, not retroactive SourceLanding custody.
4. Resolve target from task merge target, else project BaseBranch, Git DefaultBranch,
   then master using `WorktreeBaseResolver.ChooseConfiguredDefault`; unresolved target
   refuses (no HEAD fallback for deletion). Bind the configured destination and require
   fresh remote containment of the exact source SHA with `LandingGit.ObserveAsync`.
   Store observations/pins under a retirement-specific namespace, never landing refs.
5. Persist the complete authority snapshot. Call typed `SettledTask` removal. Reuse
   `GuardedWorktreeRemoval`'s canonical identity, clean tracked/untracked/submodule status,
   sequencer, ignored-content and final reread checks. The new authority arm validates
   retirement evidence, current reservation and fresh destination containment. Recheck
   confinement for this purpose even though it has no publication `CleanupContext`.
6. Commit a single command slot before ordinary `git worktree remove -- <exact-path>`.
   After confirmed directory/registration removal, revalidate authority and all checkout
   registrations before exact-old-SHA `update-ref --no-deref -d`. Never delete a changed
   or newly checked-out branch. Persist each component outcome. Keep pins and the workspace
   fence while any component or process outcome is uncertain.
7. Reconcile interruptions with fresh evidence. A spent unknown command is not replayed
   within that attempt. A later bounded attempt requires the same valid release plus
   fresh guards. If path/ref is recreated or identity changes, retain it. When all
   components are confirmed absent, mark Complete and retire operation-owned pins by
   compare-and-delete. Keep the historical path fence against stale session resume;
   future work gets new coordinates, not a recreated old task tree.

Release must cover the caller's use of the workspace, not only the delegate process.
An unresolved handoff cannot be cleared by the sweep; the caller's release records its
reviewed resolution. Reports stored in `AgentTask.Result` survive tree removal. Any
additional report, attachment or deliverable referenced only inside the tree must first
be preserved through existing durable report/deliverable storage or remain a blocker.
Ignored report files remain protected even after copying; copying is not a policy bypass.

### Publication lane

Select only active durable operations with confirmed Landed/AlreadyPresent publication
and incomplete cleanup, whose task/coordinates still satisfy the existing evidence reader.
Do not interpret a legacy Landed event as a receipt. Skip a pending land request and use
the same persisted cooldown/fairness accounting as other actions.

Queue a cleanup-only request bound to the operation. Do not hold the repository lease
while queueing; the existing executor acquires it. At execution, verify the binding before
any source resolution/publication code. Reuse `CleanupAsync`, `WorktreeCleanupJournal`,
CARD-0443 diagnostics and typed Publication removal. Preserve remote re-observation,
recovery-pin validation, both ignored inspections and branch compare-and-delete.

Record `RetryQueued` separately from cleanup Complete/Refused. A `LandingCleanup` event
updates the same operation; there is no second publication or Rebase/Verify stage outcome.
An unchanged ignored refusal remains visible and retryable after the cooldown, including
after a future shared CARD-0452 policy change. Do not gate it permanently as generic Dirty
in the scanner. Missing-directory recovery follows the original receipt's rules; an
unregistered directory that still exists remains protected.

### Reservations and races

Implement the new workspace-use reservation as persisted state coordinated through
short DB transactions, not a process-local lock. Managed admission checks it at task
create/requeue, dispatch (including ReadOnly), and actual session launch/resume reservation.
The final launch path must validate the accepted reservation generation; checking only
when composing a launch spec leaves a gap before adapter start. Retirement sees a
committed launch reservation as a live consumer. Keep existing session-generation and
repository-child journal contracts intact.

Competing release/revoke, requeue, Land, follow-up, repair-source and cleanup operations
have a defined winner under the same reservation. A new consumer before retirement claim
invalidates eligibility; a claim first refuses the consumer without deleting its work.
Card reopen or new handoff evidence invalidates an unclaimed release. Arbitrary external
filesystem edits remain outside managed admission; both fresh inspections and ordinary
non-forcing Git remain mandatory, with uncertainty reported rather than forced away.
Add the card-reopen invalidation in the card lifecycle owner service, using the same
workspace reservation rather than an unfenced post-save notification. TestDesign must
enumerate that producer and the direct session/queued-launch producers; a source search
that finds a common launch helper is not proof that reservation spans adapter start.

## Guard inventory

| Guard | Required observable behavior |
|---|---|
| G-01 Unknown/duplicate task identity, branch-path mismatch, short-ID collision | No typed authorization; files and refs unchanged. |
| G-02 Live task, missing completion time, settling floor | Retained regardless of release age or claimed status. |
| G-03 Missing/stale release, pending handoff or evidence only inside tree | `release_required` / specific evidence blocker; never infer disposition. |
| G-04 Active/queued consumer, ReadOnly review, warm/standing session, uncertain launch/child ownership | Hold, zero removal commands, zero process stops. |
| G-05 Dirty tracked, staged, untracked, submodule, sequencer or unreadable status | Retain actual bytes, including Failed/Canceled tasks. |
| G-06 Source ahead/diverged; stale local remote-tracking ref; unavailable/changed destination | Retain branch/tree; never push or reset to make it eligible. |
| G-07 Any protected ignored file at either inspection | Shared CARD-0452 refusal in both lanes, exact bytes preserved. |
| G-08 Missing/forged lease or receipt, stale tracked EF state | Fresh independent authority read refuses. |
| G-09 Missing/unregistered/locked/prunable tree, reparse escape, nested checkout | No deletion absent the exact lane's resumable receipt; no recursive fallback. |
| G-10 Source ref moves or becomes checked out after directory removal | Branch survives; partial result is durable and not counted Complete. |
| G-11 Mutation/SourceLanding, repair-source, external/main/baseline tree | Inventory-only; cannot borrow another purpose's evidence. |
| G-12 Pending/unpublished/replaced landing operation | Scheduled retry cannot publish or replace an operation. |
| G-13 Duplicate jobs, server crash, intent/result persistence failure | One claim/slot per attempt, retained pins, restart reconciliation, no success inflation. |
| G-14 Execute=false, disabled scheduler, cooldown/budget exhausted | Zero mutating actions; clear deferred counts, fair progress on later runs. |

## Implementation slices

Each slice commits and pushes separately with its real ordinary-verification outcome.
Build must not activate cleanup after only a schema/authority slice. New paths below are
marked `(new)`; migrations are CLI-generated, never hand-authored.

### S1 — Retirement disposition, persistence and scoped operator surface

Files: `server/Domain/Entities/TaskWorktreeRetirement.cs` (new),
`TaskWorktreeRetirementAttempt.cs` (new), `WorktreeResidueRun.cs` (new) in that directory;
`server/Infrastructure/Data/AppDbContext.cs`; generated `server/Migrations/*`;
`server/Application/Dtos/WorktreeRetirementDtos.cs` (new);
`server/Application/Services/TaskWorktreeRetirementService.cs` (new);
`server/Api/Endpoints/AgentTaskEndpoints.cs`; `server/Program.cs`;
`scripts/worktree-residue.ps1` (new); `server/Application/Services/DataRetentionService.cs`.

Implement exact release/revoke and report preservation checks; no deletion yet. Add
read-only run queries and retention dependencies. Tests:
`tests/Antiphon.Tests/Application/TaskWorktreeRetirementTests.cs` (new),
`WorktreeResidueEndpointTests.cs` (new), `DataRetentionServiceTests.cs`, and
`scripts/test-worktree-residue.ps1` (new, HTTP stub and temporary manifests).
Prove authorization, stale revision, handoff disposition, duplicate/idempotent release,
per-item batch failures and no writes/deletion from preview.

### S2 — Workspace-use reservation and typed retirement remover

Files: `server/Application/Dtos/WorktreeRemovalRequest.cs`;
`server/Application/Interfaces/IWorktreeRemovalEvidence.cs`;
`server/Infrastructure/Data/WorktreeRemovalEvidence.cs`;
`server/Infrastructure/Git/GuardedWorktreeRemoval.cs`, `WorktreeManager.cs`;
`server/Application/Services/TaskWorktreeRetirementService.cs`;
new reservation/journal I/O contracts in `server/Application/Interfaces/` and their
implementations in `server/Infrastructure/Data/`; `server/Application/Services/AgentTaskService.cs`,
`AgentTaskDispatcher.cs`, `AgentTaskReplyService.cs`, `AgentTaskLandService.cs`,
`AgentSessionService.cs`, `AgentSessionLaunchQueue.cs`, `CardService.cs`;
`server/Program.cs`.

Keep authority checks purpose-specific while sharing all content/deletion checks. Wire
every relevant creation/requeue/launch reservation before enabling execution. Add
`tests/Antiphon.Tests/Application/WorktreeRetirementRaceTests.cs` and
`tests/Antiphon.Tests/Infrastructure/SettledWorktreeRemovalTests.cs` (new).
Use real Git plus isolated DB for successful retirement, all terminal statuses, consumer
races and partial recovery. Extend `WorktreeRemovalAuthorityTests`/`WorktreeRemovalDefaultTests`
to prove untyped/unknown-purpose refusal remains. Run affected session admission and task
retry tests selected by TestDesign; no change to native custody or prompt delivery.

### S3 — Cleanup-only landing request and recovery

Files: `server/Domain/Entities/AgentTaskLandRequest.cs`;
`server/Domain/Enums/LandingEnums.cs`; `server/Infrastructure/Data/AppDbContext.cs` and
generated migration; `server/Application/Services/AgentTaskLandService.cs`,
`AgentTaskLandingProtocol.cs`; applicable queue/notification projection code only as
needed to carry immutable kind/operation/run identity.

Add `tests/Antiphon.Tests/Application/WorktreeLandingCleanupRetryTests.cs` (new).
Regression classes: `AgentTaskLandCleanupSafetyTests`, `AgentTaskLandPublicationTests`,
`AgentTaskLandRequestTests`, `AgentTaskLandRecoveryTests`, `WorktreeCleanupJournalTests`,
`AgentTaskLandNotificationPersistenceTests`, `AgentTaskLandNotificationRecoveryTests`.
Prove queue recovery, exact operation binding, no source-resolution/push calls, original
publication retained, CARD-0443 intent budget unchanged and no scheduled chat delivery.

### S4 — One effective sweep with durable outcomes

Files: `server/Application/Services/WorktreeResidueSweepService.cs`;
`server/Application/Dtos/WorktreeResidueDtos.cs`;
`server/Application/Settings/WorktreeResidueSettings.cs`, `WorktreeResidueSettingsValidator.cs`;
`server/Infrastructure/Agents/WorktreeResidueJob.cs`;
`server/Infrastructure/Git/WorktreeJanitorHostedService.cs`, `WorktreeManager.cs`;
`server/Program.cs`; `server/appsettings.json`.

Replace display-only matching with conservative lane discovery; include DB-only landing
residue; persist run rows, action budget, cooldown and fair cursor/order. Split ignored
facts from tracked/untracked dirt without copying ignored policy. Remove the duplicate
janitor registration; existing health sweep stays detection-only. Keep Execute false.
Extend `WorktreeResidueSweepTests`; add `WorktreeResidueRecoveryTests` and
`WorktreeResidueRegistrationTests` in `tests/Antiphon.Tests/Application/`.
Do not replace the legacy-event test with a permissive one: add a separate valid-release
fixture that actually deletes, while the original fixture remains kept.

### S5 — Operational contract and deployment acceptance

Files: `docs/orchestration-loop.md`, `docs/ops-http.md`, `docs/antiphon-api.md`,
`docs/testing-and-build.md`, `docs/session-runtime-invariants.md` (reservation boundary only),
and this plan's acceptance evidence links. Update scheduler/settings descriptions so
operators do not try to fix authority by shortening TTL or invoking PruneStaleAsync.

After ordinary Code verification and separate Review, land/deploy through the existing
workflow. Confirm canonical checkout and `/api/version` contain the landed change.
Record a fresh scoped preview with totals per lane/blocker; do not reuse September 19
counts as current eligibility. Have the caller review exact releases for a small clean
subset. Enable execution, run the existing job, inspect real filesystem/registration/ref
outcomes and persisted receipts, then confirm a later actual scheduled run. Record the
43 historical landing rows separately from present directories and retain ignored ones.
Any unique/dirty/ambiguous or nonordinary residue receives an explicit retained disposition;
no cleanup acceptance requires deleting it. Deployment/backlog deletion is a separately
commissioned action, not authorized by this Plan dispatch.

## TestDesign handoff: tests and positive controls

This safety-critical change needs an executable verification design before Code.
TestDesign must finalize method names, fixtures, fault boundaries, expected assertion
failures, variants and ordinary regression selections for each row below. Use scratch
repositories with local bare remotes and isolated DB schemas; no test touches production
worktrees, live broker, production runner or the actual backlog. Process-spawning classes
use the assembly-local `ParallelLimiter<ProcessSpawnLimit>`.

| Proposed PC | Mutation to make | Assertion the dedicated method must fail |
|---|---|---|
| PC-01 | Accept terminal status without exact release/handoff disposition | A clean never-landed tree awaiting review remains present. |
| PC-02 | Reuse release after attempt/revision/report/source identity changes | Old release cannot delete the newer attempt or its bytes. |
| PC-03 | Restore latest-task/path-or-branch heuristic for authority | Conflicting full owners/short prefixes preserve both trees. |
| PC-04 | Omit queued ReadOnly/base/repair/follow-up consumer check | In-use source is held with zero remove calls. |
| PC-05 | Bypass reservation at requeue or final launch admission | Barrier-controlled race has only one winner; no launch into retiring path. |
| PC-06 | Replace remote containment with local ancestry/remote task-branch existence | Unpublished unique commit and remote force-move retain source ref/tree. |
| PC-07 | Skip first ignored check | Protected ignored bytes survive the initial inspection variant. |
| PC-08 | Skip final ignored check | Ignored file introduced between inspections survives. TestDesign must isolate each check so the other does not mask the mutant. |
| PC-09 | Trust tracked EF receipt or omit final independent evidence read | Revoked/changed durable authority prevents deletion. |
| PC-10 | Skip canonical root/admin or nested-registration check | Aliased/outside/nested tree remains untouched. |
| PC-11 | Remove exact-old-SHA branch predicate or checkout recheck | Branch advanced/checked out after tree removal is retained. |
| PC-12 | Let cleanup-only request fall through on missing/replaced operation | No resolver, rebase, verification, push or new publication is observed. |
| PC-13 | Reset spent command intent after service recreation | Crash recovery issues no duplicate removal for that attempt. |
| PC-14 | Count queued/partial cleanup as Removed or use disk scan only | Counts reflect independent component facts; absent-directory receipt is still discovered. |
| PC-15 | Skip Execute/cooldown/action-budget/claim gate | Report-only has zero mutation; duplicate/manual triggers remain bounded and fair. |
| PC-16 | Accept legacy event or Mutation/SourceLanding as ordinary authority | Unknown receipt and excluded-purpose fixtures preserve all content. |
| PC-17 | Route ignored-only rows permanently to scanner Dirty/Keep | The retry reaches the typed remover despite ignored inventory. A recording IWorktreeManager returning an accepted policy result proves routing; real-remover integration still proves today's refusal. Add no production override/injection knob. |
| PC-18 | Drop run/request result persistence or queue recovery | Restart recovers the same operation/request and reports actual outcome without second publication. |

Also require ordinary positive cases: released clean never-landed Succeeded/Failed/Canceled
trees remove all three components; unique/dirty/ignored variants keep exact contents;
released plan with uncontained plan commit remains preserved; confirmed landing cleanup
retry succeeds after a fixture-owned ignored file is deliberately removed; untouched
ignored files keep refusal; repeated Complete operations are idempotent. No test should
claim future CARD-0452 semantics have shipped.

Cover crash cuts before/after release, claim, command intent, Git exit, directory outcome,
branch CAS and terminal result persistence. Include cancellation, Git/DB/backend outage,
already-absent components with/without receipt, inaccessible paths, duplicate job workers,
new pending Land and consumer creation during inspection. Test retained report/artifact
availability and cleanup-record retention, not only row existence.

For changed asynchronous paths, TestDesign supplies the producer/destination/persistence/
recovery/receipt inventory. Scheduled producer -> durable run + pinned land request ->
existing land queue -> original operation's cleanup -> durable run outcome is the new
chain. `ReplyTo=None` must create no caller typing obligation. Explicit manual Land's
existing immutable Outcome and complete UserPrompt receipt are regression requirements;
do not substitute event counts or enqueue acknowledgments for delivery proof.

Ordinary Code/Review follows the current Final profile: build with a producer-owned
alternate `OutputPath=bin-c459/` (forward slash), Unit lane plus the named affected
integration classes and script harness; TestDesign records the final executable command
list and fresh TRX requirements. Run foreground and await completion; do not edit source
during tests. Verify inherited reds by exact methods at the base. Remove only the output
paths produced by those runs after verifying resolved paths stay under the test workspace.
No build or test run was needed for this documentation-only Plan.

Positive controls execute after ordinary Review and confirmed land in the commissioned
SourceLanding Mutation stage. Each red/green cycle uses
`--treenode-filter "/*/*/ClassName/ExactTestMethod"`, with fresh nonzero executed-test
counts and the intended assertion failure. Build/fixture errors or zero tests are not
red. Preserve source-restoration and per-PC evidence externally under the SourceLanding
contract. This plan does not execute PCs or alter verification-snapshot cleanup.

## Completion evidence required from later stages

- Durable release/retirement and cleanup-only publication paths exist, share the current
  content guard, and survive restart without publishing or replaying uncertain deletion.
- Ordinary tests, independent Review and all finalized post-land PCs have their own
  evidence; implementation completion is not inferred from a report-only inventory.
- Deployment proves activation and one subsequent scheduled execution; durable before /
  after counts distinguish candidates, released, held, queued, refused, partial and removed.
- Each cleaned tree has an exact authority and component receipt. Protected unique,
  dirty, ignored, live, unknown and excluded trees remain present with actionable reasons.
- CARD-0452's ignored-content rule is unchanged. A later change to its shared policy
  requires no CARD-0459 bypass or scanner-policy update.
