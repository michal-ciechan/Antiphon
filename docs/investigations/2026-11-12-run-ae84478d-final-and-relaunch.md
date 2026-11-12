# Run ae84478d final result, refreshed seed, and the seeded rc/2026-11-18 launch (2026-11-12)

Task 25e35cdf. Live run `ae84478dd83d4cde8f537ba8303794fb` on `rc/2026-11-17` (`3db1a43b0`) finished on its own. This task read its final result and rebuilt the cumulative ordering seed from the finished run. It then launched the seeded Windows Full run on `rc/2026-11-18` = `56e96eabf698e9c1fcd462836ad32ed1fb259e83`, using the CARD-1667 launch set that task 44f789f3 prepared (see `2026-11-12-rc-cut-prepared.md`).

Nothing else changed. No tag was created and nothing was published or promoted. No rc ref was created or moved. Windows Update was not changed. The canonical checkout, AppHost, production runner, Docker, containers, the running Deploy task and the operator's server2 runner were only read.

## 1. Final result of run ae84478d

| Item | Value |
|---|---|
| Started / completed | 2026-11-11T19:07:51Z / 2026-11-12T11:55:25Z (60,454 s, 16.8 h). Supervisor 19112 `EXIT 1` and `SUPERVISOR-END` at 12:01:04Z. |
| Verdict | **FAIL**: `outcome=TESTS`, `succeeded=false`, `testsPassed=false`, `coverageComplete=false`, `testExit=1`, `harnessHang=completed-but-hung`, report delivered (`reportExit=0`). |
| Chunks | 1,162 timing chunks, 1,160 TRX chunks plus `client`/`scripts` (no TRX by design). `lastPhase`: **1,160 `complete`, 2 `timeout`**. No partial or unstarted chunk. Builds (npm ci, npm build, client lint, `dotnet build Antiphon.sln`) all passed. |
| Required suites | 12: antiphon, session-runner, pty-host, agents-pty, messaging, client, scripts, e2e, markdown-pdf, observability, server-git, resilience. `git-in-process` is absent, as expected: cut 3db1 predates it. `resilience` **did** run on 3db1 (1 chunk, pass). |

