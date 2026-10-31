# CARD-1622 Final Review round 2 (task 951d7ac2)

Subject: `feat/card-task-0a670765` tip `725df8e23963c094b9d651c134d162ab9f766a22`. Code commits: `d9b0d1efe`, `038ab37cd`, `f1909ae8e`, on top of `aa521eb97` / `3eb09210b` (base `2b471b21c`).
Host: server2 runner mirror, Linux. I changed no subject file. All scratch tests, outputs and the base worktree have been deleted.

Verdict: **one defect (D-1, test adequacy)**. The production code is equivalent to the old code on every path I could reach. D-1 and D-2 from round 1 are fixed. The worker's "subtle finding" is confirmed.

## Defects

**D-1: `[Timeout(30_000)]` does not fail a hung cycle walk, so round-1 A-3 is not fixed.**
- Where: `tests/Antiphon.Tests/Application/AgentTaskSubtreeRollupTests.cs:19-23` and `:49`. The chain test (:165) and the fan test (:191) use the same pattern.
- Failure: mutant M4 removes both guards in `AgentTaskCostWalk.AddToAncestors`: `if (!visited.Add(parentId)) return;` (:111) and `if (++seen > familyCount) return;` (:115). I ran only the cycle test.
  - It printed nothing until my outer `timeout 240` killed it at 240 s.
  - A second run with `--output Detailed` and a 120 s cap also printed nothing: no failure and no timeout line.
  - The body is synchronous. The token is checked once, at entry. `Rollup` never sees the token, so TUnit's timeout cannot stop it, and the lane hangs until its row deadline.
- Why: the brief requires that a hung walk FAILS. The worker reports "`[Timeout(30_000)]` (hang cap)" as the fix for A-3, but it does not cap anything.
- Fix:
  - Make the test async and run the walk off the test thread: `var rollup = await Task.Run(() => AgentTaskCostWalk.Rollup(family)).WaitAsync(cancellationToken);`, or use `WaitAsync(TimeSpan.FromSeconds(30))`. Do the same for the `Calculate(family, family)` call.
  - Then re-run M4 and show a red timeout well under 60 s. Optionally apply the same change to the chain test.

## Equivalence with 2b471b21c

**List filter matrix against the old code.** I built a scratch integration test at both base `2b471b21c` (in a detached worktree) and the tip.
- Family: 15 rows, including:
  - a child scoped to project Y, with a land-started grandchild and a Check great-grandchild;
  - a Failed child inside the window, and a Succeeded child outside it, each with children;
  - a Check child with a non-specialist child;
  - a non-land-pending child with a land-pending child;
  - an unscoped child with a Blocked child.
- Matrix: 3 status sets × includeChecks t/f × since none/window × landPending t/f × scope {none, X-exclude, X-include, Y-exclude, only-unscoped}. That is 120 `ListAsync` calls.
- Output: 936 lines (816 item rows with cost, subtree and children, plus 120 excluded-total lines). **The base and the tip are byte-identical.** I checked twice: once on the first tip build and again after restoring from the mutants.
- This confirms the worker's finding. Rows removed by status, specialist, since or land-pending never enter `candidates`, so they do not count toward a parent. They also cut the chain for anything below them: for example, `Lc` under a non-land-pending `L` never reaches root.
  - A scope-excluded row is still in `candidates`, so it still counts. Example: land=True, X-exclude gives root 5.84, which includes the Y-scoped child's 0.40.
- The worker's `list_subtree_cost_keeps_a_scope_excluded_child_and_drops_filtered_rows` also passes **at the base** (1.68, ChildCount 2). That proves it pins the old behaviour.

**Differential fuzz** (seed 951, 20,000 random families of 1-12 rows, ~20% with duplicate ids, plus outside parents, self-parents and cycles). It compares the verbatim 2b471b21c `ToSummary` figures and `Calculate` with the tip. 218,386 cases, **0 mismatches**:
- list path (the same instances, a random subset as items): unique 73,627, duplicate 17,644
- Get/detail/summary path (a separately read caller row with a different cost): unique 16,404, duplicate 3,596. `CostUsd` always equals the caller's row.
- `Calculate` with mixed instances, clones and an out-of-family subject: unique 72,179, duplicate 14,936; key sets 20,000/0.

