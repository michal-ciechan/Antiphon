# Slow script test classes: where the 45 minutes go

Date: 2026-11-17. Task `95bdabde` (Investigate, read-only profile). Master read: `62cfb330883cda0959d4f87f8f456933f35ca1cc`
(`origin/master` at 22:52Z was still this SHA). No code changed. No test was run. The only live observations were read-only
samples of the CP-12 row already running in `ws-02-d2`: a `ps` sample, `/proc/<pid>/stat` and the fixture `docker-trace.jsonl` files.

## Answer

**The cases run one at a time on a 24-core host that is about 85 % idle.** Every case in these classes carries
`[ParallelLimiter<ProcessSpawnLimit>]`, and `ProcessSpawnLimit.Limit` is `1`
(`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:15-18`). The limit applies per test process (per assembly),
not per host. So inside a row, the total time is the sum of the case durations:

- The phase journals measure an average concurrency of **1.00** in every slow row: 194416 CP-6, CP-7, CP-8, 214544 CP-6, CP-12 and fc32e512 CP-9.
- In the 214544 CP-6 TRX, the case durations add up to 1388 s, and first start to last end is also 1388 s.

Each case's cost is its own work, and nearly all of it is CPU spent on short-lived processes. A C1008 host fixture run
(`C1008HostFixture.Run`, `RollingVolumeRecycleScriptTests.cs:3427-3549`) is one `bash` interpreting the 10,747-line
`scripts/c590-remote.sh`. Every fake `docker` call starts its own `bash` plus `node` (`scripts/fixtures/c1008-fake-docker.sh:4`).
One run makes 4 to 665 fake Docker calls (median about 50 to 130), mostly `volume ls -q` and `volume inspect`.
In the live sample, the `remote.sh` bash process used 2-3 % CPU itself. Its reaped children used 1.02-1.06 cores of CPU for every
second of wall time, and the container PID namespace allocated about 150 PIDs a second. `node` was present in 121 of 240 samples.
`sleep` was present in 0 of them.

**Host load does not matter.** The same 149 C11* cases took 1197 s at load 5-13 (run 194416) and 1188 s at load 1-4
(run 214544), a ratio of 1.01. The host is idle while the class crawls: at 22:43-22:50Z the 1-minute load was 1.7-2.1 and
`top` showed 84 % idle while CP-12 ran.

## Sources

| Source | Used for |
|---|---|
| `/work/worktrees/ws-02-d2/.antiphon/checkpoints/20261117-194416-35d4/` (S0 `d2eef712` at `fead2ca07`): `report.md`, `host.txt`, `executor.log`, `rows/CP-{6,7,8}/phase/*/cases-*.jsonl` | Whole-class CP-6 and 14-class CP-8, both stopped at the 45-minute row deadline. CP-7 (jq) finished. |
| `/work/worktrees/ws-02-d2/.antiphon/checkpoints/20261117-214544-0d7d/` (S0 split plan `6d291e145`): `rows/CP-6/run.trx`, CP-6 and CP-12 phase journals | CP-6 = `C11*` (268 cases, complete). CP-12 = `C10*\|C12*`, **live**, read at 22:52Z: 42 complete, 1 in flight at 1444 s. |
| `/work/worktrees/task-fc32e512/.antiphon/checkpoints/20261117-102716-6a1f/rows/CP-9` | All 13 `VolumeRetirementApplyGateTests` cases |
| `.antiphon/task-932a7025.md` (commit `028fcf8c8`, 2026-11-01) | The only full-class receipt found: **RemoteScriptContractTests 511 cases in 59m57s**; RollingVolumeRecycleScriptTests 77 cases in 20m03s |
| `docs/superpowers/plans/2026-11-17-card-1960-checkpoints.md:46-54` | CARD-1960 CP-9 (`20261117-064318-744e`): 26 minutes of tests without finishing. Its receipts (`aaabade9` and `072158-ff3c`) are not on this runner mirror: **UNOBSERVED**. |
| Live CP-12 sample (`ps`, `/proc/<pid>/stat` over 10 s ×2, 240 × 0.25 s descendant samples, `/tmp/c1008-host-*/docker-trace.jsonl`) | Where a bash run spends its time |
| `C:\Antiphon\releases\candidates\rc-2026-11-24-…\logs\timing-…\summary.json` and the live Windows timing root | **UNOBSERVED**: Windows paths, not readable from the Linux runner |
| Host-load history `GET /api/hosts/server2/load/history` for 19:57-21:37Z and 22:00-22:50Z | `items: []`, `gaps: ["binding-unknown"]` (S0 `aaa774ee9` disables history until S1). The only load points are `host.txt` and `report.json`. |

