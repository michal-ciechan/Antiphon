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

## Verification design

Date: 2026-09-08. Stage: TestDesign. The decisions above are unchanged. This section specifies
executable tests for Code to implement and run; it does not claim that those tests exist or pass.
Inspection baseline: `d9f772a1` in the TestDesign worktree. All references below are repository
paths. No live CARD-0412 branches, production tasks, paid agents or shared stack are test fixtures.

### Proves it works now

#### Fixtures and test naming

Use `tests/Antiphon.Tests`, namespace `Antiphon.Tests.Application`. Each V row below defines one
method named exactly `T0442_Vnn`, with its listed cases as named TUnit data rows (one invocation
per case). The class key determines its file under `tests/Antiphon.Tests/Application/`:

| Key | Class | Methods | Planned invocations |
|---|---|---|---:|
| C | new `AgentTaskWorktreeContinuityTests` | V01, V27 | 5 |
| R | new `AgentTaskWorktreeBaseResolverTests` | V02-V10 | 35 |
| A | new `AgentTaskWorktreeBaseCreateTests` | V11-V14, V17 | 23 |
| P | new `DelegateScriptWorktreeBaseTests` | V15-V16 | 12 |
| M | new `AgentTaskWorktreeBaseMigrationTests` | V18 | 1 |
| D | extend `AgentTaskDispatchBaseGuardTests` | V19-V22, V26, V28 | 19 |
| W | extend `DelegationWorktreeTests` | V23-V25 | 5 |
| Total | 28 methods | V01-V28 | 100 |

Use `[Category("Integration")]` for these fixtures. Add the assembly-local
`[ParallelLimiter<ProcessSpawnLimit>]` to classes that run Git or pwsh, including extended
classes missing it today. A fixture that calls a global dispatcher/reply sweep gets unkeyed
`[NotInParallel]`; a group name alone does not isolate that sweep.

- **Git:** use `TestHelpers/ScratchGitRepo.cs`, with `git init -b master`, local test identity,
  and a scratch bare `origin` for land cases. Every setup Git command must assert successful
  exit. Use full `rev-parse <ref>^{commit}` results and compare file bytes independently of the
  resolver's decision. Create source branches in scratch linked worktrees with the real
  `WorktreeManager`; keep main checkout and source checkout HEAD/status snapshots before each
  operation. Local origin URLs point only to fixture-owned directories. No fetch is needed for
  dispatch; a recording Git seam must detect any attempted fetch in resolver/create tests.
- **Database:** `TestDbFixture.CreateIsolatedSchemaAsync()` and its connection string for every
  service/relay context. Seed distinct board/card/task GUIDs. Scope assertions to those IDs,
  including absence assertions; clear the change tracker or reopen the context between phases.
  Do not reuse `DelegateTaskApiRelay`'s current shared-default DB context unchanged.
- **Graph:** start from `AgentTaskDispatchBaseGuardTests.CreateProvider` and register Git through
  `DelegationTestServices.AddDelegationWorktreeGraph`. Extend that helper for the new resolver.
  Use a fake/recording runner and owned launch queue with no real provider executable; retain
  real resolver, create, dispatcher, worktree and land services. `TimeProvider.System` or an
  offset over real time is sufficient. Never use a frozen queue clock.
- **Settlement:** for V01, feed task-scoped assistant report/TurnEnd evidence ending with
  `DelegationReportFormatter.ReportToken(id, "done")` into the established
  `AgentTaskReplyService.OnTurnEndAsync` harness. Assert Succeeded before creating the next task;
  do not merely set every status to Succeeded and bypass no-change cleanup.
- **CLI:** execute `DelegateScriptRunner.RunAsync` against a random loopback listener. Use the
  `DelegateScriptKindTests.StubApi` pattern for payload tests and a service-backed relay for V15.
  The latter uses web JSON/string enums and the real `AgentTaskService.CreateAsync`, but owns no
  hosted dispatcher. Its caller has no parent/session reply route, so the response must contain
  `noReplyRouting=true`. Clear inherited task/capability environment variables and use only a
  temporary synthetic capability store when exercising the capability path. Fail unexpected
  detail/event/poll requests: the script can obtain its preview only from the initial POST.
  Authentication/status-mapping cases use the guarded `AntiphonWebAppFactory` and existing
  capability API setup, not a relay that pretends to implement authorization.
- **Races:** coordinate with `TaskCompletionSource` barriers in decorators around the existing
  Git/worktree I/O seams. Never use a sleep to hope that a ref moves at the right time. Queue-time
  races create the task with the real service, dispose its scope, change the fixture, then tick
  from a new provider/scope. For the SHA race, pause the worktree-manager call after resolution,
  capture its base argument, move the source ref, then release it to the real manager.
