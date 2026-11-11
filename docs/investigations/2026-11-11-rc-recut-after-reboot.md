# rc/2026-11-17 cut and seeded relaunch after the desktop reboot (2026-11-11)

Task 231427bc. A Windows restart killed run `d88de419025b4d368fb75108e4de0c36` on `rc/2026-11-16`. This task cut `rc/2026-11-17` from current `origin/master` and started one detached Windows Full qualification at concurrency 1. It used a cumulative ordering seed. No tag was created. Nothing was published or promoted. AppHost, the production runner, Docker Desktop, containers and volumes were not changed. Windows Update settings were read and not changed.

## Verdict

| Item | Value |
|---|---|
| New rc ref | `rc/2026-11-17` |
| SHA | `3db1a43b02246706dc28075fce7e7b5e84804ddb`, equal to `origin/master` at the cut (`git ls-remote origin refs/heads/master`) |
| Candidate | `rc-2026-11-17-3db1a43b0224` (`C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\candidate.json`, `pushed: false`) |
| Branch creation | `git push origin 3db1a43b0...:refs/heads/rc/2026-11-17` (new ref only); then `scripts/release-candidate.ps1 -Ref rc/2026-11-17` journalled it |
| Previous rc | `rc/2026-11-16` still at `6f8741d872d63507827014f972ccf71196722d4a`, untouched |
| Run id | `ae84478dd83d4cde8f537ba8303794fb` |
| Started | 2026-11-11T19:06:56Z (WMI create), nightly-run 19:07:10Z |

## Why the old run stopped

System event 1074 at 2026-11-11T17:36:01Z: `svchost.exe` restarted the computer on behalf of `NT AUTHORITY\SYSTEM`, reason `Operating System: Service pack (Planned)`, code `0x80020010`. Last boot was 2026-11-11T17:36:56Z. The updates installed that afternoon were three .NET security updates (KB5135241 at 14:30Z, KB5135243 at 14:43Z, KB5135242 at 14:52Z), the MSRT (KB890830, 15:02Z) and Defender intelligence. The restart came 36 minutes after the end of the configured active hours (08:00 to 17:00 local; the desktop is on UTC in November).

At the kill, d88de419 had 75 completed chunks (trx=75 on its last heartbeat at 17:35:44Z). Supervisor 50568, launch-rc 410128 and nightly 449568 are gone. Its `state-500bb290\run.lock` and its coordination `native-run.lock` still name dead pid 449568. They were left in place. Both are private to that candidate and do not gate this run, which has its own state and coordination roots.

## Pending reboot status (read only)

Read at about 19:05Z, after the reboot:

| Check | Result |
|---|---|
| `WindowsUpdate\Auto Update\RebootRequired` | absent |
| `Component Based Servicing\RebootPending`, `RebootInProgress`, `PackagesPending` | absent |
| `Auto Update\PostRebootReporting`, `Updates\UpdateExeVolatile` | absent |
| `Microsoft.Update.SystemInfo.RebootRequired` | False |
| Computer rename pending | no |
| `PendingFileRenameOperations` | one entry: `C:\Windows\System32\gamingservicesproxy_17.dll.0` (delete on next boot; Gaming Services, not Windows Update) |
| Services | `wuauserv` Running (Manual), `UsoSvc` Running (Automatic), `BITS` Running, `WaaSMedicSvc` Stopped |
| Update policy | no `Policies\...\WindowsUpdate\AU` key; active hours 8 to 17; no pause set |
| Offline update search (`IsInstalled=0`) | 14 driver updates (Intel chipset, Realtek LAN, ME interface, Dell monitor, Apple USB), all `downloaded=False`, `RebootBehavior=2` (can request a reboot) |

No restart is pending now. The risk stays: active hours end at 17:00 local, and the same unattended-restart path can fire again outside them. That will happen if a reboot-requiring update installs, such as one of the 14 drivers or a monthly cumulative update. This run will be outside active hours from 17:00Z on 2026-11-12 if it is still going. Changing Windows Update settings is the operator's call; this task changed nothing. `Get-WindowsUpdateLog` was not run because it writes a merged log file to the Desktop. The System event log gave the same install history.

## In the cut, versus rc/2026-11-16

26 commits, `6f8741d87..3db1a43b0`:

| Fix | Commits | rc failure |
|---|---|---|
| Runner gc fixtures clear read-only git objects | `d8c82cc10b4f2f5a81eaa56dbecd48700955ae29` red, `974e67ed6a84d19ab5862bc726bf54bb2219c09f` fix | session-runner group 2 (C1817/C1818) |
| Startup Ctrl+U once an unclassified Claude frame is the composer | `033f5238ed24397c3769250836339c3dcd48304e` red, `9770cb2a97139e862178d73d199c05892d420fb6` fix | antiphon group 75 (Herdr startup write) |
| Post-land class census skips raw-string fixture lines | `57166c122ced7eb975294450a0c7600c6d381e95` red, `3a8deb98a5b811e74ff6904edcb0ad75e3a266fe` fix | antiphon group 131 |
| CARD-1832 platform filter probes assert the lane only under a probe row | `c83846158...`, `a3917c15558ab34b0660f7feec8755edc82dd362` | antiphon group 2 (taxonomy test env) |
| C519 probe waits outlast the child's deadline | `3445b06173525faaa9a5bacce57232e7e1f5886b` red, `dc8ca9622902cf1c12db4f14728535b7184a11d2` fix | antiphon group 29 (ChannelOutboundUnifiedCrash) |
| TurnEndGap harness advances the composer settle delay | `cc381350447bc84a243986aa39342ed9da82cf28` | antiphon group 60 (TurnEndGapRestartDelivery) |
| CARD-1224 UnixPty delivery ceilings | `72a552bdcdd335ac57595bc3d71215b60a875ee9` red, `b35ed98633c4234f037a0fe589547206cf6b747d` fix | (Linux delivery ceilings) |
| CARD-1829 Resilience mirror project | `55dd78b35...`, `1b16bcfe7...`, `b4b8b85ce...`, `a64c1711d...`, `4c8473448...`, `1e4785252...` | new `Antiphon.Resilience.Tests` project registered as a suite |
| Batch records | `d17d4bf35...` derived impact index, `17ebc0a32...` land-graph records | |
| Why-missed notes | `358d7b65b`, `8a62ae807`, `290e610a6`, `47a92646b`, `3db1a43b0` | |

### Not in the cut

- Every older `rc/*` line was left where it was. No tag, publish or promote.
- Nothing newer than master `3db1a43b0`.
- The d88de419 failures are fixed on master by the commits above. Linux evidence only. This run is their first Windows proof.

## Seed

Seeds reorder a Full run. They never skip execution. Every chunk still runs.

Inputs:

1. Run d88de419 export: `scripts/export-nightly-stopped-run-seed.ps1 -RunDirectory <d88 log dir> -TimingRoot <d88 timing dir>` gave `C:\Antiphon\releases\task-231427bc\seed-d88de419-raw.json`. It has 1,160 chunks, 75 completed, and 16 failed-test rows in 6 chunks.
2. The adjusted run 6591e378 seed from task 500bb290 (`C:\Antiphon\releases\task-500bb290\seed-6591e378.json`), unchanged. It already carries the `ac60..6f87` source-change invalidation.
3. `scripts/impacted-classes.ps1 -Range 6f8741d87..3db1a43b0 -Json`: 229 classes, 216 derived, 8 owed paths. Dirty set = `classes` + `derivedClasses` + each owed path's `first` sample, 268 bare names. This is the same named-set rule task 500bb290 used. The unnamed owed remainder was not expanded; the largest is `TestDbFixture.cs` with 482 classes. `skippedByCap` is `Antiphon.sln`. The `client` and `scripts` suites count as dirty because `scripts/test-*.ps1` changed. They have no TRX, so they are never carried anyway.

Rules (builder `C:\Antiphon\releases\task-231427bc\build-cumulative-seed.ps1`). Base rows are the d88 plan's chunk ids. Matching is by class, as `Resolve-NightlyStoppedSeedOrder` does.

- A d88 chunk that completed with no failed row and no dirty class is passed (credit d88de419).
- A d88 chunk that completed with failed rows stays completed, and its 16 rows are the failed-first band.
- The 9 d88 chunks in flight at the kill (`antiphon-all-group-79` to `-87`, started 17:32Z to 17:36:04Z, no TRX) get no credit. They stay `started=true, completed=false` (`in-flight-at-host-restart`), so they resume right after the failed band.
- A d88 chunk that never started is passed (credit 6591e378) only when four things hold. Every class must appear in the adjusted 6591 seed, and every 6591 row for that class must be completed. No 6591 failure may remain for the class unless d88 re-ran the class to completion. No class may be dirty.
- 6591 failed rows: all 699 belong to classes that d88 re-ran to completion. The 13 failed 6591 chunks were the first 13 d88 slots. None were kept. The four outage victims (old 115 to 118, d88 groups 116 to 119) and `e2e-all-split-7` passed in d88. Groups 116 and 118 and `e2e-all-split-7` are carried. Groups 117 and 119 are invalidated by the new source changes and rerun.

