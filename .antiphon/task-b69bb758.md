# CARD-1223 Review, ordinary round 1 (Final), task b69bb758

Verdict: behavior CLEAN. No product blockers. `reviewedSourceClean` is false because the planned Windows skip fails the strict zero-skip certificate. Three advisories, none a landing condition by themselves. Land still needs a human choice on that certificate.

Subject: `origin/feat/card-task-f4b68058` tip `0de5aeb670eddbcce9a3a4d06d1d473efd1c77b5`
(Grok Code task `f4b68058-a7af-434b-aa53-04a127751c8d`). Base `ef7afd44927206f6c03547670a5add84130bdce9`.
Range `ef7afd44927206f6c03547670a5add84130bdce9..0de5aeb670eddbcce9a3a4d06d1d473efd1c77b5`: 4 commits, 9 files, +369/-7.
Plan `docs/superpowers/plans/2026-11-09-card-1223-linux-rc-watch-probe.md`.
Red `ac1069cbcdc95c0fab9ff36e7166d1dd18ac1ad4`. Compile-only repair `a154ec15135e3bca306c4ff4b262649a8e77b02a` (illegal delegate replaced by an inactive `Probe(int, bool)` spelling; assertions unchanged). Fix `0de5aeb670eddbcce9a3a4d06d1d473efd1c77b5`.

## Behavior

Off Windows, `WindowsRcBridgeProbe.Probe(int)` passes `OperatingSystem.IsWindows()` and the false overload returns `ConnectionCountMeasured` false, `Armed` false, `StateFileFound` false, `BridgeConnections` 0, without reading a session file or calling `GetExtendedTcpTable`. The Windows body, including the catch that returns a measured 0, is unchanged. `Program.cs:662-663` still registers `WindowsRcBridgeProbe`. `RcWatchSettings.Enabled` stays default true (`SupervisionSettings.cs:274`). No appsettings or compose key, no throw, no startup refusal. `SessionHealthHostedService` still ticks.

`WatchRcAsync` classifies first. `StateFileFound` false is `Unknown`, so the omission does not take the unarmed auto-arm path. After the idle return, `ConnectionCountMeasured` false logs the omission sentence once (`Entry.OmissionWarned` on the singleton store, keyed by session id, not cleared on generation change) and returns false. It does not increment or clear `ConsecutiveZeroConnProbes` and does not record `RcDegraded`. A later measured sample still uses the healthy or zero path. The hosted service awaits one tick at a time. An entry is removed only when that session leaves the live candidate set, so the flag does not move to another session id.

Production readers of the probe:

- `SessionHealthService` is the only reader of `BridgeConnections`, and only after the measured check.
- `RemoteControlBridgeClassifier` and `RemoteControlRecoveryService.ProbeBridge` ignore the count. Missing state is `Unknown`, which withholds automatic `/remote-control`.
- `AgentSessionService` preflight uses that classifier. Resume polling reads `.Armed` only. The omission's `Armed` false matches the old missing-file result and is not a healthy count.

Runner-hosted Linux Claude sessions stay on the plan's supported omission: the server process is the wrong pid and network namespace, so this card does not add a `/proc` count or a runner RPC. Launch preflight still records the existing Unknown withhold. The idle health tick logs the omission once and does not invent `RcDegraded`. That is the monitoring the plan states.

`C514_Armed_zero_connections_never_reserves_arm` assertions are untouched. The harness still uses `ConsecutiveFailedProbesBeforeAction` 2, so two unmeasured ticks would have recorded `RcDegraded` if the flag were dropped.

V-3 `Windows_probe_reports_a_measured_count` is NotExecuted on this host. It stays pending native work. PC-1 through PC-9 stay pending for method-scoped SourceLanding Mutation.

## Rerun

