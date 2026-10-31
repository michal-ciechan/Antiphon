# Desktop worktree retirement - 2026-10-31

**Completed the authorized retirement. 671 additional directories retired in this continuation; 920 total across both windows.** Updated `2026-10-31T13:32:49.275612+00:00`. Remaining eligible at this snapshot: **0**. Within the original resumed set of 654: 0 unprocessed and 0 retained after fresh guards.

Fresh inventory refreshes admitted 17 additional trees that crossed the 24-hour boundary during this continuation; these received the same per-removal checks.

The operator authorized a second two-hour window, preserving the original scope and exclusions. Each deletion uses an exact literal `git worktree remove --force` path under `C:\Antiphon\worktrees`, with per-command `core.longpaths=true`; no persistent configuration change. Guards recheck directory/.git/HEAD-log mtimes (strictly older than 24 hours), registration reciprocity, reparse points, locks, current task status, linked sessions and live consumers. Own workspace and `card-task-c490e425` are explicitly excluded. Dirty/unpushed/ignored contents are covered by standing data-loss authority; no additional approval was requested.

Operations are sequential, at most 25 per batch, with 20-second sleeps. Before every removal, cleanup yields if the host build-slot memory floor is not met or any build-slot waiter exists. Native rerun `e960ffeb` and all other active workspaces are protected. The earlier qualification finished; no release/nightly/evidence/scratch directories, unrelated registered workspaces, branches, objects, repository configuration, child journal or stale lock are deleted/edited by cleanup. Only this report is changed in the task branch. API calls are GETs. This cleanup task ran no builds or product tests.

| Measurement | Original start | First window end | Continuation start | Current |
|---|---:|---:|---:|---:|
| Registered worktrees | 1,050 | 812 | 813 | 149 |
| Drive C free, decimal GB | 238.44 | 306.88 | 305.51 | 441.44 |
| Worktree list, seconds | 11.310 | 3.765 | 2.784 | 0.332 |
| Detached add, seconds | 40.350 (prior audit) | 13.999 | not repeated | 11.637 |

Net free-space increase: **135.93 GB in the continuation**, **202.99 GB overall**. Retired logical contents were **176.52 GiB** in the original audit. Concurrent host activity, new registrations and changed load/cache limit attribution. Final detached probe add 11.637s, removal 4.016s; add/remove exits 0/0, directory present=False. Commit `8edda999623a473be2d1970727c172bfb5088d59`; path `C:\Users\lndco\AppData\Local\Temp\antiphon-wt-probe-2\task-2443d3d5-round2`.

Final registered classification: KEEP-outside-scope=12, KEEP-younger-than-24h=124, KEEP-active-task-or-live-session=8, KEEP-own-task=1, KEEP-locked=3, KEEP-unknown-task=1.

Final verification at `2026-10-31T13:32:03.353984+00:00`: all 920 retired directories are absent; 920 linked task/session records were checked, session states {'Stopped': 902, 'Failed': 14, 'None': 4}, with 0 warnings. Final task-state inspection errors: 0. Final `git worktree prune -v` exited 0 with 0 entries. Old `antiphon-*-build-output` folders under Temp were listed only: No matching folders older than 24 hours were found. Inventory and byte counts are in `retirement-old-build-outputs.csv`. Registration reconciliation: 23 new, 3 other initial registrations absent, including the first-window prune of an already-missing scratch registration (plus the explicitly skipped residue registration). No deletion errors or resource pauses occurred in this continuation.

First-window history is preserved in commit `0bae67b08c411c5d31e5ff9ed6e9f095a78bf46d` and external `retirement-round1-report.md`: 249 successful removals; 654 eligible left; 68.43 GB net free-space increase; 64.58 GiB retired logical contents. Final first-window retained counts were 133 recent, 8 active/live, 1 own, 3 locked, 1 unknown, 12 outside scope. Low memory caused five-minute pauses. Prune removed one already-missing registration; 15 registrations appeared and two additional ones disappeared concurrently. The 249 linked-session checks returned 247 Stopped and 2 Failed, with no unreleased-session facts or inspection errors. The former heartbeat ended with summary.json, idle 0.0/no hang, and both recorded PIDs were absent. No qualification process was stopped.

**Skipped residue:** `C:\Antiphon\worktrees\card-task-c490e425`. The first-window Git failure (Filename too long) removed its registration but left the directory. The guard refused a retry; it is left untouched as requested. Any future cleanup of this residue is separate. No approval hook rejected cleanup; one continuation batch exceeded Windows command length before process creation, resolved by shortening guard/logging text while preserving literal Git deletion paths.

Pushed continuation milestones:

| Additional removals | Full commit SHA |
|---:|---|
| 100 | `c3c42aaee2995e0603aeae6ee63917e5a0f0373b` |
| 200 | `f1c91bfee083eacc760575bb773eb2058201a641` |
| 300 | `42b3d43768196c2b2fab9ee900afe1928b92c072` |
| 400 | `915fc1cfb9fd75e713c57b3b94ce5d0a151835ad` |
| 500 | `5036370ecbf42834c2c9f974c046a7aaee883bc6` |
| 600 | `d179f934f532288b9e3ece13fbd3d253579f1edb` |

Evidence is external under `C:\Users\lndco\AppData\Local\Temp\antiphon-worktree-audit`: removal receipts, round1 snapshots, round2 baseline/progress/publications, live-consumer guards and final inventory/remaining-candidate CSVs. The prior 654-command operator script and its hash are retained as `retirement-round1-script.ps1`; `retire-old-worktrees.ps1` is the guarded fallback script, regenerated from the final candidate CSV. No generated evidence is committed.

--- next stage ---
next: decide
handoff: Retirement is complete for currently proven eligible worktrees; review the retained classifications. The c490e425 residue remains untouched as requested.
artifact: docs/investigations/2026-10-31-desktop-worktree-retirement.md
