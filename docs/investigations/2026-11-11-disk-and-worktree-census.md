# Disk and worktree census, 2026-11-11

Read-only census for task 05324778. Nothing was deleted, pruned, moved, or restarted. The live rc run 6591e378 was not present in any measured worktree path or branch. No fetch, gc, or prune was run. `origin/master` local and `git ls-remote` both pointed at `3fd231a2e498e463443079c40cf38711c0be8504` (committed 2026-11-11T02:33:23Z).

Sizes below are bytes and GiB (1024^3). Docker's `GB` figures are decimal (10^9): the runner worktree byte sum 397,894,346,480 matches Docker's 397.9GB. The operator's "1,642 GB used / 221 GB free" matches the GiB reading of C:, not the decimal reading.

Times are UTC. Desktop volume and the server2 read are 04:25–04:31. Directory walks of the Antiphon roots finished 05:04–05:14. Task join used that walk plus `GET /api/agent-tasks?projectId=…&unscoped=include` for six projects (9,411 tasks). The four external checkouts outside those roots were measured just after 05:24.

## Outcome

Old worktrees are being removed, slowly, by jobs that are already executing. They are not on one worktree per slot. Slot flags are off, slot directories do not exist, and every runner worktree is still an ad-hoc `task-<id8>`.

The large reclaimable pools, once each tree has aged past its floor and is not held, are:

- server2 runner worktrees: 236 terminal, clean, published trees, 386,881,912,318 bytes, 360.312 GiB (386.88 GB)
- desktop registered worktrees: 457 trees, 104.007 GiB measured, plus one nested tree of 0.203 GiB the size map missed (104.210 GiB, 111.90 GB)

Those pools are not safe to delete by hand today. Retention and desktop legacy release already own them. A volume recycle of `antiphon-runner_work` would hit the same bytes and would also destroy three live server2 tasks.

## 1. Desktop

C: volume at 04:25Z:

| | bytes | GiB | decimal GB |
|---|---:|---:|---:|
| size | 1,999,732,789,248 | 1862.395 | 1999.733 |
| used | 1,761,091,997,696 | 1640.198 | 1761.092 |
| free | 238,640,791,552 | 222.256 | 238.641 |

Live API is the desktop AppHost, `GET /api/version` SHA `7bee43614495293afbae1b0f4add019d11dacb9c` (`land-v2`, `operator-shutdown-v1`). `C:\src\Antiphon` is detached at that same SHA. Port 17202 is Aspire `dcpctrl.exe`. No production server container is running on server2; image `antiphon-server2/server:d164e93ae47a` exists and the only container from it, `antiphon-runner-state-init-1`, exited four days ago.

### Top Antiphon-owned consumers

Ubuntu's WSL `ext4.vhdx` is 5,975,834,624 bytes (5.565 GiB) and is not Antiphon's. It is left out of this list.

