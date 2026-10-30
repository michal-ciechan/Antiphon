# Task 6ea254d2 — S3 hang detection and watchdog admission

S3 is committed, pushed, and checkpoint-green on Linux. S4, the safe S5 piece, S2, and S6 are left. No suite watchdog was lowered. `tests/test-execution-policy.json` is unchanged. No project definition file (csproj/props) was touched, and land-graph `-Update` was not run.

## Commits

Branch `feat/card-task-6ea254d2`, parent `cbdf5f76bf4b14845f1e15fd8961065ace0c14c6` (S1 tip).

- `871230f10c3826ffbc3aa6cbf9a7820d4fafc56b` — `test: fail when the rc watchdog admission proof is absent`
- `5b9dd4a649bd369d4eb5e9927e4e800a01755323` — `fix: pair launch watchdogs with admitted cost and record hang progress`

The checkpoint below verified the fix SHA. This document is a later report-only commit. Mutation was not run; this task is not a mutation pass.

## Files in the fix

- `scripts/lib/nightly-watchdog-admission.ps1` (added)
- `scripts/lib/nightly-tests-impl.ps1` (admission handoff only)
- `scripts/lib/qualification-supervisor-heartbeat.ps1` (UID progress; detection only)
- `tests/Antiphon.Tests/Scripts/Fixtures/rc-full-watchdog-witness.ps1` (added)
- `tests/Antiphon.Tests/Scripts/RcFullWatchdogTests.cs` (added, four methods)
- `tests/Antiphon.Tests/Scripts/RcFullWatchdogHarnessTests.cs` (added, two methods)

## What S3 does

`Resolve-NightlyLaunchWatchdog` keeps the existing suite watchdog unless a measured calibration is eligible. Eligibility requires Basis `measured`, n >= 20, Censored not true, the 11 stratum keys matching, BoundPolicyHash equal to the live policy hash, and finite E95/Emax/M/C/Wmax. Anything else, including a missing calibration, returns the suite watchdog with reason `unmeasured-cost`. The live policy has no `watchdogCalibration` property, so production stamps Basis `default` and stays on the suite watchdog. Session-runner remains 1800000 ms. Ordinary ceiling C is 2400 s; E2E (`Antiphon.E2E.*` or suite id `e2e`) C is 1800 s. P above that ceiling is refused. When a measured W(P) or W(C) exceeds Wmax, Admitted is false and WatchdogSeconds stays the computed value (the proof case is 2040, not a clamp to Wmax).

Formula: nearest-rank p95 via decimal `ceil((95 * n) / 100)`, then ceil-to-60 of `max(600, 2 * E95 * P, Emax * P + M)`. Failed walls stay in the sample. The synthetic proof is n=20, nineteen 900 s walls plus one Failed 1936 s wall, P=900, M=60, rank 19 so E95=1, Emax=1936/900, W=2040. A second stratum (nineteen 120 s walls plus one complete 900 s wall, FailureCount=3 as one sample) yields W=960.

The native launch path calls that resolver before `Invoke-NightlyOwnedProcess` and passes `admission.WatchdogMilliseconds` as the timeout. A timeout still sets outcome `timeout {suiteId}` through `Get-NightlyWatchdogTimeoutDisposition` (CoverageComplete false, TestsPassed false). Partial diagnostics are kept. Hang suspicion in the heartbeat records `hangReason` and `suspectPids` and does not kill. New diagnostic fields (`lastCompletedUid`, `lastCompletedAtUtc`, `lastDiagGrowthAtUtc`, `inFlightUid`, `lastPhase`) reset idle when the bounded UID scan grows, and copy forward when `-DiagnosticProgress` is absent. The existing 17 heartbeat assertions were not given new PASS lines.

## Checkpoint

