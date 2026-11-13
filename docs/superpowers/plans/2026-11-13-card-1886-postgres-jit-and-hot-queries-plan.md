# CARD-1886 / CARD-1887 / CARD-1888: Postgres JIT and hot queries

Plan and verification design, 2026-11-13. Next stage: Code. This is a plan-only change; no database, running service, qualification, or product source was changed.

## Evidence and scope

Source baseline: `3dc5e5c335c9ea0e3d622b042ebf809584ce0117`. Read all five live Antiphon cards, CARD-1886 through CARD-1890. Attribution source: `docs/investigations/2026-11-13-card-1886-antiphon-cpu-attribution.md` at `a2d315284535a1bf728ddd769969971fcc3c3ebb` on `origin/feat/card-task-1540fb0b` (fetch that ref to read the report; it need not be landed to implement this plan). Its running build was `ab7af12c`, its code citations were to `b57583ce`; those observations are not measurements of this baseline.

The evidence supports JIT suppression first: 63,764 seconds of reported JIT versus 116,320 seconds of statement execution since the reset; the claim alone accounted for about 51% of execution. A three-row claim took 995 ms with 41 compiled functions and 14 shared buffers. Server process CPU was 0.92–0.95 cores, runner 0.05–0.06. Postgres container samples were 1.3–3.4 cores. SQL execution time includes waits and is **not measured CPU**. Do not add SQL-time savings to server-process CPU savings.

Two qualifications matter for the baseline: the report labels a delta “120 s” but prints endpoints 13:54:11–13:58:13 (242 s), and claim JIT totals slightly exceed its reported execution total. Retain the original observations; reconstruct elapsed time and component accounting from actual snapshots before crediting a core-equivalent saving. Never reset statistics to simplify comparison.

CARD-1889 owns durable CPU/query attribution and CARD-1890 owns logging/polling changes. This plan supplies measurements for them, not their implementation. The server's 0.95 cores cannot yet be assigned to Serilog, JSON, EF materialization, runtime actions, or polling. That requires the post-run trace.

## Decisions

- **D-1 — Application connections disable JIT.** Use an Npgsql startup `Options=-c jit=off` policy through a shared helper, applied to the server EF connection and test fixture application connections. No per-query `SET`, schema migration, database/role alteration, compose restart, or new retry policy. This immediately covers the observed EF reads and preserves independently configured administrative/analytical connections.
- **D-2 — Keep claim selection in one SQL statement.** After S1, replace the deeply correlated channel-key matching with materialized session candidates and effective key sets. Preserve global phase/sequence/trigger ordering and existing executor/retry behavior. Do not introduce a separate existence probe followed by a differently snapshotted claim.
- **D-3 — Materialize the open-task pin set once per statement.** Replace the correlated task OR with an uncorrelated union; retain all other pin reasons and the one-command seed/pins budgets. First ship a query-only rewrite. Partial indexes are evidence-gated follow-up candidates, not a speculative migration bundled into this Code scope.
- **D-4 — Deduplication remains `(UUID, kind)`.** A bounded, positive-only identity cache attached to the existing session write lease removes replay probes. It is an optimization, never the durable authority. A stored `MAX(Sequence)` or timestamp cutoff is rejected because runner sequences restart, stored sequences are rebased, timestamps may be null/out of order, and one UUID has multiple normalized kinds.
- **D-5 — Preserve complete evidence contracts.** Use SQL aggregates for scalar answers and keyset pages for history traversal. Do not truncate `RawPrompts`, lose command-wrapper context, change receipt readiness, or treat an incremental network read as proof that all entries committed. Public transcript DTOs/routes retain their current shape and semantics in this scope.
- **D-6 — Ship in measured slices.** S1 first; S2–S5 have distinct production files and can be implemented independently after S1 by separately commissioned tasks. This delegate does not dispatch them. Integrate sequentially and recheck touched file lists before parallel work. Do not place tests/builds or profiling load on the desktop until run `97150a0b` has ended.
- **D-7 — No behavior changes to sessions waiting for input.** Existing parking/release rules remain; with parking disabled no automatic release deadline exists. Query optimization grants no new typing, kill, parking, or absence-evidence authority.

These are engineering choices supported by the inspected code, not outstanding operator decisions. Verification is folded into this artifact so the next dispatch can execute Code directly.

## Ground truth