| # | path | bytes | GiB | what it is |
|---|---|---:|---:|---|
| 1 | `C:\Antiphon\worktrees` | 127,642,308,792 | 118.880 | 483 child directories, 4,906,785 files. Registered trees plus unregistered residue |
| 2 | `C:\Antiphon\releases` | 111,465,657,847 | 103.814 | 73 children, 1,682,190 files. `candidates` is 13.646 GiB; next nine `checkout-*` dirs are 4.042–4.470 GiB each |
| 3 | `C:\Users\lndco\AppData\Local\Docker\wsl\disk\docker_data.vhdx` | 62,968,037,376 | 58.644 | all Docker Desktop data, not only Antiphon. Not opened |
| 4 | `C:\src\Antiphon` | 19,919,318,235 | 18.552 | live checkout. `.git` 11,920,134,487 (11.101 GiB), other 5,273,194,017, `bin` 1,961,745,890, `node_modules` 577,554,322, `obj` 186,709,519. 193,996 files |
| 5 | `C:\logs\antiphon` | 11,614,675,193 | 10.817 | `session-runner` 10.560 GiB, `card-1039` 0.257 GiB |
| 6 | `C:\Antiphon\scratch` | 4,020,445,023 | 3.744 | 7 children. Largest `a37ceaf1-rc` 1.608 GiB (detached, not on `origin/master`, no task id) |
| 7 | `C:\Antiphon\evidence` | 2,365,020,209 | 2.203 | largest child `code-67a63a4f` 1.016 GiB |
| 8 | `C:\Antiphon\nightly` | 1,835,344,228 | 1.709 | `checkout` 1.709 GiB, logs ~0.001 GiB |
| 9 | `C:\Users\lndco\antiphon-backups` | 1,594,125,534 | 1.485 | 5 files. Includes `antiphon-20261110T104159Z.dump` (530,395,357) and `antiphon-20261110T114120Z.dump` (531,543,585) |
| 10 | `C:\src\Antiphon-card0417` | 1,082,891,239 | 1.009 | branch `card-0417-phone`. Dirty. Not in the measured roots above |
| 11 | `C:\Antiphon\verification` | 439,635,842 | 0.410 | 13 children |
| 12 | `C:\temp\antiphon-add64715-base` | 206,394,842 | 0.192 | detached, not on `origin/master`, no task id |
| 13 | `C:\src\rc-c1525` | 173,071,299 | 0.161 | detached HEAD `5a126f6fc5ae`, not on `origin/master` |
| 14 | `C:\src\rc-bundle-c-final` | 173,066,603 | 0.161 | detached HEAD `30cc91b1d92a`, not on `origin/master` |
| 15 | `C:\Users\lndco\AppData\Local\Docker\wsl\main\ext4.vhdx` | 109,051,904 | 0.102 | Docker Desktop main distro |

Below the cut: `C:\Antiphon\checkpoints` 44,514,334; `cleanup` 744,516; `review-evidence` 319,767; `code-evidence` 168,890. `herdr-test`, `herdr-test2`, `merge-evidence`, `repair-evidence`, and `review-artifacts` are empty. `C:\Users\lndco\OneDrive\Backups\antiphon-postgres` is 0 files, 0 bytes.

`node_modules` and build outputs are inside row 4, not separate roots.

### Git worktrees

`git worktree list` classified 475 registrations (the operator had seen 478 earlier the same day). This census did not re-list at that earlier moment.

| | count |
|---|---:|
| registered | 475 |
| on disk | 471 |
| missing on disk | 4 |
| marked prunable | 0 |
| HEAD contained in local `origin/master` | 79 |
| dirty | 3 |
| clean | 468 |
| unpublished commits (`git rev-list --count HEAD --not --remotes=origin`) | 1 |
| detached | 12 |
| branch name absent on origin | 2 |
| path or branch containing `6591e378` | 0 |

Task join (9,411 tasks: Succeeded 8,319, Failed 605, Canceled 482, Dispatched 5; no Queued, Working, or Blocked in the six projects):

| class | count | GiB |
|---|---:|---:|
| on disk, and (HEAD on `origin/master` or the named task is terminal and not open) | 461 | 123.221 |
| of which removable: also clean, unpushed 0, not the live checkout, not this census tree | 457 | 104.007 measured |
| landed only (not a terminal named task) | 7 | |
| terminal named task only (HEAD not on `origin/master`) | 386 | |
| both landed and terminal | 72 | |
| open task bound to the path or the id8 | 4 | see below |
| id8 with no task row | 0 | |
| class member with no size row | 1 | 0.203 actual, counted as 0 in the 104.007 |

Removable means: the directory exists, it is not this census tree and not `C:\src\Antiphon`, no open task is bound to the path or the id8, the HEAD is on `origin/master` or the named task is terminal, `git` reports clean, and the unpublished count is 0. Linked worktrees do not contain the object database. The 11.101 GiB `.git` at `C:\src\Antiphon` stays until a gc, which this census did not run. The 104.007 GiB is working-tree bytes only.

