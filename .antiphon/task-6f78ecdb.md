# CARD-1804 Code report (task 6f78ecdb)

Outcome: CARD-1804 implemented test-first on `feat/card-task-6f78ecdb` (worktree `/work/worktrees/task-6f78ecdb`, server2 runner mirror, Linux). Base master `2bf29ee9dadbd08c3ae78c186e2af00a6deb290e`. No earlier partial implementation existed (`git log --all --grep CARD-1804` empty). Runner-side deletion guards untouched (`src/` unchanged). No migration, no `tests/test-execution-policy.json` change (policyHash / PinnedInputDigest not touched).

## Commits

| Role | SHA |
|---|---|
| red (witnesses + RED stubs: `StartExecuteAsync` wraps the old sync start, `StopAsync` returns at once) | `68af7d740307535e889fc7198b102ab0f20f7945` |
| fix | `f33f9f56101dadf2d3ccbff9939e56b3c18c2760` |
| docs | `f444af1188537aeb375fb84cfe785d3c269d2609` |
| tested (code = fix; docs-only on top) | `f444af1188537aeb375fb84cfe785d3c269d2609` (guards); classes on `f33f9f561` |

## What changed

- **A-1 cross-process lock** (`RunnerWorktreeRetentionService.TryLockAcrossProcessesAsync/UnlockAcrossProcessesAsync`): after the in-process turn every tick takes `pg_try_advisory_lock(hashtextextended('antiphon.runner-worktree-retention.tick',0))` on its context's held-open connection (no cancellation on the lock query; explicit unlock, pooled reset as backstop). Another server process: preview and execute 409 `tick_in_progress`; scheduled tick skips (logged, no run, no census). Advisory locks are per database, so cloned test databases do not contend.
- **A-2 terminal run state**: execute route (`RunnerWorktreeRetentionTicks.StartExecuteAsync`, endpoint now async) takes the lock and saves the run before the 202; the scope holding the lock passes to the background tick. `RunRunnerAsync` reuses that run, and on any exception finishes it with a Deferred `tick_failed` / `tick_cancelled` row (saves the evaluated rows too; if that save fails, clears the tracker and stamps the run alone). The background's `finally` (fresh scope) finishes any still-unfinished run (`tick_stopped_disabled` / `runner_not_listed` when the tick returned without running the runner). `RunnerWorktreeRetentionTicks` is now an `IHostedService` (registered in `server/Program.cs` via the singleton) whose `StopAsync` awaits `Background` within the host shutdown bound.
- **A-3 preview**: accepted explicitly as request-bound; a request abort now finishes the run with `tick_cancelled` (no deletion effect, DryRun). Docs say to give curl a generous `--max-time`.
- Docs: `docs/docker-stack.md`, `docs/ops-http.md`, `docs/antiphon-api.md`.

## Witnesses (`C1677RetentionServerTests`)

| Witness | Red at 68af7d740 (reason) | Green at f33f9f561 |
|---|---|---|
| `C1804_a_second_server_process_refuses_while_this_one_ticks` | TimeoutException, no ConflictException (second process censused) | pass |
| `C1804_execute_route_saves_its_run_before_it_answers_and_a_switched_off_tick_finishes_it` | run row missing at the 202 ("Sequence contains no elements") | pass |
| `C1804_a_failed_tick_finishes_its_run_with_tick_failed(execute-route)` | FinishedAt null | pass |
| `C1804_a_failed_tick_finishes_its_run_with_tick_failed(scheduled)` | FinishedAt null | pass |
| `C1804_host_stop_awaits_a_running_execute_tick_and_its_run_is_finished_cancelled` | Background not completed after StopAsync | pass |
| `C1804_a_preview_cancelled_by_its_request_finishes_its_run_cancelled` | FinishedAt null | pass |

Test seams added to the World: `Monitor` is internal with a `Hold` gate that parks reads outside the route's AsyncLocal flow (the background tick); `ExecuteRouteAsync(ticks:)`; `RunRowsAsync(runId)`.

