# CARD-0459: typed cleanup for settled worktrees and publication residue

Plan task: `b7d6c5c5` (2026-09-19). Source baseline:
`95bf7f420578388d71d51d8202aecc031ed40e41`, including the
[investigation](../../investigations/2026-09-19-card-0459-worktrees-never-get-cleaned-up.md).
The requested investigation branch was already checked out elsewhere; this task uses
that exact commit on `feat/card-task-b7d6c5c5`.

Status: implementation plan complete; **TestDesign is a separate next stage**.
The PC and guard inventory below is input to that stage, not executed verification.
This dispatch changes documentation only and performs no live cleanup or configuration change.

Land status (2026-09-22): Code and Review are complete and the reviewed tip
`2240ec35f11c5b0dde8632c2d53abb615e98e1fe` is confirmed green (Unit lane 2705/2705,
cleanup-retry class 17/17) and ready for land. Its landing-owner task `cf214423` cannot
issue that land -- every attempt returns a server 500 because an earlier Conflicted/Aged
land request left an Unconfirmed operation mirror-disagreeing against a stale SHA
(product gap CARD-0603) -- so the reviewed tip is republished unchanged on
`feat/card-task-f87db7ff` for a fresh landing owner. Post-land SourceLanding Mutation
still owns every PC-1..PC-108 cycle.

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


## Verification design

TestDesign task cebd6310, inspected at edbcef111a6c61ebbb2e1b286e7bf6f162fa8bc3.
This section is appended; D-1..D-12 and S1..S5 above are unchanged. The requested
planning branch was already checked out elsewhere; this task uses the exact
requested commit on feat/card-task-cebd6310.

The fourteen original guard groups and eighteen proposed controls were a
requirements index. The final executable inventory is **G-1..G-108 and
PC-1..PC-108 below**. It supersedes only the provisional verification grouping.
Independent admission producers, repeated inspections, publication bindings and
durable handoffs need separate controls. These are test specifications for Code,
not claims that new methods already exist or have run. No build, mutation,
deployment or backlog cleanup was performed in TestDesign.

### Inspection

Paths are repo-relative. The listed executable bodies, setup and assertions were
read before naming the new cases; these are not names inferred from discovery.

| Test/fixture bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| tests/Antiphon.Tests/Application/WorktreeResidueSweepTests.cs: all six tests, SeedWorktreeTaskAsync, Fact, TaskSnap, RecordingResidueWorktrees and logger | Legacy event remains unauthorized; ignored inventory currently conflates dirt; recorder only supports legacy removal -> V-1,V-6,R-1. |
| tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs and LandingGitFixture.cs, including request/drain, independent observer, restart, save/transaction faults, crash worker and disposal | Real bare remote, original receipt and durable observation -> V-3,V-5,V-7. trees/source and full-GUID branch are not valid ordinary retirement setup. |
| Infrastructure/WorktreeRemovalAuthorityTests.cs and WorktreeRemovalDefaultTests.cs; Application/AgentTaskLandCleanupSafetyTests.cs and AgentTaskLandPublicationTests.cs under tests/Antiphon.Tests | Typed/default authority, opaque ignored bytes, target movement, missing components, remote proof refresh -> V-3,R-2,R-3. Build-junk script behavior is separately owned and excluded from the targeted filter. |
| Infrastructure/WorktreeGuardedCleanupTests.cs: content/ignored wrappers and ContentBoundaryAsync, RemovalHarness, diagnostics/probe/clock doubles, slot/budget/lock/branch/no-rescue bodies; Application/WorktreeCleanupJournalTests.cs and Store/interceptors | Initial refusal count isolates the first check; final injection starts clean; publication's two command slots differ from retirement's one -> V-3,V-7,R-4. |
| Application/AgentTaskLandRequestTests.cs and AgentTaskLandRecoveryTests.cs, including seed/service and child crash code | Immutable request IDs, acceptance races, process death and acknowledgement gaps -> V-5,V-7,R-3. |
| Application/AgentTaskLandNotificationPersistenceTests.cs and AgentTaskLandNotificationRecoveryTests.cs, including upgrade, outcome matrix, keyed enqueue and failed-insert code | Atomic owed outcome and immutable destination; several tests stop at insertion -> V-5,R-5, supplemented by recipient evidence. |
| tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs; Fixtures/LandDeliveryFixture.cs initialization/request/receipt/single-prompt/child/restart/outcome/disposal; dispatch fixture producer/receipt setup and LandDeliveryOptions boundary hooks | Real Program, isolated runner, native FakeGrok and complete UserPrompt -> V-11,R-8. Fake provider does not prove model reasoning. |
| Application/AgentSessionLaunchQueueOwnershipTests.cs and OwnershipFixture; TestHelpers/BridgeQueueHarness.cs construction/DI/submission callback/transcript insertion; AgentTaskReuseEnqueueTests refocus/brief-failure and cancellation bodies | Existing ownership is in-process; fake callback synthesizes transcript -> V-4,V-11. Static shared-context calls must not be copied into isolated race tests. |
| Application/CardCorrectionIntegrationTests.cs reopen-facts/no-spawn and BuildHarness; AgentTaskService Retry/shared Requeue call sites; AgentTaskReplyService Answer/Continue/Refine bodies | Reopen and Refine are independent producers; Continue calls Answer -> V-4,R-7. |
| Application/DataRetentionServiceTests.cs terminal task-tree/live/fresh/zero-window, dispatch-intent/notification retention bodies, SeedTaskRowAsync, service/context and cleanup helpers | Whole-tree dependency retention and artifact availability -> V-10,R-6. Existing shared-store tests use unkeyed NotInParallel. |
| Application/AgentTaskLandContractEndpointTests.cs approval/refusal bodies; TestHelpers/LandContractWebAppFactory.cs, AntiphonWebAppFactory.cs, TestDbFixture.cs and DelegationTestServices.cs | Scoped HTTP, refusing runner, disabled workers, cloned DB and correct delegation graph -> V-2,V-8,V-9. |
| Infrastructure/HangfireStartupSafetyTests.cs including residue registration and disabled-worker bodies; WorktreeResidueJob, HangfireConfiguration and Program registrations | Registration is not execution -> V-9,R-7. |
| Application/DelegateScriptLandApprovalTests.cs, DelegateScriptRunner.cs, loopback LandApiStub request/recording code; scripts/test-cleanup-codex-test-residue.ps1 manifest/assertion setup | Actual pwsh, exact JSON/IDs, per-item failures and joined teardown -> V-8. Codex residue storage/execution is not reused. |
| Application/WorktreeBaseSelectionTests.cs default candidate, unresolved-default and probe bodies | Creation's HEAD fallback cannot authorize deletion -> V-2,G-41. |
| GuardedWorktreeRemoval, ILandingGit, fresh-scope WorktreeRemovalEvidence.ReadAsync, land RunRequestAsync, session Start/Attach call sites | Final authority follows final inspection; remote refresh, pre-resolver guard and protocol guard are independent -> V-3..V-5. |

Owner sections read: project conventions, orchestration stages/landing, card
lifecycle, testing/build fast lane, process/database isolation, mutation,
delivery/restoration, and session generation/launch/delivery invariants.

**Missing setup that Code must implement:**

- WorktreeRetirementHarness under tests/Antiphon.Tests/TestHelpers, patterned
  after LandingSafetyHarness: fixture-owned bare remote, canonical repo, ordinary
  registered trees/card-task-<8hex> and feat/card-task-<8hex>, one full task,
  terminal completion older than the floor, durable external report/artifact,
  no landing history or consumers. Publish source to the fixture remote only
  during setup, clear the trace, then release through the real service. Never
  manufacture Publication authority for retirement.
- LandingGit.ObserveAsync and PinAsync currently accept only the landing ref
  prefix. S2 needs the retirement-specific typed namespace described in the
  design, while preserving rejection of arbitrary refs. G-105 covers this seam;
  do not make the fixture pass by placing retirement pins in landing namespaces.
- All contexts, children and observers use the same per-test cloned database
  from CreateIsolatedSchemaAsync. Wire AddDelegationWorktreeGraph and the new
  reservation/journal I/O services. Process-spawning classes carry Integration
  and ParallelLimiter<ProcessSpawnLimit>. Global tests using a shared store take
  unkeyed NotInParallel; never assert global counts from a shared database.
- Add test-only decorators of existing ILandingGit/evidence and the planned
  reservation/journal I/O seams, plus EF save/transaction interceptors.
  TaskCompletionSource barriers identify before-save, after-save/precommit,
  committed/lost-ack, before-command and after-command cuts. Fault target-ref
  observations explicitly; do not accidentally fail source-resolution setup.
  Assert each barrier was reached. No sleeps coordinate races.
- LandingSafetyHarness.RunAsync can initiate a manual request: do not use it to
  start a cleanup-only case. Add RequestCleanupRetryAsync fixture support and
  drain the real AgentTaskLandQueue through its normal worker/service entry,
  reading immutable request/O/RunId from the DB. Restart creates a new provider,
  context and queue, not just another method call on the old objects.
- Race fixture observes the committed reservation from another connection at
  actual adapter StartAsync/AttachAsync entry. Exercise task Create (including
  internal Merge and Commit child creation), shared Requeue, dispatcher writer
  and ReadOnly, Answer/Continue, Refine, Land, CardService.Reopen, AgentControl
  Start/AttachHerdr, direct card start, interactive queue, Resume and interrupted
  attach. A common launch-spec helper is not proof of coverage through start.
- New owned retirement crash worker follows LandingSafetyHarness's parent-owned
  DB/root pattern. Readiness includes boundary and durable identity. Kill only
  that paused worker, wait for exit and I/O, then launch a fresh worker. For
  after-Git cuts the Git child has already exited and been joined. Unknown child
  journal evidence is injected and must hold; never kill an owner to clean.
- WorktreeResidueHostFixture derives from AntiphonWebAppFactory. Keep normal
  hosts' refusing runner and disabled maintenance jobs. Scheduler qualification
  gets private Hangfire storage and a restricted owned worker able to resolve
  only the fixture residue job; test actual Program registration separately.
- New WorktreeResidueScriptTests wraps scripts/test-worktree-residue.ps1, a real
  operator-script invocation against a route-aware loopback stub and temporary
  manifests. Assert exact requests/exit codes, join all children, keep both
  scripts ASCII and parse in Windows PowerShell 5.1 and pwsh. TUnit methods make
  script PCs method-scoped.
- New tests/Antiphon.E2E/WorktreeRetirementDeliveryE2ETests.cs uses the nearest
  LandDeliveryFixture/isolated-runner pattern with ordinary workspace setup.
  Windows, Docker, Git, pwsh, redistributable ConPTY, staged native FakeGrok and
  rebuilt client/dist are required. Missing prerequisites are recorded as failed
  qualification, never skipped success.