The unsized class member is `C:/Antiphon/worktrees/land/0f9784a67773` (detached, landed, clean, unpushed 0, no task id). The size walk records the parent directory `C:\Antiphon\worktrees\land` at 218,482,324 bytes (0.203 GiB). Adding that to the measured sum gives 104.210 GiB.

The 7+386+72 split counts registrations, including the 4 missing directories. 7+386+72 = 465. Subtract the 4 missing and the class is 461. Subtract the live checkout, this census tree, and the two open landed desktop copies, and the removable count is 457.

Ten existing registrations are outside that class: the three dirty or unpushed trees below (other than the live checkout, which is inside the class and protected), the open not-landed `card-task-9d9adb37`, and six detached checkouts with no task id whose HEAD is not on `origin/master`.

### Missing (0 bytes, still registered, not marked prunable)

All four named tasks are Succeeded:

- `C:/Antiphon/worktrees/card-task-4f9a7df9`, `feat/card-task-4f9a7df9`, Succeeded Debug server2 CARD-1514
- `C:/Antiphon/worktrees/card-task-a69a0103`, `feat/card-task-a69a0103`, Succeeded Code server2 CARD-1802
- `C:/Antiphon/worktrees/card-task-a82b3848`, `feat/card-task-a82b3848`, Succeeded Custom desktop CARD-1514
- `C:/Antiphon/worktrees/card-task-d14c79fb`, `feat/card-task-d14c79fb`, Succeeded Custom desktop CARD-1514

### Dirty (do not remove)

- `C:/src/Antiphon`, detached HEAD `7bee43614495293afbae1b0f4add019d11dacb9c`, on `origin/master`, unpushed 0, 18.552 GiB. Index clean. Untracked only: `docs/investigations/2026-09-30-card-0859-codex-startup.patch`, `docs/investigations/2026-09-30-card-0860-codex-c4.patch`. This is the live AppHost checkout.
- `C:/Antiphon/scratch/089ef857-rc-check`, detached HEAD `9e97df3f7b3d594c6872a1327502d6877feaf820`, not on `origin/master`, unpushed 0, 338,919,521 bytes (0.316 GiB). Index differs, no untracked files: `scripts/fixtures/nightly/owned-child/Program.cs`, `scripts/lib/nightly-owned-process.cs`, `scripts/lib/nightly-owned-process.ps1`, `scripts/test-nightly-native.ps1`, `tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs`.
- `C:/src/Antiphon-card0417`, branch `card-0417-phone`, HEAD `b95bb390ff2cde07d9d25c8a54aed9623e4c6aae`, not on `origin/master`, unpushed 0, 1,082,891,239 bytes (1.009 GiB). Index clean. Untracked only: `.antiphon-style.log`.

### Unpushed (do not remove)

- `C:/Antiphon/scratch/78334d64-rc`, detached (no branch), HEAD `5a690f98544f0fefc1dde7c27135f076f440288f`, 8 commits not reachable from any `origin` ref, clean, not on `origin/master`, 338,605,831 bytes (0.316 GiB).

`feat/card-task-05324778` (this tree) and `feat/card-task-cfccf493` had no matching origin branch name at classification. Both HEADs were still on `origin/master`, so the unpublished count was 0. This document is the first commit on `feat/card-task-05324778`.

### Open tasks (do not remove)

Five Dispatched tasks. No other open status in the six projects.

| id8 | role | runner | card | recorded path | on disk | registered | GiB |
|---|---|---|---|---|---|---|---:|
| 05324778 | Custom | desktop | CARD-1514 | `C:\Antiphon\worktrees\card-task-05324778` | yes | yes | 0.235 |
| 4c8eac9f | Debug | server2 | CARD-1212 | desktop path above; runner copy is `/work/worktrees/task-4c8eac9f` | yes | yes | desktop 0.213; runner 2.813 |
| 5a70a9cf | Code | server2 | CARD-1304 | same shape | yes | yes | desktop 0.213; runner 4.487 |
| 9d9adb37 | Code | server2 | CARD-1224 | same shape, desktop HEAD not on `origin/master` | yes | yes | desktop 0.213; runner 2.245 |
| 9294312a | Review | server2 | CARD-1829 | `C:\Antiphon\worktrees\card-task-9294312a` | yes | no | 0.214 |