The journal records are per-case start/end offsets with `phases` (`process-spawn`, `fixture-setup`, `our-code`, `fixture-teardown`)
and `processes` (direct children only). Spawns made through `LinuxShellResult` (`RemoteScriptContractSupport.cs:53-68`,
`bash -s`, or `wsl.exe -e bash -s` on Windows) are **not instrumented**, so C1192, C983, C912 and C1208 show 0 spawns but are not spawn-free.
The TRX durations match the journal: 268/268 cases, 1388 s.

## (a) Per-class tables

Durations are seconds of case wall time. Where a case appeared in more than one journal, the latest completion is used.

| Class | Cases (source) | Observed | Sum s | p50 | p95 | max | Instrumented spawns | <1 s cases | ≥30 s cases (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| RemoteScriptContractTests | 516 (384 + 132 `C1105_Git_audit_hidden_locations`) | 462 | 3361 | 0.6 | 37.5 | 315.0 | 466 (bash 229) | 324 | 37 (2534) |
| RollingVolumeRecycleScriptTests | 80 | 33 | 1010 | 6.0 | 140.2 | 213.9 | 180 (pwsh 112, bash 33) | 13 | 8 (874) |
| RetiredTempContainerHostTests | 22 | 22 | 794 | 27.0 | 92.7 | 136.6 | 359 (bash 180) | 2 | 9 (649) |
| C1364BoundLandWaitProofTests | 9 | 9 | 593 | 40.3 | 193.4 | 193.4 | 108 (bash 45, pwsh 18) | 0 | 5 (512) |
| HostJqPrerequisiteScriptTests | 40 | 40 | 506 | 1.8 | 85.3 | 144.5 | 108 (pwsh 92) | 15 | 3 (361) |
| VolumeRetirementApplyGateTests | 13 | 13 | 366 | 4.6 | 76.3 | 118.0 | 68 (bash 32) | 5 | 4 (323) |
| RetiredTempContainerScriptTests | 16 | 16 | 219 | 8.6 | 34.1 | 78.7 | 105 (pwsh 97) | 0 | 2 (113) |
| RollingProductionMountTests | 12 | 12 | 43 | 2.4 | 8.6 | 9.0 | 34 | 6 | 0 |
| C0824BranchDeletionRecycleAuditTests | 3 | 3 | 30 | 11.1 | 11.5 | 11.5 | 26 (git 13) | 0 | 0 |
| CheckpointImportTests | 34 | 34 | 5 | 0.1 | 0.3 | 0.8 | 0 | 34 | 0 |
| C1008PlatformContractTests / C1008HostFixtureSetupInsertionTests / RollingVolumeRecycleCensusChurnTests | 6 / 3 / 1 | all | 2 / 0 / 0 | — | — | 1.4 | 0 | — | 0 |
| VolumeRetirementContentTests / ReceiptTests / WrapperTests | 41 / 27 / 7 | 0 | **UNOBSERVED** | | | | | | |

Whole-class totals:
- **RemoteScriptContractTests:** at least 3361 s (56 min) for 462 of 516 cases. 54 cases are unobserved: C1008 ×3, C1316, C1361 ×2,
  C1697, C1066 ×10, C1207 ×5, C1208 ×25, C913 ×6, C1087 and C1331. The 2026-11-01 full receipt measured 59m57s for 511 cases.
- **CP-8 recycle group (14 classes, 274 cases):** 143 cases completed in 2689 s before the deadline. The in-flight case was
  `RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain_absent`.

By method family (RemoteScriptContractTests, observed):

| Family | Cases | Sum s | Mean s | Note |
|---|---:|---:|---:|---|
| C1008 (recycle, host fixture) | 11 of 15 | ≈1390 | ≈126 | 4 to 16 fixture runs per case, 7-33 s each |
| C1105 (git audit, resume) | 218 | 985 | 4.5 | 132 hidden-location cases total 70 s; the ~10 `Resume_*`/`Redeploy_*` cases take ~900 s |
| C1192 (workspace contract) | 31 | 348 | 11.2 | Eight `Cleanup_*_requires_positive_absence` at 29-38 s each |
| C983 (required jq) | 7 | 175 | 25.0 | Case #1 runs 36 pwsh driver invocations (9 selectors × keep × external) |
| C849 | 32 | 164 | 5.1 | |
| C912 | 9 | 72 | 8.0 | |
| All others (≈150 cases) | | ≈130 | <1 | Mostly text guards. They still hold the 1-wide lane. |

### RemoteScriptContractTests: 15 slowest cases

| Case | s | Instrumented spawns | Longest single spawn s | Run |
|---|---:|---:|---:|---|
| C1008_Recycle_receipt_records_disk_and_partial_failure | 315.0 | 32 | 25.3 | 214544/CP-12 |
| C1008_Recycle_refuses_uninspectable_git | 267.9 | 40 | 25.0 | 214544/CP-12 |
| C1008_Recycle_resume_requires_matching_receipt | 221.8 | 24 | 26.6 | 214544/CP-12 |
| C1008_Recycle_resume_requires_matching_receipt_drift | 175.8 | 32 | 21.4 | 214544/CP-12 |
| C1008_Recycle_refuses_unpublished_and_dirty_work | 140.7 | 24 | 19.6 | 214544/CP-12 |
| C1008_Retire_temp_rechecks_absence_and_retirement | 93.1 | 36 | 32.5 | 214544/CP-12 |
| C1105_Resume_identity_refusals | 74.0 | 24 | 11.4 | 214544/CP-6 |
| C1105_Redeploy_accepts_previous_generation_and_requires_new_bind | 71.4 | 6 | 36.6 | 214544/CP-6 |
| C983_Required_jq_refuses_unavailable_probe_before_any_group (#1) | 70.8 | 0 (uninstrumented pwsh) | — | 194416/CP-6 |
| C1008_Recycle_resume_requires_matching_receipt_recreated | 65.1 | 8 | 26.9 | 214544/CP-12 |
| C983_Required_jq_driver_rejects_successful_false_probe | 64.9 | 0 (uninstrumented) | — | 194416/CP-6 |
| C849_Deploy_prepares_and_verifies_before_acceptance | 54.8 | 12 | 32.8 | 194416/CP-6 |
| C1105_Git_audit_volume_scale | 49.4 | 0 (uninstrumented) | — | 214544/CP-6 |
| C1105_Resume_digest_pins_previous_sha12 (#1) | 46.2 | 4 | 24.2 | 214544/CP-6 |
| C1008_Retire_temp_reclaims_below_cache_disk_gate | 43.0 | 4 | 32.4 | 214544/CP-12 |

### Recycle and volume-retirement group: 15 slowest cases

| Class | Case | s | Direct children |
|---|---|---:|---|
| RollingVolumeRecycleScriptTests | C1008_Legacy_rolling_and_jq_rosters_remain_present | 213.9 | none recorded |
| C1364BoundLandWaitProofTests | C1364_Host_recycle_admission_reports_waited | 193.4 | bash 20, chmod 20 |
| RollingVolumeRecycleScriptTests | C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts | 143.9 | pwsh 1 |
| RollingVolumeRecycleScriptTests | C1008_Host_proof_admits_only_an_authenticated_private_origin | 140.2 | bash 25, chmod 20, git 7 |
| C1364BoundLandWaitProofTests | C1364_Host_recycle_post_removal_reports_waited_temp_volumes | 139.1 | bash 8, chmod 8 |
| RetiredTempContainerHostTests | C1609_Audit_uses_current_image | 136.6 | bash 18, chmod 15, git 2 |
| VolumeRetirementApplyGateTests | Scripted_apply_refuses_until_the_operation_token_matches | 118.0 | bash 13, chmod 13, node 4 |
| RollingVolumeRecycleScriptTests | C1008_Busy_routed_and_land_in_flight_refuse | 113.1 | pwsh 44 |
| RollingVolumeRecycleScriptTests | C1361_Wrapper_census_tolerates_open_status_changes_between_passes | 100.4 | pwsh 4 |
| RetiredTempContainerHostTests | C994_Each_removal_rechecks_live_proofs | 92.7 | bash 16, chmod 16 |
| RetiredTempContainerHostTests | C1105_Temp_cleanup_uses_previous_generation | 92.3 | bash 11, chmod 8 |
| RetiredTempContainerHostTests | C994_Removal_failure_and_crash_retry_preserve_progress | 83.3 | bash 16, chmod 16 |
| RollingVolumeRecycleScriptTests | C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail | 80.6 | pwsh 4 |
| RetiredTempContainerHostTests | C1609_Cleanup_does_not_require_historical_image | 78.7 | bash 7, chmod 7 |
| RetiredTempContainerScriptTests | C994_Verifier_manifest_is_strict | 78.7 | pwsh 60 |

The HostJqPrerequisiteScriptTests slowest cases are `C1025_Preflight_precedes_every_phase` (144.5 s, 74 instrumented spawns),
`C1025_Receipt_requires_complete_current_proof` (131.2 s) and `C1025_Preflight_is_fresh_for_each_entry` (85.3 s). The other 37 cases total 145 s.

## (b) Where the time goes

| Bucket | Evidence | Share |
|---|---|---|
| **Serialization (limiter queueing)** | Average concurrency 1.00. TRX case sum = span. A case does not wait inside its own timing; it waits for every case before it. Inside a row that holds only limiter classes, the queue is the whole parallelizable fraction. | All of the row wall beyond its longest case. RemoteScript: about 3,600 s serial against a 315 s critical path. |
| **Fork/exec-bound shell work in one run** | Live CP-12: bash self CPU 230-270 ms per 10 s; reaped children 10.2-10.6 s CPU per 10 s; about 150 new PIDs a second. One `C1008HostFixture.Run` takes 7-33 s. | Most of the C1008, C1105 resume, Rolling, RetiredTemp, C1364 and VolumeRetirement wall |
| ↳ Fake Docker = bash + node per call | `c1008-fake-docker.sh:4` runs `node -` for every call. `node -e ''` costs 35-67 ms here. The trace files show 4-665 calls per run (median ~50-130), mostly `volume ls -q`/`volume inspect` pairs. `node` was present in 50 % of samples. | About half of a C1008 run (sample-based) |
| ↳ pwsh starts | 92 pwsh starts in HostJq, 112 in Rolling, 97 in RetiredTempScript, plus uninstrumented C983 drivers (36 in one case). `pwsh -NoProfile -Command 'exit 0'` costs 0.32-0.60 s here. | Roughly 1-2 minutes per class from startup alone; the rest is script work |
| Fixed sleeps and poll loops | 0 of 240 samples had a `sleep` descendant during C1008 runs. The fixture sets `C1008_LAND_WAIT_SECONDS=1` and `C1008_LAND_POLL_SECONDS=1` (`RollingVolumeRecycleScriptTests.cs:3497`). `c590-remote.sh` sleeps 5/30/45 s at lines 312, 1027, 6882, 9760-10572; those are live-docker/ssh paths not seen in the fixture sample. `c1192-workspace-contract.sh:682` sleeps 8 s by default and `:1137` sleeps 2 s, only for late-slot scenarios. `c1008-retirement.sh:80` retries with `sleep "$delay"`. The fake Docker lock retry uses `Atomics.wait` for 200 ms (`c1008-fake-docker.sh:270-273`). Per-case sleep totals for C1192 are **UNOBSERVED** (uninstrumented `bash -s`). | Small for C1008. Unknown but bounded for C1192 (8 cases × ~30 s total 240 s). |
| Per-case fixture setup | Journal `exclusive.fixture` is 1-2 s for the whole RemoteScript class. Each `chmod` spawn takes ~4 ms. Each run writes a 584 KB script copy, and `bash -n` of it costs 26-57 ms. | Negligible. Sharing one fixture across cases saves almost nothing in time. |
| Necessary work | The C1008 cases are scenario matrices. `C1008_Recycle_receipt_records_disk_and_partial_failure` (`RemoteScriptContractTests.cs:3147-3210`) runs 1 + 4×2 + 6 fixture runs in one case, each a full deploy/recycle pass of `c590-remote.sh`. | The CPU itself is real work, but it is single-threaded per run and independent across cases |

Process spawns per case: the direct children recorded are in the tables above. Grandchildren (fake Docker `node`, `jq`, `timeout`, subshells)
are visible only through the namespace PID rate, about 150 a second, which is about 1,000-5,000 processes per C1008 run. That figure is namespace-wide,
so other tenants are included; the host was otherwise quiet. WSL is the Windows path for `LinuxShellResult`, and its cost is **UNOBSERVED**.

## (c) The limit

- **The number:** `ProcessSpawnLimit.Limit => 1` (`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:15-18`, CARD-0050 S5).
  The limit is enforced per process: "The limiter is per process — it cannot cap a concurrent Antiphon.Agents.Pty.Tests run"
  (same file, lines 9-12; `docs/testing-and-build.md` Gotcha #18 and line 1543). It is not host-wide.
- **The reason:** ConPTY and FakeClaude interference. The lane was never sized for fixture-owned bash/node script models.
- **Scope:** 912 attribute uses in `tests/Antiphon.Tests`. In these classes, every method or class carries it,
  including about 190 cases that finish in under 1 s (324 under 1 s, minus the 132 hidden-location cases).
- **Precedents for wider lanes:** `GitProcessSpawnLimit` (Clamp(ProcessorCount/2, 2, 4), override `ANTIPHON_TEST_GIT_SPAWN_LIMIT`, CARD-1421)
  and `CheckpointToolSpawnLimit` (default 2, override `ANTIPHON_TEST_CHECKPOINT_TOOL_SPAWN_LIMIT`, CARD-1747).
- **Is it too low for 24 cores?** For these classes, yes. A run uses about one core. The host stayed about 85 % idle and the load was 1.7-2.1 while CP-12 ran.
  Load samples: run 194416 started at load 10.00/13.05/10.90 and ended at 6.57/5.64/5.04; run 214544 started at 1.05/3.31/4.25.
  Case durations did not move with load (ratio 1.01).
- **What raising it would change:** see (d)#1. The row's wall would fall toward max(longest case, sum ÷ width).
  CPU demand would rise to about `width` cores for the row. The build-slot broker still leases the row only `maxcpucount=6`
  (`executor.log:15-16`: CP-6 held lease `4c01d96a…` from 19:57:50 to 20:42:52, i.e. one of the broker's 4 slots
  (budget 4, per `2026-11-17-merge-259-timing.md`) for 45 minutes while using about 1 core).
- **Hazards:**
  - The fixture's own per-run deadline is 120 s. A deploy-parent run measured 22-30 s idle and over 30 s at load 19-23
    (`RollingVolumeRecycleScriptTests.cs:3517-3519`, CARD-1130). A wide lane on a host loaded by peer builds could push runs toward that deadline.

## (d) Top 5 changes, ranked by minutes saved

Estimates assume an S0-like row set on idle server2. The basis is the observed sums above. Unobserved cases are excluded, so the savings are lower bounds.

| # | Change | Saves (S0 plan) | Risk |
|---|---|---|---|
| 1 | Run fixture-owned script models on a wider lane (Linux width ≈ 6-8, e.g. a `ScriptFixtureSpawnLimit` like `GitProcessSpawnLimit`), and drop the limiter from pure text guards | ~85-100 min. RemoteScript about 56+ min goes to max(315 s, sum/6) ≈ 10 min. CP-17 about 27 min goes to ~5 min. CP-8 ≥17 min goes to ~4-5 min. CP-7 8.6 min goes to ~2.5 min. | Hidden shared state: host-wide process census reads (C1361/C1374/C1697 "host census" cases), real-checkout reads, fixed `/tmp` names, the 120 s per-run deadline under CPU load. Windows WSL concurrency is UNOBSERVED. It needs a per-class census of what each case touches before widening. A classify-by-what-is-spawned rule already exists (Gotcha #18). |
| 2 | Make fake Docker resident or in-process instead of `bash`+`node` per call. Cache `volume ls`/`inspect` results in the fake. | About 50 % of C1008-family runs: ~10 min of RemoteScript C1008/C1105-resume, ~5-8 min across Rolling, RetiredTempHost, C1364 and VolumeRetirement. Serial-equivalent, and it stacks with #1's critical path (315 s case to ~160 s). | Fake fidelity: GNU `timeout` execs `docker` by name (`RollingVolumeRecycleScriptTests.cs:3431-3433`), so an executable shim must remain. Lock semantics (`Atomics.wait` retry) must stay. |
| 3 | Split the C1008 scenario matrices into one case per scenario. For example, `receipt_records_disk_and_partial_failure` has 15 runs in one case. | 0 serial. With #1 it cuts the critical path from 315 s to ~35 s, so the rows become bounded by sum/width. | Low. More case names in pins, rosters and `test-execution-policy.json` (CARD-1050 Linux-only lists name methods). |
| 4 | Split the class into parallel TUnit processes by marker (separate rows or processes, one limiter each) | Same order as #1 without code changes to the limiter: N processes give about ÷N. The S0 6d291e145 split already does this by rows, but the rows run `Serial: true`. | CPU and slot accounting: each process takes a build-slot lease. Do not co-schedule with `Antiphon.Agents.Pty.Tests` (the per-process limiter cannot see across). Per-test DB isolation is not needed here (no DB). |
| 5 | Move pwsh-driver cases in-process or pool the pwsh host (HostJq 92 starts, Rolling 112, RetiredTempScript 97, C983 36 per case) | ~3-5 min of startup across the group, at 0.3-0.6 s per start. More if the scripts' own work is cheap: UNOBSERVED. | The scripts under test are the pwsh entry points, so testing them in-process changes what is proven. Pooling keeps the fidelity. |

Not ranked:
- "Replace fixed sleeps with event waits": the sample shows no sleep on the dominant C1008 path. Only C1192's late-slot
  8 s/2 s delays are candidates, with C1192 at 348 s in total.
- "Share one fixture across cases": fixture setup is 1-2 s per class.

**Proposed target:** the S0 17-row plan in **under 40 minutes total** on idle server2. The observed test time of its rows is about 130+ minutes serial:
CP-6 23 min, CP-12 ≥24 min and live, CP-17 ~27 min, CP-8 ≥17 min, CP-13 ~10 min, CP-7 8.6 min, CP-16 ≥6 min, CP-3/4 8 min.
#1 alone at width 6 puts every row under ~6 minutes. #1+#3 makes the critical path the slowest single scenario run (about 35 s).
The rest is the 4-minute cold build plus about 10 s startup per row.

## (e) What the Merge waiver of RemoteScriptContractTests loses

The waiver itself is **UNOBSERVED** in the sources read here; it rests on the brief and on the CARD-1960 plan:
"The whole class stays with the nightly Unit lane" (`2026-11-17-card-1960-checkpoints.md:52`).
`tests/test-execution-policy.json` (`profiles.rc.exclusions[163]`) excludes 44 Linux-only methods on Windows only, so Linux still requires them.
With the waiver, a merge that changes `scripts/c590-remote.sh` or its fixtures learns of a regression only at nightly or rc qualification.

Ten most valuable guards. The judgement is mine; the evidence is the test comments and names in `RemoteScriptContractTests.cs`.

1. `C1008_Recycle_refuses_unpublished_and_dirty_work` (:2372) and `C1008_Recycle_audits_work_as_1654` refuse the recycle
   while a work volume holds unpublished or dirty commits, so a recycle cannot destroy agents' unpushed work.
2. `C1105_Git_audit_hidden_locations` (:2852, 132 cases) refuses a private commit hidden in any Git-directory location
   (one case per location). It is the completeness check of that same audit.
3. `C1008_Recycle_refuses_uninspectable_git` and `C1008_Recycle_refuses_references_and_unknown_census`: an unreadable repository or
   unknown census fails closed instead of counting as clean.
4. `C1008_Recycle_resume_requires_matching_receipt` (:3042) and its `_drift` and `_recreated` variants: an interrupted recycle resumes only
   against its own journal.
5. `C1008_Recycle_receipt_records_disk_and_partial_failure` (:3147): an atomic receipt survives interruption at
   preflight, stop, remove and volume; it records df before and after.
6. `C1008_Retire_temp_rechecks_absence_and_retirement` (:3268) and `C1338_*` (:3391): temp retirement removes nothing without a
   ready preview and a post-removal absence check (the mutants M1-M3 in task 932a7025 went red here).
7. `C1192_Cleanup_*_requires_positive_absence` (8 cases): cleanup releases only resources it positively proved absent or owned.
8. `C1208_*` (59 cases): a recorded container, network or cidfile id never deletes a foreign or replaced object (identity reverification).
9. `C1105_Resume_post_removal_census_tracks_open_work*` (:3866) and `C1105_Redeploy_accepts_previous_generation_and_requires_new_bind` (:3444):
   the post-removal census still refuses open server2-bound work. The redeploy requires the new bind.
10. `C1316_Recycle_audit_cap_clears_the_standing_volume` (:2722): the audit cap (default 10800 s) lets redeploy-old pass on a large
    clean volume. Without it, the redeploy times out at the old 3000 s cap.

The roughly 190 sub-second text-ordering guards also go unchecked: `C849_Deploy_ordering_contract_is_pinned`, `Nested_lane_never_uses_sudo_or_python`, `Scrub_covers_*` and the `Deploy_parent_*` ones.
They cost about 2 minutes in total, but they are waived together with the slow ones because they share the class.

## Remaining uncertainties

- 54 RemoteScript cases are unobserved, including 3 heavy C1008 cases still running in live CP-12. CP-12 was at 1444 s with
  C1008_Recycle_refuses_references_and_unknown_census in flight, and may hit the 45-minute deadline again.
- VolumeRetirement Content/Receipt/Wrapper (75 cases): no Linux receipt on this mirror.
- Windows timings (`summary.json`) were not read.
- Grandchild spawn counts are namespace PID rates, not per case.
- The sleep content of uninstrumented `LinuxShellResult` cases (C1192, C983) is unknown.

## Not done, noted

- Fix idea, one line: add a wider `ScriptFixtureSpawnLimit` lane for fixture-owned shell models, with a per-class shared-state census, and a resident fake Docker.
- 65 leftover `/tmp/c1008-host-*` fixture roots (up to 11 MB each, timestamps 00:00-23:00) sit on the runner container. They are probably from deadline-killed rows. Not investigated.
