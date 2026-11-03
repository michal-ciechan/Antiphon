# CARD ops e937b0c3: server2 disk reclaim (2026-11-03)

Outcome: /work (= / = /tmp = /var/lib/docker, one LV, 705G) went from 100% (1.2G avail;
df -Pk 2,096,716 KiB avail) to 72% used, 194G avail (28% free). Stopped at the brief's 25%-free gate.

## Measure (before, read-only)
- One filesystem for everything visible from the main runner container: /dev/mapper/ubuntu--vg-ubuntu--lv 705G, 675G used.
- /work/worktrees: 566 dirs, 397.1 GiB (du -s). By task status: Succeeded 531 (383.2G), Canceled 9 (5.6G),
  Failed 6 (1.5G), Blocked 1 (0.6G, 0ce3bb38), Dispatched 2 (e937b0c3 = me, f2d12082), non-task dirs 17 (4.2G:
  task-cffa0c4b-u*/w*, task-e38d6427-lane*/w*/combined). Largest: task-06fac1ab 3.3G, fcf7ce80/8b317bb8/516b3a0a/
  ac3dd93d/2e6da28d/072cf7b1/e8784f40/1b618bf5 ~3.0G each, 737b54c5 2.9G, 815b655c 2.8G, a8bd6706/ecf8b748 2.7G,
  e13fbf72 2.2G, a4a77bb8 2.2G.
- /tmp 51G (46,600 top-level entries); ~/.nuget/packages 4.1G; /work/repos 959M; /work/cache 196K (no land-graph-audit
  entries present); /work/evidence 25M; /home 4.9G; /usr 2.1G.
- Nested docker (dind, docker system df): images 11 / 5.47GB (5.07GB reclaimable), containers 3 all running,
  volumes 13 / 42MB, build cache 0. docker system df -v not needed (volumes tiny).
- ~210G of the 675G used is not visible from inside the container (host-level: host docker images/volumes such
  as antiphon-runner_dind-data, runner-state, caches, any retained server2-temp volumes, host files). Needs a host
  ssh census (mc@server2: docker system df -v, du of /var/lib/docker/volumes) - not done, out of reach here.

## Reclaim
Procedure: docs/docker-stack.md "Manual main cleanup while running" (the documented in-container cleanup of a
running main; covered by the operator's 2026-10-31 standing instruction carried in the brief). Executed inside the
main runner container as uid 1654, runner kept running, no volume touched.
- Candidate census: GET /api/agent-tasks?since=2026-09-01 (7,481 rows, fleet), GET ?landPending=true (0 rows),
  git ls-remote origin 'refs/heads/feat/*' (4,819 heads).
- RETIRABLE = status Succeeded/Failed/Canceled AND completedAt < 2026-11-02T16:44Z (12h) AND on a branch AND
  git status --porcelain --untracked-files=all empty AND rev-list --count HEAD --not --remotes=origin == 0 AND the
  branch exists on live origin with a tip equal to or containing HEAD AND not land-pending. 437 passed (237 GiB),
  109 were younger than 12h, 0 refused on git grounds.
- Removal: generated script, one literal line per worktree, oldest completedAt first:
  guard /work/worktrees/task-XXXXXXXX && git -C /work/repos/antiphon worktree remove /work/worktrees/task-XXXXXXXX
  guard = immediately-before recheck: no /proc/*/cwd or /proc/*/fd link into the path (find -lname), status still
  clean, still 0 unpushed. No --force; no rm.
- Result: 397 worktrees removed (190.7 GiB by pre-measure), completedAt 2026-10-29T23:52Z .. 2026-11-02T12:09Z.
  3 refused by the live-process guard: task-1e52322c, task-ab6cf694, task-421e496e (Succeeded >12h but a live
  process has cwd/fds inside - left alone). 37 newest retirable worktrees (batch 6) not processed: target met.
- Class (b) bin/obj-only and (d) container/image prune and (e) /tmp were NOT run: (c) whole-worktree removal
  through git covers bin/obj, and the 25%-free gate was reached first. Nested docker had 0 exited containers anyway.
- After: 169 dirs in /work/worktrees, mirror worktree list 175 entries, 0 prunable.

## Refused / not touched
- Any docker volume, antiphon_pgdata, nuget cache (4.1G; df well below 90%), /tmp/antiphon-pty-hosts, all of /tmp.
- Blocked 0ce3bb38, Dispatched f2d12082 and e937b0c3, 109 tasks settled < 12h, 17 non-task dirs (task-cffa0c4b-*,
  task-e38d6427-*: no task row, owner unknown - need the owning tasks' say), 3 live-guarded worktrees above.

## Current largest consumers
~172 worktrees (~200G est.), /tmp 51G, nested docker images 5.5G, nuget 4.1G, ~210G invisible host-side.

## Recommendation
The runner mirror never retires terminal worktrees: 566 accumulated in ~5 days (~0.7G each, ~80G/day at current
dispatch rate), so the disk refills in ~2-3 days. CARD-0459's daily antiphon:worktree-residue job ships with
WorktreeResidue:Execute=false and is desktop-centric; CARD-0824 (per-slot reusable worktrees, Review) would bound it
structurally. Proposed Code card: a runner-side retention sweep (runner hosted service or scheduled call) that
retires mirror worktrees of terminal tasks settled > N hours with the exact guards above (clean, 0 unpushed,
live-origin containment, no live process, no land pending) via git worktree remove without force, plus a
/tmp allow-list sweep, plus a CacheDiskLow-style alarm at 85%. No existing card found (card.ps1 search: "server2
disk", "runner disk", "mirror worktree retention", "disk full", "bin/obj sweep" -> 0 hits).
Host-side ~210G needs an ssh census by the desktop lane.
Removed list: /tmp/claude-1654/-work-worktrees-task-e937b0c3/68700c00-5b22-4441-a26b-91b8b42c3cf3/scratchpad/removed.txt (scratch, not committed).
