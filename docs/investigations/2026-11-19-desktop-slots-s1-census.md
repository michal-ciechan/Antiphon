# CARD-0824 desktop hot slots: S1 read-only census (2026-11-19)

Task `2086875f` (Debug, desktop). Base master `0822e8bbc60f2c8f333504d60cf3484cc7b6f2b8`.
Plan: `docs/superpowers/plans/2026-11-19-desktop-hot-worktree-slots-plan.md` §S1 at
`origin/feat/card-task-b68892ff` `61d4149c32d52b9286a9b4feeefb1e73096ffe5b`.
Measured 2026-11-19 12:00Z to 12:35Z. The desktop was idle: `native-run.lock` was absent at the start
and again before the builds, and the build-slot broker showed 0 of 2 occupied.

I changed no setting, git config, AppHost, runner or pool. The writes were the temporary worktree
`C:\Antiphon\slots-census-tmp\wt`, its `bin/` and `obj/`, NuGet's user cache during restore, and this
note. The worktree was removed, with its directory and its registration. `.git/worktrees` in
`C:\src\Antiphon` gained a detached entry for that worktree and lost it again on removal. It never
had a branch.

## Results

| # | Measure | Value | Source |
|---|---|---|---|
| 1 | `native-run.lock` | absent | `Test-Path` (twice) |
| 1 | `C:\Antiphon\runner\phone-home.json` | absent | `Test-Path` |
| 1 | `C:\Antiphon\worktrees` directories | 219 `card-task-*`, 0 `ws-*`, 10 other (`.antiphon`, `.retained`, `land`, `card-CARD-0004`, 6 `.card-task-*.removing-*` from 2026-10-03/04) | `Get-ChildItem` |
| 1 | `card-task-*` total size | **75.45 GiB** (full walk of all 219, 201 s) | `Measure-Object Length -Sum` per tree |
| 1 | Average / max / min tree | 352.8 MiB / 5.02 GiB / 5.6 MiB | same |
| 1 | Size buckets | 202 trees between 50 and 500 MiB (44.47 GiB); 1 between 0.5 and 1 GiB (0.82 GiB); 13 at 1 GiB or more (30.12 GiB); 3 under 50 MiB | same |
| 1 | Trees with `tests\Antiphon.Tests\bin` | 8 (each of the 8 has a `C:\Antiphon\worktrees\.retained\<taskGuid>\` entry) | `Test-Path` |
| 1 | Trees with `client\node_modules` / `client\dist` | 1 / 1 (`card-task-c490e425`) | `Test-Path` |
| 1 | Oldest / newest (creation) | `card-task-66eec4dd` 2026-09-11T15:37Z / `card-task-de01ed8b` 2026-11-19T11:59Z | `CreationTimeUtc` |
| 1 | Age by creation | 177 under 48 h, 28 from 48 h to 7 d, 14 over 7 d | same |
| 1 | Owning task state | 203 Succeeded, 8 Canceled, 3 Failed, 4 Dispatched, 1 Blocked (none missing) | `GET /api/agent-tasks?status=...&since=2026-09-01` joined on the id8 |
| 1 | Task runner of those trees | 173 server2, 2 server2-temp, 44 desktop | same (`runnerId`) |
| 1 | Terminal, completed under 48 h | 177 trees, 39.36 GiB | `completedAt` |
| 1 | Terminal, completed 48 h or more ago | 37 trees, 34.96 GiB: 16 server2-runner (48.4 to 50.8 h, 0.22 GiB each) and 21 desktop-runner | same |
| 1 | S4a (`antiphon:desktop-legacy-release`) | hourly ticks, `execute=true`. Today: 07:33 filed 9 retired 10; 08:27 7/7; 09:16 7/0 (salvage refs kept, repository lease busy); 10:18 5/1; 11:27 7/6 | `C:\src\Antiphon\server\logs\antiphon-20261119.log` |
| 2 | `core.longpaths`, `C:\src\Antiphon` | **unset** (local, global and system all exit 1) | `git config --get` |
| 2 | `core.longpaths`, `C:\Antiphon\releases\checkout-fc9a0547` (newest, 2026-11-18) | **unset** (local and effective exit 1) | same |
| 2 | `core.longpaths`, `checkout-4025018e` (2026-11-17) | `true` (local) | same |
| 2 | `core.longpaths`, `checkout-0637b75f` (2026-11-17) | `true` (local) | same |
| 3 | Free on C: | 361,931,984,896 bytes (**337.1 GiB**). Used 1,637,800,804,352 (1,525.3 GiB) | `Get-PSDrive` |
| 3 | Pool drive | C: as well (`C:\Antiphon\slots`). No other fixed filesystem drive. | `Get-PSDrive -PSProvider FileSystem` |
| 3 | Defender | RealTimeProtectionEnabled=True, OnAccessProtectionEnabled=True, AntivirusEnabled=True, IsTamperProtected=True, AMRunningMode=Normal. Exclusions: **UNOBSERVED** (needs admin) | `Get-MpComputerStatus`, `Get-MpPreference` |
| 3 | Desktop build-slot broker | budget 2, maxCpuCount 4, floor 6144 MiB, 8700 MiB available, 0 leases | `GET :17204/build-slots` |
| 4 | Fresh `git worktree add --detach` (at master 0822e8bbc, 11,090 files) | **8.5 s**; tree 231.7 MiB | Stopwatch |
| 4 | Cold build `tests/Antiphon.Tests` in that fresh tree (with restore, no `bin/`/`obj/`) | **404.2 s** (MSBuild 6:41.7), 0 errors, 2203 warnings; slot wait about 1 s | Stopwatch, via `build-slot.ps1` |
| 4 | Warm build `--no-restore` straight after, nothing changed | **15.7 s** (MSBuild 0:13.4), 0 errors; slot wait 0 s | same |
| 4 | Size of one built tree (`Antiphon.Tests` closure built) | **2.28 GiB**, 16,172 files | `Measure-Object` |
| 4 | `git worktree remove --force` plus `rd` | 6.9 s | Stopwatch |
| 4 | Warm build after a real SHA change in a slot | **UNOBSERVED** (between 16 s and 404 s, depending on what changed) | not run |

### Deviation on row 4 (warm build)

The brief said to time an incremental build in an existing `card-task-*` that already has `bin\`. All 8
such trees (`053aa2ea`, `4a63f1cf`, `4b599b91`, `964900d9`, `ff89ca56`, `6ebbcb6e`, `211808d0`,
`b5e998ee`) have an entry under `C:\Antiphon\worktrees\.retained\`. Building in one would write into a
retained tree, and the standing authority allows writes only under `C:\Antiphon\slots-census-tmp` and in
this note. So the warm number is a no-op rebuild in the fresh temp tree right after its cold build. It is
the floor of warmth, not a measured lease-to-new-SHA rebuild. The cold number is the plan's "S1
fresh-tree build minutes": **6.7 minutes**.

### Is S4a working?

It is running. The hourly job executes and retires about 0 to 10 trees per tick (25 per tick cap). The
inflow is 177 trees in the last 48 h, about 3.7 an hour, so it keeps pace. The 177 terminal trees under
48 h are the designed floor. Of the 37 trees past 48 h:

- 16 server2-runner trees are only 0.4 to 2.8 h past the floor and have not been examined yet. 7 of them
  (`3802ef67`, `39f3b245`, `9dbbe5bf`, `ce86e086`, `69bc3cb2`, `d1a53b65`, `b66f32eb`) show HEAD 1 commit
  ahead of their upstream tracking ref (`git rev-list --count @{u}..HEAD`, local tracking ref, not
  fetched). S4a may hold these as `unpushed`. Not verified.
- 21 desktop-runner trees (about 31.4 GiB) stay. The server log gives the reason for 3 of them:
  `66eec4dd` and `704c7117` are `unpushed` and `c490e425` is `unregistered`. 8 are the `.retained` trees
  listed above (about 17.5 GiB). For the other 10 (`0726e42d` 5.02 GiB, `e3922461` 3.35 GiB, `a7b2b64a-rc`
  1.60 GiB, `a6fe70f2` 1.47 GiB, `7ca19c43`, `e92f04ad`, `18d59e3e`, `e6f03caf`, `1893101c`, `cf18faac`),
  no S4a line appears in the last 20 daily logs. Their held reason is **UNOBSERVED**: the run page
  (`GET .../worktree-residue/runs/{id}`) refuses a child token (403 `caller_scope`), and the preview is
  operator-only. `a7b2b64a-rc` has a name that is not `card-task-<id8>`, so S4a's leaf rule excludes it.
  `a7b2b64a-rc` and `c490e425` also fail `git status` (exit 128).
- All 37 have a clean `git status --porcelain`, except the two that exit 128.

## What the numbers say for the plan

- D-5 `SlotBytesEstimateGiB=3` covers one built `Antiphon.Tests` tree (2.28 GiB). It does not cover a tree
  that also has `client\node_modules`: `c490e425` alone is 1.21 GiB. Six slots at 3 GiB is 18 GiB, under
  the 32 GiB budget. C: has 337 GiB free.
- Warm pays: 404 s cold against 16 s for a no-op rebuild. A fresh add costs only 8.5 s, so almost all of
  the cold cost is the build, not git. For S4's success test, the dispatch-to-cwd baseline is 8.5 s for
  the add alone, and the build baseline is 404 s.
- D-9 is confirmed. `core.longpaths` is unset on `C:\src\Antiphon`, globally, and on the newest release
  checkout, so the `-c core.longpaths=true` override is the only gate.
- Defender real-time and on-access protection are on, with tamper protection, and the exclusion list could
  not be read. The plan's lock/quarantine handling for `git clean` is needed.

## Commands run (in `pwsh`, from the task worktree)

```powershell
# plan S1 block
$lock = 'C:\Antiphon\verification\native-run.lock'
'lock=' + (Test-Path -LiteralPath $lock)
'phone-home=' + (Test-Path -LiteralPath 'C:\Antiphon\runner\phone-home.json')
'longpaths-src=' + (git -C C:\src\Antiphon config --local --get core.longpaths)
git -C C:\src\Antiphon config --get core.longpaths; git config --global --get core.longpaths; git config --system --get core.longpaths
Get-ChildItem -LiteralPath 'C:\Antiphon\worktrees' -Directory | Group-Object { if ($_.Name -like 'card-task-*') {'card-task'} elseif ($_.Name -like 'ws-*') {'ws'} else {'other'} } | Select-Object Name, Count
Get-PSDrive -PSProvider FileSystem | ForEach-Object { "{0} used={1} free={2}" -f $_.Name, $_.Used, $_.Free }

# release checkouts, newest 3 by LastWriteTimeUtc
Get-ChildItem C:\Antiphon\releases -Directory -Filter 'checkout-*' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 3 |
  ForEach-Object { git -C $_.FullName config --local --get core.longpaths; git -C $_.FullName config --get core.longpaths }

# per-tree flags and full size walk
Get-ChildItem C:\Antiphon\worktrees -Directory -Filter 'card-task-*' | ForEach-Object {
  Test-Path (Join-Path $_.FullName 'tests\Antiphon.Tests\bin'); Test-Path (Join-Path $_.FullName 'client\node_modules')
  Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum }

# task state (local server, task token header only)
Invoke-RestMethod "http://localhost:17202/api/agent-tasks?status=Dispatched,Working,Blocked,Queued&since=2026-09-01T00:00:00Z"
Invoke-RestMethod "http://localhost:17202/api/agent-tasks?status=Succeeded,Failed,Canceled&since=2026-09-01T00:00:00Z"
# trees terminal >= 48h: git -C <tree> status --porcelain ; git -C <tree> rev-list --count '@{u}..HEAD'
# S4a: Select-String C:\src\Antiphon\server\logs\antiphon-2026*.log 'Desktop legacy release'

# Defender, broker
Get-MpComputerStatus; Get-MpPreference
Invoke-RestMethod http://localhost:17204/build-slots

# timing (lock re-checked absent first)
New-Item -ItemType Directory C:\Antiphon\slots-census-tmp
git -C C:\Antiphon\worktrees\card-task-2086875f worktree add --detach C:\Antiphon\slots-census-tmp\wt 0822e8bbc60f2c8f333504d60cf3484cc7b6f2b8
# cwd C:\Antiphon\slots-census-tmp\wt
pwsh -NoProfile -File C:\Antiphon\worktrees\card-task-2086875f\scripts\build-slot.ps1 -Label s1-census-cold-build -- dotnet build tests/Antiphon.Tests
pwsh -NoProfile -File C:\Antiphon\worktrees\card-task-2086875f\scripts\build-slot.ps1 -Label s1-census-warm-build -- dotnet build tests/Antiphon.Tests --no-restore
git -C C:\Antiphon\worktrees\card-task-2086875f worktree remove --force C:\Antiphon\slots-census-tmp\wt
cmd /c rd /s /q C:\Antiphon\slots-census-tmp   # verified absent; worktree list has no entry
```

Build-slot leases: cold `3a965cc8-3bb4-4bdc-8a9b-ddc989ac27c5` (held 403 s), warm
`f300a00f-70b3-4beb-bb76-709460f3e7ba` (waited 0 s, held 15 s), maxcpucount 4.

## Open for decide

1. Accept `SlotBytesEstimateGiB=3` as measured (2.28 GiB built), or raise it to 4 for trees with a client
   build.
2. Have the operator run the S4a preview (operator token) to get the held reasons for the 10 unexplained
   desktop trees (about 12.7 GiB). This plan does not change S4a (D-11).
3. A real warm-lease rebuild number has to come from S4. This census only has the no-op floor.
