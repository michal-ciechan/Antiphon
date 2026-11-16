# Desktop worktree and release-checkout cleanup inventory (2026-11-16)

Task ab570408. Host: Windows desktop. Read-only inventory taken 2026-11-16 ~14:00Z, before any deletion.

## Method

- Targets: every worktree registered in `C:\src\Antiphon` (`git worktree list --porcelain`), and the `checkout*` clones under `C:\Antiphon\releases`.
- Task state: `GET /api/agent-tasks/<id8>` (the same source `delegate.ps1 -Status` reads), re-read again immediately before each removal.
- Clean: `git status --porcelain --untracked-files=all` empty, and `git status --ignored` shows only build output (`bin/`, `obj/`, `node_modules/`, `dist/`, `TestResults/`, `bin-*/`...). Any other ignored content (`.antiphon/` evidence, `logs/`, local appsettings) = KEEP.
- Published: the branch on LIVE origin (`git ls-remote`) equals HEAD or contains it; else HEAD is an ancestor of origin/master or contained in a remote branch.
- Age: hours since the task's `completedAt`.
- Live use: no process command line or executable inside the path (Win32_Process).
- Extra KEEPs: CARD-1955 trees (open Review 54721a0f), the live run task 053aa2ea, this task's own tree, locked/land/evidence/canonical/ownerless trees.
- Sanctioned mechanism: the desktop has the CARD-0824 S4a `antiphon:desktop-legacy-release` job (48 h floor, operator-only preview, child tokens refused); the CARD-1677 runner retention covers only server2 mirrors. Neither can be driven from a child task token, so removal here is by `git worktree remove` (no `--force`) per exact path, with the same checks the policy uses plus the brief's 12 h floor.
- Release checkouts are independent clones (not worktrees); only clean clones of ENDED runs are deleted. Logs, TRX, summary, state/history/last-run live in `candidates\<id>\logs`, `state-<id>` and `task-<id>`, which are all kept.

## Summary

| Set | DELETE rows | DELETE GB | KEEP rows | KEEP GB |
|---|---|---|---|---|
| Registered worktrees | 181 | 55.25 | 35 | 44.89 |
| Release checkouts | 18 | 71.44 | 13 | 49.75 |

## Registered worktrees

