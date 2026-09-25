# CARD-0698: remove transcript read amplification

Date: 2026-09-25. Plan task: `e1ea172f`. Next: **Code**, with verification design included.

Deliver the UUID index and indexed working-state query together as the first round. Keep the transcript as the source of truth. Do not introduce a cached idle verdict, change retention, or slow delivery/health polling. The expected benefit is removal of the two mechanisms responsible for about 89% of sampled active backend occupancy; this is a prioritization estimate, not a promised CPU percentage.

## Evidence and ground truth

Read [the investigation](../../investigations/2026-09-25-card-0698-antiphon-postgres-burns-5-6-cores-transcriptentries-polled-by-full-seq-scan-36x-s.md). Its raw evidence remains on `feat/card-task-ff5ae210`, commit `f6b54d8e548b2d77f26f9ab2dcc14242c90cec16`; it is deliberately excluded from this change. Source locations below were checked at plan base `4560118790bd5e9f0f2c7e215e4287322bf9dd1d`. CARD-0698 was read through `scripts/card.ps1 get CARD-0698 -Json`.

| Card assumption / proposed remedy | Ground truth | Consequence |
|---|---|---|
| Around 564% PostgreSQL CPU and 35.8 transcript scans/s | Later post-purge investigation measured 425.77% mean CPU, 21.16 scans/s and 2.657M sequential tuples/s over 180.881 seconds; transcript queries occupied 94.1% of sampled active backend time | Compare like windows after activation; purging history did not solve the mechanism |
| Grouped working-state query is the largest consumer and lacks a session predicate | UUID membership and boundary UUID checks occupy 71.1% of sampled active hits; working queries 18.1%. The grouped query already filters requested sessions | Address both; do not merely add the already-present predicate |
| Add `(AgentSessionId, Sequence)` | That unique index already exists and is heavily used; no UUID key exists | Preserve the sequence index; add the missing lookup key |
| A unique `(AgentSessionId, Uuid)` is a dedup constraint | `PersistTranscriptAsync` dedups `(Uuid, Kind)` within a session. One native line can yield several normalized kinds | A two-column unique constraint would lose valid rows |
| Last sequence or latest end row completely describes idle | End sequence and end timestamp are independent maxima; qualifying activity must pass both on the same activity row | Preserve the exact predicate, including nulls and non-monotonic timestamps |
| Excess connections / tests / vacuum caused this sample | 14–16 app connections of 100; tests used separate containers; no transcript vacuum during sample | No pool tuning, test isolation changes or vacuum work in this fix |
| Board and terminal-task polling are the same defect | Historical completion recovery is CARD-0699; repeated sibling-board lookup is CARD-0700 | Leave their implementations to those cards |

No code, live database writes, migrations, tests or builds are performed by this Plan stage. The standing request authorizes preparing the fix without another approval round.

## Decisions

### D-1: nonunique UUID covering index, bounded membership batches

Add `IX_TranscriptEntries_AgentSessionId_Uuid` on `(AgentSessionId, Uuid)`, `INCLUDE (Kind)`, filtered by `Uuid IS NOT NULL`. It is **nonunique**. It supports both the membership projection at `AgentSessionRuntime.cs:850` and the UUID/kind existence check at `:517`. Keep `(AgentSessionId, Sequence)` and `IX_TranscriptEntries_IsApiError` intact.

Keep current sanitization, `(session, UUID, kind)` replay handling, null-UUID sequence dedup, sequence rebasing, batch fallback/stubs and boundary side effects. Partition distinct incoming UUIDs into **512-key batches**, sequentially, and union the existing `(Uuid, Kind)` results before deciding which events to insert. Skip UUID reads for an empty key set. This bounds the index lookup workload even when a runner snapshot returns an entire large history; it does not change incoming event order or dedup identity. Do not parallelize queries on one DbContext. Do not add a new max-sequence cache: the existing simple maximum was cheap in the sample.

Rejected: unique `(session, UUID)` is incorrect; unique `(session, UUID, Kind)` would add a new concurrency/failure contract and require a historical duplicate audit. Neither is required to remove read cost. A nonunique three-key index is viable, but two keys plus included `Kind` serve the measured projection and avoid making uniqueness look implied. A UUID-only index loses session locality. Including transcript text would make an unnecessarily wide index.

### D-2: replace the two GROUP BY queries with one batch of indexed probes

Add two **partial, nonunique** indexes containing only current end records:

| Database name | Keys | Predicate |
|---|---|---|
| `IX_TranscriptEntries_End_AgentSessionId_Sequence` | `(AgentSessionId, Sequence)` | `End(t)` below |
| `IX_TranscriptEntries_End_AgentSessionId_Timestamp` | `(AgentSessionId, Timestamp)` | `End(t) AND Timestamp IS NOT NULL` |

