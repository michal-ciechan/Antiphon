# Server2 housekeeping census (2026-11-19)

Read-only. Nothing was deleted, pruned, restarted, or deployed. Windows qualification run 34d41b7e was not contacted. The only name hits for that run are tracked investigation docs already in the checkouts.

Load at start, 2026-11-19T18:40:12Z: `2.51 2.61 4.49 4/3891`. Checkout `feat/card-task-b0abf10c` at `5be178005f06aa868bbb038e561c2ef22a654cb5`.

## Verdict

The hyphenated `bin-*` trees are 2,355,700,000 bytes across 48 directories. Three of them, all custom outputs of checkpoint runs whose `state.json` says `phase=done` and whose worktree had no live process, are 2,332,051,507 bytes. Those three are safe to remove by the literal commands below. The other `bin-*` paths, every checkpoint receipt store, native `bin/` and `obj/`, `/tmp`, NuGet, and Docker are needs-owner.

The space the host is actually using is elsewhere. `df -h` on the container overlay and on `/dev/mapper/ubuntu--vg-ubuntu--lv` both showed 705G size, 434G used, 243G avail, 65%. Visible `du -sh` at the same time:

| Path | du |
|---|---|
| `/tmp` | 135G |
| `/work` | 53G (49G of that is `/work/worktrees`) |
| `/state` | 41G |
| `/home` | 5.5G (`/home/app/.nuget` is 4,536,400,244 bytes) |
| `/usr` | 2.1G |
| `/opt` | 612M |
| `/var` | 49M (`/var/cache/antiphon` is 35,729,408 bytes) |

That visible sum is about 237G. The other ~197G of the 434G is not in these trees (other container layers on the same volume are the unmeasured remainder). No `obj-*` directory exists under `/work/worktrees`. No directory named `scratch`, `temp`, or `tmp` exists under the worktrees outside pruned `bin/`, `obj/`, `.git`, and `node_modules`.

## How this was measured

`find` over `/work/worktrees`, pruning `.git` and `node_modules`, for directories named `bin-*` or `obj-*`. A second pass looked for `scratch`, `temp`, and `tmp` directory names. `du -sb` on each `bin-*` hit. `du -sb` on each `.antiphon/checkpoints` root. `state.json` field `phase` (example `task-bc7a9fdd` run `20261107-150139-8f8d`, line 3 of that file) plus `exitCode`, `endedAt`, and `executorPid`. Process cwd from `/proc/*/cwd` and an fd walk for paths containing `bin-`, `obj-`, `aslot-`, `antiphon-nightly-core`, `.antiphon/checkpoints`, or `nuget-scratch` (zero holders). `du -xhd1 /tmp` (133,506 entries in `/tmp`). `docker system df` and `docker system df -v`. No prune.

Age cutoff used below: older than 24h means directory or run time before 2026-11-18T18:40Z.

## Worktree slots

41 directories under `/work/worktrees`: 27 `task-*` checkouts, 13 `ws-*` slot mirrors (`ws-01` through `ws-12`, including `ws-01-d4`, `ws-02-d3`, `ws-03-d2`, `ws-04-d2`, `ws-10-d2`), and `c0824-compose-out`. Live cwd at 18:44Z:

| pid | cwd |
|---|---|
| 2133 | `/work/worktrees/ws-10-d2` |
| 30555 | `/work/worktrees/ws-10/tests/Antiphon.Tests/bin/Debug/net9.0` |
| 25157 | `/work/worktrees/ws-03-d2` |
| 25215 | `/work/worktrees/ws-04-d2` |
| 26127 | `/work/worktrees/ws-05` |
| 27028, 30143 | `/work/worktrees/ws-06` (this census) |

No process cwd was under a `task-*` checkout or under `/tmp`. At 18:53Z a second cwd walk still found nothing under `/tmp`.

