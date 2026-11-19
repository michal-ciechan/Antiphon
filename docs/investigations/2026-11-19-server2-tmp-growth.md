# server2 /tmp growth: who writes claude-1654 and the c487 probes (2026-11-19)

Read-only investigation, task 5de65634, branch `feat/card-task-5de65634`, base `1443b7ebc82277e9b21cc83d31a686bdb62208c2`. Nothing was deleted, restarted, deployed, or written except this file. The live Windows qualification run was not contacted. Observations are from inside the server2 runner container as uid 1654 (`app`). The session runner is pid 360 (`dotnet Antiphon.SessionRunner.dll`), up since 2026-11-18, with `TMPDIR=/tmp`.

Follows [the housekeeping census](2026-11-19-server2-housekeeping-census.md). That census measured `/tmp` at 135G and left `/tmp/claude-1654` and `c487-probe-*` as needs-owner. This note names the writers.

## Outcome

`/tmp/claude-1654` is the Claude Code CLI's per-uid scratch root, not a pid-1654 leftover and not a CARD-1981 build lease. The CLI creates `claude-${uid}` (mode 0700) under `os.tmpdir()` (`TMPDIR=/tmp` here) and then one child per sanitized cwd. 930 of those children are `task-<8 hex>` sessions. Nothing in this container deletes that tree: the shipped temp sweep is off, and its `claude-scratch` family is off even when the sweep is on. At 19:22Z no process held the tree open. It is 31,824,007,168 bytes.

The other large `/tmp` entries are uid 1654, older than 24h, and unheld. Fourteen of the fifteen largest are review/compose checkouts; thirteen of those fourteen are registered git worktrees (one more contains a registered worktree as a child). They stay until `git worktree remove`.

All 175 `c487-probe-<32 hex>` directories are the TUnit probe build from `TestClassificationPolicyTests`. Each is about 49.5 MB of `bin/Debug`. The test deletes the directory in a `finally` that a killed process never reaches, and the sweep that would reclaim them is switched off. None records an agent-task id. The newest write is 2026-11-13T03:43:15Z. They are finished test output.

