# CARD-0613: structured continuation bases and accurate completion attribution

Date: 2026-09-23. Stage: Plan, with TestDesign folded in by the brief's explicit
`next: code` instruction. This is an implementation and verification plan, not
implementation evidence.

Source inspected: `884e452b6d52a5a8ad2f9184511e7a99a1ce09bc` on
`feat/card-task-0abce510`. That branch is already checked out in
`C:\Antiphon\worktrees\card-task-0abce510`; Git refused the requested checkout.
This plan is on the assigned `feat/card-task-ac840fc8`, based on that exact commit.
Do not force a second checkout of the occupied branch.

Investigation: [progress detector and sibling bases](../../investigations/2026-09-23-card-0613-progress-detector-sibling-basesha.md).
Owners: [orchestration](../../orchestration-loop.md),
[card lifecycle](../../agent-card-lifecycle.md), [project conventions](../../project-context.md),
[HTTP operations](../../ops-http.md), [testing](../../testing-and-build.md),
[session invariants](../../session-runtime-invariants.md).

## Outcome and boundaries

Expose `delegate.ps1 -Worktree -StartRef <ref-or-sha>` through task creation to
`WorktreeBaseRequestedRef`. Provision the task's own branch at that ref. A caller
continuing sibling work no longer needs to instruct its delegate to change branches.

For already-dispatched or legacy continuation instructions, assess task-scoped claims
against the actual HEAD of the registered checkout. Also recognize new commits on a
task branch reset into a divergent lineage. Record this as alternate local evidence,
without granting automatic merge-back or changing the task's recorded branch/base.

Do not rewrite historical settlements, relabel their bases, merge sibling branches,
change landing eligibility, or deploy as part of Code. A succeeded legacy off-branch
task can still need an explicit integration repair before landing; attribution does
not transfer branch ownership.

## Ground truth

Line references below are at the inspected source commit.

| Card/investigation assumption | What the code/evidence actually does | Consequence |
|---|---|---|
| The base SHA came from an unrelated sibling. | Investigation found all 18 bases on master, with `DefaultBranch` provenance. `DelegationWorktreeService.CreateForTaskAsync` records the created checkout's HEAD once (`:320-329`). | Keep the immutable base recording; fix dispatch and attribution. |
| An explicit start ref is already usable. | Entity `AgentTask.cs:225`, database column (max 300), detail DTO and resolver exist. `CreateAgentTaskRequest` and `AgentTaskService.CreateAsync` do not accept/store it; `delegate.ps1` does not send it. | Add the missing request/write/script path; no migration. |
| Selecting a base should select the branch to work on. | `WorktreeManager.CreateAsync` creates `feat/card-task-<id>` at the selected base. Resolver precedence is Repair > Explicit > MergeTarget > configured default > HEAD. | Preserve unique task branches and keep base separate from merge destination. |
| Off-branch HEAD never reaches the file rescue. | `TaskCompletionProgressService:293` gates the early rescue, but `:321-327` adds a later file-positive arm even after the off-branch negative. | Preserve that rescue and test it; do not claim it is newly enabled. Its current `Primary` origin can incorrectly confer automatic mutation authority off branch. |
| Removing the off-branch short circuit is enough. | `:305` ignores claims, but the local SHA is also read from `source.FullRef`, not the checkout's current HEAD. Claim qualification then requires reachability and baseline lineage. | Observe the actual registered checkout HEAD, retain expected-ref evidence, and qualify alternate evidence explicitly. |
| Novelty means any SHA different from baseline. | `IsNovelCommitAsync:455-466` excludes tips contained in local/remote baselines, then requires the local base to be an ancestor. `EvaluatePrimaryGraphAsync` checks that lineage again; `QualifyClaimAsync` does too. | Add an explicit primary-local fallback and remove the redundant veto only for that qualified path. Do not weaken repair/remote rules globally. |
| The probe's `LastCommitAt` can directly prove the new tip. | `AgentFilesService.ProbeProgressAsync` scans the latest 50 commits; the completion evaluator deliberately does not credit its commit timestamp for snapshotted tasks. | Read metadata for the exact candidate SHA; keep file and graph evidence separate. |
| A positive primary result only affects status. | `AllowsAutomaticWorkspaceMutation` accepts any positive `Primary` source; `AgentTaskReplyService:739-750` calls it on persisted evidence before merge-back. | Alternate positives require a distinct persisted origin; changing only an in-memory boolean is insufficient. |
| Claim syntax is `[antiphon-progress:<id> <sha>]`. | Parser requires a full GUID and full lowercase object ID: `[antiphon-progress:<full-guid> commit=<full-sha>]`, on an unquoted standalone line. | Preserve parser semantics and document the actual syntax. |
| All 18 failures were proven false, at a 34% false-failure rate. | Investigation proves 14 false, leaves 4 unproven. 18/(34+18) is 34.6% progress-failure incidence among those 52 success/progress-failure outcomes; 14/52 is a 26.9% confirmed-false lower bound. Other terminal outcomes are excluded from that denominator. | Use the measured counts; do not inflate the confirmed rate. |
| All stages use this guard. | `TryClassifyCompletedWithoutProgressAsync` applies to Code/Worktree with a dispatch time and worktree path; non-Code and Shared are excluded. | Test the actual Code settlement boundary. |

## Decisions

These are implementation decisions within the commissioned two-part fix, with no
outstanding caller choice.