Per-checkout `du -sh` falls in two bands. Twenty checkouts are 232M–270M. Eighteen are 1.4G–3.8G. On `task-f87fdda9` (2.3G, no `bin-*`) native directories named exactly `bin` or `obj` sum to 2.1G. On `task-e7d7d78b` (232M) that sum is 0. `.git` in both is a 4K gitfile, so the extra gigabytes are native build residue. `docs/testing-and-build.md` line 1395 says native `bin/` and `obj/` stay until worktree retirement. They are needs-owner.

## `bin-*` outside checkpoint stores

`du -sb` bytes and directory mtime. Newest file matches the directory mtime except `bin-merge-e2e-env`, whose newest file is `2026-11-08T16:02:38Z`.

| Bytes | Mtime (UTC) | Path | Class |
|---|---|---|---|
| 1153018792 | 2026-11-17T17:12:59 | `/work/worktrees/task-3e32f1e6-s2/tests/Antiphon.Tests/bin-r1969-stamp` | safe |
| 933184937 | 2026-11-08T16:04:58 | `/work/worktrees/task-7555284f/tests/Antiphon.E2E/bin-merge-e2e` | safe |
| 245847778 | 2026-11-08T16:02:38 | `/work/worktrees/task-7555284f/tests/Antiphon.E2E/bin-merge-e2e-env` | safe |
| 431277 | 2026-11-18T21:57:19 | `/work/worktrees/ws-10-d2/tools/Antiphon.LandGraphAudit/bin-land-audit` | needs-owner |
| 431273 | 2026-11-18T21:40:47 | `/work/worktrees/ws-01-d4/tools/Antiphon.LandGraphAudit/bin-land-audit` | needs-owner |
| 401089 | 2026-11-10T18:13:56 | `/work/worktrees/task-0ddd898d/tools/Antiphon.LandGraphAudit/bin-land-audit` | needs-owner |
| 364661 | 2026-11-10T05:27:55 | `/work/worktrees/task-f4320f2f/tools/Antiphon.LandGraphAudit/bin-land-audit` | needs-owner |
| 190807 | 2026-11-08T07:37:37 | `/work/worktrees/task-ec9dfd70/tools/Antiphon.LandGraphAudit/bin-land-audit` | needs-owner |
| 189411 | 2026-11-07T16:28:46 | `/work/worktrees/task-4f5166ec/tools/Antiphon.LandGraphAudit/bin-land-audit` | needs-owner |
| 32702 | 2026-11-07T16:28:46 | `/work/worktrees/task-4f5166ec/tools/Antiphon.LandGraphAudit.Core/bin-land-audit` | needs-owner |
| 32698 | 2026-11-08T07:37:37 | `/work/worktrees/task-ec9dfd70/tools/Antiphon.LandGraphAudit.Core/bin-land-audit` | needs-owner |
| 32698 | 2026-11-08T21:20:28 | `/work/worktrees/task-6155693e/tools/Antiphon.LandGraphAudit.Core/bin-land-audit` | needs-owner |

Safe reason for the three large paths: each sits on a `task-*` checkout with no live cwd; the matching `.antiphon/checkpoints` run is `phase=done` (`20261117-170738-21e9` exit 1 ended 2026-11-17T17:14:08Z; `20261108-154318-ff17` exit 2, `20261108-155930-87d5` exit 2, `20261108-160503-ece2` exit 0, all ended 2026-11-08); no fd holder; content older than 24h. They are gitignored custom outputs. The branches `feat/card-task-7555284f` (`eaf879a9f71ac794c37c4108b6636fee05fb5f93`) and `feat/card-task-3e32f1e6` (remote tip `3015db7a60196f9b81310133485a7b2872ea4136`; the s2 checkout is detached at `5bce70af169726aef02cbc7ee78b7558a06cbfee`) still exist on origin, so the checkouts themselves are not safe to remove.