| Card assumption | What this checkout actually does | Consequence |
|---|---|---|
| Configure a shared data source or connection-open hook | `server/Program.cs:125–138` passes the configured string to `UseNpgsql`; no application `NpgsqlDataSource` registration or JIT connection interceptor was found. Existing interceptors record commands. Hangfire has separate raw Npgsql connections in `HangfireConfiguration.cs`. | Add the policy at the EF composition boundary; do not accidentally claim Hangfire or messaging coverage. |
| Aspire defines a Postgres container command | `Antiphon.AppHost/Program.cs:20–25` uses `AddConnectionString`; Postgres is external, owned by `docker-compose.dev.yml`, which already loads pg_stat_statements. | There is no Aspire Postgres command to patch. Connection policy follows the server on desktop and new stacks. |
| A channel existence condition alone makes the claim cheap | `SessionRuntimeActionCoordinator.DrainCoreAsync` includes nested `LateConfirmedChannelMessageIds.Contains`/`Any` expressions even for non-channel candidates. Planner work remains although subplans are never executed. | A SQL `CASE` or another OR alone is insufficient; remove repeated correlated key expansion. |
| The claim means “earliest due row” | Non-channel rows remain eligible before the later due-time check; ordering is `(Phase >= QueuePhase), Sequence, TriggerKind`. Earlier channel debt blocks only overlapping destinations, including late-confirmed sources; a null carried key blocks every destination. | Do not add a global `DueAt <= now` filter or change ties. Preserve CARD-1434/1826. |
| Seed and pins are the same expensive task lookup | Both embed `Pinned`; seed additionally scans transcript totals and several lateral boundary/activity probes for up to 512 requested sessions. Pins scans session IDs with pin predicates. | Compare pin time separately from transcript aggregation/sorts; an index on tasks cannot remove seed's transcript count cost. |
| Task indexes are absent | The model has `IX_AgentTasks_Status` and `IX_AgentTasks_AgentSessionId`; the snapshot has no task `ParentSessionId` index. | Use the existing status index to enumerate open tasks once; inspect actual pg_indexes before adding anything. |
| Identity probing is a single old SELECT | CARD-1403 combines session/generation, stored max, first UUID chunk and null-UUID sequences in `TranscriptIngestPrelude`; further UUID chunks are at most 512 keys. | Preserve its snapshot and command budget; update affected old tests explicitly. |
| A since cursor has not been implemented | `TranscriptSinceRead` already requests generation/stream-bound network deltas, validates continuity, and returns the **reassembled full body** from `TranscriptReadHold`. | Network cursors explain why DB replay probes persist. Network read success must not advance an ingest cursor. |
| All whole-history reads can take a recent cutoff | `TranscriptPromptSpan.LoadAsync` returns `RawPrompts`, and uses all in-window wrappers to recognize command echoes. `PolicyRefreshService.HasBeenIdleLongEnoughAsync` materializes timestamps just to compute max. | Page complete semantic spans; aggregate scalar timestamps in SQL. |
| The full entity SQL has one proven caller | The report omits that query's complete text/queryid. `ChannelReplyDispatcher.ClassifyTtlLossAsync` is a matching full-entity prompt-history candidate; other complete recovery reads exist. | Confirm normalized SQL/parameters after the run before attributing its 1,567 ms mean to a particular method. |

## CARD-1886 options and implementation

| Option | Desktop, tests, new stacks | Reason to choose or reject |
|---|---|---|
| `ALTER DATABASE antiphon SET jit = off` | Applies to future connections to that database. Each disposable test DB and new deployment needs provisioning parity; existing pools need renewal. | Broad and simple operational mitigation, but changes persistent deployment state and misses clones/new DBs unless explicitly managed. Not this plan's default. No hand edits to `antiphon_pgdata`. |
| `ALTER ROLE ... IN DATABASE ... SET jit = off` | Requires known app roles and database names on each stack; future sessions only. | More scoped than database-wide, but identity/provisioning drift and shared-role effects remain. No assumed role name or superuser privileges in application migrations. |
| Npgsql startup `Options=-c jit=off` | Application creates new physical connections with the same policy on every host; common test connection factory uses the same helper. | **Recommended.** No extra SQL command on each open and no implicit transaction. Tests must prove physical-open and pool-reuse behavior. |
| `DbConnectionInterceptor.ConnectionOpened[Async]` issuing `SET jit=off` | Must cover both sync/async paths and every DI/test registration; applies on pooled opens too. | Extra round trip and command accounting at a hot boundary; manual connections can bypass it. Larger surface than startup options. |
| `SET LOCAL jit=off` around particular queries | Requires an explicit transaction enclosing each read; expires at transaction end. | Narrow but adds transaction/statement cost and interacts with existing recovery fences. Reject for the default. |
| Raise `jit_above_cost` in Postgres command | Change compose's Postgres command; Aspire only references it. Other production manifests/test images need parity, and activation needs container recreation/restart without deleting data. | A new arbitrary threshold can be crossed again by bad estimates. Raising inline/optimize thresholds alone still allows compilation. Broader operational change than needed. |
| Rewrite claim alone | Same SQL on all EF connections, no deployment setting. | Necessary structural improvement, but does not remove JIT from pins or other OLTP statements. Larger correctness risk, so S1 precedes it. |

