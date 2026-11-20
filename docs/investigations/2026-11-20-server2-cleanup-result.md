# Server2 cleanup result (2026-11-20)

Task `cd9d14b8`, branch `feat/card-task-cd9d14b8`, base `8a89aa0e5715f86062bbcc7ee778810c2cf4cb3e`. Run inside the server2 runner container as uid 1654 (`app`). The operator authorization of 2026-11-20 covered the finished-work classes in [the 2026-11-19 tmp note](2026-11-19-server2-tmp-growth.md) and [the housekeeping census](2026-11-19-server2-housekeeping-census.md). Nothing ran as root. Nothing was chmod'd or chown'd.

## Outcome

Removed 1,033 directories between 2026-11-20T07:35:07Z and 2026-11-20T07:44:53Z. The `/tmp` trees that were removed had a premeasured apparent size of 16,402,207,930 bytes (`du -b`). The three census custom outputs add 2,332,051,507 bytes from the 2026-11-19 `du -sb` figures; this run did not measure them again before removal. `df -h` on `overlay` and on `/dev/mapper/ubuntu--vg-ubuntu--lv` moved from 466G used, 211G available, 69% to 447G used, 230G available, 67%.

A path check at 2026-11-20T07:51:10Z still found every removed path absent and every kept path present.

## Fresh lists

Classification clock was 2026-11-20T07:25:54Z UTC. The 24-hour cut was 2026-11-19 07:25:54. The 48-hour cut was 2026-11-18 07:25:54. Sizes are apparent bytes from `du -b`.

The on-disk task class is a direct child of `/tmp/claude-1654` named `-work-worktrees-task-<8 lowercase hex>`. That is the slash-to-hyphen form of cwd `/work/worktrees/task-<8 hex>`, and it is the class Appendix B of the 19 Nov note lists. Direct children named exactly `task-<8 lowercase hex>`: 0.

| Set | Count | Apparent bytes |
|---|---:|---:|
| `-work-worktrees-task-<8 hex>` children | 930 | |
| Selected for deletion | 905 | 10,570,083,786 |
| Classification stays | 25 | |
| `c487-probe-<32 hex>` direct children of `/tmp` | 175 | 8,587,928,831 |
| `c487` selected | 175 | 8,587,928,831 |

A deep `find` for any entry newer than the cutoff, over the provisional lists, wrote nothing to `task-too-new.txt` or `c487-too-new.txt`. That walk follows symlinks. The delete-time age check used the directory mtime and the mtime of each direct child, and treated a direct-child reparse point as too new.

The 905 is one fewer than the 19 Nov class B count of 906. `task-3e32f1e6` matches registered worktree `/work/worktrees/task-3e32f1e6-s2` (the segment `task-3e32f1e6-`), so it stayed. The 19 Nov byte figures are block-rounded and are a different measurement from this `du -b`.

Samples printed from the selected lists before deletion:

| Apparent bytes | Path |
|---:|---|
| 1,936,007 | `/tmp/claude-1654/-work-worktrees-task-00011235` |
| 2,255,685 | `/tmp/claude-1654/-work-worktrees-task-006e0b89` |
| 2,054,724 | `/tmp/claude-1654/-work-worktrees-task-00a0b2d1` |
| 787,712 | `/tmp/claude-1654/-work-worktrees-task-00abf1bf` |
| 6,091,910 | `/tmp/claude-1654/-work-worktrees-task-00d82b2d` |
| 49,072,133 | `/tmp/c487-probe-009886dab56a407d83af4c13d034d2c7` |
| 49,072,100 | `/tmp/c487-probe-02759495a8d446028ee59f548d1c3b41` |
| 49,072,093 | `/tmp/c487-probe-027d12399caa40c3ba063f25230700b3` |
| 49,072,173 | `/tmp/c487-probe-03942f8165d44e9da8ed953fad4ba11d` |
| 49,082,609 | `/tmp/c487-probe-07becd79951f4ad9bffeba5b0f5cb290` |

At the start of the delete loop the runner `GET /sessions` returned 200 with 4 Running sessions, `git worktree list` of `/work/repos/antiphon` had 79 paths, and the `/proc` cwd and fd walk had 453 paths.

## Removed

Each path was checked again inside the loop: non-empty, exact parent plus a slash, leaf matches the class pattern, `GetFullPath` equals the string, directory, not a reparse point, then the class age, holder, mirror, and worktree checks. Removal was `Remove-Item -LiteralPath -Recurse -Force`. The loop recorded no `Remove-Item` error.

