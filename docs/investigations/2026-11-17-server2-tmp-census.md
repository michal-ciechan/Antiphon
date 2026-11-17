# server2 /tmp census and CARD-1677 S2 temp sweep enable plan (2026-11-17)

Task 5390dd7c, Investigate, READ-ONLY. Operator ask 2026-11-17 "Can we clean up old worktrees on server2 and desktop", step 2.
Follows [the worktree inventory](2026-11-17-server2-worktree-inventory.md) (task 3283ed4f, `e6a0f53ff45abfc294604800e48634a358157991`).
Nothing was deleted, moved, pruned, restarted or reconfigured. Master read: `29ba0c5002e642c4f4f91805be7e17660a40906d`
(`git fetch origin master` 19:33Z; equal to this branch's base and to the server's `/api/version`). Observations 19:34Z-20:30Z UTC
from inside the runner container (hostname `d0d4c9a390d9`, uid 1654 `app`; container up since 2026-11-16 13:58Z, runner build
`9b98828eb34eb552ce62bcde5a64dd045a872a88`, which contains CARD-1677 S2; `RunnerTempSweep.cs`, `WorkspaceSlotSeams.cs`,
`RunnerWorktreeRetentionService.cs` and `RunnerWorktreeRetentionSettings.cs` are byte-identical between that build and master).

## Outcome

- `/tmp` is **185.9 GiB** (one `du -x --max-depth=1` pass, 194,932,004 KiB; the inventory's "190.3 GiB" was 190,349 MiB, the same
  amount). 148,104 top-level entries: 50,709 directories (48,222 older than 24 h by mtime), 60,451 FIFOs, 32,522 sockets, 4,422 files.
- Where the bytes are:

| Bucket | Entries | GiB | In the sanctioned sweep? |
|---|---:|---:|---|
| 78 registered git worktrees (69 top-level names + 5 inside `claude-1654` + 4 nested pairs) | 78 | 102.40 (per-tree `du`) | **never** (names are unregistered; see hazard below) |
| `/tmp/claude-1654` Claude CLI scratchpads | 1,046 children | 28.05 | only family `claude-scratch` (flag off) |
| the nine registered families (`c487-probe`, `c502-exit-cap`, `land-verify`, ...) | 12,977 | 17.84 (file bytes) / 18.38 (disk) | **yes**: the whole reclaim the sweep can make today |
| everything else (unregistered names, not a worktree) | 135,057 | 42.22 (31.33 older than 72 h) | never; reported as `unregistered` rows |

- The preview run `6d157a4a-70fe-4a79-9577-5f0d41f7fe41` has **no RunnerTemp rows**: 136 rows = 70 RunnerMirror + 16 RunnerInventory, 0 RunnerTemp,
  because `RunnerWorktreeRetention:TempSweep:Enabled=false` makes `SweepTempAsync` return before it sends anything
  (`server/Application/Services/RunnerWorktreeRetentionService.cs:618-620`).
- A DryRun of the shipped sweep, simulated read-only here with the runner's own rules, **would remove 11,718 entries, 17.56 GiB at
  `MinAgeHours=72`** (12,014 at the shipped 48 h); every one is uid 1654, symlink-free, special-file-free, under 1 GiB and 100,000 entries,
  and has no holder. At the runner's hard ceiling of 200 entries per tick that is 59 ticks, about **30 h** at the 30-minute cron.
- **Enabling the sweep in DryRun is harmless to the filesystem but not to the database.** Every tick with `TempSweep:Enabled=true`
  writes one `unregistered` row per normalized prefix. The runner's `Normalize` folds only hex runs of 8+ and digit runs of 5+, so the
  21,579 random six-character directories and the 21k `clr-debug-pipe-*` pairs are not folded: the simulated census is **40,043
  unregistered rows per tick** (plus up to 200 `would_remove` + 500 kept rows), about 1.9 million Held rows a day at `*/30`, kept 30 days
  (`Retention:WorktreeResidueHeldRowRetentionDays=30`). The run's census is 50 rows per page, about 800 pages.
- **Hazard for `claude-scratch`:** the sweep never checks `git worktree` registration. Five registered worktrees live inside
  `/tmp/claude-1654/-work-worktrees-task-{1c23b333,5b164f58,6379c50f}/...`, whose mirrors are gone. Two of those entries are over the 1 GiB
  inventory limit and would be kept, but `task-6379c50f` (606 MiB, 31,171 entries, newest write 349 h ago) is under every limit: with
  `Families:claude-scratch=true` the sweep would delete three registered worktrees (`cut`, `w-135865831`, `w-de27ce2fd`; all clean and contained
  in live origin, so no commit is lost, but three admin dirs under `/work/repos/antiphon/.git/worktrees` become prunable). Keep that flag off
  until those three are removed with `git worktree remove`.

## Method (all read-only)

| Fact | Command / source |
|---|---|
| Top-level listing | `find /tmp -mindepth 1 -maxdepth 1 -printf '%y\t%U\t%T@\t%s\t%f\n'` (type, owner, mtime, size) |
| Sizes | `du -x --block-size=1K --max-depth=1 /tmp` (80 unreadable subdirs, all `/tmp/c1609rd*-host/*` owned by another uid) and `du -sx` per worktree |
| Families | names folded by hex runs (8+), digit runs (5+) and six-character random names; grouped by type |
| Sweep simulation | node `lstat` walk of each registered-family entry with the runner's rules: owner 1654 for every descendant, no symlink at the root, no socket/FIFO/device, 100,000 entries, 1 GiB of regular-file bytes, age of the newest regular-file write (`src/Antiphon.SessionRunner/RunnerTempSweep.cs:142-330, 390-470`) |
| Unregistered rows | the runner's deny list and `Normalize` (`RunnerTempSweep.cs:100-108, 359-369`) applied to every top-level name and every `claude-1654/<child>` |
| Holders | every `/proc/<pid>/cwd` and `/proc/<pid>/fd/*` in the container (102 processes; 4 root processes unreadable: docker-init, the entrypoint, dockerd, containerd) |
| Worktrees | `git -C /work/repos/antiphon worktree list --porcelain`; per tree `rev-parse HEAD`, `status --porcelain` with `GIT_OPTIONAL_LOCKS=0`, `stat` birth/mtime of the tree and of `<gitdir>/HEAD` |
| Pushed | one `git ls-remote origin 'refs/heads/*'` at 19:35Z (6,780 refs; 5,353 distinct tip commits present locally); per tree `git rev-list --count HEAD --not <those 5,353>` |
| Creator | earliest Claude/Codex transcript under `/state/claude/projects` and `/state/codex/sessions` naming the path (`grep -F`); the session cwd gives the task; task facts from `scripts/delegate.ps1 -Status <8hex>` |
| Preview run | `GET /api/agent-tasks/worktree-residue/runs/6d157a4a-...?page=n`: **pages are 0-based**; pages 0, 1, 2 hold 50 + 50 + 36 = 136 rows (136 distinct ids), page 3 is empty. `?page=1..3` alone misses the first 50 |
| Nested Docker | `docker ps -a`, `docker system df` against the container's own dind daemon |

Side effects, metadata only: two `git status` calls without `GIT_OPTIONAL_LOCKS=0` (on `/tmp/c1910-merged` and
`/tmp/antiphon-review-c0b3cb9c/master-evaluator-card1877`) may have refreshed their index stat cache. The scratch files of this task
live in its own Claude scratchpad under `/tmp/claude-1654/-work-worktrees-ws-01-d4/`.

## 1. /tmp by family

Largest families by bytes (disk KiB from `du`; age by top-level mtime):