The Review directory is inside the worktrees root but absent from `git worktree list`. It is not in the removable class. It also does not appear under `/work/worktrees` on server2.

### Unregistered children of `C:\Antiphon\worktrees`

25 child names are not themselves a registered worktree leaf. One of those, `land` (0.203 GiB), is the nested registered tree above. One is the live Review path (0.214 GiB). The other 23 sum to 15,110,528,405 bytes (14.073 GiB). They were not dirty-checked, because they are not registrations. Six `.removing-*` directories account for 772,525,897 bytes (0.720 GiB). The five largest remaining names are `card-task-0726e42d` 5.019 GiB, `card-task-e3922461` 3.354 GiB, `card-task-a7b2b64a-rc` 1.597 GiB, `card-task-a6fe70f2` 1.470 GiB, `card-task-c490e425` 1.206 GiB.

## 2. Server2

`ssh mc@server2` at 04:25:10Z, read-only. `df` on `/dev/mapper/ubuntu--vg-ubuntu--lv`:

| | bytes | GiB | df -h |
|---|---:|---:|---|
| size | 756,634,804,224 | 704.685 | 705G |
| used | 655,008,161,792 | 610.034 | 611G |
| available | 70,815,711,232 | 65.951 | 66G |

The earlier "609G used / 68G free" was a previous sample. This read is 611G used, 66G available, 91%.

`docker system df`: Images 32/28, 29.89GB, reclaimable 1.086GB (3%). Containers 40/37, 55.37GB, reclaimable 9.455GB (17%). Local Volumes 31/29, 485.6GB, reclaimable 76.55MB (0%). Build cache 385 entries, 13.45GB, all reclaimable. No `antiphon_pgdata`. No `antiphon-runner-temp_*` volume.

### All 31 volumes

Docker's own SIZE column. Role from the mount or the stack name.

| volume | size | links | role |
|---|---:|---:|---|
| `antiphon-runner_work` | 397.9GB | 2 | runner worktrees and `/work/repos`. Keep. Recycle only with `-RecycleVolumes` and an apply token |
| `antiphon-runner_runner-state` | 32.52GB | 2 | `/state`. Preserve |
| `antiphon-runner_runner-tmp` | 31.59GB | 1 | `/tmp`. Keep unless temp sweep is enabled on purpose |
| `antiphon-runner_dind-data` | 8.731GB | 1 | nested Docker. Keep |
| `openclaw-state` | 6.275GB | 1 | OpenClaw state. Not an Antiphon worktree volume |
| `antiphon-runner-cache-nuget-packages` | 4.411GB | 1 | NuGet cache. Preserve |
| `antiphon-runner-cache-npm-content` | 1.099GB | 1 | npm cache. Preserve |
| `antiphon-runner-cache-nuget-scratch` | 0B | 1 | empty NuGet scratch |
| `windmill_windmill_worker_cache` | 815.8MB | 1 | Windmill worker cache |
| `windmill_windmill_worker_native_cache` | 774.2MB | 1 | Windmill native cache |
| `antiphon-messaging_am-redpanda-data` | 256.5MB | 1 | messaging Redpanda |
| `schoolrevision-test_messaging-redpanda-test-data` | 184.6MB | 1 | school-revision test Redpanda |
| `windmill_windmill_db` | 95.23MB | 1 | Windmill Postgres |
| `antiphon-messaging_am-postgres-data` | 91.69MB | 1 | messaging Postgres |
| `schoolrevision_messaging-redpanda-data` | 83.91MB | 1 | school-revision Redpanda |
| `schoolrevision-test_school-revision-test-pgdata` | 80.85MB | 1 | school-revision test Postgres |
| `schoolrevision_school-revision-pgdata` | 80.49MB | 1 | school-revision Postgres |
| `schoolrevision-staging_school-revision-staging-pgdata` | 68.36MB | 0 | staging Postgres, unattached |
| `gym-stat_pgdata` | 66.69MB | 1 | gym-stat Postgres |
| `hermes_hermes-gateway-data` | 31.54MB | 1 | Hermes gateway |
| `hermes-data` | 26.5MB | 1 | Hermes |
| anonymous `5d80e103…` | 8.184MB | 0 | unattached |
| `portainer_data` | 2.394MB | 1 | Portainer |
| `rustdesk_rustdesk-data` | 79.07kB | 2 | RustDesk |
| `windmill_windmill_logs` | 46.86kB | 1 | Windmill logs |
| `slides_caddy_data` | 17.58kB | 1 | slides Caddy data |
| `bw-config` | 12.26kB | 1 | Bitwarden CLI config. Not opened |
| `ssh-keys` | 4.179kB | 1 | SSH key material. Not opened |
| `rclone-config` | 2.323kB | 1 | rclone config. Not opened |
| `himalaya-config` | 1.701kB | 1 | himalaya config. Not opened |
| `slides_caddy_config` | 1.262kB | 1 | slides Caddy config |