- **D-1 — Public parameter and storage.** Add nullable optional
  `WorktreeBaseRequestedRef` to the end of `CreateAgentTaskRequest` (JSON
  `worktreeBaseRequestedRef`), write it in `AgentTaskService.CreateAsync`, and expose
  Create-only `-StartRef` in `scripts/delegate.ps1`. Keep the existing response
  projection and column. Omission preserves current behavior. Reject supplied empty
  or whitespace-only values, outer whitespace, control characters, a leading `-`,
  or length over 300 with field-specific validation (`worktree_start_ref_invalid`).
  Do not truncate or silently normalize an invalid selector. This mirrors the
  worktree manager's defensive input handling and the existing database limit.
  Rejected: another column named StartRef, aliases with divergent semantics, or
  parsing a checkout command out of Goal.

- **D-2 — Fresh worktree contract.** StartRef requires an explicitly requested fresh
  Worktree, with no `AgentId`/`Agent` pin or `FollowUpOnTask`. Reject combinations
  with `RepairSourceTaskId` or `SourceLandingOperationId`
  (`worktree_start_ref_mode`) before their admission can cause side effects.
  Support ordinary Worker stages (including Plan and Code) and fresh Orchestrator
  worktrees; do not unnecessarily restrict it to Code. The script mirrors the
  explicit-worktree and incompatible-flag checks before POST. Recheck the resolved
  workspace in the service. Repair and SourceLanding already have authoritative
  structured bases; two competing selectors must not silently ignore one another.
  Existing resolver precedence, including Repair > Explicit for internal callers,
  remains unchanged. Rejected: auto-switching a live follow-up's checkout or silently
  treating StartRef as an agent pin.

- **D-3 — Resolve at provisioning, preserve identity.** Accept a locally resolvable
  Git commit-ish (branch, remote-tracking ref, commit tag, or SHA) under the existing
  repository lease and `WorktreeManager` commit-resolution contract. An absent ref
  or non-commit refuses provisioning; never fall back to master/HEAD for an explicit
  request. No automatic fetch or remote URL input is added. Recommend a full SHA
  already available in the server repository for reproducible continuation. A
  moving branch resolves when the checkout is created. Retry/reuse must retain the
  first recorded base ref/SHA/source and existing work. MergeTargetRef retains its
  current explicit/inherited semantics; StartRef never sets it. Rejected: using
  MergeTargetRef to request a base, or taking over the sibling branch.

- **D-4 — Bind alternate evidence to the registered checkout.** Keep observations
  of the expected local/remote refs. Additionally resolve `HEAD^{commit}` in
  `baseline.Primary.RegisteredCheckout`, not in the main repository and never in
  a prose-supplied path. Confirm that checkout's Git common directory equals the
  captured common directory. Read symbolic HEAD and SHA again after qualification;
  a changed snapshot is Indeterminate (`primary_head_changed`), unless an independent
  valid arm already proves progress. Detached HEAD is a valid alternate candidate;
  a failed symbolic/commit read is unknown, not detached and not no movement. Never
  scan every sibling ref looking for a matching claim. Rejected: replacing
  `source.FullRef` in the baseline or treating the main checkout's HEAD as the task's.

- **D-5 — Off-branch evidence.** Remove the unconditional `no_movement` branch.
  An off-branch or detached checkout can qualify a valid task-scoped claim only
  when the claimed SHA is reachable from the observed checkout HEAD, absent from
  both present baseline histories, and satisfies D-6's time fallback. The claim
  may name an ancestor of a later HEAD; use the claim's time, not the later tip's.
  No claim means no credit for unrelated branch movement. Run the unclaimed local
  expected-ref fast path only when the checkout is on that ref and its observed
  HEAD agrees with the ref tip; a moved expected ref elsewhere cannot bypass the
  off-branch claim requirement. Preserve independent
  dirty-file rescue using `LastFileChangeAt` at the registered path and the captured
  file cutoff; do not substitute `LastCommitAt`. A file-positive result off branch
  or on a known divergent own tip uses alternate origin. Only stable expected-ref
  observations with equal/descendant local history confer Primary file authority;
  unknown local lineage still allows file progress, with alternate authority.
  Expected-ref remote claims retain their existing exact-ref
  corroboration rules even when the local checkout changed branch. Rejected:
  accepting a bare SHA, accepting any old sibling branch solely because it is HEAD,
  or ignoring a good file arm after a graph negative/unavailable result.

- **D-6 — Primary-local novelty after divergence.** Refactor `IsNovelCommitAsync`
  to return enough information to distinguish ancestry-proven progress, qualifying
  alternate progress, no progress, and unavailable evidence. First exclude candidate
  equality/containment in **each** present local and remote baseline. Preserve the
  existing ancestry fast path for the expected local branch, including backdated
  and deep descendant commits. If the candidate is not a descendant of the local
  baseline, qualify the exact candidate by committer timestamp after
  `baseline.CapturedAt` and no later than one captured evaluation `now` from injected
  `TimeProvider`. This fallback is only for the registered primary checkout: own
  branch rewritten into another lineage can qualify without a claim; off-branch
  uses D-5. Do not pass it into repair-source or remote-only qualification.

  Add a typed commit-time operation to `ITaskProgressGit`/`TaskProgressGit`; use
  `git show -s --format=%ct <full-sha> --` through argument-list execution. Validate
  a full object ID, successful exit, exactly one parseable Unix-seconds value and
  range; no log cap or author-date comparison. Use UTC throughout. A timestamp
  second overlapping the capture instant is Indeterminate, since Git lacks
  subsecond precision; an older second is a complete negative. A future timestamp,
  missing/malformed metadata, unknown required ancestry, or an unavailable baseline
  remote makes the fallback Indeterminate rather than a false failure. There is no
  clock-skew allowance or retry that manufactures positive evidence.

  The existing second lineage checks must recognize the alternate result; otherwise
  changing only IsNovelCommitAsync still yields `baseline_lineage_broken`. Prefer a
  separate primary-alternate qualifier shared by the divergent-tip and claim paths,
  leaving `QualifyClaimAsync`'s repair/remote lineage checks intact. Rejected: global
  removal of lineage checks or accepting the file probe's most recent commit time.

