# Desktop worktree and checkout inventory (2026-11-17)

Task ab5639c7 (Investigate, CARD-1514 binding). READ-ONLY inventory of the Windows desktop. Nothing was deleted, moved, pruned, cleaned or restarted.

- Master SHA read: `29ba0c5002e642c4f4f91805be7e17660a40906d` (`origin/master` after `git fetch origin master`, 2026-11-17 ~19:05 UTC).
- Canonical checkout `C:\src\Antiphon`: DETACHED at `29ba0c5002e642c4f4f91805be7e17660a40906d` (rc/2026-11-26 = master at read time).
- Disk (Get-PSDrive C:): used 1,573.3 GiB, **free 289.1 GiB**. Docker Desktop `C:\Users\lndco\AppData\Local\Docker\wsl\disk\docker_data.vhdx` = 58.6 GiB, `...\wsl\main\ext4.vhdx` = 0.1 GiB (file length only; Docker/WSL not touched).
- Everything inventoried here totals 186.5 GiB; the class A proposal frees about **61.0 GiB**.

## Method and evidence

- `git -C C:\src\Antiphon worktree list --porcelain`: 182 registered worktrees (174 under `C:\Antiphon\worktrees`, 1 under `worktrees\land`, 2 under `C:\Antiphon\scratch`, 1 under `C:\Antiphon\evidence`, 4 under `C:\src`). One `prunable` flag.
- One `git ls-remote origin refs/heads/*` compared to each worktree HEAD. Result: 172 equal, 10 without a remote branch (detached checkouts and 3 task branches with no commits of their own).
- `git status --porcelain` per worktree (ignored build output does not count). Only 3 registered worktrees are dirty: the canonical checkout (2 untracked `.patch` files), `scratch\089ef857-rc-check` (5 staged source edits) and `C:\src\Antiphon-card0417` (1 untracked log).
- Task state: `GET /api/agent-tasks/<id8>` (the route `scripts/delegate.ps1 -Status` uses), one call per task id.
- Sanctioned-release verdicts: SELECT-only reads of `WorktreeResidueRuns` / `WorktreeResidueRunCandidates` / `TaskWorktreeRetirements` / `TaskWorktreeRetirementAttempts` in the `antiphon` DB (`docker exec antiphon-postgres psql`, read-only queries).
- Sizes: `du -sk` (allocated) for the worktrees. For `releases`, candidates, seeds and nightly, `robocopy <dir> <absent> /L /E /BYTES` (logical bytes, list-only; it created no destination, checked before and after). Canonical `C:\src\Antiphon` = 21.55 GiB (robocopy).
- Holders: a Win32_Process command-line and executable-path scan found processes holding only `releases\checkout-4025018e`, `state-4025018e`, `task-4025018e` and `releases\candidates` (the LIVE run). Process **cwd is UNOBSERVED**: non-elevated `handle.exe` returned "No matching handles found", which proves nothing without elevation.
- Side effects disclosed: `git fetch origin` in the canonical object store (remote-tracking refs only; no working tree touched). Six of my own `du.exe` processes were stopped when I replaced du with robocopy for speed.

## Sanctioned mechanisms (prefer these over hand deletion)

| Area | Mechanism | Shipped setting (server/appsettings.json) | What it does here |
|---|---|---|---|
| `C:\Antiphon\worktrees\card-task-<id8>` (any repo) | **`WorktreeLegacyRelease`** (CARD-0824 S4a; docs/orchestration-loop.md "desktop legacy release"), Hangfire `antiphon:desktop-legacy-release`, cron `15 * * * *` | `Enabled=true`, `Execute=true`, `MinTerminalHours=48`, `MaxReleasesPerTick=25`, `MaxRefusedAttempts=5` | Removes a terminal, clean, pushed worktree after 48 h through the guarded removal, creating salvage refs for reflog-only commits. Dry preview: `POST /api/agent-tasks/worktree-residue/desktop-legacy-release/preview` (operator token only; not called here). Removed per day: 11-08 145, 11-09 128, 11-10 148, 11-11 148, 11-12 214, 11-13 71, 11-14 97, 11-15 157, 11-16 56, 11-17 **0** (20 ticks; everything left is Held). |
| Same, caller-filed | `scripts/worktree-residue.ps1 -Action Release -TaskId <full guid> -ExpectedTaskRevision ... -SourceSha ... -Reason ...` | `WorktreeResidue:Execute=false` (the daily sweep only reports) | The one-command path for a tree the policy holds, such as `report_missing` or `retry_exhausted`. |
| `bin-*` outputs | `scripts/cleanup-build-junk.ps1` | requires producer creation records | Leaves legacy `bin-*` in place ("skip: producer creation records unavailable"). Worktree retirement removes them along with the tree. 49 `bin-*` dirs sit inside task worktrees, all inside trees listed below. None are in the canonical checkout. |
| `C:\Antiphon\releases\*`, `C:\Antiphon\scratch\*`, `C:\src\*` side checkouts | **none found** | - | grep of scripts/ and docs/ (retention, retire, prune, reap, sweep, cleanup) found no retention or cleanup for release clones, run state or candidates. `RunnerWorktreeRetention` is server2-only. These need hand deletion (operator command list below). |

## Totals per class

| Class | Meaning | Count | GiB |
|---|---|---:|---:|
| A | SAFE-TO-DELETE | 116 | 61.0 |
| B | SAFE-AFTER-PUSH (salvage first) | 3 | 0.4 |
| C | KEEP | 211 | 118.6 |
| D | ORPHAN | 15 | 6.6 |

Breakdown of class C: 163 desktop task worktrees (49.0 GiB) are **settling**.
 Their tasks finished within the last 48 h, and they are clean and pushed, so the sanctioned hourly release will remove them by itself from about 2026-11-18 onwards. They are not proposed for hand deletion. Six task worktrees older than 48 h are refused by the evidence gate (see C-2). The rest of class C is running tasks, the live run, the seed source, recent rc material, retained-evidence stores and the canonical checkout.

## Largest 15 items

| GiB | Class | Path | Why |
|---:|---|---|---|
| 21.55 | C | `C:\src\Antiphon` | canonical checkout: never delete |
| 5.13 | C | `C:\Antiphon\releases\checkout-053aa2ea` | rc/2026-11-23 cut, completed 2026-11-16 (<48h): keep until window passes |
| 4.97 | C | `C:\Antiphon\releases\checkout-4b599b91` | SEED SOURCE of live run (run 06863906): keep until operator says otherwise |
| 4.80 | C | `C:\Antiphon\releases\checkout-4025018e` | LIVE run f2de5b30 (class C per brief) |
| 4.76 | C | `C:\Antiphon\releases\checkout` | documented fixed path: keep |
| 4.22 | C | `C:\Antiphon\worktrees\card-task-0726e42d` | sanctioned release refused 5x (Held:retry_exhausted); attempt reason not read for this repo: operator decision |
| 4.05 | A | `C:\Antiphon\releases\checkout-8049d727` | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 4.03 | A | `C:\Antiphon\releases\checkout-302ba3d2` | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.94 | A | `C:\Antiphon\releases\checkout-1f33bc56` | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.83 | A | `C:\Antiphon\releases\checkout-ec8c178b` | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.77 | A | `C:\Antiphon\releases\checkout-4dc48523` | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.72 | A | `C:\Antiphon\releases\checkout-3ec2f72f` | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.61 | A | `C:\Antiphon\releases\checkout-8a7dbe6d` | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers (HEAD 10a826b9d on only 3 remote refs) |
| 3.48 | A | `C:\Antiphon\releases\checkout-5906dbf2` | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.47 | A | `C:\Antiphon\releases\checkout-1873094e` | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |

## Class A: proposed cleanup command list (operator/orchestrator to review and run later)

Criteria applied: the owning task is terminal; it is not the live run (f2de5b30 = 4025018e), not the seed source (06863906 = 4b599b91 / rc-2026-11-24-870a93d21f2b), and not within the last 3 days of rc material. No process command line references it. Git checkouts have a HEAD that is on an origin ref with no local-only commits. There is no release-dir retention setting, so the window used is "superseded and not the live run or its seed chain".

Note on dirt in release clones: every superseded `releases\checkout-<id>` has tracked `docs/screenshots/**/*.png` modified, or the untracked `.antiphon-nightly-owned` / `.antiphon-release-owned` markers. Both are E2E/producer-generated. A per-clone filter found **0 other** entries in all 11 dirty clones, and `git log --branches --not --remotes` = 0 in each.

Run each line as `Remove-Item -LiteralPath '<path>' -Recurse -Force` (no globs). Before running, re-check that no process holds the path.

```text
C:\Antiphon\releases\candidates\rc-2026-10-18-1f72e20158b9
C:\Antiphon\releases\candidates\rc-2026-10-18-42cbbd0417a2
C:\Antiphon\releases\candidates\rc-2026-10-18-81cfb267a0af
C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d
C:\Antiphon\releases\candidates\rc-2026-10-18-df6b422c71b0
C:\Antiphon\releases\candidates\rc-2026-10-20-0714a9a89e4b
C:\Antiphon\releases\candidates\rc-2026-10-20-0b455324d2cb
C:\Antiphon\releases\candidates\rc-2026-10-20-5a126f6fc5ae
C:\Antiphon\releases\candidates\rc-2026-10-20-5acbd708d640
C:\Antiphon\releases\candidates\rc-2026-10-20-66e9cfa9fc6b
C:\Antiphon\releases\candidates\rc-2026-10-20-91784f4d7898
C:\Antiphon\releases\candidates\rc-2026-10-20-aeaf16647b51
C:\Antiphon\releases\candidates\rc-2026-10-20-cadf547f7d69
C:\Antiphon\releases\candidates\rc-2026-10-20-ceb6e3d6b784
C:\Antiphon\releases\candidates\rc-2026-10-24-a13e544e2bae
C:\Antiphon\releases\candidates\rc-2026-10-24-a97194ee7a4a
C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f
C:\Antiphon\releases\candidates\rc-2026-10-27-4563e52529b2
C:\Antiphon\releases\candidates\rc-2026-10-27-cde8def41638
C:\Antiphon\releases\candidates\rc-2026-10-27-f84228f285dd
C:\Antiphon\releases\candidates\rc-2026-11-01-10a826b9d61f
C:\Antiphon\releases\candidates\rc-2026-11-01-679b696bd178
C:\Antiphon\releases\candidates\rc-2026-11-01-87f4a868a86c
C:\Antiphon\releases\candidates\rc-2026-11-01-e114313d36ef
C:\Antiphon\releases\candidates\rc-2026-11-01-ead93feb00fc
C:\Antiphon\releases\candidates\rc-2026-11-03-919d8d42f14d
C:\Antiphon\releases\candidates\rc-2026-11-04-f32aed42e58a
C:\Antiphon\releases\candidates\rc-2026-11-05-147034055988
C:\Antiphon\releases\candidates\rc-2026-11-06-5a95ae7a5611
C:\Antiphon\releases\candidates\rc-2026-11-07-1038d07e66a5
C:\Antiphon\releases\candidates\rc-2026-11-08-d164e93ae47a
C:\Antiphon\releases\candidates\rc-2026-11-09-0e7aef0379a0
C:\Antiphon\releases\candidates\rc-2026-11-10-74a8004f405b
C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb
C:\Antiphon\releases\candidates\rc-2026-11-12-5fef2c23b894
C:\Antiphon\releases\candidates\rc-2026-11-13-653f1145de5b
C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495
C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94
C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6
C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224
C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698
C:\Antiphon\releases\candidates\rc-2026-11-19-ab7af12c6990
C:\Antiphon\releases\checkout-1873094e
C:\Antiphon\releases\checkout-1f33bc56
C:\Antiphon\releases\checkout-302ba3d2
C:\Antiphon\releases\checkout-3ec2f72f
C:\Antiphon\releases\checkout-4dc48523
C:\Antiphon\releases\checkout-5906dbf2
C:\Antiphon\releases\checkout-8049d727
C:\Antiphon\releases\checkout-8a7dbe6d
C:\Antiphon\releases\checkout-997186e6
C:\Antiphon\releases\checkout-ec8c178b
C:\Antiphon\releases\checkout-f2e8ddf0
C:\Antiphon\releases\state-1d7cc1fe
C:\Antiphon\releases\state-231427bc
C:\Antiphon\releases\state-25e35cdf
C:\Antiphon\releases\state-500bb290
C:\Antiphon\releases\state-8e08368a
C:\Antiphon\releases\state-964900d9
C:\Antiphon\releases\task-0297fa9c
C:\Antiphon\releases\task-05d26999
C:\Antiphon\releases\task-08a27252
C:\Antiphon\releases\task-09a18478
C:\Antiphon\releases\task-0a72f317
C:\Antiphon\releases\task-0b36ae35
C:\Antiphon\releases\task-0e017562
C:\Antiphon\releases\task-136debc0
C:\Antiphon\releases\task-1873094e
C:\Antiphon\releases\task-1d7cc1fe
C:\Antiphon\releases\task-1f33bc56
C:\Antiphon\releases\task-217e7700
C:\Antiphon\releases\task-2271e6aa
C:\Antiphon\releases\task-231427bc
C:\Antiphon\releases\task-250a07fb
C:\Antiphon\releases\task-25e35cdf
C:\Antiphon\releases\task-2a34ba8c
C:\Antiphon\releases\task-302ba3d2
C:\Antiphon\releases\task-36d12cfa
C:\Antiphon\releases\task-3ca79566
C:\Antiphon\releases\task-3ec2f72f
C:\Antiphon\releases\task-42be26dd
C:\Antiphon\releases\task-44f789f3
C:\Antiphon\releases\task-4dc48523
C:\Antiphon\releases\task-4dd2a426
C:\Antiphon\releases\task-500bb290
C:\Antiphon\releases\task-5906dbf2
C:\Antiphon\releases\task-59e9866b
C:\Antiphon\releases\task-7783f346
C:\Antiphon\releases\task-78b36185
C:\Antiphon\releases\task-7b182710
C:\Antiphon\releases\task-8049d727
C:\Antiphon\releases\task-8423cb1a
C:\Antiphon\releases\task-8a7dbe6d
C:\Antiphon\releases\task-8e08368a
C:\Antiphon\releases\task-90ec246b
C:\Antiphon\releases\task-9153cbac
C:\Antiphon\releases\task-964900d9
C:\Antiphon\releases\task-997186e6
C:\Antiphon\releases\task-999f7534
C:\Antiphon\releases\task-a5303e1a
C:\Antiphon\releases\task-a5309b32
C:\Antiphon\releases\task-a9a3a406
C:\Antiphon\releases\task-b20bf5ac
C:\Antiphon\releases\task-c11e7803
C:\Antiphon\releases\task-c58e21f5
C:\Antiphon\releases\task-c9cb914c
C:\Antiphon\releases\task-ccaca2a1
C:\Antiphon\releases\task-cea1b9e0
C:\Antiphon\releases\task-cf82bfb0
C:\Antiphon\releases\task-d26dbaaf
C:\Antiphon\releases\task-e9c3b76a
C:\Antiphon\releases\task-ec8c178b
C:\Antiphon\releases\task-f2e8ddf0
C:\Antiphon\releases\task-f361e577
```