Summary.json is 889,829,274 bytes. It was streamed with a Newtonsoft `JsonTextReader` and never loaded whole. The extracts are in `C:\Antiphon\releases\task-25e35cdf\` (`summary-small.txt`, `suites.json`, `timing.json`).

### Per-suite status

| Suite | Chunks | Result |
|---|---:|---|
| antiphon | 885 | **FAIL**: 6 chunks (5 failed-row, 1 harness timeout) |
| session-runner | 101 | **FAIL**: 3 chunks (2 failed-row, 1 harness timeout) |
| pty-host | 53 | pass |
| agents-pty | 29 | pass (coverage reason, see below) |
| e2e | 76 | pass |
| markdown-pdf | 12 | pass |
| messaging, observability, server-git, resilience | 1 each | pass |
| client, scripts | 1 each | pass |

### Every failed test row (9 rows, 7 chunks): all in the KNOWN set

| Chunk | Test | Start (UTC) | Message (TRX) | Known as |
|---|---|---|---|---|
| antiphon g80 | `AgentTaskLandNotificationParkFollowUpTests.C1773_CanceledNoteDoesNotLateConfirm` | 11-11 23:15:01 | `AfterClassException: AfterClass hook failed: Cannot access a disposed object. Object name: 'TestDbFixture'.` | g80 teardown race |
| antiphon g97 | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | 11-12 00:06:42 | `c1017_checkout_under_temp: ...\Temp\c1017\9f72... is under ...\Temp\; run from a checkout outside the temp directory.` | g97 temp-root guard |
| antiphon g126 | `TestSelectionTests.C1313_SelectorLineEndings` | 11-12 08:04:23 | `live selector is not LF: scripts/select-test-rosters.ps1` (the run's long-lived `C:\Antiphon\releases\checkout`) | g126 old-clone CRLF |
| antiphon g131 | `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged` | 11-12 01:04:33 | `The application to execute does not exist: ...\regenerate-derived-impact-index\bin\Debug\net9.0\Antiphon.DerivedImpactIndex.dll` | g131 |
| antiphon g131 | `ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity` | 11-12 01:07:04 | `result.ExitCode should be 0 but was 1` (nightly-tests fixture `Exception calling "Invoke"`) | g131 |
| antiphon g133 | `ReleaseSelectionCliTests.BaselineMatchesWorker` | 11-12 01:31:20 | `FAIL RS BaselineMatchesWorker.receipt - route=worker expected=cli default` | g133 |
| session-runner g1 | `StandingRunnerCwdTests.Symlinked_or_non_directory_standing_checkout_is_refused_at_launch` | 11-12 03:44:45 | `UnauthorizedAccessException: Access to the path 'C:\tmp\c1825-link-...\agents\linked-agent' is denied.` | sr g1 |
| session-runner g1 | `StandingRunnerCwdTests.Standing_symlink_swapped_before_start_is_refused` | 11-12 03:44:45 | `UnauthorizedAccessException: Access to the path 'C:\tmp\c1834-swap-...\agents\real-agent' is denied.` | sr g1 |
| session-runner profile-16 | `C1514HostLoadNativeOverheadTests.Windows_SamplerOverheadStaysWithinBudget` | 11-12 09:37:20 | `on.P95TickMs should be less than 25d but was 119.1206d` | profile-16 load-induced (AppHost restart) |

### NEW (not in the known list): two harness timeouts, every test passed

| Chunk | Window (UTC) | TRX | Harness | Holder left alive |
|---|---|---|---|---|
| antiphon `all-group-118` | 11-12 11:00:49 to 11:04:37 (TRX), then timed out | 146/146 passed | `result=FAIL exit=1 timedOut=true`, `lastPhase=timeout`, `why=completed-but-hung` | one live `sleep` descendant (pid 82328, 104 handles); 3,261 other descendants (git/conhost) had exited |
| session-runner `all-group-4` | 11-12 03:53:23 to 04:01:27 (TRX), then timed out | 133/133 passed | the same | one live `Antiphon.PtyHost` descendant (pid 118772, 263 handles); 14,182 other descendants had exited |

Classes in g118: `E2ECopyOwnedPtyHostAppHostContractTests`, `LandingAdmissionControlTests`, `LandingGitBatchingTests`, `LandRecoveryCheckoutTests`, `LandRecoveryIgnoredNestedRepositoryTests`, `RepositoryChildRecoveryTests`, `RepositoryMutationLeaseTests`. Session-runner g4 has 30 classes, listed in its TRX, among them `TestSessionTeardownTests`, `HerdrPaneChildKillTests` and `RunnerMultilinePromptDeliveryTests`. The captures are `...\2026-11-11-1907-ae84478d\{antiphon-all-group-118,session-runner-all-group-4}-tests.log.hang.txt`.

An antiphon g118 hang capture also exists in run d88de419 (`rc-2026-11-16-...\2026-11-11-1142-d88de419\antiphon-all-group-118-tests.log.hang.txt`). Chunk ids renumber between plans, so this may be the same class set and not a one-off.

Classification: a test-hygiene leak, where a test leaves an orphan `sleep` or `PtyHost` that holds inherited handles. It is **not** an environment kill: no reboot (boot 2026-11-11T17:36:56Z, no new event 1074), Docker up and postgres healthy throughout, and the chunks either side are normal. It does not block the launch. Fixers go in separately: one Debug task per chunk, to find the test that spawns the orphan.

Coverage-only reasons (`coverageComplete=false`, not failures). `unexecuted-outcome` was raised for Linux-only cases that executed nowhere on Windows:

- `RcBridgeProbePlatformTests.Linux_probe_does_not_report_a_measured_zero` (g121)
- `ZombieCensusPlatformTests` 2 cases (g120)
- `PwshPrivateCacheTests` 4 cases (g128)
- `UnixPtyDeliveryEnvelopeTests` (agents-pty g1)

The summary also carries 25 `stale-roster` warnings. No prior Windows run finished, so none could be compared: d88de419 was killed by the reboot, and 6591e378 was superseded.

## 2. Refreshed seed

`git ls-remote origin refs/heads/rc/2026-11-18` = `56e96eabf698e9c1fcd462836ad32ed1fb259e83` (unchanged; master is the same SHA).

Inputs:

- **Final ae84 export.** `scripts/export-nightly-stopped-run-seed.ps1 -RunDirectory ...\2026-11-11-1907-ae84478d -TimingRoot ...\timing-ae84478d... -SourceCandidateId rc-2026-11-17-3db1a43b0224 -SourceSha 3db1a43b0... -SourceRunId ae84478d...` ran against the finished, read-only directory. It produced `seed-ae84478d-final-raw.json` (SHA-256 `e9696c0c...9022c`) with **1,160 chunks, all completed**, and 9 failed-test rows in 7 chunks.
- **Prior seed.** `task-231427bc\seed-cumulative.json`, the d88de419 and 6591e378 credits.
- **Dirty set.** Unchanged range `3db1a43b0..56e96eabf`, so it reuses task 44f789f3's `impacted-classes.json` (copied): 221 bare names from classes, derivedClasses and owed `first` samples. `client` and `scripts` are dirty.

Rules: the same named-class rule as tasks 500bb290, 231427bc and 44f789f3. The builder is `C:\Antiphon\releases\task-25e35cdf\build-cumulative-seed.ps1`, a copy of 44f789f3's with the base switched to the final export. It adds **one rule**: a chunk whose harness verdict was a timeout (`completed-but-hung`) gets no credit even though its TRX passed. That applies to antiphon g118 and session-runner g4.

| Count | Value |
|---|---:|
| Chunks in seed (ae84 plan) | 1,160 |
| **Seeded as passed** | **915**, all from ae84478d (44f789f3 had 876: 401 ae84 + 475 prior) |
| **Failed-first** | **7 chunks, 9 test rows**: antiphon g80, g97, g126, g131 (2), g133; session-runner g1 (2), profile-16 |
| **Rerun total (not seeded)** | **245** = 236 invalidated + 7 failed-first + 2 hung, plus `client`, `scripts` and the new `git-in-process` suite |
| **Newly invalidated by `3db1..56e9`** | **236** ae84-passed chunks with a dirty class (44f789f3 had 235; adds e2e g1/g8, which were unfinished at its snapshot) |
| Harness-hung, no credit | 2 (antiphon g118, session-runner g4) |
| Prior failed rows | 16 d88 rows, all dropped because ae84 re-ran their classes to completion; 0 kept |
| Seeded from the prior seed | 0 (every chunk completed in ae84, so the prior seed is used only for its failed rows) |

Per suite (seeded / failed-first / rerun):

| Suite | Seeded | Failed-first | Rerun |
|---|---:|---:|---:|
| antiphon | 647 | 5 | 233 |
| session-runner | 97 | 2 | 2 |
| e2e | 74 | 0 | 2 |
| agents-pty | 28 | 0 | 1 |
| pty-host | 53 | 0 | 0 |
| markdown-pdf | 12 | 0 | 0 |
| messaging, observability, resilience, server-git | 1 each | 0 | 0 |

Seed `C:\Antiphon\releases\task-25e35cdf\seed-cumulative.json`:

- `contentHash` `356556f0d4fe5ec082d87d2a9ef35d8900f1cdd4b69268dd0afd727c64455ff2`
- whole-file digest (`-PriorFailuresDigest`) `9cf227588e34d28d7e23ddddce26ee4dd97ed0ed668226f5090561741e17b2c4`
- `Read-NightlyStoppedRunSeed` from the cut's scripts accepted 1,160 chunks

The report is in `seed-cumulative-report.json` and the list in `seeded-from-ae84.txt`. The new run copied the seed into its log directory as `prior-failures.json` (725,543 bytes).

## 3. Launch

Preflight, read at 12:10 to 12:13Z:

| Check | Result |
|---|---|
| No live `supervise-rc`, `launch-rc`, `nightly-run` or `build-slot` | yes; 19112, 29468, 7448 and 18716 are all gone |
| No `run.lock` / `native-run.lock` for the new state and coordination roots | yes; `state-231427bc` lock released; old roots' stale locks untouched |
| Docker | server 29.5.3 |
| `antiphon-postgres` | Up 18 hours (healthy) |
| AppHost | `GET :17202/api/version` = `56e96eabf698...`, `land-v2`, `operator-shutdown-v1`; `/health` 200 |
| Memory | **10.1 GB** physical free of 31.9 GB; 20.7 GB commit free of 54.9 GB |
| Disk | C: 246.2 GB free |
| Deploy task / operator server2 runner | not touched |

Private checkout: `C:\Antiphon\releases\checkout-25e35cdf`. It is a new clone with `core.longpaths`, created with `--reference-if-able checkout-44f789f3 --dissociate`, so it has no alternates. It is detached at `56e96eabf`, carries both ownership markers, and its tree is clean. `launch-rc.ps1` is byte-identical to `scripts/qualification/launch-rc.ps1` (SHA-256 `DB7A6DDF...1F65664`).

Launch record `C:\Antiphon\releases\task-25e35cdf\launch-record.json` sets:

- run id `0b292fa4bc3b40158e8950fb17804a21`
- state root `C:\Antiphon\releases\state-25e35cdf`
- log root `C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\logs`
- coordination root `...\rc-2026-11-18-56e96eabf698\coordination`
- `-PriorFailuresDigest 9cf22758...b2c4 -AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-17-3db1a43b0224`
- build-slot label `rc-qualification-25e35cdf`

The run was launched at 12:13:37Z through `Win32_Process.Create` with pwsh 7:

```powershell
$cmd = '"C:\Program Files\PowerShell\7\pwsh.exe" -NoProfile -File C:\Antiphon\releases\checkout-25e35cdf\scripts\qualification\start-supervisor.ps1 -TaskRoot C:\Antiphon\releases\task-25e35cdf -LaunchScript C:\Antiphon\releases\task-25e35cdf\launch-rc.ps1 -LogRoot C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\logs -StateRoot C:\Antiphon\releases\state-25e35cdf -RunId 0b292fa4bc3b40158e8950fb17804a21'
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd; CurrentDirectory = 'C:\Antiphon\releases\checkout-25e35cdf' }
```

| Item | Value |
|---|---|
| Run id | `0b292fa4bc3b40158e8950fb17804a21` |
| Supervisor | pid 45428 (started 12:13:37.81Z; starter 48144 exited) |
| launch-rc | pid 23832 |
| build-slot | pid 7632 |
| nightly-run | pid 53512 |
| Log dir | `C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\logs\2026-11-12-1213-0b292fa4` |
| Timing root | `...\logs\timing-0b292fa4bc3b40158e8950fb17804a21` |
| State file | `C:\Antiphon\releases\state-25e35cdf\last-run.json` (`run.lock` alongside) |
| Native lock | `...\rc-2026-11-18-56e96eabf698\coordination\native-run.lock` |
| Heartbeat | `C:\Antiphon\releases\task-25e35cdf\supervisor-heartbeat.json`; supervisor log `...\task-25e35cdf\supervisor.log` |

Cold reset: `scripts/workspace-slots.ps1 cold-reset -AllRunners -Reason 'rc qualification rc/2026-11-18 56e96eabf698'` printed `no runner advertises workspaceSlotV1; nothing to cold-reset` (exit 0), so no request ids were issued.

Verified after launch:

- The heartbeat advanced: `observedAtUtc` went 12:14:40 → 12:16:43 → 12:27:52Z, and CPU climbed through npm ci, npm build, client lint and `dotnet build`.
- `dispatch-schedule.json` (12:24:33Z) accepted the seed: `historyDigest` = the seed digest `9cf22758...`, `preparationComplete=true`, `unresolved=0`, 1,163 entries including `git-in-process`.
- The 7 `priorFailure=true` chunks sit at dispatch ordinals 0 to 6, in this order: antiphon g80, g97, g131, g133, session-runner g1, antiphon g126, session-runner profile-16.
- The first failed-first chunk, **antiphon `all-group-80`**, started at **12:24:33Z** (`lastPhase=run`).

Watch with `scripts/orchestration/watch-qual.ps1` on the log directory, and with `scripts/qualification/observe.ps1 -TaskRoot C:\Antiphon\releases\task-25e35cdf -StateRoot C:\Antiphon\releases\state-25e35cdf`.

### ETA

From the finished ae84 timing root (`task-25e35cdf\eta.ps1`, a copy of 44f789f3's):

- 51.42 planned hours over 1,160 chunks.
- 16.64 elapsed hours from the first chunk start to the last chunk end.
- Observed ratio **0.325** elapsed/planned (wall/planned 0.320).

The 0.395 measured from the 06:53Z snapshot was pessimistic; the second half ran faster. Every chunk runs, because seeds only reorder. Expect about 16.7 h of chunks plus about 10 min of build and discovery, plus the small `git-in-process` suite: **about 17 h from 12:13Z, so around 05:15Z on 2026-11-13.**

### Pending reboot (read only)

Every Windows Update and CBS reboot flag is absent: `RebootRequired`, `PostRebootReporting`, `RebootPending`, `RebootInProgress`, `PackagesPending`, `UpdateExeVolatile`. `Microsoft.Update.SystemInfo.RebootRequired` is False, and no rename is pending.

`PendingFileRenameOperations` holds the same 4 delete-on-boot entries (Gaming Services dll, Edge updater temp), none from Windows Update. `wuauserv` is Stopped (Manual), `UsoSvc` and `BITS` are Running, and `WaaSMedicSvc` is Stopped. Boot was 2026-11-11T17:36:56Z, and there has been no 1074 since.

The run continues past 17:00Z, outside active hours, as on 2026-11-11. Windows Update was not changed.

## Authority

Covered by the operator's standing authority of 2026-11-11: "only rc cut and rc fixes; cut a new rc from master after fixes and rerun on prior failures". The prior run had ended on its own, so nothing needed stopping. Tags, publish and promote were not touched.