- **D-7 — Persist progress without granting integration authority.** Append
  `ProgressOrigin.PrimaryAlternate = 4` without renumbering existing values.
  Alternate positives carry the verified candidate, actual checkout HEAD in
  `LocalObserved`, registered path and a reason distinguishing
  `primary_off_branch_claim`, `primary_divergent_commit`, or
  `primary_off_branch_files`. Append optional `ObservedRef` to stored source evidence
  and its detail projection; null with the alternate reason denotes detached HEAD.
  Keep ordinary expected-ref observations separately if also evaluated. Additive
  JSON fields retain schema version 1 and round-trip old evidence. Neither
  `PrimaryDirectProgress` nor the persisted static mutation predicate may treat
  alternate origin as Primary. If off-branch, even a positive expected-ref arm must
  not authorize mutation of that checkout. A distinct Primary positive must be
  backed by a stable expected checkout/ref observation. No new notification producer
  is required: the existing completion and 'branch left for review' note carry the
  resulting status, and detail evidence explains attribution. Rejected: setting
  only an evaluation boolean that is lost before settlement reloads the JSON.

- **D-8 — Preserve uncertainty and existing rules.** Positive independent evidence
  wins; a negative requires complete applicable observations; otherwise retain the
  existing Indeterminate/fail-open-with-warning settlement. Repository/endpoint
  identity drift still prevents attribution from that source. Cancellation escapes
  without settling. Repair claims, remote-only claims, malformed/foreign/quoted
  claims, legacy no-baseline behavior and Failed/Blocked reports retain their
  contracts. No baseline or evidence backfill, no weakening of no-work failures.

- **D-9 — Explicit limitation of time evidence.** Committer time is an operational
  fallback, not cryptographic proof of authorship or a new ownership grant. A
  deliberately re-dated old divergent commit can meet it; a backdated divergent
  commit may not. Repository binding, reachability, both baseline exclusions and
  the upper/lower time bounds limit accidental misattribution. D-7 contains its
  authority. A repository-wide historical-ref snapshot or reflog custody system
  would be a separate design and is not needed for this fix. Prefer D-1 for future
  work so normal ancestry remains the decisive evidence.

- **D-10 — Rollout.** Code commits each slice, runs the closed checkpoint manifest,
  and hands off to ordinary Review. Land/deploy remain caller-commissioned stages.
  Before relying on StartRef, activate a build containing the fix, confirm
  `/api/version` and the task detail's requested/actual base. An old server can
  ignore an unknown optional JSON property; script recognition alone is not proof.
  Update orchestrator instructions to use StartRef on the new server. Do not
  replay or mass-flip the 18 historical verdicts.

## Implementation slices

### S1 — Expose and exercise structured start refs

Change `server/Application/Dtos/AgentTaskDtos.cs`,
`server/Application/Services/AgentTaskService.cs`, `scripts/delegate.ps1`, and the
stale deferred-feature comment in `server/Domain/Entities/AgentTask.cs`.
Keep `WorktreeBaseResolver.cs` and `DelegationWorktreeService.cs` behavior unless an
end-to-end test exposes a missing connection; do not reimplement their base choice.

Add `tests/Antiphon.Tests/Application/DelegateScriptStartRefTests.cs` and
`WorktreeStartRefDispatchTests.cs`. Exercise the actual script payload and service
create -> database reload -> dispatcher tick -> real worktree -> captured progress
baseline. Include a source branch occupied in another worktree. Extend test helpers
only as needed; do not prove persistence by directly setting the new entity field.

Commit/push S1 with tests authored and verification pending.

### S2 — Qualify alternate local progress and retain mutation boundaries

Change `server/Application/Services/TaskCompletionProgressService.cs`,
`server/Application/Interfaces/ITaskProgressGit.cs`,
`server/Infrastructure/Git/TaskProgressGit.cs`, and
`server/Application/Dtos/TaskProgressDtos.cs` for D-4 through D-8. Thread the baseline
capture time and one evaluation clock through the new qualification path. Keep
application policy separate from Git I/O. Existing DI supplies TimeProvider;
update direct constructor call sites with the existing fixture clock or compatible
optional clock injection.

Extend `tests/Antiphon.Tests/TestHelpers/FakeTaskProgressGit.cs` with explicit
commit times, path-bound HEADs, and typed unavailable outcomes. A missing fake time
is unavailable, never 'now'. `ControlledTaskProgressGit` already intercepts real
Git commands and should inherit the new operation. Add
`tests/Antiphon.Tests/Application/TaskCompletionContinuationTests.cs`; extend
`TaskProgressGitTests.cs` for the native timestamp contract. Preserve all existing
`TaskCompletionProgressPolicyTests`, especially C499_R13 (off-branch movement with
no claim is insufficient), R07 (repair rewrite stays Indeterminate), V12 (backdated
ancestry works), R03/R04 (baseline remote work is not new).

Commit/push S2 with tests authored and verification pending.

