# CARD-0613 — the progress detector settled a task Failed using an "unrelated sibling's" worktreeBaseSha

Date: 2026-09-23. Stage: Investigate. Repo: Antiphon. Evidence: local Postgres (`antiphon-postgres`,
db `antiphon`), the git object graph in `C:\src\Antiphon`, and a synthetic git reproduction.

## Outcome

**Root cause confirmed — and it is not the one the card names.** `WorktreeBaseSha` is recorded
correctly on every task examined. The false-`Failed` settlements come from *free-text
`git checkout -B feat/card-task-<other>` continuation instructions in the task Goal*, which move the
worktree's HEAD off the branch the progress baseline pinned. The detector then short-circuits to
`no_movement` (or, when the own-branch ref was force-moved into the sibling's lineage,
`unclaimed_or_unmatched_commit`) without ever consulting the file probe or the delegate's progress
claim.

**14 of the 18** `CompletedWithoutProgress` settlements since 2026-09-21 are demonstrably false:
real commits exist inside each task's dispatch-to-completion window. **18 of 18** carry a
`git checkout -B feat/card-task-...` instruction; **zero** tasks *without* that instruction settled
`CompletedWithoutProgress` in the same period.

## 1. Where `WorktreeBaseSha` is set

Two write sites, both in `server/Application/Services/DelegationWorktreeService.cs`:

- `DelegationWorktreeService.cs:282` — verification snapshot path (`SourceLandingOperationId` set):
  `task.WorktreeBaseSha = source.VerifiedSourceSha`.
- `DelegationWorktreeService.cs:325` — the ordinary path: `task.WorktreeBaseSha = head`, where
  `head = await _gitWorkspace.GetHeadShaAsync(info.Path, ct)` (`:321`) — the HEAD of the worktree
  directory `IWorktreeManager.CreateAsync` just produced (`:298`). Guarded by `if (!alreadyRecorded)`
  (`:320`), so a reuse never relabels.

The base *ref* comes from `WorktreeBaseResolver.Resolve(startAtSha, task.WorktreeBaseRequestedRef,
task.MergeTargetRef, probe)` (`DelegationWorktreeService.cs:289-290`,
`server/Application/Services/WorktreeBaseResolver.cs:32-48`) with precedence
Repair, Explicit, MergeTarget, DefaultBranch, RepoHead.

The baseline is snapshotted at dispatch into `ProgressBaselineJson` and pins the *full ref name*
alongside the sha. For task `4207f7a8`:

```json
{"schemaVersion":1,"capturedAt":"2026-09-22T23:55:33.9003737Z",
 "primary":{"registeredCheckout":"C:\\Antiphon\\worktrees\\card-task-4207f7a8",
            "fullRef":"refs/heads/feat/card-task-4207f7a8",
            "localSha":"bb89e77b4dcc30feb809e1549d62653890a0311a"}}
```

### The card's premise is falsified

`bb89e77b` is **not** "an unrelated sibling task's branch tip". It is a commit on `master`
(`test(CARD-0607): wait for the tailer's initial read before the exclusive lock`), i.e. master's tip
at 2026-09-22 23:55 when task `4207f7a8`'s worktree was created. Checked across **all 18**
`CompletedWithoutProgress` tasks since 2026-09-21: every recorded `WorktreeBaseSha` is an ancestor of
`master`, every one has `WorktreeBaseRef = 'master'` and `WorktreeBaseSource = 5` (`DefaultBranch`).
No task had a base sha taken from a sibling branch.

## 2. The actual mechanism

`4207f7a8`'s Goal (verbatim opening, from `AgentTasks.Goal`):

> Continue on branch feat/card-task-d2eb7b57 at 965703e65aa4571a4e1cce7ff2dd68e3f98af14e
> (git fetch origin feat/card-task-d2eb7b57 && git checkout -B feat/card-task-d2eb7b57
> 965703e65aa4571a4e1cce7ff2dd68e3f98af14e as first step, ...)

The server provisioned `4207f7a8` as an ordinary Worktree task: its own worktree at
`C:\Antiphon\worktrees\card-task-4207f7a8`, its own branch `feat/card-task-4207f7a8`, based at
master `bb89e77b`. The delegate then obeyed the Goal and re-pointed HEAD at the sibling branch, where
it committed `8bd43dd5`.

Consequences in `server/Application/Services/TaskCompletionProgressService.cs`:

- `:281` `var primaryOnExpectedBranch = isRepair || string.Equals(symbolic.FullRef, source.FullRef, ...)`
  — now **false** (`refs/heads/feat/card-task-d2eb7b57` is not `refs/heads/feat/card-task-4207f7a8`).
