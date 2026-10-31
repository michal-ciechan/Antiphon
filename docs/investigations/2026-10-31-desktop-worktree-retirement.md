# Desktop worktree retirement - 2026-10-31

Partial completion for task `2443d3d5`: **249 old worktree directories retired; 654 remain eligible and one unregistered directory remains after a Git failure.** Full cleanup did not fit the 120-minute execution window; deletions stopped to complete verification and reporting.

Used the operator's standing data-loss authority without requesting further approval. Dirty, unpushed and ignored contents were not retention reasons. Each removal used an exact literal Git path under `C:\Antiphon\worktrees`; cleanup deleted no branches or objects and changed no repository configuration, journal or stale lock. Excluded release/nightly/evidence/scratch directories were not modified. The report is the only repository file changed. All API calls were GETs. The release API records intent rather than deleting; verification cleanup is scoped to sourced Mutation tasks, so neither was used.

Eligibility used the newest mtime of the directory, its `.git` file and administrative `logs/HEAD`, strictly older than 24 hours. Task states were refreshed through `agent-tasks?since=...` and individual task GETs; active tasks, linked live/unreleased sessions, locks, uncertain identities and this task were excluded. Final inventory cut: `2026-10-31T11:25:37.692648+00:00`; zero task API inspection errors.

| Measurement | Before | After |
|---|---:|---:|
| Registered worktrees | 1,050 | 812 |
| Drive C free, decimal GB | 238.44 | 306.88 |
| `git worktree list`, seconds | 11.310 | 3.765 |
| Detached full-checkout add, seconds | 40.350 (earlier audit) | 13.999 |

Net free-space increase: **68.43 GB**. Removed logical contents were **64.58 GiB** in the earlier audit. Concurrent host activity and changed load/cache limit attribution of space and timing changes. Fifteen registrations appeared and two additional registrations disappeared concurrently. Our ledger accounts for 249 successful directory removals, one failed removal whose registration disappeared, and one explicitly pruned missing-directory registration; these reconcile 1,050 to 812.

Final registered worktrees kept: **133 younger than 24 hours, 8 active/live-session consumers, 1 own task, 3 locked, 1 unknown task and 12 outside scope**. Another **654 eligible** registrations are deferred. All 249 successful-removal directories are absent. Their linked sessions audit returned 247 Stopped and 2 Failed, with zero unreleased-session facts or inspection errors.

Removals were sequential, with heartbeat/resource checks at no more than 25 removals and 20-second sleeps between batches; low memory caused five-minute pauses. Observed heartbeat idle values were 0.0 or 0.6 minutes, with `hangSuspected=false`; positive/low-resource observations paused work. The heartbeat ended at 10:29 with `summary.json`, idle 0.0 and no hang; both recorded PIDs were absent at 10:35. No qualification files or processes were modified.

`git worktree prune -v` succeeded and removed only the stale registration for the already-missing `C:\Antiphon\scratch\investigate-b11dd450`. The detached probe at `C:\Users\lndco\AppData\Local\Temp\antiphon-wt-probe-2\task-2443d3d5` used commit `8edda999623a473be2d1970727c172bfb5088d59`; add and removal succeeded, removal took 5.198s, and the probe directory is absent. No builds or product tests ran. No Temp `antiphon-*-build-output` folders older than 24 hours were found; the empty listing is recorded externally.

Git failed on `C:\Antiphon\worktrees\card-task-c490e425` with `Filename too long`, removed its administrative registration and left its directory. The fresh registration guard refused a retry. This unregistered residue needs manual handling; no alternate filesystem deletion was attempted. Subsequent removals used per-command `-c core.longpaths=true`, without persisting configuration, and had no further deletion failures. No approval hook rejected an action.

Evidence and continuation files are under `C:\Users\lndco\AppData\Local\Temp\antiphon-worktree-audit`: `retirement-final-inventory.csv`, `retirement-final-summary.json`, `retirement-removals.jsonl`, `retirement-linked-sessions.json`, `retirement-old-build-outputs.csv`, probe/prune logs, and the original unchanged `audit.csv`.

The ASCII continuation script contains 654 literal guarded removals matching `retirement-remaining-candidates.csv`; PowerShell parsing reported zero errors. It refreshes task/session, identity, age, heartbeat and memory guards, retains changed/unknown candidates, and preserves the same pacing. Script SHA-256: `54bac209e45f50ec4b2b9a1c859090151207c5c3b2d092e0207fd139cc3afb2e`. Run in the foreground under the existing authority:

```text
! pwsh -NoProfile -File C:\Users\lndco\AppData\Local\Temp\antiphon-worktree-audit\retire-old-worktrees.ps1
```

--- next stage ---
next: decide
handoff: Provide a longer execution window or run the guarded continuation for 654 eligible worktrees; manually handle the unregistered c490e425 residue. Existing operator authority already covers the remaining cleanup.
artifact: docs/investigations/2026-10-31-desktop-worktree-retirement.md
