# Desktop worktree retirement - 2026-10-31

**Continuation in progress. 300 additional directories retired in this continuation; 549 total across both windows.** Updated `2026-10-31T12:32:35.555883+00:00`. Remaining eligible at this snapshot: **354**. Within the original resumed set of 654: 354 unprocessed and 0 retained after fresh guards.

The operator authorized a second two-hour window, preserving the original scope and exclusions. Each deletion uses an exact literal `git worktree remove --force` path under `C:\Antiphon\worktrees`, with per-command `core.longpaths=true`; no persistent configuration change. Guards recheck directory/.git/HEAD-log mtimes (strictly older than 24 hours), registration reciprocity, reparse points, locks, current task status, linked sessions and live consumers. Own workspace and `card-task-c490e425` are explicitly excluded. Dirty/unpushed/ignored contents are covered by standing data-loss authority; no additional approval was requested.

Operations are sequential, at most 25 per batch, with 20-second sleeps. Before every removal, cleanup yields if the host build-slot memory floor is not met or any build-slot waiter exists. Native rerun `e960ffeb` and all other active workspaces are protected. The earlier qualification finished; no release/nightly/evidence/scratch directories, unrelated registered workspaces, branches, objects, repository configuration, child journal or stale lock are deleted/edited by cleanup. Only this report is changed in the task branch. API calls are GETs. This cleanup task ran no builds or product tests.

| Measurement | Original start | First window end | Continuation start | Current |
|---|---:|---:|---:|---:|
| Registered worktrees | 1,050 | 812 | 813 | 517 |
| Drive C free, decimal GB | 238.44 | 306.88 | 305.51 | 369.11 |
| Worktree list, seconds | 11.310 | 3.765 | 2.784 | 2.130 |
| Detached add, seconds | 40.350 (prior audit) | 13.999 | not repeated | pending final probe |

Net free-space increase: **63.60 GB in the continuation**, **130.67 GB overall**. Retired logical contents were **116.07 GiB** in the original audit. Concurrent host activity, new registrations and changed load/cache limit attribution. The continuation final detached-add probe is pending; the first-window probe added in 13.999s and removed in 5.198s, with no residue.

Final global classification has not yet been refreshed.

Final linked-session audit and build-output listing are pending.

First-window history is preserved in commit `0bae67b08c411c5d31e5ff9ed6e9f095a78bf46d` and external `retirement-round1-report.md`: 249 successful removals; 654 eligible left; 68.43 GB net free-space increase; 64.58 GiB retired logical contents. Final first-window retained counts were 133 recent, 8 active/live, 1 own, 3 locked, 1 unknown, 12 outside scope. Low memory caused five-minute pauses. Prune removed one already-missing registration; 15 registrations appeared and two additional ones disappeared concurrently. The 249 linked-session checks returned 247 Stopped and 2 Failed, with no unreleased-session facts or inspection errors. The former heartbeat ended with summary.json, idle 0.0/no hang, and both recorded PIDs were absent. No qualification process was stopped.

**Skipped residue:** `C:\Antiphon\worktrees\card-task-c490e425`. The first-window Git failure (Filename too long) removed its registration but left the directory. The guard refused a retry; it is left untouched as requested. Any future cleanup of this residue is separate. No approval hook rejected cleanup; one continuation batch exceeded Windows command length before process creation, resolved by shortening guard/logging text while preserving literal Git deletion paths.

Pushed continuation milestones:

| Additional removals | Full commit SHA |
|---:|---|
| 100 | `c3c42aaee2995e0603aeae6ee63917e5a0f0373b` |
| 200 | `f1c91bfee083eacc760575bb773eb2058201a641` |

Evidence is external under `C:\Users\lndco\AppData\Local\Temp\antiphon-worktree-audit`: removal receipts, round1 snapshots, round2 baseline/progress/publications, live-consumer guards and final inventory/remaining-candidate CSVs. The prior 654-command operator script and its hash are retained as `retirement-round1-script.ps1`; `retire-old-worktrees.ps1` is the guarded fallback script, regenerated from the final candidate CSV. No generated evidence is committed.

--- next stage ---
next: decide
handoff: Continue the guarded retirement within the authorized window; retain c490e425 and protect the native rerun. Final verification and metrics remain pending.
artifact: docs/investigations/2026-10-31-desktop-worktree-retirement.md