- `:293` the early file-probe rescue `if (!isRepair && filePositive && primaryOnExpectedBranch)` is
  gated off.
- `:305-306` `else if (!primaryOnExpectedBranch) result = Arm(..., NoAttributedProgress, "no_movement", ...)`
  — the verdict is reached **without** calling `EvaluatePrimaryGraphAsync`, and therefore **without
  ever looking at the delegate's `[antiphon-progress:<taskId> <sha>]` claim**. The documented escape
  hatch (`TaskCompletionProgressService.ParseClaim`, `:57-95`) cannot rescue an off-branch delegate.

Stored verdict for `4207f7a8` (`CompletionProgressEvidenceJson`):

```json
{"assessment":"NoAttributedProgress","reason":"no_movement",
 "sources":[{"origin":"Primary","localObserved":"bb89e77b4dcc30feb809e1549d62653890a0311a",
             "registeredPath":"C:\\Antiphon\\worktrees\\card-task-4207f7a8","reason":"no_movement"}]}
```

`FailureReason`: *"The delegate reported completion but Antiphon observed no attributable
post-dispatch progress at C:\Antiphon\worktrees\card-task-4207f7a8. no_movement."*

### Reproduced

Synthetic repo: master advanced past a divergent sibling branch, a worktree created at master, then
the Goal's instruction executed inside it.

```
baseline: fullRef=refs/heads/feat/card-task-MINE localSha=fe391483
  git checkout -B feat/card-task-SIB <sib-sha>; <commit real work>
symbolic-ref now : refs/heads/feat/card-task-SIB      <- != pinned fullRef  => :305 no_movement
own branch tip   : fe391483  (== base fe391483)       <- never moved
actual output    : c3c7e927
base ancestor-of output: NO                           <- the card's merge-base observation
```

The `merge-base --is-ancestor` failure the card reports is a **symptom** of the branch swap, not
evidence of a mis-set base: the worktree was created off master, the output commit descends from a
branch that diverged from master earlier, so master's tip is naturally not its ancestor.

### The second arm: `unclaimed_or_unmatched_commit`

Seven of the eighteen settled with this reason instead. For these the worktree's **own** ref was
force-moved into the sibling lineage (a `checkout -B` that git refuses when the sibling branch is
checked out in a live worktree, followed by a reset or `branch -f` fallback), so `symbolic.FullRef`
still matched and evaluation reached `EvaluatePrimaryGraphAsync` (`:331-372`).
`IsNovelCommitAsync` (`:455-466`) calls a tip novel only when `source.LocalSha` is an ancestor of it
— false here — so with no claim line it falls to `:361` / `:369` `unclaimed_or_unmatched_commit`.
Verified with `git merge-base --is-ancestor`:

| task | base | observed tip | base ancestor of tip |
|---|---|---|---|
| 03d3ee30 | ddb5d0f0 | 237bd63a | NO |
| b03a74a0 | f0972c42 | 2c80c976 | NO |
| a6276b12 | f0972c42 | 22f19c54 | NO |
| 62d06f4f | f0972c42 | abc7b90d | NO |
| 78859af8 | 5ace6799 | 85c4f5ce | NO |
| dae3ad6b | f0972c42 | 234bca28 | NO |
| e32907bf | f0972c42 | ff8906b0 | NO |

Every one of those observed tips is a real work commit made during the task's window.

## 3. Blast radius

`AgentTasks` where `Workspace = Worktree` and `CreatedAt > 2026-09-21`, grouped by whether the Goal
contains `git checkout -B feat/card-task`:

| Status | FailureCode | has checkout-B instruction | count |
|---|---|---|---|
| Dispatched | | no | 1 |
| Dispatched | | yes | 1 |
| Succeeded | | no | 17 |
| Succeeded | | yes | 34 |
| **Failed** | **2 (CompletedWithoutProgress)** | **yes** | **18** |
| Failed | (other) | yes | 5 |
| Canceled | | no | 3 |
| Canceled | | yes | 1 |

- **18/18** `CompletedWithoutProgress` settlements carry the instruction. **0** occur without it.
- Among continuation dispatches that reached a terminal verdict: 34 Succeeded vs 18 false-Failed —
  a **~34% false-Failed rate** on this dispatch shape.

### Per-task ground truth (all 18)

`base` = `WorktreeBaseSha`; "own tip at settle" is `localObserved` from the stored evidence.