`hermes-dashboard` is Up and its container writable layer is 44.8GB. That sits in the 55.37GB container total, not in the 9.455GB reclaimable number, and not in a volume. An exited `hermes` container accounts for 9.25GB of writable layer.

### `antiphon-runner-session-runner-1`

Up 4 days, healthy, image `antiphon-server2/session-testing:d164e93ae47a`, id `6f03ea7dd3f9`. In-container `du` at 04:28Z:

| path | bytes | GiB |
|---|---:|---:|
| `/work` | 400,186,504,652 | 372.704 |
| `/work/worktrees` (sum of per-tree `du`) | 397,894,346,480 | 370.566 |
| `/tmp` | 32,496,786,008 | 30.265 |
| `/state` | 35,599,759,450 | 33.155 |
| NuGet cache | 4,446,304,360 | 4.141 |
| npm cache | 1,118,370,842 | 1.042 |
| `/var/lib/docker` | 9,165,106,022 | 8.536 |

`/state` and the caches sit a little above Docker's volume SIZE. Docker's figure is the one in the volume table.

Worktree census inside the container: 242 directories, 242 named `task-*`, 0 `ws-*` slots, 0 rehomes, retiring directory present and empty (mtime 04:00Z). `/work/workspace-slots` and `ledger.json` do not exist. No path contained `6591e378`.

| class | trees | bytes | GiB |
|---|---:|---:|---:|
| terminal task, clean, unpushed 0 | 236 | 386,881,912,318 | 360.312 |
| open (Dispatched) | 3 | 10,247,086,544 | 9.543 |
| terminal but dirty or unpushed | 3 | 765,347,618 | 0.713 |
| no matching task id | 0 | | |

236+3+3 = 242. The three open trees are the server2 rows in the desktop table (`task-4c8eac9f` 3,019,353,059 bytes, `task-5a70a9cf` 4,817,503,631, `task-9d9adb37` 2,410,229,854), all clean and unpushed 0.

Dirty or unpushed, do not remove:

- `/work/worktrees/task-ed0f7942`, branch `feat/card-task-ed0f7942`, HEAD `a41e77962fb76365a635ca8a5a2fcaf1efce0c7d`, dirty, unpushed 0, Succeeded, 223,903,317 bytes (0.209 GiB)
- `/work/worktrees/task-f23857e7`, branch `feat/card-task-f23857e7`, HEAD `50c36bf27bd018b44c5121367685a1ad548a4155`, dirty, unpushed 0, Succeeded, 224,636,853 bytes (0.209 GiB)
- `/work/worktrees/task-63cdf1df-land`, detached HEAD `48c8b0685eb0584e0c0cc7571d028098f4d5da55`, clean, 2 unpublished commits, Canceled, 316,807,448 bytes (0.295 GiB)