| Class | Removed | Apparent bytes |
|---|---:|---:|
| `c487-probe-<32 hex>` | 175 | 8,587,928,831 |
| `-work-worktrees-task-<8 hex>` | 855 | 7,814,279,099 |
| Census custom outputs | 3 | 2,332,051,507 (19 Nov `du -sb`) |
| Total | 1,033 | 18,734,259,437 |

The 855 task directories are the 905 selected minus 50 kept at delete time because they contain a symlink or a path named `.git`. Their premeasured bytes are 10,570,083,786 minus 2,755,804,687.

Census paths removed, parent checkouts left in place:

- `/work/worktrees/task-3e32f1e6-s2/tests/Antiphon.Tests/bin-r1969-stamp` (1,153,018,792)
- `/work/worktrees/task-7555284f/tests/Antiphon.E2E/bin-merge-e2e` (933,184,937)
- `/work/worktrees/task-7555284f/tests/Antiphon.E2E/bin-merge-e2e-env` (245,847,778)

Each parent was a registered worktree. Neither the parent nor the custom-output path was in the proc-path list. Every `state.json` under the parent's `.antiphon/checkpoints` had `phase=done`. The directory was older than 24 hours. `find -P` found no symlink and no `.git` name inside it.

The five census script identities were already absent, so they contributed no bytes:

- `/tmp/antiphon-nightly-core/Antiphon.Scripts-73ECD7BFBCFB0FFDB2863099`
- `/tmp/antiphon-nightly-core/Antiphon.Scripts-9EE4E4E3EB6967F3AAC9E999`
- `/tmp/antiphon-nightly-core/Antiphon.Scripts-B9B80E7EEB765F7CE066C7CA`
- `/tmp/antiphon-nightly-core/Antiphon.Scripts-EACE8C1799FA29248E89968B`
- `/tmp/antiphon-nightly-core/Antiphon.Scripts-974E802812EB9B902F36EA55`

`/tmp/antiphon-nightly-core/Antiphon.Scripts-4FACC533A5B71986FC8BFBFE` is present, directory mtime 2026-11-20 04:50 UTC, inside the 7-day keep. It was not a candidate.

## Volume

`df -h` was the same on `overlay` and on `/dev/mapper/ubuntu--vg-ubuntu--lv` (705G). 2026-11-20T07:18:18Z and 2026-11-20T07:35:07Z: 466G used, 211G available, 69%. 2026-11-20T07:44:53Z: 447G used, 230G available, 67%.

| Tree | Before | After | Delta |
|---|---:|---:|---:|
| `/tmp/claude-1654` (`du -b`, 07:26:11Z / 07:45:10Z) | 36,329,605,281 | 28,515,326,182 | 7,814,279,099 |
| `/tmp` excluding `claude-1654` (`du -b -d 1` total, 07:26:28Z / 07:45:07Z) | 116,080,431,195 | 107,501,114,413 | 8,579,316,782 |
| Sum | 152,410,036,476 | 136,016,440,595 | 16,393,595,881 |

The Claude delta equals the premeasured bytes of the 855 removed task directories. The rest of `/tmp` dropped 8,612,049 bytes less than the 175 c487 apparent bytes. Other agents were writing during the window, and `du` omits entries it cannot read (`/tmp/c1609rd*-host` is one such set). The `df` step is the volume result for those ten minutes, including the three `/work` census trees.

## Classification stays (25)

Reason tags overlap. Across the 25: 21 `worktree-list`, 17 `mirror-exists`, 6 over 1 GiB (1,073,741,824 bytes), 1 explicit `6379c50f`. The 19 Nov note's "four" over 1 GiB are the mirror-absent subset. Two more directories are also over 1 GiB and are registered worktrees. All six stayed. All 25 were still present at 07:51:10Z.