- Queue clocks stay live or offset-over-live. Freeze only non-queue service
  clocks; normalize timestamps/generations to PostgreSQL microseconds. Keep
  sentinels and independent readback outside the disposable tree. Fixture
  teardown verifies its exact resolved temporary root and removes owned
  junction links before recursive teardown.

Boundary combinations: cross Succeeded/Failed/Canceled with clean, unique,
tracked, untracked and ignored content; cross both purposes with first/final
content and ignored changes; cross busy/eligible destinations with delivery
faults; cross consumer-first/claim-first for each admission producer. Identity
tuples vary each field individually. Every negative starts from V-1's valid
fixture except its named defect. Do not multiply all Git errors by every role:
shared-policy tests plus the terminal/content matrix cover that independence.
If implementation splits a listed structural tuple/shared predicate into
separate bypassable guards, Code must extend the PC inventory accordingly.

### Delivery inventory

| Producer -> destination | Durable identity / persistence boundary | Recovery / observable receipt |
|---|---|---|
| Release API or exact-ID batch -> later retirement sweep -> run reader | Full TaskId/attempt, retirement ID, release snapshot, claim, attempt/command ID, RunId/candidate | V-1,V-2,V-7,V-8. Fresh-provider GET run after restart agrees with independent directory/registration/ref observations and readable artifacts. Accepted release is not removal. |
| Hangfire/manual job -> sweep -> retirement executor | RunId/candidate/retirement/attempt; action and claim committed before mutation, intent before Git | V-7,V-9. Recover durable pending rows after server/job-storage recreation. Reader sees actual component receipts. Job Succeeded alone is insufficient. |
| Sweep -> cleanup-only land request -> real queue/worker -> original operation -> run reader | RunId/candidate, TaskId, RequestId, RequiredLandingOperationId, cleanup attempt/command IDs | V-5,V-7,V-9. Recover same pinned request, never re-land. Worker-busy and worker-ready cases reach actual outcome; RetryQueued is not Complete. |
| Scheduled completion -> terminal event/run with no session destination | Same request/O/run; request snapshots ReplyTo=None, notification NotRequired with ConfirmedAt=null | V-5 uses old caller busy and eligible. No scheduled queue input or revived session; older manual debt stays unchanged. No UserPrompt is owed for this deliberately absent recipient. |
| Manual Land -> immutable Outcome -> notification worker -> session queue -> caller | Task/Request/O/Event/Notification IDs, SourceLandNotificationId/QueueMessageId, body digest, destination, prompt sequence and baseline | V-5,R-8 start at real producer and end at complete matching UserPrompt and native one-prompt evidence; busy then released and already eligible callers, every fault below. |
| S2 managed admission -> queued launch or existing session -> adapter/runner | Task/attempt, canonical workspace reservation ID/generation, session/accepted StartedAt, queued message ID where input is sent | V-4,V-11. Claim-first refuses before use; consumer-first remains fenced during lost enqueue/unknown start. Allowed dispatch/reply/refine/resume gets complete UserPrompt. In-turn question-tool answers retain existing ToolResult semantics and are excluded from the new UserPrompt acceptance. |

Every handoff below has both before-commit failure and after-commit/lost-ack
coverage; tests recreate the provider/queue. Method suffixes resolve to the
exact C459-prefixed methods in the PC table.

| Handoff | Required recovery / tests |
|---|---|
| Release commit | No release before commit; replay after lost ack returns same ID. T.ReleaseIdentityIsUnique and X crash matrix. |
| Claim commit | Independent committed claim before mutation; one winner across providers. X.ClaimCommittedBeforeIo, W.OneRetirementClaim. |
| Launch reservation -> enqueue -> adapter start | No enqueue/start without commit; lost enqueue retains responsibility; unresolved start excludes retirement. W.LaunchCommitPrecedesEnqueue, W.LaunchIntentPrecedesEnqueue, W.InterruptedAttachFenced. |
| Retirement intent -> Git -> outcome | No command without intent; spent slot never resets; reconcile rather than replay unknown command. X.IntentCommittedBeforeGit, X.SpentSlotSurvivesRestart. |
| Directory/registration -> branch CAS -> terminal retirement | Independent facts; partial retains branch/pins/fence. X.ComponentsAreIndependent, S.BranchDeleteUsesCas. |
| Sweep action -> request -> queue | Failed action commit has no enqueue; lost wakeup recovers same kind/O/RunId. X.RunIntentPrecedesEnqueue, L.LostWakeupRecoversSameRequest. |
| Cleanup terminal -> run projection | Restart repairs result projection without resolver/push. X.TerminalProjectionRecovers. |
| Manual terminal/Outcome -> enqueue | Atomic owed pair; enqueue-before-insert and insert-rollback faults separately recover to recipient. L.OutcomeObligationIsAtomic, L.EnqueueFailureRemainsOwed; R-8 V25/V31. |
| Keyed insert -> notification link -> flush | Same queue row after lost link; idle recovery without new caller input; busy remains owed. L.QueueIdentitySurvivesLostLink, L.IdleReceiptRecoversLostFlush; R-8 V26/V28. |
| Transport -> transcript -> verdict/receipt | Sent, transport ack, partial prompt, wrong recipient and old baseline cannot confirm. Persisted full prompt recovers without retyping. L.CompletePromptIsRequired, L.ReceiptFailureNeverRetypes; R-8 V30/V32. |

Add X.C459_WorkerDeathAtEveryRetirementHandoff with arguments release-before,
release-after, claim-before, claim-after, intent-before, intent-after, git-exit,
directory-result, registration-result, branch-cas, terminal-before,
terminal-after and run-projection. Before variants kill before commit and
require rollback; after variants independently observe commit before killing.
A DB exception is not reported as OS-process-death evidence.

Substitutes: real-Git service tests prove policy/component/persistence behavior,
not native delivery. Recording typed-manager success proves ignored-row routing
only, never permission to remove today's ignored contents. BridgeQueueHarness
uses a real queue but synthesizes transcript rows in the fake adapter callback;
it supports deterministic PCs, not native-input acceptance. Native E2E supplies
that evidence. In-memory Hangfire proves wiring/execution, not production
scheduler survival; DB restart tests and later commissioned scheduled execution
cover those separately. Review rejects acceptance ending at request, queue,
business event, Sent or transport acknowledgement.

### Proves it works now

All new methods here are implemented by Code. Exact guard methods are ordinary
V/R tests as well as PC targets; aliases below identify their classes.

- V-1: Released clean never-landed Succeeded/Failed/Canceled tasks retire |
  real Git/PostgreSQL | T.C459_ReleasedTerminalTasksRetire |
  all three components absent, remote unchanged, Result/artifacts readable,
  Complete retirement and zero landing rows. Include ordinary Code/Review/Plan,
  consumed handoffs and repeat Complete.
- V-2: Release/revoke/upgrade | service/DB | T class |
  exact durable disposition, snapshot and configured target precedence;
  idempotency, no legacy backfill, own progress preserves release validity.
- V-3: Unsafe content/identity/publication proof remains protected |
  real Git/DB plus I/O faults | S class |
  named refusal before forbidden command, unchanged bytes/index/refs and honest
  partial component outcomes, for both typed lanes.
- V-4: Workspace races have one winner | real DB/service entrypoints and recording
  adapter | W class | consumer-first blocks claim, claim-first blocks use;
  final Start/Attach sees accepted reservation; no stop or transaction over Git.
- V-5: Cleanup-only retry reaches original operation outcome |
  real queue/Git/DB, controlled recipient | L class and
  L.C459_ConfirmedCleanupRetryCompletes |
  same Landed/AlreadyPresent O, no resolver/rebase/verifier/push; initially ignored
  refusal succeeds only after test explicitly removes its own sentinel;
  untouched ignored contents still refuse; worker-busy and worker-ready.
- V-6: Bounded fair sweep and honest inventory | service/DB |
  U class | both lanes, DB-only residue, shared action budget, durable cooldown,
  ignored routing separate from policy, no queued/partial success inflation.
- V-7: Crash/failure recovery | Git/PostgreSQL/owned child |
  X class including C459_WorkerDeathAtEveryRetirementHandoff |
  same identities, one spent command slot, preserved partial evidence and
  recoverable run projection.
- V-8: Operator surface | real HTTP and real pwsh/loopback stub |
  E class, C class and pwsh -NoProfile -File scripts/test-worktree-residue.ps1 |
  scoped exact requests, no preview mutation, durable GET, explicit batch failures.
- V-9: Scheduled execution | private Hangfire worker/DB/Git |
  J.C459_ScheduledWorkerExecutesBothLanes and J guard methods |
  enqueue registered job through Hangfire, drain real land worker, GET actual
  two-lane results and inspect components; recreate storage/provider and run
  idempotently; exactly one daily London registration; disabled remains inert.
- V-10: Evidence retention/expiry | PostgreSQL/artifact store |
  D.C459_IncompleteRetirementRetainsEvidence and
  D.C459_CompletedRetirementExpiresInDependencyOrder |
  incomplete evidence readable past retention, completed unrelated rows expire
  in FK-safe order, zero retention window disables pruning.
- V-11: Normal admitted work reaches recipient | native isolated runner |
  WorktreeRetirementDeliveryE2ETests.C459_AdmittedWorkspaceInputReachesRecipient
  and C459_LaunchReservationRecoveryRetainsOwnership |
  dispatcher writer/ReadOnly, blocked Answer/Continue, queued/running Refine,
  direct card start, interactive start and explicit resume; existing recipients
  busy then released and already eligible; fresh launches Starting through
  launch completion. Whole expected UserPrompt after baseline, correct
  session/generation, one native submission. Lost launch enqueue/unknown start
  retains reservation until recovery; failed queue insertion for existing-live
  work must not free its workspace. No new reply-event-to-queue recovery promise.
- V-12: Activation and next actual scheduled run | separately commissioned
  operations | S5 procedure | canonical checkout/API version contain landed
  change; fresh scoped preview and exact caller releases; executing run and a
  later actual 10:00 Europe/London run with component receipts. Code/TestDesign
  does not have authority to perform this production deletion.

### Guards the regression

- R-1: Legacy event is not authority |
  WorktreeResidueSweepTests.execute_never_treats_legacy_landed_event_as_cleanup_authority |
  zero removed, both refs/trees present. Add positive release fixture separately.
- R-2: Untyped/default/unknown authority refuses |
  WorktreeRemovalAuthorityTests.C448_V24_LegacyRemovalCannotEraseTaskContents,
  WorktreeRemovalDefaultTests, and
  AgentTaskLandCleanupSafetyTests.C448_V36_UnknownPurposeCannotBorrowPublicationReceipt |
  exact bytes and refs, zero legacy delete. Add SettledTask to default-purpose arguments.
