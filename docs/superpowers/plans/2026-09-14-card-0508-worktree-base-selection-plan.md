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

Plan return items (no human scope decision is pending):

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