`bin-land-audit` is needs-owner. The `ws-*` copies are on assigned slots and are younger than 24h (`ws-10-d2` also has a live cwd). The `task-*` copies are idle and old, but no `state.json` ties them to a finished run, and each checkout's `feat/card-task-*` ref is still the origin tip. `scripts/cleanup-build-junk.ps1` lines 4–8 and 22–24 refuse to delete on name and age alone. Same rule at `docs/testing-and-build.md` lines 489–496.

## Checkpoint stores

13 roots, 43 runs, every `state.json` `phase` is `done`. No executor pid from those files was alive. `du -sb` of the root:

| Bytes | Root | Class |
|---|---|---|
| 183838118 | `/work/worktrees/task-7555284f/.antiphon/checkpoints` | needs-owner |
| 22031055 | `/work/worktrees/task-2b953d03/.antiphon/checkpoints` | needs-owner |
| 10706716 | `/work/worktrees/task-934cdb25/.antiphon/checkpoints` | needs-owner |
| 10654296 | `/work/worktrees/ws-10/.antiphon/checkpoints` | needs-owner |
| 4782968 | `/work/worktrees/task-4292c713/.antiphon/checkpoints` | needs-owner |
| 4185357 | `/work/worktrees/task-3e32f1e6-s2/.antiphon/checkpoints` | needs-owner |
| 3948127 | `/work/worktrees/ws-02-d3/.antiphon/checkpoints` | needs-owner |
| 3120050 | `/work/worktrees/task-c7a30141/.antiphon/checkpoints` | needs-owner |
| 3046961 | `/work/worktrees/task-a2845b6c/.antiphon/checkpoints` | needs-owner |
| 2818637 | `/work/worktrees/ws-10-d2/.antiphon/checkpoints` | needs-owner |
| 2547682 | `/work/worktrees/task-5b1ea772/.antiphon/checkpoints` | needs-owner |
| 2385439 | `/work/worktrees/task-91085723/.antiphon/checkpoints` | needs-owner |
| 914062 | `/work/worktrees/task-bc7a9fdd/.antiphon/checkpoints` | needs-owner |

Total 254,979,468 bytes. The `bin-*` directories inside `builds/` are the small siblings of the project outputs (largest inside a store is 1,345,080 bytes at `task-3e32f1e6-s2/.../builds/bin-r1969-stamp`). They stay with the receipt. `ws-02-d3` run `20261119-180815-becf` ended 2026-11-19T18:22:54Z exit 0 and still had `.terminal-publication.lock` and `.cleanup.lock` at 18:22Z. `ws-10-d2` run `20261118-214117-8a3d` ended 2026-11-18T21:51:27Z, younger than 24h, and the slot has a live cwd. `ws-10` has ten done runs ended 2026-11-18T06:19Z through 2026-11-18T10:00Z and a live test process in native `bin/Debug`. Receipts are the evidence of those runs. Needs-owner.

## Nightly core cache

`/tmp/antiphon-nightly-core` is 5,494,398 bytes, mtime 2026-11-19T18:02:11Z. `scripts/lib/nightly-core.ps1` lines 81–84 delete a previous `Antiphon.Nightly-*` or `Antiphon.Scripts-*` identity only after a newer build of that same name, and only when the directory is older than 7 days. Five `Antiphon.Scripts-*` directories are older than 7 days and were not open (the fd walk matched nothing under this cache). No newer Scripts identity exists, so the sweeper has not run for them.

| Bytes | Directory mtime (UTC) | Path |
|---|---|---|
| 145962 | 2026-11-09T02:26 | `/tmp/antiphon-nightly-core/Antiphon.Scripts-73ECD7BFBCFB0FFDB2863099` |
| 148590 | 2026-11-09T03:33 | `/tmp/antiphon-nightly-core/Antiphon.Scripts-9EE4E4E3EB6967F3AAC9E999` |
| 176670 | 2026-11-10T03:01 | `/tmp/antiphon-nightly-core/Antiphon.Scripts-B9B80E7EEB765F7CE066C7CA` |
| 243154 | 2026-11-11T21:53 | `/tmp/antiphon-nightly-core/Antiphon.Scripts-EACE8C1799FA29248E89968B` |
| 243790 | 2026-11-12T01:44 | `/tmp/antiphon-nightly-core/Antiphon.Scripts-974E802812EB9B902F36EA55` |

