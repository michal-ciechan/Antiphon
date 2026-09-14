# CARD-0508: Choose a Worktree base deliberately, and say which one

Date: 2026-09-14. Stage: Plan; verification design is a separate TestDesign stage.
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
| `WorktreeBaseRef` (`string?`, 300) | The ref the worktree branch was cut from. Resolved at provisioning, then immutable. |
| `WorktreeBaseSource` (`WorktreeBaseSource`) | Why: `Unset` (legacy rows), `Explicit`, `MergeTarget`, `CardCurrent`, `DefaultBranch`, `RepoHead`. |
| `WorktreeBaseTaskId` (`Guid?`) | The sibling task whose branch was chosen, when `CardCurrent`. |

`MergeTargetRef` is untouched and remains *only* the landing target. `WorktreeBaseSha`
keeps its current meaning (the created worktree's HEAD SHA) and is now the pinned SHA of
the chosen base.

Migration by CLI only, per [docs/project-context.md:125](../../project-context.md):
`dotnet ef migrations add AddWorktreeBaseSelection --project server`.

### D-2. One precedence order, resolved once

Provisioning takes `WorktreeBaseRef ?? MergeTargetRef ?? <default branch> ?? "HEAD"`, and
the dispatcher fills `WorktreeBaseRef` beforehand. In precedence order:

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

## Implementation slices

| Slice | Content | Files |
|---|---|---|
| S1 | Columns + migration + the D-2 fallback chain in provisioning (`WorktreeBaseRef ?? MergeTargetRef ?? default branch ?? HEAD`, `IOptions<GitSettings>` injected optionally so direct constructions keep working), base recorded on the row, base named in the `Dispatched` event, detail DTO fields, contract fixture regenerated. Fixes defect 2 on its own. | `AgentTask.cs`, `server/Migrations/*`, `AppDbContext.cs`, `DelegationWorktreeService.cs`, `AgentTaskDispatcher.cs` (event text), `AgentTaskDtos.cs`, `AgentTaskService.cs` (`ToDetail`), `client/src/test/fixtures/contract/agent-task-detail.json` |
| S2 | Patch-aware containment helper on `DelegationWorktreeService` (`git cherry`), replacing `IsAncestorOfBaseAsync` at both call sites. Fixes defect 3 on its own. | `DelegationWorktreeService.cs`, `AgentTaskDispatcher.cs`, `AgentTaskLandService.cs` |
| S3 | `WorktreeBaseSelector`: candidate query, reduce, rank, hold; wired into `DispatchOneAsync` under the lease; pre-lease guard call deleted; warnings moved into the claim transaction; `WorktreeBaseTaskId` recorded. Fixes defect 1. | `AgentTaskDispatcher.cs`, new `server/Application/Services/WorktreeBaseSelector.cs` |
| S4 | `-BaseRef` end to end, with the SourceLanding refusal. | `scripts/delegate.ps1`, `AgentTaskDtos.cs`, `AgentTaskService.cs` |
| S5 | Header and warning wording: `(from <base>)` on left-for-review; base-behind-default warning (D-8). | `AgentTaskReplyService.cs`, `AgentTaskDispatcher.cs` |
| S6 | Docs (D-9). | `docs/orchestration-loop.md`, `docs/antiphon-api.md` |

S1 and S2 are independently shippable and independently valuable. S3 depends on both.
S4/S5 depend on S3; S6 follows.

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
