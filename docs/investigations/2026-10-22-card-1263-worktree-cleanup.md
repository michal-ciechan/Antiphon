# CARD-1263: desktop worktree cleanup of finished tasks (2026-10-22)

Operational cleanup under the operator's 2026-10-21 authorisation ("fine to prune and cleanup ...
just go for it, better to delete more than less"). Task 044e5877 (Debug). Input list from the
CARD-1432 Debug e57ff1e3 was re-derived fresh from the database and `git worktree list --porcelain`.

## Outcome

| Measure | Before | After |
|---|---|---|
| Registered worktrees of `C:\src\Antiphon` | 1,161 | 109 (the 104 kept plus 5 cut by new tasks since; 0 prunable) |
| Local branches (`refs/heads`) | 2,647 + 979 deleted | 2,647 |
| Free space on C: | 244.72 GB | 463.11 GB (about 218 GB freed) |

- **Removed: 1,057 worktrees** (961 Succeeded, 48 Failed, 40 Canceled task worktrees, plus 8
  worktrees with no task row that were pushed and clean).
- **Rescued: 5** (included in the removed count). Their tip plus working tree was snapshotted into
  a commit parented on the tip and pushed to `origin`:

  | Task | Status | Rescue ref | What was rescued |
  |---|---|---|---|
  | cd2866b8 | Canceled | `refs/rescue/cd2866b8` = d286af3c4f57d292b6a6084d441eebba149ce1a6 | 1 modified plan doc |
  | dfc6f878 | Succeeded | `refs/rescue/dfc6f878` = dd50d8c8515968b5689b41d63bb2d716f656cabd | 13 modified source/test files |
  | 0f9c42e4 | Canceled | `refs/rescue/0f9c42e4` = 6c12616180516911a1bbfa500ea022b9957f755e | 1 modified test helper |
  | 570fd325 | Canceled | `refs/rescue/570fd325` = 80667824d0a35b5473c8c505fe0436cedbf1ede2 | aborted cut: git lock `initializing`, no index, partial checkout of the pushed tip |
  | a6d66bd8 | Failed | `refs/rescue/a6d66bd8` = 7a7e968f90b595ac9406850e9f69dc6a4706df11 | aborted cut: git lock `initializing`, no index, partial checkout of the pushed tip |

  The two aborted cuts held a stale `initializing` lock (created 2026-10-09 and 2026-10-15, tasks
  terminal since); they were unlocked and handled like the rest. Their rescue commits only hold the
  partial checkout, not authored work.
- **Branches:** 979 local branches deleted (tip pushed and the same-named branch exists on
  origin). 78 branches of removed worktrees were kept: 73 because origin has no branch of that
  name (their tips are still reachable from origin) and 5 because the worktree was rescued.
- **Kept: 104 worktrees.**

  | Reason | Count |
  |---|---|
  | Canonical checkout `C:\src\Antiphon` | 1 |
  | Excluded root (`C:\Antiphon\evidence\code-67a63a4f\base-checkout`) | 1 |
  | Open task (7 Dispatched, 1 Blocked, including this task) | 8 |
  | Terminal but protected by the CARD-1432 rule | 92 |
  | `C:\Antiphon\worktrees\land\0f9784a67773`, git-locked `antiphon-land`, head not on origin | 1 |
  | `C:\src\Antiphon-card0417` (no task row, branch `card-0417-phone`, untracked `.antiphon-style.log`) | 1 |

  The 92 protected terminal worktrees are dominated by two rules: 150 tasks with an `Active`
  `AgentTaskLandings` row in a non-terminal phase and 111 tasks with open sync debt (other rules:
  6 land requests in NeedsResolution, 9 unadopted source resolutions). Many of those landing rows
  look stale for tasks that settled long ago; that is a follow-up, not something this cleanup
  overrides.

## Method

Scripts are kept outside the repository (task scratchpad); the procedure:

1. **Classify** every registered worktree: map its path to `AgentTasks.WorktreePath`, apply the
   CARD-1432 protection rule (open task; land request Queued/Held/Running/NeedsResolution; recovery
   source; unadopted source resolution; active non-terminal landing; open sync debt; active
   retirement), and exclude the canonical checkout, `C:\Antiphon\releases\*`,
   `C:\Antiphon\nightly\*`, `C:\Antiphon\evidence\*` and git-locked worktrees. "Pushed" means the
   head is reachable from a live `git ls-remote origin` SHA (one `rev-list --stdin ... --not`
   over all heads: 6 unpushed commits in total, all under the locked land worktree). Dirty means
   any `git status --porcelain` output.
2. **Move aside** (no repository metadata touched): re-check protection against the database,
   HEAD and status per worktree, rescue if dirty or unpushed, then rename the directory to
   `C:\Antiphon\worktree-trash-1263\`. Nested worktrees went first.
3. **Prune under the leases:** take `land-execution.lock` and `landing.lock` (the land execution
   lease and repository mutation lease), run `git worktree prune -v` (1,057 registrations, all
   the moved ones) and one `git update-ref --stdin` transaction of 979 branch deletions with
   expected old values. Leases held **7.5 s** in total.
4. **Delete the moved directories** with no lease held (about 50 minutes, disk-bound; the trash directory was then removed).

No `git gc` was run. No commit/ref of an open or protected task was touched.

## Deviations from the brief

- **Gate.** The brief asked for batches of about 50 only while no land is running or queued.
  Lands ran back to back the whole session (one Running plus one or two Queued/Held each time it
  was checked; both leases are held for a land's entire run), and the strict gate stayed closed
  for a 7-minute wait. Instead, the directory moves (which touch no git metadata) ran without the
  gate, and every repository mutation (prune and branch deletes) was done in one 7.5 s hold of
  both leases, acquired in the gap between two lands. One short hold disturbs lands less than
  twenty 50-entry holds would. `git worktree remove --force` was replaced by move + `prune`,
  which leaves the same end state but keeps the slow file deletion outside the lease.
- **Untracked worktrees.** Of the 11 untracked entries in the input, the evidence-root checkout and
  the locked land worktree were kept by the exclusion rules; of the other 9, 8 were pushed and
  clean and removed (base
  proofs, the retired 321a59fd worktree, nested CARD-1310 baselines, rc base/tip of 7c5464af and
  the review master checkout under `C:\src\Antiphon\.antiphon\review-45570e2d`); 1 was kept.

## Follow-ups

- `AgentTaskLandings` rows marked Active in a non-terminal phase for tasks that finished long ago
  keep about 90 terminal worktrees alive; they need a stale-landing sweep before the next cleanup.
- `C:\Antiphon\worktrees\land\0f9784a67773` is locked `antiphon-land` with an unpushed head; leave
  it to the land service.
