# CARD-0442: continue a card's unlanded work across Worktree tasks

Date: 2026-09-08. Stage: Plan. Recommended complexity: medium; TestDesign is a separate stage.
Source: full live CARD-0442, id `d181aef8-47ff-41bc-882b-1707d9ca9fe7`.
Code inspected at `ccb48b2ca88f96e8845ac2d6e818ec2cb6a8a4b3`.

## Outcome and scope

Make a new card-bound Worktree task start at the committed tip of the card's unambiguous,
settled, unlanded predecessor. Give it its own branch and directory, and retain its existing
merge destination. Return the proposed source in the create response so `delegate.ps1` can
explain it immediately. Re-evaluate at actual launch, before a session exists.

The normal Code -> Review -> Code cycle should need neither an intervening land nor a
`-Refine` telling each new agent which branch contains the work. This deliberately replaces
CARD-0215's blanket prohibition on sibling bases with a same-card, same-repository exception.
Unrelated cards remain isolated. This plan changes no production behavior itself.

## Ground truth

| Card assumption / concern | What the code actually does | Consequence |
|---|---|---|
| `delegate.ps1 -Worktree` chooses `origin/master`. | `scripts/delegate.ps1:556` sends only `workspace=Worktree`. `POST /api/agent-tasks` calls `AgentTaskService.CreateAsync` and returns a queued row; creation of the Git worktree happens later. | The authoritative fix belongs on the server; the script is the immediate feedback surface. |
| Every new task begins at master. | `DelegationWorktreeService.CreateForTaskAsync:348` passes `task.MergeTargetRef ?? "HEAD"` to `IWorktreeManager.CreateAsync`. No dispatch-time fetch occurs. `HEAD` belongs to the resolved `RepoPath`, not necessarily master or the latest remote commit. | Preserve this fallback in this card; do not bundle a default-branch/fetch-policy rewrite. |
| The API already distinguishes the start ref from the destination. | `AgentTaskService:881` resolves `MergeTargetRef` from the request, otherwise a same-repository parent's branch/target. Worktree creation uses that same field. Explicit land uses `MergeTargetRef ?? "master"`. | Never implement continuation by setting `MergeTargetRef` to the predecessor branch. |
| No earlier warning exists. | `AgentTaskDispatcher.EvaluateCardSiblingBaseAsync:2847` finds same-card Succeeded/Blocked Worktree siblings, checks local branch existence and ancestry, and holds when `LandRequestedAt` is set. Otherwise `WarnUnlandedSiblingsAsync:2910` runs only AFTER `DispatchOneAsync` succeeds, writing a Warning and a parent-session WhenIdle note. | A capability caller without session reply routing misses the note; the create response currently has no branch preview. Preserve the useful land hold, move visibility earlier. |
| Branching afresh is accidental. | `DelegationWorktreeTests.two_top_level_worktree_tasks_on_one_card_both_branch_from_repo_head` explicitly asserts the prior commit is absent. `docs/orchestration-loop.md:129` states the prohibition. | Update the old contract and the relevant regression assertions deliberately. |
| `-OnAgent` supplies the desired new worktree. | A live follow-up in `AgentTaskService:268` changes the request to Shared and pins the prior agent; a retired agent instead contributes inherited context. | Session reuse and Git continuation are independent features. Do not route this fix through `-OnAgent`. |
| Starting from a prior branch requires checking it out or fetching it into the main checkout. | Linked worktrees share Git refs/objects. `WorktreeManager.CreateAsync:39` already supports `git worktree add -b <new-branch> <new-dir> <baseRef>`. | Resolve a local source to a full SHA and create a NEW task branch there. Never checkout/reset the predecessor or main checkout. |
| Repository path containment proves shared Git history. | `DelegationWorktreeService.SharesRepo:233` uses equal-or-contained paths. Linked sibling directories can be disjoint; nested repos can satisfy containment while having separate refs. `GitWorkspaceService.GetWorkspaceInfoAsync:597` already demonstrates `--git-common-dir` resolution. | For the new selector, compare canonical absolute Git common directories. Do not broaden the existing global path helper as a side effect. |
| Another field is needed for the actual starting SHA. | `AgentTask.WorktreeBaseSha` already records worktree creation HEAD and feeds no-target completion/check Git facts. | Reuse it, preserve it on adoption, and add only source/mode provenance. |