## Mutants (scratch, restored with `git checkout --`; two builds)

| Mutant | Killed by |
|---|---|
| M1 lock always granted | second-process witness (TimeoutException instead of 409) |
| M2 execute run not saved before 202 | execute-route-saves witness ("Sequence contains no elements") |
| M5 StopAsync does not await Background | host-stop witness (IsCompleted false) |
| M7 no tracker-clear fallback in FailRunAsync | failed-tick(scheduled) (FinishedAt null) |
| M3 no FailRunAsync in RunRunnerAsync catch + M4 no stamp in background finally (one build) | failed-tick both shapes, host-stop, execute-route-saves, preview-cancelled (all FinishedAt null); second-process unaffected as expected |

Batch 1 (M1, M2, M5, M7): 6 run, 4 failed as intended, 2 passed (failed-tick(execute-route) covered by the background stamp; preview-cancelled unaffected) — expected. Batch 2: 6 run, 5 failed as intended.

## Runs (server2 Linux, build-slot leased per command, native default output, `-p:UseAppHost=false`, TEMP=/tmp)

| Run | Source | Filter | Result |
|---|---|---|---|
| build red | 68af7d740 | `dotnet build tests/Antiphon.Tests` | ok 4m02s, slot waited=0s |
| red witnesses | 68af7d740 | `/*/*/C1677RetentionServerTests/C1804_*` | 6 total, 6 failed (intended reasons) |
| build fix | f33f9f561 | same | ok 5m09s, waited=0s |
| touched classes | f33f9f561 | `/*/*/(C1677RetentionServerTests*)\|(C1677RetentionSettingsTests*)\|(C1677TempSweepServerTests*)\|(C1677DiskAlarmTests*)/*` | **173 total, 173 passed, 0 failed, 0 skipped** (TRX 96/18/7/52) |
| mutant batch 1 build+run | f33f9f561 + M1,M2,M5,M7 | C1804_* | 4 failed / 2 passed (as intended), waited=15s |
| mutant batch 2 build+run | f33f9f561 + M3,M4 | C1804_* | 5 failed / 1 passed (as intended), waited=15s |
| build final | f444af118 (restored) | same | ok 4m00s, waited=0s |
| guard batch + doc adjacency | f444af118 | `(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(DockerStackDocumentationTests*)\|(DockerStackContractTests*)\|(Server2ProductionServerContractTests*)\|(Server2StagingServerContractTests*)` | **234 total, 234 passed, 0 failed, 0 skipped**; guards 77/77 (the nine CARD-1796-era inherited reds are green at this base), docs adjacency 157 |

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 2bf29ee9d -HeadRef f444af118`: commits=3 entries=0 violations=0.
- `git merge-tree --write-tree origin/master f444af118`: clean, tree `ad3d65cab258f20118bfe14197da8411ebc80299` (origin/master = base).
- No checkpoint plan exists for CARD-1804 (no `### Checkpoints` table); the runs above are the leased direct runs the brief named. Unlisted runs: the two mutant builds/runs (brief requires a mutant per witness).

## Owed / not run

- Whole Unit lane: nightly/rc owned, not run.
- Runner test project: not run (runner unchanged).
- Real two-process check against a live server2 + AppHost pair: not done (nothing deployed); the witness uses a second tick turn + DI scope/connection on the same database.
- PCs for SourceLanding Mutation (pending, method-scoped): PC-1 lock always granted (M1) / unlock skipped; PC-2 execute run not saved before 202 (M2); PC-3 RunRunnerAsync catch stamp removed (M3); PC-4 background finally stamp removed (M4); PC-5 StopAsync not awaiting (M5); PC-6 FailRunAsync fallback removed (M7); PC-7 reason mapping swap (`tick_cancelled`/`tick_failed`, `tick_stopped_disabled`/`runner_not_listed`).
- OS-branch witnesses: none (server logic only; advisory lock is Postgres, OS-neutral).
- Performance: green durations of the new cases 0.36-0.70 s each (class run 65 s held); no selection over 30 min.