**The caller-row adjustment is now identical:**
- `ToSummary` (`AgentTaskService.cs:4752-4754`) uses `figures.SubtreeCostUsd + (task.CostUsd - figures.StoredCostUsd)`.
- `Calculate` uses `row.SubtreeCostUsd + (task.CostUsd - stored.CostUsd)`.
- `StoredCostUsd` is the first row's cost, which is the same row `byId` keeps.
- The worker's two baselines match the old walk: 3 is now 7, and the duplicate root 105 is now 107.

## Advisories

- **A-1: the missing-id fallback differs between `ToSummary` and `Calculate`.**
  - When the caller's id is absent from `family`, `ToSummary` returns its own cost and 0 children. The old code, and `Calculate` (which keeps its full-walk fallback), also counted family rows parented to that id. The fuzz category for this case had 5,424 mismatches in 20,000.
  - Every `ToSummaryAsync` and `GetSummaryAsync` caller loads `family` by `RootTaskId == task.RootTaskId`, and `ListAsync` items are a subset of `candidates`. The case is therefore unreachable today.
  - Optional fix: route the miss through the same fallback `Calculate` uses.
- **A-2: the fan bound is a 12× ratio plus a 1 s floor, not a measured p95.** The 1 s floor dominates. Round 1 measured about 34 ms for 10,000 on loaded server2, so the bound has about 30× headroom and will not flake. A quadratic regression (old cost about 1.9 s at 2,000 rows) would still go red.

## Mutants (one build-slot lease per build, each restored with `git checkout`)

| # | mutant | result |
|---|---|---|
| M1 | `ToSummary` drops the caller-row adjustment | RED: `ToSummary_uses_the_caller_row_cost...` expected 7m, got 3m; 5 tests, 1 failed |
| M2 | duplicate id: the last row's cost wins (`costs`/`ownCosts` overwritten) | RED: duplicate-id test expected 5m, got 6m; 5 tests, 1 failed |
| M3 | drop the `parentId != origin.Id` guard | RED: duplicate-id test and cycle test expected 5m, got 7m; 5 tests, 2 failed |
| M4 | drop the visited guard and the step bound | **HANG**: no result in 240 s or 120 s (D-1) |
| M5 | child count includes a self-parent (`task.Id != parentId` removed) | RED: cycle test expected 0, got 1; 5 tests, 1 failed |

Restored tree, rebuilt: rollup class 5/5, fuzz 1/1, integration 3/3. The matrix is identical again.

## Runs (TEMP=/tmp, `UseAppHost=false`, output `bin-rev951/`, every build and run through `scripts/build-slot.ps1`)

- `AgentTaskSubtreeRollupTests/*`: 5 tests, 0 failed (tip and restored)
- `AgentTaskServiceIntegrationTests/(subtree_cost_rolls_up*)|(list_subtree_cost*)|(Rev951*)`: 3 tests, 0 failed (tip, restored, and base with the worker's witness)
- `AttentionServiceTests/a_blocked_task_is_listed_as_needing_an_answer_and_offers_reply`: 1 test, 0 failed
- `CardThreadServiceTests/A_settled_task_carries_its_report_and_the_spend_of_everything_under_it`: 1 test, 0 failed
- `AwayDigestProjectionTests/Cost_walk_rolls_children_into_their_root_once`: 1 test, 0 failed
- `scripts/audit-land-verification-graph.ps1 -Check` (tool built Release to `bin-land-audit`): exit 0, `approved`, digest=7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99, count=68, canary removed. No `-Update`.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 2b471b21c -HeadRef 725df8e23`: commits=7, entries=2, violations=0, exit 0.
- `git merge-tree --write-tree origin/master 725df8e23` (origin/master 3cd78d7ebad78ff3b8d17c4fc504521851e4eb4f): exit 0, tree 36bb922fe1f56ebd9e51b0dea74e979e4548b64a, no conflicts. The base is an ancestor of origin/master.
- I did not rerun the Unit lane. The diff touches two server service files (pure rollup logic) and Unit/integration tests. It changes no policy, script, project file or NativeLinux marker. The land graph is unchanged (68).
- N/A: OS-branch witnesses (no OS-selected behaviour) and queue, receipt and input-wait audits (no delivery or wait path changed).