| Family (folded name) | Type | Count | GiB | > 24 h | > 72 h | > 7 d | Sweep |
|---|---|---:|---:|---:|---:|---:|---|
| `claude-1654` (CLI scratchpads) | d | 1 (1,046 children) | 28.05 | - | - | - | `claude-scratch` (off), see section 4 |
| 69 top-level names holding registered worktrees (`c1877-comp-a`, `card-*-review-compose`, `review-<hex>-master`, ...) | d | 69 | 97.22 | 66 | - | - | never |
| `c487-probe-<hex32>` | d | 175 | 8.07 | 175 | 175 | 162 | registered |
| `rhp-<hex32>` | d | 11 | 7.27 | 11 | 11 | 0 | unregistered |
| `antiphon-review-<hex8>` (4 of 12 hold worktrees) | d | 12 | 7.18 | 12 | 12 | 1 | unregistered |
| `c502-exit-cap-<hex32>` | d | 72 | 4.41 | 66 | 66 | 24 | registered |
| `antiphon-pwsh-cache-session-<hex>-<hex>` | d | 77 | 3.58 | 16 | 2 | 0 | unregistered |
| `antiphon-land-verify-<hex32>` | d | 324 | 2.20 | 324 | 323 | 226 | registered |
| `antiphon-pwsh-cache-<n>-<hex>` | d | 48 | 1.83 | 8 | 1 | 0 | unregistered |
| `rh-<hex32>` | d | 14 | 1.73 | 14 | 14 | 0 | unregistered |
| `task-08e79898-stream-*` (3) | d | 3 | 5.19 | 3 | 3 | 0 | unregistered |
| `antiphon-c514-pty-<hex32>` | d | 43 | 1.21 | 43 | 42 | 24 | registered |
| `c1514-<hex32>` | d | 1,012 | 0.44 | 1,011 | 797 | 350 | registered |
| `antiphon-c448-<hex32>` | d | 2,005 | 0.34 | 2,000 | 1,897 | 687 | unregistered |
| `c487-<hex32>` | d | 230 | 0.28 | 225 | 225 | 202 | registered |

Largest families by count:

| Family | Type | Count | GiB | > 24 h | > 72 h | > 7 d | Producer |
|---|---|---:|---:|---:|---:|---:|---|
| `<rnd6>` (e.g. `gGyQ8w`) | d | 21,579 | 0.08 | 20,347 | 19,579 | 15,683 | **UNOBSERVED** (below) |
| `clr-debug-pipe-<pid>-<n>-out` / `-in` | FIFO | 21,260 / 21,259 | 0 | 20,474 | 19,808 | 13,552 | .NET runtime debugger transport, one pair per .NET process; left when the process is killed |
| `dotnet-diagnostic-<pid>-<n>-socket` | socket | 21,240 | 0 | 20,454 | 19,789 | 13,540 | .NET diagnostics IPC, same origin; denied by the sweep |
| `antiphon-bridge-queue-<hex32>` | d | 10,146 | 0.09 | 9,399 | 9,115 | 2,202 | `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` (registered) |
| `c459-<hex8>` | d | 2,715 | 0.02 | 2,715 | 2,715 | 2,527 | `tests/Antiphon.Tests/Infrastructure/SettledWorktreeRemovalTests.cs`, `Application/WorkspaceReservationKeyTests.cs`, `Application/TaskWorktreeRetirementTests.cs` |
| `c487-tw-<hex32>` | d | 2,631 | 0.03 | 2,631 | 2,631 | 2,619 | `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs` |
| `antiphon-c448-<hex32>` | d | 2,005 | 0.34 | 2,000 | 1,897 | 687 | `tests/Antiphon.Tests/Application/AgentTaskLocalMergeSafetyTests.cs`, `AgentTaskLandRegistrationSafetyWindowsTests.cs` |
| `c487-reg-<hex32>.txt` | f | 1,761 | 0.01 | 1,761 | 1,761 | 1,744 | `TestClassificationPolicyTests.cs` family |
| `antiphon-c1150-<hex32>` | d | 1,099 | 0.01 | 1,057 | 1,045 | 468 | `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.BriefIdentity.cs` |
| `c590-<hex32>`, `c716-<hex32>` | d | 670, 669 | 0.02 | 1,339 | 1,236 | 597 | test fixtures (not traced) |
| `MSBuildTemp<rnd6>` | d | 604 | 0.01 | 560 | 526 | 423 | MSBuild |
| `antiphon-remote-prep-ws<rnd6>` | d | 602 | 0.00 | 602 | 602 | 602 | `tests/Antiphon.Tests/Application/RemoteWorkspacePreparerTests.cs` |

Age buckets over all entries (top-level mtime): 0-24 h 5,915 entries (2,487 dirs) 37.80 GiB; 24-72 h 5,137 (1,850 dirs) 26.66 GiB;
72-168 h 43,904 (16,209 dirs) 106.26 GiB; older than 7 d 93,148 (30,163 dirs) 15.17 GiB.

Holders: the only `/tmp` paths any readable process holds are `/tmp/antiphon-pty-hosts/bin/20261116-135810-fe92a183/*.dll` (85 processes;
denied by the sweep) and two live Claude scratchpads (`claude-1654/-work-worktrees-ws-01-d4` = this task, `-ws-05`). **No registered
worktree and no other top-level entry has a holder.** No nested container is running (`docker ps -a`: two exited, 10 days and 2 weeks).
The four root processes are unreadable, as they are to the runner's own scan (`WorkspaceSlotSeams.cs:57-58`: another uid is skipped, not
incomplete). Holders in other containers that mount `antiphon-runner_runner-tmp`, or on the host, are **UNOBSERVED** (`ssh mc@server2`:
`Permission denied (publickey)`).

## 2. The 48,209 small directories: what and why they stay

Top three producers of directories older than 24 h:

1. **By count: `<rnd6>` empty directories, 20,347 older than 24 h (0.08 GiB).** 21,572 of 21,579 are empty (one entry with `find`); the other seven
   hold a `project.assets.json`, a `Program.cs` or a JSON file. The shape is `mkdtemp("/tmp/XXXXXX")`, which is what .NET
   `Directory.CreateTempSubdirectory()` with no prefix creates. The only two repository calls of that form
   (`tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs:1484`, `AgentTaskLandStageOutcomeTests.cs:836`) delete the directory in the
   constructor, so **the producer is UNOBSERVED**. Rate: 425-2,008 a day since 2026-10-30, so the volume is from repeated test or tool runs.
   Resolve: watch `/tmp` for `IN_CREATE` of a six-character name during one Unit-lane shard (or `strace -f -e mkdir` of one
   `dotnet run --project tests/Antiphon.Tests` shard) and read the creating command line.
2. **By count: `antiphon-bridge-queue-<hex32>`, 9,399 older than 24 h (0.09 GiB)**, `BridgeQueueHarness.cs`. A registered family: the sweep
   would take them.
3. **By count: `c459-<hex8>` 2,715 and `c487-tw-<hex32>` 2,631**, from the c459 worktree/retirement tests and `TestClassificationPolicyTests.cs`.
   Unregistered, never touched.

By size among the small directories: `c487-probe` 8.07 GiB (175, about 46 MiB each), `c502-exit-cap` 4.41 GiB, `antiphon-pwsh-cache-session-*`
3.58 GiB, `antiphon-land-verify` 2.20 GiB.

Why they stay: nothing deletes `/tmp` on this host. The runner container's `/tmp` is the named volume `antiphon-runner_runner-tmp`
(previous note), so a container recreate does not clear it; test helpers whose process is killed (timeouts, build-slot kills, container
restarts) never run their `Dispose`; and the sanctioned sweep is switched off. The 21k `clr-debug-pipe`/`dotnet-diagnostic` triples are
the same signal: each is one .NET process that did not exit normally.

## 3. The preview run 6d157a4a and what the temp sweep would remove

Run `6d157a4a-70fe-4a79-9577-5f0d41f7fe41`: started 19:30:53Z, finished 19:30:54Z, `execute=false`, `preview=true`, 136 rows, 120 held candidates.

