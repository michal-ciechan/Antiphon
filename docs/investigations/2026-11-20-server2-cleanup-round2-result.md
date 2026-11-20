# Server2 cleanup round 2 result (2026-11-20)

Task `dd6c075a`, branch `feat/card-task-dd6c075a`, base `7698603099010d03b5a17b5c49cbca63c41e3824`. Run inside the server2 runner container as uid 1654 (`app`). The operator authorization of 2026-11-20 covered the classes left by [the round 1 result](2026-11-20-server2-cleanup-result.md) and anything older in those same classes. Nothing ran as root. Nothing was chmod'd or chown'd. `/state`, Docker, Postgres, `/home`, credentials, `ws-*` worktrees, and the `.git` directory of the live repo were not deleted.

## Outcome

Removed 73 Claude task directories, 830 untracked build-output directories, and 582 log files between 2026-11-20T09:14:45Z and 2026-11-20T09:18:44Z.

The 73 task directories had a premeasured apparent size of 15,822,225,969 bytes (`du -b`). The 582 logs had a premeasured apparent size of 49,700,782 bytes. The 830 build outputs were not measured path by path; `/work/worktrees` `du -b` fell by 24,322,689,600 bytes across the same window, and those 830 directories were the only deletions under `/work/worktrees`.

`df -h` on `overlay` and on `/dev/mapper/ubuntu--vg-ubuntu--lv` moved from 452G used, 225G available, 67% (09:08Z) to 413G used, 264G available, 61% (09:19:21Z).

A path check at 2026-11-20T09:19:21Z found every removed task directory absent and both skipped task directories present.

## Fresh lists

Age cutoff was 48 hours before the delete loop: 2026-11-18 09:14:45 UTC. Sizes below are apparent bytes from `du -b`. The on-disk task class is a direct child of `/tmp/claude-1654` named `-work-worktrees-task-<8 lowercase hex>`. Direct children named exactly `task-<8 hex>`: 0. `c487-probe-*` direct children of `/tmp`: 0.

Fleet `GET /api/agent-tasks?status=Dispatched,Working,Queued,Blocked&includeChecks=true` at 09:14Z returned 21 tasks. None of the ids cleaned below were in that set. A `/proc` cwd and fd walk found no holder under a selected path. Each selected directory was checked again in the delete loop: non-empty, exact parent plus a slash, leaf matches the class pattern, directory, not a reparse point, `GetFullPath` equals the string, directory and direct-child mtimes older than the cutoff, and `find -P -newermt` found nothing newer.

Samples printed before deletion:

| Apparent bytes | Path |
|---:|---|
| 26,491,530 | `/tmp/claude-1654/-work-worktrees-task-08a2e7f3` |
| 69,253,143 | `/tmp/claude-1654/-work-worktrees-task-08ac06f9` |
| 31,480,908 | `/tmp/claude-1654/-work-worktrees-task-16c6dbc2` |
| 3,905,154 | `/tmp/claude-1654/-work-worktrees-task-19af8f63` |
| 6,735,779 | `/tmp/claude-1654/-work-worktrees-task-1fa64d10` |

## Symlinks and `.git`

Round 1 left 50 task directories because they contained a symlink or a path named `.git`. All 50 were still present, all older than 48 hours, and none was a registered worktree of `/work/repos/antiphon`.

`find -P` classified them as stand-alone `.git` directories (fixture clones inside the task mirror), plus 24 symlinks. Two symlinks, both under `8ac2299c`, pointed outside the task directory at `/work/worktrees/task-8ac2299c/scripts/lib` and `/work/worktrees/task-8ac2299c/tests`. That worktree was already absent. The other 22 symlinks resolved inside their own task directory. Every symlink was removed with `Remove-Item -LiteralPath` of the link itself, with no `-Recurse`, after a reparse-point and `LinkTarget` check. A self-test on `/tmp/dd6c075a-selftest-link` confirmed the same call leaves the target in place. The outside targets were not created or deleted.