| Path | Size GB | Task | State | Branch | Published | Age h | Verdict | Reason |
|---|---|---|---|---|---|---|---|---|
| `C:/Antiphon/scratch/089ef857-master` | 0.33 | 089ef857 | Succeeded | detached a2aedb57f | yes (HEAD ancestor of origin/master) | 568.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/scratch/a37ceaf1-rc` | 1.61 | a37ceaf1 | Succeeded | detached 12a0b107f | yes (contained in origin/feat/card-task-089ef857) | 573.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/scratch/ae4df578-baseline` | 0.17 | ae4df578 | Succeeded | detached a2aedb57f | yes (HEAD ancestor of origin/master) | 571.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/scratch/ae4df578-rc` | 0.32 | ae4df578 | Succeeded | detached 12a0b107f | yes (contained in origin/feat/card-task-089ef857) | 571.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-00011235` | 0.22 | 00011235 | Succeeded | feat/card-task-00011235 | yes (origin branch == HEAD) | 32.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-0055a160` | 0.22 | 0055a160 | Succeeded | feat/card-task-0055a160 | yes (origin branch == HEAD) | 41.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-01f26ecf` | 0.21 | 01f26ecf | Failed | feat/card-task-01f26ecf | yes (origin branch == HEAD) | 143.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-02f02e8a` | 0.20 | 02f02e8a | Canceled | feat/card-task-02f02e8a | yes (origin branch == HEAD) | 237 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-06aa57e7` | 0.21 | 06aa57e7 | Succeeded | feat/card-task-06aa57e7 | yes (origin branch == HEAD) | 105.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-0704a004` | 0.22 | 0704a004 | Succeeded | feat/card-task-0704a004 | yes (origin branch == HEAD) | 38.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-0bd83714` | 0.22 | 0bd83714 | Succeeded | feat/card-task-0bd83714 | yes (origin branch == HEAD) | 32 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-12311088` | 0.22 | 12311088 | Succeeded | feat/card-task-12311088 | yes (origin branch == HEAD) | 33.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-149cb7c3` | 0.21 | 149cb7c3 | Canceled | feat/card-task-149cb7c3 | yes (origin branch == HEAD) | 100.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-15406ecc` | 0.22 | 15406ecc | Succeeded | feat/card-task-15406ecc | yes (origin branch == HEAD) | 44.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-1637311a` | 0.22 | 1637311a | Succeeded | feat/card-task-1637311a | yes (origin branch == HEAD) | 39.9 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-18072e35` | 0.22 | 18072e35 | Succeeded | feat/card-task-18072e35 | yes (origin branch == HEAD) | 40.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-18dd27d8` | 0.21 | 18dd27d8 | Failed | feat/card-task-18dd27d8 | yes (origin branch == HEAD) | 191.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-1905deba` | 0.22 | 1905deba | Succeeded | feat/card-task-1905deba | yes (origin branch == HEAD) | 35.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-198a988b` | 0.22 | 198a988b | Succeeded | feat/card-task-198a988b | yes (origin branch == HEAD) | 37 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-1a125507` | 0.22 | 1a125507 | Succeeded | feat/card-task-1a125507 | yes (origin branch == HEAD) | 40.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-1af450d3` | 0.21 | 1af450d3 | Failed | feat/card-task-1af450d3 | yes (origin branch == HEAD) | 190.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-1ba18e23` | 0.22 | 1ba18e23 | Succeeded | feat/card-task-1ba18e23 | yes (origin branch == HEAD) | 35.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-1c76938a` | 0.22 | 1c76938a | Succeeded | feat/card-task-1c76938a | yes (origin branch == HEAD) | 28.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-1d838cd1` | 0.22 | 1d838cd1 | Succeeded | feat/card-task-1d838cd1 | yes (origin branch == HEAD) | 18.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-1e279cab` | 0.20 | 1e279cab | Canceled | feat/card-task-1e279cab | yes (origin branch == HEAD) | 251.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-1edbc845` | 0.20 | 1edbc845 | Canceled | feat/card-task-1edbc845 | yes (HEAD ancestor of origin branch) | 273.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-20805524` | 0.21 | 20805524 | Succeeded | feat/card-task-20805524 | yes (HEAD ancestor of origin branch) | 219.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-22ee1e10` | 0.22 | 22ee1e10 | Succeeded | feat/card-task-22ee1e10 | yes (origin branch == HEAD) | 15.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-23686c59` | 0.22 | 23686c59 | Succeeded | feat/card-task-23686c59 | yes (origin branch == HEAD) | 15.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-24cf8f11` | 0.22 | 24cf8f11 | Succeeded | feat/card-task-24cf8f11 | yes (origin branch == HEAD) | 36.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-24eb2f3d` | 0.22 | 24eb2f3d | Succeeded | feat/card-task-24eb2f3d | yes (origin branch == HEAD) | 35 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-25d9a69f` | 0.21 | 25d9a69f | Canceled | feat/card-task-25d9a69f | yes (origin branch == HEAD) | 75.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-27128425` | 0.22 | 27128425 | Succeeded | feat/card-task-27128425 | yes (origin branch == HEAD) | 34.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-28eecdf5` | 0.22 | 28eecdf5 | Succeeded | feat/card-task-28eecdf5 | yes (origin branch == HEAD) | 25.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-28f886d2` | 2.28 | 28f886d2 | Succeeded | feat/card-task-28f886d2 | yes (origin branch == HEAD) | 45.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-29664acc` | 0.20 | 29664acc | Succeeded | feat/card-task-29664acc | yes (origin branch == HEAD) | 292.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-29d177ef` | 0.22 | 29d177ef | Succeeded | feat/card-task-29d177ef | yes (origin branch == HEAD) | 46.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-2abcd1c0` | 0.21 | 2abcd1c0 | Succeeded | feat/card-task-2abcd1c0 | yes (HEAD ancestor of origin branch) | 189.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-2b6dde13` | 0.22 | 2b6dde13 | Succeeded | feat/card-task-2b6dde13 | yes (origin branch == HEAD) | 35.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-314297be` | 0.20 | 314297be | Canceled | feat/card-task-314297be | yes (origin branch == HEAD) | 289.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-3199d57c` | 0.22 | 3199d57c | Failed | feat/card-task-3199d57c | yes (origin branch == HEAD) | 70.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-31c683a8` | 2.28 | 31c683a8 | Succeeded | feat/card-task-31c683a8 | yes (origin branch == HEAD) | 48.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-33039320` | 0.22 | 33039320 | Succeeded | feat/card-task-33039320 | yes (origin branch == HEAD) | 33.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-35eb006c` | 1.27 | 35eb006c | Succeeded | feat/card-task-35eb006c | yes (origin branch == HEAD) | 47.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-3820a5f1` | 0.22 | 3820a5f1 | Succeeded | feat/card-task-3820a5f1 | yes (origin branch == HEAD) | 18 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-3afa3cfa` | 0.22 | 3afa3cfa | Succeeded | feat/card-task-3afa3cfa | yes (origin branch == HEAD) | 45 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-3b906877` | 0.20 | 3b906877 | Canceled | feat/card-task-3b906877 | yes (origin branch == HEAD) | 271.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-3c766de7` | 0.22 | 3c766de7 | Succeeded | feat/card-task-3c766de7 | yes (origin branch == HEAD) | 45.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-3ea0f6b5` | 0.22 | 3ea0f6b5 | Succeeded | feat/card-task-3ea0f6b5 | yes (origin branch == HEAD) | 25.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-3f159277` | 0.22 | 3f159277 | Succeeded | feat/card-task-3f159277 | yes (origin branch == HEAD) | 29.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-3fd6747e` | 0.20 | 3fd6747e | Failed | feat/card-task-3fd6747e | yes (origin branch == HEAD) | 278.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-4292c713` | 0.21 | 4292c713 | Succeeded | feat/card-task-4292c713 | yes (HEAD ancestor of origin branch) | 192.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-484dd12d` | 0.22 | 484dd12d | Succeeded | feat/card-task-484dd12d | yes (origin branch == HEAD) | 30.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-48728776` | 0.22 | 48728776 | Succeeded | feat/card-task-48728776 | yes (origin branch == HEAD) | 35.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-4c25994e` | 0.22 | 4c25994e | Succeeded | feat/card-task-4c25994e | yes (origin branch == HEAD) | 24.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-4f5166ec` | 0.21 | 4f5166ec | Succeeded | feat/card-task-4f5166ec | yes (HEAD ancestor of origin branch) | 213.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-4f8dc817` | 0.22 | 4f8dc817 | Succeeded | feat/card-task-4f8dc817 | yes (origin branch == HEAD) | 33.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-50e9a890` | 0.20 | 50e9a890 | Canceled | feat/card-task-50e9a890 | yes (origin branch == HEAD) | 251.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-555ce937` | 0.20 | 555ce937 | Canceled | feat/card-task-555ce937 | yes (origin branch == HEAD) | 238.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-5675aac7` | 0.22 | 5675aac7 | Succeeded | feat/card-task-5675aac7 | yes (origin branch == HEAD) | 43.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-56bf971d` | 0.20 | 56bf971d | Succeeded | feat/card-task-56bf971d | yes (HEAD ancestor of origin branch) | 288 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-5786ffb9` | 0.22 | 5786ffb9 | Succeeded | feat/card-task-5786ffb9 | yes (origin branch == HEAD) | 33.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-593c280c` | 0.22 | 593c280c | Succeeded | feat/card-task-593c280c | yes (origin branch == HEAD) | 36.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-5a2fffb9` | 0.21 | 5a2fffb9 | Canceled | feat/card-task-5a2fffb9 | yes (origin branch == HEAD) | 216.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-5b1ea772` | 0.21 | 5b1ea772 | Succeeded | feat/card-task-5b1ea772 | yes (HEAD ancestor of origin branch) | 198.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-5c14b3c5` | 0.22 | 5c14b3c5 | Succeeded | feat/card-task-5c14b3c5 | yes (origin branch == HEAD) | 31.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-5c3cf9f6` | 0.22 | 5c3cf9f6 | Succeeded | feat/card-task-5c3cf9f6 | yes (origin branch == HEAD) | 45 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-5ca0e7b6` | 0.22 | 5ca0e7b6 | Succeeded | feat/card-task-5ca0e7b6 | yes (origin branch == HEAD) | 17.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-605c6f4e` | 0.20 | 605c6f4e | Succeeded | feat/card-task-605c6f4e | yes (HEAD ancestor of origin branch) | 276.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-613f2230` | 0.20 | 613f2230 | Succeeded | feat/card-task-613f2230 | yes (HEAD ancestor of origin branch) | 277 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-6155693e` | 0.21 | 6155693e | Succeeded | feat/card-task-6155693e | yes (HEAD ancestor of origin branch) | 184.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-642a3764` | 0.21 | 642a3764 | Failed | feat/card-task-642a3764 | yes (origin branch == HEAD) | 221.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-6430f6eb` | 0.22 | 6430f6eb | Succeeded | feat/card-task-6430f6eb | yes (origin branch == HEAD) | 29.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-64fb3f5d` | 0.20 | 64fb3f5d | Canceled | feat/card-task-64fb3f5d | yes (HEAD ancestor of origin branch) | 273.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-665816dd` | 0.22 | 665816dd | Succeeded | feat/card-task-665816dd | yes (origin branch == HEAD) | 32.9 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-66eb7e18` | 0.22 | 66eb7e18 | Succeeded | feat/card-task-66eb7e18 | yes (origin branch == HEAD) | 43.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-68b66c6d` | 0.22 | 68b66c6d | Succeeded | feat/card-task-68b66c6d | yes (origin branch == HEAD) | 41.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-68f81b18` | 0.22 | 68f81b18 | Succeeded | feat/card-task-68f81b18 | yes (origin branch == HEAD) | 18.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-6eccb98b` | 0.22 | 6eccb98b | Failed | feat/card-task-6eccb98b | yes (HEAD ancestor of origin/master) | 47.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-6f663ce6` | 0.22 | 6f663ce6 | Succeeded | feat/card-task-6f663ce6 | yes (origin branch == HEAD) | 47.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-7342010d` | 0.22 | 7342010d | Succeeded | feat/card-task-7342010d | yes (origin branch == HEAD) | 35.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-73e7dfdd` | 0.22 | 73e7dfdd | Succeeded | feat/card-task-73e7dfdd | yes (origin branch == HEAD) | 31.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-7555284f` | 0.21 | 7555284f | Succeeded | feat/card-task-7555284f | yes (HEAD ancestor of origin branch) | 189.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-7708d552` | 0.22 | 7708d552 | Succeeded | feat/card-task-7708d552 | yes (origin branch == HEAD) | 31.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-77ab4e11` | 0.22 | 77ab4e11 | Succeeded | feat/card-task-77ab4e11 | yes (origin branch == HEAD) | 21.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-78d029fc` | 0.22 | 78d029fc | Succeeded | feat/card-task-78d029fc | yes (origin branch == HEAD) | 35.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-7a422522` | 0.22 | 7a422522 | Succeeded | feat/card-task-7a422522 | yes (origin branch == HEAD) | 29 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-7ac938e9` | 2.27 | 7ac938e9 | Succeeded | feat/card-task-7ac938e9 | yes (origin branch == HEAD) | 32.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-7bb75309` | 0.22 | 7bb75309 | Succeeded | feat/card-task-7bb75309 | yes (origin branch == HEAD) | 30.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-7c7132b3` | 0.20 | 7c7132b3 | Failed | feat/card-task-7c7132b3 | yes (origin branch == HEAD) | 278.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-801ad6c3` | 0.20 | 801ad6c3 | Succeeded | feat/card-task-801ad6c3 | yes (HEAD ancestor of origin branch) | 284.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-80c30683` | 0.22 | 80c30683 | Succeeded | feat/card-task-80c30683 | yes (origin branch == HEAD) | 14.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-8112d494` | 0.22 | 8112d494 | Succeeded | feat/card-task-8112d494 | yes (origin branch == HEAD) | 34.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-856fac4d` | 0.22 | 856fac4d | Succeeded | feat/card-task-856fac4d | yes (origin branch == HEAD) | 43.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-8592d536` | 0.20 | 8592d536 | Canceled | feat/card-task-8592d536 | yes (origin branch == HEAD) | 273.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-870a2044` | 0.22 | 870a2044 | Succeeded | feat/card-task-870a2044 | yes (origin branch == HEAD) | 16.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-87596cc5` | 0.21 | 87596cc5 | Failed | feat/card-task-87596cc5 | yes (origin branch == HEAD) | 79.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-882cae23` | 0.22 | 882cae23 | Succeeded | feat/card-task-882cae23 | yes (origin branch == HEAD) | 41.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-8998668f` | 2.28 | 8998668f | Succeeded | feat/card-task-8998668f | yes (origin branch == HEAD) | 45.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-8d020b07` | 0.22 | 8d020b07 | Succeeded | feat/card-task-8d020b07 | yes (origin branch == HEAD) | 18.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-8d8824ed` | 0.22 | 8d8824ed | Succeeded | feat/card-task-8d8824ed | yes (origin branch == HEAD) | 34.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-8de3f823` | 0.22 | 8de3f823 | Succeeded | feat/card-task-8de3f823 | yes (origin branch == HEAD) | 46.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-8ec395a1` | 0.22 | 8ec395a1 | Succeeded | feat/card-task-8ec395a1 | yes (origin branch == HEAD) | 14.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-8f001b6d` | 0.22 | 8f001b6d | Succeeded | feat/card-task-8f001b6d | yes (origin branch == HEAD) | 24.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-9248daa5` | 0.22 | 9248daa5 | Succeeded | feat/card-task-9248daa5 | yes (origin branch == HEAD) | 29.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-9294312a` | 0.21 | 9294312a | Canceled | feat/card-task-9294312a | yes (origin branch == HEAD) | 129 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-9325be95` | 0.22 | 9325be95 | Succeeded | feat/card-task-9325be95 | yes (origin branch == HEAD) | 46.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-934cdb25` | 0.21 | 934cdb25 | Succeeded | feat/card-task-934cdb25 | yes (HEAD ancestor of origin branch) | 185.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-93af5925` | 0.22 | 93af5925 | Succeeded | feat/card-task-93af5925 | yes (origin branch == HEAD) | 42 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-93f5087b` | 0.22 | 93f5087b | Succeeded | feat/card-task-93f5087b | yes (origin branch == HEAD) | 35.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-94b17435` | 0.21 | 94b17435 | Succeeded | feat/card-task-94b17435 | yes (HEAD ancestor of origin branch) | 182.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-9aa09d20` | 0.22 | 9aa09d20 | Succeeded | feat/card-task-9aa09d20 | yes (origin branch == HEAD) | 38.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-9c1feeba` | 0.22 | 9c1feeba | Succeeded | feat/card-task-9c1feeba | yes (origin branch == HEAD) | 46 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-a2845b6c` | 0.21 | a2845b6c | Succeeded | feat/card-task-a2845b6c | yes (HEAD ancestor of origin branch) | 192.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-a41890aa` | 0.20 | a41890aa | Succeeded | feat/card-task-a41890aa | yes (HEAD ancestor of origin branch) | 286.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-a9b46304` | 0.20 | a9b46304 | Canceled | feat/card-task-a9b46304 | yes (origin branch == HEAD) | 251.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-ab7e65ba` | 0.22 | ab7e65ba | Failed | feat/card-task-ab7e65ba | yes (HEAD ancestor of origin/master) | 46.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-abd9b404` | 0.22 | abd9b404 | Succeeded | feat/card-task-abd9b404 | yes (origin branch == HEAD) | 43.9 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-ac7f437c` | 0.22 | ac7f437c | Succeeded | feat/card-task-ac7f437c | yes (origin branch == HEAD) | 42.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-ac8b9d8e` | 0.22 | ac8b9d8e | Succeeded | feat/card-task-ac8b9d8e | yes (origin branch == HEAD) | 26.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-b0e54eca` | 0.21 | b0e54eca | Succeeded | feat/card-task-b0e54eca | yes (HEAD ancestor of origin branch) | 170.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-b0ec9aab` | 0.22 | b0ec9aab | Succeeded | feat/card-task-b0ec9aab | yes (origin branch == HEAD) | 40 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-b1318d57` | 0.22 | b1318d57 | Succeeded | feat/card-task-b1318d57 | yes (origin branch == HEAD) | 35 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-b2663606` | 0.22 | b2663606 | Succeeded | feat/card-task-b2663606 | yes (origin branch == HEAD) | 34.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-b440ac48` | 0.22 | b440ac48 | Succeeded | feat/card-task-b440ac48 | yes (origin branch == HEAD) | 31.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-b4f39ca7` | 1.05 | b4f39ca7 | Succeeded | feat/card-task-b4f39ca7 | yes (origin branch == HEAD) | 48.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-b5b7324a` | 0.20 | b5b7324a | Failed | feat/card-task-b5b7324a | yes (HEAD ancestor of origin branch) | 274.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-b7c4a9b7` | 0.20 | b7c4a9b7 | Canceled | feat/card-task-b7c4a9b7 | yes (HEAD ancestor of origin branch) | 251.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-baba7a4e` | 0.22 | baba7a4e | Succeeded | feat/card-task-baba7a4e | yes (origin branch == HEAD) | 20.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-bc7a9fdd` | 0.21 | bc7a9fdd | Succeeded | feat/card-task-bc7a9fdd | yes (HEAD ancestor of origin branch) | 214.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-bd9e5cb5` | 0.22 | bd9e5cb5 | Succeeded | feat/card-task-bd9e5cb5 | yes (origin branch == HEAD) | 18 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-bee64303` | 0.21 | bee64303 | Succeeded | feat/card-task-bee64303 | yes (HEAD ancestor of origin branch) | 187.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-bf127f48` | 0.22 | bf127f48 | Succeeded | feat/card-task-bf127f48 | yes (origin branch == HEAD) | 33.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-bffbe413` | 0.36 | bffbe413 | Succeeded | feat/card-task-bffbe413 | yes (origin branch == HEAD) | 31.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-c2489e73` | 0.22 | c2489e73 | Succeeded | feat/card-task-c2489e73 | yes (origin branch == HEAD) | 21.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-c3d468f8` | 0.22 | c3d468f8 | Failed | feat/card-task-c3d468f8 | yes (origin branch == HEAD) | 32.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-c51e92c8` | 2.61 | c51e92c8 | Succeeded | feat/card-task-c51e92c8 | yes (origin branch == HEAD) | 37.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-c6ca6929` | 0.21 | c6ca6929 | Succeeded | feat/card-task-c6ca6929 | yes (HEAD ancestor of origin branch) | 186.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-c6da537d` | 0.22 | c6da537d | Succeeded | feat/card-task-c6da537d | yes (origin branch == HEAD) | 27.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-c7a30141` | 0.21 | c7a30141 | Succeeded | feat/card-task-c7a30141 | yes (HEAD ancestor of origin branch) | 209.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-ca8076ae` | 0.22 | ca8076ae | Succeeded | feat/card-task-ca8076ae | yes (origin branch == HEAD) | 48 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-cbedf221` | 0.22 | cbedf221 | Succeeded | feat/card-task-cbedf221 | yes (origin branch == HEAD) | 48 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-cd9133eb` | 0.22 | cd9133eb | Succeeded | feat/card-task-cd9133eb | yes (origin branch == HEAD) | 34.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-ce8ef291` | 0.22 | ce8ef291 | Succeeded | feat/card-task-ce8ef291 | yes (origin branch == HEAD) | 37.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-cf50eda6` | 0.22 | cf50eda6 | Succeeded | feat/card-task-cf50eda6 | yes (origin branch == HEAD) | 30.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-cfccf493` | 0.20 | cfccf493 | Canceled | feat/card-task-cfccf493 | yes (HEAD ancestor of origin/master) | 271.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-d20d7003` | 0.22 | d20d7003 | Succeeded | feat/card-task-d20d7003 | yes (origin branch == HEAD) | 19 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-d2564da4` | 0.20 | d2564da4 | Succeeded | feat/card-task-d2564da4 | yes (HEAD ancestor of origin branch) | 282.1 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-d5ec6eff` | 0.22 | d5ec6eff | Succeeded | feat/card-task-d5ec6eff | yes (origin branch == HEAD) | 33.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-d684bc25` | 0.22 | d684bc25 | Succeeded | feat/card-task-d684bc25 | yes (origin branch == HEAD) | 46.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-d8f79eb5` | 0.22 | d8f79eb5 | Succeeded | feat/card-task-d8f79eb5 | yes (origin branch == HEAD) | 13.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-d989d9d8` | 0.21 | d989d9d8 | Canceled | feat/card-task-d989d9d8 | yes (origin branch == HEAD) | 100.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-da90742c` | 0.21 | da90742c | Succeeded | feat/card-task-da90742c | yes (HEAD ancestor of origin branch) | 189.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-daf2ea47` | 0.22 | daf2ea47 | Succeeded | feat/card-task-daf2ea47 | yes (origin branch == HEAD) | 30.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-dc70c718` | 0.22 | dc70c718 | Succeeded | feat/card-task-dc70c718 | yes (origin branch == HEAD) | 48.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-dc726c22` | 0.22 | dc726c22 | Succeeded | feat/card-task-dc726c22 | yes (origin branch == HEAD) | 16.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-dd35b058` | 0.20 | dd35b058 | Canceled | feat/card-task-dd35b058 | yes (origin branch == HEAD) | 289.4 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-e24018e3` | 0.20 | e24018e3 | Succeeded | feat/card-task-e24018e3 | yes (HEAD ancestor of origin branch) | 279.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-e479506f` | 0.22 | e479506f | Succeeded | feat/card-task-e479506f | yes (origin branch == HEAD) | 29.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-e5258e8c` | 0.20 | e5258e8c | Succeeded | feat/card-task-e5258e8c | yes (HEAD ancestor of origin branch) | 290.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-e594649c` | 0.22 | e594649c | Succeeded | feat/card-task-e594649c | yes (origin branch == HEAD) | 30.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-e5c123ad` | 0.22 | e5c123ad | Succeeded | feat/card-task-e5c123ad | yes (origin branch == HEAD) | 15 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-e7d7d78b` | 0.21 | e7d7d78b | Succeeded | feat/card-task-e7d7d78b | yes (HEAD ancestor of origin branch) | 217.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-ec0488ff` | 0.22 | ec0488ff | Succeeded | feat/card-task-ec0488ff | yes (origin branch == HEAD) | 48.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-ec4754a8` | 0.22 | ec4754a8 | Succeeded | feat/card-task-ec4754a8 | yes (origin branch == HEAD) | 47.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-ec9dfd70` | 0.21 | ec9dfd70 | Succeeded | feat/card-task-ec9dfd70 | yes (HEAD ancestor of origin branch) | 196.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-ed0f7942` | 0.21 | ed0f7942 | Succeeded | feat/card-task-ed0f7942 | yes (HEAD ancestor of origin branch) | 190.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f1cf9f3c` | 0.22 | f1cf9f3c | Succeeded | feat/card-task-f1cf9f3c | yes (origin branch == HEAD) | 31.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f1fde86b` | 0.21 | f1fde86b | Failed | feat/card-task-f1fde86b | yes (origin branch == HEAD) | 190.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f21bf3ba` | 0.22 | f21bf3ba | Succeeded | feat/card-task-f21bf3ba | yes (origin branch == HEAD) | 31.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f2976027` | 0.20 | f2976027 | Succeeded | feat/card-task-f2976027 | yes (HEAD ancestor of origin branch) | 282.9 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f303b3ec` | 0.20 | f303b3ec | Succeeded | feat/card-task-f303b3ec | yes (HEAD ancestor of origin branch) | 289.3 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f38e63e0` | 0.22 | f38e63e0 | Failed | feat/card-task-f38e63e0 | yes (HEAD ancestor of origin/master) | 18.2 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f3a457ae` | 0.22 | f3a457ae | Succeeded | feat/card-task-f3a457ae | yes (origin branch == HEAD) | 48.6 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f442a78f` | 0.21 | f442a78f | Succeeded | feat/card-task-f442a78f | yes (HEAD ancestor of origin branch) | 211.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f674c79a` | 0.20 | f674c79a | Canceled | feat/card-task-f674c79a | yes (HEAD ancestor of origin branch) | 273.5 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f6d722a9` | 0.22 | f6d722a9 | Succeeded | feat/card-task-f6d722a9 | yes (origin branch == HEAD) | 42.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f87fdda9` | 0.21 | f87fdda9 | Succeeded | feat/card-task-f87fdda9 | yes (HEAD ancestor of origin branch) | 198.7 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-f9e020bf` | 2.38 | f9e020bf | Succeeded | feat/card-task-f9e020bf | yes (origin branch == HEAD) | 31 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/worktrees/card-task-fc30e791` | 0.22 | fc30e791 | Succeeded | feat/card-task-fc30e791 | yes (origin branch == HEAD) | 44.9 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/temp/antiphon-add64715-base` | 0.19 | add64715 | Succeeded | detached ade542225 | yes (contained in origin/feat/card-task-00ae50cf) | 419.8 | DELETE | terminal task AND fully published AND clean AND older than 12 hours |
| `C:/Antiphon/evidence/code-67a63a4f/base-checkout` | 0.99 |  |  | detached 1ef629678 | yes (contained in origin/feat/card-task-151b4e13) |  | KEEP | evidence directory; task state '' not terminal; terminal age h < 12h; ignored non-build content: logs/ |
| `C:/Antiphon/scratch/089ef857-rc-check` | 0.32 | 089ef857 | Succeeded | detached 9e97df3f7 | yes (contained in origin/feat/card-task-089ef857) | 568.4 | KEEP | 5 modified tracked files |
| `C:/Antiphon/scratch/78334d64-rc` | 0.32 | 78334d64 | Succeeded | detached 5a690f985 | no () | 571.1 | KEEP | HEAD not on origin |
| `C:/Antiphon/worktrees/card-task-016fd320` | 0.22 | 016fd320 | Succeeded | feat/card-task-016fd320 | no (origin branch 202970c67 != HEAD) | 0.1 | KEEP | terminal age 0.1h < 12h; HEAD not on origin |
| `C:/Antiphon/worktrees/card-task-053aa2ea` | 2.29 | 053aa2ea | Succeeded | feat/card-task-053aa2ea | yes (origin branch == HEAD) | 3.6 | KEEP | live qualification run task; terminal age 3.6h < 12h; ignored non-build content: .antiphon/c1210-audit/ |
| `C:/Antiphon/worktrees/card-task-211808d0` | 1.89 | 211808d0 | Succeeded | feat/card-task-211808d0 | yes (origin branch == HEAD) | 233.1 | KEEP | ignored non-build content: .antiphon/native-211808d0/, .antiphon/task-211808d0.md |
| `C:/Antiphon/worktrees/card-task-2a292434` | 0.22 | 2a292434 | Succeeded | feat/card-task-2a292434 | yes (origin branch == HEAD) | 3.3 | KEEP | terminal age 3.3h < 12h |
| `C:/Antiphon/worktrees/card-task-3c25d1ab` | 0.22 | 3c25d1ab | Succeeded | feat/card-task-3c25d1ab | yes (origin branch == HEAD) | 4.9 | KEEP | terminal age 4.9h < 12h |
| `C:/Antiphon/worktrees/card-task-495cb51f` | 0.22 | 495cb51f | Succeeded | feat/card-task-495cb51f | yes (origin branch == HEAD) | 3.9 | KEEP | terminal age 3.9h < 12h |
| `C:/Antiphon/worktrees/card-task-4a63f1cf` | 2.29 | 4a63f1cf | Succeeded | feat/card-task-4a63f1cf | yes (origin branch == HEAD) | 16.3 | KEEP | ignored non-build content: .antiphon/c1210-audit/ |
| `C:/Antiphon/worktrees/card-task-54721a0f` | 0.22 | 54721a0f | Dispatched | feat/card-task-54721a0f | yes (origin branch == HEAD) |  | KEEP | CARD-1955 subject of open Review 54721a0f; task state 'Dispatched' not terminal; terminal age h < 12h |
| `C:/Antiphon/worktrees/card-task-6ebbcb6e` | 2.26 | 6ebbcb6e | Succeeded | feat/card-task-6ebbcb6e | yes (origin branch == HEAD) | 44.3 | KEEP | ignored non-build content: .antiphon/c1210-audit/, .antiphon/checkpoints/ |
| `C:/Antiphon/worktrees/card-task-7ca19c43` | 0.82 | 7ca19c43 | Succeeded | feat/card-task-7ca19c43 | yes (origin branch == HEAD) | 217.3 | KEEP | ignored non-build content: .antiphon/trace/ |
| `C:/Antiphon/worktrees/card-task-84a1901d` | 0.22 | 84a1901d | Succeeded | feat/card-task-84a1901d | yes (origin branch == HEAD) | 1.5 | KEEP | terminal age 1.5h < 12h |
| `C:/Antiphon/worktrees/card-task-885b5234` | 0.22 | 885b5234 | Succeeded | feat/card-task-885b5234 | yes (origin branch == HEAD) | 0.2 | KEEP | CARD-1955 subject of open Review 54721a0f; terminal age 0.2h < 12h |
| `C:/Antiphon/worktrees/card-task-8b491f75` | 0.22 | 8b491f75 | Succeeded | feat/card-task-8b491f75 | yes (origin branch == HEAD) | 10.1 | KEEP | terminal age 10.1h < 12h |
| `C:/Antiphon/worktrees/card-task-964900d9` | 2.29 | 964900d9 | Succeeded | feat/card-task-964900d9 | yes (origin branch == HEAD) | 17.8 | KEEP | ignored non-build content: .antiphon/c1210-audit/, .antiphon/rc-cut-964900d9/ |
| `C:/Antiphon/worktrees/card-task-9cbb56ed` | 0.22 | 9cbb56ed | Succeeded | feat/card-task-9cbb56ed | yes (origin branch == HEAD) | 0.3 | KEEP | terminal age 0.3h < 12h |
| `C:/Antiphon/worktrees/card-task-9e615360` | 0.22 | 9e615360 | Succeeded | feat/card-task-9e615360 | yes (origin branch == HEAD) | 5 | KEEP | CARD-1955 subject of open Review 54721a0f; terminal age 5h < 12h |
| `C:/Antiphon/worktrees/card-task-ab570408` | 0.22 | ab570408 | Dispatched | feat/card-task-ab570408 | yes (HEAD ancestor of origin/master) |  | KEEP | own task worktree; task state 'Dispatched' not terminal; terminal age h < 12h |
| `C:/Antiphon/worktrees/card-task-b5e998ee` | 1.89 | b5e998ee | Canceled | feat/card-task-b5e998ee | yes (origin branch == HEAD) | 235.2 | KEEP | ignored non-build content: .antiphon/b5e998ee-evidence/ |
| `C:/Antiphon/worktrees/card-task-bad86a85` | 0.22 | bad86a85 | Succeeded | feat/card-task-bad86a85 | yes (origin branch == HEAD) | 9.4 | KEEP | terminal age 9.4h < 12h |
| `C:/Antiphon/worktrees/card-task-cc5b4299` | 0.22 | cc5b4299 | Succeeded | feat/card-task-cc5b4299 | yes (origin branch == HEAD) | 8 | KEEP | terminal age 8h < 12h |
| `C:/Antiphon/worktrees/card-task-e92f04ad` | 0.22 | e92f04ad | Succeeded | feat/card-task-e92f04ad | yes (origin branch == HEAD) | 93.3 | KEEP | ignored non-build content: .antiphon/probe/ |
| `C:/Antiphon/worktrees/card-task-ebcd3b04` | 0.22 | ebcd3b04 | Succeeded | feat/card-task-ebcd3b04 | no () | 0.1 | KEEP | terminal age 0.1h < 12h; HEAD not on origin |
| `C:/Antiphon/worktrees/card-task-ee76e32c` | 0.22 | ee76e32c | Succeeded | feat/card-task-ee76e32c | yes (origin branch == HEAD) | 3.5 | KEEP | CARD-1955 subject of open Review 54721a0f; terminal age 3.5h < 12h |
| `C:/Antiphon/worktrees/card-task-f7f2de80` | 0.22 | f7f2de80 | Succeeded | feat/card-task-f7f2de80 | yes (origin branch == HEAD) | 8.2 | KEEP | CARD-1955 subject of open Review 54721a0f; terminal age 8.2h < 12h |
| `C:/Antiphon/worktrees/card-task-fbc651a4` | 0.22 | fbc651a4 | Succeeded | feat/card-task-fbc651a4 | yes (origin branch == HEAD) | 4 | KEEP | terminal age 4h < 12h |
| `C:/Antiphon/worktrees/card-task-fd30d2b0` | 0.22 | fd30d2b0 | Succeeded | feat/card-task-fd30d2b0 | yes (origin branch == HEAD) | 2.6 | KEEP | CARD-1955 subject of open Review 54721a0f; terminal age 2.6h < 12h |
| `C:/Antiphon/worktrees/card-task-ff89ca56` | 2.29 | ff89ca56 | Succeeded | feat/card-task-ff89ca56 | yes (origin branch == HEAD) | 33.7 | KEEP | ignored non-build content: .antiphon/c1210-audit/ |
| `C:/Antiphon/worktrees/land/0f9784a67773` | 0.20 |  |  | detached 679b696bd | yes (HEAD ancestor of origin/master) |  | KEEP | land worktree (locked); task state '' not terminal; terminal age h < 12h; worktree locked |
| `C:/src/Antiphon` | 21.53 |  |  | detached 9b98828eb | yes (HEAD ancestor of origin/master) |  | KEEP | canonical checkout; task state '' not terminal; terminal age h < 12h; 2 untracked files; ignored non-build content: .antiphon/3904d52e-baseline.log, .antiphon/3904d52e-boundaries.log, .antiphon/3904d52e-evidence-green.log (+2312); live process: claude.exe#4152,claude.exe#25428,claude.exe#11720,claude.exe#25196,claude.exe#10220,claude.exe#45180,claude.exe#49952,claude.exe#43980,claude.exe#104268,pwsh.exe#111732,cmd.exe#118848,Antiphon.SessionRunner.exe#131840,Antiphon.AppHost.exe#142512,pwsh.exe#24944,dotnet.exe#138284,node.exe#49024,esbuild.exe#98712,Antiphon.Server.exe#156120,node.exe#138752,node.exe#92556,esbuild.exe#90484,esbuild.exe#37848,cmd.exe#110792,Antiphon.Messaging.FakeGateway.exe#98492,claude.exe#141340 |
| `C:/src/Antiphon-card0417` | 1.01 |  |  | card-0417-phone | yes (origin branch == HEAD) |  | KEEP | no task owner (unknown/human tree); task state '' not terminal; terminal age h < 12h; 1 untracked files; ignored non-build content: .antiphon/evidence/, .antiphon/phone-browser.mjs, .antiphon/run-positive-controls.py (+3) |
| `C:/src/rc-bundle-c-final` | 0.16 |  |  | detached 30cc91b1d | yes (contained in origin/feat/card-task-021f53d8) |  | KEEP | no task owner (unknown/human tree); task state '' not terminal; terminal age h < 12h |
| `C:/src/rc-c1525` | 0.16 |  |  | detached 5a126f6fc | yes (contained in origin/feat/card-task-021f53d8) |  | KEEP | no task owner (unknown/human tree); task state '' not terminal; terminal age h < 12h |

