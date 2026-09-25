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
- CP-2 through CP-4 passed at source commit `1ec704201362c8bd35b98ff259dc20662edb65d8`.
  The final documentation commit adds this evidence without changing tested code.
- Every planned positive control remains **PENDING**, for method-scoped SourceLanding Mutation.
- Live acceptance remains **PENDING**: before/after `pg_stat_user_tables` deltas, PostgreSQL CPU,
  transcript-confirmed normal queued delivery and replay/catch-up after desktop activation.

| Checkpoint | Executed | Passed | Failed | Skipped | Reruns |
|---|---:|---:|---:|---:|---:|
| CP-1, baseline production | 2 | 0 | 2 expected assertions | 0 | 1 |
| CP-2, entire Unit lane | 2,999 | 2,999 | 0 | 28 unrelated | 3 |
| CP-3, five full integration classes | 168 | 168 | 0 | 0 | 1 |
| CP-4, persistence/replay manifest | 15 | 15 | 0 | 0 | 0 |

CP-3's roster is 4 hot-path methods, 12 working-state methods, 4 migration methods,
25 `SessionMessageQueueServiceTests` and 123 `SessionMessageQueueDeliveryVerificationTests`.
CP-4 executed all 11 C561 methods, all 3 C698 methods and the sequence-restart regression.
The CP-3 duration tripwire reported zero unlisted tests >=5 seconds.

The CP-2 retries corrected two target-typed `params` test constructions and an adjacent
Slow-registry reason, then rebuilt after CP-3 fixture fixes. CP-3 initially had 166 passes and
two fixture failures: the existing fresh-start queue test assumed `cmd.exe`, and the generic
EXPLAIN replay helper doubled EF's leading `@` parameter marker. The former now uses the
current process executable for admission in that one test (no launch worker is running); its
queue outcome assertions are unchanged. The latter now normalizes captured parameter names
and asserts PostgreSQL's `generic_plans` counter advances. No production fix was needed after
the initial query/index implementation. The large fixture also now verifies a wholly stale
20,000-row tail, with no null timestamp accidentally providing early activity evidence.

The 28 Unit skips are the existing Windows-specific cases in `AgentRegistrySettingsTests` (1),
`AgentExecutableResolverTests` (1), `AgentPinPathTests` (1), `ClaudeRemoteControlLaunchArgsTests`
(1), `DelegationReportFormatterTests` (1), `DirectoryBrowseServiceTests` (5),
`GrokRulesTransportCompatibilityTests` (12 argument cases), `PtyDeliveryCeilingsTests` (3),
`SessionDeliveryProfileTests` (2), plus `MarkdownPdfRendererTests` (1; no Edge/Chrome).
The full method roster is in the task report and `unit-skips.txt`. No new/affected test skipped.

### Isolated PostgreSQL evidence

Final CP-3 evidence is in `.antiphon/card0698-checkpoints/cp3-rerun1-evidence/`.

- Actual captured production queries use the UUID index with **both** key conditions for
  1/512/1,025 keys; 1,025 distinct keys generate three probes (512/512/1).
- Working batches for 1 and 32 sessions execute **one statement**, with both top-one boundary
  indexes and the original sequence range index, without transcript sequential scans or
  grouped historical aggregates. Custom and separately prepared, forced-generic plans pass;
  no sequential-scan disabling or planner hints are used.
- Replaying the captured production SQL for 100 singleton UUID probes, a 1,025-key catch-up
  lookup, and 100 mixed working batches: transcript `seq_scan` **0 -> 0**, `idx_scan`
  **0 -> 11,849**. The observer waits for positive index-counter publication after flushing
  the producer. Index removal in only the owned database makes both plan assertions fail.
- Populated pre-change upgrade: **377,003 rows**, **1,401 ms** for the migration, all three
  indexes valid/ready/nonunique, and an unchanged hash of all persisted columns. This is a
  synthetic rehearsal duration, not a live deployment estimate.

| New index | Bytes in isolated fixture |
|---|---:|
| Session + UUID, including kind | 22,437,888 |
| End session + sequence | 1,474,560 |
| End session + non-null timestamp | 1,097,728 |
| Total | 25,010,176 |

Actual Down/Up preserved rows and original-query outcomes. A controlled old writer held
concurrent creation in a lock wait while a second connection successfully inserted; releasing
the first writer let the migration finish. The generated commands each have
`TransactionSuppressed=true`. Generated Up/Down scripts are retained beside checkpoint logs.
The no-build CLI script export initially looked for a framework-suffixed directory; setting
`AppendTargetFrameworkToOutputPath=false` reused the checkpoint output successfully, without
another build. The EF design-time build above was the only unlisted build; no unlisted test ran.