- R-3: Existing publication/request/recovery |
  AgentTaskLandCleanupSafetyTests, AgentTaskLandPublicationTests,
  AgentTaskLandRequestTests, AgentTaskLandRecoveryTests |
  original publication facts, fresh proof, no duplicate publication or unsafe cleanup.
- R-4: CARD-0443 journal/budget |
  WorktreeGuardedCleanupTests, WorktreeCleanupJournalTests |
  maximum two publication slots, capture committed before retry, independent
  component facts. New retirement's one slot cannot reset/borrow these slots.
- R-5: Manual notification persistence |
  AgentTaskLandNotificationPersistenceTests, AgentTaskLandNotificationRecoveryTests |
  one event/obligation, immutable destination/body/digest, keyed recovery.
  V-5/R-8 additionally require recipient evidence.
- R-6: Existing task-tree retention |
  DataRetentionServiceTests methods
  A_fully_terminal_stale_tree_loses_every_row_and_its_events,
  A_tree_with_one_live_member_survives_entirely,
  A_tree_whose_newest_row_is_within_the_window_survives_entirely,
  A_zero_task_window_skips_tasks, C508_IntentTaskTreeRetention and
  C508_NotificationTaskTreeRetention |
  whole-tree boundaries preserved; unrelated eligible tree still prunes.
- R-7: Launch/reopen/scheduler |
  AgentSessionLaunchQueueOwnershipTests methods
  Owns_is_true_from_enqueue_until_the_launch_settles,
  ResumeInterrupted_registers_before_running_and_a_second_call_is_a_noop,
  A_faulted_resume_still_releases_ownership and
  Queued_launch_carries_the_explicit_accepted_generation_and_never_re_reads_a_replaced_row;
  CardCorrectionIntegrationTests.Reopen_writes_one_revision_with_the_superseded_terminal_facts_and_clears_them
  and Reopen_defaults_to_the_backlog_column_and_never_spawns;
  HangfireStartupSafetyTests |
  exact generation, correct ownership lifetime, reopen without spawn and disabled
  production workers.
- R-8: Native manual Land delivery |
  AgentTaskLandDeliveryE2ETests exact methods
  C467_V22_AlreadyIdleGetsOutcomeWithoutNewInput,
  C467_V23_BusyCallerDoesNotBlockAnotherLand,
  C467_V25_HardCrashAfterOutcomeCommitRecoversReceipt,
  C467_V26_HardCrashAfterQueueInsertReusesRow,
  C467_V27_LostRequestWakeupRecoversAtBoot,
  C467_V28_LostFlushWakeupRecoversOnIdleCaller,
  C467_V29_RealOutcomeProducerMatrix,
  C467_V30_ReceiptSaveFailureNeverRetypes,
  C467_V31_EnqueueFailureRecoversAutomatically,
  C467_V32_StatusPollingCannotDischargeUnreceivedOutcome |
  complete matching UserPrompt linked to immutable notification and native
  one-prompt evidence; busy remains owed, each crash/failure recovers.

### Guard inventory

Aliases below are space-saving notation. New files use their class name plus
.cs in the indicated tests/Antiphon.Tests directory. E2E methods above use
tests/Antiphon.E2E. All 108 guards have distinct controls; none is omitted.
The D-6 shared ignored policy is tested, not changed.

| Alias | Directory / class |
|---|---|
| T | Application / TaskWorktreeRetirementTests (new) |
| E | Application / WorktreeResidueEndpointTests (new) |
| W | Application / WorktreeRetirementRaceTests (new) |
| S | Infrastructure / SettledWorktreeRemovalTests (new) |
| L | Application / WorktreeLandingCleanupRetryTests (new) |
| U | Application / WorktreeResidueSweepTests (extend) |
| X | Application / WorktreeResidueRecoveryTests (new) |
| J | Application / WorktreeResidueRegistrationTests (new) |
| D | Application / DataRetentionServiceTests (extend) |
| C | Application / WorktreeResidueScriptTests (new) |

| Guard | Plan reference and safety-critical invariant | PC |
|---|---|---|
| G-1 | D-3 / original G-01: Authority requires one full task owner. | PC-1 |
| G-2 | D-3 / original G-01,G-08: Removal coordinates match the exact canonical identity tuple. | PC-2 |
| G-3 | settled lane 1 / original G-02: Only terminal task statuses admit retirement. | PC-3 |
| G-4 | settled lane 1 / original G-02: Completion exists and meets the settling floor. | PC-4 |
| G-5 | D-4 / original G-03: A durable explicit release is required. | PC-5 |
| G-6 | records / original G-03: Release binds an immutable attempt/revision/report/source/handoff tuple. | PC-6 |
| G-7 | D-5 / original G-03: Pending or undocumented handoffs cannot be guessed consumed. | PC-7 |
| G-8 | settled lane report paragraph / original G-03: Referenced reports and deliverables remain retrievable outside the tree. | PC-8 |
| G-9 | S1 operator surface: Caller scope protects release/status. | PC-9 |
| G-10 | D-8 / revoke contract: Revocation cannot dismantle a claimed or spent retirement. | PC-10 |
| G-11 | S1 retention: Incomplete cleanup retains its authority and referenced evidence. | PC-11 |
| G-12 | D-8 / creation producers: Task creation reserves borrowed workspace use. | PC-12 |
| G-13 | D-8 / requeue producers: All requeue paths invalidate an unclaimed release and honor a claim. | PC-13 |
| G-14 | D-8 / reply producers: Answer/continue admission cannot use a retired workspace. | PC-14 |
| G-15 | D-8 / dispatch: Writer dispatch revalidates persisted workspace reservation. | PC-15 |
| G-16 | D-8 / ReadOnly dispatch: ReadOnly dispatch participates despite skipping the Git mutation lease. | PC-16 |
| G-17 | D-8 / Land producer: Land admission competes with retirement under the same reservation. | PC-17 |
| G-18 | reservations paragraph / CardService: Card reopen invalidates release atomically. | PC-18 |
| G-19 | S2 / AgentSessionService.StartAsync: Direct card launch is fenced through adapter start. | PC-19 |
| G-20 | S2 / LaunchInteractiveAsync: Interactive/standing launch is fenced through adapter start. | PC-20 |
| G-21 | S2 / ResumeAsync: Explicit session resume cannot enter retired coordinates. | PC-21 |
| G-22 | S2 / ResumeInterruptedLaunchAsync: Restart attachment also honors retirement fence. | PC-22 |
| G-23 | D-8 / queue generation: Queued launch carries and verifies accepted reservation generation. | PC-23 |
| G-24 | D-8 / launch handoff: Launch reservation lasts through unresolved adapter start. | PC-24 |
| G-25 | D-8 / original G-13: One persisted retirement claimant wins across providers. | PC-25 |
| G-26 | D-8 / settled lane 7: Historical fence rejects stale reuse after complete cleanup. | PC-26 |
| G-27 | settled lane 3 / original G-04: All queued/live task consumers hold retirement. | PC-27 |
| G-28 | settled lane 3 / original G-04: Session and warm/standing workspace owners hold retirement. | PC-28 |
| G-29 | settled lane 3 / original G-04: Unknown runtime ownership is not proof of exit. | PC-29 |
| G-30 | settled lane 3 / original G-04: Unresolved repository-child ownership holds cleanup. | PC-30 |
| G-31 | D-5 / original G-04: Open commit/merge recovery and unresolved land history block retirement. | PC-31 |
| G-32 | settled lane 5 / original G-09: SettledTask checks canonical managed-root confinement without CleanupContext. | PC-32 |
| G-33 | D-3 / original G-09: Nested registrations prevent leaf-tree removal. | PC-33 |
| G-34 | original G-09: Present unregistered or unreadable registration state never means safe absence. | PC-34 |
| G-35 | original G-09: Locked registration refuses before remove. | PC-35 |
| G-36 | original G-09: Prunable registration is retained. | PC-36 |
| G-37 | D-12 / original G-11: Mutation role is excluded independently of SourceLanding. | PC-37 |
| G-38 | D-12 / original G-11: SourceLanding binding cannot borrow ordinary authority. | PC-38 |
| G-39 | D-12 / original G-11: Repair-source task cannot retire its owner's workspace. | PC-39 |
| G-40 | D-12 / original G-11: Nonordinary discoveries remain inventory-only. | PC-40 |
| G-41 | settled lane 4: Target selection has configured precedence and no unresolved HEAD fallback. | PC-41 |
| G-42 | D-3 / original G-06: Exact source must be contained in the fresh configured remote target. | PC-42 |
| G-43 | D-3 / original G-06: Remote endpoint/ref fingerprint stays bound. | PC-43 |
| G-44 | publication lane / original G-06: Remote proof is refreshed immediately before directory cleanup. | PC-44 |
| G-45 | settled lane 6 / original G-06: Remote proof is refreshed before branch removal. | PC-45 |
| G-46 | original G-08: Lease must be genuinely owned for the canonical common directory. | PC-46 |
| G-47 | original G-08: Authority comes from a fresh committed evidence scope. | PC-47 |
| G-48 | settled lane 5 / original G-08: Final independent authority read follows final content inspection. | PC-48 |
| G-49 | S2 / original G-08,G-11: Unknown or mismatched typed purposes cannot borrow retirement authority. | PC-49 |
| G-50 | original G-05: First content inspection must accept clean tracked/index/untracked/submodule/sequencer state. | PC-50 |
| G-51 | original G-05: Second content inspection catches new work. | PC-51 |
| G-52 | D-6 / original G-07 / proposed PC-07: First ignored-content policy boundary remains mandatory. | PC-52 |
| G-53 | D-6 / original G-07 / proposed PC-08: Final ignored-content policy boundary catches newly introduced bytes. | PC-53 |
| G-54 | D-7 / original G-09: Absent components need the same operation's prior deletion intent. | PC-54 |
| G-55 | settled lane 7 / original G-09: Recreated coordinates do not inherit prior deletion authority. | PC-55 |
| G-56 | settled lane 6 / original G-10: Branch SHA is checked before attempting deletion. | PC-56 |
| G-57 | settled lane 6 / original G-10: Branch delete is an exact-old-SHA CAS. | PC-57 |
| G-58 | settled lane 6 / original G-10: All checkouts are reread before branch deletion. | PC-58 |
| G-59 | D-7 / settled lane 6: Uncertain or partial cleanup retains operation pins and fence. | PC-59 |
| G-60 | settled lane 7: Only unchanged operation-owned pins are retired. | PC-60 |
| G-61 | D-8 / original G-13: Claim must be durably committed before mutation. | PC-61 |
| G-62 | D-7 / original G-13: Command intent is committed before Git execution. | PC-62 |
| G-63 | D-7 / original G-13: Spent retirement command slot survives restart. | PC-63 |
| G-64 | D-7 / original G-13: Component facts commit independently and unknown results stay unknown. | PC-64 |
| G-65 | D-7,D-8 / original G-13: Cancellation and I/O uncertainty preserve claimed recovery state. | PC-65 |
| G-66 | D-10 / asynchronous handoff: Sweep action identity persists before land enqueue. | PC-66 |
| G-67 | D-9 / original G-12: Cleanup retry admission requires exact confirmed active operation. | PC-67 |
| G-68 | D-9 / original G-12: Execution checks binding before source resolution. | PC-68 |
| G-69 | D-9 / original G-12: Protocol independently refuses cleanup-only fallback. | PC-69 |
| G-70 | D-10 / delivery: Scheduled request snapshots ReplyTo=None without changing task/manual obligations. | PC-70 |
| G-71 | D-9 / original G-12: Cleanup retry preserves original publication and emits only cleanup outcome. | PC-71 |
| G-72 | discovery / proposed PC-14: DB-only confirmed residue is discovered without a directory scan hit. | PC-72 |
| G-73 | D-6 / proposed PC-17: Ignored inventory does not permanently bypass typed policy evaluation. | PC-73 |
| G-74 | D-10 / proposed PC-14: Queued/refused/partial actions are never counted removed. | PC-74 |
| G-75 | D-11 / original G-14: Execute=false forbids all mutation while retaining inventory. | PC-75 |
| G-76 | D-1,D-11 / original G-14: Disabled scheduler registers and executes no residue job. | PC-76 |
| G-77 | D-1 / original G-14: Exactly one scheduler owns cleanup. | PC-77 |
| G-78 | D-7 / original G-14: 24-hour refusal cooldown is durable. | PC-78 |
| G-79 | D-11 / original G-14: Per-run action budget bounds both lanes together. | PC-79 |
| G-80 | D-11 / original G-14: Fair persisted ordering prevents poison-head starvation. | PC-80 |
| G-81 | D-10 / observable outcome: Run query reads durable outcomes after producer restart. | PC-81 |
| G-82 | D-7,D-9 / delivery handoff: Lost land enqueue is recovered from same durable request. | PC-82 |
| G-83 | D-10 / delivery handoff: Terminal operation reconciles a lost run-result update. | PC-83 |
| G-84 | D-10: Durable reports contain no opaque Git/file content. | PC-84 |
| G-85 | D-10 / manual delivery regression: Manual destination/body remain immutable across scheduled work. | PC-85 |
| G-86 | D-10 / manual delivery recovery: Terminal manual event and outcome obligation commit atomically. | PC-86 |
| G-87 | D-10 / queue handoff: Notification keyed queue identity is recovered without duplication. | PC-87 |
| G-88 | D-10 / recipient verdict: Only complete matching UserPrompt confirms delivery. | PC-88 |
| G-89 | D-10 / flush handoff: An already eligible recipient is reached after lost flush wakeup. | PC-89 |
| G-90 | D-10 / recipient-to-receipt recovery: Receipt-save failure recovers by confirming without typing twice. | PC-90 |
| G-91 | D-10 / enqueue failure: Enqueue failures remain owed and retry automatically. | PC-91 |
| G-92 | D-7 / records: Release identity is idempotent and unique for a task attempt. | PC-92 |
| G-93 | S1 migration / D-4: Migration never fabricates historical workspace releases. | PC-93 |
| G-94 | S1 operator script: File batches preserve exact reviewed full IDs and per-item failures. | PC-94 |
| G-95 | discovery / preview: Preview is observational even with executing service settings. | PC-95 |
| G-96 | D-8 / original G-04: Cleanup never stops a workspace owner. | PC-96 |
| G-97 | D-6,D-7 / original G-05,G-09: Removal never falls back to force/recursive deletion. | PC-97 |
| G-98 | records / snapshot revision: Cleanup's own progress writes do not invalidate a valid release. | PC-98 |
| G-99 | D-8 / lock order: Slow Git/remote I/O does not hold admission row transactions. | PC-99 |
| G-100 | D-8 / launch handoff: Launch intent commits before enqueue or adapter start. | PC-100 |
| G-101 | D-8 / refinement producer: Refinement admission observes the workspace fence. | PC-101 |
| G-102 | D-8 / internal task producer: Automatic merge task creation reserves the borrowed worktree. | PC-102 |
| G-103 | D-8 / internal task producer: Commit-recovery task creation reserves its source workspace. | PC-103 |
| G-104 | D-8 / direct attachment producer: Managed Herdr attachment cannot adopt retired coordinates. | PC-104 |
| G-105 | settled lane 4 / retirement-specific pins: Retirement observation and pin refs stay in their own typed namespace. | PC-105 |
| G-106 | S1 release acceptance: Release validates the caller-reviewed revision/source/report tuple against current server state. | PC-106 |
| G-107 | D-10 / bounded reporting: Run inventory and result responses obey their configured page bound. | PC-107 |
| G-108 | S2 / interface default authority: The new SettledTask purpose remains fail-closed in an unimplemented IWorktreeManager default. | PC-108 |