JIT can help long CPU-bound analytical queries and hurt short reads; its decision uses estimated plan cost, including generic prepared-plan decisions. See [PostgreSQL 16 JIT decisions](https://www.postgresql.org/docs/16/jit-decision.html). No analytical workload requiring JIT is established by the inspected paths; that is not proof that none exists. Compare the other expensive statements before/after and keep analytical jobs on separately configured connections if a measured regression justifies it.

### S1 — connection policy

Files: new `server/Infrastructure/Data/ApplicationPostgresConnection.cs`; `server/Program.cs`; `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs`; new `tests/Antiphon.Tests/Infrastructure/PostgresJitPolicyTests.cs`; existing fixture tests as necessary; a short operational note in `docs/testing-and-build.md`.

Implement a pure connection-string transformation using `NpgsqlConnectionStringBuilder`. Preserve all unrelated fields and existing `Options`, append a final `-c jit=off` once, and make repeated application idempotent. A supplied earlier `-c jit=on` must not win. Do not parse or serialize connection settings in logs or assertion failure messages. Npgsql documents `Options` as startup server parameters: [connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html). Verify behavior with the repository's Npgsql/EF provider version, not just a source-string assertion.

Call the helper before `Program`'s EF `UseNpgsql`. Apply it when `TestDbFixtureLifecycle` publishes the shared application connection (`shared` in initialization); cloned DB strings inherit it. Derive the maintenance connection from the original string so administrative behavior is unchanged. Audit the factory override in `AntiphonWebAppFactory`, `BuildOptions`, and manual `SessionStateTestFixture`/`TranscriptHotPathFixture` options: they should consume the normalized fixture string, not implement another policy. Exercise that path in tests. No `AppDbContext` model edit is needed. Hangfire stays outside this first change because the attributed statements are application EF queries.

Activation is a later reviewed deployment of the server on the active rc line, after the live run. Newly created EF pools take the policy without database recreation. Verify `current_setting('jit')` through the **application connection**, not an unrelated psql connection; psql may correctly continue to say `on`. Rollback is a code revert followed by normal server activation, not a database `Down` or pool intervention during this Plan.

### S2 — claim query with bounded key expansion

Files: `server/Application/Services/SessionRuntimeActionCoordinator.cs`; new `server/Infrastructure/Data/SessionRuntimeActionClaimQuery.cs`; new `tests/Antiphon.Tests/Application/RuntimeActionClaimQueryTests.cs`; existing runtime action test files only when adding behavioral assertions.

Use one parameterized SQL statement returning the mapped action entity (`FromSqlRaw` with typed `@sessionId`, `@now`, `@queuePhase`; no string-interpolated identifiers). The replacement shape is:

```sql
WITH candidates AS MATERIALIZED (
  SELECT * FROM "SessionRuntimeActions" WHERE "AgentSessionId" = @sessionId
), channel AS MATERIALIZED (
  SELECT * FROM candidates WHERE "TriggerKind" = 'channel'
), destinations AS MATERIALIZED (
  SELECT "Id" AS action_id, "ConversationKey" AS destination
  FROM channel WHERE "ConversationKey" IS NOT NULL
  UNION
  SELECT c."Id", m."ConversationKey"
  FROM channel c
  CROSS JOIN LATERAL unnest(c."LateConfirmedChannelMessageIds") AS late(id)
  JOIN "SessionQueuedMessages" m ON m."Id" = late.id
    AND m."AgentSessionId" = @sessionId
  WHERE m."ConversationKey" IS NOT NULL
)
SELECT a.* FROM candidates a
WHERE a."TriggerKind" <> 'channel'
 OR (a."DueAt" <= @now AND NOT EXISTS (
   SELECT 1 FROM channel b WHERE b."Sequence" < a."Sequence"
   AND (b."ConversationKey" IS NULL OR a."ConversationKey" IS NULL
     OR EXISTS (SELECT 1 FROM destinations da JOIN destinations db
                  ON db.destination = da.destination
                WHERE da.action_id = a."Id" AND db.action_id = b."Id"))))
ORDER BY (a."Phase" >= @queuePhase), a."Sequence", a."TriggerKind"
LIMIT 1;
```

Use the actual `ChannelReplyDebt` constant (currently `channel`); keep every mapped column and its EF conversion. The destination set includes carried keys and every non-null late-confirmed source key. Empty/null arrays contribute no late keys. Null carried keys retain the existing wildcard semantics even when late keys exist. Same-sequence rows do not block each other because the old predicate uses strict `<`.

Channel key expansion is fed only by channel rows. When there are none, it produces no work; the repeated table-wide OR-correlated subqueries disappear. Materialization is a candidate plan improvement, not a promised estimated-cost value. Measure small and debt-heavy cases; do not silently set `enable_seqscan=off` or inflate JIT thresholds to make the rewrite look successful. Existing retry-time propagation is unchanged and remains a separately measured possible hot query.

Collect old versus new `EXPLAIN (ANALYZE, BUFFERS, SETTINGS, FORMAT JSON, TIMING OFF)` on disposable representative data with JIT both on and off, custom and generic prepared plans. Record estimates, actual rows/loops, JIT phases, buffers, planning/execution time, sort spill, and output action identity. Fixtures: no actions; transcript-only; channel-only; mixed phases; not-yet-due earlier non-channel; different destinations; null wildcard; late keys matching carried/late keys; multiple late keys; same sequence. Gate: identical first action and subsequent drain order; no JIT with application policy; transcript-only plan below the default 100,000 JIT cost threshold and no queued-message work. If this last goal fails, keep S1 and investigate S2 rather than claim its structural goal was met.

## CARD-1887: seed and pins

### S3 — uncorrelated open-task membership

Files: `server/Infrastructure/Data/SessionStateLoader.cs`; new `tests/Antiphon.Tests/Application/SessionStatePinQueryTests.cs`; extend `SessionStateProjectionTests.cs` and `SessionStateWarmupTests.cs` where needed. Do not edit S4's store or runtime files.

Current task predicate is `EXISTS (... Status IN (0,1,2,3) AND (AgentSessionId=s.Id OR ParentSessionId=s.Id))`. Both statements should start with:

```sql
WITH open_tasks AS MATERIALIZED (
  SELECT "AgentSessionId", "ParentSessionId" FROM "AgentTasks"
  WHERE "Status" IN (0,1,2,3)
), task_pins AS MATERIALIZED (
  SELECT "AgentSessionId" AS id FROM open_tasks WHERE "AgentSessionId" IS NOT NULL
  UNION
  SELECT "ParentSessionId" FROM open_tasks WHERE "ParentSessionId" IS NOT NULL
)
-- retain existing SELECT / lateral projections
-- replace ONLY the task arm of Pinned with:
-- s."Id" IN (SELECT id FROM task_pins)
```

This pays for the open-task set once, deduplicates parent/child identity, and lets an uncorrelated membership subplan be hashed. Status literals remain the persisted enum values. Preserve active session statuses 1/2/3, all unsettled queued-message conditions, unconfirmed nonterminal land notifications, and unmaterialized nonterminal dispatch warnings. A missing session remains missing, even if a task contains its ID. Do not add a process cache requiring invalidation on every task status transition; stale false pin membership risks eviction of evidence.

Before and after, explain the exact tagged production statements with 1, 64 and 512 requested sessions and pins over the whole session set. Seed transcript cardinalities should include 0, 1,500 and roughly 75,000 rows/session; task fixture should approximate 27,695 mostly terminal rows and varied open-task distribution. Record task rows and filters, relation sizes, estimates versus actuals, shared reads/hits, lateral loops, aggregate cost, sort method/temp blocks. Inspect `pg_stat_activity.wait_event_type/wait_event` and `pg_locks` during slow calls; a sequential scan alone does not prove all elapsed time is CPU, and the current report does not establish locking or sorts as causes. Run `ANALYZE` only on owned fixture data; do not VACUUM/ANALYZE or reconfigure the live database during the Plan.

If this fails the acceptance below, compare splitting into two independent indexed EXISTS probes and these **additive migration candidates**, with actual index inventory first:

```sql
CREATE INDEX CONCURRENTLY "IX_AgentTasks_Open_AgentSessionId"
ON "AgentTasks" ("AgentSessionId")
WHERE "Status" IN (0,1,2,3) AND "AgentSessionId" IS NOT NULL;
CREATE INDEX CONCURRENTLY "IX_AgentTasks_Open_ParentSessionId"
ON "AgentTasks" ("ParentSessionId")
WHERE "Status" IN (0,1,2,3) AND "ParentSessionId" IS NOT NULL;
```

These can help separate equality probes; the single status-index scan may already be cheaper for whole-fleet pins. No redundant transcript index is proposed: `(session, sequence)`, `(session, kind, sequence)`, partial `(session, uuid) INCLUDE kind`, and working-boundary indexes already exist. A later migration must be CLI-generated after the latest master migration, retain existing indexes, avoid a transaction around concurrent index creation, and have a safe `Down`. Its amendment must include the entire CARD-1782 migration/downgrade selector and model tests, not just the new index test. It is outside this plan's closed Code list until those additional rows are concrete.

## CARD-1888: replay identity and full reads

### S4 — bounded committed-identity reuse

Files: `server/Application/Services/AgentSessionRuntime.cs`, `server/Application/Services/SessionStateStore.cs`, new `server/Application/Services/TranscriptIdentityWindow.cs`; `server/Infrastructure/Data/TranscriptIngestPrelude.cs` only if the result needs metadata; new `tests/Antiphon.Tests/Application/TranscriptIdentityWindowTests.cs`; existing `AgentSessionRuntimeTests.Persist.cs`, `SessionStateCommitTests.cs`, and `tests/Antiphon.Tests/TestHelpers/TranscriptHotPathFixture.cs` for shared scale setup.

Options: (1) raw stored sequence/timestamp cutoff is unsafe (D-4); (2) a durable generation/stream ingestion cursor could avoid even reassembling history, but requires contiguous successful commits, stream identity in the ingest path, safe partial-failure rollback, and migration/wire coverage; (3) a bounded positive identity window is the smallest safe DB improvement. Choose (3) now. Option (2) is a later extension, not permission to change `TranscriptSinceRead`'s complete-snapshot contract.

Attach the window to a `SessionStateStore.Entry`, accessible only under its existing lease. Cache sanitized `(UUID, kind)` identities proved present by a completed DB probe or successful row/stub commit. Preserve original-source pairing for UI/actions when filtering entries. Unknown keys still use the current 512-UUID chunks; null-UUID entries always use the existing sequence probe. Never infer all kinds of a UUID from one cached kind.

Keep the one session/max/null-sequence prelude even on a fully cached replay. Compare its actual `StartedAt` to the window's accepted generation; on mismatch discard the window and repeat full identity verification before admitting any filtered batch. Bind the window to the entry/server epoch and reset epoch. Clear it on reconciliation/ambiguous outcomes, accepted-generation change, mutation/deletion, eviction, and disabled/unadmitted cache operation. Restart starts empty and safely re-probes. A row skipped after both row and stub failures must remain eligible on replay. Do not publish positive evidence before commit; partial fallback adds only confirmed durable rows. Recovery/receipt transaction fences still run on duplicate batches.

Bound storage: at most 4,096 identities and 512 KiB estimated key storage per admitted session, with a 16 MiB store-wide ceiling; measure key UTF-16 bytes plus a documented fixed entry overhead. Eviction only removes optimization entries, so overflow falls back to DB. Do not cache transcript text or an unbounded history. Report attempted/avoided identity counts and window bytes without UUID/text labels. Use existing timing labels; adding a label selects its existing contract tests.

Preserve `C1403_persist_prelude_is_one_command`'s one-command duplicate prelude, and the C698 1,025-key boundedness witness. Update their expected identity **contents** for hot replay; retain a cold/restarted instance arm proving full chunked dedup still works. Do not weaken counts to “some queries.” The existing hot-path fixture creates its large dataset through each `CreateAsync(large: true)` call: share that dataset/template once per run and clone for each case. The dropped-index positive control must alter only its clone; measure shared seed and clone costs separately.

### S5 — aggregates and bounded transcript traversal

Files: `PolicyRefreshService.cs`, `AgentTaskPipelineStatusService.cs`, `TranscriptPromptSpan.cs`, `ChannelReplyDispatcher.cs` under `server/Application/Services`; new `tests/Antiphon.Tests/Application/TranscriptReadBudgetTests.cs`; existing prompt-span, turn-selector, policy refresh, channel reply and pipeline status tests.

1. `PolicyRefreshService.HasBeenIdleLongEnoughAsync` is the exact `(Timestamp, CreatedAt)` whole-session read. Replace its list/max with nullable `MaxAsync(t => (DateTime?)(t.Timestamp ?? t.CreatedAt))`. Null result keeps the empty-transcript answer false. This returns one scalar instead of ~74,491 rows.
2. `AgentTaskPipelineStatusService` has the related multi-session timestamp read. Group in SQL by session and compute nullable `MAX(COALESCE(Timestamp,CreatedAt))`; compare that max to each session's minimum dispatch time in memory. This is equivalent to filtering timestamps after dispatch and taking max, including no-row/default fallback. Preserve the API DTO and activity fallback.
3. `TranscriptPromptSpan.LoadAsync` exactly matches the four-column prompt query. Traverse `(session, kind)` with `Sequence > after AND Sequence <= through ORDER BY Sequence LIMIT 256`. Capture `through = MAX(Sequence)` once; keep pages in one repeatable-read snapshot for the complete multi-page operation, finishing before external effects. No per-page transaction or OFFSET. If a caller already owns a transaction, do not nest or silently change its isolation: retain the existing single-statement load in that arm, and test it explicitly. Assemble the existing complete `RawPrompts`, compute the command-name set over the complete in-window rows, then apply the existing housekeeping/provider rules. Bound pages/commands, not semantic history. For boolean `HasTurnPromptAfterAsync`/`HasTurnPromptSinceAsync`, fetch the wrapper-name context separately and page only candidates above the sequence/time bound, retaining null timestamps; short-circuit only after a non-housekeeping candidate is proved. An older wrapper can classify a later raw echo, so never inspect a page in isolation.
4. `ChannelReplyDispatcher.ClassifyTtlLossAsync` can traverse full prompt candidates in the same 256-row keyset order, stopping at the first **correlated** match (not the first text match). Keep the terminal turn's ownership checks. The report's unqualified full-entity query is not uniquely attributable: retain captured SQL/queryid evidence identifying this caller, or explicitly report that the unidentified read remains outstanding. Do not change checked recovery snapshot reads merely because they also return entities.
5. `AgentSessionService.GetTranscriptAsync` already accepts a stored `since` cursor but returns an unbounded suffix. Do not silently cap its current response: old clients can mistake a partial response for completeness. An optional page-size/has-more API plus client drain loop is an alternative requiring a separate DTO/route and frontend verification slice. This plan optimizes the identified internal reads without changing that contract.

Paging alone reduces peak statement rows, not necessarily total SQL work or memory in callers that require a complete result. Measure aggregate CPU/statement time; do not claim full history became constant cost. If full prompt assembly remains in the top 20, profile its callers and design a revision-bound span cache or a bounded settlement algorithm with its evidence semantics before claiming CARD-1888 complete.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards) and [test performance standards](../../test-performance-standards.md). This Plan runs no builds/tests: it changes Markdown only. Code has the closed list below, grouped by committed slice; never commission the entire repository suite for this work.