`/tmp` families, largest only (the top 40 names do not sum to 32.50 GB; names that would print a secret were not listed): `c487-probe` 8,538,856,433 bytes across 174 entries, `antiphon-master` 2,053,725,117, `antiphon-land-verify` 1,463,227,313 across 253, `card0395` 1,430,974,851, `c502-exit-cap` 536,249,461 across 30. On 2026-11-08 `/tmp` was about 141 GB. It is 32.50 GB now, with temp sweep still off.

### `antiphon-runner-build-slots-1`

Up 6 weeks, healthy, image `antiphon-server2/session-testing:a8b4e9e5a071`. No `/work`. Worktrees 0, slots 0. `/tmp` is 8,490,163 bytes, almost all `antiphon-logs` (8,477,875). Build slots (`SessionRunner__BuildSlots__MaxConcurrent=4`) are compile leases, not workspace slots.

## 3. Cleanup that already exists

### Runner worktree retention (running)

Shipped in `server/appsettings.json`: `RunnerWorktreeRetention` Enabled true, Execute true, Runners `server2`, cron `*/30 * * * *`, MinTerminalHours 12, MaxRemovalsPerTick 25, MaxAbsentPerTick 200, MaxTickMinutes 20. The running log records `minTerminalHours 24`, the desktop user-secret override. TempSweep Enabled false, Execute false, MinAgeHours 48, MaxEntriesPerTick 200, MaxBytesPerTick 20 GiB.

It retires `/work/worktrees/task-<8hex>` of finished tasks. It does not `rm` from this census, does not prune, fetch, or delete branches, and does not touch volumes, `/state`, `/work/repos`, or slots. `examined` is the count of server2 tasks that still have a remote worktree path, including paths whose directory is already gone. That is why examined is ~485 while only 242 directories remain. `sent` is present-tree removal requests. `absentSent` is the already-gone lane.

Today's ticks, all `absentSent 0`:

| UTC | examined | eligible | backedOff | sent |
|---|---:|---:|---:|---:|
| 00:01 | 485 | 11 | 3 | 8 |
| 00:30 | 480 | 8 | 3 | 5 |
| 01:00 | 480 | 5 | 3 | 2 |
| 01:30 | 483 | 5 | 3 | 2 |
| 02:00 | 483 | 8 | 3 | 5 |
| 02:30 | 482 | 5 | 3 | 2 |
| 03:00 | 482 | 4 | 3 | 1 |
| 03:30 | 487 | 4 | 3 | 1 |
| 04:00 | 486 | 5 | 3 | 2 |
| 04:35 | 486 | 5 | 3 | 2 |
| 05:00 | 484 | 8 | 3 | 5 |

The job is running. Eligible stays at 4–11 because of the 24h floor and holds (`settling`, `live_owner`). The 25-per-tick cap is not the current throttle, and the 2026-11-08 `already_absent` budget burn is not happening (`absentSent 0`). The 360.312 GiB is the terminal-clean population, not the set that is eligible on the next tick. The log file was still being written at 05:24Z; the 05:00 line is the last completed retention tick in it.

### Desktop legacy release (running)

`WorktreeLegacyRelease`: Enabled true, Execute true, MinTerminalHours 48, MaxReleasesPerTick 25, MaxTickMinutes 20, cron `15 * * * *`. It removes local `card-task-<id8>` worktrees of terminal tasks after the guards. Dirty and unpushed are held.

| UTC finish | examined | eligible | backedOff | filed | retired |
|---|---:|---:|---:|---:|---:|
| 00:23 | 7477 | 6499 | 16 | 4 | 4 |
| 01:26 | 7481 | 6501 | 16 | 6 | 6 |
| 02:30 | 7484 | 6502 | 16 | 7 | 7 |
| 03:25 | 7482 | 6501 | 16 | 7 | 4 |
| 04:35 | 7484 | 6505 | 16 | 7 | 9 |

Eligible ~6,500 includes tasks whose directories are already gone. The tick stops on MaxTickMinutes (about 20 minutes) before the 25 cap. Observed removals are 4–9 per hour, 30 retired across these five ticks. At that pace the 457 present removable trees take on the order of three days, and only the ones the job actually selects.