| Apparent bytes | Id | Why it stayed |
|---:|---|---|
| 2,476,037,684 | `1c23b333` | over 1 GiB, worktree list |
| 3,240,426 | `2abcd1c0` | mirror `/work/worktrees/task-2abcd1c0`, worktree list |
| 4,443,653,757 | `2b7ef7b4` | over 1 GiB |
| 33,232 | `3e32f1e6` | worktree list (`/work/worktrees/task-3e32f1e6-s2`) |
| 2,920,015,006 | `40a75a5a` | over 1 GiB |
| 26,657 | `4292c713` | mirror exists, worktree list |
| 1,964,721,105 | `4b605c04` | over 1 GiB |
| 2,464,333,692 | `5b164f58` | over 1 GiB, worktree list |
| 12,396,292 | `5b1ea772` | mirror exists, worktree list |
| 1,334,333 | `6155693e` | mirror exists, worktree list |
| 634,998,085 | `6379c50f` | explicit exclusion, worktree list |
| 1,646,130 | `7555284f` | mirror exists, worktree list |
| 71,414 | `934cdb25` | mirror exists, worktree list |
| 15,861,841 | `94b17435` | mirror exists, worktree list |
| 2,651,920,557 | `968281fc` | over 1 GiB |
| 12,425,691 | `a2845b6c` | mirror exists, worktree list |
| 20,056,217 | `b0e54eca` | mirror exists, worktree list |
| 341,934,910 | `bc7a9fdd` | mirror exists, worktree list |
| 13,478,870 | `bee64303` | mirror exists, worktree list |
| 2,117,287 | `c6ca6929` | mirror exists, worktree list |
| 8,713,749 | `c7a30141` | mirror exists, worktree list |
| 14,430,989 | `d989d9d8` | mirror exists, worktree list |
| 1,269,818 | `da90742c` | mirror exists, worktree list |
| 854,398 | `ed0f7942` | mirror exists, worktree list |
| 1,220,518 | `f23857e7` | mirror exists, worktree list |

Full path for each id: `/tmp/claude-1654/-work-worktrees-task-<id>`.

## Skipped during the delete loop (55)

50 of the 905 selected task directories contain a symlink or a path named `.git` (scratchpad fixture trees). Premeasured apparent bytes 2,755,804,687. `Remove-Item -Recurse` follows links, so these 50 stayed. A `find -P` recount found 43 with a `.git` name and no symlink, 4 with a symlink and no `.git` name (`6c5b2084`, `8ac2299c`, `d9de1c95`, `e418768c`), and 3 with both (`436ebdc6`, `5b21d0be`, `777ca96d`). All 50 were still present at 07:51:10Z.

Parent `/tmp/claude-1654/`, leaf `-work-worktrees-task-<id>`:

`08a2e7f3`, `08ac06f9`, `16c6dbc2`, `19af8f63`, `1fa64d10`, `2377d73d`, `3997c63a`, `3e76de6d`, `436ebdc6`, `437a6c9a`, `4f64e784`, `4f9a7df9`, `5b21d0be`, `5b3c6c26`, `5cd1f66f`, `5ef0b4bc`, `67e63dbb`, `6c3a5d56`, `6c5b2084`, `715407ca`, `777ca96d`, `7dea795f`, `84d0a026`, `872e19d8`, `8ac2299c`, `961d90c8`, `988017cf`, `a494d175`, `aa63b6d1`, `bb0a6ce8`, `bce24013`, `bd1e6657`, `c270da97`, `c9cb7349`, `ca5bf90c`, `d3c686cb`, `d9de1c95`, `dfecf4e4`, `e1349cf7`, `e418768c`, `ebfb9548`, `ec8d925b`, `edb215b1`, `ef4c37b4`, `f189bd64`, `f2976027`, `f29c3d90`, `f5eb193f`, `f60492a2`, `f822e84d`.

The other five skips are the absent script identities listed above. The loop recorded no empty path, no parent mismatch, no leaf mismatch, no link at the top of a selected directory, no age failure, no open handle, and no live-session child.

## Largest `/tmp` entries left in place

`du -b` at 07:45Z. These were outside the authorized classes.

| Apparent bytes | Path |
|---:|---|
| 28,515,326,182 | `/tmp/claude-1654` |
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

`/tmp/c1609rd*-host` remains, owned by uid 1916, unreadable by uid 1654. At 07:51:10Z the live counts were 0 `c487-probe-<32 hex>` directories, 75 `-work-worktrees-task-<8 hex>` directories (the 25 stays plus the 50 symlink-or-git skips), and 0 exact `task-<8 hex>` directories.

`/state`, live `/work` worktrees, Docker, Postgres, NuGet, `.git` of the live repo, `/home`, credential directories, `ws-*` scratch, and loose files under `/tmp/claude-1654` were outside this deletion.