| Lane | Reason | Rows |
|---|---|---:|
| RunnerMirror | `settling` (inside `MinTerminalHours`) | 54 |
| RunnerMirror | `mutation_excluded` | 27 |
| RunnerMirror | `sync_debt` | 23 |
| RunnerMirror | `kept_recheck_pending` | 9 |
| RunnerMirror | `outside_scope` | 5 |
| RunnerMirror | `live_owner` (`ws-05`, `ws-02-d2`) | 2 |
| RunnerInventory | `identity_unknown` (the 12 class-D trees, `c0824-compose-out`, `c1886-s1-integrated`, `task-63cdf1df-land`, `.antiphon-test-evidence`) | 16 |
| RunnerTemp | - | **0** |

This answers one UNOBSERVED of the inventory: the five A1 mirrors (`2b953d03`, `0ddd898d`, `f4320f2f`, `741fb04b`, `91085723`) are
`kept_recheck_pending`: an earlier tick kept them and the back-off has not expired; their first kept reason is in an earlier run (UNOBSERVED here).

What enables the temp census: `RunnerWorktreeRetention:TempSweep:Enabled=true` (env form `RunnerWorktreeRetention__TempSweep__Enabled=true`)
on the **server**. With `TempSweep:Execute=false` every tick, and the preview route always, sends `Mode=DryRun`; the runner's DryRun branch
only appends `would_remove` and never calls `DeleteAsync` (`RunnerTempSweep.cs:324-329`). The operator one-shot execute also keeps the temp
sweep DryRun (`writeAll` → `preview: true`, `RunnerWorktreeRetentionService.cs:598, 638`).

Simulated DryRun with the shipped table (nothing refused by the walk; all owner 1654, no symlink root, no special file):

| Family | Entries | GiB | clean ≥ 48 h | GiB | clean ≥ 72 h | GiB | ≥ 7 d | GiB |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `c487-probe` | 175 | 7.97 | 175 | 7.97 | 175 | 7.97 | 162 | 7.37 |
| `c502-exit-cap` | 72 | 4.35 | 66 | 4.25 | 66 | 4.25 | 24 | 0.40 |
| `land-verify` | 324 | 2.17 | 324 | 2.17 | 323 | 2.17 | 226 | 1.09 |
| `pty-test-root` | 291 | 1.61 | 291 | 1.61 | 291 | 1.61 | 203 | 0.97 |
| `c514-pty` | 43 | 1.19 | 43 | 1.19 | 42 | 1.17 | 24 | 0.35 |
| `c1514` | 1,012 | 0.38 | 807 | 0.22 | 797 | 0.22 | 351 | 0.07 |
| `c487` | 230 | 0.15 | 225 | 0.15 | 225 | 0.15 | 202 | 0.15 |
| `rcspeed` (dirs + `.ps1`) | 684 | 0.03 | 684 | 0.03 | 684 | 0.03 | 588 | 0.02 |
| `bridge-queue` | 10,146 | 0.00 | 9,399 | 0.00 | 9,115 | 0.00 | 2,276 | 0.00 |
| **Total** | **12,977** | **17.84** | **12,014** | | **11,718** | **17.56** | | |

No registered worktree is under a registered top-level family name (checked all 78 paths). Ordering is oldest-first by newest write, so the
first ticks take mostly old `bridge-queue`, `c487`, `rcspeed` and `c1514` entries (tiny); the 8 GiB of `c487-probe` arrives as their age comes up.

## 4. Safe enable plan

**Settings** (server, section `RunnerWorktreeRetention:TempSweep`, `server/Application/Settings/RunnerWorktreeRetentionSettings.cs:84-94`,
shipped in `server/appsettings.json:263-270`): `Enabled` (false), `Execute` (false), `MinAgeHours` (48, validator ≥ 1), `MaxEntriesPerTick`
(200, validator 1-1000, but the runner clamps to 200: `TempSweepLimits.MaxEntries`), `MaxBytesPerTick` (20 GiB, runner clamp 20 GiB),
`Families:<id>` (only `claude-scratch=false` shipped; every other family defaults on). The sweep also needs `RunnerWorktreeRetention:Enabled=true`
and `Runners` to list `server2` (both shipped on).

**Where it lives and how it applies:** server only. The runner needs nothing: it advertises `tempSweepV1` on the phone-home descriptor whenever
phone-home is on under Linux (`PhoneHomeRuntimeAdapter.cs:108-109`, `Program.cs:88-98`), and build `9b98828e` has it. So **no runner recreate and
no held rollout**. The server reads `IOptionsMonitor<...>.CurrentValue` at the start of every tick and again right before the temp send
(`RunnerWorktreeRetentionService.cs:369, 618-622`), so:
- desktop AppHost server: edit `server/appsettings.Development.json` (hot reload, no restart), provided no `RunnerWorktreeRetention__TempSweep__*`
  environment variable shadows it (docs/docker-stack.md:1082 states the same rule for `Delegation`);
- server2 server container (`antiphon-server2-server`): it has no reloadable settings file; the env var goes through its compose environment and needs a
  server container recreate (`scripts/deploy-server2-server.ps1`), not a runner change.
Which process is the live production server, and therefore which one runs the scheduled tick, is **UNOBSERVED** from the runner
(`PhoneHome__ServerOrigin` and `ANTIPHON_API` are both `https://antiphon.desktop.codeperf.net`; `/api/version` = `29ba0c50`). The tick holds a
database-wide advisory lock (CARD-1804), so a second server process with the sweep off just ticks without sweeping.

**First-run bound:** at most 200 entries and 20 GiB per tick, a 10-minute runner time budget and a 15-minute server budget; a 30-minute cron.
Entry gates: registered family, not denied, family flag on, root not a symlink, owner 1654 for every descendant, ≤ 100,000 entries and ≤ 1 GiB,
newest regular-file write older than `MinAgeHours`, a complete holder scan with no holder (re-checked per entry right before delete), no-follow
delete. Families: `c487-probe`, `c487`, `pty-test-root`, `c514-pty`, `c502-exit-cap`, `c1514`, `land-verify`, `bridge-queue`, `rcspeed`
(+ `claude-scratch` behind its own flag and mirror-absent check).

**Rollback / kill switch:** `TempSweep:Enabled=false` (stops sending at the next re-read, including mid-tick); `TempSweep:Execute=false` for back to
dry run; `RunnerWorktreeRetention:Enabled=false` stops everything. A deleted entry is not recoverable.

**What could go wrong:**
- DB growth (measured above): ~40k Held `unregistered` rows per tick while `Enabled=true`, DryRun or Execute. Over the ~59 ticks needed to drain
  the registered families that is about 2.4 million rows, pruned after 30 days. Reading one run's census is ~800 pages of 50.
- A live test using `/tmp`: only an entry whose newest regular-file write is older than `MinAgeHours` goes, and a holder blocks it; a test that
  keeps a directory open without writing for 72 h and holds no fd/cwd is the residual case. At 72 h no current run qualifies (the oldest live
  .NET process here started 30 h ago).
- A holder in another PID namespace or uid (host processes, another container mounting `runner-tmp`, root-owned dind fixtures): invisible to the
  scan, which skips other uids. All registered-family entries are owned by 1654 and created by tests run as 1654.
- Build-slot temp and checkpoint tool temp: not registered families (`build-slots` is also hard-denied), so never touched. The checkpoint tool
  writes under the task's own tree and the scratchpad, not a registered `/tmp` name (not traced further).
- `claude-scratch`: the registered-worktree hazard in the Outcome; and a resumed task whose mirror was retired would lose its old scratchpad.