### Positive controls still pending

Ordinary baseline-red and dropped-index checks do not discharge SourceLanding controls.
All controls below remain **PENDING**, scoped to the named method for later Mutation.

| Mutation | Exact test method (class as above unless prefixed) |
|---|---|
| Auto compact becomes an end | `TranscriptWorkingStateQueryTests.Auto_and_unknown_compact_do_not_end_work` |
| Continuation exclusion removed | `TranscriptWorkingStateQueryTests.Manual_compact_and_continuation_are_idle` |
| Timestamp taken only from highest-sequence end | `TranscriptWorkingStateQueryTests.End_sequence_and_timestamp_maxima_can_come_from_different_rows` |
| Independent activity maxima replace row correlation | `TranscriptWorkingStateQueryTests.Stale_replay_requires_one_qualifying_row_not_independent_activity_maxima` |
| `>=` becomes `>`; null handling removed (separate mutations) | `TranscriptWorkingStateQueryTests.Null_and_equal_timestamps_preserve_conservative_working` |
| Session correlation removed | `TranscriptWorkingStateQueryTests.No_end_activity_is_working` and `Missing_and_empty_sessions_are_idle` (scope each exact method) |
| UUID-only dedup | `AgentSessionRuntimeTests.C698_same_uuid_different_kinds_survive_replay_across_sessions_and_generations` |
| Chunking removed | `AgentSessionRuntimeTests.C698_1025_uuid_keys_are_bounded_and_replays_preserve_every_pair_and_sequence` |
| Empty UUID guard removed | `AgentSessionRuntimeTests.C698_null_uuid_batches_dedup_by_sequence_without_a_uuid_read` |
| UUID access path removed | `TranscriptHotPathQueryTests.Uuid_membership_seeks_by_session_and_uuid` |
| Two working statements restored | `TranscriptHotPathQueryTests.Working_batch_uses_one_statement_and_indexed_boundaries` |
| Boundary classification parameterized | `TranscriptHotPathQueryTests.Prepared_plans_keep_the_partial_index_paths` |
| Concurrent flag removed | `TranscriptHotPathMigrationTests.Generated_sql_and_model_preserve_unique_sequence_and_suppress_index_transactions` |
| Nonconcurrent creation blocks the second writer | `TranscriptHotPathMigrationTests.Concurrent_index_build_waits_for_old_writer_without_blocking_another_insert` |
| UUID index made unique | `TranscriptHotPathMigrationTests.Populated_prechange_upgrade_preserves_all_rows_and_uuid_kinds` |

## Applying the additive migration

Use one migration owner on the deployment host, preferably while the old API is serving. The
old binary remains compatible. The generated Up creates UUID, end/sequence and end/timestamp
indexes serially, all with `CREATE INDEX CONCURRENTLY IF NOT EXISTS`. Repair 1 first renames
each invalid original to its reserved `_invalid` name, then drops that name with a separate
`DROP INDEX CONCURRENTLY IF EXISTS`. The conditional rename, drop and create are separate
transaction-suppressed commands; concurrent DDL cannot run inside the conditional block.
The unique session/sequence index and
the API-error partial index remain intact. There is no transcript rewrite or backfill.

Generate a **non-idempotent**, migration-specific script with the repository-local CLI from
the reviewed build. The `from` migration is `20260924151156_AddLandHoldNotificationOwner`;
the `to` migration is `20260925081051_AddTranscriptHotPathIndexes`. The reviewed export is
`.antiphon/card0698-checkpoints/migration-up.sql`. Inspect transaction boundaries:
all concurrent creates and drops must be outside `BEGIN`/`START TRANSACTION`; a later transaction
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
definitions and ownership, then rerun the repaired migration. It drops/recreates invalid indexes
and preserves valid originals without changing their OIDs. It also finishes a cleanup interrupted
after the rename. The `_invalid` names are reserved for this migration; the Up guard refuses a
valid or unrelated relation there. `IF NOT EXISTS` does not validate an arbitrary existing valid
index's definition. Never stamp migration history manually or remove a pre-change index.

## Rollback and activation

Roll back the binary first and leave valid additive indexes installed. The repaired Down uses
one transaction-suppressed `DROP INDEX CONCURRENTLY IF EXISTS` per original and cleanup name,
so interruption after any drop can retry. Its isolated round trip is a data compatibility test;
do not remove these indexes merely to roll back code. Any later removal still needs the same
ownership/definition checks and migration-history reconciliation through EF.

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