## Decisions

These are implementation defaults, not unanswered operator questions.

### D-1. Separate source selection from landing

Introduce a concrete `AgentTaskWorktreeBaseResolver` application service, used by both create
and dispatch. It reads card-bound task rows through `AppDbContext` and consumes strict Git
observations through the existing Git infrastructure seam. Keep process execution in
Infrastructure for new operations; do not add another direct `Process.Start` in Application.
An interface is appropriate only for the external Git I/O seam, not for the resolver itself.

The resolver returns a typed decision: default target, continue source, wait for land, or
needs explicit selection, with candidate task ids/branches/full SHAs and reasons. All source
refs are resolved to immutable commit SHAs before being passed to worktree creation.
`MergeTargetRef`, `ParentTaskId`, `RootTaskId`, card binding, and session ownership remain
unchanged. An inherited parent destination is still the destination.

### D-2. Expose two narrow overrides

Add `-BaseTask <short-id|guid>` and `-FreshWorktree` to `delegate.ps1` create. They are mutually
exclusive and require the resolved workspace to be Worktree. The API accepts
`worktreeBaseTask` and `freshWorktree` (omitted means automatic). Resolve the task identifier
server-side, after normal caller authorization and card binding. These switches do not select
an agent, reuse a process, bypass a land hold, or grant access to another repository/card.

`-BaseTask` explicitly chooses one same-card/same-Git-repository source when histories diverge.
It must have the same effective landing destination (null means the existing explicit-land
default `master` for this comparison). A mismatch is 422, not implicit rebinding.
`-FreshWorktree` chooses the pre-change `MergeTargetRef ?? HEAD` behavior, with an immediate
warning listing any omitted same-card work. Neither accepts an arbitrary ref/path.
Reject combination with `-OnAgent` so its Shared rewrite cannot silently discard the request.

Persist requested mode (`Auto=0`, `Target`, `Task`) and `RequestedWorktreeBaseTaskId` on
`AgentTask`; retain the explicit intent over queueing/restarts/retries. Record resolved
`WorktreeBaseTaskId` and `WorktreeBaseBranch` when a worktree is actually created, alongside
the existing `WorktreeBaseSha`. Add these fields to task detail, with null source for a target
base. New enum values/defaults and nullable columns need one CLI-generated EF migration.
Persist the typed create-time preview as `WorktreeBasePreviewJson`, including its observation
time, so launch can compare with what the caller actually saw after a server restart. Bound
its size to the card's candidate summaries; do not store Git command output. This snapshot is
advisory evidence, never a substitute for launch-time resolution. Do not guess source
provenance for historical rows.

### D-3. Automatic selection uses Git containment, not creation time

Inventory existing Worktree task branches for the resolved card GUID, excluding the new task.
Validate repository identity using canonical absolute Git common directories, with the OS
path comparison rules. A missing original worktree can still have an available local branch
in the same common repository. Do not scan every remote `feat/*` branch or infer a card from
a branch's short id. Do not mix histories from clones merely because their origin URLs match.

For automatic selection, require a Succeeded task, a matching effective landing destination,
a resolvable local commit, and no open task currently using that source worktree (including
a Shared `-OnAgent` follow-up). If the branch is checked out, inspect that checkout: tracked,
staged, untracked changes or an in-progress rebase/merge make it unsuitable for automatic
continuation. An unregistered but resolvable branch has no working files to copy.

Determine whether source work is absent from the current default base. Exclude ancestors of
that base. For linear ranges, also exclude branches whose entire range is patch-equivalent
(`git cherry` has no `+` entries), so a rebased-and-landed branch is not resurrected. Do not
claim patch equivalence for merge-containing ranges: treat those as uncertain and report
them for explicit selection. Git errors are distinct from "no unlanded work".