- **No-launch oracle:** at every refused/held/prelaunch-blocked boundary assert no new target
  task branch/directory, null task session/worktree coordinates, no session row or launch-queue
  entry for that task, and zero runner starts. A missing session alone would miss premature Git
  worktree creation. Create refusal also means no new runnable task row. Snapshot source refs,
  main HEAD/index/worktree and source file contents to prove they were not changed.

#### Numbered cases

In these fixtures `M` is the initial master commit, `A` adds `code-a.txt = "A\n"`, and `B`
descends from A and adds `code-b.txt = "B\n"`. `X` is a divergent child of M with a distinct
marker. Candidate rows are same-card, Succeeded, clean and quiescent unless the case says otherwise.
Explicit source requests always go through server-side identifier resolution. Each refusal
asserts the relevant reason/branch identity, not just a nonzero exit or an exception of any type.

| ID / class | Behaviour and named cases | Layer / test | Expected evidence |
|---|---|---|---|
| V-1 / C | Full cycle; `implicit_master`, `explicit_master`, `inherited_parent` (3) | integration / `T0442_V01` | Execute the sequence below. Independent HEAD/marker, persisted destination, detail/event/brief and final bare-origin assertions all pass. |
| V-2 / R | Tip ordering; `newer_review_at_ancestor`, `equal_tip_completion`, `equal_tip_id_tie` (3) | integration / `T0442_V02` | A newer Review at A cannot beat Code B. Equal tips collapse to one snapshot, latest completion labels it, and equal completion timestamps use a fixed task-ID tie-break. Repeat each resolution after reversing row insertion order. No ambiguity or omitted-work warning for ancestors/equal content. |
| V-3 / R | Repository/card identity; `disjoint_linked_worktree`, `nested_repository`, `same_origin_clone`, `other_card_guid`, `same_identifier_other_board` (5) | integration / `T0442_V03` | Only the linked-worktree positive is eligible. Compare canonical absolute common directories with platform path rules; Windows case/trailing-separator aliases of the positive resolve identically. Nested repo and same-origin clone remain distinct even when branch names/SHA objects are available locally. Card equality uses GUID, never display identifier/short branch text. |
| V-4 / R | Landed/uncertain history; `ancestor`, `linear_patch_equivalent`, `merge_range` (3) | integration / `T0442_V04` | Ancestor of current fallback is omitted as already contained. For patch equivalence, cherry-pick A onto a changed target: hashes differ and real `git cherry` contains only `-`; Auto selects target. A merge-containing off-target range is reported as uncertain, not declared landed from empty cherry output or chosen automatically. Explicit quiescent Task selection retains the exact requested SHA in all three cases. |
| V-5 / R | Non-success sources; `blocked`, `failed`, `canceled` (3) | integration / `T0442_V05` | With only that kept branch, Auto returns the legacy fallback and names the omitted branch/status. Explicit Task inherits its committed A when quiescent. It does not recover uncommitted content. |
| V-6 / R | Active writers; `queued_original`, `dispatched_original`, `working_original`, `queued_shared_followup`, `dispatched_shared_followup`, `working_shared_followup` (6) | integration / `T0442_V06` | Auto warns and excludes; Task refuses with 422. For Shared cases the source row itself stays Succeeded; the separate open follow-up owns its checkout. Thus filtering source status alone cannot pass. Settling/removing the writer restores eligibility on re-resolution. |
| V-7 / R | Unsafe checkout; `tracked_unstaged`, `staged`, `untracked`, `merge_in_progress`, `rebase_in_progress` (5) | integration / `T0442_V07` | Auto excludes with precise warning; explicit Task refuses 422. Commit/clean ordinary changes or finish/abort the scratch merge/rebase, then the same branch becomes eligible. Preserve file bytes/index and in-progress state during every refused observation. |
| V-8 / R | Availability; `unregistered_local_branch`, `missing_original_directory`, `missing_local_branch`, `remote_only`, `git_error` (5) | integration / `T0442_V08` | First two continue A using a verified surviving common repository/local branch. Missing-local and remote-only cases retain a kept task record but offer warned fallback; explicit Task refuses. Inject a strict Git observation error separately from missing-ref: Auto says inspection unknown/fallback, Task refuses, neither claims no prior work. Remote-only fixture has objects and `refs/remotes/origin/<branch>` but no `refs/heads/<branch>`; no implicit fetch/remote substitution. |
| V-9 / R | Eligible tip plus excluded work; `excluded_contained`, `excluded_divergent` (2) | integration / `T0442_V09` | B remains Auto's source. An excluded row at committed A is not falsely advertised as missing committed work; an excluded X is named with its reason and SHA. If dirty working files exist, any dirty-files warning must not claim those bytes were inherited merely because the committed tip is contained. |
| V-10 / R | Legacy fallback; `no_card_auto`, `bound_no_candidates`, `fresh_target` (3) | integration / `T0442_V10` | Put repository HEAD on a scratch `topic` branch ahead of master. With null destination, fallback is topic HEAD, not master/origin/master; source provenance is null. Fresh with eligible A names A as intentionally omitted. Within each case, repeat with explicit `release` destination: start at release and retain that field. Auto with no card makes no sibling inventory query. |
| V-11 / A | Invalid overrides; `both_flags`, `shared`, `readonly`, `onagent_task`, `onagent_fresh`, `task_without_card` (6) | integration / `T0442_V11` | Real create refuses 422 before a runnable row or worktree. Exercise the OnAgent cases with a live prior agent so the existing Shared rewrite cannot discard the flag. Error names the invalid combination; explicit Task is never silently changed to Auto. |
| V-12 / A | Explicit boundaries; `cross_card`, `cross_board`, `nested_repo`, `separate_clone`, `destination_mismatch` (5) | integration / `T0442_V12` | Authorized caller cannot use BaseTask to cross card/common-repo/destination boundaries; 422, no new task/source mutation. Destination mismatch fixture uses source `release` and requested `master`; also show null and explicit `master` compare equal in its accepted control. Parent/root/card/session routing fields are unchanged by accepted selection. |
| V-13 / A | Source identifier; `full_guid`, `unique_short`, `missing`, `ambiguous_short` (4) | integration / `T0442_V13` | Full and unique 8-hex short IDs resolve to the same persisted GUID. Missing ID is the existing 404; seed two same-prefix GUIDs for existing 409 ambiguity. No arbitrary ref/path interpretation. Refused responses cannot expose an unauthorized card's candidates. |
| V-14 / A | Divergence before creation; `two_tips`, `four_tips` (2) | integration / `T0442_V14` | Use graph fixtures below. Auto returns Conflict/409 with candidate IDs, branches/full SHAs and both recovery switches, before inserting a runnable row. Choosing each maximal tip by BaseTask works independently with unchanged landing target and omitted-other-history feedback. Fresh succeeds with all omitted work named; no automatic merge or newest-task selection. |
| V-15 / P | Immediate actual-script feedback; `continue`, `wait`, `fresh`, `unknown_fallback` (4) | integration / `T0442_V15` | Run real `delegate.ps1 -Role Code -Worktree -Card <fixture-card> -Goal ...` through the real-create relay. Capture POST response and process output before any tick. Continue prints source task/branch/full SHA, isolated-new-branch intent and landing destination; other cases print waiting or exact omission/inspection reason and fallback. `noReplyRouting=true`; queue remains untouched. Preserve an independent singular Warning and routing/scope output in the same response. No later event or session interruption can satisfy this test. |
| V-16 / P | Real-script payload/validation; `omitted`, `base_task`, `fresh`, `both_flags`, `shared`, `readonly`, `onagent_task`, `onagent_fresh` (8) | integration / `T0442_V16` | Stub captures actual JSON: omitted emits neither override, Task sends the caller's short ID as `worktreeBaseTask`, Fresh sends `freshWorktree=true`. Illegal combinations exit nonzero before POST. For valid Worktree input exercise both `-Worktree` and `-Workspace Worktree` within the case. An older response without `worktreeBase` is still printable. |
| V-17 / A | Earlier access/launch guards; `revoked_capability`, `directory_denied`, `quota`, `provider_signin`, `concurrency`, `routing_pin` (6) | integration / `T0442_V17` | Combine an otherwise selectable A/BaseTask with the existing refusal fixtures. Preserve 403 revoked capability, 422 directory boundary, and their established 409 quota/sign-in/concurrency/pin refusals. No new runnable task, runner/worktree start or silent reroute. Unauthorized caller/directory cases perform no source Git observation and disclose no source preview. Use synthetic registry/usage/hold state, never live availability. |
| V-18 / M | Persistence upgrade; `upgrade_and_roundtrip` (1) | integration / `T0442_V18` | In an isolated schema, migrate to the immediate predecessor of the new migration, insert a historical task with existing branch/base SHA, then migrate up. New mode defaults to Auto=0; nullable requested/resolved/preview fields stay null; historical SHA/destination survive without guessed source. Save/reload Auto, Target and Task rows with preview observation time/candidates, then recreate the service provider and project detail. Values survive exactly; preview stores summaries, not raw Git output. Locate the new migration by name, not `migrations[^1]`. |
| V-19 / D | Launch re-evaluation; `new_descendant`, `landed_in_queue`, `unchanged` (3) | integration / `T0442_V19` | Preview A, persist, dispose provider. Add B, land A onto target, or change nothing. New provider's tick chooses B, target, or A respectively and records actual SHA/source/destination. Changed decisions retain the original preview/time, explain the difference in durable Warning/event/detail and a routed parent WhenIdle note. Unchanged continuation needs no late omitted-A warning. Repeat the changed case without reply routing: persisted evidence still exists and no parent message is needed. |
| V-20 / D | Land precedence; `auto_two`, `task_two`, `fresh_two`, `auto_four`, `task_four`, `fresh_four` (6) | integration / `T0442_V20` | For graphs below set one relevant uncontained sibling's durable `LandRequestedAt`, including when it is not the explicitly chosen source. Preview is wait (not ambiguity refusal). Create succeeds Queued; repeated ticks create no worktree/session and pipeline says `siblingLandInFlight`, without duplicate hold spam. Also create with a valid preview first, then start land before tick: same hold. Fresh/BaseTask cannot bypass it. |
| V-21 / D | Land completion and remaining tips; `all_contained`, `still_divergent` (2) | integration / `T0442_V21` | Clear durable pending land only after scratch integration into target, then tick from a fresh scope. Fully integrated four-branch graph releases Auto onto current target despite retained branch names. Partial land leaving two maximal uncontained tips makes Auto durably Blocked before launch, naming both and resolve/retry or cancel/recreate choices. Historic LandRequested event with null column must not hold. Resolving remaining histories then `RetryAsync` releases the same Auto task. |
| V-22 / D | Inputs worsen after preview; `auto_diverges`, `task_deleted`, `task_dirty`, `task_active` (4) | integration / `T0442_V22` | Create from valid A, then introduce X or invalidate the explicit source before tick. Durable Blocked/error evidence and no-launch oracle; no fallback to master. Detail survives provider restart and names branches/actions. For the active case source row stays Succeeded and a new Shared follow-up is the writer. |
| V-23 / W | Immutable snapshot; `ref_moves_after_observation` (1) | integration / `T0442_V23` | Capture full A SHA passed to worktree creation, move source to B behind the barrier, then create. New HEAD and WorktreeBaseSha are A, A marker exists, B marker absent, source still at B. Event/brief/detail identify inherited A; no checkout/reset of the source or main repository. |
| V-24 / W | Failed SHA creation; `creation_fails` (1) | integration / `T0442_V24` | Fail the worktree add at selected A through the I/O seam. Assert the existing failed-creation path, zero runner starts, one attempted full-SHA base and no second attempt from HEAD/master. Source A/ref/files survive. Reuse existing rollback tests for partial directory/registration cleanup. |
| V-25 / W | Adoption wins; `persisted_coordinates`, `crash_before_coordinates`, `registered_missing_directory` (3) | integration / `T0442_V25` | Existing task branch contains A plus task-owned C. Before retry, add divergent sibling X and invalidate any former explicit source. Adopt/heal this task branch instead of blocking/reselecting/recreating from the new preview. C survives; branch/directory ownership is the same task. For persisted data retain its original WorktreeBaseSha=A and source provenance, even though HEAD=C. Unsaved provenance stays unknown rather than invented; if no base SHA was recorded, do not assert a reconstructed historical A. Missing-directory arm preserves C while using the existing locked-registration heal path. |
| V-26 / D | Retry with no worktree yet; `auto`, `target`, `task` (3) | integration / `T0442_V26` | Prelaunch-block a task, reload and call real `RetryAsync`. Requested mode/source GUID and original preview survive, attempt increments, and destination/parent/root/card stay unchanged. At next tick Auto may re-resolve to B, Target still uses target despite B, Task still uses chosen A despite B. If Task remains invalid it blocks again rather than defaulting. |
| V-27 / C | Existing Git-facts semantics; `no_target`, `explicit_target` (2) | integration / `T0442_V27` | Start at inherited A, add B. With null target, existing completion/check Git facts use WorktreeBaseSha=A and count only the task's new range. With explicit target M, retain target-relative A+B facts. In both cases provenance identifies inherited A. Re-adopt after B and recheck: recorded creation base must not change to B and make the new range disappear. |
| V-28 / D | Recovery is explicit; `question_is_not_source_selection` (1) | integration / `T0442_V28` | After V22-shaped Blocked, submit an ordinary reply containing a branch/task ID through the established reply path. It may follow its existing no-session refusal/handling, but cannot change requested mode/source or create a worktree/session. Only the named retry-after-integration or cancel/recreate override paths select history. |