Historical costs in `scripts/lib/nightly-class-costs.json` are Windows **serial test-seconds, not p95 or row wall time**: SessionStateProjection 49s/12 results, Warmup 76s/16, Commit 33s/8, CacheRead 20s/2, RuntimeAction 103s/28, Recovery 45s/17, Routing 115s/35, Deferral 62s/12, TemporaryBlock 9s/4, AgentSessionRuntime 108s/47, TranscriptHotPathQuery 141s/4. Provenance: investigation `658b8049233b1d9b75ef2b2fdeda70fd183e8426`, runs `79b742de` at rc `f84228f285dd` or `8f055f87` at `105792f7d22f`; load context is incomplete. The complete pipeline-status class is 730s/69; use its exact affected method instead. Do not relabel historical counts as the current census.

No comparable measured p95 for these proposed selections is available in this checkout. CP estimates and the following commissioning budgets are provisional, tracked by CARD-1886/1887/1888 with CARD-1404/1514 timing follow-up. Capture all per-case durations on the first authorized execution; use retained comparable runs to obtain p95 with sample count, SHA, OS, host/load, and derive subsequent test/row timeouts near **2 × measured p95**. One run is a bounded provisional sample, not a stable percentile. Do not add repeat full-suite runs solely to manufacture a sample.