| Count | Value |
|---|---:|
| Chunks in seed (d88 plan) | 1,160 |
| Seeded as passed | 491 (24 from d88de419, 467 from 6591e378) |
| Failed-first chunks | 6 (16 test rows) |
| In flight at kill, no credit | 9 |
| Rerun total (not seeded) | 669 (1,160 minus 491), plus `client-all` and `scripts-all` |
| Newly invalidated by `6f87..3db1` | 49 (45 chunks d88 passed, 4 chunks the 6591 seed carried) |
| Already unfinished or source-changed before this cut | 379 not-started chunks with a dirty class, plus 223 not-started chunks the 6591 seed did not carry |

Failed-first band, by d88 chunk:

| d88 chunk | Rows | Fix in cut |
|---|---:|---|
| `session-runner-all-group-2` | 9 | `974e67ed6` |
| `antiphon-all-group-2` | 3 | `c83846158`, `a3917c155` |
| `antiphon-all-group-29` | 1 | `dc8ca9622` |
| `antiphon-all-group-60` | 1 | `cc3813504` |
| `antiphon-all-group-75` | 1 | `9770cb2a9` |
| `antiphon-all-group-131` | 1 | `3a8deb98a` |

The 45 d88-passed chunks invalidated by the new range are listed in `C:\Antiphon\releases\task-231427bc\seed-cumulative-report.json` (`d88PassedInvalidated`). The 24 d88 credits are in `seeded-from-d88.txt` beside it.

Seed file: `C:\Antiphon\releases\task-231427bc\seed-cumulative.json`. Envelope source: run d88de419, candidate `rc-2026-11-16-6f8741d872d6`, SHA `6f8741d87...`. `contentHash` is `ea637aaeda12039f79468f25459720b623a6d6b9d57fbb3f69d770c6d338ddea`. Whole-file digest (`-PriorFailuresDigest`) is `d222403c4efbff58d92f6c31af4c49885eb7574f875f747c94f2095300835125`. `Read-NightlyStoppedRunSeed` accepted it. Timestamps were kept as ISO-8601 strings (`ConvertFrom-Json -DateKind String`). The culture-string round trip that broke the 500bb290 digest did not recur.

## Launch

CARD-1667 repository set. `scripts/qualification/launch-rc.ps1` was copied byte-for-byte to `C:\Antiphon\releases\task-231427bc\launch-rc.ps1` (SHA-256 `db7a6ddff77ec57da00dbbe47de05ab543f1c6d1db110a02138aa17121f65664`). `scripts/qualification/start-supervisor.ps1` was run from this task's worktree, which is at the cut SHA. It was started with `C:\Program Files\PowerShell\7\pwsh.exe` through `Win32_Process.Create` (return 0). `C:\Antiphon\releases\checkout` was fetched and detached at the cut SHA first. Both ownership markers were present.

Preflight immediately before the create:

- No `supervise-rc`, `launch-rc` or `nightly-run` process was alive.
- No `native-run.lock` existed in the new coordination root. The old candidate's lock names dead pid 449568.
- Docker server 29.5.3 was up, and `antiphon-postgres` was Up (healthy).
- `GET http://localhost:17202/api/version` answered `7bee43614495293afbae1b0f4add019d11dacb9c` with `land-v2`.

The workspace cold reset (`scripts/workspace-slots.ps1 cold-reset -AllRunners -Reason "rc qualification rc/2026-11-17 3db1a43b0224"`) exited 0 with `no runner advertises workspaceSlotV1; nothing to cold-reset`. No request ids.

Nightly arguments are `-Profile rc -Trigger rc -SelectionMode Full -RunId ae84478dd83d4cde8f537ba8303794fb -Ref rc/2026-11-17 -ExpectedSha 3db1a43b02246706dc28075fce7e7b5e84804ddb -CandidateId rc-2026-11-17-3db1a43b0224`. They also carry the private `-StateRoot C:\Antiphon\releases\state-231427bc`, `-LogRoot` and `-CoordinationRoot` under the candidate, `-PriorFailuresPath` and `-PriorFailuresDigest` as above, and `-AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-16-6f8741d872d6`. Concurrency is the default 1. Build slot lease `77dd7e98-5679-408d-a733-f84f101f6199` was granted at once (`maxcpucount=4`).