Checkpoint tool run `20261110-231147-bbe0`, plan rows CP-1 and CP-2, one build `bin-c1223/`, slot granted, exit 0. Receipt: dirty=0, sourceState=clean, buildSource=verified, source `0de5aeb670eddbcce9a3a4d06d1d473efd1c77b5`.

    CHECKPOINT CP-1 commit=0de5aeb670eddbcce9a3a4d06d1d473efd1c77b5 build=ok filter=/*/*/RcBridgeProbePlatformTests/* executed=6 passed=6 failed=0 skipped=1 trx=/work/worktrees/task-b69bb758/.antiphon/checkpoints/20261110-231147-bbe0/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=0de5aeb670eddbcce9a3a4d06d1d473efd1c77b5 sourceState=clean buildSource=verified
    CHECKPOINT-TIMING CP-1 waited=0s executed=8.195s wall=8.284s
    PHASES CP-1 slotWait=0s build=364.2570564s startup=6.7041355s testsWall=0.0552098s teardown=0.4079505s hostWall=7.167315s
    CHECKPOINT CP-2 commit=0de5aeb670eddbcce9a3a4d06d1d473efd1c77b5 build=reused filter=/*/*/SessionHealthTests/(Unavailable_connection_count_does_not_record_rc_degraded*)|(C514_Armed_zero_connections_never_reserves_arm*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-b69bb758/.antiphon/checkpoints/20261110-231147-bbe0/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=0de5aeb670eddbcce9a3a4d06d1d473efd1c77b5 sourceState=clean buildSource=verified
    CHECKPOINT-TIMING CP-2 waited=0s executed=30.241s wall=30.305s
    PHASES CP-2 slotWait=0s build=0s startup=17.3321084s testsWall=5.3290062s teardown=6.5706585s hostWall=29.2317594s

CP-1 TRX: six Passed (`Linux_probe_does_not_report_a_measured_zero`, `Forced_unsupported_platform_is_unmeasured`, `Docker_stack_records_the_supported_omission`, `Agent_kinds_records_the_linux_omission`, `Session_runtime_records_the_linux_omission`, `Health_source_logs_the_omission_sentence`) and `Windows_probe_reports_a_measured_count` NotExecuted. CP-2 TRX: both methods Passed. Build 364s, under the 30 minute stop. No container-start phase was separated from host startup.

`scripts/validate-checkpoint-receipt.ps1` on `report.json` for rows CP-1,CP-2: `CHECKPOINT SOURCE INVALID reason=row_failed` (exit 2) because CP-1 `skipped=1`. Rows CP-2 only: `CHECKPOINT SOURCE VALID source=0de5aeb670eddbcce9a3a4d06d1d473efd1c77b5 rows=1`. The complete selection therefore does not earn `reviewedSourceClean: true`.

Guard batch, one lease, not a plan row (brief-named nine-class union, which also covers the impacted `TestLaneCategoryGuardTests` row): `BUILD SLOT granted lease=8e8135c3-817e-437a-ba4b-c0816c28150e waited=0s`. `dotnet run --project tests/Antiphon.Tests -p:UseAppHost=false` with the nine-class filter. Result: total 78, succeeded 78, failed 0, skipped 0, duration 1m 42s, exit 0. The slot note says the implicit build did not receive `-maxcpucount` (handed to the test program). The command stayed inside the lease (`held=323s`, then released).

## Other gates

- `scripts/check-evidence-diff.ps1 -BaseRef ef7afd44927206f6c03547670a5add84130bdce9 -HeadRef 0de5aeb670eddbcce9a3a4d06d1d473efd1c77b5`: commits=4 entries=0 violations=0, exit 0.
- `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`: tree `6f556f64d79a8c57354c1bc7c413677cc2c79848`, exit 0. `origin/master` is `ef7afd44927206f6c03547670a5add84130bdce9`.
- `scripts/impacted-classes.ps1 -Range ef7afd44927206f6c03547670a5add84130bdce9..HEAD`: one row, `kinds=test-lane-category`, filter `/*/*/(TestLaneCategoryGuardTests*)/*`. Migrations kind not selected. Downgrade caller count 0. No migration file in the diff.
- `scripts/check-os-witnesses.ps1` against Code's declaration `/work/worktrees/task-f4b68058/.antiphon/card-1223-os-witnesses.json`: exit 0, syntax/inventory complete. `NATIVE pending W-windows`. `NATIVE pending restartImpact` (Code set `required: true` after the checker counted the probe as a post-start dependency; this card restarts nothing).

## Advisories

1. `docs/docker-stack.md` Claude RC watch owner cell still says `CARD-1223 (Backlog)`. The omission sentence replaced the silent-zero text, which is what the plan asked. The parenthetical is stale.
2. The plan says CARD-1782 selects nothing. The selector emits `TestLaneCategoryGuardTests` because the new test file is a category-census input. That class ran inside the 78.
3. Nothing asserts that the omission warning is logged once. Dropping `OmissionWarned` would still leave V-8 green. The plan added no S2 test for it. The flag's lifetime is the review above.

PCs stay pending. Filters are the plan's PC-1..PC-9 table.
