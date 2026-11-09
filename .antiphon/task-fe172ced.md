# CARD-1804 Review round 1 (task fe172ced)

Verdict: CLEAN. No blocker, no regression. Three advisories for a follow-up card; none holds the land.

Subject: Code task 6f78ecdb (`6f78ecdb-02a4-4a11-876e-5d37f93569ef`), branch `feat/card-task-6f78ecdb`, tip `ff7cab22c2295fefd27fb2fd339cb6d6d5eb7355` (report commit over the tested tip `f444af1188537aeb375fb84cfe785d3c269d2609`; product and test code identical at both). Base master `2bf29ee9dadbd08c3ae78c186e2af00a6deb290e`. Reviewed on the server2 runner mirror `/work/worktrees/task-fe172ced` (Linux); nothing ran on the desktop. The task record names `agentKind: ClaudeCode` where the brief said Grok; noted only.

## Checks (brief A-1..A-3, 4, 5)

- **A-1 lock.** `pg_try_advisory_lock(hashtextextended('antiphon.runner-worktree-retention.tick',0))` is a SESSION lock on the scoped `AppDbContext` connection, opened with `OpenConnectionAsync` and so held by EF for the whole tick (EF does not close a user-opened connection between queries). It never waits (try-lock), takes no caller cancellation, and is released by an explicit `pg_advisory_unlock` plus `CloseConnectionAsync` in a `finally` on every path: scheduled tick (`RunTickAsync`), preview (`RunPreviewAsync`), execute refused or failed before the background started (`StartExecuteAsync` finally), execute background (`RunExecuteAsync` finally, which also disposes the scope that carried the connection). Fail closed: `UseNpgsql` has no `EnableRetryOnFailure`, so an unreachable or dropped Postgres throws out of the lock query or the next tick query; the tick stops, nothing proceeds unlocked, the turn is released. Key space: the other bigint users are `grok-rules-brief:<guid>` (`hashtextextended`) and GUID-derived int64 (`ChannelBridgeService`); the `hashtext` int4 keys sign-extend into the same space; no shared string, collision negligible. Pool: one pinned connection per tick for at most `MaxTickMinutes` (20) against the default pool of 100; routes are exclusive with the scheduled tick, so never more than one. Second process: preview and execute answer 409 `tick_in_progress`, the scheduled tick returns `[]` with an Information log and writes no run (test `C1804_a_second_server_process_refuses_while_this_one_ticks` proves all three and that the lock releases). Cloned per-test databases do not contend (advisory locks are per database).
- **A-2 run state.** `StartExecuteAsync` takes the turn, then the lock, then `StartExecuteRunAsync` saves the run, then hands the scope to the background and returns the 202. Every exit finishes the run: `RunRunnerAsync` catch writes a Deferred `tick_failed` / `tick_cancelled` row (`tick_cancelled` only when the token is cancelled), saving the evaluated rows or, when that save fails, clearing the tracker and stamping the run alone; the background `finally` runs `FinishUnfinishedRunAsync` in a fresh scope (`tick_stopped_disabled` / `runner_not_listed` when the tick returned without running the runner); a finished or missing run is left alone, so no double finish. Host stop: `IHostedService.StopAsync` awaits `Background.WaitAsync(cancellationToken)` with the host shutdown token, so it cannot hang; `ApplicationStopping` fires before hosted-service stop, which cancels the tick first. The background task never faults (catch-all, logged).
- **A-3 preview.** Runs on the request token; a cancel finishes the run `tick_cancelled`, deletes nothing (DryRun), releases turn and lock (test proves it). Accepted and documented.
- **(4) Guards.** `src/` and `server/Migrations/` unchanged (diff stat empty). In the server the diff moves the census body into `CensusRunnerAsync` unchanged; `ClassifyAsync`, the send-time recheck, `MinTerminalHours`, dirty/unpublished/live checks (runner side) and the reason codes are untouched. No migration; the new reason codes are strings in an existing column.
- **(5) Tests and docs.** Six witnesses red at `68af7d740` for the stated reasons (below); each asserts a durable outcome (run row, FinishedAt, Deferred row, request counts, worktree path kept). Four mutants re-run by me, all killed as predicted. Three docs read against the code: accurate.

## Runs (build-slot leased per command, native default output, `-p:UseAppHost=false`, TEMP=/tmp)