### S3 — Prove real settlement and document the caller contract

Add `tests/Antiphon.Tests/Application/ContinuationSettlementTests.cs` for real Git,
isolated PostgreSQL, actual dispatcher capture and `AgentTaskReplyService.OnTurnEndAsync`.
Use/extend `tests/Antiphon.Tests/TestHelpers/RepairSourceWorld.cs` (optional clock,
service configuration and request-created ordinary task) for actual dispatch.
Give profile-v1 delivery cases real notification/flush registrations and the
existing `C544Boundary`/`C544DeliveryFault` hooks from `C544DeliveryRig.cs`. Reuse
`BridgeQueueHarness`/`QueuedReceiptAssertions` for actual recipient submission.
Do not use `C544World.DispatchAsync` as proof of provisioning: it manually marks
the task dispatched and does not capture a Git baseline.

Update `docs/ops-http.md`, `docs/orchestration-loop.md`, and
`server/Bundles/orchestrator.md` with the new continuation idiom, ref availability,
mode exclusions, immutable task branch, and the distinction from RepairSource.
Example: `pwsh -NoProfile -File scripts/delegate.ps1 -Role Code -Worktree -StartRef
<full-sha> -Goal <work>`. For a long brief, load its contents from a file and pass
that string to `-Goal`; this script has no `-GoalFile` parameter. Correct the claimed
token syntax in new prose. Do not edit generated
`docs/cards/` or rewrite the investigation's historical record.

Commit/push S3 before running CP-1. Fix any failing checkpoint in a new committed
slice and rerun only affected rows, recording the reason and rerun count.

## Verification design

### Inspection

Bodies inspected before designing these cases:

| Existing tests/helpers | Boundaries used here |
|---|---|
| `TaskCompletionProgressPolicyTests.cs`, `FakeTaskProgressGit.cs`, `StubWorkspaceProgressProbe` | Graph containment, claim parser, incomplete evidence, repair and primary classification: V-4..V-7, R-3..R-7. |
| `TaskProgressGitTests.cs`, `ControlledTaskProgressGit.cs`, `ScratchGitRepo.cs` | Real commit graph, isolated bare remote, Git fault injection, process environment for dates: V-3, R-3..R-5. |
| `DelegateScriptRepairSourceTests.cs`, `DelegateScriptRunner.cs`, `DelegateCreateStubApi.cs` | Real pwsh parameter binding and captured JSON, zero-POST refusals: V-1, R-1. |
| `RepairSourceDispatchTests.cs` create/dispatch/retry cases and service factory; `WorktreeBaseSelectionTests.cs` precedence, probe, base persistence and round-trip cases | Request persistence, occupied branches, base choice, retries: V-2, R-1/R-2. |
| `RepairSourceWorld.cs`, `RepairSourceSettlementTests.cs` including V17/V19/V25/V26/V38/V38b, R23/R24 | Actual dispatcher/reply path, merge-back boundary, replay and receipt: V-8..V-10, R-6..R-8. |
| `QueuedReceiptAssertions.cs`, `BridgeQueueHarness.cs` configuration; `C544DeliveryRig.cs` caller submission, scanner and fault hooks; `C544World.cs` service/dispatch setup | Real queue versus fake provider distinction and recovery cuts: V-10, R-8. |
| `AgentTaskReplyC544UnifiedCompletionTests.cs`, `VerificationRoundDeliveryTests.cs` refusal recovery example; production settlement/notification producer | Durable task/event/notification/queue identity and whole-prompt receipt: V-10, R-8. |

Missing setup to implement: fixed evaluation clock in continuation tests; exact
committer metadata in the fake graph; path-aware checkout HEAD and common-directory
faults; a request-created ordinary task in the real-dispatch fixture; profile-v1
notification service registration with owned failure hooks. Use
`DelegationTestServices.AddDelegationWorktreeGraph`, isolated schemas, fake adapters
and the assembly-local `ParallelLimiter<ProcessSpawnLimit>` on every Git/pwsh/queue
integration class. No live provider, production runner, browser or user repository.

### Delivery inventory

The queue protocol is unchanged, but a corrected completion status crosses it.
The new capstone must not stop at a database status or queued note.

| Producer/destination | Persistence and recovery | Observable receipt and identity |
|---|---|---|
| `AgentTaskReplyService` classifies an ordinary profile-v1 Code/Worktree report; destination is its captured parent session. | Task status, result, progress JSON, settlement event and `TaskCompletion` obligation commit together. `AgentTaskLandNotificationService` and its hosted scan recover the persisted obligation. | Join task ID -> settlement event ID -> notification ID -> `SessionQueuedMessage.SourceLandNotificationId`/`SourceTaskId` -> intended parent session's whole matching `UserPrompt`. |
| Notification reconciliation enqueues completion; real `SessionMessageQueueService` submits it when the caller is eligible. | Recover before enqueue, failed insert, after insert, lost wakeup and after acceptance before receipt bookkeeping. Duplicate settlement/reconcile retains one obligation/queue identity and one accepted whole prompt. | Fake adapter's **actual OnSubmitted** callback records receipt. Zero writes while busy; later exactly one complete prompt with the corrected succeeded header and report claim. |

Use a profile-v1 task created by the real service, not a legacy seeded row, for
recovery coverage. Fault matrix for `C613_CompletionDeliveryRecovery`: busy and
already eligible callers crossed with `obligation-insert`, `settled-committed`,
`note-insert`, `note-committed`, dropped completion wakeup, and `prompt-accepted`.
Assert the hook fired and the expected durable state before restarting services.
Before settlement commit, replay the actual turn; after commit, run notification
reconciliation/scanning. No handcrafted succeeded evidence or inserted expected
receipt. `C613_CompletionReceipt` covers the no-fault route for both caller states.