### Positive controls

For each row: **break its G-n by the specified compiling production defect;
expect the exact method red at the named assertion; restore and rebuild; expect
that method green.** These are 108 distinct controls, not eighteen aggregate
mutation runs. Code implements all targets and runs ordinary V/R. Ordinary
Review judges the tests and pending mutation design before land. Commissioned
post-land SourceLanding Mutation performs and reports break/red/restore/green,
with per-PC evidence stored in the assigned external verification root.

Every method below has the exact C459_ prefix. Arguments in the setup column
are mandatory cases in that one method. A PC selects only that exact method;
the TRX must name the failing assertion and affected argument. Controls may
share fixture helpers but never a PC identity. Setup/build failure or zero
executed cases is not red.

Isolation matters: exercise release/admission guards at the service boundary
where the action is accepted, before later deletion guards could mask them.
For lower remover guards, obtain a fully valid typed request then change only
the named fact. A test-only I/O decorator can hold downstream observations
constant; it must not stub the policy being mutated. Crash/inspection changes
are triggered from the preceding completed boundary, not from the guard call a
mutant removes. For first content/ignored checks assert immediate refusal at
inspectionCalls==1, preserving sentinels: a second check catching the defect
does not mask this assertion. Final-check tests start clean and introduce bytes
only before the second inspection, asserting zero remove calls even if Git
itself would refuse. Branch precheck and old-SHA CAS have separate tests and
separate mutations; likewise all three cleanup-only publication bindings.

