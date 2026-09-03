# CARD-0215 — a build's branch is a sibling of the plan branch, never its descendant

**Plan pass, 2026-09-03. Mechanism traced in the current code and confirmed against the live task row and git history of all three cited instances; no production code is changed by this plan.**

## Verified verdict

The plan doc does not "drop out" of anything. It is never in the build branch's history in the first place, and nothing in the pipeline ever puts it there.

Every Worktree task's branch is created by `DelegationWorktreeService.CreateForTaskAsync`, which passes `task.MergeTargetRef ?? "HEAD"` to `WorktreeManager.CreateAsync`, which runs `git worktree add -b feat/card-task-<id> <path> <baseRef>` in `task.RepoPath`. `MergeTargetRef` is set once, in `AgentTaskService.CreateAsync`:

```
MergeTargetRef = request.MergeTargetRef
    ?? (SharesRepoWith(parent, repo) ? parent.WorktreeBranch ?? parent.MergeTargetRef : null)
```

`delegate.ps1` has no parameter that sends `mergeTargetRef`, and an orchestrator that is an interactive session (no `ANTIPHON_TASK_TOKEN`, so no parent task) yields `null`. For every top-level dispatch, then:

- base = `HEAD` of the main checkout (`C:\src\Antiphon`, on `master`), i.e. master's tip at the moment the dispatcher tick creates the worktree;
- merge target = none, so on success `TryMergeBackAsync` returns `LeftForHuman` and the branch is kept.

A Plan task therefore leaves its commit only on `feat/card-task-<planId>`. The Execute task dispatched next gets a fresh branch from master's tip: a **sibling** of the plan branch off the same (or a later) master commit. Its later land rebases `master..HEAD` of the build branch onto master and fast-forwards; the plan commit was never in that range, so it is neither replayed nor dropped. The feature code lands, the doc does not, and the plan branch sits unreachable until someone notices.

Evidence, all from `origin` as of today:

| Card | Plan commit (branch) | Plan's parent | Build commit on master | Build's parent | Plan is ancestor of build? |
|---|---|---|---|---|---|
| CARD-0122 | `3723bbf` (`feat/card-task-2378995d`) | `a67c673` | `ef91470` | `a67c673` | no — same parent, siblings |
| CARD-0032 | `dce786f` (`feat/card-task-fb15a6fe`) | `0abeeed` | `a6ae4408` (S1) | `6129c789` (a later master commit) | no |
| CARD-0036 | `29433d3` (`feat/card-task-b25bdf26`) | `1b1b667` | `90248a0b` (feat), `0abeeed6` (tests) | master's first-parent line, which contains `1b1b667` | no |

The CARD-0122 task row (`GET /api/agent-tasks/2378995d`) records `mergeTargetRef` empty, the `Dispatched` event "Worktree created ... on feat/card-task-2378995d (no merge target - branch left for review)", and the `Completed` event "Branch feat/card-task-2378995d kept - no merge target was set." All three plan branches still exist on `origin` with exactly one commit above master.

## What the investigation rules out

- **Retries are not the mechanism.** `AgentTaskService.RequeueAsync` keeps `WorktreePath` and `WorktreeBranch`; the dispatcher only calls `CreateForTaskAsync` when `WorktreePath is null`; and `WorktreeManager.CreateAsync` re-attaches an existing branch rather than re-creating it (CARD-0220). Attempt 2 runs on attempt 1's branch with attempt 1's base. A cancel-and-redispatch is a new task and gets a new branch off HEAD, which is the same base rule, not a different one. This is why the single clean attempt (CARD-0122) reproduced it: the base is wrong from the first tick.
- **The land/merge-back rebase does not drop reachability.** `git rebase <target>` replays only `<target>..HEAD`. The plan commit is on a different branch and never in that range. Nothing is lost; nothing was there.
- **The delegate is not the actor.** `server/Bundles/delegate-basics.md` and `orchestrator.md` say nothing about branches, bases, or plan ancestry. A build delegate told in prose to "build on top of `feat/card-task-<plan>`" has no harness mechanism to honour that; the only ways it can see the doc are `git show <branch>:<path>` (content without ancestry — exactly the recovery shape the card describes) or an explicit `git merge`/`rebase` it is never instructed to run. The delegate's read is a symptom, not the cause.

## Does today's land-first practice close the gap?

Ordering-wise, yes. `-Land <planTask>` runs `PrepareLandAsync` (fetch, rebase onto master) then `FinalizeLandAsync`, whose `AdvanceTargetAsync` fast-forwards master — via `merge --ff-only` inside the main checkout because master is checked out there — and pushes. The next dispatcher tick's `git worktree add ... HEAD` then branches from a master that contains the plan. Checked on every plan-then-build pair landed this week:

| Card | Plan commit | Build commit | Build's parent | Plan is ancestor? |
|---|---|---|---|---|
| CARD-0324 | `f3176c0d` | `1a5c8844` | `4c469cdf` | yes |
| CARD-0243 | `c3a97b21` | `582da043` | `f3176c0d` | yes |
| CARD-0093 | `dadc5d79` | `f094ef82` | `7447d603` | yes |
| CARD-0090 | `ff8a83b8` | `5f73b9ee` | `2c246c0a` | yes |

The mechanism is still latent, because nothing in the harness enforces the ordering the practice depends on:

1. **The land is asynchronous and slow.** `delegate.ps1 -Land` returns "queued" before git runs. `AgentTaskLandHostedService` then fetches, rebases, runs `dotnet build` whenever master moved since the plan branched (`BaseMoved`, the normal case an hour after dispatch — minutes of wall clock), pushes, and removes the worktree. The dispatcher creates the Execute worktree on its next 5-second tick from whatever HEAD is at that instant. An Execute dispatched inside that window branches from pre-plan master. The doc still reaches master through the plan's own land, so the *ancestry* hole closes at the build's land, but the build's worktree does not contain the plan file for the whole build: a brief that says "read `docs/superpowers/plans/...`" fails inside the worktree, and the delegate improvises or `git show`s it in.
2. **A refused land reproduces the original failure exactly.** `LandRefused` (origin ahead of local master, verification failure, or a conflict that flips the plan task to `Blocked` and spawns a Merge task) arrives as a WhenIdle note. If Execute is already dispatched, its branch is off pre-plan master and the note is not tied to the build in any way.
3. **A land held behind a Shared writer** (`AgentTaskLandService.FindSharedWriterAsync`, re-queued every 5 s) is case 1 for as long as that Shared task runs.
4. **The habit has no backstop.** Skip the plan's `-Land`, or land the build first, and it is 2026-08-27 again. `-Land` itself only exists since 2026-08-31 (`ae596005`); the four instances predate it and the orchestrator merged by hand.
5. **The sub-orchestrator path is already ordered.** Children of a Worktree parent get `MergeTargetRef = parent.WorktreeBranch`; a plan child merges back into the parent's branch in `AgentTaskReplyService.MergeBackAsync` before its report is delivered, and the build child branches from that same parent branch. No gap there unless the plan child's merge-back conflicted.

So the practice is the right practice, and the fix is to make the server enforce and verify the ordering it relies on, at the two points where the harness already makes branch decisions: worktree creation at dispatch, and land.

## Where the fix does not belong

- **Not the delegate-basics bundle.** The delegate cannot repair a base the harness chose, does not know the plan branch name unless the brief says so, and a rule to `git rebase` onto a sibling branch invites conflicts and a second, undocumented integration path. No bundle change is proposed.
- **Not a `-BaseOn <planTask>` knob in `delegate.ps1`.** The only server-side base knob is `mergeTargetRef`, and it is also the merge-back target: basing the build on `feat/card-task-<plan>` makes the build merge back *into the plan's kept worktree branch*, turning a Succeeded plan task into a live integration point that the worktree janitor may prune, and requiring `-Land <planTask>` to land the build. That is a heavier flow than needed and does nothing for the forgotten-land case. Keep it as a documented follow-up only if a build must ever start before a plan is landable.

## Slice 1 — dispatch-time base guard (the fix)