Substitutes: in-memory Git is policy evidence only; loopback HTTP proves script
serialization only; the fake adapter proves application-to-submitted-input delivery,
not native ConPTY behavior. No native input code changes, so a new native lane is
excluded. Existing legacy repair receipt tests remain regression coverage.

### Proves it works now

All named C613 methods below are to be implemented. Parameterized rows require
separate outcome assertions and descriptive case labels.

| ID | Class.method | Setup and decisive result |
|---|---|---|
| V-1 | `DelegateScriptStartRefTests.C613_StartRefPostsExactSelector` | Real script with branch/tag/full SHA, Worktree, and optional explicit merge target. Payload has exact `worktreeBaseRequestedRef`; StartRef does not invent a merge target. Also check omission and 422 pass-through. |
| V-2 | `WorktreeStartRefDispatchTests.C613_StartRefCreatesOwnBranchAndBaseline` | API create/service persistence then real dispatcher, with occupied sibling source. Branch/tag/SHA selectors resolve to that commit on the task's own branch; stored requested ref, actual ref/source=Explicit/base SHA and baseline agree. Source checkout unchanged; requested/inherited merge target retained independently. |
| V-3 | `TaskProgressGitTests.C613_ExactCommitterTime` | Exact SHA read with different author and committer dates returns committer UTC time; missing object, nonzero exit, malformed/multiple/out-of-range output are unavailable. Verify both SHA lengths in typed validation; real SHA-256 repository only if local Git supports it, otherwise fake parser case remains mandatory. |
| V-4 | `TaskCompletionContinuationTests.C613_OffBranchClaimQualifiesActualHead` | Own ref stays at B; registered HEAD is sibling C (or later D); valid claim C, divergence before B, C post-capture. ProgressObserved, PrimaryAlternate, verified=C, local observed=D/C, correct observed ref/path. Include detached HEAD. |
| V-5 | `TaskCompletionContinuationTests.C613_DivergentOwnTipIsProgress` | Symbolic HEAD still expected; own tip C diverges before B; no claim, C post-capture. ProgressObserved/PrimaryAlternate, never baseline_lineage_broken or unmatched. Include status probe unavailable to prove exact graph evidence is independent. |
| V-6 | `TaskCompletionContinuationTests.C613_OffBranchFilesRemainProgress` | Own ref unchanged, no claim, dirty-file positive at registered path. ProgressObserved/PrimaryAlternate with mutation disallowed; run with graph negative and metadata unavailable. Empty file arm plus unrelated LastCommitAt alone must not pass. |
| V-7 | `TaskCompletionContinuationTests.C613_AncestryFastPathStillWorks` | Expected branch descendant with old author/committer date and >50 ancestors remains Primary progress without consulting time fallback; ordinary file-positive and explicit merge behavior remain intact. |
| V-8 | `ContinuationSettlementTests.C613_ContinuationCommitsSettleSucceeded` | Actual dispatch at B, create divergent sibling history in scratch repo, then off-branch claimed C or reset expected branch to C without a claim. Clean worktree ensures files cannot mask the defect. Real turn-end persists Succeeded, no failure code/incident, exact evidence/claim and unchanged baseline. Include detached claim. |
| V-9 | `ContinuationSettlementTests.C613_AlternateProgressNeverMerges` | Explicit merge target and alternate claim, divergent own tip (clean and dirty), or off-branch dirty files. Real settlement Succeeded, no Merged event, target/expected/source refs and worktree preserved. Reload JSON and assert mutation predicate false. Repeat OnTurnEnd after provider recreation. |
| V-10 | `ContinuationSettlementTests.C613_CompletionReceipt`, `C613_CompletionDeliveryRecovery` | Actual qualified alternate completion reaches busy/eligible caller, survives the inventory's cuts, retains one identity and whole matching report. Assert received succeeded header, claim, retained-for-review fact, and no false no-progress failure text. |
| V-11 | `TaskCompletionContinuationTests.C613_DivergentOwnTipStillReachesRemoteClaim` | Review follow-up. Own ref reset to divergent C; the claimed commit D is reachable from the EXPECTED REMOTE ref only (run it as the remote tip and as an ancestor of remote tip E). The divergent alternate arm cannot qualify D, so it must not short-circuit: ProgressObserved/PrimaryRemote, verified=claimed=D, remote observed=remote tip, local observed=C, no `claimed_commit_unreachable` verdict, and no merge-back authority. |

### Guards the regression