| task | reason | base | own tip at settle | real work found | verdict |
|---|---|---|---|---|---|
| 5782b48f | no_movement | 8c2be396 | = base | 4 commits on `feat/card-task-b1638e39` in window | FALSE |
| bf1620ec | no_movement | a0fc9fab | = base | 3 commits on `feat/card-task-e55e2313` in window | FALSE |
| c0219455 | no_movement | a0fc9fab | = base | 1 commit on `feat/card-task-5c627d8e` in window | FALSE |
| 6e0f42c5 | no_movement | 8c2be396 | = base | 3 commits on `feat/card-task-49545b22` in window | FALSE |
| dae3ad6b | unclaimed | f0972c42 | 234bca28 | observed tip is real work | FALSE |
| e32907bf | unclaimed | f0972c42 | ff8906b0 | observed tip is real work | FALSE |
| 6409a3c7 | no_movement | f0972c42 | = base | target branch still at instructed sha | unproven |
| 9156f49a | no_movement | f0972c42 | = base | target branch still at instructed sha | unproven |
| b03a74a0 | unclaimed | f0972c42 | 2c80c976 | observed tip is real work | FALSE |
| a6276b12 | unclaimed | f0972c42 | 22f19c54 | observed tip is real work | FALSE |
| 62d06f4f | unclaimed | f0972c42 | abc7b90d | observed tip is real work | FALSE |
| b976e9ae | no_movement | d0ab05f7 | = base | 4 commits on `feat/card-task-3986b2f9` in window | FALSE |
| 78859af8 | unclaimed | 5ace6799 | 85c4f5ce | observed tip is real work | FALSE |
| ca4e3576 | no_movement | 5ace6799 | = base | target branch still at instructed sha | unproven |
| cbb34c38 | no_movement | 5ace6799 | = base | target `feat/card-task-375526ed` +33 commits, unattributed | unproven |
| 4207f7a8 | no_movement | bb89e77b | = base | `8bd43dd5` on `feat/card-task-d2eb7b57` | FALSE |
| 0f4d1ab6 | no_movement | bb89e77b | = base | `81fbd186` committed 00:53:45Z, settled 00:54:50Z | FALSE |
| 03d3ee30 | unclaimed | ddb5d0f0 | 237bd63a | observed tip is real work | FALSE |

**14 confirmed false-Failed, 4 unproven** — no work was located for those four, so their verdict may
be correct, but it was reached by the same blind path either way.

### Why the orchestrator writes these instructions

There is no supported parameter for "start this task's worktree at branch X / sha Y":

- `WorktreeBaseRequestedRef` exists on the entity (`server/Domain/Entities/AgentTask.cs:225`) and is
  read by the resolver (`DelegationWorktreeService.cs:290`), but **no code path ever writes it** — it
  is absent from `CreateAgentTaskRequest` (`server/Application/Dtos/AgentTaskDtos.cs`) and from
  `scripts/delegate.ps1`. The `WorktreeBaseSource.Explicit` arm is dead.
- The only real levers are `MergeTargetRef` and `-RepairSource <taskId>`
  (`scripts/delegate.ps1:76, 925` → `RepairSourceTaskId`), the latter adding a second `RepairSource`
  progress arm. All 18 tasks used neither (`WorktreeBaseSource = 5 DefaultBranch`).

So the free-text `git checkout -B` idiom is a workaround for a missing dispatch primitive, and it is
documented nowhere: `grep -rn "checkout -B feat/card-task" --include=*.md --include=*.ps1` matches
nothing outside generated `docs/cards/`.

### Relation to CARD-0603

Consistent with it and probably upstream of much of it: a task settled `Failed` this way cannot land
its branch, which is the landing deadlock CARD-0603 describes being worked around repeatedly. Not
proven here that every CARD-0603 workaround traces to this — the causal link is the shared `Failed`
state, and that link was not measured task by task.

## Remaining uncertainties

1. The exact git command sequence behind the `unclaimed_or_unmatched_commit` arm (own ref moved into
   sibling lineage) is inferred from the object graph, not recovered from delegate transcripts.
   `git checkout -B` refusing a branch already checked out in a live worktree is the most likely
   trigger for a reset fallback, but that was not read from a transcript.
2. The four "unproven" tasks: no work was located on the instructed target branch. Their transcripts
   were not read.
3. `0f4d1ab6`'s own branch now points at `81fbd186` although the settlement snapshot read the base
   sha 65 s after that commit was made; the ref must have been moved afterwards. Who moved it was not
   traced.

## Not done, noted

Fix idea (not designed, not implemented): give dispatch a real "start at ref/sha" primitive — wire
`WorktreeBaseRequestedRef` through `CreateAgentTaskRequest` and `delegate.ps1` so continuations stop
being free text; and stop the detector short-circuiting — at `TaskCompletionProgressService.cs:305`
an off-branch HEAD should fall through to claim qualification and the file probe rather than assert
`no_movement`, and `IsNovelCommitAsync` should be able to attribute a commit made after the baseline
timestamp even when the baseline sha is not its ancestor.