Those five are safe (958,166 bytes). The fourteen `Antiphon.Nightly-*` directories are dated 2026-11-12T20:39Z through 2026-11-19T18:02Z, inside the 7-day keep. Needs-owner.

## `/tmp`

133,506 entries. `du -xhd1` rows sum to about 92.6G of `G` entries, 43,924M of `M` entries, and 1,029,684K of `K` entries. No process cwd was under `/tmp`. Families, not a license to delete by glob:

| Family | Count | What du showed | Mtime span (UTC) | Class |
|---|---|---|---|---|
| `/tmp/claude-1654` | 1 directory, 30G | per-worktree subdirs, including 1.2G for `ws-02-d3` | directory 2026-11-19T17:59:05 | needs-owner |
| `c487-probe-*` | 175 | each reported 48M | 2026-11-08T13:44:36 through 2026-11-13T03:43:15 | needs-owner |
| `antiphon-land-verify-*` | 352 | 24 of them 94M; the rest smaller | 2026-11-06T15:52:39 through 2026-11-18T19:49:19 | needs-owner |
| `antiphon-pwsh-cache-*` | 160 | 13 of them 88M | 2026-11-13T15:06:49 through 2026-11-19T18:49:19 | needs-owner |
| `c502-exit-cap-*` | 72 | 6 of them 561M | 2026-11-09T01:37:33 through 2026-11-17T16:49:14 | needs-owner |
| sdk 6-character dirs | 2707 | `du` 4.0K each; 2392 older than 24h | mixed | needs-owner |
| `aslot-55e1874a` | 1 | 24,659 bytes | 2026-11-19T08:49 | needs-owner |

`/tmp/claude-1654`: pid 1654 is dead, but the directory is younger than 24h and holds a cache for the live `ws-02-d3` slot. Needs-owner.

`c487-probe-*`: all 175 are older than 24h and unheld by cwd, about 8.4G at the reported 48M each. Age and a name are not deletion authority (`cleanup-build-junk.ps1` lines 4–8). No board row was read to prove CARD-0487 finished. Needs-owner until an owner accepts the literal list.

`antiphon-land-verify-*`: `docs/testing-and-build.md` line 501 says the landing verifier retains its external artifacts directory. The newest is younger than 24h. Needs-owner.

`antiphon-pwsh-cache-*`: the newest directory was written at 18:49Z, during this census. Needs-owner.

`/tmp/aslot-55e1874a`: CARD-1981 lease directory (`docs/testing-and-build.md` around line 1366). Owner pid files `23187`, `31893`, `9307`, `9365`, and `9409` are all dead. The directory is younger than 24h. Needs-owner until it ages out.

Six-character `/tmp` directories are the SDK `RestoringCommand` leftovers described at `docs/testing-and-build.md` line 1366 when `TMPDIR` was not an `aslot-*`. 2,392 are older than 24h. At 4K each they are not the 135G. Needs-owner as a class; a sweeper can emit literal paths after an empty-dir and cwd check.

Other single directories at or above 1G, all needs-owner. Mtimes are the directory mtime. None had a live cwd. Several names say `kept`:

| du | Mtime (UTC) | Path |
|---|---|---|
| 3.4G | 2026-11-15T07:26:21 | `/tmp/card-1943-review-compose` |
| 3.1G | 2026-11-13T15:07:15 | `/tmp/card1873-review-d16edc11` |
| 3.1G | 2026-11-13T13:09:32 | `/tmp/antiphon-review-d1a9978d` |
| 2.7G | 2026-11-14T19:20:04 | `/tmp/c1915-compose` |
| 2.4G | 2026-11-16T23:11:35 | `/tmp/s5c-review-d5ecb5af` |
| 2.4G | 2026-11-14T20:36:48 | `/tmp/review-18072e35-compose2` |
| 2.4G | 2026-11-15T07:20:42 | `/tmp/card-1884-review-compose` |
| 2.4G | 2026-11-14T03:25:09 | `/tmp/c1910-merged-now` |
| 2.4G | 2026-11-13T08:56:54 | `/tmp/c1836-s4a-combined-53820e0f` |
| 2.4G | 2026-11-14T21:27:24 | `/tmp/c0824-compose` |
| 2.3G | 2026-11-14T20:50:30 | `/tmp/review-18072e35-master` |
| 2.3G | 2026-11-16T04:53:38 | `/tmp/cc5b4299-rc22` |
| 2.3G | 2026-11-16T04:53:37 | `/tmp/cc5b4299-rc21` |
| 2.3G | 2026-11-14T15:44:19 | `/tmp/card1901-e594649c-base` |
| 2.3G | 2026-11-15T04:05:26 | `/tmp/card-1925-mutant` |
| 1.9G | 2026-11-16T18:06:12 | `/tmp/card-1908-cf58c961` |
| 1.8G | 2026-11-13T02:57:39 | `/tmp/task-08e79898-stream-target-red` |
| 1.8G | 2026-11-13T03:03:04 | `/tmp/task-08e79898-stream-target-green` |
| 1.8G | 2026-11-13T03:06:20 | `/tmp/task-08e79898-stream-phases` |
| 1.8G | 2026-11-13T05:26:35 | `/tmp/rhp-f84b96d344cb496c81991cfd69c5fefb` |
| 1.8G | 2026-11-13T05:27:08 | `/tmp/rhp-e055c0b20ab949f7bc1a9e129e731182` |
| 1.8G | 2026-11-13T10:50:00 | `/tmp/rh-bacbef2b6dba4fc0b4ca520d051692f2` |
| 1.4G | 2026-11-11T00:42:53 | `/tmp/card0395` |
| 1.4G | 2026-11-17T08:53:23 | `/tmp/card-1964-red-d32418928` |
| 1.2G | 2026-11-14T02:29:18 | `/tmp/c1886-layering-red` |
| 1.1G | 2026-11-13T11:12:24 | `/tmp/s5a-bin-c1836-s5a-kept-9afb7a90` |
| 1.1G | 2026-11-18T08:26:14 | `/tmp/card-1976-kept-proof` |
| 1.1G | 2026-11-18T09:07:48 | `/tmp/card-1976-kept-proof-6b8e` |
| 1.1G | 2026-11-13T10:53:23 | `/tmp/card-1850-s2-bin-c1850-s2-green-kept` |
| 1.1G | 2026-11-17T19:29:12 | `/tmp/c1893-bin-c1893s0-old` |
| 1.1G | 2026-11-13T15:40:51 | `/tmp/c1884-bin-guards-344c8f13` |
| 1.1G | 2026-11-15T03:15:03 | `/tmp/c1820-r4-green-run1-4f8dc817` |
| 998M | 2026-11-14T21:35:07 | `/tmp/c0824-witness` |
| 903M | 2026-11-18T08:40:57 | `/tmp/card-1976-kept-contract` |
| 903M | 2026-11-18T09:25:29 | `/tmp/card-1976-kept-contract-6b8e` |
| 631M | 2026-11-13T06:34:07 | `/tmp/node-compile-cache` |
| 479M | 2026-11-13T14:00:28 | `/tmp/cpu-trace-fdf6e86b` |
| 128M | 2026-11-07T11:33:46 | `/tmp/dotnet-tools` |

`/tmp/dotnet-tools` and `/tmp/node-compile-cache` are shared caches. Needs-owner even though they are old.

## Session state and package cache

