# CARD-0698: antiphon-postgres burns ~5.6 cores: TranscriptEntries polled by full seq scan ~36x/s

Date: 2026-09-25. Investigate task: `ff5ae210`. Verdict: **confirmed current transcript-read amplification**, reconstructed from live statistics/activity and matching deployed source. Ready for **Plan**, with the measurement uncertainties below. This does not establish that PostgreSQL caused every overnight Windows timeout.

The dominant measured work is transcript UUID deduplication, followed by the two working-state queries. The working-state query remains part of the main card's scope; it was not the largest query in this post-cleanup sample. Connections, test databases and autovacuum did not explain the measured load.

> Publication note (Plan task e1ea172f): only this Markdown investigation is carried forward. Raw evidence and collectors remain on branch `feat/card-task-ff5ae210`, commit `f6b54d8e548b2d77f26f9ab2dcc14242c90cec16`. Evidence links below target that immutable source; the 52k-line raw evidence directory is not included in this branch.

## Custody and measurement

- Task branch `feat/card-task-ff5ae210`, initial HEAD `3fdd91abb76a06bef729f4d1e8571713c9f310cb`.
- Desktop `/api/version` returned `323872edbf9a91927e68b0adfcd93d6b6c7545aa`, capability `land-v2`. A timeout-bounded `git diff 323872edbf9a91927e68b0adfcd93d6b6c7545aa HEAD -- server client tests/Antiphon.Tests/TestHelpers` was empty. Source citations below apply to that deployed source.
- Container `antiphon-postgres`, ID `c1033627dd49`, PostgreSQL 16.14, port 17280, database `antiphon`. Used its local Unix socket via `docker exec ... psql -X`; no password/connection-string output was needed.
- SELECT only against `pg_stat*`/`pg_statio*`, `pg_settings` and catalogs. No database writes, EXPLAIN execution, resets, ALTER, VACUUM, extension installation or configuration changes. The explicitly requested follow-up cards were filed through `scripts/card.ps1 -DescriptionFile`, after sampling.
- **pg_stat_statements unavailable:** `pg_extension` contains only `plpgsql`, `shared_preload_libraries` is empty. `track_activities=on`, `track_counts=on`, `track_activity_query_size=1024`, `track_io_timing=off`. It was not enabled.
- 90 activity samples at nominal 2-second spacing, first `07:20:29.722020Z`, last `07:23:27.672902Z`. Statistics bracket: `07:20:28.660526Z` to `07:23:29.541583Z`, **180.881057 seconds**. Sixteen Docker CPU/memory samples cover the same interval. All times below are UTC unless marked local.
- The card changed while this task ran. Its later description records an operator purge of **186,362** rows followed by `VACUUM FULL` at approximately **08:11 local**, before this sample. Those are attributed card statements, not operations performed or independently replayed by this investigator. The live catalog does corroborate the resulting approximately 377k rows / 248 MiB table. See [card-details.json](https://github.com/michal-ciechan/Antiphon/blob/f6b54d8e548b2d77f26f9ab2dcc14242c90cec16/docs/investigations/2026-09-25-card-0698-postgres-load/card-details.json), CARD-0698 `updatedAt=2026-09-25T07:21:19.361066Z`. The original brief's bullets were retained as the checklist; the later card's extra retention change belongs to its separately dispatched task.

All raw statistics, activity rows and tools are in [the evidence directory](https://github.com/michal-ciechan/Antiphon/tree/f6b54d8e548b2d77f26f9ab2dcc14242c90cec16/docs/investigations/2026-09-25-card-0698-postgres-load/). `before.json`/`after.json` contain catalog rows keyed by relation name/OID; `activity.json` has explicit `sample`, PID, query start and SQL text; `summary.json` keys query shapes by SHA-256 prefix. SQL in activity includes other sessions' writes; this collector only read them. No application row contents or provider transcripts were retrieved.

Reproduce the measurement on the desktop collection host with Python and Docker available, after retrieving the collector and analyzer from the evidence commit above into a separate untracked evidence checkout/directory (these scripts are not present in this docs-only branch):

```powershell
python docs/investigations/2026-09-25-card-0698-postgres-load/collect.py 180
python docs/investigations/2026-09-25-card-0698-postgres-load/analyze.py
```

These commands overwrite the evidence files with a new measurement; preserve this committed sample when comparing. They do not build, start tests or change the database.

## What the numbers mean

`pg_stat_activity` is a snapshot, not a query history. Exact calls/minute, exact total execution time and a whole-workload mean are **unavailable**. No successful EF command timing stream was found in the inspected server log. Do not replace those missing values with query age or table-scan counts.

For the tables below, an observed call is a distinct `(PID, query_start, captured SQL)`; sampled idle statements count once. Calls/min use the effective 179.951-second activity window, approximately three minutes. The listed main query shapes all started inside the bracketing statistics window. Counts are lower bounds; rates are corresponding approximate observed rates. Fast queries can execute thousands of times unseen. `Completed n / mean / sum` uses only observations with `state=idle`, calculated as `state_change - query_start`: a sampled server statement lifecycle duration, not a representative population mean or EF/request latency. Slow active calls with no later completed observation contribute no invented duration.

Estimated active backend seconds are `active hits * 179.951 / 90`, with leader/client and parallel-worker time shown separately. This includes waits, is not CPU time, and cannot be treated as exact SQL total elapsed time. Fixed 2-second sampling can alias periodic work. Parallel workers are additional backend time, never additional SQL calls. Queries longer than 1,023 captured bytes are truncated; identical prefixes may merge several actual statements/callers.

## Container, connections and aggregate work

| Measure | Observed result |
|---|---|
| Docker CPU | **425.77% mean**, 318.63-518.53%; approximately 4.26 core equivalents |
| Docker memory | 766.4-811.4 MiB; Docker reports a 7.756 GiB limit |
| App client connections | **14-16**, mean 14.71; all database `antiphon`, empty `application_name` |
| Client states | Active 0-6, mean 2.08; idle 7-16, mean 12.33; idle-in-transaction 0-2, mean 0.30 |
| max_connections | 100; at most 16 app connections plus one collector connection, not exhaustion |
| Parallel workers | 174 worker rows over 90 samples; they are not connection-pool sessions |
| Database commits | +73,961 = **24,533.6/min**, about 409/s; includes other workloads and the small observer overhead, not SQL call counts |
| Database tuple reads | +529,249,404 returned, +42,625,592 fetched |
| Temporary files / deadlocks | 0 / 0 delta; no heavyweight lock wait in captured query samples |
| Transcript table scans | +3,827 sequential scans = **21.16/s**, +480,587,035 sequential tuples = **2.657M/s** |
| Transcript index work | +125,929 index scans; `(AgentSessionId,Sequence)` alone +125,837 scans / +39,822,813 index tuples |
| Transcript writes | **244 inserted**, 0 updated, 0 deleted in this window |

Evidence: [summary.json](https://github.com/michal-ciechan/Antiphon/blob/f6b54d8e548b2d77f26f9ab2dcc14242c90cec16/docs/investigations/2026-09-25-card-0698-postgres-load/summary.json), [docker-stats.json](https://github.com/michal-ciechan/Antiphon/blob/f6b54d8e548b2d77f26f9ab2dcc14242c90cec16/docs/investigations/2026-09-25-card-0698-postgres-load/docker-stats.json), and before/after `TranscriptEntries` relation OID 32769. Parallel scans can increment scan counters per participant, so 21.16 scans/s is not 21.16 SQL calls/s.

Transcript heap reads were +16,026,731 PostgreSQL blocks and hits +18,689,791. These are shared-buffer misses/hits, **not proof of physical disk traffic**: the OS page cache may satisfy misses. The configured shared buffer count is 16,384 blocks (128 MiB). UUID samples include 17 `IO/DataFileRead` and four `LWLock/BufferMapping` waits; 59 had no wait recorded. CPU and read churn coexist.

Idle-in-transaction appeared in 21/90 samples, 27 backend observations. Maximum observed transaction age was **15.976s**, PID 1631920 at sample 48, last statement `SELECT p."BaseBranch" ... WHERE p."Id"=$1 LIMIT 1`; PID 1631918 also reached 15.278s. That query maps to `server/Application/Services/DelegationWorktreeService.cs:225`. These transactions ended within the sample; no hours-old transaction or blocker was observed. What non-database work held each transaction is not established by the last statement alone.

The collector made approximately 90 extra short-lived psql connections plus bracket queries. The database's +131 `sessions` counter is therefore **not** evidence of application connection churn. Blank application names prevent splitting the pool by service/process using these rows.

A later Windows snapshot at 08:26:36 local reported eight logical processors and 100% total CPU; other consumers included Defender, an agent CLI and the server. Its process counters and Docker counters are different windows and accounting surfaces; do not add or directly equate them. See [host.json](https://github.com/michal-ciechan/Antiphon/blob/f6b54d8e548b2d77f26f9ab2dcc14242c90cec16/docs/investigations/2026-09-25-card-0698-postgres-load/host.json). The current evidence establishes a substantial PostgreSQL contributor, not an exclusive cause of all host saturation.

## Ranked queries and mechanisms

There were 353 active, query-bearing client/worker hits; **332 (94.1%) referenced TranscriptEntries**. Eight further parallel-worker observations had no captured query. The table below ranks identifiable application queries by sampled backend occupancy. IDs refer to `summary.json` and full SQL in `activity.json`.

| Rank / SQL fingerprint | Observed calls (~per min) | Client + worker active seconds (estimated) | Completed n / mean ms / sum ms | Distinct client PIDs |
|---|---:|---:|---:|---:|
| 1. UUID batch dedup `b44a9eb28d50` | 141 (~47.01) | **177.95 + 275.92** | 52 / 134.206 / 6,978.726 | 16 |
| 2a. Working after boundary `12515f1bfa9c` | 50 (~16.67) | **69.98 + 8.00** | 15 / 132.433 / 1,986.493 | 16 |
| 2b. Working without boundary `80f411352c23` | 38 (~12.67) | **45.99 + 4.00** | 15 / 61.931 / 928.970 | 13 |
| 3. Boundary UUID existence `755b2ec881be` | 14 (~4.67) | **18.00 + 29.99** | 5 / 142.764 / 713.821 | 9 |
| 4. UserPrompt receipt rows `969fe6ea9d33` | 12 (~4.00) | 4.00 + 8.00 | 10 / 141.027 / 1,410.265 | 7 |
| 5. Queued input confirmation texts `e89c3b22db43` | 6 (~2.00) | 4.00 + 6.00 | 4 / 0.036 / 0.144 | 6 |
| 6. Completion-note existence `c8af6330c2b1` | 28 (~9.34) | 6.00 + 0 | 25 / 7.443 / 186.073 | 12 |
| 7. Full-session compaction projection `3da5930422f4` | 4 (~1.33) | 6.00 + 0 | 1 / 0.323 / 0.323 | 3 |

Ranks 6/7 tie with other 3-hit queries: `AgentSessions.LastSeenAt` updates (`cb82bcc9c0b4`, 27 observed, 24 completed, mean 10.078ms, sum 241.866ms, 15 PIDs, all three active waits WALSync) and `DISCARD ALL` connection resets. Those are not larger sampled consumers than the transcript reads.

**1 / 3: repeated UUID membership checks have no matching UUID index.** The exact rank-1 SQL is:

```sql
SELECT t."Uuid", t."Kind"
FROM "TranscriptEntries" AS t
WHERE t."AgentSessionId" = $1 AND t."Uuid" IS NOT NULL AND t."Uuid" = ANY ($2)
```

`server/Application/Services/AgentSessionRuntime.cs:850` issues this for each nonempty persist batch; `:838` first checks session existence, `:868` then obtains the current maximum sequence. It still does these reads when the entire incoming batch was already persisted (`:924`). Individual live transcript events reach this path at `:374`; full catch-up reaches it at `:705` and `:727`. The separate turn-boundary check is the exact `(session,uuid,kind)` EXISTS shape at `:517`.

The live catalog lists **only** `PK_TranscriptEntries(Id)`, unique `IX_TranscriptEntries_AgentSessionId_Sequence(AgentSessionId,Sequence)`, and partial `IX_TranscriptEntries_IsApiError`. Source `server/Infrastructure/Data/AppDbContext.cs:1227` and `:1234` agrees. UUID is not an indexed key. The session prefix can restrict a lookup, but UUID matching cannot seek by its dedup key. The same UUID query was observed in client backends and two parallel workers; together the two UUID shapes account for **251/353 = 71.1%** of query-bearing active hits. This reconstructs expensive repeated history membership work, consistent with the very large transcript scan delta and tiny insert delta. A per-query execution plan was not captured, so the exact access path chosen for each parameter set remains unproven.

**2: each working-state check recomputes historical boundaries twice.** `server/Application/Services/SessionMessageQueueService.cs:4593` sends even a single-session request through `IsWorkingBatchAsync`. Its `end` query (`:4616`) restricts to supplied session IDs, filters kinds/text and groups by session to compute both max sequence and max timestamp. The after-end query (`:4663`) joins activity to that aggregate; the without-end query (`:4683`) separately tests activity against the same grouped end query. The two database round trips match the captured SQL prefixes exactly. Both must inspect historical boundary information; the second query does not reuse the first query's aggregate result.

Callers include queue flush/status checks (`SessionMessageQueueService.cs:612`, `:1362`, `:4718`), agent list (`AgentService.cs:100`), attention (`AttentionService.cs:2591`), and dispatcher sweeps (`AgentTaskDispatcher.cs:5900`, `:6366`). Their combined queries account for **64/353 = 18.1%** of query-bearing active hits. The separate simple `max(Sequence)` lookup was sampled 52 times, all idle/completed, mean **0.05075ms**, sum **2.639ms**, with zero active hits. It must not be conflated with the costly grouped working-state query.

Two corrections to the card's initial suggested diagnosis follow directly from evidence: **the grouped query already has an AgentSessionId predicate**, and **the suggested (AgentSessionId,Sequence) index already exists and is heavily used**. No missing-migration explanation is supported.

**4 / 5 / 7: receipt and compaction reads add further transcript work.** The UserPrompt receipt shape matches `LandNoteReceipt.Prompts` (`server/Application/Services/LandNoteReceipt.cs:38`) consumed and ordered by `AgentTaskLandNotificationService.cs:200`; compatible receipt reads also exist at `CheckCompactionContinuationService.cs:797`, so this SQL alone cannot uniquely assign a caller. The text projection with UserPrompt/QueuedUserPrompt/ask-user-question ToolResult predicates maps to `SessionMessageQueueService.cs:3641` (also the related earlier candidate path at `:3495`). The full-session projection with token columns, ModelCalls and CreatedAt is `ContextCompactionService.cs:236`. Their volumes are below the UUID/working-state paths in this window; none establishes a rapid retry loop by itself.

## High observed call counts and independent structural findings

Ranking by **observed calls**, rather than occupancy, changes the top list:

| Query / fingerprint | Observed calls (~per min) | Completed n / mean ms / sum ms | PIDs | Issuing source |
|---|---:|---:|---:|---|
| Sibling boards `b2f9a5cacf69` | **224 (~74.69)** | 224 / 0.05850 / 13.103 | 16 | `CardTaskFileService.cs:203` |
| Runner binding `dcf721a3945f` | **179 (~59.68)** | 178 / 0.03752 / 6.679 | 16 | `PhoneHomeRunnerDirectory.cs:120` |
| UUID dedup `b44a9eb28d50` | **141 (~47.01)** | 52 / 134.206 / 6,978.726 | 16 | `AgentSessionRuntime.cs:850` |
| Session existence `f2518b8a7dea` | **70 (~23.34)** | 70 / 0.04510 / 3.157 | 16 | e.g. `AgentSessionRuntime.cs:838`; multiple callers |
| Full AgentSessions prefix `9e77b955c0c1` | **61 (~20.34)** | 58 / 0.05184 / 3.007 | 15 | Truncated before WHERE; cannot identify one issuing method |
| Simple max sequence `4d593149e3da` | **52 (~17.34)** | 52 / 0.05075 / 2.639 | 16 | `AgentSessionRuntime.cs:868` and other equivalent callers |

All source files in that table are under `server/Application/Services/`, except `PhoneHomeRunnerDirectory.cs` under `server/Infrastructure/Agents/SessionRunner/`. Sibling-board reads had zero active hits; runner binding had one (approximately 2 sampled backend seconds). These are observed-call rankings, **not** a complete ranking of actual calls.

Two separate defects were filed using the requested CLI/file path, with no spawn:

- **CARD-0699**, `f81a4171-69f1-43f2-853e-494c9443fb63`: completion recovery selects all historical terminal sourced tasks every pass, without excluding stamped tasks or paging (`server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs:66`). Every one-second pass repairs stamps per task (`server/Application/Services/CompletionNoteStamp.cs:34`) and makes per-task existence probes (`AgentTaskCheckService.cs:291`). The conditional stamp UPDATE was itself observed 22 times (~7.34/min; 21 completed, mean 0.161ms, sum 3.379ms, 11 PIDs). The TaskCompletion EXISTS is rank 6 above. Land notifications accumulated 1,052 seq scans / 1.40M tuple reads, but other callers contribute to those table totals. Mechanism: durable idempotency prevents duplicate delivery without removing historical candidates from recurring work. See [filed description](https://github.com/michal-ciechan/Antiphon/blob/f6b54d8e548b2d77f26f9ab2dcc14242c90cec16/docs/investigations/2026-09-25-card-0698-postgres-load/completion-defect.md).
- **CARD-0700**, `59658f2d-babb-45fa-88fb-3d64eb1f51d2`: card-file sync iterates every board (`server/Application/Services/CardTaskFileService.cs:43`); each inspection loops other boards and recalculates unpinned sibling slugs before checking publication eligibility (`CardTaskFilePolicy.cs:203`, `:209`, `:213`). `UniqueBoardSlugAsync` repeats the exact hot sibling SELECT (`CardTaskFileService.cs:203`). `GetCardStatusAsync` invokes the same inspection (`CardTaskFilePolicy.cs:145`). The nested board/sibling lookup structure can grow quadratically per global sweep. Boards had **9,099 seq scans / 877,915 tuple reads** over 180.881s. This is work amplification despite a normal timer, not evidence of an interval violation, and its low observed server durations do not make it the principal CPU culprit. See [filed description](https://github.com/michal-ciechan/Antiphon/blob/f6b54d8e548b2d77f26f9ab2dcc14242c90cec16/docs/investigations/2026-09-25-card-0698-postgres-load/card-files-defect.md).

## Poller and endpoint audit

The listed intervals are **source defaults/guards**, not a claim to have read effective live configuration. Sampling establishes query occupancy, not the exact frequency of every HTTP request or hosted-service tick.

| Requested area | Source evidence and conclusion |
|---|---|
| Dispatcher tick | `AgentTaskDispatcherHostedService.cs:35` uses a PeriodicTimer with at least 1s; default `DelegationSettings.cs:16` is 5s. Serial tick invokes independent sweeps (`AgentTaskDispatcher.cs:293`); working-state checks above are reached by deferred report/warm-agent work. No demonstrated delay-free dispatcher loop. |
| Janitor | `server/Infrastructure/Git/WorktreeJanitorHostedService.cs:49` delays at least one hour after each pass. No query attribution as a hot consumer. |
| Reconciliation | `server/Infrastructure/Agents/SessionReconciliationHostedService.cs:32` clamps to at least 1s; default `SessionReconciliationSettings.cs:21` is 15s. No observed evidence that it runs faster. |
| Land monitor / notifications | `AgentTaskLandMonitorHostedService.cs:21` delays LandSweepSeconds (default 5, validated 1-60); notification reconciliation has a fixed 5s delay at `AgentTaskLandNotificationHostedService.cs:121`. Receipt queries and notification updates are observed, but their shares cannot be separated from the completion recovery scanner by activity alone. |
| Check / interpreter | `AgentTaskCheckHostedService.cs:58` drains a channel; failures are dropped after schedule advancement (`:76`). It is not an unconditional database busy loop. Working-state and receipt reads are shared with check-related paths. |
| Completion recovery | `CompletionNoteWorkHostedService.cs:51` really waits 1s; the structural issue is historical/per-row work per pass, CARD-0699. |
| Card-file sync | `CardTaskFileSyncHostedService.cs:45` uses a timer with at least 5s, default 60s. CARD-0700 explains why many SQL queries do not imply 140 timer ticks/s. |
| `/api/cards` | `client/src/api/boards.ts:566` has no `refetchInterval`; SignalR invalidates list/detail keys (`client/src/hooks/useSignalRInvalidation.ts:76`). Cards saw 9 seq scans and 449 index scans in the window, unlike the Boards amplification. Actual HTTP request frequency is unmeasured. |
| `agent-tasks/pipeline` and attention | `client/src/api/agentTasks.ts:741` and `client/src/api/attention.ts:249` poll every 15s; agent-list polling is 5s (`client/src/api/agents.ts:554`). Agent/attention callers share working-state queries. No endpoint-to-SQL trace means their exact contributions remain unassigned. |
| Transcript UI | `client/src/features/agents/SessionTranscriptPanel.tsx:214` loads initially and on SignalR reconnect (`:236`), not on an unconditional interval. Server transcript GET always syncs first (`AgentSessionService.cs:1733`); runner fetch has no since cursor (`SessionRunnerHttpClient.cs:380`), then persist dedupes the returned history. This is a real amplification route; its frequency in this sample is not known. |
| Runner event reconnect | `SessionRunnerEventPump.cs:94` backs off exceptions by at least 100ms (default 1000ms). A normal stream EOF falls through the outer loop without that delay; no repeated EOF or catch-up storm was evidenced. Latest catch-up log was 06:08:51.889 local, well before sampling. This branch is not presented as the live root cause. |
| CARD-0679 R6 / CARD-0696 | `PhoneHomeRunnerDirectory.cs:348` queries bound session IDs only before the first recovered inventory; `:374` is the synchronous indexed source query. The sampled RunnerId/RunnerStoreId/RunnerCwd projection is instead ordinary **binding resolution at :120**. Do not mislabel it as UnknownRemoteSessionIds. No exact bound-ID query was captured. The known unbounded pending-inventory issue is already tracked by CARD-0696, not duplicated. |
| CARD-0691 sweep | Its plan exists at `docs/superpowers/plans/2026-09-25-card-0691-pool-release-and-ptyhost-leaks-plan.md:69`, but the named new `ReleaseUnownedPoolDelegatesAsync` and `PoolReleaseGraceSeconds` are absent from the inspected/deployed source. That proposed sweep cannot explain this sample. Existing warm-agent retirement remains one working-state caller. |

Unless a fuller path is shown, hosted-service filenames in this table are under `server/Infrastructure/Orchestration/`, settings under `server/Application/Settings/`, application services under `server/Application/Services/`, and runner client/directory/pump under `server/Infrastructure/Agents/SessionRunner/`.

Log corroboration: `C:\src\Antiphon\server\logs\antiphon-20260925.log:57065` records the latest startup catch-up for 39 sessions at 06:08:51.889 local; `:39286` records a prior disconnect at 04:06:49.693 local. These are not activity-window reconnects. Error-only DbCommand lines exist, but successful per-command timings were not available for the requested window.

## Tables, dead tuples, autovacuum and tests

End-of-window public-table catalog/statistics values (sizes include indexes and TOAST):

| Table | Total MiB | Estimated live / dead rows | Latest autovacuum UTC | Window seq scans / index scans |
|---|---:|---:|---|---:|
| TranscriptEntries | 247.93 | 377,647 / 0 | Sep 25 01:21:54 | 3,827 / 125,929 |
| SessionQueuedMessages | 28.76 | 12,654 / 1,125 | Sep 24 15:30:32 | 129 / 3,893 |
| AgentTaskLandNotifications | 34.48 | 2,584 / 48 | Sep 25 07:21:58 | 1,052 / 1,617 |
| AgentTaskLandings | 0.46 | 225 / 76 | Sep 24 19:29:38 | See raw counters |
| AgentTaskLandRequests | 0.60 | 450 / 74 | Sep 25 03:41:52 | See raw counters |
| CardRevisions | 4.66 | 4,374 / 0 | None recorded | 3 / 0 |
| Alerts | 40.48 | 10,308 / 239 | Sep 24 01:15:09 | See raw counters |
| AgentIncidents | 3.38 | 3,534 / 511 | Sep 24 01:15:09 | 50 / 198 |

There is no Attention table: `server/Application/Services/AttentionService.cs:150` builds the feed from other rows, including working-state reads. Autovacuum is on (scale factor 0.2, threshold 50); notification autovacuum and autoanalyze each advanced once during the window, and AgentSessions autoanalyze twice. Neither bracket snapshot had an in-progress vacuum. Transcript estimates had zero dead tuples and 707 modifications since analyze initially, 951 finally; existing statistics had not been invalidated by millions of new rows. No transcript vacuum ran during the sample.

**Physical bloat was not measured.** Dead-tuple estimates and heap/index sizes cannot establish free-space bloat, and no pgstattuple extension or page scan was added. Some small historical tables may retain spare pages; that is not evidence that bloat caused the measured transcript work. The card's earlier full vacuum also makes this a post-compaction snapshot.

The dev instance listed only `antiphon`, `postgres`, `template0` and `template1`; its only non-system table schema was `public` (95 tables). No test database/schema or test connection was seen. `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:28` creates a separate postgres:16-alpine Testcontainer and `:41` obtains that container's connection string. `TestDbFixture.cs:29` bypasses warmup for owned worker children; CARD-0646 did not redirect the default fixture to port 17280.

Two simultaneous test containers were independently observed on ports **65324** and **64513**, not 17280. Later Docker snapshot: dev Postgres **358.87%** CPU versus **0.02%** and **4.43%** for those test containers; memory 787.5 MiB versus 80.03/187.7 MiB. See [test-container-stats.jsonl](https://github.com/michal-ciechan/Antiphon/blob/f6b54d8e548b2d77f26f9ab2dcc14242c90cec16/docs/investigations/2026-09-25-card-0698-postgres-load/test-container-stats.jsonl). They still share desktop/WSL resources; this snapshot does not reconstruct their overnight peak. Arbitrary custom tests with explicit connection strings are not ruled out historically. No tests or builds were launched by this investigation.

## Remaining uncertainties and stage boundary

1. No pg_stat_statements, live EXPLAIN plan, parameter values or tracing: exact query execution counts/totals, caller shares and chosen per-parameter plans are not known. An isolated reproduction with representative session history and execution-plan/command capture would resolve the access-path and per-call attribution gaps. This does not require changing the live DB in this stage.
2. Fixed-interval activity sampling cannot prove the absence of short bursts, sub-two-second locks or every too-fast poller. The normal-EOF reconnect branch is unobserved. Endpoint/request and per-service instrumentation in a separate authorized investigation would resolve those specific questions.
3. The earlier ~564% CPU / 35.8 scans/s card measurements and the operator's manual cleanup were not collected here; this artifact independently establishes continuing post-cleanup load at 425.77% average CPU. Historical host saturation and the effect of the separately commissioned seven-day retention change remain outside this measured window.
4. UUID missing-key lookup cost and historical working-state computation are reconstructed mechanisms, not evidence for a particular replacement design. Their correctness constraints include replay dedup, manual-vs-auto compaction, interrupted prompts, row-correlated sequence/timestamp ordering, and complete delivery receipts, all visible in the cited source.

## Not done, noted

Plan should evaluate the measured transcript access paths and repeated historical working-state computation; no fix, index, cache, polling/retention change, red-test design or Checkpoints manifest was designed or implemented in this Investigate stage.

The brief requested a plan artifact and `next: code`, but the governing Investigate bundle explicitly forbids fix design and requires `next: plan` for confirmed mechanisms. Therefore the requested `docs/superpowers/plans/2026-09-25-card-0698-postgres-load-plan.md` is deliberately not claimed as delivered. No additional approval was requested: standing authority covered the investigation and the two follow-up card filings. This evidence and its collectors are the only repository changes.