Ascending B-trees support the descending top-one reads. Give the end/sequence index its own EF model index name as well as database name: an unnamed `HasIndex` on the same property list must not replace the existing unique sequence index.

`End(t)` is precisely the predicate currently in `SessionMessageQueueService.IsWorkingBatchAsync`:

```sql
t."Kind" IN ('TurnEnd', 'SessionRestartBoundary')
OR (t."Kind" = 'CompactBoundary' AND t."Text" IS NOT NULL
    AND strpos(t."Text", '(manual)') > 0)
OR (t."Kind" = 'UserPrompt' AND t."Text" IS NOT NULL
    AND t."Text" LIKE '[Request interrupted%')
```

The same literal predicate must appear in the index filter and the production query. Parenthesize the entire OR expression before appending the timestamp condition. Migration files retain their historical literal definitions. SQL construction belongs under `server/Infrastructure/Data/`, in a small concrete `TranscriptWorkingStateQuery` helper taking the existing DbContext, requested IDs and cancellation token; do not add a repository interface, new DI graph or Domain infrastructure dependency. `IsWorkingAsync` keeps its existing wrapper; `IsWorkingBatchAsync` delegates its database read to the helper and preserves its dictionary contract.

Use parameterized PostgreSQL SQL with a `uuid[]` requested-session parameter, `unnest` as the driver, two `LEFT JOIN LATERAL` top-one boundary probes and a `CASE` choosing between two activity `EXISTS` probes. No interpolated IDs, no dynamic identifier from input, and no new session-status filter. Read the batch in **one database statement**, so the two maxima and activity share a statement snapshot:

```text
for each requested session s, within that one statement:
  endSeq = highest Sequence among End rows for s, or null
  endTs  = highest non-null Timestamp among End rows for s, or null
  if endSeq is null:
    working = EXISTS activity row for s
  else:
    working = EXISTS activity row for s with Sequence > endSeq
              AND (row.Timestamp IS NULL OR endTs IS NULL OR row.Timestamp >= endTs)
```

The two boundary probes use their respective partial indexes with `ORDER BY ... DESC LIMIT 1`. The timestamp probe explicitly excludes nulls. The after-end activity probe seeks using the **existing** `(AgentSessionId, Sequence)` index; `CASE` keeps the no-end path separate from its range condition. Do not fetch transcripts into application memory or loop through one SQL command per session. An empty input returns an empty dictionary without SQL. Existing callers supply distinct IDs; retain the existing duplicate-input behavior rather than silently widening the contract.

Activity excludes exactly `TurnEnd`, `TurnTitle`, `SessionRestartBoundary`, `QueuedUserPrompt`, `QueueEnqueue`, `QueueDequeue`, `QueueRemove`, and `CompactBoundary`, plus `UserPrompt` rows with non-null text starting with `<command-name>`, `<local-command-stdout>`, `This session is being continued from a previous conversation`, or `[Request interrupted`. Null text stays eligible on otherwise eligible kinds. Preserve the server query's current case-sensitive prefix semantics, without introducing the `TrimStart` used by some in-memory helpers. Unknown kinds remain activity, as today. Do not infer idle from process status or missing timestamps.

This removes historical aggregation instead of just running it less frequently. It uses two narrow maintained indexes, not application-maintained summary state. Activity work still scales with a session's post-end tail; a no-end session with only housekeeping can require examining that session's history. That residual cost is explicit and tested. Do not promise constant work for every possible transcript shape.

