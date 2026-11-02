# task-13fb02d0: Review round 1 (diff-only), CARD-1640

Subject: feat/card-task-db2df8ea. Reviewed range: 3a49b60133327f4441f06698ef3369b1d277d049..4b945e6fb550a5165e3cfc4623e109688dda3396 (tested source). The report commit 0c71d13c5268883ac2fa83e41f16dbd14d742654 was also checked. Landing owner: db2df8ea.
Verdict: **1 defect (D-1)**: a new silent fallback to the target through a stale remote-tracking ref. Everything else checked out.

## D-1: a stale `refs/remotes/origin/<b>` is trusted as the sibling tip, so Auto falls silently to the target

- **Where:** `server/Application/Services/AgentTaskWorktreeBaseResolver.cs:136-144`, which reads `RemoteBranchCommitsAsync` and writes the result into `commits` with no freshness check. Its effects show at `:168-173` (the pending-land wait) and `:259-270` (Contained, then skip). The create fetch at `AgentTaskService.cs:1881` runs only for `source_unreadable`, so it never runs in this case.
- **Failure:** the reviewer probe below seeds a sibling with a local commit A, sets `refs/remotes/origin/<b>` to the master SHA, then deletes the local branch.
  - Base: `decision=Target`, warning "has no local commit".
  - Tip: `decision=Target reason=<none>`. The only warning is `Branch <b> has no local branch here; using origin/<b> @ <master sha>.` Commit A is dropped as Contained, and nothing names it.
  - The same stale SHA also defeats the pending-land wait. Before this change, a pending-land sibling with no local branch returned `WaitForLand/pending_land_unknown`. Now a stale tracking SHA that the target contains removes it from `pendingLand`, and dispatch proceeds from the target while the land is pending.
- **Why it is reachable:** `RemoteWorkspaceService.PushBranchAsync` (`RemoteWorkspaceService.cs:119-121`) runs `git push origin <branch>` from the desktop worktree at dispatch, which is at its base SHA. That push sets the server repo's `refs/remotes/origin/<b>` to the base. The runner then pushes its real commits from server2, and nothing in the server repo fetches that tracking ref again: the settlement and progress fetches write to `refs/antiphon` pins. So for every runner task, the tracking ref is stale at the base, and the base is usually contained in the target. This is the "exists only under refs/remotes/origin" case that CARD-1640 names, and the operator's preferred topology is server2.
- **Fix:**
  - Do not treat a tracking-only tip as the sibling's source unless it is proven current.
  - At create, fetch whenever the local branch is missing, even when a tracking ref exists, and resolve again from the fetched tip.
  - At dispatch, accept a tracking-only tip only when it equals a settled source SHA the board recorded (for example a sync-debt `SourceSha` or the settlement head). Otherwise return `source_unreadable`, which blocks.
  - Do not apply the remote read to pending-land branches: keep `WaitForLand`.
  - Add a witness: tracking ref at the base, real commit on origin only. It must not return Target at dispatch, and it must continue from the real tip at create.

Reviewer probe (uncommitted; it was removed afterwards): `AgentTaskWorktreeBaseResolverTests.R13FB_stale_tracking_ref_at_the_base_is_not_a_silent_target`. It asserts `auto.Decision != Target`. It was red at the base and red at the tip.

## Priorities from the brief