| Phase | Provisional budget and ownership |
|---|---|
| Build | 60–120s per new checkpoint build; separate from test body and slot waits. One build at a time per worktree. |
| Container/readiness | Shared `TestDbFixture` once per assembly/run, 5–30s expected; 60s provisional bound, record actual. No product container. |
| Migration/seed | Shared template once per run; 5–20s expected, 40s provisional bound. No per-test migration. Owned EXPLAIN scale data built once per fixture/run, 5–30s expected. |
| Clone/isolation | Per-test template clone/transaction, 0.1–3s expected; 6s provisional bound. Existing fresh-start fixture tests may recreate their owned instance because startup is their subject; record it. |
| Body | Ordinary new assertions 0.1–5s; database-shape/scale cases 5–30s. Provisional individual cap 60s for a scale case; use event barriers and injected clocks, no timing sleeps as assertions. Existing measured longer cases retain their documented bounds pending evidence. |
| Teardown | 1–10s expected, 20s provisional bound; join every owned process and DB cleanup. |
| Row/run | CP estimates below include build. Run separately per `After`; expected 5–8m per main slice group, 2m final guards. Set provisional row deadline 10m, charged run deadline 20m per slice group; these are explicit commissioning exceptions pending measured p95, not importer 15m/3× defaults. |
| Admission | Slot wait separate, bounded at 10m for this commissioning; if unavailable, report not run/exit 4. No unleased retry. |

Do not raise existing fixture timeouts just to match this table. For newly introduced waits use the tighter owner bound or measured bound; record exceptions before running. Any actual or commissioned row/selection/run above 30 minutes requires a Debug task with timing evidence; the caller must commission it rather than repeat the long selection. Execute per-slice groups, not a monolithic run across all rows. If cold build/setup makes a group exceed its estimate, preserve that first receipt and investigate.

Evidence: ignored `.antiphon/checkpoints/<run-id>/`, per-case TRX and timing JSONL; performance SQL plans/stats under ignored `.antiphon/evidence/card-1886/`. Record DB/HTTP/CLI/filesystem spans separately, unknown phases as unknown, and remainder separately. Enable the repository's existing test timing instrumentation. Retain exact group membership and direct/transitive ProjectReference graph. Categories are server/database integration and existing Unit guards; server product dependency changes are covered through `Antiphon.Tests`. Messaging, runner-native, and PTY projects are not changed. This is ordinary scoped verification, not rc release qualification credit.

## Post-run measurement and acceptance

The following is a **later desktop measurement lane**, not a Plan action or a load test while `97150a0b` is live. Confirm the run ended directly, record active rc/server SHA, resolved process owners, database version, session counts and action/transcript rates. No blanket stop/restart, volume edit, statistics reset, or action on the live qualification is authorized by this Plan.