### Not running, or not installed

- Temp sweep: shipped off. `/tmp` shrank from ~141 GB on 8 November to 32.50 GB without it. Turning it on is a config change. The shipped switch is Enabled false, Execute false.
- Worktree residue, job `antiphon:worktree-residue`: Enabled true, Execute false, cron `0 10 * * *` Europe/London, MaxActionsPerRun 25. Inventory only. The data-retention sweep at 01:24Z deleted 0 worktree-residue rows.
- CARD-1008 volume recycling: scripted, not a cron. Default `redeploy-old` keeps `antiphon-runner_work`, `antiphon-runner_runner-tmp`, and `antiphon-runner_dind-data`. Deleting those needs `-RecycleVolumes` plus `ANTIPHON_VOLUME_RETIREMENT_APPLY=enabled-<operation-id>`. State and cache recycle flags are not the shipped path. `down -v` of the main stack is not a cleanup step. No temp-runner private volumes remain. `server2-temp` is retired, draining, not accepting.
- CARD-0826 host cleanup: not operational. `scripts/host-cleanup.ps1` and `scripts/host-cleanup-server2.sh` are absent from this worktree and from `C:\src\Antiphon`. `scripts/worktree-residue.ps1` is present. No host-cleanup last-run log exists, because the scripts are not on disk.

## 4. One worktree per slot

CARD-1750 (`fbd0ad8f-5a9b-4338-8171-d06fdd643259`) is InProgress, High/Soon, rank 5, human-rated. Title: turn on per-slot worktrees with the next server2 rollout and AppHost restart (temp canary first). The operator note on the card (2026-11-07) says both slot flags were off and that per-task worktrees were still in use. That is still true.

Flags read from the running server2 container environment, from compose, and from shipped settings:

| flag | value now | source |
|---|---|---|
| `PhoneHome__WorkspaceSlots__Enabled` | false | not present in the running container env; `docker-compose.server2-runner.yml` default is `false` |
| `PhoneHome:WorkspaceSlots:Enabled` | false | code default. Not overridden in `server/appsettings.json` |
| `PhoneHome:WorkspaceSlots:Headroom` | 4 | code default, unused while Enabled is false |
| `LingerRetries` | 3 | code default |
| `OverflowWaitSeconds` | 10 | code default |
| `MaxOverflowSlots` | 4 | code default |
| `SlotBytesEstimateGiB` | 3 | code default |
| `PoolDiskBudgetGiB` | 64 | code default |
| `IdleRetireHours` | 72 | code default |
| `MaxQuarantinedDirectories` | 4 | code default |
| `MaxRehomesPerSlotPerDay` | 3 | code default |
| `ColdResetSchedule` | Weekly, Sunday, 02:00 UTC | code default |
| `Delegation:WorkspaceSlotsEnabled` | false | bool default. Absent from `server/appsettings.json`. `server/appsettings.Development.json` does not exist. Restart-bound |
| `Delegation:WorkspaceSlotRoles` | `*` (every role except Mutation) | code default. Hot-reloadable, but leases nothing while the enabled flag is false |
| runner feature `workspaceSlotV1` | absent | server2 phone-home features include `workspaceRetentionV1` and `tempSweepV1`, not `workspaceSlotV1` |
| `SessionRunner__BuildSlots__MaxConcurrent` | 4 | running env. This is the build-slot lease, not a workspace slot |
| `PhoneHome__Capacity` | 10 | running env. Occupied 9 at 04:25Z. Desktop capacity 2, occupied 1 |

Slots that exist: 0 on server2, 0 in the build-slots container, 0 on the desktop. Bound to a slot: 0. Ad-hoc: 242/242 runner worktrees are `task-*`. Desktop registrations are `card-task-*` plus the named scratch, evidence, land, and `C:\src` checkouts.

What the rollout still has to do, from the card (the commands live in `docs/investigations/2026-11-06-investigate-slot-and-retention-settings.md` on `feat/card-task-374f6129`, section 5):