| ID | Class.method / retained suite | Required boundary |
|---|---|---|
| R-1 | `DelegateScriptStartRefTests.C613_InvalidStartRefStopsBeforePost`; `WorktreeStartRefDispatchTests.C613_StartRefAdmissionMatrix` | Explicit blank, whitespace, leading option, control character and 301 chars rejected; 300 valid chars accepted. Shared/ReadOnly/omitted workspace, pin, follow-up, RepairSource and SourceLanding rejected before rows/launch; direct API also refuses. Worker Plan/Code and Orchestrator Worktree accepted. Missing selector/blob tag refuses provisioning without fallback or brief. |
| R-2 | `WorktreeStartRefDispatchTests.C613_ReusedWorktreeKeepsRecordedBase`; named C508 cases in CP-4 | Move the requested source ref after first provisioning; retry/reuse keeps original recorded base and existing work. Omitted StartRef follows original default/merge precedence; no entity or baseline relabeling. |
| R-3 | `TaskCompletionContinuationTests.C613_LocalBaselineContainmentRejects`; `C613_RemoteBaselineContainmentRejects` | Candidate equals/is ancestor of local or remote captured tip even with a fresh/future committer date: no alternate credit. Remote-baseline case uses local/remote divergence so it cannot be rejected only by the local guard. |
| R-4 | `TaskCompletionContinuationTests.C613_TimeLowerBound`; `C613_TimeUpperBound`; `C613_TimeReadUnavailable` | Clearly pre-capture divergent commit is negative; same-second overlap is Indeterminate; strictly later and <=now qualifies; future date, malformed/read failure is Indeterminate with no mutation. Claimed old ancestor under a newly dated HEAD uses the claim's old timestamp and does not qualify. |
| R-5 | `TaskCompletionContinuationTests.C613_ClaimMustBeReachable`; `C613_OffBranchRequiresClaim`; `C613_RegisteredCheckoutIdentity`; `C613_HeadChangeIsIndeterminate` | Unreachable claim, another task's marker, quoted/conflicting/malformed marker, or no claim on another branch cannot rescue. Claim on an unrelated ref/main checkout but not registered HEAD cannot rescue. Changed common directory and changed observed HEAD/ref are Indeterminate. Probe only the recorded path, not report prose. |
| R-6 | `TaskCompletionContinuationTests.C613_IncompleteEvidenceCannotFail`; `C613_AlternateEvidenceRoundTripsWithoutAuthority` | Unknown ancestry/status/required baseline remote yields Indeterminate unless independent files/normal ancestry prove progress. Cancellation propagates. Old/new JSON and detail projections round-trip; alternate never becomes Primary after reload. |
| R-7 | entire existing `TaskCompletionProgressPolicyTests` and `RepairSourceSettlementTests` | Repair claims still need lineage and explicit claim; remote-only claims still need exact expected-ref corroboration; no other-ref scanning, no future-dated baseline rescue, legacy baseline omission unchanged; Failed/Blocked bypass probing. |
| R-8 | `ContinuationSettlementTests.C613_NoWorkStillFails`; V-9/V-10 replay assertions | On expected or alternate HEAD, complete quiet observations still persist Failed/CompletedWithoutProgress and one incident; corrected success does not mint that incident. Duplicate settlement does not duplicate events, obligations, accepted notes, or mutation. |
| R-9 | `TaskCompletionContinuationTests.C613_DivergentOwnTipWithUnreadableRemoteStaysIndeterminate` | Review follow-up. Divergent own tip whose candidate fails the D-6 lower time bound, with the remote observation unreadable, claimed and unclaimed: Indeterminate `source_remote_unreadable` on an incomplete PrimaryRemote arm, never `primary_commit_predates_dispatch` or any other complete negative, and no mutation authority. The divergent arm must fall through to the remote-unavailable check exactly as D-5's off-branch arm does. |
| R-10 | `TaskCompletionContinuationTests.C613_DivergentNegativeNeverOverridesIncompleteObservation` | Review follow-up, third pass. A divergent own tip whose divergent-arm verdict is a COMPLETE NEGATIVE, with the arm that actually qualifies the claim answering Indeterminate: `baseline_remote_unavailable` (baseline remote unavailable, live remote readable), `source_remote_unreadable` (the reachability read cannot answer) and `baseline_lineage_broken` (the claimed commit IS the observed remote tip, force-pushed off the baseline lineage), plus the same collision on the LOCAL claim arm with an unmoved remote. Every case is Indeterminate on that arm's own origin with the divergent negative absent from the evidence, never `claimed_commit_unreachable`/`primary_commit_predates_dispatch`, and no mutation authority. |

Use fixed baseline/evaluation times and explicit `GIT_AUTHOR_DATE`/
`GIT_COMMITTER_DATE` in real Git fixture commands. Do not sleep to move across the
timestamp boundary or rely on machine-time coincidence. Set tracked dirty file mtimes
explicitly for file cutoff cases. Fixtures must separately prove divergent ancestry
with real Git; a chain B -> C cannot reproduce this incident.

### Guard inventory

G-1..G-23 below enumerate the independently bypassable guards affected by this plan
and their distinct PCs. Existing untouched repair/remote algorithms receive ordinary
R-7 coverage plus PC-18 for the newly introduced fallback's scope boundary. Queue
implementation guards are unchanged; V-10 exercises their handoffs, and PC-20 checks
that corrected verdict production is actually coupled to the recipient assertion.
No untested changed guard is deliberately deferred.

### Positive controls

Mutation runs these after confirmed land, in the commissioned SourceLanding snapshot,
with external evidence and exact restoration. Code authors and runs the ordinary
tests; it does not run PCs. Each row is one distinct compiling defect, one precise
`/*/*/<Class>/<ExactMethod>` filter, red at the stated assertion then green restored.
For parameterized methods all argument cases may run, but no class-wide PC filter.

