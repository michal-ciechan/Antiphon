# CARD-1480 S1 Final Review (task 0c944fa1) of Code task 2f53685f

Reviewed `feat/card-task-2f53685f` at **be783f4e177900d506f62e667fa13845911f9989** (base 0ea49a7cc).
Verdict: clean. No regression, no reachable stale-base build, no new block without a debt, no vacuous test.

Refinement received mid-review: the operator dropped the Unit lane. The extra RV-U run
`20261022-134553-d994` was stopped with the tool's `stop` verb before its build started, and its
scratch worktree was removed. Scope is CP-2..CP-4 plus one mutation per decision.

## Receipt (checkpoint tool, server2, run 20261022-134500-96ce, verdict GREEN exit=0)

```
CHECKPOINT CP-2 commit=be783f4e1... build=ok executed=13 passed=13 failed=0 skipped=0 slot=granted waited=195s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=be783f4e1... build=ok executed=122 passed=122 failed=0 skipped=0 slot=granted waited=360s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=be783f4e1... build=ok executed=96 passed=96 failed=0 skipped=0 slot=granted waited=30s dirty=0 sourceState=clean buildSource=verified
```

`scripts/validate-checkpoint-receipt.ps1 -Rows CP-2,CP-3,CP-4` and the tool's `validate` both print
`CHECKPOINT SOURCE VALID source=be783f4e177900d506f62e667fa13845911f9989 rows=3`. CP-1, the red row,
was not re-run, as the brief directs. The tip is the tested source: c88082a68..be783f4e1 changes only
`.antiphon/task-2f53685f.md`. The CP-2..CP-4 run at 268695d5 was a build failure and ran no tests.

## Mutations (detached scratch worktree at be783f4e1, build-slot gated, discarded)

| Decision | Mutation | Result |
|---|---|---|
| D-1 substitution | comment out `commits[branch] = settled;` | V-1 (Decision), V-2 (SourceSha), V-4 (Decision) and V-5 (WorktreeBaseSha) red. V-3, V-7 and V-8 stay green. |
| D-4 block | dispatcher `selected.SyncUnverified` -> `false` (same build as D-1; different file and method) | V-6 Code and V-6 Review red on `blocked.Status`. |
| D-3/D-5 identity | inventory FullRef compared with `branch + "-MUT3"` (debts read for the wrong branch) | V-3: all 3 cases red on `selected.Decision`. |

Every failure is a Shouldly assertion. None is a build error or a zero count.

## Mode x debt matrix (code reading at be783f4e1)

The rows are Auto, Task, StartRef and fresh mode.

- **No debt:** the inventory returns an empty map, `unverified` is empty and the code path is unchanged. CP-3 and CP-4 confirm this (122 and 96 passing).
- **Pending, S a fast-forward of L:** `commits[b]=S`, and the merged map still holds B, so `IsMerged` is false and containment of S decides.
  - Auto -> `Continue S` (V-1). Pending land -> `WaitForLand pending_land @S` (V-4).
  - Task -> `Continue S` (V-2). Fresh -> `Target`, and the warning names S.
- **S absent, diverged or not a commit:** the row is unverified.
  - Auto -> `Unknown source_sync_unverified`, and the dispatcher Blocks it (V-3, V-6).
  - Task on the requested row -> `Unknown`. The dispatcher Blocks, and create refuses `worktree_base_source_invalid`.
  - Task on another row -> warning only; `explicitSource` is the requested row, so the stale tip is never used.
  - Pending land -> `WaitForLand pending_land_unknown`.
  - Fresh -> `Target` plus a warning.
- **Held or Superseded:** handled the same as Pending (state filter `!= Ready`).
- **Ready, same tip, prior attempt:** no change (V-8, mutation-sensitive).
- **Debt for another branch:** ignored by the FullRef filter.
- **StartRef:** returns `Target()` at :82-85 before the inventory, with zero git commands (V-7).

Safety direction:
- The resolver has exactly one dispatcher call site (`AgentTaskDispatcher.cs:5757`) and there is exactly one cut site (`CreateForTaskAsync`, :5789).
- `SyncUnverified` blocks there in every mode, whatever the role or preview.
- Retries, reroutes, pool placements and pinned agents all reach this one claim path.
- The resolver is skipped only for:
  - adoption of the task's own kept branch,
  - a repair start,
  - Target mode without a preview.

  None of these inherits a sibling tip.
- The other two inventory callers (dispatcher sibling warnings :5533 and `CardSiblingInspectionService`:136) are `warningsOnly=true`. They skip the debt read and are unchanged.

Bounded query: one read, `TaskId IN (ids) AND State != Ready`. The ids are capped by `MaxInventoryRows+1` plus pending rows. The unique index on (TaskId, Attempt) makes one debt per attempt, so the "newest unpaid" reduction cannot pick a stale row over a paid one. `byId[d.TaskId]` cannot miss, and `IsFull(SourceSha)` guards the `!`. There is no N+1.

The two Code choices are both fail-closed:
- A debt with no local branch blocks Auto. Before the fix that case dispatched from the target.
- In Task mode a non-requested unverifiable row only warns, but the build uses only the requested row.

## Disclosures (not defects under the standing verdict policy)

1. **rc port will not compile as cherry-picked.** All three commits apply cleanly onto
   `origin/rc/2026-10-20` ceb6e3d6b, including `slow-tests-allowlist.txt` (checked in a scratch worktree, then discarded).
   However, `AgentTaskWorktreeBaseSyncDebtTests.cs` uses `[ParallelLimiter<GitProcessSpawnLimit>]`.
   That type comes from CARD-1421 `cc4a5564d` and does not exist on rc. The rc siblings `AgentTaskWorktreeBaseResolverTests`
   and `AgentTaskDispatchBaseGuardTests` use `ProcessSpawnLimit`. The rc pick therefore needs a one-line swap to
   `ProcessSpawnLimit` in that test file (or cc4a5564d), and an rc build of CP-2. The Code report's
   "helpers exist on rc" missed this.
2. **Held and Superseded debts are terminal.** Held only restamps, and Superseded means the registration is gone. A Succeeded,
   unlanded sibling with such a debt and an unverifiable S therefore blocks every later same-card Auto dispatch until
   one of these happens:
   - the sibling lands,
   - someone syncs the branch to S,
   - the caller uses `-StartRef`, `-FreshWorktree` or `-BaseTask <other>`.

   This is fail-closed by plan D-2/D-5, and the Block text names the remedies.
3. **Residue that predates this change.** If git fails or hits a limit inside the new verify probes, the result is `Unknown git_inspection_error/...` (Incomplete). For an Auto non-Review task with no previewed Continue source, that still falls through to the target. This is the resolver's existing policy for any inspection error, and before this change the same debt case went to the target silently anyway.
4. **Test gap, not vacuity.** No test seeds a debt whose FullRef names another branch, so deleting the FullRef
   predicate entirely would stay green. The prior-attempt and Ready filters are pinned.

## Landability

- `git merge-tree --write-tree origin/master(9cf77dbad) be783f4e1`: clean.
- `scripts/check-evidence-diff.ps1 -BaseRef 0ea49a7cc -HeadRef be783f4e1`: commits=5, violations=0.
- Cleanup: `bin-c1480rv` deleted; both scratch worktrees removed. The checkpoint tool removed its own row outputs.

PCs: the plan names none. Every PC stays pending for SourceLanding Mutation.
