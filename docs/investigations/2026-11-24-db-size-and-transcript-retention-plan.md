# Production database size and transcript retention

**Decision for 2026-11-24:** Keep the existing retention safeguards. `TranscriptEntries` occupies **3.227 GB (64.6% of 4.993 GB)**. Roughly **987,000 rows** are older than 30 days and account for **0.835 GB** of allocated relation space. The read-only eligibility reconstruction found **zero eligible transcript rows** at 30 days and at the shipped 7-day cutoff. At the measured state, the requested cleanup would produce **0 GB of newly reusable space and 0 GB of filesystem savings**.

## Production measurement

The database was PostgreSQL 16.14 on server2, `antiphon`, via Docker context `antiphon-server2`, container `antiphon-server2-postgres-postgres-1` pinned to `2da164890799a5dbadf8de741f7ac9a6b814c3f96963d911508b4a7d492b2ab5`. `/api/version` returned `6ed4cb58cd75fe386e94965165d96752452431a8`. The 10 remote PostgreSQL backends came from the desktop. The SQL used read-only psql sessions, UTC, a 20-second statement timeout, a 2-second lock timeout, and one statement at a time. The catalog and samples were taken at approximately 18:49–18:56 UTC while ingestion continued. No builds or tests ran. Production verification consisted of the read-only SQL and log checks described here.