1. Before each activated slice collect a ten-minute baseline; after activation/warmup collect a matched ten-minute window. Use 5–10s `docker stats --no-stream` CPU samples for the observed application Postgres container and process `TotalProcessorTime` deltas for server/runner. Record start/end wall-clock timestamps, process IDs/start times, and all competing build/test load. Container CPU percent /100 is cores; process CPU seconds /elapsed seconds is cores. Incomparable workloads are inconclusive, not a failed performance fix.
2. Snapshot `pg_stat_statements_info` (including reset/dealloc) and current database's pg_stat_statements at both boundaries. Retain `(dbid, userid, queryid, toplevel)`, calls, rows, total_exec_time, total_plan_time, shared hit/read blocks, temp blocks, and **all available** `jit_*` counters/times. Join by that full identity; query rewrites get new queryids, so group known old/new normalized shapes under the same operation. Exclude/reset or evicted intervals explicitly, never treat missing counters as zero. See [Postgres 16 pg_stat_statements](https://www.postgresql.org/docs/16/pgstatstatements.html).
3. Compute delta mean = delta exec ms / delta calls, rows/call, blocks/call, operation share = operation delta exec / all app delta exec, and statement-time rate = total delta exec ms /(1000 × actual elapsed seconds). Sum JIT generation/inlining/optimization/emission times once; do not add them to execution totals or confuse counts with milliseconds. Keep timing instrumentation/accounting caveats visible.
4. Obtain the EXPLAIN comparisons specified in S2/S3 on owned fixtures first. After the live run, use bounded read-only desktop EXPLAIN transactions only for the exact SELECTs under investigation; set a local statement deadline, roll back, and do not explain a mutating worker/drain operation. Save actual query shape and parameter cardinalities without transcript contents or connection strings.

| Gate | Target at matched load; these are acceptance targets, not achieved measurements |
|---|---|
| S1 JIT | Application `SHOW jit` off on fresh and reused connections; target operations' delta `jit_functions = 0` and JIT time increments 0; no JIT block in their application-policy plans. |
| S1/S2 claim | Mean <5ms, sampled p95 <10ms on the representative three-row/transcript-only workload; >=95% execution-time reduction versus its matched JIT-on baseline. Claim share falls from historical ~51% to <5% of app statement time. Debt-heavy correctness/performance separately recorded. |
| S1 host effect | Target >=0.4 core lower ten-minute Postgres mean at comparable ~3 claim calls/s; historical 0.4–0.56 is an estimate to validate, not a guaranteed CPU subtraction. Other top statements may not regress >10% at matched workload without explanation. |
| S3 seed/pins | Mean <20ms and >100× fewer shared blocks/call for the observed batch distribution; record 512-session/large-transcript seed separately because exact count still scans its range. Pin membership identical in both queries. No blanket <20ms promise for an arbitrary 512 × 75k-row seed. |
| S4 identity | Warm replay plus <=10 new identities: identity rows/call <10, >=95% fewer UUID keys probed versus baseline, and full replay no UUID probe branch after warmup. Cold/restart/overflow counts separately retained. No lost/duplicate durable rows/actions. |
| S5 reads | Timestamp aggregate returns <=1 row/session; new keyset history commands <=256 rows/page; explicitly report the existing-transaction compatibility arm. Named unbounded history statements disappear from matched delta top 20; account for replacement pages as one operation so fragmentation cannot fake success. |
| Combined DB | Target >=50% lower app statement-time rate after S1, >=65% after S1–S5 at matched load, with workload-normalized per-operation numbers beside the share. Overlapping JIT/pins/ingest savings are not added twice. |
| Server and runner | Server CPU must not regress >10% from the new post-run baseline; aspiration <=0.80 server cores if baseline remains ~0.95. Runner remains near its measured baseline, with >0.02-core growth investigated. A server-core saving is not required to prove a PostgreSQL-only JIT reduction. |

Run the attribution report's trace commands after `97150a0b` ends, using freshly resolved owned PIDs, not historical PIDs. Server first, runner second: `dotnet-trace collect -p <pid> --profile dotnet-sampled-thread-time --duration 00:05:00 -o <owned-output>.nettrace`; use `cpu-sampling` only if the installed tool rejects the profile. Then separate five-minute `dotnet-counters collect` runs: server `System.Runtime,Microsoft.AspNetCore.Hosting,Microsoft-AspNetCore-Server-Kestrel,Npgsql`; runner `System.Runtime`; refresh interval 5s, CSV. Convert server trace to speedscope. Preserve before/after stats and counters; do not include tracing overhead in the acceptance CPU windows. Report inclusive and exclusive frames for runtime actions, seed/materialization, transcript ingestion, Serilog, serialization/Npgsql, and HostStats polling. CARD-1890's CPU claim and any further server method optimization wait for this evidence; S1's database fix does not.

## Verification design

Platform reads completed at 2026-11-13 ~14:07Z: `GET /api/runner-defaults` revision 2 resolved an available Linux default; `GET /api/session-runners` also showed an available Windows runner and a retired entry. Re-read at dispatch. Checkpoints use the **portable server/Postgres lane** on the current eligible default: omit `-Runner` and `-Platform`. Never encode a fleet hostname/location into checkpoint commands. The deferred desktop process-measurement lane specifically needs the observed Windows host; pin only that separate observation if commissioned.

Existing tests affected are mandatory. The [CARD-1782 impacted-classes table](../../../tests/impacted-classes.json) is the authority: run `scripts/impacted-classes.ps1` over each actual diff before qualification. For planned paths, `test-lane-category` applies and is covered by CP-10; migrations/public DTO-routes/timing-labels/remote-scripts/dispatch statement-budget kinds are **none**. Preserve existing SQL operation tags; S4 does affect C1403 and C698 statement budgets even though the table's dispatch-only glob does not select them. CP-4 names them. If scope expands into a migration, public DTO/route, operation-label change, or remote script, amend this plan with the complete selected classes/methods before that build. Do not mark those categories none after changing their paths.