`/state` is 41G: `session-runner` 24G, `grok` 12G, `codex` 4.2G, `claude` 1.7G. That is live runner state, not a build leftover. Needs-owner. `/home/app/.nuget` (4,536,400,244 bytes) is the shared package cache (`NUGET_PACKAGES` in `docker-compose.server2-runner.yml`). Needs-owner. `/var/cache/antiphon/nuget-scratch` is mode 0700 and is the preserved NuGet lock scratch (`docs/docker-stack.md`). Needs-owner.

## Docker, not pruned

`docker system df` at census time:

| Type | Total | Active | Size | Reclaimable |
|---|---|---|---|---|
| Images | 20 | 2 | 6.827GB | 6.637GB (97%) |
| Containers | 2 | 0 | 0B | 0B |
| Local Volumes | 21 | 1 | 1.403GB | 1.021GB (72%) |
| Build Cache | 0 | 0 | 0B | 0B |

`docker system df -v` shows one container link on `antiphon-test-pg:4eb5a9ee3a8579c0b7d9f0b8e3aa3142` and one on `c1609rda1b88c089c722bb7:audit`. One volume has LINKS 1 and is 381.8MB. An unlinked anonymous volume is 884.5MB. The named `c1609…` volumes are tens of bytes and unlinked. All of this is needs-owner. This census did not prune.

## Commands for the safe paths

Not run. One literal absolute path each. No glob, no variable.

```
rm -rf -- /work/worktrees/task-3e32f1e6-s2/tests/Antiphon.Tests/bin-r1969-stamp
rm -rf -- /work/worktrees/task-7555284f/tests/Antiphon.E2E/bin-merge-e2e
rm -rf -- /work/worktrees/task-7555284f/tests/Antiphon.E2E/bin-merge-e2e-env
rm -rf -- /tmp/antiphon-nightly-core/Antiphon.Scripts-73ECD7BFBCFB0FFDB2863099
rm -rf -- /tmp/antiphon-nightly-core/Antiphon.Scripts-9EE4E4E3EB6967F3AAC9E999
rm -rf -- /tmp/antiphon-nightly-core/Antiphon.Scripts-B9B80E7EEB765F7CE066C7CA
rm -rf -- /tmp/antiphon-nightly-core/Antiphon.Scripts-EACE8C1799FA29248E89968B
rm -rf -- /tmp/antiphon-nightly-core/Antiphon.Scripts-974E802812EB9B902F36EA55
```

Safe total: 2,333,009,673 bytes. That does not move the 434G used figure.

## Proposed recurring policy

Keep the weekly Windmill job `u/lndcobra/antiphon_build_junk_cleanup` as the no-context skip it already is (`docs/bootstrap.md` lines 120–122, `cleanup-build-junk.ps1` lines 22–24). Add a separate read-only census, same host, same Monday, that writes a report and deletes nothing. A later explicit apply may run only the literal paths the report marked safe, and only when all of these are still true at apply time:

- The path is older than 24h, and a cwd plus fd walk does not show a live holder.
- It is a `bin-*` or `obj-*` custom output, or an `Antiphon.Scripts-*` / `Antiphon.Nightly-*` identity older than 7 days, or an `aslot-*` whose `owner.pid` processes are dead.
- The producing checkpoint run, when there is one, is `phase=done`.
- The path is not under a `ws-*` slot that has a live cwd, not a native `bin/` or `obj/`, not `.antiphon/checkpoints` (keep receipts with the run), not `antiphon-land-verify-*` (retained by the verifier), not a name containing `kept`, not `/home/app/.nuget`, not `/var/cache/antiphon`, not `/state`, and not any Docker object.
- `c487-probe-*` and the other `/tmp` card trees stay needs-owner until the card owner accepts that list. Do not promote them by age.

Native `bin/` and `obj/` stay until worktree retirement (`docs/testing-and-build.md` line 1395). Docker prune stays an operator step. The census must not use a glob or `git clean`.

## Not done, noted

A sweeper that applies the policy above is not implemented, and the ~8.4G of `c487-probe-*` plus the multi-gigabyte `/tmp` card trees were left in place because age alone is not a finished-task proof.
