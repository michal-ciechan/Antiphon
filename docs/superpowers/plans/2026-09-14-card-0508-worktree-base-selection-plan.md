# CARD-0508: Choose a Worktree base deliberately, and say which one

Date: 2026-09-14. Stage: Plan; verification design is a separate TestDesign stage.
Current authority: **Plan amendment B** at the end resolves P-3/P-4 and overrides
the conflicting A-1.1/A-2 producer contracts. The amendment A audit remains the
historical rejection record. **Next: TestDesign; Code gate remains closed.**
Based on Investigate task `571c79f8`
(report: `C:/Antiphon/evidence/card-0508-571c79f8/2026-09-13-card-0508-worktree-card-branch-base.md`)
and checkout `a17ceec3e50fd1c0233eb7f1487d9f7b4a7c9cba`.

## Outcome and scope

A card-bound `-Worktree` dispatch has no base-ref decision at all. Provisioning computes
`task.MergeTargetRef ?? "HEAD"`
([DelegationWorktreeService.cs:169](../../../server/Application/Services/DelegationWorktreeService.cs)),
a root card task persists a null merge target, and so every one of the seven reported
occurrences branched from `HEAD` and only *then* had its card's kept branch compared to
that base, as a `Warning` written after the session had already launched
([AgentTaskDispatcher.cs:651](../../../server/Application/Services/AgentTaskDispatcher.cs)).

Fix the decision, not the warning. One selection step, resolved once at provisioning time
under the repository lease, with a recorded answer and a recorded reason. Three defects
live on that one line:

1. **The reported one.** The card's current kept branch is never a candidate. Text in
   `-Goal` is prose, not a request field, and is read by nothing — confirmed on `f42b6b25`,
   which named the branch and SHA in its opening goal and still got `HEAD`.
2. **A latent one on the same line.** `HEAD` is whatever branch the *main checkout* happens
   to have checked out, not `master`. The owner document already says "from master HEAD"
   ([docs/orchestration-loop.md:159](../../orchestration-loop.md)) and landing already
   defaults to `master` (`MergeTargetRef ?? "master"`,
   [AgentTaskLandService.cs:752](../../../server/Application/Services/AgentTaskLandService.cs)).
   `C:\src\Antiphon` sits on `feat/card-task-4f849a0e` as this plan is written, so base and
   landing target disagree *by construction* today.
3. **The card's "spurious duplicate warning".** Containment is tested with
   `merge-base --is-ancestor`
   ([AgentTaskDispatcher.cs:2892](../../../server/Application/Services/AgentTaskDispatcher.cs)),
   which cannot see a branch that landed by rebase: its commits are on `master` under new
   SHAs, so an already-published docs branch warns forever — and, once selection exists,
   would also compete to be a base.

Out of scope, deliberately: reading anything out of `-Goal`; any mutation of a sibling's
kept branch (no auto-rebase, no auto-merge); any change to where a branch *lands*; the
SourceLanding/Mutation provisioning path, which takes `VerifiedSourceSha` and returns before
the base is used ([DelegationWorktreeService.cs:170-180](../../../server/Application/Services/DelegationWorktreeService.cs));
backfilling base fields on historical rows.

## Ground truth

Verified against `a17ceec3` on 2026-09-14.

| Claim or proposed shortcut | Evidence | Consequence |
|---|---|---|
| The base is literally `MergeTargetRef ?? "HEAD"`, with no other input. | [DelegationWorktreeService.cs:169](../../../server/Application/Services/DelegationWorktreeService.cs); `WorktreeManager` then does `worktree add -b <branch> <path> <baseRef>` ([WorktreeManager.cs:109](../../../server/Infrastructure/Git/WorktreeManager.cs)). | There is exactly one line to change, and it needs an input that does not exist yet. |
| A card-bound root cannot have a merge target. | Assigned from an explicit request, else a same-repo parent's `WorktreeBranch`/target ([AgentTaskService.cs:909-912](../../../server/Application/Services/AgentTaskService.cs)); `delegate.ps1`'s create body has no `mergeTargetRef` member ([delegate.ps1:731-767](../../../scripts/delegate.ps1)). | A root dispatch is *guaranteed* to hit the `HEAD` arm. This is deterministic, not flaky. |
| Reuse `MergeTargetRef` to carry the base. | It is the landing target and part of landing *identity*: `MergeTargetRef ?? "master"` is snapshotted into the operation and re-checked ([AgentTaskLandingProtocol.cs:33](../../../server/Application/Services/AgentTaskLandingProtocol.cs), [:425](../../../server/Application/Services/AgentTaskLandingProtocol.cs), [AgentTaskLandService.cs:752](../../../server/Application/Services/AgentTaskLandService.cs), [WorktreeRemovalEvidence.cs:28](../../../server/Infrastructure/Data/WorktreeRemovalEvidence.cs)). | Setting it to a sibling branch would land the work *on the sibling* and invalidate recorded coordinates. A separate column is required, not a reuse. |
| `MergeTargetRef` is also a gate, not just data. | `MergeTargetRef is not null` refuses SourceLanding admission ([AgentTaskService.cs:196](../../../server/Application/Services/AgentTaskService.cs), [SourceLandingAdmission.cs:26](../../../server/Application/Services/SourceLandingAdmission.cs)) and blocks verification removal authority ([WorktreeRemovalEvidence.cs:48](../../../server/Infrastructure/Data/WorktreeRemovalEvidence.cs)). | Writing a base into it would silently disqualify every card-bound task from the verification lane. |
| Chaining breaks the `git=N commits` header. | The range base prefers the merge target and falls back to `WorktreeBaseSha` ([DelegationGitFacts.cs:24](../../../server/Application/Services/DelegationGitFacts.cs)), which provisioning already records from the created worktree's own HEAD ([DelegationWorktreeService.cs:210](../../../server/Application/Services/DelegationWorktreeService.cs)). | It does not. The recorded base SHA moves with the chosen base, so settlement and the check digest keep agreeing and report only *this* task's commits. No change needed there. |
| The existing guard's verdict is taken under the lease. | It runs at [AgentTaskDispatcher.cs:586](../../../server/Application/Services/AgentTaskDispatcher.cs), before `DispatchOneAsync` acquires the repository lease ([:2977](../../../server/Application/Services/AgentTaskDispatcher.cs)). | A concurrent land can invalidate the ancestry answer between guard and provisioning. Selection must move inside the lease — the fix is also a race fix. |
| "Never from a sibling" was an accident. | It is deliberate policy with a comment ([AgentTaskDispatcher.cs:575-580](../../../server/Application/Services/AgentTaskDispatcher.cs)) and a test that asserts it, `two_top_level_worktree_tasks_on_one_card_both_branch_from_repo_head` ([DelegationWorktreeTests.cs:54](../../../tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs)) — CARD-0215, for linear rebase-back. | The policy must be *changed*, with the reason answered (D-7), not quietly contradicted. |
| That test blocks the fix. | Its first task is never settled (`Status` defaults to `Queued`, [AgentTaskEnums.cs:83](../../../server/Domain/Enums/AgentTaskEnums.cs)) and its worktree is live. It proves only that a *running* sibling is not a base. | Keep the candidate set to `Succeeded`/`Blocked` and the test passes unchanged; its name and comment become false and must be corrected in the same commit. |
| A default branch has to be invented. | `Project.BaseBranch`, default `"master"` ([Project.cs:15](../../../server/Domain/Entities/Project.cs)); `Git:DefaultBranch`, `"master"` in [appsettings.json:10](../../../server/appsettings.json) — but `"main"` as the code default ([GitSettings.cs:9](../../../server/Application/Settings/GitSettings.cs)), which is the trap in this area. | Both already exist. Prefer the project's, then the setting, and never fail a dispatch because a config default does not resolve. |
| An unresolvable base could cut a bad worktree. | `EnsureRefExistsAsync` throws `ValidationException` before `worktree add` and before the metadata write ([WorktreeManager.cs:759](../../../server/Infrastructure/Git/WorktreeManager.cs), called at [:90](../../../server/Infrastructure/Git/WorktreeManager.cs)). | A bad explicit base fails the task with the ref named, before any session row or directory exists. No new validation layer needed. |
| The caller-side workaround is harmless. | A delegate's `git reset --hard origin/<branch>` moves the worktree's symbolic ref, and `TryMergeBackAsync` then refuses with `source_branch_mismatch` ([DelegationWorktreeService.cs:375-376](../../../server/Application/Services/DelegationWorktreeService.cs)) — recorded for `2f303d08`, `6ca44b19`, `f42b6b25`. | The documented workaround actively breaks landing. Caller discipline is not a mitigation here, it is a second defect. |
| `WorktreeBaseGitSession` is the provisioning seam to change. | No such symbol exists anywhere in the tree. | The dispatch brief's guess; the seam is `DelegationWorktreeService.CreateForTaskAsync` plus `DispatchOneAsync`. |

## Decisions

### D-1. New columns. `MergeTargetRef` keeps exactly one meaning

Add to `AgentTask`:

| Column | Meaning |
|---|---|
| `WorktreeBaseRequestedRef` (`string?`, 300) | The base the **caller** asked for (`-BaseRef`, D-6). Written at create, never by the server. Null in S1/S2. |
| `WorktreeBaseRef` (`string?`, 300) | The base actually **used**. Written once at provisioning, then immutable. |
| `WorktreeBaseSource` (`WorktreeBaseSource`) | Why: `Unset` (legacy rows), `Repair`, `Explicit`, `MergeTarget`, `CardCurrent`, `DefaultBranch`, `RepoHead`. |
| `WorktreeBaseTaskId` (`Guid?`) | The task whose branch or SHA was chosen: the sibling when `CardCurrent`, the repair owner when `Repair`. |

Amended by A-1: request and record are **separate** columns, and `Repair` is a source.
The original single `WorktreeBaseRef` was both input and output, so a reused or
re-attempted worktree would re-read its own recorded answer and mislabel it `Explicit`.
`CardCurrent` is reserved by S1 and first written by S3.

