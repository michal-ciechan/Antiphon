# CARD-1987 S1+S2 code review (Final, round 1)

Review task `e56b2b08` (Claude Opus). Subject: `origin/feat/card-task-cd7b1a6e`, Code task `cd7b1a6e` (Grok).
Reviewed range `997f9b8d9ffc4ba870d2fd56caf7c03781591624..e61b5f6dc5b7d4e89ae283a6e43821ec25a7d5a6`.
Plan: `docs/superpowers/plans/2026-11-18-card-1987-query-rate-and-hot-path-caching-plan.md` (S1, S2);
closed rows: `docs/superpowers/plans/2026-11-18-card-1987-checkpoints.md`.

**Verdict: CLEAN. No blockers, no regressions.** Four advisories (A-1..A-4) do not hold the land.

## Evidence (server2, linux, rerun by this review)

| Row | Commit | Result |
|---|---|---|
| CP-1 red | `e861497cfa2b6e98e2c7695d4b8d4ae56fed4260` | executed=1 failed=1: "binding reads, budget 1", was 5 (run `20261118-195152-f899`) |
| CP-3 red | `954856158148fc8e9f8af3a944306e8d31cb3fcc` | executed=1 failed=1: "3 commands, budget 1; transactions 1, budget 0" (run `20261118-194628-8ceb`) |
| CP-2 | `e61b5f6dc5` | executed=9 passed=9 (4 C1987 cache + 5 PhoneHomeSessionRoutingTests), source clean, buildSource verified |
| CP-4 | `e61b5f6dc5` | executed=4 passed=4 (C1987 x2, burst test, StallDetector_fires_after_configured_idle) |
| CP-G guards | `e61b5f6dc5` | `(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)` executed=53 passed=53 |

Green run `20261118-193658-672b`: one build, three rows, GREEN exit 0, `unlisted: none`;
`validate --expected-source-sha e61b5f6dc5...` prints `CHECKPOINT SOURCE VALID rows=3`.
`check-evidence-diff.ps1` over the range: commits=5 entries=0 violations=0.
`git merge-tree --write-tree origin/master HEAD` (master `997f9b8d9f`): clean, tree `9cf1a6def8`.
No timeout widened, no assertion loosened; the burst test moved from a SaveChanges counter to a
`QueryBudget` on `rt.activity.write` with the same expected 2, as the plan specified.

## S1 binding cache

- `Route` caches `Local`/`Remote` 60 s per session id, never `Missing`, and still calls
  `Resolve(runnerId)` on every remote call (`C1987_resolve_runs_on_every_call`). Cap 4,096 with
  expired-then-all eviction; the singleton registration (`Program.cs:392`) keeps one cache.
  Listeners are weak references held alive by the client's own delegate field; dead ones prune.
- **No writer changes a binding after insert.** Every `RunnerId/RunnerStoreId/RunnerCwd` assignment
  on `AgentSession` in `server/` is an object initializer on a new row with `Id = Guid.NewGuid()`
  (`AgentTaskDispatcher.cs:6376`, `:6916`, `AgentControlService.cs:798`, ...); the other `RunnerId`
  assignments are on `TaskWorktreeMirrorBinding`, a residue row and `AgentTask`. The only change is
  deletion: `DataRetentionService` uses `ExecuteDeleteAsync` (seen by the interceptor), for sessions
  idle 7 days.
- **Second server process (server2 Docker cutover).** Invalidation is in-process only, but since
  bindings are insert-only and `Missing` is never cached, a row inserted by another process is read
  fresh. The worst stale effect is a session deleted by another process (or by a cascade) routing to
  its old runner for up to 60 s instead of `NotFoundException`; the runner then answers for a session
  it no longer has. That is acceptable; the cache does not need disabling across processes.
- **Delivery evidence is not cached.** The cache holds only the routing binding. Runner answers
  (get, snapshot, buffer, transcript, absence), queue verdicts, receipts and transcript rows are
  read live as before; transcript-confirmed UserPrompt remains the verdict.
- Missing live `ExecuteUpdate` integration test: not a blocker. No production code path issues an
  update of the binding columns, so the write interceptor is defence in depth; its parser is pinned
  by representative SQL in `C1987_binding_write_drops_the_cached_route` (A-2).

## S2 one-statement activity write

- One `ExecuteSqlInterpolatedAsync`, tag `-- rt.activity.write` at column 0, no transaction; claim
  before the `try`, warning on failure, signature unchanged.
- Equivalent to the old path: same predicate (`AgentSessionId`, `Phase = 4` StreamingTurn, `CompletedAt
  IS NULL`, newest `AttemptNumber`, `LIMIT 1`), same two columns, same value. A data-modifying CTE runs
  even though the outer query does not read it, so the attempt update happens whether or not the
  session row exists, as before. Columns are `timestamp with time zone` and `Phase` is `integer`
  (model snapshot), so the UTC `DateTime` and `int` parameters bind without a session-time-zone shift.
  Neither `AgentSession` nor `RunAttempt` has a concurrency token and no SaveChanges interceptor in
  `server/` reacts to these columns, so bypassing the tracked save loses nothing. Old and new both
  write only these columns, so no other column's update can be lost; the single statement also
  closes the old read-then-write gap on the attempt.
- The stall detector reads the same attempt's `LastEventAt`; `RunAttemptStallDetectorTests` is green
  and `C1987_activity_write_sets_both_timestamps` pins newest-streaming written, older-streaming and
  completed unchanged.
- The `RunnerBindingWriteInterceptor` does not fire on this statement (pinned).

## DerivedImpactIndex

`tests/derived-impact-index.json` changes are a regenerated index (class counts and `first` lists for
touched files, new `RoutingBindingCacheTests`); the guard row is green at the tip.

## Advisories (cards, not land holds)

- **A-1** `RunnerBindingWriteInterceptor` and the SaveChanges hook notify when the command or save
  returns, before an outer transaction commits. A concurrent `Route` between notify and commit could
  re-cache the old binding for 60 s. Moot today (no post-insert binding writer); if a writer is ever
  added, notify on commit (`IDbTransactionInterceptor.TransactionCommitted`).
- **A-2** The parser witnesses use hand-written SQL; a live `ExecuteUpdate`/`ExecuteDelete` against
  `TestDbFixture` would pin the EF-generated text.
- **A-3** `RunnerBindingInvalidation` is process-static. A parallel test that deletes `AgentSessions`
  through any `AppDbContext` calls `Notify(null)` and could clear the cache inside
  `C1987_route_reads_the_binding_once_per_ttl`'s five-call loop (budget 1). The window is microseconds;
  a per-instance or injectable notifier would remove it.
- **A-4** FK cascade deletes of `AgentSessions` (if any parent cascades) bypass both hooks; bounded by
  the same 60 s, same harmless effect as A-1.