Partial-index use depends on the planner recognizing the predicate; parameters for session IDs are fine, but parameterizing the boundary kinds/markers can prevent implication under a generic plan. Keep these trusted classification literals fixed and test both plan modes. [PostgreSQL partial-index documentation](https://www.postgresql.org/docs/16/indexes-partial.html).

### D-3: defer a per-session working-state projection

A lone `LastSequence` or `IsWorking` column cannot preserve the current rule. A future projection needs at least a version, end-sequence maximum, independent end-timestamp maximum, qualifying post-end activity state and a persistence watermark. A late end with an older sequence but newer timestamp can invalidate previously qualifying activity. A scalar latest activity sequence/timestamp pair also fails the row-correlation rule.

Correct maintenance must cover `AgentSessionRuntime.PersistTranscriptAsync` and its per-row fallback, direct restart-boundary insertion at `AgentSessionService.cs:2958`, session deletion/cascades, and `DataRetentionService.PruneTranscriptsAsync`. It needs transactional updates with transcript writes, concurrency control across ingest paths, startup backfill, repair/versioning, and recomputation on deletion. A process-local dictionary updated only by runtime ingestion misses these writers and restart recovery. That is substantially more risk than the three indexes and read rewrite, so it is **not Round 1**.

If post-activation evidence still shows costly long tails, commission a separate projection slice with an independent equivalence/backfill/race plan. Do not carry a silent summary fallback or dual-write experiment into this patch.

### D-4: keep polling and correctness gates fresh

No timer changes or TTL cache in Round 1. A cached idle result can cause input during work, policy restart during a turn, or unsafe maintenance input. Caching display-only agent/attention projections could be considered later, with explicit staleness and invalidation; it must not share a cached result with delivery or lifecycle gates. Cursor-based runner catch-up is a later protocol optimization requiring generation/fork/replay coverage, not a substitute for indexing persisted UUID lookups.

Rejected: global `enable_seqscan=off`, larger connection pools, `VACUUM FULL`, another retention cut, broad `Kind` indexes, and enabling `pg_stat_statements` as a prerequisite. None is needed for this fix. The investigation did not identify a timer running faster than configured.

### D-5: additive concurrent migration; old binary remains compatible

Generate **one** migration, `AddTranscriptHotPathIndexes`, using the repository-local EF CLI after model changes. Configure all three indexes with Npgsql `IsCreatedConcurrently()` and the UUID index with `IncludeProperties`. Inspect the generated SQL and transaction suppression with the pinned provider; do not assume its behavior from the model alone. [Npgsql index configuration](https://www.npgsql.org/efcore/modeling/indexes.html).

Build the UUID index first, then the two end indexes, serially. At the measured 377,647 rows / 247.93 MiB total relation size, these are bounded index additions with no table rewrite or transcript backfill. Expect seconds to minutes, **not a measured duration**; rehearse on a representative isolated fixture and record elapsed time/index bytes. CPU and I/O temporarily rise. The live size includes TOAST and existing indexes, so it is not an exact heap-read budget.

`CONCURRENTLY` permits ordinary writes but performs two table scans, can wait for older transactions, cannot run inside a transaction block, and can leave an invalid index after failure. Only one concurrent build per table may run at a time. Do not wrap the migration SQL in `BEGIN`/`psql -1`. [PostgreSQL 16 CREATE INDEX](https://www.postgresql.org/docs/16/sql-createindex.html).

`Program.cs:831` automatically migrates before serving, so a migration timeout can delay API availability. Prefer applying the reviewed index-only migration while the old API is still serving, from the deployment host's approved configuration path. Do not put connection strings on a command line, read another user's credentials, or launch another Program to apply it. Scope the migration command timeout to **300 seconds per index**; a short metadata-lock timeout (5 seconds) is a retryable refusal after inspection, not permission to kill blockers. Use one migration owner. Verify the generated script's transaction boundaries before using it; do not use an idempotent wrapper that embeds concurrent DDL in a transaction or procedural block.

After partial failure, inspect the migration history plus `pg_index.indisvalid`, `indisready` and `pg_get_indexdef`. No blind rerun or `IF NOT EXISTS` that mistakes an invalid/wrong index for success. Recover only these new indexes: if this migration is unrecorded, drop its already-created indexes concurrently after checking exact definitions and ownership, then rerun the reviewed migration. Never stamp migration history manually. If startup has already applied it successfully, do not replay it.

Rollback the binary first if necessary; leave valid additive indexes in place. No data rollback is needed. Test a generated Down/Up round trip in isolation, but do not run Down on the live database merely to roll back code. Inspect whether the generated Down needs an online operational equivalent before any later index removal.

## Call-site and cadence inventory

**Rate notation:** `D` = dispatcher default 5 seconds, minimum 1 (`DelegationSettings`, `AgentTaskDispatcherHostedService`); `H` = health/stranded-queue timer, defaults 60 seconds, minimum 10 of enabled RC watches; `S` = supervisor default 10 seconds. These are source defaults/guards, **not verified live overrides**. Event/request rates and eligible row counts are unmeasured. No per-caller SQL trace exists; do not divide sampled query counts among callers or equate scan counters with calls.

Measured UUID membership is approximately **47.01 observed calls/minute**, boundary UUID existence **4.67/minute**, working-after-end **16.67/minute**, working-without-end **12.67/minute**. Each is a sampled lower bound, not the actual execution rate. A nonempty working-state call currently sends **two** SQL commands; after this change it sends one.

### UUID ingestion and full-history replay

All service paths here are under `server/Application/Services/` unless qualified.

| Issuer / route | Current source cadence / multiplier |
|---|---|
| `AgentSessionRuntime.ObserveTranscriptAsync:374 -> PersistTranscriptAsync:825/:850` | Once per received transcript event; event rate unmeasured |
| `AgentSessionRuntime.IsUnseenTurnBoundaryAsync:517` | Eligible boundary events; UUID EXISTS, followed when applicable by separate ApiCallId EXISTS at `:527` (the latter is not fixed by a UUID index) |
| `AgentSessionRuntime.CatchUpTranscriptAsync:700/:705`, `SyncTranscriptAsync:721/:727` | One membership query for each nonempty full snapshot today; all-duplicate snapshots still do existence/max reads |
| `Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:128` | Startup/reconnect per live session; exceptional reconnect backoff default 1 second, normal EOF has no delay, but no EOF storm was measured |
| `AgentSessionService:1733 GetTranscriptAsync`; transcript panel initial load/reconnect | Per HTTP GET, even when the client asks for a `since` cursor; runner snapshot itself has no cursor; no unconditional transcript-panel poll |
| `AgentSessionService.TryLateConfirmBootPromptAsync:1327` | Conditional boot-prompt confirmation after a delivery attempt; launch-driven |
| `AgentTaskDispatcher:1807/:2402/:2690/:2759` | `D`, gated delivery/deadline/stall candidates; may re-pull before acting |
| `StandingSpecialistSeatService:42` | `S`, per candidate seat |
| `SpecialistRequestService:280`, `.Qualification:39` | Request reconciliation every 2 seconds; qualification authorization on demand |
| `SessionMessageQueueService:3432/:3538/:3585` | Delivery-confirmation/recovery attempts, conditional re-pulls; no standalone timer |
| `SessionMessageQueueService:4824` | Overlay recovery on demand |
| `Infrastructure/Supervision/SessionHealthActions:82`; `RemoteControlRecoveryService:347/:471` | `H` or explicit recovery; gated by screen/state evidence |
| `HerdrStatusCorroborationService:128` | Supervisor corroboration period default 60 seconds, suspicious candidates only |
| `ContextCompactionService:196`, `PolicyRefreshService:252` | Supervisor sub-sweeps every minute, eligible sessions only; policy refresh also explicit |
| `SubscriptionUsageMonitorService:177` | Default 30-minute monitor, per eligible session; local-command acceptance can involve additional queue checks |
| `ApiErrorRecoveryService:253/:516` | Default 60-second recovery sweep and recovery actions |
| `BootReplyWatchdogService:146` | `S`, conditional watchdog candidates |
| `GrokRulesRefreshService.RecoverSessionAsync:58`, `InitializeAsync:166` | Startup and 5-second recovery passes per eligible Grok session; initialization also pulls on each pending acceptance iteration with a 250 ms delay (at most about 4/s before I/O cost), until ready/failed/canceled |
| `AgentTaskLandNotificationService:197` | Notification reconciliation default 5 seconds, due recipients only |
| `AgentTaskReplyService:354` | In-turn reply/question handling, request-driven |

### Every direct working-state caller

The table enumerates the direct calls found with `rg -n 'IsWorking(Batch)?Async\(' server`; line numbers identify the base above. The wrapper at `SessionMessageQueueService:4593–4596` is the common query owner, not another poller.

| File, method and call-site lines | Current cadence / fan-out |
|---|---|
| `AgentService.GetAllAsync:100`; `IsSessionWorkingAsync:157` | Agent list and detail hooks each poll every 5 seconds (12/minute per active query), plus requests/invalidation; list batches running sessions |
| `AttentionService.BuildOpenTaskItemsAsync:917/:1000/:1058`, `BuildLandItemsAsync:1329`, `BuildAgentOutlivedTaskItemsAsync:2591` | Attention and summary hooks each 15 seconds (4/minute per active query), plus explicit requests; several per-task/per-note calls and one batch |
| `AgentTaskDispatcher.FailNeverStartedAsync:1846/:1940`, `SettleDeferredReportsAsync:3392`, `TryReuseWarmAgentAsync:5900`, `RetireIdleWarmAgentsAsync:6366` | `D`; gated per-task calls plus candidate/warm-session batches |
| `SessionMessageQueueService.EnqueueAsync:612` | Per enqueue |
| `SessionMessageQueueService.FlushStrandedQueuesAsync:1322` | `H`, eligible sessions |
| `SessionMessageQueueService.FlushSessionAsync:1362`, `FlushIfIdleAsync:1397` | Boundary/manual-compaction/delivery events and recovery calls; completion worker can call FlushIfIdle on its 1-second passes (CARD-0699) |
| `SessionMessageQueueService.HandleDeliveryFailureAsync:4199`, `BuildQueueDtoAsync:4718` | Per delivery failure / queue DTO construction and queue request; no independent timer |
| `SessionMessageQueueService.TryDismissOverlayAsync:4837`, `TryPollLocalCommandAsync:4940` | Recovery / local-command requests, gated; no independent timer in these methods |
| `AgentSessionService.WriteRestartBoundaryIfInterruptedAsync:2951` | Session relaunch, once per decision |
| `DelegateCheckProbe.GatherSessionAsync:337` | Check probe requests; scheduled check cadence defaults 5–60 minutes per task, dispatcher queues due work; no busy-loop in check worker |
| `TaskProgressPolicy.EvaluateAsync:79` | `D` and attention/expectation previews, sometimes twice around a fresh pull |
| `TaskDeadlinePolicy.EvaluateAsync:168` | `D`, attention and check probes, sometimes twice around a fresh pull |
| `CheckCompactionContinuationService.BuildScopeAsync:1018` | Dispatcher compaction reconciliation (`D`) and its revalidation steps |
| `SpecialistRequestService.AdvanceAsync:158`, `.Qualification.AuthorizeQualificationAsync:40`, `.Qualification.AdvanceQualificationAsync:133` | 2-second request reconciliation; authorization on demand; per candidate |
| `StandingSpecialistSeatService.ReconcileAsync:43` | `S`, per candidate seat |
| `ContextCompactionService.IsEligibleFromStoreAsync:260` | Minute compaction sub-sweep / explicit request |
| `PolicyRefreshService.EvaluateGatesAsync:500` | Minute policy sub-sweep / explicit refresh |
| `QueuedInputWatchdogService.EvaluateSessionAsync:127` | Minute queued-input sub-sweep, per candidate |
| `HerdrStatusCorroborationService.TryRaiseAsync:123/:129` | Default 60-second corroboration, two reads when a fresh pull is needed |
| `RemoteControlRecoveryService.ExecuteAutomaticArmUnderLockAsync:348`, `TryDismissIdleUnderLockAsync:472` | `H` / recovery actions; fresh read after pull |
| `Infrastructure/Supervision/SessionHealthActions.TryDismissRemoteControlMenuAsync:86` | `H` / explicit health action, menu-gated |
| `SubscriptionUsageMonitorService.PollSessionAsync:189` | Default 30-minute monitor, per eligible session |
| `ScheduleService.FireCoreAsync:334` | Due schedule fires; schedule-dependent |
| `AgentTaskService.GetAsync:2146` | Task-detail requests/invalidation; detail hook has no interval (the separate list/summary/pipeline hooks each use 15 seconds) |
| `AgentTaskReplyService.ReleaseDelegateAsync:2035`, `SessionLivenessAsync:3229`, `BlockUnmarkedWaitingAsync:3399` | Reply/settlement/release events, also reached by dispatcher deferred work |
| `DiagnosticsBundleService.BuildAsync:175` | Explicit diagnostics requests |

Other measured transcript queries remain identifiable: `LandNoteReceipt.Prompts:38` through `AgentTaskLandNotificationService:200` and compatible `CheckCompactionContinuationService:797` reads (combined observed 4/minute); queue confirmation at `SessionMessageQueueService:3495/:3641` (2/minute); `ContextCompactionService:236` full projection (1.33/minute); completion-note existence (9.34/minute, CARD-0699). These rates are not exclusive caller attribution. They are the first residual shapes to inspect if the live scan target is missed.

## Implementation rounds and slices

**Round 1, this Code dispatch, today:** S0–S3 and the complete checkpoint list. Three additive indexes, one working-state query, bounded UUID reads, focused tests. No client, runner protocol, retention or lifecycle changes. The UUID index is created first to deliver the dominant read benefit as early as possible. Code may report S1 as an emergency deployable subset if S2 encounters a genuine blocker, but that is partial completion; CARD-0698 remains open and does not claim the scan target.

| Slice | Files / work | Completion evidence |
|---|---|---|
| S0 | Add `tests/Antiphon.Tests/Application/TranscriptHotPathQueryTests.cs` with baseline-compatible capture/plan tests; isolated fixture under `TestHelpers` if needed | CP-1 expected assertion failures on current code |
| S1 | `server/Infrastructure/Data/AppDbContext.cs` UUID model index; `server/Application/Services/AgentSessionRuntime.cs` bounded/empty UUID lookup; `AgentSessionRuntimeTests.Persist.cs` | UUID plans, replay and chunk-boundary tests |
| S2 | End indexes in `AppDbContext.cs`; new `server/Infrastructure/Data/TranscriptWorkingStateQuery.cs`; replace query body in `SessionMessageQueueService.cs`; CLI-generated `server/Migrations/*AddTranscriptHotPathIndexes*` plus snapshot | One-statement indexed plan, exact working-state equivalence, actual migration upgrade |
| S3 | `TranscriptWorkingStateQueryTests.cs`, `TranscriptHotPathMigrationTests.cs`, complete performance fixture; register new Slow classes in `tests/Antiphon.Tests/slow-tests-allowlist.txt`; add operational result notes to this plan/investigation | CP-2–CP-4; deployment instructions and residual risks in Code report |

**Round 2, only if measurements require it:** quantify remaining ApiCallId checks, receipt/compaction scans and post-end tails before selecting another index, a cursor protocol, display cache or transactional summary. Track new structural defects on cards when found; do not absorb CARD-0699 or CARD-0700. No Round-2 implementation is authorized by this checkpoint manifest.

## Deployment and acceptance

Code implementation, checkpoint evidence and concrete migration/rollback notes are maintained in
[the S0-S3 verification record](../../investigations/2026-09-25-card-0698-code-verification.md).

The Code stage proves the query/migration in isolation; the caller's desktop deployment lane proves the live CPU result. Server2 Plan/Code must not restart the desktop stack from this worktree.

1. Complete scoped Code and separate Review, publish through the normal land path. Record the exact landed and intended deployment SHAs. From the canonical desktop checkout, verify its HEAD contains the land; after an out-of-band push update it using the runbook. Apply the reviewed additive migration as in D-5 or account explicitly for its startup cost.
2. Use the [canonical AppHost restart](../../apphost-runbook.md), from an independent operator shell with the documented Job Object caveat. Never use the worktree override as a convenience, restart the runner, or kill sessions for this change. Verify `/api/version` SHA and `land-v2` immediately, plus migration history and all three index definitions/validity; health alone does not prove activation.
3. Once migration/startup catch-up is finished, collect two comparable **180-second** windows with normal active sessions and UI. Retrieve the original collector from the investigation commit into an untracked evidence directory; its Python dependency belongs on the desktop collection host, not this Linux runner (which has no `python3`). Keep baseline and after files separate. Poll within the task; do not fire-and-forget collection. No statistics reset, transcript purge, extension install or live `EXPLAIN ANALYZE` is needed.
4. Record transcript `seq_scan`, `seq_tup_read`, `idx_scan`, index-level deltas, table row estimate, inserted/deleted rows, active session count, Docker CPU samples and migration duration/index sizes. Exclude migration, ANALYZE, retention passes and index-build scans from the steady-state comparison. Note concurrent CARD-0699/0700/retention deployments as confounders.
5. Acceptance target: **<= 0.1 transcript sequential scans/second** in each steady-state window, and at least **70% lower mean PostgreSQL CPU** than the comparable baseline (investigation baseline 425.77%, equivalent target about 128% Docker CPU). These are acceptance thresholds, not a pre-implementation guarantee. Also require a normal transcript-confirmed queued delivery and replay/catch-up without duplicate boundary notifications. Never send a fake user task to a live agent merely for a metric.
6. If the aggregate scan target misses, sample the remaining SQL and attribute it; UUID/working EXPLAIN success alone does not close the card. Report Code correctness separately from pending/failed live acceptance. Expected insert overhead is three maintained indexes, only one for most rows; no extra summary-table write. Report observed migration/write latency if that tradeoff is material.

## Verification design

Use PostgreSQL 16 Testcontainers through `TestDbFixture.CreateIsolatedSchemaAsync` (currently an isolated cloned **database**, despite its name). Never use port 17280 or production runner 17204. Keep row assertions session-scoped. Performance fixtures and DDL/scan counters use their own database and serialize within their class; they must not reset counters or drop indexes in the shared fixture. No real model, Windows pty or browser is necessary for this data-access change.

### Tests and red controls

| ID | Class / test design | Required assertion and red control |
|---|---|---|
| V-1 | `TranscriptHotPathQueryTests.Uuid_membership_seeks_by_session_and_uuid` | Capture the actual runtime membership command and parameters; on the large fixture require an index access with both key conditions, no transcript Seq Scan/Parallel Seq Scan, and no session-history scan followed by UUID filtering. Old schema lacks that access path, so CP-1 must fail this assertion |
| V-2 | `TranscriptHotPathQueryTests.Working_batch_uses_one_statement_and_indexed_boundaries` | Capture actual IsWorkingBatchAsync commands for 1 and 32 requested IDs: exactly one transcript SQL command per nonempty invocation, both boundary indexes used, no transcript sequential scan or grouped historical aggregate. Old code emits two commands, so CP-1 must fail; replay its plans for diagnostic comparison |
| V-3 | `TranscriptHotPathQueryTests.Prepared_plans_keep_the_partial_index_paths` | Explain the captured production commands with ordinary planning and a separately prepared generic-plan execution; session/UUID values remain parameters. No planner hints or disabled sequential scans. Detect accidentally parameterized boundary predicates |
| V-4 | `TranscriptHotPathQueryTests.Repeated_hot_reads_do_not_add_transcript_sequential_scans` | After seeding/analyzing, run 100 singleton UUID probes, a 1,025-key catch-up and 100 mixed working batches; fresh observer snapshots show zero transcript seq_scan delta. End setup transactions first; account for asynchronous stats publication with bounded polling/flush on the test connection. No global stats reset. Drop the new indexes only in this owned database to show the plan controls fail |
| V-5 | `TranscriptWorkingStateQueryTests` (12 nonparameterized tests below) | Exact persisted-row outcomes; compare with a test-only frozen original SQL/LINQ oracle and assert explicit expected dictionaries. An oracle comparison alone is insufficient. Each mutation described below must change an asserted result |
| V-6 | Three new `AgentSessionRuntimeTests.C698_*` methods | (1) same UUID/different kinds survives and same pair replays once across sessions/generations; (2) 1,025 keys including repeats straddling chunks preserve all distinct `(Uuid,Kind)` pairs, chunk cap <=512, monotonically rebased sequences and replay produces no new rows; (3) null-UUID batches retain sequence dedup and send no UUID membership query. Removing chunking/empty guard or keying by UUID alone is red |
| V-7 | `TranscriptHotPathMigrationTests` (4 methods) | (1) generated SQL/model preserves original unique sequence index and creates the three nonunique indexes concurrently with transaction suppression; (2) actual upgrade of populated pre-change schema keeps rows including repeated UUID/different kinds and null UUIDs; (3) Down/Up preserves data and old-query compatibility; (4) a second connection can insert while concurrent creation waits on a controlled existing writer. Nonconcurrent creation/unique UUID migration must fail |
| R-1 | Existing `SessionMessageQueueServiceTests` | Real queue outcomes cover interrupt/local commands, restart boundary, fresh work and row-correlated stale backfill; no refactoring of these tests to agree with the new implementation |
| R-2 | Existing `SessionMessageQueueDeliveryVerificationTests` | Manual compact flushes without spurious finished events; auto compact remains working; queue housekeeping and continuation records remain inert; transcript receipt rules stay unchanged |
| R-3 | Existing `AgentSessionRuntimeTests.C561_*` and `Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart` | Sanitization, stub/skip/failure recovery, replay and monotonic stored sequence remain unchanged; exclude its Windows `cmd.exe` restart test from the Linux filter |
| R-4 | Existing Unit lane including classification/model/migration contracts | Generated model and test registration regressions; use the documented Unit filter, not the full integration assembly |

V-5's 12 tests: empty input emits no SQL; missing/empty session reads idle; no-end activity reads working; no-end housekeeping reads idle; TurnEnd and restart boundary end work; interrupt prefix ends work but an in-text mention does not; manual compact/continuation reads idle; auto/unknown compact does not end work; local-command/queue records remain inert while raw slash-prefixed and unknown-kind work remain activity; stale replay above an end reads idle while a mixed qualifying row reads working; null timestamps and equal timestamp preserve conservative working; **max end sequence and max end timestamp from different rows** preserve the current rule. Include null text and leading-whitespace/case-negative prefixes in those tests. Query both singleton and mixed-session batch forms, with unrelated session rows, for every relevant case.

Specific positive controls for V-5: make auto compact an end; remove the continuation exclusion; use the timestamp from only the highest-sequence end; use independent activity maxima instead of an existential row; replace `>=` with `>`; drop null handling; remove session correlation. Each has a named scenario above that goes red. Execute mutations only in the later Mutation stage unless diagnosing a failed checkpoint; ordinary Code evidence is CP-1's baseline red plus the scoped green runs.

### Representative query-plan fixture

Seed approximately **377,000** synthetic transcript rows with `generate_series`/bulk insert in the owned test database: one 200k-row session, one 100k-row session, and the remainder spread over at least 128 sessions. Include realistic short/medium text, repeated UUID across normalized kinds, null UUIDs, many completed turns, null/non-monotonic end timestamps, a long stale post-end tail, a housekeeping-only session and sessions with no end. Analyze after bulk insert. No copied production transcript text.

Use a DbCommand interceptor to capture the **production** commands and typed parameters (including UUID and working-state paths); EXPLAIN those commands, not a hand-written easier query. `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` is allowed only in this disposable fixture. Parse the plan recursively; allow Index Scan, Index Only Scan and Bitmap Index/Heap access when bounded by the proper keys, including heap fetches on recently written pages. Reject Seq Scan/Parallel Seq Scan on TranscriptEntries specifically, not tiny requested-ID scans. Require top-one indexed boundary probes, no history-wide grouped aggregate, and a sequence range condition on the after-end activity branch. Do not require exact timing, cost estimates, buffer counts or one brittle whole-plan string.

Exercise UUID lists of 1, 512 and 1,025 (the last must split), hits/misses, single large session and 32-session working batches. Save plans/command counts and before/after counter observations under the checkpoint evidence directory. Measure timings as evidence, not assertions that fail on a busy host. A fixture/setup failure or zero executed tests is not red evidence.

### Execution and cost

Write S0 without referring to not-yet-generated migration types or new production helpers so it compiles against the baseline. CP-1 deliberately runs the two baseline-red methods; require **two expected assertion failures**, not compiler/fixture errors. Keep its evidence after implementing S1–S3. Add the migration tests after scaffolding. New heavy PostgreSQL test classes are Integration/Slow and registered; no fixture-only tests pretending to be Unit.

Generate the migration with `dotnet tool restore` then `dotnet ef migrations add AddTranscriptHotPathIndexes --project server`, per the owner. The server2 worktree has no local serving Program holding its outputs: the Windows stop-server advice does not authorize stopping the desktop. The EF-required design-time build is a **declared tooling exception** to the checkpoint build list (reason: CLI-only migration generation); use an isolated output arrangement supported by the existing EF tool, and report any such build separately. Never hand-author a migration/snapshot to avoid the CLI.

For TUnit rows use `scripts/run-checkpoint.ps1`, `-ResultsRoot .antiphon/card0698-checkpoints`, the exact filter below and its `-MinExecuted`; pass `-NoBuild` for reused output and comma-separated `-Expect` class tokens. Table backslashes escape Markdown pipes; pass bare `|` in the actual filter argument. Each rerun gets a fresh results directory. The script supplies `UseAppHost=false` on Linux. Inspect its fresh TRX roster/counts, and report each CP-n line with executed/passed/failed/skipped/reruns. CP-1's exit 1 is accepted **only** for its two designed assertion failures. All other rows must be green; no new/affected contract may be skipped. Report any unrelated Unit platform skips explicitly. An unlisted test/build needs its reason stated before running and in the report.

### Cost

Ordinary checkpoint floor: **40 minutes** (10 + 12 + 14 + 4). Authoring/scaffolding/evidence estimate: **60 minutes**; dispatch `-ExpectAbout` approximately **100 minutes**. These are planning budgets, not execution limits. Deployment plus two live 180-second windows is a separate approximately **15–25 minute** desktop obligation, including a 5-minute-per-index migration timeout budget if waits occur. This Plan stage ran zero tests/builds.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S0 (baseline production) | `tests/Antiphon.Tests -> bin-c698-red/` | baseline-red | `/*/*/TranscriptHotPathQueryTests/(Uuid_membership_seeks*)\|(Working_batch_uses*)` | V-1/V-2 red controls | exactly 2 executed, exactly 2 expected assertion failures, 0 skipped; no setup/build failure | 2 | 10 |
| CP-2 | S1–S3 | `tests/Antiphon.Tests -> bin-c698-green/` | unit | `/*/*/*/*[Category=Unit]` | R-4 | all selected Unit tests, >=1 executed, 0 failed; unrelated platform skips named, no affected contract skipped | 1 | 12 |
| CP-3 | S1–S3 | `CP-2` | transcript-integration | `/*/Antiphon.Tests.Application/(TranscriptHotPathQueryTests*)\|(TranscriptWorkingStateQueryTests*)\|(TranscriptHotPathMigrationTests*)\|(SessionMessageQueueServiceTests*)\|(SessionMessageQueueDeliveryVerificationTests*)/*` | V-1–V-5, V-7, R-1/R-2 | all five classes executed, all 4+12+4 new methods and existing selected methods, 0 failed/skipped | 20 | 14 |
| CP-4 | S1–S3 | `CP-2` | persist-regression | `/*/*/AgentSessionRuntimeTests/(C561_*)\|(C698_*)\|(Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart)` | V-6, R-3 | all 11 C561, 3 C698 and 1 sequence-restart tests, 0 failed/skipped | 15 | 4 |