`f822e84d` had one `.git` file whose gitdir was inside the same task directory (`scratchpad/r1/.git/worktrees/w1`), not under `/work/repos/antiphon/.git/worktrees`. It was removed with the parent.

The four deleted directories over 1 GiB contained only stand-alone `.git` directories, no symlink and no gitdir under the live repo.

## Registered worktrees

`task-6379c50f` contained three registered detached worktrees. Each was clean (`git status --porcelain -uall` empty, including ignored files), each HEAD was an ancestor of `origin/master`, the owner id was not in the open-task set, and no process cwd or fd was under the tree. They were removed with `git worktree remove` (no `--force`) and `git worktree prune`, then the parent directory was removed.

| Worktree | HEAD |
|---|---|
| `.../scratchpad/cut` | `74178698ea316af2902c5ef653e8ecedecf3c698` |
| `.../scratchpad/w-135865831` | `1358658310c32e1945060a3dfc0dd4a67a3d635b` |
| `.../scratchpad/w-de27ce2fd` | `de27ce2fd8c66d260e83e1cb0f431aae54035d53` |

Full parent: `/tmp/claude-1654/-work-worktrees-task-6379c50f/0070ccc5-5710-438d-b7de-c56ddacfad1d/scratchpad/`. None of the three remains in `git worktree list`.

## Removed

| Class | Removed | Apparent bytes |
|---|---:|---:|
| Round-1 symlink or `.git` task directories | 50 | 2,755,804,687 |
| Over 1 GiB task directories (`2b7ef7b4`, `40a75a5a`, `4b605c04`, `968281fc`) | 4 | 11,980,310,425 |
| `6379c50f` after `git worktree remove` | 1 | 634,998,085 |
| Other `-work-worktrees-task-<8 hex>` caches whose owner was not open | 18 | 451,112,772 |
| Task directories total | 73 | 15,822,225,969 |
| Untracked `bin`, `obj`, `publish`, `bin-*`, `obj-*`, `publish-*` under settled `/work/worktrees/task-*` | 830 | 24,322,689,600 (`/work/worktrees` du delta) |
| `/tmp/*.log` direct children older than 48 hours | 582 | 49,700,782 |
| `c487-probe-*` | 0 | 0 |
| `Antiphon.Scripts-*` older than 7 days | 0 | 0 |

`/tmp/claude-1654` `du -b` moved from 28,516,703,830 (09:08:21Z) to 12,694,479,305 (09:19:21Z), a drop of 15,822,224,525. That is 1,444 bytes less than the premeasured task-directory total. `/tmp` `du -b -d 1` moved from 136,025,878,748 to 120,163,150,862, a drop of 15,862,727,886. The task directories plus the logs are 15,871,926,751. The gap is other writes during the window, and `du` still cannot read `/tmp/c1609rd*-host` (76 permission-denied lines, uid other than 1654).

The 18 caches corresponded to registered checkouts that are still present: `task-2abcd1c0`, `task-3e32f1e6-s2`, `task-4292c713`, `task-5b1ea772`, `task-6155693e`, `task-7555284f`, `task-934cdb25`, `task-94b17435`, `task-a2845b6c`, `task-b0e54eca`, `task-bc7a9fdd`, `task-bee64303`, `task-c6ca6929`, `task-c7a30141`, `task-d989d9d8`, `task-da90742c`, `task-ed0f7942`, `task-f23857e7`. Only the `/tmp/claude-1654` cache was removed. Two of those caches (`5b1ea772`, `2abcd1c0`) contained stand-alone `.git` directories and no live-repo gitdir.

The 830 build directories were the outermost `bin` / `obj` / `publish` / `bin-*` / `obj-*` / `publish-*` directories under the 27 `/work/worktrees/task-*` checkouts whose id was not in the open-task set. `git ls-files` showed no tracked file under any of them. None was younger than 48 hours. None had a holder. The worktrees themselves were left in place. `ws-*` was not scanned for this class.

## Skipped