| Guard | PC | Deliberate defect | Exact test and expected assertion failure |
|---|---|---|---|
| G-1 Request is persisted (D-1) | PC-1 | Drop the service entity assignment. | `WorktreeStartRefDispatchTests/C613_StartRefCreatesOwnBranchAndBaseline`: requested ref/Explicit/base SHA differs after reload/dispatch. |
| G-2 Script actually transports it (D-1) | PC-2 | Omit the payload member while retaining parameter binding. | `DelegateScriptStartRefTests/C613_StartRefPostsExactSelector`: captured JSON lacks selector. |
| G-3 Fresh-worktree mode restriction (D-2) | PC-3 | Skip StartRef's API mode validation. | `WorktreeStartRefDispatchTests/C613_StartRefAdmissionMatrix`: Shared/omitted-workspace request succeeds instead of field-specific refusal. |
| G-4 Input validation before persistence (D-1) | PC-4 | Skip StartRef syntax/length validation at create. | `WorktreeStartRefDispatchTests/C613_StartRefAdmissionMatrix`: invalid input is queued instead of create-time refusal (before provisioning can mask it); use the leading-option row as the decisive assertion. |
| G-5 First base remains immutable (D-3) | PC-5 | Force provisioning to rewrite recorded base on reuse. | `WorktreeStartRefDispatchTests/C613_ReusedWorktreeKeepsRecordedBase`: recorded SHA/ref changes. |
| G-6 Off-branch claims reach qualification (D-5) | PC-6 | Restore the off-branch `no_movement` short circuit. | `TaskCompletionContinuationTests/C613_OffBranchClaimQualifiesActualHead`: ProgressObserved becomes negative. |
| G-7 Divergence is not an automatic veto (D-6) | PC-7 | Restore IsNovel's baseline-ancestor-only return for divergent own tips. | `TaskCompletionContinuationTests/C613_DivergentOwnTipIsProgress`: alternate positive disappears. |
| G-8 File evidence survives off-branch graphs (D-5) | PC-8 | Gate all file positives on expected symbolic branch. | `TaskCompletionContinuationTests/C613_OffBranchFilesRemainProgress`: file-only row loses progress. |
| G-9 Actual checkout reachability (D-4/D-5) | PC-9 | Bypass claim-to-observed-HEAD reachability in alternate qualifier. | `TaskCompletionContinuationTests/C613_ClaimMustBeReachable`: unreachable fresh claim becomes positive. |
| G-10 Off-branch requires a task claim (D-5) | PC-10 | Use current HEAD as the candidate when an off-branch claim is missing. | `TaskCompletionContinuationTests/C613_OffBranchRequiresClaim`: unrelated new branch gets credit without a valid marker. |
| G-11 Local baseline exclusion (D-6) | PC-11 | Omit local-baseline equality/containment for alternate candidates. | `TaskCompletionContinuationTests/C613_LocalBaselineContainmentRejects`: a re-dated contained candidate gets credit. |
| G-12 Remote baseline exclusion (D-6) | PC-12 | Omit remote-baseline containment for alternate candidates. | `TaskCompletionContinuationTests/C613_RemoteBaselineContainmentRejects`: already-remote divergent work gets credit. |
| G-13 Lower time bound (D-6) | PC-13 | Treat all known nonfuture times as after capture. | `TaskCompletionContinuationTests/C613_TimeLowerBound`: clearly old divergent candidate becomes positive. |
| G-14 Upper time bound (D-6) | PC-14 | Remove candidate time <= evaluation-now guard. | `TaskCompletionContinuationTests/C613_TimeUpperBound`: future candidate becomes positive. |
| G-15 Recorded repository binding (D-4) | PC-15 | Skip registered checkout common-directory comparison. | `TaskCompletionContinuationTests/C613_RegisteredCheckoutIdentity`: replacement repo's valid fresh claim gets positive instead of Indeterminate. |
| G-16 Stable observation (D-4) | PC-16 | Skip the final HEAD/ref consistency check. | `TaskCompletionContinuationTests/C613_HeadChangeIsIndeterminate`: moved HEAD result becomes positive. |
| G-17 Missing required evidence is unknown (D-6/D-8) | PC-17 | Coerce alternate qualifier's unavailable result to complete negative. | `TaskCompletionContinuationTests/C613_IncompleteEvidenceCannotFail`: Indeterminate becomes NoAttributedProgress. |
| G-18 Fallback is primary-local only (D-6/D-8) | PC-18 | Apply timestamp fallback to a claimed divergent repair-source commit. | `TaskCompletionContinuationTests/C613_RepairRewriteStaysIndeterminate`: fresh repair rewrite gets ProgressObserved. Add this named R-7 test with explicit timestamp so the mutant cannot pass by unavailable fake data. |
| G-19 Alternate evidence grants no mutation (D-7) | PC-19 | Admit PrimaryAlternate in the persisted mutation predicate. | `ContinuationSettlementTests/C613_AlternateProgressNeverMerges`: predicate false assertion or preserved refs/worktree/no-Merged assertion fails after real settlement/reload. |
| G-20 Correct classification reaches recipient (D-8) | PC-20 | Make the alternate positive result a complete `no_movement` negative at the classification boundary. | `ContinuationSettlementTests/C613_CompletionReceipt`: received succeeded header assertion fails; require assertion failure, not missing fixture/timeout/build error. |
| G-21 Divergent arm does not swallow remote corroboration (D-5/D-6) | PC-21 | Return the divergent alternate result unconditionally again, before the remote arms. | `TaskCompletionContinuationTests/C613_DivergentOwnTipStillReachesRemoteClaim`: the PrimaryRemote positive becomes a `claimed_commit_unreachable` negative. |
| G-22 Divergent arm does not swallow the fail-open remote read (D-6/D-8) | PC-22 | Return the divergent alternate result before the `remote.State == Unavailable` check. | `TaskCompletionContinuationTests/C613_DivergentOwnTipWithUnreadableRemoteStaysIndeterminate`: Indeterminate `source_remote_unreadable` becomes a complete `primary_commit_predates_dispatch` negative. |
| G-23 A divergent complete negative never overrides an incomplete observation (D-6/D-8) | PC-23 | Restore the `viaRemote.Assessment == ProgressObserved` / `divergent is not null` preference so the divergent complete negative wins over an Indeterminate qualified arm (equivalently: delete `PreferLeastCommittal`'s `qualified.Complete`/`NoAttributedProgress` test and return the fallback whenever it exists). | `TaskCompletionContinuationTests/C613_DivergentNegativeNeverOverridesIncompleteObservation`: the Indeterminate assertion fails as `NoAttributedProgress` with reason `claimed_commit_unreachable` (remote shapes) or `primary_commit_predates_dispatch` (the local-arm shape). |

Audit: guards=22, mapped PCs=22, missing=0, duplicate PC mappings=0. Metadata parser
failure variants are part of G-17; separate lower/upper predicates have distinct PCs.
All PC-19/20 fixture assertions must be arranged to reach the decisive predicate or
recipient check, without an earlier redundant success assertion masking it.

### Out of scope

No live data mutation or replay, new landing permission, automatic branch repair,
native terminal changes, remote sibling discovery/fetch policy, UI base picker,
new asynchronous notification protocol, or deployment in Code. No migration is
needed. Existing delivery guards are tested through the corrected outcome; this
card does not redesign their recovery machinery. A full solution/native/E2E run is
not justified for these bounded server/script paths.

### Cost

Estimates, not measured runs: ordinary Code floor is **39 minutes**, the sum of the
CP table. Authoring/fixture work is approximately 120 minutes; dispatch
`-ExpectAbout 160` is reasonable. One isolated build is shared by all rows after
all slices, avoiding six extra full-graph builds (estimated 12-18 minutes saved).
Unit/affected-class selection avoids the unrelated full-assembly/native lanes.

Mutation floor is approximately **65 minutes**: initial isolated build 5;
PC-6..18 (13 method-scoped unit cycles) 26 total including rebuild/restoration;
PC-1..5 (five script/dispatch cycles) 20; PC-19..20 (two settlement/receipt cycles)
14. Ordinary V/R is not repeated wholesale during Mutation. These shared-file
controls run sequentially; no sharding or sub-delegation is required.

### Checkpoints

Closed ordinary Code/Review manifest. Commit all S1-S3 before CP-1; reuse only those
unchanged outputs. Use `scripts/run-checkpoint.ps1`, `-OutputPath bin-c613/`, a fresh
results root under `.antiphon/c613-checkpoints/`, and the exact filters below.
Markdown `\|` in table cells means a literal `|` in the actual argument. For class
groups pass each listed class as comma-separated `-Expect` tokens; CP-4 expects
each listed method, CP-1 uses `-MinExecuted 1` and requires the new continuation
class and retained policy class in the executed roster. Nonzero lane totals alone
do not prove those classes executed.

Report every CP with commit, build outcome, exact filter, executed/passed/failed/
skipped counts, fresh TRX path and reruns. No test skips count as coverage for a
named method. A new test that cannot fail against its production defect is a stub.
Run serially in the foreground, retain exact output inventory, and clean only
owned `bin-c613` directories after verifying resolved paths remain in this worktree.
No edits while a run is in flight. If a named group exceeds a foreground window,
split only its listed classes/methods and report the split; do not drop rows. Any
unlisted build/test needs a reason. Confirm inherited failures with only their exact
methods at the base commit before assigning blame.

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c613/` | unit-policy | `/*/*/*/*[Category=Unit]` | V-4..V-7, R-3..R-7 unit arms | >=1 executed, new `TaskCompletionContinuationTests` and retained `TaskCompletionProgressPolicyTests` present, 0 failed | 7 |
| CP-2 | S1-S3 | CP-1 | script-contract | `/*/*/(DelegateScriptStartRefTests*)\|(DelegateScriptRepairSourceTests*)/*` | V-1, R-1 script arms | both listed classes and all new cases, 0 failed | 2 |
| CP-3 | S1-S3 | CP-1 | dispatch-contract | `/*/*/(WorktreeStartRefDispatchTests*)\|(RepairSourceDispatchTests*)/*` | V-2, R-1/R-2 dispatch, R-7 repair admission | both listed classes and all new cases, 0 failed | 7 |
| CP-4 | S1-S3 | CP-1 | base-selection | `/*/*/WorktreeBaseSelectionTests/(C508_BasePrecedence*)\|(C508_DefaultBranchMatrix*)\|(C508_ProvisioningPreservesFailedDefault*)\|(C508_BaseFieldsUpgradeAndRoundTrip*)\|(C508_ResolverAndCommitProbe*)` | R-2 precedence/persistence | all five methods, 0 failed | 4 |
| CP-5 | S1-S3 | CP-1 | native-git | `/*/*/TaskProgressGitTests/*` | V-3, R-4 native metadata, R-7 remote graph | class including C613 method, 0 failed | 3 |
| CP-6 | S1-S3 | CP-1 | continuation-settlement | `/*/*/ContinuationSettlementTests/*` | V-8..V-10, R-6 persisted authority, R-8 | all new settlement/receipt/recovery methods and argument cases, 0 failed | 9 |
| CP-7 | S1-S3 | CP-1 | repair-settlement-regression | `/*/*/RepairSourceSettlementTests/*` | R-7, R-8 existing replay/receipt | all listed class methods, 0 failed | 7 |