## Release checkout clones (C:\Antiphon\releases)

| Path | Size GB | Run task | HEAD | Published | Dirty | Modified (UTC) | Verdict | Reason |
|---|---|---|---|---|---|---|---|---|
| `C:\Antiphon\releases\checkout-0297fa9c` | 4.07 | 0297fa9c | 3364197bf | yes (tip) | 0 | 11/08/2026 09:34:59 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-0b36ae35` | 4.39 | 0b36ae35 | 653f1145d | yes (tip) | 0 | 11/09/2026 11:32:30 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-136debc0` | 3.86 | 136debc0 | 679b696bd | yes (tip) | 0 | 11/01/2026 12:59:23 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-1d7cc1fe` | 4.95 | 1d7cc1fe | ab7af12c6 | yes (tip) | 0 | 11/13/2026 10:05:11 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-2271e6aa` | 3.93 | 2271e6aa | 1038d07e6 | yes (tip) | 0 | 11/06/2026 08:27:48 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-250a07fb` | 0.29 | 250a07fb | 87f4a868a | yes (tip) | 0 | 11/01/2026 10:25:04 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-25e35cdf` | 5.28 | 25e35cdf | 56e96eabf | yes (tip) | 0 | 11/12/2026 17:54:12 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-36d12cfa` | 4.04 | 36d12cfa | d164e93ae | yes (tip) | 0 | 11/07/2026 02:15:54 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-3ca79566` | 4.29 | 3ca79566 | 74a8004f4 | yes (tip) | 0 | 11/08/2026 02:34:29 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-42be26dd` | 4.47 | 42be26dd | 7bee43614 | yes (tip) | 0 | 11/09/2026 19:31:15 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-44f789f3` | 0.32 | 44f789f3 | 56e96eabf | yes (tip) | 0 | 11/12/2026 06:57:30 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-4dd2a426` | 4.48 | 4dd2a426 | ac6d00022 | yes (tip) | 0 | 11/10/2026 17:25:36 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-59e9866b` | 4.08 | 59e9866b | 0e7aef037 | yes (tip) | 0 | 11/07/2026 17:38:11 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-78b36185` | 3.76 | 78b36185 | e114313d3 | yes (tip) | 0 | 11/02/2026 10:31:46 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-8e08368a` | 4.96 | 8e08368a | b559bfba2 | yes (tip) | 0 | 11/14/2026 16:24:54 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-964900d9` | 4.92 | 964900d9 | a71c45ab4 | yes (tip) | 0 | 11/15/2026 20:57:42 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-c9cb914c` | 4.37 | c9cb914c | 5fef2c23b | yes (tip) | 0 | 11/08/2026 20:44:51 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout-ff89ca56` | 4.97 | ff89ca56 | 64e3d78c4 | yes (tip) | 0 | 11/15/2026 05:35:14 | DELETE | ended run, clean clone, HEAD on origin, no launch record of a live run or seed references it, no live process |
| `C:\Antiphon\releases\checkout` | 4.76 |  | 3db1a43b0 | yes (tip) | 0 | 11/11/2026 19:58:11 | KEEP | no run id (shared default checkout; path prefix of the live checkout) |
| `C:\Antiphon\releases\checkout-053aa2ea` | 4.79 | 053aa2ea | 9b98828eb | yes (tip) | 0 | 11/16/2026 12:23:01 | KEEP | LIVE run 053aa2ea checkout; live process: pwsh.exe#102860,pwsh.exe#154908,pwsh.exe#141204,Antiphon.E2E.exe#42948,Antiphon.SessionRunner.exe#48240,Antiphon.PtyHost.exe#56388,OpenConsole.exe#54868,fakegrok.exe#92980,dotnet.exe#148888,Antiphon.PtyHost.exe#79024,OpenConsole.exe#38720,fakegrok.exe#32432 |
| `C:\Antiphon\releases\checkout-1873094e` | 3.36 | 1873094e | f84228f28 | yes (tip) | 2 | 10/27/2026 22:15:00 | KEEP | 2 modified/untracked files |
| `C:\Antiphon\releases\checkout-1f33bc56` | 3.80 | 1f33bc56 | f32aed42e | yes (tip) | 1 | 11/04/2026 04:38:07 | KEEP | 1 modified/untracked files |
| `C:\Antiphon\releases\checkout-302ba3d2` | 3.88 | 302ba3d2 | 919d8d42f | yes (tip) | 4 | 11/03/2026 14:35:13 | KEEP | 4 modified/untracked files |
| `C:\Antiphon\releases\checkout-3ec2f72f` | 3.58 | 3ec2f72f | cde8def41 | yes (tip) | 5 | 10/30/2026 22:30:30 | KEEP | 5 modified/untracked files |
| `C:\Antiphon\releases\checkout-4dc48523` | 3.77 | 4dc48523 | ead93feb0 | yes (tip) | 5 | 11/02/2026 06:23:05 | KEEP | 5 modified/untracked files |
| `C:\Antiphon\releases\checkout-5906dbf2` | 3.48 | 5906dbf2 | 4563e5252 | yes (tip) | 7 | 10/29/2026 01:23:57 | KEEP | 7 modified/untracked files |
| `C:\Antiphon\releases\checkout-8049d727` | 4.05 | 8049d727 | 5a95ae7a5 | yes (tip) | 4 | 11/04/2026 18:52:38 | KEEP | 4 modified/untracked files |
| `C:\Antiphon\releases\checkout-8a7dbe6d` | 3.61 | 8a7dbe6d | 10a826b9d | yes (tip) | 1 | 11/03/2026 00:25:58 | KEEP | 1 modified/untracked files |
| `C:\Antiphon\releases\checkout-997186e6` | 3.47 | 997186e6 | 105792f7d | yes (tip) | 7 | 10/26/2026 22:06:38 | KEEP | 7 modified/untracked files |
| `C:\Antiphon\releases\checkout-ec8c178b` | 3.83 | ec8c178b | 147034055 | yes (tip) | 4 | 11/04/2026 12:20:41 | KEEP | 4 modified/untracked files |
| `C:\Antiphon\releases\checkout-f2e8ddf0` | 3.36 | f2e8ddf0 | a13e544e2 | yes (tip) | 2 | 10/26/2026 00:49:28 | KEEP | 2 modified/untracked files |

`task-<id>`, `state-<id>`, `candidates\*`, `.antiphon-test-evidence`, the land-graph canary and the loose rc-2026-10-18 logs are evidence and are not touched.

## Candidate evidence directories by run (operator decision; nothing deleted)

| Candidate | Modified (UTC) | Total GB | logs GB | Older than 14 days |
|---|---|---|---|---|
| rc-2026-10-18-1f72e20158b9 | 10/18/2026 23:18:41 | 0.05 | 0.05 | yes |
| rc-2026-10-18-42cbbd0417a2 | 10/19/2026 04:07:20 | 0.08 | 0.08 | yes |
| rc-2026-10-18-81cfb267a0af | 10/20/2026 17:40:40 | 0.01 | 0.01 | yes |
| rc-2026-10-18-a1d24b6fc92d | 10/18/2026 16:47:35 | 0.03 | 0.03 | yes |
| rc-2026-10-18-df6b422c71b0 | 10/19/2026 13:04:42 | 0.02 | 0.02 | yes |
| rc-2026-10-20-0714a9a89e4b | 10/22/2026 16:06:50 | 0.14 | 0.14 | yes |
| rc-2026-10-20-0b455324d2cb | 10/21/2026 11:07:02 | 0.04 | 0.04 | yes |
| rc-2026-10-20-5a126f6fc5ae | 10/23/2026 02:01:22 | 0.10 | 0.10 | yes |
| rc-2026-10-20-5acbd708d640 | 10/21/2026 07:15:07 | 0.03 | 0.03 | yes |
| rc-2026-10-20-66e9cfa9fc6b | 10/21/2026 15:50:19 | 0.06 | 0.06 | yes |
| rc-2026-10-20-91784f4d7898 | 10/20/2026 20:23:05 | 0.00 | 0.00 | yes |
| rc-2026-10-20-aeaf16647b51 | 10/21/2026 10:05:33 | 0.00 | 0.00 | yes |
| rc-2026-10-20-cadf547f7d69 | 10/21/2026 12:50:53 | 0.06 | 0.06 | yes |
| rc-2026-10-20-ceb6e3d6b784 | 10/22/2026 01:57:33 | 0.22 | 0.22 | yes |
| rc-2026-10-24-a13e544e2bae | 10/25/2026 13:12:03 | 0.39 | 0.39 | yes |
| rc-2026-10-24-a97194ee7a4a | 10/24/2026 07:20:39 | 0.51 | 0.51 | yes |
| rc-2026-10-25-105792f7d22f | 10/26/2026 09:46:21 | 0.54 | 0.54 | yes |
| rc-2026-10-27-4563e52529b2 | 10/28/2026 10:13:21 | 0.57 | 0.57 | yes |
| rc-2026-10-27-cde8def41638 | 10/30/2026 08:24:40 | 1.01 | 1.01 | yes |
| rc-2026-10-27-f84228f285dd | 10/27/2026 22:15:43 | 0.35 | 0.35 | yes |
| rc-2026-11-01-10a826b9d61f | 11/02/2026 13:13:56 | 0.63 | 0.63 | yes |
| rc-2026-11-01-679b696bd178 | 11/01/2026 11:37:57 | 0.69 | 0.69 | yes |
| rc-2026-11-01-87f4a868a86c | 11/01/2026 10:25:31 | 0.00 | 0.00 | yes |
| rc-2026-11-01-e114313d36ef | 11/02/2026 10:32:50 | 0.14 | 0.14 | yes |
| rc-2026-11-01-ead93feb00fc | 11/02/2026 05:15:07 | 0.18 | 0.18 | yes |
| rc-2026-11-03-919d8d42f14d | 11/03/2026 08:14:01 | 0.33 | 0.33 | no |
| rc-2026-11-04-f32aed42e58a | 11/03/2026 16:36:04 | 0.65 | 0.65 | no |
| rc-2026-11-05-147034055988 | 11/04/2026 10:18:13 | 0.31 | 0.31 | no |
| rc-2026-11-06-5a95ae7a5611 | 11/04/2026 18:08:52 | 1.29 | 1.29 | no |
| rc-2026-11-07-1038d07e66a5 | 11/06/2026 06:07:32 | 0.59 | 0.59 | no |
| rc-2026-11-08-d164e93ae47a | 11/06/2026 21:38:41 | 0.64 | 0.64 | no |
| rc-2026-11-09-0e7aef0379a0 | 11/07/2026 16:08:37 | 0.34 | 0.34 | no |
| rc-2026-11-10-74a8004f405b | 11/08/2026 01:44:28 | 0.30 | 0.30 | no |
| rc-2026-11-11-3364197bf1cb | 11/08/2026 09:36:18 | 0.36 | 0.36 | no |
| rc-2026-11-12-5fef2c23b894 | 11/08/2026 17:49:40 | 0.39 | 0.39 | no |
| rc-2026-11-13-653f1145de5b | 11/09/2026 04:40:56 | 0.40 | 0.40 | no |
| rc-2026-11-14-7bee43614495 | 11/09/2026 13:06:24 | 1.53 | 1.53 | no |
| rc-2026-11-15-ac6d00022e94 | 11/10/2026 12:48:54 | 1.54 | 1.54 | no |
| rc-2026-11-16-6f8741d872d6 | 11/11/2026 11:42:34 | 0.28 | 0.28 | no |
| rc-2026-11-17-3db1a43b0224 | 11/11/2026 19:07:48 | 1.56 | 1.56 | no |
| rc-2026-11-18-56e96eabf698 | 11/12/2026 12:13:59 | 1.54 | 1.54 | no |
| rc-2026-11-19-ab7af12c6990 | 11/13/2026 05:58:13 | 1.55 | 1.55 | no |
| rc-2026-11-20-b559bfba2e9a | 11/14/2026 10:40:03 | 1.62 | 1.62 | no |
| rc-2026-11-21-64e3d78c487d | 11/15/2026 04:33:56 | 1.63 | 1.63 | no |
| rc-2026-11-22-a71c45ab4d57 | 11/15/2026 20:30:44 | 0.64 | 0.64 | no |
| rc-2026-11-23-9b98828eb34e | 11/16/2026 10:37:42 | 0.21 | 0.21 | no |

Total candidates: 23.54 GB; older than 14 days: 25 dirs, 5.85 GB.


## Deletion plan (printed before execution)

Free on C: before deletion: 236679979008 bytes (220.4 GiB). Each line re-verifies task state, registration, HEAD, clean status, ignored content, live processes and origin publication immediately before acting, then removes that one path and checks it is gone. Worktrees use `git worktree remove` without `--force`; a local branch is deleted only when live origin has it at the same commit.

```powershell
Remove-TaskWorktree -Path 'C:/Antiphon/scratch/a37ceaf1-rc' -Id 'a37ceaf1' -Branch '' -ExpectedHead '12a0b107f4c896979dda94b4afb628842f601883' -SizeBytes 1726701795
Remove-TaskWorktree -Path 'C:/Antiphon/scratch/ae4df578-baseline' -Id 'ae4df578' -Branch '' -ExpectedHead 'a2aedb57fc98422a1c1723f4e1f0d4cef40d70f3' -SizeBytes 182167191
Remove-TaskWorktree -Path 'C:/Antiphon/scratch/ae4df578-rc' -Id 'ae4df578' -Branch '' -ExpectedHead '12a0b107f4c896979dda94b4afb628842f601883' -SizeBytes 338537725
Remove-TaskWorktree -Path 'C:/Antiphon/scratch/089ef857-master' -Id '089ef857' -Branch '' -ExpectedHead 'a2aedb57fc98422a1c1723f4e1f0d4cef40d70f3' -SizeBytes 351218536
Remove-TaskWorktree -Path 'C:/temp/antiphon-add64715-base' -Id 'add64715' -Branch '' -ExpectedHead 'ade54222543381e2e1b31ad79d8ae33a4035f70b' -SizeBytes 206394842
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-29664acc' -Id '29664acc' -Branch 'feat/card-task-29664acc' -ExpectedHead '1470340559889c2985ea722173181ea25c51b47c' -SizeBytes 214293125
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-e5258e8c' -Id 'e5258e8c' -Branch 'feat/card-task-e5258e8c' -ExpectedHead '5449f8f50b9272d6ea468920725724ec88c56d68' -SizeBytes 214325646
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-dd35b058' -Id 'dd35b058' -Branch 'feat/card-task-dd35b058' -ExpectedHead '96882045e00c74f60f72a52aad51e47789b9d8a4' -SizeBytes 214381271
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-314297be' -Id '314297be' -Branch 'feat/card-task-314297be' -ExpectedHead '96882045e00c74f60f72a52aad51e47789b9d8a4' -SizeBytes 214381271
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f303b3ec' -Id 'f303b3ec' -Branch 'feat/card-task-f303b3ec' -ExpectedHead 'b5f97136167d942484aa23ea8660cf50cc800eff' -SizeBytes 214373316
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-56bf971d' -Id '56bf971d' -Branch 'feat/card-task-56bf971d' -ExpectedHead 'da0106cbe7c500bade6659d4700b4a6cabe2b148' -SizeBytes 214427968
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-a41890aa' -Id 'a41890aa' -Branch 'feat/card-task-a41890aa' -ExpectedHead '2e07f8d38bf520817bc25f317835831141825c69' -SizeBytes 214494447
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-801ad6c3' -Id '801ad6c3' -Branch 'feat/card-task-801ad6c3' -ExpectedHead '1ef88240b3c5ee2c759db958e5fb0b635b1248cc' -SizeBytes 214530627
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f2976027' -Id 'f2976027' -Branch 'feat/card-task-f2976027' -ExpectedHead '5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a' -SizeBytes 214530627
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-d2564da4' -Id 'd2564da4' -Branch 'feat/card-task-d2564da4' -ExpectedHead '84e19071e25abc7a595fddfac145b187dc5f2d11' -SizeBytes 214540480
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-e24018e3' -Id 'e24018e3' -Branch 'feat/card-task-e24018e3' -ExpectedHead '240231e86c90707cd6e0d0c88affcd5bddfb4247' -SizeBytes 214626454
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-3fd6747e' -Id '3fd6747e' -Branch 'feat/card-task-3fd6747e' -ExpectedHead 'd95d3f2cdce02fd95bf2145ecee979769cfc4c3c' -SizeBytes 214702620
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-7c7132b3' -Id '7c7132b3' -Branch 'feat/card-task-7c7132b3' -ExpectedHead 'd95d3f2cdce02fd95bf2145ecee979769cfc4c3c' -SizeBytes 214702620
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-613f2230' -Id '613f2230' -Branch 'feat/card-task-613f2230' -ExpectedHead 'd95d3f2cdce02fd95bf2145ecee979769cfc4c3c' -SizeBytes 214702620
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-605c6f4e' -Id '605c6f4e' -Branch 'feat/card-task-605c6f4e' -ExpectedHead '6cda98f3c7797c4b9b3619b45e94b067efb708f2' -SizeBytes 214719220
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-b5b7324a' -Id 'b5b7324a' -Branch 'feat/card-task-b5b7324a' -ExpectedHead '467b00c25ba04fb2a6cbe583079e06211c93c8a7' -SizeBytes 214768443
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-1edbc845' -Id '1edbc845' -Branch 'feat/card-task-1edbc845' -ExpectedHead '249d4654d2410c4a3959a492ee0165e8a75b75f9' -SizeBytes 214722660
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-8592d536' -Id '8592d536' -Branch 'feat/card-task-8592d536' -ExpectedHead '5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a' -SizeBytes 214530627
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-64fb3f5d' -Id '64fb3f5d' -Branch 'feat/card-task-64fb3f5d' -ExpectedHead 'cc8845f030d934507e7a0d2db7d939be06d9f2bc' -SizeBytes 214734057
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f674c79a' -Id 'f674c79a' -Branch 'feat/card-task-f674c79a' -ExpectedHead 'f6f380c08e3468a5b98415e14f72380377fc6f6e' -SizeBytes 215260024
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-cfccf493' -Id 'cfccf493' -Branch 'feat/card-task-cfccf493' -ExpectedHead '3ce7f87ef06315ace62572d7e51b2de0347dbef9' -SizeBytes 214736448
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-3b906877' -Id '3b906877' -Branch 'feat/card-task-3b906877' -ExpectedHead 'd10e1c51a72852aa51895109b66e567f81346dca' -SizeBytes 214804294
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-1e279cab' -Id '1e279cab' -Branch 'feat/card-task-1e279cab' -ExpectedHead '93efbc748def106ce2c2ca15d56589142c46986b' -SizeBytes 217861390
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-a9b46304' -Id 'a9b46304' -Branch 'feat/card-task-a9b46304' -ExpectedHead '93efbc748def106ce2c2ca15d56589142c46986b' -SizeBytes 217861390
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-b7c4a9b7' -Id 'b7c4a9b7' -Branch 'feat/card-task-b7c4a9b7' -ExpectedHead '93efbc748def106ce2c2ca15d56589142c46986b' -SizeBytes 217861390
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-50e9a890' -Id '50e9a890' -Branch 'feat/card-task-50e9a890' -ExpectedHead '93efbc748def106ce2c2ca15d56589142c46986b' -SizeBytes 217861390
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-555ce937' -Id '555ce937' -Branch 'feat/card-task-555ce937' -ExpectedHead 'df1fc145181a3bedf444f9113f822f30d3b136f6' -SizeBytes 218593413
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-02f02e8a' -Id '02f02e8a' -Branch 'feat/card-task-02f02e8a' -ExpectedHead '1c8610ffb1b6b101fd99f1666ff4d790c0d7ea8f' -SizeBytes 218609396
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-642a3764' -Id '642a3764' -Branch 'feat/card-task-642a3764' -ExpectedHead '5d5973be2cd867bf592cd0363240e728f64d3b28' -SizeBytes 220767667
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-20805524' -Id '20805524' -Branch 'feat/card-task-20805524' -ExpectedHead '1394d6e3db90d12ec38de564e7042c1d4603ee2c' -SizeBytes 220812141
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-e7d7d78b' -Id 'e7d7d78b' -Branch 'feat/card-task-e7d7d78b' -ExpectedHead 'edb2c20156afa4da503b1b29fb83f1f953987bd5' -SizeBytes 220826289
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-5a2fffb9' -Id '5a2fffb9' -Branch 'feat/card-task-5a2fffb9' -ExpectedHead '6d5531762633dbb9077739cb451386c6c2131255' -SizeBytes 220889622
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-bc7a9fdd' -Id 'bc7a9fdd' -Branch 'feat/card-task-bc7a9fdd' -ExpectedHead 'b8fe734030a7a2d08f4a45d090326ccd576b9fe7' -SizeBytes 220902654
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-4f5166ec' -Id '4f5166ec' -Branch 'feat/card-task-4f5166ec' -ExpectedHead '0e7aef0379a073c206e8398dcaed539bc33eaf9b' -SizeBytes 221051550
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f442a78f' -Id 'f442a78f' -Branch 'feat/card-task-f442a78f' -ExpectedHead 'b2733b00ccec9a7ca69f14bcf2145cdc881a3d33' -SizeBytes 220846205
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-c7a30141' -Id 'c7a30141' -Branch 'feat/card-task-c7a30141' -ExpectedHead '1705a3ee8c8102562addd96f65fe4c8c94e34ced' -SizeBytes 223436788
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f87fdda9' -Id 'f87fdda9' -Branch 'feat/card-task-f87fdda9' -ExpectedHead 'b1e129b44855a4c6682a3b1fecc8fb445be7e11a' -SizeBytes 223854188
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-5b1ea772' -Id '5b1ea772' -Branch 'feat/card-task-5b1ea772' -ExpectedHead '0f76e7b0a9379345684dbb4f5452073e53eb82f4' -SizeBytes 223815581
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-ec9dfd70' -Id 'ec9dfd70' -Branch 'feat/card-task-ec9dfd70' -ExpectedHead '3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e' -SizeBytes 223854701
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-4292c713' -Id '4292c713' -Branch 'feat/card-task-4292c713' -ExpectedHead '7f938e410f9f196e2b8b1dd9617276704c57c5f8' -SizeBytes 224138968
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-a2845b6c' -Id 'a2845b6c' -Branch 'feat/card-task-a2845b6c' -ExpectedHead 'df1516b315860aec4cad872114ec4b6d4a0ef272' -SizeBytes 224124730
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-18dd27d8' -Id '18dd27d8' -Branch 'feat/card-task-18dd27d8' -ExpectedHead 'e99271f377ba5de8acb0f7b51abd1512f34fad91' -SizeBytes 224155329
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-ed0f7942' -Id 'ed0f7942' -Branch 'feat/card-task-ed0f7942' -ExpectedHead '7624fb6820a48805c2fb23c9d7e38f324f7cece2' -SizeBytes 224393118
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-1af450d3' -Id '1af450d3' -Branch 'feat/card-task-1af450d3' -ExpectedHead '7624fb6820a48805c2fb23c9d7e38f324f7cece2' -SizeBytes 224393118
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f1fde86b' -Id 'f1fde86b' -Branch 'feat/card-task-f1fde86b' -ExpectedHead '7624fb6820a48805c2fb23c9d7e38f324f7cece2' -SizeBytes 224393118
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-7555284f' -Id '7555284f' -Branch 'feat/card-task-7555284f' -ExpectedHead 'cfefd4b64ee4ba7cb50c1edd699cca448d0e3f1c' -SizeBytes 224161057
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-da90742c' -Id 'da90742c' -Branch 'feat/card-task-da90742c' -ExpectedHead '584224f0eeefad8455da671528804e9fcfdc2e65' -SizeBytes 224431993
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-2abcd1c0' -Id '2abcd1c0' -Branch 'feat/card-task-2abcd1c0' -ExpectedHead '1e353c7dd75a9fcc1c4651cfed12b62a4508f81e' -SizeBytes 224117738
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-bee64303' -Id 'bee64303' -Branch 'feat/card-task-bee64303' -ExpectedHead '5fef2c23b89414b8818aaf40db9ad738bce9524c' -SizeBytes 224437194
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-c6ca6929' -Id 'c6ca6929' -Branch 'feat/card-task-c6ca6929' -ExpectedHead 'a5aff46b35d551b798dac2bcc71738cabe926fdb' -SizeBytes 225060732
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-934cdb25' -Id '934cdb25' -Branch 'feat/card-task-934cdb25' -ExpectedHead 'c126f1b6cddde68db926b2c35e1a6044296824e9' -SizeBytes 225102776
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-6155693e' -Id '6155693e' -Branch 'feat/card-task-6155693e' -ExpectedHead '87000d366861f8c5281e83ed92b57a66d9fd8860' -SizeBytes 225143148
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-94b17435' -Id '94b17435' -Branch 'feat/card-task-94b17435' -ExpectedHead 'cad7766950ee96edc13e6ebf805ec5b4bbebbd6f' -SizeBytes 225183234
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-b0e54eca' -Id 'b0e54eca' -Branch 'feat/card-task-b0e54eca' -ExpectedHead 'c599061752fce18db7e7e2ea64cd87d3b365fb02' -SizeBytes 225884088
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-01f26ecf' -Id '01f26ecf' -Branch 'feat/card-task-01f26ecf' -ExpectedHead 'e25cebdf5f2c357c36f515de3836389c8379db97' -SizeBytes 227212592
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-9294312a' -Id '9294312a' -Branch 'feat/card-task-9294312a' -ExpectedHead '013d29a0a9d2f8ebe90e9edb7766e0922c439c65' -SizeBytes 229749117
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-06aa57e7' -Id '06aa57e7' -Branch 'feat/card-task-06aa57e7' -ExpectedHead '618d03c66dc8aeb41a8ee576e9266e5f5560fb2b' -SizeBytes 230365072
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-149cb7c3' -Id '149cb7c3' -Branch 'feat/card-task-149cb7c3' -ExpectedHead 'd5bf65756462839f68ef48898700bee9bcdf283e' -SizeBytes 230484181
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-d989d9d8' -Id 'd989d9d8' -Branch 'feat/card-task-d989d9d8' -ExpectedHead '56e96eabf698e9c1fcd462836ad32ed1fb259e83' -SizeBytes 230471349
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-87596cc5' -Id '87596cc5' -Branch 'feat/card-task-87596cc5' -ExpectedHead '422e30c87db4453eaea0e5dfccdd10c37d7d116f' -SizeBytes 230155189
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-25d9a69f' -Id '25d9a69f' -Branch 'feat/card-task-25d9a69f' -ExpectedHead '422e30c87db4453eaea0e5dfccdd10c37d7d116f' -SizeBytes 230155189
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-3199d57c' -Id '3199d57c' -Branch 'feat/card-task-3199d57c' -ExpectedHead '27d8bdcf29e5fa9e797e81121b02e6ce5fb212cb' -SizeBytes 230885314
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-dc70c718' -Id 'dc70c718' -Branch 'feat/card-task-dc70c718' -ExpectedHead '89f64a6b0d2b1c72bfc35e2630ec8038cf143961' -SizeBytes 235101527
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f3a457ae' -Id 'f3a457ae' -Branch 'feat/card-task-f3a457ae' -ExpectedHead 'a4975c8e79b97fc248f6fabc8db8a1e9270a9201' -SizeBytes 235377128
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-b4f39ca7' -Id 'b4f39ca7' -Branch 'feat/card-task-b4f39ca7' -ExpectedHead '9494c5e1ac16c5397cf14c8b017de19b23135212' -SizeBytes 1125852843
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-ec0488ff' -Id 'ec0488ff' -Branch 'feat/card-task-ec0488ff' -ExpectedHead '1f44a31a694edab5a1f3ce160b9a0505ef3adf43' -SizeBytes 234915338
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-31c683a8' -Id '31c683a8' -Branch 'feat/card-task-31c683a8' -ExpectedHead 'd7fac40684e6bf9abf5049055767ac238dfad7f4' -SizeBytes 2447139216
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-cbedf221' -Id 'cbedf221' -Branch 'feat/card-task-cbedf221' -ExpectedHead '966c684fc2c42bc9aabbc46a774cf77b087b926d' -SizeBytes 235389938
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-ca8076ae' -Id 'ca8076ae' -Branch 'feat/card-task-ca8076ae' -ExpectedHead '8cec1e74e75819ef20756c228add987ce4ae4170' -SizeBytes 235383485
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-ec4754a8' -Id 'ec4754a8' -Branch 'feat/card-task-ec4754a8' -ExpectedHead 'd403313ab8d631effa851f46dc7cedad356ee8e4' -SizeBytes 235396899
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-6f663ce6' -Id '6f663ce6' -Branch 'feat/card-task-6f663ce6' -ExpectedHead 'e8af418d2f1bd92a0f0d22ac4cf4cc3bde789ed6' -SizeBytes 235600679
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-6eccb98b' -Id '6eccb98b' -Branch 'feat/card-task-6eccb98b' -ExpectedHead '8cec1e74e75819ef20756c228add987ce4ae4170' -SizeBytes 235383485
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-35eb006c' -Id '35eb006c' -Branch 'feat/card-task-35eb006c' -ExpectedHead 'c280e7c79ddcc72d597a960ad71059f5d7dacad8' -SizeBytes 1361630054
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-8de3f823' -Id '8de3f823' -Branch 'feat/card-task-8de3f823' -ExpectedHead 'eeddd04645e8d7c3720bf42ee221a77b7e1b6850' -SizeBytes 235444422
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-29d177ef' -Id '29d177ef' -Branch 'feat/card-task-29d177ef' -ExpectedHead '43bf2c9643be8273e781a6bb7daa49a61f614f5a' -SizeBytes 235395914
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-d684bc25' -Id 'd684bc25' -Branch 'feat/card-task-d684bc25' -ExpectedHead '17b511f68c636135bee82db6fec63f1b152f33df' -SizeBytes 235441625
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-ab7e65ba' -Id 'ab7e65ba' -Branch 'feat/card-task-ab7e65ba' -ExpectedHead '8cec1e74e75819ef20756c228add987ce4ae4170' -SizeBytes 235383485
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-9325be95' -Id '9325be95' -Branch 'feat/card-task-9325be95' -ExpectedHead 'eeddd04645e8d7c3720bf42ee221a77b7e1b6850' -SizeBytes 235444422
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-9c1feeba' -Id '9c1feeba' -Branch 'feat/card-task-9c1feeba' -ExpectedHead 'ec0bb426228e410e50065789af8c980aaf2afaf7' -SizeBytes 235413725
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-3c766de7' -Id '3c766de7' -Branch 'feat/card-task-3c766de7' -ExpectedHead '17b511f68c636135bee82db6fec63f1b152f33df' -SizeBytes 235441625
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-8998668f' -Id '8998668f' -Branch 'feat/card-task-8998668f' -ExpectedHead 'e615e445186eea3036d4ea22dd0a923c1148b86d' -SizeBytes 2447877217
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-28f886d2' -Id '28f886d2' -Branch 'feat/card-task-28f886d2' -ExpectedHead 'f8c5cd7f08f8748a77d9500a72878544d740449a' -SizeBytes 2448083405
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-3afa3cfa' -Id '3afa3cfa' -Branch 'feat/card-task-3afa3cfa' -ExpectedHead '7b846c030d69503bf3672443982dafbb025d76cf' -SizeBytes 235424419
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-5c3cf9f6' -Id '5c3cf9f6' -Branch 'feat/card-task-5c3cf9f6' -ExpectedHead 'e234faef00e8ec2648094e3e0d8b70eda69af395' -SizeBytes 235393981
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-fc30e791' -Id 'fc30e791' -Branch 'feat/card-task-fc30e791' -ExpectedHead 'f701b2ba967d503427d7064fe4fffa17b8c1738d' -SizeBytes 235424483
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-15406ecc' -Id '15406ecc' -Branch 'feat/card-task-15406ecc' -ExpectedHead '5ae64d98535261d82a979814e6e522352441edd5' -SizeBytes 235629991
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-abd9b404' -Id 'abd9b404' -Branch 'feat/card-task-abd9b404' -ExpectedHead 'd144f8d25931506878e14c5a240af9a157d1d52f' -SizeBytes 235441292
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-5675aac7' -Id '5675aac7' -Branch 'feat/card-task-5675aac7' -ExpectedHead '919fb8cc35dbf0e0ca34ca290d22f3bf3d16d903' -SizeBytes 236080416
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-856fac4d' -Id '856fac4d' -Branch 'feat/card-task-856fac4d' -ExpectedHead '27a43f25221c1508f12a2c6a4b89500d0050986a' -SizeBytes 235444600
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-66eb7e18' -Id '66eb7e18' -Branch 'feat/card-task-66eb7e18' -ExpectedHead '919fb8cc35dbf0e0ca34ca290d22f3bf3d16d903' -SizeBytes 236080416
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f6d722a9' -Id 'f6d722a9' -Branch 'feat/card-task-f6d722a9' -ExpectedHead '21c9f51b4a01c3bd07547c46a5e6da4e58e6c243' -SizeBytes 235463344
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-ac7f437c' -Id 'ac7f437c' -Branch 'feat/card-task-ac7f437c' -ExpectedHead '9c3dc075d2085e41d21e26bc9b6bc3afc36bad03' -SizeBytes 235509791
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-93af5925' -Id '93af5925' -Branch 'feat/card-task-93af5925' -ExpectedHead '02b9f9218c1c1d1e3ff567d847aaf6c7626384ee' -SizeBytes 235488690
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-68b66c6d' -Id '68b66c6d' -Branch 'feat/card-task-68b66c6d' -ExpectedHead '8b5afc6ba560e612a75595a83954589e5453d9cf' -SizeBytes 235488690
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-882cae23' -Id '882cae23' -Branch 'feat/card-task-882cae23' -ExpectedHead '557a17154e7231d45b09173011b5cf5a531ba841' -SizeBytes 236087398
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-0055a160' -Id '0055a160' -Branch 'feat/card-task-0055a160' -ExpectedHead '2dc164c6adf2308777909389024bbbca51f47117' -SizeBytes 235473346
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-1a125507' -Id '1a125507' -Branch 'feat/card-task-1a125507' -ExpectedHead '3a8e115bb54847458f9495e82e1a9cc05642f039' -SizeBytes 235735776
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-18072e35' -Id '18072e35' -Branch 'feat/card-task-18072e35' -ExpectedHead 'cb2210e934b040a69ed4563a4ffa40f4f1b0d91c' -SizeBytes 235521827
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-b0ec9aab' -Id 'b0ec9aab' -Branch 'feat/card-task-b0ec9aab' -ExpectedHead '3a8e115bb54847458f9495e82e1a9cc05642f039' -SizeBytes 235735776
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-1637311a' -Id '1637311a' -Branch 'feat/card-task-1637311a' -ExpectedHead '92cadbb21df1d47d830c4e2df4a50f3d84539525' -SizeBytes 236185621
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-9aa09d20' -Id '9aa09d20' -Branch 'feat/card-task-9aa09d20' -ExpectedHead '22b84b7ebb462316db6e4b3e561dcc08cd1dc0e1' -SizeBytes 236199155
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-0704a004' -Id '0704a004' -Branch 'feat/card-task-0704a004' -ExpectedHead 'fc92658cb88fb282455230b8e04595df78e2a58d' -SizeBytes 236572108
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-c51e92c8' -Id 'c51e92c8' -Branch 'feat/card-task-c51e92c8' -ExpectedHead '38ff81666342d31f08176e27d3adcaabbf4185fd' -SizeBytes 2799002285
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-ce8ef291' -Id 'ce8ef291' -Branch 'feat/card-task-ce8ef291' -ExpectedHead 'e8ca4e7eadc9c50539deefac7c408c764e5aa645' -SizeBytes 236586185
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-198a988b' -Id '198a988b' -Branch 'feat/card-task-198a988b' -ExpectedHead '832ffb1fb5dcbcd203e0ef55190063779ab243aa' -SizeBytes 236584747
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-24cf8f11' -Id '24cf8f11' -Branch 'feat/card-task-24cf8f11' -ExpectedHead '08cc3dcac230658c9bb7f580d32ad91906665fbd' -SizeBytes 236598824
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-593c280c' -Id '593c280c' -Branch 'feat/card-task-593c280c' -ExpectedHead '08cc3dcac230658c9bb7f580d32ad91906665fbd' -SizeBytes 236598824
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-48728776' -Id '48728776' -Branch 'feat/card-task-48728776' -ExpectedHead '08cc3dcac230658c9bb7f580d32ad91906665fbd' -SizeBytes 236598824
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-1ba18e23' -Id '1ba18e23' -Branch 'feat/card-task-1ba18e23' -ExpectedHead '714b6b38a8ee265bda16359d9ecbb630bc0a0868' -SizeBytes 236611449
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-93f5087b' -Id '93f5087b' -Branch 'feat/card-task-93f5087b' -ExpectedHead 'ba1bd8d18cac7ca4252c6cc452d841fa33ff2d77' -SizeBytes 236610193
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-2b6dde13' -Id '2b6dde13' -Branch 'feat/card-task-2b6dde13' -ExpectedHead '4a2ea08d4fb6bb01aefeb7c37388813318944e6a' -SizeBytes 236613565
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-78d029fc' -Id '78d029fc' -Branch 'feat/card-task-78d029fc' -ExpectedHead '4ebf513703f0f0ae6a68b8edb2ad1389c223e3dc' -SizeBytes 236636968
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-1905deba' -Id '1905deba' -Branch 'feat/card-task-1905deba' -ExpectedHead '083d7b76331e8ecf512ddb63808f000616ad0c0f' -SizeBytes 236605328
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-7342010d' -Id '7342010d' -Branch 'feat/card-task-7342010d' -ExpectedHead '79bb6e9b6a11474873f6d8678db7d874c2f0078d' -SizeBytes 236623823
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-24eb2f3d' -Id '24eb2f3d' -Branch 'feat/card-task-24eb2f3d' -ExpectedHead 'd7ad08b71a19fa79e744284e5ffe7f0168de561a' -SizeBytes 236622818
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-b1318d57' -Id 'b1318d57' -Branch 'feat/card-task-b1318d57' -ExpectedHead '257dd5e74fce59af0bcb28b293bb2a3f297c8a68' -SizeBytes 236612817
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-8d8824ed' -Id '8d8824ed' -Branch 'feat/card-task-8d8824ed' -ExpectedHead 'd252de5a10ddaca93d0b3740d29a7cc28787bf66' -SizeBytes 236603794
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-27128425' -Id '27128425' -Branch 'feat/card-task-27128425' -ExpectedHead 'e2d0aceda0ea9f508a440ce589f03a76e05422ef' -SizeBytes 236635992
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-b2663606' -Id 'b2663606' -Branch 'feat/card-task-b2663606' -ExpectedHead '9a83e86ea560721a4b06e8e357f3f8b45f019c84' -SizeBytes 236628463
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-8112d494' -Id '8112d494' -Branch 'feat/card-task-8112d494' -ExpectedHead 'eb7bb5473d5c789e6d930087a0d0c37b5fef4824' -SizeBytes 236642993
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-cd9133eb' -Id 'cd9133eb' -Branch 'feat/card-task-cd9133eb' -ExpectedHead '64e3d78c487d51570f708492ab67b5db6d0de9a9' -SizeBytes 236656522
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-5786ffb9' -Id '5786ffb9' -Branch 'feat/card-task-5786ffb9' -ExpectedHead 'eca2abbd6ffddd49794e9dc3fec9a44d55b47af0' -SizeBytes 236614044
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-33039320' -Id '33039320' -Branch 'feat/card-task-33039320' -ExpectedHead '8b16069d906e3bab738d1b58777d28532638f1d7' -SizeBytes 236635456
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-4f8dc817' -Id '4f8dc817' -Branch 'feat/card-task-4f8dc817' -ExpectedHead 'f5b6e778d7a3709c5d00c30dc3c6181918fd735d' -SizeBytes 236647692
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-12311088' -Id '12311088' -Branch 'feat/card-task-12311088' -ExpectedHead 'e4be192eee36bf6d38a5f27b9347362e283a5b4b' -SizeBytes 236628945
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-d5ec6eff' -Id 'd5ec6eff' -Branch 'feat/card-task-d5ec6eff' -ExpectedHead '46234d5e050635235a154efd87fa0abc0ecdb2ce' -SizeBytes 236658667
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-bf127f48' -Id 'bf127f48' -Branch 'feat/card-task-bf127f48' -ExpectedHead '7e19abd0fe0d8432f81048f892381ef51747bdc0' -SizeBytes 236654554
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-665816dd' -Id '665816dd' -Branch 'feat/card-task-665816dd' -ExpectedHead '3f1f78550e7afe6bdcacdb79e16549ac97706f0b' -SizeBytes 236689243
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-00011235' -Id '00011235' -Branch 'feat/card-task-00011235' -ExpectedHead '4ce55f36794f3e2fa904d56ba980f4182d89ab3a' -SizeBytes 236734885
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-7ac938e9' -Id '7ac938e9' -Branch 'feat/card-task-7ac938e9' -ExpectedHead 'b9cf498c77d40bf14260cb6003ba0a8c5610cf76' -SizeBytes 2432524465
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-c3d468f8' -Id 'c3d468f8' -Branch 'feat/card-task-c3d468f8' -ExpectedHead 'b9cf498c77d40bf14260cb6003ba0a8c5610cf76' -SizeBytes 236665515
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-0bd83714' -Id '0bd83714' -Branch 'feat/card-task-0bd83714' -ExpectedHead 'c5cb89dc9fccfd7d09faf22d4b9d7c68e071cde5' -SizeBytes 236673780
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f1cf9f3c' -Id 'f1cf9f3c' -Branch 'feat/card-task-f1cf9f3c' -ExpectedHead '8cd52326ee640a946ffbb64cbe70cb0de5c5428d' -SizeBytes 236747322
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-73e7dfdd' -Id '73e7dfdd' -Branch 'feat/card-task-73e7dfdd' -ExpectedHead 'a5bac0cb8d52ff5d3796971f5ba7fca2a68e2065' -SizeBytes 236712537
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-7708d552' -Id '7708d552' -Branch 'feat/card-task-7708d552' -ExpectedHead '2972ffa395c77b0776fd429b2d0ca0a0d444baa7' -SizeBytes 236761187
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-bffbe413' -Id 'bffbe413' -Branch 'feat/card-task-bffbe413' -ExpectedHead 'c4c76d8a4c822a848f6ca498497338522082e5d4' -SizeBytes 391608684
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-5c14b3c5' -Id '5c14b3c5' -Branch 'feat/card-task-5c14b3c5' -ExpectedHead '6bdee153c4ba082aa06bfb9fd00933e83a2d667c' -SizeBytes 236746421
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-b440ac48' -Id 'b440ac48' -Branch 'feat/card-task-b440ac48' -ExpectedHead 'fb58d323a432f495d4af3ffae950a1df7cb491d1' -SizeBytes 236670587
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f21bf3ba' -Id 'f21bf3ba' -Branch 'feat/card-task-f21bf3ba' -ExpectedHead '632ebf203404be6f32e3b44e4da95587af0a5934' -SizeBytes 236742826
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f9e020bf' -Id 'f9e020bf' -Branch 'feat/card-task-f9e020bf' -ExpectedHead '8951eb01d00716052df3c4e37c22655eebb4d8e9' -SizeBytes 2557815145
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-daf2ea47' -Id 'daf2ea47' -Branch 'feat/card-task-daf2ea47' -ExpectedHead '1bd7b2ff62411fe0f784df425d4949c850ad5cd7' -SizeBytes 236789657
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-7bb75309' -Id '7bb75309' -Branch 'feat/card-task-7bb75309' -ExpectedHead 'ec1188b6d31bb0c4a321f4ee3312ed04e6fcbddd' -SizeBytes 236767272
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-e594649c' -Id 'e594649c' -Branch 'feat/card-task-e594649c' -ExpectedHead 'e791df1b7970038b167488f187c77fde01a7a004' -SizeBytes 236132511
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-484dd12d' -Id '484dd12d' -Branch 'feat/card-task-484dd12d' -ExpectedHead 'a0dc5a3e136cbb5cf101052ae4d6a3712b23e97b' -SizeBytes 236719590
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-cf50eda6' -Id 'cf50eda6' -Branch 'feat/card-task-cf50eda6' -ExpectedHead '529faec3f35b16c2b3b0c2730ab055e4678b7897' -SizeBytes 236753562
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-9248daa5' -Id '9248daa5' -Branch 'feat/card-task-9248daa5' -ExpectedHead '95a55eed13308806b4b19aad2440d12babd5cb51' -SizeBytes 236784161
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-e479506f' -Id 'e479506f' -Branch 'feat/card-task-e479506f' -ExpectedHead 'b6531be930e85d14053d1b0381ca1bc932f76276' -SizeBytes 236140135
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-6430f6eb' -Id '6430f6eb' -Branch 'feat/card-task-6430f6eb' -ExpectedHead 'ecabfb26e5a8ed7605824c1f6f7b7916bd16c175' -SizeBytes 236791506
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-3f159277' -Id '3f159277' -Branch 'feat/card-task-3f159277' -ExpectedHead 'd100f8fa2c679a5c74b6b235087308776a8f5d50' -SizeBytes 236854882
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-7a422522' -Id '7a422522' -Branch 'feat/card-task-7a422522' -ExpectedHead '982464a081e23fda47fe61d409d6a2dde211dec9' -SizeBytes 236870056
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-1c76938a' -Id '1c76938a' -Branch 'feat/card-task-1c76938a' -ExpectedHead 'c4eeff2e35e310698e2fd85e83271f49f5881895' -SizeBytes 236150589
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-c6da537d' -Id 'c6da537d' -Branch 'feat/card-task-c6da537d' -ExpectedHead '7ac976b596b4c8251bf352f291f0f040a6c9547e' -SizeBytes 236166709
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-ac8b9d8e' -Id 'ac8b9d8e' -Branch 'feat/card-task-ac8b9d8e' -ExpectedHead '946bd856d32183350ab2a1f0bed6dbe337bb7e4a' -SizeBytes 236873582
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-3ea0f6b5' -Id '3ea0f6b5' -Branch 'feat/card-task-3ea0f6b5' -ExpectedHead '8359e1b783110d14fc1192980fe7e5c920b02bbd' -SizeBytes 236867168
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-28eecdf5' -Id '28eecdf5' -Branch 'feat/card-task-28eecdf5' -ExpectedHead '34d62eb94c0397075910c5c75b68cfb10c902bae' -SizeBytes 236931080
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-8f001b6d' -Id '8f001b6d' -Branch 'feat/card-task-8f001b6d' -ExpectedHead 'cd6458f5e74e1d5cce690aef193922ee1ea9eca3' -SizeBytes 236896173
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-4c25994e' -Id '4c25994e' -Branch 'feat/card-task-4c25994e' -ExpectedHead '914cc241355c51c5c864bfd7e8d55a48702196c7' -SizeBytes 236177384
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-77ab4e11' -Id '77ab4e11' -Branch 'feat/card-task-77ab4e11' -ExpectedHead '360baf90bea12f3a1f802cdafff4361fb9f259be' -SizeBytes 237815273
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-c2489e73' -Id 'c2489e73' -Branch 'feat/card-task-c2489e73' -ExpectedHead 'a44117fb92499c1d95e21bbfa49642214cab175d' -SizeBytes 236971006
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-baba7a4e' -Id 'baba7a4e' -Branch 'feat/card-task-baba7a4e' -ExpectedHead '0112f4adff001a7ee36a69191cd27dbb8bc56f73' -SizeBytes 236942730
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-d20d7003' -Id 'd20d7003' -Branch 'feat/card-task-d20d7003' -ExpectedHead '9083d4d003b246f9ab191a7be87997abb77bd1df' -SizeBytes 237891471
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-68f81b18' -Id '68f81b18' -Branch 'feat/card-task-68f81b18' -ExpectedHead '7c91620132fb2636f0b31e09b9046983875f8d38' -SizeBytes 237917047
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-f38e63e0' -Id 'f38e63e0' -Branch 'feat/card-task-f38e63e0' -ExpectedHead '9083d4d003b246f9ab191a7be87997abb77bd1df' -SizeBytes 237891471
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-8d020b07' -Id '8d020b07' -Branch 'feat/card-task-8d020b07' -ExpectedHead 'a71c45ab4d5703d480f4eee4c4e30adab45bc4f9' -SizeBytes 237917047
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-1d838cd1' -Id '1d838cd1' -Branch 'feat/card-task-1d838cd1' -ExpectedHead 'bbd150e8223063922edf076e1c374d95b9c5c45c' -SizeBytes 237900385
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-bd9e5cb5' -Id 'bd9e5cb5' -Branch 'feat/card-task-bd9e5cb5' -ExpectedHead 'ac735d1d3d2574c6683f600e7a1c184ee16c4b55' -SizeBytes 237904431
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-3820a5f1' -Id '3820a5f1' -Branch 'feat/card-task-3820a5f1' -ExpectedHead '4e16fc96cea6d89ff8612550d3c7a4d97abe3fcb' -SizeBytes 237906990
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-5ca0e7b6' -Id '5ca0e7b6' -Branch 'feat/card-task-5ca0e7b6' -ExpectedHead '43471713c3785c5095ac0523d9b9e6fde5170dc9' -SizeBytes 237919957
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-870a2044' -Id '870a2044' -Branch 'feat/card-task-870a2044' -ExpectedHead '8ae67acecf7a7bd9d2b59d0613b7561d6b8d9066' -SizeBytes 237923441
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-dc726c22' -Id 'dc726c22' -Branch 'feat/card-task-dc726c22' -ExpectedHead '8ac04f9e6d5e14fd98dea2db197a2e8c8c55a63f' -SizeBytes 237921549
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-22ee1e10' -Id '22ee1e10' -Branch 'feat/card-task-22ee1e10' -ExpectedHead 'e67cc3f0960432d39aa7aa7c33aecd05056f92fe' -SizeBytes 237928058
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-23686c59' -Id '23686c59' -Branch 'feat/card-task-23686c59' -ExpectedHead '9267a5608ad200cdd8f525fc30de72d9629ba77d' -SizeBytes 237935037
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-e5c123ad' -Id 'e5c123ad' -Branch 'feat/card-task-e5c123ad' -ExpectedHead '52a90a3935f1f8ed4198200ce3a5e21ca8dab370' -SizeBytes 237938749
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-8ec395a1' -Id '8ec395a1' -Branch 'feat/card-task-8ec395a1' -ExpectedHead '98c5006a139f454a9e04a93b711071c7a04c75a0' -SizeBytes 237945105
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-80c30683' -Id '80c30683' -Branch 'feat/card-task-80c30683' -ExpectedHead '43a816c3d0b49159f530b90e154662c64b928881' -SizeBytes 237943761
Remove-TaskWorktree -Path 'C:/Antiphon/worktrees/card-task-d8f79eb5' -Id 'd8f79eb5' -Branch 'feat/card-task-d8f79eb5' -ExpectedHead '2e1f62df6cd416a197790bb54a45c8c25c559959' -SizeBytes 237965240
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-0297fa9c' -Id '0297fa9c' -ExpectedHead '3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e' -SizeBytes 4367865090
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-0b36ae35' -Id '0b36ae35' -ExpectedHead '653f1145de5b72c2617a018cd606e0e8ba3cfe6e' -SizeBytes 4709368780
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-136debc0' -Id '136debc0' -ExpectedHead '679b696bd178e3c8bb14b32fcd04a1c26b148435' -SizeBytes 4143747793
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-1d7cc1fe' -Id '1d7cc1fe' -ExpectedHead 'ab7af12c699039270af7e22a5d7fd794035bc325' -SizeBytes 5317527222
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-2271e6aa' -Id '2271e6aa' -ExpectedHead '1038d07e66a54b630a1bc1d394923bc7049b73dd' -SizeBytes 4216312433
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-250a07fb' -Id '250a07fb' -ExpectedHead '87f4a868a86ce594d6dc9d1f1365315cb3f86df9' -SizeBytes 315154918
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-25e35cdf' -Id '25e35cdf' -ExpectedHead '56e96eabf698e9c1fcd462836ad32ed1fb259e83' -SizeBytes 5669313043
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-36d12cfa' -Id '36d12cfa' -ExpectedHead 'd164e93ae47a79ed175beeb9367212f7a7d5d1d5' -SizeBytes 4340548985
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-3ca79566' -Id '3ca79566' -ExpectedHead '74a8004f405bc54666fea33889839f0ff4d62ec7' -SizeBytes 4603861443
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-42be26dd' -Id '42be26dd' -ExpectedHead '7bee43614495293afbae1b0f4add019d11dacb9c' -SizeBytes 4800101273
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-44f789f3' -Id '44f789f3' -ExpectedHead '56e96eabf698e9c1fcd462836ad32ed1fb259e83' -SizeBytes 345797247
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-4dd2a426' -Id '4dd2a426' -ExpectedHead 'ac6d00022e940ee3b90584c7d313567b4491c498' -SizeBytes 4811773668
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-59e9866b' -Id '59e9866b' -ExpectedHead '0e7aef0379a073c206e8398dcaed539bc33eaf9b' -SizeBytes 4385532424
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-78b36185' -Id '78b36185' -ExpectedHead 'e114313d36ef0892676c4e0f4be2c956808c7978' -SizeBytes 4035235180
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-8e08368a' -Id '8e08368a' -ExpectedHead 'b559bfba2e9a420495122d27171352fe9b7e1ba4' -SizeBytes 5328497023
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-964900d9' -Id '964900d9' -ExpectedHead 'a71c45ab4d5703d480f4eee4c4e30adab45bc4f9' -SizeBytes 5279530569
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-c9cb914c' -Id 'c9cb914c' -ExpectedHead '5fef2c23b89414b8818aaf40db9ad738bce9524c' -SizeBytes 4695075744
Remove-ReleaseCheckout -Path 'C:\Antiphon\releases\checkout-ff89ca56' -Id 'ff89ca56' -ExpectedHead '64e3d78c487d51570f708492ab67b5db6d0de9a9' -SizeBytes 5339574274