**Recommendation (operator's to decide):**
1. Accept the row cost for a short census only: just after a scheduled tick (:00/:30 UTC), set on the ticking server
   `"TempSweep": { "Enabled": true, "Execute": false, "MinAgeHours": 72, "MaxEntriesPerTick": 200, "MaxBytesPerTick": 21474836480, "Families": { "claude-scratch": false } }`,
   then `POST /api/agent-tasks/worktree-residue/runner-retention/server2/preview` (operator token, generous `--max-time`), and set `Enabled=false`
   again before the next :00/:30. Read the run (pages from 0) and check: RunnerTemp `would_remove` rows ≈ 200 (the bound), kept reasons only
   `tick_bound`/`too_young`/`family_disabled`, no `temp_scan_incomplete`, an `unregistered` row count near 40k, and bytes matching section 3.
2. Then `Enabled=true`, `Execute=true`, `MinAgeHours=72`. After the first Execute tick verify: the tick log line `Runner temp sweep server2 mode
   Execute: candidates ... removed <=200 (... bytes)`; `removed` rows only for registered families; `df -B1G /tmp` drop; no failure in the
   test lanes that own those families; no holder-related `live_process` surprise. Leave it on for ~30 h, then decide whether to keep it on
   (steady ~1.9 M rows/day) or turn it off and re-run monthly.
3. Keep `Families:claude-scratch=false` until the three `task-6379c50f` scratch worktrees are removed (section 5) and the 1 GiB limit is
   accepted as the only guard for the rest; with it on, 880 mirror-gone scratchpads (11.07 GiB) are under 1 GiB, 6 (16.17 GiB) are over and stay.

Fix idea (not designed): fold six-character random names and `clr-debug-pipe`/`dotnet-diagnostic` names in `Normalize`, or cap `unregistered` rows per tick.

## 5. Class D, class B and the /tmp worktrees: literal command lists (NOT run)

There is no sanctioned tool for any of these: the retention job only acts on `/work/worktrees/task-<8hex>` and reports the rest as
`identity_unknown`. The safe form is `git worktree remove` WITHOUT `--force` (it refuses a tree with modified or untracked non-ignored files,
and ignored `bin/obj` go with it), run inside `antiphon-runner-session-runner-1` as uid 1654, one path at a time, after re-checking right before
each one: `git -C <path> status --porcelain` empty, `git -C <path> rev-parse HEAD` unchanged, and no `/proc/*/cwd|fd` under the path. Never
`rm`, never `git worktree prune`, never `--force`.

### Class D (12 trees, 25.00 GiB, `/work/worktrees`; clean, HEAD in a live origin ref per the inventory; `task-3e32f1e6-s2` is inside the window, last)

```
git -C /work/repos/antiphon worktree remove /work/worktrees/task-8e452527-checks
git -C /work/repos/antiphon worktree remove /work/worktrees/task-71aaddba-measure
git -C /work/repos/antiphon worktree remove /work/worktrees/task-8e452527-repair
git -C /work/repos/antiphon worktree remove /work/worktrees/task-37cb8e72-master
git -C /work/repos/antiphon worktree remove /work/worktrees/task-15406ecc-coverage2
git -C /work/repos/antiphon worktree remove /work/worktrees/task-ac7f437c-base
git -C /work/repos/antiphon worktree remove /work/worktrees/task-37cb8e72-s1base
git -C /work/repos/antiphon worktree remove /work/worktrees/task-37cb8e72-s1tip
git -C /work/repos/antiphon worktree remove /work/worktrees/task-8e452527-base
git -C /work/repos/antiphon worktree remove /work/worktrees/task-0a2ad036-base
git -C /work/repos/antiphon worktree remove /work/worktrees/task-71aaddba-messaging-measure
git -C /work/repos/antiphon worktree remove /work/worktrees/task-3e32f1e6-s2
```

### Class B (5 trees, 3.37 GiB): salvage push, then remove

```
git -C /work/worktrees/c0824-compose-out push origin HEAD:refs/heads/salvage/c0824-compose-out
git -C /work/repos/antiphon worktree remove /work/worktrees/c0824-compose-out
git -C /work/worktrees/c1886-s1-integrated push origin HEAD:refs/heads/salvage/c1886-s1-integrated
git -C /work/repos/antiphon worktree remove /work/worktrees/c1886-s1-integrated
git -C /work/worktrees/task-63cdf1df-land push origin HEAD:refs/heads/salvage/task-63cdf1df-land
git -C /work/repos/antiphon worktree remove /work/worktrees/task-63cdf1df-land
# already salvaged (HEAD = live refs/heads/salvage/stale-merge-34125850-reports); remove only after confirming that ref with ls-remote:
git -C /work/repos/antiphon worktree remove /work/worktrees/task-34125850
# operator decision: local branch merge184 is not on origin but adds 0 commits outside origin refs:
git -C /work/repos/antiphon worktree remove /work/worktrees/task-9d003ac4
```

After a salvage push, confirm it with `git ls-remote origin refs/heads/salvage/<name>` equal to the tree's HEAD before the remove.

### The 78 /tmp worktrees (102.40 GiB)

All 78 are registered, detached except four (`scratch/c1877-comp-a`, `scratch/c1877-comp-b`, `review-compose-1915`, `scratch/card-1943-review`),
none locked, none prunable, **none held**. Every creating task found is terminal (Succeeded). Split:

| Group | Trees | GiB | Action |
|---|---:|---:|---|
| clean and HEAD contained in a live origin ref (0 commits outside) | 30 | 37.50 | `git worktree remove` (list below) |
| commits not on any live origin ref (upper bound: 1,427 live tip SHAs are not present locally) | 42 | 60.49 | salvage push, confirm, then remove (list below); most are review/compose merges whose commits are local merge or rebase products |
| dirty with no unpushed commit | 6 | 4.40 | keep; staged or modified files are real work (e.g. `/tmp/c1910-merged` stages three CARD-1910 docs) |

Creator attribution: the earliest transcript naming the path. Where that transcript starts after the tree's birth, the creator is
UNOBSERVED (the creating transcript is not on `/state`) and the table names the earliest mention only. The five
`/tmp/claude-1654/-work-worktrees-task-<id>/...` trees belong by path to tasks `1c23b333`, `5b164f58` and `6379c50f` (mirrors gone).

| Path | GiB | Born (UTC) | HEAD age h | Branch | HEAD | HEAD = live tip | Commits not on live origin | Porcelain all/tracked | Creator (transcript) | Creator task | Group |
|---|---:|---|---:|---|---|---|---:|---|---|---|---|
| `/tmp/c1877-comp-a` | 4.56 | 2026-11-13T11:28 | 104 | scratch/c1877-comp-a | `dc590eb63f` | no | 5 | 0/0 | UNOBSERVED (first mention task-1fa64d10 2026-11-13T11:42) | - | B-tmp unpushed |
| `/tmp/c1877-comp-b` | 4.56 | 2026-11-13T11:28 | 104 | scratch/c1877-comp-b | `ea56909477` | no | 6 | 0/0 | UNOBSERVED (first mention task-d1a9978d 2026-11-13T12:33) | - | B-tmp unpushed |
| `/tmp/card-1915-review-compose` | 3.71 | 2026-11-14T13:14 | 79 | review-compose-1915 | `f14600eb07` | no | 1 | 0/0 | task-d684bc25 | Succeeded CARD-0824 Worker/Code | B-tmp unpushed |
| `/tmp/card-1943-review-compose` | 3.33 | 2026-11-15T06:51 | 61 | scratch/card-1943-review | `265b9dff16` | no | 9 | 0/0 | UNOBSERVED (first mention ws-03-d2 2026-11-17T19:02) | - | B-tmp unpushed |
| `/tmp/c1915-compose` | 2.69 | 2026-11-14T19:20 | 72 | (detached) | `7ee4a6c478` | no | 29 | 1/1 | UNOBSERVED (first mention ws-03-d2 2026-11-17T19:02) | - | B-tmp unpushed |
| `/tmp/card1862-s1-b4ab4c34` | 2.63 | 2026-11-13T01:48 | 114 | (detached) | `b4ab4c34ea` | yes | 0 | 0/0 | task-371692c7 | Succeeded CARD-1862 Worker/Debug | clean+contained |
| `/tmp/review-01e2734e-master2` | 2.41 | 2026-11-13T15:17 | 99 | (detached) | `61485745e0` | no | 12 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/c1514-r2-master` | 2.41 | 2026-11-13T15:56 | 100 | (detached) | `bb04ca147d` | no | 7 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/card-1875-master-scratch` | 2.39 | 2026-11-13T09:04 | 107 | (detached) | `d341b96f18` | no | 3 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/card-1850-s2-review-scratch` | 2.35 | 2026-11-13T10:22 | 105 | (detached) | `fd4bcd3ef9` | no | 8 | 0/0 | UNOBSERVED (first mention task-66a1f6d2 2026-11-13T12:13) | - | B-tmp unpushed |
| `/tmp/claude-1654/…/-work-worktrees-task-5b164f58/…/scratchpad/mastertree` | 2.33 | 2026-11-13T07:50 | 108 | (detached) | `156cae9627` | no | 5 | 1/0 | UNOBSERVED (first mention UNOBSERVED ) | - | B-tmp unpushed |
| `/tmp/s5c-review-d5ecb5af/merge` | 2.32 | 2026-11-16T22:43 | 21 | (detached) | `e9c7c80e10` | no | 1 | 0/0 | UNOBSERVED (first mention ws-01-d4 2026-11-17T19:33) | - | B-tmp unpushed |
| `/tmp/card-1884-review-compose` | 2.32 | 2026-11-15T07:20 | 60 | (detached) | `3521981e6a` | no | 3 | 0/0 | UNOBSERVED (first mention ws-03-d2 2026-11-17T19:02) | - | B-tmp unpushed |
| `/tmp/c0824-compose` | 2.31 | 2026-11-14T21:27 | 70 | (detached) | `353d5cecb0` | no | 35 | 0/0 | UNOBSERVED (first mention ws-03-d2 2026-11-17T19:02) | - | B-tmp unpushed |
| `/tmp/review-18072e35-compose2` | 2.31 | 2026-11-14T20:36 | 71 | (detached) | `f15599e225` | no | 21 | 0/0 | UNOBSERVED (first mention task-73e7dfdd 2026-11-15T04:45) | - | B-tmp unpushed |
| `/tmp/review-575ab1a1-master` | 2.31 | 2026-11-14T07:20 | 84 | (detached) | `e3c442d860` | no | 6 | 0/0 | UNOBSERVED (first mention task-d684bc25 2026-11-14T08:56) | - | B-tmp unpushed |
| `/tmp/c1886-master-repair` | 2.31 | 2026-11-14T02:25 | 89 | (detached) | `44106d436f` | no | 10 | 0/0 | UNOBSERVED (first mention task-71aaddba 2026-11-14T03:14) | - | B-tmp unpushed |
| `/tmp/c1910-merged-now` | 2.31 | 2026-11-14T03:25 | 88 | (detached) | `1e09a851ac` | yes | 0 | 8/8 | UNOBSERVED (first mention task-d684bc25 2026-11-14T08:56) | - | dirty |
| `/tmp/claude-1654/…/-work-worktrees-task-1c23b333/…/scratchpad/onmaster` | 2.31 | 2026-11-13T07:48 | 108 | (detached) | `8303e9d36e` | no | 7 | 0/0 | UNOBSERVED (first mention UNOBSERVED ) | - | B-tmp unpushed |
| `/tmp/card-1820-s1r3e-review-master` | 2.30 | 2026-11-13T09:47 | 106 | (detached) | `7b41baa587` | no | 8 | 0/0 | UNOBSERVED (first mention task-2187186f 2026-11-13T10:32) | - | B-tmp unpushed |
| `/tmp/c1836-s4a-combined-53820e0f` | 2.30 | 2026-11-13T08:56 | 107 | (detached) | `3e3c90e4f8` | no | 26 | 1/1 | UNOBSERVED (first mention task-1180038b 2026-11-13T12:43) | - | B-tmp unpushed |
| `/tmp/card-1820-s1r6d-review-253bead91` | 2.30 | 2026-11-13T11:34 | 104 | (detached) | `745acd865a` | no | 5 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/cc5b4299-rc22` | 2.30 | 2026-11-16T04:53 | 39 | (detached) | `a71c45ab4d` | yes | 0 | 0/0 | ws-02 | - | clean+contained |
| `/tmp/card-1925-mutant` | 2.30 | 2026-11-15T04:05 | 64 | (detached) | `eb7bb5473d` | yes | 0 | 0/0 | UNOBSERVED (first mention ws-01-d4 2026-11-17T19:33) | - | clean+contained |
| `/tmp/cc5b4299-rc21` | 2.30 | 2026-11-16T04:53 | 39 | (detached) | `64e3d78c48` | yes | 0 | 0/0 | ws-02 | - | clean+contained |
| `/tmp/review-18072e35-master` | 2.29 | 2026-11-14T20:50 | 71 | (detached) | `8b5afc6ba5` | yes | 0 | 0/0 | UNOBSERVED (first mention ws-01-d4 2026-11-17T19:33) | - | clean+contained |
| `/tmp/task-15406ecc-coverage2` | 2.29 | 2026-11-14T15:28 | 76 | (detached) | `8cec1e74e7` | yes | 0 | 0/0 | task-15406ecc | Succeeded CARD-1892 Worker/Debug | clean+contained |
| `/tmp/card1884-review-2db533c9/master` | 2.29 | 2026-11-14T11:28 | 80 | (detached) | `b559bfba2e` | yes | 0 | 0/0 | task-d684bc25 | Succeeded CARD-0824 Worker/Code | clean+contained |
| `/tmp/g90-master-c1029` | 2.29 | 2026-11-14T16:58 | 75 | (detached) | `8cec1e74e7` | yes | 0 | 0/0 | UNOBSERVED (first mention task-abd9b404 2026-11-14T17:23) | - | clean+contained |
| `/tmp/card1901-e594649c-base` | 2.29 | 2026-11-14T15:44 | 76 | (detached) | `b559bfba2e` | yes | 0 | 0/0 | task-e594649c | Succeeded CARD-1901 Worker/Code | clean+contained |
| `/tmp/card-1912-base-8c06` | 2.29 | 2026-11-14T10:51 | 81 | (detached) | `8c06c7e11f` | yes | 0 | 0/0 | task-d684bc25 | Succeeded CARD-0824 Worker/Code | clean+contained |
| `/tmp/c1886-master-base` | 2.29 | 2026-11-14T02:47 | 89 | (detached) | `f805ae444d` | yes | 0 | 0/0 | UNOBSERVED (first mention task-71aaddba 2026-11-14T03:14) | - | clean+contained |
| `/tmp/antiphon-master-8592d536` | 1.94 | 2026-11-05T03:34 | 304 | (detached) | `cc8845f030` | no | 0 | 0/0 | UNOBSERVED (first mention task-f059674d 2026-11-05T04:31) | - | clean+contained |
| `/tmp/card-1850-s3a-review-ea79a8f2` | 1.36 | 2026-11-13T12:59 | 103 | (detached) | `73420536de` | no | 5 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/card-1964-red-d32418928` | 1.33 | 2026-11-17T08:53 | 11 | (detached) | `d32418928c` | no | 0 | 0/0 | UNOBSERVED (first mention ws-01-d4 2026-11-17T19:33) | - | clean+contained |
| `/tmp/antiphon-review-ff132917/base` | 1.31 | 2026-11-12T16:45 | 123 | (detached) | `8981204363` | no | 0 | 0/0 | task-ff132917 | Succeeded CARD-0747 Worker/Review | clean+contained |
| `/tmp/c2cf0b8a-review/red-d2` | 1.31 | 2026-11-12T17:56 | 122 | (detached) | `2a7687d4b0` | no | 0 | 0/0 | task-c2cf0b8a | Succeeded CARD-0747 Worker/Review | clean+contained |
| `/tmp/antiphon-review-ff132917/red-source` | 1.31 | 2026-11-12T16:49 | 123 | (detached) | `b4cb13465e` | no | 0 | 0/0 | task-ff132917 | Succeeded CARD-0747 Worker/Review | clean+contained |
| `/tmp/c2cf0b8a-review/red-d1` | 1.31 | 2026-11-12T17:56 | 122 | (detached) | `1fc57f4b21` | no | 0 | 0/0 | task-c2cf0b8a | Succeeded CARD-0747 Worker/Review | clean+contained |
| `/tmp/c1886-layering-red` | 1.14 | 2026-11-14T02:29 | 89 | (detached) | `6382939955` | yes | 0 | 1/1 | UNOBSERVED (first mention task-71aaddba 2026-11-14T03:14) | - | dirty |
| `/tmp/card1887-master-scratch` | 0.39 | 2026-11-14T00:41 | 91 | (detached) | `4f72821926` | no | 9 | 0/0 | UNOBSERVED (first mention task-ee0bb3d9 2026-11-14T02:37) | - | B-tmp unpushed |
| `/tmp/c1893-red-068308a85` | 0.38 | 2026-11-16T21:01 | 23 | (detached) | `068308a85d` | no | 0 | 0/0 | UNOBSERVED (first mention task-4d0255a8 2026-11-17T04:56) | - | clean+contained |
| `/tmp/antiphon-review-40d54591/master` | 0.37 | 2026-11-13T23:27 | 92 | (detached) | `1fde603cce` | yes | 0 | 0/0 | task-40d54591 | Succeeded CARD-1884 Worker/Review | clean+contained |
| `/tmp/card-1875-red-scratch` | 0.37 | 2026-11-13T09:08 | 107 | (detached) | `e87ad93afc` | no | 0 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | clean+contained |
| `/tmp/card-1925-compose` | 0.29 | 2026-11-15T03:57 | 64 | (detached) | `3ab96a934f` | no | 4 | 0/0 | UNOBSERVED (first mention ws-01-d4 2026-11-17T19:33) | - | B-tmp unpushed |
| `/tmp/card1887-master-now` | 0.26 | 2026-11-14T01:20 | 90 | (detached) | `7585f86ed2` | no | 9 | 0/0 | UNOBSERVED (first mention task-ee0bb3d9 2026-11-14T02:37) | - | B-tmp unpushed |
| `/tmp/card1887-master-f805` | 0.26 | 2026-11-14T01:58 | 90 | (detached) | `d8b6eba0b0` | no | 9 | 0/0 | UNOBSERVED (first mention task-ee0bb3d9 2026-11-14T02:37) | - | B-tmp unpushed |
| `/tmp/card-1886-s2-review-55233180` | 0.26 | 2026-11-13T21:44 | 94 | (detached) | `823c5c6513` | no | 1 | 0/0 | UNOBSERVED (first mention task-ee0bb3d9 2026-11-14T02:37) | - | B-tmp unpushed |
| `/tmp/c1836-s5b-review-scratch` | 0.26 | 2026-11-13T13:36 | 102 | (detached) | `3975dbe892` | no | 9 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/s5a-review-master-253bead9` | 0.25 | 2026-11-13T11:51 | 104 | (detached) | `f9ab34fc60` | no | 15 | 1/1 | UNOBSERVED (first mention task-1180038b 2026-11-13T12:43) | - | B-tmp unpushed |
| `/tmp/s5a-review-master-90ae19f6` | 0.25 | 2026-11-13T10:41 | 105 | (detached) | `8349d534dc` | no | 15 | 1/1 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/card1943-base-c1029` | 0.24 | 2026-11-15T05:45 | 62 | (detached) | `64e3d78c48` | yes | 0 | 0/0 | task-73e7dfdd | Succeeded CARD-1943 Worker/Code | clean+contained |
| `/tmp/c1893-merge-review` | 0.24 | 2026-11-16T21:02 | 23 | (detached) | `0f1f7bf489` | yes | 0 | 2/2 | UNOBSERVED (first mention ws-01-d4 2026-11-17T19:33) | - | dirty |
| `/tmp/c1947-compose-23686c59` | 0.24 | 2026-11-15T22:07 | 46 | (detached) | `d8ed9035cd` | no | 3 | 0/0 | UNOBSERVED (first mention ws-01-d4 2026-11-17T19:33) | - | B-tmp unpushed |
| `/tmp/c1947-master-23686c59` | 0.24 | 2026-11-15T22:06 | 46 | (detached) | `a71c45ab4d` | yes | 0 | 0/0 | UNOBSERVED (first mention ws-01-d4 2026-11-17T19:33) | - | clean+contained |
| `/tmp/c1906-compose-wt` | 0.24 | 2026-11-15T17:09 | 51 | (detached) | `aa812c1b74` | no | 1 | 3/3 | UNOBSERVED (first mention ws-01-d4 2026-11-17T19:33) | - | B-tmp unpushed |
| `/tmp/review-18072e35-compose` | 0.24 | 2026-11-14T20:06 | 72 | (detached) | `c9d1614791` | no | 21 | 0/0 | UNOBSERVED (first mention ws-01-d4 2026-11-17T19:33) | - | B-tmp unpushed |
| `/tmp/c1910-merged` | 0.24 | 2026-11-14T02:48 | 89 | (detached) | `cf05ed8a04` | yes | 0 | 8/8 | UNOBSERVED (first mention task-71aaddba 2026-11-14T03:14) | - | dirty |
| `/tmp/card-1886-s2-review-1fde603c` | 0.24 | 2026-11-13T22:52 | 93 | (detached) | `13af8d0fc8` | no | 8 | 0/0 | UNOBSERVED (first mention task-71aaddba 2026-11-14T03:14) | - | B-tmp unpushed |
| `/tmp/card-1885-review-fe5b7764` | 0.24 | 2026-11-13T18:19 | 96 | (detached) | `4301f316e8` | no | 19 | 0/0 | UNOBSERVED (first mention task-71aaddba 2026-11-14T03:14) | - | B-tmp unpushed |
| `/tmp/antiphon-review-40d54591/historical` | 0.24 | 2026-11-13T23:29 | 92 | (detached) | `3dc5e5c335` | no | 0 | 0/0 | task-40d54591 | Succeeded CARD-1884 Worker/Review | clean+contained |
| `/tmp/antiphon-review-b9760da0/baseline` | 0.24 | 2026-11-13T19:22 | 96 | (detached) | `a9a54e4d6e` | yes | 0 | 0/0 | task-b9760da0 | Succeeded CARD-1884 Worker/Review | clean+contained |
| `/tmp/card-1894-review-scratch` | 0.24 | 2026-11-13T17:04 | 99 | (detached) | `de586ecbdb` | no | 2 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/review-01e2734e-master` | 0.24 | 2026-11-13T14:29 | 101 | (detached) | `1531a0ee7a` | no | 11 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/c1514-index-m78442d9` | 0.24 | 2026-11-13T14:48 | 101 | (detached) | `012e29e2fe` | no | 4 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/c1836-s5b-review-scratch-3dc5e5c` | 0.24 | 2026-11-13T14:11 | 102 | (detached) | `d832a42a4c` | no | 9 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/c1514-index-master` | 0.24 | 2026-11-13T14:24 | 101 | (detached) | `a1b59d68cf` | no | 4 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/c1514-index-tip` | 0.24 | 2026-11-13T14:24 | 101 | (detached) | `1a79c5119b` | yes | 0 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | clean+contained |
| `/tmp/antiphon-review-c0b3cb9c/master-evaluator-card1877` | 0.24 | 2026-11-13T10:40 | 105 | (detached) | `90ae19f65b` | yes | 0 | 9/9 | task-c0b3cb9c | Succeeded CARD-1877 Worker/Review | dirty |
| `/tmp/c1877-master` | 0.24 | 2026-11-13T11:29 | 104 | (detached) | `253bead91b` | yes | 0 | 0/0 | UNOBSERVED (first mention task-4a404450 2026-11-13T13:09) | - | clean+contained |
| `/tmp/antiphon-review-c0b3cb9c/master-plus-card1877` | 0.24 | 2026-11-13T10:40 | 105 | (detached) | `90ae19f65b` | yes | 0 | 2/2 | task-c0b3cb9c | Succeeded CARD-1877 Worker/Review | dirty |
| `/tmp/card-1820-s1r3e-review-clean` | 0.24 | 2026-11-13T09:57 | 106 | (detached) | `7b41baa587` | no | 8 | 0/0 | UNOBSERVED (first mention task-37cb8e72 2026-11-13T17:49) | - | B-tmp unpushed |
| `/tmp/s3b-r3-master-scratch` | 0.24 | 2026-11-13T10:18 | 105 | (detached) | `f6f48d497c` | no | 11 | 1/1 | UNOBSERVED (first mention task-1180038b 2026-11-13T12:43) | - | B-tmp unpushed |
| `/tmp/card-1836-s3b-r2-1871-compose` | 0.24 | 2026-11-13T09:12 | 107 | (detached) | `949f85e5c7` | no | 10 | 0/0 | UNOBSERVED (first mention task-5dcf323f 2026-11-13T09:43) | - | B-tmp unpushed |
| `/tmp/claude-1654/…/-work-worktrees-task-6379c50f/…/scratchpad/cut` | 0.22 | 2026-11-03T06:33 | 349 | (detached) | `74178698ea` | yes | 0 | 0/0 | UNOBSERVED (first mention UNOBSERVED ) | - | clean+contained |
| `/tmp/claude-1654/…/-work-worktrees-task-6379c50f/…/scratchpad/w-de27ce2fd` | 0.22 | 2026-11-03T06:38 | 349 | (detached) | `de27ce2fd8` | no | 0 | 0/0 | UNOBSERVED (first mention UNOBSERVED ) | - | clean+contained |
| `/tmp/claude-1654/…/-work-worktrees-task-6379c50f/…/scratchpad/w-135865831` | 0.22 | 2026-11-03T06:36 | 349 | (detached) | `1358658310` | no | 0 | 0/0 | UNOBSERVED (first mention UNOBSERVED ) | - | clean+contained |
| `/tmp/c1629-plain-one` | 0.21 | 2026-11-02T05:59 | 374 | (detached) | `0e164b02df` | no | 0 | 0/0 | UNOBSERVED (first mention task-b9729f4b 2026-11-02T06:02) | - | clean+contained |

Clean and contained (30): `git worktree remove` commands

```
git -C /work/repos/antiphon worktree remove /tmp/card1862-s1-b4ab4c34
git -C /work/repos/antiphon worktree remove /tmp/cc5b4299-rc22
git -C /work/repos/antiphon worktree remove /tmp/card-1925-mutant
git -C /work/repos/antiphon worktree remove /tmp/cc5b4299-rc21
git -C /work/repos/antiphon worktree remove /tmp/review-18072e35-master
git -C /work/repos/antiphon worktree remove /tmp/task-15406ecc-coverage2
git -C /work/repos/antiphon worktree remove /tmp/card1884-review-2db533c9/master
git -C /work/repos/antiphon worktree remove /tmp/g90-master-c1029
git -C /work/repos/antiphon worktree remove /tmp/card1901-e594649c-base
git -C /work/repos/antiphon worktree remove /tmp/card-1912-base-8c06
git -C /work/repos/antiphon worktree remove /tmp/c1886-master-base
git -C /work/repos/antiphon worktree remove /tmp/antiphon-master-8592d536
git -C /work/repos/antiphon worktree remove /tmp/card-1964-red-d32418928
git -C /work/repos/antiphon worktree remove /tmp/antiphon-review-ff132917/base
git -C /work/repos/antiphon worktree remove /tmp/c2cf0b8a-review/red-d2
git -C /work/repos/antiphon worktree remove /tmp/antiphon-review-ff132917/red-source
git -C /work/repos/antiphon worktree remove /tmp/c2cf0b8a-review/red-d1
git -C /work/repos/antiphon worktree remove /tmp/c1893-red-068308a85
git -C /work/repos/antiphon worktree remove /tmp/antiphon-review-40d54591/master
git -C /work/repos/antiphon worktree remove /tmp/card-1875-red-scratch
git -C /work/repos/antiphon worktree remove /tmp/card1943-base-c1029
git -C /work/repos/antiphon worktree remove /tmp/c1947-master-23686c59
git -C /work/repos/antiphon worktree remove /tmp/antiphon-review-40d54591/historical
git -C /work/repos/antiphon worktree remove /tmp/antiphon-review-b9760da0/baseline
git -C /work/repos/antiphon worktree remove /tmp/c1514-index-tip
git -C /work/repos/antiphon worktree remove /tmp/c1877-master
git -C /work/repos/antiphon worktree remove /tmp/claude-1654/-work-worktrees-task-6379c50f/0070ccc5-5710-438d-b7de-c56ddacfad1d/scratchpad/cut
git -C /work/repos/antiphon worktree remove /tmp/claude-1654/-work-worktrees-task-6379c50f/0070ccc5-5710-438d-b7de-c56ddacfad1d/scratchpad/w-de27ce2fd
git -C /work/repos/antiphon worktree remove /tmp/claude-1654/-work-worktrees-task-6379c50f/0070ccc5-5710-438d-b7de-c56ddacfad1d/scratchpad/w-135865831
git -C /work/repos/antiphon worktree remove /tmp/c1629-plain-one
```

Commits not on live origin (42): salvage first (then `git worktree remove <path>` after `ls-remote` confirms the salvage ref)

```
git -C /tmp/c1877-comp-a push origin HEAD:refs/heads/salvage/tmp-c1877-comp-a   # 5 commits
git -C /tmp/c1877-comp-b push origin HEAD:refs/heads/salvage/tmp-c1877-comp-b   # 6 commits
git -C /tmp/card-1915-review-compose push origin HEAD:refs/heads/salvage/tmp-card-1915-review-compose   # 1 commits
git -C /tmp/card-1943-review-compose push origin HEAD:refs/heads/salvage/tmp-card-1943-review-compose   # 9 commits
git -C /tmp/c1915-compose push origin HEAD:refs/heads/salvage/tmp-c1915-compose   # 29 commits; then decide on 1 porcelain line(s)
git -C /tmp/review-01e2734e-master2 push origin HEAD:refs/heads/salvage/tmp-review-01e2734e-master2   # 12 commits
git -C /tmp/c1514-r2-master push origin HEAD:refs/heads/salvage/tmp-c1514-r2-master   # 7 commits
git -C /tmp/card-1875-master-scratch push origin HEAD:refs/heads/salvage/tmp-card-1875-master-scratch   # 3 commits
git -C /tmp/card-1850-s2-review-scratch push origin HEAD:refs/heads/salvage/tmp-card-1850-s2-review-scratch   # 8 commits
git -C /tmp/claude-1654/-work-worktrees-task-5b164f58/8faff552-e7bf-4db0-a9c7-0518b367d2c4/scratchpad/mastertree push origin HEAD:refs/heads/salvage/tmp-task-5b164f58-scratch-mastertree   # 5 commits; then decide on 1 porcelain line(s)
git -C /tmp/s5c-review-d5ecb5af/merge push origin HEAD:refs/heads/salvage/tmp-s5c-review-d5ecb5af-merge   # 1 commits
git -C /tmp/card-1884-review-compose push origin HEAD:refs/heads/salvage/tmp-card-1884-review-compose   # 3 commits
git -C /tmp/c0824-compose push origin HEAD:refs/heads/salvage/tmp-c0824-compose   # 35 commits
git -C /tmp/review-18072e35-compose2 push origin HEAD:refs/heads/salvage/tmp-review-18072e35-compose2   # 21 commits
git -C /tmp/review-575ab1a1-master push origin HEAD:refs/heads/salvage/tmp-review-575ab1a1-master   # 6 commits
git -C /tmp/c1886-master-repair push origin HEAD:refs/heads/salvage/tmp-c1886-master-repair   # 10 commits
git -C /tmp/claude-1654/-work-worktrees-task-1c23b333/ee4cb9db-d96d-40a6-ab75-6c220729acfd/scratchpad/onmaster push origin HEAD:refs/heads/salvage/tmp-task-1c23b333-scratch-onmaster   # 7 commits
git -C /tmp/card-1820-s1r3e-review-master push origin HEAD:refs/heads/salvage/tmp-card-1820-s1r3e-review-master   # 8 commits
git -C /tmp/c1836-s4a-combined-53820e0f push origin HEAD:refs/heads/salvage/tmp-c1836-s4a-combined-53820e0f   # 26 commits; then decide on 1 porcelain line(s)
git -C /tmp/card-1820-s1r6d-review-253bead91 push origin HEAD:refs/heads/salvage/tmp-card-1820-s1r6d-review-253bead91   # 5 commits
git -C /tmp/card-1850-s3a-review-ea79a8f2 push origin HEAD:refs/heads/salvage/tmp-card-1850-s3a-review-ea79a8f2   # 5 commits
git -C /tmp/card1887-master-scratch push origin HEAD:refs/heads/salvage/tmp-card1887-master-scratch   # 9 commits
git -C /tmp/card-1925-compose push origin HEAD:refs/heads/salvage/tmp-card-1925-compose   # 4 commits
git -C /tmp/card1887-master-now push origin HEAD:refs/heads/salvage/tmp-card1887-master-now   # 9 commits
git -C /tmp/card1887-master-f805 push origin HEAD:refs/heads/salvage/tmp-card1887-master-f805   # 9 commits
git -C /tmp/card-1886-s2-review-55233180 push origin HEAD:refs/heads/salvage/tmp-card-1886-s2-review-55233180   # 1 commits
git -C /tmp/c1836-s5b-review-scratch push origin HEAD:refs/heads/salvage/tmp-c1836-s5b-review-scratch   # 9 commits
git -C /tmp/s5a-review-master-253bead9 push origin HEAD:refs/heads/salvage/tmp-s5a-review-master-253bead9   # 15 commits; then decide on 1 porcelain line(s)
git -C /tmp/s5a-review-master-90ae19f6 push origin HEAD:refs/heads/salvage/tmp-s5a-review-master-90ae19f6   # 15 commits; then decide on 1 porcelain line(s)
git -C /tmp/c1947-compose-23686c59 push origin HEAD:refs/heads/salvage/tmp-c1947-compose-23686c59   # 3 commits
git -C /tmp/c1906-compose-wt push origin HEAD:refs/heads/salvage/tmp-c1906-compose-wt   # 1 commits; then decide on 3 porcelain line(s)
git -C /tmp/review-18072e35-compose push origin HEAD:refs/heads/salvage/tmp-review-18072e35-compose   # 21 commits
git -C /tmp/card-1886-s2-review-1fde603c push origin HEAD:refs/heads/salvage/tmp-card-1886-s2-review-1fde603c   # 8 commits
git -C /tmp/card-1885-review-fe5b7764 push origin HEAD:refs/heads/salvage/tmp-card-1885-review-fe5b7764   # 19 commits
git -C /tmp/card-1894-review-scratch push origin HEAD:refs/heads/salvage/tmp-card-1894-review-scratch   # 2 commits
git -C /tmp/review-01e2734e-master push origin HEAD:refs/heads/salvage/tmp-review-01e2734e-master   # 11 commits
git -C /tmp/c1514-index-m78442d9 push origin HEAD:refs/heads/salvage/tmp-c1514-index-m78442d9   # 4 commits
git -C /tmp/c1836-s5b-review-scratch-3dc5e5c push origin HEAD:refs/heads/salvage/tmp-c1836-s5b-review-scratch-3dc5e5c   # 9 commits
git -C /tmp/c1514-index-master push origin HEAD:refs/heads/salvage/tmp-c1514-index-master   # 4 commits
git -C /tmp/card-1820-s1r3e-review-clean push origin HEAD:refs/heads/salvage/tmp-card-1820-s1r3e-review-clean   # 8 commits
git -C /tmp/s3b-r3-master-scratch push origin HEAD:refs/heads/salvage/tmp-s3b-r3-master-scratch   # 11 commits; then decide on 1 porcelain line(s)
git -C /tmp/card-1836-s3b-r2-1871-compose push origin HEAD:refs/heads/salvage/tmp-card-1836-s3b-r2-1871-compose   # 10 commits
```

Dirty, keep for a decision (6):

```
/tmp/c1910-merged-now  8 porcelain line(s), 8 tracked
/tmp/c1886-layering-red  1 porcelain line(s), 1 tracked
/tmp/c1893-merge-review  2 porcelain line(s), 2 tracked
/tmp/c1910-merged  8 porcelain line(s), 8 tracked
/tmp/antiphon-review-c0b3cb9c/master-evaluator-card1877  9 porcelain line(s), 9 tracked
/tmp/antiphon-review-c0b3cb9c/master-plus-card1877  2 porcelain line(s), 2 tracked
```

`git worktree remove` of the three `task-6379c50f` scratch trees is the precondition for ever turning `claude-scratch` on.

## 6. Reclaimable GiB by action

| # | Action | GiB | Mechanism | Time | Notes |
|---:|---|---:|---|---|---|
| 1 | Mirrors aging past the retention window | up to 91.79 | sanctioned, already on (`Execute=true`) | 12-24 h after each task ends (the applied floor is 12 h shipped, 24 h observed on 2026-11-08); 25 per tick, every 30 min | 54 `settling` rows in the preview; still subject to `report_unpreserved`, `sync_debt` etc. |
| 2 | /tmp worktrees, clean and contained | 37.50 | hand `git worktree remove`, 30 commands | minutes | no sanctioned tool |
| 3 | /tmp worktrees with unpushed commits | 60.49 | salvage push + remove, 42 pairs | under an hour | operator decides whether the commits matter |
| 4 | Class D orphan helpers | 25.00 | hand `git worktree remove`, 12 commands | minutes | |
| 5 | Class A, hand removal past the sync-debt hold | 18.13 (A2) + 9.42 (A1) | sanctioned for A1 once `kept_recheck_pending` expires; A2 needs the operator to bypass `sync_debt` | A1: next re-send (≤ 24 h back-off); A2: minutes by hand | inventory's list |
| 6 | Temp sweep, registered families | 17.56 (≥ 72 h) | sanctioned, `TempSweep:Enabled/Execute=true` | ~30 h at 200 entries per tick | DB cost: ~40k rows per tick |
| 7 | Claude scratchpads of retired mirrors | 11.07 (≤ 1 GiB each) | `claude-scratch=true` | after 6 | only after the `task-6379c50f` worktrees are removed |
| 8 | Class B | 3.37 | salvage + remove | minutes | |
| 9 | Nested Docker (dind) | 6.27 images + 1.02 volumes (reclaimable, read-only numbers) | `docker image prune` / volume prune: NOT advised for volumes (operator rule: never touch a Docker volume) | minutes | host Docker numbers UNOBSERVED |
| 10 | Unregistered /tmp remainder older than 72 h | 31.33 | none; would need new registered families | - | `rhp-*`, `rh-*`, `antiphon-review-*` (non-worktree), `antiphon-pwsh-cache-*`, `task-08e79898-stream-*` |

Recommended order: (2) and (4) first (63 GiB, content-safe, no setting change), then (3) after a salvage decision, then the census of
section 4 step 1 and (6), with (1) running on its own throughout; (5) A2 and (7) last. Disk at 19:34Z: 705 GiB, 587 GiB used, 90 GiB free (87 %).

## Not done, noted

- Nothing deleted, no setting changed, no preview or execute call (a preview writes a residue run and needs the operator token).
- UNOBSERVED: the producer of the 21,579 random six-character directories; which server process is the live ticking server; the host's Docker
  numbers and any holder outside this container (no SSH key); the first kept reason of the five A1 mirrors (an earlier run).
- Fix idea (not designed): fold random-name prefixes in the temp sweep's `Normalize` (or cap its unregistered rows), and make the sweep refuse an
  entry that contains a registered worktree.