**Files:** `server/Application/Services/AgentTaskDispatcher.cs` (the Worktree branch just before `CreateForTaskAsync`, and the queued-task loop's hold pattern), `server/Application/Services/DelegationWorktreeService.cs` (git helpers).

Before creating a Worktree task's worktree, when the task is bound to a card (`CardId != null`), find its **kept sibling branches**: other tasks on the same card, same repo (`RepoPath` equal or within), `Workspace == Worktree`, `Status` Succeeded or Blocked, `WorktreeBranch != null`, whose branch still exists (`git show-ref --verify --quiet refs/heads/<branch>` in `RepoPath`). Branch existence is the durable "not landed" signal: `FinalizeLandAsync` deletes the branch, and `TryMergeBackAsync` removes a merged or empty one. For each sibling, resolve the base ref the worktree will use (`task.MergeTargetRef ?? "HEAD"`) and test `git merge-base --is-ancestor <sibling branch> <baseRef>` in `RepoPath`.

For a sibling whose tip is **not** an ancestor of the base:

- **Land in flight** (that sibling's latest `LandRequested`/`Landed`/`LandRefused` event is `LandRequested`, or a `Held` event after it): **hold this task this tick**, the same way the Shared-writer lease holds it — `continue` without dispatching, record one `Held` event on the transition ("held: CARD-nnnn's kept branch feat/card-task-<sib> (task <sib>) is landing and is not yet in <baseRef>"), reuse the existing `everHeld` set so re-holds are silent. The land finishes, master advances, the next tick passes. A refused land flips the latest event to `LandRefused` and the hold lifts into the warn arm below, so a hold can never be permanent.
- **No land in flight** (never requested, or refused): dispatch anyway, add a `Warning` event, and deliver a WhenIdle note to the caller session with the branch, its tip SHA, the commit count above the base, and the sibling's short id: "task <id> branched from <baseRef> without CARD-nnnn's kept branch feat/card-task-<sib> (<tip>, 1 commit: 'docs(plan): ...'). Land <sib> first, or expect its commits to be absent from this branch." Not a refusal: an orchestrator that deliberately superseded a plan must be able to proceed, and a hard refusal on an abandoned branch would block Execute with no verb to clear it.

Put the git probes in `DelegationWorktreeService` (`KeptBranchExistsAsync`, `IsAncestorOfBaseAsync`) next to `GitAsync`; keep the DB query and the hold/warn decision in the dispatcher where the other holds live. Cost is one `show-ref` and one `merge-base` per kept sibling per tick, only for card-bound Worktree tasks, only while they are queued.

**Tests** (a new `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.cs` on the same `ScratchGitRepo` harness as `AgentTaskDispatchFailureTests.cs`):

1. Sibling Succeeded task with a kept branch one commit above master and a `LandRequested` event → the new task is held with exactly one `Held` event, no worktree; append a `Landed` event and fast-forward master to the sibling tip → next tick dispatches and `merge-base --is-ancestor <sibling> HEAD` succeeds inside the new worktree.
2. Same sibling, no land events → dispatched; a `Warning` event naming the branch and tip; a queued WhenIdle message to the caller session.
3. Sibling whose branch was deleted (landed) → no hold, no warning.

## Slice 2 — land-time sibling ancestry in the outcome line

**File:** `server/Application/Services/AgentTaskLandService.cs` (`RunAsync`, after `PrepareLandAsync` succeeds and before `FinalizeLandAsync`), reusing Slice 1's probes.

This is the card's "confirm the plan commit is an ancestor before declaring a card's docs settled" ask, done where §5 of the orchestration loop says the orchestrator's git involvement is zero. For the landing task's kept sibling branches (same predicate as Slice 1) whose tip is not an ancestor of the rebased `HEAD`, add a `Warning` event and append `unlanded-sibling=<sib>:<branch>` (comma-separated if several) to the `Landed` outcome line that is delivered to the caller. Do not refuse: the orchestrator reads the line and lands or drops the sibling. Refusing is the caller's decision to make (see below); the plan defaults to warn.

**Test** (`tests/Antiphon.Tests/Application/AgentTaskLandStageOutcomeTests.cs`): land a build branch while a same-card Succeeded task's kept branch is not an ancestor → `Landed` outcome contains `unlanded-sibling=` and a `Warning` event exists; land the sibling first → no marker, no warning.

## Slice 3 — pin the base rule and say it in the docs

**Files:** `tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs`, `docs/orchestration-loop.md` (§1 cycle and §5 landing), `docs/session-runtime-invariants.md` (one bullet next to the CARD-0221/CARD-0308 worktree bullets).

- A characterisation test next to the existing "child branches from `feat/parent`" case: two top-level Worktree tasks on the same card with no merge target both branch from the repo HEAD; after the first commits, the second's HEAD does not contain that commit. This is the test that would have failed the card's premise on 2026-08-26 and it names the mechanism for the next reader.
- Docs: one paragraph stating that a Worktree task branches from its merge target or master HEAD and never from a sibling task's branch; that a plan must be landed before Execute is dispatched; that the dispatcher holds Execute while the plan's land is in flight and warns when the plan branch is simply not landed; and that a `Landed` line carrying `unlanded-sibling=` means a same-card branch is still stranded. Replace the §1 "cherry-pick or copy it onto master" sentence, which predates `-Land` and describes the manual restore this card is about.

## Decisions that are yours

1. **Slice 2: warn or refuse?** Warn is recommended (a superseded plan is a legitimate state, and a refusal needs a "drop that branch" verb the script does not have). Refuse is a one-line change if 4/4 silent losses outweigh that.
2. **Sibling predicate: Succeeded only, or Succeeded + Blocked?** Succeeded + Blocked is recommended: a plan whose land conflicted is Blocked with its branch kept, and it is exactly the branch most likely to be forgotten.
3. **Whether to expose `mergeTargetRef` on `delegate.ps1` at all** (`-BaseOn`). Not recommended now, for the reasons above; keep as a follow-up card if a real need appears.

## Estimate

| Slice | Authoring | Verification floor |
|---|---:|---:|
| 1 — dispatch guard + 3 tests | 60 min | 30 min (targeted dispatcher tests) |
| 2 — land outcome marker + test | 30 min | 15 min |
| 3 — characterisation test + docs | 20 min | 10 min |

Execute-ready on Grok or Codex terra; the git probes and the hold pattern both have in-file precedent to copy.
