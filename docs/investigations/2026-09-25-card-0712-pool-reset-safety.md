# CARD-0712 — PostgreSQL pooled-session safety audit

The server uses the Npgsql pool behind EF Core. The three shipped server connection strings are
`server/appsettings.json` (simple mode), `Antiphon.AppHost/appsettings.json` (Aspire's injected
connection), and `docker-compose.yml` (standalone server). `docker-compose.server2-runner.yml`
starts only the phone-home runner; it has no PostgreSQL connection. External deployment overrides
must carry the same setting if they replace `DefaultConnection`.

| Session-state surface | Source finding | Safety with `No Reset On Close=true` |
|---|---|---|
| `SET` outside a transaction | No runtime `SET` found. `SessionMessageQueueService` uses `set_config('statement_timeout', ..., true)` after `BeginTransactionAsync`; the `true` makes it transaction-local. | Transaction end restores the prior value. `PostgresPooledStateTests` checks the same physical connection after return to the pool. |
| Advisory locks | Runtime SQL uses only `pg_advisory_xact_lock`: `DelegationOpenGate`, `GrokRulesRefreshService`, `SpecialistRequestService` and qualification, `AgentTaskDispatcher`, `CapacityRecoveryService`, and `SourceLandingAdmission`. No `pg_advisory_lock` or session-level unlock operation. | PostgreSQL releases each lock at transaction end; an autocommit call releases it at statement end. Neither leaves a lock in the pool. `CapacityRecoveryService.TryClaimCountedSlotAsync` can be called without an explicit transaction; that is an existing atomicity concern, but does not leave session state and is outside this setting change. |
| `LISTEN` / `UNLISTEN` | No server SQL uses either. | No subscription can follow a pooled connection to another caller. |
| Temporary tables | One historical EF migration, `AttachUnambiguousLegacyLandEvidence`, creates three temporary tables with `ON COMMIT DROP`. No runtime temp-table SQL. | Migration transactions drop the tables at commit, including the down migration; rollback also removes them. |
| Prepared statements / auto-prepare | No server `Prepare` call or `Max Auto Prepare` setting. Npgsql defaults `Max Auto Prepare` to 0; its persistent prepared statements are tracked per physical connection. Only a test-only hot-path fixture issues `PREPARE` and `DEALLOCATE ALL` on its own pool. | The production pool has no automatic preparations to reconcile with a reset. Disabling reset does not invalidate Npgsql-managed prepared statements if a future configuration enables them. |
| `search_path` / `application_name` | No runtime SQL changes either. No shipped connection string sets either. Test fixture isolation uses separate databases rather than `SearchPath`. `SetApplicationName` in the codebase configures ASP.NET Data Protection, not PostgreSQL. | No per-borrow setting to leak. |
| Raw `NpgsqlConnection` | `server/Program.cs` opens one admin connection to the `postgres` database at startup for `SELECT pg_database` / optional `CREATE DATABASE`; normal persistence uses EF Core. | That connection only reads/creates a database and sets no session state. The separate database name gives it a separate pool. |

Npgsql documents pool reset, persistent prepared statements, and the default disabled auto-prepare:
[performance](https://www.npgsql.org/doc/performance.html),
[preparation](https://www.npgsql.org/doc/prepare.html), and
[connection parameters](https://www.npgsql.org/doc/connection-string-parameters.html).

Desktop acceptance remains an activation check: after landing and restarting the canonical server,
measure a fresh `pg_stat_statements` interval and verify `DISCARD ALL` falls near zero. This
worktree cannot establish that property for the desktop process.

## Worktree verification

The configuration test failed red before the strings changed (1 failed), then the full Unit lane
passed (3,121 passed, 29 skipped). The combined affected integration selection ran 99 cases:
73 passed, 24 failed, 2 skipped. The new same-backend pooled-state regression, both persistence
classes, `CapacityRecoveryTaskTests`, `GrokRulesTransactionTests`, and
`RepositoryMutationLeaseOwnerTests` all passed. The 24 failures were confined to 19
`AgentTaskConcurrencyLimitTests` cases (the class's non-git temporary directory now hits the
existing default-Worktree validation), three process-based `RepositoryMutationLeaseTests`
cases (including a PowerShell child assembly mismatch), and two `DelegationScopeHoldTests`
worktree dispatch cases. No server C# or test helper was changed to explain those failures;
the production change is limited to the three connection strings. The lease/advisory-lock
acceptance gate therefore remains non-green on this Linux runner and needs review or a
Windows qualification run. No PC is discharged by this verification.