```


## Results (executed 2026-11-16 14:20-15:05Z)

| Measure | Value |
|---|---|
| Free on C: at task start | 235258888192 bytes (219.1 GiB) |
| Free on C: just before deletion | 236447428608 bytes (220.2 GiB) |
| Free on C: after | 377904029696 bytes (352.0 GiB) |
| Logical bytes of removed trees | 131283899373 (122.3 GiB) |
| Worktrees removed / skipped | 174 / 7 (directory already absent at removal time; removed meanwhile by another actor, most likely the hourly CARD-0824 S4a desktop release) |
| Release checkouts removed | 18 |
| Local branches deleted (origin at same commit) | 129; kept 40 where origin moved ahead or the branch is gone from origin (HEAD contained elsewhere) |
| Failures / refusals at removal time | 0 |
| Registered worktrees now | 35 |
| `git worktree prune` | dry run and run: nothing to prune |

Live run 053aa2ea supervisor heartbeat was current (hangSuspected=false) after the removals; `checkout-053aa2ea`, `task-053aa2ea`, `state-053aa2ea` and its candidate were not touched.

### KEEP rows holding commits not reachable from origin

| Path | HEAD | Now |
|---|---|---|
| `C:/Antiphon/scratch/78334d64-rc` | 5a690f98544f0fefc1dde7c27135f076f440288f | not on any origin branch |
| `C:/Antiphon/worktrees/card-task-016fd320` | 39a1852f39858c10fec038a90c9d0a47ca70f74e | not on any origin branch |
| `C:/Antiphon/worktrees/card-task-ebcd3b04` | 5fe59dfd926d47e4439ce885cfafd003aed662ca | not on any origin branch |

The canonical stash list has 3 entries (2026-08-16..21; one names feat/card-task-c6bc61f7, which has no worktree). Untouched.

### Removal log

| UTC | Kind | Path | Size GB | Task | Result | Local branch deleted | Detail |
|---|---|---|---|---|---|---|---|
| 2026-11-16T14:24:45 | wt | `C:/Antiphon/scratch/a37ceaf1-rc` | 1.61 | a37ceaf1 | REMOVED | no |  |
| 2026-11-16T14:24:51 | wt | `C:/Antiphon/scratch/ae4df578-baseline` | 0.17 | ae4df578 | REMOVED | no |  |
| 2026-11-16T14:25:06 | wt | `C:/Antiphon/scratch/ae4df578-rc` | 0.32 | ae4df578 | REMOVED | no |  |
| 2026-11-16T14:25:18 | wt | `C:/Antiphon/scratch/089ef857-master` | 0.33 | 089ef857 | REMOVED | no |  |
| 2026-11-16T14:25:25 | wt | `C:/temp/antiphon-add64715-base` | 0.19 | add64715 | REMOVED | no |  |
| 2026-11-16T14:25:35 | wt | `C:/Antiphon/worktrees/card-task-29664acc` | 0.20 | 29664acc | REMOVED | yes |  |
| 2026-11-16T14:25:43 | wt | `C:/Antiphon/worktrees/card-task-e5258e8c` | 0.20 | e5258e8c | REMOVED | kept(origin=2b137d81d) |  |
| 2026-11-16T14:25:53 | wt | `C:/Antiphon/worktrees/card-task-dd35b058` | 0.20 | dd35b058 | REMOVED | yes |  |
| 2026-11-16T14:26:01 | wt | `C:/Antiphon/worktrees/card-task-314297be` | 0.20 | 314297be | REMOVED | yes |  |
| 2026-11-16T14:26:10 | wt | `C:/Antiphon/worktrees/card-task-f303b3ec` | 0.20 | f303b3ec | REMOVED | kept(origin=c3118c9db) |  |
| 2026-11-16T14:26:19 | wt | `C:/Antiphon/worktrees/card-task-56bf971d` | 0.20 | 56bf971d | REMOVED | kept(origin=7948e04a0) |  |
| 2026-11-16T14:26:27 | wt | `C:/Antiphon/worktrees/card-task-a41890aa` | 0.20 | a41890aa | REMOVED | kept(origin=3b5001ee5) |  |
| 2026-11-16T14:26:35 | wt | `C:/Antiphon/worktrees/card-task-801ad6c3` | 0.20 | 801ad6c3 | REMOVED | kept(origin=a3cd0fe6b) |  |
| 2026-11-16T14:26:43 | wt | `C:/Antiphon/worktrees/card-task-f2976027` | 0.20 | f2976027 | REMOVED | kept(origin=422c6f0b9) |  |
| 2026-11-16T14:26:49 | wt | `C:/Antiphon/worktrees/card-task-d2564da4` | 0.20 | d2564da4 | REMOVED | kept(origin=92a576d31) |  |
| 2026-11-16T14:26:58 | wt | `C:/Antiphon/worktrees/card-task-e24018e3` | 0.20 | e24018e3 | REMOVED | kept(origin=38726af4e) |  |
| 2026-11-16T14:27:05 | wt | `C:/Antiphon/worktrees/card-task-3fd6747e` | 0.20 | 3fd6747e | REMOVED | yes |  |
| 2026-11-16T14:27:14 | wt | `C:/Antiphon/worktrees/card-task-7c7132b3` | 0.20 | 7c7132b3 | REMOVED | yes |  |
| 2026-11-16T14:27:21 | wt | `C:/Antiphon/worktrees/card-task-613f2230` | 0.20 | 613f2230 | REMOVED | kept(origin=1dcad366c) |  |
| 2026-11-16T14:27:31 | wt | `C:/Antiphon/worktrees/card-task-605c6f4e` | 0.20 | 605c6f4e | REMOVED | kept(origin=24f1ea0ab) |  |
| 2026-11-16T14:27:39 | wt | `C:/Antiphon/worktrees/card-task-b5b7324a` | 0.20 | b5b7324a | REMOVED | kept(origin=841898902) |  |
| 2026-11-16T14:27:46 | wt | `C:/Antiphon/worktrees/card-task-1edbc845` | 0.20 | 1edbc845 | REMOVED | kept(origin=f47be567b) |  |
| 2026-11-16T14:27:53 | wt | `C:/Antiphon/worktrees/card-task-8592d536` | 0.20 | 8592d536 | REMOVED | yes |  |
| 2026-11-16T14:28:00 | wt | `C:/Antiphon/worktrees/card-task-64fb3f5d` | 0.20 | 64fb3f5d | REMOVED | kept(origin=fbbb00882) |  |
| 2026-11-16T14:28:09 | wt | `C:/Antiphon/worktrees/card-task-f674c79a` | 0.20 | f674c79a | REMOVED | kept(origin=cde2e9127) |  |
| 2026-11-16T14:28:19 | wt | `C:/Antiphon/worktrees/card-task-cfccf493` | 0.20 | cfccf493 | REMOVED | kept(origin=) |  |
| 2026-11-16T14:28:26 | wt | `C:/Antiphon/worktrees/card-task-3b906877` | 0.20 | 3b906877 | REMOVED | yes |  |
| 2026-11-16T14:28:34 | wt | `C:/Antiphon/worktrees/card-task-1e279cab` | 0.20 | 1e279cab | REMOVED | yes |  |
| 2026-11-16T14:28:40 | wt | `C:/Antiphon/worktrees/card-task-a9b46304` | 0.20 | a9b46304 | REMOVED | yes |  |
| 2026-11-16T14:28:50 | wt | `C:/Antiphon/worktrees/card-task-b7c4a9b7` | 0.20 | b7c4a9b7 | REMOVED | kept(origin=e04e2efa8) |  |
| 2026-11-16T14:28:59 | wt | `C:/Antiphon/worktrees/card-task-50e9a890` | 0.20 | 50e9a890 | REMOVED | yes |  |
| 2026-11-16T14:29:07 | wt | `C:/Antiphon/worktrees/card-task-555ce937` | 0.20 | 555ce937 | REMOVED | yes |  |
| 2026-11-16T14:29:17 | wt | `C:/Antiphon/worktrees/card-task-02f02e8a` | 0.20 | 02f02e8a | REMOVED | yes |  |
| 2026-11-16T14:29:25 | wt | `C:/Antiphon/worktrees/card-task-642a3764` | 0.21 | 642a3764 | REMOVED | yes |  |
| 2026-11-16T14:29:32 | wt | `C:/Antiphon/worktrees/card-task-20805524` | 0.21 | 20805524 | REMOVED | kept(origin=c7216a06f) |  |
| 2026-11-16T14:29:40 | wt | `C:/Antiphon/worktrees/card-task-e7d7d78b` | 0.21 | e7d7d78b | REMOVED | kept(origin=a06d96a50) |  |
| 2026-11-16T14:29:47 | wt | `C:/Antiphon/worktrees/card-task-5a2fffb9` | 0.21 | 5a2fffb9 | REMOVED | yes |  |
| 2026-11-16T14:29:53 | wt | `C:/Antiphon/worktrees/card-task-bc7a9fdd` | 0.21 | bc7a9fdd | REMOVED | kept(origin=6fb21150e) |  |
| 2026-11-16T14:29:59 | wt | `C:/Antiphon/worktrees/card-task-4f5166ec` | 0.21 | 4f5166ec | REMOVED | kept(origin=b6052173f) |  |
| 2026-11-16T14:30:06 | wt | `C:/Antiphon/worktrees/card-task-f442a78f` | 0.21 | f442a78f | REMOVED | kept(origin=171182056) |  |
| 2026-11-16T14:30:13 | wt | `C:/Antiphon/worktrees/card-task-c7a30141` | 0.21 | c7a30141 | REMOVED | kept(origin=436e0d359) |  |
| 2026-11-16T14:30:20 | wt | `C:/Antiphon/worktrees/card-task-f87fdda9` | 0.21 | f87fdda9 | REMOVED | kept(origin=3364197bf) |  |
| 2026-11-16T14:30:29 | wt | `C:/Antiphon/worktrees/card-task-5b1ea772` | 0.21 | 5b1ea772 | REMOVED | kept(origin=ed63b5861) |  |
| 2026-11-16T14:30:36 | wt | `C:/Antiphon/worktrees/card-task-ec9dfd70` | 0.21 | ec9dfd70 | REMOVED | kept(origin=d18822e62) |  |
| 2026-11-16T14:30:43 | wt | `C:/Antiphon/worktrees/card-task-4292c713` | 0.21 | 4292c713 | REMOVED | kept(origin=13ed70c73) |  |
| 2026-11-16T14:30:50 | wt | `C:/Antiphon/worktrees/card-task-a2845b6c` | 0.21 | a2845b6c | REMOVED | kept(origin=de40bdb3a) |  |
| 2026-11-16T14:30:58 | wt | `C:/Antiphon/worktrees/card-task-18dd27d8` | 0.21 | 18dd27d8 | REMOVED | yes |  |
| 2026-11-16T14:31:05 | wt | `C:/Antiphon/worktrees/card-task-ed0f7942` | 0.21 | ed0f7942 | REMOVED | kept(origin=a41e77962) |  |
| 2026-11-16T14:31:11 | wt | `C:/Antiphon/worktrees/card-task-1af450d3` | 0.21 | 1af450d3 | REMOVED | yes |  |
| 2026-11-16T14:31:18 | wt | `C:/Antiphon/worktrees/card-task-f1fde86b` | 0.21 | f1fde86b | REMOVED | yes |  |
| 2026-11-16T14:31:25 | wt | `C:/Antiphon/worktrees/card-task-7555284f` | 0.21 | 7555284f | REMOVED | kept(origin=eaf879a9f) |  |
| 2026-11-16T14:31:32 | wt | `C:/Antiphon/worktrees/card-task-da90742c` | 0.21 | da90742c | REMOVED | kept(origin=a4341ba53) |  |
| 2026-11-16T14:31:39 | wt | `C:/Antiphon/worktrees/card-task-2abcd1c0` | 0.21 | 2abcd1c0 | REMOVED | kept(origin=9c096f32f) |  |
| 2026-11-16T14:31:45 | wt | `C:/Antiphon/worktrees/card-task-bee64303` | 0.21 | bee64303 | REMOVED | kept(origin=57bbfc99d) |  |
| 2026-11-16T14:31:52 | wt | `C:/Antiphon/worktrees/card-task-c6ca6929` | 0.21 | c6ca6929 | REMOVED | kept(origin=ea50afd90) |  |
| 2026-11-16T14:31:58 | wt | `C:/Antiphon/worktrees/card-task-934cdb25` | 0.21 | 934cdb25 | REMOVED | kept(origin=f7001bbaa) |  |
| 2026-11-16T14:32:05 | wt | `C:/Antiphon/worktrees/card-task-6155693e` | 0.21 | 6155693e | REMOVED | kept(origin=d608b09ba) |  |
| 2026-11-16T14:32:11 | wt | `C:/Antiphon/worktrees/card-task-94b17435` | 0.21 | 94b17435 | REMOVED | kept(origin=f1a220891) |  |
| 2026-11-16T14:32:17 | wt | `C:/Antiphon/worktrees/card-task-b0e54eca` | 0.21 | b0e54eca | REMOVED | kept(origin=214114346) |  |
| 2026-11-16T14:32:25 | wt | `C:/Antiphon/worktrees/card-task-01f26ecf` | 0.21 | 01f26ecf | REMOVED | yes |  |
| 2026-11-16T14:32:33 | wt | `C:/Antiphon/worktrees/card-task-9294312a` | 0.21 | 9294312a | REMOVED | yes |  |
| 2026-11-16T14:32:41 | wt | `C:/Antiphon/worktrees/card-task-06aa57e7` | 0.21 | 06aa57e7 | REMOVED | yes |  |
| 2026-11-16T14:32:51 | wt | `C:/Antiphon/worktrees/card-task-149cb7c3` | 0.21 | 149cb7c3 | REMOVED | yes |  |
| 2026-11-16T14:33:02 | wt | `C:/Antiphon/worktrees/card-task-d989d9d8` | 0.21 | d989d9d8 | REMOVED | yes |  |
| 2026-11-16T14:33:20 | wt | `C:/Antiphon/worktrees/card-task-87596cc5` | 0.21 | 87596cc5 | REMOVED | yes |  |
| 2026-11-16T14:33:27 | wt | `C:/Antiphon/worktrees/card-task-25d9a69f` | 0.21 | 25d9a69f | REMOVED | yes |  |
| 2026-11-16T14:33:37 | wt | `C:/Antiphon/worktrees/card-task-3199d57c` | 0.22 | 3199d57c | REMOVED | yes |  |
| 2026-11-16T14:33:37 | wt | `C:/Antiphon/worktrees/card-task-dc70c718` | 0.22 | dc70c718 | SKIP |  | directory already absent |
| 2026-11-16T14:33:37 | wt | `C:/Antiphon/worktrees/card-task-f3a457ae` | 0.22 | f3a457ae | SKIP |  | directory already absent |
| 2026-11-16T14:33:37 | wt | `C:/Antiphon/worktrees/card-task-b4f39ca7` | 1.05 | b4f39ca7 | SKIP |  | directory already absent |
| 2026-11-16T14:33:37 | wt | `C:/Antiphon/worktrees/card-task-ec0488ff` | 0.22 | ec0488ff | SKIP |  | directory already absent |
| 2026-11-16T14:33:37 | wt | `C:/Antiphon/worktrees/card-task-31c683a8` | 2.28 | 31c683a8 | SKIP |  | directory already absent |
| 2026-11-16T14:33:37 | wt | `C:/Antiphon/worktrees/card-task-cbedf221` | 0.22 | cbedf221 | SKIP |  | directory already absent |
| 2026-11-16T14:33:37 | wt | `C:/Antiphon/worktrees/card-task-ca8076ae` | 0.22 | ca8076ae | SKIP |  | directory already absent |
| 2026-11-16T14:33:45 | wt | `C:/Antiphon/worktrees/card-task-ec4754a8` | 0.22 | ec4754a8 | REMOVED | yes |  |
| 2026-11-16T14:33:53 | wt | `C:/Antiphon/worktrees/card-task-6f663ce6` | 0.22 | 6f663ce6 | REMOVED | yes |  |
| 2026-11-16T14:34:05 | wt | `C:/Antiphon/worktrees/card-task-6eccb98b` | 0.22 | 6eccb98b | REMOVED | kept(origin=) |  |
| 2026-11-16T14:34:17 | wt | `C:/Antiphon/worktrees/card-task-35eb006c` | 1.27 | 35eb006c | REMOVED | yes |  |
| 2026-11-16T14:34:26 | wt | `C:/Antiphon/worktrees/card-task-8de3f823` | 0.22 | 8de3f823 | REMOVED | yes |  |
| 2026-11-16T14:34:35 | wt | `C:/Antiphon/worktrees/card-task-29d177ef` | 0.22 | 29d177ef | REMOVED | yes |  |
| 2026-11-16T14:34:46 | wt | `C:/Antiphon/worktrees/card-task-d684bc25` | 0.22 | d684bc25 | REMOVED | yes |  |
| 2026-11-16T14:34:56 | wt | `C:/Antiphon/worktrees/card-task-ab7e65ba` | 0.22 | ab7e65ba | REMOVED | kept(origin=) |  |
| 2026-11-16T14:35:04 | wt | `C:/Antiphon/worktrees/card-task-9325be95` | 0.22 | 9325be95 | REMOVED | yes |  |
| 2026-11-16T14:35:12 | wt | `C:/Antiphon/worktrees/card-task-9c1feeba` | 0.22 | 9c1feeba | REMOVED | yes |  |
| 2026-11-16T14:35:20 | wt | `C:/Antiphon/worktrees/card-task-3c766de7` | 0.22 | 3c766de7 | REMOVED | yes |  |
| 2026-11-16T14:35:32 | wt | `C:/Antiphon/worktrees/card-task-8998668f` | 2.28 | 8998668f | REMOVED | yes |  |
| 2026-11-16T14:35:42 | wt | `C:/Antiphon/worktrees/card-task-28f886d2` | 2.28 | 28f886d2 | REMOVED | yes |  |
| 2026-11-16T14:35:49 | wt | `C:/Antiphon/worktrees/card-task-3afa3cfa` | 0.22 | 3afa3cfa | REMOVED | yes |  |
| 2026-11-16T14:35:58 | wt | `C:/Antiphon/worktrees/card-task-5c3cf9f6` | 0.22 | 5c3cf9f6 | REMOVED | yes |  |
| 2026-11-16T14:36:06 | wt | `C:/Antiphon/worktrees/card-task-fc30e791` | 0.22 | fc30e791 | REMOVED | yes |  |
| 2026-11-16T14:36:13 | wt | `C:/Antiphon/worktrees/card-task-15406ecc` | 0.22 | 15406ecc | REMOVED | yes |  |
| 2026-11-16T14:36:21 | wt | `C:/Antiphon/worktrees/card-task-abd9b404` | 0.22 | abd9b404 | REMOVED | yes |  |
| 2026-11-16T14:36:30 | wt | `C:/Antiphon/worktrees/card-task-5675aac7` | 0.22 | 5675aac7 | REMOVED | yes |  |
| 2026-11-16T14:36:37 | wt | `C:/Antiphon/worktrees/card-task-856fac4d` | 0.22 | 856fac4d | REMOVED | yes |  |
| 2026-11-16T14:36:45 | wt | `C:/Antiphon/worktrees/card-task-66eb7e18` | 0.22 | 66eb7e18 | REMOVED | yes |  |
| 2026-11-16T14:36:52 | wt | `C:/Antiphon/worktrees/card-task-f6d722a9` | 0.22 | f6d722a9 | REMOVED | yes |  |
| 2026-11-16T14:37:01 | wt | `C:/Antiphon/worktrees/card-task-ac7f437c` | 0.22 | ac7f437c | REMOVED | yes |  |
| 2026-11-16T14:37:10 | wt | `C:/Antiphon/worktrees/card-task-93af5925` | 0.22 | 93af5925 | REMOVED | yes |  |
| 2026-11-16T14:37:17 | wt | `C:/Antiphon/worktrees/card-task-68b66c6d` | 0.22 | 68b66c6d | REMOVED | yes |  |
| 2026-11-16T14:37:24 | wt | `C:/Antiphon/worktrees/card-task-882cae23` | 0.22 | 882cae23 | REMOVED | yes |  |
| 2026-11-16T14:37:33 | wt | `C:/Antiphon/worktrees/card-task-0055a160` | 0.22 | 0055a160 | REMOVED | yes |  |
| 2026-11-16T14:37:42 | wt | `C:/Antiphon/worktrees/card-task-1a125507` | 0.22 | 1a125507 | REMOVED | yes |  |
| 2026-11-16T14:37:52 | wt | `C:/Antiphon/worktrees/card-task-18072e35` | 0.22 | 18072e35 | REMOVED | yes |  |
| 2026-11-16T14:37:59 | wt | `C:/Antiphon/worktrees/card-task-b0ec9aab` | 0.22 | b0ec9aab | REMOVED | yes |  |
| 2026-11-16T14:38:07 | wt | `C:/Antiphon/worktrees/card-task-1637311a` | 0.22 | 1637311a | REMOVED | yes |  |
| 2026-11-16T14:38:14 | wt | `C:/Antiphon/worktrees/card-task-9aa09d20` | 0.22 | 9aa09d20 | REMOVED | yes |  |
| 2026-11-16T14:38:22 | wt | `C:/Antiphon/worktrees/card-task-0704a004` | 0.22 | 0704a004 | REMOVED | yes |  |
| 2026-11-16T14:38:36 | wt | `C:/Antiphon/worktrees/card-task-c51e92c8` | 2.61 | c51e92c8 | REMOVED | yes |  |
| 2026-11-16T14:38:44 | wt | `C:/Antiphon/worktrees/card-task-ce8ef291` | 0.22 | ce8ef291 | REMOVED | yes |  |
| 2026-11-16T14:38:52 | wt | `C:/Antiphon/worktrees/card-task-198a988b` | 0.22 | 198a988b | REMOVED | yes |  |
| 2026-11-16T14:38:59 | wt | `C:/Antiphon/worktrees/card-task-24cf8f11` | 0.22 | 24cf8f11 | REMOVED | yes |  |
| 2026-11-16T14:39:08 | wt | `C:/Antiphon/worktrees/card-task-593c280c` | 0.22 | 593c280c | REMOVED | yes |  |
| 2026-11-16T14:39:15 | wt | `C:/Antiphon/worktrees/card-task-48728776` | 0.22 | 48728776 | REMOVED | yes |  |
| 2026-11-16T14:39:23 | wt | `C:/Antiphon/worktrees/card-task-1ba18e23` | 0.22 | 1ba18e23 | REMOVED | yes |  |
| 2026-11-16T14:39:29 | wt | `C:/Antiphon/worktrees/card-task-93f5087b` | 0.22 | 93f5087b | REMOVED | yes |  |
| 2026-11-16T14:39:37 | wt | `C:/Antiphon/worktrees/card-task-2b6dde13` | 0.22 | 2b6dde13 | REMOVED | yes |  |
| 2026-11-16T14:39:43 | wt | `C:/Antiphon/worktrees/card-task-78d029fc` | 0.22 | 78d029fc | REMOVED | yes |  |
| 2026-11-16T14:39:50 | wt | `C:/Antiphon/worktrees/card-task-1905deba` | 0.22 | 1905deba | REMOVED | yes |  |
| 2026-11-16T14:39:57 | wt | `C:/Antiphon/worktrees/card-task-7342010d` | 0.22 | 7342010d | REMOVED | yes |  |
| 2026-11-16T14:40:06 | wt | `C:/Antiphon/worktrees/card-task-24eb2f3d` | 0.22 | 24eb2f3d | REMOVED | yes |  |
| 2026-11-16T14:40:13 | wt | `C:/Antiphon/worktrees/card-task-b1318d57` | 0.22 | b1318d57 | REMOVED | yes |  |
| 2026-11-16T14:40:22 | wt | `C:/Antiphon/worktrees/card-task-8d8824ed` | 0.22 | 8d8824ed | REMOVED | yes |  |
| 2026-11-16T14:40:30 | wt | `C:/Antiphon/worktrees/card-task-27128425` | 0.22 | 27128425 | REMOVED | yes |  |
| 2026-11-16T14:40:39 | wt | `C:/Antiphon/worktrees/card-task-b2663606` | 0.22 | b2663606 | REMOVED | yes |  |
| 2026-11-16T14:40:47 | wt | `C:/Antiphon/worktrees/card-task-8112d494` | 0.22 | 8112d494 | REMOVED | yes |  |
| 2026-11-16T14:40:53 | wt | `C:/Antiphon/worktrees/card-task-cd9133eb` | 0.22 | cd9133eb | REMOVED | yes |  |
| 2026-11-16T14:41:01 | wt | `C:/Antiphon/worktrees/card-task-5786ffb9` | 0.22 | 5786ffb9 | REMOVED | yes |  |
| 2026-11-16T14:41:09 | wt | `C:/Antiphon/worktrees/card-task-33039320` | 0.22 | 33039320 | REMOVED | yes |  |
| 2026-11-16T14:41:18 | wt | `C:/Antiphon/worktrees/card-task-4f8dc817` | 0.22 | 4f8dc817 | REMOVED | yes |  |
| 2026-11-16T14:41:34 | wt | `C:/Antiphon/worktrees/card-task-12311088` | 0.22 | 12311088 | REMOVED | yes |  |
| 2026-11-16T14:41:41 | wt | `C:/Antiphon/worktrees/card-task-d5ec6eff` | 0.22 | d5ec6eff | REMOVED | yes |  |
| 2026-11-16T14:41:48 | wt | `C:/Antiphon/worktrees/card-task-bf127f48` | 0.22 | bf127f48 | REMOVED | yes |  |
| 2026-11-16T14:41:55 | wt | `C:/Antiphon/worktrees/card-task-665816dd` | 0.22 | 665816dd | REMOVED | yes |  |
| 2026-11-16T14:42:02 | wt | `C:/Antiphon/worktrees/card-task-00011235` | 0.22 | 00011235 | REMOVED | yes |  |
| 2026-11-16T14:42:14 | wt | `C:/Antiphon/worktrees/card-task-7ac938e9` | 2.27 | 7ac938e9 | REMOVED | yes |  |
| 2026-11-16T14:42:21 | wt | `C:/Antiphon/worktrees/card-task-c3d468f8` | 0.22 | c3d468f8 | REMOVED | yes |  |
| 2026-11-16T14:42:28 | wt | `C:/Antiphon/worktrees/card-task-0bd83714` | 0.22 | 0bd83714 | REMOVED | yes |  |
| 2026-11-16T14:42:36 | wt | `C:/Antiphon/worktrees/card-task-f1cf9f3c` | 0.22 | f1cf9f3c | REMOVED | yes |  |
| 2026-11-16T14:42:43 | wt | `C:/Antiphon/worktrees/card-task-73e7dfdd` | 0.22 | 73e7dfdd | REMOVED | yes |  |
| 2026-11-16T14:42:51 | wt | `C:/Antiphon/worktrees/card-task-7708d552` | 0.22 | 7708d552 | REMOVED | yes |  |
| 2026-11-16T14:43:00 | wt | `C:/Antiphon/worktrees/card-task-bffbe413` | 0.36 | bffbe413 | REMOVED | yes |  |
| 2026-11-16T14:43:08 | wt | `C:/Antiphon/worktrees/card-task-5c14b3c5` | 0.22 | 5c14b3c5 | REMOVED | yes |  |
| 2026-11-16T14:43:15 | wt | `C:/Antiphon/worktrees/card-task-b440ac48` | 0.22 | b440ac48 | REMOVED | yes |  |
| 2026-11-16T14:43:21 | wt | `C:/Antiphon/worktrees/card-task-f21bf3ba` | 0.22 | f21bf3ba | REMOVED | yes |  |
| 2026-11-16T14:43:33 | wt | `C:/Antiphon/worktrees/card-task-f9e020bf` | 2.38 | f9e020bf | REMOVED | yes |  |
| 2026-11-16T14:43:42 | wt | `C:/Antiphon/worktrees/card-task-daf2ea47` | 0.22 | daf2ea47 | REMOVED | yes |  |
| 2026-11-16T14:43:49 | wt | `C:/Antiphon/worktrees/card-task-7bb75309` | 0.22 | 7bb75309 | REMOVED | yes |  |
| 2026-11-16T14:43:55 | wt | `C:/Antiphon/worktrees/card-task-e594649c` | 0.22 | e594649c | REMOVED | yes |  |
| 2026-11-16T14:44:03 | wt | `C:/Antiphon/worktrees/card-task-484dd12d` | 0.22 | 484dd12d | REMOVED | yes |  |
| 2026-11-16T14:44:10 | wt | `C:/Antiphon/worktrees/card-task-cf50eda6` | 0.22 | cf50eda6 | REMOVED | yes |  |
| 2026-11-16T14:44:19 | wt | `C:/Antiphon/worktrees/card-task-9248daa5` | 0.22 | 9248daa5 | REMOVED | yes |  |
| 2026-11-16T14:44:29 | wt | `C:/Antiphon/worktrees/card-task-e479506f` | 0.22 | e479506f | REMOVED | yes |  |
| 2026-11-16T14:44:38 | wt | `C:/Antiphon/worktrees/card-task-6430f6eb` | 0.22 | 6430f6eb | REMOVED | yes |  |
| 2026-11-16T14:44:46 | wt | `C:/Antiphon/worktrees/card-task-3f159277` | 0.22 | 3f159277 | REMOVED | yes |  |
| 2026-11-16T14:44:55 | wt | `C:/Antiphon/worktrees/card-task-7a422522` | 0.22 | 7a422522 | REMOVED | yes |  |
| 2026-11-16T14:45:01 | wt | `C:/Antiphon/worktrees/card-task-1c76938a` | 0.22 | 1c76938a | REMOVED | yes |  |
| 2026-11-16T14:45:12 | wt | `C:/Antiphon/worktrees/card-task-c6da537d` | 0.22 | c6da537d | REMOVED | yes |  |
| 2026-11-16T14:45:18 | wt | `C:/Antiphon/worktrees/card-task-ac8b9d8e` | 0.22 | ac8b9d8e | REMOVED | yes |  |
| 2026-11-16T14:45:27 | wt | `C:/Antiphon/worktrees/card-task-3ea0f6b5` | 0.22 | 3ea0f6b5 | REMOVED | yes |  |
| 2026-11-16T14:45:33 | wt | `C:/Antiphon/worktrees/card-task-28eecdf5` | 0.22 | 28eecdf5 | REMOVED | yes |  |
| 2026-11-16T14:45:42 | wt | `C:/Antiphon/worktrees/card-task-8f001b6d` | 0.22 | 8f001b6d | REMOVED | yes |  |
| 2026-11-16T14:45:49 | wt | `C:/Antiphon/worktrees/card-task-4c25994e` | 0.22 | 4c25994e | REMOVED | yes |  |
| 2026-11-16T14:45:57 | wt | `C:/Antiphon/worktrees/card-task-77ab4e11` | 0.22 | 77ab4e11 | REMOVED | yes |  |
| 2026-11-16T14:46:04 | wt | `C:/Antiphon/worktrees/card-task-c2489e73` | 0.22 | c2489e73 | REMOVED | yes |  |
| 2026-11-16T14:46:10 | wt | `C:/Antiphon/worktrees/card-task-baba7a4e` | 0.22 | baba7a4e | REMOVED | yes |  |
| 2026-11-16T14:46:21 | wt | `C:/Antiphon/worktrees/card-task-d20d7003` | 0.22 | d20d7003 | REMOVED | yes |  |
| 2026-11-16T14:46:29 | wt | `C:/Antiphon/worktrees/card-task-68f81b18` | 0.22 | 68f81b18 | REMOVED | yes |  |
| 2026-11-16T14:46:42 | wt | `C:/Antiphon/worktrees/card-task-f38e63e0` | 0.22 | f38e63e0 | REMOVED | kept(origin=) |  |
| 2026-11-16T14:46:50 | wt | `C:/Antiphon/worktrees/card-task-8d020b07` | 0.22 | 8d020b07 | REMOVED | yes |  |
| 2026-11-16T14:46:59 | wt | `C:/Antiphon/worktrees/card-task-1d838cd1` | 0.22 | 1d838cd1 | REMOVED | yes |  |
| 2026-11-16T14:47:07 | wt | `C:/Antiphon/worktrees/card-task-bd9e5cb5` | 0.22 | bd9e5cb5 | REMOVED | yes |  |
| 2026-11-16T14:47:15 | wt | `C:/Antiphon/worktrees/card-task-3820a5f1` | 0.22 | 3820a5f1 | REMOVED | yes |  |
| 2026-11-16T14:47:23 | wt | `C:/Antiphon/worktrees/card-task-5ca0e7b6` | 0.22 | 5ca0e7b6 | REMOVED | yes |  |
| 2026-11-16T14:47:33 | wt | `C:/Antiphon/worktrees/card-task-870a2044` | 0.22 | 870a2044 | REMOVED | yes |  |
| 2026-11-16T14:47:41 | wt | `C:/Antiphon/worktrees/card-task-dc726c22` | 0.22 | dc726c22 | REMOVED | yes |  |
| 2026-11-16T14:47:48 | wt | `C:/Antiphon/worktrees/card-task-22ee1e10` | 0.22 | 22ee1e10 | REMOVED | yes |  |
| 2026-11-16T14:47:55 | wt | `C:/Antiphon/worktrees/card-task-23686c59` | 0.22 | 23686c59 | REMOVED | yes |  |
| 2026-11-16T14:48:05 | wt | `C:/Antiphon/worktrees/card-task-e5c123ad` | 0.22 | e5c123ad | REMOVED | yes |  |
| 2026-11-16T14:48:14 | wt | `C:/Antiphon/worktrees/card-task-8ec395a1` | 0.22 | 8ec395a1 | REMOVED | yes |  |
| 2026-11-16T14:48:21 | wt | `C:/Antiphon/worktrees/card-task-80c30683` | 0.22 | 80c30683 | REMOVED | yes |  |
| 2026-11-16T14:48:29 | wt | `C:/Antiphon/worktrees/card-task-d8f79eb5` | 0.22 | d8f79eb5 | REMOVED | yes |  |
| 2026-11-16T14:49:13 | co | `C:\Antiphon\releases\checkout-0297fa9c` | 4.07 | 0297fa9c | REMOVED |  |  |
| 2026-11-16T14:49:59 | co | `C:\Antiphon\releases\checkout-0b36ae35` | 4.39 | 0b36ae35 | REMOVED |  |  |
| 2026-11-16T14:50:47 | co | `C:\Antiphon\releases\checkout-136debc0` | 3.86 | 136debc0 | REMOVED |  |  |
| 2026-11-16T14:51:44 | co | `C:\Antiphon\releases\checkout-1d7cc1fe` | 4.95 | 1d7cc1fe | REMOVED |  |  |
| 2026-11-16T14:52:35 | co | `C:\Antiphon\releases\checkout-2271e6aa` | 3.93 | 2271e6aa | REMOVED |  |  |
| 2026-11-16T14:52:41 | co | `C:\Antiphon\releases\checkout-250a07fb` | 0.29 | 250a07fb | REMOVED |  |  |
| 2026-11-16T14:53:38 | co | `C:\Antiphon\releases\checkout-25e35cdf` | 5.28 | 25e35cdf | REMOVED |  |  |
| 2026-11-16T14:54:33 | co | `C:\Antiphon\releases\checkout-36d12cfa` | 4.04 | 36d12cfa | REMOVED |  |  |
| 2026-11-16T14:55:15 | co | `C:\Antiphon\releases\checkout-3ca79566` | 4.29 | 3ca79566 | REMOVED |  |  |
| 2026-11-16T14:56:07 | co | `C:\Antiphon\releases\checkout-42be26dd` | 4.47 | 42be26dd | REMOVED |  |  |
| 2026-11-16T14:56:15 | co | `C:\Antiphon\releases\checkout-44f789f3` | 0.32 | 44f789f3 | REMOVED |  |  |
| 2026-11-16T14:57:10 | co | `C:\Antiphon\releases\checkout-4dd2a426` | 4.48 | 4dd2a426 | REMOVED |  |  |
| 2026-11-16T14:58:05 | co | `C:\Antiphon\releases\checkout-59e9866b` | 4.08 | 59e9866b | REMOVED |  |  |
| 2026-11-16T14:58:42 | co | `C:\Antiphon\releases\checkout-78b36185` | 3.76 | 78b36185 | REMOVED |  |  |
| 2026-11-16T14:59:41 | co | `C:\Antiphon\releases\checkout-8e08368a` | 4.96 | 8e08368a | REMOVED |  |  |
| 2026-11-16T15:00:39 | co | `C:\Antiphon\releases\checkout-964900d9` | 4.92 | 964900d9 | REMOVED |  |  |
| 2026-11-16T15:01:40 | co | `C:\Antiphon\releases\checkout-c9cb914c` | 4.37 | c9cb914c | REMOVED |  |  |
| 2026-11-16T15:02:45 | co | `C:\Antiphon\releases\checkout-ff89ca56` | 4.97 | ff89ca56 | REMOVED |  |  |