| Run | Source | Where | Result |
|---|---|---|---|
| Build red | 68af7d740 | detached scratch worktree | ok 4m17s |
| Red witnesses `/*/*/C1677RetentionServerTests/C1804_*` | 68af7d740 | scratch | 6 total, 6 failed: preview_cancelled FinishedAt null; failed_tick(scheduled) FinishedAt null; failed_tick(execute-route) FinishedAt null; execute_route_saves "Sequence contains no elements"; host_stop Background.IsCompleted false; second_process TimeoutException instead of ConflictException |
| Checkpoint run `20261109-100041-670f` CP-1 (`(C1677RetentionServerTests*)\|(C1677RetentionSettingsTests*)\|(C1677TempSweepServerTests*)\|(C1677DiskAlarmTests*)/*`) | ff7cab22c | this worktree, `bin-r1804/` | executed=173 passed=173 failed=0 skipped=0, slot=granted, dirty=0 sourceState=clean buildSource=verified |
| Checkpoint CP-2 (guard batch + MechanicalPcContract, TaskPlatformGuidance, C1729Layering, DockerStackDocumentation/Contract, Server2Production/Staging contracts) | ff7cab22c | same build (reused) | executed=234 passed=234 failed=0 skipped=0 |
| `validate --evidence report.json --expected-source-sha ff7cab22c…` | | | `CHECKPOINT SOURCE VALID source=ff7cab22c… rows=2`, exit 0; wall 11m16s |
| Mutant batch A: M1 lock always granted (`is true` -> `is not null`), M5 `StopAsync` does not await | f444af118 + A | scratch, build 4m15s | 6 run, 2 failed as intended (second_process: TimeoutException; host_stop: IsCompleted false), 4 passed |
| Mutant batch B: M2 execute run not saved before 202, M7 `FailRunAsync` tracker-clear fallback removed | f444af118 + B | scratch, build 2m52s | 6 run, 2 failed as intended (execute_route_saves: "Sequence contains no elements"; failed_tick(scheduled): FinishedAt null), 4 passed |

Mutations restored (`git checkout --`), scratch worktree removed. `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 2bf29ee9d… -HeadRef ff7cab22c…`: commits=4 entries=1 (the Code report markdown) violations=0. `git merge-tree --write-tree origin/master ff7cab22c…`: clean, tree `86200bef66ca88248553df16949af5d3c33abe93` (origin/master = base). No `### Checkpoints` table exists for CARD-1804 (the Code said so); the checkpoint extract used here is `.antiphon/c1804-review-r1-plan.md` (ignored, not committed). Unlisted runs by me: the red build/run and two mutant builds/runs (brief items 5).

## Advisories (follow-up card; not blocking)

- **R-1 stale lock in the pool.** If the explicit `pg_advisory_unlock` fails while the connection is still healthy, `CloseConnectionAsync` returns it to the pool with the server session still holding the lock; Npgsql applies `DISCARD ALL` lazily on that physical connection's next use, so until it is reused or pruned (`Connection Idle Lifetime`, 300 s default) every scheduled tick skips with only an Information log and no run row, and the routes 409. Suggest a Warning on a skipped scheduled tick (or an attention row after two consecutive skips) and, on unlock failure, breaking the physical connection instead of pooling it.
- **R-2 execute run save cancellation.** `StartExecuteRunAsync` saves with `RequestAborted`; a cancel observed after the INSERT committed leaves an unfinished run with no finisher (rare). The turn and lock are already held, so `CancellationToken.None` (or the host token) for that one save closes it.
- **R-3 lock lost mid-request.** A connection dropped while the tick awaits a runner request (Postgres restart) releases the lock at once but is noticed only at the tick's next query; one in-flight `WorkspaceRetain` may overlap another process's tick. The runner-side guards and the `already_absent` answer cover it; noted, no change asked.

## Owed / pending

- PCs for SourceLanding Mutation stay pending, method-scoped: PC-1 lock always granted / unlock skipped; PC-2 run not saved before 202; PC-3 `RunRunnerAsync` catch stamp; PC-4 background finally stamp; PC-5 `StopAsync` await; PC-6 `FailRunAsync` fallback; PC-7 reason mapping swap. Independent pairs: {PC-1, PC-5} and {PC-2, PC-6}; PC-2 and PC-4 share one witness.
- Whole Unit lane: nightly and rc qualification, not run. Runner test project: not run (runner unchanged). Real two-process AppHost + server2 check: not done (the witness uses a second Ticks instance with its own scope and connection on one database).

## Review evidence

subjectTaskId: 6f78ecdb-02a4-4a11-876e-5d37f93569ef
reviewedSourceSha: ff7cab22c2295fefd27fb2fd339cb6d6d5eb7355
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 2bf29ee9dadbd08c3ae78c186e2af00a6deb290e..ff7cab22c2295fefd27fb2fd339cb6d6d5eb7355