Collapse equal tips, then eliminate tips that are ancestors of another eligible tip. If one
tip contains every eligible unlanded tip, use it. Several task rows at the same SHA are one
source snapshot; prefer the most recently completed row only to label identical content,
with task id as a deterministic tie-break. A newer read-only Review at A must not override
a Code branch at B when A is an ancestor of B.

If two distinct maximal tips remain, the source is ambiguous: return a conflict before task
creation, naming both and suggesting `-BaseTask` or `-FreshWorktree`. Do not merge them or pick
the newest task. This prevents an apparently successful launch that quietly omits a fix.

Other same-card kept branches (active, Blocked, Failed, Canceled, dirty, missing/unverifiable,
or with a different destination) are visible warnings, not automatic sources. With no eligible
source, retain the normal target base and explicitly name these omissions in the create
response. This is the card's permitted warning fallback for cases that cannot safely be
inferred. With an eligible source, still name any excluded branch it does not contain.

An explicit `-BaseTask` may select a quiescent Blocked/Failed/Canceled task's committed work,
but cannot bypass an active writer, dirty/rebasing checkout, missing commit, repository/card
boundary, or pending land. It deliberately accepts a chosen history even if already contained
in the target; do not replace an explicit source with the target silently.

### D-4. Keep the dispatch-time land guard and handle races explicitly

Read the durable `LandRequestedAt` column, not historic LandRequested events. Preserve the
existing hold for relevant same-card kept work that is not yet in the target. A task waiting
for land remains Queued, creates no worktree/session, and retains the existing
`siblingLandInFlight` pipeline explanation. A completed land is re-evaluated against the
current target; a lingering branch name alone is not proof of unlanded work.
The land hold takes precedence over ambiguous-source refusal while the histories are being
integrated. Apply the same ordering in preview and dispatch. Only Auto without a card falls
straight through to legacy behavior; explicit Task mode requires a bound card.

Create-time selection is a preview, not a promise about Git state minutes later. Recompute
after the dispatcher has claimed the task and passed launch/provider guards, immediately
before worktree creation. Use the same selection logic as the preview. A newly completed
descendant may be chosen; a branch landed meanwhile may reduce Auto to the target. Record
the actual choice and, when changed, why it differs from the preview.

If explicit input becomes invalid or Auto becomes ambiguous after queueing, use the existing
durable Blocked/error-reporting path before any session/worktree is created. The message
names the branches and the available actions: resolve the histories then retry, or cancel
and recreate with `-BaseTask` / `-FreshWorktree`. Do not reinterpret an ordinary question reply
as a Git ref or silently revert explicit Task mode to the target.

Resolve and validate the full source SHA, then use that SHA for `worktree add`; subsequent ref
movement cannot redirect the checkout to a different commit. Snapshot semantics cover a
source moving after observation: no source branch is modified, and provenance states the
exact commit inherited. An inability to create at that SHA is an ordinary failed creation,
never an automatic retry from master. No global Git lock or source-worktree ownership transfer
is needed to copy an immutable commit. Existing same-task adoption takes precedence over
new source selection, including crash windows where branch/directory exist before coordinates
were saved. Adoption must preserve prior commits and an already-recorded `WorktreeBaseSha`.

### D-5. Proactive feedback is part of the feature

Append an optional structured `worktreeBase` preview to `AgentTaskCreatedDto`, containing
decision, fallback ref, source task/branch/full SHA when known, and candidate warnings. Compute
it after directory authorization/card binding but before saving a runnable row. Preserve all
existing routing/quota/scope warnings; do not overload or replace their singular `Warning`.
An inspection failure produces an explicit unknown/fallback warning, not "no prior branch".

The real script prints, for example:

```text
base preview: CARD-0442 continues task abcdef12 on feat/card-task-abcdef12 @ <sha>; new isolated branch; landing target master (explicit land)
base preview: waiting for task abcdef12 land; source will be rechecked before launch
WARNING: base preview is HEAD; CARD-0442 task abcdef12 has dirty/active work that will not be inherited
```