| PC / guard | Exact test method | Setup / variants | Compiling defect; decisive red assertion |
|---|---|---|---|
| PC-1 / G-1 | `TaskWorktreeRetirementTests.C459_UniqueFullOwner` | Use unknown owner, duplicate path owners and colliding eight-character prefixes with otherwise valid releases. | Break: replace unique-owner refusal with latest candidate selection; expect `authorized.Count.ShouldBe(0)`. |
| PC-2 / G-2 | `SettledWorktreeRemovalTests.C459_CoordinatesMatchReceipt` | Change task ID, repo, path, branch, common/admin directory or source SHA one at a time after claim; a sibling-prefix path is distinct. | Break: return true from the retirement coordinate tuple equality check; expect `removeCalls.ShouldBe(0)`. |
| PC-3 / G-3 | `TaskWorktreeRetirementTests.C459_TerminalRequired` | Queued, Dispatched, Working and Blocked versus Succeeded/Failed/Canceled, all with old completion and release evidence. | Break: remove the terminal-status predicate; expect `accepted.ShouldBeFalse()`. |
| PC-4 / G-4 | `TaskWorktreeRetirementTests.C459_SettlingFloor` | Null, 120 minutes minus one microsecond, exactly 120 minutes, and plus one microsecond using DB-precision time. | Break: make completion-age eligibility always true; expect `accepted.ShouldBeFalse() for null and below-floor`. |
| PC-5 / G-5 | `TaskWorktreeRetirementTests.C459_ReleaseRequired` | Clean contained old task, NextStage=None or a Done card, with no release. | Break: treat a missing release as NoFurtherWorkspaceUse; expect `removeCalls.ShouldBe(0)`. |
| PC-6 / G-6 | `TaskWorktreeRetirementTests.C459_ReleaseSnapshotIsExact` | Change each tuple field independently; unchanged tuple with cleanup-owned concurrency-token rotation remains valid. | Break: skip structural equality between released and current authorization snapshot; expect `staleReleaseAccepted.ShouldBeFalse()`. |
| PC-7 / G-7 | `TaskWorktreeRetirementTests.C459_HandoffDispositionRequired` | Each Code/Review/Plan/TestDesign/Land/Decide handoff; missing legacy stage/report evidence; consumed/superseded/canceled dispositions; one known consumer still open. | Break: return resolved for an absent or incomplete handoff disposition; expect `removeCalls.ShouldBe(0)`. |
| PC-8 / G-8 | `TaskWorktreeRetirementTests.C459_ArtifactsPreserved` | Inline Result, external report and attachment readback succeed; tree-only, missing or digest-mismatched artifacts block. | Break: omit the durable artifact availability check; expect `removeCalls.ShouldBe(0)`. |
| PC-9 / G-9 | `WorktreeResidueEndpointTests.C459_CallerScopeEnforced` | Authorized parent succeeds; child releasing sibling, other project/root and scoped run read fail. | Break: skip the caller scope authorization check; expect `response.StatusCode.ShouldBe(HttpStatusCode.Forbidden)`. |
| PC-10 / G-10 | `TaskWorktreeRetirementTests.C459_RevokeRespectsIntent` | Before claim allowed; active claim refused; pre-command refusal releases claim and allows revoke; intent/partial/complete forbid it. | Break: allow revoke when claim or deletion intent is present; expect `revokeAccepted.ShouldBeFalse()`. |
| PC-11 / G-11 | `DataRetentionServiceTests.C459_IncompleteRetirementRetainsEvidence` | At retention+1 day keep task tree, retirement, attempts, run links and readable artifact; unrelated eligible task prunes. | Break: remove retirement dependencies from the task-retention exclusion; expect `retainedTask.ShouldNotBeNull()`. |
| PC-12 / G-12 | `WorktreeRetirementRaceTests.C459_CreateReservesWorkspace` | AgentTaskService.CreateAsync ordinary child, base, follow-up and repair-source requests target a claimed subtree/branch. | Break: omit reservation admission in task creation; expect `acceptedConsumers.ShouldBe(0)`. |
| PC-13 / G-13 | `WorktreeRetirementRaceTests.C459_RequeueReservesWorkspace` | RetryAsync, RerouteAsync, EscalateAsync and wall reroute, each consumer-first and claim-first. | Break: remove the shared requeue reservation admission; expect `claimFirst.Requeued.ShouldBeFalse()`. |
| PC-14 / G-14 | `WorktreeRetirementRaceTests.C459_AnswerReservesWorkspace` | AnswerAsync (Blocked and open-question variants) and ContinueWithAuthorityAsync through its AnswerAsync call; claim at barrier before state/enqueue; an existing valid live reservation wins against retirement. | Break: omit AnswerAsync workspace admission; expect `answerAdmissions.ShouldBe(0) for claim-first`. |
| PC-15 / G-15 | `WorktreeRetirementRaceTests.C459_WriterDispatchReservesWorkspace` | Create queued task before release, then claim after selection but before dispatch commit; reverse ordering also. | Break: omit writer dispatch's reservation generation comparison; expect `dispatchCommitted.ShouldBeFalse()`. |
| PC-16 / G-16 | `WorktreeRetirementRaceTests.C459_ReadOnlyDispatchReservesWorkspace` | Queued ReadOnly Review/base consumer and claim-first dispatch barrier. | Break: skip reservation validation only for ReadOnly; expect `adapterStarts.ShouldBe(0)`. |
| PC-17 / G-17 | `WorktreeRetirementRaceTests.C459_LandReservesWorkspace` | Pending explicit Land before claim blocks retirement; claim first refuses new Land, with no request side effects. | Break: omit Land's workspace reservation check; expect `newPendingRequests.ShouldBe(0)`. |
| PC-18 / G-18 | `WorktreeRetirementRaceTests.C459_CardReopenInvalidatesRelease` | CardService.ReopenAsync versus claim, in both orders and with a rolled-back reopen. | Break: move release invalidation after commit and omit it; expect `unclaimedReleaseStillValid.ShouldBeFalse()`. |
| PC-19 / G-19 | `WorktreeRetirementRaceTests.C459_DirectStartFenced` | Pause after spec composition, race retirement, resume to adapter.StartAsync; persisted launch-first consumer blocks claim. | Break: omit the final workspace admission on StartAsync; expect `adapterStarts.ShouldBe(0) for claim-first`. |
| PC-20 / G-20 | `WorktreeRetirementRaceTests.C459_InteractiveStartFenced` | Drive AgentControlService.StartAsync and dispatcher-created session through EnqueueInteractiveSession; pause after spec composition. | Break: omit final interactive workspace admission; expect `adapterStarts.ShouldBe(0) for claim-first`. |
| PC-21 / G-21 | `WorktreeRetirementRaceTests.C459_ResumeFenced` | Existing stopped session on original path; claim wins after composition; Resume and Continue variants. | Break: omit final resume workspace admission; expect `adapterStarts.ShouldBe(0)`. |
| PC-22 / G-22 | `WorktreeRetirementRaceTests.C459_InterruptedAttachFenced` | Restart with queued/interrupted launch generation, including an unknown backend outcome. | Break: omit reservation validation before AttachAsync; expect `adapterAttaches.ShouldBe(0)`. |
| PC-23 / G-23 | `WorktreeRetirementRaceTests.C459_QueuedGenerationIsImmutable` | Persist g1 then replace with g2; invoke each enqueue/resume worker with g1; also unchanged-g1 success. | Break: load current workspace generation instead of validating the queued value; expect `adapterStarts.ShouldBe(0)`. |
| PC-24 / G-24 | `WorktreeRetirementRaceTests.C459_LaunchIntentPrecedesEnqueue` | Read independent DB at adapter entry; lose enqueue wakeup then restart while start remains unresolved. | Break: release launch reservation immediately after enqueue; expect `retirementClaimedWhileStartPending.ShouldBeFalse()`. |
| PC-25 / G-25 | `WorktreeRetirementRaceTests.C459_OneRetirementClaim` | Two providers and separate connections race one retirement; pause winner after claim and restart loser. | Break: return accepted for a claim owned by another execution instead of refusing; expect `acceptedClaimants.ShouldBe(1)`. |
| PC-26 / G-26 | `WorktreeRetirementRaceTests.C459_CompletedPathStaysFenced` | Complete all components, recreate old coordinates via managed retry/resume, versus a fresh task/path. | Break: remove completed reservations from admission lookup; expect `oldCoordinateAdmissions.ShouldBe(0)`. |
| PC-27 / G-27 | `WorktreeRetirementRaceTests.C459_TaskConsumersHold` | Queued/Dispatched/Working/Blocked times ReadOnly/child/base/follow-up/repair consumer relations; exact path, descendant and same branch aliases; sibling-prefix negative. | Break: drop the task-consumer predicate from retirement eligibility; expect `removeCalls.ShouldBe(0)`. |
| PC-28 / G-28 | `WorktreeRetirementRaceTests.C459_SessionOwnersHold` | Starting/Running/Stopping CWDs, pooled warm and standing agent workspace, including descendants with terminal task rows. | Break: drop session/agent workspace ownership from eligibility; expect `removeCalls.ShouldBe(0)`. |
| PC-29 / G-29 | `WorktreeRetirementRaceTests.C459_UnknownBackendHolds` | Backend unavailable, null process observation and StopReturned with no exit evidence. | Break: map unknown runtime observation to absent; expect `removeCalls.ShouldBe(0)`. |
| PC-30 / G-30 | `WorktreeRetirementRaceTests.C459_UnknownChildHolds` | Child journal live/unknown/root-exited-descendant-unknown versus confirmed joined exit. | Break: treat unresolved child journal as completed; expect `removeCalls.ShouldBe(0)`. |
| PC-31 / G-31 | `TaskWorktreeRetirementTests.C459_RecoveryDebtHolds` | Open commit recovery, merge obligation, unpublished failed landing and pending land request, each alone. | Break: ignore recovery-debt predicate; expect `removeCalls.ShouldBe(0)`. |
| PC-32 / G-32 | `SettledWorktreeRemovalTests.C459_ManagedRootRequired` | Outside root, sibling prefix, dot-segment escape and Windows junction retarget; ordinary in-root path succeeds. | Break: restrict confinement check back to CleanupContext != null; expect `removeCalls.ShouldBe(0)`. |
| PC-33 / G-33 | `SettledWorktreeRemovalTests.C459_NestedRegistrationHolds` | Nested registered worktree below candidate, including alias/case normalized descendant. | Break: omit nested-registration check; expect `removeCalls.ShouldBe(0)`. |
| PC-34 / G-34 | `SettledWorktreeRemovalTests.C459_RegistrationRequired` | Present directory without registration and worktree-list failure; no command intent authorizing removal. | Break: remove present-unregistered early refusal (leave downstream inspection intact); expect `inspectionCalls.ShouldBe(0) for the present-unregistered boundary`. |
| PC-35 / G-35 | `SettledWorktreeRemovalTests.C459_LockedRegistrationHolds` | Lock initially and introduce lock between inspections, each lane. | Break: ignore Locked in inspection eligibility; expect `removeCalls.ShouldBe(0)`. |
| PC-36 / G-36 | `SettledWorktreeRemovalTests.C459_PrunableRegistrationHolds` | Inject a valid porcelain row marked prunable, with present directory and other identity facts valid. | Break: ignore Prunable in inspection eligibility; expect `removeCalls.ShouldBe(0)`. |
| PC-37 / G-37 | `TaskWorktreeRetirementTests.C459_MutationIsExcluded` | Ordinary-shaped Mutation task with SourceLandingOperationId=null. | Break: remove Role==Mutation exclusion; expect `authorized.ShouldBeFalse()`. |
| PC-38 / G-38 | `TaskWorktreeRetirementTests.C459_SourceLandingIsExcluded` | Code role with nonnull SourceLanding binding and otherwise valid retirement. | Break: remove SourceLandingOperationId exclusion; expect `authorized.ShouldBeFalse()`. |
| PC-39 / G-39 | `TaskWorktreeRetirementTests.C459_RepairSourceIsExcluded` | Ordinary role with RepairSourceTaskId and exact apparent coordinates. | Break: remove RepairSourceTaskId exclusion; expect `authorized.ShouldBeFalse()`. |
| PC-40 / G-40 | `WorktreeResidueSweepTests.C459_NonordinaryInventoryOnly` | Main checkout, external evidence tree, nested baseline, branch-only no receipt, unregistered leftover and non-card-task name. | Break: route all matched discoveries to settled-task authorization; expect `typedRemovalRequests.ShouldBeEmpty()`. |
| PC-41 / G-41 | `TaskWorktreeRetirementTests.C459_TargetSelectionIsExplicit` | MergeTargetRef, project BaseBranch, Git DefaultBranch, master precedence; missing selected ref with valid HEAD refuses. | Break: fall back to HEAD when the selected target cannot resolve; expect `authorized.ShouldBeFalse() for unresolved selected target`. |
| PC-42 / G-42 | `SettledWorktreeRemovalTests.C459_RemoteContainmentRequired` | Ahead, divergent, patch-equivalent only, task remote branch only, stale origin/master, and released uncontained Plan commit; descendant target succeeds. | Break: replace remote containment result with local target ancestry; expect `removeCalls.ShouldBe(0)`. |
| PC-43 / G-43 | `SettledWorktreeRemovalTests.C459_DestinationIdentityRequired` | Change pushurl or configured target after release while both endpoints contain the SHA. | Break: skip destination fingerprint comparison; expect `removeCalls.ShouldBe(0)`. |
| PC-44 / G-44 | `SettledWorktreeRemovalTests.C459_RemoteRefreshBeforeDirectory` | Initial proof valid then remote rewrites/deletes/errors at the directory authority refresh, both purposes. | Break: reuse initial remote proof for the pre-directory authority read; expect `removeCalls.ShouldBe(0)`. |
| PC-45 / G-45 | `SettledWorktreeRemovalTests.C459_RemoteRefreshBeforeBranch` | Directory already removed; remote loses source or is unreadable before branch phase. | Break: reuse directory-phase remote proof in CompleteBranchAsync; expect `branchDeleteCalls.ShouldBe(0)`. |
| PC-46 / G-46 | `SettledWorktreeRemovalTests.C459_GenuineLeaseRequired` | Null, forged, disposed and other-repository lease, with otherwise matching receipt. | Break: skip leases.Owns validation; expect `removeCalls.ShouldBe(0)`. |
| PC-47 / G-47 | `SettledWorktreeRemovalTests.C459_CommittedReceiptRequired` | Tracked valid release/claim with committed missing/revoked/changed/unsupported-schema state. | Break: return tracked retirement evidence instead of reading a new scope; expect `removeCalls.ShouldBe(0)`. |
| PC-48 / G-48 | `SettledWorktreeRemovalTests.C459_FinalAuthorityRequired` | Independent connection changes receipt during last InspectAsync; other reads all valid. | Break: delete final AuthorityAsync invocation before worktree remove; expect `removeCalls.ShouldBe(0)`. |
| PC-49 / G-49 | `SettledWorktreeRemovalTests.C459_PurposeCannotBorrowAuthority` | Clean real tree with valid retirement supplied under unknown purpose; real guarded manager with otherwise valid evidence. | Break: route an unknown purpose through SettledTask authority in the guarded manager; expect `removeCalls.ShouldBe(0)`. |
| PC-50 / G-50 | `SettledWorktreeRemovalTests.C459_FirstContentInspectionRequired` | Initially staged, unstaged, untracked, dirty submodule, sequencer or unreadable status; inspection ordinal recorded. | Break: coerce the first rejected inspection into a nonnull accepted snapshot matching the request with empty status/ignored paths (retain the second inspection); expect `inspectionCalls.ShouldBe(1) at refusal`. |
| PC-51 / G-51 | `SettledWorktreeRemovalTests.C459_FinalContentInspectionRequired` | Each dirty-state variant introduced only after first inspection; ordinary Git may itself refuse, so assert no remove command. | Break: coerce the final rejected inspection into a nonnull accepted snapshot matching the request with empty status/ignored paths; expect `removeCalls.ShouldBe(0)`. |
| PC-52 / G-52 | `SettledWorktreeRemovalTests.C459_FirstIgnoredInspectionRequired` | Protected .antiphon, .claude and bin-private bytes present initially, both purposes; record inspection calls. | Break: omit only first HasProtectedIgnored check; expect `inspectionCalls.ShouldBe(1)`. |
| PC-53 / G-53 | `SettledWorktreeRemovalTests.C459_FinalIgnoredInspectionRequired` | First inspection clean, create ignored sentinel before second inspection, both purposes. | Break: omit only final HasProtectedIgnored check; expect `removeCalls.ShouldBe(0)`. |
| PC-54 / G-54 | `SettledWorktreeRemovalTests.C459_AbsenceNeedsOwnIntent` | Directory/registration/ref absent independently, with no intent, wrong attempt/identity intent or correct earlier intent. | Break: accept absence based on path/ref alone; expect `complete.ShouldBeFalse() without matching intent`. |
| PC-55 / G-55 | `SettledWorktreeRemovalTests.C459_RecreatedTreeHolds` | Recreate directory, registration or changed ref after component deletion and before recovery. | Break: skip recreation/identity mismatch guard during reconcile; expect `complete.ShouldBeFalse()`. |
| PC-56 / G-56 | `SettledWorktreeRemovalTests.C459_BranchPrecheckRequired` | Advance source ref after directory removal but before show-ref read. | Break: omit current SHA comparison while retaining CAS; expect `branchDeleteCalls.ShouldBe(0)`. |
| PC-57 / G-57 | `SettledWorktreeRemovalTests.C459_BranchDeleteUsesCas` | Move ref after precheck, immediately before update-ref. | Break: remove ExpectedSourceSha argument from update-ref -d; expect `actualBranchSha.ShouldBe(concurrentSha)`. |
| PC-58 / G-58 | `SettledWorktreeRemovalTests.C459_NewCheckoutHoldsBranch` | Create another checkout after directory removal before branch registration read. | Break: omit source-branch checkout recheck; expect `branchDeleteCalls.ShouldBe(0)`. |
| PC-59 / G-59 | `WorktreeResidueRecoveryTests.C459_IncompletePinsRetained` | Command result unknown, registration-query failure and branch failure after directory removal. | Break: retire pins on directory success alone; expect `allRequiredPinsPresent.ShouldBeTrue()`. |
| PC-60 / G-60 | `WorktreeResidueRecoveryTests.C459_PinRetirementUsesCas` | Complete components then change one owned pin; include another retirement's same-suffix pin. | Break: drop old SHA from pin compare-and-delete; expect `changedPinSha.ShouldBe(concurrentSha)`. |
| PC-61 / G-61 | `WorktreeResidueRecoveryTests.C459_ClaimCommittedBeforeIo` | Fail claim save/commit and lose commit acknowledgement; inspect separate DB at first mutation. | Break: start mutation before claim commit; expect `committedClaimAtMutation.ShouldBeTrue()`. |
| PC-62 / G-62 | `WorktreeResidueRecoveryTests.C459_IntentCommittedBeforeGit` | Fail intent save/commit versus committed-intent lost acknowledgement; observer at Git entry. | Break: move intent persistence after worktree remove; expect `committedCommandIdAtRemove.ShouldNotBeNull()`. |
| PC-63 / G-63 | `WorktreeResidueRecoveryTests.C459_SpentSlotSurvivesRestart` | Crash after intent before spawn and after Git exit before result. For the decisive latter arm introduce a lock after inspection so ordinary remove spends its slot but leaves the tree; join Git, remove only the fixture lock, restart the same attempt without advancing cooldown. | Break: clear command ID when reopening interrupted attempt; expect `removeCallsForAttempt.ShouldBeLessThanOrEqualTo(1)`. |
| PC-64 / G-64 | `WorktreeResidueRecoveryTests.C459_ComponentsAreIndependent` | Cuts after Git exit, directory/registration result and branch CAS; DB write fails before/after commit. | Break: set all three outcomes complete after a directory Git exit zero; expect `complete.ShouldBeFalse() while branch remains`. |
| PC-65 / G-65 | `WorktreeResidueRecoveryTests.C459_OutageKeepsFence` | Cancellation at lease/inspection/intent/Git/result; DB/Git/backend outage after intent. | Break: release workspace fence in the post-intent exception handler; expect `fencePresent.ShouldBeTrue()`. |
| PC-66 / G-66 | `WorktreeResidueRecoveryTests.C459_RunIntentPrecedesEnqueue` | Fail run/action commit; lose acknowledgement; read committed run candidate at TryEnqueue boundary. | Break: enqueue before persisting run/action link; expect `committedRunLinkAtEnqueue.ShouldNotBeNull()`. |
| PC-67 / G-67 | `WorktreeLandingCleanupRetryTests.C459_AdmissionPinsPublication` | Missing/replaced/inactive/unconfirmed operation, pending other request and legacy Landed event. | Break: omit operation binding validation in RequestCleanupRetryAsync; expect `newCleanupRequests.ShouldBe(0)`. |
| PC-68 / G-68 | `WorktreeLandingCleanupRetryTests.C459_ExecutionPinsPublicationBeforeResolver` | Accept valid request, replace/deconfirm operation before worker; trap ObserveSourceAsync and source fetch. | Break: remove cleanup-only guard before AgentTaskLandSourceResolver; expect `sourceResolutionCalls.ShouldBe(0)`. |
| PC-69 / G-69 | `WorktreeLandingCleanupRetryTests.C459_ProtocolPinsPublication` | Invoke protocol with a cleanup-only request bound to missing/replaced receipt and otherwise publishable source. | Break: remove protocol cleanup-only binding check; expect `publicationCommands.ShouldBeEmpty()`. |
| PC-70 / G-70 | `WorktreeLandingCleanupRetryTests.C459_ScheduledHasNoCallerObligation` | Original parent busy and idle, pending manual note plus scheduled retry, task ReplyTo=Session. | Break: copy task.ReplyTo into scheduled cleanup request; expect `scheduledNote.State.ShouldBe(LandNotificationState.NotRequired)`. |
| PC-71 / G-71 | `WorktreeLandingCleanupRetryTests.C459_PublicationIsNotRepeated` | Landed and AlreadyPresent, refused then successful cleanup; compare original IDs/timestamps and stages. | Break: emit Landed instead of LandingCleanup for cleanup-only completion; expect `newPublicationEvents.ShouldBe(0)`. |
| PC-72 / G-72 | `WorktreeResidueSweepTests.C459_AbsentPublishedTreeDiscovered` | Confirmed incomplete operation with prior intent and absent directory, scanner empty. | Break: derive candidates only from filesystem scan; expect `candidateOperationIds.ShouldContain(originalOperationId)`. |
| PC-73 / G-73 | `WorktreeResidueSweepTests.C459_IgnoredInventoryReachesPolicy` | Ignored-only confirmed residue, fresh cooldown; recording typed manager returns accepted; second real-manager arm refuses today. | Break: classify ignored-only rows permanently Dirty/Keep before typed removal; expect `typedPolicyCalls.ShouldBe(1)`. |
| PC-74 / G-74 | `WorktreeResidueSweepTests.C459_OnlyCompleteCountsRemoved` | Mix queued publication, directory-only partial, refused ignored, held and all-three complete retirement. | Break: increment Removed on enqueue or directory success; expect `report.Removed.ShouldBe(1)`. |
| PC-75 / G-75 | `WorktreeResidueSweepTests.C459_ExecuteFalseIsReadOnly` | Valid released retirement and confirmed publication residue plus execute=false. | Break: ignore Execute switch; expect `mutatingActions.ShouldBe(0)`. |
| PC-76 / G-76 | `WorktreeResidueRegistrationTests.C459_DisabledSchedulerStaysOff` | WorktreeResidue disabled and Hangfire server disabled separately; actual Program composition. | Break: register/start residue worker despite disabled setting; expect `residueRecurringJobs.ShouldBeEmpty()`. |
| PC-77 / G-77 | `WorktreeResidueRegistrationTests.C459_OneScheduler` | Inspect actual DI hosted services and fresh Hangfire registration; daily 10:00 Europe/London, no janitor. | Break: restore AddHostedService<WorktreeJanitorHostedService>(); expect `janitorHostedServices.ShouldBeEmpty()`. |
| PC-78 / G-78 | `WorktreeResidueSweepTests.C459_CooldownSurvivesRestart` | 23:59:59.999999, 24h and beyond since refusal, across recreated provider and both lanes. | Break: reset NotBefore during startup; expect `actionsBeforeDue.ShouldBe(0)`. |
| PC-79 / G-79 | `WorktreeResidueSweepTests.C459_BudgetIsShared` | 0 invalid configuration, 1, 25 and 26 eligible actions mixed across lanes, duplicate trigger while busy. | Break: apply MaxActionsPerRun separately per lane; expect `totalAcceptedActions.ShouldBeLessThanOrEqualTo(limit)`. |
| PC-80 / G-80 | `WorktreeResidueSweepTests.C459_FairnessSurvivesRestart` | More than two pages, oldest refusal and both lanes, equal-time IDs; restart between budget-limited runs. | Break: order by CreatedAt each run without advancing persisted evaluation order; expect `allDueCandidateIds.ShouldBe(evaluatedDistinctIds)`. |
| PC-81 / G-81 | `WorktreeResidueEndpointTests.C459_RunResultSurvivesRestart` | Execute retirement, replace service provider and GET run with its exact component evidence/IDs. | Break: serve only an in-memory last-run projection; expect `fetched.Components.ShouldBe(persistedComponents)`. |
| PC-82 / G-82 | `WorktreeLandingCleanupRetryTests.C459_LostWakeupRecoversSameRequest` | Crash before/after request commit and before/after TryEnqueue; discard queue then invoke real sweep/worker. | Break: exclude cleanup-only requests from recovery sweep; expect `cleanup.CompletedRequestId.ShouldBe(originalRequestId)`. |
| PC-83 / G-83 | `WorktreeResidueRecoveryTests.C459_TerminalProjectionRecovers` | Commit cleanup terminal event then fail run projection; restart and read through run endpoint. | Break: omit reconciliation of candidates whose request is terminal; expect `fetchedCandidate.TerminalOutcome.ShouldBe(actualOutcome)`. |
| PC-84 / G-84 | `WorktreeResidueEndpointTests.C459_RunReportOmitsOpaqueContent` | Inject synthetic credential-like stderr and a file-content marker into a refused candidate. | Break: persist raw Git stderr into candidate reason; expect `serializedRun.ShouldNotContain(privateMarker)`. |
| PC-85 / G-85 | `WorktreeLandingCleanupRetryTests.C459_ManualNoteSnapshotSurvivesCleanup` | Produce real manual obligation then edit task parent and run scheduled cleanup, original recipient busy/idle. | Break: rewrite existing notification destination from current task; expect `manualNote.ParentSessionId.ShouldBe(originalCaller)`. |
| PC-86 / G-86 | `WorktreeLandingCleanupRetryTests.C459_OutcomeObligationIsAtomic` | Producer save/commit/ack failures; restart actual notification worker and reach recipient. | Break: skip AddNotification on manual terminal completion; expect `receivedOutcomeCount.ShouldBe(1)`. |
| PC-87 / G-87 | `WorktreeLandingCleanupRetryTests.C459_QueueIdentitySurvivesLostLink` | Real producer; crash after keyed row insert before QueueMessageId save; restart while caller busy. Count matching body/destination rows including unkeyed duplicates, then release caller and require the complete UserPrompt. | Break: enqueue recovered note without SourceLandNotificationId; expect `rowsForOutcomeBodyAndDestination.ShouldHaveSingleItem()`. |
| PC-88 / G-88 | `WorktreeLandingCleanupRetryTests.C459_CompletePromptIsRequired` | Producer body with tail sentinel; transport success, Sent/Delivered flag, prefix-only UserPrompt, old baseline and wrong recipient each cannot confirm; full current prompt does. | Break: mark notification Confirmed from Sent/Delivered without transcript completeness; expect `note.ConfirmedAt.ShouldBeNull() before complete prompt`. |
| PC-89 / G-89 | `WorktreeLandingCleanupRetryTests.C459_IdleReceiptRecoversLostFlush` | Real producer/queue; suppress one flush wakeup; hosted recovery gets full prompt without new input. | Break: exclude land-origin notes from completion flush recovery; expect `completeMatchingPrompts.ShouldBe(1)`. |
| PC-90 / G-90 | `WorktreeLandingCleanupRetryTests.C459_ReceiptFailureNeverRetypes` | Prompt persisted, fail verdict/receipt commit then restart; two recovery passes. | Break: requeue for typing before checking late transcript receipt; expect `submittedMatchingBodies.ShouldHaveSingleItem()`. |
| PC-91 / G-91 | `WorktreeLandingCleanupRetryTests.C459_EnqueueFailureRemainsOwed` | Producer outcome committed; before insert and transaction rollback fail twice; restore I/O, busy/idle destination. | Break: mark notification NotRequired on enqueue exception; expect `completeMatchingPrompts.ShouldBe(1)`. |
| PC-92 / G-92 | `TaskWorktreeRetirementTests.C459_ReleaseIdentityIsUnique` | Concurrent identical releases reuse one ID; conflicting snapshot refused. | Break: mint a fresh retirement on every identical release; expect `retirementIds.Distinct().Count().ShouldBe(1)`. |
| PC-93 / G-93 | `TaskWorktreeRetirementTests.C459_LegacyRowsAreUnreleased` | Upgrade from previous migration with terminal rows across roles, then repeat migrate. | Break: backfill NoFurtherWorkspaceUse for old terminal rows in migration; expect `legacyReleaseCount.ShouldBe(0)`. |
| PC-94 / G-94 | `WorktreeResidueScriptTests.C459_BatchRequiresFullIds` | Valid manifest, short ID, duplicate/conflicting ID, stale revision and multiline reason; no release-all mode. | Break: expand missing/short IDs into all terminal candidates; expect `unexpectedReleasePosts.ShouldBe(0)`. |
| PC-95 / G-95 | `WorktreeResidueEndpointTests.C459_PreviewCannotExecute` | POST preview with valid removable rows and global Execute=true. | Break: delegate preview to executing RunAsync without preview override; expect `mutatingActions.ShouldBe(0)`. |
| PC-96 / G-96 | `WorktreeRetirementRaceTests.C459_CleanupNeverStopsOwner` | Known live and unknown owners with releasable task. | Break: call IDelegateSessionStopper before returning live-owner refusal; expect `stopCalls.ShouldBe(0)`. |
| PC-97 / G-97 | `SettledWorktreeRemovalTests.C459_RemovalNeverForces` | Start clean; intercept ordinary remove to return failure, introduce owned ignored sentinel after inspections; downstream retry trap records force arguments and sentinel bytes. | Break: retry failed ordinary worktree remove with --force; expect `forceOrRecursiveDeleteCalls.ShouldBe(0)`. |
| PC-98 / G-98 | `TaskWorktreeRetirementTests.C459_OwnProgressKeepsRelease` | Claim and result rotate task token, unchanged release snapshot; positive retirement completes. | Break: compare released task token to cleanup-rotated current task token; expect `allComponentsComplete.ShouldBeTrue()`. |
| PC-99 / G-99 | `WorktreeRetirementRaceTests.C459_GitDoesNotHoldDbRows` | Pause remote observation after claim; separate connection can read claim and quickly refuse a new consumer; reverse-order lease contention releases rows. | Break: move remote observation inside the row-lock transaction; expect `independentAdmissionFinishedBeforeGitRelease.ShouldBeTrue()`. |
| PC-100 / G-100 | `WorktreeRetirementRaceTests.C459_LaunchCommitPrecedesEnqueue` | Fail launch reservation save/commit and lose acknowledgement; observe DB at queue/adapter boundary. | Break: invoke launch enqueue before reservation commit; expect `committedLaunchReservationAtEnqueue.ShouldNotBeNull()`. |
| PC-101 / G-101 | `WorktreeRetirementRaceTests.C459_RefineReservesWorkspace` | RefineAsync Queued goal amendment and Working queue input; claim-first and existing-live-reservation-first. | Break: omit RefineAsync workspace admission; expect `refinementAdmissions.ShouldBe(0) for claim-first`. |
| PC-102 / G-102 | `WorktreeRetirementRaceTests.C459_MergeChildReservesWorkspace` | Drive conflict child creation against claimed source; caller scope otherwise valid. | Break: omit reservation check in merge-child creation; expect `newMergeChildren.ShouldBe(0)`. |
| PC-103 / G-103 | `WorktreeRetirementRaceTests.C459_CommitChildReservesWorkspace` | Drive commit-child creation against claimed source; independent recovery-debt fixture cleared to isolate admission. | Break: omit reservation check in commit-child creation; expect `newCommitChildren.ShouldBe(0)`. |
| PC-104 / G-104 | `WorktreeRetirementRaceTests.C459_HerdrAttachReservesWorkspace` | AgentControlService.AttachHerdrAsync with known matching pane and CWD; retired subtree versus distinct sibling. | Break: omit workspace reservation check at attach admission; expect `acceptedAttachments.ShouldBe(0)`. |
| PC-105 / G-105 | `SettledWorktreeRemovalTests.C459_RetirementRefsCannotBorrowNamespaces` | Successful retirement uses refs/antiphon/retirement/<retirement-id>/ only; reject heads, tags, another retirement prefix and land prefix in retirement-specific calls, including an existing wrong-namespace ref already at the requested SHA. | Break: remove the retirement namespace/binding check and allow an arbitrary valid ref; expect `wrongNamespaceAccepted.ShouldBeFalse()`. |
| PC-106 / G-106 | `WorktreeResidueEndpointTests.C459_ReleaseRejectsStaleApprovalSnapshot` | Send a correct scoped release, then separately stale revision, different full source SHA and different report digest; spoofed extra path/remote/status fields cannot alter canonical server coordinates. | Break: skip the initial equality check between caller-reviewed tuple and current task/source/report facts; expect `staleApprovalAccepted.ShouldBeFalse()`. |
| PC-107 / G-107 | `WorktreeResidueEndpointTests.C459_RunReportPageIsBounded` | Persist more than two pages of mixed candidates; read every page after restart and require exact IDs once, bounded rows and summary totals matching all rows. | Break: remove the Take(pageLimit) bound from the run-result query; expect `returnedRows.Count.ShouldBeLessThanOrEqualTo(pageLimit)`. |
| PC-108 / G-108 | `SettledWorktreeRemovalTests.C459_InterfaceDefaultCannotDeleteSettledTask` | Cast a LegacyOnlyManager as IWorktreeManager and submit a SettledTask request; its RemoveAsync counter is observable and no real Git is needed. | Break: make default typed TryRemoveAsync call legacy RemoveAsync for SettledTask; expect `legacyRemovalCalls.ShouldBe(0)`. |