Run `20261030-025019-73e1`. Manifest imported 19 rows; this invocation selected CP-8, CP-9, CP-17, CP-18, CP-19. Host Debian GNU/Linux 12 (bookworm). `sourceState=clean`, `buildSource=verified`. Verdict GREEN, exit 0. Tool summary wall 7m23s, BUDGET wall=443.444s execution=443.439s. `unlisted: none`. The tool deleted the project output `bin-rcspeed-b2log/`. Phase lines show one product build (CP-8, 255.4532951s) and four reused rows (`build=reused`, build phase 0s). The unedited summary line still says `builds: 8`.

CHECKPOINT CP-8 commit=5b9dd4a649bd369d4eb5e9927e4e800a01755323 build=ok filter=/*/Antiphon.Tests.Checkpoints/CheckpointExecutorLogTests/(concurrent_callbacks_append_each_line_once_without_overlap*)|(note_returns_when_the_sink_resumes_on_the_caller_context*)|(repeat_dispose_completes_after_the_sink_dispose_faults*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-6ea254d2/.antiphon/rcspeed/5b9dd4a649bd369d4eb5e9927e4e800a01755323/s3/20261030-025019-73e1/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=5b9dd4a649bd369d4eb5e9927e4e800a01755323 sourceState=clean buildSource=verified

CHECKPOINT CP-9 commit=5b9dd4a649bd369d4eb5e9927e4e800a01755323 build=reused filter=/*/Antiphon.Tests.Scripts/(RcFullWatchdogTests*)|(ReleaseGatePolicyTests*)/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-6ea254d2/.antiphon/rcspeed/5b9dd4a649bd369d4eb5e9927e4e800a01755323/s3/20261030-025019-73e1/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=5b9dd4a649bd369d4eb5e9927e4e800a01755323 sourceState=clean buildSource=verified

CHECKPOINT CP-17 commit=5b9dd4a649bd369d4eb5e9927e4e800a01755323 build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogHarnessTests/HeartbeatExistingContract executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-6ea254d2/.antiphon/rcspeed/5b9dd4a649bd369d4eb5e9927e4e800a01755323/s3/20261030-025019-73e1/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=5b9dd4a649bd369d4eb5e9927e4e800a01755323 sourceState=clean buildSource=verified

CHECKPOINT CP-18 commit=5b9dd4a649bd369d4eb5e9927e4e800a01755323 build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogHarnessTests/C1397ExistingContract executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-6ea254d2/.antiphon/rcspeed/5b9dd4a649bd369d4eb5e9927e4e800a01755323/s3/20261030-025019-73e1/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=5b9dd4a649bd369d4eb5e9927e4e800a01755323 sourceState=clean buildSource=verified