| Witness | Concrete oracle and negative control | Selection |
|---|---|---|
| V-1 S1 | Real PostgreSQL returns `jit=off` for fresh/reopened pooled application connections, factory override and clone; preserves other startup settings. Seed a high-estimate SELECT and show zero JIT functions in its JSON plan. Removing the helper call makes the setting/plan assertion fail. | New `PostgresJitPolicyTests`; existing fixture lifecycle/isolation tests. |
| V-2 S2 | Compare exact selected IDs/drain order against an independent small in-memory truth table for all cases in S2. Assert no queued-message work on transcript-only plan. Removing carried or late-key overlap must fail recipient/order assertions. | New `RuntimeActionClaimQueryTests`; existing runtime action/recovery/routing/deferral/temporary-block classes (C1434/C1826 included). |
| V-3 S3 | Compare seed.Pinned and LoadPinnedIds against an independently enumerated fixture set spanning every task/session status, parent-only/child-only/both, null IDs, pending/sent/settled queue, notification/intent terminal states, missing sessions. Assert one SQL command per load and no task subplan repeated per session. Removing parent or queue membership fails. | New `SessionStatePinQueryTests`; existing projection/warmup/cache-read classes. |
| V-4 S4 | Replay 602 identities with 3 new pairs, same UUID/different kind, sanitized UUID, tailer reset, null UUID, concurrent lease writers, cancellation before/after commit, fallback/stub failure, retention reset, generation change, restart and memory-pressure eviction. Count queried keys/returned identities, durable entries, actions and monotonic stored sequence. Deliberately cache a failed row or skip by stored max: missing-row assertion must fail. | New `TranscriptIdentityWindowTests`; all `AgentSessionRuntimeTests` partials (C698/C1403); `SessionStateCommitTests`; `SessionStateWarmupTests`; `TranscriptSinceClientTests`. |
| V-5 S5 | Compare aggregate answer with independent in-memory expected max for empty/null/out-of-order timestamps and shared session dispatch times. Compare complete paged prompt outputs/order with fixtures crossing 255/256/257 and >1,500 rows; wrapper on a different page, raw echo before wrapper, pre-dispatch delimiters, null timestamp, allowlisted provider notes, concurrent append/delete. Assert every DB page <=256, fixed snapshot upper bound and unchanged caller verdict. Remove one page/context wrapper: assertions fail. | New `TranscriptReadBudgetTests`; existing prompt-span/turn-selector/policy-refresh/channel-late-confirm classes; exact pipeline last-activity method. |
| R-1 | Standing guard batch, actual diff impact census, no unsafe OR shapes/suffix collisions, native default build property consistency, source/evidence/merge-tree checks. | CP-10 and read-only Git/script checks. |

Use real shared test Postgres for SQL translation/plans and durable behavior. Inject clock/barriers and external clients; no production runner or provider starts. New test classes must carry the existing Unit/Integration classification and appropriate shared DB fixture. Every new regression needs an assertion that goes red when its protected behavior is removed. Commit and push regression tests before fixes; each long run verifies a named committed SHA. SourceLanding positive controls remain a separate later stage, method-scoped on each exact method, not a class/suite mutation run.

Before execution compare each suffix pattern against declarations in `tests/`; existing class operands below have no additional differently named suffix matches at this baseline (partial files are the same class). New proposed class names must be rechecked after addition. CP-8 is a single exact method and valid without `*`. Inspect fresh TRX for every intended class/method and nonzero counts; `--list-tests` is not execution evidence. `Min` is a conservative execution floor, not a claim about expanded argument counts.

Commit each slice and push. Invoke the checkpoint tool once per committed `After` group, selecting `--rows` when needed, with `--expected-source-sha <full-sha>`, `--serial`, provisional `--row-timeout 10m --total-timeout 20m --total-timeout-override-reason 'CARD-1886/1887/1888 provisional bounded commissioning; collect p95'` and bounded admission. For the 10m admission bound, import the plan to an ignored manifest and set only `build.slotWaitMinutes: 10`; run it as `run <manifest.yaml>` with the same row selections and source binding. Bootstrap the tool through `scripts/build-slot.ps1` if no built tool exists; report that bootstrap separately. Wait on exit 75 until terminal; never end a delegate turn with an owned run in flight. The checkpoint executor takes build slots itself. Direct builds, if a justified extra is needed, use the host gate and native default output; no `-o`/`OutputPath`. Keep source frozen during runs. A red checkpoint is not green because its failure appears inherited: reproduce the exact failing case at base, preserve both receipts, then report the real outcome.

S2 Code amendment (f56921e1): execute CP-2 and CP-10 only for the commissioned S2 slice. CP-10 includes DerivedImpactIndexTests per the brief. CP-5/CP-8 estimates are rounded up to integer minutes because the importer refuses fractional estimates; those S4/S5 rows remain out of this slice. An initial committed CP-2 run retains the legacy query to prove the plan-budget test red before the SQL replacement. No migrations or OS-selected production behavior change. Initial red receipt 20261113-201257-24fb retains bin-c1886-s2/ by design; the corrected CP-2 run uses fresh bin-c1886-s2b/ with the same row/filter, preserving red artifacts. The actual diff's only matching impacted-classes policy row is `test-lane-category`, covered by CP-10. Derived reverse-edge candidates extend well beyond this query; the named CP-2 classes bound this slice's unchanged execution/retry/delivery behavior. No full assembly or Unit lane run is commissioned.

