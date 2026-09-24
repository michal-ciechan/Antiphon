# CARD-0657: synchronize runner work before completion attribution

Date: 2026-09-24. Stage: Plan, with verification design folded into this dispatch.
Task: `5ef9bb0e`. Source inspected: `25c09394`.
Next stage: **Code**. Three bounded Code rounds; run the checkpoint manifest below.

## Outcome and scope

An ordinary runner-bound Worktree task that pushes commit S to its own task branch
must synchronize its desktop worktree to S **before** completion progress is
classified. Its report, progress evidence, git summary and deliverable must refer
to that same observation. The original Code task remains the landing owner;
Review starts at S and the caller lands that owner with `-ExpectedSourceSha S`.

Implement this on the desktop server. No runner RPC, runner deployment, transcript
format change, new landing protocol, new card status, historical task rewrite or
SourceLanding snapshot mutation is required. Preserve CARD-0604 D-15's desktop
canonical-worktree model and its prohibition on destructive synchronization.

The card was read through `scripts/card.ps1 get CARD-0657 -Json`. Its examples are
CARD-0640 task `993405ad` (reported push `b8aeaa51`), CARD-0643 task `3cfdd4a3`, and
CARD-0646 task `3416c8aa`. These are card evidence, not live reproductions performed
by this Plan. No builds or product tests ran in this documentation dispatch.

## Ground truth