The 09:07 UTC restore drill counted 3,748,263 transcript rows. The evening catalog estimated 3,892,017 live rows, and `n_tup_ins` was 3,886,188 at 18:50. The evening `pg_database_size` was 4,992,908,311 bytes, up about 148 MB from the morning report. The morning report also recorded 1,871,495 rows in the desktop database just before the November 18 cutover. That earlier count is from a different database snapshot. [Backup and restore comparison](2026-11-24-server2-db-backup-verification.md#4-restore-proof-scratch-on-server2)

### 15 largest user relations

All are in `public`. Sizes are MiB (1,048,576 bytes), rounded independently. Total is `pg_total_relation_size`, heap main is `pg_relation_size`, heap aux is the remaining base-table forks from `pg_table_size`, TOAST includes its own index, and indexes are `pg_indexes_size` on the base table. Live/dead tuples and last autovacuum are from `pg_stat_user_tables` and the counts are estimates. Dates shown are UTC in 2026.

| Relation | Total | Heap main | Heap aux | TOAST | Base indexes | Live | Dead | Last autovacuum |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| TranscriptEntries | 3077.25 | 2018.84 | 0.58 | 292.00 | 765.84 | 3,892,017 | 936 | 11-23 22:03:32 |
| HostLoadSamples | 621.95 | 126.80 | 0.05 | 445.18 | 49.92 | 183,468 | 20,610 | 11-24 09:14:34 |
| AgentTaskEvents | 306.11 | 211.60 | 0.08 | 24.94 | 69.49 | 987,597 | 0 | 11-18 17:31:34 |
| AgentTasks | 162.88 | 38.02 | 0.04 | 115.31 | 9.52 | 33,050 | 6,407 | 11-18 17:31:37 |
| SessionQueuedMessages | 147.34 | 57.54 | 0.04 | 78.34 | 11.42 | 71,273 | 9,196 | 11-18 17:31:48 |
| WorkspaceUseReservations | 135.39 | 107.55 | 0.05 | 0.01 | 27.77 | 528,448 | 35,970 | 11-18 17:32:28 |
| HostLoadSampleIdentities | 80.85 | 53.77 | 0.04 | 0.00 | 27.04 | 240,215 | 0 | 11-24 16:54:39 |
| AgentTaskLandNotifications | 71.02 | 9.99 | 0.03 | 58.91 | 2.08 | 11,177 | 1,355 | 11-24 18:06:40 |
| WorktreeResidueRunCandidates | 37.12 | 31.23 | 0.03 | 0.01 | 5.85 | 130,238 | 5,938 | 11-23 14:49:12 |
| HostOccupancySamples | 15.81 | 9.27 | 0.03 | 0.01 | 6.50 | 60,237 | 1,881 | 11-24 08:15:29 |
| Alerts | 13.59 | 10.75 | 0.03 | 0.01 | 2.80 | 13,656 | 756 | 11-18 17:31:42 |
| WorktreeResidueCandidateCursors | 9.42 | 5.23 | 0.03 | 0.00 | 4.16 | 28,860 | 119 | 11-24 10:02:31 |
| CardRevisions | 8.71 | 4.57 | 0.03 | 2.98 | 1.13 | 15,305 | 0 | 11-18 17:31:42 |
| AgentSessions | 8.31 | 6.96 | 0.03 | 0.01 | 1.31 | 10,459 | 1,039 | 11-22 22:35:51 |
| Cards | 7.40 | 2.80 | 0.03 | 4.02 | 0.54 | 2,270 | 110 | 11-22 09:43:35 |

- `TranscriptEntries` totals 3,226,730,496 bytes. The main heap holds 2,116,902,912 bytes, base indexes hold 803,037,184 bytes, and TOAST holds 306,184,192 bytes (9.5%). The sampled stored datum sizes average about 397 bytes for `Text` and 54 for `ToolInput` per row including nulls. Both columns are `text`. The three largest indexes are session/kind/sequence (309 MiB), unique session/sequence (214 MiB), and the primary key (125 MiB), followed by session/UUID (113 MiB). The seven base indexes lack a global `CreatedAt` index. [Transcript schema and indexes](../../server/Infrastructure/Data/AppDbContext.cs#L1766)
- `HostLoadSamples` totals 652,165,120 bytes, including 466,804,736 bytes of TOAST (71.6%). `PayloadJson` averages roughly 2,140 bytes per sampled row and `CountersJson` about 235 bytes. Both are JSON encoded in `text`. Its separate raw-telemetry job prunes derived samples after seven days by default. The live catalog recorded 103,600 deletes. [Telemetry retention query](../../server/Infrastructure/HostLoad/HostLoadDerivedService.cs#L81) · [Telemetry settings](../../server/Application/Settings/HostLoadHistorySettings.cs#L13)
- `AgentTaskEvents` totals 320,978,944 bytes, including 26,148,864 bytes of TOAST (8.1%). `Detail` (`varchar(4000)`) averages about 141 bytes per row and `InputBody` (`text`) is sparse, averaging under one byte per sampled row with nulls included and about 479 bytes among non-null rows in `pg_stats`. `AgentTaskId, At` is its largest index (39 MiB). The existing task-tree retention default is 180 days. Validated FK `FK_AgentTaskEvents_AgentTasks_AgentTaskId` cascades from its parent task. [Task event schema](../../server/Infrastructure/Data/AppDbContext.cs#L2623) · [Task-tree retention](../../server/Application/Services/DataRetentionService.cs#L474) · [Retention settings](../../server/Application/Settings/RetentionSettings.cs#L20)

### Age distribution and allocated bytes

The cutoffs are strict UTC comparisons against 2026-11-24 18:50, using `TranscriptEntries.CreatedAt` (ingestion), `HostLoadSamples.SampledAt` (observation) and `AgentTaskEvents.At` (event). GB below are decimal. The byte estimates are the age bucket's share of current allocated heap, TOAST and indexes.

| Relation | Age | Rows | Allocated footprint | Sample spread for footprint |
| --- | --- | ---: | ---: | ---: |
| TranscriptEntries | >14d | ~1,354,000 (34.8%) | 1.212 GB (37.6%) | 1.120–1.289 GB |
| TranscriptEntries | >30d | ~987,000 (25.4%) | 0.835 GB (25.9%) | 0.786–0.904 GB |
| TranscriptEntries | >60d | ~354,000 (9.1%) | 0.257 GB (8.0%) | 0.228–0.269 GB |
| HostLoadSamples | >14d, >30d, >60d | 0 | 0 GB | Exact zero |
| AgentTaskEvents | >14d | 173,783 | 0.099 GB (30.7%) | 0.089–0.113 GB |
| AgentTaskEvents | >30d | 124,017 | 0.054 GB (16.8%) | 0.037–0.067 GB |
| AgentTaskEvents | >60d | 32,600 | 0.013 GB (3.9%) | 0.007–0.018 GB |

| Relation | Earliest observed UTC | Latest observed UTC | Evidence |
| --- | --- | --- | --- |
| TranscriptEntries | 2026-08-13 20:05:37.795993 | 2026-11-24 18:55:17.011930 | Earliest across five block samples. Latest from bounded `ctid` scans near the heap tail. Global extrema require stronger evidence because there is no date-leading index. |
| HostLoadSamples | 2026-11-17 18:52:04.035173 | 2026-11-24 18:52:28.373720 | Exact in the query snapshot. Derived-age index endpoints and pending-derived index, with zero pending rows. |
| AgentTaskEvents | 2026-08-09 13:51:03.429315 | 2026-11-24 18:49:38.322579 | Exact in the query snapshot. Per-task forward/reverse limits and age counts from `IX_AgentTaskEvents_AgentTaskId_At`; 0.67 seconds. |

For each relation the byte sample used five `TABLESAMPLE SYSTEM (0.5) REPEATABLE` passes with seeds 17, 29, 43, 71 and 113. The raw transcript sample had 98,563 row observations (intersecting samples may repeat a page) and 24,999 were older than 30 days. For each bucket, the estimator apportions heap plus TOAST using the sampled share of summed `pg_column_size(t.*)` and apportions base indexes by row fraction. For events it uses the indexed count divided by the live-tuple estimate for the index share. Per-column `pg_column_size` identified the largest datum columns. The shares approximate the distribution of allocated bytes, including pre-existing slack and page overhead. The base index approximation does not model varying key widths or partial-index membership. PostgreSQL samples whole blocks and time is strongly correlated with physical order in the transcript and event tables (about 0.992 and 0.999 in `pg_stats`). The seed spread is empirical variation, with no statistical confidence interval implied. Transcript 30-day count estimates ranged from 0.94 to 1.08 million across seeds. [PostgreSQL 16 block sampling](https://www.postgresql.org/docs/16/sql-select.html#SQL-FROM)

## Why old transcripts remain

`DataRetentionHostedService` runs at startup and every six hours by default. `Retention:TranscriptRetentionDays` defaults to seven. The deployment settings files and the queried process, user and machine environment scopes had no observed override for either setting. The actual service log reported zero transcript deletes in all 24 matched sweeps on November 20–24, including the November 24 09:19:40 and 15:19:45 UTC lines, with 0 and 1 session rows deleted respectively. The table statistic `n_tup_del=0` corroborated zero recorded transcript deletes in the current counters. The pass runs through the application's hosted background service. [Hosted job](../../server/Infrastructure/Supervision/DataRetentionHostedService.cs#L29) · [Registration](../../server/Program.cs#L1129) · [Retention settings](../../server/Application/Settings/RetentionSettings.cs#L14) · [Server log source](../logs.md#desktop-and-apphost)

The pass selects Stopped/Failed sessions with an old `LastSeenAt`, excludes persistent sessions and work with outstanding delivery/recovery obligations, then checks the **newest** transcript row's `CreatedAt`. A qualifying session is pruned as one whole set under the same mutation lease used by ingest, with reseed after commit. The recheck covers fresh transcript rows, runtime actions, outbound protection and unsettled inbound payloads. The separate session-retention pass also checks task references before cascading. The transcript-only pass has no direct open-card or open-task exclusion. [Session retention](../../server/Application/Services/DataRetentionService.cs#L244) · [Transcript retention and mutation lease](../../server/Application/Services/DataRetentionService.cs#L301)

The result below was reconstructed from live rows. The eligibility CTE used the production prefilter from `DataRetentionService.cs:312–341`, including pending supervision, runtime actions, unresolved check compaction, legacy check publication, land notices, warning intents and inbound payloads. It used one per-session index probe for `count(*), min("CreatedAt"), max("CreatedAt")` on `IX_TranscriptEntries_AgentSessionId_Sequence` after the prefilter. The one execution took 10.26 seconds. The conservative column also requires `EndedAt` before cutoff and excludes current open card/task links through the owner, card, child, parent and specialist session references.

| Cutoff | Old terminal sessions | Persistent | Outbound protected | Other guard | Survived built-in metadata guards | Conservative subset before transcript probe | Survivors with transcript rows | Eligible rows |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 7d | 9,795 | 2,949 | 8,559 | 25 | 1,156 | 1,126 | 0 | 0 |
| 30d | 6,825 | 2,889 | 5,610 | 11 | 1,138 | 1,117 | 0 | 0 |

Protection counts overlap. In the bounded transcript sample, 24,078 of 24,999 old observations (96.3%) belonged to outbound-protected sessions, 7,912 (31.6%) to persistent sessions, and 14,258 (57.0%) to sessions with an open card/task reference in the conservative link check. Also, 23,767 (95.1%) were in terminal sessions with `LastSeenAt` before the cutoff. The outbound guard is the largest measured protection.

The dominant outbound guard is persisted queue discovery: 20,549 **Sent Delegation-origin** messages in 5,600 old terminal sessions have null settlement, delivery and discovery-close markers. The comparable counts are 2,007 System, 8,439 Check and 7 Scheduled sources, with session overlap. Of the Delegation sources, 9,958 were created before the migration's timestamp. That migration introduced the nullable close marker without populating it on existing queue rows. None of those 20,549 Delegation messages had an earlier stored sent Channel-origin message with a conversation key under the production sequence/time predicate. An absent predecessor in current storage does not establish whether historical channel context once existed. The dispatcher can close a terminal-generation machine source with no original channel context after it finds a complete owning prompt/turn. The stored rows explain why retention skips the histories. They leave open why the historical close path did not run or did not establish terminal/turn proof. [Discovery protection](../../server/Application/Services/ChannelOutboundEvidence.cs#L10) · [Close marker migration](../../server/Migrations/20261004231302_ExtendChannelOutboundRecovery.cs#L14) · [Matching and closure](../../server/Application/Services/ChannelReplyDispatcher.cs#L441) · [Prior route lookup](../../server/Application/Services/ChannelReplyDispatcher.cs#L1762)

## What would be lost

| Dependency | Evidence and impact |
| --- | --- |
| Session resume and replay | The resume path rebuilds provider launch state. Ingest and working-state reconstruction read stored sequence boundaries, UUIDs, prompt/boundary rows and watermarks. A partial historic deletion can change deduplication, turn reconstruction and the state exposed after resume. [Resume](../../server/Application/Services/AgentSessionService.cs#L2191) · [Ingest prelude](../../server/Infrastructure/Data/TranscriptIngestPrelude.cs#L27) · [State loader](../../server/Infrastructure/Data/SessionStateLoader.cs#L51) |
| Delivery and publication verdicts | The durable `UserPrompt` establishes delivery. Outbound, input attention, supervision and check recovery retain or query stored prompt/turn sequences. A gap removes evidence for delivered, withheld, bounced and pending outcomes. [Delivery invariants](../session-runtime-invariants.md#deferred-transcript-effects-card-1259) · [Runtime prompt matching](../../server/Application/Services/AgentSessionRuntime.cs#L524) · [Input attention](../../server/Application/Services/AttentionService.TaskInputs.cs#L196) · [Channel protection](../../server/Application/Services/ChannelOutboundEvidence.cs#L10) |
| Reviews, reports and card/task history | Review dispatch finds boundaries and response text. Reply settlement reads report turn text, tool receipts and usage. Settled `AgentTask.Result` and report evidence are separate stored fields, while fallback/reinspection and completion/land receipt checks still need the source transcript. Card fields and revisions have their own tables. Open tasks and cards may reference terminal sessions. [Review reply](../../server/Application/Services/ReviewReplyDispatcher.cs#L28) · [Report selection](../../server/Application/Services/TaskReportTurnSelector.cs#L248) · [Tool receipts](../../server/Application/Services/AgentTaskReplyService.cs#L3569) · [Task result and evidence](../../server/Domain/Entities/AgentTask.cs#L375) · [Land receipt](../../server/Application/Services/AgentTaskLandNotificationService.cs#L400) |
| Transcript retrieval, search in history and exports | The session endpoint fetches stored transcript entries for the panel and historical inspection. Diagnostic bundles select stored rows. Card search uses its own card service and data. A session can still exist with empty transcript retrieval after sanctioned retention. [Transcript API](../../server/Application/Services/AgentSessionService.cs#L2350) · [Client panel](../../client/src/features/agents/SessionTranscriptPanel.tsx#L205) · [Diagnostic export](../../server/Application/Services/DiagnosticsBundleService.cs#L231) · [Card search](../../server/Application/Services/CardService.cs#L238) |
| Cost and usage | Settlement reads token-bearing transcript rows and stores tokens and cost on the task. Stored settled totals survive a transcript-only purge. Live/context usage and any reconstruction of per-turn usage lose their input. [Token query](../../server/Application/Services/AgentTaskReplyService.cs#L4630) · [Settlement costing](../../server/Application/Services/AgentTaskReplyService.cs#L1267) · [Usage rollup](../../server/Application/Services/DelegationUsageRollup.cs#L29) · [Context usage](../../server/Application/Services/SessionContextUsage.cs#L157) |
| Constraints and caches | Live `pg_constraint` shows `TranscriptEntries.AgentSessionId → AgentSessions.Id ON DELETE CASCADE` and `SessionRuntimeActions.TranscriptEntryId → TranscriptEntries.Id ON DELETE RESTRICT`. Deleting sessions also cascades queues, summaries, runtime actions, API recoveries and modal episodes, sets card-owner/run-attempt links null, and can be blocked by decision questions. SQL-level mutation bypasses the application's transcript mutation lease, prompt-span stamp and reseed. [Session cascade](../../server/Infrastructure/Data/AppDbContext.cs#L1819) · [Mutation lease](../../server/Application/Services/DataRetentionService.cs#L386) · [Prompt cache invariant](../session-runtime-invariants.md#L1631) |

## Reusable space and filesystem savings

- **At the measured eligibility:** 0 GB from age-policy cleanup. PostgreSQL still recorded 936 estimated dead heap tuples from ordinary activity. The query did not measure existing page-level bloat. A general rewrite could recover existing slack independently of retention, which these figures do not quantify.
- **Age-only footprint for comparison:** The roughly 0.835 GB at 30 days comprises about 0.63 GB of heap plus TOAST and 0.20 GB of index allocation. This is a footprint estimate for protected history. Deleting a cohort leaves dead versions and writes WAL. When snapshots permit, a plain vacuum can make the affected storage available for table/TOAST reuse and clean index versions. Index space reuse depends on page layout and incoming keys. Budget **0 GB operating-system recovery** for ordinary vacuum, except when it can truncate completely empty consecutive tail pages under a short exclusive lock. `VACUUM (TRUNCATE FALSE)` avoids that truncation lock. [PostgreSQL 16 routine vacuuming](https://www.postgresql.org/docs/16/routine-vacuuming.html) · [VACUUM options](https://www.postgresql.org/docs/16/sql-vacuum.html)
- **Physical compaction:** `VACUUM FULL` takes `ACCESS EXCLUSIVE` for the operation, blocking transcript readers and writers, and writes a new compact heap/TOAST and indexes while retaining the old files until completion. Budget roughly one current relation (3.23 GB) for the rewrite plus WAL and normal growth. A full-table `pg_repack` needs a usable unique key and client/server extension, briefly takes exclusive locks at setup and swap, and otherwise allows normal DML with a `SHARE UPDATE EXCLUSIVE` lock. Budget roughly twice the target relation (6.45 GB) plus WAL/growth. Live extension inventory found no available or installed `pg_repack`. A compacted relation can return the size difference to the filesystem. The hypothetical age-only footprint gives an order of magnitude around 0.8 GB, with actual rewrite savings requiring a rehearsal on a restored copy that measures both pre-existing slack and compacted size. [PostgreSQL 16 table rewrites](https://www.postgresql.org/docs/16/routine-vacuuming.html) · [pg_repack requirements](https://reorg.github.io/pg_repack/)
- **Partitioning and disk:** `pg_class.relkind='r'` confirms an ordinary heap. A drop of an already eligible partition removes its files without a rewrite; a direct drop takes a parent exclusive lock, while concurrent detach can use a lighter parent lock and still needs a later drop to free space. Partitioning would be a schema and retention-policy project because a time partition could contain protected sessions. `df -B1` on PGDATA recorded **190,405,451,776 bytes free (190.4 GB, 177.3 GiB)** and host `df -h /` showed **178G available, 74% used** on the same filesystem. Physical capacity presently supports deferring a rewrite. There were no replication slots and the oldest other observed transaction snapshot was under one second at the probe. These are instantaneous observations. [PostgreSQL 16 partition removal](https://www.postgresql.org/docs/16/ddl-partitioning.html#DDL-PARTITIONING-MAINTENANCE)

## Decision evidence and recovery

The operator decision is whether to commission isolated analysis of the old discovery obligations. The pertinent evidence is receipt/terminal-generation coverage for the protected sources and whether those sources still have a delivery or audit obligation. Dropping protection on age alone would remove evidence from open work, durable reply discovery and provider/session replay. Raising the window from 7 to 30 days only narrows eligibility. There is no separate supported immediate transcript purge or transcript pacing setting. The hosted pass deletes one session per commit, with no fixed row-size cap, stable order or inter-session pause in its current loop. [Transcript pass loop](../../server/Application/Services/DataRetentionService.cs#L345)

The verified 09:07 backup `antiphon-20261124T090703Z.dump` and isolated restore evidence predate later live writes. A fresh verified server2 backup immediately before any approved deletion provides a current recovery source. An in-flight delete can roll back with its own transaction. For committed batches, recovery depends on candidate/session and row-ID receipts kept off-host with the backup. Restore to isolated scratch and reconcile only the incident rows against current live state under a separately authorized recovery window using the application mutation and reseed contract. Replacing the whole live database with the morning dump would lose later production writes. Windmill task `28dc44fe` owns daily-backup reactivation, whose completion was outside this measurement. [Verified backup and restore drill](2026-11-24-server2-db-backup-verification.md#4-restore-proof-scratch-on-server2)

### Not done, noted

With zero eligible rows, schedule no cleanup batch or compaction window. If a separate decision clears specific obligations and approves a 30-day policy, take and verify a fresh server2 backup, rehearse on its isolated restore, preview the sanctioned whole-session filter plus ended-at and open-card/task holds, and size any future controlled rollout at one complete session per transaction (oldest newest-entry first, session ID as tie-break, one-second pause, one worker, 1,000-row initial canary ceiling with larger sessions held for rehearsal). Record row IDs/counts and batch time in restricted receipts, compare one-minute tagged DB latency and slow-query rate with a 15-minute baseline, stop on a lock timeout, new ingest/delivery errors or a sustained latency increase, and monitor dead tuples, autovacuum, relation file sizes, WAL and filesystem space. Leave ordinary vacuum on its regular cycle. Any explicit `VACUUM FULL` or `pg_repack` requires a separate operator-approved window and measured benefit. [Database rate diagnostics](../ops-http.md#L155)

## Reproduction anchors

Use the pinned container with `PGOPTIONS="-c default_transaction_read_only=on -c statement_timeout=20000 -c lock_timeout=2000 -c timezone=UTC"` and `psql -X -w -v ON_ERROR_STOP=1 -d antiphon` as the container's existing PostgreSQL role. The following samples project only timestamps, sizes and counts.

```sql
SELECT s.schemaname, s.relname, pg_total_relation_size(s.relid) AS total,
       pg_relation_size(s.relid) AS heap_main,
       pg_table_size(s.relid) - pg_relation_size(s.relid)
         - CASE WHEN c.reltoastrelid > 0 THEN pg_total_relation_size(c.reltoastrelid)
                ELSE 0 END AS heap_aux,
       CASE WHEN c.reltoastrelid > 0 THEN pg_total_relation_size(c.reltoastrelid)
            ELSE 0 END AS toast,
       pg_indexes_size(s.relid) AS indexes, n_live_tup, n_dead_tup, last_autovacuum
FROM pg_stat_user_tables s JOIN pg_class c ON c.oid = s.relid
ORDER BY total DESC LIMIT 15;

WITH sample AS MATERIALIZED (
  SELECT "CreatedAt" AS at, pg_column_size(t.*) AS width
  FROM "TranscriptEntries" t TABLESAMPLE SYSTEM (0.5) REPEATABLE (17)
)
SELECT count(*) AS sampled,
       count(*) FILTER (WHERE at < timestamptz '2026-10-25 18:50Z') AS n30,
       sum(width) FILTER (WHERE at < timestamptz '2026-10-25 18:50Z')
         / sum(width)::numeric AS byte_share30
FROM sample;
```

For the 30-day outbound census, join `SessionQueuedMessages.AgentSessionId` to the Stopped/Failed sessions with `LastSeenAt < '2026-10-25 18:50Z'`. Group by `Origin` after applying the `DiscoverySources` predicate in `ChannelOutboundEvidence.cs:10–17`. To reconstruct eligibility, apply all metadata guards in `DataRetentionService.cs:312–341` before reading transcripts and use a lateral aggregate constrained to each candidate's `AgentSessionId`. Confirm the plan uses the session/sequence index on the transcript table. The 30-day transcript sample reproduced 5,588 older rows of 20,148 at seed 17, with 28.123% of summed tuple width.