S2 pending SourceLanding Mutation inventory (all pending; Code's legacy red is ordinary regression evidence):

| PC | Exact test method on RuntimeActionClaimQueryTests | Compiling defect variants |
|---|---|---|
| PC-S2-1 | Selection_and_drain_match_legacy_and_independent_destination_oracle | Remove carried keys from destinations; remove the earlier-null wildcard; remove the later-null wildcard. |
| PC-S2-2 | Selection_and_drain_match_legacy_and_independent_destination_oracle | Remove late keys; remove the queued-message session restriction; include non-channel rows in the blocker set. |
| PC-S2-3 | Selection_and_drain_match_legacy_and_independent_destination_oracle | Add a global due-time filter; reverse phase priority; reverse sequence priority; reverse trigger-kind priority. |
| PC-S2-4 | Small_and_debt_heavy_plans_bound_cost_and_seek_queued_messages | Restore the legacy correlated selector (cost assertion); replace the keyed queued-message join with a compiling table-wide scan (scan assertion). |

Unique `(session, sequence, kind)` storage prevents two `channel` rows at the same sequence. The ties fixture therefore uses channel/transcript/status/catchup ties, preserving the existing strict-earlier rule without manufacturing invalid rows. No numeric PC budget from the original plan is superseded; Mutation owns method-scoped execution, missing-control discovery and any newly discovered variant.

### Checkpoints

Every row below is portable server/Postgres lane; ordinary scope is exactly these rows. Reuse cells refer only to an earlier build with the same `After`. Grouped run S5 includes CP-6–CP-9. CP-10 is the final integrated source guard run. EstimatedMinutes are provisional under the budget above.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1886-s1/ | jit-policy | `/*/*/(PostgresJitPolicyTests*)\|(TestDbFixtureLifecycleTests*)\|(TestDbFixtureIsolationTests*)\|(TestDbFixtureLazyInitializationTests*)\|(TestDbFixtureSchemaCacheLifecycleTests*)/*` | V-1 | all listed, 0 failed | 3 | 5 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1886-s2b/ | action-claim | `/*/*/(RuntimeActionClaimQueryTests*)\|(SessionRuntimeActionTests*)\|(SessionRuntimeActionRecoveryTests*)\|(SessionRuntimeActionRoutingTests*)\|(SessionRuntimeActionDeferralTests*)\|(SessionRuntimeActionTemporaryBlockTests*)/*` | V-2 | all listed, 0 failed | 6 | 6 |
| CP-3 | S3 | tests/Antiphon.Tests -> bin-c1887-s3/ | pin-query | `/*/*/(SessionStatePinQueryTests*)\|(SessionStateProjectionTests*)\|(SessionStateWarmupTests*)\|(SessionStateCacheReadTests*)/*` | V-3 | all listed, 0 failed | 4 | 4 |
| CP-4 | S4 | tests/Antiphon.Tests -> bin-c1888-s4/ | ingest-identity | `/*/*/(TranscriptIdentityWindowTests*)\|(AgentSessionRuntimeTests*)\|(SessionStateCommitTests*)\|(SessionStateWarmupTests*)\|(TranscriptSinceClientTests*)/*` | V-4 | all listed, 0 failed | 5 | 5 |
| CP-5 | S4 | CP-4 | transcript-index-regression | `/*/*/TranscriptHotPathQueryTests/*` | V-4 | all listed, 0 failed | 4 | 3 |
| CP-6 | S5 | tests/Antiphon.Tests -> bin-c1888-s5/ | transcript-read-budget | `/*/*/(TranscriptReadBudgetTests*)\|(TranscriptPromptSpanTests*)\|(TaskReportTurnSelectorTests*)/*` | V-5 | all listed, 0 failed | 3 | 4 |
| CP-7 | S5 | CP-6 | refresh-and-reply | `/*/*/(PolicyRefreshServiceTests*)\|(ChannelReplyLateConfirmDispatchTests*)/*` | V-5 | all listed, 0 failed | 2 | 2 |
| CP-8 | S5 | CP-6 | pipeline-activity | `/*/*/AgentTaskPipelineStatusTests/last_activity_uses_transcript_after_dispatch_and_falls_back_otherwise` | V-5 | all listed, 0 failed | 1 | 1 |
| CP-9 | S5 | CP-6 | channel-ttl | `/*/*/ChannelReplyDurabilityTests/(Ttl_with_no_matching_prompt_is_stale_ttl*)\|(Ttl_with_a_matching_prompt_but_no_turn_end_is_turn_incomplete*)\|(Ttl_with_a_completed_answer_captures_before_recording_expired_preparation*)` | V-5 | all listed, 0 failed | 3 | 1 |
| CP-10 | all | tests/Antiphon.Tests -> bin-c1886-all/ | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)/*` | R-1 | all listed, 0 failed | 9 | 2 |

## Nightly pins, publication and handoff

S1–S5 as listed do **not** edit the protected nightly input roster in `scripts/test-live-lane-run.ps1`: `scripts/lib/nightly-common.ps1`, `nightly-policy.ps1`, `nightly-coverage.ps1`, `nightly-core.ps1`, `tests/test-execution-policy.json`, and `scripts/fixtures/nightly/c487-probe/discovery.diag`. Adding ordinary classified tests is not itself an edit to those six pinned inputs. Do not gratuitously repin historical evidence or change the nightly policy to make a failing check pass.

If implementation needs a policy/census change, it becomes a separately declared slice: recompute the policy hash (and selection catalog hash/pin if that catalog changed), run `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest`, update its `PinnedInputDigest` in the same deliberate change, and append exact `LiveLaneRunnerTests`, `NightlyCoreTests`, and `ReleaseGatePlatformTests` methods selected by CARD-1782 before execution. Source/runtime changes also invalidate any affected live-lane **receipt fingerprints**; requalifying those receipts is distinct from changing the six-file constant. Never re-pin run `97150a0b` or edit its inputs while it is live.

At Code completion run evidence-diff over base..HEAD, fetch master, and require conflict-free `git merge-tree --write-tree origin/master HEAD`; never merge/rebase the task branch. Report full pushed SHAs and each checkpoint's counts/receipt. Product acceptance remains pending until the post-run desktop windows and active rc SHA prove it. A pushed code change alone is not activation.

--- next stage ---
next: code
handoff: Implement S1 first, then S2–S5 under the embedded verification design and closed checkpoints. Preserve replay and channel ordering semantics. Keep desktop run 97150a0b untouched; defer activation, CPU windows and traces until it ends. Add migrations or API paging only through a verification-complete plan amendment.
artifact: docs/superpowers/plans/2026-11-13-card-1886-postgres-jit-and-hot-queries-plan.md