### Out of scope

- The 446 never-landed directories are candidates, not approved removals. Terminal
  state plus completed handoff permits consideration; exact release, remote
  containment, ownership and current content proof still gate deletion.
  The 43 historical ignored refusals stay protected; current counts require
  a new commissioned census. No test/deployment goal is “delete all 446”.
- CARD-0452 policy changes, ignored allowlists, build-output pre-deletion,
  remote branch cleanup, force/recursive production deletion and killing owners
  are excluded. Every content-control mutant is temporary post-land test work
  inside its fixture. The shipped shared ignored predicate remains unchanged.
- Mutation/SourceLanding and repair-source/evidence/main/baseline cleanup remain
  their own workflows. The tests prove exclusion, not new authority for them.
  Full native custody qualification and CARD-0443's separately commissioned
  elevated Handle cases are not repeated: this card changes no native custody
  or diagnostic implementation. Shared publication journal/guard tests remain.
- The Cartesian product of every provider, OS, role and error is excluded.
  Windows real Git and native FakeGrok qualify physical behavior; controlled
  runtime observations cover unavailable backends; policy matrices cover the
  specified independent fields/terminal statuses. No live provider, production
  runner, live broker or real backlog is a fixture.
- S2 changes admission, not the semantics of existing in-turn tool answers or
  event-before-queue reply recovery. V-11 tests admitted UserPrompt paths and
  ownership across failures. Do not silently broaden this change into a new
  reply-delivery architecture; a proposed change to that seam requires renewed
  Plan/TestDesign with its own durable producer/recipient contract.