1. **Silent fallback.** The residual is broader than "a whole-inspection git error", and D-1 is a new path. The dispatcher (`AgentTaskDispatcher.cs:5890-5896`) blocks an Auto Unknown only when SyncUnverified is true, or when the Review role or a previewed source is involved. Every `Incomplete` reason (`inspection_timeout`, `candidate_limit`, `git_command_limit`, `git_inspection_error`) therefore still dispatches a non-Review Auto task with no preview from the target. A `source_unreadable` result never gets a preview, because create refuses it.
   - Reachability: `git_inspection_error` is rare in practice (a corrupt repository, `for-each-ref` exit not 0/1, a CommonAsync failure on the task's own RepoPath). The limit reasons are more reachable, and the new remote `for-each-ref` costs one more command against the budget.
   - Pre-existing row skips that also fall to the target: sibling repository unavailable (`:224-229`), containment Unknown (`:272-276`), open writer (`:284-291`), dirty tip. These were not introduced here.
   - `AgentTaskDispatcher.cs` is untouched in the range (confirmed from the diff stat).
2. **No new harm** (D-1 aside):
   - Landed sibling: excluded through the Landed/LandedWithResidue events. No refusal.
   - Failed or Canceled sibling: not inspectable, warning only. No refusal.
   - Branch deleted after landing: excluded by the landed set. No refusal.
   - Review, Merge or Docs dispatch: same rule. A Review blocks instead of reviewing the target, which is correct. Review and Docs siblings were already sources before this change.
   - Card with no sibling: early Target, unchanged.
   - `-FreshWorktree`: Target (witnessed in V08).
   - `-StartRef`: early Target. `-BaseTask`: unchanged.
   - Orchestrator (not a Worktree task): early Target.
   - Handoff create: refuses with 409 `worktree_base_unreadable` and does not fetch, the same as Ambiguous.
   - The worker's behaviour change is confirmed. A Succeeded, unlanded sibling whose branch was deleted, with no Landed event and no origin copy, refuses Auto creates and blocks Auto dispatches. The message is actionable: it names the branch, says to sync or push, and offers `-StartRef` and `-FreshWorktree`. `-FreshWorktree` is the only remedy that works when there is no origin copy.
3. **The fetch at create.**
   - Each branch is bounded: the lease poll and the fetch share one 30s stopwatch, and each of the three local git calls is limited to 30s. It runs under the start-ref repository mutation lease.
   - No ref injection. The branch name comes from the board's `WorktreeBranch` and must pass `check-ref-format refs/heads/<b>`, which rejects `:`, `..`, spaces and glob characters. A leading `-` is refused explicitly. The refspec starts with `+` and the remote is the literal `origin`. No `--` separator is used, but none is needed.
   - No new credential exposure: `Diagnostic` is git's own stderr, the same as the existing `-StartRef` path.
4. **Tests.**
   - T0442_V08 and the two C1349_ContainedInventory rows are not weakened. Each now asserts the new decision, reason and command counts, and the V08 fresh assertions were added.
   - `a_sibling_whose_branch_was_deleted_is_silent` is byte-identical at base and tip: the method-body sha256 is 0ad5cc0d… in both.
   - At the base source with the tip tests, all 8 C1640-related witnesses were red, each on its decision or throw assertion: V08 remote_only, missing_local_branch and git_error; C1640 create on_origin and nowhere; C1640 dispatch; C1349 absent local ref and prefix-neighbor ref. All 8 were green at the tip.
   - C508_GuardRefMismatchWarnedOnce: 4/4 at the base, 4/4 in the tip class run, and 12/12 over 3 tip reruns. It is a load flake, not a regression: the (0,True) case has no siblings and returns at `:116`, before any changed code.
5. **Gates.**
   - Narrow rerun at the tip, over the six classes plus the probe: 247 total, 246 passed, 1 failed (the probe only), 0 skipped. The six classes themselves are 246/246.
   - Land graph `-Check`: approved, count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE.
   - check-evidence-diff: 3a49b601..4b945e6f gives commits=3, entries=0, violations=0. ..0c71d13c gives commits=4, entries=1, violations=0.
   - merge-tree: clean onto origin/master (3a49b601) and onto origin/rc/2026-11-01 (e114313d). The change is master-only.

## Advisories
- A-1: create has no overall network deadline. It loops over up to 256 unreadable branches, each with its own 30s budget, against the start-ref "one deadline for all network work" design. Origin down with k unreadable siblings means about k×30s per create.
- A-2: when the lease is busy, the fetch failure text reuses the start-ref message ("Start ref 'refs/remotes/origin/<b>' … The task was not created; retry").
- A-3: the C1349 prefix-neighbor rewrite returns a `refs/heads/...` neighbor for the remote query too. The remote read's own prefix-neighbor filter (`refs/remotes/origin/<b>/x`) therefore has no witness.
- A-4: the dispatcher residual (Incomplete reasons with no preview leading to Target) is a follow-up card candidate after CARD-0824 S1, as the worker said.

## Runs (Linux server2; every run through build-slot, native output, UseAppHost=false, TEMP=/tmp; no checkpoint receipts)
| run | source | slot lease / waited | result |
|---|---|---|---|
| build | server at 3a49b601, tests at the tip plus the probe | 2c4141a8 / 0s | ok |
| base rows | V08*, C1640*, C1349_ContainedInventory*, deleted-sibling, C508*, R13FB* | b102574c / 375s | 21 total, 9 failed (8 witnesses + probe), 12 passed |
| build | tip 4b945e6f plus the probe | 8e48ba77 / 210s | ok |
| tip classes | the six classes | 77096119 / 0s | 247 total, 246 passed, 1 failed (probe), 0 skipped |
| C508 x3 | tip | 19cb8a8f / 0s | 12/12 |
| land audit build + -Check | tip | f0801985 / 0s | approved; bin-land-audit deleted |

Unlisted runs: none. The whole Unit lane was not run, because the brief commissioned only the narrow rerun under the operator's minimal-testing rule, so ordinaryScopeCompleted is Interim. PCs a-f from the Code report stay pending, and PC-b (the remote read) should be redesigned under D-1.
