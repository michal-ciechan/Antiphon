# Why missed: rc run 3528e6b1 group-86 DispatchConcurrency

Windows Full rc run 3528e6b1 (rc/2026-11-30 tip bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0), chunk `antiphon-all-group-86.trx`: 457 results, 2 failed, both in `DispatchConcurrencyDispatchTests`.

- `Put_racing_claim_observes_one_complete_policy` at 30.4s: `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` from `NpgsqlExecutionStrategy.ExecuteAsync` during `DispatchConcurrencySettingsService.OccupancyAsync` `ToListAsync`.
- `Competing_claims_admit_only_one_task`: TUnit `TimeoutException` `timed out after 00:03:00`, task status `WaitingForActivation`.

## Facts

- Production `UseNpgsql` does not call `EnableRetryOnFailure`. The test fixture's `TestDbFixtureLifecycle.BuildOptions` does not either. The default `NpgsqlExecutionStrategy` is non-retrying, and that wrapper text is what it throws around a failed command. `AgentTaskDispatcher.IsTransientCapacity` only treats SQLSTATE `40P01`, `40001`, and `55P03` as capacity misses. This failure has no SQLSTATE: the inner exception is `TimeoutException: Timeout during reading attempt` after about 30s (`[db-failure] elapsedMs=30014` on the quiet Linux run).
- `[Timeout(180_000)]` on both methods is the 3 minute TUnit cap. `HarnessBudget` inside the class is 20s and throws `InvalidOperationException("harness deadline")` when it fires. `WaitingForActivation` is the incomplete async-method status at the TUnit cap.
- Linux, same tree as master `2afaf4bebfb763c12ace57849ed7dd17e5bfe0ca`, testcontainer Postgres only (`postgres:16-alpine`, database `test_…` / `antiphon_test`):
  - load1=29.37: class 8 total, 2 failed, 6 passed, 4m 23s. The same two methods.
  - load1=8.51: class 8 total, 2 failed, 6 passed, 4m 15s. Same stacks.
- Lock sample during `RequestedModelAliasSchemaTests.Open_transaction_does_not_keep_access_exclusive_on_agent_tasks` before the fix, database `test_df0fb9e2175a42599e8aa425347ed3c8`: the session whose query was `SELECT count(*)::int AS "Value" FROM "AgentTasks" WHERE "RequestedModelAlias" IS NULL` held `AccessExclusiveLock` granted; the other session's `SELECT count(*) FROM "AgentTasks"` waited with `AccessShareLock`. The probe then failed `PostgresException 57014` (statement_timeout 3s) in 3.6s.
- After `1c58737aa72ce0e84b413f046dbfc48ba82ba937`, one run of `RequestedModelAliasSchemaTests` plus `DispatchConcurrencyDispatchTests` was 9/9/0 in 53s (start load1=13.82). `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default`, which drops the alias column and rewinds, was 1/1 in 32s (start load1=5.31).
- The class is not in a slow-test allowlist. Its measured cost before this interceptor was 8/8 in 22 serial seconds (`scripts/lib/nightly-class-costs.json`, run `8f055f87`, rc-2026-10-25). No `docs/investigations/2026-11-*` note lists these two methods as a known failure. The previous full run's group-86 row was `WorktreeDefaultCallerTests.NightlyKeepsDedicatedCloneGuard` (`docs/investigations/2026-11-20-run-e7f92f66-ended.md`).

## Cause

`RequestedModelAliasSchema` runs `ALTER TABLE "AgentTasks" ADD COLUMN IF NOT EXISTS "RequestedModelAlias"` on every command whose text contains that column name, on the command's own connection and transaction. PostgreSQL takes `ACCESS EXCLUSIVE` for that statement even when the column is already present, and holds it until the transaction commits.

The claim query is `AgentTasks.FromSqlInterpolated(SELECT * FROM "AgentTasks" … FOR UPDATE)` plus a status predicate. The command text EF sends contains `RequestedModelAlias`, so the alias interceptor runs. Interceptors are registered as failure-evidence, then the alias interceptor, then the test's pause interceptor. The `ALTER` therefore runs before `DispatchCommandInterceptor` waits. `Put_racing_claim_observes_one_complete_policy` pauses the claim, then calls `ReadProjectAsync` → `OccupancyAsync` on another connection. That `SELECT` needs `AccessShare` and waits until Npgsql's 30s command timeout. The pause is released only after that read returns, so the wait cannot end.

`Competing_claims_admit_only_one_task` pauses at save, which is after the `ALTER`. The first claim holds `ACCESS EXCLUSIVE` across the pause. The second claim blocks inside `PrepareAsync` on that relation lock, before `pg_advisory_xact_lock`. `WaitForUngrantedAdvisoryAsync` does not see an advisory waiter. The blocked command uses `CancellationToken.None` and the 30s command timeout, and the test method is still awaiting when TUnit cancels it at 180s.

The column already exists on ordinary clones: the assembly template is migrated to tip. The interceptor still re-issued the `ALTER` on every mention. Historical databases created from `template0` still need the first add; that path is unchanged.

## Fix

`1c58737aa72ce0e84b413f046dbfc48ba82ba937`. When `information_schema.columns` already shows `RequestedModelAlias`, the interceptor does not `ALTER`. A database is remembered only when the observing command has no open transaction, so an uncommitted add is not treated as visible to other sessions. `DROP COLUMN` of this column, or `DROP TABLE` of `AgentTasks`, forgets that memory. The first add on a database that lacks the column is still the in-transaction `ALTER`.

The witness is `RequestedModelAliasSchemaTests.Open_transaction_does_not_keep_access_exclusive_on_agent_tasks`, committed red at `4e6d1496a03d0b40572af8c039c112864f1f8e8e` (interceptor bytes identical to `2afaf4beb`) and green in the 9/9 run above. No retry was added and neither 180s attribute was widened.

## Introducing commit

`000375cf3440441cf3d70430eea1cf85443e79c1` (2026-11-20 18:20:11 +0000, `test: keep pre-alias seeds and the C1584 roster current`). It is on master and on rc tip `bd34a7ff`. The file is absent from `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`, which is an ancestor of HEAD and is the cut associated with the previous full run's seed.

## Outside the selection

`DispatchConcurrencyDispatchTests` is `[Category("Integration")]`. Merge verification is the short set (2026-11-18): one build, narrow witness rows, land-graph `-Check`, pin recompute, derived-index regeneration, and evidence-diff. This class is not in that set. The previous full run's cut did not contain the interceptor, so that run could not fail this way. A land of `000375cf3` did not execute this class.

## Cheapest pre-land check

`tests/Antiphon.Tests/TestHelpers/RequestedModelAliasSchema.cs` -> `/*/*/RequestedModelAliasSchemaTests/*`

The dispatch class is the rc scenario and the larger check: `/*/*/DispatchConcurrencyDispatchTests/*`. Together they were 53s on this host after the fixture was warm.

## Exemption

none