| Card assumption | What the inspected code does | Design consequence |
|---|---|---|
| Settlement never fetches the runner branch. | `AgentTaskReplyService.SettleAsync`, around line 614, calls `ClassifyReportAsync` first. The only settlement call to `RemoteWorkspaceService.SyncAsync` is inside `MergeBackAsync`, around line 1524, reached only for Succeeded Worktree tasks whose progress permits automatic mutation. | The defect is ordering, not a missing transport. Move preparation ahead of attribution and remove the late duplicate sync. |
| Progress only reads local HEAD. | `TaskCompletionProgressService.EvaluateSourceAsync` also calls `ITaskProgressGit.ObserveExactRefAsync`. `EvaluatePrimaryGraphAsync` around lines 676-697 requires a task-scoped claim for remote-only movement; a local descendant on the expected branch succeeds without a claim. | A runner push can already be fetched as an object yet fail `unclaimed_or_unmatched_commit`: its desktop branch/HEAD remain at baseline. Synchronizing the owned checkout makes the ordinary primary evidence true. Do not globally weaken remote attribution. |
| The report-attribution matcher needs Git. | `ExtractMarkedTurnAsync` matches the owning transcript prompt and report boundary. Commit attribution is in `TaskCompletionProgressService`; `TryDescribeGitAsync` separately counts base..HEAD. A report token is not the full-GUID `[antiphon-progress:... commit=...]` claim. | Preserve prompt/report correlation, including CARD-0649. Prepare Git only after report correlation; give the commit matcher and summary the prepared SHA. Never use a commit as permission to attribute an uncorrelated turn. |
| CARD-0604 already defined settlement sync. | [CARD-0604 D-15](2026-09-22-card-0604-persistent-runner-dind-plan.md#d-15-ordinary-remote-tasks-run-in-a-mirror-worktree-the-desktop-worktree-stays-canonical) requires fetch, ff-only, then progress/landing/retirement. `RemoteWorkspaceService.SyncAsync` currently uses `fetch origin task.WorktreeBranch`, then `merge --ff-only FETCH_HEAD`; it checks dirt but not task-branch identity or repository ownership and can return Synced=true with a null SHA. | Retain the helper, harden its identity and result contract, and implement the intended order. Never consume shared FETCH_HEAD or claim success with an unknown SHA. |
| Baselines are available for every runner task. | `AgentTaskDispatcher` around line 4083 captures `ProgressBaselineJson` only for Code/Worktree tasks. It captures canonical repo/common directory, registered path, expected ref, local SHA and origin endpoint fingerprint. Remote preparation subsequently pushes the dispatch branch. | Capture this identity for all eligible ordinary remote Worktree roles too; keep Code's existing capture. A baseline remote state of Missing is normal before the initial push. |
| Land needs a new remote path. | `AgentTaskLandService.RequestAsync` already requires Succeeded and an expected SHA. `AgentTaskLandSourceResolver` observes the source remote, can advance a behind-only checkout, and refuses divergent or mismatched reviewed source. | Keep the existing owner and land protocol. Prove the synchronized owner can use it and that a later remote advance invalidates stale approval. |
| Review StartRef will fetch the desired source. | CARD-0613 `worktreeBaseRequestedRef`/`-StartRef` resolves an object already in the desktop repo. It creates Review's own branch and never transfers ownership. | Settlement must make S locally resolvable before reporting success. Review uses the full S, not a moving branch or the runner's path. |
| Card transitions inspect commits. | `CardWorkTransitionService` reads durable task status/timestamps, no Git or runner. Newest Succeeded with no open task moves to Review; Failed does not; Blocked remains open. | Fix settlement evidence and status. No new transition rule and no automatic Done. |
| Git operations are bounded and serialized already. | `LandingGit.RunAsync` has a five-minute child timeout and journals mutating children. `TaskProgressGit` acquires a repository lease for fetch/pin. The old mirror sync does not acquire that lease. | Use existing bounded Git I/O, a shorter aggregate sync budget and the repository mutation lease; avoid nested lease acquisition. |
| A retry can reprocess a settled report. | `OnTurnEndAsync` accepts only Dispatched/Working tasks. The deferred-report sweep re-hands open marked reports. Blocked answers stamp a new prompt watermark; terminal tasks require explicit retry. | Re-entry before the settlement save must be idempotent. A sync block uses the existing reply path; terminal history is never silently reclassified. |

## Decisions

These are implementation decisions within the brief, not unresolved operator
defaults. No approval or separate TestDesign dispatch is needed.

### D-1. One preparation result before any Git-dependent completion decision

Reuse `RemoteWorkspaceService.SyncAsync`, with a typed result in
`server/Application/Dtos/RemoteSettlementSyncDtos.cs` (new). Result states:
`NotApplicable`, `Synchronized`, `NoPushedProgress`, `Refused`, `Unavailable`.
Carry full source ref, baseline SHA, observed remote SHA, desktop-before SHA,
confirmed desktop-after SHA, observation ref, endpoint fingerprint and stable
reason code where applicable. Successful synchronization requires a full SHA.

Factor the completion preparation in `AgentTaskReplyService` so a correlated
candidate for successful settlement prepares once before
`TryClassifyCompletedWithoutProgressAsync`, deliverable/report-file lookup, git
counts, scope drift and merge-back. Both explicit `done` and the existing
unmarked-success fallback use it. Nudge-only, interrupted, uncorrelated, explicit
failed and question/blocked turns do not fetch or change the checkout.

Pass the result into `TaskCompletionProgressService.EvaluateAsync`; its public
entry point must also prepare an eligible remote task when called without an
already prepared result. The shared predicate/result prevents report-time callers
from bypassing the fix. Never recursively call the progress evaluator from sync.
The prepared result is per evaluation, not a process-wide success cache.

Rejected: sync only inside merge-back (the current circular dependency); accepting
every remote commit without first proving task identity; fetching during transcript
correlation (Git must not turn a foreign prompt into a task report).

### D-2. Trust exactly the task's branch, not a report-supplied ref

Eligibility is Workspace=Worktree, a nonblank RunnerId, Role != Mutation and no
SourceLandingOperationId. A missing mirror path is not permission to skip the guard:
RunnerId and the ordinary task coordinates decide eligibility. Local, Shared,
ReadOnly and all Mutation tasks retain their existing behavior.

Derive `feat/card-task-<first eight lowercase hex characters of task.Id>` in code.
Require ordinal equality with the recorded WorktreeBranch, and require the baseline
FullRef to equal `refs/heads/` plus that derived branch. Validate the registered
desktop worktree, repo/common directory, symbolic HEAD and branch tip against the
task/baseline. Refuse detached/wrong-branch, replacement repo, duplicate/locked/
prunable registration, active sequencer or active retirement reservation. Never
switch branches to make validation pass. A repair-source owner ref is never a sync
target; existing repair attribution remains separate.

Fetch only that full ref from the desktop repository's configured **origin**,
checked against the captured endpoint fingerprint. The push endpoint is the source
of runner publications, as in `TaskProgressGit`; reject ambiguous/missing/changed
endpoint configuration. Never accept a URL, fetch refspec, cwd or branch from report
text or from a runner response. A generic `feat/card-task-*` prefix check is too weak:
another task's branch is forbidden even within that namespace.

This is attribution to a task-owned branch, not proof of the human author of a
commit. It trusts the existing repository credential/branch namespace model; it
does not add commit signing or broaden runner credentials.

### D-3. Observe a pinned commit, then fast-forward under the repository lease

1. Validate eligibility and identity. Use the existing workspace-use admission
   fence. Read clean status including untracked files and submodules; refuse dirt
   before any checkout mutation. Capture baseline identity for new eligible remote
   non-Code roles in `AgentTaskDispatcher.CaptureProgressBaselineAsync` too.
2. Reuse `ITaskProgressGit.ObserveExactRefAsync`'s endpoint/advertisement/object
   validation and namespaced progress observation. Extend its bounded observation
   support if needed so the sync result always has a task-owned pin even when the
   object was already local. Use `--no-tags --no-write-fetch-head` and one full-ref
   refspec into `refs/antiphon/progress/<full-task-id>/...`, never into the checked-out
   branch. Do not fetch all origin refs or consume FETCH_HEAD. Pins use the existing
   progress-ref retirement ownership and must not accumulate per poll.
3. Observe/fetch without holding a second lease around a helper that acquires its
   own. Then acquire the repository mutation lease tagged with this task and
   `RepositoryLeasePurposes.WorktreeSettlement`; revalidate endpoint, registration,
   HEAD/ref, baseline identity, sequencer and clean status under it. Validate the
   observation pin still names S. No DB transaction is held across network/Git.
4. Require baseline B to be an ancestor of S and current desktop tip L to equal S
   or be an ancestor of S. Rewound remote, desktop-ahead and divergent histories
   refuse; `merge --ff-only` alone would otherwise accept an already-ahead desktop
   and wrongly report its unrelated tip. For eligible remote Code tasks an explicit
   parsed claim C, when present, must be reachable from S and novel against the
   original captured baselines. An unpushed C cannot borrow an earlier pushed S.
5. If L != S, run `git -c merge.autoStash=false merge --ff-only <full-S>` using
   `ILandingGit`, then verify symbolic HEAD, branch tip, clean status and HEAD=S.
   If L == S, verify the same postconditions without another merge. Release the
   lease before any later progress pin or merge-back operation.

All external Git goes through the existing child-journal/timeout path. Give this
whole sync attempt a linked 45-second deadline, distinguish its expiration from
caller cancellation, and await child termination/drained output before returning.
No prompt, detached job, shell interpolation or raw stderr in task events.

An origin tip change after S was pinned does not silently change this evaluation.
Use S throughout, even if the remote later advances. Landing re-observes origin
and enforces its own exact-SHA approval. A changed pin or local checkout during
validation produces uncertainty, never a guessed success.

Rejected: reset/checkout -B, autostash, force-push, merging by FETCH_HEAD, a broad
fetch, and a second checkout or review branch used as the landing owner.

### D-4. Use the same observation in the commit matcher and persisted evidence

For Synchronized, evaluate primary progress against the captured B and prepared S;
do not re-fetch and mix a newer remote tip into the same evaluation. Confirm the
desktop still represents S. A new descendant S is ordinary `Primary` progress,
including when the report contains no progress claim. Preserve claim parsing and
foreign/malformed claim warnings; a valid task-scoped claim must meet D-3's
reachability/novelty test. Do not synthesize a claim from prose or from a report token.
Keep novelty checks against both captured local and remote baselines: merely
materializing a commit already present in captured history is not new work.

Add optional `RemoteSync` data to the existing CompletionProgressEvidence JSON
and progress DTO projection (schema-compatible additive fields; no database
migration). Include task attempt, state, full ref, observed/confirmed SHA and reason.
For Code success `sources[].VerifiedSha` and the public commit equal S. Preserve
ProgressBaselineJson and WorktreeBaseSha byte-for-byte during settlement.

For non-Code remote completions store sync facts without imposing Code's requirement
to produce a new commit: an unchanged Review branch is a valid completed review.
Do not mark unchanged history ProgressObserved to achieve that result; role completion
and the progress assessment are separate, and unchanged evidence grants no merge-back.
Use prepared S for `TryDescribeGitAsync` range end and branch-backed deliverable
resolution. Do not count master HEAD or resolve a different branch after a sync
failure. A sync record is not a Clean Review outcome or publication receipt.

Local evaluation, CARD-0613 alternate evidence, repair-source claim requirements,
and their existing Indeterminate fail-open behavior stay unchanged. The stronger
sync gate below applies only to ordinary runner-bound completions.

### D-5. Explicit failure and retry semantics

| Observation at a completion candidate | Progress / settlement | Recovery |
|---|---|---|
| Own branch has novel S, desktop confirmed at S | Code Succeeded; Primary progress S | Review and land the original owner normally. |
| Own branch exists at B, clean desktop also at B | Code NoAttributedProgress, Failed / CompletedWithoutProgress, reason `runner_no_pushed_progress` | Report says no new pushed commit was observed; it cannot claim the runner has no uncommitted/local work. Caller fixes push and explicitly retries the task. |
| Exact origin branch is definitively missing | Code NoAttributedProgress, Failed / CompletedWithoutProgress, reason `runner_branch_not_pushed` | Preserve report and desktop/mirror. No fallback to stale remote-tracking refs or to another branch. Non-Code cannot use a missing source: Blocked. |
| Valid task claim C exists but is not reachable from fetched S, or is not novel | Code Failed / CompletedWithoutProgress; `runner_reported_commit_not_pushed` or existing `claimed_commit_not_novel` | Identify the claim and observed S in structured evidence; never fetch C as an arbitrary object/ref. |
| Unchanged valid S for Plan/Review/etc. | Synchronization succeeds; existing role classification remains | Do not require a review to author commits. |
| Dirty/off-branch/detached desktop, divergence/rewind/local-ahead, wrong branch namespace, missing/changed identity or endpoint | Refused / Indeterminate; **Blocked**, report retained, no CompletedWithoutProgress | Stable `runner_sync_*` reason and explicit repair instruction. No auto merge, autosave, land or cleanup. |
| Fetch/inspection error, ambiguous response, timeout, unavailable baseline/fingerprint or lease busy | Unavailable / Indeterminate; **Blocked**, report retained | One bounded attempt, no polling loop or automatic agent relaunch. Resolve cause, then use existing `-Reply` to request a new completion report and re-evaluate. |
| Caller cancellation or shutdown before settlement save | Propagate cancellation; no synthetic verdict | Existing open-report sweep re-hands after restart. Reinspection at L=S is idempotent. |

Stable reason examples: `runner_sync_dirty`, `runner_sync_identity_mismatch`,
`runner_sync_branch_mismatch`, `runner_sync_diverged`, `runner_sync_local_ahead`,
`runner_sync_baseline_unavailable`, `runner_sync_endpoint_changed`,
`runner_sync_fetch_unavailable`, `runner_sync_timeout`, `runner_sync_lease_busy`.
Do not infer a missing branch from a generic exit 128. Use exact-ref advertisement
absence (the existing ls-remote exit 2 contract); inspection errors are uncertainty.

This deliberately tightens CARD-0604's warning-only outcome for remote sync refusal:
keep its report-preservation rule, but do not publish Succeeded for an unconfirmed
canonical checkout. Blocked uses the existing workflow and retains session ownership;
it is not a new status, automatic kill or no-progress accusation. Retain the original
report/handoff in Result, emit the reason as a warning/Blocked event, and cap the
structured NextStage to Decide with a repair handoff so the caller does not dispatch
Review against an unknown SHA. A sync block does not set CompletedWithoutProgress.

Replies use existing prompt watermark semantics, not replay of the old done token.
If the blocked session is no longer usable, the caller uses the existing explicit
retry path after repair; no implicit spend or fresh session. Every attempt prepares
again, never reuses a prior RemoteSync success solely from JSON. Preserve original
progress baseline semantics on retries; do not recapture B from S merely to sync.
Historical Failed tasks are not backfilled by deployment. An already-running legacy
remote task without captured source identity blocks with baseline-unavailable;
explicit retry can capture missing identity at dispatch, but must never replace an
existing valid baseline or treat an old synchronization record as fresh evidence.

There is no separate retry scheduler or retry endpoint in this card. Interrupted
Git plus an uncommitted DB settlement recovers through existing child journals and
open-report re-hand. Tests must cover failure after fast-forward and before the
settlement save, and require a single durable completion/outbox result on recovery.

### D-6. Preserve reports, warnings and workspace ownership

Store full sync facts with the settlement, emit a stable task warning on refusal,
and put the source SHA in the successful caller note. Replayed terminal boundaries
produce no second fetch/merge/event/notification. Missing service wiring for an
eligible remote task is Unavailable, not NotApplicable or success.

On blocked/failed sync paths retain desktop and mirror files. No autosave may sweep
desktop dirt into a runner task, and sync itself never removes a mirror. Existing
explicit retirement/publication cleanup stays responsible for removal. Keep
SourceLanding entirely outside this flow, including source preparation and cleanup.

### D-7. Review, landing and cards consume S without taking over ownership

For successful Code completion, the caller reads full S from persisted progress and
uses `delegate.ps1 -Role Review -Worktree -StartRef <S>` (with the normal card and
verification-subject binding). Review gets its own branch. Commission the normal
review evidence for that S; StartRef alone is not review approval.

Land remains `delegate.ps1 -Land <original-Code-task-id> -ExpectedSourceSha <S>`
with any required `-ReviewEvidenceId`. Keep `AgentTaskLandService`,
`AgentTaskLandSourceResolver`, publication receipts and cleanup rules. A remote
advance to S2 between settlement/review and land must refuse stale S approval under
the existing source resolver. No automatic land, owner reassignment, merge-target
change or manual landing through the Review worktree.

Remove the old sync call from `MergeBackAsync`; any explicitly configured merge-back
uses only successfully prepared primary evidence and rechecks identity as today.
The normal task with no MergeTargetRef stays on its branch for explicit Land.
`CardWorkTransitionService` needs tests, not a new algorithm: successful settled
Code moves the eligible card to Review when nothing is open; Failed/Blocked do not
manufacture that move; human moves and Done remain authoritative.

## Implementation slices

Each round starts from the preceding committed work. Commit the red tests, then the
production fix, and push each meaningful slice. Stop at a bounded round boundary
with truthful checkpoint evidence rather than silently extending the dispatch.

| Slice / Code round | Files and change | Tests / completion boundary | Authoring budget |
|---|---|---|---:|
| S1: safe desktop synchronization | `server/Application/Services/RemoteWorkspaceService.cs`; new `server/Application/Dtos/RemoteSettlementSyncDtos.cs`; `server/Application/Interfaces/ITaskProgressGit.cs` and `server/Infrastructure/Git/TaskProgressGit.cs` only as required for a retained exact observation; `server/Program.cs` dependency wiring; update progress-Git fakes if the I/O seam changes. Implement D-2/D-3 and typed result. | New `tests/Antiphon.Tests/Application/RunnerSettlementSyncTests.cs`; update `RemoteWorktreeMirrorTests.cs` fixture/old assertion shape. Real bare origin plus a separate runner clone proves publication is absent from desktop before sync. CP-1/2. | 70 min + ~3 verification, <90 total |
| S2: completion ordering and attribution | `AgentTaskReplyService.cs`, `TaskCompletionProgressService.cs`, `AgentTaskDispatcher.cs`, `server/Application/Dtos/TaskProgressDtos.cs`. Prepare before matcher/file readers, consume one snapshot, capture non-Code remote identity, implement status/reason/NextStage policy and durable sync facts. No generic transcript gate rewrite. | New `RunnerCompletionProgressTests.cs`, `RunnerTaskSettlementTests.cs`, and `tests/Antiphon.Tests/TestHelpers/RunnerSettlementWorld.cs`; reuse patterns from RepairSourceWorld without loosening its local/repair tests. CP-3/4. | 80 min + ~3 verification, <90 total |
| S3: review/land/card/recovery contract | Finish SHA-based report/deliverable projection in `AgentTaskReplyService.cs` if not already in S2; `docs/orchestration-loop.md`, `docs/testing-and-build.md`, `docs/ops-http.md` describe success SHA, blocked recovery and no-push reason. Land/card production changes only for a failure demonstrated by the named capstone; no new protocol. | New `RunnerSettlementWorkflowTests.cs`; regression classes in CP-6. Prove original-owner land, exact Review base, replay, failed-save recovery, card states and non-Code behavior. CP-5/6. | 70 min + ~3 verification, <90 total |

Source paths without a prefix in S2/S3 are under `server/Application/Services/`;
test classes are under `tests/Antiphon.Tests/Application/` unless qualified above.
If a necessary schema/protocol redesign is discovered, report it at the round
boundary; this plan does not authorize expanding into runner custody or landing redesign.

## Verification design

### Harness and red-first discipline

Use actual Git with an isolated bare origin, desktop repository/worktree at B and
a separate clone standing in for the runner. Commit/push only in that clone; assert
desktop HEAD=B and object S absent before exercising production sync. Use local
paths and fake runner directories; no production runner, GitHub, Docker deployment
or paid provider turn. Git helpers must use bounded process execution (LandingGit
or an equivalent timeout/kill/drain wrapper), not unbounded ScratchGitRepo calls.

Database tests use an isolated TestDbFixture schema and the real
`AgentTaskReplyService.OnTurnEndAsync`; seed the actual owning UserPrompt, assistant
report and TurnEnd. Warm the shared fixture before timing the behavioral operation
(the existing CARD-0644 pattern), not by raising behavioral timeouts. Process tests
carry `[ParallelLimiter<ProcessSpawnLimit>]`; no concurrent Pty suite. Mark pure
policy/fake-I/O tests Unit and real Git/database tests Integration/Slow as appropriate.

Before each production slice, add compiling behavioral regressions against the
current production surface. New DTOs/seams may have compatibility scaffolding, but
the red must be the described wrong outcome, never a build/fixture failure. The
red checkpoint records its exact intended failing methods; unchanged negative
guards may already pass. Then implement and run the corresponding green row.
If a row is already green, demonstrate the named regression by temporarily omitting
the guarded production behavior and record that method's assertion failure; do not
count self-comparisons or zero tests. Such an extra control run is reported with its
reason, and its mutation is restored before the green checkpoint.

### Coverage roster and decisive assertions

Names below are prescribed test methods, one execution each (no parameter expansion
needed). Existing regression class counts are determined by the fresh TRX; Min
floors below count only the prescribed new methods, except CP-1/2 include the six
existing mirror tests. Matrix cases inside a method do not increase Min.

| ID | Class.Method | Assertion / expected red mechanism |
|---|---|---|
| V-1 | RunnerSettlementSyncTests.Pushed_tip_fast_forwards_exact_owned_checkout | B -> S after runner-only push; result contains full S, branch and clean desktop; dropping ff leaves HEAD=B. |
| V-2 | RunnerSettlementSyncTests.Rejects_foreign_task_branch_before_fetch | Other task branch, master and option/refspec-shaped values issue no fetch/merge; old helper accepts supplied WorktreeBranch. |
| V-3 | RunnerSettlementSyncTests.Rejects_changed_checkout_identity | Wrong common dir, detached/wrong HEAD and invalid registration never change checkout; old helper lacks identity checks. |
| V-4 | RunnerSettlementSyncTests.Dirty_or_sequenced_checkout_is_preserved | Index, tracked/untracked/submodule dirt and sequencer retained byte-for-byte; no reset/stash/checkout/autosave. |
| V-5 | RunnerSettlementSyncTests.Diverged_rewound_and_local_ahead_tips_refuse | Both histories and files preserved; old merge can report success when desktop is ahead. |
| V-6 | RunnerSettlementSyncTests.Missing_and_unchanged_remote_are_not_fetch_errors | Definite absence/unchanged B have explicit no-push evidence; transport failure is Unavailable. |
| V-7 | RunnerSettlementSyncTests.Pins_exact_ref_without_fetch_head_or_sibling_fetch | Command trace has one allowed full ref; foreign FETCH_HEAD/remote-tracking ref cannot select content; old FETCH_HEAD path violates this. |
| V-8 | RunnerSettlementSyncTests.Lease_contention_never_mutates | Held repository lease causes typed unavailable, no fetch/merge mutation outside lease; existing sync is unleased. |
| V-9 | RunnerSettlementSyncTests.Endpoint_change_is_not_followed | Missing/ambiguous/changed origin is rejected without contacting an unapproved endpoint; reasons contain no endpoint or secret. |
| V-10 | RunnerSettlementSyncTests.Timeout_and_cancellation_await_child_exit | Controlled Git timeout yields Unavailable; caller cancellation propagates; no background child/merge. No wall-clock 45-second sleep in tests. |
| V-11 | RunnerSettlementSyncTests.Unknown_post_merge_head_is_not_success | Read failure after merge yields unavailable, never Synced=true/null; retry reinspection at S succeeds. |
| V-12 | RunnerSettlementSyncTests.Excluded_workspaces_never_sync | Local/Shared/ReadOnly/Mutation/SourceLanding touch no Git even with inconsistent optional runner fields. |
| V-13 | RunnerCompletionProgressTests.Own_pushed_tip_without_claim_is_primary_progress | Matching prepared S produces Primary/VerifiedSha=S; old remote-only matcher rejects absent claim. |
| V-14 | RunnerCompletionProgressTests.Unpushed_claim_cannot_borrow_older_push | C not reachable from S is negative despite prior pushed progress; no arbitrary SHA fetch. |
| V-15 | RunnerCompletionProgressTests.Uses_one_remote_observation | Remote advances to S2 after preparation; evidence/summary still name S and no second fetch occurs. |
| V-16 | RunnerCompletionProgressTests.Direct_evaluation_prepares_remote_source | Calling EvaluateAsync without a supplied result reaches the same sync guard. |
| V-17 | RunnerCompletionProgressTests.Local_and_repair_claim_rules_are_unchanged | Local alternate/repair paths keep origin and authority; missing remote sync service does not affect local tasks. |
| V-18 | RunnerCompletionProgressTests.Sync_failure_cannot_use_desktop_file_rescue | Dirty/unavailable remote sync cannot become primary progress from desktop file timestamps. |
| V-19 | RunnerCompletionProgressTests.Persisted_remote_evidence_round_trips | Additive JSON/DTO includes full S, attempt/state/reason; old JSON still parses with RemoteSync absent. |
| V-20 | RunnerCompletionProgressTests.Missing_remote_sync_dependency_is_unavailable | Eligible remote task with incomplete DI cannot fall through to legacy success. |
| V-21 | RunnerTaskSettlementTests.Runner_push_settles_success_without_claim | Real report -> real reply service -> Succeeded, full S, no zero-progress incident, correct git counts; reproduces card's error before S2. |
| V-22 | RunnerTaskSettlementTests.No_push_settles_with_specific_reason | Missing/unchanged branch and unpushed-claim cases use D-5 reasons, retain raw report/workspace, no merge/publication. |
| V-23 | RunnerTaskSettlementTests.Sync_uncertainty_blocks_and_reply_retries | Transient fetch/lease refusal -> Blocked, Decide handoff, retained report; repair + new prompt/report -> Succeeded with S. Old boundary cannot re-settle after reply. |
| V-24 | RunnerTaskSettlementTests.Refused_sync_never_autosaves_or_releases_workspace | Dirt/divergence/identity refusal produces no autosave/merge/retirement, no CompletedWithoutProgress incident; ownership remains. |
| V-25 | RunnerTaskSettlementTests.Uncorrelated_and_nonfinal_turns_never_fetch | Foreign prompt, interrupted boundary, nudge, explicit blocked/failed reports issue no sync; preserve CARD-0649 gate. |
| V-26 | RunnerTaskSettlementTests.Artifact_and_git_summary_use_fetched_commit | Plan pushed only from clone resolves artifact and SHA from S, summary uses B..S, no master fallback. |
| V-27 | RunnerSettlementWorkflowTests.Review_start_ref_and_owner_land_use_same_sha | Real S locally resolvable; Review admitted/provisioned at S on own branch; land original Code owner with expected S confirms publication at bare origin. |
| V-28 | RunnerSettlementWorkflowTests.Remote_advance_refuses_old_review_approval | Push S2 after Review at S; original owner land at S is refused by existing source resolver, no target publication of S2. |
| V-29 | RunnerSettlementWorkflowTests.Card_moves_only_after_successful_sync | Real transition sweep: successful newest task -> Review; Failed/Blocked and later human move are preserved; no automatic Done. |
| V-30 | RunnerSettlementWorkflowTests.Replay_and_failed_save_settle_once | Inject save failure after B->S; recreate services/re-hand; S confirmed, baseline unchanged, one completion/outbox result; terminal replay does no sync. |
| V-31 | RunnerSettlementWorkflowTests.Remote_review_can_finish_without_new_commit | New remote Review baseline is captured; origin S=B is confirmed, task succeeds without Code no-progress rule. |
| V-32 | RunnerSettlementWorkflowTests.Recovery_does_not_reuse_old_sync_success | Stale previous-attempt sync JSON cannot bypass fresh preparation; repair/retry retains attribution baseline, and sourced/local task regression remains excluded. |

Regression coverage: six existing `RemoteWorktreeMirrorTests` methods (update their
fixture to supply valid identity/lease, keep push tests); `TaskCompletionProgressPolicyTests`
and `TaskCompletionContinuationTests` for claim and CARD-0613 behavior;
`CardWorkTransitionServiceTests` and `AgentTaskLandAdmissionControlledTests` for the
unchanged downstream gates. Do not run entire namespaces or the full assembly.

### Execution and evidence

Use `scripts/run-checkpoint.ps1` with the exact row filter, MinExecuted and comma-separated
Expect class names. Results roots must be fresh, e.g. `.antiphon/c657/CP-2-<sha>`.
Every invocation owns its build and output; build via `dotnet run`/`dotnet build` as
the wrapper does, never `dotnet test`. CP red rows intentionally return test failure
and require the named assertion failures; CP green rows require zero failed/skipped
new methods and execution of every listed existing class. Include per-CP counts,
commit SHA, TRX path and reruns in the Code report. Baseline reds and controls are
evidence, not a claim of green verification.

Keep production/test source frozen during each run. If an unexpected failure appears,
re-run only that failing method at the base to establish inheritance; report that
extra run and reason. Never widen timeouts, loosen assertions or add retries to hide
red. A cold build/fixture may exceed these estimates: report actual elapsed time,
retain coverage and stop between rounds if the dispatch budget is exhausted.

Maintain a producer-owned inventory of the exact `bin-c657-rN/` outputs before/after
builds, verify every resolved cleanup target stays in this worktree, and remove
only this dispatch's output directories after its awaited runs. Never delete
foreign outputs by age or use cross-shell recursive cleanup.

### Cost

Three Code rounds, each <90 minutes: 70/80/70 minutes authoring plus a target of
about 3 minutes verification per round. Ordinary checkpoint floor: **9 estimated
minutes**, including the red and green builds. These are planning estimates, not
measured timings or license to skip a row. Expected dispatch budgets are roughly
73, 83 and 73 minutes. No live server2 canary or broad suite is required in this
ordinary profile: the bug and integration contract are desktop Git/database behavior.
Review may commission a live canary separately; it must not be claimed from these tests.

### Checkpoints

This is the closed list for Code. `S1-tests`, etc. mean committed compiling tests
before that round's production fix. All filters use CARD-0403 trailing class wildcards.
Red rows require intended assertion failures (not all failures); their Min is still
an executed-result floor. If the wrapper Expect option only supports green outcomes,
retain its nonzero exit/TRX as red evidence and report the expected methods explicitly.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-c657-r1/` | sync-red | `/*/*/(RunnerSettlementSyncTests*)|(RemoteWorktreeMirrorTests*)/*` | V-1..V-12; mirror guards | 12 new + 6 existing executed; identity/pin/ahead/null-SHA regressions fail at their stated assertion | 18 | 1.5 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c657-r1/` | sync-green | `/*/*/(RunnerSettlementSyncTests*)|(RemoteWorktreeMirrorTests*)/*` | V-1..V-12; mirror guards | all listed, 0 failed/skipped | 18 | 1.5 |
| CP-3 | S2-tests | `tests/Antiphon.Tests -> bin-c657-r2/` | attribution-red | `/*/*/(RunnerCompletionProgressTests*)|(RunnerTaskSettlementTests*)/*` | V-13..V-26 | 14 new executed; V-13/V-16/V-21/V-23/V-26 fail on old ordering/outcome | 14 | 1.5 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c657-r2/` | attribution-green | `/*/*/(RunnerCompletionProgressTests*)|(RunnerTaskSettlementTests*)|(TaskCompletionProgressPolicyTests*)|(TaskCompletionContinuationTests*)/*` | V-13..V-26; local/repair/alternate regression | all listed classes, 0 failed; 14 new methods, 0 skipped | 14 | 1.5 |
| CP-5 | S3-tests | `tests/Antiphon.Tests -> bin-c657-r3/` | workflow-red | `/*/*/RunnerSettlementWorkflowTests*/*` | V-27..V-32 | 6 new executed; use pre-S2 ordering as named control if S1/S2 already make the capstone green | 6 | 1.5 |
| CP-6 | S1-S3 | `tests/Antiphon.Tests -> bin-c657-r3/` | workflow-green | `/*/*/(RunnerSettlementWorkflowTests*)|(CardWorkTransitionServiceTests*)|(AgentTaskLandAdmissionControlledTests*)/*` | V-27..V-32; card/land regression | all listed classes, 0 failed; 6 new methods, 0 skipped | 6 | 1.5 |