- Production enablement, exact backlog releases and later scheduled acceptance
  are separately commissioned S5 work after land, not this dispatch.
  A feature left permanently Execute=false does not pass S5.

### Cost

All minutes below are **estimated**, not measured. TestDesign executed zero
tests/builds. Record actual expanded counts, skips, failures and fresh TRX paths
against each Code commit; report base-commit reproduction for inherited reds.
No full-assembly timing is assumed from CARD-0110's obsolete 25.5-minute sample.
This plan uses the current Unit-plus-named-integration Final profile.

Ordinary commands after implementing the methods:

~~~powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c459/ --nologo
if ($LASTEXITCODE -ne 0) { throw 'Antiphon.Tests build failed' }
npm --prefix client run build
if ($LASTEXITCODE -ne 0) { throw 'client build failed' }
dotnet build tests/Antiphon.E2E --property:OutputPath=bin-c459/ --nologo
if ($LASTEXITCODE -ne 0) { throw 'Antiphon.E2E build failed' }

$c459Results = Join-Path '.antiphon' ('c459-vr-' + [Guid]::NewGuid().ToString('N'))
function Invoke-C459Tests {
    param([string]$Project, [string]$Name, [string]$Filter)
    $result = Join-Path $c459Results $Name
    if (Test-Path -LiteralPath $result) { throw 'Results must be fresh' }
    dotnet run --project $Project --no-build --property:OutputPath=bin-c459/ -- --treenode-filter $Filter --report-trx --report-trx-filename run.trx --results-directory $result
    if ($LASTEXITCODE -ne 0) { throw "Failed selection: $Name" }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $result 'run.trx') -Raw
    $counts = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counts -or [int]$counts.executed -eq 0) { throw "No executed tests: $Name" }
}