The actual worktree-created event includes source task, branch, full SHA, and destination;
detail/status exposes persisted provenance. Include one short source line in the delegated
brief so the new worker can verify HEAD without discovering missing context itself. An
actual decision that differs from the preview also emits the existing Warning/event and
parent WhenIdle notification when routing exists. Capability callers can always read task
detail. No additional session message is required to make the create response useful.

### D-6. Limit Git and lifecycle changes

Use local shared refs/objects for this increment. A source with only a remote branch and no
local verifiable head is a proactive fallback warning (or explicit-source refusal), not an
invitation to fetch from arbitrary URLs. Automated cross-clone/remote-only recovery is out of
scope. This avoids making create-time availability depend on network credentials and avoids
overwriting a source's unpushed local commits. The ordinary local multi-round cycle needs no
fetch because every task's branch is in the same Git repository.

Starting from predecessor A does not mean landing A. Review can make zero commits at A, and
the following Code task can start there and produce B. Explicitly landing B uses the existing
rebase/verify/fast-forward/push machinery and includes the inherited work. Do not delete or
mark the older tasks landed merely because B contains them; existing containment-aware cleanup
and land residue handling remain responsible for their branches. Do not weaken conflict,
push-failure, or verification-refusal behavior.

Keep `WorktreeBaseSha` as the actual creation snapshot for no-target Git facts. The existing
explicit-target Git-facts behavior is target-relative; do not rename it as task-only change
count or change it as part of this card. Source provenance makes the inherited range visible.

## Rejected alternatives

| Alternative | Reason rejected |
|---|---|
| Change only `delegate.ps1` to discover/check out branches. | Misses UI/API/queued dispatch and cannot authoritatively resolve card/repository identity. |
| Set `MergeTargetRef` to the prior task branch. | Changes where work lands and can integrate into the wrong task. |
| Reuse the previous directory or `-OnAgent`. | Couples Git continuity to process context and sacrifices separate task workspaces. |
| Pick the last-created or highest-stage task. | Re-reviews can contain no new commits; divergent fixes cannot be ordered by timestamps. |
| Warn only after launch, as today. | Does not satisfy the reported failure; a session-less caller receives no WhenIdle note. |
| Automatically merge all same-card branches or land the predecessor. | Resolves an orchestration/review decision without authorization and introduces merge conflicts into dispatch. |
| Only ship create-time warnings forever. | Valid minimum fallback, but leaves the common linear multi-round cycle unnecessarily manual. |

## Implementation slices and test ownership

TestDesign should turn these obligations into numbered verification and positive-control cases.
The separate stage is intentional; this document does not claim executable verification is done.