**V01 sequence and independent oracle.** The null-target and explicit-master cases use M as
destination; the inherited-parent case has a parent task on `feat/parent`, with matching effective
destination for each same-card stage. Retain the same parent relationship across those requests.
Record each created row's `MergeTargetRef`, `ParentTaskId`, `RootTaskId`, `CardId` and reply routing
as resolved normally by create; source resolution must not rewrite them.

1. Create/dispatch Code 1 without overrides. Commit A in its new directory and settle normally.
   Main/destination and bare-origin destination are still M. A exists only on the kept task tip.
2. Create Review without overrides through `AgentTaskService`. Before tick its create preview
   names A. Dispatch: HEAD is exactly A and marker bytes match, in a different task branch and
   directory. WorktreeBaseSha=A; landing destination unchanged. Settle Review with zero commits.
   Exercise real no-change cleanup; the following source may be the original Code row if Review's
   empty branch was removed. Do not require a nonexistent Review branch or guess its provenance.
3. Create/dispatch Code 2 without overrides. It starts exactly at A with the marker. Commit B,
   settle normally and verify A+B remain in Code 2. Destination has not advanced during any
   create/dispatch/settlement. Brief, worktree-created event and task detail agree on actual
   inherited SHA and effective destination; null is labelled master for explicit land, not HEAD.