1. Canary `PhoneHome__WorkspaceSlots__Enabled=true` in `secrets/stack.temp.env` only, on a new temp runner. `server2-temp` is already retired, so the card's "canary on server2-temp" step needs the next rollout's temp runner, not the dead one.
2. After that canary passes, set the flag in `secrets/stack.env` and redeploy the standing runner.
3. On the desktop, set `Delegation:WorkspaceSlotsEnabled=true` and `Delegation:WorkspaceSlotRoles=none` without overwriting other user-secret keys, then restart AppHost from the main checkout at the rc tip.
4. Widen roles from `none` to `Plan` for one Worktree Plan task, prove a slot lease and release, then widen further. Mutation never leases.
5. Rollback is `WorkspaceSlotRoles=none` while leases drain, then both flags false and a restart.

Turning the flags on does not delete the 242 runner trees or the 475 desktop registrations. Retention and legacy release still own those.

## 5. Safest order

Expected bytes are working-tree bytes. They come back only after the owning job reaches each tree. They do not come back out of `C:\src\Antiphon\.git` or out of the Docker vhdx until a separate gc or compact, which this census is not recommending as a next step.

1. Leave server2 retention on. No operator action. Pool it may eventually remove: 236 trees, 386,881,912,318 bytes, 360.312 GiB (386.88 GB), after the 24h floor and the hold checks. Ready on a given tick is the log's eligible count (4–11 at 05:00), not 236. Do not raise `MaxRemovalsPerTick` or lower the 24h secret from this census. Both are operator settings.
2. Leave desktop legacy release on. No operator action. Registered removable pool: 457 trees, 104.210 GiB including the land nest (111.90 GB). The 20-minute tick is the throttle (4–9 trees an hour). The 48h floor still applies. Do not hand-delete.
3. Operator only, and only if the work is to be discarded. Desktop: `089ef857-rc-check` 0.316 GiB (modified nightly files), `Antiphon-card0417` 1.009 GiB (untracked log on `card-0417-phone`), `78334d64-rc` 0.316 GiB (8 unpublished commits). Server2: `task-ed0f7942` 0.209 GiB dirty, `task-f23857e7` 0.209 GiB dirty, `task-63cdf1df-land` 0.295 GiB with 2 unpublished commits. Combined 2.354 GiB. The live checkout's two untracked patches stay.
4. Do not delete the five Dispatched trees, including unregistered `card-task-9294312a` (0.214 GiB) and the three server2 mirrors (9.543 GiB).
5. Unregistered desktop directories other than `land` and `9294312a`: 14.073 GiB, not classified, not covered by legacy release. Includes 0.720 GiB of `.removing-*` residue. Operator decision, after a dirty check this census did not run. Residue Execute is false, so nothing will remove them on its own.
6. `C:\Antiphon\releases`, 103.814 GiB. No cleanup job. Operator chooses which `checkout-*` and whether `candidates` (13.646 GiB) stays. Not a worktree.
7. Server2 `/tmp`, 32.50 GB, temp sweep off. Largest family `c487-probe` is 8.54 GB. Enabling TempSweep is an operator config change; dry-run before Execute. Not a hand `rm`.
8. Operator Docker prune, separate from volumes: build cache 13.45 GB, stopped-container reclaimable 9.455 GB. Do not prune the running `hermes-dashboard` 44.8 GB layer as if it were reclaimable.
9. Desktop session-runner logs, 10.560 GiB of the 10.817 GiB log root. Operator log policy. Not a worktree job.
10. Do not recycle `antiphon-runner_work` (397.9 GB), `runner-state`, the NuGet or npm caches, or `dind-data`. That is an operator apply-token step and it would wipe the open tasks and the unpublished land tree. `antiphon_pgdata` is not on this host. Do not `down -v` the main stack.

One worktree per slot is step none of the disk recovery. It is CARD-1750, still InProgress, and it needs a rollout whose temp runner does not exist yet.