`MergeTargetRef` is untouched and remains *only* the landing target. `WorktreeBaseSha`
keeps its current meaning (the created worktree's HEAD SHA) and is now the pinned SHA of
the chosen base.

Migration by CLI only, per [docs/project-context.md:125](../../project-context.md):
`dotnet ef migrations add AddWorktreeBaseSelection --project server`.

### D-2. One precedence order, resolved once

Provisioning takes
`<repair start sha> ?? WorktreeBaseRequestedRef ?? MergeTargetRef ?? <default branch> ?? "HEAD"`
and records the answer in `WorktreeBaseRef`/`WorktreeBaseSource`. In precedence order:

0. **`Repair`** — the repair owner's tip SHA, today's `startAtSha` argument. Amended by
   A-1: this arm already exists in production and outranks everything below it; the
   original formula omitted it.
1. **`Explicit`** — a base the caller chose (D-6).
2. **`MergeTarget`** — a child of a same-repo Worktree parent, exactly as today
   (integration once per level; unchanged).
3. **`CardCurrent`** — the card's current kept branch (D-3). New.
4. **`DefaultBranch`** — `Project.BaseBranch` for the task's project, else
   `Git:DefaultBranch`, else `master`; used only if it resolves in that repo.
5. **`RepoHead`** — `HEAD`, plus a `Warning` naming the default branch that failed to
   resolve. A misconfigured default degrades loudly; it never fails a dispatch.

Levels 4 and 5 replace today's bare `"HEAD"` and are what fix defect 2 for *every*
Worktree task, card-bound or not.

One formula, one implementation: A-1 makes this a single resolver called by **both**
provisioning and the pre-lease dispatch guard, so the guard can no longer observe a
different base than the one the worktree is cut from.

### D-3. What "the card's current kept branch" means

Candidates: same `CardId`, `Workspace = Worktree`, `Status ∈ {Succeeded, Blocked}`,
non-null `WorktreeBranch`, same repository (`DelegationWorktreeService.SharesRepo`), local
branch still present (`KeptBranchExistsAsync`) — i.e. exactly today's candidate query at
[AgentTaskDispatcher.cs:2861-2878](../../../server/Application/Services/AgentTaskDispatcher.cs) —
minus anything already contained in the level-4/5 fallback base.

- **Containment is patch-aware, not ancestry-only.** `git cherry <base> <branch>`: output
  empty, or every line prefixed `-`, means every commit on that branch is already present
  in the base by patch id. This sees a rebase-landed branch that `merge-base --is-ancestor`
  cannot, and it is the whole of defect 3. It replaces `IsAncestorOfBaseAsync` in the
  dispatcher guard and at land time
  ([AgentTaskLandService.cs:456](../../../server/Application/Services/AgentTaskLandService.cs)).
- **Reduce:** drop any candidate contained in another candidate by the same test — a chain
  built by this very fix collapses to its tip automatically.
- **Rank survivors:** sibling task `CreatedAt` descending; tie-break tip commit time
  (`git log -1 --format=%ct`) descending; final tie-break branch name, ordinal. Primary key
  is a stored column, not a git clock. On the reported cards this picks the Code branch over
  the older docs branch, because the Code stage's task is created later.
- **Winner** becomes the base; `WorktreeBaseTaskId` records the sibling.
- **Every other survivor is still divergent work** and still gets today's `Warning` text,
  which is now truthful rather than describing the base the server itself chose.
- **`LandRequestedAt` on any survivor still holds** the dispatch, with today's `HoldDetail`
  wording — unchanged. A ref that is mid-land is not a base.
- **Running siblings (`Queued`/`Dispatched`/`Working`) are never candidates**: a live
  worktree's branch moves underneath. `Failed`/`Canceled` are never candidates either: a
  failed attempt is not the card's current work. Both are recorded refusals, not omissions.

### D-4. Resolve under the repository lease, and warn before the first turn

Move the single evaluation into `DispatchOneAsync`, between lease acquisition
([:2977](../../../server/Application/Services/AgentTaskDispatcher.cs)) and
`BeginTransactionAsync` ([:2992](../../../server/Application/Services/AgentTaskDispatcher.cs)),
and delete the pre-lease call at [:581-608](../../../server/Application/Services/AgentTaskDispatcher.cs).
A hold writes its `Held` event and returns `false` in exactly the shape of the
lease-hold path at [:2979-2988](../../../server/Application/Services/AgentTaskDispatcher.cs) —
before the claim transaction, so no event survives a claim that never happened.

The base decision and any residual divergence warnings are written *inside* the claim
transaction, alongside the `Dispatched` event at
[:3105](../../../server/Application/Services/AgentTaskDispatcher.cs). That keeps the
property the "written after the dispatch" comment at
[:630-651](../../../server/Application/Services/AgentTaskDispatcher.cs) was protecting — a
launch that throws leaves no warning about work that never started — while making the base
visible *before* the delegate's first turn, which is the card's explicit minimum ask.

### D-5. The dispatch event and the parent note say which base and why

`Worktree created at <path> on <branch> from <base> (<source>[, task <short>, N commits])`,
keeping the existing merge-target clause. The `WhenIdle` note to the parent session says
the same thing. An orchestrator then learns the base from the dispatch, not from a delegate
discovering absent code mid-task.

### D-6. `-BaseRef` is the only explicit contract

`delegate.ps1` gains `-BaseRef <ref>` (Create parameter set) → `worktreeBaseRef` on
`CreateAgentTaskRequest`. Refused locally and at create alongside `-SourceLanding`, matching
the existing merge-target refusal at
[AgentTaskService.cs:196](../../../server/Application/Services/AgentTaskService.cs).
Nothing is ever parsed out of `-Goal`.

### D-7. The chained-land consequence is stated, not engineered away

CARD-0215's reason was a linear rebase-back. Chaining changes that honestly: a branch cut
from a sibling contains the sibling's commits, so landing it publishes them too, and the
sibling's own later land then finds nothing beyond the target and cleans up —
`NothingToMerge`, already implemented at
[DelegationWorktreeService.cs:401-413](../../../server/Application/Services/DelegationWorktreeService.cs).
Two consequences to carry deliberately:

- The land-time `unlanded-sibling=<id>:<branch>` token
  ([AgentTaskLandService.cs:454-466](../../../server/Application/Services/AgentTaskLandService.cs))
  stops firing for a chained predecessor — for the right reason, because its commits really
  are in the rebased HEAD.
- The completion header's `branch <X> left for review`
  ([AgentTaskReplyService.cs:1401](../../../server/Application/Services/AgentTaskReplyService.cs))
  becomes `branch <X> (from <base>) left for review`, so the orchestrator knows one land
  covers two branches before it orders one.

### D-8. A base behind the default branch is reported, never repaired

Chaining can leave the new worktree missing default-branch commits. Record a `Warning`
with `git rev-list --count <base>..<defaultBranch>`. Do not merge (this repo forbids merge
commits) and do not rebase the sibling's commits into the new branch — rewriting commits
that still belong to a kept branch guarantees a conflict at that branch's own land.

### D-9. Documentation

- [docs/orchestration-loop.md:159](../../orchestration-loop.md): replace the
  "merge target, or master HEAD, never a sibling (CARD-0215)" sentence with the D-2
  precedence, the patch-aware containment rule, and the fact that holds are unchanged.
  "Land a Plan before dispatching Execute" becomes a convenience, not a correctness
  requirement.
- Same document, §5's `unlanded-sibling=` paragraph (lines 600-608): the token now means
  *not present by patch id*, so a rebase-landed branch no longer appears.
- [docs/antiphon-api.md](../../antiphon-api.md): `worktreeBaseRef` on create; the new detail
  fields; that `mergeTargetRef` is a landing target and never a base.
- No `AGENTS.md` line: this removes a caller obligation rather than adding one.

### D-10. Rejected alternatives

| Rejected | Why |
|---|---|
| Set `MergeTargetRef` to the sibling branch. | Lands the work on the sibling, invalidates snapshotted landing coordinates, and silently disqualifies the task from SourceLanding (ground truth rows 3-4). |
| Parse the branch out of `-Goal`. | Two occurrences (`6ca44b19`, `f42b6b25`) name the branch in the goal and still got `HEAD`. The goal is prose; making it a routing input is worse than the bug. |
| Keep `HEAD`, just warn earlier. | The card's minimum ask, but it leaves the wrong worktree standing, and the caller-side repair it forces breaks landing with `source_branch_mismatch`. |
| Auto-rebase the new branch onto the default branch after chaining. | Either drops the sibling's commits (a zero-commit branch rebased onto master is just master) or rewrites them, which conflicts at the sibling's land. |
| Ancestry-only containment. | Keeps the already-landed docs-branch warning the card reports, and would let that branch compete for selection. |
| Resolve the base at create time. | A queued task can wait through a land; the answer would be stale, and create holds no repository lease. |
| Hold on divergence instead of ranking. | Divergent kept branches are the norm today — CARD-0499 and CARD-0502 each had two — so holding would stall Execute behind a superseded docs branch, the exact failure CARD-0146 S4's warn-and-dispatch choice avoids. |
| Chain from a running sibling. | Its branch moves under the new worktree; `two_top_level_worktree_tasks_on_one_card_both_branch_from_repo_head` is right about that case and stays green. |

### D-11. Preserve the configured default's identity through a failed probe

The resolver takes `DefaultBranchProbe(Ref, ResolvesToCommit)`, not a nullable
successful ref. B-1 defines both callers and every output arm. A failed probe
is data about a named candidate, so losing the name is unnecessary and makes
the warning contract impossible. Reject deriving the name from ambient config
inside the pure resolver or hard-coding `master` in its fallback output.

### D-12. The committed dispatch owns its original warning intent

Save immutable per-warning intent in the successful claim transaction, anchored
to that claim's final `Dispatched` event ID. Materialize the Warning/notification
pair separately from that intent (B-2/B-3). A commit, including one whose
acknowledgement is lost, creates the obligation; later launch failure does not
cancel it. A held, refused or rolled-back claim creates none. This replaces
A-2.3's stronger promise about launch exceptions: the warning describes the
committed worktree/base observation, not successful native startup.

### D-13. Add producer custody, reuse the delivery machine

One new `AgentTaskDispatchWarningIntents` table and a scoped materializer retain
the original IDs, destination, detail, body and digest. Extend the existing
notification hosted service to scan this table before its notification scan.
Do not add another queue, receipt matcher or hosted worker. A separate producer
checkpoint preserves A-2's atomic event/note projection while closing the
earlier claim-to-warning gap. B-5 records the rejected alternatives.

### D-14. Recovery never consults moving task or Git state

Replay only frozen intent. Task status, current attempt counter, current parent,
ref movement, requeue and land are neither prerequisites nor replacement inputs.
Serialize projection on the intent row; commit event, note and materialized
marker together. Preserve unresolved intent through retention and show aged or
failed materialization in the existing dispatch-warning attention kind (B-3).

### D-15. S1/S2 scope is unchanged

The guard still evaluates before the repository lease. Capture its result at
claim time without re-running it; compare only observed and recorded ref names
for S1b. Same-ref SHA drift and hold races remain S3 limitations. No sibling
selection, ranking, chaining or new landing target is authorized. These are
implementation decisions within the commissioned amendment, not pending human
scope defaults.

### D-16. TestDesign must reopen the verification audit

B-4 supplies the missing PC-60/61 seams and names affected tests. TestDesign
must adapt controls whose producer moved, add custody/recovery guards, and
recompute the complete ordinary-plus-PC floor. Existing estimates are a lower
bound, not an approved complete budget; this Plan dispatch runs no product tests.

## Implementation slices

| Slice | Content | Files |
|---|---|---|
| S1 | Columns + migration + `WorktreeBaseResolver` (A-1), the shared D-2 chain used by provisioning **and** the pre-lease guard (`IOptions<GitSettings>` injected optionally so direct constructions keep working), base recorded on the row, base named in the `Dispatched` event, detail DTO fields, contract fixture regenerated. Fixes defect 2 on its own. | `AgentTask.cs`, `server/Migrations/*`, `AppDbContext.cs`, new `WorktreeBaseResolver.cs`, `DelegationWorktreeService.cs`, `AgentTaskDispatcher.cs` (guard base + event text), `AgentTaskDtos.cs`, `AgentTaskService.cs` (`ToDetail`), `client/src/test/fixtures/contract/agent-task-detail.json` |
| S1b | Guard's observed base carried on `SiblingBaseGuard`, compared against the recorded `WorktreeBaseRef` when capturing the successful claim's intent (B-2); one `base-observation-stale` warning when they differ. | `AgentTaskDispatcher.cs`, new `DispatchBaseWarningIntentService.cs` |
| S2 | Patch-aware containment helper on `DelegationWorktreeService` (`git cherry`), replacing `IsAncestorOfBaseAsync` at both call sites. Fixes defect 3 on its own. | `DelegationWorktreeService.cs`, `AgentTaskDispatcher.cs`, `AgentTaskLandService.cs` |
| S2b | A-2 event/note delivery, plus B-2/B-3 claim-time warning intent and recovery before event/note commit; pending-intent attention and retention. `RequestId` nullable, `DispatchBase` kind, immutable payload, existing queue/receipt worker. B-6 gives the complete added file/test slices. | `AgentTaskLandNotification.cs`, new `AgentTaskDispatchWarningIntent.cs`, `LandingEnums.cs`, `AppDbContext.cs`, `server/Migrations/*`, `AgentTaskDispatcher.cs`, new `DispatchBaseWarningIntentService.cs`, new `DispatchBaseNotificationPayload.cs`, `AgentTaskLandNotificationHostedService.cs`, `AgentTaskLandNotificationService.cs`, `Program.cs`, `AttentionService.cs`, `DataRetentionService.cs`, `AttentionDtos.cs`, `client/src/api/attention.ts`, `client/src/features/attention/attentionVisuals.ts` |
| S3 | `WorktreeBaseSelector`: candidate query, reduce, rank, hold; wired into `DispatchOneAsync` under the lease; pre-lease guard call deleted; warnings moved into the claim transaction; `WorktreeBaseTaskId` recorded. Fixes defect 1. | `AgentTaskDispatcher.cs`, new `server/Application/Services/WorktreeBaseSelector.cs` |
| S4 | `-BaseRef` end to end, with the SourceLanding refusal. | `scripts/delegate.ps1`, `AgentTaskDtos.cs`, `AgentTaskService.cs` |
| S5 | Header and warning wording: `(from <base>)` on left-for-review; base-behind-default warning (D-8). | `AgentTaskReplyService.cs`, `AgentTaskDispatcher.cs` |
| S6 | Docs (D-9). | `docs/orchestration-loop.md`, `docs/antiphon-api.md` |

S1 and S2 are independently shippable and independently valuable. S1b depends on S1;
S2b depends on S2. S3 depends on S1+S2. S4/S5 depend on S3; S6 follows.
This release is S1 + S1b + S2 + S2b.

## Acceptance cases for TestDesign

Existing tests whose assertions change — all in the same commit as their slice, none
deleted:

1. `DelegationWorktreeTests.two_top_level_worktree_tasks_on_one_card_both_branch_from_repo_head`
   ([:54](../../../tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs)) — assertions
   unchanged, renamed to `a_running_sibling_is_never_a_base`, comment corrected.
2. `AgentTaskDispatchBaseGuardTests.a_kept_sibling_with_no_land_dispatches_with_a_warning_and_whenidle_note`
   ([:115](../../../tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.cs)) —
   **inverts**: the sibling becomes the base, so there is no warning; the note names the
   chosen base. Becomes `a_kept_sibling_with_no_land_becomes_the_base_and_the_note_names_it`.
3. `AgentTaskDispatchBaseGuardTests.a_stranded_request_row_with_a_null_column_only_warns`
   ([:186](../../../tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.cs)) —
   still no hold, but now a base rather than a warning.
4. `a_sibling_land_in_flight_holds_until_the_base_contains_it`,
   `a_test_design_worktree_is_held_while_its_card_plan_land_is_in_flight`,
   `a_sibling_whose_branch_was_deleted_is_silent` — unchanged, and are the fence proving the
   hold semantics survived.

New, in `tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs` (real git,
`[Category("Integration")] [Category("Slow")] [ParallelLimiter<ProcessSpawnLimit>]`):

5. `a_worktree_without_a_merge_target_branches_from_the_default_branch_not_checked_out_head`
   — main checkout left on a feature branch; the new worktree starts at `master` (defect 2).
6. `an_unresolvable_default_branch_falls_back_to_head_with_a_warning` — misconfiguration
   degrades, never fails.
7. `a_settled_kept_sibling_becomes_the_base_and_is_recorded` — `WorktreeBaseRef`,
   `WorktreeBaseSource = CardCurrent`, `WorktreeBaseTaskId`, and the sibling tip an ancestor
   of the new branch.
8. `an_explicit_base_ref_wins_over_the_card_current_branch`.
9. `an_unresolvable_explicit_base_leaves_no_directory_branch_or_registration` — same
   assertions as the existing `a_failed_worktree_add_leaves_no_registration_branch_or_directory`
   ([:196](../../../tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs)).
10. `a_sibling_whose_commits_already_landed_by_rebase_is_neither_base_nor_warning` — commit
    on the branch, cherry-pick it onto `master`, leave the branch in place: the reported
    false positive, reproduced and then silent (defect 3).
11. `the_newest_of_two_divergent_kept_siblings_is_the_base_and_the_other_is_warned` — the
    CARD-0499 shape, one docs branch and one Code branch.
12. `a_base_behind_the_default_branch_is_warned_with_the_missing_commit_count`.
13. `a_chained_branch_land_publishes_its_predecessor_and_the_predecessor_land_reports_nothing_to_merge`
    — D-7's consequence, asserted rather than assumed.
14. `source_landing_provisioning_ignores_base_selection` — the `VerifiedSourceSha` arm is
    untouched and `WorktreeBaseSha == SourceLandingSha` still holds.

New, in `tests/Antiphon.Tests/Application/WorktreeBaseSelectionTests.cs` (selection rules;
git-backed only where containment is asserted):

15. Candidates exclude `Queued`/`Dispatched`/`Working`/`Failed`/`Canceled` siblings.
16. Cross-repo siblings excluded (`SharesRepo` both directions).
17. A candidate contained in another candidate is dropped.
18. Ranking is `CreatedAt` desc, then tip time, then branch name — and is deterministic
    across two runs on identical inputs.
19. A survivor with `LandRequestedAt` holds, with today's `HoldDetail` text.
20. A task that already has `WorktreePath` is never re-based (re-dispatch and adoption).

New, in `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.cs`:

21. The `Dispatched` event names base and source, and exists before any `AgentSession` row.
22. The parent session's `WhenIdle` note names the chosen base.
23. Selection happens under the repository lease: a land taken between the old guard point
    and provisioning cannot change the verdict (the race that exists today).

Contract and script:

24. `ContractSnapshotTests` passes against the regenerated `agent-task-detail.json`.
25. `DelegateScriptKindTests`-style case: `-BaseRef` sends `worktreeBaseRef`;
    `-BaseRef` with `-SourceLanding` is refused locally without a round trip.

Execution note for Code: `Antiphon.Tests` is ~25.5 min, so run
`--treenode-filter "/*/Antiphon.Tests.Application/*/*"` for these and the full assembly
once at the end.

## Delivery and decisions remaining

Two decisions belong to the orchestrator, not to Code:

- **Does the default-branch fallback (D-2 level 4) apply to every Worktree task, or only to
  card-bound ones?** Planned: every task, because landing already assumes `master` and the
  owner doc already claims `master`. Narrower alternative: card-bound only, leaving
  non-card Worktree dispatches on `HEAD`. The wide version is a behaviour change for
  dispatches nobody complained about; it is also the only version where base and landing
  target agree.
- **Slice order S1/S2 versus S3.** S1 and S2 each fix a real defect alone and are low risk;
  S3 carries the policy reversal and the D-7 consequence. Shipping S1+S2 first buys a
  correct base (`master`, not a stray feature branch) and a silent already-landed sibling
  before any chaining exists.

Not in this plan: goal parsing; mutation of any kept branch; landing-target changes;
historical backfill; scoping or changing the SourceLanding lane.

Next stage: **testdesign**.


## Verification design

Appended by TestDesign task 1963c81d on 2026-09-14 after fetching and resetting
this task worktree to origin/master, aa18eb0e. The fix design above is unchanged.
The commissioning brief resolves the two open scope decisions: the default branch
applies to **every Worktree task**, and this release contains **S1 + S2 only**.
S3, and its dependent S4/S5 behavior, are deferred. Do not invert acceptance
cases 2/3 or implement the chained-land test in this release.

**Design-review verdict: return to Plan, not Code.** The ordinary checks below
are concrete, but P-1/P-2 expose seams the current slice description does not
make verifiable. In particular, no successful queue insert can substitute for
parent receipt. The readiness audit below deliberately does not claim that the
Code handoff gate has passed.

### Inspection

Read bodies at aa18eb0e, not just test names:

- DelegationWorktreeTests: creation, two top-level tasks, healing, failed-add
  rollback, adoption, NewTask/CreateService/ExpectedCoordinates; ScratchGitRepo
  including its environment-aware GitInAsync; DelegationTestServices including
  both AddDelegationWorktreeGraph and CreateGitGraph; TestDbFixture and
  IsolatedTestSchema | real refs, process ownership, direct construction,
  migrated isolated database, retry/adoption -> V-1..V-6, R-1/R-2.
- AgentTaskDispatchBaseGuardTests: all six CARD-0215/0146 tests, C499_V06,
  SeedKeptSiblingAsync, SeedQueuedWorktreeTaskAsync, SeedParentSessionAsync,
  SeedCardAsync, CreateProvider | active versus stranded land requests,
  missing branch, divergent sibling, parent queue, Code/TestDesign roles ->
  V-7/V-8, R-3, P-1/P-2.
- AgentTaskLandStageOutcomeTests: land_warns_when_a_same_card_kept_branch_is_not_an_ancestor,
  land_is_silent_when_the_sibling_was_landed_first, and their
  SeedSucceededWorktreeAsync/CreateLand/RequestHeadAsync/card helpers |
  real publication with a retained sibling -> V-9, R-4.
- PostLandMutationWorktreeTests: C478_V02_RebasedSnapshotAfterSourceRemoval,
  C478_V03_CreateRestartAndMissingCommit, C478_G056_ExactL; nearest
  PostLandMutationWorld.CreateAsync/Request in PostLandMutationCustodyTests |
  verified-source provisioning -> V-5. Its default runner is fake; it proves
  Git/admission isolation, not native recipient receipt.
- ContractSnapshotTests: Delegated_task_board_and_drawer_contracts,
  Task/Event, SnapshotAsync/Scrub/FixturesDir; the entire
  agent-task-detail.json fixture; SharedApp.GetAsync and
  AntiphonAppFixture.InitializeAsync/StartHostAsync | persisted API projection,
  fixed IDs, snapshot capture-versus-compare -> V-10.
- DelegateScriptKindTests: Kind_Grok_is_posted_as_agentKind,
  an_omitted_Kind_sends_no_agentKind_at_all,
  an_undelegatable_Kind_is_refused_by_the_script_before_any_request,
  RunDelegateAsync and StubApi; DelegateScriptRunner and DelegateCreateStubApi;
  delegate.ps1 Create parameter/body handling | real pwsh/HTTP transport ->
  R-5; acceptance 25 excluded with S4, no BaseRef cases named for this release.
- AgentTaskLandDeliveryE2ETests: C467_V22 through V32 and
  C498_FailureOutcomeReachesCaller; LandDeliveryFixture.InitializeAsync,
  RequestAsync, ReceiptAsync, AssertOnePromptAsync, UseChildAsync,
  RunChildAsync, KillChildAsync, SnapshotAsync, ArrangeOutcomeAsync,
  UntilAsync/DisposeAsync; LandDeliveryOptions including FileBoundary |
  real Program, queue, isolated runner, native FakeGrok, hard server cuts ->
  V-11..V-14. This is the nearest fixture for any new delivery test.
- AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts, six C488 receipt
  wrappers and SeedAsync; AgentTaskLandNotificationRecoveryTests
  .C467_V09_KeyedQueueRacesAndDistinctEvents and
  .C467_V08_RetryAndDestinationMatrix/FailedInsert;
  AgentTaskLandNotificationPersistenceTests.C488_ApprovalOutcomeTransactionAtomic |
  false receipts, uniqueness, retry, atomicity -> R-6 and PCs 27..39.
  BridgeQueueHarness.CreateAsync/OnSubmitted/InsertEntryAsync and
  LandingSafetyHarness.BuildServices/SaveFault/TransactionFault were also read.
  These explicitly seed evidence and are component substitutes, not end-to-end receipt.
- Production inspection: DelegationWorktreeService.CreateForTaskAsync,
  IsAncestorOfBaseAsync, KeptBranchExistsAsync, SharesRepo;
  WorktreeManager.CreateAsync/EnsureRefExistsAsync; dispatcher sibling
  evaluation/warning/claim/session creation; land sibling collection,
  terminal notification producer; LandNotificationPayload,
  AgentTaskLandNotificationService.ReconcileAsync and
  SessionMessageQueueService.EnqueueAsync's key/insert/idle branches,
  LateConfirmAttemptedMessagesAsync and PromptSubmissionMatch identity/completeness bodies |
  concrete mutation locations and P-1/P-2.

Required owners read for this work: project-context, testing-and-build
(especially isolated output, per-method PCs, fixture graph and delivery),
orchestration-loop stage/dispatch/landing contracts, and session-runtime-invariants
delivery/receipt sections. No source or test implementation is changed here.

Acceptance-case disposition (numbers refer to the original list above):

| Case | This release |
|---|---|
| 1 | R-1; retain assertions. Rename/comment correction may say default branch; do not imply settled siblings now chain. |
| 2 | R-3 unchanged warn-and-dispatch contract; **inversion deferred to S3**. |
| 3 | R-3 unchanged stranded-event warning contract; **inversion deferred to S3**. |
| 4 | R-3, including both Code and TestDesign land holds and deleted branch. |
| 5 | V-1 for card/non-card, project/global, and different checked-out HEAD. |
| 6 | V-2; resolved fallback SHA and durable Warning naming the failed default. |
| 7 | Deferred S3; no CardCurrent base. |
| 8 | Deferred S3/S4; V-3 only tests S1's internally supplied recorded ref, not a new request contract. |
| 9 | V-3 internally supplied invalid ref; public -BaseRef path deferred S4. |
| 10 | V-7/V-9 patch-equivalent sibling silence; no sibling-selection algorithm is introduced. |
| 11 | Deferred S3 ranking. |
| 12 | Deferred S5; distinct from S1's unresolved-default Warning. |
| 13 | Deferred S3/D-7; no chained-land verification designed here. |
| 14 | V-5. |
| 15 | No selector tests: S3 deferred. R-3 retains existing eligibility; V-7 tests Succeeded and Blocked for the changed containment predicate. |
| 16 | No new selector tests: S3 deferred. Existing SharesRepo filtering remains; no repository-policy change. |
| 17 | Deferred S3 reduction. |
| 18 | Deferred S3 ranking/tie-breaks. |
| 19 | R-3 active land flag remains authoritative; no selector-survivor rules yet. |
| 20 | V-4 protects S1's immutable recorded decision during reuse; S3 adoption selection is deferred. |
| 21 | V-8 event content and persistence before launch; distinguish tracking order from external commit visibility below. |
| 22 | Deferred D-5 chosen-base parent note. Existing divergent-sibling note is still a changed emission path in S2 -> V-14/P-2. |
| 23 | S3 moving the sibling decision under the lease is deferred. V-6 protects existing provisioning lease; P-1 identifies the S1/S2 base mismatch without implementing S3. |
| 24 | V-10. |
| 25 | Deferred S4 per the explicit S1/S2 release; R-5 checks existing caller compatibility only. |

Missing setup that Code must supply once Plan clears the seams:

1. New WorktreeBaseSelectionTests must use the nearest real-Git creation fixture
   above, Category Integration and ParallelLimiter<ProcessSpawnLimit>. It is
   an S1 fallback/S2 containment fixture, not a WorktreeBaseSelector fixture.
   Use isolated stores for dispatcher/persistence tests. Register the Git graph
   through DelegationTestServices. CreateGitGraph currently constructs the
   service directly without passing GitSettings to it; the S1 optional-options
   constructor change must be reflected there. Passing options only to
   WorktreeManager cannot test selection settings.
2. Existing SeedQueuedWorktreeTaskAsync sets CardId but not ProjectId. Explicitly
   set task.ProjectId to the seeded project for project-default cases. Also
   test null ProjectId and null CardId independently. Do not infer project
   defaults from a board the service never loaded.
3. Distinct ref tips are mandatory: common seed B; master M; configured trunk T;
   project release P; explicit E; merge target C; checked-out feature H. Assert
   all tested tips differ before provisioning. For cherry equivalence, add a
   separate default-branch commit before cherry-picking so the published SHA
   genuinely differs; assert ancestry false and native cherry minus output
   before invoking the application. Avoid an accidental fast-forward fixture.
4. Fresh migration coverage needs a disposable database migrated to the immediate
   predecessor of AddWorktreeBaseSelection, a seeded historical task, then the
   CLI-generated migration. TestDbFixture's already-migrated clone alone cannot
   prove upgrading historical rows. No historical backfill is expected.
5. The existing contract drawer snapshots a ReadOnly task, so merely regenerating
   it only exercises null/Unset fields. Retain that fixture and add explicit HTTP
   assertions on a seeded Worktree task with non-null ref/source/SHA. No
   CardCurrent fixture is needed. A capture run is not a comparison run.
6. LandDeliveryFixture currently seeds no CardId. Add an arrangement helper that
   attaches the existing landing task and a retained sibling to a fixture-owned
   card/project, then builds the real patch histories. Keep the fixture's request,
   land, notification, queue and native receipt path. No wrapper that calls the
   old C467 method without this arrangement satisfies CARD-0508.
7. Native tests require Windows, staged FakeGrok and modern ConPTY, Docker
   Postgres, SDK from global.json, git, pwsh, and rebuilt client/dist.
   AntiphonAppFixture owns a random runner; assert its port is not 17204.
   Keep child server/runner/DB ownership through crash cuts. Never use the
   production runner or a live messaging broker.
8. Dispatch-warning receipt/crash arrangement is missing: the current dispatcher
   provider seeds a Running row but no native process and drains no delivery
   queue. Reuse the native caller portion of LandDeliveryFixture only after
   Plan supplies P-2's producer identity/recovery boundary. Do not label its
   current SessionQueuedMessages assertion recipient evidence.

### Delivery inventory

| Path | Producer -> destination | Persistence and durable identity | Recovery and observable receipt |
|---|---|---|---|
| S1 worktree-created Dispatched event | DispatchOneAsync -> task detail/event reader | AgentTask.Id plus AgentTaskEvent.Id; decision and event committed with the claim | V-8/V-10 read through a fresh context/HTTP. This is a persisted query result, not session input. SignalR invalidation alone proves nothing; refresh must expose the recorded tuple. S1 does not add a parent note. |
| S2 divergent-sibling dispatch warning | EvaluateCardSiblingBaseAsync -> WarnUnlandedSiblingsAsync -> parent's WhenIdle queue -> native caller | Event has its own ID; queue gets SourceTaskId and conversation key task:{taskId:N}, but no event ID, content digest, or keyed warning obligation | **P-2 gap:** queue saves in a separate scope, then warning events save. Enqueue exceptions are swallowed. After successful dispatch the task is no longer selected as Queued, so a later tick does not reconstruct this warning. Neither a unique per-warning identity nor producer recovery exists. V-14 must end in the matching complete UserPrompt after its attempt floor. |
| S2 land outcome containing/suppressing unlanded-sibling= | CollectUnlandedSiblingsAsync -> terminal event/notification -> notification worker -> session queue -> native caller | Request.Id -> event.Id -> AgentTaskLandNotification.Id -> SourceLandNotificationId/QueueMessageId -> destination session and ConfirmingPromptSequence; frozen body and digest | Existing atomic terminal obligation, keyed insert, scans/retry and transcript catch-up. V-11..V-13 must read matching complete UserPrompt, not just Landed/ConfirmedAt/queue status. SourceEventId and request/task/operation identities must agree. |

Land handoff coverage, all using the changed sibling arrangement:

- Producer before terminal commit: roll back the event and obligation together,
  restart the producer, then obtain one native receipt (V-12).
- Terminal commit before enqueue: hard-kill at terminal-committed, restart only
  the owned server, recover the same notification; publication is not repeated
  (V-12).
- Enqueue fails before insert: two FileBoundary enqueue-errors failures,
  preserve the same notification, then recover without another request (V-12).
- Queue insert before QueueMessageId save: kill at queue-inserted while busy;
  restart, reuse the same keyed row, release caller and prove one receipt (V-12).
- Lost flush after insertion: drop completion wakeup on an already idle caller;
  scan must deliver without new human input (V-12).
- Attempt persisted before typing: use queue-before-typing, kill/restart,
  verify eventual complete receipt from the same row (V-12).
- Native prompt imported before verdict/receipt save: cuts queue-before-verdict
  and receipt-before-save; restart/catch-up, same confirming prompt, no second
  native submission after two additional notification scans (V-12).
- Receipt persisted: restart and scan twice; receipt remains correlated and no
  replay occurs (V-12).
- Both ordinary busy and already eligible recipients are exercised through real
  producers (V-11), not by manually inserting a successful notification.

V-14 must cover the corresponding dispatch-to-warning-intent, intent-to-queue,
queue-to-typing and receipt-save boundaries, plus enqueue failure, busy/idle,
two divergent siblings on one task, and a second dispatch attempt. P-2 must first
define which durable identity distinguishes those warnings. A shared task ID is
not sufficient. Do not fabricate that identity in a test-only outbox.

Substitutes: ScratchGitRepo proves real Git content, not delivery;
MockEventBus proves only published in-process payloads; seeded transcript
receipt tests prove the matcher and persistence, not transport; native FakeGrok
proves the production queue/runner/native transcript path for the selected
profile, not the behavior of a live hosted model. An HTTP 202, queue insertion,
terminal event, Sent flag, screen, or transport acknowledgement cannot satisfy
delivery acceptance. Ordinary Review must reject evidence that ends at any of
those substitutes without the recipient transcript.

Plan return items (no human scope decision is pending). **Both are resolved by
Plan amendment A at the end of this document — P-1 by A-1, P-2 by A-2.** The original
statements are kept verbatim so the gap and its answer stay readable together:

- **P-1: align the S1/S2 base observation.** The guard still chooses
  task.MergeTargetRef ?? "HEAD" before provisioning; S1 changes provisioning's
  base. With sibling patches only in checked-out H, the guard can become silent
  while the new worktree at M lacks them; with patches only in M it can warn or
  hold unnecessarily. Specify how both use the actual S1 base while retaining
  the S3 deferral, and how the pre-lease observation's limitations are reported.
  Also account for the current startAtSha repair override, absent from the
  original S1 formula: retain its priority and specify its recorded source.
- **P-2: give the changed dispatch-warning path a durable producer seam.**
  Specify event/obligation identity, destination/body snapshot, atomic commit,
  idempotent queue handoff, recovery scan, receipt evidence and injectable
  crash boundaries. The existing catch-and-log cannot pass required recovery.
  This is an implementation-design decision, not permission to treat delivery
  as optional or to pull S3 chaining into this release.

### Proves it works now

All new names below are test implementation requirements, not claims that tests
already exist or passed. DW = DelegationWorktreeTests; BS =
WorktreeBaseSelectionTests; DG = AgentTaskDispatchBaseGuardTests; LS =
AgentTaskLandStageOutcomeTests; LD = AgentTaskLandDeliveryE2ETests; CS =
ContractSnapshotTests. Exact-method filters expand the class/method names in
these rows, never the V label itself.

| ID | Behavior / layer | Test / setup | Expected decisive result |
|---|---|---|---|
| V-1 | Deliberate base / real git + dispatcher persistence | BS.C508_DefaultBranchMatrix: card present/absent crossed with project P, global T, no options (master); H checked out in every row. Also project P missing with T present, global T missing with master present. | First six rows create at P/T/M respectively. Missing chosen default uses H, not a second configured candidate. Stored Ref/Source/SHA agree; CardCurrent/TaskId never invented. Eight rows. |
| V-2 | Loud fallback / real dispatcher | DG.C508_MissingDefaultWarns: invalid project default and invalid global default; then detached HEAD with a valid commit. | Dispatch succeeds at exact H; one default-resolution Warning names failed ref and HEAD fallback. Event/row say RepoHead. Missing HEAD/unborn repo fails without a session; it cannot create from no commit. |
| V-3 | Precedence/invalid input / real git | BS.C508_BasePrecedence: E+C+P, C+P, E only, invalid E with valid C/P, invalid C with valid P. Ref supplied internally on AgentTask; S4 API is absent. | E wins first/third, C second; MergeTargetRef stays byte-identical (including null). Last two throw ValidationException naming ref before branch/directory/registration. No silent fallback from deliberate input. |
| V-4 | Decision survives retry / git + fresh DB | DW.C508_ReuseKeepsRecordedBase: provision from M, commit task work, advance M and change project default; re-dispatch with path present, then adoption with path/branch cleared. Also legacy row with no recorded tuple. | Existing task tip/index/content preserved; recorded Ref/Source/TaskId/SHA remain original, not new HEAD. No fabricated historical decision. Path-present dispatch never calls creation. |
| V-5 | Special lanes unchanged / real git/admission | PostLandMutationWorktreeTests.C508_SourceLandingIgnoresDefault: use PostLandMutationWorld with provision:false, valid request and verified L, advance master, configure missing default, provision; DW.C508_RepairStartShaWins uses startAtSha at a distinct commit. | Snapshot HEAD and WorktreeBaseSha equal L; no default lookup warning. Repair branch starts exactly at supplied repair SHA, even with a distinct merge target/default. Plan must specify repair source label (P-1). |
| V-6 | Leased provisioning / real lease | DW.C508_BaseResolutionRequiresOwnedLease: occupied genuine common-directory lease, foreign lease, then valid released lease. Move default tip before acquisition. Wrap the real IWorktreeManager in a call-recording decorator for the foreign-lease assertion. | No worktree or recorded decision for occupied/foreign lease; foreign lease is refused before calling the manager (call count zero). Successful creation uses tip observed after acquiring the legitimate lease. The decorator observes the service fence separately from the manager's refusal. No S3 race-fix claim. |
| V-7 | Patch containment / real git and dispatcher | BS.C508_CherryContainmentMatrix: ancestor/empty, two rebased minus lines, plus-only, minus+plus, empty branch/base name, missing branch, missing base, missing directory. DG.C508_RebasedSiblingUsesActualDefault: Succeeded/Blocked crossed with equivalent/divergent and H containing/not containing sibling patches. | Empty/all-minus true; any plus or failed/invalid probe false. Dispatcher silence is based on actual M, not H; divergent branch still warns/holds appropriately. P-1 must clear before these dispatcher rows can be green. |
| V-8 | Base is inspectable / claim persistence | DG.C508_DispatchedEventRecordsBaseBeforeLaunch: exact M and source in creation event; capture change-tracker ordering at session Add using a test interceptor, plus fresh-context read after commit and before draining launch queue. Repeat with failure before claim commit. | Event is staged before session Add; committed event/tuple visible before launch; failed claim leaves neither event nor session. Do not assert a separately committed event exists before any session DB row: existing claim transaction commits both together. |
| V-9 | Both containment call sites / real land | LS.C508_RebasedSiblingMarkerMatrix: retained sibling all-minus versus minus+plus; build task is independently cut from M, never sibling. Add a component row invoking CollectUnlandedSiblingsAsync through reflection with verifiedSha=M and a scratch HEAD containing an additional sibling patch; this row supplies real git/DB inputs but is not a full land. | All-minus produces no marker/warning; mixed still includes exact sibling short ID/branch. Frozen verified SHA, not a later moving HEAD, determines result. Remote target has intended build content; sibling branch remains unmodified. |
| V-10 | Upgrade/DTO contract / PostgreSQL + HTTP | BS.C508_BaseFieldsUpgradeAndRoundTrip; CS.Delegated_task_board_and_drawer_contracts extended with Worktree HTTP assertions and regenerated legacy drawer fixture. | Legacy null/Unset/null, 300-character Ref roundtrip, Ref=master/Source=DefaultBranch/TaskId=null/SHA exact on new task, unchanged merge target. Stable fixture comparison on second run; assert all three new JSON field names explicitly. |
| V-11 | Changed land outcome reaches caller / native E2E | LD.C508_SiblingOutcomeReachesCaller: equivalent and mixed sibling histories crossed with busy=false/true. Arrange before RequestAsync. | Four real receipts; expected marker absent/present in complete queued body AND native UserPrompt. Busy has zero typing attempts before release; eligible receives without new input. Exactly one prompt per notification. |
| V-12 | Every land handoff recovers / native E2E | LD.C508_SiblingOutcomeRecovery: nine cuts described in Delivery inventory, mixed sibling payload in every row. Reuse existing barriers; add pre-commit and post-confirm restart arrangements. | Same request/event/notification/queue identities where already durable; marker survives complete receipt; failed pre-commit attempt leaves no partial event/obligation; one native submission; publication not repeated during notification recovery. |
| V-13 | False receipt rejection / component + native evidence | Existing AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts (15 rows) plus LD.C508_SiblingOutcomeReachesCaller. | Only complete, flattened-complete and within-tolerance full prompts confirm. Wrong destination/ID/kind, head/splice, old sequence/time, Sent/screen flags remain unconfirmed. Component evidence never replaces V-11. |
| V-14 | Changed dispatch warning reaches/reaches-again only when owed / native E2E | New DG-adjacent native test class DispatchBaseWarningDeliveryE2ETests.C508_WarningProducerToReceipt and .C508_WarningRecovery. Nearest fixture is LandDeliveryFixture; arrange real dispatcher producer, not seeded warning rows. | Busy/idle receipt and all dispatch handoffs above, complete warning body including sibling branch/tip, one native submission per durable warning. **Not implementable from the current fix design until P-2 is resolved.** |

### Guards the regression

- R-1: running sibling remains excluded | DW.two_top_level_worktree_tasks_on_one_card_both_branch_from_repo_head
  (renamed as case 1 permits), exact second HEAD equals default tip and sibling
  tip is not its ancestor; existing
  a_worktree_is_created_from_the_merge_target_and_recorded_on_the_task stays green.
- R-2: failure/recovery preserves Git ownership | DW
  .a_failed_worktree_add_leaves_no_registration_branch_or_directory,
  .a_leftover_worktree_from_a_previous_attempt_is_adopted_not_an_error,
  .healing_re_attaches_the_task_branch_and_keeps_its_commits,
  .failed_creation_preserves_unknown_hook_content; no weakening existing
  registration, branch, index or retained-byte assertions.
- R-3: CARD-0215 policy is preserved in this release | DG
  .a_kept_sibling_with_no_land_dispatches_with_a_warning_and_whenidle_note and
  .a_stranded_request_row_with_a_null_column_only_warns keep their current
  assertions; also retain both named land-hold tests, deleted-branch silence
  and C499_V06_ARepairIsHeldWhileItsOwnerIsLanding. Queue-note assertions are
  component regressions only; V-14 is the recipient requirement.
- R-4: divergent land warning still protects caller awareness | LS
  .land_warns_when_a_same_card_kept_branch_is_not_an_ancestor and
  .land_is_silent_when_the_sibling_was_landed_first retain marker presence/
  absence; new V-9 specifically leaves a rebased equivalent branch present,
  unlike a land that cleans up the sibling.
- R-5: existing script request remains compatible | DelegateScriptKindTests
  .Kind_Grok_is_posted_as_agentKind,
  .an_omitted_Kind_sends_no_agentKind_at_all,
  .an_undelegatable_Kind_is_refused_by_the_script_before_any_request.
  Assert captured JSON and zero HTTP requests on refusal. No script change is
  commissioned by S1/S2.
- R-6: land delivery foundations remain intact | named C467/C488 methods in
  Inspection, including atomic outcome, keyed queue race/destination collision,
  retry/destination matrix and false receipt matrix. These are ordinary tests
  in Code; their controls run only after land in Mutation.
  Add AgentTaskLandNotificationPersistenceTests.C508_AfterSaveRollsBackOutcome:
  the same read harness with TerminalCut="after-save" must throw
  InjectedSaveFailure and leave zero terminal events and notifications in a
  fresh context; restart without the cut then produces exactly one of each.

### Guard inventory

Scope: base-selection/recording and the changed warning/outcome paths. Unchanged
Git cleanup authority, provider authentication, quota and all other runtime
guards are outside this diff; their owning batteries are not relabeled as new
CARD-0508 guards. Cheap empty-input prechecks in the containment helper are
covered by V-7; the decisive safety fence is failed Git inspection never
establishes containment (G-21).

| Guard | Plan reference and independently bypassable assertion | Control |
|---|---|---|
| G-1 | S1/D-2: internally supplied WorktreeBaseRef wins | PC-1 |
| G-2 | S1/D-2: merge target wins over defaults | PC-2 |
| G-3 | D-1: selecting a base never changes MergeTargetRef | PC-3 |
| G-4 | D-2: project default wins over global | PC-4 |
| G-5 | D-2: global default wins over hard master | PC-5 |
| G-6 | S1 direct constructor without options uses master | PC-6 |
| G-7 | S1: default fallback applies with and without CardId | PC-7 |
| G-8 | D-1: recorded SHA is exact chosen creation base | PC-8 |
| G-9 | D-1: recorded Source describes the selected arm | PC-9 |
| G-10 | D-1: recorded Ref identifies that arm | PC-10 |
| G-11 | S1/S3 boundary: no automatic sibling task ID is fabricated | PC-11 |
| G-12 | D-2: unresolved chosen default emits Warning | PC-12 |
| G-13 | D-2: bad deliberate base refuses before creation | PC-13 |
| G-14 | D-1/case 20: reuse does not overwrite recorded decision | PC-14 |
| G-15 | Explicit exclusion/case 14: SourceLanding uses verified L | PC-15 |
| G-16 | Existing CARD-0499 override: repair startAtSha keeps priority | PC-16 |
| G-17 | D-4/S1: creation requires owned repository lease | PC-17 |
| G-18 | S2: empty successful cherry output is contained | PC-18 |
| G-19 | S2: all-minus successful cherry output is contained | PC-19 |
| G-20 | S2: any plus means uncontained, even with minus lines | PC-20 |
| G-21 | S2: failed Git inspection never means contained | PC-21 |
| G-22 | S1/S2/P-1: dispatch containment uses the actual base | PC-22 |
| G-23 | S2 land call: compare to pinned verified SHA with patch awareness | PC-23 |
| G-24 | D-3 unchanged: uncontained sibling with active LandRequestedAt holds | PC-24 |
| G-25 | D-3 unchanged: stale LandRequested event alone does not hold | PC-25 |
| G-26 | Delivery: terminal event owes an atomic durable land notification | PC-26 |
| G-27 | Delivery: land queue handoff retains notification identity | PC-27 |
| G-28 | Delivery: enqueue failure retains an owed retry | PC-28 |
| G-29 | Delivery: only UserPrompt evidence can confirm | PC-29 |
| G-30 | Delivery: evidence belongs to the destination session | PC-30 |
| G-31 | Delivery: prompt carries matching notification identity | PC-31 |
| G-32 | Delivery: matching body is complete | PC-32 |
| G-33 | Delivery: sequence must be above attempt floor | PC-33 |
| G-34 | Delivery: timestamp floor applies without sequence | PC-34 |
| G-35 | Delivery: busy caller is not typed into | PC-35 |
| G-36 | Delivery: already eligible caller recovers lost wakeup | PC-36 |
| G-37 | Delivery: native receipt survives metadata-save failure without retyping | PC-37 |
| G-38 | Delivery: keyed queue rejects crossed destination/digest | PC-38 |
| G-39 | Delivery: failed terminal save rolls back notification with event | PC-39 |
| G-40 | P-2 missing guard: dispatch warning has durable producer obligation before handoff | PC-40 |
| G-41 | P-2 missing guard: retries distinguish warning identity from task/report identity | PC-41 |
| G-42 | P-2 missing guard: dispatch warning recovers before enqueue and after ambiguous insertion | PC-42 |

There is no "no guards" justification: these assertions protect which code a
delegate executes and whether the caller actually learns about omitted work.

### Positive controls

Each executable row describes a compiling production defect; tests/expected
values are not mutated. Restore source, refresh timestamps/rebuild, then run
the same exact method green. PC-1..25 target tests above; PC-26..39 use the
read delivery methods. Do not batch shared-file mutations.

| PC | Break the matching G by this compiling defect | Exact method; intended red assertion |
|---|---|---|
| PC-1 | Omit WorktreeBaseRef from S1's precedence expression | BS.C508_BasePrecedence; E row HEAD must equal E, not C |
| PC-2 | Omit MergeTargetRef from that expression | BS.C508_BasePrecedence; C row HEAD equals C |
| PC-3 | Assign task.MergeTargetRef = selected base during provisioning | BS.C508_BasePrecedence; merge target remains original/null |
| PC-4 | Prefer options default to project default | BS.C508_DefaultBranchMatrix; project rows HEAD equals P |
| PC-5 | Replace non-null configured default with master | BS.C508_DefaultBranchMatrix; global rows HEAD equals T |
| PC-6 | Use main instead of master when options are absent | BS.C508_DefaultBranchMatrix; no-options rows HEAD equals M (H distinct) |
| PC-7 | Apply default-branch selection only when CardId is non-null | BS.C508_DefaultBranchMatrix; non-card rows HEAD equals chosen default |
| PC-8 | Record main checkout HEAD instead of created base SHA | BS.C508_DefaultBranchMatrix; stored SHA equals created worktree HEAD, not H |
| PC-9 | Record RepoHead for successful DefaultBranch selection | BS.C508_DefaultBranchMatrix; Source equals DefaultBranch |
| PC-10 | Record HEAD instead of resolved default ref | BS.C508_DefaultBranchMatrix; Ref equals project/global/master name |
| PC-11 | Set WorktreeBaseTaskId to task.Id for default selection | BS.C508_DefaultBranchMatrix; TaskId is null |
| PC-12 | Omit fallback Warning event insertion | DG.C508_MissingDefaultWarns; one ref-naming Warning exists |
| PC-13 | Replace an unresolvable deliberate base with HEAD before CreateAsync | BS.C508_BasePrecedence; ValidationException for invalid E/C (not directory success) |
| PC-14 | On adoption overwrite WorktreeBaseSha with reused worktree HEAD | DW.C508_ReuseKeepsRecordedBase; original base SHA unchanged after task commit |
| PC-15 | Pass current repo HEAD to CreateVerificationAsync instead of source.VerifiedSourceSha | PostLandMutationWorktreeTests.C508_SourceLandingIgnoresDefault; snapshot HEAD equals L |
| PC-16 | Drop startAtSha from precedence | DW.C508_RepairStartShaWins; HEAD equals repair SHA |
| PC-17 | Remove only DelegationWorktreeService's failed-Owns throw; leave the manager guard intact | DW.C508_BaseResolutionRequiresOwnedLease; after catching the foreign-lease refusal, manager call count must still be zero (mutant is one). The decorator exposes the independently bypassed service fence. |
| PC-18 | Return false for successful empty cherry output | BS.C508_CherryContainmentMatrix; ancestor row true |
| PC-19 | Restore ancestry-only helper implementation | BS.C508_CherryContainmentMatrix; non-ancestor all-minus row true |
| PC-20 | Use Any(minus) in place of All(minus) | BS.C508_CherryContainmentMatrix; mixed row false |
| PC-21 | Return true on unsuccessful git cherry | BS.C508_CherryContainmentMatrix; missing-ref row false |
| PC-22 | Force dispatcher comparison baseRef to HEAD | DG.C508_RebasedSiblingUsesActualDefault; H-only patches still warn while M-only patches are silent |
| PC-23 | Replace the land call with ancestry-only comparison | LS.C508_RebasedSiblingMarkerMatrix; all-minus marker absent. Separate variant: replace verifiedSha with moving HEAD; verified-base row marker remains present. Both variants required. |
| PC-24 | Skip the LandRequestedAt hold arm | DG.a_sibling_land_in_flight_holds_until_the_base_contains_it; first status remains Queued and WorktreePath null |
| PC-25 | Treat any persisted LandRequested event as active land regardless of null column | DG.a_stranded_request_row_with_a_null_column_only_warns; status Dispatched and Held count zero |
| PC-26 | Remove AddNotification from terminal land producer | LD.C508_SiblingOutcomeRecovery; terminal-committed row has one owed notification and later one complete recipient prompt |
| PC-27 | Pass a new Guid as sourceLandNotificationId on each reconcile enqueue | LD.C508_SiblingOutcomeRecovery; queue-insert cut reuses original queue ID and one native prompt |
| PC-28 | Return from ReconcileAsync whenever EnqueueAttempts > 0 and QueueMessageId is null | LD.C508_SiblingOutcomeRecovery; enqueue-errors row eventually has one complete recipient prompt |
| PC-29 | Permit QueuedUserPrompt alongside UserPrompt in receipt query | AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts; queued-prompt row ConfirmedAt null |
| PC-30 | Remove destination session predicate from receipt query | AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsDestination; ConfirmedAt null |
| PC-31 | In the receipt predicate, pass row.Body after its first newline to both IsConfirmedBy and IsCompleteIn, discarding the identifying header while retaining the full outcome-body check | AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsIdentity; wrong-identity prompt leaves ConfirmedAt null. Both header-sensitive comparisons are altered at this single predicate to avoid masking. |
| PC-32 | Remove IsCompleteIn conjunct | AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsCompleteBody; ConfirmedAt null |
| PC-33 | Remove sequence-floor restriction | AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsSequenceFloor; ConfirmedAt null |
| PC-34 | Remove timestamp-floor restriction | AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsTimeFloor; ConfirmedAt null |
| PC-35 | Force ordinary queue working verdict false at delivery eligibility | LD.C508_SiblingOutcomeReachesCaller; busy row DeliveryAttempts remains zero before release |
| PC-36 | Disable completion scan as well as deliberately dropped wakeup | LD.C508_SiblingOutcomeRecovery; lost-flush row obtains complete prompt without human input |
| PC-37 | Make LateConfirmAttemptedMessagesAsync return LateConfirmCounts.Empty before examining attempted rows | LD.C508_SiblingOutcomeRecovery; queue-before-verdict cut restores the existing native receipt without a second native prompt. Require the duplicate-count assertion, not a timeout in another cut. |
| PC-38 | Omit destination/digest conflict checks on existing keyed queue row | AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents; crossed destination throws ConflictException |
| PC-39 | In CompleteTerminalLockedAsync commit and dispose the open transaction before terminal SaveChangesAsync instead of afterwards, so its implicit transaction survives the after-save injected exception | AgentTaskLandNotificationPersistenceTests.C508_AfterSaveRollsBackOutcome; fresh-context event and notification counts are zero after the injected failure |
| PC-40 | **Not executable:** no dispatch-warning obligation insertion exists to remove | DispatchBaseWarningDeliveryE2ETests.C508_WarningRecovery; post-dispatch/pre-enqueue crash must still produce complete receipt |
| PC-41 | **Not executable:** no durable warning key exists to corrupt | DispatchBaseWarningDeliveryE2ETests.C508_WarningProducerToReceipt; two siblings and retry give one receipt per distinct warning |
| PC-42 | **Not executable:** no producer recovery worker exists to disable | DispatchBaseWarningDeliveryE2ETests.C508_WarningRecovery; enqueue failure/ambiguous insertion recover automatically to one complete receipt |

The last three rows are explicit failed design checks, not placeholder
mutations and not instructions to improvise production architecture in Mutation.
PC-17 observes the service's manager-call boundary separately from the manager
guard. PC-31 drops the identity header from both comparisons; merely removing
IsConfirmedBy is equivalent because completeness still checks the header.
Do not substitute masked controls. The missing producer controls are why next
is Plan rather than Code.

Mutation reports break, intended assertion red, restore, fresh build and green
after the reviewed implementation lands. Code implements tests and runs all
ordinary V/R. Separate ordinary Review judges those results and this pending
inventory before land. No positive controls were executed in this documentation
task.

### Out of scope

- S3 candidate selection/reduction/ranking, its CARD-0215 policy reversal, the
  two test inversions and chained-land consequence: explicitly deferred by caller.
- S4 public BaseRef request/script, S5 base-behind-default and completion-header
  changes: dependent on deferred S3 in the landed slice table. Do not silently
  pull them into Code because acceptance 25 mentions them.
- S6 wording claiming sibling selection: cannot ship with S1/S2. Necessary
  documentation may describe only default-base recording and patch containment.
- Goal parsing, automatic sibling mutation, landing-target changes, historical
  backfill and new SourceLanding policy: original exclusions remain.
- Hosted-model live canaries, browser visual changes and unrelated provider
  transport matrices: no UI/native framing change; isolated native queue
  receipt is the required delivery proof here.
- Squash/semantic equivalence and merge-commit analysis beyond git cherry:
  S2 promises Git patch equivalence, not semantic equality. Cases use linear
  commits as this repository's no-merge policy requires.
- No tests/builds were run by TestDesign. Reading test bodies and checking this
  document cannot be reported as a green product baseline.

### Cost

All figures below are **estimates**, not measured runs. The old 25.5-minute full
assembly figure in the original plan is stale; the owner records no current
clean full-suite duration. Charge setup once per stage, then method runtime
and fresh rebuilds. Do not run mutation against a dirty Code worktree.

Ordinary V/R floor (Code), once Plan/TestDesign clears the failed readiness audit:

| Work | Selection | Minutes |
|---|---|---:|
| Setup, CLI migration generation, isolated builds, frontend build | Antiphon.Tests and Antiphon.E2E using OutputPath=bin-c508/; owned DB/native prerequisites | 12 |
| Focused S1/S2 + compatibility | DW, BS, DG, LS, PostLandMutationWorktreeTests named methods; R-5/R-6 named methods; fresh TRX counts | 18 |
| Contract capture then real compare | CS.Delegated_task_board_and_drawer_contracts plus new HTTP assertions | 6 |
| Changed land native receipts/recovery | V-11 four rows + V-12 nine rows | 26 |
| Dispatch-warning native receipts/recovery | V-14, budget reserved for Plan-defined path | 16 |
| Unit floor | /*/*/*/*[Category=Unit] | 3 |
| Required one full Antiphon.Tests pass | disjoint namespace chunks; exception is this dispatch's explicit full-suite instruction, not a 25.5-minute promise | 120 |
| **Ordinary total** | setup 12 + V/R 189 | **201** |

Mutation floor, excluding authoring/triage time:

| Work | Arithmetic | Minutes |
|---|---|---:|
| Fresh SourceLanding setup/build/discovery | once | 8 |
| PC-1..25, with PC-23's second variant | 26 variants x (0.5 red method + 0.5 restore/rebuild + 0.5 green method) | 39 |
| PC-26..28, PC-35..37 native methods | 6 x (3 red + 0.5 restore/rebuild + 3 green) | 39 |
| PC-29..34, PC-38..39 component methods | 8 x (0.5 red + 0.5 restore/rebuild + 0.5 green) | 12 |
| PC-40..42 missing-seam reservation | 3 x (3 red + 0.5 restore/rebuild + 3 green); **not executable authorization** | 19.5 |
| **Mutation planning total** | setup 8 + cycles 109.5 | **117.5** |

Combined **planning floor = 201 + 117.5 = 318.5 minutes**. Of that, the currently
specified executable-cycle estimate before resolving masking/seams is not a
complete acceptance budget. Supplying P-2 can increase
the number of required cycles; the next TestDesign must replace the reservations
with final executable rows and recompute the total before next: code.

Use these command forms after implementation, with a fresh results directory
and TRX name for each invocation:

~~~powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c508/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c508/ -- --treenode-filter '/*/Antiphon.Tests.Application/(DelegationWorktreeTests*)|(WorktreeBaseSelectionTests*)|(AgentTaskDispatchBaseGuardTests*)|(AgentTaskLandStageOutcomeTests*)/*' --report-trx --report-trx-filename base.trx --results-directory .antiphon/c508-base
dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-c508/ -- --treenode-filter '/*/*/ContractSnapshotTests/Delegated_task_board_and_drawer_contracts' --report-trx --report-trx-filename contract.trx --results-directory .antiphon/c508-contract
~~~

For each PC use the exact class/method in its row with
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c508-pc/
(or Antiphon.E2E for LD), followed by
-- --treenode-filter '/*/*/ClassName/ExactTestMethod' and fresh TRX/results paths.
ClassName/ExactTestMethod here denotes literal substitution from a PC row, not
a runnable wildcard recipe. Parameterized methods execute their documented
rows; inspect that the intended mutated row failed at its decisive assertion.
Compile errors, fixture errors, zero tests and generic timeouts without the
asserted missing recipient condition are not positive-control red.

Savings are estimates: running the 26 base-control variants as exact methods
at 0.5 minutes per invocation rather than an estimated four-minute creation
class saves 26 x 2 x 3.5 = **182 minutes**; no claim of measured speedup.
Retain one explicitly required full run, then target failures; repeating the
estimated 120-minute broad pass for each of 26 variants would add 6,240 minutes
and is prohibited for PCs. No zero-cost native or recovery coverage is claimed.
Run all commands in the foreground, inspect every TRX count, and keep
Antiphon.Tests and Pty/native test assemblies sequential. Inventory task-owned
alternate outputs before builds; validate absolute paths stay in this worktree
and remove only outputs created by these runs at teardown.

**Readiness audit at this handoff to Plan:** bodies read as listed; guards=42,
mapped=42, missing PC IDs=0, duplicate PC mappings=0. Executability gate
**fails**: PC-40/41/42 have no production seam. No claim of "all PCs executable"
or code-ready verification is made. P-1/P-2 and those three controls must return through
TestDesign; retain S1/S2 scope. This document is the concrete rejection artifact,
not permission to proceed to Code with incomplete recipient evidence.

**Superseded by Plan amendment A (task `dd438a95`, 2026-09-14).** A-1 gives the dispatcher
rows of V-5/V-7 a single shared base formula; A-2 gives PC-40/41/42 a production seam on
the existing durable-notification machine. The executability gate is *not* re-asserted
here — that verdict belongs to the next TestDesign pass, which must re-read the bodies
named in A-2 and write the controls. Nothing in this amendment authorises Code directly.

## Plan amendment A: P-1 and P-2 resolved

Appended by Plan task `dd438a95` on 2026-09-14 against `origin/master` `e4aa5924`,
answering the two return items TestDesign `1963c81d` raised at the Code gate. Scope is
unchanged: **S1 + S2 only**, now with S1b and S2b, and **S3 stays deferred** — no sibling
is chosen as a base in this release, acceptance cases 2/3 are not inverted, and the
chained-land consequence (D-7) is not exercised.

### A-1. One base formula, resolved by one resolver, observed once and reported

**The defect.** `DelegationWorktreeService.CreateForTaskAsync` computes
`startAtSha ?? task.MergeTargetRef ?? "HEAD"`
([DelegationWorktreeService.cs:169](../../../server/Application/Services/DelegationWorktreeService.cs)),
while the pre-lease dispatch guard computes its own `task.MergeTargetRef ?? "HEAD"`
([AgentTaskDispatcher.cs:2909](../../../server/Application/Services/AgentTaskDispatcher.cs)).
Two independent expressions of "the base". S1 changes only the first, so after S1 they
disagree *by construction* for every card-bound task with no merge target: the guard tests
sibling containment against the main checkout's `HEAD` while the worktree is cut from
`master`. TestDesign named both directions — patches only in `HEAD` make the guard silent
about a sibling the new worktree genuinely lacks; patches only in `master` make it warn or
hold about a sibling the new worktree already has. S2 does not fix this; it makes it
sharper, because `git cherry` gives a *more* accurate answer about the *wrong* base.

**A-1.1 — extract `WorktreeBaseResolver`.** New
`server/Application/Services/WorktreeBaseResolver.cs`. One method, no I/O of its own:

**Historical signature, superseded by B-1 below (P-4).**

```
static ResolvedBase Resolve(
    string? repairStartSha,          // null unless RepairSourceTaskId is set
    string? requestedRef,            // AgentTask.WorktreeBaseRequestedRef (-BaseRef; null in S1/S2)
    string? mergeTargetRef,
    string? defaultBranchIfResolvable)  // null when it does not rev-parse in this repo

record ResolvedBase(string Ref, WorktreeBaseSource Source, string? UnresolvedDefault);
```

Precedence is D-2 levels 0-5 exactly, with `CardCurrent` absent because S3 is deferred.
`UnresolvedDefault` is non-null only on the `RepoHead` arm and carries the default-branch
name that failed, so the misconfiguration warning text has one producer.

**A-1.2 — one ref probe, two callers.** Add
`Task<bool> RefExistsAsync(string repo, string theRef, CancellationToken ct)` to
`DelegationWorktreeService` (`git rev-parse --verify --quiet <ref>^{commit}`, `Ok`-only,
returning `false` rather than throwing). Provisioning and the guard both use it to produce
`defaultBranchIfResolvable`. `WorktreeManager.EnsureRefExistsAsync`
([WorktreeManager.cs:759](../../../server/Infrastructure/Git/WorktreeManager.cs)) keeps its
throwing contract for the explicit-base failure path (acceptance case 9) and is not reused
here — a fallback candidate that does not resolve is a routine miss, not a validation error.

**A-1.3 — the guard uses the resolver.** In `EvaluateCardSiblingBaseAsync`, replace
`var baseRef = task.MergeTargetRef ?? "HEAD";` with the resolver's `Ref`, passing
`repairStartSha: null` (see A-1.4). Sibling containment, the hold text and the warning text
all then quote the base the worktree will actually be cut from. In the ordinary card-bound
case — no merge target, no explicit base, `master` resolvable — guard and provisioning now
agree exactly, which is the whole of P-1's first half.

**A-1.4 — repair tasks: recorded, not evaluated.** `startAtSha` is the repair owner's tip
SHA and already outranks every other candidate. It is resolved inside `DispatchOneAsync` by
`PrepareRepairSourceAsync` ([AgentTaskDispatcher.cs:4105](../../../server/Application/Services/AgentTaskDispatcher.cs)),
which the pre-lease guard must not call — it is a multi-step git read that can *fail the
task*, and running it twice can return two answers.

- Provisioning records the repair base: `WorktreeBaseRef` = the SHA, `WorktreeBaseSource` =
  `Repair`, `WorktreeBaseTaskId` = `RepairSourceTaskId`, `WorktreeBaseSha` = the same SHA.
  That is the "recorded source" P-1 asks for, and `Repair` is added to the enum by A-1.
- The guard **skips sibling evaluation entirely** when `task.RepairSourceTaskId is not null`
  and `task.WorktreePath is null`. A repair branch is deliberately cut from one specific
  owner commit; "the card's other kept branches are not in that commit" is true by design
  and is not a finding. The existing owner-is-landing hold immediately above
  ([AgentTaskDispatcher.cs:584-604](../../../server/Application/Services/AgentTaskDispatcher.cs))
  already covers the one case that does matter, and is untouched.
- The skip is not silent: the `Dispatched` event's base clause reads
  `from <sha> (repair of <short>)`, which is the record. No extra `Warning` is emitted —
  a repair dispatch that warns about every sibling on the card would be noise on every
  repair.

**A-1.5 — report the residual gap instead of hiding it (S1b).** The guard still runs before
the repository lease, so a land taken between the guard and provisioning can still move
`master` underneath the answer. Moving the evaluation under the lease is D-4, which is S3
and stays deferred. What S1b adds is a *report*, not a second decision:

- `SiblingBaseGuard` gains `string ObservedBaseRef`.
- `WarnUnlandedSiblingsAsync` takes the guard rather than just its warning list. After a
  successful dispatch it reloads `WorktreeBaseRef`/`WorktreeBaseSource` for the task and,
  when the recorded ref differs from `ObservedBaseRef`, writes one extra `Warning` in the
  same `SaveChangesAsync`:
  `sibling containment was evaluated against <observed>; the worktree was cut from <actual> (<source>). Re-check before relying on the warnings above.`
- That warning is a `DispatchBase` obligation like any other (A-2), so it reaches the parent.

Deliberately **not** done here, and why: re-running containment against the recorded base
after dispatch would produce a second answer that is no more authoritative than the first
(the lease is already released by then), and the *hold* decision — the only one that must
be right before launch — cannot be revisited after the session exists. The correct fix is
D-4's move under the lease, and it belongs to S3 with the rest of the selection work.

**A-1.6 — request and record are different columns.** See the amended D-1. `-BaseRef`
(S4) writes `WorktreeBaseRequestedRef` at create; the server writes `WorktreeBaseRef` at
provisioning and never reads it as an input. Without the split, the worktree-reuse arm
([DelegationWorktreeService.cs:189-205](../../../server/Application/Services/DelegationWorktreeService.cs))
and any requeued second attempt would resolve against the *previous attempt's recorded
answer* and relabel a `DefaultBranch` base as `Explicit`. Provisioning writes the record
once: if `WorktreeBaseRef` is already non-null on entry the existing value and source are
left alone, which also makes the reuse arm idempotent.

**A-1.7 — what this changes for verification.** V-5's "Plan must specify repair source
label" is answered by A-1.4 (`Repair`, owner task id recorded, guard skipped). V-7's
"P-1 must clear before these dispatcher rows can be green" is answered by A-1.1-A-1.3: the
dispatcher's silence is now computed from the resolver's base, which is the base
provisioning uses. G-22 ("dispatch containment uses the actual base") gets a real seam —
its positive control mutates `WorktreeBaseResolver.Resolve` to return `"HEAD"` on the
`DefaultBranch` arm and the guard row goes red without touching provisioning.

### A-2. The dispatch-base warning becomes a durable obligation on the existing machine

**The defect.** `WarnUnlandedSiblingsAsync`
([AgentTaskDispatcher.cs:2945-2977](../../../server/Application/Services/AgentTaskDispatcher.cs))
writes a `Warning` event and then calls `_queue.EnqueueAsync` inline inside a
`try`/`catch` that logs and swallows. There is no per-warning identity, no snapshot of the
body against the destination, no atomic commit of event-and-obligation, and no recovery:
once the task leaves `Queued` no later tick reconstructs the warning, so a lost enqueue is
lost permanently. S2 changes which warnings this path emits, so the path is in scope for
delivery acceptance and cannot be verified as it stands — TestDesign's V-14, and PC-40/41/42
which have nothing to mutate.

**A-2.1 — reuse `AgentTaskLandNotification`, do not clone it.** That table already is a
durable delivery obligation with an immutable body, a content digest, a destination
snapshot, attempt/backoff state, a keyed queue handoff and transcript-only receipt
([AgentTaskLandNotification.cs](../../../server/Domain/Entities/AgentTaskLandNotification.cs),
[AgentTaskLandNotificationService.cs](../../../server/Application/Services/AgentTaskLandNotificationService.cs)).
Three small changes make it carry dispatch-base notes:

| Change | Detail |
|---|---|
| `LandNotificationKind` gains `DispatchBase` | [LandingEnums.cs:15](../../../server/Domain/Enums/LandingEnums.cs). |
| `RequestId` becomes `Guid?` | The FK to `AgentTaskLandRequest` becomes optional ([AppDbContext.cs:1545](../../../server/Infrastructure/Data/AppDbContext.cs)); a dispatch note has no land request. Every existing row keeps its value; nothing is backfilled. |
| `SourceEventId` points at the `Warning` event | Unchanged column, unchanged unique index ([AppDbContext.cs:1543](../../../server/Infrastructure/Data/AppDbContext.cs)). |

Rejected: a parallel `AgentTaskDispatchNotification` table with its own worker. It would
duplicate ~150 lines of a subtle recovery machine — lease probe, keyed-row reuse, parked and
truncated verdicts, catch-up, baseline floor — and the second copy is where the drift would
land. Also rejected: renaming the table. The name becomes a mild misnomer; a rename
migration on a hot delivery table is not worth it inside a base-selection fix, and the
misnomer is recorded in D-9's `docs/antiphon-api.md` line instead.

**A-2.2 — the identity P-2 asked for.** `SourceEventId` **is** the durable per-warning key,
and its unique index enforces one obligation per warning event. Two divergent siblings on
one task produce two `Warning` events and therefore two obligations and two receipts; a task
that is dispatched twice produces new events and new obligations. This is exactly what
distinguishes warning identity from task identity, which the existing conversation key
`task:{taskId:N}` cannot do. The queue row is keyed by `sourceLandNotificationId`, whose
unique filtered index
([AppDbContext.cs:1549](../../../server/Infrastructure/Data/AppDbContext.cs)) makes the
handoff idempotent without any new mechanism.

**A-2.3 — atomic commit, without pulling D-4 forward.**

**This producer timing is superseded by B-2/B-3 (P-3); the pair's atomicity remains.**

The obligation must commit atomically with **its own event**, not with the claim transaction. So
`WarnUnlandedSiblingsAsync` keeps running *after* `DispatchOneAsync` returns — the comment
at [AgentTaskDispatcher.cs:655-657](../../../server/Application/Services/AgentTaskDispatcher.cs)
("a launch that throws leaves no warning about work that never started") still holds, and
D-4's move into the claim transaction stays with S3. The change is to what that method does:

1. For each warning: add the `Warning` event **and** an `AgentTaskLandNotification` built
   by a new `DispatchBaseNotificationPayload.Create(task, sourceEvent)` alongside the
   existing `LandNotificationPayload.Create`
   ([LandNotificationPayload.cs](../../../server/Application/Services/LandNotificationPayload.cs)),
   in the same change tracker. Same shape, same `DelegationNoteDigest.Compute(
   $"{ReplyTo}:{ParentSessionId:N}
{body}")` digest, same initial-state rule —
   `ReplyTo == None` gives `NotRequired`, a null `ParentSessionId` gives
   `DestinationUnavailable`, otherwise `Queued`. `RequestId = null`,
   `LandingOperationId = null`, `SourceEventId = <the event's Id>`, `Kind = DispatchBase`.
2. **The body gains a header**, exactly as land notes do:
   `[dispatch-base <noteId:N> task=<taskId:N> warning=<eventId:N>]` on its own line above
   today's warning text. Two warnings on one task then differ in the receipt matcher even
   before their branch names do, which is what makes PC-41's assertion sharp rather than
   incidental. This changes what the orchestrator reads, deliberately.
3. One `SaveChangesAsync` wrapped in an explicit transaction, so an event never exists
   without its obligation and an obligation never references a missing event.
4. **Delete the inline `_queue.EnqueueAsync` call and its `catch`.** Delivery is the
   worker's job from here; the swallowed exception disappears with the code that produced it.

**A-2.4 — recovery is the existing scan, not a new worker.**
`AgentTaskLandNotificationHostedService` selects by `State` alone and is kind-agnostic
([AgentTaskLandNotificationHostedService.cs:22-23](../../../server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs)),
so a `DispatchBase` row is picked up by the boot scan and the 5-second backstop with no
change. One change is needed in `ReconcileAsync`: the repository-lease probe is gated on
`Kind is Outcome or Conflict` ([AgentTaskLandNotificationService.cs:53](../../../server/Application/Services/AgentTaskLandNotificationService.cs))
and `DispatchBase` must stay outside that gate — the obligation is committed in its own
transaction after the dispatcher has released the lease, so probing would only delay a note
that is already safe to send. Everything else — destination-unavailable, backoff, keyed-row
reuse, parked/truncated verdicts, `CatchUpTranscriptAsync`, the
`IsConfirmedBy`/`IsCompleteIn` receipt match — applies unchanged.

**A-2.5 — crash boundaries.** `LandDeliveryBoundary.ReachedAsync` takes a free-form
boundary name ([LandDeliveryBoundary.cs:6](../../../server/Application/Services/LandDeliveryBoundary.cs)),
so no type changes. One new producer boundary, `dispatch-warning-before-commit`, is reached
in `WarnUnlandedSiblingsAsync` between building the event/obligation pair and committing it.
`before-enqueue`, `queue-inserted`, `receipt-before-save`, the `completion` wakeup drop and
`notification-scan` are all inherited. The dispatcher needs `LandDeliveryBoundary?` injected
optionally, defaulting to null so existing direct constructions keep working.

**A-2.6 — what this makes executable.**

| Control | Seam it now has |
|---|---|
| PC-40 (durable obligation before handoff) | Mutate step 1 to add the event without the obligation, or step 2 to two `SaveChangesAsync` calls. A cut at `dispatch-warning-before-commit` then yields an event with no receipt. |
| PC-41 (warning identity, not task identity) | Mutate the obligation key from `SourceEventId` to `TaskId`. Two divergent siblings collapse to one receipt; the unique index or the second receipt assertion goes red. |
| PC-42 (producer recovery) | Mutate the scan query to exclude `Kind == DispatchBase`, or return early for it in `ReconcileAsync`. The enqueue-failure and ambiguous-insertion cases never recover. |

**A-2.7 — the two existing readers of `RequestId`, checked.** Making the column nullable
touches exactly two consumers, and neither needs a compatibility shim:

- `AgentTaskLandMonitorService` dereferences `note.RequestId` with `SingleAsync`
  ([AgentTaskLandMonitorService.cs:58](../../../server/Application/Services/AgentTaskLandMonitorService.cs)),
  but its query is already filtered to `Kind == LandNotificationKind.Outcome`
  ([:45](../../../server/Application/Services/AgentTaskLandMonitorService.cs)). A
  `DispatchBase` row is never selected, so the aged-receipt escalation is unchanged and
  cannot throw. Code must not widen that filter.
- `AttentionService` selects **every** unconfirmed non-legacy note with no kind filter
  ([AttentionService.cs:1200](../../../server/Application/Services/AttentionService.cs)), so
  a `DispatchBase` note *will* reach the attention feed once it ages past
  `LandWarningSeconds`. That is wanted — an undelivered dispatch warning is the same
  condition as an undelivered land outcome — but two things must change with it:
  `AttentionItemDto.LandRequestId` is already `Guid?`
  ([AttentionDtos.cs:355](../../../server/Application/Dtos/AttentionDtos.cs)) so the
  signature holds, while the summary text `Land {note.Kind} notification` and the client
  label `Land receipt missing`
  ([attentionVisuals.ts:44](../../../client/src/features/attention/attentionVisuals.ts))
  are wrong for a dispatch note. Add `AttentionKind.DispatchWarningUnconfirmed` with its own
  client label, selected on `note.Kind == DispatchBase`, and drop the `request=` clause from
  that variant's detail string rather than printing an empty Guid.

That second bullet is the one place where S2b touches the client. It is one enum value, one
`attentionVisuals` entry and the matching `attention.ts` union member — no new component.

V-14 can then arrange a real dispatcher producer against `LandDeliveryFixture`'s native
caller, cut at `dispatch-warning-before-commit`, `before-enqueue`, `queue-inserted` and
`receipt-before-save`, and end at a matching complete `UserPrompt` — with no test-only
outbox and no fabricated identity. The delivery-inventory row for "S2 divergent-sibling
dispatch warning" is superseded: persistence and durable identity are
`AgentTaskEvent.Id` = `AgentTaskLandNotification.SourceEventId` (unique), committed
together; recovery and receipt are the existing scan and transcript evidence.

### A-3. Consequences for the rest of the plan

- **D-9 documentation.** Add to `docs/antiphon-api.md`: `worktreeBaseRequestedRef` on
  create (S4) versus the recorded `worktreeBaseRef`/`worktreeBaseSource`/`worktreeBaseTaskId`
  on detail, and that `AgentTaskLandNotifications` now also carries `DispatchBase` notes with
  a null `RequestId`. Add to `docs/orchestration-loop.md`: the dispatch-base warning is a
  durable obligation with a transcript receipt, so its absence from a parent session is a
  defect rather than an expected loss.
- **Acceptance cases.** Cases 1-25 are unchanged in wording. Case 21 additionally asserts
  `WorktreeBaseSource` on a repair dispatch is `Repair` with `WorktreeBaseTaskId` set
  (A-1.4). Two cases are added, both inside the S1/S2 release:
  26. `a_repair_worktree_records_its_owner_sha_as_the_base_and_evaluates_no_siblings`.
  27. `a_guard_base_that_differs_from_the_recorded_base_is_warned_once` (S1b).
- **Cost.** S1b is a warning comparison and one extra event; S2b is three schema touches,
  a new payload factory, a delete of the inline enqueue, one new boundary name, and one
  attention kind with its client label (A-2.7). Neither adds a worker, a table
  or a migration beyond `AddWorktreeBaseSelection` plus a second small migration for the
  nullable `RequestId` and the `DispatchBase` kind. The PC-40/41/42 reservation in the cost
  table stops being "not executable authorization" and becomes ordinary budget.
- **Still deferred, unchanged.** S3 (`WorktreeBaseSelector`, `CardCurrent`, the CARD-0215
  reversal, moving evaluation under the lease per D-4), S4 (`-BaseRef` wiring), S5 (header
  and base-behind-default wording), S6 (docs beyond the two lines above), and D-7's
  chained-land consequence. Acceptance cases 2, 3, 7, 8, 11, 13 and 25 remain out of this
  release.

Next stage: **testdesign** — make PC-40/41/42 executable against the A-2 seam, re-verify
the dispatcher rows of V-5 and V-7 against A-1, and add controls for cases 26 and 27.

## Verification design: amendment A audit

Appended by TestDesign task 4ef2136b on 2026-09-14, after a clean reset to fetched
origin/master 17f026d1. This section supersedes the earlier verification rows only
where explicitly identified below. It does not rewrite amendment A or the fix
design. S1/S2, including S1b/S2b, remain the entire release.

**Verdict: next Plan.** PC-40/41/42 now have concrete, compiling mutations for
the committed-warning path. Cases 26/27 have controls. Two specification gaps
still prevent a complete Code handoff:

- **P-3, producer recovery before warning commit.** At this checkout,
  AgentTaskDispatcher.TickAsync selects Queued tasks (line 321).
  DispatchOneAsync sets Dispatched and commits the claim (lines 3255/3277),
  then returns before WarnUnlandedSiblingsAsync runs (line 677). A-2.3
  deliberately retains that order. Killing the server at A-2.5's
  dispatch-warning-before-commit rolls back the event AND obligation. On
  restart the task is not Queued and the notification worker scans only
  existing notifications. Neither participant reconstructs the lost warning.
  The same loss window exists between successful dispatch and entry into the
  warning method, and after a warning SaveChanges failure. Atomicity of the
  warning pair does not close this earlier handoff.
- **P-4, unresolved-default input is lost.** A-1.1's four-argument pure Resolve
  receives null as defaultBranchIfResolvable when probing fails. Two different
  missing defaults therefore give identical inputs, yet ResolvedBase must
  return their different names in UnresolvedDefault. That contract cannot be
  implemented as written without another input or a changed responsibility.
  A-1.2's bool probe also does not retain the name for the resolver.

Plan must specify durable recovery of the *original observed warning* across
P-3, including a dispatch-attempt identity, destination/body custody, recovery
owner and replay semantics. Re-running TickAsync, requeueing the task, creating
another warning by hand, or re-evaluating now-moving refs in a test does not
recover that obligation. This is an implementation seam to return to Plan;
it does not authorize moving S3's selection decision under the lease.
Plan must also make P-4's input/output contract satisfiable. No human scope
decision is pending.

### Inspection

Bodies re-read for this amendment, at 17f026d1:

- AgentTaskDispatchBaseGuardTests: all seven tests, including
  C499_V06_ARepairIsHeldWhileItsOwnerIsLanding; SeedKeptSiblingAsync,
  SeedQueuedWorktreeTaskAsync, SeedParentSessionAsync, SeedCardAsync and
  CreateProvider | real Git, status/land flag, repair, card/project linkage,
  fake parent and undrained launch queue -> V-5/V-7/V-15/V-16, R-3.
- DelegationWorktreeTests: merge-target creation, two top-level tasks,
  healing, failed creation, unknown-hook retention, leftover adoption,
  NewTask, CreateService and ExpectedCoordinates; ScratchGitRepo in full;
  DelegationTestServices in full | distinct SHA fixtures, constructor options,
  preservation and repository lease -> V-1..V-6, R-1/R-2, PC-44.
- PostLandMutationWorktreeTests: C478_V02_RebasedSnapshotAfterSourceRemoval,
  C478_V03_CreateRestartAndMissingCommit, C478_G056_ExactL;
  PostLandMutationWorld.Request/CreateAsync/TaskService | provision:false
  does not create a mutation task; explicit admission is necessary -> V-5.
- AgentTaskLandNotificationPersistenceTests: C488_ApprovalOutcomeTransactionAtomic,
  C467_V07_ConcurrentSettlementAndExplicitCleanup, C467_V17_UpgradeAndLegacyEvidence,
  C467_V05_OutcomeObligationMatrix; LandingSafetyHarness.BuildServices,
  SaveFault and TransactionFault | real migration, FK/index, before/after-save
  rollback -> V-17/V-20, PC-40/51.
- AgentTaskLandNotificationRecoveryTests in full; AgentTaskLandReceiptTests
  in full, including SeedAsync; BridgeQueueHarness.CreateAsync registration,
  OnSubmitted and InsertEntryAsync; TestDbFixture/IsolatedTestSchema in full |
  immutable destination, keyed insert, due retry, report exclusions, synthetic
  receipt evidence -> V-18, R-6/R-7, PC-52..59/63.
- AgentTaskLandDeliveryE2ETests: C467_V22..32, C498_FailureOutcomeReachesCaller;
  LandDeliveryFixture and LandDeliveryOptions in full | real Program/native
  caller, parent-owned DB/runner, child death, queue/save barriers, scans and
  native input census -> V-11..14/V-16/V-21, PC-41/42/50. This is the nearest
  fixture for new DispatchBaseWarningDeliveryE2ETests.
- AgentTaskLandMonitoringTests: C467_V16_AttentionSurvivesRecencyAndDeduplicates,
  C467_V15_AgedPayloadPreservesLandingEvidence and
  C467_V15_ThresholdsUseMeaningfulProgress; attentionVisuals.test.ts in full,
  including item/ALL_KINDS; attentionVisuals.ts and test-client.ps1 argument
  forwarding | requestless warning visibility, threshold boundaries, total
  client mapping -> V-19, PC-57/58.
- Production: dispatcher queued selection, guard, warning producer and claim
  commit/launch tail; CreateForTaskAsync/IsAncestorOfBaseAsync; notification
  payload, ReconcileAsync, hosted scan and monitor in full; attention note
  projection; notification/queue FK and unique-index configuration |
  P-3/P-4 and executable mutation locations below.

The preceding TestDesign's untouched land-marker, contract-snapshot and script
inspections remain its recorded evidence; this pass does not claim to have
rerun or newly inspected those unchanged test bodies. Required owners read:
orchestration-loop, project-context, testing-and-build and the relevant
session-runtime-invariants delivery requirements.

Setup additions/corrections before implementation:

1. New DispatchBaseNotificationTests uses the nearest DG dispatcher fixture
   for real warning production and the read notification/BridgeQueueHarness
   fixtures for worker and false-receipt components. Use isolated cloned
   databases, the DelegationTestServices graph, Integration and
   ParallelLimiter<ProcessSpawnLimit>. Register CompletionNoteFlushQueue and
   the scoped notifier; DG's existing provider does not do so. Its old queue
   assertion must explicitly reconcile the produced note after TickAsync,
   because A-2 removes synchronous enqueue.
2. Keep native tests in Antiphon.E2E with its process limiter and the existing
   C467LandDelivery nonparallel group. Extend the fixture to create a *new
   queued task* and card/siblings for the real dispatcher; its default TaskId
   identifies a Succeeded landing task. Configure the dispatched delegate to
   use the fixture's native FakeGrok definition and owned home as well as the
   parent; merely changing the parent leaves DG's ClaudeCode launch live.
   Do not call the land request path to manufacture a DispatchBase warning.
3. Generalize receipt lookup to exact notification ID, not FirstOrDefault by
   task/kind. Generalize AssertOnePromptAsync's hard-coded "[land " search to
   the exact frozen first line. Count matching complete UserPrompt entries
   and native user_message_chunk records for each warning after two further
   scans. Current helpers would miss dispatch headers and a second warning.
4. FileBoundary does not yet recognize dispatch-warning-before-commit.
   Add that cut and a way to pause queue reconciliation while inspecting a
   committed producer pair. The existing "terminal" cut already blocks
   before-enqueue; do not await terminal-committed for a dispatch.
   Add warning EventKind=Warning save-fault registration using the read
   SaveFault mechanism; ensure unrelated warnings cannot arm it.
5. For case 27, pause a recording lease decorator on its first acquisition
   request, after guard evaluation and before delegating to the genuine
   repository lease. While that same tick is paused, change only
   Project.BaseBranch from M to P through another DB context, then release
   the barrier and let the decorator acquire the real lease.
   The successful tick must observe M before the barrier and reload P for
   provisioning. Do not mutate a stored base tuple to create fake drift.
   Cover warnings.Count=0, 1 and 2; the original caller skips the warning
   method for an empty list, which would mask the new comparison.
6. Give repair owner, project default, explicit requested ref, merge target
   and checkout HEAD distinct tips. Add another same-card kept sibling
   with LandRequestedAt set: bypassing the repair skip then causes a visible
   unwanted hold. The repair owner's own LandRequestedAt is independently
   tested by C499_V06. A path-present repair retains its record on the next
   tick. Give the Code owner an actual uniquely registered worktree on its
   recorded branch/path: SeedKeptSiblingAsync alone leaves that branch
   unchecked-out, so PrepareRepairSourceAsync would refuse before selection.
   Read exact Ref/Source/TaskId/SHA through a fresh context.
7. Extend the prior migration arrangement for BOTH new migrations: first
   upgrade historical tasks for the request/record columns; separately
   preserve an existing land request/note and upgrade nullable RequestId.
   An already-migrated template alone cannot establish either upgrade.
   Compare persisted IDs, body, digest, enum numeric values and FK/indexes.
8. Retain Windows/native FakeGrok, modern ConPTY, git/pwsh, Docker PostgreSQL,
   pinned SDK, rebuilt client/dist and owned random runner prerequisites.
   A frozen clock is permitted only in monitor/notification components;
   queue/native timers use the fixture's real or offset clock. P-3/P-4
   require production design changes, not test-only setup substitutes.

### Delivery inventory

This inventory replaces the earlier S2 dispatch row. The land-outcome path and
all nine V-12 cuts remain required as previously specified.

| Path | Producer and destination | Persistence boundary and durable identity | Recovery and receipt |
|---|---|---|---|
| S1 base/repair creation detail | Provisioning/claim -> task detail reader | Task ID and Dispatched event ID; recorded Ref/Source/TaskId/SHA committed with claim | V-8/V-10/V-15 fresh DB/HTTP reads; query visibility, not session input |
| S2 sibling warning | Guard observation -> WarnUnlandedSiblingsAsync -> parent session | Successful claim precedes separate event/DispatchBase obligation transaction. Event.Id = note.SourceEventId; note.Id = queue.SourceLandNotificationId. Header binds note/task/event; snapshot binds destination/body/digest | After pair commit: kind-agnostic scan, keyed queue and complete native UserPrompt. Before pair commit: P-3 has no recovery identity/owner |
| S1b base-ref mismatch warning | Recorded-vs-observed ref comparison -> same parent | A distinct Warning event and DispatchBase note, even when there are no sibling warnings; same identities as preceding row | Same recovery/receipt requirements; no special exemption for a diagnostic warning |
| S2 land sibling marker | Real land producer -> caller | Request -> event -> notification -> keyed queue -> confirming transcript sequence | Retain V-11/V-12 full native path and V-13 false-evidence rejection |
| Unconfirmed dispatch warning attention | AttentionService -> HTTP/client reader | Existing note ID and ConditionKey, nullable request ID | Fresh GET after threshold/error and after receipt, plus client visual mapping; attention visibility is not delivery acceptance |

Dispatch handoff matrix (each row uses an actually dispatched task and a real
parent; queued rows are never manually invented in native tests):

| Cut / boundary | Required observation and continuation |
|---|---|
| Successful dispatch before warning method | Claim/session committed; restart without requeue or new request must recover original warnings. **P-3 fails this requirement.** |
| dispatch-warning-before-commit, plus injected before-save/after-save failure | Fresh observer sees zero warning events and zero notes after rollback. Restart must still reach original parent receipt. **Atomicity is testable; automatic continuation is not supplied by A-2.** |
| Pair committed, before-enqueue | Observe exact event/note pair, no queue row; restart owned server and recover same note ID to complete receipt |
| before-enqueue throws twice | Persist RetryPending, error, due time and original ID; due retry reaches receipt without re-dispatch |
| queue-inserted before QueueMessageId save | Busy parent; observe keyed queue row and null note.QueueMessageId, kill child, restart, reuse exact queue ID, release parent and receive once |
| completion wakeup dropped | Already eligible parent; production completion scan delivers without new input |
| queue-before-typing | Attempt metadata is durable; restart keeps the keyed row and reaches one complete prompt |
| queue-before-verdict and receipt-before-save | Existing native prompt is imported; restart/catch-up confirms that prompt, not a second submission |
| Receipt saved | Restart and complete two more notification scans; no extra native prompt or obligation |

V-14 covers both busy and already eligible parents, two divergent siblings,
and a deliberate second dispatch attempt. After the fixture-owned delegate
has settled and quiesced, arrange the same task Queued with Attempt incremented,
path/branch cleared and the recorded base retained, then drive the real
dispatcher/adoption path. This arrangement tests dispatch-attempt identity,
not the public retry API. A successful new attempt owes new event/note identities;
reconciling the same attempt's committed notes does not. Repeated idle ticks
without a new dispatch owe nothing new. This does not solve precommit attempt
recovery in P-3.

Receipt acceptance requires all links plus exact complete UserPrompt content
in the snapshotted parent after the queue attempt floor. Each new note header
is "[dispatch-base <noteId:N> task=<taskId:N> warning=<eventId:N>]".
Use the immutable first line and full body, not merely a branch name.
Include absent/incorrect header, wrong event ID, wrong note ID, wrong session,
truncated/spliced body, stale sequence, stale timestamp, and QueueEnqueue/
QueuedUserPrompt/Sent-only controls through the component matcher matrix.
Full and flattened-complete bodies after the floor are positive receipt cases.

ScratchGitRepo is real Git only; DG's Running session is a row without a native
process; BridgeQueueHarness.OnSubmitted inserts synthetic transcript entries.
None proves recipient delivery. Native FakeGrok proves the production
queue/runner/transcript path for its profile, not a live hosted model. Fresh
HTTP detail/attention reads prove query visibility only. Ordinary Review must
reject a design or execution report stopping at accepted request, event,
obligation, queue insertion, Sent, ConfirmedAt without its referenced prompt,
screen evidence or transport acknowledgement.

### Proves it works now

These are test implementation requirements, not passing-run claims. Prior
aliases DW/BS/DG/LS/LD/CS remain. DBN = DispatchBaseNotificationTests in
Antiphon.Tests; DE = DispatchBaseWarningDeliveryE2ETests in Antiphon.E2E.
Existing V/R rows remain except for these replacements/additions:

| ID | Behavior and layer | Exact test method / arrangement | Decisive expected result |
|---|---|---|---|
| V-3 update | Request/record precedence, real Git | BS.C508_BasePrecedence supplies WorktreeBaseRequestedRef, never recorded WorktreeBaseRef; retain E/C/default and invalid deliberate-ref rows | E/C priority and unchanged MergeTargetRef; recorded output never acts as caller input |
| V-5 update | SourceLanding and repair, real Git/admission/dispatcher | PostLandMutationWorktreeTests.C508_SourceLandingIgnoresDefault explicitly calls TaskService.CreateAsync after CreateAsync(provision:false), then provisions at L with invalid default; DW.C508_RepairStartShaWins; V-15 below | Verified L unchanged with no default warning. Repair wins over requested E, C and default, recording Ref=SHA, Source=Repair, TaskId=owner and BaseSha=SHA |
| V-7 update | Shared resolver and patch-aware call sites, Git/dispatcher | BS.C508_CherryContainmentMatrix retained; DG.C508_RebasedSiblingUsesActualDefault: Succeeded/Blocked x patches only in M/only in H/in both/in neither, each without/with active sibling land flag (16 rows) | Always assert created HEAD=M or Queued/no worktree as appropriate, independently of guard/ref. Contained in M: silent and dispatches even with land flag. Uncontained in M: warning or hold. H never supplies expected values |
| V-7 addition | Resolver and ref probe, component/real Git | BS.C508_ResolverAndCommitProbe: repair/E/C/resolved default/null arms; existing branch, commit tag, blob tag, missing ref, missing directory | Shared formula's returned Ref/Source agree with independent expected constants; RefExists accepts only a successfully resolved commit. Both production callers must reach this resolver; method-level result alone is insufficient |
| V-10 update | Request/record schema, migration/HTTP | BS.C508_BaseFieldsUpgradeAndRoundTrip retains existing setup and adds RequestedRef=null for historical and ordinary dispatches, distinct input/output roundtrip | Four new fields including RequestedRef persist independently; public S4 create wiring is still excluded |
| V-14 replacement | Warning producer to recipient, native E2E | DE.C508_WarningProducerToReceipt: busy=false/true, two real divergent siblings, one re-dispatch; DE.C508_WarningBootScanReachesCaller: committed pair, stop/restart server, ordinary scan | Two receipts per dispatch, four distinct notes/events over two attempts; exact one queue/native prompt per note; no typing before busy release; eligible receives automatically |
| V-14 recovery | Postcommit warning recovery, native E2E | DE.C508_WarningRecovery: pair-committed/before-enqueue, enqueue-errors, queue-inserted, lost-flush, queue-before-typing, queue-before-verdict, receipt-before-save, post-receipt restart (8 rows) | Original persisted identity reused at every handoff; complete recipient evidence; no duplicate submission after catch-up |
| V-15 / case 26 | Repair record and skip, dispatcher | DG.C508_RepairRecordsOwnerAndSkipsSiblings: other same-card sibling divergent with land flag absent/present; owner not landing; repeat path-present tick | Dispatched at owner's SHA; Source=Repair and TaskId=owner; exact repair base event clause; no sibling Warning/Held/DispatchBase notes. Owner-is-landing hold stays R-3 |
| V-16 / case 27 | Ref drift report, dispatcher/native | DG.C508_GuardRefMismatchWarnedOnce: M->P and fallback HEAD->M, each with 0/1/2 ordinary warnings, plus same-ref control; DE.C508_MismatchWarningReachesCaller: mismatch with no ordinary warning, busy/idle | Exactly one mismatch event/note per successful dispatch, total notes N+1, exact observed/actual/source text; equal ref emits zero mismatch notes. Native receipt contains the complete mismatch text once |
| V-17 | Producer pair/transaction, PostgreSQL | DBN.C508_WarningCommitCreatesObligation; DBN.C508_WarningCommitAtomic uses warning-specific SaveFault before-save/after-save, real dispatcher, fresh observer | Successful dispatch has one note per Warning with matching SourceEventId and null request/operation. Failed pair commit has zero of both. This component does not assert that re-dispatch is recovery |
| V-18 | Payload, route, state and shared worker, components | DBN.C508_WarningPayloadAndDestination; .C508_WarningStatesAndLease; .C508_WarningQueueDigestCollision; .C508_WarningsAreNotReports | Exact header/body/digest, original destination after task edit, None->NotRequired and missing/stopped/failed/deleted destinations remain owed/unconfirmed; DispatchBase enqueues under an occupied repo lease; changed digest on same destination/key refuses; report lookup/distillation does not consume or rewrite warning |
| V-19 | Requestless monitor/attention, DB+HTTP/client | AgentTaskLandMonitoringTests.C508_RequestlessDispatchNotes; attentionVisuals.test.ts adds "draws DispatchWarningUnconfirmed as a missing dispatch receipt" using item/ALL_KINDS | Sweep does not dereference null RequestId; Outcome still ages normally. At 299.999/300/899.999/900 seconds, warning attention is absent/Warning/Warning/Error, correct DispatchWarningUnconfirmed kind, null LandRequestId, no request= text; error-before-threshold visible; receipt clears it. Visual has dispatch receipt label, declared group, task target and stable key |
| V-20 | Nullable request migration, PostgreSQL | DBN.C508_RequestlessNotificationUpgrade using predecessor migration and a genuine old land pair | Old request ID/body/digest/enum values unchanged; requestless DispatchBase roundtrips; FK and unique event/queue indexes survive; applying migration again is inert |
| V-21 / P-3 | Lost producer handoff, native acceptance gate | DE.C508_DispatchWarningPrecommitCrashRecovers at dispatch-warning-before-commit; add successful-claim/pre-warning cut after Plan supplies it | After restart with no requeue/new request, complete original parent receipt. A-2 cannot meet this expected result; this is a rejection case, not a green-able test design |
| V-22 / P-4 | Missing default identity, pure resolver contract | BS.C508_UnresolvedDefaultRetainsName for two distinct invalid defaults and no higher-priority ref | Both return RepoHead but retain different failed names. A-1.1 supplies identical inputs and cannot meet this result |

V-16 compares refs only, as A-1.5 specifies. A moving tip under the SAME ref
does not trigger its warning and is NOT certified as detected. A-1.7's proposed
resolver mutation changes both callers, not just the guard; V-7's independent
HEAD=M assertion prevents that shared defect from appearing consistent.
P-4 does not invalidate resolved-default V-7 rows; it blocks the missing-default
warning contract in V-2/V-22.

### Guards the regression

- R-1/R-2/R-4/R-5/R-6 remain as previously defined.
- R-3 keeps the existing hold/warn semantics and C499_V06 owner hold.
  Its WhenIdle-note component explicitly runs ReconcileAsync on the produced
  note before checking the queue. Do not keep an assertion that enqueue
  happens synchronously in TickAsync. No helper-supplied warning or transcript
  can replace V-14.
- R-7: requestless DispatchBase must not disturb existing Outcome/Held/Conflict/
  Aged/legacy handling | AgentTaskLandMonitoringTests
  .C467_V15_ThresholdsUseMeaningfulProgress,
  .C467_V15_AgedPayloadPreservesLandingEvidence and
  .C467_V16_AttentionSurvivesRecencyAndDeduplicates; notification recovery
  .C467_V08_DestinationSnapshotsRemainOwed and
  .C467_V14_LandNotesDoNotCountAsReports; receipt
  .C467_V11_RejectFalseReceipts. Their request/receipt/body assertions remain.
- R-8: unchanged base tuple survives retry while task work advances |
  DW.C508_ReuseKeepsRecordedBase now also asserts RequestedRef remains null
  and a fresh task with recorded Ref but null RequestedRef cannot be relabeled
  Explicit. Check the tuple through fresh DB reads; do not derive expected
  Source from the resolver under test.

### Guard inventory

The original G-1..39 remain, with G-1 now referring to RequestedRef, G-22 to
the shared resolver's default-base contract, and G-23 to patch awareness only.
The independently bypassable pinned-SHA half of G-23 is split out as G-43.
G-38 is destination collision only; its digest half is now G-63. IDs below
replace G-40..42 and append the remaining guards. Every row maps to its own
same-numbered PC; no PC is shared between guards.

| Guard | Plan reference / independently bypassable invariant | Control |
|---|---|---|
| G-40 | A-2.3 successful warning commit includes its obligation | PC-40 |
| G-41 | A-2.2 different warning events on one task do not coalesce | PC-41 |
| G-42 | A-2.4 scan includes committed DispatchBase obligations | PC-42 |
| G-43 | S2 land comparison uses pinned verifiedSha, not moving HEAD | PC-43 |
| G-44 | A-1.6 recorded Ref is never consumed as RequestedRef | PC-44 |
| G-45 | A-1.4 fresh repair bypasses sibling evaluation, not owner hold | PC-45 |
| G-46 | A-1.4 repair Source records Repair | PC-46 |
| G-47 | A-1.4 repair TaskId records RepairSourceTaskId | PC-47 |
| G-48 | A-1.5 unequal observed/recorded refs trigger the mismatch predicate | PC-48 |
| G-49 | A-1.5 one mismatch warning per dispatch, independent of sibling count | PC-49 |
| G-50 | A-1.5 mismatch warning also owes parent delivery | PC-50 |
| G-51 | A-2.3 warning event/obligation rollback together on save failure | PC-51 |
| G-52 | A-2.3 snapshotted parent is immutable after producer commit | PC-52 |
| G-53 | A-2.3 digest binds reply route and frozen full body | PC-53 |
| G-54 | A-2.3 ReplyTo.None creates no delivery attempt | PC-54 |
| G-55 | A-2.3 unavailable destination remains owed, never NotRequired | PC-55 |
| G-56 | A-2.4 DispatchBase delivery is independent of occupied repository lease | PC-56 |
| G-57 | A-2.7 land outcome monitor excludes requestless DispatchBase | PC-57 |
| G-58 | A-2.7 aged dispatch note appears as DispatchWarningUnconfirmed | PC-58 |
| G-59 | Shared delivery: dispatch warning cannot satisfy report lookup | PC-59 |
| G-60 | Delivery/P-3: committed dispatch retains recoverable warning intent before pair commit | PC-60 |
| G-61 | A-1.1/P-4: fallback result retains failed configured default name | PC-61 |
| G-62 | A-2.3 dispatch header binds note, task and source-event identities | PC-62 |
| G-63 | Shared keyed handoff refuses digest collision at the same destination | PC-63 |
| G-64 | A-1.2 fallback probe accepts only a ref resolving to a commit | PC-64 |
| G-65 | A-1.5 post-dispatch comparison runs even with zero ordinary sibling warnings | PC-65 |

No "none" justification applies. New nullable schema shape and client visual
decoration are ordinary compatibility checks, not additional runtime admission
guards; their DB/HTTP/type and Vitest assertions are V-19/V-20. Shared
UserPrompt-kind/session/identity/completeness/floor, queue busy/idle and late
confirmation guards G-27..39 apply unchanged to DispatchBase; run the new
payload through those ordinary matrices rather than duplicating guard IDs.

### Positive controls

PC-1 consumes RequestedRef in the shared resolver. PC-22 now mutates
WorktreeBaseResolver.Resolve's successful DefaultBranch arm to return
Ref="HEAD", retaining Source; DG.C508_RebasedSiblingUsesActualDefault must fail
its independent expected default HEAD/hold/warning assertions. Both callers
change, so guard/provision equality alone cannot detect it. PC-23 has only its
ancestry-only variant; its old moving-HEAD variant is PC-43. Other PC-1..39
definitions remain, subject to the explicit request/record update.

The following are compiling production defects against amendment A's specified
implementation, with tests and fixture assertions unchanged:

| PC | Break the matching guard | Exact method and decisive red |
|---|---|---|
| PC-40 | In WarnUnlandedSiblingsAsync omit adding DispatchBaseNotificationPayload.Create's result to AgentTaskLandNotifications, retain event insertion/commit | DBN.C508_WarningCommitCreatesObligation; committed warning count=1, matching obligation count must be 1 (mutant 0) |
| PC-41 | Add a task-level coalescing condition before adding a note: skip insertion when either the tracked Local collection or a DB query already contains a DispatchBase note with this TaskId; still add each Warning event | DE.C508_WarningProducerToReceipt; expected event IDs each have one receipt, and two notes for the first dispatch (mutant one). Fresh IDs/FKs remain valid; do not assign TaskId into SourceEventId |
| PC-42 | Add n.Kind != LandNotificationKind.DispatchBase to the hosted notification scan query | DE.C508_WarningBootScanReachesCaller; after real producer commit and restart, matching complete prompt count must become 1 (mutant 0), with two recorded completed scans and no extra caller input |
| PC-43 | Pass moving HEAD instead of verifiedSha to the land sibling comparison | LS.C508_RebasedSiblingMarkerMatrix; pinned-M component row must retain the sibling marker even though moving HEAD contains it |
| PC-44 | Pass task.WorktreeBaseRef in place of task.WorktreeBaseRequestedRef into the resolver | DW.C508_ReuseKeepsRecordedBase; the separate adversarial input-isolation row supplies recorded Ref=E, null RequestedRef and no existing managed tree: actual created HEAD must equal configured M, not E. Existing record fields remain unchanged by A-1.6; a Source-only assertion would be masked by that preservation guard |
| PC-45 | Remove only the fresh-repair exclusion from the sibling guard condition; retain owner-is-landing hold | DG.C508_RepairRecordsOwnerAndSkipsSiblings; other sibling's land flag must not hold repair: status must be Dispatched, not Queued |
| PC-46 | Record WorktreeBaseSource.Explicit for the Repair arm | DG.C508_RepairRecordsOwnerAndSkipsSiblings; persisted Source must equal Repair |
| PC-47 | Set repair WorktreeBaseTaskId=null | DG.C508_RepairRecordsOwnerAndSkipsSiblings; persisted TaskId must equal owner.Id |
| PC-48 | Invert the recorded-vs-observed ref comparison in the mismatch producer | DG.C508_GuardRefMismatchWarnedOnce; unequal-ref row with one ordinary warning must have one mismatch event (mutant 0); equal-ref control must have none |
| PC-49 | After constructing the mismatch event/note pair, append an additional valid pair with fresh IDs when guard.Warnings.Count > 1 | DG.C508_GuardRefMismatchWarnedOnce; two-sibling row must have one mismatch event and three total notes (mutant two/four); no uniqueness error qualifies |
| PC-50 | Retain mismatch Warning but omit only its notification insertion; ordinary sibling obligations remain | DE.C508_MismatchWarningReachesCaller; zero-sibling mismatch yields one complete native prompt (mutant 0) |
| PC-51 | Commit and dispose the explicit warning transaction immediately before its SaveChangesAsync; remove its later CommitAsync so SaveChanges commits implicitly | DBN.C508_WarningCommitAtomic; after warning-specific after-save exception, fresh observer must find zero warning events and notes (mutant one each) |
| PC-52 | For DispatchBase in ReconcileAsync, replace note.ParentSessionId with current AgentTask.ParentSessionId before destination resolution | DBN.C508_WarningPayloadAndDestination; after task destination edit A->B, persisted note and queue destination must remain A |
| PC-53 | Compute DispatchBase ContentDigest from body alone, dropping ReplyTo/ParentSessionId prefix | DBN.C508_WarningPayloadAndDestination; exact digest must equal DelegationNoteDigest.Compute(route plus newline plus full body); same text on A/B must produce different digests |
| PC-54 | Remove ReplyTo.None's NotRequired arm from DispatchBase payload initial-state expression | DBN.C508_WarningStatesAndLease; None with a valid parent must remain NotRequired, queue count and enqueue attempts zero |
| PC-55 | Return NotRequired instead of DestinationUnavailable when DispatchBase has null parent | DBN.C508_WarningStatesAndLease; missing-parent row's state must be DestinationUnavailable with no fabricated receipt |
| PC-56 | Extend ReconcileAsync's repository-lease probe kind pattern to include DispatchBase | DBN.C508_WarningStatesAndLease; with a genuine occupied lease, DispatchBase must still acquire a queue ID while Outcome's remains null |
| PC-57 | Widen AgentTaskLandMonitorService's Outcome query to Outcome or DispatchBase | AgentTaskLandMonitoringTests.C508_RequestlessDispatchNotes; capture the SweepAsync exception and assert it is null (mutant request lookup throws); valid Outcome aging still creates its expected Aged notes. This is a method assertion, not a fixture setup exception |
| PC-58 | Map all unconfirmed notes to AttentionKind.LandOutcomeUnconfirmed, deleting the DispatchBase branch | AgentTaskLandMonitoringTests.C508_RequestlessDispatchNotes; aged note.Kind must project DispatchWarningUnconfirmed with null LandRequestId |
| PC-59 | Remove SourceLandNotificationId exclusion from AgentTaskCheckService.HasCompletionNoteAsync | DBN.C508_WarningsAreNotReports; adversarial task-key/digest-equal warning alone must return false |
| PC-60 | **No compiling red/restore/green cycle exists for A-2's absent precommit-intent recovery guard.** Removing pair insertion is already PC-40 and does not add a green recovery baseline | DE.C508_DispatchWarningPrecommitCrashRecovers; required exact original receipt is unreachable after the cut. Return P-3 to Plan; this row deliberately fails executability |
| PC-61 | **No compiling cycle can restore A-1.1's impossible missing-name contract.** Its failed probes discard the distinguishing input | BS.C508_UnresolvedDefaultRetainsName; expected distinct names from identical inputs cannot go green. Return P-4 to Plan; do not hard-code fixture branch names |
| PC-62 | Replace dispatch header construction with the unadorned warning detail; retain fresh valid IDs and compute digest over the resulting body | DBN.C508_WarningPayloadAndDestination; exact first line must include note/task/event GUIDs |
| PC-63 | Remove only ContentDigest comparison from existing-key conflict validation, retain session/destination comparison | DBN.C508_WarningQueueDigestCollision; same key/session with changed digest must throw ConflictException |
| PC-64 | Remove the ^{commit} suffix from RefExistsAsync's rev-parse argument | BS.C508_ResolverAndCommitProbe; blob-tag row must return false (mutant true) before any provisioning call |
| PC-65 | Keep the old call-site condition that invokes WarnUnlandedSiblingsAsync only when guard.Warnings.Count > 0 | DG.C508_GuardRefMismatchWarnedOnce; zero-sibling mismatch row must have one mismatch event (mutant 0) |

PC-40 tests committed obligation inclusion; PC-51 tests rollback. Two saves
inside one still-open transaction are not a broken atomicity control.
Assigning TaskId to SourceEventId violates the existing FK to AgentTaskEvent;
that database error is not PC-41 receipt evidence. PC-42 intentionally uses a
producer-commit observation and scan observations, not a before-enqueue barrier
the mutation prevents reaching.

Native expected-absence assertions use a bounded observation helper that
returns the collected receipt rows on deadline, then asserts the exact count
and identities. A generic fixture UntilAsync timeout is not the decisive red.
On green, validate the complete prompt and native input census as well.
PC-41/50 may fail their explicit obligation cardinality assertion before
receipt waiting; their ordinary green acceptance still ends at recipient
evidence. All controls remain exact-method scoped; never run an entire class
as a PC cycle.

Mutation runs break, expected assertion red, restore, rebuild and same-method
green after land; ordinary Code implements tests and runs V/R, and separate
Review judges both tests and the pending PC design before land. No mutation
or product test was run in this documentation task.

### Out of scope

- S3 selector/ranking/chaining and moving the pre-lease guard under the lease;
  S4 public BaseRef wiring; S5/S6 dependent behavior remain deferred. Original
  acceptance cases 2/3 are not inverted. Cases 26/27 are in scope as V-15/V-16.
- Same-ref SHA movement and held-before-dispatch decisions cannot be diagnosed
  by A-1.5's ref-only, post-success comparison. Record this limitation rather
  than claiming the race is fixed. Cross-product testing of S3 decisions is
  excluded because no S3 policy is introduced.
- Native session profile framing and hosted-model behavior are unchanged.
  One owned FakeGrok profile establishes the required native receipt path;
  browser visual rendering is excluded for the single mapped attention kind,
  covered by fresh HTTP data, the type build and focused Vitest assertions.
- A no-parent or ReplyTo.None dispatch cannot have a native recipient receipt.
  V-18 instead proves the explicit DestinationUnavailable/NotRequired contract;
  these are not substitutions for the Session-route busy/idle cases.
- P-3's precommit recovery and P-4's failed-name preservation are **not**
  exclusions or accepted risks. They are mandatory Plan return items.

### Cost

All times are **estimates**, not measurements. This pass ran zero builds,
product tests or PCs. It performed document/source inspection only.
Replace the previous reservation table with this audited lower bound.

| Ordinary floor (Code) | Suite/filter coverage | Minutes |
|---|---|---:|
| Setup/build | Two CLI migrations, owned prerequisites, Antiphon.Tests and Antiphon.E2E isolated outputs, client build | 14 |
| Focused V/R | DW/BS/DG/LS, DBN, PostLandMutationWorktreeTests and R-5/R-6/R-7 named methods | 26 |
| Upgrade and HTTP contract | V-10/V-20, CS capture then separate comparison | 8 |
| Land native delivery | V-11 four rows and V-12 nine cuts | 26 |
| Dispatch native delivery | V-14, V-16; P-3 rejection observation included, no claim of green | 36 |
| Client | attentionVisuals.test.ts through scripts/test-client.ps1 | 1 |
| Unit | /*/*/*/*[Category=Unit] | 3 |
| One full Antiphon.Tests pass | Disjoint namespace chunks, required by this dispatch's standing full-suite instruction; no repeated broad PC runs | 120 |
| **Ordinary lower bound** | **setup/build 14 + V/R 220** | **234** |

| PC floor (Mutation) | Arithmetic / exact-method selection | Minutes |
|---|---|---:|
| Fresh SourceLanding setup/build | one owned snapshot, external evidence | 8 |
| Component/Git controls | 54 executable PCs x (0.5 apply/build + 0.5 red + 0.5 restore/build + 0.5 green) | 108 |
| Native controls | PC-26/27/28/35/36/37/41/42/50: 9 x (0.5 apply/build + 3 red + 0.5 restore/build + 3 green) | 63 |
| **Executable PC lower bound** | **setup/build 8 + all 63 specified executable cycles 171** | **179** |

**Known verification lower bound = 234 + 179 = 413 minutes.** This is not a
complete release floor: PC-60/61 have no executable green baseline and are
not counted as zero-cost completed coverage. No numeric complete-acceptance
budget can honestly be approved until Plan supplies those seams and TestDesign
counts their real cycles. This explicitly fails the requested final Cost gate,
in addition to the executability gate, so the handoff is Plan.

For Code, use the prior isolated build commands and named V/R class filters;
add DBN and AgentTaskLandMonitoringTests to the affected Antiphon.Tests
selection and DE to the E2E selection. For example:

~~~powershell
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c508/ -- --treenode-filter '/*/Antiphon.Tests.Application/(DispatchBaseNotificationTests*)|(AgentTaskDispatchBaseGuardTests*)|(AgentTaskLandMonitoringTests*)/*' --report-trx --report-trx-filename dispatch.trx --results-directory .antiphon/c508-dispatch
dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-c508/ -- --treenode-filter '/*/*/DispatchBaseWarningDeliveryE2ETests/*' --report-trx --report-trx-filename warnings.trx --results-directory .antiphon/c508-warnings
pwsh -File scripts/test-client.ps1 attentionVisuals.test.ts
~~~

For each PC substitute the expanded exact class/method in its row into the
previous method-filter command, using Antiphon.E2E for DE/LD and Antiphon.Tests
otherwise. Give every red/green invocation a fresh results directory and TRX;
require the named method, nonzero counts and intended assertion. All commands
are foreground and awaited. No parallel native assemblies. Do not batch
mutations sharing a source file/method. Refresh restored timestamps and verify
the rebuilt output; remove only inventoried task-owned alternate outputs.

Estimated method-scoping savings for 54 component/Git controls: compared with
four-minute class runs, 54 x 2 x (4 - 0.5) = **378 minutes**, with identical
build costs. Savings from excluding P-3/P-4 are **zero**: neither is accepted
coverage or optional spend. No broad-suite speedup or clean baseline is claimed.

**Readiness audit:** newly touched/nearest fixture bodies read as above;
guards=65, mapped=65, missing PC IDs=0, duplicate PC mappings=0.
Specified executable controls=63; PC-60/61 fail executability, and a complete
verification floor cannot yet be certified. PC-40/41/42 and cases 26/27 are
fully specified against A-2/A-1. **Code gate remains closed.** Return P-3/P-4
to Plan, then TestDesign for the final all-executable audit.

## Plan amendment B: P-3 and P-4 resolved

Plan task `88c70cc3`, 2026-09-14. Read the full supplied brief, fetched
`origin/master`, and reset the clean task branch to `fc3bb9bb` before inspection.
This amendment is the binding correction to A-1.1/A-1.2 and A-2.3..A-2.6;
the audit above documents why those previous contracts were rejected.
Release remains **S1 + S1b + S2 + S2b**. **S3 stays deferred.**

### B-0. Ground truth at the amendment baseline

| Plan assumption | What `fc3bb9bb` actually does | Required correction |
|---|---|---|
| An atomic Warning/note pair is enough for producer recovery. | `AgentTaskDispatcher.TickAsync` selects Queued tasks; `DispatchOneAsync` commits Dispatched/session at lines 3255-3277 and only later returns to the warning call at line 677. A crash before that call loses the local guard result. | Commit original warning intent with the claim, before any postcommit launch work. |
| Successful return from `DispatchOneAsync` is a durable dispatch boundary. | After commit it still loads attachments, builds the launch spec, resolves environment, queues launch/brief and publishes an event (lines 3279-3338). Those operations can throw after the claim is durable. | Treat the claim commit as the warning obligation boundary, including postcommit exceptions; never depend on a return or another success flag. |
| Task ID or `Attempt` identifies a warning-producing dispatch. | One task can be requeued. The final `Dispatched` event is created with a fresh GUID for each successful claim; the earlier worktree-created event is a different event of the same type (lines 3141-3150 and 3266-3274). | Use the **final agent-dispatch event ID**, not task ID, mutable Attempt or the first event of type Dispatched. |
| The existing worker can find a lost precommit warning. | `AgentTaskLandNotificationHostedService` pages only existing notification rows, excluding Confirmed/NotRequired/LegacyUnverified. `ReconcileAsync` starts from a notification ID. Neither scans dispatches or observed warnings. | Add an intent-materialization pass to that hosted service, with independent due/error handling. |
| A nullable successful default is enough for `UnresolvedDefault`. | No `WorktreeBaseResolver` exists yet. Production provisioning still uses `startAtSha ?? MergeTargetRef ?? "HEAD"`; the guard separately uses `MergeTargetRef ?? "HEAD"`. The planned null input cannot distinguish two failed defaults. | Carry `(Ref, ResolvesToCommit)` to the shared resolver. |
| Making `RequestId` nullable is the whole schema change. | Current notification FK to `SourceEventId` is required and Restrict; `SourceEventId` and keyed queue notification ID are unique (`AppDbContext.cs:1576-1584`). There is no place to store a warning before its event exists. | Add a producer-intent table; keep the existing required Warning-event FK on the eventual note. |
| Current retention also protects pre-note destinations. | `DataRetentionService.PruneSessionsAsync`/`PruneTranscriptsAsync` consult notifications, not intents. Task pruning excludes landing/request trees, but a new dispatch-only note has no request. | Protect unmaterialized intent destinations; exclude intent/notification-owning task trees from ordinary deletion rather than hitting a Restrict FK. |
| Existing fixtures already exercise these cuts. | DG's parent is a DB row, not a native caller. `LandDeliveryOptions.FileBoundary` recognizes land/queue/receipt cuts, not dispatch intent cuts. `LandDeliveryFixture.ReceiptAsync`/`AssertOnePromptAsync` select land kinds/headers. | Extend the real dispatcher/native fixture as the audit specifies; component rows alone cannot prove delivery. |

Inspected production owners: dispatcher guard/claim/launch tail, worktree
provisioning/reuse, notification payload/entity/worker/reconciler, event model,
EF mappings, attention and retention. Nearest test bodies read: DG's hold,
warning, repair and deleted-sibling tests; notification transaction/upgrade
tests; `LandDeliveryOptions` and fixture receipt, child restart and prompt census.
These are source observations, not executed tests or deployment evidence.

### B-1. P-4: name-preserving resolver contract (S1)

Replace A-1.1's fourth argument with a non-null value:

```csharp
record DefaultBranchProbe(string Ref, bool ResolvesToCommit);
record ResolvedBase(string Ref, WorktreeBaseSource Source, string? UnresolvedDefault);

static ResolvedBase Resolve(
    string? repairStartSha,
    string? requestedRef,
    string? mergeTargetRef,
    DefaultBranchProbe defaultBranch);
```

The callers choose exactly one configured default: first nonblank
`Project.BaseBranch`, then nonblank `GitSettings.DefaultBranch`, then `master`.
Ignore whitespace-only settings but do not rewrite a nonblank ref. Do not try
the next setting after the chosen candidate fails to resolve; the selected
candidate falls back to HEAD and is the name reported in the warning.
The shared probe helper on `DelegationWorktreeService` returns
`new DefaultBranchProbe(candidate, await RefExistsAsync(repo, candidate, ct))`.
The candidate and bool travel together even on failure; no null sentinel.
Cancellation propagates. The existing A-1.2 commit-only Git probe contract stays.

| Condition, in order | Ref | Source | UnresolvedDefault |
|---|---|---|---|
| Repair SHA supplied | repair SHA | Repair | null |
| Requested ref supplied | requested ref | Explicit | null |
| Merge target supplied | merge target | MergeTarget | null |
| Named default resolves to a commit | `defaultBranch.Ref` | DefaultBranch | null |
| Named default fails to resolve | `HEAD` | RepoHead | **`defaultBranch.Ref`** |

The pure resolver performs no config/DB/Git lookup and retains the name
verbatim. Higher-priority arms suppress default warnings even when the probe
failed. Deliberate repair/request/merge refs keep their existing failure
contracts; this fallback applies only to the default candidate. Both guard and
provisioning call this resolver with their own observation. Provisioning
obtains the project setting freshly at its existing lease-protected point;
it does not reuse the guard's tracked project or earlier probe. The repair
skip and SourceLanding early return remain as A-1.4 specifies.

The selected `ResolvedBase` is also available to dispatch capture without a
second probe: return it as provisioning metadata alongside the task's recorded
tuple. Reuse must distinguish the effective existing base record from a newly
computed candidate; B does not authorize overwriting A-1.6's recorded fields.
Emit an unresolved-default diagnostic only for a newly used fallback decision,
not merely because an unused default failed. The warning's saved detail names
the failed configured ref and HEAD; B-2 gives it the same custody as sibling
and mismatch warnings. Standalone provisioning tests may inspect the returned
diagnostic; native delivery is a dispatcher obligation.

### B-2. P-3: capture immutable intent with the successful claim (S2b)

Add `server/Domain/Entities/AgentTaskDispatchWarningIntent.cs` and its DbSet and
mapping. This is a producer checkpoint, not a second delivery ledger.

| Field | Contract |
|---|---|
| `Id` (`Guid`, PK) | Preallocated **future Warning event ID**. Never regenerated by materialization or retry. |
| `DispatchEventId` (`Guid`) | The final agent-dispatch event in this successful claim; FK to AgentTaskEvent, Restrict. This is the dispatch-attempt identity. |
| `TaskId` (`Guid`), `Attempt` (`int`) | Task FK, Restrict; attempt counter is a diagnostic snapshot only. Capture asserts the dispatch event belongs to this task. |
| `WarningKey` (`string`, 100) | `sibling:<siblingTaskId:N>`, `base-observation-stale`, or `default-unresolved`. Unique `(DispatchEventId, WarningKey)`. |
| `NotificationId` (`Guid`) | Preallocated note ID, unique. No FK to a not-yet-existing note. |
| `ReplyTo`, `ParentSessionId` | Original route from the locked claim. Parent is a loose GUID snapshot, as on notifications; no cascading session FK. |
| `Detail`, `Body`, `ContentDigest` | Exact Warning detail; full A-2 dispatch header plus detail; route + newline + body digest. Text is frozen at capture, not formatted again on recovery. Digest max length 128. |
| `CreatedAt`, `InitialState` | Original claim time and A-2 payload rule: None -> NotRequired; otherwise null parent -> DestinationUnavailable; otherwise Queued. |
| `MaterializedAt` (`DateTime?`) | Null until event, note and marker commit in one transaction. A timestamp is a projection checkpoint, never receipt evidence. |
| `MaterializationAttempts`, `NextAttemptAt`, `LastErrorCode`, `LastErrorAt`, `ConcurrencyToken` | Retry bookkeeping; initialized to 0, CreatedAt, null, null and a fresh GUID respectively. Only these and MaterializedAt may change. Due index on `(MaterializedAt, NextAttemptAt, Id)`. |

`DispatchBaseNotificationPayload` gets a capture factory that accepts the
preallocated IDs/route/detail/time and returns the frozen payload. Its
materialization factory accepts an intent, never the current AgentTask. Keep
the header exactly `[dispatch-base <NotificationId:N> task=<TaskId:N> warning=<Id:N>]`
so A's matcher and PC-62 remain applicable. The attempt-to-warning link is in
the intent table; no new header field is required. Claim event ID plus
WarningKey controls identity; order of the sibling list does not.

Capture consumes immutable `(WarningKey, Detail)` drafts plus the final claim
event and route metadata, not the dispatcher's private `SiblingBaseGuard`
type. The dispatcher formats each original sibling observation once and adds
the mismatch/default drafts without further lookups. Expose those drafts as
an internal record in the payload file. Production DI and hand-built dispatcher
harnesses must register the capture/materialization dependencies; missing
registration must never silently skip persistence. Extend
`tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs` and update direct
constructor fixtures as necessary.

Change `DispatchOneAsync` to accept the optional `SiblingBaseGuard` observation.
The guard still runs **before** the repository lease. Resolve its observed ref
even when it finds zero siblings, so the zero-warning mismatch case survives.
Held and repair-skipped paths retain their existing behavior. In the cold
Worktree path, after provisioning and all precommit refusal checks:

SourceLanding produces no dispatch-base intents. For other Worktree tasks,
default-fallback drafts do not require a CardId; sibling and mismatch drafts
require an actual guard observation. Path-present reuse does not invent a new
observation or relabel the recorded base.

1. Keep a reference to the final agent-dispatch event that is already created
   immediately before the successful claim save. Its GUID is the attempt ID.
2. `DispatchBaseWarningIntentService.Capture` adds one intent per frozen sibling
   warning, plus one mismatch if observed ref differs from the actual recorded
   ref, plus any newly used unresolved-default diagnostic from B-1. It reads
   the claim's route and base metadata, **performs no Git/DB re-evaluation**,
   and never calls SaveChanges. A guard with no siblings still reaches Capture.
3. Save and commit these intents in the **same transaction** as Dispatched,
   the final dispatch event, task base tuple and new session. A claim loser,
   hold, precommit failure or rollback has zero committed intents. Never place
   capture on an earlier branch that commits Failed or returns false.
4. Finish the existing launch tail. Remove the old direct Warning/enqueue
   producer. A best-effort materialization after successful return may reduce
   latency, using a fresh scope and the committed attempt ID; it is optional,
   has no authority to reconstruct intent and cannot fail a dispatched task.
   The hosted scan is the mandatory recovery owner.

The table is already eligible when the claim commits, even if the original
process dies before returning or launch subsequently fails. Do not add a
post-launch `Ready` bit: losing that write would recreate P-3. D-12 explicitly
changes the old launch-exception promise; the message describes a worktree
whose creation/assignment committed, which remains true after startup failure.
Uncommitted worktree filesystem residue has no warning obligation in this
design. It continues through existing worktree adoption/recovery.

Replaying the same committed attempt must reuse its intent IDs. A **new
successful claim** gets a new final dispatch event and new warning/note IDs,
even if TaskId, Attempt, parent and text are unchanged. Repeated ticks on a
non-Queued task create nothing. Do not backfill historical dispatches: there
is no trustworthy original observation or destination to recover for them.

### B-3. Materialization, recovery and the complete custody chain

Register scoped `DispatchBaseWarningIntentService` in `server/Program.cs`.
Its `MaterializeAsync(Guid intentId, CancellationToken ct)`:

1. Opens a DB transaction in a fresh row scope, loads the intent with
   `SELECT ... FOR UPDATE`, and returns if already materialized. Validate
   the saved digest/header and the dispatch-event/task binding; a mismatch
   remains unresolved with an error, never gets silently reformatted.
2. Adds the Warning with `Id = intent.Id`, original TaskId/Detail/CreatedAt;
   adds DispatchBase note with `Id = intent.NotificationId`,
   `SourceEventId = intent.Id`, null RequestId/operation, and the exact saved
   route/body/digest/initial state. No new IDs and no current task/ref reads.
3. Reaches `dispatch-warning-before-commit`, saves both rows and
   `MaterializedAt`, and commits all three changes together. The existing
   Warning-event FK and unique note.SourceEventId remain unchanged.
   Atomic rollback leaves the committed intent pending with neither row.
4. On an exception, dispose/rollback and clear the failed context before using
   a fresh transaction to record attempts/error/next due time. Reload under
   the row lock: an already committed MaterializedAt wins over a lost commit
   acknowledgement or competing retry. Use the existing bounded 5..300 second
   retry progression, not a permanent failure/cancel state. If error recording
   itself fails, the pending row still makes the next scan retry possible.

For a competing materializer the row lock serializes this exact projection;
after the first commit the second is a no-op. Unexpected existing IDs with a
null marker are an integrity error, not permission to allocate replacement
IDs or overwrite payloads. The normal after-save failure rolls everything
back; an ambiguous commit reload finds all three committed changes.

`AgentTaskLandNotificationHostedService` first pages due intents with
`MaterializedAt == null && NextAttemptAt <= now`, then performs its existing
kind-agnostic notification scan. Use independent fresh row scopes, ascending
ID cursor/128-row pages, cursor reset each boot/5-second cycle, per-row failure
isolation, and continue the notification pass even when the intent pass fails.
Do not filter intents by current task status/Attempt/parent, notification
existence, or a live dispatcher. Both None and missing-parent intents are
materialized; their saved initial states decide whether delivery is required.
The materializer never types/enqueues. `ReconcileAsync` continues to own keyed
enqueue, backoff, transcript catch-up and receipt after note creation.
DispatchBase remains outside its Outcome/Conflict repository-lease gate.

| Durable handoff | Identity/custody | Restart behavior |
|---|---|---|
| Claim -> intent | Final Dispatched event ID -> WarningKey -> intent.Id + NotificationId; exact saved route/text/digest | Boot/due intent scan finds it regardless of subsequent task state. |
| Intent -> Warning + note | intent.Id = Warning.Id = note.SourceEventId; intent.NotificationId = note.Id; pair + marker atomic | Before commit: original pending intent retries. After commit/lost acknowledgement: marker and unique IDs make projection inert. |
| Note -> queue | note.Id = queue.SourceLandNotificationId; existing unique key/digest guard | Insertion ambiguity reuses the existing queue row; no replacement ID. |
| Queue -> parent receipt | Frozen destination, exact header/full body, sequence/time floor | Complete original UserPrompt confirms delivery; Sent, screen and transport acknowledgement do not. |

Attention must cover the new pre-note interval. Project an unmaterialized
intent with `ReplyTo != None` as `DispatchWarningUnconfirmed` under the same
age/error thresholds as A-2.7, with summary `Dispatch warning awaiting
materialization`, task target, null LandRequestId and no request clause.
Use `ConditionKey = dispatch:<NotificationId:N>:receipt` for both pending
intent and eventual DispatchBase note so it remains one condition. Suppress
the intent projection when its exact note exists, and deduplicate the merged
projection by this key to cover a materialization commit between the two reads.
An intent may name NotificationId in detail, but `LandNotificationId` stays
null until that row exists. Do not create an Aged land request for an intent.

`DataRetentionService` must protect the original parent session and whole
transcript while any required intent is unmaterialized, even after task route
edits. After materialization the existing unresolved-note protection takes
over atomically. Exclude whole task trees owning **any** intent or notification
from task pruning, consistent with retained landing history, so the Restrict
FK cannot abort a sweep of unrelated eligible trees. No intent-history purge
or replay of legacy rows is added here. Retention/attention changes are the
necessary consequences of adding pre-note custody, within S2b.

### B-4. Boundary contract and TestDesign return requirements

Retain `LandDeliveryBoundary` as the instance-scoped seam; the dispatcher and
materializer accept it optionally. Add these names and observations to
`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs`:

| Boundary | Placement and identity | Decisive recovery requirement |
|---|---|---|
| `dispatch-warning-claim-before-commit` | After Capture, before successful claim SaveChanges/Commit; identity = final Dispatched event ID | Kill/rollback: no committed dispatch/session/intent/pair. A later queued dispatch is a new claim, not proof of recovery for this one. |
| `dispatch-warning-claim-committed` | Immediately after claim CommitAsync, before attachment/spec/launch work; same identity | Committed claim and exact intents, zero pairs while materialization is gated. Kill/restart: original intents reach original parent without requeue or new request. |
| `dispatch-warning-after-dispatch` | At successful return, before any optional materialization; same identity | Cover the audit's successful-dispatch/pre-warning cut. Recovery does not require this hook ever being reached. |
| `dispatch-warning-before-materialize` | At MaterializeAsync entry, before opening the transaction or acquiring the row lock; identity = intent.Id | An independent fixture gate blocks both scan and optional fast path while a fresh observer inspects committed intents. Release on restart to permit normal recovery. |
| `dispatch-warning-before-commit` | Materializer after constructing pair, before SaveChanges/Commit; identity = intent.Id | Kill or before/after-save fault: original intent survives, pair/marker roll back, scan recreates exact pair and reaches receipt. |
| `dispatch-warning-materialized` | After pair/marker commit, before inline notification reconciliation (if any); identity = intent.Id | Lost acknowledgement/restart never creates a second pair. |
| `dispatch-warning-intent-scan` | End of each completed intent pass, separate from notification-scan | Tests observe boot and repeated recovery without driving Tick/requeue. |

The fixture needs an independent materialization gate before acquiring the
intent row lock, for both scan and optional fast path; stopping only the
dispatcher hook would let the background scan win the claimed crash window.
Hold it while inspecting intents at the two postclaim cuts, then restart
the owned server with the gate released. Scope all barriers/save faults to the
fixture task/attempt/intent so unrelated warnings cannot satisfy them. Do not
wait for a notification boundary in a mutant that never creates a note.

TestDesign owns the final executable matrix. These concrete seams close its
two missing design rows:

| Return item | Compiling production mutation to design against | Required test result |
|---|---|---|
| PC-60 / V-21 | In the hosted service omit **only the intent scan invocation**, retaining Capture, MaterializeAsync, the notification scan and optional post-dispatch fast path. | `DispatchBaseWarningDeliveryE2ETests.C508_DispatchWarningPrecommitCrashRecovers`: at the immediate postclaim cut the fast path has never run; after restart committed pending intents cannot materialize in the mutant. Assert expected original note/complete native prompt count, not a fixture timeout. Restored scan must produce the same saved IDs/body/destination once. Include the pre-pair-commit and successful-return cuts in ordinary V-21. |
| PC-61 / V-22 | Return `UnresolvedDefault = null` only on Resolve's RepoHead arm, retaining HEAD and Source. | `WorktreeBaseSelectionTests.C508_UnresolvedDefaultRetainsName`: two arbitrary distinct invalid configured refs yield HEAD/RepoHead and retain their respective original names. The mutant loses both names; restoration passes without hard-coded fixture names. Higher-priority arms still return null diagnostic. |

Adapt PC-40/41/49/50/51/62 to the capture/materialization factories, not the
deleted warning method. PC-40 still omits note insertion while keeping the
Warning; PC-51 still tests pair/marker rollback, independently of claim
atomicity. PC-48 and PC-65 move to the capture comparison/call site. PC-52/53
must cover both claim-to-intent and intent-to-note custody, not only edits
after a note already exists. PC-42 continues to target the **notification**
scan, distinct from PC-60's **intent** scan. Existing post-note/land controls
and false-receipt cases remain required.

Add guards for atomic claim+intent insertion, attempt-key separation,
concurrent/lost-ack projection, saved body/destination after refs and task
route change, independent retry paging, pre-note attention and retention.
Include postcommit launch exception and terminal task/requeue cases: recovery
must still use the old intent; a genuinely new claim gets different IDs.
For P-4, add caller-level default-fallback warning assertions using two
different names and the ordinary real-Git missing-default case. A pure
resolver test alone cannot catch a caller that discards the name beforehand.

Keep DE's native parent/delegate FakeGrok, owned random runner, real Program,
DB isolation and busy/eligible cases. Capture expected IDs/body/route from the
committed intent with a fresh observer **before** restart; change refs or task
route only after that capture. Require exact matching complete UserPrompt and
native input census once per intent after two further completed scans. Reusing
the same attempt or manually inserting an event/note is not recovery evidence.

### B-5. Alternatives rejected and retained limitations

| Alternative | Reason rejected |
|---|---|
| Only add another warning-pair transaction or retry the post-dispatch method in memory. | Neither leaves discoverable custody after process death before entry or commit. |
| Re-evaluate siblings on restart, or requeue the task. | Ref tips, config, status and destination may have changed; it manufactures a new observation/dispatch rather than delivering the original one. |
| Use `(TaskId, Attempt)` alone, or deduplicate by warning text. | Attempt is mutable and task retries can repeat the value; identical text on two successful claims still represents two obligations. |
| Add a post-launch success/ready marker before making intent eligible. | The process can die after launch but before that marker, recreating the unowned recovery gap. |
| Move selection/holds under the lease now. | That is S3 and changes the decision policy. B only moves custody of an already-computed observation into the claim. |
| Put Warning and delivery note directly in the claim transaction. | Would also solve loss, but removes A's separate projection boundary and moves all Warning/note construction into claim handling. The chosen intent makes original observation custody explicit while preserving the already-designed pair rollback/recovery contract. |
| Use a notification row with no Warning-event FK yet, or add a second delivery queue/worker. | Weakens existing notification identity/receipt assumptions or duplicates them. The small producer ledger hands off to the existing required-FK table. |
| Store a single replaceable warning JSON blob on AgentTask. | Requeue can overwrite a pending prior attempt and per-warning uniqueness/recovery become implicit. Typed per-warning rows have database-enforced identity. |

The pre-lease guard can still race a land. Same-ref tip movement is still not
detected by S1b's ref comparison; snapshotting warning text does not improve
its truth at a later instant. None of B's recovery claims certify that S3 race
as fixed. DispatchBase may be delivered after a later failure or settlement;
the body retains the original observation, never silently updates it.

### B-6. Amendment implementation slices, files and tests

Paths below are repo-relative; new files are explicitly marked. Keep each
source slice with its named ordinary tests and commit before long runs.

| Slice | Files | Required tests/coverage |
|---|---|---|
| B/S1: preserve failed default name | new `server/Application/Services/WorktreeBaseResolver.cs`; `server/Application/Services/DelegationWorktreeService.cs`; `server/Application/Services/AgentTaskDispatcher.cs` | new `tests/Antiphon.Tests/Application/WorktreeBaseSelectionTests.cs`: `C508_UnresolvedDefaultRetainsName`, `C508_ResolverAndCommitProbe`; existing `DelegationWorktreeTests.cs` missing-default and precedence cases; `AgentTaskDispatchBaseGuardTests.cs` actual-default matrix. |
| B/S2b-1: schema and claim custody | new `server/Domain/Entities/AgentTaskDispatchWarningIntent.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated `server/Migrations/*` and model snapshot; new `server/Application/Services/DispatchBaseNotificationPayload.cs`; new `server/Application/Services/DispatchBaseWarningIntentService.cs`; `server/Application/Services/AgentTaskDispatcher.cs`; `server/Program.cs` | new `tests/Antiphon.Tests/Application/DispatchBaseNotificationTests.cs`: claim atomicity, same-task distinct claim IDs, frozen payload; extend `C508_RequestlessNotificationUpgrade` to verify the intent table/indexes/FKs and no legacy backfill. Preserve DG repair/hold/zero-sibling-mismatch coverage. |
| B/S2b-2: recovery, visibility and retention | `server/Application/Services/DispatchBaseWarningIntentService.cs`; `server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs`; `server/Application/Services/AttentionService.cs`; `server/Application/Services/DataRetentionService.cs`; A's attention enum/DTO/client mapping files | DBN `C508_WarningCommitCreatesObligation`, `C508_WarningCommitAtomic`, destination/retry/concurrency/lost-ack tests; `tests/Antiphon.Tests/Application/AgentTaskLandMonitoringTests.cs` pending-intent-to-note attention; `tests/Antiphon.Tests/Application/DataRetentionServiceTests.cs` original session/transcript/task-tree custody and unrelated pruning; focused `client/src/features/attention/attentionVisuals.test.ts`. |
| B/S2b-3: native acceptance and owner docs | new `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs`; `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs`; `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs`; `docs/orchestration-loop.md`; `docs/antiphon-api.md` | DE `C508_DispatchWarningPrecommitCrashRecovers`, producer/receipt, boot/recovery and mismatch methods; all prior V/R land cuts remain. Docs describe claim-owned original warnings, later-failure behavior and the reused notification table, without claiming S3 selection. |

Generate the intent table in the still-unimplemented S2b migration alongside
nullable RequestId; A's two-migration upgrade requirement remains. Do not
manually write migrations or amend an already-applied migration if Code later
finds one exists. No product source, migration or fixture is implemented by
this Plan amendment.

### B-7. Readiness and cost handoff

P-3 now has a durable producer identity, immutable body/destination, atomic
claim boundary, specified boot/due recovery owner, replay semantics and native
crash cuts. P-4 now has sufficient distinguishing input and defined outputs.
There is no pending scope decision for the caller.

**Next: test-design.** Re-audit PC-60/61 against B-4, relocate affected existing
controls, add the new custody/retention/attention guards, and enumerate the
complete verification floor before Code. The previous **413-minute lower
bound is not a complete floor**: include both missing cycles, claim/projection
crash variants, two-caller default diagnostics, concurrent recovery and the
additional retention/attention tests. Recompute counts and costs rather than
merely adding two rows to the previous 63 executable controls. No product test,
build or mutation ran here; document consistency checks are not runtime proof.


## Verification design: amendment B final audit

Appended by TestDesign task 3b831296 on 2026-09-14 at fetched/reset
origin/master d5eaa24a. This section is the current verification authority.
It supersedes the earlier failed executability/cost verdicts and the verification
rows explicitly replaced below; the fix design and historical audits are unchanged.
Scope remains **S1 + S1b + S2 + S2b; S3/S4/S5 remain deferred**.

**Verdict: next Code.** B-1 retains the distinguishing failed-default input;
B-2/B-3 retain the original dispatch obligation before materialization. PC-60
and PC-61 now have compiling defects and achievable restored expectations.
No further implementation-design or human decision is needed. “Executable”
here means specified against the commissioned implementation, not implemented
or run at this documentation-only baseline.

### Inspection

Bodies read during this pass, rather than inferred from names:

- AgentTaskDispatchBaseGuardTests in full, including all seven tests and
  SeedKeptSiblingAsync/SeedQueuedWorktreeTaskAsync/SeedParentSessionAsync/
  SeedCardAsync/CreateProvider | pre-lease observation, owner hold, stranded
  request, project omission and non-native parent -> V-2/V-7/V-15/V-16/V-23/V-24,
  R-3. The existing helper writes identical plan.md content for each sibling:
  the second divergent sibling needs a different file and branch from the common
  seed, otherwise fixture creation can produce no commit or accidental containment.
- DelegationWorktreeTests merge-target creation, two top-level tasks,
  NewTask/CreateService/ExpectedCoordinates; ScratchGitRepo and
  DelegationTestServices in full | real distinct refs, optional settings and
  direct construction -> V-1..V-7/V-22/V-30/V-31. Unchanged adoption/cleanup,
  land-marker, SourceLanding-world, contract and script bodies retain the
  earlier inspections; this pass does not claim to have re-read them.
- AgentTaskLandNotificationPersistenceTests in full; LandingSafetyHarness
  InitializeAsync/BuildServices/SeedAsync/RestartServicesAsync/CreateContext,
  SaveFault and TransactionFault | predecessor migration, before/after-save
  and committed-ack faults -> V-17/V-20/V-23/V-26. EventKind alone is not a
  task-scoped fault selector.
- AgentTaskLandNotificationRecoveryTests and AgentTaskLandReceiptTests in full;
  BridgeQueueHarness.CreateAsync/OnSubmitted/InsertEntryAsync; TestDbFixture
  and IsolatedTestSchema in full | 263-row scan, keyed collisions, retry,
  destination and false-receipt matrices, synthetic transcript source ->
  V-18/V-25..V-27, R-6/R-7/R-9. Cloned databases, not SearchPath isolation.
- AgentTaskLandDeliveryE2ETests.C467_V22..V32; LandDeliveryFixture and
  LandDeliveryOptions in full, including native caller setup, child ownership,
  ReceiptAsync, AssertOnePromptAsync, SnapshotAsync and FileBoundary |
  native busy/eligible input, both scan observations and owned restart ->
  V-11/V-12/V-14/V-16/V-21/V-25/V-30. These are the nearest fixtures for DE.
- AgentTaskLandMonitoringTests in full; attentionVisuals.test.ts including
  item/ALL_KINDS/key/target tests | threshold edges, error visibility,
  requestless attention and total client mapping -> V-19/V-28.
- DataRetentionServiceTests: first five transcript cases; session retention
  and cascade cases; stale/live/recent task-tree and zero-window cases;
  CreateService, seed/read/cleanup helpers | whole transcript, loose session
  references, tree-wide exclusion, cleanup ordering -> V-29/R-10.
  These are the nearest fixtures for the added retention methods.
- DelegationTestServicesTests and DelegationHarnessCensusTests in full |
  helper options, TryAdd and graph ownership -> R-11; these checks do not
  substitute for resolving the complete dispatcher or real Program boot.
- Production: dispatcher sibling guard, Warning producer, complete claim and
  launch tail; worktree probe/creation/reuse; notification reconciler and
  hosted scan in full; AttentionService.BuildLandItemsAsync;
  DataRetentionService session/transcript/queue/task pruning | B's actual
  handoffs, observable mutation seams and independently bypassable guards.

Required owners read: project-context; testing-and-build (isolation, clocks,
method filters, output custody and post-land Mutation); orchestration-loop
stage/receipt contract; session-runtime-invariants receipt requirements.
No build, product test or mutation ran.

Missing setup is assigned to Code, with concrete completion conditions:

1. Register the intent service/payload dependencies in Program and
   DelegationTestServices; extend direct constructions. DG component tests
   explicitly materialize committed intents, then reconcile exact note IDs.
   TickAsync no longer promises synchronous Warning/queue visibility.
   DBN uses DG's real dispatcher/Git graph plus the BridgeQueueHarness worker
   graph in an isolated cloned database. Override its NoWorktreeManager when
   composing the graphs. Register CompletionNoteFlushQueue and notifier.
2. BS uses ScratchGitRepo/DW construction, Integration and the process limiter.
   Pass GitSettings to the service, not just WorktreeManager. Set task.ProjectId
   explicitly for project tests. Keep independent P/T/M/E/C/repair/H tips;
   assert their inequality. Do not derive expected refs, digests or headers
   from the factory under test.
3. DE uses the real Program, real dispatcher and queue, native FakeGrok for
   BOTH caller and dispatched delegate, fixture-owned homes, modern ConPTY,
   Docker Postgres, pinned SDK and rebuilt client/dist. Keep the E2E process
   limiter and C467LandDelivery group. Assert owned runner port != 17204.
   Initialize the native parent first, then start the owned child with gates,
   then seed a NEW Queued dispatch task/card/project/siblings. The fixture's
   existing Succeeded landing TaskId is not that task. Do not make land
   requests or insert intents/events/notes to stand in for native production.
4. Extend FileBoundary with all seven B-4 names. Add an independent
   dispatch-warning-before-materialize gate, armed before dispatch, covering
   the scan and optional fast path before either takes the intent row lock.
   Scope barrier/fault records by task, final dispatch-event ID and intent ID.
   Scope queue barriers through the saved notification/queue identity.
   Add fixture-owned SaveChanges/transaction interceptors for claim,
   projection and retry-error writes; do not let quota/repair warnings arm them.
   A before-save failure, SavedChangesAsync failure and
   TransactionCommittedAsync lost acknowledgement are separate arrangements.
5. Capture immutable expected IDs/route/body/digest/time from a NEW observer
   after claim commit while materialization is gated. Save that snapshot
   outside the child before killing it. Restart the same DB/runner with the
   gate released; never requeue to prove original recovery. A preclaim rollback
   has no committed obligation, so its later successful dispatch is a new claim.
6. Replace land-kind/first-row receipt lookup with exact note-ID lookup.
   Compare the saved first line AND complete saved body, destination and
   queue attempt floor to UserPrompt and ConfirmingPromptSequence.
   Count native user_message_chunk submissions by the exact header, and also
   count unkeyed copies of the frozen warning detail to detect the deleted
   inline producer returning. Expected count is one per required intent after
   two further completed intent AND notification scans. Use a bounded collector
   that returns rows at deadline and asserts counts; never use an unreachable
   note boundary or generic UntilAsync timeout as PC red.
7. Preserve the A-audit lease decorator for M->P/HEAD->M mismatch. Add a
   post-observation, pre-claim route edit using a fresh context for V-24; force
   the locked claim to load the changed route (do not reuse a tracked outer
   task). For frozen-observation tests, move/delete sibling refs only after
   observing their original draft, and before capture/projection as specified.
8. Retention tests use an isolated clone and the nearest helpers, retaining
   [NotInParallel] for global sweeps. Use 200-day-old terminal task trees and
   100-day-old terminal sessions/transcripts. Remove PersistentSessionId,
   AgentTask.AgentSessionId and current ParentSessionId references to the old
   destination, with no queue/note before projection, so existing guards cannot
   mask missing intent protection. Test session and transcript passes separately.
   Dispose the clone or delete owned notes/intents before their Restrict-linked
   events/tasks; the old CleanupAsync order alone cannot clean this new fixture.
9. Race tests use instance-scoped DB interceptors/barriers, fresh scopes and
   Task.WhenAll that is awaited. Intercept the intent SELECT for the row-lock
   test: hold the first materializer before pair commit; a second scope's
   lock acquisition must remain pending until release. Observe both completions
   with explicit error/count assertions. A SQL constraint exception escaping
   setup is not a passing concurrency test or a valid PC red.
10. Attention handoff tests use command interceptors to commit projection
    between the two reads in EACH order. Also create a newer unrelated note on
    the same task to prove suppression is by exact NotificationId. Component
    clock may be frozen; queue/native clock must advance with real time.
    Retention handoff tests interleave projection at the corresponding query
    boundary and retain the original destination in both orders.
11. V-20 upgrades from each of the TWO new migrations' immediate predecessors,
    using historical raw SQL as the read upgrade fixture does. Preserve an old
    land request/pair and a historical dispatch; verify no new intents/backfill,
    unique (DispatchEventId,WarningKey), unique NotificationId, due index,
    Restrict task/event FKs, and nullable RequestId. Preserve existing event/
    queue uniqueness and old enum values. Reapply migrations inertly. C508_IntentUniqueKeys also upgrades a disposable predecessor store; mutating an EF model index alone would leave an already-created database index unchanged and is not an executable PC.

### Delivery inventory

The following replaces all earlier dispatch-delivery inventories.

| Path | Producer -> destination | Persistence boundary and durable identity | Recovery / observable receipt |
|---|---|---|---|
| Base/repair detail | Provisioning + successful claim -> task detail reader | TaskId and creation/final dispatch event IDs; tuple and session in claim transaction | Fresh DB/HTTP tuple/event read, V-8/V-10/V-15; query visibility, not session input |
| Sibling warning | Frozen pre-lease guard -> Capture -> original parent | Final dispatch-event ID + sibling:<id> -> intent.Id (future Warning.Id), NotificationId; immutable route/body/digest committed with claim | Boot/due intent scan, locked projection, notification scan, keyed queue, exact complete original UserPrompt |
| Ref-mismatch warning | Observed-ref vs recorded-ref comparison -> Capture -> original parent | Same chain, WarningKey=base-observation-stale; independent even with zero sibling warnings | Same recovery and receipt; one mismatch per successful claim |
| Newly used unresolved-default warning | Provisioning's B-1 metadata -> Capture -> original parent | Same chain, WarningKey=default-unresolved; full failed name retained, including non-card dispatch | Same recovery and receipt, V-30; standalone provisioning result is not native acceptance |
| Intent projection | MaterializeAsync -> Warning + DispatchBase note | intent.Id = Warning.Id = note.SourceEventId; intent.NotificationId = note.Id; pair + MaterializedAt commit atomically | Lock, rollback, due retry, lost-ack reload. MaterializedAt is not a receipt |
| Note delivery | ReconcileAsync -> session queue -> original parent | note.Id = SourceLandNotificationId; exact QueueMessageId, digest, route and attempt floor | Keyed insert recovery, completion/notification scans, catch-up; matching complete UserPrompt + native census |
| Land sibling marker | Real land -> caller | Existing request -> terminal event -> note -> keyed queue | All V-11/V-12 cuts retained with changed real-Git sibling payload; complete caller UserPrompt |
| Pending warning attention | Intent or exact note -> attention HTTP/client | dispatch:<NotificationId:N>:receipt across materialization | One condition until receipt, threshold/error visibility; never creates a land request and never discharges delivery |

Custody belongs to the successful claim even if launch later throws, the task
settles, Attempt changes, the parent route changes, or refs disappear. No Ready
bit, dispatcher liveness, current task status, new request or requeue is a
recovery prerequisite. Same claim replays existing identities; a new successful
claim with identical TaskId/Attempt/text owes new identities.

These native cases use actual producers. Helpers can share arrangement code;
each named method executes its own specified cut, not the entire matrix.
This split replaces the aggregate C508_SiblingOutcomeRecovery and
C508_WarningRecovery test names, preserving all their cases.

| V ID / exact method (LD or DE alias below) | Handoff / required observation before continuation |
|---|---|
| V-12 LD.C508_SiblingOutcomeRollbackRecovers | Before terminal commit: event/note both absent after rollback; restart real land producer, then receipt |
| V-12 LD.C508_SiblingOutcomeCommitRecovers | Terminal committed, no queue: same note reaches receipt; no repeated publication |
| V-12 LD.C508_SiblingOutcomeEnqueueRecovers | Two failed before-enqueue calls: original obligation/backoff, then receipt |
| V-12 LD.C508_SiblingOutcomeQueueInsertRecovers | Busy caller, queue insert before note QueueMessageId save: same keyed row, release, one receipt |
| V-12 LD.C508_SiblingOutcomeLostFlushRecovers | Eligible caller, drop completion wakeup: scan delivers without new input |
| V-12 LD.C508_SiblingOutcomeAttemptRecovers | Attempt persisted before typing: restart, same row, one complete prompt |
| V-12 LD.C508_SiblingOutcomeVerdictRecovers | Native prompt before verdict: restart/catch-up, same prompt, no retyping |
| V-12 LD.C508_SiblingOutcomeReceiptSaveRecovers | Prompt before receipt save: restart, same sequence, one native input |
| V-12 LD.C508_SiblingOutcomeConfirmedDoesNotReplay | Receipt saved: restart and two more scans, no second submission |
| V-21 DE.C508_ClaimRollbackCreatesNoObligation | dispatch-warning-claim-before-commit hard kill: zero committed final event/new session/intents/pair. No recovery receipt owed by this aborted claim |
| V-21 DE.C508_DispatchWarningPrecommitCrashRecovers | Immediate dispatch-warning-claim-committed cut, eligible parent; fast path has not run, independent materialization gate held. Capture original intents/zero pairs; kill/restart -> exact original receipts |
| V-21 DE.C508_ClaimCommitBusyRecovery | Same immediate postclaim cut with busy parent; zero delivery attempts before release, then original receipts |
| V-21 DE.C508_DispatchReturnCrashRecovers | Successful dispatch-warning-after-dispatch cut with materialization gated; kill/restart -> original receipts |
| V-21 DE.C508_ProjectionPrecommitCrashRecovers | Gate entry first to capture expected intent, release entry, kill at dispatch-warning-before-commit; pair/marker absent, original intent retries to receipt |
| V-21 DE.C508_ProjectionSaveFailureRecovers | Two argument rows: before-save and after-save projection faults; observe rollback, restart, same IDs/body/route to receipt |
| V-21 DE.C508_PostClaimLaunchFailureRecovers | One-shot failure in postcommit launch-spec tail after snapshot; failed task still owns intent; restart/due scan reaches original parent |
| V-14 DE.C508_WarningBootScanReachesCaller | Pair + marker committed at dispatch-warning-materialized; hold before-enqueue, restart; scan delivers same note |
| V-14 DE.C508_WarningPairCommitRecovers | Pair committed, before-enqueue cut; original event/note survives restart to receipt |
| V-14 DE.C508_WarningEnqueueRecovers | Two enqueue failures, same ID/backoff; due retry -> receipt |
| V-14 DE.C508_WarningQueueInsertRecovers | Busy parent, ambiguous insert: exact queue ID reused, one receipt |
| V-14 DE.C508_WarningLostFlushRecovers | Eligible parent, lost wakeup: completion scan -> receipt |
| V-14 DE.C508_WarningAttemptRecovers | Attempt-before-typing cut: original row -> complete prompt |
| V-14 DE.C508_WarningVerdictRecovers | Prompt-before-verdict cut: same prompt after restart, no duplicate |
| V-14 DE.C508_WarningReceiptSaveRecovers | Receipt-before-save cut: same sequence after restart, no duplicate |
| V-14 DE.C508_WarningConfirmedDoesNotReplay | Saved receipt, restart/two further scans: one native input |

Each DE required-recipient row includes at least one real divergent sibling;
the immediate postclaim cut uses a sibling warning. V-16's two native mismatch
methods and V-30's four default rows also snapshot at the gated postclaim cut
and restart before delivery, exercising their own real producers through the
same claim/projection factories. Busy x every fault x every warning kind is
excluded: queue eligibility is common to the keyed queue, the claim cut has
busy and eligible coverage, V-14/V-16/V-30 exercise each producer and ordinary
components cross the payload/state boundaries. No handoff is excluded.

Substitutes remain explicit: real Git proves patch/base facts; DG fake session
rows prove claim/DB behavior; BridgeQueueHarness inserts synthetic transcripts
and proves worker/matcher behavior; fresh HTTP proves reader visibility.
Native FakeGrok proves the production queue/runner/native transcript path for
that profile, not a hosted model. Acceptance, insertion, terminal event,
MaterializedAt, Sent, ConfirmedAt without its linked complete prompt, screen
or transport acknowledgement alone never satisfy delivery acceptance.
**Ordinary Review must reject evidence stopping before recipient evidence.**

### Proves it works now

All names below are implementation requirements. DW/BS/DG/LS/LD/CS retain their
earlier expansions. DBN = DispatchBaseNotificationTests; DE =
DispatchBaseWarningDeliveryE2ETests; LM = AgentTaskLandMonitoringTests;
DR = DataRetentionServiceTests. DBN/BS are new files; their nearest fixtures
were read above. V-1..V-13 and V-15 retain the A-audit amendments except where
this section updates setup or splits a method.

| ID | Behavior / layer | Exact test / setup | Expected |
|---|---|---|---|
| V-2 update | Named fallback / dispatcher | DG.C508_MissingDefaultWarns: two generated invalid project/global names, card/non-card, detached valid H and unborn-H refusal; materialize capture | Newly used fallback records H/RepoHead, exactly one matching default intent/Warning/note with original name; absent usable H fails with no new session/intent |
| V-11 update | Land marker / native | LD.C508_SiblingOutcomeReachesCaller has equivalent+eligible, equivalent+busy and mixed+eligible rows; LD.C508_SiblingOutcomeWaitsForBusyCaller has the mixed+busy row | Four distinct real-producer cases retained; complete expected marker/body and one native prompt; busy attempts remain zero before release |
| V-14 update | Producer -> native parent | DE.C508_WarningProducerToReceipt (eligible), DE.C508_WarningBusyProducerToReceipt (busy), two divergent siblings and two genuine claims each; quiesce/stop the owned delegate before arranging Queued/adoption; keep Attempt unchanged for the second claim | Four distinct warning/notification IDs per method, two final dispatch-event IDs, one queue/complete native prompt per warning. Repeated ticks without a claim add none; busy has no typing before release |
| V-16 update | Ref mismatch / dispatcher + native | DG.C508_GuardRefMismatchWarnedOnce keeps M->P, HEAD->M, 0/1/2 warnings and equal-ref rows; DE.C508_MismatchWarningReachesCaller eligible, DE.C508_MismatchWarningWaitsForCaller busy | One base-observation-stale intent and pair per unequal-ref claim, N+1 total; complete mismatch prompt once in original parent |
| V-17 update | Atomic materialization / PostgreSQL | DBN.C508_WarningCommitCreatesObligation, .C508_WarningCommitAtomic, real dispatcher-produced intent then materialization; before/after-save faults | Success: exactly one Warning/note + marker with original IDs. Fault: original pending intent remains, zero pair, null marker. Dispose failed scope, retry same intent, exactly one pair |
| V-18 update | Frozen payload/state / component | DBN.C508_WarningPayloadAndDestination captures intent first; edit task A->B before projection, again before reconcile. Retain C508_WarningStatesAndLease, C508_WarningQueueDigestCollision, C508_WarningsAreNotReports | Exact independently formatted header and route+body digest at claim, projection and queue; original destination. None has no enqueue; null/missing/stopped/failed/deleted destination remains owed. No digest collision or report consumption |
| V-19 update | Note attention / DB+HTTP/client | LM.C508_RequestlessDispatchNotes, focused attentionVisuals.test.ts case from A | Correct kind, dispatch key, nullable request and label; Outcome aging unchanged; linked complete receipt clears note condition |
| V-20 update | Both migration boundaries / PostgreSQL | DBN.C508_RequestlessNotificationUpgrade plus BS.C508_BaseFieldsUpgradeAndRoundTrip | Setup item 11 schema/index/FK assertions, no intent backfill, preserved old land data, independent input/output columns and stable second contract comparison |
| V-21 replacement | Lost original producer handoffs / native | Exact DE methods in cut table above | Claim rollback owes nothing; every committed intent reaches its original parent after restart without requeue. Correct original IDs/body/time/route survive pair rollback and postclaim launch failure |
| V-22 replacement | Failed default name / pure resolver | BS.C508_UnresolvedDefaultRetainsName generates two unequal invalid refs; all five B-1 arms crossed with success/failure probe | RepoHead retains the respective verbatim name; higher-priority arms retain their own ref/source and null diagnostic. Names are inputs, never fixture constants in production |
| V-23 | Claim custody / dispatcher + PostgreSQL | DBN.C508_ClaimCapturesWarningIntents; .C508_ClaimIntentAtomic (before-save, after-save, before-commit); .C508_RefusedClaimHasNoIntent (lease/sibling hold, stale claim, invalid ref, failed progress baseline, optional expiry) | Successful final claim event, session/base and every expected draft commit together. Aborted/refused claims have no committed final event/session/intent; baseline-failure creation event is not mistaken for successful final dispatch |
| V-24 | Identity and captured route / component + real dispatcher | DBN.C508_IntentAttemptIdentity; .C508_IntentCaptureRoute; .C508_IntentCaptureBinding; .C508_IntentUniqueKeys | Same committed event with reordered drafts reuses each key/ID; new final event with same task/Attempt/text gets distinct IDs. Locked claim's edited parent wins over stale outer task. Cross-task capture refuses before rows; duplicate event/key or NotificationId violates the named unique constraint |
| V-25 | Immutable projection / component + native | DBN.C508_IntentProjectionCustody; DE.C508_IntentFrozenRecoveryReachesCaller with task Succeeded/Failed/Canceled/Queued (four rows), Attempt and route A->B edits, sibling refs moved/deleted after committed-intent snapshot | Exact saved Warning/notification IDs, detail/body/digest/CreatedAt/route; no Git re-evaluation; original parent receives once, B receives zero. Gate ordinary redispatch for Queued row until old-intent receipt, then a genuinely new claim owes different IDs |
| V-26 | Serialized projection and integrity / PostgreSQL | DBN.C508_IntentProjectionConcurrent; .C508_IntentProjectionLostAcknowledgement; .C508_IntentProjectionIntegrity | Competing scopes block/then no-op with no caught errors, one pair/marker. Postcommit lost ack retains marker/IDs/attempts. Digest, independently bad header with valid digest, cross-task dispatch-event binding and unexpected existing IDs each leave pending marker, error and no replacement rows |
| V-27 | Retry and scan fairness / hosted service + DB | DBN.C508_IntentMaterializationRetry; .C508_IntentRetryBookkeepingFailure; .C508_IntentScanPaging; .C508_IntentScanIsolation | 5/10/20/40/80/160/300/300 seconds; the actual worker skips before due and retries at equality (direct MaterializeAsync calls are not claimed to enforce scan eligibility). Bookkeeping failure leaves original intent discoverable. 263 intents ordered by PostgreSQL ID, poison first row, future row, terminal/edited-attempt task and materialized row; all due healthy rows across three pages progress. Next cycle revisits newly due low ID; row or whole intent-pass fault still permits unrelated note handoff |
| V-28 | Pre-note attention / DB+HTTP | LM.C508_PendingIntentAttention; .C508_IntentAttentionHandoff; frozen clock and both read-order interleavings | At 299.999/300/899.999/900s: absent/Warning/Warning/Error. Earlier error: Error. None: absent. Missing destination: visible. Exact dispatch key remains one condition through commit, no request clause/Aged request, null LandNotificationId before row, real ID afterwards; receipt removes it |
| V-29 | Recipient/history retention / PostgreSQL | DR.C508_IntentSessionRetention; .C508_IntentTranscriptRetention; .C508_IntentTaskTreeRetention; .C508_NotificationTaskTreeRetention | Required pending intent protects old destination and every transcript row despite task route edit, including projection interleavings; unresolved note takes over. None and confirmed-note controls permit ordinary pruning when no other references. Any intent (pending/materialized/None) or note (including confirmed/None) on a child protects entire stale tree; unrelated tree/events prune in same sweep without exception |
| V-30 | Both default callers / real Git + native | BS.C508_ProvisioningPreservesFailedDefault; DG.C508_GuardPreservesFailedDefault; DE.C508_MissingDefaultReachesCaller: two generated invalid names x (card+busy, non-card+eligible), four rows | Provisioning metadata retains each failed name; guard independently observes HEAD after the chosen candidate fails (M exists and differs from H); materialized warning/native complete body names it. Each chosen invalid ref falls to H even with valid lower-priority setting |
| V-31 | Default boundary combinations / real Git + component | BS.C508_DefaultCandidateSelection; .C508_DefaultProbeCancellation; extend DW.C508_ReuseKeepsRecordedBase | null/empty/whitespace project and global settings select first nonblank, then master; nonblank invalid ref retained verbatim, no cascade to next setting. Canceled probe propagates cancellation, no fallback. Unused/default probe failure on deliberate base or reuse creates no diagnostic; recorded tuple survives |

V-18 also runs DispatchBase through the read false-evidence matrix, adding
wrong task/event/header and flattened-complete cases. These are component
matcher assertions; V-14/V-16/V-21/V-25/V-30 remain native acceptance.

### Guards the regression

- R-1..R-8 remain with A's request/record correction. R-3 now explicitly
  materializes original intents before note reconciliation. Preserve hold,
  deleted-branch, stranded-request and repair-owner assertions; no S3 inversion.
- R-9: shared notification scan and receipt remain kind-agnostic |
  AgentTaskLandNotificationRecoveryTests.C467_V10_BootScanFairnessAndClearedPending,
  C467_V08_DestinationSnapshotsRemainOwed, C467_V09_KeyedQueueRacesAndDistinctEvents,
  C467_V14_LandNotesDoNotCountAsReports; AgentTaskLandReceiptTests
  .C467_V11_RejectFalseReceipts, .C467_V12_CatchUpAndRecoverReceiptWithoutRetyping,
  .C467_V13_RetentionCancellationAndSupersession. Preserve exact queue ID,
  immutable bytes, receipt floor and unconfirmed retention assertions.
- R-10: retention still makes progress | DR stale terminal session/transcript,
  no-referencer cascade, fully stale task tree, live/recent tree and zero-window
  cases read above. V-29 adds unrelated eligible tree/session controls and
  asserts caught sweep exception is null before checking retained/deleted IDs.
- R-11: construction compatibility | DelegationTestServicesTests and
  DelegationHarnessCensusTests (existing owner-prescribed batteries), affected
  dispatcher classes and real Program boot resolve capture dependencies;
  required capture is never silently skipped on null optional dependency.
- The new focused native methods replace aggregate method names only.
  No cut, warning producer, native receipt, two-extra-scan census or failed
  receipt row is dropped to obtain a cheaper verification floor.

### Guard inventory

This is the complete current inventory, including the not-yet-implemented
assertions. Each G-n maps 1:1 to distinct PC-n below. G-23/G-43 split patch
awareness from pinned SHA; G-38/G-63 split destination from digest collision;
G-52/G-71/G-73 split queue, claim and projection destination custody;
G-51/G-67 split projection from claim atomicity; G-42/G-60 split notification
from intent scans; G-65/G-116 split capture invocation from the guard's
zero-sibling observation. These independent fences must not mask one another.
No "none" justification applies. Field widths, nullable DTO shape and enum
compatibility are V-10/V-20 checks; new identity uniqueness and binding guards
are expressly inventoried. Existing provider/cleanup/authentication guards
outside the changed paths remain with their owning suites.

| Guard | Plan reference / safety-critical invariant | Positive control |
|---|---|---|
| G-1 | S1 requested ref wins | PC-1 |
| G-2 | S1 merge target beats defaults | PC-2 |
| G-3 | D-1 merge target is never changed | PC-3 |
| G-4 | B-1 project default beats global | PC-4 |
| G-5 | B-1 global default beats hard master | PC-5 |
| G-6 | S1 direct construction defaults to master | PC-6 |
| G-7 | S1 defaults apply without a card | PC-7 |
| G-8 | D-1 recorded SHA equals created base | PC-8 |
| G-9 | D-1 Source identifies selected arm | PC-9 |
| G-10 | D-1 Ref identifies selected arm | PC-10 |
| G-11 | S1 no fabricated sibling owner | PC-11 |
| G-12 | B-1/B-2 used unresolved default owes warning | PC-12 |
| G-13 | D-2 invalid deliberate base refuses | PC-13 |
| G-14 | A-1.6 recorded decision survives reuse | PC-14 |
| G-15 | SourceLanding uses verified L | PC-15 |
| G-16 | A-1.4 repair start SHA wins | PC-16 |
| G-17 | D-4/S1 service enforces owned lease | PC-17 |
| G-18 | S2 successful empty cherry means contained | PC-18 |
| G-19 | S2 all-minus cherry means contained | PC-19 |
| G-20 | S2 any plus means uncontained | PC-20 |
| G-21 | S2 failed Git read never proves containment | PC-21 |
| G-22 | A-1/B-1 shared formula uses actual default | PC-22 |
| G-23 | S2 land comparison is patch-aware | PC-23 |
| G-24 | D-3 active sibling land still holds | PC-24 |
| G-25 | D-3 stranded event is not active land | PC-25 |
| G-26 | Delivery terminal event includes obligation | PC-26 |
| G-27 | Delivery queue key retains note identity | PC-27 |
| G-28 | Delivery enqueue failure remains retryable | PC-28 |
| G-29 | Receipt requires UserPrompt kind | PC-29 |
| G-30 | Receipt belongs to destination | PC-30 |
| G-31 | Receipt carries matching identity | PC-31 |
| G-32 | Receipt body is complete | PC-32 |
| G-33 | Receipt is above sequence floor | PC-33 |
| G-34 | Receipt respects timestamp floor | PC-34 |
| G-35 | Queue respects busy recipient | PC-35 |
| G-36 | Queue recovers eligible recipient's lost wakeup | PC-36 |
| G-37 | Queue catches up receipt without retyping | PC-37 |
| G-38 | Keyed queue rejects crossed destination | PC-38 |
| G-39 | Terminal save failure rolls back pair | PC-39 |
| G-40 | B-3 materialized warning includes note | PC-40 |
| G-41 | B-2/B-3 distinct warnings never coalesce by task | PC-41 |
| G-42 | B-3 notification scan includes DispatchBase | PC-42 |
| G-43 | S2 land comparison pins verified SHA | PC-43 |
| G-44 | A-1.6 recorded Ref is not input | PC-44 |
| G-45 | A-1.4 fresh repair skips sibling evaluation | PC-45 |
| G-46 | A-1.4 repair Source is Repair | PC-46 |
| G-47 | A-1.4 repair records owner task | PC-47 |
| G-48 | B-2 unequal ref observation produces mismatch | PC-48 |
| G-49 | B-2 one mismatch per successful claim | PC-49 |
| G-50 | B-2/B-3 mismatch owes parent delivery | PC-50 |
| G-51 | B-3 projection pair/marker rolls back together | PC-51 |
| G-52 | B-3 queue handoff uses note's saved destination | PC-52 |
| G-53 | B-2 capture digest binds route and full body | PC-53 |
| G-54 | A-2/B-2 None creates no delivery attempt | PC-54 |
| G-55 | A-2/B-2 missing destination remains owed | PC-55 |
| G-56 | A-2/B-3 dispatch delivery ignores occupied repo lease | PC-56 |
| G-57 | A-2.7 monitor excludes requestless notes | PC-57 |
| G-58 | A-2.7 note attention has dispatch kind | PC-58 |
| G-59 | Shared warning is not completion report | PC-59 |
| G-60 | B-3 boot/due intent scan owns pre-pair recovery | PC-60 |
| G-61 | B-1 resolver retains failed default name | PC-61 |
| G-62 | B-2 capture header binds note/task/warning | PC-62 |
| G-63 | Keyed queue rejects same-destination digest collision | PC-63 |
| G-64 | B-1 probe accepts only commit refs | PC-64 |
| G-65 | B-2 capture executes for zero-sibling mismatch | PC-65 |
| G-66 | B-2 successful claim includes every intent | PC-66 |
| G-67 | B-2 claim and intents share atomic transaction | PC-67 |
| G-68 | B-2 attempt identity is final dispatch event | PC-68 |
| G-69 | B-2 new claim never coalesces by task/Attempt | PC-69 |
| G-70 | B-2 replay preserves warning-key identity | PC-70 |
| G-71 | B-2 route comes from locked claim | PC-71 |
| G-72 | B-2 capture retains original observed detail | PC-72 |
| G-73 | B-3 materialization retains intent destination | PC-73 |
| G-74 | B-3 materialization retains complete saved body | PC-74 |
| G-75 | B-3 Warning event keeps intent ID | PC-75 |
| G-76 | B-3 note keeps preallocated NotificationId | PC-76 |
| G-77 | B-3 row lock serializes competing materializers | PC-77 |
| G-78 | B-3 committed projection wins lost acknowledgement | PC-78 |
| G-79 | B-3 unexpected existing IDs are integrity refusal | PC-79 |
| G-80 | B-3 validates saved route/body digest | PC-80 |
| G-81 | B-3 validates identifying header independently | PC-81 |
| G-82 | B-3 validates dispatch-event/task binding | PC-82 |
| G-83 | B-3 materialization failure persists bounded backoff | PC-83 |
| G-84 | B-3 failed error bookkeeping cannot discharge intent | PC-84 |
| G-85 | B-3 scan ignores current task status | PC-85 |
| G-86 | B-3 scan ignores changed Attempt | PC-86 |
| G-87 | B-3 scan advances beyond page one | PC-87 |
| G-88 | B-3 cursor resets every cycle | PC-88 |
| G-89 | B-3 one row failure cannot stop later rows | PC-89 |
| G-90 | B-3 intent-pass failure cannot suppress notification pass | PC-90 |
| G-91 | B-3 session retention protects pre-note destination | PC-91 |
| G-92 | B-3 transcript retention protects whole pre-note transcript | PC-92 |
| G-93 | B-3 any intent protects entire task tree | PC-93 |
| G-94 | B-3 any note protects entire task tree | PC-94 |
| G-95 | B-3 required unmaterialized warning is visible | PC-95 |
| G-96 | B-3 attention identity spans intent and note | PC-96 |
| G-97 | B-3 concurrent reads merge to one condition | PC-97 |
| G-98 | B-3 exact existing note suppresses stale intent projection | PC-98 |
| G-99 | B-3 None has no pending-receipt attention | PC-99 |
| G-100 | B-3 materialization errors surface before age threshold | PC-100 |
| G-101 | B-3 overdue pending receipt escalates | PC-101 |
| G-102 | B-3 pre-note attention never fabricates note reference | PC-102 |
| G-103 | B-1 provisioning caller preserves failed candidate input | PC-103 |
| G-104 | B-1 guard honors failed chosen default without cascading | PC-104 |
| G-105 | B-1 unused deliberate-base defaults create no diagnostic | PC-105 |
| G-106 | B-2 refusal cannot commit warning intent | PC-106 |
| G-107 | B-2 deleted inline warning producer stays deleted | PC-107 |
| G-108 | B-2 database enforces per-attempt warning uniqueness | PC-108 |
| G-109 | B-2 database enforces preallocated note uniqueness | PC-109 |
| G-110 | B-2 Capture validates event belongs to claim task | PC-110 |
| G-111 | B-1 choose first nonblank configured default | PC-111 |
| G-112 | B-1 cancellation is not a missing default | PC-112 |
| G-113 | B-3 intent scan respects NextAttemptAt | PC-113 |
| G-114 | B-3 projection preserves original age | PC-114 |
| G-115 | B-3 projected Warning preserves original detail | PC-115 |
| G-116 | B-2 guard observes base even when sibling query is empty | PC-116 |

### Positive controls

For each row: break the corresponding G by the stated compiling production
defect, keep tests/expected values fixed, require the exact method red at its
listed assertion, restore, rebuild and require that same method green.
The table replaces every earlier PC definition, including the historical
failed-seam rows. Aliases expand exactly as in V/R; select the literal class
and exact method from each row with --treenode-filter, as the PC-60/61 commands
below demonstrate. Parameterized methods run all their declared rows; inspect the decisive
mutated row, not exit status alone.

| Control | Compiling defect | Exact method / decisive red assertion |
|---|---|---|
| PC-1 | Drop requestedRef from Resolve precedence | BS.C508_BasePrecedence: E+C+P creates HEAD=E |
| PC-2 | Drop mergeTargetRef from Resolve precedence | BS.C508_BasePrecedence: C+P creates HEAD=C |
| PC-3 | Assign selected base to task.MergeTargetRef | BS.C508_BasePrecedence: original/null MergeTargetRef unchanged |
| PC-4 | Choose global before nonblank project default | BS.C508_DefaultBranchMatrix: project rows HEAD=P |
| PC-5 | Replace configured global default with master | BS.C508_DefaultBranchMatrix: global rows HEAD=T |
| PC-6 | Use main when options absent | BS.C508_DefaultBranchMatrix: no-options HEAD=M |
| PC-7 | Gate default resolution on non-null CardId | BS.C508_DefaultBranchMatrix: non-card HEAD=selected default |
| PC-8 | Record main checkout HEAD instead of created worktree HEAD | BS.C508_DefaultBranchMatrix: stored SHA=created HEAD, not H |
| PC-9 | Record RepoHead for resolved DefaultBranch | BS.C508_DefaultBranchMatrix: Source=DefaultBranch |
| PC-10 | Record HEAD for resolved default | BS.C508_DefaultBranchMatrix: Ref=chosen default name |
| PC-11 | Set WorktreeBaseTaskId=task.Id on default arm | BS.C508_DefaultBranchMatrix: BaseTaskId=null |
| PC-12 | Omit default-unresolved draft from Capture input | DG.C508_MissingDefaultWarns: one default intent and Warning naming original ref |
| PC-13 | Replace unresolved requested/merge ref with HEAD before creation | BS.C508_BasePrecedence: ValidationException naming invalid E/C; no new tree |
| PC-14 | Overwrite BaseSha with adopted worktree's advanced HEAD | DW.C508_ReuseKeepsRecordedBase: original BaseSha unchanged |
| PC-15 | Pass current repository HEAD instead of VerifiedSourceSha to CreateVerificationAsync | PostLandMutationWorktreeTests.C508_SourceLandingIgnoresDefault: snapshot HEAD=L |
| PC-16 | Drop repairStartSha from Resolve precedence | DW.C508_RepairStartShaWins: HEAD=owner repair SHA |
| PC-17 | Remove only service's failed-Owns throw, retaining manager guard | DW.C508_BaseResolutionRequiresOwnedLease: manager call count=0 after foreign-lease refusal |
| PC-18 | Return false for empty successful cherry output | BS.C508_CherryContainmentMatrix: ancestor row true |
| PC-19 | Restore ancestry-only helper | BS.C508_CherryContainmentMatrix: rebased non-ancestor/all-minus row true |
| PC-20 | Use Any(minus) instead of All(minus) | BS.C508_CherryContainmentMatrix: mixed row false |
| PC-21 | Return true for unsuccessful cherry | BS.C508_CherryContainmentMatrix: missing-ref row false |
| PC-22 | Resolve successful DefaultBranch as Ref=HEAD, preserving Source | DG.C508_RebasedSiblingUsesActualDefault: independent expected HEAD=M and exact hold/warning result |
| PC-23 | Use ancestry-only comparison at land call | LS.C508_RebasedSiblingMarkerMatrix: all-minus marker absent |
| PC-24 | Skip LandRequestedAt hold arm | DG.a_sibling_land_in_flight_holds_until_the_base_contains_it: first status Queued, path null |
| PC-25 | Treat historical LandRequested event as active despite null column | DG.a_stranded_request_row_with_a_null_column_only_warns: Dispatched and zero Held |
| PC-26 | Omit AddNotification in terminal land producer | LD.C508_SiblingOutcomeCommitRecovers: committed terminal has one owed note and complete original receipt |
| PC-27 | Pass fresh Guid on each reconcile keyed enqueue | LD.C508_SiblingOutcomeQueueInsertRecovers: original queue ID reused and one native prompt |
| PC-28 | Return when EnqueueAttempts>0 and QueueMessageId=null | LD.C508_SiblingOutcomeEnqueueRecovers: original note obtains one complete prompt |
| PC-29 | Allow QueuedUserPrompt alongside UserPrompt | AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts: queued-prompt row ConfirmedAt=null |
| PC-30 | Remove session predicate from receipt query | AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsDestination: ConfirmedAt=null |
| PC-31 | Strip first line from expected body in BOTH IsConfirmedBy and IsCompleteIn receipt conjuncts | AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsIdentity: wrong identity leaves ConfirmedAt=null |
| PC-32 | Remove IsCompleteIn conjunct | AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsCompleteBody: head-only remains unconfirmed |
| PC-33 | Remove sequence-floor predicate | AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsSequenceFloor: ConfirmedAt=null |
| PC-34 | Remove timestamp-floor predicate | AgentTaskLandReceiptTests.C488_ApprovalReceiptNeedsTimeFloor: ConfirmedAt=null |
| PC-35 | Force queue working verdict false in delivery eligibility | LD.C508_SiblingOutcomeWaitsForBusyCaller: DeliveryAttempts=0 before release |
| PC-36 | Disable completion scan, retaining test's dropped wakeup | LD.C508_SiblingOutcomeLostFlushRecovers: complete prompt without new input |
| PC-37 | Return LateConfirmCounts.Empty before attempted-row examination | LD.C508_SiblingOutcomeVerdictRecovers: native submission count=1 after catch-up |
| PC-38 | Remove destination conflict comparison, retaining digest comparison | AgentTaskLandNotificationRecoveryTests.C467_V09_KeyedQueueRacesAndDistinctEvents: crossed-session enqueue throws ConflictException |
| PC-39 | Commit/dispose terminal transaction before SaveChanges; remove later commit | AgentTaskLandNotificationPersistenceTests.C508_AfterSaveRollsBackOutcome: after-save fault leaves zero terminal events/notes |
| PC-40 | In MaterializeAsync omit note insertion, retaining Warning and marker save | DBN.C508_WarningCommitCreatesObligation: Warning count=1, matching note count=1 (mutant 0) |
| PC-41 | At projection skip note insertion if Local/DB already has a DispatchBase note for TaskId; still insert Warning/marker | DE.C508_WarningProducerToReceipt: each original warning has a note/complete native receipt; first claim note count=2 |
| PC-42 | Add Kind!=DispatchBase to notification scan, retaining intent pass | DE.C508_WarningBootScanReachesCaller: original note complete prompt count=1 after restart/scans |
| PC-43 | Pass moving HEAD instead of verifiedSha | LS.C508_RebasedSiblingMarkerMatrix: pinned-M row retains marker even though moving HEAD contains patch |
| PC-44 | Pass WorktreeBaseRef instead of WorktreeBaseRequestedRef to resolver | DW.C508_ReuseKeepsRecordedBase: adversarial recorded E/null request/no managed tree creates at M |
| PC-45 | Remove fresh-repair skip only, retaining owner land hold | DG.C508_RepairRecordsOwnerAndSkipsSiblings: other sibling's active land cannot prevent Dispatched |
| PC-46 | Record Explicit for Repair source | DG.C508_RepairRecordsOwnerAndSkipsSiblings: persisted Source=Repair |
| PC-47 | Set repair WorktreeBaseTaskId=null | DG.C508_RepairRecordsOwnerAndSkipsSiblings: BaseTaskId=owner.Id |
| PC-48 | Invert mismatch comparison when building capture drafts | DG.C508_GuardRefMismatchWarnedOnce: unequal-ref/one-sibling row mismatch count=1; equal-ref count=0 |
| PC-49 | In mismatch materialization, if claim has >1 sibling intent, add a second valid Warning/note pair with fresh IDs and same detail | DG.C508_GuardRefMismatchWarnedOnce: two-sibling claim has one mismatch and three total notes (mutant two/four), no constraint error |
| PC-50 | Omit only mismatch note insertion in materializer; retain Warning/marker | DE.C508_MismatchWarningReachesCaller: zero-sibling mismatch has one complete native prompt |
| PC-51 | Commit/dispose projection transaction before SaveChanges; remove later commit | DBN.C508_WarningCommitAtomic: after-save fault leaves pair counts=0 and MaterializedAt=null |
| PC-52 | In ReconcileAsync replace DispatchBase ParentSessionId with current task parent | DBN.C508_WarningPayloadAndDestination: after second A->B edit, queue destination remains A |
| PC-53 | Compute capture digest from body only | DBN.C508_WarningPayloadAndDestination: committed intent digest equals independent route+newline+body digest; same bytes/IDs with A vs B give different factory digests |
| PC-54 | Remove None->NotRequired capture-state arm | DBN.C508_WarningStatesAndLease: None/valid-parent note NotRequired, queue/enqueue attempts=0 |
| PC-55 | Use NotRequired for required/null-parent capture state | DBN.C508_WarningStatesAndLease: DestinationUnavailable and no receipt |
| PC-56 | Include DispatchBase in ReconcileAsync lease gate | DBN.C508_WarningStatesAndLease: DispatchBase gets queue ID under occupied lease while Outcome does not |
| PC-57 | Widen Outcome monitor query to include DispatchBase | LM.C508_RequestlessDispatchNotes: caught SweepAsync exception=null; Outcome still ages |
| PC-58 | Map DispatchBase notes to LandOutcomeUnconfirmed | LM.C508_RequestlessDispatchNotes: kind DispatchWarningUnconfirmed and null LandRequestId |
| PC-59 | Remove SourceLandNotificationId exclusion in HasCompletionNoteAsync | DBN.C508_WarningsAreNotReports: task-key/digest-equal warning alone returns false |
| PC-60 | Omit ONLY hosted intent-scan invocation; retain Capture, MaterializeAsync, notification scan and any fast path | DE.C508_DispatchWarningPrecommitCrashRecovers: at immediate postclaim cut fast path never ran; after restart original note/complete prompt count=1 (mutant 0) |
| PC-61 | Set UnresolvedDefault=null on RepoHead arm only | BS.C508_UnresolvedDefaultRetainsName: two generated invalid input names retained respectively |
| PC-62 | Build capture body from unadorned detail, computing digest consistently | DBN.C508_WarningPayloadAndDestination: committed intent first line equals exact independent dispatch-base header |
| PC-63 | Remove ContentDigest conflict comparison only | DBN.C508_WarningQueueDigestCollision: same key/session with changed digest throws ConflictException |
| PC-64 | Remove ^{commit} suffix from rev-parse probe | BS.C508_ResolverAndCommitProbe: blob tag false before provisioning |
| PC-65 | Gate capture call on guard.Warnings.Count>0 | DG.C508_GuardRefMismatchWarnedOnce: zero-sibling unequal-ref claim has one mismatch intent/pair |
| PC-66 | Omit Capture invocation at successful final claim, retaining claim/session commit | DBN.C508_ClaimCapturesWarningIntents: committed original intent count equals draft count, not zero |
| PC-67 | Commit/dispose claim transaction before final SaveChanges; remove later commit | DBN.C508_ClaimIntentAtomic: after-save injected fault leaves zero final dispatch events/new sessions/intents |
| PC-68 | Use earlier worktree-created Dispatched event ID for intent.DispatchEventId | DBN.C508_ClaimCapturesWarningIntents: each DispatchEventId equals captured final agent-dispatch event ID, not creation event |
| PC-69 | Skip capture when existing intent has same TaskId and diagnostic Attempt | DBN.C508_IntentAttemptIdentity: second successful claim with unchanged Attempt gets distinct intent/note IDs |
| PC-70 | On replay of same committed event/key overwrite saved NotificationId with a new Guid and regenerate its valid header/digest | DBN.C508_IntentAttemptIdentity: reversed draft order/replay retains original per-key intent/notification IDs |
| PC-71 | Pass outer tick task's pre-claim route snapshot to Capture instead of freshly locked claim route | DBN.C508_IntentCaptureRoute: preclaim A->B route edit produces intent.ParentSessionId=B and matching digest |
| PC-72 | Before Capture, refresh sibling draft detail with current DescribeKeptBranchAsync results | DBN.C508_IntentProjectionCustody: ref moved after guard observation still yields original captured branch/tip/detail |
| PC-73 | After payload creation overwrite note.ParentSessionId with current task parent from DB | DBN.C508_WarningPayloadAndDestination: first A->B edit before projection leaves note.ParentSessionId=A |
| PC-74 | Replace note.Body with intent.Detail after validation, retaining saved digest | DBN.C508_IntentProjectionCustody: note.Body byte-equals committed intent.Body |
| PC-75 | Allocate new Warning ID and assign note.SourceEventId to that new ID, preserving valid FK | DBN.C508_IntentProjectionCustody: Warning.Id=original intent.Id |
| PC-76 | Allocate new note ID and consistently update its header/digest, without altering intent | DBN.C508_IntentProjectionCustody: note.Id=original intent.NotificationId |
| PC-77 | Remove FOR UPDATE from intent load only | DBN.C508_IntentProjectionConcurrent: second scope has not completed its intent read while first owns projection lock; both ultimately succeed without duplicate pair |
| PC-78 | In exception recovery clear reloaded MaterializedAt and schedule retry instead of honoring committed marker | DBN.C508_IntentProjectionLostAcknowledgement: original MaterializedAt remains non-null, no retry counters/error, one pair |
| PC-79 | On existing pair ID with null marker set MaterializedAt=now and return instead of recording integrity error | DBN.C508_IntentProjectionIntegrity: collision row remains MaterializedAt=null with error, original unexpected bytes unchanged |
| PC-80 | Remove digest comparison from intent validation | DBN.C508_IntentProjectionIntegrity: independently corrupted digest leaves zero projected rows and null marker |
| PC-81 | Remove header identity validation while retaining digest check | DBN.C508_IntentProjectionIntegrity: wrong header with recomputed valid digest leaves zero projected rows and null marker |
| PC-82 | Remove event.AgentTaskId==intent.TaskId validation | DBN.C508_IntentProjectionIntegrity: valid FK to other task's event still produces zero projected rows and null marker |
| PC-83 | Always schedule materialization retry at 5 seconds | DBN.C508_IntentMaterializationRetry: attempt 2 NextAttemptAt=error time+10s; later delays saturate at 300s |
| PC-84 | When recording retry error fails, fresh-update MaterializedAt=now then swallow | DBN.C508_IntentRetryBookkeepingFailure: original pending marker null after both faults; clean worker later creates original pair |
| PC-85 | Restrict intent scan to task.Status==Dispatched | DBN.C508_IntentScanPaging: due intents on Succeeded/Failed/Canceled/Queued tasks all materialize |
| PC-86 | Require intent.Attempt==current task.Attempt in scan | DBN.C508_IntentScanPaging: due original intent on incremented-attempt task materializes |
| PC-87 | End intent pass after first 128-row page | DBN.C508_IntentScanPaging: all expected IDs including page three have original notes |
| PC-88 | Keep previous pass cursor across five-second cycles instead of resetting | DBN.C508_IntentScanPaging: low-ID formerly-future intent materializes after becoming due |
| PC-89 | Return from intent pass on first row exception instead of continuing | DBN.C508_IntentScanIsolation: healthy next row/page materializes despite first-row fault |
| PC-90 | Continue outer hosted loop on intent-query failure before running notification pass | DBN.C508_IntentScanIsolation: preexisting unrelated due note gets keyed queue row while repeated intent-pass query faults remain armed |
| PC-91 | Remove pending-required-intent exclusion from PruneSessionsAsync | DR.C508_IntentSessionRetention: old otherwise-unreferenced A survives; unrelated stale session deletes |
| PC-92 | Remove pending-required-intent exclusion from PruneTranscriptsAsync | DR.C508_IntentTranscriptRetention: all old A transcript IDs survive; unrelated transcript deletes |
| PC-93 | Remove intent-owning-tree exclusion from PruneTasksAsync, retaining note/request exclusions | DR.C508_IntentTaskTreeRetention: caught sweep exception=null and unrelated tree deleted; intent-owning entire tree survives |
| PC-94 | Remove notification-owning-tree exclusion, retaining intent/request exclusions | DR.C508_NotificationTaskTreeRetention: note-only/no-intent/no-request tree survives, unrelated tree deleted, caught exception=null |
| PC-95 | Omit pending-intent projection from AttentionService | LM.C508_PendingIntentAttention: at 300s original pending required intent appears once |
| PC-96 | Use intent.Id instead of NotificationId in pending condition key | LM.C508_IntentAttentionHandoff: key before/after is dispatch:<original NotificationId:N>:receipt |
| PC-97 | Remove final merged ConditionKey deduplication | LM.C508_IntentAttentionHandoff: commit between intent read and note read yields exactly one condition |
| PC-98 | Suppress pending intent whenever ANY note for TaskId exists instead of exact NotificationId | LM.C508_IntentAttentionHandoff: another warning's note does not hide this pending original intent; original key still present |
| PC-99 | Remove ReplyTo!=None filter on pending intent attention | LM.C508_PendingIntentAttention: old None intent has zero receipt conditions |
| PC-100 | Apply age-only early continue to pending intents even with LastErrorCode | LM.C508_PendingIntentAttention: 1-second-old error intent is visible as Error |
| PC-101 | Always choose Warning severity for pending intent | LM.C508_PendingIntentAttention: 900-second required intent severity=Error |
| PC-102 | Assign LandNotificationId=intent.NotificationId before that row exists | LM.C508_PendingIntentAttention: pending item LandNotificationId=null, task target present |
| PC-103 | On failed probe pass DefaultBranchProbe("master",false) from provisioning instead of original probe | BS.C508_ProvisioningPreservesFailedDefault: two arbitrary failed configured names survive returned metadata |
| PC-104 | Choose master in guard's default probe instead of the configured failing candidate | DG.C508_GuardPreservesFailedDefault: with valid M distinct from H, both arbitrary missing-default rows observe HEAD, not master; resulting default warning still names original ref |
| PC-105 | On Resolve's Explicit arm return failed defaultBranch.Ref in UnresolvedDefault instead of null | DG.C508_MissingDefaultWarns: requested-base row has zero default intents/pairs despite invalid configured default |
| PC-106 | In the existing failed-progress-baseline branch add a fresh task-bound Dispatched event and Capture its frozen drafts before the existing Failed commit | DBN.C508_RefusedClaimHasNoIntent: failed-baseline row has zero intents; its committed worktree-created event does not authorize any |
| PC-107 | After successful dispatch enqueue each frozen intent.Detail directly to saved parent using old task conversation key, while retaining durable delivery | DE.C508_WarningProducerToReceipt: native warning-detail census contains only keyed expected prompts, no unkeyed duplicate |
| PC-108 | In CLI-generated S2b migration Up change unique:true to false ONLY on (DispatchEventId,WarningKey) index | DBN.C508_IntentUniqueKeys: predecessor-to-current upgrade then duplicate event/key with distinct IDs must throw 23505 for named index |
| PC-109 | In CLI-generated S2b migration Up change unique:true to false ONLY on NotificationId index | DBN.C508_IntentUniqueKeys: predecessor-to-current upgrade then duplicate NotificationId with different event/key must throw 23505 for named index |
| PC-110 | Remove Capture's event/task binding check | DBN.C508_IntentCaptureBinding: cross-task final event capture is refused and no intent is tracked/committed |
| PC-111 | Treat whitespace-only Project.BaseBranch as the selected candidate | BS.C508_DefaultCandidateSelection: whitespace project with valid global selects T/DefaultBranch |
| PC-112 | Catch OperationCanceledException in shared default probe and return named false probe | BS.C508_DefaultProbeCancellation: canceled call throws cancellation, no RepoHead result |
| PC-113 | Remove NextAttemptAt<=now predicate from intent scan only | DBN.C508_IntentMaterializationRetry: worker has zero additional materialization attempts before due, then projects at exact due time |
| PC-114 | Set note.CreatedAt and Warning.At to current materialization time | DBN.C508_IntentProjectionCustody: both equal original committed intent.CreatedAt after clock advances |
| PC-115 | Set Warning.Detail to current generic task title instead of intent.Detail | DBN.C508_IntentProjectionCustody: Warning.Detail equals original frozen detail |
| PC-116 | Retain the guard's old early return before resolving its observed base when siblings.Count==0 | DG.C508_GuardRefMismatchWarnedOnce: zero-sibling M->P claim emits one mismatch explicitly naming observed M and recorded P |


PC-60's mutant retains all post-note machinery; its test inspects committed
intents before restart and asserts missing original note/complete-prompt
counts after bounded observation of normal notification scans. It must not
wait for dispatch-warning-intent-scan in a mutant with that invocation removed.
PC-42 starts from an already committed pair and observes scan completion,
not before-enqueue that its mutant excludes. PC-51 after-save fault is inside
the projection transaction; PC-67 after-save fault is inside the claim
transaction. Neither can be replaced by a before-save-only assertion.

PC-52/53 assertions begin at committed intent, continue through projection
and queue, then verify original route/body. PC-71/73 independently damage the
earlier route handoffs; PC-74/75/76/114/115 independently damage copied payload/
identity/time. PC-108/109 mutate the generated migration's index creation,
then the test upgrades its disposable predecessor database. Merely changing
model metadata against a migrated template is a surviving, invalid control.

PC-77 observes lock exclusion before allowing the first projection to commit;
its finally releases both owned operations and awaits them. PC-79/80/81/82
use valid seeded foreign keys and one deliberately corrupted semantic field
at a time. A digest-valid bad header keeps PC-81 independent of PC-80.
Expected integrity failures are asserted as method results/state, never
accepted as setup failure. PC-93/94 catch the actual sweep exception and
assert null plus successful unrelated pruning; a Restrict failure cannot
be reported as green retention.

Code implements tests and runs ordinary V/R. Separate ordinary Review judges
the tests, evidence and pending control inventory before land. **Mutation
runs every break/red/restore/green cycle after land**, with fresh per-PC
TRX/results and external evidence in the recorded SourceLanding snapshot.
No tests or positive controls were executed by this TestDesign task.

### Out of scope

- S3 selection, ranking, chaining, moving the guard under the lease and the
  CARD-0215 test inversions; S4 public BaseRef wiring; S5 dependent output.
  Owner docs describe only the commissioned S1/S2 behavior.
- Same-ref SHA movement and held-before-claim races: B reports ref mismatch
  only; it does not make the original observation contemporaneous with launch.
- Historical intent backfill, intent-history purge, kept-branch mutation and
  changed landing targets: explicitly excluded by B. History preservation
  and ordinary unrelated pruning are nevertheless tested.
- Native receipt for None/missing parent is impossible by contract. V-18/
  V-28/V-29 verify NotRequired versus unresolved custody; those rows never
  substitute for required Session-route receipt.
- No new guard-only unresolved-name output is invented: the guard's observable
  contract is its actual comparison ref, hold/warning text and captured drafts.
  V-30 makes wrong chosen-candidate probing visible with M != H; provisioning
  metadata and actual warning/native body prove preservation of the name.
- Browser visual E2E and hosted-model canaries are unnecessary for the single
  attention enum/label mapping and unchanged native framing. HTTP, type build,
  focused Vitest and the owned native FakeGrok lane cover the changed behavior.
- Exhaustive busy x fault x warning-kind combinations are excluded for the
  shared-path reason given in Delivery inventory; every producer, handoff,
  failure class and both queue eligibility states remain covered.

### Cost

**All figures are estimates**, not measured product evidence. This is a
complete execution floor for the specified V/R and **all 116 PC cycles**,
not the earlier 413-minute lower bound. Authoring, ordinary Review analysis,
failure triage and report writing are additional; zero time is assigned to
none of the required verification work. Native estimates include startup,
restart, transcript observation and the two extra completed scans. A slow
actual full suite is not subject to an artificial 120-minute kill deadline.

| Ordinary floor (Code) | Suites / filters / arithmetic | Minutes |
|---|---|---:|
| Setup/build | Two CLI migrations, SDK/Docker/native prerequisites, isolated Tests/E2E builds and client build | 20 |
| Base + existing focused V/R | DW/BS/DG/LS, PostLandMutationWorktreeTests, R-5/R-6/R-7/R-9 and helper checks | 38 |
| Added custody/recovery/retention components | DBN, LM, DR; V-23..V-31 including 263-row worker, races, route edits, retry and false receipts | 20 |
| Upgrade | V-10/V-20, both predecessor upgrades and identity constraint assertions | 8 |
| HTTP contract | CS capture then independent comparison with non-null Worktree assertions | 6 |
| Changed land native | V-11 four producer rows + V-12 nine cuts = 13 x 3 | 39 |
| Dispatch native producers | Two producer/two-claim methods x 4 + boot x 3 + two mismatch methods x 3 | 17 |
| Dispatch native post-note recovery | Eight exact cut methods x 3 | 24 |
| Dispatch native claim/projection recovery | Rollback 3 + postclaim eligible/busy 6 + successful-return 3 + projection precommit 3 + before/after-save 6 + launch exception 3 | 24 |
| Dispatch frozen/default native coverage | Four terminal/requeue-route rows x 3 + two names x two card/eligibility arrangements x 3 | 24 |
| Client | pwsh -File scripts/test-client.ps1 attentionVisuals.test.ts | 1 |
| Unit | /*/*/*/*[Category=Unit] | 3 |
| One full Antiphon.Tests pass | Nine disjoint namespace chunks below; required by commissioning standing instruction, then only target failures | 120 |
| **Ordinary total** | **setup/build 20 + V/R 324** | **344** |

| PC floor (Mutation) | All cycles, exact-method scoped | Minutes |
|---|---|---:|
| Fresh SourceLanding setup/build | One owned snapshot, discovery and external evidence setup | 10 |
| Component/Git controls | 103 PCs x (0.5 apply/build + 0.75 red + 0.5 restore/build + 0.75 green) | 257.5 |
| Migration identity controls | PC-108/109: 2 x (0.5 apply/build + 2 red upgrade/method + 0.5 restore/build + 2 green upgrade/method) | 10 |
| Single-cut native controls | PC-26/27/28/35/36/37/42/50/60: 9 x (0.5 apply/build + 3 red + 0.5 restore/build + 3 green) | 63 |
| Two-claim native producer controls | PC-41/107: 2 x (0.5 apply/build + 4 red + 0.5 restore/build + 4 green) | 18 |
| **Mutation total** | **setup/build 10 + all 116 cycles 348.5** | **358.5** |

**Total verification floor = setup/build 30 + ordinary V/R 324 +
PC red/restore/green work 348.5 = 702.5 minutes (11h 42m 30s), estimated.**
The 103 component/Git PCs are every ID in 1..116 except
26,27,28,35,36,37,41,42,50,60,107,108,109.
PC-23 has one variant; pinned SHA is the separately counted PC-43.
No failed-seam reservation or missing cycle remains.

Estimated savings with the same coverage: splitting V-12 into exact cuts
lets PC-26/27/28/36/37 run one three-minute cut instead of the nine-cut
27-minute method: 5 x 2 x (27 - 3) = **240 minutes**. Exact-method component
selection versus estimated four-minute class runs saves
103 x 2 x (4 - 0.75) = **669.5 minutes**. Different sets of controls, so combined
estimated saving is **909.5 minutes**. Additional batching/sharding savings
are **0 claimed**: many defects share source files, and sourced Mutation
owns one recorded snapshot. Savings from dropping required cases are zero.

Use producer-owned forward-slash outputs. Example ordinary selections
(after implementation, one fresh results directory/TRX per invocation):

~~~powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c508/ --nologo
dotnet build tests/Antiphon.E2E --property:OutputPath=bin-c508/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c508/ -- --treenode-filter '/*/Antiphon.Tests.Application/(WorktreeBaseSelectionTests*)|(DelegationWorktreeTests*)|(AgentTaskDispatchBaseGuardTests*)|(AgentTaskLandStageOutcomeTests*)|(DispatchBaseNotificationTests*)|(AgentTaskLandMonitoringTests*)|(DataRetentionServiceTests*)/*' --report-trx --report-trx-filename focused.trx --results-directory .antiphon/c508-b-focused
dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-c508/ -- --treenode-filter '/*/*/DispatchBaseWarningDeliveryE2ETests/C508_*' --report-trx --report-trx-filename warnings.trx --results-directory .antiphon/c508-b-warnings
dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-c508/ -- --treenode-filter '/*/*/AgentTaskLandDeliveryE2ETests/C508_*' --report-trx --report-trx-filename land.trx --results-directory .antiphon/c508-b-land
pwsh -File scripts/test-client.ps1 attentionVisuals.test.ts
~~~

The unaffected land/caller regressions named in R-6/R-7/R-9 run their exact
methods in Antiphon.Tests. CS runs its named method separately twice; a
capture run alone is not a comparison.

The required full Tests pass uses these nine exact, disjoint namespace
filters (inventoried at d5eaa24a):

~~~text
/*/Antiphon.Tests/*/*
/*/Antiphon.Tests.Agents/*/*
/*/Antiphon.Tests.AgentTui/*/*
/*/Antiphon.Tests.ApiKeys/*/*
/*/Antiphon.Tests.Application/*/*
/*/Antiphon.Tests.Domain.StateMachine/*/*
/*/Antiphon.Tests.Infrastructure/*/*
/*/Antiphon.Tests.Scripts/*/*
/*/Antiphon.Tests.TestHelpers/*/*
~~~

Verify executed counts in fresh TRX against the current discovered inventory;
split the large Application chunk into disjoint class selections if a
foreground window requires it. Do not repeat the broad pass for each fix or PC.

Concrete PC-60/61 commands (red and restored-green use separate fresh result
directories and freshly built outputs, never --no-build after a mutation):

~~~powershell
dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-c508-pc/ -- --treenode-filter '/*/*/DispatchBaseWarningDeliveryE2ETests/C508_DispatchWarningPrecommitCrashRecovers' --report-trx --report-trx-filename pc60-red.trx --results-directory .antiphon/c508-pc60-red
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c508-pc/ -- --treenode-filter '/*/*/WorktreeBaseSelectionTests/C508_UnresolvedDefaultRetainsName' --report-trx --report-trx-filename pc61-red.trx --results-directory .antiphon/c508-pc61-red
~~~

All runs are foreground and awaited. Commit/push Code before long runs; freeze
source under a running test. Antiphon.Tests and native/Pty assemblies run
sequentially. Restore PC source/timestamps and verify rebuilt DLL identity;
SourceLanding Mutation writes only external evidence, never commits/pushes.
Inventory output directories before creating them, validate resolved paths
stay within the owned workspace, then remove only those producer-created
alternate outputs. On failure, target the same methods on the base commit
before claiming pre-existing red; never weaken assertions/retry budgets.

**Readiness audit:** touched and nearest bodies read as recorded;
guards=116, mapped=116, missing=0, duplicate PC mappings=0;
all 116 PCs have a compiling defect and exact decisive assertion.
Ordinary floor=344 minutes, PC floor=358.5 minutes, total=702.5 minutes,
all estimated. P-3/P-4 executability gaps are closed; implementation/tests
remain Code's work. **Next: code**, followed by ordinary Review, land,
and all pending SourceLanding Mutation cycles.
