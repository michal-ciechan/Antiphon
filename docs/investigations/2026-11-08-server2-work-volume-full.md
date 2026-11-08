# server2 /work volume full: measurement and reclaim plan (2026-11-08)

Task c04ce26d (Investigate, read-only). Measured 2026-11-08 14:40-15:10 UTC from the server2 runner container
(`6f03ea7dd3f9`, uid 1654). Server build `d164e93ae47a79ed175beeb9367212f7a7d5d1d5` (contains CARD-1677 S1+S2).

## Verdict

Confirmed mechanism, two independent leaks on one ext4 LV (`/dev/mapper/ubuntu--vg-ubuntu--lv`, 705 GB):

1. **Settled task mirrors are not being retired.** 398 mirrors under `/work/worktrees` = 340 GB. 390 belong to
   Succeeded/Failed/Canceled tasks. Every terminal mirror checked (229 terminal of 237 checked, largest first, 288 GB) is
   clean and its branch on origin equals HEAD. A DryRun retention preview (run `ddb57b73-0993-4bef-8f2c-af99ccb6e08d`)
   examined 2712, called 2290 eligible, and spent its whole 25-request budget (`MaxRemovalsPerTick=25`) on
   historical tasks whose mirrors are already gone (25 x `Removed/already_absent`). The 214 settled, clean,
   published mirrors aged 24-70 h (98 GB) got no row at all. The other 168 (217 GB, aged 0-24 h, 75 GB of it
   >=12 h) are held `settling`.
2. **`/tmp` is 141 GB, 117 GB of it `c487-probe-*`.** There are 2503 dirs of ~48 MB each (a built TUnit probe:
   `bin/` 47 MB). The producer is `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs:469`
   (`RunProbeAsync`). It creates `c487-probe-<guid>`, runs `dotnet build`, and never deletes the dir (no
   `Directory.Delete` in the file; 5 call sites). The rate is rising: 12 (11-02), 18, 240, 463, 516, 546, 708
   (11-08 so far). The CARD-1677 S2 runner temp sweep registers exactly this family
   (`src/Antiphon.SessionRunner/RunnerTempSweep.cs:61`) but ships OFF (`TempSweep:Enabled=false`, `Execute=false`,
   docs/docker-stack.md "Runner temp sweep (CARD-1677 S2)").

Free space was 6.3 GB at 14:40 and 3.2 GB at 15:05 UTC. It is falling about 3 GB per 25 min while
builds run.

## (1) Filesystem split

`df`: 705G size, 670-673G used, 6.3G-3.2G avail, inodes 19 %. `/work`, `/tmp`, `/state`, `/state/codex` and
`/var/lib/docker` (nested daemon) are all bind mounts of the same LV. The container `/` overlay also lives on it.

| Path | MB | Notes |
|---|---:|---|
| /work/worktrees | 348,490 | 398 `task-<8hex>` mirrors + `.antiphon-test-evidence` |
| /tmp | 141,519 | 39,270 top-level entries, table below |
| /state | 30,084 | session-runner 17,237; grok/sessions 7,824; codex 3,011; claude 1,528 |
| /work/evidence | 976 | |
| /work/repos (antiphon) | 835 | shared object store |
| /work/review-evidence, merge-, code-, test-evidence | 381 / 94 / 72 / 67 | |
| /home/app/.nuget/packages | 4,214 | NuGet cache, keep |
| /home/app/.npm/_cacache | 725 | |
| nested docker (`docker system df`) | ~7,200 | images 5.9 GB (5.7 GB reclaimable), volumes 1.3 GB |
| **visible total** | **~535,000** | **about 135-140 GB of the 670 GB is outside this container's view** (host root, host Docker daemon). Not measurable from here. |

Inside a mirror (task-ec9dfd70, 3.7 GB): `tests/` 2,835 MB, almost all `bin/Debug` (Antiphon.Tests 912, E2E 365,
Observability 222, ...); `server` 287, `src` 250, `.antiphon` 224 (`failfirst/<sha>/portable` 207). The checkout
itself is about 100 MB, so retiring a mirror frees nearly all of it.

`/tmp` by family (MB, entry count):

| Family | MB | n | Registered sweep family |
|---|---:|---:|---|
| c487-probe-<32hex> | 120,048 | 2503 | yes `c487-probe` |
| antiphon-bridge-queue-* | 9,727 | 9727 | yes `bridge-queue` |
| claude-1654 (CLI scratchpads) | 7,829 | 1 | `claude-scratch`, flag off by design |
| c<digits>* misc | 3,535 | 3329 | mostly no |
| antiphon-c1653-sock/peer/adopt/launch | 3,036/1,177/575/201 | | yes `pty-test-root` |
| c487-tw* | 2,511 | 2511 | no (unregistered) |
| antiphon-land-verify-* | 1,947 | 552 | yes `land-verify` |
| antiphon-master-8592d536 | 1,990 | 1 | no |
| antiphon-c79-stop / adoption-tests / c1636-adopt | 1,503 / 1,186 / 987 | | yes `pty-test-root` |
| card0395 | 1,190 | 1 | no |