4. Explicitly land Code 2 using `AgentTaskLandService.RunAsync`, with scratch origin only. Assert
   successful existing land outcome, both marker bytes on local target and bare-origin target,
   and ancestry/patch evidence for the inherited and new commits (do not assume SHA survives a
   rebase). Parent-target case leaves master at M. Older tasks are not marked landed/deleted by
   source selection; existing cleanup owns their eventual residue. There is no warning that
   contained A/Review work was omitted. A subsequent Auto preview offers the current target.

**Divergence and CARD-0412-shaped fixtures.** These are synthetic reproductions of the stated
four-unlanded-branch situation, not a claim to have inspected tonight's live DAG.

```text
two:   M--A             four:  M--A--B
        \--X                    \--X--Y
```

Seed two rows at A/X for `two_tips`; seed four rows at A/B/X/Y for `four_tips`. Each off-M commit
has a unique marker. Give A the newest completion time and B the oldest to defeat timestamp
selection. Four branches have two maximal tips B/Y; candidate diagnostics inventory all four
and identify the two competing maxima. Repeat the four-tip resolution after rewriting the
fixture as four independent children of M; all four must be competing maxima. This second
assertion uses the same data-row invocation and a fresh schema/repo.

For V20/V21, seed the durable land flag plus a historic event; do not equate the event with the
flag. A completed all-contained land integrates both chains into target in the scratch repo
(a scratch merge is fixture setup, never resolver behavior). A partial land integrates A only,
leaving B and Y both uncontained. If only one maximal tip remains after any integration, Auto
continues that tip; cover this intermediate state in `still_divergent` after its initial Blocked
assertion, before finishing integration/retry. Never “fix” this test by accepting newest-tip
fallback or allowing Fresh to ignore an outstanding land.