CHECKPOINT CP-19 commit=5b9dd4a649bd369d4eb5e9927e4e800a01755323 build=reused filter=/*/Antiphon.Tests.Scripts/RcRosterRepairTests/Costs executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-6ea254d2/.antiphon/rcspeed/5b9dd4a649bd369d4eb5e9927e4e800a01755323/s3/20261030-025019-73e1/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=5b9dd4a649bd369d4eb5e9927e4e800a01755323 sourceState=clean buildSource=verified

PHASES CP-8 build=255.4532951s testsWall=0.1227635s. CP-9 testsWall=41.0420421s. CP-17 testsWall=3.2288982s. CP-18 testsWall=11.1038752s. CP-19 testsWall=3.6999423s. Receipt: `.antiphon/rcspeed/5b9dd4a649bd369d4eb5e9927e4e800a01755323/s3/20261030-025019-73e1/report.md`.

## Left

Stopped at this green slice boundary. S4 exact-leaf packing and the safe independent part of S5 were not started. S2 stays excluded: the brief's native verify says the 20 s post-test linger did not reproduce (0.15 s). S6, the Windows parallelism pilot, stays excluded.

Owed native rows this task did not run: CP-2 (Windows owner), CP-4 (Windows E2E), CP-6 (Linux host provider; A4 deferred and not authored here), CP-7 (Windows, S2). S6's Windows measurement is also unrun. CP-10 through CP-16 belong to slices that were not started.

## Deviations

- Calibration is checked inside `Resolve-NightlyLaunchWatchdog`. `Get-NightlyPolicyHash` and `Read-NightlyExecutionPolicy` were not rewritten, and no `watchdogCalibration` section was added. An absent calibration is the fail-closed `unmeasured-cost` path.
- Plan line 255 asks for script-census rows in the execution policy. Those rows were not added. The brief forbids editing `tests/test-execution-policy.json`. The witness script lives under `tests/Antiphon.Tests/Scripts/Fixtures/` and is not a nightly script. The wrappers call scripts that already exist.
- Class cost values stay per-node doubles. Provenance is the separate Basis stamp, `default` on the planner path, so the suite watchdog is what gets armed.
- The five brief sources (rc-run time breakdown `2714a65f`, design reviews `16008df1` and `88500c23`, S1 review `a45b2ddc`, S1 native verify `b9561ea9`) were not re-read in this closeout. The slice followed the plan and the production scripts. The linger figure used to exclude S2 is the figure stated in the brief.

## OS witness

`scripts/check-os-witnesses.ps1` exit 0 against `.antiphon/rcspeed/5b9dd4a649bd369d4eb5e9927e4e800a01755323/s3/os-witness.json`. Output: six CHANGED head paths, CANDIDATE base line 21 and head line 22 of `nightly-tests-impl.ps1` (pre-existing `$IsLinux`, classified non-behavior), `NATIVE pending restartImpact`, then `OS-WITNESS complete syntax/inventory; execution and runtime identity require Review`. Exit 0 is syntax and inventory only. No Windows execution is claimed. `restartImpact.required` is true because the three `scripts/lib` paths match the restart census. Native obligation status is `pending`, command `not-run`.

## Evidence diff

`scripts/check-evidence-diff.ps1 -BaseRef cbdf5f76bf4b14845f1e15fd8961065ace0c14c6 -HeadRef 5b9dd4a649bd369d4eb5e9927e4e800a01755323` exited 0. `commits=2 entries=0 violations=0`.

## Land

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` against `98fc3ceb6440a17b68cafe3ecc21c76d190b18ca` exited 1. Not merged.

The same command at the S1 parent already conflicted on these nine paths:

- `.antiphon/task-af2b78c8.md` (add/add)
- `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` (add/add)
- `docs/orchestration-loop.md` (content)
- `scripts/lib/nightly-tests-impl.ps1` (content)
- `scripts/lib/test-phase-evidence.ps1` (content)
- `server/Application/Services/CapacityRecoveryService.cs` (content)
- `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs` (add/add)
- `tests/Antiphon.Tests/Application/LandGraphEvaluationScriptTests.cs` (content)
- `tools/Antiphon.LandGraphAudit/EvaluationBudget.cs` (add/add)

HEAD adds one path: `scripts/lib/qualification-supervisor-heartbeat.ps1` (add/add). Its blob at the S1 parent equals origin/master blob `1a90c0de22eaedbb3b9d9bbeef81444ed34b5b92`. The conflict is the S3 progress-identity edit (+126/-2 against that shared blob). `nightly-tests-impl.ps1` was already conflicted; this slice's admission dot-source, planner stamp, native timeout handoff, and timeout disposition sit in that file. The new admission library and the three test files did not appear in the conflict list.

## Cleanup

Owned `bin-rcspeed-s3red` and `bin-rcspeed-s3-boot` directories were deleted by literal path. The checkpoint tool deleted the project `bin-rcspeed-b2log/` output. The run's evidence copy under the results directory was kept.

--- next stage ---
next: review
handoff: Review S3 behavior at 5b9dd4a649bd369d4eb5e9927e4e800a01755323. Closed list is CP-8, CP-9, CP-17, CP-18, CP-19, run 20261030-025019-73e1, GREEN. A later commit only adds this report. Do not treat the slice as a watchdog reduction.
artifact: /work/worktrees/task-6ea254d2/.antiphon/task-6ea254d2.md