| Process | Pid | Note |
|---|---|---|
| `start-supervisor.ps1` (WMI starter) | 9964 | exits after writing identity |
| `supervise-rc.ps1` | 19112 | started 2026-11-11T19:06:56.8783347Z; `detach=new-console`, `consoleProcessIds=[19112]` (starter pid not in it) |
| `launch-rc.ps1` | 29468 | parent 19112 |
| `build-slot.ps1` | 7448 | parent 29468 |
| `nightly-run.ps1` (`run.lock` owner) | 18716 | started 2026-11-11T19:07:10.7490991Z, bootId 197 |

## Watch paths

| What | Path |
|---|---|
| Log directory | `C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d` |
| Log root | `C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs` |
| Timing root | `C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\timing-ae84478dd83d4cde8f537ba8303794fb` |
| State | `C:\Antiphon\releases\state-231427bc\last-run.json` (and `run.lock`) |
| Heartbeat | `C:\Antiphon\releases\task-231427bc\supervisor-heartbeat.json` |
| Supervisor log | `C:\Antiphon\releases\task-231427bc\supervisor.log` |
| Identity | `C:\Antiphon\releases\task-231427bc\supervisor-identity.json` |
| Own state copy | `C:\Antiphon\releases\task-231427bc\own-last-run.json` |
| Schedule | `<log directory>\dispatch-schedule.json` |

Watcher:

```powershell
pwsh -NoProfile -File scripts/orchestration/watch-qual.ps1 -LogDir 'C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d' -StateFile '<owned-scratch>\qual.seen.txt' -Heartbeat 'C:\Antiphon\releases\task-231427bc\supervisor-heartbeat.json'
pwsh -NoProfile -File scripts/qualification/observe.ps1 -TaskRoot 'C:\Antiphon\releases\task-231427bc' -StateRoot 'C:\Antiphon\releases\state-231427bc'
```

`observe.ps1` credits only run id `ae84478dd83d4cde8f537ba8303794fb`. The end signal is `completedAt` for that run id. Do not stop the supervisor; stopping is human-only.

## Heartbeat and first chunk

| observedAtUtc | cpuSeconds | trx | newest | hangSuspected |
|---|---|---|---|---|
| 2026-11-11T19:07:38.2827914Z | 14.4 | 0 | last-run.json | false |
| 2026-11-11T19:08:08Z | 65.2 | 0 | npm-ci.log.stderr.tmp | false |
| 2026-11-11T19:16:44.7325170Z | 73.6 | 0 | log_261111191634139.diag | false |

The supervisor wrote `STATE-COPY own-last-run.json` at 19:07:38Z and 19:08:08Z. `last-run.json` names this run id, `phase=tests`, SHA `3db1a43b0...`. The client and solution builds and profile discovery for every suite passed. That includes the new `resilience` and `server-git` suites.

`dispatch-schedule.json` has 1,162 entries. Its `orderingReason` is `operator-seed-order`, and its `historyDigest` is the launch digest `d222403c...`. Six entries have `priorFailure=true`, and they lead the schedule. Chunk ids are not binding, so two of them were renumbered by the new plan:

| Order | New chunk | Holds the d88 failed methods from |
|---:|---|---|
| 0 | `session-runner-all-group-2` | the same chunk |
| 1 | `antiphon-all-group-75` | the same chunk |
| 2 | `antiphon-all-group-130` | d88 group 131 |
| 3 | `antiphon-all-group-2` | the same chunk |
| 4 | `antiphon-all-group-60` | the same chunk |
| 5 | `antiphon-all-group-28` | d88 group 29 |

Next comes the resume band. It starts with the d88-passed chunks that the new range invalidated (`antiphon-all-group-3`, `-4`, `-9`, and so on). The nine chunks in flight at the kill are at ordinals 48 to 56. `client-all` and `scripts-all` are at 593 and 594, and `resilience-all` is at 671.

First chunk: `session-runner-all-group-2-execution` was created at 2026-11-11T19:16:33.8133145Z, and its diag log was the newest file at 19:16:44Z. Supervisor 19112, launch-rc 29468 and nightly 18716 were alive at 19:17Z.

## Authority

Operator standing authority of 2026-11-11 covers this cut and rerun. Tags, publish, promote, and Windows Update settings stayed untouched.