The three largest trees were byte-identical across two samples 11 minutes apart. At that rate they do not approach the volume limit. `/tmp` as a whole grew 6,090,752 bytes in the same window (about 338 days to fill today's free space if that rate held). The volume itself grew 1,641,639,936 bytes in the window, outside `/tmp`.

## 1. Who writes /tmp/claude-1654

| Fact | Evidence |
|---|---|
| Owner | uid 1654 (`app`), mode 0700. `stat` birth 2026-10-30T00:45:37Z, directory mtime 2026-11-19T17:59:05Z. |
| Name | Claude Code CLI `/usr/local/bin/claude` (233,709,640 bytes, mtime 2026-11-18T01:25Z). The bundled function builds `claude-${process.getuid() ?? 0}` under the process temp root, `mkdir` mode 448 (0700), and refuses the write in a diskless session with the text "nothing is created, verified or written under the shared per-uid temp root". A second helper joins `bash-edit-diff` onto that same root. |
| Layout | 1,055 immediate children at 19:24Z (992 directories plus 63 regular files). 930 directories are `-work-worktrees-task-<8 hex>`, the slash-to-hyphen form of cwd `/work/worktrees/task-<8 hex>`. 15 are `-work-worktrees-ws-*` slot cwds. Under those, 2,058 `<session-uuid>` directories hold `scratchpad/` and sometimes `tasks/`. |
| Live writer at 19:12Z | No `claude` process. Six `grok` processes, all with `TMPDIR=/tmp`, cwds under `/work/worktrees/ws-*`. Their `TMPDIR` is the temp root the CLI would use; it is not `/tmp/claude-1654` itself. |
| Holders | A `/proc/*/fd` and cwd walk at 19:12Z (122 processes, 5 unreadable: root pids 1, 7, 54, 94, and a uid-1654 defunct pwsh) found no cwd under `/tmp` and no fd under `claude-1654`. The only `/tmp` fds were 85 processes in `/tmp/antiphon-pty-hosts`. A repeat at 19:22Z still had no holder of `claude-1654`. One cwd was `/tmp/aslot-152d784b/...`, a live CARD-1981 lease. |
| Session overlap | 31 of the 2,058 session directories match a live PtyHost `--session` id. All 31 sit under slot or other scratch, not under the 906 task directories listed for removal. A resumed session rewrites its scratchpad. |
| Per task | The 8 hex characters are the task id prefix. 17 of 930 still have `/work/worktrees/task-<id>`. 913 do not, and none of those 913 has a `task-<id>` name in `/work/worktrees-retiring`. |
| Deletion today | None. `RunnerWorktreeRetention:TempSweep:Enabled` is false and `Families:claude-scratch` is false (`server/appsettings.json:263-270`). The family pattern is `^claude-1654/-work-worktrees-task-[0-9a-f]{8}\z`, default off, and only when the mirror is gone (`src/Antiphon.SessionRunner/RunnerTempSweep.cs:122-127`, `:349-356`). It does not match `ws-*`, `bash-edit-diff`, or loose files. An entry over 1,073,741,824 bytes or 100,000 entries is kept (`RunnerTempSweep.cs:29-30`). |

Child sizes at 19:13Z (`du -x --max-depth=1 --block-size=1`), directories only; the 63 loose files add 37,389,036 bytes and are included in the parent total:

| Class | Count | Bytes |
|---|---:|---:|
| `task-*` directories | 930 | 29,870,911,488 |
| of which mirror still present | 17 | 477,204,480 |
| of which mirror absent | 913 | 29,393,707,008 |
| `ws-*` slot directories | 15 | 1,720,496,128 |
| other directories (`bash-edit-diff` is 133,099,520 of this) | 47 | 194,985,984 |
| parent total, including the 63 files | 1 | 31,824,007,168 |

Largest children:

| Bytes | Path | Why it stays |
|---:|---|---|
| 4,645,314,560 | `.../task-2b7ef7b4` | Over the 1 GiB sweep inventory limit. One session scratchpad holds the bytes (`scratchpad/old` is 2,832,678,912). Mirror absent. Not a registered worktree. |
| 2,989,154,304 | `.../task-40a75a5a` | Same shape, mirror absent, over 1 GiB. |
| 2,711,703,552 | `.../task-968281fc` | Same, mirror absent, over 1 GiB. |
| 2,514,710,528 | `.../task-1c23b333` | Registered worktree `.../ee4cb9db-d96d-40a6-ab75-6c220729acfd/scratchpad/onmaster`. Mirror absent. Over 1 GiB, so the sweep would keep it anyway. |
| 2,499,940,352 | `.../task-5b164f58` | Registered worktree `.../8faff552-e7bf-4db0-a9c7-0518b367d2c4/scratchpad/mastertree`. Over 1 GiB. |
| 1,996,685,312 | `.../task-4b605c04` | Over 1 GiB, mirror absent, not a registered worktree. |
| 1,288,187,904 | `.../-work-worktrees-ws-02-d3` | This slot. Live scratch. 31 live session ids include several under this directory. |
| 699,875,328 | `.../task-6379c50f` | Under 1 GiB and mirror absent, so a switched-on `claude-scratch` family would delete it. It contains three registered worktrees: `scratchpad/cut`, `scratchpad/w-135865831`, `scratchpad/w-de27ce2fd`. |

CARD-1981 does not cover this tree. `scripts/lib/LeaseTempDirectory.cs:16` creates `/tmp/aslot-<8 hex>` and `scripts/build-slot.ps1:120-122` points only the wrapped `dotnet` child's `TMPDIR` at that lease. `LeaseTempDirectory.BestEffortRemove` deletes the lease on dispose. The Claude CLI resolves its own root from the session `TMPDIR`, which the runner sets to `/tmp`. `claude-1654` is outside every aslot.

## 2. The fifteen largest /tmp entries

`du -x --max-depth=1 --block-size=1 /tmp` from 2026-11-19T19:10:36Z to 19:11:01Z. Load at the start of that pass was `20.79 17.39 11.57`. The `/tmp` total on that pass was 144,386,744,320 bytes. Every row is uid 1654. A holder walk at 19:12Z and again at 19:22Z found no cwd or fd in any of them. `du` could not read nested directories under `/tmp/c1609rd*-host/*` (another uid); those rows are the same ones the 2026-11-17 census skipped, and they are not in this top fifteen.

| Bytes | Directory mtime (UTC) | Path | What it is |
|---:|---|---|---|
| 31,824,007,168 | 2026-11-19T17:59:05 | `/tmp/claude-1654` | Claude CLI scratch, section 1 |
| 3,572,383,744 | 2026-11-15T07:26:21 | `/tmp/card-1943-review-compose` | Registered worktree (`gitdir: /work/repos/antiphon/.git/worktrees/card-1943-review-compose`) |
| 3,265,331,200 | 2026-11-13T13:09:32 | `/tmp/antiphon-review-d1a9978d` | Review evidence. No `.git`. Contains `base.tar` (238,499,840 bytes) and checkpoint logs. |
| 3,259,346,944 | 2026-11-13T15:07:15 | `/tmp/card1873-review-d16edc11` | Review evidence. No `.git`. Task json and combined trx. |
| 2,886,332,416 | 2026-11-14T19:20:04 | `/tmp/c1915-compose` | Registered worktree |
| 2,491,813,888 | 2026-11-16T23:11:35 | `/tmp/s5c-review-d5ecb5af` | Contains registered worktree `.../merge` |
| 2,486,358,016 | 2026-11-15T07:20:42 | `/tmp/card-1884-review-compose` | Registered worktree |
| 2,482,110,464 | 2026-11-14T21:27:24 | `/tmp/c0824-compose` | Registered worktree |
| 2,481,733,632 | 2026-11-14T20:36:48 | `/tmp/review-18072e35-compose2` | Registered worktree |
| 2,478,768,128 | 2026-11-14T03:25:09 | `/tmp/c1910-merged-now` | Registered worktree |
| 2,474,430,464 | 2026-11-13T08:56:54 | `/tmp/c1836-s4a-combined-53820e0f` | Registered worktree |
| 2,467,389,440 | 2026-11-16T04:53:37 | `/tmp/cc5b4299-rc22` | Registered worktree |
| 2,465,370,112 | 2026-11-15T04:05:26 | `/tmp/card-1925-mutant` | Registered worktree |
| 2,464,980,992 | 2026-11-16T04:53:38 | `/tmp/cc5b4299-rc21` | Registered worktree |
| 2,461,507,584 | 2026-11-14T20:50:30 | `/tmp/review-18072e35-master` | Registered worktree |

`git -C /work/repos/antiphon worktree list --porcelain` at 19:14Z listed 77 worktrees and named each registered path above. Registered worktrees stay. Removal is `git worktree remove` of that exact path after the owner is finished, never `rm`.

`c487-probe-*` is not in this list because each directory is ~49.5 MB. The family is 8,663,785,472 bytes across 175 directories, which would rank second.

## 3. c487-probe

Producer: `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs:497-503`. `WithTempDirectoryAsync` creates `Path.GetTempPath() + "c487-probe-" + Guid.NewGuid().ToString("N")`, runs the body, and in a `finally` calls `StartRefGit.DeleteDirectory`. `StartRefGit.cs:118-129` swallows a failed delete. `BuildAndRunProbeAsync` at `:551` writes a tiny TUnit project and runs `dotnet build`. The same prefix is the registered sweep family (`RunnerTempSweep.cs:67-69`). `scripts/lib/c487-harness.ps1:46` creates `c487-<guid>`, a different family, not these directories.

Measured at 19:13Z:

| Fact | Value |
|---|---|
| Count | 175 directories, 0 non-directories, all uid 1654 |
| `Probe.csproj` present | 175 of 175 |
| Disk bytes | 8,663,785,472 (min 49,496,064, max 49,565,696) |
| Oldest directory mtime | 2026-11-08T13:44:36Z `c487-probe-9febe7ecdb504c608d5f05ce4cd43f6e` |
| Newest directory mtime | 2026-11-13T03:43:15Z `c487-probe-100e9856cf2d4885a9bc51b5074dfe36` |
| Where the bytes are | One sample, `c487-probe-ffb1b01fe1fc4debaadd660701d229eb`: 49,496,064 total, of which `bin/Debug` is 48,619,520 |
| Task id inside | The newest tree has no `/work/worktrees` path and no `feat/card-task` string in any file under 2 MB. The guid is `Guid.NewGuid()`, not a task id. |
| Holders | None at 19:12Z or 19:22Z |
| Nested under a lease | 0 `c487-probe-*` children inside the 2 `aslot-*` directories present at 19:12Z |

They are finished. The creating test process is gone, nothing holds them, and the newest write is six days old. They were not cleaned because the process died before `finally` (a timeout, a build-slot kill, or a container restart), and `TempSweep:Enabled=false` has left the registered family untouched since. No new top-level probe has appeared since 2026-11-13. A `dotnet` test launched through `build-slot.ps1` now gets `TMPDIR=/tmp/aslot-*`, so `Path.GetTempPath()` for that process is the lease, and `BestEffortRemove` deletes the lease when the driver returns. A kill before dispose can still leak an `aslot-*`; it does not recreate a top-level `c487-probe-*`.

## 4. Growth

Two samples. Sample 1 is the 19:10:21Z `du -sx` for the two largest trees and the 19:11:01Z full listing for the third. Sample 2 is one `du -sx` at 19:21:47Z. Load at sample 2 was `7.66 16.45 14.68`.

| Path | Sample 1 bytes | Sample 2 bytes | Delta |
|---|---:|---:|---:|
| `/tmp/claude-1654` | 31,824,007,168 | 31,824,007,168 | 0 |
| `/tmp/card-1943-review-compose` | 3,572,383,744 | 3,572,383,744 | 0 |
| `/tmp/antiphon-review-d1a9978d` | 3,265,331,200 | 3,265,331,200 | 0 |

`du -sx /tmp` was 144,386,744,320 at 19:11:01Z and 144,392,835,072 at 19:22:27Z, a gain of 6,090,752 bytes in 686 seconds. The volume (`/dev/mapper/ubuntu--vg-ubuntu--lv`, 756,634,804,224 bytes, the 705G disk) moved the other way in magnitude: used went from 464,804,306,944 (19:11:01Z) to 466,445,946,880 (19:21:50Z), up 1,641,639,936 bytes, while these three trees stayed still. That 1.53 GiB is not `/tmp`.

Extrapolation from the `/tmp` delta only. Free space at 19:21:50Z was 259,377,926,144 bytes. At 6,090,752 bytes per 686 seconds, that free space lasts about 338 days. The three large trees contribute none of it in this window, so their own time-to-full is not a number. Eleven minutes is a short window: the historical Claude root, born 2026-10-30T00:45:37Z and now 31,824,007,168 bytes, averaged about 1.43 GiB/day over those 20.8 days. At that older rate the same free space would last about 169 days from this tree alone. `/tmp` was 185.9 GiB on 2026-11-17 and is 134.5 GiB now, so the tree also shrinks when worktree retention removes registered checkouts. The temp sweep has not been the thing shrinking it.

The volume's 1.53 GiB in 649 seconds would consume the free space in about 1.2 days if it continued. It is a burst outside `/tmp` during a loaded hour (other agents were building). It is not the `/tmp` leak rate.

## 5. What is safe, what stays, who sweeps

Safe means, at 2026-11-19T19:22Z: uid 1654, no cwd and no fd holder anywhere in the tree, and the newest observed write older than 48 hours (the shipped `TempSweep:MinAgeHours`). A later run has to repeat the holder check. This note did not delete anything.

**Safe class A, the 175 probes.** Exact commands are appendix A. 8,663,785,472 bytes. Newest directory mtime 2026-11-13T03:43:15Z. The matching code path is the registered `c487-probe` family, which is on by default once `TempSweep:Enabled` is true.

**Safe class B, 906 Claude task directories.** Exact commands are appendix B. 11,336,323,072 bytes. Predicate: name matches `-work-worktrees-task-<8 hex>`, `/work/worktrees/task-<id>` is absent, nothing in `/work/worktrees-retiring` starts with `task-<id>`, no direct child is a live `--session` id, no registered worktree path is under the directory, `du` size is at most 1 GiB, and every direct child's mtime is at or before 2026-11-17T17:35:58Z (the newest such child, on `task-be9d04c4`). That is older than 48 hours at 19:22Z on the 19th. These are the directories the `claude-scratch` family would remove, except that family is off, and it also refuses anything it has not re-walked.

**Stays.**

- `/tmp/claude-1654` itself, `bash-edit-diff`, the 63 loose files, and all 15 `ws-*` children. Slots are reused, 12 of the 15 mirrors exist, and `bash-edit-diff` was modified at 17:59Z.
- The 17 task directories whose mirrors exist: `2abcd1c0`, `4292c713`, `5b1ea772`, `6155693e`, `7555284f`, `934cdb25`, `94b17435`, `a2845b6c`, `b0e54eca`, `bc7a9fdd`, `bee64303`, `c6ca6929`, `c7a30141`, `d989d9d8`, `da90742c`, `ed0f7942`, `f23857e7`.
- The four over-1 GiB task directories and the three that contain registered worktrees, listed in section 1.
- Every registered worktree in section 2, including `/tmp/s5c-review-d5ecb5af/merge`.
- `/tmp/card1873-review-d16edc11` and `/tmp/antiphon-review-d1a9978d`. They are review evidence with task json and a base tarball, not a registered family, and age alone does not make them disposable.
- `/tmp/antiphon-pty-hosts` (live fds, hard-denied by the sweep) and any `/tmp/aslot-*` with a live cwd.
- `/tmp/node-compile-cache` and `/tmp/dotnet-tools`.

**Who sweeps.** The job that already matches this tree is the server retention tick `RunnerWorktreeRetention`, cron `*/30 * * * *`, runners `server2`, with `TempSweep:Enabled=false` and `Execute=false` (`server/appsettings.json:250-270`). Turning `Enabled` on with `claude-scratch` left false reclaims class A through the existing holder, age, owner, and size checks, 200 entries and 20 GiB per tick. Class B stays out until `claude-scratch` is turned on, and that flag stays off while `task-6379c50f` still contains registered worktrees. Windmill `u/lndcobra/antiphon_host_cleanup_server2` is the host-only cleanup (`docs/host-cleanup.md` row 62), not this container volume. A new Hangfire job would duplicate the tick that is already shipped and switched off.

## Not done, noted

A per-session temp root for the Claude CLI, removed when that pty session ends, would stop `/tmp/claude-1654` from accumulating; the CARD-1981 lease only wraps `dotnet`.

## Appendix A. Class A removal commands

Predicate above. Each path is one of the 175 directories. Re-check holders before running. These commands were not run.

```
rm -rf -- /tmp/c487-probe-009886dab56a407d83af4c13d034d2c7
rm -rf -- /tmp/c487-probe-02759495a8d446028ee59f548d1c3b41
rm -rf -- /tmp/c487-probe-027d12399caa40c3ba063f25230700b3
rm -rf -- /tmp/c487-probe-03942f8165d44e9da8ed953fad4ba11d
rm -rf -- /tmp/c487-probe-07becd79951f4ad9bffeba5b0f5cb290
rm -rf -- /tmp/c487-probe-09c9d9fec7b84dc3affcb171e77d7751
rm -rf -- /tmp/c487-probe-0c77a94efd5c44d6b2bb842a9b2e2b96
rm -rf -- /tmp/c487-probe-0e5cf1eb97aa47d9b0e38c88d0feb9e8
rm -rf -- /tmp/c487-probe-100e9856cf2d4885a9bc51b5074dfe36
rm -rf -- /tmp/c487-probe-148771ebb59b4722974fd3070e006cd4
rm -rf -- /tmp/c487-probe-179602bf52c941f5ad2ad4da15aa7b5a
rm -rf -- /tmp/c487-probe-17ff87524b4a47b192685972aaa060db
rm -rf -- /tmp/c487-probe-1a3ebed20ef1464bb41f9402f8204170
rm -rf -- /tmp/c487-probe-1a687c624a0349838d7aac9fe07aab0a
rm -rf -- /tmp/c487-probe-1b387d759df140d7b97facc02d58b35b
rm -rf -- /tmp/c487-probe-1eadbd400acb4b868f5d5b0302a52cb3
rm -rf -- /tmp/c487-probe-25ec111bbb0747b6b6043550918a6bac
rm -rf -- /tmp/c487-probe-2707cff534b643668ae55b00d38b4fa1
rm -rf -- /tmp/c487-probe-271e38a2f76643859d01de305836d373
rm -rf -- /tmp/c487-probe-27965bce05744983967e3420019776dd
rm -rf -- /tmp/c487-probe-27b10c01d3ce4efa9ed9137dbdd60494
rm -rf -- /tmp/c487-probe-28a6cdf2406845b285858b158821dcd3
rm -rf -- /tmp/c487-probe-2c1aefc2c7bd434fb31dcd0af7948fbe
rm -rf -- /tmp/c487-probe-2d10ed9a22dc4512ad7031ed3da448c0
rm -rf -- /tmp/c487-probe-3033077184f74d7e81fcdb94cb8418af
rm -rf -- /tmp/c487-probe-306408bf47804cd1930b018404b99c2e
rm -rf -- /tmp/c487-probe-324909adbd0945f28cabc024c35d9360
rm -rf -- /tmp/c487-probe-3254651396c14fee9b3ee767a2802579
rm -rf -- /tmp/c487-probe-3404750617c24c2e979e6231f661f226
rm -rf -- /tmp/c487-probe-343c8b8c692944e9a3e22b3d836f239b
rm -rf -- /tmp/c487-probe-347f9d891aa84ade8410ad4a4b8a3868
rm -rf -- /tmp/c487-probe-38783e58b5154d40b94fb28ee029edc2
rm -rf -- /tmp/c487-probe-3a86d78e74aa4ae2b5ea68a38e045acd
rm -rf -- /tmp/c487-probe-3b508d6d5d7045b8ad7081ebe3b655ad
rm -rf -- /tmp/c487-probe-3d6f997dc73a417695379e3959640866
rm -rf -- /tmp/c487-probe-3de04ec407c64acab504881dc4bf7f4a
rm -rf -- /tmp/c487-probe-3ec8dba6a43e462abc60c51461e84024
rm -rf -- /tmp/c487-probe-3febc6ca798b49cdac815a53863994f8
rm -rf -- /tmp/c487-probe-42256b3336e14980801f9c2cecc56536
rm -rf -- /tmp/c487-probe-450c42b9bcae460dbd9b21e19a202347
rm -rf -- /tmp/c487-probe-453f3ba022ed480599d8eb061cab7429
rm -rf -- /tmp/c487-probe-465b046a4e034e67b3f8ef1c8b9b9a15
rm -rf -- /tmp/c487-probe-47a26c4ece144167ad89340dc0086c5d
rm -rf -- /tmp/c487-probe-489d18f2adea42daa49f60a304546c1b
rm -rf -- /tmp/c487-probe-49981a2e076c416a936a78cbab3bdb8f
rm -rf -- /tmp/c487-probe-4a6b115341ad430eab875eba93f98d72
rm -rf -- /tmp/c487-probe-4a6dfcbd4a454ee7bd5b74b376ee6431
rm -rf -- /tmp/c487-probe-4a9a08ddcbf94b82a7ca4f6b51f59da0
rm -rf -- /tmp/c487-probe-4b74de0acc6f4db98ce6eec25001729d
rm -rf -- /tmp/c487-probe-4c8b9aa552f54fbeafa98d49ce09d6c9
rm -rf -- /tmp/c487-probe-4d3a683e32f5446e97b61060030a250a
rm -rf -- /tmp/c487-probe-4d6ef3717f0244019abbea7f8f938c0a
rm -rf -- /tmp/c487-probe-4f2a74cc09424a60b7e830720991c288
rm -rf -- /tmp/c487-probe-4f6ec6a6498a4cef9637a9f506c9fb5b
rm -rf -- /tmp/c487-probe-4fcceea2356445d4a2eb450e6f782608
rm -rf -- /tmp/c487-probe-531b5970d9c14a969296c81c5bb5b8d1
rm -rf -- /tmp/c487-probe-532dc90373c94ddbbe93b07430c60471
rm -rf -- /tmp/c487-probe-553ca37dce884975b6b921a04e2c3484
rm -rf -- /tmp/c487-probe-58ea4bb06d9c4a24baf801228ef5fa9d
rm -rf -- /tmp/c487-probe-5931665931ed419bac10ac916ffda4a0
rm -rf -- /tmp/c487-probe-5b875c8213274eb79cf5798e3ec535f2
rm -rf -- /tmp/c487-probe-5bbb0391e800441dbd5bf59882406aa4
rm -rf -- /tmp/c487-probe-5bffdbcca7e94635a787b782a740b893
rm -rf -- /tmp/c487-probe-635929f904a042029b3f10ed8dbd6c55
rm -rf -- /tmp/c487-probe-6489f87e34aa49a2a65234c0fac8adef
rm -rf -- /tmp/c487-probe-657e4ade23bb448eaf3ae61e121e3c6c
rm -rf -- /tmp/c487-probe-69575ef0834a48a29349577ab93df0ff
rm -rf -- /tmp/c487-probe-699c290f769f412182c3d0a673477e1a
rm -rf -- /tmp/c487-probe-6a0900fbc0184a9f80abff4f7edd8c46
rm -rf -- /tmp/c487-probe-6e8b7b5d31a3403d91efa96c66c45fcd
rm -rf -- /tmp/c487-probe-6f618e9c13b74bb1bfcb1a9c8b6cc688
rm -rf -- /tmp/c487-probe-6fb12db232e7457fad15aef7bc800143
rm -rf -- /tmp/c487-probe-70aa8e69202146d984f9c1a986166f76
rm -rf -- /tmp/c487-probe-70b974b100e84d0d854c0c2d441e7df8
rm -rf -- /tmp/c487-probe-73441d1e89284d6ab6375f303cc9f898
rm -rf -- /tmp/c487-probe-756745ce1a464a80a92b725c1f80014d
rm -rf -- /tmp/c487-probe-765116e8f994411b9bc916740a898606
rm -rf -- /tmp/c487-probe-7790227c4f7a49a98e1e0762741dbcfa
rm -rf -- /tmp/c487-probe-78109033f27245789366a43259234764
rm -rf -- /tmp/c487-probe-781ef593ef9f43858ea2575200c6a47c
rm -rf -- /tmp/c487-probe-79ef9c2c5db545cbae7b9b764147d8f2
rm -rf -- /tmp/c487-probe-7caa3ee28b6d4e349db76d842445450d
rm -rf -- /tmp/c487-probe-7d68e07e2a7c45fd8087eb30c8bf71f1
rm -rf -- /tmp/c487-probe-7d699f1090d445658b804db6da21d182
rm -rf -- /tmp/c487-probe-7e3070d797b648ed9cd2a83fe2270b93
rm -rf -- /tmp/c487-probe-7f6bdcdd04894646a1ae53576ad645ab
rm -rf -- /tmp/c487-probe-7f84f19aa7c24a2f826b8cec12e222e2
rm -rf -- /tmp/c487-probe-7fb582989d764c758a20e4ea417c37ad
rm -rf -- /tmp/c487-probe-80426f22e4774b458b836b83bb7e9009
rm -rf -- /tmp/c487-probe-81e7b3b25a574892bdffe4668752e226
rm -rf -- /tmp/c487-probe-828e56a479c84ef5910b36ec9acabef4
rm -rf -- /tmp/c487-probe-884c7415fdf94d6fb50fdb37ae598bd3
rm -rf -- /tmp/c487-probe-8a124298c231442ca0aff8b8a2c56bda
rm -rf -- /tmp/c487-probe-8b03686aa81b44b7a39e9c5a7a367d71
rm -rf -- /tmp/c487-probe-8f45b9697a4e4563b2ce45b3dd815832
rm -rf -- /tmp/c487-probe-91b2a81745344d45a40db4447f7a6eb9
rm -rf -- /tmp/c487-probe-934a787633ae4f50a2170382216adce1
rm -rf -- /tmp/c487-probe-960dd3a38e69484eaaff9e8453ca3f5f
rm -rf -- /tmp/c487-probe-9617958d811b4101822f2e7962bb2c1c
rm -rf -- /tmp/c487-probe-969959df3e5e48b9a52a3f5dd5456fcd
rm -rf -- /tmp/c487-probe-9990150792c840a488020c33ee2044be
rm -rf -- /tmp/c487-probe-9c719de74373479596c38690962e8bff
rm -rf -- /tmp/c487-probe-9d2d99dec0184734a6aa951b95a36d74
rm -rf -- /tmp/c487-probe-9de19322f9bf42299e988ccc7d7e4773
rm -rf -- /tmp/c487-probe-9e2b0156cb1842b2bcb6063609ab69db
rm -rf -- /tmp/c487-probe-9ee50da738eb4bcd9db823ab36c75117
rm -rf -- /tmp/c487-probe-9febe7ecdb504c608d5f05ce4cd43f6e
rm -rf -- /tmp/c487-probe-a04eedf020824580a640d7ea48eeaabf
rm -rf -- /tmp/c487-probe-a0a0ad11654d4e678d6c787ead4b0797
rm -rf -- /tmp/c487-probe-a3385ed06b794387a605f5bbfb222683
rm -rf -- /tmp/c487-probe-a398a703708e41efa2a776126eb53c0a
rm -rf -- /tmp/c487-probe-a3f97aa5fa974e5c8aa962340276106b
rm -rf -- /tmp/c487-probe-a52ed7206c0a4810b8a73004113d72ff
rm -rf -- /tmp/c487-probe-a8392ac0be36400ebeb4920cd6e49482
rm -rf -- /tmp/c487-probe-aa96dab922ea4e729a82f7e028f27d51
rm -rf -- /tmp/c487-probe-ab32c8cef33e48e586b82e8e458cad80
rm -rf -- /tmp/c487-probe-abfe1bc4816e48c88f8bbea32e7a71b3
rm -rf -- /tmp/c487-probe-acf8314ec6e1426ebc9cfe9d5007d7c8
rm -rf -- /tmp/c487-probe-ae828f180e674cd28f714ac30caf733a
rm -rf -- /tmp/c487-probe-aebdf16c723147a6ba629b53842dafdb
rm -rf -- /tmp/c487-probe-af99c1af76a4420c9fa2e37ccae5aea7
rm -rf -- /tmp/c487-probe-b14ed2e0657d4ca6ac0876cdfecf6a9b
rm -rf -- /tmp/c487-probe-b185cf6d0191471b88cb8f7b7ca7a2a8
rm -rf -- /tmp/c487-probe-b312540f152f4251afce7c2630a66dc3
rm -rf -- /tmp/c487-probe-b5087af3d42a48a5ab785eacec9e857a
rm -rf -- /tmp/c487-probe-b55b37dcaecf46e5bbb86bd0d2dcb491
rm -rf -- /tmp/c487-probe-bb8ac3551eed432bae6d9fb452ab80b5
rm -rf -- /tmp/c487-probe-c011d695e2a748da950cda703851ca40
rm -rf -- /tmp/c487-probe-c0c3d4b0622c4176a801f8f37053871f
rm -rf -- /tmp/c487-probe-c0cad47a73c74cf2845eed291fbcd047
rm -rf -- /tmp/c487-probe-c23b9b440f73412b83363c0daa5e0027
rm -rf -- /tmp/c487-probe-c323f8df2fbc4f86b218b11c2e3096fb
rm -rf -- /tmp/c487-probe-c360d656f08548b8b8c39dbb2bd575f2
rm -rf -- /tmp/c487-probe-c64e71fe343b4a43b9ab7270bb65a94c
rm -rf -- /tmp/c487-probe-c6618acd7f6341a5a9c92dcfe8f407ab
rm -rf -- /tmp/c487-probe-c6866d2065404ba5bff459b59ddec186
rm -rf -- /tmp/c487-probe-ca5c958db8dd47c2affa7e8109f68299
rm -rf -- /tmp/c487-probe-cab2469be9ec46c7a98bd0373b25e82b
rm -rf -- /tmp/c487-probe-cc8a48726f454edda2c539d6646f6ec9
rm -rf -- /tmp/c487-probe-cd0f588479b04857b837da1660ba9e70
rm -rf -- /tmp/c487-probe-cdbcf9102c5e4fa3b90ecc70f4885052
rm -rf -- /tmp/c487-probe-cf8307e5b4e64ec0a9829e76090b36cd
rm -rf -- /tmp/c487-probe-cff154f27a06412d92980a78a4a126a9
rm -rf -- /tmp/c487-probe-d0a9295e475b48ee8b5d00a5bf70cb99
rm -rf -- /tmp/c487-probe-d4df108042f14fb397a6e6c60eaee64e
rm -rf -- /tmp/c487-probe-d4f8872e4e4444409bbc11874061dfee
rm -rf -- /tmp/c487-probe-d5988edd09e748e2bf139c367979c267
rm -rf -- /tmp/c487-probe-d7c9675342384e15affb0fc3d16ae200
rm -rf -- /tmp/c487-probe-db48076e42cd4aa391af6230a88f5f3b
rm -rf -- /tmp/c487-probe-dcf0c9747e3d4c23a68adceee737ab03
rm -rf -- /tmp/c487-probe-ddb61623aa4f403bbacd6ceafe5d2baa
rm -rf -- /tmp/c487-probe-de77d48595ca453684895e4bf5c6e585
rm -rf -- /tmp/c487-probe-deec35c0a9eb4db49c6fb38018622ccb
rm -rf -- /tmp/c487-probe-e0693f8e462641f680bfa061f3034332
rm -rf -- /tmp/c487-probe-e2c9633705394402a11aaa2bd87bd824
rm -rf -- /tmp/c487-probe-e387a4bb0c6a4d439b6e8cd11b019ccf
rm -rf -- /tmp/c487-probe-e49a8ea54ba444d28f0d75a3345200b3
rm -rf -- /tmp/c487-probe-e67e30ec04c04983aeb615a08e9ee58f
rm -rf -- /tmp/c487-probe-e7e62c673fff4acd83f93361457bb631
rm -rf -- /tmp/c487-probe-e80dc438598845d6a5dc3eecd86b85a7
rm -rf -- /tmp/c487-probe-e82bb3c66b5444e28f4721e6588022ac
rm -rf -- /tmp/c487-probe-e9d2021eb6eb46b18b7922c8dba7abbb
rm -rf -- /tmp/c487-probe-ebd4c61090dd4e61b7b44e9ce897c2e8
rm -rf -- /tmp/c487-probe-ec71b267f8964e989aac06b7ce9ca911
rm -rf -- /tmp/c487-probe-ecb5870488d348e59a4c2e0916ecff89
rm -rf -- /tmp/c487-probe-f4ee3caf7fbf4351bfc13cd779dbc278
rm -rf -- /tmp/c487-probe-f59b1f56261e41d4a639170c53a86411
rm -rf -- /tmp/c487-probe-f5a3113b0cd843c4893635d314053ef7
rm -rf -- /tmp/c487-probe-f7dfeef3ea4e41f4958e62d3c312cd0c
rm -rf -- /tmp/c487-probe-f8f7cc3233ba4295acb61755ca27f754
rm -rf -- /tmp/c487-probe-f9f6abf7eb1645759002a949025574f3
rm -rf -- /tmp/c487-probe-fa53d46e2c954399a1cbb0ccdf4223ac
rm -rf -- /tmp/c487-probe-fba99cd6b6864187b00b34e90d76787f
rm -rf -- /tmp/c487-probe-fbdd29da971e423780f25ef25dff1b57
rm -rf -- /tmp/c487-probe-ffb1b01fe1fc4debaadd660701d229eb
```

## Appendix B. Class B removal commands

Predicate above. 906 paths. Re-check holders, mirror absence, and registered worktrees before running. These commands were not run.

```
rm -rf -- /tmp/claude-1654/-work-worktrees-task-00011235
rm -rf -- /tmp/claude-1654/-work-worktrees-task-006e0b89
rm -rf -- /tmp/claude-1654/-work-worktrees-task-00a0b2d1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-00abf1bf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-00d82b2d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-00efc28c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0165567a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-024cf048
rm -rf -- /tmp/claude-1654/-work-worktrees-task-02550ff6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-038a3b2f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-03afb6f5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-046e892d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-04beb796
rm -rf -- /tmp/claude-1654/-work-worktrees-task-04f1a7ff
rm -rf -- /tmp/claude-1654/-work-worktrees-task-053dff43
rm -rf -- /tmp/claude-1654/-work-worktrees-task-053fe121
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0628e278
rm -rf -- /tmp/claude-1654/-work-worktrees-task-064629ef
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0665f172
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0677b5ed
rm -rf -- /tmp/claude-1654/-work-worktrees-task-06aa57e7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0704a004
rm -rf -- /tmp/claude-1654/-work-worktrees-task-072cf7b1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0754711b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-076f7110
rm -rf -- /tmp/claude-1654/-work-worktrees-task-077e181d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-08469fcb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-08a29a5c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-08a2e7f3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-08ac06f9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-08c004b5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-08cca1b8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-092e692a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-09726111
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0af9a4e5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0b9a5438
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0be34a95
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0c3b7de3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0c839994
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0c839a5d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0ca7a8e1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0cd29649
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0ce3bb38
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0e8a1b50
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0ed344bc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0eeb3129
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0efa38ca
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0f317c85
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0f359985
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0f4dc1cc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0f994d7f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-0fe759b9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-10098129
rm -rf -- /tmp/claude-1654/-work-worktrees-task-10145984
rm -rf -- /tmp/claude-1654/-work-worktrees-task-118a855f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-11b5204f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-11b71861
rm -rf -- /tmp/claude-1654/-work-worktrees-task-121cbdd3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-122f2dcf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1296ccbd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-12ba9a25
rm -rf -- /tmp/claude-1654/-work-worktrees-task-12c87cda
rm -rf -- /tmp/claude-1654/-work-worktrees-task-12ea19bf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-131fbc4c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-134ddd46
rm -rf -- /tmp/claude-1654/-work-worktrees-task-135047fd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-13fb02d0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-14adea19
rm -rf -- /tmp/claude-1654/-work-worktrees-task-150e7b11
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1637311a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-16424111
rm -rf -- /tmp/claude-1654/-work-worktrees-task-16a9c9be
rm -rf -- /tmp/claude-1654/-work-worktrees-task-16c6dbc2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-17885722
rm -rf -- /tmp/claude-1654/-work-worktrees-task-17fe9eec
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1858aab5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-189595a5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-18fbae37
rm -rf -- /tmp/claude-1654/-work-worktrees-task-19a14614
rm -rf -- /tmp/claude-1654/-work-worktrees-task-19af8f63
rm -rf -- /tmp/claude-1654/-work-worktrees-task-19b61d3a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-19d9c260
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1a10b6cd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1a3ccb7a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1a8785ed
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1aa2da7c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1aa83aa9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1ad1dd2b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1ae8dfd4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1b618bf5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1ba2c9cb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1c022492
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1c0653a5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1c0b1911
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1c1a23d3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1c1cf07a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1c2d93f1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1c44bc80
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1c4f8dd2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1c5142f8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1cf13574
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1d023ed2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1dc468a4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1dcb8d90
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1e52322c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1ed0ea30
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1ee29680
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1f46c184
rm -rf -- /tmp/claude-1654/-work-worktrees-task-1fa64d10
rm -rf -- /tmp/claude-1654/-work-worktrees-task-203ef46a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-20d47839
rm -rf -- /tmp/claude-1654/-work-worktrees-task-211c38d3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-212363b0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-22521507
rm -rf -- /tmp/claude-1654/-work-worktrees-task-22e56658
rm -rf -- /tmp/claude-1654/-work-worktrees-task-230155cc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-230d1f5d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-233b0e13
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2377d73d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-23889c9b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-23fce9a6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-24291915
rm -rf -- /tmp/claude-1654/-work-worktrees-task-24370dc7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-24cf8f11
rm -rf -- /tmp/claude-1654/-work-worktrees-task-24eb2f3d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-24eb34b0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-250763e5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2603a99c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-27123eab
rm -rf -- /tmp/claude-1654/-work-worktrees-task-272c809a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-27a25d75
rm -rf -- /tmp/claude-1654/-work-worktrees-task-27ca134f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-27eba224
rm -rf -- /tmp/claude-1654/-work-worktrees-task-27f5aa02
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2838f508
rm -rf -- /tmp/claude-1654/-work-worktrees-task-291753f8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-296074ab
rm -rf -- /tmp/claude-1654/-work-worktrees-task-29664acc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-29cd334a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2a6ecbbd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2a731e30
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2b554164
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2b89e41c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2bc285a9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2bcc38e9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2bef90c6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2c8d00db
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2db0be9a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2de07422
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2deb2419
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2e0375f1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2e1bab40
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2e6da28d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2e77eeb2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2ea19578
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2f31a015
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2f58c570
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2f9c3f5c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2faa475f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-2fafa091
rm -rf -- /tmp/claude-1654/-work-worktrees-task-304984ac
rm -rf -- /tmp/claude-1654/-work-worktrees-task-306148cc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-314297be
rm -rf -- /tmp/claude-1654/-work-worktrees-task-31433c87
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3169ab97
rm -rf -- /tmp/claude-1654/-work-worktrees-task-31873171
rm -rf -- /tmp/claude-1654/-work-worktrees-task-31e20f5a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-320f7bd0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-32354421
rm -rf -- /tmp/claude-1654/-work-worktrees-task-324876aa
rm -rf -- /tmp/claude-1654/-work-worktrees-task-32f24400
rm -rf -- /tmp/claude-1654/-work-worktrees-task-333e1bd2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-336f397f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-33847aaa
rm -rf -- /tmp/claude-1654/-work-worktrees-task-34125850
rm -rf -- /tmp/claude-1654/-work-worktrees-task-344b9bdc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-34664e17
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3501177a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-353132a7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-35623cb8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3593eb2f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-359d84c5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-36218f42
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3646f5a9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-36932792
rm -rf -- /tmp/claude-1654/-work-worktrees-task-36a5ec03
rm -rf -- /tmp/claude-1654/-work-worktrees-task-36b3dc05
rm -rf -- /tmp/claude-1654/-work-worktrees-task-36bca1ea
rm -rf -- /tmp/claude-1654/-work-worktrees-task-36c89ac0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-370083f1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-37684a82
rm -rf -- /tmp/claude-1654/-work-worktrees-task-378de644
rm -rf -- /tmp/claude-1654/-work-worktrees-task-37b01055
rm -rf -- /tmp/claude-1654/-work-worktrees-task-37d76938
rm -rf -- /tmp/claude-1654/-work-worktrees-task-37f1e778
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3802ef67
rm -rf -- /tmp/claude-1654/-work-worktrees-task-38030aae
rm -rf -- /tmp/claude-1654/-work-worktrees-task-38d3c07b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-394d7d1a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3997c63a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-39a11efd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-39c0fd93
rm -rf -- /tmp/claude-1654/-work-worktrees-task-39e2eabd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3a179edb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3a33532b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3af9f716
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3b0adade
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3b0e3e9e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3be64a0a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3c25d1ab
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3cdf5799
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3d2eb2ca
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3d4bf7c3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3d6a3f6f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3e32f1e6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3e4381e4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3e56dbb0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3e76de6d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3e96732b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3eb247de
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3f46a92a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-3fa792e4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4047a80b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-406bca79
rm -rf -- /tmp/claude-1654/-work-worktrees-task-415d0fa1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-421e496e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4230f0b9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-426568ea
rm -rf -- /tmp/claude-1654/-work-worktrees-task-42e16a50
rm -rf -- /tmp/claude-1654/-work-worktrees-task-436ebdc6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-437a6c9a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-437ceb6c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-43f316b5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-44670038
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4468a62d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-449c541d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-44b087e9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-44c44a07
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4521968e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4555c064
rm -rf -- /tmp/claude-1654/-work-worktrees-task-455c5755
rm -rf -- /tmp/claude-1654/-work-worktrees-task-456cfb77
rm -rf -- /tmp/claude-1654/-work-worktrees-task-459edfef
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4651491a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-46541b3e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-46addf68
rm -rf -- /tmp/claude-1654/-work-worktrees-task-47f9be84
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4809596b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-48e15e43
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4919f750
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4987d1e9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4a75ce55
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4aa6e6f8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4af5405a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4b2ef72c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4b9d4604
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4bf79590
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4c2d90db
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4c5fe834
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4c956d48
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4ca0643a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4d0255a8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4d05a61d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4d4e6cda
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4d63b6fe
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4d6af62a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4ed13efe
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4f546055
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4f64e784
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4f978baa
rm -rf -- /tmp/claude-1654/-work-worktrees-task-4f9a7df9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5034186b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-506ad7e2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5073e215
rm -rf -- /tmp/claude-1654/-work-worktrees-task-516b3a0a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-520fd455
rm -rf -- /tmp/claude-1654/-work-worktrees-task-52410842
rm -rf -- /tmp/claude-1654/-work-worktrees-task-52484483
rm -rf -- /tmp/claude-1654/-work-worktrees-task-52aeb04f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-52dac4e7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-52e1f0c1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-52f47408
rm -rf -- /tmp/claude-1654/-work-worktrees-task-53194ebc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-536536e9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-53edd261
rm -rf -- /tmp/claude-1654/-work-worktrees-task-53fd4ecc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5480410c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5495e6d5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5498723a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-54c47f10
rm -rf -- /tmp/claude-1654/-work-worktrees-task-551ea479
rm -rf -- /tmp/claude-1654/-work-worktrees-task-55b2b668
rm -rf -- /tmp/claude-1654/-work-worktrees-task-55b96312
rm -rf -- /tmp/claude-1654/-work-worktrees-task-56bf971d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-56e7e9b7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-577b2ee0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-57cc5faa
rm -rf -- /tmp/claude-1654/-work-worktrees-task-57e28053
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5864e36c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-58f85687
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5960094e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5967044f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5a6278c8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5a733e90
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5aaa94f1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5ae98d31
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5b21d0be
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5b3c6c26
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5b7656a9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5b84b043
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5bdb0e6f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5be78095
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5c209018
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5cd1f66f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5d1e98cf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5d22b898
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5d714273
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5d97c7d9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5db7c600
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5e86c3d0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5e9340ec
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5ec136af
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5ef0b4bc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5f346008
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5f4dce6e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5f63ea92
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5fd6ac3e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5fefd110
rm -rf -- /tmp/claude-1654/-work-worktrees-task-5ff161fd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6008f08d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6026001a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-60cb0a38
rm -rf -- /tmp/claude-1654/-work-worktrees-task-618f963c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-61cede6f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-61d7498e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-62afb5fc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-62f21433
rm -rf -- /tmp/claude-1654/-work-worktrees-task-62fa85a6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-632e079b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-636394f6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6394f325
rm -rf -- /tmp/claude-1654/-work-worktrees-task-63affa8d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-63cdf1df
rm -rf -- /tmp/claude-1654/-work-worktrees-task-64437049
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6532e78e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-654750f8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6578802d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-65e6f8b0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6652bca1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-67077ff7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-67587ad0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-675b622d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-67640bd1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-67e63dbb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-684dc939
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6863b533
rm -rf -- /tmp/claude-1654/-work-worktrees-task-689df4fd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-68b66c6d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-692b70e4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6949514d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-69bc3cb2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6a12a89d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6a3c622e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6a7c2ca6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6b6471d0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6c1b0f87
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6c3a5d56
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6c414224
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6c5b2084
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6c65bbc8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6ccbe444
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6db87da1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6dfad742
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6e2dbcd6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6e58ba06
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6ea96bcb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6f3ab7aa
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6f66c43e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6f78ecdb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6f817352
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6fd10cb5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6ff01870
rm -rf -- /tmp/claude-1654/-work-worktrees-task-6ffbb647
rm -rf -- /tmp/claude-1654/-work-worktrees-task-70cde4e7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7114858c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7128bbf6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-715407ca
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7289f7c7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-731f7074
rm -rf -- /tmp/claude-1654/-work-worktrees-task-737b54c5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-73837e64
rm -rf -- /tmp/claude-1654/-work-worktrees-task-73a9b0f6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7519a931
rm -rf -- /tmp/claude-1654/-work-worktrees-task-75c9410c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7708d552
rm -rf -- /tmp/claude-1654/-work-worktrees-task-777ca96d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-77ab4e11
rm -rf -- /tmp/claude-1654/-work-worktrees-task-78583646
rm -rf -- /tmp/claude-1654/-work-worktrees-task-78c5cba0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-78e606d5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7909871a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-79245209
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7982c828
rm -rf -- /tmp/claude-1654/-work-worktrees-task-79a20078
rm -rf -- /tmp/claude-1654/-work-worktrees-task-79d3d1ba
rm -rf -- /tmp/claude-1654/-work-worktrees-task-79f6a820
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7ab0d65a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7b966444
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7c02ed68
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7c150718
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7c36e01b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7c6f052c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7d7761ce
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7d883155
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7dea795f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7dfff223
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7eb005f3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7ef9aa63
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7f1d7c3b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7f675cf6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-7fe35bc3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-80170e29
rm -rf -- /tmp/claude-1654/-work-worktrees-task-801ad6c3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-80663342
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8125d25d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-815b655c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8195f9ce
rm -rf -- /tmp/claude-1654/-work-worktrees-task-81a59a10
rm -rf -- /tmp/claude-1654/-work-worktrees-task-81e40a23
rm -rf -- /tmp/claude-1654/-work-worktrees-task-81ea09a8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-82074cc2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-821a7c5b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8225fae1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-83096283
rm -rf -- /tmp/claude-1654/-work-worktrees-task-833a515a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-833bd9b4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8347d02a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-83d30eec
rm -rf -- /tmp/claude-1654/-work-worktrees-task-84692314
rm -rf -- /tmp/claude-1654/-work-worktrees-task-846b5854
rm -rf -- /tmp/claude-1654/-work-worktrees-task-84a1901d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-84a19311
rm -rf -- /tmp/claude-1654/-work-worktrees-task-84b44899
rm -rf -- /tmp/claude-1654/-work-worktrees-task-84c4fa59
rm -rf -- /tmp/claude-1654/-work-worktrees-task-84cf7bb4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-84d0a026
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8559e75b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-856fac4d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-85da972b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8626ac28
rm -rf -- /tmp/claude-1654/-work-worktrees-task-869bd8ad
rm -rf -- /tmp/claude-1654/-work-worktrees-task-86a12341
rm -rf -- /tmp/claude-1654/-work-worktrees-task-86bd65be
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8719af3e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-87232051
rm -rf -- /tmp/claude-1654/-work-worktrees-task-872e19d8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-87596cc5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-87954929
rm -rf -- /tmp/claude-1654/-work-worktrees-task-87cbe3ef
rm -rf -- /tmp/claude-1654/-work-worktrees-task-87d367d8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-87d98f50
rm -rf -- /tmp/claude-1654/-work-worktrees-task-87ebe4f0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-88cb4849
rm -rf -- /tmp/claude-1654/-work-worktrees-task-894ccf98
rm -rf -- /tmp/claude-1654/-work-worktrees-task-89cb5516
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8a2a0e71
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8a3a40d6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8a7544d8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8ac2299c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8acb8a4d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8b043bee
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8b317bb8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8b609b8a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8b9a4097
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8b9b7a9a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8bbde9af
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8bc75abf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8bd5eef3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8c49a43b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8c6a97b4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8d020b07
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8d58928b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8e38d51f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8eb266f0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8ec4caa8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8f001b6d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8f14e2c9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8f4389b2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8f51d41b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8faaee74
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8fcf2612
rm -rf -- /tmp/claude-1654/-work-worktrees-task-8ff99f6a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-90e0bae3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-90f054d8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-92047d67
rm -rf -- /tmp/claude-1654/-work-worktrees-task-927b4796
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9295f0f3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-92a2c7bb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-92fb5616
rm -rf -- /tmp/claude-1654/-work-worktrees-task-931a28cf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-935ca9be
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9360f65d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-93742813
rm -rf -- /tmp/claude-1654/-work-worktrees-task-93bda762
rm -rf -- /tmp/claude-1654/-work-worktrees-task-94181272
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9478d178
rm -rf -- /tmp/claude-1654/-work-worktrees-task-94a1008d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-95236406
rm -rf -- /tmp/claude-1654/-work-worktrees-task-95bbe60b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9602645b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-961d90c8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9688c1d8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-96f2fc86
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9758fc71
rm -rf -- /tmp/claude-1654/-work-worktrees-task-977203e7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-97a782cd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-97d93984
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9843b768
rm -rf -- /tmp/claude-1654/-work-worktrees-task-988017cf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-990531c3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-99e23919
rm -rf -- /tmp/claude-1654/-work-worktrees-task-99f321fb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9a85e184
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9ac68027
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9aef59e9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9b6f690f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9c0b4fa3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9c6efe95
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9c7ca54d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9cc88f18
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9d003ac4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9da7d9cf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9dbbe5bf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9e9cf510
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9ee6f8ca
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9eef369d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9f29e134
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9f577891
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9f89bb21
rm -rf -- /tmp/claude-1654/-work-worktrees-task-9fd69563
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a0362fd8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a0dbfa79
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a11ccbbc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a14e0f34
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a1fb2dc7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a207ee5c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a22bdfed
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a28cac94
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a2a41764
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a2c91e7b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a2cfcdc1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a31dab4a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a31ee409
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a361af41
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a36e64ed
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a382d6ef
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a38b6048
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a3a942ab
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a3ce2552
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a3ff6d0b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a41890aa
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a42d888f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a437c696
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a45306f3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a45f88c8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a494d175
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a4a77bb8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a4e63c30
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a519f6af
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a549ddb0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a59abe7c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a5aebe24
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a5f3d226
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a6025c48
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a64bf439
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a695169d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a69a0103
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a6c5c347
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a7108dd6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a753db19
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a790b059
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a7de4ea4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a80b01f9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a84df3ad
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a89708b6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a8bd6706
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a921ab9d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a97c9731
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a9b3bc09
rm -rf -- /tmp/claude-1654/-work-worktrees-task-a9f45276
rm -rf -- /tmp/claude-1654/-work-worktrees-task-aa3c54ff
rm -rf -- /tmp/claude-1654/-work-worktrees-task-aa58fb53
rm -rf -- /tmp/claude-1654/-work-worktrees-task-aa63b6d1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-aa89c309
rm -rf -- /tmp/claude-1654/-work-worktrees-task-aaabade9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-aae1b064
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ab00c63e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ab6cf694
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ab903d9e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-abecae1e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ac3dd93d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-acc138d0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-accd195d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-acd85970
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ace48db2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-acf70e92
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ad29ef52
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ae6457ac
rm -rf -- /tmp/claude-1654/-work-worktrees-task-aea81717
rm -rf -- /tmp/claude-1654/-work-worktrees-task-aec861c3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-aeecbb03
rm -rf -- /tmp/claude-1654/-work-worktrees-task-af70c827
rm -rf -- /tmp/claude-1654/-work-worktrees-task-af991039
rm -rf -- /tmp/claude-1654/-work-worktrees-task-afa927c5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-aff9b6f2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b02344a7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b02d7898
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b054e10c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b055e88c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b0bdc516
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b0fe0dcb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b124e131
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b1627a3f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b1cdbed3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b1e4f841
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b255559a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b26811ff
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b2d063aa
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b2fdaf1a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b383e483
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b384b934
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b38dbb26
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b3bd8985
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b3c01691
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b46848cb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b578f67a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b6a88568
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b6b7abdc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b73aeb1d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b80e7169
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b842c9bf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b8eac553
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b8f46d8a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b922ebf8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b9729f4b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-b978e850
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ba15e8f5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ba1cbbcc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ba6624a1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ba83460f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ba8ec885
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ba9f47c2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-baacd1ef
rm -rf -- /tmp/claude-1654/-work-worktrees-task-babf94bb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bacbedc4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bad0e712
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bad528a5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bad86a85
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bae2a09e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bb0a6ce8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bb0cd0ec
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bb924129
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bbd026a3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bbe3899e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bbf78945
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bc583514
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bce1e9ec
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bce24013
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bd1133db
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bd1e6657
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bdaa665c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bdf87233
rm -rf -- /tmp/claude-1654/-work-worktrees-task-be9d04c4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bf34cd7b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bf8f2d94
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bfd0c681
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bfd5c10d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-bffd3e70
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c04ce26d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c0cea8f5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c0e71ca7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c1453c82
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c18bc650
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c18cec40
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c1c1c031
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c1cb4001
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c238f9ca
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c26a1b0b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c270da97
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c28b88b9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c2c73160
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c389a57b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c3ba3e10
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c3ef4038
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c3f172ad
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c44579d7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c4b4ad81
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c58df935
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c682149c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c69c6449
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c747c0c4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c748deca
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c7653f3f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c7ae562e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c7e4a230
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c7f02607
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c7fd24a5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c81faedc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c8274f67
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c98cbc73
rm -rf -- /tmp/claude-1654/-work-worktrees-task-c9cb7349
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ca2107c0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ca347801
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ca5bf90c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ca8076ae
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cad08e40
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cad48ede
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cb632056
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cb6b4d80
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cbf389dd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cc3ffc66
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cc42b674
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ccd13def
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ccde064e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ccfe679f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cd9133eb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cdd7d74c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cde14651
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ce86e086
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ce8ef291
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cf298805
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cfe29503
rm -rf -- /tmp/claude-1654/-work-worktrees-task-cfec26fc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d092ee10
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d0b57b9c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d0ce3b58
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d12e11f0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d19080aa
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d1a471b0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d20d7003
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d2564da4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d28b29bf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d2c3674f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d2fc13b0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d340e01f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d36127b5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d3c686cb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d4b80a8c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d56a0975
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d5fd03f4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d670e3ae
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d67a21b1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d67c4ca2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d6b0addf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d6da454b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d6e48431
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d707a486
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d7614fb7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d7868261
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d8072595
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d84c5448
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d86bdf31
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d8d8a562
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d94b0c85
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d9877c94
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d99c29ee
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d9de1c95
rm -rf -- /tmp/claude-1654/-work-worktrees-task-d9f29397
rm -rf -- /tmp/claude-1654/-work-worktrees-task-da0006c8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-da1c6eeb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-da5dcdfe
rm -rf -- /tmp/claude-1654/-work-worktrees-task-da743107
rm -rf -- /tmp/claude-1654/-work-worktrees-task-daceeb1c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-daf2ea47
rm -rf -- /tmp/claude-1654/-work-worktrees-task-db2df8ea
rm -rf -- /tmp/claude-1654/-work-worktrees-task-db41dabc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-db8d32c0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-dbd75ccf
rm -rf -- /tmp/claude-1654/-work-worktrees-task-dc12c438
rm -rf -- /tmp/claude-1654/-work-worktrees-task-dc60bbd7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-dc7e1fe2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-dcfe6922
rm -rf -- /tmp/claude-1654/-work-worktrees-task-dd1fb91b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-dd35b058
rm -rf -- /tmp/claude-1654/-work-worktrees-task-dd90f1b7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-df7abdd7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-df8ec44a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-dfecf4e4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e056f0ff
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e0c34d39
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e0de6690
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e1349cf7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e23c0f83
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e24018e3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e278d330
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e28c82d0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e2e4f720
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e379bcd9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e38f32c6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e418768c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e4276bff
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e43d428d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e45ab432
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e4919459
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e4d33bd2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e5258e8c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e52b2bed
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e5c123ad
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e63ff4c2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e681212b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e6b4c624
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e72099f7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e778bd07
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e7c70293
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e7d4f999
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e814f8ee
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e8242d52
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e84d5998
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e857ef3c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e8784f40
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e87df8c7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e937b0c3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e942f776
rm -rf -- /tmp/claude-1654/-work-worktrees-task-e96c2ae3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ea539697
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ea7eebeb
rm -rf -- /tmp/claude-1654/-work-worktrees-task-eab3a4fc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-eb0f53d8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-eb353c94
rm -rf -- /tmp/claude-1654/-work-worktrees-task-eb43fc0e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ebfb9548
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ec6f244f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ec8d925b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ece4f83a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ecf8b748
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ed18c421
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ed4fc02c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ed6858e5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ed8199e4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-edb215b1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ee315192
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ee4f93c5
rm -rf -- /tmp/claude-1654/-work-worktrees-task-eedbd3b0
rm -rf -- /tmp/claude-1654/-work-worktrees-task-eeee4db1
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ef4432c3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ef4c37b4
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f0717652
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f0960466
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f0afe131
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f123dae7
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f144c9e8
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f189bd64
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f1a11dd3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f2976027
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f29c3d90
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f2d12082
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f2ed8c32
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f2f59326
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f2fe07ed
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f303b3ec
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f31c70a6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f35f1237
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f3973a3a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f3a457ae
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f41ed354
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f43511cc
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f442a78f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f4830a56
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f4a68261
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f4cf239f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f5e96e75
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f5eb193f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f60492a2
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f67b2762
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f686ba8a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f6d722a9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f6e3cc3c
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f6eea571
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f742a321
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f761fe1a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f7703a17
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f80457bd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f822e84d
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f84c519b
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f893d1a3
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f8fefbc6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f9696a82
rm -rf -- /tmp/claude-1654/-work-worktrees-task-f9fbf3bd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fa691f90
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fada17c6
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fb875ebd
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fbb67b8e
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fbed504a
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fc30e791
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fc778a50
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fcd04446
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fcf7ce80
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fd633b94
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fd701dd9
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fda2f329
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fde20c05
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fe172ced
rm -rf -- /tmp/claude-1654/-work-worktrees-task-fee31007
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ff7ab869
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ff8f8f3f
rm -rf -- /tmp/claude-1654/-work-worktrees-task-ffe310fe
```