**V04 patch/merge recipes.** For `linear_patch_equivalent`, create A off M, advance master with
an unrelated file, then cherry-pick A onto master; assert distinct SHAs, non-ancestry of A, and
only `-` entries before calling the resolver. For `merge_range`, branch `left` and `right` at M,
commit independent L/R marker files, and merge right into left with `--no-ff`. Keep that merge
tip as the source. Advance master with an unrelated file, then cherry-pick L and R individually.
Assert the source range contains a merge, is not an ancestor of master, and real `git cherry
master <source>` has no `+` entries. This forces the uncertainty check to matter even though
linear patch checks would call the individual commits applied. Use argument-list Git helpers,
not shell-expanded command strings. All branches remain confined to disposable fixtures.

### Guards the regression

| ID | Future regression | Caught by / decisive assertion |
|---|---|---|
| R-1 | Revert creation to MergeTargetRef/HEAD, or reuse predecessor directory | V01, V23: exact A HEAD/marker in a distinct task branch/directory, and both commits finally land. |
| R-2 | Last-created/stage-based choice, silent divergent merge/pick | V02, V14, V22: ancestry wins; 2/4 maxima refuse before create or block before launch. |
| R-3 | Path containment, origin URL or card display label becomes identity | V03, V12, V17: foreign fixture is excluded/refused and unauthorized source is never inspected/disclosed. |
| R-4 | Active/dirty/failed sources enter Auto or explicit override bypasses custody checks | V05-V08: required fallback/refusal and unchanged source state; Shared follow-up closes the source-status loophole. |
| R-5 | Landed rebased work is resurrected, or merge ranges are guessed equivalent | V04, V21: real patch-only range is excluded; uncertain merge is reported; retained landed branch names do not hold. |
| R-6 | Preview exists only in a late Warning/WhenIdle message, or replaces existing warnings | V15: real process output and POST response are asserted with no dispatcher/reply route; independent Warning/routing/scope text survives. |
| R-7 | Preview freezes selection across restarts, live ref redirects creation, or add failure retries master | V19, V22-V24: new provider observes current state, add receives full immutable SHA, failure never changes base. |
| R-8 | Continuation changes the landing destination or task/session identity | V01, V12, V26: persisted identities unchanged, scratch master/parent/origin advance only at explicit land. |
| R-9 | Fresh/BaseTask bypass land, or historical event holds forever | V20-V21 plus existing pipeline hold tests: Queued and `siblingLandInFlight` while durable flag applies, actual containment releases it. |
| R-10 | Retry forgets intent or adoption overwrites commits/base SHA | V25-V28: same task retains C and original A base, explicit intent survives fresh provider/retry, question text is never a Git selector. |
| R-11 | New dependencies break unrelated callers or weaken land refusals | Run the existing class/method set below; retain conflict, push-failure, verification-refusal, capability and OnAgent behavior. |