c487-probe age: 1027 dirs older than 48 h (~49 GB, the sweep's `MinAgeHours`), 1897 older than 12 h.

## (2) Mirrors: state, publication, cleanliness

State comes from `GET /api/agent-tasks/<id8>` (the record `delegate.ps1 -Status` reads), publication from one
`git ls-remote origin 'refs/heads/feat/card-task-*'` (5640 refs), and cleanliness from `git status --porcelain
--untracked-files=all`. Retention verdicts come from preview run `ddb57b73`.

| Class | n | GB |
|---|---:|---:|
| (a) settled + clean + branch on origin == HEAD (checked) | 229 | 288.1 |
| (a?) settled, git check not reached in budget (smallest dirs) | 161 | 36.4 |
| (b) unpublished or dirty | 0 found among terminal mirrors | 0 |
| (c) live (Dispatched/Blocked) | 8 | 16.0 |

Live mirrors (do not touch): 1590d06a, b009716e (Dispatched, 1 dirty file), 7afab706, 077e181d (Dispatched,
HEAD not on origin), c04ce26d (this task), 2b953d03 (Blocked, local branch `feat/card-task-2b953d03-master`
not on origin), 317b5f73, 83c6ade8 (Blocked).

Retention verdict for present mirrors (terminal only):

| Verdict | n | GB | age h |
|---|---:|---:|---|
| no row (eligible, never sent) | 214 | 98.2 | 24-70 |
| Held `settling` | 168 | 217.5 | 0-24 (75.5 GB >= 12 h) |
| Held `sync_debt` | 8 | 8.9 | 24-57 |
| Held `live_owner` | 8 | 16.0 | live |

The run also held 206 `sync_debt`, 27 `mutation_excluded` and 5 `outside_scope` rows for mirrors that no longer
exist.

The 40 largest mirrors (GB, status, role, card, hours since completion, retention verdict, origin vs HEAD,
dirty-file count, class):

| Mirror | GB | Status | Role | Card | Age | Retention | Origin | Dirty | Class |
|---|---:|---|---|---|---|---|---|---:|---|
| task-ec9dfd70 | 3.72 | Succeeded | Merge | CARD-1514 | 5.6h | settling | equal | 0 | a |
| task-6552d9b1 | 3.71 | Succeeded | Merge | CARD-1514 | 13.4h | settling | equal | 0 | a |
| task-f1516a6c | 3.57 | Succeeded | Merge | CARD-1514 | 4.9h | settling | equal | 0 | a |
| task-8d31fc5d | 3.54 | Succeeded | Merge | CARD-1514 | 10.0h | settling | equal | 0 | a |
| task-79675854 | 3.52 | Succeeded | Merge | CARD-1514 | 3.5h | settling | equal | 0 | a |
| task-40a459ed | 3.52 | Succeeded | Merge | CARD-1514 | 8.0h | settling | equal | 0 | a |
| task-5cb10fd3 | 3.52 | Succeeded | Merge | CARD-1514 | 9.2h | settling | equal | 0 | a |
| task-7c71ea4e | 3.52 | Succeeded | Merge | CARD-1514 | 10.6h | settling | equal | 0 | a |
| task-cf0ad24d | 3.51 | Succeeded | Merge | CARD-1514 | 12.4h | settling | equal | 0 | a |
| task-90ac18a9 | 3.51 | Succeeded | Merge | CARD-1514 | 15.3h | settling | equal | 0 | a |
| task-e6423203 | 3.51 | Succeeded | Merge | CARD-1514 | 15.8h | settling | equal | 0 | a |
| task-1590d06a | 3.41 | Dispatched | Code | CARD-1780 | -h | live_owner | equal | 0 | c |
| task-ac04d0ad | 3.40 | Succeeded | Merge | CARD-1514 | 23.0h | settling | equal | 0 | a |
| task-5a2fffb9 | 3.26 | Canceled | Merge | CARD-1514 | 25.0h | NO_ROW | equal | 0 | a |
| task-df5a54fd | 3.20 | Succeeded | Merge | CARD-1514 | 20.9h | settling | equal | 0 | a |
| task-73b64ea8 | 3.20 | Succeeded | Merge | CARD-1514 | 20.5h | settling | equal | 0 | a |
| task-51d7da79 | 3.20 | Succeeded | Merge | CARD-1514 | 20.2h | settling | equal | 0 | a |
| task-4f5166ec | 3.20 | Succeeded | Merge | CARD-1514 | 22.4h | settling | equal | 0 | a |
| task-7f9d46dc | 3.16 | Succeeded | Merge | CARD-1734 | 26.0h | NO_ROW | equal | 0 | a |
| task-f2ba309e | 3.16 | Succeeded | Merge | CARD-1514 | 27.1h | NO_ROW | equal | 0 | a |
| task-dc68779f | 3.16 | Canceled | Merge | CARD-1734 | 27.4h | NO_ROW | equal | 0 | a |
| task-20805524 | 3.16 | Succeeded | Merge | CARD-1728 | 28.6h | sync_debt | equal | 0 | a |
| task-0738eb51 | 3.16 | Succeeded | Merge | CARD-1741 | 28.8h | NO_ROW | equal | 0 | a |
| task-fd0ebdc3 | 2.86 | Succeeded | Review | CARD-0824 | 49.2h | NO_ROW | equal | 0 | a |
| task-ee4f93c5 | 2.62 | Succeeded | Review | CARD-1783 | 0.6h | settling | equal | 0 | a |
| task-4c5fe834 | 2.51 | Succeeded | Review | CARD-1777 | 3.4h | settling | equal | 0 | a |
| task-d68d1868 | 2.37 | Succeeded | Code | CARD-1778 | 2.2h | settling | equal | 0 | a |
| task-4f4d35b9 | 2.36 | Succeeded | Code | CARD-1740 | 8.6h | settling | equal | 0 | a |
| task-ea539697 | 2.36 | Succeeded | Review | CARD-1740 | 7.7h | settling | equal | 0 | a |
| task-5bc0abaf | 2.36 | Succeeded | Code | CARD-1740 | 7.9h | settling | equal | 0 | a |
| task-2b953d03 | 2.34 | Blocked | Code | CARD-1514 | 19.3h | live_owner | notcontained | 0 | c |
| task-ea51697a | 2.31 | Succeeded | Merge | CARD-1514 | 0.9h | settling | equal | 0 | a |
| task-94181272 | 2.28 | Succeeded | Review | CARD-1748 | 7.1h | settling | equal | 0 | a |
| task-26d7bf3f | 2.27 | Succeeded | Code | CARD-1747 | 12.3h | settling | equal | 0 | a |
| task-b009716e | 2.27 | Dispatched | Code | CARD-1773 | -h | live_owner | equal | 1 | c |
| task-806e4892 | 2.27 | Succeeded | Code | CARD-1774 | 2.4h | settling | equal | 0 | a |
| task-52f47408 | 2.27 | Succeeded | Review | CARD-1774 | 1.5h | settling | equal | 0 | a |
| task-33905ff4 | 2.27 | Succeeded | Code | CARD-1782 | 2.6h | settling | equal | 0 | a |
| task-d53f3fba | 2.26 | Succeeded | Code | CARD-1773 | 1.4h | settling | equal | 0 | a |
| task-999353af | 2.26 | Succeeded | Code | CARD-1777 | 5.2h | settling | equal | 0 | a |

The full per-mirror join is generated evidence and is not committed.

## (3) Sanctioned reclaim procedure

- **Mirrors: CARD-1677 runner worktree retention** (docs/docker-stack.md "Runner worktree retention (CARD-1677)").
  The server job `antiphon:runner-worktree-retention` runs every 30 min and ships `Enabled=true`, `Execute=true`,
  `Runners=["server2"]`, `MaxRemovalsPerTick=25`, `MinTerminalHours=12` (`server/appsettings.json:243`). Preview:
  `POST $ANTIPHON_API/api/agent-tasks/worktree-residue/runner-retention/server2/preview`. It always sends DryRun
  and returns `{sent, runId, examined, eligible, backedOff}`. Read the rows with
  `GET /api/agent-tasks/worktree-residue/runs/<runId>?page=0..n`. It refuses (keeps) a tree that is dirty, unpushed,
  has a branch gone from origin, has a live process or session, has sync debt, is a slot, is SourceLanding or
  Mutation, or was used in the last 10 min. Execute moves the tree to `/work/worktrees-retiring/` and then runs
  `git worktree remove` without `--force`. It never runs `rm`. Kill switch: `RunnerWorktreeRetention:Enabled=false`.
  Measured preview: 25 sent, all `already_absent`, **0 bytes**. At the current ordering and budget the next real
  tick also reclaims nothing from present mirrors.
- **/tmp: CARD-1677 S2 runner temp sweep** (docs/docker-stack.md "Runner temp sweep (CARD-1677 S2)"). Settings:
  `RunnerWorktreeRetention:TempSweep:Enabled` (shipped false) and `TempSweep:Execute` (shipped false). With
  `Enabled=true` the same preview route also sends one DryRun temp sweep (Lane `RunnerTemp` rows). It acts only on
  registered families at top level. An entry must be runner-uid owned throughout, have no symlink root, hold at
  most 100,000 entries and 1 GiB, have its newest write older than 48 h, and have no holder process. At most 200
  entries and 20 GiB go per tick, deleted no-follow. Denied names and unregistered prefixes are only reported.
  Rollback: `Enabled=false` / `Execute=false`. The preview could not size it because the sweep is off. From the
  census above: c487-probe 1027 dirs older than 48 h (about 49 GB). At 200 entries per tick that is about
  9.6 GB per 30 min, so about 2.5 h to drain.
- **Rolling recycle (CARD-1008, `RecycleUnpublishedWork` / `RecycleWorktreeDirty`, `retire-temp`)** is the Docker
  volume recycle for a rollout. It does not reclaim this host's mirrors or `/tmp`, and none of its phases apply here.
- No script in `scripts/` (recycle/reclaim/prune/sweep) touches `/work/worktrees` or `/tmp` on the runner
  outside these two server-driven paths. A manual `git worktree remove` or `rm` is not a sanctioned procedure.

## (4) What grew

- c487-probe leak: about 500-700 new dirs per day since 11-04 (about 25-34 GB/day), from every Antiphon.Tests run that
  includes TestClassificationPolicyTests on server2 (Code, Review, Merge, rc qualification).
- Mirror cold Debug outputs: Merge mirrors 3.2-3.8 GB each (23 of the top 23 are Merge or rc work, mostly CARD-1514),
  Code/Review mirrors 2.3-2.4 GB. 122 tasks completed on 11-08 alone, and nothing has been retired for at least
  70 h (the oldest present terminal mirror completed 11-05).
- Checkpoint/land-audit outputs live inside the mirrors (`tools/*/bin-land-audit`, `.antiphon/failfirst/<sha>/portable`
  about 207 MB in task-ec9dfd70). They go with the mirror. No separate `bin-r*`/`bin-c*` directory exists at /work level.
- `/work/*-evidence` total about 1.6 GB. TRX and evidence are not the cause.

## (5) Ranked reclaim candidates

| # | Candidate | GB freed | Path | Who |
|---|---|---:|---|---|
| 1 | c487-probe-* older than 48 h | ~49 (then ~25-34/day ongoing) | temp sweep `Enabled=true` -> preview -> `Execute=true` | operator setting (2026-11-03 decision kept it off) |
| 2 | Settled mirrors with no row, 24-70 h, clean+published | 98.2 | retention, blocked by the per-tick budget being spent on `already_absent` | needs a decision or code (ordering/budget) |
| 3 | Settled mirrors held `settling` >= 12 h | 75.5 | retention; reason for `settling` past 12 h not established | investigate |
| 4 | Other registered /tmp families (bridge-queue, land-verify, pty-test-root) | ~15-20 | same temp sweep, slower (200 entries per tick) | as #1 |
| 5 | Nested Docker images unused | 5.7 | `docker image prune` (not sanctioned here, not run) | operator |
| 6 | Unseen ~135-140 GB outside the container | ? | host-side `du` on server2 | operator/host |

**Single safest next action:** enable the temp sweep in dry run and read its preview. Set
`RunnerWorktreeRetention__TempSweep__Enabled=true` with `TempSweep:Execute=false`, run
`curl -s -X POST "$ANTIPHON_API/api/agent-tasks/worktree-residue/runner-retention/server2/preview"`, and read the
`RunnerTemp` rows of the returned `runId`. Then set `TempSweep:Execute=true`. This deletes only registered,
48 h-idle, holder-free, runner-owned temp families and frees about 49 GB over about 5 ticks.

## Remaining uncertainties

- Whether real (non-preview) retention ticks are running at all and advancing past `already_absent` rows. The
  runner log has no retention lines, and desktop server logs were not read (desktop excluded). A recorded
  `already_absent` should not be re-sent. If it is re-sent, the backlog never drains. Resolve by reading the last
  scheduled `runner-retention:server2` residue runs on the server.
- Why `settling` holds tasks 12-24 h past completion when `MinTerminalHours=12`.
- About 165 small terminal mirrors (36 GB) were not git-checked within the budget.
- About 135-140 GB of used space is not visible from inside the runner container.

## Side effects of this investigation

- One preview POST: it wrote residue run `ddb57b73-0993-4bef-8f2c-af99ccb6e08d` (DryRun, nothing moved).
- One `git fetch -q origin` in `/work/repos/antiphon`, which updated remote-tracking refs only.
- Nothing deleted, moved, pruned or stopped.

## Not done, noted

- Fix idea (not designed): delete the probe dir in `RunProbeAsync`'s caller `finally`, and make retention skip
  `already_absent`/never-mirrored tasks before spending `MaxRemovalsPerTick`.
