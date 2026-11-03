# CARD-1692 Code e63ff4c2: CARD-1680 mutation-survivor witnesses

Outcome: 53 new assertions in `scripts/test-apphost-health-window.ps1` (cases W7-W11). All are
green at master. All 11 survivors from the c389a57b sweep go red under their mutant, including
the 6 the brief required. Test-only: no production change.

- Branch: `feat/card-task-e63ff4c2`. Worktree: `/work/worktrees/task-e63ff4c2`. Landing owner: task e63ff4c2.
- Base: `e56e4d1b6a727b3127b473307b860b7117980f09`. Witness commit: `19660b25260af3c7ef02f87f987b332bcbd7912a`.
- Source: card CARD-1692. Mutation report: `docs/investigations/2026-11-03-mutation-1680.md` on
  `origin/feat/card-task-c389a57b` (67c9ce03f). No plan or Checkpoints table, so no checkpoint-tool rows.
  The harness runs below are the whole ordinary verification.

## Seam additions (portable: no /bin/sh, no Linux-only paths, ASCII)

- Launcher `launcherCodeUnreadable`: an exited launcher reports `ExitCode = $null`.
- Port seam `portUntil`: 17202 is owned for polls in `[portAt, portUntil)`.
- `foreignAt`: another restart's stamp `777777 <utc>` is written through the held lock stream,
  because FileShare.Read refuses a second writer on Windows. It is also saved to `foreign.txt`
  for the byte-for-byte compare.
- `otherSha`: the version probe answers `b`x40.

## Witnesses (mutant -> case, red evidence on a scratch copy)

Each mutant was one literal edit of a copy of `scripts/` in the session scratchpad, so the
worktree was never edited and no restore was needed. Each harness ran from that copy with its
own build-slot lease (`c1692-mut-<id>`).

| Mutant | Case | Under mutant (FAIL lines) |
|---|---|---|
| M20 launcher exit 0 = failure | W7, W7t | 6 FAIL: exit=1 polls=3; "launcher exited" printed; W7t lock removed |
| M21 null exit code = failure | W7n, W7nt | 6 FAIL: exit=1 polls=3; W7nt lock removed |
| M19 Remove-AppHostLock `if ($true)` | W8 (launcher exit 9), W8b (vanished AppHost), W8h (healthy exit 0) | 3 FAIL: foreign stamp deleted (actual=[]) |
| M25 no 17202 check on dead wrapper | W9 (health -> exit 0), W9t (stall) | 6 FAIL: exit=1 polls=4; child stopped; W9t lock removed |
| M08 progress credited after 200 | W10s (200 + wrong SHA, log grows) | 2 FAIL: 31.1 s / 113 polls (hard cap 30) vs < 15 s; "hit the hard cap" |
| M15a Keep=9 | W11 (12 restarts, .1..10 = generations 12..3) | 1 FAIL: apphost.log.10 missing |
| M15b Keep=11 | W11 | 1 FAIL: apphost.log.11 exists |
| M18 initial snapshot $null | W10 (silent launch, pollMs 1000) | 2 FAIL: polls=3 (> 2), last progress names "apphost.log appeared" |
| M12 no MaxWaitSec clamp | W10c (-TimeoutSec 4 -MaxWaitSec 1, health at poll 8) | 2 FAIL: exit=1 polls=3 |
| M09a port counted only on opening | W9c (17202 open at 1, closed at 4) | 1 FAIL: last progress is not `listening=False` |
| M09b port counted only on closing | W9o (17202 opens at poll 6) | 1 FAIL: last progress is not `listening=True` |

The brief asked for mutant runs on M20, M21, M19, M25, M08 and M15a. The rest were cheap, so
they ran too, and every one is red. Mutant logs are in the session scratchpad, not the repo.

Note on M19: the dead-launch `-Case Guards` assertion `R-1 cleanup preserves replacement lock`
calls Remove-AppHostLock directly, so it should also kill M19. The sweep never ran Guards for
M19, and neither did I: this is reasoning only. W8 adds the restart-level witness through all
three lock-releasing exits.

## Ordinary verification at 19660b252 (clean tree, one lease each)

| Harness | Result | slot waited |
|---|---|---|
| health-window (all, W1-W11) | 83 passed, 0 failed (30 existing + 53 new) | 165s |
| graceful-stop | 11 passed, 0 failed | 105s |
| server-version | 131 passed, 0 failed | 285s |
| dead-launch Launcher | 3, failed=0 | 120s |
| dead-launch Recovery | 4, failed=0 | 195s |
| dead-launch Alert | 12, failed=0 | 330s |
| dead-launch Guards | 49, failed=0 | 135s |

The per-case green runs before the commit (W7 15, W8 6, W9 11, W10 9, W11 12) were the same
content, uncommitted. The full run above is the committed proof.

Other checks:
- `check-evidence-diff.ps1 -Repository . -BaseRef e56e4d1b6 -HeadRef HEAD`: commits=1
  entries=0 violations=0 (rerun after this report commit; see the final message).
- `git merge-tree --write-tree origin/master HEAD`: clean (tree d9641e8a8). origin/master ==
  base e56e4d1b6.

Not run, and why:
- Unit lane / .NET suites: the change only touches one PowerShell harness, and no .NET project
  reads it.
- Windows native run: nothing runs on the desktop (a qualification run is live). The cases use
  only seams and ASCII, and they quote arguments the same way the existing cases do.

Pending for Mutation: no PC rows (no plan). The witnesses above are the survivors' kill evidence.