Invoke-C459Tests 'tests/Antiphon.Tests' 'unit' '/*/*/*/*[Category=Unit]'
Invoke-C459Tests 'tests/Antiphon.Tests' 'disposition-surface' '/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueEndpointTests*)|(WorktreeResidueScriptTests*)/*'
Invoke-C459Tests 'tests/Antiphon.Tests' 'workspace-races' '/*/*/WorktreeRetirementRaceTests/*'
Invoke-C459Tests 'tests/Antiphon.Tests' 'settled-removal' '/*/*/SettledWorktreeRemovalTests/*'
Invoke-C459Tests 'tests/Antiphon.Tests' 'cleanup-retry' '/*/*/WorktreeLandingCleanupRetryTests/*'
Invoke-C459Tests 'tests/Antiphon.Tests' 'sweep-recovery-scheduler' '/*/*/(WorktreeResidueSweepTests*)|(WorktreeResidueRecoveryTests*)|(WorktreeResidueRegistrationTests*)/*'
Invoke-C459Tests 'tests/Antiphon.Tests' 'publication-regression' '/*/*/(AgentTaskLandCleanupSafetyTests*)|(AgentTaskLandPublicationTests*)|(AgentTaskLandRequestTests*)|(AgentTaskLandRecoveryTests*)/*'
Invoke-C459Tests 'tests/Antiphon.Tests' 'journal-notifications' '/*/*/(WorktreeGuardedCleanupTests*)|(WorktreeCleanupJournalTests*)|(AgentTaskLandNotificationPersistenceTests*)|(AgentTaskLandNotificationRecoveryTests*)/*'
Invoke-C459Tests 'tests/Antiphon.Tests' 'legacy-authority' '/*/*/WorktreeRemovalAuthorityTests/C448_V24_LegacyRemovalCannotEraseTaskContents'
Invoke-C459Tests 'tests/Antiphon.Tests' 'interface-defaults' '/*/*/WorktreeRemovalDefaultTests/*'
Invoke-C459Tests 'tests/Antiphon.Tests' 'retention' '/*/*/DataRetentionServiceTests/(C459_*)|(A_fully_terminal_stale_tree_loses_every_row_and_its_events*)|(A_tree_with_one_live_member_survives_entirely*)|(A_tree_whose_newest_row_is_within_the_window_survives_entirely*)|(A_zero_task_window_skips_tasks*)|(C508_IntentTaskTreeRetention*)|(C508_NotificationTaskTreeRetention*)'
Invoke-C459Tests 'tests/Antiphon.Tests' 'launch-ownership' '/*/*/AgentSessionLaunchQueueOwnershipTests/(Owns_is_true_from_enqueue_until_the_launch_settles*)|(ResumeInterrupted_registers_before_running_and_a_second_call_is_a_noop*)|(A_faulted_resume_still_releases_ownership*)|(Queued_launch_carries_the_explicit_accepted_generation_and_never_re_reads_a_replaced_row*)'
Invoke-C459Tests 'tests/Antiphon.Tests' 'reopen' '/*/*/CardCorrectionIntegrationTests/(Reopen_writes_one_revision_with_the_superseded_terminal_facts_and_clears_them*)|(Reopen_defaults_to_the_backlog_column_and_never_spawns*)'
Invoke-C459Tests 'tests/Antiphon.Tests' 'hangfire' '/*/*/HangfireStartupSafetyTests/*'
pwsh -NoProfile -File scripts/test-worktree-residue.ps1
if ($LASTEXITCODE -ne 0) { throw 'Worktree residue script harness failed' }

Invoke-C459Tests 'tests/Antiphon.E2E' 'native-workspace' '/*/*/WorktreeRetirementDeliveryE2ETests/*'
Invoke-C459Tests 'tests/Antiphon.E2E' 'native-manual-land' '/*/*/AgentTaskLandDeliveryE2ETests/(C467_V22_*)|(C467_V23_*)|(C467_V25_*)|(C467_V26_*)|(C467_V27_*)|(C467_V28_*)|(C467_V29_*)|(C467_V30_*)|(C467_V31_*)|(C467_V32_*)'
~~~

The function rejects zero tests but cannot infer intended coverage: inspect the
fresh TRX method/class/argument roster against the tables, including each
selected OR operand. OptIn native tests must actually run. Use the existing
duration-tripwire on each TRX and justify new slow cases in the normal allowlist.
Run sequentially, in foreground, await every command, commit before large runs,
and freeze source while a run is active. If a selection exceeds one foreground
window, split by the already listed classes/methods, not by skipping coverage.
No Antiphon.Agents.Pty.Tests process runs alongside these tests.

| Ordinary Code floor | Minutes |
|---|---:|
| Setup, migration/tool/native prerequisites and both builds/client bundle | 14 |
| Unit selection | 2 |
| Release, HTTP/script classes and targeted retention | 10 |
| Workspace races / launch boundaries | 25 |
| Settled real-Git removal matrices | 25 |
| Cleanup-only queue/delivery integration | 15 |
| Sweep, recovery/crash and scheduler qualification | 18 |
| Existing R-1..R-7 classes/methods, excluding duplicate selections already covered | 30 |
| Native V-11 and R-8 | 45 |
| Standalone script harness / artifact-count audit | 2 |
| **Ordinary V/R after setup** | **172** |
| **Code total including setup/build** | **186** |

Separate post-land **Mutation floor**: use one exact method per PC, with the
fully expanded Class.Method in the PC table; no class filter for red or green.
Example for PC-52 (same command with fresh result directories after restoration):

~~~powershell
$c459PcResults = Join-Path '.antiphon' ('c459-pc52-red-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c459-pc/ -- --treenode-filter '/*/*/SettledWorktreeRemovalTests/C459_FirstIgnoredInspectionRequired' --report-trx --report-trx-filename run.trx --results-directory $c459PcResults
~~~

For a sourced run, replace only the result-root location with the externally
assigned evidence root; source/output work remains within the managed snapshot.
Record mutation diff/hash, method/arguments, decisive failure, restored source
hash and fresh green TRX. Refresh restored source timestamps or rebuild so a
mutated DLL cannot masquerade as green. Never commit/push from a SourceLanding
snapshot. These TestDesign changes are ordinary task-branch documentation and
are committed/pushed.

| PC bucket (disjoint aliases above) | PCs | Red minutes each | Restore/rebuild each | Green each | Bucket minutes |
|---|---:|---:|---:|---:|---:|
| DB/policy/surface T,E,D,U,J,C | 34 | 0.4 | 0.6 | 0.4 | 47.6 |
| Workspace races W | 26 | 0.7 | 0.6 | 0.7 | 52.0 |
| Removal/authority S | 26 | 1.0 | 0.6 | 1.0 | 67.6 |
| Retirement recovery X | 9 | 1.5 | 0.6 | 1.5 | 32.4 |
| Cleanup request/delivery L | 13 | 1.0 | 0.6 | 1.0 | 33.8 |
| **Every PC red/restore/green** | **108** | | | | **233.4** |
| Mutation snapshot setup, baseline build/receipt audit | | | | | **10.0** |
| **Mutation total** | | | | | **243.4** |

**Total verification floor = 14 setup/build + 172 ordinary V/R + 10 Mutation
setup + 233.4 every-PC cycles = 429.4 minutes (about 7 hours 9 minutes), estimated.**
Independent ordinary Review repeating this profile has its own 186-minute
allowance; commissioning both Code and a full repeat Review totals 615.4 minutes.
S5 deployment acceptance adds an estimated 30 operator minutes and 0-24 hours
wall time to witness the subsequent real scheduled run; that waiting is not
test CPU time and does not make report-only delivery acceptable.

Savings are explicit estimates: compared with rerunning the six affected
integration groups (123 minutes: 10+25+25+15+18+30) for both red and green on every
PC, method selection saves 108*2*123 - (233.4 - 108*0.6) = **26,399.4 minutes**.
This is avoided hypothetical suite work, not measured speedup. Building once
for the 16 ordinary test invocations instead of rebuilding each, at an estimated
one incremental minute each, avoids **15 minutes**. No savings from PC batching
or sharding are booked (0 minutes): many controls share files/guards, and a
sourced snapshot cannot be expanded into unbound worktrees. The floor includes
all 108 cycles and the native recipient evidence; neither is traded away.

Handoff audit: bodies and nearest fixtures read; **guards=108, mapped=108,
missing=0, duplicate PC mappings=0**. Every PC has a concrete compiling defect,
exact method, required setup and decisive assertion; no PC relies on a build
failure or zero tests. Native prerequisites and missing fixture work are
explicit implementation tasks, not untestable seams. Numeric costs include
ordinary and every post-land PC cycle. Next stage: **Code**, ordinary V/R first,
separate Review before land, then commissioned SourceLanding Mutation.