| Path | Why it stayed |
|---|---|
| `/tmp/claude-1654/-work-worktrees-task-1c23b333` | Registered detached worktree `scratchpad/onmaster`, HEAD `8303e9d36e790092b4f685c2e13aaa2477903604` (2026-11-13, "docs: retain S3b red and verified final checkpoint evidence"). After `git fetch origin`, no `origin/*` ref contains it, and it is not an ancestor of `origin/master`. Still registered. |
| `/tmp/claude-1654/-work-worktrees-task-5b164f58` | Registered worktree `scratchpad/mastertree` is dirty: untracked `scripts/c1820-review-harness/Harness.csproj` and `Program.cs`. HEAD `156cae9627ebc8235c247ccf1a050c0950824f62` is not on any `origin/*` ref. `git worktree remove --force` was not used. Still registered. |
| `/tmp/antiphon-nightly-core/Antiphon.Scripts-4FACC533A5B71986FC8BFBFE` | Directory mtime 2026-11-20T04:50:03Z, inside the 7-day keep. |
| `Antiphon.Nightly-*` under `/tmp/antiphon-nightly-core` | All 39 directory mtimes are newer than 7 days. The oldest is `Antiphon.Nightly-110090ADEA15D766D8F23377` at 2026-11-13T11:31:52Z. |
| 28 `/tmp/*.log` files | 27 were younger than 48 hours. One further direct child did not pass the name, link, or path check and was not removed. |
| `/home/app/.local/share/NuGet/http-cache` | 860,933 bytes. Left in place because it is under `/home`. |
| `/home/app/.nuget/packages` | 4,536,391,847 bytes. Shared package cache in active use. |
| Compose and review trees under `/tmp` (`codex-bisect-6d7de792`, `card-1943-review-compose`, `antiphon-review-d1a9978d`, and the same set round 1 listed) | Outside the named classes. Top entries are unchanged from the before `du`. |
| `/tmp/antiphon-pty-hosts`, `/tmp/c1609rd*-host`, `/state`, Docker, Postgres, `/home` | Not in the delete set. |

The loop recorded no empty path, no parent mismatch, no leaf mismatch, no top-level link, no age failure, no open handle, and no `Remove-Item` error.

## Volume

| Tree | Before (09:08:21Z) | After (09:19:21Z) | Delta |
|---|---:|---:|---:|
| `/tmp/claude-1654` | 28,516,703,830 | 12,694,479,305 | 15,822,224,525 |
| `/tmp` (`du -b -d 1` total) | 136,025,878,748 | 120,163,150,862 | 15,862,727,886 |
| `/work/worktrees` | 71,289,224,272 | 46,966,534,672 | 24,322,689,600 |

`df -h` was the same on `overlay` and on `/dev/mapper/ubuntu--vg-ubuntu--lv` (705G).

## Largest `/tmp` entries left in place

`du -b` at 09:19Z. These were outside the deleted classes, except `/tmp/claude-1654`, which still holds the two skipped worktrees (about 2.48 GiB and 2.46 GiB at the round 1 measurement) plus slot caches and loose files.

| Apparent bytes | Path |
|---:|---|
| 120,163,150,862 | `/tmp` |
| 12,694,479,305 | `/tmp/claude-1654` |
| 3,481,386,974 | `/tmp/codex-bisect-6d7de792` |
| 3,445,139,115 | `/tmp/card-1943-review-compose` |
| 3,203,030,980 | `/tmp/antiphon-review-d1a9978d` |
| 3,199,713,278 | `/tmp/card1873-review-d16edc11` |
| 2,848,739,207 | `/tmp/c1915-compose` |
| 2,455,685,490 | `/tmp/s5c-review-d5ecb5af` |
| 2,450,142,228 | `/tmp/card-1884-review-compose` |
| 2,446,214,561 | `/tmp/c0824-compose` |
| 2,445,892,450 | `/tmp/review-18072e35-compose2` |
| 2,442,982,516 | `/tmp/c1910-merged-now` |
