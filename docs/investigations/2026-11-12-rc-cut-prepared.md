# rc/2026-11-18 cut and seeded run prepared, not launched (2026-11-12)

Task 44f789f3. This task cut `rc/2026-11-18` from current `origin/master`. It built the cumulative ordering seed from the live run `ae84478dd83d4cde8f537ba8303794fb` plus the earlier seeds. It also wrote the launch record for the next Windows Full run. **Nothing was launched or stopped.** The live run on `rc/2026-11-17` is untouched. That covers supervisor 19112, launch-rc 29468, nightly 18716, its `run.lock`, `native-run.lock`, checkout and log directory. No tag was created, nothing was published or promoted, and Windows Update settings were not changed. AppHost, the production runner, Docker and the containers were only read.

This is the same procedure as `2026-11-11-rc-recut-after-reboot.md` (task 231427bc) and `2026-11-11-rc-cut-and-run-launch.md` (task 500bb290), stopped before the launch.

## Verdict

| Item | Value |
|---|---|
| New rc ref | `rc/2026-11-18` (created by this task, a new ref only) |
| SHA | `56e96eabf698e9c1fcd462836ad32ed1fb259e83`. `git ls-remote origin refs/heads/master` gave the same value at 06:5xZ, so master did not move from the brief's value. |
| Candidate | `rc-2026-11-18-56e96eabf698`. Journal at `C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\candidate.json` (`pushed: false`), written by `scripts/release-candidate.ps1 -Ref rc/2026-11-18 -CheckoutRoot C:\Antiphon\releases\checkout-44f789f3` |
| Previous rc | `rc/2026-11-17` still at `3db1a43b02246706dc28075fce7e7b5e84804ddb`, untouched |
| Prepared run id | `7c44a70ba0774acdbf333f2e1dc6f345` (fresh, not started) |
| Task directory | `C:\Antiphon\releases\task-44f789f3\` |
| Private checkout | `C:\Antiphon\releases\checkout-44f789f3`. A new clone, detached at the cut SHA, with both ownership markers and a clean tree. The live run's `C:\Antiphon\releases\checkout` was not used. |

## In the cut, versus rc/2026-11-17

75 commits, `3db1a43b0..56e96eabf`, 86 files.

| Fix / feature | Key commits | Live-run failure it answers |
|---|---|---|
| g80: assembly DB teardown waits for open After(Class) hooks | `11974e9a1` red, `749f9e6bd`, `d784f3087`; note `ec870381e` | antiphon g80 `AgentTaskLandNotificationParkFollowUpTests.C1773_CanceledNoteDoesNotLateConfirm` |
| CARD-1842 S2 expected-duration roster, S5 watch-qual ETA line, S6 ledger durations | `f88cd7d30`..`1f77f6663` (11 commits) | (tooling) |
| CARD-1830 GitInProcess mirror project, registered as the new `git-in-process` suite | `32d00564e`, `0686117e5`, `4c1fd182d`, `1d9eb261e` | (new suite) |
| Batch 183 records | `4581774b1` land graph, `b02f110fb` derived index | |
| g133: select-release requires the server-git, resilience and git-in-process rc suites | `ab1b5696f`; note `02258bbca` | antiphon g133 `ReleaseSelectionCliTests.BaselineMatchesWorker` |
| g97: C1017 temp-root refusal admits a temp source with a persistent report root | `27eea23cf` red, `a24e32209`; note `3a1bb095a` | antiphon g97 `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` |
| CARD-1304 Review 7415c025 witnesses D-1..D-5 | `10b795a98`..`7f632fb9b` (14 commits) | |
| CARD-1841 staged helpers reuse their already built references | `348273a0c` red, `0a74771fc`, `79a516c27` | |
| Batch records | `24a3e6059` land graph | |
| g131: C1336 admits policy suite executables, and the solution build produces the index driver | `b5d8930f9`, `fcb583146`; notes `b74d466ee`, `c4b3134eb`, `dbb726ef8`, `8917844b6` | antiphon g131 `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`, `ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity` |
| CARD-0747 stale land-note replay; D-1 expire route needs the operator token and honours the off switch | `42d25c8ec` red, `f02b8e5d1`, `182955b35`, `956806d72`, `dda72b2b6`, `fa3f8e0f0`, `e6b95fe00`, `13c2f6d84`, `d51132292`, `b4c860dea` | (product: new `LandNoteExpirySweepService`) |
| CARD-1843 mirror test paths select their own project | `e9d02e72c` red, `8e5892703`, `b165bbd4f`, `357808314`, `ee98990d0` | |
| Batch 185 records | `47261a24d` land graph, `1637b5d56`, `2850a7411`, `60009e9b4` | |
| session-runner g1: standing link fixtures, and cleanup uses the 54cf48892 `Directory.Delete` helper | `74a7ca1ef`, `2012e4ad0`, `efea0749a`, `f4bc63052`, `e01aa6d0a`; note `56e96eabf` | session-runner g1 `StandingRunnerCwdTests` (2 methods) |

### Not in the cut

- CARD-1844 S1/S2 and CARD-1750 have no commits on master at `56e96eabf`. Neither does anything newer than master.
- Every older `rc/*` line is unchanged. No tag was created, and nothing was published or promoted.
- The five live-run fixes have Linux evidence, plus native evidence for sr g1 as the brief states. The new run is the Windows Full proof for the other four.

## Seed

A seed reorders a Full run. It never skips execution, so every chunk still runs.

Inputs:

1. **Live-run snapshot.** `C:\Antiphon\releases\task-44f789f3\snapshot-live-run.ps1` copied, at 2026-11-12T06:53:44Z, the run directory's root `*.trx` and `*.timing.json` and the timing root's `groups-planned*.json`. It wrote them into `...\task-44f789f3\snap\logs\`. Every source file was opened with `FileShare.ReadWrite|Delete`, so the live writer was never blocked. The active chunk (`e2e-all-split-35`, `active-chunk.json` generation 1255, the same before and after) was skipped. No `summary.json` exists in the live directory, and none was read. The exporter ran on the snapshot, not on the live directory: `scripts/export-nightly-stopped-run-seed.ps1 -RunDirectory <snap run dir> -TimingRoot <snap timing dir> -SourceCandidateId rc-2026-11-17-3db1a43b0224 -SourceSha 3db1a43b0... -SourceRunId ae84478d...`. It produced `seed-ae84478d-raw.json` (SHA-256 `92d2905d...`) with 1,160 chunks, 627 completed and 7 failed-test rows in 5 chunks.
2. **Earlier seeds.** The task-231427bc cumulative seed `C:\Antiphon\releases\task-231427bc\seed-cumulative.json` carries the d88de419 partial run and the 6591e378 credits. It was already invalidated for `ac60..6f87` and `6f87..3db1`.
3. **Dirty set.** `scripts/impacted-classes.ps1 -Range 3db1a43b0..56e96eabf -Json` gave 193 classes, 176 derived classes and 5 owed paths, with `skippedByCap` = `Antiphon.sln`. The dirty set is `classes` + `derivedClasses` + each owed path's `first` sample, which makes 221 bare names. This is the same named-set rule tasks 500bb290 and 231427bc used. The unnamed owed remainder was not expanded: `docs/ops-http.md` 153, `SupervisionSettings.cs` 146, `SessionMessageQueueService.cs` 125, `AttentionService.cs` 20, `AgentTaskLandNotificationService.cs` 15. Because seeds only order the run, the cost of that gap is ordering, not coverage. The `client` and `scripts` suites count as dirty.

Rules (builder `C:\Antiphon\releases\task-44f789f3\build-cumulative-seed.ps1`). The base rows are the ae84 plan's chunk ids, and matching is by class (`Resolve-NightlyStoppedSeedOrder`).

- An ae84 chunk that completed with no failed row and no dirty class is **passed** (credit ae84478d).
- An ae84 chunk that completed with failed rows is **failed-first**. Its 7 rows are the leading band.
- An ae84 chunk in flight at the snapshot gets **no credit**.
- An ae84 chunk that had not started is **passed** only when all of these hold:
  - every class appears in the 231427bc seed, and every row there for that class is completed and not `-failed`;
  - no prior failure remains for the class unless ae84 re-ran it to completion;
  - no class is dirty.
- The prior failed rows are the 16 d88 rows. All of them belong to classes that ae84 re-ran to completion in its failed-first band, so all were dropped.

| Count | Value |
|---|---:|
| Chunks in seed (ae84 plan) | 1,160 |
| **Seeded as passed** | **876**: 401 from ae84478d; 475 from the prior seed (460 6591e378, 15 d88de419) |
| **Failed-first** | **5 chunks, 7 test rows** |
| **Rerun total (not seeded)** | **284** (1,160 − 876, including the 5 failed-first), plus `client-all`, `scripts-all` and the new `git-in-process` suite |
| **Newly invalidated by `3db1..56e9`** | **235**: 221 chunks ae84 passed, and 14 chunks the prior seed carried |
| Prior seed unfinished (no earlier pass) | 41 (40 e2e chunks, including the in-flight `e2e-all-split-35`, and `antiphon-all-group-33`) |
| No class list in plan (never carried) | 3 (`observability-all`, `resilience-all`, `server-git-all`) |

Failed-first band. The new plan's chunk ids are assigned by the planner at launch, and the band is bound **by class**. The run that follows puts these at schedule ordinals 0 to 4 with `priorFailure=true`. Read the new ids from the new run's `dispatch-schedule.json`. Task 231427bc saw the same renumbering: d88 g131 became ae84 g130.

| ae84 chunk | Rows | Class(es) | Fix in cut |
|---|---:|---|---|
| `antiphon-all-group-80` | 1 | `AgentTaskLandNotificationParkFollowUpTests` | `749f9e6bd`, `d784f3087` |
| `antiphon-all-group-97` | 1 | `C0824RefOnlyCleanupTests` | `a24e32209` |
| `antiphon-all-group-131` | 2 | `DerivedImpactIndexTests`, `ReleaseGateFollowupTests` | `b5d8930f9`, `fcb583146` |
| `antiphon-all-group-133` | 1 | `ReleaseSelectionCliTests` | `ab1b5696f` |
| `session-runner-all-group-1` | 2 | `StandingRunnerCwdTests` | `e01aa6d0a` |

Lists are in `C:\Antiphon\releases\task-44f789f3\seed-cumulative-report.json` (`ae84PassedInvalidated`, `priorCarriedInvalidated`, `inFlight`, `failedFirstChunksAe84`) and in `seeded-from-ae84.txt`.

Seed file: `C:\Antiphon\releases\task-44f789f3\seed-cumulative.json`. Envelope source: run ae84478d, candidate `rc-2026-11-17-3db1a43b0224`, SHA `3db1a43b0...` (an ancestor of the cut). `contentHash` is `0a5b9e1b3d635f80b18a577c2418e59ae74a089d953efcc7e5a499393ca1ad67`. The whole-file digest (`-PriorFailuresDigest`) is `ed5c91698d692f7c38650ffe37dbb3756f4bc942acdfd65860be9b414b0e7054`, recomputed independently. `Read-NightlyStoppedRunSeed` from the cut's scripts accepted 1,160 chunks.

## Launch (prepared, NOT run)

The launch record is `C:\Antiphon\releases\task-44f789f3\launch-record.json`. It has `checkoutRoot` `C:\Antiphon\releases\checkout-44f789f3`, `expectedSha` = cut, and `buildSlotLabel` `rc-qualification-44f789f3`. The nightly arguments are:

`-Profile rc -Trigger rc -SelectionMode Full -RunId 7c44a70ba0774acdbf333f2e1dc6f345 -Ref rc/2026-11-18 -ExpectedSha 56e96eabf698e9c1fcd462836ad32ed1fb259e83 -CandidateId rc-2026-11-18-56e96eabf698 -CheckoutRoot C:\Antiphon\releases\checkout-44f789f3 -ReleaseRoot C:\Antiphon\releases -StateRoot C:\Antiphon\releases\state-44f789f3 -LogRoot C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\logs -CoordinationRoot C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\coordination -Repository michal-ciechan/Antiphon -PriorFailuresPath C:\Antiphon\releases\task-44f789f3\seed-cumulative.json -PriorFailuresDigest ed5c9169...7054 -AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-17-3db1a43b0224`

`launch-rc.ps1` was copied byte-for-byte from `scripts/qualification/` (SHA-256 `db7a6ddf...1f65664`, unchanged since 3db1).

### Stop sequence (operator, human-only; this task did NOT run it)

```powershell
# 1. Dry run: prints the recorded supervisor/driver pids and lock action, kills nothing, exits 2
pwsh -NoProfile -File scripts/qualification/stop-supervisor.ps1 -TaskRoot 'C:\Antiphon\releases\task-231427bc' -CoordinationRoot 'C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\coordination' -RunId 'ae84478dd83d4cde8f537ba8303794fb' -Reason 'superseded by rc/2026-11-18 56e96eabf'
# 2. The stop itself (same arguments plus -Confirm)
pwsh -NoProfile -File scripts/qualification/stop-supervisor.ps1 -TaskRoot 'C:\Antiphon\releases\task-231427bc' -CoordinationRoot 'C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\coordination' -RunId 'ae84478dd83d4cde8f537ba8303794fb' -Reason 'superseded by rc/2026-11-18 56e96eabf' -Confirm
# 3. Prove it: none of 19112 / 29468 / 7448 (build-slot) / 18716 alive, and no process
#    whose command line contains supervise-rc, launch-rc or nightly-run; build-slot lease released.
```

The live run holds a build-slot lease for its whole life through `build-slot.ps1` pid 7448. If it is not stopped first, a new launch waits up to 45 minutes for the slot. If it got one, it would be a second native owner: the new candidate has its own coordination root, so `native-run.lock` would not serialise the two runs. Stopping first is mandatory.

### Launch command (after the stop, from any shell; leaves the caller's Job Object)

```powershell
$cmd = '"C:\Program Files\PowerShell\7\pwsh.exe" -NoProfile -File C:\Antiphon\releases\checkout-44f789f3\scripts\qualification\start-supervisor.ps1 -TaskRoot C:\Antiphon\releases\task-44f789f3 -LaunchScript C:\Antiphon\releases\task-44f789f3\launch-rc.ps1 -LogRoot C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\logs -StateRoot C:\Antiphon\releases\state-44f789f3 -RunId 7c44a70ba0774acdbf333f2e1dc6f345'
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd; CurrentDirectory = 'C:\Antiphon\releases\checkout-44f789f3' }
```

Then run `pwsh -NoProfile -File scripts/workspace-slots.ps1 cold-reset -AllRunners -Reason "rc qualification rc/2026-11-18 56e96eabf698"` and record the request ids. Watch with `scripts/orchestration/watch-qual.ps1` on the new log directory, and with `scripts/qualification/observe.ps1 -TaskRoot C:\Antiphon\releases\task-44f789f3 -StateRoot C:\Antiphon\releases\state-44f789f3`.

### Preflight checklist (read at 2026-11-12 06:57-06:59Z)

| Check | Result |
|---|---|
| No live `supervise-rc` / `launch-rc` / `nightly-run` | **FAILS (expected)**: 19112, 29468, 7448 and 18716 are alive on run ae84478d. This is the only failing item. |
| `rc/2026-11-18` = launch `expectedSha` | yes, `56e96eabf...`. If rc/2026-11-18 moves before the launch, nightly-run's fetch and reset would mismatch the pin. Re-pin the record and re-cut the seed digest then. |
| Private checkout HEAD = cut, markers present, clean | yes |
| `C:\Antiphon\releases\task-44f789f3` has no `supervisor-identity.json`; run id unused | yes |
| `state-44f789f3` / new coordination root have no `run.lock` / `native-run.lock` | yes; neither exists yet |
| Docker | server 29.5.3 up |
| `antiphon-postgres` | Up 13 hours (healthy) |
| AppHost API | `GET :17202/api/version` = `7bee43614495293afbae1b0f4add019d11dacb9c`, `land-v2`, `operator-shutdown-v1`; `/health` 200 Healthy |
| Memory | 5.4 GB physical free of 31.9 GB, 18.5 GB commit free of 54.9 GB, with the live run going. It frees up once the run is stopped. |
| Disk | C: 242.3 GB free of 1,862 GB |

### Pending reboot (read only)

Every Windows Update and CBS reboot flag is absent: `RebootRequired`, `RebootPending`, `RebootInProgress`, `PackagesPending`, `PostRebootReporting`, `UpdateExeVolatile`. `Microsoft.Update.SystemInfo.RebootRequired` is False, and no computer rename is pending.

`PendingFileRenameOperations` holds 4 delete-on-boot entries: the Gaming Services proxy dll and three Edge updater temp paths. None comes from Windows Update.

`wuauserv` is Stopped (Manual), `UsoSvc` and `BITS` are Running, and `WaaSMedicSvc` is Stopped. There is no AU policy key and no pause. Active hours are 08:00 to 17:00, and the desktop is on GMT (= UTC). Since boot (2026-11-11T17:36:56Z) the only installs were Store apps at 18:00Z, and there has been no new event 1074.

There is no restart pending now. The risk from 2026-11-11 stays: a ~20-hour run will be outside active hours from 17:00Z. Changing Windows Update settings is the operator's call, and nothing was changed.

## ETA

These figures come from the snapshot's plans and 628 completed timing sidecars (`C:\Antiphon\releases\task-44f789f3\eta.ps1`, read-only):

- The ae84 plan totals 51.42 planned hours over 1,160 chunks. The completed chunks account for 29.38 planned hours, and they took 11.60 elapsed hours from the first chunk start at 19:16:33Z to the last end at 06:52:21Z. That gives an observed ratio of **0.395** elapsed/planned; the wall/planned ratio is 0.389.
- **New run:** all chunks run, so 51.42 h × 0.395 ≈ **20.3 h** of chunks. Add about 10 minutes of build and discovery (ae84 took 19:07 to 19:16Z), plus the small new `git-in-process` suite. That is about **20.5 h from launch**. A launch at 08:00Z would finish around 04:30Z on 2026-11-13.
- **Live run if left alone:** 22.04 planned hours remain, which is about **8.7 h**, so roughly 15:30Z today. The repository `qualification-eta.ps1` on the same snapshot gave `outstanding=533 pointSeconds=30811` (8.6 h).

## Authority

The operator's standing authority of 2026-11-11 covers the cut: "only rc cut and rc fixes; cut a new rc from master after fixes and rerun on prior failures". Tags, publish, promote, the stop and the launch stay human-only or were deliberately not done. Windows Update settings were not touched.