Deliberately update these old assertions in `AgentTaskDispatchBaseGuardTests`: clean eligible
`a_kept_sibling_with_no_land_dispatches_with_a_warning_and_whenidle_note` now continues and needs
no omitted-source interruption; `a_sibling_whose_branch_was_deleted_is_silent` now distinguishes
a missing kept branch (warning) from a legitimately cleaned task with no kept coordinates;
`a_stranded_request_row_with_a_null_column_only_warns` now continues when eligible and is never
held solely by the historic event. Preserve both existing in-flight-land tests.

Rename/reframe `DelegationWorktreeTests.two_top_level_worktree_tasks_on_one_card_both_branch_from_repo_head`
as explicit Target/legacy low-level behavior. It is not evidence for default Auto; V01 must
exercise the real create/dispatch selector. Keep the existing adoption/heal/rollback assertions
and strengthen adoption to check the recorded base, which today's method overwrites with HEAD.

### Positive controls

Code executes each PC independently: retain tests, make only the stated one-line production
mutation, rebuild/run the named V method, observe its specified assertion failure, revert that
line, rebuild/rerun and observe green. These are semantic line targets because the new production
symbols do not exist yet; record the actual file/line/diff in the Code report. No fake-only
mutation, compile failure, startup/fixture error, timeout, skipped case or zero-test run counts
as a red control. For multi-case methods the expected failing cases are named below; other cases
may stay green. Reuse the restored-green evidence as the final evidence for that method if there
are no subsequent relevant edits.

