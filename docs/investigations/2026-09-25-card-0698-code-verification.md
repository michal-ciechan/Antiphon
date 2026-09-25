# CARD-0698 S0-S3 implementation and rollout evidence

The implementation adds three nonunique transcript indexes, partitions UUID membership into
512-key reads, and computes working state in one parameterized PostgreSQL statement. UUID plus
kind remains the replay identity. The end sequence and end timestamp are independent maxima;
both activity tests still apply to the same row. CARD-0699/0700, polling, retention, delivery,
runner protocol and cached state are outside this change.

## Verification

The checkpoint manifest is in
[the plan](../superpowers/plans/2026-09-25-card-0698-postgres-load-plan.md#checkpoints).
The full command logs, TRX files, captured commands/typed parameters, JSON plans, migration SQL
and scan-counter observations are under `.antiphon/card0698-checkpoints/` in task `81c898bc`.
The complete caller report is `.antiphon/task-81c898bc.md`.

- Baseline test commits: `3fff5198` and `421ad2dc50774e2d728690581d58e1118867f8b8`.
- CP-1: 2 executed, 0 passed, 2 expected assertion failures, 0 skipped. UUID membership lacks
  a seek on both keys; the working read emits 2 statements instead of 1. One preceding build
  attempt failed on a missing test DTO import and was corrected; it is not red evidence.
- EF tooling exception: repository-local `dotnet-ef` 9.0.20 restored; one design-time build with
  `OutputPath=bin-c698-ef/` and `UseAppHost=false` generated
  `20260925081051_AddTranscriptHotPathIndexes`. No migration/snapshot was handwritten.
- CP-2 through CP-4 and isolated rehearsal results: recorded below after execution.
- Every planned positive control remains **PENDING**, for method-scoped SourceLanding Mutation.
- Live acceptance remains **PENDING**: before/after `pg_stat_user_tables` deltas, PostgreSQL CPU,
  transcript-confirmed normal queued delivery and replay/catch-up after desktop activation.

## Applying the additive migration

Use one migration owner on the deployment host, preferably while the old API is serving. The
old binary remains compatible. The generated Up creates UUID, end/sequence and end/timestamp
indexes serially, all with `CREATE INDEX CONCURRENTLY`. The unique session/sequence index and
the API-error partial index remain intact. There is no transcript rewrite or backfill.

Generate a **non-idempotent**, migration-specific script with the repository-local CLI from
the reviewed build. The `from` migration is the immediately preceding migration in the repo;
the `to` migration is `20260925081051_AddTranscriptHotPathIndexes`. Inspect transaction boundaries:
all three concurrent creates must be outside `BEGIN`/`START TRANSACTION`; a later transaction
around the migration-history insert is allowed. Do not use `--idempotent` or `psql -1`.

Apply through the deployment host's approved libpq service/password-file configuration (no
connection string or password on the command line). For example, once that configuration is
selected, use `psql -X --set ON_ERROR_STOP=1 --file <reviewed-script.sql>` with process-local
`PGOPTIONS='-c statement_timeout=300000 -c lock_timeout=5000'`. Preserve/restore any previous
`PGOPTIONS`; the budgets apply only to this migration connection. This gives each statement,
including each index, a 300-second server budget and a 5-second lock budget. Do not launch
another server Program to apply it. Startup's automatic migrator uses the normal EF command
timeout; pre-application avoids spending startup availability on index construction.

PostgreSQL concurrent creation permits writers, uses multiple scans and can wait on old
transactions. A timeout is an inspection/retry decision, never authorization to kill blockers.
See [PostgreSQL 16 CREATE INDEX](https://www.postgresql.org/docs/16/sql-createindex.html) and
[Npgsql concurrent index configuration](https://www.npgsql.org/efcore/modeling/indexes.html).

Before applying, and after any error, inspect `__EFMigrationsHistory` plus `pg_index.indisvalid`,
`indisready` and `pg_get_indexdef` for these exact names:

- `IX_TranscriptEntries_AgentSessionId_Uuid`
- `IX_TranscriptEntries_End_AgentSessionId_Sequence`
- `IX_TranscriptEntries_End_AgentSessionId_Timestamp`

If already recorded and valid, do not replay. If unrecorded after partial creation, check exact
definitions and ownership, then drop only these new indexes concurrently, one statement at a
time outside transactions, before rerunning the reviewed script. No blind `IF NOT EXISTS`,
manual migration-history stamp, or removal of an existing pre-change index is appropriate.

## Rollback and activation

Roll back the binary first and leave valid additive indexes installed. The generated Down uses
ordinary `DROP INDEX` inside the migrator's transaction; its isolated round trip is a data
compatibility test, **not** an online removal recipe. If index removal is later necessary, use
a separately reviewed `DROP INDEX CONCURRENTLY` operation per index outside a transaction,
with the same ownership/definition checks and migration-history reconciliation through EF.

After landing, the caller uses the canonical desktop checkout and restart runbook, verifies
`/api/version` SHA and `land-v2`, then verifies migration history and all three valid/ready index
definitions. Only then measure two comparable 180-second steady-state windows, excluding
index construction, ANALYZE, retention and catch-up. The plan's thresholds remain <=0.1
transcript sequential scans/second and >=70% lower mean PostgreSQL CPU. Record concurrent
CARD-0699/0700 deployments as confounders. No desktop stack was restarted by this Code task.

The remaining cost includes post-end activity tails and housekeeping-only no-end histories;
three additional maintained indexes cost writes/storage. Neither the SQL rewrite nor isolated
plans establish the aggregate live CPU result. If live targets miss, attribute the remaining
queries before commissioning another slice.