| Slice | Files / change | Required evidence |
|---|---|---|
| S1: selection and strict Git observations | New `server/Application/Services/AgentTaskWorktreeBaseResolver.cs`; extend `server/Application/Interfaces/IGitService.cs` and `server/Infrastructure/Git/GitService.cs` with narrowly typed identity/ref/ancestry/clean-state observations; register in `server/Program.cs` and `tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs`. Reuse existing worktree/Git infrastructure instead of duplicating process wrappers. | New `AgentTaskWorktreeBaseResolverTests`: same-card GUID and canonical common-dir isolation, nested repo negative, unique linear chain, equal tips, divergence, patch-equivalent linear history, missing/error/dirty/active sources, and explicit destination mismatch. Use scratch real Git for Git-dependent results. |
| S2: request, persistence, immediate output | `server/Domain/Entities/AgentTask.cs`, `server/Domain/Enums/AgentTaskEnums.cs`, `server/Infrastructure/Data/AppDbContext.cs`, CLI-generated `server/Migrations/*`; `server/Application/Dtos/AgentTaskDtos.cs`, `AgentTaskService.cs`, `scripts/delegate.ps1`, `client/src/api/agentTasks.ts`. Endpoint remains thin. | New `AgentTaskWorktreeBaseCreateTests` and `DelegateScriptWorktreeBaseTests` using the real script/stub API pattern in `DelegateScriptKindTests` / `DelegateScriptRunner`: omitted/default modes, overrides, invalid combination, unauthorized/cross-card sources, warning composition, preview available before launch, migration round trip/defaults. Preserve capability and kind behavior. |
| S3: dispatch and recovery | `AgentTaskDispatcher.cs`, `DelegationWorktreeService.cs`, `DelegationReportFormatter.BuildBrief`, DTO projection and retry handling in `AgentTaskService.cs`. `AgentTaskPipelineStatusService.cs` only if needed to preserve accurate existing land holds. | Extend `AgentTaskDispatchBaseGuardTests` for automatic continuation, source changes between create/dispatch, land start/completion races, ambiguous-before-launch blocking, and non-session callers. Extend `DelegationWorktreeTests` for explicit SHA starts, merge target unchanged, distinct directories/branches, retry/adoption without base reset, and creation failure preserving source work. Update the old "both branch from HEAD" test to describe explicit Target/low-level fallback, and add a real server-selection regression for default Auto. |
| S4: full multi-round behavior and owner docs | `docs/orchestration-loop.md` replaces the blanket sibling ban and mandatory Plan land prerequisite; `docs/antiphon-api.md`, `docs/ops-http.md`, `.claude/skills/antiphon-delegate/SKILL.md` document preview, overrides, and recovery. Amend only relevant bundle wording after reading its owner. | Scratch-repo Code A -> no-change Review A -> Code B -> explicit land B; verify HEAD/file contents at every start, target unchanged until land, combined content lands, and old branches are not falsely reported as omitted. Retain `AgentTaskLandStageOutcomeTests`, `AgentTaskPipelineStatusTests`, `DelegationWorktreeTests`, and existing follow-up/capability regressions. |

Update copied/manual test registrations through `DelegationTestServices` as the testing owner
requires; a new resolver dependency must not break unrelated dispatcher/AgentTaskService
harnesses. Locate constructors/call sites before choosing injection changes. Client presentation
work beyond compatible DTO fields and existing task-detail provenance is unnecessary.

## TestDesign handoff and acceptance

The decisive positive control is a committed marker present only on an unlanded Code branch:
under the old implementation the next default Worktree task lacks the marker; with Auto it
starts at the predecessor SHA in a different directory/branch. Repeat across a zero-change
Review and a third task with a second marker. Both inherited commits must reach the unchanged
landing target when the final Code task is explicitly landed.

Also require a proactive-script test that cannot pass by inspecting a later Warning event:
assert source/branch/SHA or a precise fallback reason in the initial create response and actual
`delegate.ps1` output, with `noReplyRouting=true`. Preserve authentication/concurrency/routing
refusals before launches. Do not dispatch a paid/live agent for these tests.

Use real scratch Git repositories (and a scratch bare remote for landing), the existing isolated
Postgres schema helper, and fake runner/queue seams. Keep production runner port 17204 out of
tests. TestDesign owns exact filters, positive-control mutations, and counts. Follow
`docs/testing-and-build.md`: TUnit via `dotnet run --project tests/Antiphon.Tests`, an isolated
forward-slash `OutputPath`, nonzero executed-test evidence, and no parallel TUnit assemblies.
Read the full testing owner before execution; this planning stage ran no builds or tests.

## Delivery and rollout

Implement S1-S4 on a task branch after TestDesign. Generate the migration with the repository's
EF CLI procedure; old rows default to Auto but existing worktrees must always be adopted rather
than recreated. Deploy server and script together for the new flags; an old script still gets
automatic server continuity, while its older output will not show the structured preview.
No AppHost restart, deployment, board mutation, merge, or production dispatch is part of Plan.

Until the fix is landed, follow the current orchestration contract: land this plan before
starting TestDesign, or explicitly arrange for TestDesign to contain this branch. Do not rely
on the not-yet-implemented automatic continuation to deliver this plan to its next stage.