| ID | One-line mutation / target guard | Expected red, then restored green |
|---|---|---|
| PC-1 | At new worktree creation, replace the selected source SHA argument with `task.MergeTargetRef ?? "HEAD"`. | V01: inherited marker/HEAD fails before land in all destination cases. |
| PC-2 | Replace the predicate refusing multiple distinct maximal tips with `false`. | V14: expected 409/no-row oracle fails for two and four tips; a different exception is not accepted. |
| PC-3 | Replace the new same-card-GUID candidate predicate with `true`. | V03 `other_card_guid` and `same_identifier_other_board` wrongly select the foreign marker. |
| PC-4 | Replace canonical-common-directory equality with `true`. | V03 nested/clone negatives wrongly inherit. Fixtures make same-named refs resolvable so a later missing-ref failure cannot hide the mutation. |
| PC-5 | Replace effective-landing-destination equality with `true`. | V12 `destination_mismatch` ceases refusing; accepted control still passes. |
| PC-6 | Replace the Auto Succeeded-status eligibility predicate with `true`. | V05 non-success rows are selected instead of warned fallback. |
| PC-7 | Replace the open-writer refusal predicate with `false`. | V06 Shared-follow-up cases permit explicit/Auto continuation; positive post-settlement controls still pass. |
| PC-8 | Replace the checkout safe-state result used by eligibility with `true`. | V07 unsafe checkout cases select/refuse incorrectly while original bytes/index remain asserted. This includes operation-in-progress checks, not just porcelain dirt. |
| PC-9 | Change local-head lookup to accept the matching remote-tracking ref when local lookup fails. | V08 `remote_only` incorrectly continues; it has a real remote-tracking ref/object. |
| PC-10 | Map strict Git inspection failure to an empty-success/no-candidates result. | V08 `git_error` loses its unknown warning or explicit-source refusal. |
| PC-11 | Disable linear patch-equivalent exclusion (its predicate becomes `false`). | V04 `linear_patch_equivalent` resurrects A instead of selecting target. |
| PC-12 | Disable the merge-containing-range uncertainty guard (predicate becomes `false`). | V04 `merge_range` ceases reporting uncertainty; fixture has no `+` cherry entries to expose false equivalence. |
| PC-13 | Disable the durable pending-land hold predicate. | V20: either premature create conflict instead of wait, or premature launch; all override modes must enforce the hold. |
| PC-14 | Change the pending-land test from the durable column to historic LandRequested-event existence. | V21: completion remains held even after the column clears and integration is proven. |
| PC-15 | Use persisted create preview as the dispatch decision instead of re-resolving. | V19 `new_descendant` starts A instead of B; V22 `auto_diverges` launches instead of becoming Blocked. Run both methods for this PC. |
| PC-16 | Replace the final full-SHA worktree-add argument with the selected branch name. | V23: barrier moves ref to B and created HEAD is wrong. |
| PC-17 | Replace the failed-selected-SHA creation throw with a second add using `"HEAD"`. | V24: spy sees fallback attempt and/or failed task becomes dispatched. |
| PC-18 | Assign `task.MergeTargetRef = task.WorktreeBaseBranch` after resolving a continuation. | V01: persisted destination changes before any land; no need to permit a wrong-target land to detect it. |
| PC-19 | Disable the same-task adoption fast path and pass its would-be new source through normal resolution. | V25: existing C is blocked/reset by divergent/invalid external sources instead of adopted. |
| PC-20 | Change adoption's preserve-existing-base assignment to unconditional current HEAD. | V25 `persisted_coordinates`: WorktreeBaseSha becomes C; V27 would also expose lost task-only range. |
| PC-21 | In retry, reset requested mode to Auto and requested source to null in one assignment/statement. | V26 `target`/`task`: stored intent or subsequent target/A HEAD is wrong. |
| PC-22 | Remove the create DTO's structured preview assignment (set it null). | V15: initial service response/output lacks source or precise wait/fallback; no event polling can rescue it. |
| PC-23 | Disable the real script's preview-printing condition (replace it with `$false`). | V15: response remains correct but actual initial CLI output fails. |
| PC-24 | Disable the override-combination validation predicate. | V11/V16 invalid combinations cease failing at their required boundary. Mutate server and script separately, restoring between them; each is an independent control arm. |
| PC-25 | In separate arms, replace `if (capability.RevokedAt is not null)` in `AgentTaskService.AuthenticateAsync` with `if (false)`, then restore; replace the allowed-root refusal condition in `DelegationWorkspaceResolver.ResolveAsync` with `false`, then restore. | V17 revoked/directory-denied cases observe forbidden source access and lose their expected refusal. Test the real API for revoked credentials, not an already-authorized service caller. |
| PC-26 | Change the launch-time invalid/ambiguous decision branch from durable Blocked to target fallback. | V22: invalid explicit input or new Auto divergence starts a worktree/session instead of retaining actionable Blocked evidence. |
| PC-27 | Replace ancestry-based winner selection with the most recently completed eligible task row. | V02 `newer_review_at_ancestor` chooses A instead of B despite B containing the complete eligible history. |
| PC-28 | In explicit Task selection, replace its source decision with target fallback when that source is already an ancestor of target. | V04 `ancestor`: explicit source SHA/provenance differs even if file content happens to match, so silent reinterpretation is detected. |

For PC-25, keep every other gate satisfied and retain the valid caller control. A red status
alone without proof of the forbidden source access does not validate the custody assertion.
Its two arms and PC-24's two language arms mean
**30 mutation arms across 28 numbered controls**. Do not weaken authorization in a running host;
all mutations run exclusively in the isolated test process and are reverted before commit.

### Out of scope

- Paid/live-agent, production-runner, Telegram/Herdr delivery, browser rendering, AppHost restart,
  deploy and live CARD-0412 repair. Initial CLI timing is fully observable before an isolated
  dispatcher tick; a real provider/session would add cost without strengthening that oracle.
- Remote fetch/recovery, cross-clone continuation, arbitrary base refs/paths, automatic history
  integration, predecessor ownership transfer and global Git locks: D6 excludes them. Negative
  tests ensure the new overrides do not introduce them.
- Rewriting target-relative Git facts, default-branch/fetch policy, or older-task cleanup policy.
  V10/V27 and existing land/residue tests pin the boundaries instead.
- Full TUnit assemblies, Pty assembly and client Vitest as default loops. No browser/UI behavior
  changes are planned. Run the scoped tests below and client TypeScript check for the DTO change;
  broaden only for changed surfaces or a concrete failure, per the testing owner.

### Cost

Suites forced: `Antiphon.Tests` only, the named classes/methods below; no concurrent second TUnit
assembly. Planned new coverage is **28 methods / 100 invocations**, plus retained regressions and
**30 isolated positive-control mutation arms**. These are planned counts, not measured results.
Budget approximately **45-75 minutes** for Code's verification after implementation: initial
build/schema setup, real Git/pwsh scenarios, retained land tests and sequential red/rebuild/green
arms dominate. Warm scoped-green runs should be substantially shorter; report actual elapsed
times instead of claiming this estimate was measured. Test implementation time is additional.