Two class A worktrees of `C:\src\markdown-package` (Canceled tasks, clean, HEAD in its `origin/master`). The sanctioned release holds them only as `report_missing`, so release them caller-filed with `scripts/worktree-residue.ps1 -Action Release` (full task GUID, revision, source SHA, reason), not by hand:

```text
C:\Antiphon\worktrees\card-task-157eb737
C:\Antiphon\worktrees\card-task-c0b9f2f7
```

| GiB | Path | Owner | Why |
|---:|---|---|---|
| 0.05 | `C:\Antiphon\releases\candidates\rc-2026-10-18-1f72e20158b9` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.08 | `C:\Antiphon\releases\candidates\rc-2026-10-18-42cbbd0417a2` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.01 | `C:\Antiphon\releases\candidates\rc-2026-10-18-81cfb267a0af` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.03 | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.02 | `C:\Antiphon\releases\candidates\rc-2026-10-18-df6b422c71b0` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.14 | `C:\Antiphon\releases\candidates\rc-2026-10-20-0714a9a89e4b` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.04 | `C:\Antiphon\releases\candidates\rc-2026-10-20-0b455324d2cb` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.10 | `C:\Antiphon\releases\candidates\rc-2026-10-20-5a126f6fc5ae` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.03 | `C:\Antiphon\releases\candidates\rc-2026-10-20-5acbd708d640` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.06 | `C:\Antiphon\releases\candidates\rc-2026-10-20-66e9cfa9fc6b` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.00 | `C:\Antiphon\releases\candidates\rc-2026-10-20-91784f4d7898` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.00 | `C:\Antiphon\releases\candidates\rc-2026-10-20-aeaf16647b51` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.06 | `C:\Antiphon\releases\candidates\rc-2026-10-20-cadf547f7d69` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.22 | `C:\Antiphon\releases\candidates\rc-2026-10-20-ceb6e3d6b784` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.39 | `C:\Antiphon\releases\candidates\rc-2026-10-24-a13e544e2bae` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.51 | `C:\Antiphon\releases\candidates\rc-2026-10-24-a97194ee7a4a` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.55 | `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.58 | `C:\Antiphon\releases\candidates\rc-2026-10-27-4563e52529b2` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 1.04 | `C:\Antiphon\releases\candidates\rc-2026-10-27-cde8def41638` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.35 | `C:\Antiphon\releases\candidates\rc-2026-10-27-f84228f285dd` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.65 | `C:\Antiphon\releases\candidates\rc-2026-11-01-10a826b9d61f` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.70 | `C:\Antiphon\releases\candidates\rc-2026-11-01-679b696bd178` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.00 | `C:\Antiphon\releases\candidates\rc-2026-11-01-87f4a868a86c` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.14 | `C:\Antiphon\releases\candidates\rc-2026-11-01-e114313d36ef` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.19 | `C:\Antiphon\releases\candidates\rc-2026-11-01-ead93feb00fc` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.33 | `C:\Antiphon\releases\candidates\rc-2026-11-03-919d8d42f14d` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.67 | `C:\Antiphon\releases\candidates\rc-2026-11-04-f32aed42e58a` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.32 | `C:\Antiphon\releases\candidates\rc-2026-11-05-147034055988` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 1.32 | `C:\Antiphon\releases\candidates\rc-2026-11-06-5a95ae7a5611` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.62 | `C:\Antiphon\releases\candidates\rc-2026-11-07-1038d07e66a5` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.67 | `C:\Antiphon\releases\candidates\rc-2026-11-08-d164e93ae47a` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.35 | `C:\Antiphon\releases\candidates\rc-2026-11-09-0e7aef0379a0` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.32 | `C:\Antiphon\releases\candidates\rc-2026-11-10-74a8004f405b` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.37 | `C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.41 | `C:\Antiphon\releases\candidates\rc-2026-11-12-5fef2c23b894` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.41 | `C:\Antiphon\releases\candidates\rc-2026-11-13-653f1145de5b` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 1.56 | `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 1.57 | `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 0.28 | `C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 1.59 | `C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 1.58 | `C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 1.58 | `C:\Antiphon\releases\candidates\rc-2026-11-19-ab7af12c6990` | rc candidate | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| 3.47 | `C:\Antiphon\releases\checkout-1873094e` | task 1873094e Deploy: Cut rc candidate from f84228f2 and start fresh Full Windows qualificat | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.94 | `C:\Antiphon\releases\checkout-1f33bc56` | task 1f33bc56 Custom: Start the rc/2026-11-04 Windows Full run failed-first | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 4.03 | `C:\Antiphon\releases\checkout-302ba3d2` | task 302ba3d2 Custom: Start the rc/2026-11-03 Windows Full run failed-first | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.72 | `C:\Antiphon\releases\checkout-3ec2f72f` | task 3ec2f72f Custom: Commission the fresh Windows rc qualification run from rc cde8def41 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.77 | `C:\Antiphon\releases\checkout-4dc48523` | task 4dc48523 Custom: Stop run c8a7bcee, seed from it, restart rc on the new tip | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.48 | `C:\Antiphon\releases\checkout-5906dbf2` | task 5906dbf2 Deploy: Replace run 79b742de: cut candidate from rc 4563e525, start fresh Full | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 4.05 | `C:\Antiphon\releases\checkout-8049d727` | task 8049d727 Custom: Stop run 766e3df5, seed from it, restart rc on rc/2026-11-06 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.61 | `C:\Antiphon\releases\checkout-8a7dbe6d` | task 8a7dbe6d Custom: Stop run aa64a906, seed from it, restart rc on the new tip | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers (HEAD 10a826b9d on only 3 remote refs) |
| 3.47 | `C:\Antiphon\releases\checkout-997186e6` | task 997186e6 Deploy: Resume or relaunch Windows qualification | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.83 | `C:\Antiphon\releases\checkout-ec8c178b` | task ec8c178b Custom: Stop run 0b05506b, seed from it, restart rc on rc/2026-11-05 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 3.36 | `C:\Antiphon\releases\checkout-f2e8ddf0` | task f2e8ddf0 Deploy: Launch full Windows qualification a13e544e | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| 0.00 | `C:\Antiphon\releases\state-1d7cc1fe` | task 1d7cc1fe Custom: Cut rc/2026-11-19 at ab7af12c6 and prepare seeded run (no launch) | superseded run state dir; task Succeeded 11-13 05:50 |
| 0.00 | `C:\Antiphon\releases\state-231427bc` | task 231427bc Custom: Recut rc after reboot and relaunch seeded Windows run | superseded run state dir; task Succeeded 11-11 19:18 |
| 0.00 | `C:\Antiphon\releases\state-25e35cdf` | task 25e35cdf Custom: Final result of ae84478d, refresh seed, launch rc/2026-11-18 | superseded run state dir; task Succeeded 11-12 12:28 |
| 0.00 | `C:\Antiphon\releases\state-500bb290` | task 500bb290 Custom: Cut next rc from master and launch seeded Windows run | superseded run state dir; task Succeeded 11-11 12:05 |
| 0.00 | `C:\Antiphon\releases\state-8e08368a` | task 8e08368a Custom: Cut the next rc candidate from master b559bfba2 and prepare the seed | superseded run state dir; task Succeeded 11-14 10:32 |
| 0.00 | `C:\Antiphon\releases\state-964900d9` | task 964900d9 Custom: Cut rc/2026-11-22 from master and prepare the run | superseded run state dir; task Succeeded 11-15 20:22 |
| 0.00 | `C:\Antiphon\releases\task-0297fa9c` | task 0297fa9c Custom: Replace the Windows rc run with failed-first on rc/2026-11-11 | superseded run task dir; task Succeeded 11-08 09:50 |
| 0.00 | `C:\Antiphon\releases\task-05d26999` | task 05d26999 Test: Windows qualification rc/2026-10-20 | superseded run task dir; task Canceled 10-21 11:43 |
| 0.00 | `C:\Antiphon\releases\task-08a27252` | task 08a27252 Test: Windows qualification rc/2026-10-20 | superseded run task dir; task Succeeded 10-21 10:13 |
| 0.00 | `C:\Antiphon\releases\task-09a18478` | task 09a18478 Test: Windows qualification rc/2026-10-20 | superseded run task dir; task Succeeded 10-20 20:25 |
| 0.02 | `C:\Antiphon\releases\task-0a72f317` | task 0a72f317 Custom: Windows: land graph combined tree and C1313 land class | superseded run task dir; task Succeeded 10-29 15:35 |
| 0.00 | `C:\Antiphon\releases\task-0b36ae35` | task 0b36ae35 Custom: Replace the Windows rc run with failed-first on rc/2026-11-13 | superseded run task dir; task Succeeded 11-09 04:51 |
| 0.00 | `C:\Antiphon\releases\task-0e017562` | task 0e017562 Custom: Stop run 7f8e9fd1 per the documented procedure and export its seed | superseded run task dir; task Succeeded 11-03 06:33 |
| 0.00 | `C:\Antiphon\releases\task-136debc0` | task 136debc0 Custom: Restart seeded Windows Full rc run on rc/2026-11-01 (hop fixed) | superseded run task dir; task Succeeded 11-01 11:50 |
| 0.00 | `C:\Antiphon\releases\task-1873094e` | task 1873094e Deploy: Cut rc candidate from f84228f2 and start fresh Full Windows qualificat | superseded run task dir; task Succeeded 10-27 22:27 |
| 0.00 | `C:\Antiphon\releases\task-1d7cc1fe` | task 1d7cc1fe Custom: Cut rc/2026-11-19 at ab7af12c6 and prepare seeded run (no launch) | superseded run task dir; task Succeeded 11-13 05:50 |
| 0.00 | `C:\Antiphon\releases\task-1f33bc56` | task 1f33bc56 Custom: Start the rc/2026-11-04 Windows Full run failed-first | superseded run task dir; task Succeeded 11-03 16:51 |
| 0.00 | `C:\Antiphon\releases\task-217e7700` | task 217e7700 Test: Launch full Windows rc qualification on rc/2026-10-24 | superseded run task dir; task Succeeded 10-24 07:37 |
| 0.00 | `C:\Antiphon\releases\task-2271e6aa` | task 2271e6aa Custom: Start the failed-first Windows rc run on rc/2026-11-07 | superseded run task dir; task Succeeded 11-06 06:21 |
| 0.00 | `C:\Antiphon\releases\task-231427bc` | task 231427bc Custom: Recut rc after reboot and relaunch seeded Windows run | superseded run task dir; task Succeeded 11-11 19:18 |
| 0.00 | `C:\Antiphon\releases\task-250a07fb` | task 250a07fb Custom: Start Windows Full rc run on rc/2026-11-01 with failed-first seed | superseded run task dir; task Canceled 11-06 17:05 |
| 0.00 | `C:\Antiphon\releases\task-25e35cdf` | task 25e35cdf Custom: Final result of ae84478d, refresh seed, launch rc/2026-11-18 | superseded run task dir; task Succeeded 11-12 12:28 |
| 0.00 | `C:\Antiphon\releases\task-2a34ba8c` | task 2a34ba8c Test: Start Windows qualification for rc 0714a9a89 | superseded run task dir; task Succeeded 10-22 16:10 |
| 0.00 | `C:\Antiphon\releases\task-302ba3d2` | task 302ba3d2 Custom: Start the rc/2026-11-03 Windows Full run failed-first | superseded run task dir; task Succeeded 11-03 08:35 |
| 0.00 | `C:\Antiphon\releases\task-36d12cfa` | task 36d12cfa Custom: Replace the Windows rc run with failed-first on rc/2026-11-08 | superseded run task dir; task Succeeded 11-06 21:54 |
| 0.00 | `C:\Antiphon\releases\task-3ca79566` | task 3ca79566 Custom: Replace the Windows rc run with failed-first on rc/2026-11-10 | superseded run task dir; task Succeeded 11-08 01:58 |
| 0.00 | `C:\Antiphon\releases\task-3ec2f72f` | task 3ec2f72f Custom: Commission the fresh Windows rc qualification run from rc cde8def41 | superseded run task dir; task Succeeded 10-30 08:41 |
| 0.00 | `C:\Antiphon\releases\task-42be26dd` | task 42be26dd Custom: Replace the Windows rc run with failed-first on rc/2026-11-14 | superseded run task dir; task Succeeded 11-09 13:11 |
| 0.07 | `C:\Antiphon\releases\task-44f789f3` | task 44f789f3 Custom: Prepare next rc cut and seed (no launch) | superseded run task dir; task Succeeded 11-12 07:02 |
| 0.00 | `C:\Antiphon\releases\task-4dc48523` | task 4dc48523 Custom: Stop run c8a7bcee, seed from it, restart rc on the new tip | superseded run task dir; task Succeeded 11-02 05:27 |
| 0.00 | `C:\Antiphon\releases\task-4dd2a426` | task 4dd2a426 Custom: Start the Windows failed-first run on rc/2026-11-15 | superseded run task dir; task Succeeded 11-10 13:06 |
| 0.00 | `C:\Antiphon\releases\task-500bb290` | task 500bb290 Custom: Cut next rc from master and launch seeded Windows run | superseded run task dir; task Succeeded 11-11 12:05 |
| 0.00 | `C:\Antiphon\releases\task-5906dbf2` | task 5906dbf2 Deploy: Replace run 79b742de: cut candidate from rc 4563e525, start fresh Full | superseded run task dir; task Succeeded 10-28 10:26 |
| 0.00 | `C:\Antiphon\releases\task-59e9866b` | task 59e9866b Custom: Replace the Windows rc run with failed-first on rc/2026-11-09 | superseded run task dir; task Succeeded 11-07 16:21 |
| 0.01 | `C:\Antiphon\releases\task-7783f346` | task 7783f346 Custom: Windows native verify A: failed chunks groups and splits | superseded run task dir; task Succeeded 10-29 13:07 |
| 0.00 | `C:\Antiphon\releases\task-78b36185` | task 78b36185 Custom: Stop run a074ff9a, seed from it, restart rc on the new tip | superseded run task dir; task Succeeded 11-02 10:45 |
| 0.00 | `C:\Antiphon\releases\task-7b182710` | task 7b182710 Test: Windows qualification rc 81cfb267 run | superseded run task dir; task Succeeded 10-20 18:53 |
| 0.00 | `C:\Antiphon\releases\task-8049d727` | task 8049d727 Custom: Stop run 766e3df5, seed from it, restart rc on rc/2026-11-06 | superseded run task dir; task Succeeded 11-04 18:23 |
| 0.00 | `C:\Antiphon\releases\task-8423cb1a` | task 8423cb1a Test: Stop abandoned rc qualification 5a126f6fc | superseded run task dir; task Succeeded 10-23 11:51 |
| 0.00 | `C:\Antiphon\releases\task-8a7dbe6d` | task 8a7dbe6d Custom: Stop run aa64a906, seed from it, restart rc on the new tip | superseded run task dir; task Succeeded 11-02 13:27 |
| 0.00 | `C:\Antiphon\releases\task-8e08368a` | task 8e08368a Custom: Cut the next rc candidate from master b559bfba2 and prepare the seed | superseded run task dir; task Succeeded 11-14 10:32 |
| 0.00 | `C:\Antiphon\releases\task-90ec246b` | task 90ec246b Deploy: Staged server2 rollout of rc/2026-11-08 with retention and slot flags | superseded run task dir; task Succeeded 11-07 01:12 |
| 0.00 | `C:\Antiphon\releases\task-9153cbac` | task 9153cbac Test: Windows qualification rc/2026-10-20 | superseded run task dir; task Canceled 10-21 15:32 |
| 0.00 | `C:\Antiphon\releases\task-964900d9` | task 964900d9 Custom: Cut rc/2026-11-22 from master and prepare the run | superseded run task dir; task Succeeded 11-15 20:22 |
| 0.00 | `C:\Antiphon\releases\task-997186e6` | task 997186e6 Deploy: Resume or relaunch Windows qualification | superseded run task dir; task Succeeded 10-26 09:59 |
| 0.24 | `C:\Antiphon\releases\task-999f7534` | task 999f7534 Custom: Final Windows re-gate of rc/2026-10-18 at df6b422c | superseded run task dir; task Succeeded 10-19 14:11 |
| 0.00 | `C:\Antiphon\releases\task-a5303e1a` | task a5303e1a Test: Windows RC qualification rc/2026-10-20 ceb6e3d6b | superseded run task dir; task Failed 10-22 05:45 |
| 0.00 | `C:\Antiphon\releases\task-a5309b32` | task a5309b32 Custom: Native Windows proof of stop-supervisor on a private sleeper tree | superseded run task dir; task Succeeded 11-09 20:02 |
| 0.00 | `C:\Antiphon\releases\task-a9a3a406` | task a9a3a406 Test: Windows qualification rc 81cfb267 | superseded run task dir; task Succeeded 10-20 17:18 |
| 0.01 | `C:\Antiphon\releases\task-b20bf5ac` | task b20bf5ac Custom: Windows: slot executor receipt-write-failed (Debug and fix) | superseded run task dir; task Succeeded 10-29 14:20 |
| 0.00 | `C:\Antiphon\releases\task-c11e7803` | task c11e7803 Test: Start Windows rc qualification 5a126f6fc | superseded run task dir; task Succeeded 10-23 02:22 |
| 0.00 | `C:\Antiphon\releases\task-c58e21f5` | task c58e21f5 Test: Windows RC qualification monitor (successor) | superseded run task dir; task Failed 10-22 07:05 |
| 0.00 | `C:\Antiphon\releases\task-c9cb914c` | task c9cb914c Custom: Replace the Windows rc run with failed-first on rc/2026-11-12 | superseded run task dir; task Succeeded 11-08 18:02 |
| 0.00 | `C:\Antiphon\releases\task-ccaca2a1` | task ccaca2a1 Deploy: Launch full Windows qualification 105792f7 | superseded run task dir; task Canceled 10-26 09:42 |
| 0.00 | `C:\Antiphon\releases\task-cea1b9e0` | task cea1b9e0 Debug: Debug Windows qualification bootstrap | superseded run task dir; task Succeeded 10-20 17:37 |
| 0.00 | `C:\Antiphon\releases\task-cf82bfb0` | task cf82bfb0 Custom: Final result of run 0b292fa4 and seed for the next cut | superseded run task dir; task Succeeded 11-13 05:40 |
| 0.00 | `C:\Antiphon\releases\task-d26dbaaf` | task d26dbaaf Custom: Stop run f40ad905 per the documented procedure and export its seed | superseded run task dir; task Succeeded 11-03 16:16 |
| 0.00 | `C:\Antiphon\releases\task-e9c3b76a` | task e9c3b76a Test: Windows qualification rc/2026-10-20 | superseded run task dir; task Canceled 10-21 17:47 |
| 0.00 | `C:\Antiphon\releases\task-ec8c178b` | task ec8c178b Custom: Stop run 0b05506b, seed from it, restart rc on rc/2026-11-05 | superseded run task dir; task Succeeded 11-04 10:35 |
| 0.00 | `C:\Antiphon\releases\task-f2e8ddf0` | task f2e8ddf0 Deploy: Launch full Windows qualification a13e544e | superseded run task dir; task Succeeded 10-25 13:26 |
| 0.00 | `C:\Antiphon\releases\task-f361e577` | task f361e577 Test: Windows qualification rc/2026-10-20 | superseded run task dir; task Succeeded 10-21 07:34 |
| 0.01 | `C:\Antiphon\worktrees\card-task-157eb737` | task 157eb737 (repo C:\src\markdown-package) | Canceled >40 days, clean, HEAD in origin/master; sanctioned release holds it only for report_missing (Canceled has no report) |
| 0.01 | `C:\Antiphon\worktrees\card-task-c0b9f2f7` | task c0b9f2f7 (repo C:\src\markdown-package) | Canceled >40 days, clean, HEAD in origin/master; sanctioned release holds it only for report_missing (Canceled has no report) |

## Class B: safe after a salvage push

| GiB | Path | Unpushed | Proposed salvage first |
|---:|---|---|---|
| 0.34 | `C:\Antiphon\scratch\78334d64-rc` | detached HEAD `5a690f98544f0fefc1dde7c27135f076f440288f`. 8 commits (2026-10-23, task 78334d64 Code CARD-1514 "Windows timing fixes (CP-8 reds)") reachable from no remote ref: cb12fcb6, 22de9830, 47dbb921, f829281a, eb22bc24, 385a313a, 7e8d2da4, 5a690f98 (the last reverts 7e8d2da4) | `git -C C:\src\Antiphon push origin 5a690f98544f0fefc1dde7c27135f076f440288f:refs/heads/rescue/scratch-78334d64-rc`, then `git -C C:\src\Antiphon worktree remove C:\Antiphon\scratch\78334d64-rc` |
| 0.01 | `C:\Antiphon\worktrees\card-task-66eec4dd` (markdown-package) | 1 commit `3ab4e4fa022d921fb0534a9fad88d1419a02c670` "Mockup B: inline comments" (2026-09-11), no remote branch; policy Held:unpushed | `git -C C:\src\markdown-package push origin 3ab4e4fa022d921fb0534a9fad88d1419a02c670:refs/heads/feat/card-task-66eec4dd`, then let the sanctioned release take it |
| 0.01 | `C:\Antiphon\worktrees\card-task-704c7117` (markdown-package) | 1 commit `ea970d249cb7ff22e434774181d05bb4af2ae583` "Mockup C: inline comments" (2026-09-11), no remote branch; policy Held:unpushed | `git -C C:\src\markdown-package push origin ea970d249cb7ff22e434774181d05bb4af2ae583:refs/heads/feat/card-task-704c7117`, then the sanctioned release |

## Class D: orphans (what each is)

| GiB | Path | What it is | State |
|---:|---|---|---|
| 0.99 | `C:\Antiphon\evidence\code-67a63a4f\base-checkout` | code-evidence base checkout, clean; HEAD on origin (feat/card-task-151b4e13, rescue/card-task-5bc00d31) | evidence of task 67a63a4f Code CARD-0527 (Succeeded); detached 1ef629678, on 3 remote refs |
| 0.15 | `C:\Antiphon\releases\antiphon-landgraph-canary-b5f7763cebe842acbd990f7ac0b593b2` | one-off canary directory, no task id | land-graph canary scratch (2026-10-29); n/a |
| 0.14 | `C:\Antiphon\worktrees\.card-task-5fcba512.removing-ec201e2d085b1894` | half-finished guarded removal; the retirement service owns these | task 5fcba512 (moved aside by a removal); n/a |
| 0.14 | `C:\Antiphon\worktrees\.card-task-608ed47c.removing-07bc3fac8dc6f57f` | half-finished guarded removal; the retirement service owns these | task 608ed47c (moved aside by a removal); n/a |
| 0.14 | `C:\Antiphon\worktrees\.card-task-9888335c.removing-c972aef0726f2b9a` | half-finished guarded removal; the retirement service owns these | task 9888335c (moved aside by a removal); n/a |
| 0.14 | `C:\Antiphon\worktrees\.card-task-d493e467.removing-8302484fa7c3c828` | half-finished guarded removal; the retirement service owns these | task d493e467 (moved aside by a removal); n/a |
| 0.14 | `C:\Antiphon\worktrees\.card-task-fa69124f.removing-e8a88f3848672c76` | half-finished guarded removal; the retirement service owns these | task fa69124f (moved aside by a removal); n/a |
| 0.14 | `C:\Antiphon\worktrees\.card-task-ffc7e9ad.removing-79405ad685d2cf98` | half-finished guarded removal; the retirement service owns these | task ffc7e9ad (moved aside by a removal); n/a |
| 1.63 | `C:\Antiphon\worktrees\card-task-a7b2b64a-rc` | orphan directory: git registration gone; contains bin-a7b2b64a outputs | task a7b2b64a Debug CARD-1514 (Succeeded 2026-10-23); UNOBSERVED (no gitdir) |
| 1.30 | `C:\Antiphon\worktrees\card-task-c490e425` | orphan directory: git registration gone | task c490e425 Custom (Succeeded 2026-10-29); UNOBSERVED (no gitdir) |
| 0.20 | `C:\Antiphon\worktrees\land\0f9784a67773` | land scratch worktree, no task row; HEAD in origin/master, clean | land worktree (no task); detached 679b696bd, in origin/master |
| 0.01 | `C:\Antiphon\worktrees\land\76535c9fff35` | leftover land directory | land scratch (no task); n/a |
| 1.09 | `C:\src\Antiphon-card0417` | manual CARD-0417 checkout; only dirt is untracked .antiphon-style.log; branch pushed | no task; branch card-0417-phone = origin tip |
| 0.18 | `C:\src\rc-bundle-c-final` | hand-made rc bundle checkout (2026-10), clean, HEAD on origin | no task; detached 30cc91b1d, on 9 remote refs |
| 0.18 | `C:\src\rc-c1525` | hand-made rc checkout (2026-10), clean, HEAD on origin | no task; detached 5a126f6fc, on 9 remote refs (incl. rc lines) |

D items are not on the class A list. The `.removing-*` move-asides and the two gitdir-less Antiphon dirs (`card-task-a7b2b64a-rc`, `card-task-c490e425`) belong to the retirement service, which records `unregistered` for c490e425. They need an operator call because their git state cannot be read: the registration is gone. `land\0f9784a67773`, `evidence\code-67a63a4f\base-checkout`, `C:\src\rc-bundle-c-final` and `C:\src\rc-c1525` are clean, and their HEAD is on origin. If the operator agrees they are unused, they are A-equivalent and removable with `git -C C:\src\Antiphon worktree remove <path>`.

## Class C-2: older than 48 h but refused by the sanctioned release (evidence gate)

| Task | Worktree | Refusal x attempts | Note |
|---|---|---|---|
| 211808d0 Custom CARD-1514 | `C:\Antiphon\worktrees\card-task-211808d0` | evidence_retained_missing x5 (last 11-10) | retained copy under `worktrees\.retained\211808d0...` |
| 6ebbcb6e Debug CARD-1514 | `C:\Antiphon\worktrees\card-task-6ebbcb6e` | evidence_retained_missing x5 (last 11-17 14:15) | `.retained\6ebbcb6e...` exists |
| 7ca19c43 Custom CARD-1514 | `C:\Antiphon\worktrees\card-task-7ca19c43` | evidence_retention_budget_bytes x5 | evidence larger than the retention budget |
| b5e998ee Custom CARD-1514 (Canceled) | `C:\Antiphon\worktrees\card-task-b5e998ee` | evidence_retained_missing x5 | `.retained\b5e998ee...` exists |
| e92f04ad Custom CARD-1514 | `C:\Antiphon\worktrees\card-task-e92f04ad` | ignored_content_preserved x5 | ignored content the gate will not discard |
| ff89ca56 Custom CARD-1514 | `C:\Antiphon\worktrees\card-task-ff89ca56` | evidence_retained_missing x4, then Refused | rc/2026-11-15 cut task |

All six are clean, and their branches equal the origin tips. Retained evidence keeps them in class C. Their release is a decision for the operator, not a hand delete.

## Live run, seed source and seeds (KEEP)

- LIVE run f2de5b30c626461099fbfbb73fd8ada8: `C:\Antiphon\releases\checkout-4025018e`, `task-4025018e`, `state-4025018e`, `candidates\rc-2026-11-26-29ba0c5002e6`. Process command lines reference all four. The worktree `C:\Antiphon\worktrees\card-task-4025018e` is also kept.
- Seed source (run 06863906, candidate rc-2026-11-24-870a93d21f2b): `checkout-4b599b91`, `task-4b599b91`, `state-4b599b91`, `candidates\rc-2026-11-24-870a93d21f2b` (logs `2026-11-17-0029-06863906` + timing). Class C until the operator says otherwise.
- Seeds `C:\Users\lndco\antiphon-seeds` (13.1 MiB, KEEP): 06863906.json, 06863906.report.json, 0b05506b.json, 160200cb.json, 160200cb.report.json, 231f573c.json, 34d41b7e.json, 34d41b7e.report.json, 472c7495.json, 4e0d1159.json, 766e3df5.json, 7f8e9fd1.json, 8e938b6d.json, a074ff9a.json, a21d8174.json, a21d8174.report.json, a6410818.json, aa64a906.json, b19a9880.json, b7812833.json, b7812833.report.json, c3f65ddb.json, c8a7bcee.json, cumulative-20261110.json, cumulative-20261110.report.json, d47848cc.json, dce78ee0.json, e737e9fe.json, f40ad905.json.
- `C:\Antiphon\nightly` (1.71 GiB, `last-run.json` 2026-09-04) is the Windmill nightly target named in AGENTS.md: KEEP. It has been stale since 2026-09-04, which is a separate question.

## Full inventory

| Class | GiB | Path | Owner | State / sanctioned verdict | Pushed | Dirty | Age | Reason |
|---|---:|---|---|---|---|---|---|---|
| A | 0.05 | `C:\Antiphon\releases\candidates\rc-2026-10-18-1f72e20158b9` | rc candidate |  | n/a | n/a | 2026-10-19 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.08 | `C:\Antiphon\releases\candidates\rc-2026-10-18-42cbbd0417a2` | rc candidate |  | n/a | n/a | 2026-10-19 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.01 | `C:\Antiphon\releases\candidates\rc-2026-10-18-81cfb267a0af` | rc candidate |  | n/a | n/a | 2026-10-20 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.03 | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d` | rc candidate |  | n/a | n/a | 2026-10-18 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.02 | `C:\Antiphon\releases\candidates\rc-2026-10-18-df6b422c71b0` | rc candidate |  | n/a | n/a | 2026-10-19 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.14 | `C:\Antiphon\releases\candidates\rc-2026-10-20-0714a9a89e4b` | rc candidate |  | n/a | n/a | 2026-10-22 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.04 | `C:\Antiphon\releases\candidates\rc-2026-10-20-0b455324d2cb` | rc candidate |  | n/a | n/a | 2026-10-21 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.10 | `C:\Antiphon\releases\candidates\rc-2026-10-20-5a126f6fc5ae` | rc candidate |  | n/a | n/a | 2026-10-23 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.03 | `C:\Antiphon\releases\candidates\rc-2026-10-20-5acbd708d640` | rc candidate |  | n/a | n/a | 2026-10-21 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.06 | `C:\Antiphon\releases\candidates\rc-2026-10-20-66e9cfa9fc6b` | rc candidate |  | n/a | n/a | 2026-10-21 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.00 | `C:\Antiphon\releases\candidates\rc-2026-10-20-91784f4d7898` | rc candidate |  | n/a | n/a | 2026-10-20 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.00 | `C:\Antiphon\releases\candidates\rc-2026-10-20-aeaf16647b51` | rc candidate |  | n/a | n/a | 2026-10-21 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.06 | `C:\Antiphon\releases\candidates\rc-2026-10-20-cadf547f7d69` | rc candidate |  | n/a | n/a | 2026-10-21 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.22 | `C:\Antiphon\releases\candidates\rc-2026-10-20-ceb6e3d6b784` | rc candidate |  | n/a | n/a | 2026-10-22 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.39 | `C:\Antiphon\releases\candidates\rc-2026-10-24-a13e544e2bae` | rc candidate |  | n/a | n/a | 2026-10-25 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.51 | `C:\Antiphon\releases\candidates\rc-2026-10-24-a97194ee7a4a` | rc candidate |  | n/a | n/a | 2026-10-24 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.55 | `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f` | rc candidate |  | n/a | n/a | 2026-10-26 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.58 | `C:\Antiphon\releases\candidates\rc-2026-10-27-4563e52529b2` | rc candidate |  | n/a | n/a | 2026-10-28 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 1.04 | `C:\Antiphon\releases\candidates\rc-2026-10-27-cde8def41638` | rc candidate |  | n/a | n/a | 2026-10-30 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.35 | `C:\Antiphon\releases\candidates\rc-2026-10-27-f84228f285dd` | rc candidate |  | n/a | n/a | 2026-10-27 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.65 | `C:\Antiphon\releases\candidates\rc-2026-11-01-10a826b9d61f` | rc candidate |  | n/a | n/a | 2026-11-02 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.70 | `C:\Antiphon\releases\candidates\rc-2026-11-01-679b696bd178` | rc candidate |  | n/a | n/a | 2026-11-01 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.00 | `C:\Antiphon\releases\candidates\rc-2026-11-01-87f4a868a86c` | rc candidate |  | n/a | n/a | 2026-11-01 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.14 | `C:\Antiphon\releases\candidates\rc-2026-11-01-e114313d36ef` | rc candidate |  | n/a | n/a | 2026-11-02 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.19 | `C:\Antiphon\releases\candidates\rc-2026-11-01-ead93feb00fc` | rc candidate |  | n/a | n/a | 2026-11-02 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.33 | `C:\Antiphon\releases\candidates\rc-2026-11-03-919d8d42f14d` | rc candidate |  | n/a | n/a | 2026-11-03 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.67 | `C:\Antiphon\releases\candidates\rc-2026-11-04-f32aed42e58a` | rc candidate |  | n/a | n/a | 2026-11-03 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.32 | `C:\Antiphon\releases\candidates\rc-2026-11-05-147034055988` | rc candidate |  | n/a | n/a | 2026-11-04 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 1.32 | `C:\Antiphon\releases\candidates\rc-2026-11-06-5a95ae7a5611` | rc candidate |  | n/a | n/a | 2026-11-04 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.62 | `C:\Antiphon\releases\candidates\rc-2026-11-07-1038d07e66a5` | rc candidate |  | n/a | n/a | 2026-11-06 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.67 | `C:\Antiphon\releases\candidates\rc-2026-11-08-d164e93ae47a` | rc candidate |  | n/a | n/a | 2026-11-06 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.35 | `C:\Antiphon\releases\candidates\rc-2026-11-09-0e7aef0379a0` | rc candidate |  | n/a | n/a | 2026-11-07 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.32 | `C:\Antiphon\releases\candidates\rc-2026-11-10-74a8004f405b` | rc candidate |  | n/a | n/a | 2026-11-08 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.37 | `C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb` | rc candidate |  | n/a | n/a | 2026-11-08 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.41 | `C:\Antiphon\releases\candidates\rc-2026-11-12-5fef2c23b894` | rc candidate |  | n/a | n/a | 2026-11-08 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.41 | `C:\Antiphon\releases\candidates\rc-2026-11-13-653f1145de5b` | rc candidate |  | n/a | n/a | 2026-11-09 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 1.56 | `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495` | rc candidate |  | n/a | n/a | 2026-11-09 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 1.57 | `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94` | rc candidate |  | n/a | n/a | 2026-11-10 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 0.28 | `C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6` | rc candidate |  | n/a | n/a | 2026-11-11 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 1.59 | `C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224` | rc candidate |  | n/a | n/a | 2026-11-11 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 1.58 | `C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698` | rc candidate |  | n/a | n/a | 2026-11-12 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 1.58 | `C:\Antiphon\releases\candidates\rc-2026-11-19-ab7af12c6990` | rc candidate |  | n/a | n/a | 2026-11-13 | superseded candidate (run ended; its failed-first seed is exported to C:\Users\lndco\antiphon-seeds where one exists) |
| A | 3.47 | `C:\Antiphon\releases\checkout-1873094e` | task 1873094e Deploy: Cut rc candidate from f84228f2 and start fresh Full Windows qualificat | Succeeded 10-27 22:27 | see checkout table | generated only | 2026-10-27 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| A | 3.94 | `C:\Antiphon\releases\checkout-1f33bc56` | task 1f33bc56 Custom: Start the rc/2026-11-04 Windows Full run failed-first | Succeeded 11-03 16:51 | see checkout table | generated only | 2026-11-04 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| A | 4.03 | `C:\Antiphon\releases\checkout-302ba3d2` | task 302ba3d2 Custom: Start the rc/2026-11-03 Windows Full run failed-first | Succeeded 11-03 08:35 | see checkout table | generated only | 2026-11-03 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| A | 3.72 | `C:\Antiphon\releases\checkout-3ec2f72f` | task 3ec2f72f Custom: Commission the fresh Windows rc qualification run from rc cde8def41 | Succeeded 10-30 08:41 | see checkout table | generated only | 2026-10-30 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| A | 3.77 | `C:\Antiphon\releases\checkout-4dc48523` | task 4dc48523 Custom: Stop run c8a7bcee, seed from it, restart rc on the new tip | Succeeded 11-02 05:27 | see checkout table | generated only | 2026-11-02 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| A | 3.48 | `C:\Antiphon\releases\checkout-5906dbf2` | task 5906dbf2 Deploy: Replace run 79b742de: cut candidate from rc 4563e525, start fresh Full | Succeeded 10-28 10:26 | see checkout table | generated only | 2026-10-29 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| A | 4.05 | `C:\Antiphon\releases\checkout-8049d727` | task 8049d727 Custom: Stop run 766e3df5, seed from it, restart rc on rc/2026-11-06 | Succeeded 11-04 18:23 | see checkout table | generated only | 2026-11-04 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| A | 3.61 | `C:\Antiphon\releases\checkout-8a7dbe6d` | task 8a7dbe6d Custom: Stop run aa64a906, seed from it, restart rc on the new tip | Succeeded 11-02 13:27 | see checkout table | generated only | 2026-11-03 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers (HEAD 10a826b9d on only 3 remote refs) |
| A | 3.47 | `C:\Antiphon\releases\checkout-997186e6` | task 997186e6 Deploy: Resume or relaunch Windows qualification | Succeeded 10-26 09:59 | see checkout table | generated only | 2026-10-26 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| A | 3.83 | `C:\Antiphon\releases\checkout-ec8c178b` | task ec8c178b Custom: Stop run 0b05506b, seed from it, restart rc on rc/2026-11-05 | Succeeded 11-04 10:35 | see checkout table | generated only | 2026-11-04 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| A | 3.36 | `C:\Antiphon\releases\checkout-f2e8ddf0` | task f2e8ddf0 Deploy: Launch full Windows qualification a13e544e | Succeeded 10-25 13:26 | see checkout table | generated only | 2026-10-26 | superseded rc producer clone; HEAD on origin rc/master refs; no local-only commits; dirt only E2E-regenerated docs/screenshots/*.png + .antiphon-*-owned markers |
| A | 0.00 | `C:\Antiphon\releases\state-1d7cc1fe` | task 1d7cc1fe Custom: Cut rc/2026-11-19 at ab7af12c6 and prepare seeded run (no launch) | Succeeded 11-13 05:50 | n/a | n/a | 2026-11-13 | superseded run state dir; task Succeeded 11-13 05:50 |
| A | 0.00 | `C:\Antiphon\releases\state-231427bc` | task 231427bc Custom: Recut rc after reboot and relaunch seeded Windows run | Succeeded 11-11 19:18 | n/a | n/a | 2026-11-12 | superseded run state dir; task Succeeded 11-11 19:18 |
| A | 0.00 | `C:\Antiphon\releases\state-25e35cdf` | task 25e35cdf Custom: Final result of ae84478d, refresh seed, launch rc/2026-11-18 | Succeeded 11-12 12:28 | n/a | n/a | 2026-11-13 | superseded run state dir; task Succeeded 11-12 12:28 |
| A | 0.00 | `C:\Antiphon\releases\state-500bb290` | task 500bb290 Custom: Cut next rc from master and launch seeded Windows run | Succeeded 11-11 12:05 | n/a | n/a | 2026-11-11 | superseded run state dir; task Succeeded 11-11 12:05 |
| A | 0.00 | `C:\Antiphon\releases\state-8e08368a` | task 8e08368a Custom: Cut the next rc candidate from master b559bfba2 and prepare the seed | Succeeded 11-14 10:32 | n/a | n/a | 2026-11-15 | superseded run state dir; task Succeeded 11-14 10:32 |
| A | 0.00 | `C:\Antiphon\releases\state-964900d9` | task 964900d9 Custom: Cut rc/2026-11-22 from master and prepare the run | Succeeded 11-15 20:22 | n/a | n/a | 2026-11-16 | superseded run state dir; task Succeeded 11-15 20:22 |
| A | 0.00 | `C:\Antiphon\releases\task-0297fa9c` | task 0297fa9c Custom: Replace the Windows rc run with failed-first on rc/2026-11-11 | Succeeded 11-08 09:50 | n/a | n/a | 2026-11-08 | superseded run task dir; task Succeeded 11-08 09:50 |
| A | 0.00 | `C:\Antiphon\releases\task-05d26999` | task 05d26999 Test: Windows qualification rc/2026-10-20 | Canceled 10-21 11:43 | n/a | n/a | 2026-10-21 | superseded run task dir; task Canceled 10-21 11:43 |
| A | 0.00 | `C:\Antiphon\releases\task-08a27252` | task 08a27252 Test: Windows qualification rc/2026-10-20 | Succeeded 10-21 10:13 | n/a | n/a | 2026-10-21 | superseded run task dir; task Succeeded 10-21 10:13 |
| A | 0.00 | `C:\Antiphon\releases\task-09a18478` | task 09a18478 Test: Windows qualification rc/2026-10-20 | Succeeded 10-20 20:25 | n/a | n/a | 2026-10-20 | superseded run task dir; task Succeeded 10-20 20:25 |
| A | 0.02 | `C:\Antiphon\releases\task-0a72f317` | task 0a72f317 Custom: Windows: land graph combined tree and C1313 land class | Succeeded 10-29 15:35 | n/a | n/a | 2026-10-29 | superseded run task dir; task Succeeded 10-29 15:35 |
| A | 0.00 | `C:\Antiphon\releases\task-0b36ae35` | task 0b36ae35 Custom: Replace the Windows rc run with failed-first on rc/2026-11-13 | Succeeded 11-09 04:51 | n/a | n/a | 2026-11-09 | superseded run task dir; task Succeeded 11-09 04:51 |
| A | 0.00 | `C:\Antiphon\releases\task-0e017562` | task 0e017562 Custom: Stop run 7f8e9fd1 per the documented procedure and export its seed | Succeeded 11-03 06:33 | n/a | n/a | 2026-11-03 | superseded run task dir; task Succeeded 11-03 06:33 |
| A | 0.00 | `C:\Antiphon\releases\task-136debc0` | task 136debc0 Custom: Restart seeded Windows Full rc run on rc/2026-11-01 (hop fixed) | Succeeded 11-01 11:50 | n/a | n/a | 2026-11-01 | superseded run task dir; task Succeeded 11-01 11:50 |
| A | 0.00 | `C:\Antiphon\releases\task-1873094e` | task 1873094e Deploy: Cut rc candidate from f84228f2 and start fresh Full Windows qualificat | Succeeded 10-27 22:27 | n/a | n/a | 2026-10-27 | superseded run task dir; task Succeeded 10-27 22:27 |
| A | 0.00 | `C:\Antiphon\releases\task-1d7cc1fe` | task 1d7cc1fe Custom: Cut rc/2026-11-19 at ab7af12c6 and prepare seeded run (no launch) | Succeeded 11-13 05:50 | n/a | n/a | 2026-11-13 | superseded run task dir; task Succeeded 11-13 05:50 |
| A | 0.00 | `C:\Antiphon\releases\task-1f33bc56` | task 1f33bc56 Custom: Start the rc/2026-11-04 Windows Full run failed-first | Succeeded 11-03 16:51 | n/a | n/a | 2026-11-03 | superseded run task dir; task Succeeded 11-03 16:51 |
| A | 0.00 | `C:\Antiphon\releases\task-217e7700` | task 217e7700 Test: Launch full Windows rc qualification on rc/2026-10-24 | Succeeded 10-24 07:37 | n/a | n/a | 2026-10-25 | superseded run task dir; task Succeeded 10-24 07:37 |
| A | 0.00 | `C:\Antiphon\releases\task-2271e6aa` | task 2271e6aa Custom: Start the failed-first Windows rc run on rc/2026-11-07 | Succeeded 11-06 06:21 | n/a | n/a | 2026-11-06 | superseded run task dir; task Succeeded 11-06 06:21 |
| A | 0.00 | `C:\Antiphon\releases\task-231427bc` | task 231427bc Custom: Recut rc after reboot and relaunch seeded Windows run | Succeeded 11-11 19:18 | n/a | n/a | 2026-11-12 | superseded run task dir; task Succeeded 11-11 19:18 |
| A | 0.00 | `C:\Antiphon\releases\task-250a07fb` | task 250a07fb Custom: Start Windows Full rc run on rc/2026-11-01 with failed-first seed | Canceled 11-06 17:05 | n/a | n/a | 2026-11-01 | superseded run task dir; task Canceled 11-06 17:05 |
| A | 0.00 | `C:\Antiphon\releases\task-25e35cdf` | task 25e35cdf Custom: Final result of ae84478d, refresh seed, launch rc/2026-11-18 | Succeeded 11-12 12:28 | n/a | n/a | 2026-11-13 | superseded run task dir; task Succeeded 11-12 12:28 |
| A | 0.00 | `C:\Antiphon\releases\task-2a34ba8c` | task 2a34ba8c Test: Start Windows qualification for rc 0714a9a89 | Succeeded 10-22 16:10 | n/a | n/a | 2026-10-23 | superseded run task dir; task Succeeded 10-22 16:10 |
| A | 0.00 | `C:\Antiphon\releases\task-302ba3d2` | task 302ba3d2 Custom: Start the rc/2026-11-03 Windows Full run failed-first | Succeeded 11-03 08:35 | n/a | n/a | 2026-11-03 | superseded run task dir; task Succeeded 11-03 08:35 |
| A | 0.00 | `C:\Antiphon\releases\task-36d12cfa` | task 36d12cfa Custom: Replace the Windows rc run with failed-first on rc/2026-11-08 | Succeeded 11-06 21:54 | n/a | n/a | 2026-11-06 | superseded run task dir; task Succeeded 11-06 21:54 |
| A | 0.00 | `C:\Antiphon\releases\task-3ca79566` | task 3ca79566 Custom: Replace the Windows rc run with failed-first on rc/2026-11-10 | Succeeded 11-08 01:58 | n/a | n/a | 2026-11-08 | superseded run task dir; task Succeeded 11-08 01:58 |
| A | 0.00 | `C:\Antiphon\releases\task-3ec2f72f` | task 3ec2f72f Custom: Commission the fresh Windows rc qualification run from rc cde8def41 | Succeeded 10-30 08:41 | n/a | n/a | 2026-10-31 | superseded run task dir; task Succeeded 10-30 08:41 |
| A | 0.00 | `C:\Antiphon\releases\task-42be26dd` | task 42be26dd Custom: Replace the Windows rc run with failed-first on rc/2026-11-14 | Succeeded 11-09 13:11 | n/a | n/a | 2026-11-09 | superseded run task dir; task Succeeded 11-09 13:11 |
| A | 0.07 | `C:\Antiphon\releases\task-44f789f3` | task 44f789f3 Custom: Prepare next rc cut and seed (no launch) | Succeeded 11-12 07:02 | n/a | n/a | 2026-11-12 | superseded run task dir; task Succeeded 11-12 07:02 |
| A | 0.00 | `C:\Antiphon\releases\task-4dc48523` | task 4dc48523 Custom: Stop run c8a7bcee, seed from it, restart rc on the new tip | Succeeded 11-02 05:27 | n/a | n/a | 2026-11-02 | superseded run task dir; task Succeeded 11-02 05:27 |
| A | 0.00 | `C:\Antiphon\releases\task-4dd2a426` | task 4dd2a426 Custom: Start the Windows failed-first run on rc/2026-11-15 | Succeeded 11-10 13:06 | n/a | n/a | 2026-11-11 | superseded run task dir; task Succeeded 11-10 13:06 |
| A | 0.00 | `C:\Antiphon\releases\task-500bb290` | task 500bb290 Custom: Cut next rc from master and launch seeded Windows run | Succeeded 11-11 12:05 | n/a | n/a | 2026-11-11 | superseded run task dir; task Succeeded 11-11 12:05 |
| A | 0.00 | `C:\Antiphon\releases\task-5906dbf2` | task 5906dbf2 Deploy: Replace run 79b742de: cut candidate from rc 4563e525, start fresh Full | Succeeded 10-28 10:26 | n/a | n/a | 2026-10-29 | superseded run task dir; task Succeeded 10-28 10:26 |
| A | 0.00 | `C:\Antiphon\releases\task-59e9866b` | task 59e9866b Custom: Replace the Windows rc run with failed-first on rc/2026-11-09 | Succeeded 11-07 16:21 | n/a | n/a | 2026-11-07 | superseded run task dir; task Succeeded 11-07 16:21 |
| A | 0.01 | `C:\Antiphon\releases\task-7783f346` | task 7783f346 Custom: Windows native verify A: failed chunks groups and splits | Succeeded 10-29 13:07 | n/a | n/a | 2026-10-29 | superseded run task dir; task Succeeded 10-29 13:07 |
| A | 0.00 | `C:\Antiphon\releases\task-78b36185` | task 78b36185 Custom: Stop run a074ff9a, seed from it, restart rc on the new tip | Succeeded 11-02 10:45 | n/a | n/a | 2026-11-02 | superseded run task dir; task Succeeded 11-02 10:45 |
| A | 0.00 | `C:\Antiphon\releases\task-7b182710` | task 7b182710 Test: Windows qualification rc 81cfb267 run | Succeeded 10-20 18:53 | n/a | n/a | 2026-10-20 | superseded run task dir; task Succeeded 10-20 18:53 |
| A | 0.00 | `C:\Antiphon\releases\task-8049d727` | task 8049d727 Custom: Stop run 766e3df5, seed from it, restart rc on rc/2026-11-06 | Succeeded 11-04 18:23 | n/a | n/a | 2026-11-05 | superseded run task dir; task Succeeded 11-04 18:23 |
| A | 0.00 | `C:\Antiphon\releases\task-8423cb1a` | task 8423cb1a Test: Stop abandoned rc qualification 5a126f6fc | Succeeded 10-23 11:51 | n/a | n/a | 2026-10-23 | superseded run task dir; task Succeeded 10-23 11:51 |
| A | 0.00 | `C:\Antiphon\releases\task-8a7dbe6d` | task 8a7dbe6d Custom: Stop run aa64a906, seed from it, restart rc on the new tip | Succeeded 11-02 13:27 | n/a | n/a | 2026-11-02 | superseded run task dir; task Succeeded 11-02 13:27 |
| A | 0.00 | `C:\Antiphon\releases\task-8e08368a` | task 8e08368a Custom: Cut the next rc candidate from master b559bfba2 and prepare the seed | Succeeded 11-14 10:32 | n/a | n/a | 2026-11-15 | superseded run task dir; task Succeeded 11-14 10:32 |
| A | 0.00 | `C:\Antiphon\releases\task-90ec246b` | task 90ec246b Deploy: Staged server2 rollout of rc/2026-11-08 with retention and slot flags | Succeeded 11-07 01:12 | n/a | n/a | 2026-11-07 | superseded run task dir; task Succeeded 11-07 01:12 |
| A | 0.00 | `C:\Antiphon\releases\task-9153cbac` | task 9153cbac Test: Windows qualification rc/2026-10-20 | Canceled 10-21 15:32 | n/a | n/a | 2026-10-21 | superseded run task dir; task Canceled 10-21 15:32 |
| A | 0.00 | `C:\Antiphon\releases\task-964900d9` | task 964900d9 Custom: Cut rc/2026-11-22 from master and prepare the run | Succeeded 11-15 20:22 | n/a | n/a | 2026-11-16 | superseded run task dir; task Succeeded 11-15 20:22 |
| A | 0.00 | `C:\Antiphon\releases\task-997186e6` | task 997186e6 Deploy: Resume or relaunch Windows qualification | Succeeded 10-26 09:59 | n/a | n/a | 2026-10-27 | superseded run task dir; task Succeeded 10-26 09:59 |
| A | 0.24 | `C:\Antiphon\releases\task-999f7534` | task 999f7534 Custom: Final Windows re-gate of rc/2026-10-18 at df6b422c | Succeeded 10-19 14:11 | n/a | n/a | 2026-10-19 | superseded run task dir; task Succeeded 10-19 14:11 |
| A | 0.00 | `C:\Antiphon\releases\task-a5303e1a` | task a5303e1a Test: Windows RC qualification rc/2026-10-20 ceb6e3d6b | Failed 10-22 05:45 | n/a | n/a | 2026-10-22 | superseded run task dir; task Failed 10-22 05:45 |
| A | 0.00 | `C:\Antiphon\releases\task-a5309b32` | task a5309b32 Custom: Native Windows proof of stop-supervisor on a private sleeper tree | Succeeded 11-09 20:02 | n/a | n/a | 2026-11-09 | superseded run task dir; task Succeeded 11-09 20:02 |
| A | 0.00 | `C:\Antiphon\releases\task-a9a3a406` | task a9a3a406 Test: Windows qualification rc 81cfb267 | Succeeded 10-20 17:18 | n/a | n/a | 2026-10-20 | superseded run task dir; task Succeeded 10-20 17:18 |
| A | 0.01 | `C:\Antiphon\releases\task-b20bf5ac` | task b20bf5ac Custom: Windows: slot executor receipt-write-failed (Debug and fix) | Succeeded 10-29 14:20 | n/a | n/a | 2026-10-29 | superseded run task dir; task Succeeded 10-29 14:20 |
| A | 0.00 | `C:\Antiphon\releases\task-c11e7803` | task c11e7803 Test: Start Windows rc qualification 5a126f6fc | Succeeded 10-23 02:22 | n/a | n/a | 2026-10-23 | superseded run task dir; task Succeeded 10-23 02:22 |
| A | 0.00 | `C:\Antiphon\releases\task-c58e21f5` | task c58e21f5 Test: Windows RC qualification monitor (successor) | Failed 10-22 07:05 | n/a | n/a | 2026-10-22 | superseded run task dir; task Failed 10-22 07:05 |
| A | 0.00 | `C:\Antiphon\releases\task-c9cb914c` | task c9cb914c Custom: Replace the Windows rc run with failed-first on rc/2026-11-12 | Succeeded 11-08 18:02 | n/a | n/a | 2026-11-08 | superseded run task dir; task Succeeded 11-08 18:02 |
| A | 0.00 | `C:\Antiphon\releases\task-ccaca2a1` | task ccaca2a1 Deploy: Launch full Windows qualification 105792f7 | Canceled 10-26 09:42 | n/a | n/a | 2026-10-26 | superseded run task dir; task Canceled 10-26 09:42 |
| A | 0.00 | `C:\Antiphon\releases\task-cea1b9e0` | task cea1b9e0 Debug: Debug Windows qualification bootstrap | Succeeded 10-20 17:37 | n/a | n/a | 2026-10-20 | superseded run task dir; task Succeeded 10-20 17:37 |
| A | 0.00 | `C:\Antiphon\releases\task-cf82bfb0` | task cf82bfb0 Custom: Final result of run 0b292fa4 and seed for the next cut | Succeeded 11-13 05:40 | n/a | n/a | 2026-11-13 | superseded run task dir; task Succeeded 11-13 05:40 |
| A | 0.00 | `C:\Antiphon\releases\task-d26dbaaf` | task d26dbaaf Custom: Stop run f40ad905 per the documented procedure and export its seed | Succeeded 11-03 16:16 | n/a | n/a | 2026-11-03 | superseded run task dir; task Succeeded 11-03 16:16 |
| A | 0.00 | `C:\Antiphon\releases\task-e9c3b76a` | task e9c3b76a Test: Windows qualification rc/2026-10-20 | Canceled 10-21 17:47 | n/a | n/a | 2026-10-21 | superseded run task dir; task Canceled 10-21 17:47 |
| A | 0.00 | `C:\Antiphon\releases\task-ec8c178b` | task ec8c178b Custom: Stop run 0b05506b, seed from it, restart rc on rc/2026-11-05 | Succeeded 11-04 10:35 | n/a | n/a | 2026-11-04 | superseded run task dir; task Succeeded 11-04 10:35 |
| A | 0.00 | `C:\Antiphon\releases\task-f2e8ddf0` | task f2e8ddf0 Deploy: Launch full Windows qualification a13e544e | Succeeded 10-25 13:26 | n/a | n/a | 2026-10-25 | superseded run task dir; task Succeeded 10-25 13:26 |
| A | 0.00 | `C:\Antiphon\releases\task-f361e577` | task f361e577 Test: Windows qualification rc/2026-10-20 | Succeeded 10-21 07:34 | n/a | n/a | 2026-10-21 | superseded run task dir; task Succeeded 10-21 07:34 |
| A | 0.01 | `C:\Antiphon\worktrees\card-task-157eb737` | task 157eb737 (repo C:\src\markdown-package) | Canceled 2026-10-04 / legacy-release Held:report_missing | equal; in origin/master | 0 | done Canceled 2026-10-04 | Canceled >40 days, clean, HEAD in origin/master; sanctioned release holds it only for report_missing (Canceled has no report) |
| A | 0.01 | `C:\Antiphon\worktrees\card-task-c0b9f2f7` | task c0b9f2f7 (repo C:\src\markdown-package) | Canceled 2026-10-04 / legacy-release Held:report_missing | equal; in origin/master | 0 | done Canceled 2026-10-04 | Canceled >40 days, clean, HEAD in origin/master; sanctioned release holds it only for report_missing (Canceled has no report) |
| B | 0.34 | `C:\Antiphon\scratch\78334d64-rc` | task 78334d64 Code CARD-1514 (Succeeded) Windows timing fixes | registered, detached | detached 5a690f985: 8 commits reachable from NO remote ref | 0 | created 2026-10-23 18:29; modified 2026-10-23 18:29 | unpushed commits cb12fcb6..5a690f98 (2026-10-23): salvage ref first |
| B | 0.01 | `C:\Antiphon\worktrees\card-task-66eec4dd` | task 66eec4dd (repo C:\src\markdown-package) | Succeeded 2026-09-11 / legacy-release Held:unpushed | no remote branch; 1 commit 3ab4e4fa on NO remote ref | 0 | done Succeeded 2026-09-11 | unpushed commit: salvage ref first |
| B | 0.01 | `C:\Antiphon\worktrees\card-task-704c7117` | task 704c7117 (repo C:\src\markdown-package) | Succeeded 2026-09-11 / legacy-release Held:unpushed | no remote branch; 1 commit ea970d24 on NO remote ref | 0 | done Succeeded 2026-09-11 | unpushed commit: salvage ref first |
| C | 1.80 | `C:\Antiphon\nightly` | Windmill u/lndcobra/antiphon_nightly_tests target (AGENTS.md) | last-run.json 2026-09-04 | n/a | n/a | 2026-09-04 | configured nightly checkout: keep (stale since 2026-09-04; separate question) |
| C | 0.00 | `C:\Antiphon\releases\.antiphon-test-evidence` | test evidence store |  | n/a | n/a | 2026-11-17 | modified today: keep |
| C | 1.66 | `C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a` | rc candidate |  | n/a | n/a | 2026-11-14 | recent candidate (<=3 days) or the seed chain just behind the live run: keep one more cycle |
| C | 1.67 | `C:\Antiphon\releases\candidates\rc-2026-11-21-64e3d78c487d` | rc candidate |  | n/a | n/a | 2026-11-15 | recent candidate (<=3 days) or the seed chain just behind the live run: keep one more cycle |
| C | 0.67 | `C:\Antiphon\releases\candidates\rc-2026-11-22-a71c45ab4d57` | rc candidate |  | n/a | n/a | 2026-11-15 | recent candidate (<=3 days) or the seed chain just behind the live run: keep one more cycle |
| C | 0.70 | `C:\Antiphon\releases\candidates\rc-2026-11-23-9b98828eb34e` | rc candidate |  | n/a | n/a | 2026-11-16 | recent candidate (<=3 days) or the seed chain just behind the live run: keep one more cycle |
| C | 0.77 | `C:\Antiphon\releases\candidates\rc-2026-11-24-870a93d21f2b` | rc candidate |  | n/a | n/a | 2026-11-17 | SEED SOURCE candidate (-SeedSourceCandidateId) |
| C | 0.00 | `C:\Antiphon\releases\candidates\rc-2026-11-25-38c64361b903` | rc candidate |  | n/a | n/a | 2026-11-17 | recent candidate (<=3 days) or the seed chain just behind the live run: keep one more cycle |
| C | 0.08 | `C:\Antiphon\releases\candidates\rc-2026-11-26-29ba0c5002e6` | rc candidate |  | n/a | n/a | 2026-11-17 | LIVE run candidate |
| C | 4.76 | `C:\Antiphon\releases\checkout` | shared release checkout named by docs/release-gates.md:551 | clean, HEAD 3db1a43b0 in origin/master | in origin/master | 0 | 2026-11-11 | documented fixed path: keep |
| C | 5.13 | `C:\Antiphon\releases\checkout-053aa2ea` | task 053aa2ea Custom: Cut rc/2026-11-23 from master and prepare the run | Succeeded 11-16 10:31 | see checkout table | generated only | 2026-11-16 | rc/2026-11-23 cut, completed 2026-11-16 (<48h): keep until window passes |
| C | 0.36 | `C:\Antiphon\releases\checkout-0637b75f` | task 0637b75f Custom: RC cut phase A: new rc branch at master 38c64361b, journal, seed, laun | Succeeded 11-17 17:34 | see checkout table | generated only | 2026-11-17 | RC cut phase A for rc/2026-11-25 (superseded by A2 4025018e today); <48h: keep until the live run settles |
| C | 4.80 | `C:\Antiphon\releases\checkout-4025018e` | task 4025018e Custom: RC cut phase A2: rc branch at master 29ba0c500, journal, launch record | Succeeded 11-17 18:28 | see checkout table | generated only | 2026-11-17 | LIVE run f2de5b30 (class C per brief) |
| C | 4.97 | `C:\Antiphon\releases\checkout-4b599b91` | task 4b599b91 Custom: Cut rc/2026-11-24 from 870a93d21 and prepare the run | Succeeded 11-17 00:24 | see checkout table | generated only | 2026-11-17 | SEED SOURCE of live run (run 06863906): keep until operator says otherwise |
| C | 0.00 | `C:\Antiphon\releases\state-053aa2ea` | task 053aa2ea Custom: Cut rc/2026-11-23 from master and prepare the run | Succeeded 11-16 10:31 | n/a | n/a | 2026-11-16 | rc/2026-11-23 cut, completed 2026-11-16 (<48h): keep until window passes |
| C | 0.00 | `C:\Antiphon\releases\state-4025018e` | task 4025018e Custom: RC cut phase A2: rc branch at master 29ba0c500, journal, launch record | Succeeded 11-17 18:28 | n/a | n/a | 2026-11-17 | LIVE run f2de5b30 (class C per brief) |
| C | 0.00 | `C:\Antiphon\releases\state-4b599b91` | task 4b599b91 Custom: Cut rc/2026-11-24 from 870a93d21 and prepare the run | Succeeded 11-17 00:24 | n/a | n/a | 2026-11-17 | SEED SOURCE of live run (run 06863906): keep until operator says otherwise |
| C | 0.00 | `C:\Antiphon\releases\state-ff89ca56` | task ff89ca56 Custom: Cut rc/2026-11-15 from master and prepare the run | Succeeded 11-15 04:25 | n/a | n/a | 2026-11-15 | its desktop worktree is held by the evidence gate (evidence_retained_missing): keep together |
| C | 0.00 | `C:\Antiphon\releases\task-053aa2ea` | task 053aa2ea Custom: Cut rc/2026-11-23 from master and prepare the run | Succeeded 11-16 10:31 | n/a | n/a | 2026-11-16 | rc/2026-11-23 cut, completed 2026-11-16 (<48h): keep until window passes |
| C | 0.00 | `C:\Antiphon\releases\task-0637b75f` | task 0637b75f Custom: RC cut phase A: new rc branch at master 38c64361b, journal, seed, laun | Succeeded 11-17 17:34 | n/a | n/a | 2026-11-17 | RC cut phase A for rc/2026-11-25 (superseded by A2 4025018e today); <48h: keep until the live run settles |
| C | 0.00 | `C:\Antiphon\releases\task-4025018e` | task 4025018e Custom: RC cut phase A2: rc branch at master 29ba0c500, journal, launch record | Succeeded 11-17 18:28 | n/a | n/a | 2026-11-17 | LIVE run f2de5b30 (class C per brief) |
| C | 0.00 | `C:\Antiphon\releases\task-4b599b91` | task 4b599b91 Custom: Cut rc/2026-11-24 from 870a93d21 and prepare the run | Succeeded 11-17 00:24 | n/a | n/a | 2026-11-17 | SEED SOURCE of live run (run 06863906): keep until operator says otherwise |
| C | 0.00 | `C:\Antiphon\releases\task-ff89ca56` | task ff89ca56 Custom: Cut rc/2026-11-15 from master and prepare the run | Succeeded 11-15 04:25 | n/a | n/a | 2026-11-15 | its desktop worktree is held by the evidence gate (evidence_retained_missing): keep together |
| C | 0.34 | `C:\Antiphon\scratch\089ef857-rc-check` | task 089ef857 Debug (Succeeded) Native drain hang in C1380 | registered, detached | detached 9e97df3f7 | 5 | created 2026-10-23 22:29; modified 2026-10-23 22:29 | 5 STAGED source edits (nightly-owned-process) not committed anywhere: real changes |
| C | 0.04 | `C:\Antiphon\worktrees\.antiphon` | creation-locks + worktrees metadata | live metadata | n/a | n/a |  | runtime metadata: keep |
| C | 0.09 | `C:\Antiphon\worktrees\.retained` | retirement service retained-evidence store (19 task ids) | preserved evidence | n/a | n/a | mod 2026-11-17 | retained evidence: keep |
| C | 0.19 | `C:\Antiphon\worktrees\card-CARD-0004` | repo C:\src\torquay-leander, branch feat/card-CARD-0004 | no task-id leaf; not a sanctioned-release candidate | no remote branch | 23 | mod 2026-09-06 | dirty with 23 entries, unpushed branch: keep |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-016fd320` | task 016fd320 Custom CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 13:58; modified 2026-11-16 13:58; done 2026-11-16 14:03 (29h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-03afb6f5` | task 03afb6f5 Review CARD-1878 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 03:38; modified 2026-11-17 03:38; done 2026-11-17 04:12 (15h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 2.33 | `C:\Antiphon\worktrees\card-task-053aa2ea` | task 053aa2ea Custom CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 10:14; modified 2026-11-16 10:14; done 2026-11-16 10:31 (33h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.22 | `C:\Antiphon\worktrees\card-task-0637b75f` | task 0637b75f Custom CARD-1514 | Succeeded / legacy-release Held:settling | no remote branch; HEAD 679b696bd is in origin/master (no own commits) | 0 | created 2026-11-17 17:21; modified 2026-11-17 17:21; done 2026-11-17 17:34 (2h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 4.22 | `C:\Antiphon\worktrees\card-task-0726e42d` | task 0726e42d (repo C:\src\markdown-package) | Canceled 2026-10-10 / legacy-release Held:retry_exhausted | equal | 0 | done Canceled 2026-10-10 | sanctioned release refused 5x (Held:retry_exhausted); attempt reason not read for this repo: operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-0729df64` | task 0729df64 Code CARD-1883 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 11:22; modified 2026-11-17 11:23; done 2026-11-17 12:31 (7h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-0ca7a8e1` | task 0ca7a8e1 Review CARD-1883 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 14:57; modified 2026-11-17 14:57; done 2026-11-17 15:41 (4h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-10145984` | task 10145984 Review CARD-1920 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 08:19; modified 2026-11-17 08:19; done 2026-11-17 08:27 (11h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-11b5204f` | task 11b5204f Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 15:08; modified 2026-11-16 15:08; done 2026-11-16 16:13 (27h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-13eaf3b2` | task 13eaf3b2 Code CARD-1948 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 02:00; modified 2026-11-17 02:00; done 2026-11-17 03:27 (16h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.07 | `C:\Antiphon\worktrees\card-task-1893101c` | task 1893101c (repo C:\src\markdown-package) | Succeeded 2026-10-05 / legacy-release Held:retry_exhausted | no remote branch; HEAD in origin/master | 0 | done Succeeded 2026-10-05 | sanctioned release refused 5x (Held:retry_exhausted); attempt reason not read for this repo: operator decision |
| C | 0.04 | `C:\Antiphon\worktrees\card-task-18d59e3e` | task 18d59e3e (repo C:\src\markdown-package) | Succeeded 2026-10-04 / legacy-release Held:retry_exhausted | equal; in origin/master | 0 | done Succeeded 2026-10-04 | sanctioned release refused 5x (Held:retry_exhausted); attempt reason not read for this repo: operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-1a367b40` | task 1a367b40 Review CARD-1820 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 03:37; modified 2026-11-17 03:38; done 2026-11-17 05:35 (14h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-1c1a23d3` | task 1c1a23d3 Review CARD-1948 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 03:37; modified 2026-11-17 03:37; done 2026-11-17 04:31 (15h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-1cfb5216` | task 1cfb5216 Docs CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 06:02; modified 2026-11-17 06:02; done 2026-11-17 06:41 (13h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-1df346ce` | task 1df346ce Review CARD-1883 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:14; modified 2026-11-16 18:14; done 2026-11-16 18:57 (25h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-2048f2d0` | task 2048f2d0 Plan CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 10:24; modified 2026-11-17 10:25; done 2026-11-17 10:42 (9h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 1.93 | `C:\Antiphon\worktrees\card-task-211808d0` | task 211808d0 Custom CARD-1514 | Succeeded / legacy-release Held:retry_exhausted | equal | 0 | created 2026-11-06 19:59; modified 2026-11-06 19:59; done 2026-11-06 21:03 (262h) | >=48h but sanctioned release refused 5x by the evidence gate (retained evidence): operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-21daa066` | task 21daa066 Review CARD-1964 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 04:46; modified 2026-11-17 04:46; done 2026-11-17 05:21 (14h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-23362b22` | task 23362b22 Debug CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 01:17; modified 2026-11-17 01:17; done 2026-11-17 01:40 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-233b0e13` | task 233b0e13 Debug CARD-1960 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 13:00; modified 2026-11-17 13:00; done 2026-11-17 14:32 (5h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-235c38c6` | task 235c38c6 Debug CARD-1955 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 17:08; modified 2026-11-16 17:08; done 2026-11-16 18:20 (25h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-23a610fb` | task 23a610fb Code CARD-1890 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 08:56; modified 2026-11-17 08:56; done 2026-11-17 10:53 (9h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-24cbd9e1` | task 24cbd9e1 Plan CARD-1954 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 00:00; modified 2026-11-17 00:00; done 2026-11-17 00:11 (19h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-277baf57` | task 277baf57 Code CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 14:54; modified 2026-11-17 14:54; done 2026-11-17 17:02 (2h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-2838f508` | task 2838f508 Review CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 06:43; modified 2026-11-17 06:43; done 2026-11-17 06:48 (13h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-292c3299` | task 292c3299 Docs CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 06:50; modified 2026-11-17 06:50; done 2026-11-17 07:08 (12h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-2a292434` | task 2a292434 Custom CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 10:36; modified 2026-11-16 10:36; done 2026-11-16 10:50 (33h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-2a400c38` | task 2a400c38 Review CARD-1960 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 01:59; modified 2026-11-17 01:59; done 2026-11-17 02:41 (17h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-2b159393` | task 2b159393 Debug CARD-1955 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 15:20; modified 2026-11-16 15:20; done 2026-11-16 15:59 (28h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-2bef90c6` | task 2bef90c6 Plan CARD-1964 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 05:22; modified 2026-11-17 05:22; done 2026-11-17 05:33 (14h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-2cd01c86` | task 2cd01c86 Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 13:49; modified 2026-11-17 13:50; done 2026-11-17 14:50 (5h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-2e74cf4e` | task 2e74cf4e Debug CARD-1958 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 01:08; modified 2026-11-17 01:09; done 2026-11-17 02:04 (17h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-31f0501d` | task 31f0501d Plan CARD-1882 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 00:36; modified 2026-11-17 00:36; done 2026-11-17 00:43 (19h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-324876aa` | task 324876aa Review CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 07:10; modified 2026-11-17 07:10; done 2026-11-17 07:15 (12h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-3283ed4f` | task 3283ed4f Investigate CARD-1514 | Dispatched / legacy-release Held:live_owner | equal | 0 | created 2026-11-17 19:01; modified 2026-11-17 19:01 | task running (Dispatched) |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-32a9a06f` | task 32a9a06f Review CARD-1890 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 07:38; modified 2026-11-17 07:39; done 2026-11-17 08:04 (11h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-32f24400` | task 32f24400 Debug CARD-1958 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 02:06; modified 2026-11-17 02:07; done 2026-11-17 04:09 (15h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-3802ef67` | task 3802ef67 Review CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 08:51; modified 2026-11-17 08:52; done 2026-11-17 09:17 (10h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-3871f961` | task 3871f961 Investigate CARD-1950 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 03:36; modified 2026-11-17 03:37; done 2026-11-17 04:02 (15h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-39f3b245` | task 39f3b245 Review CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 10:06; modified 2026-11-17 10:06; done 2026-11-17 10:30 (9h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-3c25d1ab` | task 3c25d1ab Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 08:30; modified 2026-11-16 08:31; done 2026-11-16 09:11 (34h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-3e32f1e6` | task 3e32f1e6 Review CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 17:04; modified 2026-11-17 17:04; done 2026-11-17 17:30 (2h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.22 | `C:\Antiphon\worktrees\card-task-4025018e` | task 4025018e Custom CARD-1514 | Succeeded / legacy-release Held:settling | no remote branch; HEAD 679b696bd is in origin/master (no own commits) | 0 | created 2026-11-17 18:24; modified 2026-11-17 18:24; done 2026-11-17 18:28 (1h) | cut task of the LIVE run f2de5b30 (A2): keep |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-43193a26` | task 43193a26 Review CARD-1889 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 08:07; modified 2026-11-17 08:07; done 2026-11-17 08:31 (11h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-495cb51f` | task 495cb51f Custom CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 10:01; modified 2026-11-16 10:02; done 2026-11-16 10:12 (33h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 2.32 | `C:\Antiphon\worktrees\card-task-4a63f1cf` | task 4a63f1cf Debug CARD-1947 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-15 20:57; modified 2026-11-15 20:57; done 2026-11-15 21:51 (46h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 2.33 | `C:\Antiphon\worktrees\card-task-4b599b91` | task 4b599b91 Custom CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 00:05; modified 2026-11-17 00:06; done 2026-11-17 00:24 (19h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-4b8393cc` | task 4b8393cc Code CARD-1956 | Failed / legacy-release Held:settling | equal | 0 | created 2026-11-16 21:56; modified 2026-11-16 21:56; done 2026-11-16 22:00 (21h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-4d0255a8` | task 4d0255a8 Debug CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 04:56; modified 2026-11-17 04:56; done 2026-11-17 06:04 (13h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-4f199f91` | task 4f199f91 Review CARD-1974 | Dispatched / legacy-release Held:live_owner | equal | 0 | created 2026-11-17 18:55; modified 2026-11-17 18:55 | task running (Dispatched) |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-4f507352` | task 4f507352 Review CARD-1820 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:51; modified 2026-11-16 23:52; done 2026-11-17 00:31 (19h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-4f546055` | task 4f546055 Plan CARD-1888 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 06:49; modified 2026-11-17 06:49; done 2026-11-17 06:58 (13h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-4f978baa` | task 4f978baa Plan CARD-1889 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 07:57; modified 2026-11-17 07:57; done 2026-11-17 08:05 (11h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-50b780b8` | task 50b780b8 Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 16:18; modified 2026-11-17 16:18; done 2026-11-17 17:19 (2h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-520fd455` | task 520fd455 Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 21:43; modified 2026-11-16 21:43; done 2026-11-16 22:21 (21h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-52484483` | task 52484483 Plan CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 10:56; modified 2026-11-17 10:56; done 2026-11-17 11:09 (8h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-52aeb04f` | task 52aeb04f Review CARD-1889 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 07:45; modified 2026-11-17 07:45; done 2026-11-17 07:54 (12h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-52dac4e7` | task 52dac4e7 Code CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 11:42; modified 2026-11-17 11:43; done 2026-11-17 13:19 (6h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-54721a0f` | task 54721a0f Review CARD-1955 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 13:56; modified 2026-11-16 13:57; done 2026-11-16 15:06 (28h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-54b64112` | task 54b64112 Review CARD-1888 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 07:00; modified 2026-11-17 07:00; done 2026-11-17 07:13 (12h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-55d5be7a` | task 55d5be7a Debug CARD-1956 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:23; modified 2026-11-16 18:23; done 2026-11-16 20:22 (23h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-57e7098c` | task 57e7098c Investigate CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 03:00; modified 2026-11-17 03:00; done 2026-11-17 03:09 (16h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-5a713719` | task 5a713719 Review CARD-1958 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 04:10; modified 2026-11-17 04:10; done 2026-11-17 06:00 (14h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-5db7c600` | task 5db7c600 Plan CARD-1890 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 07:27; modified 2026-11-17 07:27; done 2026-11-17 07:37 (12h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-5e090a01` | task 5e090a01 Debug CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 06:40; modified 2026-11-17 06:40; done 2026-11-17 08:50 (11h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-5f0633e8` | task 5f0633e8 Review CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 00:37; modified 2026-11-17 00:38; done 2026-11-17 01:07 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-5f96c990` | task 5f96c990 Review CARD-1964 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 05:36; modified 2026-11-17 05:36; done 2026-11-17 06:10 (13h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-6292b6f7` | task 6292b6f7 Review CARD-1882 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 20:09; modified 2026-11-16 20:09; done 2026-11-16 20:14 (23h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-636394f6` | task 636394f6 Review CARD-1883 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 12:33; modified 2026-11-17 12:33; done 2026-11-17 13:05 (6h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-6652bca1` | task 6652bca1 Plan CARD-1964 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 04:30; modified 2026-11-17 04:30; done 2026-11-17 04:44 (15h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-6696c9ea` | task 6696c9ea Review CARD-1888 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 06:25; modified 2026-11-17 06:25; done 2026-11-17 06:47 (13h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-67a5f843` | task 67a5f843 Debug CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 20:21; modified 2026-11-16 20:21; done 2026-11-16 21:22 (22h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-6863b533` | task 6863b533 Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 22:23; modified 2026-11-16 22:23; done 2026-11-16 23:50 (20h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-69bc3cb2` | task 69bc3cb2 Review CARD-1890 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 10:55; modified 2026-11-17 10:55; done 2026-11-17 11:30 (8h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-6ea96bcb` | task 6ea96bcb Code CARD-1888 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 08:55; modified 2026-11-17 08:56; done 2026-11-17 10:42 (9h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 2.30 | `C:\Antiphon\worktrees\card-task-6ebbcb6e` | task 6ebbcb6e Debug CARD-1514 | Succeeded / legacy-release Held:retry_exhausted | equal | 0 | created 2026-11-14 17:01; modified 2026-11-14 17:02; done 2026-11-14 17:47 (74h) | >=48h but sanctioned release refused 5x by the evidence gate (retained evidence): operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-705f622b` | task 705f622b Debug CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:04; modified 2026-11-16 18:04; done 2026-11-16 18:11 (25h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-74b623f9` | task 74b623f9 Code CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 19:29; modified 2026-11-16 19:30; done 2026-11-16 20:43 (23h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-77238f98` | task 77238f98 Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 02:24; modified 2026-11-17 02:24; done 2026-11-17 03:29 (16h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.84 | `C:\Antiphon\worktrees\card-task-7ca19c43` | task 7ca19c43 Custom CARD-1514 | Succeeded / legacy-release Held:retry_exhausted | equal | 0 | created 2026-11-07 12:06; modified 2026-11-07 12:06; done 2026-11-07 12:51 (247h) | >=48h but sanctioned release refused 5x by the evidence gate (retained evidence): operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-7f4a215b` | task 7f4a215b Debug CARD-1514 | Dispatched (created 19:01 today, after the list snapshot) | n/a | n/a | today | running task |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-7f6ac898` | task 7f6ac898 Merge CARD-1957 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 03:39; modified 2026-11-17 03:39; done 2026-11-17 08:56 (11h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-82a03c61` | task 82a03c61 Review CARD-1954 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 00:14; modified 2026-11-17 00:14; done 2026-11-17 00:51 (19h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-82a60f8b` | task 82a60f8b Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 17:33; modified 2026-11-17 17:33; done 2026-11-17 18:22 (1h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-83d30eec` | task 83d30eec Docs CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 01:09; modified 2026-11-17 01:09; done 2026-11-17 01:46 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-843d81c4` | task 843d81c4 Investigate CARD-1912 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:38; modified 2026-11-16 23:38; done 2026-11-16 23:54 (20h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-84a1901d` | task 84a1901d Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 11:35; modified 2026-11-16 11:36; done 2026-11-16 12:39 (31h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-8626ac28` | task 8626ac28 Plan CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 09:51; modified 2026-11-17 09:51; done 2026-11-17 10:04 (9h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-87ebe4f0` | task 87ebe4f0 Plan CARD-1960 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 01:42; modified 2026-11-17 01:42; done 2026-11-17 01:57 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-885b5234` | task 885b5234 Debug CARD-1955 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 12:52; modified 2026-11-16 12:53; done 2026-11-16 13:54 (30h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-8a7544d8` | task 8a7544d8 Code CARD-1820 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 05:38; modified 2026-11-17 05:38; done 2026-11-17 06:35 (13h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-8b386cd5` | task 8b386cd5 TestDesign CARD-1882 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:16; modified 2026-11-16 18:17; done 2026-11-16 18:27 (25h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-8b491f75` | task 8b491f75 Debug CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 03:38; modified 2026-11-16 03:39; done 2026-11-16 04:02 (39h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-8b8bd64c` | task 8b8bd64c Code CARD-1883 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 13:07; modified 2026-11-17 13:07; done 2026-11-17 14:55 (5h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-8c6a97b4` | task 8c6a97b4 Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:59; modified 2026-11-16 19:00; done 2026-11-16 19:38 (24h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-8d0de99b` | task 8d0de99b Investigate CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:37; modified 2026-11-16 23:38; done 2026-11-16 23:44 (20h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-8d32fab2` | task 8d32fab2 Review CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 01:48; modified 2026-11-17 01:48; done 2026-11-17 02:20 (17h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-907c7944` | task 907c7944 Custom CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:53; modified 2026-11-16 23:53; done 2026-11-17 00:03 (19h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-926788ad` | task 926788ad Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 10:54; modified 2026-11-17 10:54; done 2026-11-17 13:33 (6h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-92c62298` | task 92c62298 Review CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 20:46; modified 2026-11-16 20:46; done 2026-11-16 21:48 (22h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 2.33 | `C:\Antiphon\worktrees\card-task-964900d9` | task 964900d9 Custom CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-15 20:05; modified 2026-11-15 20:05; done 2026-11-15 20:22 (47h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-9b9b24c8` | task 9b9b24c8 Merge CARD-1836 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:59; modified 2026-11-16 23:59; done 2026-11-17 01:02 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-9c6efe95` | task 9c6efe95 Debug CARD-1950 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 04:06; modified 2026-11-17 04:06; done 2026-11-17 06:31 (13h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-9cbb56ed` | task 9cbb56ed Plan CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 13:44; modified 2026-11-16 13:44; done 2026-11-16 13:48 (30h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-9dbbe5bf` | task 9dbbe5bf Review CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 10:45; modified 2026-11-17 10:45; done 2026-11-17 10:53 (9h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-9e615360` | task 9e615360 Review CARD-1955 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 05:58; modified 2026-11-16 05:58; done 2026-11-16 09:09 (34h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-a0639c99` | task a0639c99 Review CARD-1956 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 20:25; modified 2026-11-16 20:25; done 2026-11-16 21:54 (22h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-a0e750a9` | task a0e750a9 Review CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 13:21; modified 2026-11-17 13:21; done 2026-11-17 14:08 (5h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-a1d414b6` | task a1d414b6 Plan CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 00:37; modified 2026-11-17 00:37; done 2026-11-17 00:47 (19h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-a3b6865d` | task a3b6865d Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 15:04; modified 2026-11-17 15:04; done 2026-11-17 16:16 (3h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 1.37 | `C:\Antiphon\worktrees\card-task-a6fe70f2` | task a6fe70f2 (repo C:\src\markdown-package) | Succeeded 2026-10-05 / legacy-release Held:retry_exhausted | equal | 0 | done Succeeded 2026-10-05 | sanctioned release refused 5x (Held:retry_exhausted); attempt reason not read for this repo: operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-a84df3ad` | task a84df3ad Investigate CARD-1844 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 09:09; modified 2026-11-17 09:09; done 2026-11-17 09:22 (10h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-a89a352d` | task a89a352d Review CARD-1820 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 06:37; modified 2026-11-17 06:38; done 2026-11-17 07:56 (12h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-aa63b6d1` | task aa63b6d1 Review CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 02:30; modified 2026-11-17 02:31; done 2026-11-17 02:36 (17h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-aaabade9` | task aaabade9 Code CARD-1960 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 03:39; modified 2026-11-17 03:39; done 2026-11-17 09:45 (10h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ab41aa31` | task ab41aa31 Code CARD-1974 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 17:44; modified 2026-11-17 17:45; done 2026-11-17 18:53 (1h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ab5639c7` | task ab5639c7 Investigate CARD-1514 | Dispatched / legacy-release Held:live_owner | no-remote-branch | 0 | created 2026-11-17 19:01; modified 2026-11-17 19:02 | task running (Dispatched) |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ab570408` | task ab570408 Custom CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 13:57; modified 2026-11-16 13:57; done 2026-11-16 15:05 (28h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ae89df49` | task ae89df49 Investigate CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 17:37; modified 2026-11-16 17:37; done 2026-11-16 17:59 (26h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-aefcc5e5` | task aefcc5e5 TestDesign CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 02:11; modified 2026-11-17 02:11; done 2026-11-17 02:27 (17h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 1.92 | `C:\Antiphon\worktrees\card-task-b5e998ee` | task b5e998ee Custom CARD-1514 | Canceled / legacy-release Held:retry_exhausted | equal | 0 | created 2026-11-06 18:33; modified 2026-11-06 18:33; done 2026-11-06 18:58 (265h) | >=48h but sanctioned release refused 5x by the evidence gate (retained evidence): operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-b65e738f` | task b65e738f Review CARD-1882 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 19:40; modified 2026-11-16 19:40; done 2026-11-16 19:47 (24h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-b66f32eb` | task b66f32eb Review CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 11:20; modified 2026-11-17 11:20; done 2026-11-17 11:39 (8h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ba15e8f5` | task ba15e8f5 Investigate CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 14:28; modified 2026-11-17 14:28; done 2026-11-17 14:38 (5h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-bad86a85` | task bad86a85 Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 04:04; modified 2026-11-16 04:05; done 2026-11-16 04:44 (39h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-be9d04c4` | task be9d04c4 Investigate CARD-1961 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 17:35; modified 2026-11-17 17:35; done 2026-11-17 17:48 (2h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-c1c28227` | task c1c28227 Investigate CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 00:53; modified 2026-11-17 00:53; done 2026-11-17 01:14 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-c3619b22` | task c3619b22 Review CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 00:50; modified 2026-11-17 00:50; done 2026-11-17 01:11 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-c58df935` | task c58df935 Plan CARD-1888 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 06:06; modified 2026-11-17 06:06; done 2026-11-17 06:22 (13h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-c6c85b5b` | task c6c85b5b Code CARD-1957 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:37; modified 2026-11-16 23:37; done 2026-11-17 02:20 (17h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-c747c0c4` | task c747c0c4 Investigate CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 09:37; modified 2026-11-17 09:37; done 2026-11-17 09:46 (10h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-c913e8aa` | task c913e8aa Review CARD-1882 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 00:46; modified 2026-11-17 00:46; done 2026-11-17 01:09 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ca3fe53e` | task ca3fe53e Review CARD-1888 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 13:41; modified 2026-11-17 13:41; done 2026-11-17 14:52 (5h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ca5bf90c` | task ca5bf90c Review CARD-1961 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 15:27; modified 2026-11-17 15:28; done 2026-11-17 15:51 (4h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-cad08e40` | task cad08e40 Code CARD-1964 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 06:39; modified 2026-11-17 06:39; done 2026-11-17 08:27 (11h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-cad48ede` | task cad48ede Review CARD-1957 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 02:23; modified 2026-11-17 02:23; done 2026-11-17 03:06 (16h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-cc5b4299` | task cc5b4299 Debug CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 04:51; modified 2026-11-16 04:51; done 2026-11-16 06:07 (37h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-cd7aa04a` | task cd7aa04a Debug CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 12:47; modified 2026-11-17 12:48; done 2026-11-17 13:47 (6h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-cd9b15c9` | task cd9b15c9 Review CARD-1955 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:23; modified 2026-11-16 18:24; done 2026-11-16 19:43 (24h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ce86e086` | task ce86e086 Review CARD-1969 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 11:12; modified 2026-11-17 11:12; done 2026-11-17 11:18 (8h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.07 | `C:\Antiphon\worktrees\card-task-cf18faac` | task cf18faac (repo C:\src\markdown-package) | Succeeded 2026-10-05 / legacy-release Held:retry_exhausted | no remote branch; HEAD on 2 remote refs | 0 | done Succeeded 2026-10-05 | sanctioned release refused 5x (Held:retry_exhausted); attempt reason not read for this repo: operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-cf58c961` | task cf58c961 Investigate CARD-1908 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:03; modified 2026-11-16 18:03; done 2026-11-16 18:11 (25h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-d0ae093a` | task d0ae093a Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 01:04; modified 2026-11-17 01:04; done 2026-11-17 01:34 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-d1556676` | task d1556676 Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 08:58; modified 2026-11-17 08:58; done 2026-11-17 10:51 (9h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-d1a53b65` | task d1a53b65 Review CARD-1888 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 10:45; modified 2026-11-17 10:45; done 2026-11-17 11:34 (8h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-d2eef712` | task d2eef712 Code CARD-1893 | Dispatched / legacy-release Held:live_owner | equal | 0 | created 2026-11-17 19:00; modified 2026-11-17 19:00 | task running (Dispatched) |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-d2fff5d4` | task d2fff5d4 Code CARD-1836 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:02; modified 2026-11-16 18:02; done 2026-11-16 21:50 (22h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-d336fcc3` | task d336fcc3 Docs CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:39; modified 2026-11-16 23:39; done 2026-11-17 00:34 (19h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-d5ecb5af` | task d5ecb5af Review CARD-1836 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 21:52; modified 2026-11-16 21:52; done 2026-11-16 23:11 (20h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-d7c12587` | task d7c12587 Code CARD-1878 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 01:32; modified 2026-11-17 01:32; done 2026-11-17 03:03 (16h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-dc5b6c22` | task dc5b6c22 Plan CARD-1820 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:38; modified 2026-11-16 23:39; done 2026-11-16 23:49 (20h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-dc76c20f` | task dc76c20f Plan CARD-1882 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 19:49; modified 2026-11-16 19:49; done 2026-11-16 19:54 (24h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-de583ac6` | task de583ac6 Review CARD-1960 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 14:33; modified 2026-11-17 14:33; done 2026-11-17 15:01 (4h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-e2208352` | task e2208352 Code CARD-1958 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:37; modified 2026-11-16 23:37; done 2026-11-17 01:04 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-e2dd6b0a` | task e2dd6b0a Plan CARD-1882 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:02; modified 2026-11-16 18:03; done 2026-11-16 18:13 (25h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 3.08 | `C:\Antiphon\worktrees\card-task-e3922461` | task e3922461 (repo C:\src\markdown-package) | Succeeded 2026-10-05 / legacy-release Held:retry_exhausted | equal | 0 | done Succeeded 2026-10-05 | sanctioned release refused 5x (Held:retry_exhausted); attempt reason not read for this repo: operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-e5d7e9da` | task e5d7e9da Debug CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 03:22; modified 2026-11-17 03:22; done 2026-11-17 03:33 (16h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.07 | `C:\Antiphon\worktrees\card-task-e6f03caf` | task e6f03caf (repo C:\src\markdown-package) | Succeeded 2026-10-05 / legacy-release Held:recovery_debt | equal; in origin/master | 0 | done Succeeded 2026-10-05 | sanctioned release refused 5x (Held:recovery_debt); attempt reason not read for this repo: operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-e7394f29` | task e7394f29 TestDesign CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 01:13; modified 2026-11-17 01:14; done 2026-11-17 01:58 (18h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-e92f04ad` | task e92f04ad Custom CARD-1514 | Succeeded / legacy-release Held:retry_exhausted | equal | 0 | created 2026-11-12 16:05; modified 2026-11-12 16:50; done 2026-11-12 16:50 (123h) | >=48h but sanctioned release refused 5x by the evidence gate (retained evidence): operator decision |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-e96c2ae3` | task e96c2ae3 Code CARD-1820 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 01:05; modified 2026-11-17 01:05; done 2026-11-17 03:29 (16h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-eb85f57b` | task eb85f57b Plan CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:47; modified 2026-11-16 23:47; done 2026-11-16 23:55 (20h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ebcd3b04` | task ebcd3b04 Plan CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 14:00; modified 2026-11-16 14:00; done 2026-11-16 14:02 (29h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ec0bad49` | task ec0bad49 Plan CARD-1883 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:02; modified 2026-11-16 18:02; done 2026-11-16 18:10 (25h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ee76e32c` | task ee76e32c Debug CARD-1955 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 09:11; modified 2026-11-16 09:11; done 2026-11-16 10:35 (33h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ee9cdb64` | task ee9cdb64 Review CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 23:58; modified 2026-11-16 23:58; done 2026-11-17 00:33 (19h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-ef70b56b` | task ef70b56b Plan CARD-1889 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 07:27; modified 2026-11-17 07:27; done 2026-11-17 07:43 (12h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-f29c3d90` | task f29c3d90 Review CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 02:01; modified 2026-11-17 02:01; done 2026-11-17 02:09 (17h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-f4a56d33` | task f4a56d33 Plan CARD-1920 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 07:58; modified 2026-11-17 07:58; done 2026-11-17 08:17 (11h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-f7f2de80` | task f7f2de80 Debug CARD-1955 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 00:40; modified 2026-11-16 00:40; done 2026-11-16 05:56 (38h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-f8dd5dd4` | task f8dd5dd4 TestDesign CARD-1882 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 19:56; modified 2026-11-16 19:56; done 2026-11-16 20:07 (23h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-f99cc46c` | task f99cc46c Code CARD-1893 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 18:15; modified 2026-11-16 18:15; done 2026-11-16 19:15 (24h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-fae5c49d` | task fae5c49d Code CARD-1961 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 14:10; modified 2026-11-17 14:10; done 2026-11-17 15:26 (4h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-fb6ac229` | task fb6ac229 Review CARD-1964 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 08:29; modified 2026-11-17 08:29; done 2026-11-17 09:06 (10h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-fbc651a4` | task fbc651a4 Investigate CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 09:58; modified 2026-11-16 09:58; done 2026-11-16 10:09 (33h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-fc32e512` | task fc32e512 Review CARD-1960 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 09:51; modified 2026-11-17 09:51; done 2026-11-17 12:58 (7h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-fcd04446` | task fcd04446 Code CARD-1888 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 11:36; modified 2026-11-17 11:36; done 2026-11-17 13:39 (6h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-fd30d2b0` | task fd30d2b0 Review CARD-1955 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 10:38; modified 2026-11-16 10:38; done 2026-11-16 11:34 (32h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-fde20c05` | task fde20c05 Debug CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-17 14:50; modified 2026-11-17 14:51; done 2026-11-17 15:33 (4h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 0.25 | `C:\Antiphon\worktrees\card-task-fee31007` | task fee31007 Merge CARD-1514 | Succeeded / legacy-release Held:settling | equal | 0 | created 2026-11-16 19:45; modified 2026-11-16 19:46; done 2026-11-16 21:40 (22h) | settling: <48h since completion; sanctioned hourly release takes it after 48h |
| C | 2.32 | `C:\Antiphon\worktrees\card-task-ff89ca56` | task ff89ca56 Custom CARD-1514 | Succeeded / legacy-release Refused:evidence_retained_missing | equal | 0 | created 2026-11-15 03:57; modified 2026-11-15 03:57; done 2026-11-15 04:25 (63h) | >=48h but sanctioned release refused 5x by the evidence gate (retained evidence): operator decision |
| C | 21.55 | `C:\src\Antiphon` | canonical checkout (AppHost, rc/2026-11-26 detached) | live | detached 29ba0c500 | 2 | n/a | canonical checkout: never delete |
| C | 0.01 | `C:\Users\lndco\antiphon-seeds` | 29 seed files (listed below) |  | n/a | n/a | 2026-11-17 | KEEP per brief |
| D | 0.99 | `C:\Antiphon\evidence\code-67a63a4f\base-checkout` | evidence of task 67a63a4f Code CARD-0527 (Succeeded) | registered, detached | detached 1ef629678, on 3 remote refs | 0 | created 2026-09-16 10:34; modified 2026-09-16 10:41 | code-evidence base checkout, clean; HEAD on origin (feat/card-task-151b4e13, rescue/card-task-5bc00d31) |
| D | 0.15 | `C:\Antiphon\releases\antiphon-landgraph-canary-b5f7763cebe842acbd990f7ac0b593b2` | land-graph canary scratch (2026-10-29) |  | n/a | n/a | 2026-10-29 | one-off canary directory, no task id |
| D | 0.14 | `C:\Antiphon\worktrees\.card-task-5fcba512.removing-ec201e2d085b1894` | task 5fcba512 (moved aside by a removal) | move-aside leftover of a guarded removal (2026-10-03/04) | n/a | n/a | mod 2026-10-03..04 | half-finished guarded removal; the retirement service owns these |
| D | 0.14 | `C:\Antiphon\worktrees\.card-task-608ed47c.removing-07bc3fac8dc6f57f` | task 608ed47c (moved aside by a removal) | move-aside leftover of a guarded removal (2026-10-03/04) | n/a | n/a | mod 2026-10-03..04 | half-finished guarded removal; the retirement service owns these |
| D | 0.14 | `C:\Antiphon\worktrees\.card-task-9888335c.removing-c972aef0726f2b9a` | task 9888335c (moved aside by a removal) | move-aside leftover of a guarded removal (2026-10-03/04) | n/a | n/a | mod 2026-10-03..04 | half-finished guarded removal; the retirement service owns these |
| D | 0.14 | `C:\Antiphon\worktrees\.card-task-d493e467.removing-8302484fa7c3c828` | task d493e467 (moved aside by a removal) | move-aside leftover of a guarded removal (2026-10-03/04) | n/a | n/a | mod 2026-10-03..04 | half-finished guarded removal; the retirement service owns these |
| D | 0.14 | `C:\Antiphon\worktrees\.card-task-fa69124f.removing-e8a88f3848672c76` | task fa69124f (moved aside by a removal) | move-aside leftover of a guarded removal (2026-10-03/04) | n/a | n/a | mod 2026-10-03..04 | half-finished guarded removal; the retirement service owns these |
| D | 0.14 | `C:\Antiphon\worktrees\.card-task-ffc7e9ad.removing-79405ad685d2cf98` | task ffc7e9ad (moved aside by a removal) | move-aside leftover of a guarded removal (2026-10-03/04) | n/a | n/a | mod 2026-10-03..04 | half-finished guarded removal; the retirement service owns these |
| D | 1.63 | `C:\Antiphon\worktrees\card-task-a7b2b64a-rc` | task a7b2b64a Debug CARD-1514 (Succeeded 2026-10-23) | gitdir C:\src\Antiphon\.git\worktrees\card-task-a7b2b64a-rc ABSENT | UNOBSERVED (no gitdir) | UNOBSERVED | mod 2026-10-23 | orphan directory: git registration gone; contains bin-a7b2b64a outputs |
| D | 1.30 | `C:\Antiphon\worktrees\card-task-c490e425` | task c490e425 Custom (Succeeded 2026-10-29) | gitdir ABSENT; legacy-release Held:unregistered | UNOBSERVED (no gitdir) | UNOBSERVED | mod 2026-10-29 | orphan directory: git registration gone |
| D | 0.20 | `C:\Antiphon\worktrees\land\0f9784a67773` | land worktree (no task) | registered, detached | detached 679b696bd, in origin/master | 0 | created 2026-09-25 22:31; modified 2026-10-31 09:25 | land scratch worktree, no task row; HEAD in origin/master, clean |
| D | 0.01 | `C:\Antiphon\worktrees\land\76535c9fff35` | land scratch (no task) | not a registered worktree | n/a | n/a | mod 2026-10-05 | leftover land directory |
| D | 1.09 | `C:\src\Antiphon-card0417` | no task | registered, detached | branch card-0417-phone = origin tip | 1 | created 2026-09-09 16:28; modified 2026-09-09 17:09 | manual CARD-0417 checkout; only dirt is untracked .antiphon-style.log; branch pushed |
| D | 0.18 | `C:\src\rc-bundle-c-final` | no task | registered, detached | detached 30cc91b1d, on 9 remote refs | 0 | created 2026-10-23 01:37; modified 2026-10-23 01:38 | hand-made rc bundle checkout (2026-10), clean, HEAD on origin |
| D | 0.18 | `C:\src\rc-c1525` | no task | registered, detached | detached 5a126f6fc, on 9 remote refs (incl. rc lines) | 0 | created 2026-10-23 02:38; modified 2026-10-23 02:38 | hand-made rc checkout (2026-10), clean, HEAD on origin |

## Remaining uncertainty

- Process cwd and open handles are UNOBSERVED (no elevation). The holder check is command-line only. Re-check right before any deletion.
- markdown-package `retry_exhausted` attempt reasons were not read row by row. Those trees stay class C. Its `origin` refs were not fetched, so "in origin/master" for those uses the local remote-tracking refs.
- Release-dir sizes are logical bytes (robocopy). Worktree sizes are allocated (du).
- No retention window is configured for `C:\Antiphon\releases`. The "superseded and not live/seed/<=3 days" window is this note's proposal, not a repository rule.

## Not done, noted

- Fix idea (not designed): a sanctioned retention sweep for `C:\Antiphon\releases\{checkout,task,state}-<id>` and `candidates\*`, keyed off the live run and seed chain, would replace the hand list above.