Run from the task worktree with Docker available for the test-owned Postgres container. Do not
restart the shared local stack to test this change. Use the same isolated forward-slash output
directory for every arm so daemon binaries remain untouched and build ledgers do not multiply.

```powershell
# New V cases only. One class-filtered foreground process at a time.
$c442Classes = @(
    'AgentTaskWorktreeContinuityTests',
    'AgentTaskWorktreeBaseResolverTests',
    'AgentTaskWorktreeBaseCreateTests',
    'DelegateScriptWorktreeBaseTests',
    'AgentTaskWorktreeBaseMigrationTests',
    'AgentTaskDispatchBaseGuardTests',
    'DelegationWorktreeTests'
)
$c442Run = Get-Date -Format 'yyyyMMdd-HHmmss'
foreach ($c442Class in $c442Classes) {
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c442/ -- --treenode-filter "/*/Antiphon.Tests.Application/$c442Class/T0442_V*" --report-trx --report-trx-filename "c442-$c442Run-$c442Class.trx"
    if ($LASTEXITCODE -ne 0) { throw "$c442Class failed: $LASTEXITCODE" }
}

# Positive-control example. Use the table's class and exact Vnn for every other arm.
# Rebuild after each production mutation AND after restoring it; never --no-build here.
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c442/ -- --treenode-filter '/*/Antiphon.Tests.Application/AgentTaskWorktreeContinuityTests/T0442_V01' --report-trx --report-trx-filename "c442-$c442Run-pc01-red.trx"
# Inspect fresh executed assertion failures; revert ONLY the mutation line, then:
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c442/ -- --treenode-filter '/*/Antiphon.Tests.Application/AgentTaskWorktreeContinuityTests/T0442_V01' --report-trx --report-trx-filename "c442-$c442Run-pc01-restored.trx"
```

Retained class runs, same command prefix and unique TRX filenames, with exact filters:

```text
/*/Antiphon.Tests.Application/DelegationWorktreeTests/*
/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*
/*/Antiphon.Tests.Application/AgentTaskLandStageOutcomeTests/*
/*/Antiphon.Tests.Application/AgentTaskPipelineStatusTests/*
/*/Antiphon.Tests.Application/DelegateScriptKindTests/*
/*/Antiphon.Tests.Application/DelegateScriptCapabilityTests/*
/*/Antiphon.Tests.Application/DelegationCapabilityTests/*
/*/*/DelegationTestServicesTests/*
/*/*/DelegationHarnessCensusTests/*
/*/Antiphon.Tests.Application/AgentTaskServiceIntegrationTests/a_follow_up_*
/*/Antiphon.Tests.Application/AgentTaskServiceIntegrationTests/OnAgent_defaults_stage_to_FollowUp_and_sets_FollowUpOfTaskId
/*/Antiphon.Tests.Application/AgentTaskServiceIntegrationTests/retrying_*
/*/Antiphon.Tests.Application/AgentTaskAgentKindTests/a_follow_up_*
/*/Antiphon.Tests.Application/AgentTaskPoolTests/a_follow_up_in_the_same_run_keeps_the_context_uncompacted
/*/Antiphon.Tests.Application/AgentTaskPoolTests/a_pinned_follow_up_waits_while_its_agent_is_still_working
```

Full existing Worktree/Dispatch class runs subsume their new V methods: if those runs occur after
the last relevant edit, reuse their V results rather than running them twice just for counting.
Retained-regression counts come from fresh TRX results at Code HEAD, not a static source `[Test]`
count. For the client DTO compile check, run `node node_modules/typescript/bin/tsc -b --pretty false`
from `client/` and retain its real exit code (install dependencies with the established lockfile
workflow only if absent). No new Vitest tests are required for compatible optional DTO fields.

For every invocation, inspect the fresh TRX `UnitTestResult` names/outcomes and `Counters`, and
check the intended class and case names actually executed. Require the planned per-class V counts
above and zero failed/skipped/not-executed V cases on final green. A data-row split/rename during
implementation must update this manifest with a reason; never lower a count just to accept a
filter that matched nothing. `--list-tests` and exit zero alone are not execution evidence.
Use unique filenames/paths for every PC arm and restoration, including PC15's two methods.

Code's final report includes a V/R/PC table with executed counts, failures, restored outcomes,
fresh TRX artifact paths, actual mutation diffs and unresolved items. A pre-existing failure is
verified by rerunning that exact test at the base commit in isolation. Finish with
`git diff --check`, confirm no mutation remains, and remove only this checkout's verified
`bin-c442` directories using native PowerShell path handling per the testing owner. Commit/push
the implementation and evidence; landing/deployment remain the caller's operation.
